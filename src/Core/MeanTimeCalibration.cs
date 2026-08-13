using System.Collections.Generic;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Local seconds-of-day at longitude 0 (zone 0), as a pure function of UT
	/// — calibrated once per body, then never touches the subsolar-longitude/
	/// rotation-frame path again for that body.
	///
	/// Decision (approved by the user, 2026-07-22): LOCAL TIME is MEAN time
	/// (civil clock, permanently linear in UT — like real-world GMT), not
	/// apparent/true solar time. The dial and dawn/dusk phases share the same
	/// fraction for simplicity (the equation-of-time drift is small enough
	/// not to visibly diverge) — this class ultimately still just feeds the
	/// clock row's own fraction.
	///
	/// Generalized to any body (M3 point 6, go 2026-07-28) — was home-only
	/// (`HomeClockCalibration`) through M3 point 5; renamed to match. Per-body
	/// cache (`Dictionary<CelestialBody, ...>`, same key pattern already used
	/// by `PSystemManager.OrbitRendererDataCache` elsewhere in this codebase
	/// — CelestialBody is a stable, long-lived singleton per body, safe as a
	/// dictionary key).
	///
	/// `solarDayLength` is frozen at calibration time alongside the offset
	/// (never re-read from the live, uncached `body.solarDayLength`
	/// afterward) so the linear formula below is driftless by construction.
	/// `Reset()` (called from `SaWindow.Open()`) re-anchors on every window
	/// (re)open as a cheap safety net.
	///
	/// The true-based calibration is corrected for the equation of time
	/// (`SolarMath.EquationOfTimeSeconds`, equation of center, e^3 order) so
	/// the resulting offset is the SAME mean-time constant regardless of when
	/// calibration happens to occur. Verified end to end against JNSQ Kerbin
	/// (design doc §3.6, notes/verifiche-api.md addendum): sampled across a
	/// full year (T+0/3/6/9/12 months), the calibrated offset landed on
	/// 21600.00s +/- 0.073s at the e^2-order formula (residual = the
	/// truncated e^3 term, confirmed to 4 significant figures against the
	/// logged values); with the e^3 term included here the residual drops to
	/// ~1ms across the whole year.
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

				// eqTimeSeconds is MEAN minus TRUE in KSP's rotation
				// convention (positive just after perihelion, when the
				// sundial lags the mean clock) — sign verified empirically
				// against real JNSQ logs before locking it in.
				double eqTimeSeconds = SolarMath.EquationOfTimeSeconds(body, star, ut, solarDayLength);
				state.offsetSeconds = f0True * solarDayLength - utMod + eqTimeSeconds;
				state.frozenDayLength = solarDayLength;
				cache[body] = state;
			}

			double s = (ut % state.frozenDayLength + state.offsetSeconds) % state.frozenDayLength;
			if (s < 0) s += state.frozenDayLength;
			return s;
		}

		/// <summary>Forces recalibration of every cached body on the next call — safety net invoked when the SA window (re)opens.</summary>
		public static void Reset()
		{
			cache.Clear();
		}
	}
}
