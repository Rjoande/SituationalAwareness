using System;
using System.IO;
using UnityEngine;

namespace SituationalAwareness.WeatherReport
{
	/// <summary>
	/// The player's consent to the weather report (notes/indagine-meteo.md
	/// §8.3, trigger reworked 2026-09-10): the disclaimer below is shown the
	/// first time the SA setting is switched on, and until "Accept" has been
	/// pressed nothing is read or written by this companion.
	///
	/// Consent is GLOBAL — one file in PluginData, not a per-save flag — because
	/// it belongs to the person, not to the career: accepted once, another save
	/// only needs the setting switched on. It is tied to the disclaimer's
	/// version, so a changed text asks again. Declining stores nothing, which
	/// is exactly why switching the setting on again shows the dialog again.
	///
	/// English only, not localized (decision §8.3, confirmed 2026-09-10): one
	/// authoritative wording of what the player is agreeing to.
	/// </summary>
	internal static class ReportConsent
	{
		internal const int DisclaimerVersion = 1;

		internal const string DisclaimerTitle = "Situational Awareness — Weather Report";

		internal const string DisclaimerText =
			"Weather Report is an optional diagnostic tool. When the weather shown by Situational Awareness " +
			"does not match what you see, you press the matching label and the plugin writes one sample to a " +
			"local file: the raw values it read from EVE's cloud layers (coverage, density, particle and lightning " +
			"settings), SA's own classification, your vessel and camera position, altitude, universal time, body, " +
			"biome, sun elevation and solar flux, the transmittance reported by WeatherDrivenSolarPanel if installed, " +
			"plus the label you chose and any note you type.\n\n" +
			"Once per session it also writes the list of plugins loaded in your game, names and versions only, " +
			"because the same sample means different things with different visual packs installed.\n\n" +
			"Nothing is read or written until you accept, and nothing is ever sent anywhere by this plugin. The " +
			"files stay in GameData/SituationalAwareness/PluginData/WeatherReport/ until you decide to share them " +
			"yourself, as explained in the Weather Report section of the README. No personal information, account " +
			"name, file path or system detail is collected.\n\n" +
			"You can turn this off at any time in Difficulty Settings > Situational Awareness. Declining leaves " +
			"Situational Awareness fully working; only the report button stays hidden.";

		internal const string AcceptLabel = "Accept";
		internal const string DeclineLabel = "Decline";

		private const string NodeName = "SA_WEATHER_REPORT_CONSENT";
		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/SituationalAwareness/PluginData/WeatherReport/consent.cfg";

		private static bool? granted;

		/// <summary>True once "Accept" has been recorded for the current
		/// disclaimer version.</summary>
		internal static bool IsGranted
		{
			get
			{
				if (granted == null) granted = Load();
				return granted.Value;
			}
		}

		internal static void Grant()
		{
			granted = true;
			try
			{
				ConfigNode root = new ConfigNode();
				ConfigNode node = root.AddNode(NodeName);
				node.AddValue("accepted", true);
				node.AddValue("disclaimerVersion", DisclaimerVersion);
				node.AddValue("date", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				root.Save(FilePath);
			}
			catch (Exception e)
			{
				// Consent still holds for this session; it will simply be
				// asked again next time. Better than a report that cannot start.
				Debug.LogWarning("[SA_WeatherReport] could not save consent: " + e.Message);
			}
		}

		private static bool Load()
		{
			try
			{
				if (!File.Exists(FilePath)) return false;
				ConfigNode root = ConfigNode.Load(FilePath);
				ConfigNode node = root?.GetNode(NodeName);
				if (node == null) return false;
				bool accepted = false;
				int version = 0;
				node.TryGetValue("accepted", ref accepted);
				node.TryGetValue("disclaimerVersion", ref version);
				return accepted && version == DisclaimerVersion;
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] could not read consent: " + e.Message);
				return false;
			}
		}
	}
}
