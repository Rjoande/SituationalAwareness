using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Turns EVE's raw cloud layers into one weather state (design in
	/// notes/indagine-meteo.md §3, thresholds tuned on the author's 152-press
	/// survey — notes/survey-analisi.md).
	///
	/// Classifies on FUNCTIONAL properties only, never on layer names: a name
	/// table is what makes WeatherDrivenSolarPanel silently mis-categorise every
	/// pack it has not been updated for, whereas fall speed, particle count and
	/// optical density mean the same thing in any EVE volumetrics pack.
	///
	/// Holds no EVE types itself — everything that does lives in
	/// EveWeatherReader, which is only ever entered once Available says the
	/// assemblies are loaded.
	/// </summary>
	internal static class WeatherClassifier
	{
		// --- Thresholds -----------------------------------------------------
		// Every number below is tuned against the survey, and every one of them
		// is a judgement call at a genuinely fuzzy boundary — see the analysis
		// note before changing any of them.

		/// <summary>Optical depth (coverage x density x thickness, summed over
		/// the layers overhead) above which the sky reads as cloudy rather than
		/// clear. Deliberately weighted by density, so a thin cirrus veil at 50%
		/// coverage stays "clear" the way the survey's author called it while a
		/// cumulus deck at 10% does not.
		///
		/// 0.3 rather than the 0.05 that maximised raw agreement: 0.05 scored
		/// two points higher overall but called 55% of the author's clear skies
		/// cloudy, which reads as a broken panel. 0.3 is balanced (64% of clear
		/// calls and 67% of cloudy calls match) and that is close to the ceiling
		/// — the labels themselves overlap, "Clear / partly cloudy" appears
		/// verbatim in the survey notes.</summary>
		private const double CloudyOpticalDepth = 0.3;

		/// <summary>Coverage AT the vessel above which we are inside a layer
		/// rather than under it.</summary>
		private const float FogCoverageHere = 0.15f;

		/// <summary>...and coverage overhead must be essentially nothing for it
		/// to be fog. This second half matters more than the first: a thick
		/// layer that surrounds you AND continues above you is overcast (or a
		/// storm), not fog. Without it Eve — whose permanent haze sits at 0.86
		/// around the vessel and 0.76 overhead — reported fog forever, and
		/// three quarters of the survey's rain and storm samples came out as
		/// fog too.</summary>
		private const float FogCoverageSky = 0.05f;

		/// <summary>Particle fields smaller than this are scenery, not weather:
		/// real precipitation runs 50k-800k particles, while Duna's permanent
		/// ambient haze is 500 and its meteor shower is 2. Without this floor
		/// Duna would report a dust storm at all times. Set below EVE's own
		/// 10000 default so that a config which simply never sets a count still
		/// counts as real weather.</summary>
		internal const float MinWeatherParticleCount = 5000f;

		/// <summary>Fall speed separating liquid from everything that drifts:
		/// rain is 12-25 and hail 32, while snow and dust sit at 0.3-0.8.</summary>
		internal const float LiquidFallSpeed = 5f;

		/// <summary>Interpolated lightning frequency (on a layer that actually
		/// has a lightning config) above which precipitation is a thunderstorm.
		/// The survey's three "storm" presses read 0.58 and its one "rain" press
		/// 0.66 — the two are NOT separable in the data, so this threshold
		/// mostly decides how far into the thunder cloud type you must be, not
		/// storm-versus-rain. Kerbin's plain Rain type sits at 0.05 and stays
		/// below it.
		///
		/// Also used by the dry-lightning path below.</summary>
		private const float ThunderstormLightning = 0.25f;

		/// <summary>
		/// Optical depth contributed by layers that can actually produce
		/// lightning, above which the sky is a thunderstorm even with nothing
		/// falling — a gas giant's permanent electrical cloud deck has no rain
		/// layer to detect.
		///
		/// The first attempt at this gated on raw coverage and produced 18
		/// thunderstorms on clear or merely cloudy skies, so the rule was
		/// dropped. Weighting by optical depth instead separates cleanly:
		/// across the survey, every non-storm press on Kerbin, Duna and Laythe
		/// scores exactly 0.00 here, while Eve's storms score 43 and Jool's
		/// 1800-2500. 10 sits at roughly 4x margin on both sides of that gap,
		/// and the result is stable anywhere from 5 to 100 — this is not a
		/// delicately balanced number.
		/// </summary>
		private const double DryLightningOpticalDepth = 10.0;

		/// <summary>EVE's particle gate is 0..1; anything above this counts as
		/// actually falling. Kept low on purpose — the gate is already zero when
		/// EVE draws nothing.</summary>
		private const float PrecipitatingIntensity = 0.02f;

		// Weather changes on the scale of minutes; sampling every layer costs
		// two CPU texture samples each, so the 10 Hz readout reuses a cached
		// classification instead of recomputing it every tick.
		private const double RefreshIntervalSec = 1.0;

		// --- Display hysteresis (user decision 2026-09-10: 5 s / 30 s) ------
		// A vessel moving along a front, or parked right on the clear/cloudy
		// optical-depth boundary, sees the raw classification flip every few
		// samples. The shown state only follows the raw one once the raw one
		// has held steady for a while — 30 s for the fuzzy calls (clear vs
		// cloudy, fog in/out, dry lightning), but only 5 s when precipitation
		// starts or stops: EVE's particle gate is crisp and the player can see
		// the drops, so half a minute of CLEAR under visible rain would read
		// as a bug rather than as a steady panel.

		private const double HoldPrecipSec = 5.0;
		private const double HoldOtherSec = 30.0;

		/// <summary>Forecast horizon: three calendar days. Transitions past it
		/// are not worth a line, and "no change" past it is worth exactly one.</summary>
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
		/// Airless bodies (test 2026-09-11, Vall's geysers): EVE can still put
		/// a plume or a dust field on a body with no atmosphere, and standing
		/// in one is weather worth showing. But Clear or Cloudy on a body with
		/// no sky is not — a decorative layer, or Dres's ring overhead, must not
		/// paint "clear" on every airless moon. So those two states are reported
		/// as Unknown there (section hidden), and only fog or precipitation
		/// shows: the section appears when you enter the plume and goes away
		/// when you leave it.
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
			// Keyed on vessel AND body (bug, test 2026-09-11): the same craft
			// moved to another body — cheat menu, or a real Laythe-to-Jool
			// flight — kept the previous body's time windows and reported
			// Kerbin's fronts on Duna. Everything cached here is per body.
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
				// Hidden, but the hysteresis above keeps tracking the real
				// state, so re-entering the plume is judged against Clear,
				// not against a fresh Unknown.
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
			// On an airless body fog IS the weather (a geyser plume): entering
			// it gets the quick hold, like precipitation — the plume is
			// visible and the section should not lag half a minute behind it.
			// Leaving keeps the slow hold, so a plume you fly along the edge
			// of does not blink the whole section on and off.
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

			// --- 4. Fog: inside a layer rather than under one. Checked after
			// precipitation (rain you are standing in is rain, not fog) and
			// before cloudiness (the sky above is irrelevant once you cannot
			// see through the air around you).
			//
			// The "nothing overhead" half of the rule exists for Eve's
			// permanent haze and is skipped on airless bodies (test
			// 2026-09-11, Vall): a geyser plume is an 80 km column, so from
			// inside it there is always more plume above you, and the rule
			// could never fire — the one state such a body can show was
			// unreachable. With no atmosphere there is no haze to guard
			// against, so being inside a layer is all that fog means there.
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

		/// <summary>Called when the flight scene tears down: the cached EVE
		/// layer objects do not survive it.</summary>
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
