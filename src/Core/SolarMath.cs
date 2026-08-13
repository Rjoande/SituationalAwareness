using System;
using UnityEngine;

namespace SituationalAwareness.Core
{
	public enum SaPhaseSurface { Dawn, Morning, Noon, Afternoon, Dusk, Night }

	public enum SaPhaseTidalLock { Day, Terminator, Night }

	/// <summary>
	/// Local-time / day-phase math (design doc §3, §4.2, §5.3). Longitudes
	/// throughout are degrees, normalized to (-180, 180]. Never assumes stock
	/// body physics — everything is read from the CelestialBody/star at
	/// runtime, so this is planet-pack-safe by construction.
	/// </summary>
	internal static class SolarMath
	{
		private const double Rad2Deg = 180.0 / Math.PI;
		private const double Deg2Rad = Math.PI / 180.0;

		// Solar elevation snap threshold against float noise near the horizon
		// (ported from RealBattery's SolarElevationRad).
		private const double ElevationSnapDeg = 0.5;

		private static double Clamp(double value, double min, double max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}

		/// <summary>Normalizes a longitude to (-180, 180].</summary>
		public static double NormalizeLon(double lonDeg)
		{
			double l = lonDeg % 360.0;
			if (l <= -180.0) l += 360.0;
			else if (l > 180.0) l -= 360.0;
			return l;
		}

		/// <summary>Wraps a degree value to [0, 360).</summary>
		public static double WrapDeg(double deg)
		{
			double d = deg % 360.0;
			if (d < 0) d += 360.0;
			return d;
		}

		/// <summary>Signed shortest angular delta a→b in degrees, in (-180, 180]. Positive = b is east of a.</summary>
		public static double DeltaLon(double aDeg, double bDeg)
		{
			return NormalizeLon(bDeg - aDeg);
		}

		public static double SubsolarLongitude(CelestialBody body, CelestialBody star)
		{
			return body.GetLongitude(star.position);
		}

		/// <summary>
		/// Fraction of the local solar day elapsed (0 = midnight, 0.5 = noon),
		/// design doc §3.2. GetLongitude already encodes the body's rotation
		/// direction (notes/verifiche-api.md §2) — no manual retrograde
		/// correction needed here.
		/// </summary>
		public static double DayFraction(double observerLonDeg, double subsolarLonDeg)
		{
			double diff = ((observerLonDeg - subsolarLonDeg + 180.0) % 360.0 + 360.0) % 360.0;
			return diff / 360.0;
		}

		/// <summary>Center longitude of the timezone slice containing lonDeg (design doc §3.3).</summary>
		public static double ZoneCenterLongitude(double lonDeg, double zoneWidthDeg)
		{
			double norm = NormalizeLon(lonDeg);
			double k = Math.Round(norm / zoneWidthDeg);
			return NormalizeLon(k * zoneWidthDeg);
		}

		public static int ZoneIndex(double lonDeg, double zoneWidthDeg)
		{
			double norm = NormalizeLon(lonDeg);
			return (int)Math.Round(norm / zoneWidthDeg);
		}

		/// <summary>Splits a [0,1) day fraction into local hh:mm:ss for N local hours per day.</summary>
		public static void SplitLocalTime(double dayFraction01, int hoursPerDay, out int hh, out int mm, out int ss)
		{
			double totalHours = dayFraction01 * hoursPerDay;
			hh = (int)Math.Floor(totalHours) % hoursPerDay;
			if (hh < 0) hh += hoursPerDay;
			double fracHour = totalHours - Math.Floor(totalHours);
			double totalMinutes = fracHour * 60.0;
			mm = (int)Math.Floor(totalMinutes);
			double fracMinute = totalMinutes - mm;
			ss = (int)Math.Floor(fracMinute * 60.0);
		}

		/// <summary>Sol counter from UT 0, per body, bypassing Kronometer's calendar epoch offset (design doc §3.4).</summary>
		public static int SolFromUT0(double ut, double solarDayLengthAbsSec, double zoneCenterLonDeg, double subsolarLonDeg)
		{
			double cycles = ut / solarDayLengthAbsSec;
			double fNow = DayFraction(zoneCenterLonDeg, subsolarLonDeg);
			double cyclesFrac = cycles - Math.Floor(cycles);
			double f0 = fNow - cyclesFrac;
			f0 -= Math.Floor(f0);
			return 1 + (int)Math.Floor(cycles + f0);
		}

		/// <summary>Hour angle from local noon, degrees, in (-180, 180] (design doc §3.5: H = (f-0.5)*360).</summary>
		public static double HourAngleDeg(double dayFraction01)
		{
			return NormalizeLon((dayFraction01 - 0.5) * 360.0);
		}

		/// <summary>Surface day-phase classification from the timezone's hour angle (design doc §3.5 table).</summary>
		public static SaPhaseSurface ClassifyPhase(double hourAngleDeg, double zoneWidthDeg)
		{
			double half = zoneWidthDeg / 2.0;
			double h = hourAngleDeg;

			if (Math.Abs(h) < half) return SaPhaseSurface.Noon;
			if (Math.Abs(h + 90.0) < half) return SaPhaseSurface.Dawn;
			if (Math.Abs(h - 90.0) < half) return SaPhaseSurface.Dusk;
			if (h >= -90.0 + half && h < -half) return SaPhaseSurface.Morning;
			if (h > half && h <= 90.0 - half) return SaPhaseSurface.Afternoon;
			return SaPhaseSurface.Night;
		}

		/// <summary>
		/// Solar elevation above the local horizon, degrees. Ported from
		/// RealBattery's SolarElevationRad (zenith angle from up·sunDir),
		/// with the same anti-noise snap near the horizon.
		/// </summary>
		public static double SolarElevationDeg(Vessel v, CelestialBody star)
		{
			Vector3d worldPos = v.GetWorldPos3D();
			Vector3d up = (worldPos - v.mainBody.position).normalized;
			Vector3d sunDir = (star.position - worldPos).normalized;

			double cosz = Clamp(Vector3d.Dot(up, sunDir), -1.0, 1.0);
			double zenith = Math.Acos(cosz);
			double elevRad = Math.PI / 2.0 - zenith;

			double elevDeg = elevRad * Rad2Deg;
			if (Math.Abs(elevDeg) < ElevationSnapDeg) elevDeg = 0.0;
			return elevDeg;
		}

		/// <summary>Solar azimuth, degrees, 0 = north, from vessel.north/east (design doc §4.2, verified M0 §5).</summary>
		public static double SolarAzimuthDeg(Vessel v, CelestialBody star)
		{
			Vector3d sunDir = (star.position - v.GetWorldPos3D()).normalized;
			double e = Vector3d.Dot(sunDir, v.east);
			double n = Vector3d.Dot(sunDir, v.north);
			double az = Math.Atan2(e, n) * Rad2Deg;
			return WrapDeg(az);
		}

		/// <summary>
		/// Distance from the vessel to the nearest terminator (design doc
		/// §5.3, tidal-lock-on-star only): bidirectional, always the closer
		/// of the two terminators (subsolar ± 90°), measured along the
		/// parallel (R·cos(lat)·Δλ) — the distance a rover actually drives.
		/// </summary>
		public static void TerminatorDistance(CelestialBody body, double vesselLatDeg, double vesselLonDeg,
			double subsolarLonDeg, out double distanceKm, out double distanceDeg, out bool toEast)
		{
			double termDawn = NormalizeLon(subsolarLonDeg - 90.0);
			double termDusk = NormalizeLon(subsolarLonDeg + 90.0);
			double deltaDawn = DeltaLon(vesselLonDeg, termDawn);
			double deltaDusk = DeltaLon(vesselLonDeg, termDusk);
			double delta = Math.Abs(deltaDawn) <= Math.Abs(deltaDusk) ? deltaDawn : deltaDusk;

			distanceDeg = Math.Abs(delta);
			toEast = delta > 0.0;

			double latRad = vesselLatDeg * Deg2Rad;
			distanceKm = (body.Radius / 1000.0) * Math.Cos(latRad) * Math.Abs(delta) * Deg2Rad;
		}

		/// <summary>
		/// Mean-minus-true clock gap in seconds at the given UT (equation of
		/// center, e^3 order). Moved here from the old HomeClockCalibration
		/// (M3 point 6, go 2026-07-28) and generalized to any body — reused
		/// live by the SOLAR TIME row's equation-of-time display, not just
		/// MeanTimeCalibration's one-time offset.
		///
		/// Generalization pitfall (caught before writing this): the
		/// eccentricity term must come from the orbit AROUND THE STAR, not
		/// from `body.orbit` directly — for a moon like Mun, `body.orbit` is
		/// Mun's orbit around KERBIN, whose eccentricity has nothing to do
		/// with the equation of time (that's governed by Kerbin's own
		/// eccentricity around the Sun). <see cref="StarOrbitingAncestor"/>
		/// walks the referenceBody chain up to whichever ancestor orbits
		/// `star` directly, and its eccentricity is what's used here — for
		/// Kerbin itself this is a no-op (already the star-orbiting body).
		/// `solarDayLength` stays the LOCAL body's own value (its own
		/// rotation), only the eccentricity/mean-anomaly term changes source.
		/// </summary>
		public static double EquationOfTimeSeconds(CelestialBody body, CelestialBody star, double ut, double solarDayLength)
		{
			CelestialBody eccSource = StarOrbitingAncestor(body, star);
			if (eccSource == null || eccSource.orbit == null) return 0.0;
			double e = eccSource.orbit.eccentricity;
			if (e <= 0.0) return 0.0;

			double m = eccSource.orbit.getMeanAnomalyAtUT(ut);
			double e2 = e * e;
			double e3 = e2 * e;
			double meanMinusTrueRad = (2.0 * e - 0.25 * e3) * Math.Sin(m)
				+ 1.25 * e2 * Math.Sin(2.0 * m)
				+ (13.0 / 12.0) * e3 * Math.Sin(3.0 * m);
			return meanMinusTrueRad * (solarDayLength / (2.0 * Math.PI));
		}

		/// <summary>
		/// Walks referenceBody from `body` up to the ancestor that orbits
		/// `star` directly (reference equality — star is the same singleton
		/// CelestialBody instance StarResolver already resolved, so this is
		/// safe). Returns `body` itself if it already orbits the star (the
		/// common case: the home body, or any planet), or null if the chain
		/// never reaches it (shouldn't happen for a resolved star, but a
		/// planet-pack edge case isn't worth crashing over).
		/// </summary>
		private static CelestialBody StarOrbitingAncestor(CelestialBody body, CelestialBody star)
		{
			CelestialBody cur = body;
			int guard = 0;
			while (cur != null && cur.referenceBody != null && cur.referenceBody != star)
			{
				cur = cur.referenceBody;
				if (++guard > 32) return null; // pathological cfg, never trust an unbounded loop
			}
			return cur != null && cur.referenceBody == star ? cur : null;
		}
	}
}
