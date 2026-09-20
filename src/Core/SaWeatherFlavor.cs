using System.Collections.Generic;
using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Per-body naming and iconography for weather states: "RAIN" is a poor
	/// description of what falls out of Eve's sky.
	///
	/// **Presentation only.** An entry renames and re-illustrates a state the
	/// classifier has already decided, and can never change WHICH state that is:
	/// a config able to reclassify would make the label a lie the day the
	/// classifier is retuned.
	///
	/// Config-driven and ModuleManager-patchable, so a planet pack can describe
	/// its own bodies without SA knowing anything about them:
	///
	/// <code>
	/// SA_WEATHER_FLAVOR
	/// {
	///     body = Eve                 // CelestialBody.bodyName (internal id)
	///     condition = Rain           // SaWeatherState value
	///     name = #LOC_..._or_literal // shown instead of the generic name
	///     icon = Some/Path/texture   // optional, replaces the state icon
	///     iconNight = Some/Path/tex  // optional, replaces it only at night;
	///                                // "icon" (or the generic one) still
	///                                // covers daytime when this is the only
	///                                // field set
	/// }
	/// </code>
	/// </summary>
	internal static class SaWeatherFlavor
	{
		internal const string NodeName = "SA_WEATHER_FLAVOR";

		private struct Entry
		{
			public string Name;
			public string IconPath;
			public string IconNightPath;
		}

		// Keyed by "bodyName|State", read once per classification change.
		private static Dictionary<string, Entry> entries;

		/// <summary>
		/// Name and icon for this body/state pair, falling back to the generic ones
		/// the caller passes in when no entry applies. <paramref name="isNight"/>
		/// only picks between an entry's own <c>icon</c>/<c>iconNight</c>: the
		/// generic day/night switch already happened in the fallback.
		/// </summary>
		internal static void Resolve(string bodyName, SaWeatherState state, bool isNight,
			string fallbackName, string fallbackIconPath, out string name, out string iconPath)
		{
			name = fallbackName;
			iconPath = fallbackIconPath;
			if (entries == null) Load();
			if (entries.Count == 0 || string.IsNullOrEmpty(bodyName)) return;

			if (!entries.TryGetValue(Key(bodyName, state), out Entry entry)) return;
			if (!string.IsNullOrEmpty(entry.Name)) name = entry.Name;
			if (isNight && !string.IsNullOrEmpty(entry.IconNightPath)) iconPath = entry.IconNightPath;
			else if (!string.IsNullOrEmpty(entry.IconPath)) iconPath = entry.IconPath;
		}

		private static string Key(string bodyName, SaWeatherState state) => bodyName + "|" + state;

		private static void Load()
		{
			entries = new Dictionary<string, Entry>();
			if (GameDatabase.Instance == null) return;

			foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(NodeName))
			{
				string body = node.GetValue("body");
				string condition = node.GetValue("condition");
				if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(condition)) continue;

				// Logged rather than silently skipped: a patch that never takes
				// effect is far harder to debug than one that says why.
				if (!TryParseState(condition, out SaWeatherState state))
				{
					Debug.LogWarning("[SA] " + NodeName + " for " + body
						+ " has an unrecognised condition '" + condition + "' — entry skipped.");
					continue;
				}

				entries[Key(body, state)] = new Entry
				{
					Name = node.GetValue("name"),
					IconPath = node.GetValue("icon"),
					IconNightPath = node.GetValue("iconNight")
				};
			}
		}

		private static bool TryParseState(string raw, out SaWeatherState state)
		{
			foreach (SaWeatherState candidate in System.Enum.GetValues(typeof(SaWeatherState)))
			{
				if (string.Equals(candidate.ToString(), raw, System.StringComparison.OrdinalIgnoreCase))
				{
					state = candidate;
					return true;
				}
			}
			state = SaWeatherState.Unknown;
			return false;
		}

		/// <summary>Dropped on scene changes, so a mid-session ModuleManager reload
		/// is picked up.</summary>
		internal static void Reset() => entries = null;
	}
}
