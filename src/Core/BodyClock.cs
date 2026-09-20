using System;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Global local-hour system derived from the game's own clock (design doc
	/// §3.1), never hardcoded: stock 6h, JNSQ+Kronometer 12h, RSS 24h. N is
	/// global rather than per-body because dateTimeFormatter is a single static
	/// instance for the whole game.
	/// </summary>
	internal static class BodyClock
	{
		/// <summary>
		/// solarDayLength beyond this (seconds) is the star-lock sentinel. A body
		/// tidally locked on its star gets exactly double.MaxValue, which is
		/// FINITE — so the guard must be a threshold, never IsInfinity alone.
		/// </summary>
		public const double StarLockDayLengthThreshold = 1e17;

		/// <summary>
		/// Local hours per solar day, rounded from the active dateTimeFormatter.
		/// Without Kronometer this is disconnected from the home body's real
		/// physics (design doc §3.6): SA follows the game's clock, not a
		/// recomputed truth.
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

		public static double ZoneWidthDeg => 360.0 / LocalHoursPerDay;

		/// <summary>
		/// True solar day duration for this body, always positive. The sign of the
		/// raw solarDayLength only flags retrograde apparent solar motion, and
		/// GetLongitude already encodes rotation direction, so f(λ) never needs it.
		/// </summary>
		public static double SolarDayLengthAbsSeconds(CelestialBody body)
		{
			return Math.Abs(body.solarDayLength);
		}

		public static double LocalHourSeconds(CelestialBody body) => SolarDayLengthAbsSeconds(body) / LocalHoursPerDay;

		/// <summary>Degenerate solar day (tidal lock on the star, or any other runaway value).</summary>
		public static bool HasDegenerateSolarDay(CelestialBody body)
		{
			return double.IsNaN(body.solarDayLength) || Math.Abs(body.solarDayLength) > StarLockDayLengthThreshold;
		}
	}
}
