using System.Collections.Generic;
using SituationalAwareness.Core;
using UnityEngine;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// Texture lookup for the weather section's icon. Icons are authored WHITE
	/// on transparent and tinted at runtime, so one texture serves every colour
	/// a state can be shown in.
	///
	/// Every lookup degrades quietly: a missing texture leaves the section with
	/// its text label alone, so art may arrive after the code.
	/// </summary>
	internal static class SaWeatherIcons
	{
		private const string Folder = "SituationalAwareness/Textures/";

		// Optional night variants: SA knows the sun's elevation, so a clear night
		// can show stars instead of a sun. Absent files fall back to the day icon.
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
		/// Optional severity mark for the icon's corner, overriding the bold "!"
		/// SA otherwise draws from its own font. White where it should take the
		/// severity colour, dark where it should stay dark: the tint multiplies.
		/// </summary>
		internal const string AlertPath = Folder + "SA_weather_alert";

		/// <summary>
		/// Icon for a weather section the science gate keeps closed: a padlock,
		/// shown dimmed under the one weather label that is uppercase.
		/// </summary>
		internal const string LockedPath = Folder + "SA_weather_locked";

		private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

		/// <summary>
		/// GameDatabase path for a state's icon, before any flavor override. Night
		/// variants exist only for the two states where the sky itself is the
		/// subject; there is no night version of rain worth drawing.
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
