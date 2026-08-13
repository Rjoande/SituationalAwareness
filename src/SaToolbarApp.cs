using KSP.UI.Screens;
using ToolbarControl_NS;
using UnityEngine;

namespace SituationalAwareness
{
	[KSPAddon(KSPAddon.Startup.MainMenu, true)]
	public class SaToolbarRegistration : MonoBehaviour
	{
		public void Start()
		{
			ToolbarControl.RegisterMod(SaToolbarApp.MODID, SaToolbarApp.MODNAME);
		}
	}

	/// <summary>
	/// Toolbar button opening/closing the single SaWindow. Flight scene only
	/// (design doc §6.9) — SA reads live vessel telemetry, there is nothing
	/// meaningful to show in the editor.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class SaToolbarApp : MonoBehaviour
	{
		internal const string MODID = "SituationalAwareness_NS";
		internal const string MODNAME = "Situational Awareness";

		private ToolbarControl toolbarControl;

		public void Start()
		{
			toolbarControl = gameObject.AddComponent<ToolbarControl>();
			toolbarControl.AddToAllToolbars(
				UI.SaWindow.Open, UI.SaWindow.CloseCurrent,
				ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
				MODID, "SaButton",
				"SituationalAwareness/Textures/SA_38",
				"SituationalAwareness/Textures/SA_24",
				MODNAME);
			UI.SaWindow.OnClosed = () => toolbarControl.SetFalse(false);
		}

		public void OnDestroy()
		{
			UI.SaWindow.OnClosed = null;
			if (toolbarControl != null)
			{
				toolbarControl.OnDestroy();
				Destroy(toolbarControl);
			}
		}
	}
}
