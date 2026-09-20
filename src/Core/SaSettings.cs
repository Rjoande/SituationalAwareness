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
	// SOLAR TIME dial line, click-cycled between the clock (HH:MM:SS) and the
	// raw equation-of-time gap (±mm:ss).
	public enum SaSolarTimeFormat { Clock, EquationOfTime }

	/// <summary>
	/// SA's section in the stock settings page (Difficulty -> SA), a stock-looking
	/// home for the handful of options the panel needs (design doc §6.6).
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

		/// <summary>Alternate MET format, "T+1y 23d 03:14:09" in stock's own style,
		/// instead of the default all-letters timer. Only meaningful while
		/// showMissionTime is on (see Enabled below).</summary>
		[GameParameters.CustomParameterUI("#LOC_SA_settings_metStockalike",
			toolTip = "#LOC_SA_settings_metStockalike_tip")]
		public bool metStockalikeFormat;

		/// <summary>Adds a SOLAR TIME line to the Surface dial; off by default,
		/// the same opt-in pattern as MET.</summary>
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
		/// EXT TEMP, PRESSURE, live GRAVITY and WEATHER stay "???" on a body until
		/// the matching experiment has been credited there (SaScienceGate). On by
		/// default, so the panel learns along with the program; no effect in
		/// Sandbox, where there is no R&amp;D to ask.
		/// </summary>
		[GameParameters.CustomParameterUI("#LOC_SA_settings_scienceGate",
			toolTip = "#LOC_SA_settings_scienceGate_tip")]
		public bool gateOnScience = true;

		/// <summary>
		/// Opt-in switch for the diagnostic companion (SaWeatherReport.dll), off
		/// by default. The companion reads this and, the first time it is on with
		/// no consent on record, shows its disclaimer in flight; declining puts it
		/// back to off. SA itself does nothing with it.
		/// </summary>
		[GameParameters.CustomParameterUI("#LOC_SA_settings_weatherReport",
			toolTip = "#LOC_SA_settings_weatherReport_tip")]
		public bool enableWeatherReport;

		/// <summary>
		/// Window/font scale stacked on top of the stock UI Scale, 0.5-2.0 in 0.05
		/// steps.
		///
		/// Global rather than per save: this slider is only the CONTROL, while the
		/// value lives in SaPersist and SaUiScaleSync copies it in and out. A
		/// GameParameters field is still the only way to get a slider into the
		/// stock settings dialog.
		/// </summary>
		[GameParameters.CustomFloatParameterUI("#LOC_SA_settings_uiScale",
			toolTip = "#LOC_SA_settings_uiScale_tip",
			minValue = 0.5f, maxValue = 2.0f, stepCount = 31, displayFormat = "F2")]
		public float uiScale = 1.0f;

		/// <summary>
		/// How much of the body's radius a SUB_ORBITAL vessel can be under and
		/// still read as Surface mode (SaModeSelector.IsLowSubOrbital). The 0.15-1.5
		/// range spans a strict "barely left the ground" reading up to counting any
		/// suborbital arc as surface on a small, low-gravity body.
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
			// metStockalikeFormat only means anything while MET itself is shown.
			// Greyed out rather than hidden, so the dependency stays visible.
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

		/// <summary>False = live sensed gravity, true = the fixed body ASL value.</summary>
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

		public static bool GateOnScience
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return true;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().gateOnScience;
			}
		}

		public static bool EnableWeatherReport
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return false;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().enableWeatherReport;
			}
		}

		/// <summary>Off by default: the orbit dial's planet disc takes the body's
		/// real map colour instead of a neutral default.</summary>
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

		/// <summary>
		/// SA's own window/font scale multiplier, stacked on top of
		/// GameSettings.UI_SCALE. Read from SaPersist, never from the current
		/// game: the per-save slider is only its control (see SaUiScaleSync).
		/// </summary>
		public static float UiScale
		{
			get
			{
				SaPersist.EnsureLoaded();
				return SaPersist.UiScale;
			}
		}

		/// <summary>Falls back to the field default before a game is loaded.</summary>
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
	/// Player-global state, independent of the save: unit choices, window
	/// position, collapsed strip state (design doc §6.4/§6.7) and panel scale.
	/// Lives in PluginData/ rather than GameData/, which ModuleManager never
	/// scans, so saving it triggers no MM cache rebuild.
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
		/// <summary>Panel scale, the value behind the SaParams.uiScale slider. Same range as the slider.</summary>
		public static float UiScale = 1.0f;
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

			float scale = 0f;
			if (node.TryGetValue("uiScale", ref scale) && scale > 0f) UiScale = Mathf.Clamp(scale, 0.5f, 2f);

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
			node.AddValue("uiScale", UiScale);
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
