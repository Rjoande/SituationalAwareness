using System.Collections.Generic;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Local seconds-of-day at longitude 0, as a pure function of UT: LOCAL TIME
	/// is MEAN time, the civil clock linear in UT, not apparent solar time
	/// (design doc §3.6).
	///
	/// Calibrated once per body, with solarDayLength frozen at that moment
	/// alongside the offset, so the linear formula below is driftless by
	/// construction. The calibration is corrected for the equation of time, which
	/// makes the offset the same constant whenever it happens to be computed.
	/// </summary>
	internal static class MeanTimeCalibration
	{
		private struct CalibState
		{
			public double offsetSeconds;
			public double frozenDayLength;
		}

		private static readonly Dictionary<CelestialBody, CalibState> cache = new Dictionary<CelestialBody, CalibState>();

		public static double Zone0Seconds(CelestialBody body, CelestialBody star, double ut, double solarDayLength)
		{
			if (!cache.TryGetValue(body, out CalibState state))
			{
				double subsolarLon = SolarMath.SubsolarLongitude(body, star);
				double f0True = SolarMath.DayFraction(0.0, subsolarLon);
				double utMod = ut % solarDayLength;
				if (utMod < 0) utMod += solarDayLength;

				// MEAN minus TRUE in KSP's rotation convention: positive just
				// after perihelion, when the sundial lags the mean clock.
				double eqTimeSeconds = SolarMath.EquationOfTimeSeconds(body, star, ut, solarDayLength);
				state.offsetSeconds = f0True * solarDayLength - utMod + eqTimeSeconds;
				state.frozenDayLength = solarDayLength;
				cache[body] = state;
			}

			double s = (ut % state.frozenDayLength + state.offsetSeconds) % state.frozenDayLength;
			if (s < 0) s += state.frozenDayLength;
			return s;
		}

		public static void Reset()
		{
			cache.Clear();
		}
	}
}
