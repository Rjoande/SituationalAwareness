using System.Collections.Generic;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// "Phase A" forecast (design 2026-09-09/10, sentences reworked with the
	/// user 2026-09-11): what the layers' own clocks say about the coming
	/// hours, and nothing more.
	///
	/// EVE's TimeSettings is a deterministic on/off cycle anchored to UT 0 —
	/// so WHEN a weather system opens or closes is exact, to the second, for
	/// any future time. What this deliberately does not know is WHERE that
	/// system's coverage will be relative to the vessel: inside an open window
	/// the cloud map still drifts over you (Kerbin's at ~100 km/h), so a rain
	/// cell can arrive with no window changing at all. That needs the coverage
	/// map and the layer's drift (a "phase B" through private EVE state), and
	/// until then every sentence here is worded to promise only what the clock
	/// can back, in this order of priority:
	///
	///   Ends       — the precipitation falling now ends NO LATER than t (its
	///                layers close, nothing of the same kind opens before then).
	///   Risk       — a kind of weather nothing currently open can produce
	///                becomes possible at t. Possible, not certain.
	///   Stable     — no precipitation window is open and none opens inside
	///                the horizon: nothing can fall for that long.
	///   Possible   — exactly one kind of weather has a window open, and that
	///                window closes at t with no reopening before: exposure
	///                bounded, rain not promised either way.
	///   Changeable — a window is open with no end in sight (Kerbin's two rain
	///                systems alternate without a gap): anything can happen,
	///                nothing can be timed. The honest phase-A answer there.
	///
	/// A "new front in t" sentence used to sit where Possible/Changeable are
	/// now. It was true and useless: on Kerbin every front is "cloudy, then
	/// still cloudy", and the player cannot plan anything on it.
	///
	/// Holds no EVE types: the windows arrive already reduced to numbers, so
	/// this stays testable and safe to JIT on an install without EVE.
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

			// Only layers that can actually drop something drive the sentences:
			// a cirrus veil that comes and goes is scenery, not a forecast. A
			// body with none of them (Jool's permanent decks) gets no line.
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
					// No horizon on this one: "the storm you are standing in
					// ends within 1d 4h" is worth a line however far off it is
					// (a Duna dust storm lasts longer than the 3-day horizon).
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

			// --- 3. Stable: nothing open, nothing opening inside the horizon
			// (Risk above already caught anything closer). Duna between storms.
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

			// --- 5. Changeable: a window is open with no end the clock can
			// name. Seconds carries the next transition of any open weather
			// layer, so the UI can vary its wording per window rather than
			// per tick.
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
		/// When the open windows of <paramref name="cls"/> all close, provided
		/// no closed window of the same class reopens at or before that moment
		/// — otherwise the exposure simply continues and there is no bound to
		/// report. False when nothing of that class is open at all.
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
