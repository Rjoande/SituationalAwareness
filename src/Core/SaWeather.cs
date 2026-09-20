namespace SituationalAwareness.Core
{
	/// <summary>
	/// Weather states SA can report. Deliberately a short list, with no "overcast"
	/// tier: the clear/cloudy boundary is already the weakest one, and a third
	/// level would be invented rather than measured.
	/// </summary>
	internal enum SaWeatherState
	{
		/// <summary>No EVE volumetric clouds installed, or nothing to report
		/// (vacuum, wrong scene). The section hides rather than lying.</summary>
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
	/// How much the current weather should worry a pilot. Drives the small alert
	/// badge on the weather icon: the icons stay a single neutral colour, so
	/// severity is carried by one dedicated mark, not by recolouring the glyph.
	/// </summary>
	internal enum SaWeatherSeverity
	{
		None,
		/// <summary>Reduced visibility, or something falling.</summary>
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
		/// <summary>Which windowed layers count as "the same weather" when
		/// forecasting.</summary>
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

		/// <summary>The plain state a class names when nothing more specific is
		/// known: a forecast, not an observation.</summary>
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

		/// <summary>Clear and cloudy are deliberately unmarked: a badge that shows
		/// up in ordinary weather stops meaning anything.</summary>
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
		/// <summary>The state SA shows, after WeatherClassifier's hysteresis.</summary>
		public SaWeatherState State;
		/// <summary>The call on this very sample, before any hysteresis. What a
		/// companion should record: a filter tuned for a calm panel is not what a
		/// threshold study wants.</summary>
		public SaWeatherState RawState;
		public SaWeatherForecast Forecast;
		/// <summary>Optical depth of everything overhead: coverage x cloud-type
		/// density x layer thickness, summed over the active non-FX layers.
		/// Drives the clear/cloudy call.</summary>
		public double SkyOpticalDepth;
		/// <summary>EVE's own particle-field render gate at the vessel, 0..1:
		/// whether EVE is drawing precipitation particles here, and how densely.
		/// 0 when nothing is falling.</summary>
		public double PrecipIntensity01;
		/// <summary>Name of the layer that decided the state, for diagnostics only:
		/// a layer name is never a classification criterion.</summary>
		public string DominantLayer;
	}

	/// <summary>Which forecast sentence applies, in the order WeatherForecaster
	/// tests them.</summary>
	internal enum SaForecastKind
	{
		/// <summary>No time-windowed weather on this body, or none shown.</summary>
		None,
		/// <summary>What is falling now ends no later than Seconds from now:
		/// its layers all close, with nothing of the same class opening first.</summary>
		Ends,
		/// <summary>A dormant layer able to produce State opens in Seconds, and
		/// nothing open now can. "Risk", because whether its coverage lands on the
		/// vessel is not knowable from the clock.</summary>
		Risk,
		/// <summary>No precipitation window is open and none opens inside the
		/// horizon: nothing can fall for that long.</summary>
		Stable,
		/// <summary>One kind of weather is open and closes in Seconds with no
		/// reopening before: exposure bounded, outcome not.</summary>
		Possible,
		/// <summary>A window is open with no end the clock can name, layers of the
		/// same class alternating without a gap. Seconds = next transition.</summary>
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
