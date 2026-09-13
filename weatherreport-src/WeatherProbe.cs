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
	/// <summary>Raw sample of one EVE cloud layer at the moment of a report press (notes/indagine-meteo.md §7).</summary>
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
		// Particle field of this layer, when it has one at all (empty name
		// when it doesn't). Reached through EveLayerInfo's cached reflection —
		// these are what actually separate rain from snow from dust
		// (fall speed, stretch) and real weather from ambient decoration
		// (particle count: 500 for Duna-Dust-Sparse against 200k-800k for
		// real precipitation). See notes/survey-analisi.md.
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
	/// Raw snapshot of everything a report button press records
	/// (notes/indagine-meteo.md §7, corrected for IVA/non-IVA camera —
	/// EVA vs vessel dropped, adds no information here).
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
		// What SA itself claimed at the moment of the press, via
		// SaExtensionPoint. The whole point of the companion after the §8.6
		// pivot is comparing SA's verdict against what the player saw — without
		// this column a report says "the weather is wrong" with no way to tell
		// which side of the classifier missed.
		public string SaWeatherState;
		public double SaSkyOpticalDepth;
		public double SaPrecipIntensity;
		public string SaDominantLayer;
		public readonly List<WeatherLayerSample> Layers = new List<WeatherLayerSample>();
	}

	/// <summary>
	/// Raw EVE/WDSP reader — no classification, that is future work once
	/// this data has been used to tune it (§7 "Ordine di lavoro"). Soft
	/// dependency on both: never touches EVE/WDSP types unless their
	/// assemblies are actually loaded (notes §6).
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

			// radarAltitude reads a huge negative sentinel when there's no
			// ground return (high orbit/vacuum) — only record when sane.
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
		/// SaExtensionPoint API. Guarded like everything else here: an older SA
		/// without these methods must degrade to an empty column, not a crash —
		/// the companion ships separately and can meet an SA that predates it.
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
		/// Simplified elevation (angle above the local horizon), home star
		/// only — a context field for the report row, not the multi-star-
		/// aware calculation SA's own SolarMath/StarResolver do internally
		/// (not reachable from here, internal to SA's assembly). Good
		/// enough for "what did the sky look like", not meant to match SA's
		/// own SUN row to the decimal.
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
		/// IVA vs non-IVA (user correction 2026-08-17: EVA dropped, adds no
		/// information — droplets-on-glass only exists in IVA/Internal,
		/// both of which render through InternalCamera, and EVA never
		/// reaches either). Verified on the decompiled CameraManager.cs:
		/// CameraMode has Flight/Map/External/IVA/Internal: IVA is a
		/// kerbal's first-person view, Internal is SetCameraInternal's
		/// robotic-controller-style internal view — both are "inside", the
		/// rest is not. Camera.main is used for position rather than
		/// FlightCamera.fetch.mainCamera specifically because it stays
		/// correct across all these camera-switching modes (Unity's own
		/// "whichever camera is actually rendering" accessor).
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
		/// WeatherDrivenSolarPanel: no compile-time reference (notes §6).
		/// currentOutput/WeatherImpactFactor are ordinary [KSPField]s, read
		/// via PartModule.Fields[...] like any other KSP mod would — that
		/// part needs no .NET reflection at all, KSPField exposes private
		/// fields too. VolumetricCloudTransmittance is a static method with
		/// no KSPField equivalent, so that one genuinely needs
		/// Type/MethodInfo reflection.
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
		/// One row per raymarched cloud layer on the current body (2D-only
		/// layers, LayerRaymarchedVolume == null, are skipped — no
		/// SampleCoverage to call). API verified on the decompiled
		/// Atmosphere.dll/EVEManager.dll (2026-08-17), not assumed.
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
					// altitude, planetRadiusCheck off (notes §3 — covers
					// high layers seen from the ground underneath them).
					// Bug fix (in-game test 2026-08-17): a few layers (seen on
					// "Aurora" ones) have InnerSphereRadius == OuterSphereRadius
					// == 0 — a degenerate shell, not a real cloud sphere — which
					// puts skyPoint at the body's own center and made
					// SampleCoverage return NaN. Falls back to the "here" sample
					// instead of sampling a meaningless point.
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
						// Bug fix (report analysis 2026-09-04): cloudType is
						// NORMALISED 0..1, not an index — EVE's own getCloudFrac
						// scales it by (Count - 1) before indexing. The old
						// RoundToInt(raw) could only ever return index 0 or 1,
						// which mislabelled exactly the interesting samples
						// (thunderstorms came out as "Fog", snow as "Rain").
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

					// Particle field: EVE's own render gate, replicated from the
					// decompiled ParticleField.Update() —
					//   t = clamp01((cov - minCov) / (maxCov - minCov))
					//   t *= interpolated particle field density
					// so > 0 means EVE is actually drawing particles here.
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
		/// CloudType.Density interpolated the way EVE interpolates every other
		/// per-type value (getCloudFrac: scale the normalised 0..1 cloudType by
		/// Count-1, lerp between the two neighbours). EVE exposes
		/// GetInterpolatedCloudType* for particle field/droplets/lightning/wet
		/// surfaces but NOT for Density, so it is replicated here — it is the
		/// optical thickness, i.e. what separates a thin cirrus veil from an
		/// overcast deck. Also returns the nearest type index for the label.
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
	/// The one piece of EVE that has no public accessor: CloudsRaymarchedVolume
	/// keeps its ParticleField private, and ParticleField keeps the config NAME
	/// private too — but ParticleFieldManager.GetConfig(name) is public, so a
	/// single cached reflection hop per layer is enough to reach the whole
	/// public ParticleFieldConfig surface. Cached per layer instance and fully
	/// guarded: any failure just means "this layer has no particle field", never
	/// an exception into the caller.
	/// </summary>
	internal static class EveLayerInfo
	{
		private static readonly Dictionary<CloudsRaymarchedVolume, ParticleFieldConfig> Cache =
			new Dictionary<CloudsRaymarchedVolume, ParticleFieldConfig>();

		private static FieldInfo particleFieldField;
		private static FieldInfo configNameField;
		private static bool reflectionResolved;

		internal static ParticleFieldConfig GetParticleFieldConfig(CloudsRaymarchedVolume layer)
		{
			if (layer == null) return null;
			if (Cache.TryGetValue(layer, out ParticleFieldConfig cached)) return cached;

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
