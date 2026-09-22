using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Atmosphere;
using EVEManager;
using SituationalAwareness.Extensibility;
using UnityEngine;

namespace SituationalAwareness.WeatherReport
{
	/// <summary>Raw sample of one EVE cloud layer at the moment of a report press.</summary>
	internal readonly struct WeatherLayerSample
	{
		public readonly string LayerName;
		public readonly float CovHere;
		public readonly float CovSky;
		public readonly float CloudTypeRaw;
		public readonly string TypeName;
		public readonly float CloudTypeDensity;
		public readonly bool FxOnly;
		public readonly float ParticleFieldDensity;
		public readonly float DropletsDensity;
		public readonly float LightningFrequency;
		public readonly float WetSurfacesIntensity;
		public readonly float Fade;
		public readonly float MinAltitudeM;
		public readonly float MaxAltitudeM;
		// Particle field of this layer, when it has one; empty name otherwise.
		// These are what separate rain from snow from dust (fall speed, stretch)
		// and real weather from ambient decoration (particle count: a few hundred
		// for scenery against 200k-800k for real precipitation).
		public readonly string ParticleFieldName;
		public readonly float ParticleFallSpeed;
		public readonly float ParticleCount;
		public readonly float ParticleStretch;
		public readonly float PrecipIntensity;

		public WeatherLayerSample(string layerName, float covHere, float covSky, float cloudTypeRaw, string typeName,
			float cloudTypeDensity, bool fxOnly,
			float particleFieldDensity, float dropletsDensity, float lightningFrequency, float wetSurfacesIntensity,
			float fade, float minAltitudeM, float maxAltitudeM,
			string particleFieldName, float particleFallSpeed, float particleCount, float particleStretch,
			float precipIntensity)
		{
			LayerName = layerName;
			CovHere = covHere;
			CovSky = covSky;
			CloudTypeRaw = cloudTypeRaw;
			TypeName = typeName;
			CloudTypeDensity = cloudTypeDensity;
			FxOnly = fxOnly;
			ParticleFieldDensity = particleFieldDensity;
			DropletsDensity = dropletsDensity;
			LightningFrequency = lightningFrequency;
			WetSurfacesIntensity = wetSurfacesIntensity;
			Fade = fade;
			MinAltitudeM = minAltitudeM;
			MaxAltitudeM = maxAltitudeM;
			ParticleFieldName = particleFieldName;
			ParticleFallSpeed = particleFallSpeed;
			ParticleCount = particleCount;
			ParticleStretch = particleStretch;
			PrecipIntensity = precipIntensity;
		}
	}

	/// <summary>
	/// Raw snapshot of everything a report button press records.
	/// </summary>
	internal sealed class WeatherSample
	{
		public double UT;
		public string BodyName;
		public string BiomeName;
		public double Latitude, Longitude;
		public double AltitudeAsl;
		public double? AltitudeAgl;
		public string Situation;
		public double SunElevationDeg;
		public double SolarFluxWm2;
		public double? WdspTransmittance;
		public double? WeatherImpactFactor;
		public bool CameraIsIva;
		public Vector3 CameraPosition;
		public Vector3 VesselPosition;
		// What SA itself claimed at the moment of the press. The companion exists
		// to compare that verdict against what the player saw: without this
		// column a report says "the weather is wrong" with no way to tell which
		// side of the classifier missed.
		public string SaWeatherState;
		public double SaSkyOpticalDepth;
		public double SaPrecipIntensity;
		public string SaDominantLayer;
		public readonly List<WeatherLayerSample> Layers = new List<WeatherLayerSample>();
	}

	/// <summary>
	/// Raw EVE/WDSP reader: records, never classifies. Soft dependency on both,
	/// so it never touches their types unless the assemblies are loaded.
	/// </summary>
	internal static class WeatherProbe
	{
		private static bool? eveAvailable;
		private static bool? wdspAvailable;

		internal static bool EveAvailable
		{
			get
			{
				if (eveAvailable == null)
				{
					eveAvailable = AssemblyLoader.loadedAssemblies.Any(a => a.name == "Atmosphere")
						&& AssemblyLoader.loadedAssemblies.Any(a => a.name == "EVEManager");
				}
				return eveAvailable.Value;
			}
		}

		private static bool WdspAvailable
		{
			get
			{
				if (wdspAvailable == null)
				{
					wdspAvailable = AssemblyLoader.loadedAssemblies.Any(a => a.name == "WeatherDrivenSolarPanel");
				}
				return wdspAvailable.Value;
			}
		}

		internal static WeatherSample Sample(Vessel vessel)
		{
			if (vessel == null || vessel.mainBody == null) return null;

			WeatherSample sample = new WeatherSample
			{
				UT = Planetarium.GetUniversalTime(),
				BodyName = vessel.mainBody.bodyName,
				BiomeName = ScienceUtil.GetExperimentBiome(vessel.mainBody, vessel.latitude, vessel.longitude),
				Latitude = vessel.latitude,
				Longitude = vessel.longitude,
				AltitudeAsl = vessel.altitude,
				Situation = vessel.situation.ToString(),
				SolarFluxWm2 = vessel.solarFlux,
			};

			// radarAltitude reads a huge negative sentinel with no ground return,
			// so it is only recorded when sane.
			double agl = vessel.radarAltitude;
			sample.AltitudeAgl = agl > -1e6 ? (double?)agl : null;

			SampleSunElevation(vessel, sample);
			SampleCamera(sample);
			SampleWdsp(vessel, sample);
			SampleSaVerdict(vessel, sample);

			if (EveAvailable)
			{
				SampleEveLayers(vessel, sample);
			}

			return sample;
		}

		/// <summary>
		/// SA's own classification at this instant, through the public
		/// SaExtensionPoint API. Guarded like everything else here: the companion
		/// ships separately and can meet an SA that predates these methods, which
		/// must leave the column empty rather than crash.
		/// </summary>
		private static void SampleSaVerdict(Vessel vessel, WeatherSample sample)
		{
			try
			{
				sample.SaWeatherState = SaExtensionPoint.CurrentWeather(vessel);
				SaExtensionPoint.CurrentWeatherDetail(vessel, out double depth, out double precip, out string layer);
				sample.SaSkyOpticalDepth = depth;
				sample.SaPrecipIntensity = precip;
				sample.SaDominantLayer = layer;
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] SA weather verdict unavailable: " + e.Message);
				sample.SaWeatherState = "";
			}
		}

		/// <summary>
		/// Simplified elevation above the local horizon, home star only: SA's own
		/// multi-star-aware math is internal to its assembly and out of reach
		/// here. Good enough for "what did the sky look like", not meant to match
		/// SA's SUN row to the decimal.
		/// </summary>
		private static void SampleSunElevation(Vessel vessel, WeatherSample sample)
		{
			CelestialBody sun = FlightGlobals.Bodies.Find(b => b.isStar);
			if (sun == null)
			{
				sample.SunElevationDeg = double.NaN;
				return;
			}
			Vector3d up = (vessel.CoM - vessel.mainBody.position).normalized;
			Vector3d toSun = (sun.position - vessel.CoM).normalized;
			sample.SunElevationDeg = 90.0 - Vector3d.Angle(up, toSun);
		}

		/// <summary>
		/// IVA vs non-IVA, which is what matters: droplets on glass only exist in
		/// the IVA and Internal camera modes, both rendering through
		/// InternalCamera, and everything else is outside. Position comes from
		/// Camera.main rather than FlightCamera's own camera, since it stays
		/// correct across every one of these modes.
		/// </summary>
		private static void SampleCamera(WeatherSample sample)
		{
			CameraManager.CameraMode mode = CameraManager.Instance != null
				? CameraManager.Instance.currentCameraMode
				: CameraManager.CameraMode.Flight;
			sample.CameraIsIva = mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal;

			Camera cam = Camera.main;
			sample.CameraPosition = cam != null ? cam.transform.position : Vector3.zero;
			sample.VesselPosition = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.transform.position : Vector3.zero;
		}

		/// <summary>
		/// WeatherDrivenSolarPanel, with no compile-time reference.
		/// currentOutput and WeatherImpactFactor are ordinary [KSPField]s, read
		/// through PartModule.Fields like any KSP mod would and needing no .NET
		/// reflection. VolumetricCloudTransmittance is a static method with no
		/// KSPField equivalent, so that one does.
		/// </summary>
		private static void SampleWdsp(Vessel vessel, WeatherSample sample)
		{
			if (!WdspAvailable) return;

			try
			{
				Assembly asm = AssemblyLoader.loadedAssemblies.First(a => a.name == "WeatherDrivenSolarPanel").assembly;
				Type fnType = asm.GetType("WDSP_GenericFunctionModule.GenericFunctionModule");
				MethodInfo method = fnType?.GetMethod("VolumetricCloudTransmittance", BindingFlags.Public | BindingFlags.Static);
				if (method != null)
				{
					CelestialBody sun = FlightGlobals.Bodies.Find(b => b.isStar);
					object[] args = { sun, null };
					object result = method.Invoke(null, args);
					if (result is double d) sample.WdspTransmittance = d;
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] WDSP transmittance read failed: " + e.Message);
			}

			foreach (Part part in vessel.parts)
			{
				foreach (PartModule module in part.Modules)
				{
					if (module.moduleName != "weatherDrivenSolarPanel") continue;
					BaseField field = module.Fields["WeatherImpactFactor"];
					if (field != null)
					{
						sample.WeatherImpactFactor = Convert.ToDouble(field.GetValue(module));
					}
					return;
				}
			}
		}

		/// <summary>
		/// One row per raymarched cloud layer on the current body. 2D-only layers
		/// are skipped: they have no SampleCoverage to call.
		/// </summary>
		private static void SampleEveLayers(Vessel vessel, WeatherSample sample)
		{
			string bodyName = vessel.mainBody.bodyName;
			List<CloudsObject> layers;
			try
			{
				layers = GenericEVEManager<CloudsObject>.GetObjectList()
					.Where(x => x.Body == bodyName && x.LayerRaymarchedVolume != null)
					.ToList();
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] EVE layer enumeration failed: " + e.Message);
				return;
			}

			Vector3 hereWorld = sample.CameraPosition;
			Vector3 bodyCenter = vessel.mainBody.position;
			Vector3 upDir = (vessel.transform.position - bodyCenter).normalized;

			foreach (CloudsObject obj in layers)
			{
				CloudsRaymarchedVolume layer = obj.LayerRaymarchedVolume;
				try
				{
					float covHere = layer.SampleCoverage(hereWorld, out float cloudTypeHere, true);

					// "Sky" sample: straight up from the vessel at mid-layer
					// altitude, with planetRadiusCheck off so a high layer is
					// still seen from the ground underneath it. A degenerate
					// shell (both radii 0, as some aurora layers have) would put
					// the sample point at the body's centre and return NaN, so it
					// falls back to the "here" sample instead.
					float midRadius = (layer.InnerSphereRadius + layer.OuterSphereRadius) / 2f;
					float covSky;
					float cloudTypeSky;
					if (midRadius > 0f)
					{
						Vector3 skyPoint = bodyCenter + upDir * midRadius;
						covSky = layer.SampleCoverage(skyPoint, out cloudTypeSky, false);
					}
					else
					{
						covSky = covHere;
						cloudTypeSky = cloudTypeHere;
					}

					float cloudTypeRaw = covHere > 0f ? cloudTypeHere : cloudTypeSky;
					List<CloudType> types = layer.CloudTypes;
					string typeName = "-";
					float cloudTypeDensity = 0f;
					float particleField = 0f, droplets = 0f, lightning = 0f, wetSurfaces = 0f;
					if (types != null && types.Count > 0)
					{
						// cloudType is NORMALISED 0..1, not an index: EVE's own
						// getCloudFrac scales it by (Count - 1) before indexing,
						// and rounding the raw value would only ever name the
						// first two types.
						cloudTypeDensity = InterpolateDensity(types, cloudTypeRaw, out int idx);
						typeName = types[idx].TypeName;
						particleField = layer.GetInterpolatedCloudTypeParticleFieldDensity(cloudTypeRaw);
						droplets = layer.GetInterpolatedCloudTypeDropletsDensity(cloudTypeRaw);
						lightning = layer.GetInterpolatedCloudTypeLightningFrequency(cloudTypeRaw);
						wetSurfaces = layer.GetInterpolatedCloudTypeWetSurfacesDensity(cloudTypeRaw);
					}

					float fade = 1f;
					TimeSettings timeSettings = layer.CloudsPQS != null ? layer.CloudsPQS.TimeSettings : null;
					if (timeSettings != null) fade = timeSettings.GetFadeForUT(sample.UT);

					float minAlt = layer.InnerSphereRadius - layer.PlanetRadius;
					float maxAlt = layer.OuterSphereRadius - layer.PlanetRadius;

					bool fxOnly = layer.RaymarchingSettings != null && layer.RaymarchingSettings.FxOnlyLayer;

					// Particle field: EVE's own render gate, as ParticleField.Update
					// applies it — clamp01((cov - minCov) / (maxCov - minCov))
					// times the interpolated density — so anything above 0 means
					// EVE is really drawing particles here.
					ParticleFieldConfig pf = EveLayerInfo.GetParticleFieldConfig(layer);
					string pfName = pf != null ? pf.Name : "";
					float fallSpeed = pf != null ? pf.FallSpeed : 0f;
					float count = pf != null ? pf.FieldParticleCount : 0f;
					float stretch = pf != null ? pf.ParticleStretch : 0f;
					float precip = 0f;
					if (pf != null && fade > 0f)
					{
						float span = pf.MaxCoverageThreshold - pf.MinCoverageThreshold;
						float t = span > 0f ? Mathf.Clamp01((covHere - pf.MinCoverageThreshold) / span) : 0f;
						precip = t * particleField;
					}

					sample.Layers.Add(new WeatherLayerSample(obj.Name, covHere, covSky, cloudTypeRaw, typeName,
						cloudTypeDensity, fxOnly,
						particleField, droplets, lightning, wetSurfaces, fade, minAlt, maxAlt,
						pfName, fallSpeed, count, stretch, precip));
				}
				catch (Exception e)
				{
					Debug.LogWarning("[SA_WeatherReport] Layer sample failed for " + obj.Name + ": " + e.Message);
				}
			}
		}

		/// <summary>
		/// CloudType.Density interpolated the way EVE interpolates its other
		/// per-type values: scale the normalised cloudType by Count-1 and lerp the
		/// two neighbours. EVE ships no GetInterpolatedCloudType* for Density,
		/// which is the optical thickness separating a cirrus veil from an
		/// overcast deck. Also returns the nearest type index, for the label.
		/// </summary>
		private static float InterpolateDensity(List<CloudType> types, float cloudTypeRaw, out int nearestIndex)
		{
			if (types.Count == 1)
			{
				nearestIndex = 0;
				return types[0].Density;
			}
			float scaled = Mathf.Clamp(cloudTypeRaw, 0f, 1f) * (types.Count - 1);
			int current = Mathf.Clamp((int)scaled, 0, types.Count - 1);
			int next = Mathf.Min(current + 1, types.Count - 1);
			float frac = scaled - current;
			nearestIndex = Mathf.Clamp(Mathf.RoundToInt(scaled), 0, types.Count - 1);
			return Mathf.Lerp(types[current].Density, types[next].Density, frac);
		}
	}

	/// <summary>
	/// The one piece of EVE with no public accessor: CloudsRaymarchedVolume keeps
	/// its ParticleField private and ParticleField keeps the config name private,
	/// but ParticleFieldManager.GetConfig is public, so one cached reflection hop
	/// per layer reaches the whole config surface. Fully guarded: any failure
	/// means "this layer has no particle field", never an exception at the caller.
	/// </summary>
	internal static class EveLayerInfo
	{
		// CloudsRaymarchedVolume -> ParticleFieldConfig, typed as object on purpose:
		// Mono resolves field types when the class loads, and this class must
		// stay loadable on an install without EVE.
		private static readonly Dictionary<object, object> Cache = new Dictionary<object, object>();

		private static FieldInfo particleFieldField;
		private static FieldInfo configNameField;
		private static bool reflectionResolved;

		internal static ParticleFieldConfig GetParticleFieldConfig(CloudsRaymarchedVolume layer)
		{
			if (layer == null) return null;
			if (Cache.TryGetValue(layer, out object cached)) return cached as ParticleFieldConfig;

			ParticleFieldConfig result = null;
			try
			{
				ResolveReflection();
				if (particleFieldField != null && configNameField != null)
				{
					object particleField = particleFieldField.GetValue(layer);
					if (particleField != null)
					{
						string name = configNameField.GetValue(particleField) as string;
						if (!string.IsNullOrEmpty(name)) result = ParticleFieldManager.GetConfig(name);
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] particle field lookup failed: " + e.Message);
			}

			Cache[layer] = result;
			return result;
		}

		private static void ResolveReflection()
		{
			if (reflectionResolved) return;
			reflectionResolved = true;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			particleFieldField = typeof(CloudsRaymarchedVolume).GetField("particleField", flags);
			Type particleFieldType = particleFieldField?.FieldType;
			configNameField = particleFieldType?.GetField("particleFieldConfig", flags);
		}
	}
}
