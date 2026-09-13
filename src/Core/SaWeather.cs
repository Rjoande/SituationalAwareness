namespace SituationalAwareness.Core
{
	/// <summary>
	/// Weather states SA can report, as proposed in notes/indagine-meteo.md §3
	/// and tuned against the author's survey (notes/survey-analisi.md).
	/// Deliberately the same short list as §3 — no "overcast" tier, because the
	/// survey showed the clear/cloudy boundary is already the weakest one and a
	/// third level would be invented, not measured.
	/// </summary>
	internal enum SaWeatherState
	{
		/// <summary>No EVE volumetric clouds installed, or nothing to report
		/// (vacuum, wrong scene). The row hides itself rather than lying.</summary>
		Unknown,
		Clear,
		Cloudy,
		Fog,
		Rain,
		Snow,
		Thunderstorm,
		DustStorm
	}

	/// <summary>
	/// How much the current weather should worry a pilot. Drives the small
	/// alert badge on the weather icon — the icons themselves stay a single
	/// neutral colour (user request 2026-09-09), so severity is carried by one
	/// dedicated mark instead of by recolouring the whole glyph.
	/// </summary>
	internal enum SaWeatherSeverity
	{
		/// <summary>Nothing to flag: no badge drawn at all.</summary>
		None,
		/// <summary>Worth noticing — reduced visibility or something falling.</summary>
		Caution,
		/// <summary>Actively hostile: lightning, or dust thick enough to matter.</summary>
		Warning
	}

	/// <summary>
	/// What a layer can drop on you, independent of whether it is doing so
	/// right now. Coarser than SaWeatherState on purpose: a thunderstorm is
	/// rain with lightning, and for "will it rain later" the lightning is
	/// beside the point.
	/// </summary>
	internal enum SaPrecipClass
	{
		None,
		Liquid,
		Snow,
		Dust
	}

	internal static class SaWeatherStates
	{
		/// <summary>Precipitation class of a state: which windowed layers
		/// count as "the same weather" for forecasting purposes.</summary>
		internal static SaPrecipClass PrecipClassOf(SaWeatherState state)
		{
			switch (state)
			{
				case SaWeatherState.Rain:
				case SaWeatherState.Thunderstorm:
					return SaPrecipClass.Liquid;
				case SaWeatherState.Snow:
					return SaPrecipClass.Snow;
				case SaWeatherState.DustStorm:
					return SaPrecipClass.Dust;
				default:
					return SaPrecipClass.None;
			}
		}

		/// <summary>The plain state a precipitation class names when nothing
		/// more specific is known (a forecast, not an observation).</summary>
		internal static SaWeatherState StateOf(SaPrecipClass cls)
		{
			switch (cls)
			{
				case SaPrecipClass.Liquid: return SaWeatherState.Rain;
				case SaPrecipClass.Snow: return SaWeatherState.Snow;
				case SaPrecipClass.Dust: return SaWeatherState.DustStorm;
				default: return SaWeatherState.Unknown;
			}
		}

		/// <summary>
		/// Severity of a state. Clear and cloudy are deliberately unmarked: a
		/// badge that shows up in ordinary weather stops meaning anything.
		/// </summary>
		internal static SaWeatherSeverity SeverityOf(SaWeatherState state)
		{
			switch (state)
			{
				case SaWeatherState.Fog:
				case SaWeatherState.Rain:
				case SaWeatherState.Snow:
					return SaWeatherSeverity.Caution;
				case SaWeatherState.Thunderstorm:
				case SaWeatherState.DustStorm:
					return SaWeatherSeverity.Warning;
				default:
					return SaWeatherSeverity.None;
			}
		}
	}

	/// <summary>
	/// One weather classification, plus the raw quantities behind it so the UI
	/// (and the survey companion, through SaExtensionPoint) can show or record
	/// WHY the state came out the way it did.
	/// </summary>
	internal struct SaWeatherReadout
	{
		/// <summary>The state SA shows: the classifier's call after the
		/// display hysteresis in WeatherClassifier has had its say.</summary>
		public SaWeatherState State;
		/// <summary>The classifier's call on this very sample, before any
		/// hysteresis. What the survey companion should record — a filter
		/// tuned for a calm panel is not what a threshold study wants.</summary>
		public SaWeatherState RawState;
		/// <summary>What comes next, from the layers' own time windows
		/// (WeatherForecaster). Kind None when there is nothing to say.</summary>
		public SaWeatherForecast Forecast;
		/// <summary>Optical depth of everything overhead: coverage x cloud-type
		/// density x layer thickness, summed over the active non-FX layers.
		/// Drives the clear/cloudy call.</summary>
		public double SkyOpticalDepth;
		/// <summary>EVE's own particle-field render gate at the vessel, 0..1 —
		/// literally "is EVE drawing precipitation particles here, and how
		/// densely". 0 when nothing is falling.</summary>
		public double PrecipIntensity01;
		/// <summary>Name of the layer that decided the state, for diagnostics.
		/// Never used to classify — see §3 on why naming is not a criterion.</summary>
		public string DominantLayer;
	}

	/// <summary>Which of the four forecast sentences applies.</summary>
	internal enum SaForecastKind
	{
		/// <summary>Nothing worth a line: no time-windowed weather on this
		/// body, or the weather is not shown at all.</summary>
		None,
		/// <summary>The precipitation falling now comes from layers that all
		/// close within the horizon, with nothing of the same class opening
		/// before then — it ends no later than Seconds from now.</summary>
		Ends,
		/// <summary>A dormant layer able to produce State opens in Seconds,
		/// and nothing open right now can produce it. "Risk", because whether
		/// its coverage lands on the vessel is not knowable from the clock.</summary>
		Risk,
		/// <summary>No precipitation window is open and none opens inside the
		/// horizon: nothing can fall for that long (Duna between storms).</summary>
		Stable,
		/// <summary>Exactly one kind of weather has a window open, closing in
		/// Seconds with no reopening before: exposure bounded, outcome not.</summary>
		Possible,
		/// <summary>A window is open with no end the clock can name (Kerbin's
		/// alternating rain systems): anything may happen, nothing can be
		/// timed. Seconds = next transition of any open weather layer, so the
		/// UI can vary its wording per window rather than per tick.</summary>
		Changeable
	}

	internal struct SaWeatherForecast
	{
		public SaForecastKind Kind;
		/// <summary>Ends: the state ending (the shown one). Risk/Possible: the
		/// plain state the window can produce. Unused otherwise.</summary>
		public SaWeatherState State;
		/// <summary>Time to the event, in UT seconds. For Stable, the horizon;
		/// for Changeable, the next transition (wording seed only).</summary>
		public double Seconds;
	}
}
