using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Atmosphere;
using EVEManager;
using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>One EVE cloud layer, reduced to the quantities SA classifies on.</summary>
	internal struct EveLayerReading
	{
		public string Name;
		/// <summary>Coverage at the vessel itself: am I inside this layer.</summary>
		public float CoverageHere;
		/// <summary>Coverage straight up at mid-layer altitude: is this layer
		/// between me and the sky.</summary>
		public float CoverageSky;
		/// <summary>Interpolated CloudType.Density, the optical thickness per unit
		/// length: a cirrus veil is ~0.0005 and a cumulus deck ~0.012, which is
		/// why coverage alone cannot tell a clear sky from an overcast one.</summary>
		public float Density;
		public float ThicknessM;
		/// <summary>True for layers that render no volume at all, only effects
		/// (particles, sound). They must not count as cloud cover, but they can
		/// still be the precipitation.</summary>
		public bool FxOnly;
		/// <summary>EVE's own particle render gate at the vessel, 0..1. Already
		/// includes the coverage thresholds and the per-cloud-type density.</summary>
		public float PrecipIntensity;
		/// <summary>Particle fall speed: ~12-25 for rain, ~0.3-0.8 for snow and
		/// dust, 32 for hail. The functional rain/snow split.</summary>
		public float FallSpeed;
		/// <summary>Particles in the field: 200k-800k for real weather, a few
		/// hundred for ambient decoration. Keeps permanent scenery from reading as
		/// a dust storm.</summary>
		public float ParticleCount;
		/// <summary>Interpolated lightning frequency, meaningful ONLY when
		/// HasLightning is true (EVE's own default is 1.0 for cloud types that
		/// never set it, so the raw value alone would claim lightning everywhere).</summary>
		public float LightningFrequency;
		public bool HasLightning;
	}

	/// <summary>
	/// One EVE layer's time window, as EVE itself evaluates it: a clock-driven
	/// on/off cycle anchored to UT 0 (TimeSettings.GetFadeForUT), a pure function
	/// of universal time with no randomness and no dependence on local time or on
	/// the body's day. Everything a forecast needs, with nothing sampled.
	/// </summary>
	internal struct EveLayerWindow
	{
		public string Name;
		/// <summary>Seconds per config unit: 3600 for "Hours", 1 otherwise.</summary>
		public double UnitSeconds;
		public double Offset;
		public double Duration;
		public double RepeatInterval;
		public double FadeTime;
		/// <summary>What this layer can drop on the vessel when open; None for a
		/// plain cloud deck, or a particle field too thin to count as weather.</summary>
		public SaPrecipClass Precip;

		/// <summary>
		/// Same arithmetic as EVE's GetFadeForUT: where in the cycle this UT
		/// falls, in config units. The layer is open (fade &gt; 0) for
		/// 0 &lt; phase &lt;= Duration.
		/// </summary>
		public double PhaseAt(double ut)
		{
			double t = ut / UnitSeconds;
			t += RepeatInterval - Offset;
			t %= RepeatInterval;
			if (t < 0.0) t += RepeatInterval;
			return t;
		}

		public bool IsOpenAt(double ut)
		{
			double phase = PhaseAt(ut);
			return phase > 0.0 && phase <= Duration;
		}

		/// <summary>Seconds from <paramref name="ut"/> until the layer next
		/// closes (if open) or next opens (if closed).</summary>
		public double SecondsToNextTransition(double ut)
		{
			double phase = PhaseAt(ut);
			double units = phase <= Duration ? Duration - phase : RepeatInterval - phase;
			return Math.Max(0.0, units * UnitSeconds);
		}

		/// <summary>Seconds from <paramref name="ut"/> until the layer next
		/// opens: zero if it is open right now.</summary>
		public double SecondsToNextOpen(double ut)
		{
			double phase = PhaseAt(ut);
			if (phase > 0.0 && phase <= Duration) return 0.0;
			return Math.Max(0.0, (RepeatInterval - phase) * UnitSeconds);
		}
	}

	/// <summary>
	/// The only class in SA that touches EVE types. Every entry point is called
	/// exclusively after WeatherClassifier has confirmed the assemblies are
	/// loaded, so on an install without EVE these method BODIES are never JITted
	/// and the missing reference never surfaces.
	///
	/// That guard covers method bodies only. Loading the class itself (which Mono
	/// does as soon as any caller of a static method here is compiled, gated or
	/// not) resolves the types of every field, so NO field may be of an EVE
	/// type: the caches below are keyed and valued on object and cast at use.
	/// An EVE-typed field here breaks SaToolbarApp.Start on installs without EVE.
	///
	/// Two traps in EVE's API to keep in mind: the cloudType out-value is
	/// normalised 0..1 rather than an index, and SampleCoverage keeps returning a
	/// stale non-zero coverage for a layer whose time window has closed, so every
	/// reading must be gated on GetFadeForUT.
	/// </summary>
	internal static class EveWeatherReader
	{
		// Keys are CloudsRaymarchedVolume instances and ParticleFieldCache values
		// are ParticleFieldConfig; typed as object on purpose (see class summary).
		private static readonly Dictionary<object, object> ParticleFieldCache = new Dictionary<object, object>();
		private static readonly Dictionary<object, bool> LightningCache = new Dictionary<object, bool>();
		private static readonly Dictionary<object, EveLayerWindow> WindowCache = new Dictionary<object, EveLayerWindow>();

		private static FieldInfo particleFieldField;
		private static FieldInfo particleConfigNameField;
		private static FieldInfo lightningField;
		private static FieldInfo timeUnitField;
		private static FieldInfo timeDurationField;
		private static FieldInfo timeOffsetField;
		private static FieldInfo timeRepeatField;
		private static FieldInfo timeFadeField;
		private static bool reflectionResolved;

		/// <summary>
		/// Every raymarched layer of this body that is actually live right now.
		/// Returns an empty list rather than throwing on any EVE-side surprise:
		/// a weather row that goes blank is acceptable, an exception per frame
		/// inside the readout is not.
		/// </summary>
		internal static List<EveLayerReading> Read(Vessel vessel, double ut)
		{
			List<EveLayerReading> readings = new List<EveLayerReading>();
			if (vessel == null || vessel.mainBody == null) return readings;

			List<CloudsObject> layers;
			try
			{
				string bodyName = vessel.mainBody.bodyName;
				layers = GenericEVEManager<CloudsObject>.GetObjectList()
					.Where(x => x.Body == bodyName && x.LayerRaymarchedVolume != null)
					.ToList();
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA] EVE layer enumeration failed: " + e.Message);
				return readings;
			}

			Vector3 here = vessel.transform.position;
			Vector3 bodyCenter = vessel.mainBody.position;
			Vector3 up = (here - bodyCenter).normalized;

			foreach (CloudsObject obj in layers)
			{
				try
				{
					EveLayerReading reading = ReadLayer(obj, ut, here, bodyCenter, up);
					if (reading.Name != null) readings.Add(reading);
				}
				catch (Exception e)
				{
					Debug.LogWarning("[SA] EVE layer read failed for " + obj.Name + ": " + e.Message);
				}
			}
			return readings;
		}

		private static EveLayerReading ReadLayer(CloudsObject obj, double ut, Vector3 here, Vector3 bodyCenter, Vector3 up)
		{
			CloudsRaymarchedVolume layer = obj.LayerRaymarchedVolume;

			// Time gate FIRST, and hard: EVE only refreshes the fade multipliers
			// while a layer is enabled, so one outside its window keeps reporting
			// the coverage it had when it closed.
			TimeSettings timeSettings = layer.CloudsPQS != null ? layer.CloudsPQS.TimeSettings : null;
			if (timeSettings != null && timeSettings.GetFadeForUT(ut) <= 0f) return default(EveLayerReading);

			float covHere = layer.SampleCoverage(here, out float cloudTypeHere, true);

			float midRadius = (layer.InnerSphereRadius + layer.OuterSphereRadius) / 2f;
			float covSky;
			float cloudTypeSky;
			if (midRadius > 0f)
			{
				covSky = layer.SampleCoverage(bodyCenter + up * midRadius, out cloudTypeSky, false);
			}
			else
			{
				// Degenerate shell (auroras): sampling it would land on the
				// body's centre and return NaN.
				covSky = covHere;
				cloudTypeSky = cloudTypeHere;
			}
			if (float.IsNaN(covHere)) covHere = 0f;
			if (float.IsNaN(covSky)) covSky = 0f;

			float cloudType = covHere > 0f ? cloudTypeHere : cloudTypeSky;

			EveLayerReading reading = new EveLayerReading
			{
				Name = obj.Name,
				CoverageHere = covHere,
				CoverageSky = covSky,
				ThicknessM = Mathf.Max(0f, layer.OuterSphereRadius - layer.InnerSphereRadius),
				FxOnly = layer.RaymarchingSettings != null && layer.RaymarchingSettings.FxOnlyLayer
			};

			List<CloudType> types = layer.CloudTypes;
			if (types != null && types.Count > 0)
			{
				reading.Density = InterpolateDensity(types, cloudType);
				reading.LightningFrequency = layer.GetInterpolatedCloudTypeLightningFrequency(cloudType);
				reading.HasLightning = HasLightning(layer);

				ParticleFieldConfig pf = GetParticleFieldConfig(layer);
				if (pf != null)
				{
					reading.FallSpeed = pf.FallSpeed;
					reading.ParticleCount = pf.FieldParticleCount;
					// EVE's own gate, as ParticleField.Update applies it:
					// clamp01((coverage - min) / (max - min)) * particle density.
					float span = pf.MaxCoverageThreshold - pf.MinCoverageThreshold;
					if (span > 0f)
					{
						float t = Mathf.Clamp01((covHere - pf.MinCoverageThreshold) / span);
						reading.PrecipIntensity = t * layer.GetInterpolatedCloudTypeParticleFieldDensity(cloudType);
					}
				}
			}
			return reading;
		}

		/// <summary>
		/// Every time-windowed raymarched layer of this body, reduced to its
		/// clock parameters and precipitation class. Layers without a
		/// TimeSettings block (permanent decks) are not returned: they have no
		/// transitions to forecast. Same "never throw into the readout" contract
		/// as Read.
		/// </summary>
		internal static List<EveLayerWindow> ReadWindows(Vessel vessel)
		{
			List<EveLayerWindow> windows = new List<EveLayerWindow>();
			if (vessel == null || vessel.mainBody == null) return windows;

			List<CloudsObject> layers;
			try
			{
				string bodyName = vessel.mainBody.bodyName;
				layers = GenericEVEManager<CloudsObject>.GetObjectList()
					.Where(x => x.Body == bodyName && x.LayerRaymarchedVolume != null)
					.ToList();
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA] EVE layer enumeration failed: " + e.Message);
				return windows;
			}

			foreach (CloudsObject obj in layers)
			{
				try
				{
					if (TryReadWindow(obj, vessel.mainBody, out EveLayerWindow window)) windows.Add(window);
				}
				catch (Exception e)
				{
					Debug.LogWarning("[SA] EVE time window read failed for " + obj.Name + ": " + e.Message);
				}
			}
			return windows;
		}

		/// <summary>
		/// TimeSettings keeps every parameter private and exposes only
		/// GetFadeForUT, so the five fields are read once per layer by reflection
		/// and cached. A layer whose settings cannot be read contributes no
		/// forecast.
		/// </summary>
		private static bool TryReadWindow(CloudsObject obj, CelestialBody body, out EveLayerWindow window)
		{
			CloudsRaymarchedVolume layer = obj.LayerRaymarchedVolume;
			if (WindowCache.TryGetValue(layer, out window)) return window.Name != null;

			window = default(EveLayerWindow);
			TimeSettings timeSettings = layer.CloudsPQS != null ? layer.CloudsPQS.TimeSettings : null;
			if (timeSettings != null)
			{
				ResolveReflection();
				if (timeDurationField != null && timeOffsetField != null && timeRepeatField != null && timeFadeField != null)
				{
					double repeat = (float)timeRepeatField.GetValue(timeSettings);
					if (repeat > 0.0)
					{
						object unit = timeUnitField != null ? timeUnitField.GetValue(timeSettings) : null;
						bool hours = unit != null
							&& string.Equals(unit.ToString(), "Hours", StringComparison.OrdinalIgnoreCase);
						window = new EveLayerWindow
						{
							Name = obj.Name,
							UnitSeconds = hours ? 3600.0 : 1.0,
							Duration = (float)timeDurationField.GetValue(timeSettings),
							Offset = (float)timeOffsetField.GetValue(timeSettings),
							RepeatInterval = repeat,
							FadeTime = (float)timeFadeField.GetValue(timeSettings),
							Precip = PrecipClassOf(layer, body)
						};
					}
				}
			}
			WindowCache[layer] = window;
			return window.Name != null;
		}

		/// <summary>
		/// What a layer can drop, by the same functional criteria the classifier
		/// applies to what IS falling: a real-sized particle field, split
		/// liquid/frozen on fall speed and frozen split snow/dust on whether the
		/// body has an ocean.
		/// </summary>
		private static SaPrecipClass PrecipClassOf(CloudsRaymarchedVolume layer, CelestialBody body)
		{
			ParticleFieldConfig pf = GetParticleFieldConfig(layer);
			if (pf == null || pf.FieldParticleCount < WeatherClassifier.MinWeatherParticleCount) return SaPrecipClass.None;
			if (pf.FallSpeed >= WeatherClassifier.LiquidFallSpeed) return SaPrecipClass.Liquid;
			return body.ocean ? SaPrecipClass.Snow : SaPrecipClass.Dust;
		}

		/// <summary>
		/// CloudType.Density interpolated the way EVE interpolates its other
		/// per-type values: scale the normalised cloudType by Count-1 and lerp the
		/// two neighbours. EVE ships GetInterpolatedCloudType* for particles,
		/// droplets and lightning, but not for Density.
		/// </summary>
		private static float InterpolateDensity(List<CloudType> types, float cloudType)
		{
			if (types.Count == 1) return types[0].Density;
			float scaled = Mathf.Clamp01(cloudType) * (types.Count - 1);
			int current = Mathf.Clamp((int)scaled, 0, types.Count - 1);
			int next = Mathf.Min(current + 1, types.Count - 1);
			return Mathf.Lerp(types[current].Density, types[next].Density, scaled - current);
		}

		/// <summary>
		/// Whether this layer can produce lightning at all. EVE defaults
		/// CloudType.lightningFrequency to 1.0 when a config does not set it, so
		/// the interpolated value alone would claim thunderstorms over ordinary
		/// cumulus; the real answer is whether the layer has a lightning config
		/// object, which is private and needs reflection.
		/// </summary>
		private static bool HasLightning(CloudsRaymarchedVolume layer)
		{
			if (LightningCache.TryGetValue(layer, out bool cached)) return cached;
			bool result = false;
			try
			{
				ResolveReflection();
				if (lightningField != null) result = lightningField.GetValue(layer) != null;
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA] EVE lightning lookup failed: " + e.Message);
			}
			LightningCache[layer] = result;
			return result;
		}

		/// <summary>
		/// The layer's particle field config. CloudsRaymarchedVolume keeps its
		/// ParticleField private and ParticleField keeps the config name private,
		/// but ParticleFieldManager.GetConfig is public, so one cached reflection
		/// hop per layer opens the whole config surface.
		/// </summary>
		private static ParticleFieldConfig GetParticleFieldConfig(CloudsRaymarchedVolume layer)
		{
			if (ParticleFieldCache.TryGetValue(layer, out object cached)) return cached as ParticleFieldConfig;
			ParticleFieldConfig result = null;
			try
			{
				ResolveReflection();
				if (particleFieldField != null && particleConfigNameField != null)
				{
					object particleField = particleFieldField.GetValue(layer);
					if (particleField != null)
					{
						string name = particleConfigNameField.GetValue(particleField) as string;
						if (!string.IsNullOrEmpty(name)) result = ParticleFieldManager.GetConfig(name);
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA] EVE particle field lookup failed: " + e.Message);
			}
			ParticleFieldCache[layer] = result;
			return result;
		}

		private static void ResolveReflection()
		{
			if (reflectionResolved) return;
			reflectionResolved = true;
			const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			particleFieldField = typeof(CloudsRaymarchedVolume).GetField("particleField", Flags);
			lightningField = typeof(CloudsRaymarchedVolume).GetField("lightning", Flags);
			particleConfigNameField = particleFieldField?.FieldType.GetField("particleFieldConfig", Flags);
			timeUnitField = typeof(TimeSettings).GetField("unit", Flags);
			timeDurationField = typeof(TimeSettings).GetField("duration", Flags);
			timeOffsetField = typeof(TimeSettings).GetField("offset", Flags);
			timeRepeatField = typeof(TimeSettings).GetField("repeatInterval", Flags);
			timeFadeField = typeof(TimeSettings).GetField("fadeTime", Flags);
		}

		/// <summary>
		/// Dropped when the scene changes: the cache keys are EVE layer objects
		/// that do not survive a scene reload.
		/// </summary>
		internal static void ClearCaches()
		{
			ParticleFieldCache.Clear();
			LightningCache.Clear();
			WindowCache.Clear();
		}
	}
}
