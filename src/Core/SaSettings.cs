using System.Collections;
using System.IO;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

namespace SituationalAwareness.Core
{
	public enum SaTempUnit { Celsius, Kelvin }
	public enum SaPressureUnit { Kpa, Atm }
	public enum SaTerminatorUnit { Km, Deg }
	public enum SaGravityUnit { G, Mps2 }
	public enum SaCoordUnit { Decimal, Dms }
	// M3 point 6 (go 2026-07-28): SOLAR TIME dial line, click-cycled between
	// the clock (HH:MM:SS) and the raw equation-of-time gap (±mm:ss).
	public enum SaSolarTimeFormat { Clock, EquationOfTime }

	/// <summary>
	/// SA section in the stock settings page (Difficulty -> SA), pattern
	/// KRILL/KrillParams — a stock-looking home for the one player-global
	/// toggle SA needs (design doc §6.6): showing MET is redundant with the
	/// stock UI most of the time, default OFF.
	/// </summary>
	public class SaParams : GameParameters.CustomParameterNode
	{
		public override string Title => Localizer.Format("#LOC_SA_settings_title");
		public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
		public override string Section => "SituationalAwareness";
		public override string DisplaySection => "Situational Awareness";
		public override int SectionOrder => 1;
		public override bool HasPresets => false;

		[GameParameters.CustomParameterUI("#LOC_SA_settings_showMet",
			toolTip = "#LOC_SA_settings_showMet_tip")]
		public bool showMissionTime;

		/// <summary>Retest 2026-07-30, user request: alternate MET format, "T+1y 23d 03:14:09" (letters for y/d, colon HH:MM:SS below that — mimics stock's own style) instead of the default all-letters timer. Only meaningful when showMissionTime is on (see Enabled below).</summary>
		[GameParameters.CustomParameterUI("#LOC_SA_settings_metStockalike",
			toolTip = "#LOC_SA_settings_metStockalike_tip")]
		public bool metStockalikeFormat;

		/// <summary>M3 point 6 (go 2026-07-28): adds a SOLAR TIME line to the Surface dial (design doc §9 point 6) — off by default, same "opt in to an extra line" pattern as MET.</summary>
		[GameParameters.CustomParameterUI("#LOC_SA_settings_showSolarTime",
			toolTip = "#LOC_SA_settings_showSolarTime_tip")]
		public bool showSolarTime;

		[GameParameters.CustomParameterUI("#LOC_SA_settings_fixedGravity",
			toolTip = "#LOC_SA_settings_fixedGravity_tip")]
		public bool useFixedSurfaceGravity;

		[GameParameters.CustomParameterUI("#LOC_SA_settings_bodyMapColor",
			toolTip = "#LOC_SA_settings_bodyMapColor_tip")]
		public bool useBodyMapColorForDial;

		/// <summary>
		/// Window/font scale, on top of the stock UI Scale (retest
		/// 2026-07-27: general readability complaint, "+50%, regolabile?").
		/// Default reverted to 1.0 (further refinement, same day) — the
		/// user's first-pass default of 1.5 turned out too aggressive once
		/// seen in game; range extended below 1.0 too, for players who want
		/// it smaller. Range 0.5-2.0 in 0.05 steps (matches
		/// CustomFloatParameterUI usage elsewhere in stock GameParameters.cs,
		/// e.g. the physics-range sliders: stepCount + minValue/maxValue +
		/// displayFormat).
		/// </summary>
		[GameParameters.CustomFloatParameterUI("#LOC_SA_settings_uiScale",
			toolTip = "#LOC_SA_settings_uiScale_tip",
			minValue = 0.5f, maxValue = 2.0f, stepCount = 31, displayFormat = "F2")]
		public float uiScale = 1.0f;

		/// <summary>
		/// M3 point 4 (design doc §9, decided 2026-07-25, go 2026-07-28):
		/// how much of the body's radius a SUB_ORBITAL vessel can be under
		/// and still read as Surface mode (a short hop, not a real orbital
		/// insertion) — see SaModeSelector.IsLowSubOrbital. Default 0.25
		/// (the value decided at design time); range 0.15-1.5 covers a
		/// strict "barely left the ground" reading up to "basically any
		/// suborbital arc still counts as surface" on a small/low-gravity
		/// body.
		/// </summary>
		[GameParameters.CustomFloatParameterUI("#LOC_SA_settings_surfaceAltThreshold",
			toolTip = "#LOC_SA_settings_surfaceAltThreshold_tip",
			minValue = 0.15f, maxValue = 1.5f, stepCount = 28, displayFormat = "F2")]
		public float surfaceAltitudeThresholdMultiplier = 0.25f;

		public override void SetDifficultyPreset(GameParameters.Preset preset)
		{
		}

		public override bool Enabled(MemberInfo member, GameParameters parameters)
		{
			return true;
		}

		public override bool Interactible(MemberInfo member, GameParameters parameters)
		{
			// metStockalikeFormat only means anything when MET itself is
			// shown (retest 2026-07-30, user request: "dipendente da show
			// MET") — greyed out rather than hidden, so the dependency is
			// visible rather than the option silently disappearing.
			if (member.Name == nameof(metStockalikeFormat))
			{
				return parameters.CustomParams<SaParams>().showMissionTime;
			}
			return true;
		}

		public override IList ValidValues(MemberInfo member)
		{
			return null;
		}

		public static bool ShowMissionTime
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return false;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().showMissionTime;
			}
		}

		/// <summary>Off by default: "T+1y 23d 03:14:09" (letters for y/d, colon HH:MM:SS below) instead of the default all-letters MET timer.</summary>
		public static bool MetStockalikeFormat
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return false;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().metStockalikeFormat;
			}
		}

		/// <summary>Off by default: adds the SOLAR TIME line to the Surface dial.</summary>
		public static bool ShowSolarTime
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return false;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().showSolarTime;
			}
		}

		/// <summary>Default (false) = live sensed gravity (sensorGravimeter-style); true = fixed body ASL value.</summary>
		public static bool UseFixedSurfaceGravity
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return false;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().useFixedSurfaceGravity;
			}
		}

		/// <summary>Off by default: orbit dial's planet disc uses the body's real map/orbit-line color (attenuated) instead of a neutral default.</summary>
		public static bool UseBodyMapColorForDial
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return false;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().useBodyMapColorForDial;
			}
		}

		/// <summary>SA's own window/font scale multiplier, stacked on top of GameSettings.UI_SCALE (retest 2026-07-27). Defaults to 1.0 even before a game is loaded, matching the field default.</summary>
		public static float UiScale
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 1.0f;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().uiScale;
			}
		}

		/// <summary>Suborbital-altitude threshold multiplier for SaModeSelector (M3 point 4). Defaults to 0.25 even before a game is loaded, matching the field default.</summary>
		public static float SurfaceAltitudeThresholdMultiplier
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 0.25f;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().surfaceAltitudeThresholdMultiplier;
			}
		}
	}

	/// <summary>
	/// Player-global, per-save-independent state: unit choices, window
	/// position, collapsed strip state (design doc §6.4/§6.7). PluginData/,
	/// not GameData/ (ModuleManager never scans it — same convention as
	/// KRILL's keymap, no MM cache rebuild on save).
	/// </summary>
	internal static class SaPersist
	{
		private const string RootNodeName = "SA_SETTINGS";

		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/SituationalAwareness/PluginData/settings.cfg";

		private static bool loaded;

		public static SaTempUnit TempUnit = SaTempUnit.Celsius;
		public static SaPressureUnit PressureUnit = SaPressureUnit.Kpa;
		public static SaTerminatorUnit TerminatorUnit = SaTerminatorUnit.Km;
		public static SaGravityUnit GravityUnit = SaGravityUnit.G;
		public static SaCoordUnit CoordUnit = SaCoordUnit.Decimal;
		public static SaSolarTimeFormat SolarTimeFormat = SaSolarTimeFormat.Clock;
		public static bool Collapsed;
		public static bool HasWindowPosition;
		public static Vector2 WindowPosition;

		public static void EnsureLoaded()
		{
			if (loaded) return;
			loaded = true;

			if (!File.Exists(FilePath)) return;
			ConfigNode root = ConfigNode.Load(FilePath);
			ConfigNode node = root?.GetNode(RootNodeName);
			if (node == null) return;

			string temp = null;
			if (node.TryGetValue("tempUnit", ref temp) && System.Enum.TryParse(temp, out SaTempUnit tu))
				TempUnit = tu;
			string pres = null;
			if (node.TryGetValue("pressureUnit", ref pres) && System.Enum.TryParse(pres, out SaPressureUnit pu))
				PressureUnit = pu;
			string term = null;
			if (node.TryGetValue("terminatorUnit", ref term) && System.Enum.TryParse(term, out SaTerminatorUnit teu))
				TerminatorUnit = teu;
			string grav = null;
			if (node.TryGetValue("gravityUnit", ref grav) && System.Enum.TryParse(grav, out SaGravityUnit gu))
				GravityUnit = gu;
			string coord = null;
			if (node.TryGetValue("coordUnit", ref coord) && System.Enum.TryParse(coord, out SaCoordUnit cu))
				CoordUnit = cu;
			string solarFmt = null;
			if (node.TryGetValue("solarTimeFormat", ref solarFmt) && System.Enum.TryParse(solarFmt, out SaSolarTimeFormat sf))
				SolarTimeFormat = sf;

			bool collapsed = false;
			if (node.TryGetValue("collapsed", ref collapsed)) Collapsed = collapsed;

			float px = 0f, py = 0f;
			bool hasX = node.TryGetValue("windowX", ref px);
			bool hasY = node.TryGetValue("windowY", ref py);
			if (hasX && hasY)
			{
				HasWindowPosition = true;
				WindowPosition = new Vector2(px, py);
			}
		}

		public static void Save()
		{
			ConfigNode root = new ConfigNode();
			ConfigNode node = root.AddNode(RootNodeName);
			node.AddValue("tempUnit", TempUnit.ToString());
			node.AddValue("pressureUnit", PressureUnit.ToString());
			node.AddValue("terminatorUnit", TerminatorUnit.ToString());
			node.AddValue("gravityUnit", GravityUnit.ToString());
			node.AddValue("coordUnit", CoordUnit.ToString());
			node.AddValue("solarTimeFormat", SolarTimeFormat.ToString());
			node.AddValue("collapsed", Collapsed);
			if (HasWindowPosition)
			{
				node.AddValue("windowX", WindowPosition.x);
				node.AddValue("windowY", WindowPosition.y);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			root.Save(FilePath);
		}
	}
}
