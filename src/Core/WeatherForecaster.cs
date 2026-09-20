using System.Collections.Generic;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// What the layers' own clocks say about the coming hours, and nothing more.
	/// EVE's time windows are exact to the second for any future time, but say
	/// nothing about WHERE a system's coverage will be: the cloud map drifts over
	/// you at ~100 km/h, so a rain cell can arrive with no window changing. Every
	/// sentence therefore promises only what the clock can back, tested in
	/// priority order (see SaForecastKind): Ends, Risk, Stable, Possible,
	/// Changeable.
	///
	/// Holds no EVE types: the windows arrive already reduced to numbers.
	/// </summary>
	internal static class WeatherForecaster
	{
		/// <summary>Two transitions closer than this are one event: Kerbin's
		/// weather-1 closes at the exact UT weather-2 opens.</summary>
		private const double SameEventToleranceSec = 1.0;

		internal static SaWeatherForecast Forecast(List<EveLayerWindow> windows, SaWeatherState shownState,
			double ut, double horizonSec)
		{
			SaWeatherForecast none = default(SaWeatherForecast);
			if (windows == null || windows.Count == 0) return none;

			// Only layers that can drop something drive the sentences: a cirrus
			// veil that comes and goes is scenery. A body with none gets no line.
			bool anyWeatherLayer = false;
			foreach (EveLayerWindow w in windows)
			{
				if (w.Precip != SaPrecipClass.None) { anyWeatherLayer = true; break; }
			}
			if (!anyWeatherLayer) return none;

			// --- 1. Ends: what is falling now stops when its layers close.
			SaPrecipClass current = SaWeatherStates.PrecipClassOf(shownState);
			if (current != SaPrecipClass.None)
			{
				if (TryBoundedWindow(windows, current, ut, out double lastClose))
				{
					// No horizon here: the end of a storm you are standing in is
					// worth a line however far off it is, and a dust storm can
					// outlast the horizon.
					return new SaWeatherForecast { Kind = SaForecastKind.Ends, State = shownState, Seconds = lastClose };
				}
			}

			// --- Which kinds of weather have a window open right now.
			bool[] openClass = new bool[4];
			int openClassCount = 0;
			SaPrecipClass singleOpen = SaPrecipClass.None;
			foreach (EveLayerWindow w in windows)
			{
				if (w.Precip == SaPrecipClass.None || !w.IsOpenAt(ut) || openClass[(int)w.Precip]) continue;
				openClass[(int)w.Precip] = true;
				openClassCount++;
				singleOpen = w.Precip;
			}

			// --- 2. Risk: a kind of weather nothing open can produce, opening
			// within the horizon. Earliest wins.
			double riskAt = double.PositiveInfinity;
			SaPrecipClass riskClass = SaPrecipClass.None;
			foreach (EveLayerWindow w in windows)
			{
				if (w.Precip == SaPrecipClass.None || openClass[(int)w.Precip]) continue;
				double open = w.SecondsToNextOpen(ut);
				if (open < riskAt) { riskAt = open; riskClass = w.Precip; }
			}
			if (riskClass != SaPrecipClass.None && riskAt <= horizonSec)
			{
				return new SaWeatherForecast
				{
					Kind = SaForecastKind.Risk,
					State = SaWeatherStates.StateOf(riskClass),
					Seconds = riskAt
				};
			}

			// --- 3. Stable: nothing open, and Risk above already caught anything
			// opening inside the horizon.
			if (openClassCount == 0)
			{
				return new SaWeatherForecast { Kind = SaForecastKind.Stable, Seconds = horizonSec };
			}

			// --- 4. Possible: one kind open, and its exposure has a known end.
			if (openClassCount == 1 && TryBoundedWindow(windows, singleOpen, ut, out double closesIn))
			{
				return new SaWeatherForecast
				{
					Kind = SaForecastKind.Possible,
					State = SaWeatherStates.StateOf(singleOpen),
					Seconds = closesIn
				};
			}

			// --- 5. Changeable: a window is open with no end the clock can name.
			// Seconds carries the next transition of any open layer, so the UI can
			// vary its wording per window rather than per tick.
			double nextTransition = double.PositiveInfinity;
			foreach (EveLayerWindow w in windows)
			{
				if (w.Precip == SaPrecipClass.None || !w.IsOpenAt(ut)) continue;
				double t = w.SecondsToNextTransition(ut);
				if (t < nextTransition) nextTransition = t;
			}
			return new SaWeatherForecast { Kind = SaForecastKind.Changeable, Seconds = nextTransition };
		}

		/// <summary>
		/// When the open windows of <paramref name="cls"/> all close, provided no
		/// closed window of the same class reopens at or before that moment:
		/// otherwise the exposure continues and there is no bound to report.
		/// False when nothing of that class is open at all.
		/// </summary>
		private static bool TryBoundedWindow(List<EveLayerWindow> windows, SaPrecipClass cls, double ut, out double lastClose)
		{
			lastClose = -1.0;
			foreach (EveLayerWindow w in windows)
			{
				if (w.Precip != cls || !w.IsOpenAt(ut)) continue;
				double close = w.SecondsToNextTransition(ut);
				if (close > lastClose) lastClose = close;
			}
			if (lastClose < 0.0) return false;

			foreach (EveLayerWindow w in windows)
			{
				if (w.Precip != cls || w.IsOpenAt(ut)) continue;
				if (w.SecondsToNextOpen(ut) <= lastClose + SameEventToleranceSec) return false;
			}
			return true;
		}
	}
}
