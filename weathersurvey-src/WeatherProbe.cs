using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Atmosphere;
using EVEManager;
using UnityEngine;

namespace SituationalAwareness.WeatherSurvey
{
	/// <summary>Raw sample of one EVE cloud layer at the moment of a survey press (notes/indagine-meteo.md §7).</summary>
	internal readonly struct WeatherLayerSample
	{
		public readonly string LayerName;
		public readonly float CovHere;
		public readonly float CovSky;
		public readonly float CloudTypeRaw;
		public readonly string TypeName;
		public readonly float ParticleFieldDensity;
		public readonly float DropletsDensity;
		public readonly float LightningFrequency;
		public readonly float WetSurfacesIntensity;
		public readonly float Fade;
		public readonly float MinAltitudeM;
		public readonly float MaxAltitudeM;

		public WeatherLayerSample(string layerName, float covHere, float covSky, float cloudTypeRaw, string typeName,
			float particleFieldDensity, float dropletsDensity, float lightningFrequency, float wetSurfacesIntensity,
			float fade, float minAltitudeM, float maxAltitudeM)
		{
			LayerName = layerName;
			CovHere = covHere;
			CovSky = covSky;
			CloudTypeRaw = cloudTypeRaw;
			TypeName = typeName;
			ParticleFieldDensity = particleFieldDensity;
			DropletsDensity = dropletsDensity;
			LightningFrequency = lightningFrequency;
			WetSurfacesIntensity = wetSurfacesIntensity;
			Fade = fade;
			MinAltitudeM = minAltitudeM;
			MaxAltitudeM = maxAltitudeM;
		}
	}

	/// <summary>
	/// Raw snapshot of everything a survey button press records
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

			if (EveAvailable)
			{
				SampleEveLayers(vessel, sample);
			}

			return sample;
		}

		/// <summary>
		/// Simplified elevation (angle above the local horizon), home star
		/// only — a context field for the survey row, not the multi-star-
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
				Debug.LogWarning("[SA_WeatherSurvey] WDSP transmittance read failed: " + e.Message);
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
				Debug.LogWarning("[SA_WeatherSurvey] EVE layer enumeration failed: " + e.Message);
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
					float particleField = 0f, droplets = 0f, lightning = 0f, wetSurfaces = 0f;
					if (types != null && types.Count > 0)
					{
						// cloudType is a continuous blend value, not a discrete
						// index — nearest-neighbor is good enough for a raw
						// probe label, the interpolated densities below are
						// the real (blended) numbers.
						int idx = Mathf.Clamp(Mathf.RoundToInt(cloudTypeRaw), 0, types.Count - 1);
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

					sample.Layers.Add(new WeatherLayerSample(obj.Name, covHere, covSky, cloudTypeRaw, typeName,
						particleField, droplets, lightning, wetSurfaces, fade, minAlt, maxAlt));
				}
				catch (Exception e)
				{
					Debug.LogWarning("[SA_WeatherSurvey] Layer sample failed for " + obj.Name + ": " + e.Message);
				}
			}
		}
	}
}
