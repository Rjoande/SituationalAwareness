using SituationalAwareness.Extensibility;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Single source of truth for whether and how the weather block shows,
	/// shared by the extended panel's section and the collapsed strip so the
	/// two can never drift apart.
	/// </summary>
	internal static class SaWeatherVisibility
	{
		internal enum Level
		{
			/// <summary>Not Surface/TidalLock, or genuinely Unknown with nothing keeping the section alive for the Weather Report.</summary>
			Hidden,
			/// <summary>A real reading exists, but the science gate has not credited an atmosphere-analysis experiment on this body yet.</summary>
			LockedGated,
			/// <summary>Nothing to read at all (no EVE, no atmosphere outside a plume, ...), kept on screen only because the Weather Report is on and its companion is listening.</summary>
			LockedNoReading,
			/// <summary>A real, unlocked reading — the ordinary case.</summary>
			Shown
		}

		internal static Level Compute(SaReadout r)
		{
			bool modeOk = r.Mode == SaMode.Surface || r.Mode == SaMode.TidalLock;
			if (!modeOk) return Level.Hidden;

			bool unknown = r.Weather.State == SaWeatherState.Unknown;
			if (unknown)
			{
				bool keepForReport = SaParams.EnableWeatherReport && SaExtensionPoint.HasCornerSubscriber;
				return keepForReport ? Level.LockedNoReading : Level.Hidden;
			}
			return r.WeatherUnlocked ? Level.Shown : Level.LockedGated;
		}

		/// <summary>
		/// EXT TEMP / PRESSURE rows and the strip's temperature figure need an
		/// atmosphere, unlike the weather icon itself, which can still show
		/// from inside an airless body's plume.
		/// </summary>
		internal static bool ShowAtmosphericRows(SaReadout r)
		{
			return (r.Mode == SaMode.Surface || r.Mode == SaMode.TidalLock) && r.BodyHasAtmosphere;
		}
	}
}
