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
		/// Latitude above which longitude — and azimuth, local time, timezone and
		/// the dial with it — is treated as numerically unstable (design doc §5.5).
		/// Tuned by eye rather than from the elevation amplitude alone: this is
		/// about where the sky visibly stops getting dark.
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
			// displayName, not bodyName/name: the latter are internal identifiers
			// ("Sun") that ignore the pack's own display name ("Kerbol"). The
			// grammar tag is baked into the localized string and Localizer does
			// not strip it, so StripGenderTag does.
			r.BodyName = CleanDisplayName(body.displayName);
			r.BodyNameInternal = body.bodyName;
			r.BodyHasAtmosphere = body.atmosphere;
			r.BodyIsStar = body.isStar;
			// bodyName, not displayName: only the root star keeps the internal
			// name "Sun" in any planet pack (see SaReadout.BodyIsSun).
			r.BodyIsSun = string.Equals(body.bodyName, "Sun", System.StringComparison.OrdinalIgnoreCase);
			// OrbitRendererDataCache, not body.orbitDriver: see
			// SaReadout.BodyMapColorRaw.
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
			// Generic "this body does not rotate relative to what it orbits"
			// badge, shown whatever the mode: it is as true of a star-locked body
			// seen from orbit as of a moon locked on its planet.
			r.BodyTidallyLocked = body.tidallyLocked;
			r.SolarDayLengthSec = BodyClock.SolarDayLengthAbsSeconds(body);
			r.Mode = SaModeSelector.Select(vessel);
			r.BodyChain = BuildBodyChain(body);
			BuildGravity(ref r, vessel, body);
			BuildHullTemperature(ref r, vessel);
			// Self-throttled to ~1 Hz internally, since sampling every cloud layer
			// costs CPU texture reads; a no-op without EVE installed.
			r.Weather = WeatherClassifier.Classify(vessel, r.UT);

			r.ExtTempUnlocked = SaScienceGate.IsUnlocked(SaScienceGate.FieldExtTemp, body);
			r.PressureUnlocked = SaScienceGate.IsUnlocked(SaScienceGate.FieldPressure, body);
			r.GravityUnlocked = SaScienceGate.IsUnlocked(SaScienceGate.FieldGravity, body);
			// No atmosphere, no atmospheric analysis to credit: the plume that is
			// the only weather an airless body can show is not something an
			// experiment could have told you about.
			r.WeatherUnlocked = !body.atmosphere || SaScienceGate.IsUnlocked(SaScienceGate.FieldWeather, body);

			r.IsHomeBody = body.isHomeWorld;

			r.Latitude = vessel.latitude;
			r.Longitude = vessel.longitude;
			r.NearPole = Math.Abs(r.Latitude) > NearPoleLatitudeThresholdDeg;
			r.Altitude = vessel.altitude;
			r.AltitudeAglValid = vessel.heightFromTerrain >= 0.0;
			r.AltitudeAglM = vessel.heightFromTerrain;
			r.PressureKPa = vessel.staticPressurekPa;
			// Kelvin. atmosphericTemperature on purpose, never externalTemperature,
			// which folds in reentry shock heating.
			r.ExternalTemperatureK = vessel.atmosphericTemperature;
			r.BiomeName = ScienceUtil.GetExperimentBiomeLocalized(body, vessel.latitude, vessel.longitude);

			r.SunElevationDeg = SolarMath.SolarElevationDeg(vessel, star);
			if (r.Mode != SaMode.Orbit)
			{
				r.SunAzimuthDeg = SolarMath.SolarAzimuthDeg(vessel, star);
			}

			// Stock field, used directly: the Kopernicus luminosity StarResolver
			// can read is not confirmed to be an absolute figure (design doc
			// §4.3), so no inverse-square override is built on it.
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
		/// Local time at the KSC's ACTUAL coordinates on the home body, which sit
		/// in a nonzero timezone like anywhere else — not "UT mod day", which
		/// would just repeat the UT row. SpaceCenter.Instance derives them from
		/// the in-scene KSC transform, so a pack that relocates the KSC is
		/// followed with no hardcoded coordinates.
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

			// The KSC is on the home body by construction, so the calibrate-once
			// path always applies: pure UT arithmetic, no per-tick trig.
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

			// Mean-time calibration on every body: LOCAL TIME is the civil clock
			// everywhere, and the live apparent value lives in the SOLAR TIME
			// fields below instead.
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
		/// TRUE/apparent solar time at the vessel's exact longitude: never
		/// zone-quantized and never mean-time-calibrated, unlike LOCAL TIME above.
		/// The sunrise/sunset countdown is LOCAL TIME's own logic fed the
		/// exact-longitude hour angle, in the same formula shape as
		/// <see cref="TryBuildSolarCountdownFast"/> so the two stay in step.
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
		/// Shared by BuildOrbit and the unthrottled TryBuildOrbitTimerFast, so the
		/// two paths cannot disagree. patchEndTransition == ESCAPE triggers the
		/// override described on SaReadout.NextOrbitEventIsSoiChange.
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
		/// Cheap, unthrottled slice of BuildOrbit for the fast timer refresh:
		/// OrbitIllumination.Status and the patched-conic fields are O(1), and
		/// TryResolveStar is cache-backed, so this is safe every physics tick —
		/// unlike the full Build(), which walks vessel.parts for HULL TEMP.
		/// Returns false only when there is no valid orbit context.
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
		/// Cheap, unthrottled slice of BuildSolarTime for the fast per-frame
		/// refresh: a moving vessel changes longitude continuously, so its
		/// exact-position sunrise/sunset countdown needs the same unthrottled
		/// treatment as the orbit timer. Same formula shape as BuildSolarTime's
		/// own countdown and pure O(1) trig.
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
		/// Strips the Lingoona grammar tag KSP bakes into localized body names.
		/// The tag letter is the word's grammatical gender in the active language
		/// ("Kerbin^N" but "Sole^M"), so a plain Replace("^N", "") silently fails
		/// on everything else; KSP's own LocalizeRemoveGender() cuts from the last
		/// '^' whatever the letter, and is a no-op when there is no tag. Runs
		/// after Localizer.Format, so it covers both a literal displayName and a
		/// resolved loc key.
		/// </summary>
		internal static string CleanDisplayName(string raw)
		{
			return string.IsNullOrEmpty(raw) ? raw : raw.LocalizeRemoveGender();
		}

		/// <summary>Star -> ... -> current body, star included (footer chain, design doc §5).</summary>
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
		/// The live reading is a real acceleration in m/s^2, under the same range
		/// check the stock gravimeter uses: beyond 3 body radii it has none.
		/// GeeASL is already in g, so the fixed ASL figure is converted with the
		/// game's own PhysicsGlobals.GravitationalAcceleration.
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
		/// Both quantities are described on SaReadout.HullTempK. Guards: parts with
		/// skinTemperature &lt; 0 (the never-updated default) or maxTemp &lt;= 0 are
		/// skipped, and if none qualifies this falls back to the root part alone.
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

		/// <summary>max(core, skin) ratio for one part; see BuildHullTemperature
		/// for why both layers matter.</summary>
		private static double WorstPartRatio(Part p)
		{
			double coreRatio = p.temperature / p.maxTemp;
			double skinRatio = p.skinMaxTemp > 0.0 ? p.skinTemperature / p.skinMaxTemp : 0.0;
			return Math.Max(coreRatio, skinRatio);
		}

		private static SaSignalLevel ToSignalLevel(CommNet.SignalStrength s)
		{
			// Stock thresholds: >0.75 Green, >0.5 Yellow, >0.25 Orange, >1e-9 Red,
			// else None. None stays distinct from Red, since "no connection at
			// all" and "connected but very weak" are different states.
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
