using System;
using System.Collections.Generic;
using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Vessel -> SaReadout. Pure data, no formatting (design doc §7). Call
	/// once per refresh tick (the window debounces this, not per-frame).
	/// </summary>
	internal static class SaReadoutProvider
	{
		/// <summary>
		/// M3 point 5 (design doc §5.5/§9): latitude above which longitude
		/// (and azimuth/local time/timezone/dial derived from it) is treated
		/// as numerically unstable. Retuned to 87.5° (retest 2026-07-28,
		/// empirical observation in game: the "midnight sun" effect visibly
		/// starts around there) — the original 89° estimate came from a
		/// pure elevation-amplitude argument (~cos(89°) ≈ 1° swing), tighter
		/// than what actually reads as "never gets dark" in practice.
		/// </summary>
		public const double NearPoleLatitudeThresholdDeg = 87.5;

		public static SaReadout Build(Vessel vessel)
		{
			SaReadout r = default;
			if (vessel == null || vessel.mainBody == null)
			{
				r.Valid = false;
				return r;
			}
			r.Valid = true;
			r.UT = Planetarium.GetUniversalTime();
			r.MissionTime = vessel.missionTime;
			r.VesselName = vessel.vesselName;

			CelestialBody body = vessel.mainBody;
			// displayName (verified 2026-07-19: CelestialBody.bodyDisplayName,
			// aliased as .displayName), not .bodyName/.name — the latter are
			// internal identifiers ("Sun") that ignore the pack's own chosen
			// display name ("Kerbol"). Same field WDSP uses for its own star
			// picker on the decompiled source. "^N" is a raw KSP localization
			// grammar tag baked into the localized string itself (confirmed:
			// WDSP strips the exact same suffix) — not something Localizer
			// removes for us, so we strip it too (retest 2026-07-21: showed
			// up as "KERBIN^N" in the UI).
			r.BodyName = CleanDisplayName(body.displayName);
			r.BodyHasAtmosphere = body.atmosphere;
			r.BodyIsStar = body.isStar;
			// bodyName (internal identifier), not displayName — only the
			// root star must keep the exact internal name "Sun" in any
			// planet pack; a secondary star (e.g. Grannus) never carries it
			// (bug fix 2026-07-24, see SaReadout.BodyIsSun doc comment).
			r.BodyIsSun = string.Equals(body.bodyName, "Sun", System.StringComparison.OrdinalIgnoreCase);
			// PSystemManager.OrbitRendererDataCache, not body.orbitDriver —
			// see SaReadout.BodyMapColorRaw doc comment (bug fix 2026-07-22).
			r.BodyMapColorRaw = PSystemManager.OrbitRendererDataCache != null
				&& PSystemManager.OrbitRendererDataCache.TryGetValue(body, out OrbitRendererData orbitRenderData)
				? orbitRenderData.orbitColor
				: Color.gray;

			bool resolved = StarResolver.TryResolveStar(body, out CelestialBody resolvedStar, out double _);
			CelestialBody star = resolved && resolvedStar != null
				? resolvedStar
				: (Planetarium.fetch != null ? Planetarium.fetch.Sun : FlightGlobals.Bodies[0]);
			r.StarName = star != null ? CleanDisplayName(star.displayName) : "?";

			r.IsStarLocked = SaModeSelector.IsTidalLockedOnStar(body);
			// Generic "this body doesn't rotate relative to what it orbits"
			// badge — shown regardless of mode/vessel situation. Bug found
			// in M1 retest (fase 5): excluding the star-locked case here
			// meant a vessel ORBITING a star-locked body (Moho) got no
			// LOCKED indication at all, even though it's just as true as
			// for a moon locked on its planet.
			r.BodyTidallyLocked = body.tidallyLocked;
			r.SolarDayLengthSec = BodyClock.SolarDayLengthAbsSeconds(body);
			r.Mode = SaModeSelector.Select(vessel);
			r.BodyChain = BuildBodyChain(body);
			BuildGravity(ref r, vessel, body);
			BuildHullTemperature(ref r, vessel);

			r.IsHomeBody = body.isHomeWorld;

			r.Latitude = vessel.latitude;
			r.Longitude = vessel.longitude;
			r.NearPole = Math.Abs(r.Latitude) > NearPoleLatitudeThresholdDeg;
			r.Altitude = vessel.altitude;
			r.AltitudeAglValid = vessel.heightFromTerrain >= 0.0;
			r.AltitudeAglM = vessel.heightFromTerrain;
			r.PressureKPa = vessel.staticPressurekPa;
			// Kelvin (verified M0 addendum: PhysicsGlobals.spaceTemperature = 4.0,
			// Kerbin's atmosphereTemperatureSeaLevel = 288.0 — these are Kelvin
			// values, not Celsius). atmosphericTemperature on purpose, never
			// externalTemperature (that one folds in reentry shock heating).
			r.ExternalTemperatureK = vessel.atmosphericTemperature;
			r.BiomeName = ScienceUtil.GetExperimentBiomeLocalized(body, vessel.latitude, vessel.longitude);

			r.SunElevationDeg = SolarMath.SolarElevationDeg(vessel, star);
			if (r.Mode != SaMode.Orbit)
			{
				r.SunAzimuthDeg = SolarMath.SolarAzimuthDeg(vessel, star);
			}

			// M1: stock field directly. The Kopernicus "luminosity" value
			// StarResolver can read is not verified to be an absolute-watts
			// luminosity (design doc §4.3, notes/verifiche-api.md §6) — a
			// Grannus-system cross-check is deferred to M3 before building
			// any inverse-square override on top of it.
			r.SolarFluxWm2 = vessel.solarFlux;

			if (vessel.Connection != null)
			{
				r.IsConnected = vessel.Connection.IsConnected;
				r.SignalLevel = ToSignalLevel(vessel.Connection.Signal);
			}

			int n = BodyClock.LocalHoursPerDay;
			double zoneWidth = BodyClock.ZoneWidthDeg;
			double subsolarLon = SolarMath.SubsolarLongitude(body, star);

			BuildKscTime(ref r, n, zoneWidth);

			switch (r.Mode)
			{
				case SaMode.TidalLock:
					BuildTidalLock(ref r, body, vessel, subsolarLon);
					break;
				case SaMode.Orbit:
					BuildOrbit(ref r, vessel, body, star);
					break;
				default:
					BuildSurface(ref r, n, zoneWidth, vessel, body, star, subsolarLon);
					break;
			}

			return r;
		}

		/// <summary>
		/// Local time AT THE KSC'S ACTUAL COORDINATES on the home body
		/// (bug fixed after M1 test fase 1: this used to be "UT mod day",
		/// which just repeated the UT row and ignored the KSC's real
		/// longitude — the KSC sits at a nonzero timezone like anywhere
		/// else). SpaceCenter.Instance.Latitude/Longitude are computed from
		/// the actual in-scene KSC transform (notes/verifiche-api.md
		/// addendum), so this is correct even when a planet pack relocates
		/// the KSC — no hardcoded coordinates anywhere.
		/// </summary>
		private static void BuildKscTime(ref SaReadout r, int n, double zoneWidth)
		{
			SpaceCenter sc = SpaceCenter.Instance;
			if (sc == null || sc.cb == null)
			{
				r.KscTimeValid = false;
				return;
			}

			CelestialBody home = sc.cb;
			bool resolved = StarResolver.TryResolveStar(home, out CelestialBody homeStar, out double _);
			CelestialBody effectiveStar = resolved && homeStar != null
				? homeStar
				: (Planetarium.fetch != null ? Planetarium.fetch.Sun : FlightGlobals.Bodies[0]);

			// KSC is always ON the home body by construction — always safe to
			// use the calibrate-once path (MeanTimeCalibration), pure UT
			// arithmetic from here on, no per-tick trig (retest 2026-07-21).
			double homeSolarDay = BodyClock.SolarDayLengthAbsSeconds(home);
			int kIndex = SolarMath.ZoneIndex(sc.Longitude, zoneWidth);
			double zone0Sec = MeanTimeCalibration.Zone0Seconds(home, effectiveStar, r.UT, homeSolarDay);
			double zoneSec = Wrap(zone0Sec + kIndex * (homeSolarDay / n), homeSolarDay);
			double f = zoneSec / homeSolarDay;
			SolarMath.SplitLocalTime(f, n, out int hh, out int mm, out int ss);

			r.KscTimeValid = true;
			r.KscHour = hh;
			r.KscMinute = mm;
			r.KscSecond = ss;
			r.KscTimeZoneIndex = kIndex;
		}

		private static double Wrap(double value, double period)
		{
			double v = value % period;
			if (v < 0) v += period;
			return v;
		}

		private static void BuildSurface(ref SaReadout r, int n, double zoneWidth, Vessel vessel,
			CelestialBody body, CelestialBody star, double subsolarLon)
		{
			double zoneCenter = SolarMath.ZoneCenterLongitude(vessel.longitude, zoneWidth);
			r.TimeZoneIndex = SolarMath.ZoneIndex(vessel.longitude, zoneWidth);

			// M3 point 6 (go 2026-07-28): mean-time calibration, extended
			// from home-only to every body — LOCAL TIME is the civil/mean
			// clock everywhere now, not just home (apparent solar time on
			// non-home bodies used to leak the raw DayFraction here; that
			// live value moved to the new SOLAR TIME fields below instead).
			double zone0Sec = MeanTimeCalibration.Zone0Seconds(body, star, r.UT, r.SolarDayLengthSec);
			double zoneSec = Wrap(zone0Sec + r.TimeZoneIndex * (r.SolarDayLengthSec / n), r.SolarDayLengthSec);
			double f = zoneSec / r.SolarDayLengthSec;
			SolarMath.SplitLocalTime(f, n, out int hh, out int mm, out int ss);
			r.LocalHour = hh;
			r.LocalMinute = mm;
			r.LocalSecond = ss;
			r.DayProgress01 = f;

			double hourAngle = SolarMath.HourAngleDeg(f);
			r.PhaseSurface = SolarMath.ClassifyPhase(hourAngle, zoneWidth);

			r.Sol = SolarMath.SolFromUT0(r.UT, r.SolarDayLengthSec, zoneCenter, subsolarLon);

			double rate = 360.0 / Math.Max(r.SolarDayLengthSec, 1.0);
			double tSunset = SolarMath.WrapDeg(90.0 - hourAngle) / rate;
			double tSunrise = SolarMath.WrapDeg(-90.0 - hourAngle) / rate;
			if (tSunrise < tSunset)
			{
				r.TimeToNextEventSec = tSunrise;
				r.NextEventIsSunrise = true;
			}
			else
			{
				r.TimeToNextEventSec = tSunset;
				r.NextEventIsSunrise = false;
			}

			BuildSolarTime(ref r, n, vessel, body, star, subsolarLon, rate);
		}

		/// <summary>
		/// SOLAR TIME (M3 point 6, go 2026-07-28): TRUE/apparent solar time
		/// at the vessel's EXACT longitude — never zone-quantized, never
		/// mean-time-calibrated, unlike LOCAL TIME above. This is exactly
		/// the plain live `DayFraction` formula LOCAL TIME itself used to
		/// use on non-home bodies before mean-time calibration was extended
		/// to everywhere. Same countdown logic as LOCAL TIME's own
		/// alba/tramonto (tSunset/tSunrise), just fed the exact-longitude
		/// hour angle — kept in sync with <see cref="TryBuildSolarCountdownFast"/>
		/// by using the exact same formula shape.
		/// </summary>
		private static void BuildSolarTime(ref SaReadout r, int n, Vessel vessel, CelestialBody body,
			CelestialBody star, double subsolarLon, double rate)
		{
			double fSolar = SolarMath.DayFraction(vessel.longitude, subsolarLon);
			SolarMath.SplitLocalTime(fSolar, n, out int sh, out int sm, out int ss2);
			r.SolarHour = sh;
			r.SolarMinute = sm;
			r.SolarSecond = ss2;

			double hourAngleSolar = SolarMath.HourAngleDeg(fSolar);
			double tSunsetSolar = SolarMath.WrapDeg(90.0 - hourAngleSolar) / rate;
			double tSunriseSolar = SolarMath.WrapDeg(-90.0 - hourAngleSolar) / rate;
			if (tSunriseSolar < tSunsetSolar)
			{
				r.SolarTimeToNextEventSec = tSunriseSolar;
				r.SolarNextEventIsSunrise = true;
			}
			else
			{
				r.SolarTimeToNextEventSec = tSunsetSolar;
				r.SolarNextEventIsSunrise = false;
			}

			r.EquationOfTimeSec = SolarMath.EquationOfTimeSeconds(body, star, r.UT, r.SolarDayLengthSec);
		}

		private static void BuildOrbit(ref SaReadout r, Vessel vessel, CelestialBody body, CelestialBody star)
		{
			OrbitIllumination.Status(vessel, body, star, out SaPhaseOrbit phase, out double tToTransition,
				out bool inEclipseNow, out double thetaNow, out double phi);
			r.PhaseOrbit = phase;
			r.OrbitLitFraction01 = OrbitIllumination.LitFraction(vessel, body, star);
			r.OrbitThetaNowRad = thetaNow;
			r.OrbitPhiRad = phi;

			ComputeOrbitTimer(vessel, tToTransition, inEclipseNow,
				out r.TimeToNextEventSec, out r.NextOrbitEventIsEclipse, out r.NextOrbitEventIsSoiChange, out r.OrbitPeriodSec);
		}

		/// <summary>
		/// Shared by BuildOrbit (throttled, full readout) and
		/// TryBuildOrbitTimerFast (unthrottled, SaWindow's per-tick "T−"/
		/// Period refresh — retest 2026-07-27: the 10Hz panel throttle made
		/// the orbit timer feel laggy right after a long burn reshaped the
		/// orbit) — one formula, so the two paths can never disagree.
		///
		/// Escape trajectory override: verified on the decompiled Orbit.cs/
		/// PatchedConicSolver.cs — `patchEndTransition == ESCAPE` means the
		/// CURRENT patch (always patch 0, recomputed every frame for the
		/// active vessel regardless of maneuver nodes) ends by leaving the
		/// SoI, at `EndUT`. On such a patch the eclipse/light transition
		/// time from OrbitIllumination.Status is not necessarily wrong
		/// numerically, but it's meaningless: either genuinely infinite
		/// (hyperbolic, `orbit.period` itself is PositiveInfinity per
		/// Orbit.cs) or a finite countdown to a crossing that will never
		/// happen because the vessel leaves this SoI first (still
		/// elliptical, e&lt;1, but apoapsis beyond the SoI). Both cases are
		/// replaced by a countdown to the actual SoI change, and the period
		/// is forced to infinite (the stock UI keeps showing the raw
		/// two-body value here, which is misleading — SA deliberately
		/// doesn't).
		/// </summary>
		private static void ComputeOrbitTimer(Vessel vessel, double eclipseTransitionSec, bool inEclipseNow,
			out double timeToNextEventSec, out bool nextEventIsEclipse, out bool nextEventIsSoiChange, out double orbitPeriodSec)
		{
			if (vessel.orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE)
			{
				nextEventIsSoiChange = true;
				nextEventIsEclipse = false;
				timeToNextEventSec = Math.Max(0.0, vessel.orbit.EndUT - Planetarium.GetUniversalTime());
				orbitPeriodSec = double.PositiveInfinity;
				return;
			}

			nextEventIsSoiChange = false;
			timeToNextEventSec = eclipseTransitionSec;
			nextEventIsEclipse = !inEclipseNow;
			orbitPeriodSec = vessel.orbit.period;
		}

		/// <summary>
		/// Cheap, unthrottled slice of BuildOrbit for SaWindow's fast timer
		/// refresh (retest 2026-07-27). OrbitIllumination.Status and the
		/// patched-conic fields it reads are pure O(1) trig/field access —
		/// safe to call every physics tick, unlike the full Build() (which
		/// walks vessel.parts for HULL TEMP, see BuildHullTemperature).
		/// StarResolver.TryResolveStar is cache-backed after the first call
		/// per body, so re-resolving the star here every tick is cheap too.
		/// Returns false only if there's no valid orbit context (mirrors
		/// Build()'s own guard).
		/// </summary>
		public static bool TryBuildOrbitTimerFast(Vessel vessel, out double timeToNextEventSec,
			out bool nextEventIsEclipse, out bool nextEventIsSoiChange, out double orbitPeriodSec, out bool bodyIsStar)
		{
			timeToNextEventSec = 0.0;
			nextEventIsEclipse = false;
			nextEventIsSoiChange = false;
			orbitPeriodSec = 0.0;
			bodyIsStar = false;
			if (vessel == null || vessel.mainBody == null) return false;

			CelestialBody body = vessel.mainBody;
			bodyIsStar = body.isStar;
			bool resolved = StarResolver.TryResolveStar(body, out CelestialBody resolvedStar, out double _);
			CelestialBody star = resolved && resolvedStar != null
				? resolvedStar
				: (Planetarium.fetch != null ? Planetarium.fetch.Sun : FlightGlobals.Bodies[0]);

			OrbitIllumination.Status(vessel, body, star, out SaPhaseOrbit _, out double tToTransition,
				out bool inEclipseNow, out double _, out double _);
			ComputeOrbitTimer(vessel, tToTransition, inEclipseNow,
				out timeToNextEventSec, out nextEventIsEclipse, out nextEventIsSoiChange, out orbitPeriodSec);
			return true;
		}

		/// <summary>
		/// Cheap, unthrottled slice of BuildSolarTime for SaWindow's fast
		/// per-frame refresh (M3 point 6, go 2026-07-28: user flagged the
		/// same "will 10Hz keep up" concern already solved for the orbit
		/// timer — a fast-moving vessel changes longitude continuously, so
		/// the exact-position sunrise/sunset countdown needs the same
		/// unthrottled treatment). Same formula shape as BuildSolarTime's
		/// own countdown, pure O(1) trig — safe every rendered frame, unlike
		/// the full Build() (walks vessel.parts for HULL TEMP).
		/// </summary>
		public static bool TryBuildSolarCountdownFast(Vessel vessel, out double timeToNextEventSec, out bool nextEventIsSunrise)
		{
			timeToNextEventSec = 0.0;
			nextEventIsSunrise = false;
			if (vessel == null || vessel.mainBody == null) return false;

			CelestialBody body = vessel.mainBody;
			bool resolved = StarResolver.TryResolveStar(body, out CelestialBody resolvedStar, out double _);
			CelestialBody star = resolved && resolvedStar != null
				? resolvedStar
				: (Planetarium.fetch != null ? Planetarium.fetch.Sun : FlightGlobals.Bodies[0]);

			double subsolarLon = SolarMath.SubsolarLongitude(body, star);
			double solarDayLength = BodyClock.SolarDayLengthAbsSeconds(body);
			double rate = 360.0 / Math.Max(solarDayLength, 1.0);

			double fSolar = SolarMath.DayFraction(vessel.longitude, subsolarLon);
			double hourAngleSolar = SolarMath.HourAngleDeg(fSolar);
			double tSunsetSolar = SolarMath.WrapDeg(90.0 - hourAngleSolar) / rate;
			double tSunriseSolar = SolarMath.WrapDeg(-90.0 - hourAngleSolar) / rate;
			if (tSunriseSolar < tSunsetSolar)
			{
				timeToNextEventSec = tSunriseSolar;
				nextEventIsSunrise = true;
			}
			else
			{
				timeToNextEventSec = tSunsetSolar;
				nextEventIsSunrise = false;
			}
			return true;
		}

		private static void BuildTidalLock(ref SaReadout r, CelestialBody body, Vessel vessel, double subsolarLon)
		{
			double hourAngle = SolarMath.DeltaLon(subsolarLon, vessel.longitude);
			r.TidalLockHourAngleDeg = hourAngle;
			const double half = OrbitIllumination.TerminatorBandDeg;

			if (Math.Abs(Math.Abs(hourAngle) - 90.0) < half)
			{
				r.PhaseTidalLock = SaPhaseTidalLock.Terminator;
			}
			else if (Math.Abs(hourAngle) < 90.0)
			{
				r.PhaseTidalLock = SaPhaseTidalLock.Day;
			}
			else
			{
				r.PhaseTidalLock = SaPhaseTidalLock.Night;
			}

			SolarMath.TerminatorDistance(body, vessel.latitude, vessel.longitude, subsolarLon,
				out double distKm, out double distDeg, out bool toEast);
			r.TerminatorDistanceKm = distKm;
			r.TerminatorDistanceDeg = distDeg;
			r.TerminatorToEast = toEast;
		}

		/// <summary>
		/// Strips the Lingoona Grammar gender tag KSP bakes into localized
		/// body names (bug fix 2026-07-24: NOT a fixed "^N" — the tag letter
		/// is the grammatical gender of that specific word in the active
		/// language, e.g. confirmed on the installed it-it dictionary:
		/// "Kerbin^N" but "Sole^M" — a plain `Replace("^N","")` (what WDSP's
		/// own star picker does too) silently fails on anything not tagged
		/// N). `LocalizeRemoveGender()` is KSP's own extension method
		/// (`LingoonaGrammarExtensions.cs`, verified on the decompiled
		/// source): cuts from the LAST '^' onward regardless of the letter,
		/// safe no-op if there's no tag at all. Covers both an explicit
		/// literal displayName (passes through Localizer.Format unresolved)
		/// and a loc-key reference (resolved to whatever the active
		/// language's dictionary entry says, tag included) — Format()
		/// either returns the input verbatim (key not found) or the
		/// dictionary value (key found), and this runs after that point
		/// either way.
		/// </summary>
		internal static string CleanDisplayName(string raw)
		{
			return string.IsNullOrEmpty(raw) ? raw : raw.LocalizeRemoveGender();
		}

		/// <summary>Star -> ... -> current body, star included (footer chain, design doc §5 rework).</summary>
		private static CelestialBody[] BuildBodyChain(CelestialBody body)
		{
			List<CelestialBody> chain = new List<CelestialBody>(4);
			CelestialBody cur = body;
			int guard = 0;
			while (cur != null && guard++ < 8)
			{
				chain.Add(cur);
				if (cur.isStar) break;
				cur = cur.referenceBody;
			}
			chain.Reverse();
			return chain.ToArray();
		}

		/// <summary>
		/// Verified on the decompiled ModuleEnviroSensor (SensorType.GRAV) +
		/// FlightGlobals.getGeeForceAtPosition: the live reading is a real
		/// acceleration in m/s^2 (gMagnitudeAtCenter is GM in SI units, not
		/// pre-scaled to "g" despite the rest of the stock UI using g's
		/// elsewhere), gated by the SAME range check the stock part uses
		/// (beyond 3 body radii it has no reading). GeeASL is already in g's,
		/// so the fixed ASL value needs the same SI conversion constant the
		/// game itself uses (PhysicsGlobals.GravitationalAcceleration).
		/// </summary>
		private static void BuildGravity(ref SaReadout r, Vessel vessel, CelestialBody body)
		{
			r.GravityLiveValid = vessel.orbit.altitude <= body.Radius * 3.0;
			if (r.GravityLiveValid)
			{
				r.GravityLiveMps2 = FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude;
			}
			r.GravityAslMps2 = body.GeeASL * PhysicsGlobals.GravitationalAcceleration;
		}

		/// <summary>
		/// skinThermalMass-weighted average of skinTemperature across every
		/// part (verified on the decompiled Part.cs: `skinThermalMass`/
		/// `skinTemperature`/`skinMaxTemp` are all public). HULL TEMP names
		/// the exterior shell, and the skin layer is what actually responds
		/// to reentry/hypersonic heating; the core lags far behind via slow
		/// internal conduction (`skinToInternalFlux`). skinThermalMass is a
		/// genuinely separate quantity from `thermalMass`, not an
		/// approximation of it — verified on the decompiled
		/// FlightIntegrator: once corrected, `thermalMass = Max(thermalMass
		/// - skinThermalMass, 0.1)`, i.e. KSP itself treats `thermalMass` as
		/// internal-only. Weighting by mass keeps the same physical logic as
		/// the original core-based design (different materials, different
		/// specific heat, thermalMass/skinThermalMass already fold that in)
		/// — just applied to the layer that's actually informative here.
		///
		/// Bug fix 2026-07-27, round 2 (test M3 retest, user follow-up: "in
		/// effetti dovrebbe essere media pesata di SkinTemperature, non
		/// core"): round 1 only fixed HullTempWorstRatio (the color) to
		/// consider skin, leaving the displayed VALUE core-weighted — a
		/// mismatch that could show a cool core reading in red/yellow
		/// (color driven by skin) during exactly the reentry scenario this
		/// row exists for. Switching the value itself to skin removes the
		/// mismatch and makes HULL TEMP responsive to the same event its
		/// color already reacts to.
		///
		/// HullTempWorstRatio stays `max(temperature/maxTemp,
		/// skinTemperature/skinMaxTemp)` (unchanged from round 1) and
		/// SEPARATE from the average on purpose (design discussion
		/// 2026-07-26): a single small part near its max while the rest of
		/// a heavy vessel stays cool would be invisible in a weighted
		/// average, so the color-driving signal is the single worst ratio
		/// across all parts, not the average's own ratio. Verified on the
		/// decompiled `Part.HeatGaugeUpdate()` (stock heat gauge overlay)
		/// and the overheat-explosion roll in `FlightIntegrator`: both use
		/// exactly this max-of-core-and-skin formula. skinMaxTemp defaults
		/// to maxTemp at part init if left at its own -1 sentinel, so it
		/// should never be &lt;=0 in flight, but the guard costs nothing.
		///
		/// Guards: parts with skinTemperature &lt; 0 (Part.cs default, never
		/// updated) or maxTemp &lt;= 0 are skipped entirely; if no part
		/// qualifies (e.g. skinThermalMass sums to ~0), falls back to
		/// vessel.rootPart alone.
		/// </summary>
		private static void BuildHullTemperature(ref SaReadout r, Vessel vessel)
		{
			double sumSkinThermalMass = 0.0;
			double sumWeightedSkinTemp = 0.0;
			double worstRatio = 0.0;

			if (vessel.parts != null)
			{
				for (int i = 0; i < vessel.parts.Count; i++)
				{
					Part p = vessel.parts[i];
					if (p == null || p.skinTemperature < 0.0 || p.maxTemp <= 0.0) continue;

					sumSkinThermalMass += p.skinThermalMass;
					sumWeightedSkinTemp += p.skinThermalMass * p.skinTemperature;

					double ratio = WorstPartRatio(p);
					if (ratio > worstRatio) worstRatio = ratio;
				}
			}

			if (sumSkinThermalMass > 1e-6)
			{
				r.HullTempK = sumWeightedSkinTemp / sumSkinThermalMass;
			}
			else
			{
				Part root = vessel.rootPart;
				r.HullTempK = (root != null && root.skinTemperature >= 0.0) ? root.skinTemperature : 0.0;
				if (root != null && root.maxTemp > 0.0) worstRatio = WorstPartRatio(root);
			}
			r.HullTempWorstRatio = worstRatio;
		}

		/// <summary>max(core, skin) ratio for one part — see BuildHullTemperature's 2026-07-27 doc comment for why both layers matter.</summary>
		private static double WorstPartRatio(Part p)
		{
			double coreRatio = p.temperature / p.maxTemp;
			double skinRatio = p.skinMaxTemp > 0.0 ? p.skinTemperature / p.skinMaxTemp : 0.0;
			return Math.Max(coreRatio, skinRatio);
		}

		private static SaSignalLevel ToSignalLevel(CommNet.SignalStrength s)
		{
			// Stock thresholds (notes/verifiche-api.md §7): >0.75 Green,
			// >0.5 Yellow, >0.25 Orange, >1e-9 Red, else None. Refined after
			// M1 feedback: keep "no connection at all" (None) visually
			// distinct (grey/off) from "connected but very weak" (Red) —
			// collapsing them together hid a real state change.
			switch (s)
			{
				case CommNet.SignalStrength.Green:
					return SaSignalLevel.Green;
				case CommNet.SignalStrength.Yellow:
				case CommNet.SignalStrength.Orange:
					return SaSignalLevel.Yellow;
				case CommNet.SignalStrength.Red:
					return SaSignalLevel.Red;
				default:
					return SaSignalLevel.Off;
			}
		}
	}
}
