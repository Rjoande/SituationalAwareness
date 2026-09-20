using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Turns EVE's raw cloud layers into one weather state.
	///
	/// Classifies on FUNCTIONAL properties only, never on layer names: fall speed,
	/// particle count and optical density mean the same thing in any EVE
	/// volumetrics pack, whereas a name table silently mis-categorises every pack
	/// it has not been updated for. Holds no EVE types itself; EveWeatherReader
	/// does.
	/// </summary>
	internal static class WeatherClassifier
	{
		// --- Thresholds -----------------------------------------------------
		// Every number below is tuned empirically and sits at a genuinely fuzzy
		// boundary; none of them is a physical constant.

		/// <summary>Optical depth (coverage x density x thickness, summed over the
		/// layers overhead) above which the sky reads as cloudy rather than clear.
		/// Weighted by density, so a thin cirrus veil at 50% coverage stays clear
		/// while a cumulus deck at 10% does not.</summary>
		private const double CloudyOpticalDepth = 0.3;

		/// <summary>Coverage AT the vessel above which we are inside a layer
		/// rather than under it.</summary>
		private const float FogCoverageHere = 0.15f;

		/// <summary>...and coverage overhead must be essentially nothing for it to
		/// be fog. This half matters more than the first: a thick layer that
		/// surrounds you AND continues above you is overcast, not fog, and without
		/// it a body with permanent haze reports fog forever.</summary>
		private const float FogCoverageSky = 0.05f;

		/// <summary>Particle fields smaller than this are scenery, not weather:
		/// real precipitation runs 50k-800k particles, an ambient haze a few
		/// hundred. Set below EVE's own 10000 default, so a config that never sets
		/// a count still counts as real weather.</summary>
		internal const float MinWeatherParticleCount = 5000f;

		/// <summary>Fall speed separating liquid from everything that drifts:
		/// rain is 12-25 and hail 32, while snow and dust sit at 0.3-0.8.</summary>
		internal const float LiquidFallSpeed = 5f;

		/// <summary>Interpolated lightning frequency, on a layer that really has a
		/// lightning config, above which precipitation is a thunderstorm. Mostly
		/// decides how far into the thunder cloud type you must be: a plain rain
		/// type sits an order of magnitude below it. Also used by the dry-lightning
		/// path.</summary>
		private const float ThunderstormLightning = 0.25f;

		/// <summary>
		/// Optical depth contributed by layers that can produce lightning, above
		/// which the sky is a thunderstorm even with nothing falling: a gas giant's
		/// permanent electrical deck has no rain layer to detect.
		///
		/// Weighting by optical depth rather than raw coverage is what makes this
		/// separable — ordinary skies score 0 here and a gas giant's score in the
		/// thousands, so anything from 5 to 100 behaves the same.
		/// </summary>
		private const double DryLightningOpticalDepth = 10.0;

		/// <summary>EVE's particle gate is 0..1; anything above this counts as
		/// falling. Kept low, since the gate is already zero when EVE draws
		/// nothing.</summary>
		private const float PrecipitatingIntensity = 0.02f;

		// Weather changes on the scale of minutes, while sampling every layer
		// costs two CPU texture samples each, so the 10 Hz readout reuses a cached
		// classification rather than recomputing one per tick.
		private const double RefreshIntervalSec = 1.0;

		// --- Display hysteresis ---------------------------------------------
		// A vessel crossing a front, or parked on the clear/cloudy boundary, sees
		// the raw classification flip every few samples, so the shown state only
		// follows once the raw one has held. 30 s for the fuzzy calls, but 5 s
		// when precipitation starts or stops: EVE's particle gate is crisp and the
		// player can see the drops, so half a minute of CLEAR under visible rain
		// would read as a bug.
		private const double HoldPrecipSec = 5.0;
		private const double HoldOtherSec = 30.0;

		/// <summary>Forecast horizon: three calendar days. Transitions past it are
		/// not worth a line, and "no change" past it is worth exactly one.</summary>
		private const int HorizonDays = 3;

		private static bool? available;
		private static double lastSampleUT = double.NegativeInfinity;
		private static Vessel lastVessel;
		private static CelestialBody lastBody;
		private static bool lastBodyAirless;
		private static SaWeatherReadout cached;

		private static SaWeatherState shownState = SaWeatherState.Unknown;
		private static SaWeatherState candidateState = SaWeatherState.Unknown;
		private static double candidateSinceUT;
		private static List<EveLayerWindow> windows;

		/// <summary>EVE volumetrics present at all. Checked once; keeps every
		/// EVE type out of the code path on installs without it.</summary>
		internal static bool Available
		{
			get
			{
				if (available == null)
				{
					available = AssemblyLoader.loadedAssemblies.Any(a => a.name == "Atmosphere")
						&& AssemblyLoader.loadedAssemblies.Any(a => a.name == "EVEManager");
				}
				return available.Value;
			}
		}

		/// <summary>
		/// Current weather for this vessel, or an Unknown readout when there is
		/// nothing meaningful to say (no EVE, vacuum).
		///
		/// On an airless body EVE can still put a plume or a dust field in the
		/// way, and standing in one is weather worth showing; Clear or Cloudy
		/// there is not, since a decorative layer or a ring overhead must not
		/// paint "clear" on every airless moon. Those two are therefore reported
		/// as Unknown, so the section appears on entering a plume and goes away
		/// on leaving it.
		/// </summary>
		internal static SaWeatherReadout Classify(Vessel vessel, double ut)
		{
			if (!Available || vessel == null || vessel.mainBody == null)
			{
				return default(SaWeatherReadout);
			}

			bool sameContext = vessel == lastVessel && vessel.mainBody == lastBody;
			if (sameContext && ut - lastSampleUT >= 0.0 && ut - lastSampleUT < RefreshIntervalSec)
			{
				return cached;
			}
			// Keyed on vessel AND body: everything cached here is per body, so the
			// same craft arriving at another one must not keep reporting the
			// previous body's layers and time windows.
			if (!sameContext)
			{
				EveWeatherReader.ClearCaches();
				windows = null;
				shownState = SaWeatherState.Unknown;
				candidateState = SaWeatherState.Unknown;
			}

			lastVessel = vessel;
			lastBody = vessel.mainBody;
			lastBodyAirless = !vessel.mainBody.atmosphere;
			lastSampleUT = ut;
			cached = Build(vessel, ut);
			cached.RawState = cached.State;
			cached.State = Filter(cached.State, ut);
			cached.Forecast = BuildForecast(vessel, cached.State, ut);
			if (lastBodyAirless && (cached.State == SaWeatherState.Clear || cached.State == SaWeatherState.Cloudy))
			{
				// Hidden, but the hysteresis above keeps tracking the real state,
				// so re-entering the plume is judged against Clear rather than
				// against a fresh Unknown.
				cached.State = SaWeatherState.Unknown;
				cached.Forecast = default(SaWeatherForecast);
			}
			return cached;
		}

		/// <summary>
		/// The hysteresis itself. Unknown passes straight through in both
		/// directions: leaving the atmosphere is not a flicker, and the first
		/// real reading after a scene load should not wait half a minute.
		/// Measured in UT, so under time warp a real change shows up after
		/// the same amount of game time rather than the same number of frames.
		/// </summary>
		private static SaWeatherState Filter(SaWeatherState raw, double ut)
		{
			if (raw == SaWeatherState.Unknown || shownState == SaWeatherState.Unknown)
			{
				shownState = raw;
				candidateState = raw;
				return shownState;
			}
			if (raw == shownState)
			{
				candidateState = raw;
				return shownState;
			}
			if (raw != candidateState)
			{
				candidateState = raw;
				candidateSinceUT = ut;
				return shownState;
			}
			bool precipInvolved = SaWeatherStates.PrecipClassOf(raw) != SaPrecipClass.None
				|| SaWeatherStates.PrecipClassOf(shownState) != SaPrecipClass.None;
			// On an airless body fog IS the weather, so entering it gets the quick
			// hold like precipitation: the plume is visible and the section should
			// not lag behind it. Leaving keeps the slow hold, so flying along the
			// edge of one does not blink the section on and off.
			bool enteringAirlessFog = lastBodyAirless && raw == SaWeatherState.Fog;
			double hold = precipInvolved || enteringAirlessFog ? HoldPrecipSec : HoldOtherSec;
			if (ut - candidateSinceUT >= hold) shownState = raw;
			return shownState;
		}

		/// <summary>
		/// The layers' time windows are static per body, so they are read once
		/// per vessel/body and the forecast itself is pure arithmetic on them.
		/// </summary>
		private static SaWeatherForecast BuildForecast(Vessel vessel, SaWeatherState shown, double ut)
		{
			if (shown == SaWeatherState.Unknown) return default(SaWeatherForecast);
			if (windows == null) windows = EveWeatherReader.ReadWindows(vessel);
			double day = KSPUtil.dateTimeFormatter != null && KSPUtil.dateTimeFormatter.Day > 0
				? KSPUtil.dateTimeFormatter.Day
				: 21600.0;
			return WeatherForecaster.Forecast(windows, shown, ut, HorizonDays * day);
		}

		private static SaWeatherReadout Build(Vessel vessel, double ut)
		{
			List<EveLayerReading> layers = EveWeatherReader.Read(vessel, ut);
			SaWeatherReadout result = new SaWeatherReadout { State = SaWeatherState.Clear };
			if (layers.Count == 0) return result;

			// --- 1. Precipitation: EVE's own particle gate decides, so SA
			// agrees with what the player can actually see falling.
			EveLayerReading precip = default(EveLayerReading);
			bool precipitating = false;
			foreach (EveLayerReading layer in layers)
			{
				if (layer.PrecipIntensity <= PrecipitatingIntensity) continue;
				if (layer.ParticleCount < MinWeatherParticleCount) continue;
				if (!precipitating || layer.PrecipIntensity > precip.PrecipIntensity)
				{
					precip = layer;
					precipitating = true;
				}
			}

			// --- 2. Sky opacity, for the clear/cloudy call. FX-only layers
			// render no volume, so they contribute nothing here even though
			// they may well be the precipitation above.
			double opticalDepth = 0.0;
			double lightningOpticalDepth = 0.0;
			string thickest = null;
			string thickestElectrified = null;
			double thickestContribution = 0.0;
			double thickestElectrifiedContribution = 0.0;
			foreach (EveLayerReading layer in layers)
			{
				if (layer.FxOnly) continue;
				double contribution = layer.CoverageSky * layer.Density * layer.ThicknessM;
				if (contribution <= 0.0) continue;
				opticalDepth += contribution;
				if (contribution > thickestContribution)
				{
					thickestContribution = contribution;
					thickest = layer.Name;
				}
				if (layer.HasLightning && layer.LightningFrequency >= ThunderstormLightning)
				{
					lightningOpticalDepth += contribution;
					if (contribution > thickestElectrifiedContribution)
					{
						thickestElectrifiedContribution = contribution;
						thickestElectrified = layer.Name;
					}
				}
			}
			result.SkyOpticalDepth = opticalDepth;

			if (precipitating)
			{
				result.PrecipIntensity01 = precip.PrecipIntensity;
				result.DominantLayer = precip.Name;
				result.State = PrecipitationState(precip, vessel.mainBody);
				return result;
			}

			// --- 3. Electrical sky with nothing falling: a gas giant's permanent
			// storm deck. Only reachable once precipitation has been ruled out,
			// and only at an optical depth no ordinary weather reaches.
			if (lightningOpticalDepth >= DryLightningOpticalDepth)
			{
				result.State = SaWeatherState.Thunderstorm;
				result.DominantLayer = thickestElectrified;
				return result;
			}

			// --- 4. Fog: inside a layer rather than under one. After precipitation
			// (rain you are standing in is rain) and before cloudiness (the sky
			// above is irrelevant once you cannot see through the air around you).
			//
			// The "nothing overhead" half of the rule guards against permanent
			// haze and is skipped on airless bodies: a geyser plume is a tall
			// column, so from inside it there is always more plume above and the
			// rule could never fire, leaving such a body no reachable state.
			bool airless = !vessel.mainBody.atmosphere;
			EveLayerReading fog = layers
				.Where(l => !l.FxOnly && l.CoverageHere >= FogCoverageHere
					&& (airless || l.CoverageSky <= FogCoverageSky))
				.OrderByDescending(l => l.CoverageHere)
				.FirstOrDefault();
			if (fog.Name != null)
			{
				result.State = SaWeatherState.Fog;
				result.DominantLayer = fog.Name;
				return result;
			}

			result.DominantLayer = thickest;
			result.State = opticalDepth >= CloudyOpticalDepth ? SaWeatherState.Cloudy : SaWeatherState.Clear;
			return result;
		}

		/// <summary>
		/// What kind of precipitation this is, from the particle field's own
		/// physics rather than from any name: fast-falling particles are liquid,
		/// slow-drifting ones are snow on a body that has water and dust on one
		/// that does not. Lightning promotes it to a thunderstorm, but only when
		/// the layer really carries a lightning config.
		/// </summary>
		private static SaWeatherState PrecipitationState(EveLayerReading layer, CelestialBody body)
		{
			if (layer.FallSpeed >= LiquidFallSpeed)
			{
				bool thunder = layer.HasLightning && layer.LightningFrequency >= ThunderstormLightning;
				return thunder ? SaWeatherState.Thunderstorm : SaWeatherState.Rain;
			}
			// Slow particles: snow where there is water to freeze, dust where
			// there is not. Body-level and crude, but it is the only distinction
			// EVE itself makes available without reading particle textures.
			return body.ocean ? SaWeatherState.Snow : SaWeatherState.DustStorm;
		}

		/// <summary>Called when the flight scene tears down: the cached EVE layer
		/// objects do not survive it.</summary>
		internal static void Reset()
		{
			lastVessel = null;
			lastBody = null;
			lastBodyAirless = false;
			lastSampleUT = double.NegativeInfinity;
			cached = default(SaWeatherReadout);
			shownState = SaWeatherState.Unknown;
			candidateState = SaWeatherState.Unknown;
			windows = null;
			if (Available) EveWeatherReader.ClearCaches();
		}
	}
}
