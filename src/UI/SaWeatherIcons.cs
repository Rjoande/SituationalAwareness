using System.Collections.Generic;
using SituationalAwareness.Core;
using UnityEngine;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// Texture lookup for the weather section's icon.
	///
	/// Icons are authored WHITE on transparent and tinted at runtime, so one
	/// texture serves every colour the state can be shown in — the panel keeps
	/// its single-hue avionics look and a new state colour needs no new art.
	///
	/// Everything here degrades quietly: a missing texture means the section
	/// shows its text label alone. That is deliberate — the art is authored
	/// separately from the code, and a build that hard-failed on a missing PNG
	/// would block the mod on an asset that is allowed to arrive later.
	/// </summary>
	internal static class SaWeatherIcons
	{
		private const string Folder = "SituationalAwareness/Textures/";

		// Two optional night variants: SA already knows the sun's elevation, so
		// a "clear night" can show stars instead of a sun for free. Absent
		// files simply fall back to the day icon.
		private const string NightSuffix = "_night";

		private static readonly Dictionary<SaWeatherState, string> BaseNames =
			new Dictionary<SaWeatherState, string>
			{
				{ SaWeatherState.Clear, "SA_weather_clear" },
				{ SaWeatherState.Cloudy, "SA_weather_cloudy" },
				{ SaWeatherState.Fog, "SA_weather_fog" },
				{ SaWeatherState.Rain, "SA_weather_rain" },
				{ SaWeatherState.Snow, "SA_weather_snow" },
				{ SaWeatherState.Thunderstorm, "SA_weather_thunderstorm" },
				{ SaWeatherState.DustStorm, "SA_weather_dust" }
			};

		/// <summary>
		/// Optional severity mark drawn in the icon's bottom-left corner. Absent
		/// by default: SA draws a bold "!" from its own font instead, which was
		/// judged good enough at real size. Dropping this PNG in overrides it
		/// with no code change — white where it should take the severity colour,
		/// dark where it should stay dark, since the tint is multiplicative.
		/// </summary>
		internal const string AlertPath = Folder + "SA_weather_alert";

		/// <summary>
		/// Icon for a weather section the science gate keeps closed (a plain
		/// cloud, drawn by the user 2026-09-09): shown dimmed, with a "?" in
		/// the badge corner, under the one weather label that is uppercase.
		/// Optional like every other texture — absent, the label stands alone.
		/// </summary>
		internal const string LockedPath = Folder + "SA_weather_locked";

		private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

		/// <summary>
		/// GameDatabase path for a state's icon, before any flavor override.
		/// Night variants apply only to the two states where the difference is
		/// visible at all — there is no night version of rain worth drawing.
		/// </summary>
		internal static string PathFor(SaWeatherState state, bool night)
		{
			if (!BaseNames.TryGetValue(state, out string name)) return null;
			bool nightVariantExists = night
				&& (state == SaWeatherState.Clear || state == SaWeatherState.Cloudy)
				&& Load(Folder + name + NightSuffix) != null;
			return Folder + name + (nightVariantExists ? NightSuffix : "");
		}

		/// <summary>Sprite for a GameDatabase texture path, or null if the
		/// texture is not installed. Cached, including the misses.</summary>
		internal static Sprite Load(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (Cache.TryGetValue(path, out Sprite cached)) return cached;

			Sprite sprite = null;
			Texture2D texture = GameDatabase.Instance != null
				? GameDatabase.Instance.GetTexture(path, false)
				: null;
			if (texture != null)
			{
				sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
					new Vector2(0.5f, 0.5f));
			}
			Cache[path] = sprite;
			return sprite;
		}
	}
}
