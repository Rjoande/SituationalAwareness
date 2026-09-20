using System.Collections;
using SituationalAwareness.Core;
using SituationalAwareness.Extensibility;
using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.WeatherReport
{
	/// <summary>
	/// Entry point: puts one small button in the top-right corner of SA's weather
	/// section, opening the report's own window. SA keeps that section on screen
	/// as UNKNOWN wherever the report setting is on, so the button stays
	/// reachable. Before the player switches the report on AND accepts the
	/// disclaimer the companion is inert: no button, no window, no file.
	///
	/// The setting lives in SA because a GameParameters checkbox has no click
	/// callback of its own: this class re-evaluates it when the flight scene
	/// starts and whenever settings are applied, which is where the disclaimer
	/// appears. Declining writes the setting back to off, so the dialog returns
	/// the next time it is switched on.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	internal class WeatherReportApp : MonoBehaviour
	{
		private WeatherReportWindow window;
		private GameObject opener;
		private PopupDialog dialog;

		private void Start()
		{
			// Re-populate on every firing, not once: SaExtensionPoint's contract
			// is that SA rebuilds this Transform on every collapse toggle and
			// window re-open.
			SaExtensionPoint.OnWeatherCornerBuilt += BuildOpenerButton;
			GameEvents.OnGameSettingsApplied.Add(Evaluate);
			// One frame later: the flight UI is not ready for a popup inside the
			// Start() that loads the scene.
			StartCoroutine(EvaluateNextFrame());
		}

		private void OnDestroy()
		{
			// Critical: a static event without this leaks a handler bound to a
			// dead MonoBehaviour across every scene reload.
			SaExtensionPoint.OnWeatherCornerBuilt -= BuildOpenerButton;
			GameEvents.OnGameSettingsApplied.Remove(Evaluate);
			if (dialog != null) dialog.Dismiss();
			if (window != null) Destroy(window.gameObject);
		}

		private IEnumerator EvaluateNextFrame()
		{
			yield return null;
			Evaluate();
		}

		/// <summary>The report may show itself: switched on, and consented.</summary>
		private static bool Active => SaParams.EnableWeatherReport && ReportConsent.IsGranted;

		/// <summary>
		/// The state machine, such as it is: off -> hide everything; on with
		/// consent -> show the opener; on without consent -> ask.
		/// </summary>
		private void Evaluate()
		{
			if (!SaParams.EnableWeatherReport)
			{
				HideAll();
				return;
			}
			if (ReportConsent.IsGranted)
			{
				if (opener != null) opener.SetActive(true);
				return;
			}
			ShowDisclaimer();
		}

		private void HideAll()
		{
			if (opener != null) opener.SetActive(false);
			if (window != null) window.Hide();
		}

		private void ShowDisclaimer()
		{
			if (dialog != null) return;
			dialog = PopupDialog.SpawnPopupDialog(
				new MultiOptionDialog("SaWeatherReportConsent",
					ReportConsent.DisclaimerText, ReportConsent.DisclaimerTitle,
					HighLogic.UISkin, 480f,
					new DialogGUIButton(ReportConsent.AcceptLabel, OnAccept, true),
					new DialogGUIButton(ReportConsent.DeclineLabel, OnDecline, true)),
				false, HighLogic.UISkin);
		}

		private void OnAccept()
		{
			dialog = null;
			ReportConsent.Grant();
			if (opener != null) opener.SetActive(true);
		}

		private void OnDecline()
		{
			dialog = null;
			// Back to off in the save's own settings, so the player sees the
			// checkbox unticked and ticking it again brings the dialog back.
			if (HighLogic.CurrentGame != null)
			{
				HighLogic.CurrentGame.Parameters.CustomParams<SaParams>().enableWeatherReport = false;
			}
			HideAll();
		}

		private void BuildOpenerButton(Transform corner)
		{
			if (corner == null) return;

			GameObject go = ReportUi.Go("ReportOpener", corner);
			RectTransform rect = go.GetComponent<RectTransform>();
			ReportUi.Stretch(rect);

			Image background = go.AddComponent<Image>();
			background.color = ReportUi.ButtonFill;
			Button button = go.AddComponent<Button>();
			ColorBlock colors = button.colors;
			colors.highlightedColor = ReportUi.ButtonHover * 2f;
			colors.pressedColor = ReportUi.ButtonPressed * 2f;
			button.colors = colors;
			button.targetGraphic = background;

			// A pencil-ish glyph rather than an icon file: the companion ships no
			// textures, and one character fits the 14px corner SA reserves.
			Text glyph = ReportUi.Label(go.transform, "✎", 11, ReportUi.Amber, TextAnchor.MiddleCenter);
			ReportUi.Stretch(glyph.rectTransform);

			button.onClick.AddListener(ToggleWindow);

			// The opener IS the "report is active" indicator: it exists only while
			// the setting is on and consent is on record. The slot itself is
			// toggled, and sits outside SA's layout, so this moves nothing.
			opener = corner.gameObject;
			opener.SetActive(Active);
		}

		private void ToggleWindow()
		{
			if (!Active) return;
			if (window == null) window = WeatherReportWindow.Create();
			window.Toggle();
		}
	}
}
