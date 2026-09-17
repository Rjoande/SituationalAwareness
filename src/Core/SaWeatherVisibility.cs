using SituationalAwareness.Extensibility;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Single source of truth for whether/how the weather block shows,
	/// shared by the extended panel's weather section and the collapsed
	/// strip's icon+temperature (2026-09-15, user request: "verifica tutte
	/// le condizioni di attivazione/esclusione del meteo mostrato, oppure
	/// valuta una fonte unica di verità"). Before this, ApplyExtended alone
	/// knew the three conditions that decide it (mode, Unknown state, the
	/// Weather Report's keep-alive) inline; duplicating them for the strip
	/// would have let the two drift apart on the next tweak to any one of
	/// them.
	/// </summary>
	internal static class SaWeatherVisibility
	{
		internal enum Level
		{
			/// <summary>Not Surface/TidalLock, or genuinely Unknown with nothing keeping the section alive for the Weather Report.</summary>
			Hidden,
			/// <summary>A real reading exists, but the science gate has not credited an atmosphere-analysis experiment on this body yet.</summary>
			LockedGated,
			/// <summary>Nothing to read at all (no EVE, no atmosphere outside a plume, an unsupported layer setup, ...), kept on screen only because the Weather Report is on and its companion is listening.</summary>
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
		/// EXT TEMP / PRESSURE rows, and the strip's temperature figure: need
		/// an atmosphere, unlike the weather icon itself (which can still
		/// show from inside an airless body's plume). Same rule the extended
		/// panel already applied inline (showAtmosphericRows) — named here so
		/// both views read it from the one place.
		/// </summary>
		internal static bool ShowAtmosphericRows(SaReadout r)
		{
			return (r.Mode == SaMode.Surface || r.Mode == SaMode.TidalLock) && r.BodyHasAtmosphere;
		}
	}
}
