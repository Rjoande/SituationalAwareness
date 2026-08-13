using System;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Global local-hour system derived from the game's own clock (design doc
	/// §3.1) — never hardcoded, harmonizes with whatever the player sees
	/// elsewhere in the UI (Kronometer-aware: stock 6h, JNSQ+Kronometer 12h,
	/// RSS 24h...). N is a single global value, not per-body: KSPUtil's
	/// dateTimeFormatter is one static instance for the whole game.
	/// </summary>
	internal static class BodyClock
	{
		/// <summary>
		/// solarDayLength beyond this (seconds) is treated as the star-lock
		/// sentinel. Verified on the decompiled source (notes/verifiche-api.md
		/// §1): a body tidally locked on its star gets exactly
		/// double.MaxValue from the game itself (a FINITE value) — the guard
		/// must be a threshold, never IsInfinity/IsFinite alone.
		/// </summary>
		public const double StarLockDayLengthThreshold = 1e17;

		/// <summary>
		/// Local hours per solar day, rounded from the active dateTimeFormatter.
		/// Without Kronometer this is disconnected from the home body's real
		/// physics (design doc §3.6) — intentional: SA follows the game's own
		/// clock, not a recomputed truth.
		/// </summary>
		public static int LocalHoursPerDay
		{
			get
			{
				IDateTimeFormatter fmt = KSPUtil.dateTimeFormatter;
				if (fmt == null || fmt.Hour <= 0)
				{
					return 6;
				}
				int n = (int)Math.Round((double)fmt.Day / fmt.Hour);
				return Math.Max(1, n);
			}
		}

		/// <summary>Width of one timezone slice, in degrees of longitude.</summary>
		public static double ZoneWidthDeg => 360.0 / LocalHoursPerDay;

		/// <summary>
		/// True solar day duration for this body, always positive. The sign of
		/// the raw solarDayLength only flags retrograde apparent solar motion
		/// (notes/verifiche-api.md §1) — GetLongitude already encodes the
		/// rotation direction on its own, so f(λ) never needs the sign.
		/// </summary>
		public static double SolarDayLengthAbsSeconds(CelestialBody body)
		{
			return Math.Abs(body.solarDayLength);
		}

		/// <summary>Length of one local hour, in real UT seconds.</summary>
		public static double LocalHourSeconds(CelestialBody body) => SolarDayLengthAbsSeconds(body) / LocalHoursPerDay;

		/// <summary>Degenerate solar day (tidal lock on the star, or any other runaway value).</summary>
		public static bool HasDegenerateSolarDay(CelestialBody body)
		{
			return double.IsNaN(body.solarDayLength) || Math.Abs(body.solarDayLength) > StarLockDayLengthThreshold;
		}
	}
}
