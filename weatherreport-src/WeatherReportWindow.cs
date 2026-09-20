using System;
using SituationalAwareness.Extensibility;
using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.WeatherReport
{
	/// <summary>
	/// The report's own window, opened from the small button in the corner of
	/// SA's weather section. A window rather than a grid squeezed into SA's own
	/// 138px column: it can afford full-width buttons, a real notes field, and
	/// SA's current verdict shown next to them, which is what makes a report a
	/// deliberate "SA says X, I see Y" instead of a blind sample.
	/// </summary>
	internal class WeatherReportWindow : MonoBehaviour
	{
		// English on purpose, not localized: these strings are the report's own
		// vocabulary and go straight into the CSV, where a translated label would
		// make pooled data unusable.
		private static readonly string[] Labels =
		{
			"Clear", "Cloud", "Fog", "Rain", "Storm", "Snow", "Dust", "Other"
		};

		private const float WindowWidth = 208f;
		private const float ButtonWidth = 92f;
		private const float ButtonHeight = 24f;

		private Canvas canvas;
		private CanvasScaler scaler;
		private RectTransform windowRect;
		private InputField noteField;
		private Text verdictLabel;
		private float nextVerdictRefresh;

		/// <summary>
		/// Held for as long as the note field has keyboard focus, or typing a note
		/// fires flight commands. The hover lock in ReportWindowDrag sits on the
		/// header alone, where the pointer rarely is while typing: focus, not
		/// hover, decides where keystrokes go.
		/// </summary>
		private const string TypingLockId = "SA_WeatherReport_typing";
		private bool typingLocked;

		internal static WeatherReportWindow Create()
		{
			GameObject host = new GameObject("SaWeatherReportWindow");
			DontDestroyOnLoad(host);
			return host.AddComponent<WeatherReportWindow>();
		}

		private void Awake()
		{
			canvas = gameObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			// Above SA's own 900: this window is opened from SA and must never
			// end up behind it.
			canvas.sortingOrder = 905;
			scaler = gameObject.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			scaler.scaleFactor = GameSettings.UI_SCALE;
			gameObject.AddComponent<GraphicRaycaster>();

			Build();
			gameObject.SetActive(false);

			// A custom overlay canvas must honour F2 like any other KSP UI.
			GameEvents.onHideUI.Add(OnHideUI);
			GameEvents.onShowUI.Add(OnShowUI);
		}

		private void OnDestroy()
		{
			GameEvents.onHideUI.Remove(OnHideUI);
			GameEvents.onShowUI.Remove(OnShowUI);
			SetTypingLock(false);
		}

		private void OnDisable()
		{
			// Hide() deactivates the object, which stops Update: drop the lock
			// here or a note left with focus would wedge the controls.
			SetTypingLock(false);
		}

		private void SetTypingLock(bool locked)
		{
			if (locked == typingLocked) return;
			typingLocked = locked;
			if (locked) InputLockManager.SetControlLock(ControlTypes.All, TypingLockId);
			else InputLockManager.RemoveControlLock(TypingLockId);
		}

		private void OnHideUI() => canvas.enabled = false;
		private void OnShowUI() => canvas.enabled = true;

		private void Build()
		{
			Image window = ReportUi.Panel_("Window", transform, ReportUi.Panel);
			windowRect = window.rectTransform;
			windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
			windowRect.pivot = new Vector2(0.5f, 0.5f);
			windowRect.anchoredPosition = new Vector2(240f, 40f);
			windowRect.sizeDelta = new Vector2(WindowWidth, 100f);
			Outline outline = window.gameObject.AddComponent<Outline>();
			outline.effectColor = ReportUi.PanelEdge;
			outline.effectDistance = new Vector2(1f, -1f);

			ReportUi.Vertical(window.gameObject, 0, 0f);
			window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			BuildHeader(window.transform);

			GameObject content = ReportUi.Go("Content", window.transform);
			ReportUi.Vertical(content, 8, 6f);

			verdictLabel = ReportUi.Label(content.transform, "", 10, ReportUi.TextDim, TextAnchor.MiddleCenter);
			ReportUi.Size(verdictLabel.gameObject, -1f, 13f);

			BuildButtonGrid(content.transform);

			Text caption = ReportUi.Label(content.transform, "Notes:", 10, ReportUi.TextDim);
			ReportUi.Size(caption.gameObject, -1f, 13f);
			noteField = BuildNoteField(content.transform);

			ReportUi.Size(ReportUi.TextButton(content.transform, "Close", 11, Hide).gameObject, -1f, 20f);
		}

		private void BuildHeader(Transform parent)
		{
			Image header = ReportUi.Panel_("Header", parent, ReportUi.HeaderBg);
			ReportUi.Size(header.gameObject, -1f, 20f);
			Text title = ReportUi.Label(header.transform, "WEATHER REPORT", 10, ReportUi.Amber, TextAnchor.MiddleCenter);
			ReportUi.Stretch(title.rectTransform);
			ReportWindowDrag drag = header.gameObject.AddComponent<ReportWindowDrag>();
			drag.Target = windowRect;
		}

		private void BuildButtonGrid(Transform parent)
		{
			GameObject grid = ReportUi.Go("Buttons", parent);
			GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
			layout.cellSize = new Vector2(ButtonWidth, ButtonHeight);
			layout.spacing = new Vector2(4f, 4f);
			layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			layout.constraintCount = 2;
			// 8 labels, 2 columns -> 4 rows.
			ReportUi.Size(grid, -1f, 4f * ButtonHeight + 3f * 4f);

			foreach (string label in Labels)
			{
				string captured = label;
				ReportUi.TextButton(grid.transform, captured, 11, () => OnLabelPressed(captured));
			}
		}

		private InputField BuildNoteField(Transform parent)
		{
			GameObject go = ReportUi.Go("Note", parent);
			ReportUi.Size(go, -1f, 22f);
			Image background = go.AddComponent<Image>();
			background.color = ReportUi.FieldBg;
			InputField field = go.AddComponent<InputField>();
			field.targetGraphic = background;

			Text text = ReportUi.Label(go.transform, "", 11, ReportUi.Text);
			text.supportRichText = false;
			RectTransform rect = text.rectTransform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(5f, 3f);
			rect.offsetMax = new Vector2(-5f, -3f);

			field.textComponent = text;
			field.lineType = InputField.LineType.SingleLine;
			return field;
		}

		private void Update()
		{
			scaler.scaleFactor = GameSettings.UI_SCALE;
			SetTypingLock(noteField != null && noteField.isFocused);
			// Once a second: SA's own classifier is throttled to the same rate, so
			// polling faster would only re-read a cached value.
			if (Time.unscaledTime < nextVerdictRefresh) return;
			nextVerdictRefresh = Time.unscaledTime + 1f;
			RefreshVerdict();
		}

		private void RefreshVerdict()
		{
			if (verdictLabel == null) return;
			try
			{
				string state = SaExtensionPoint.CurrentWeather(FlightGlobals.ActiveVessel);
				verdictLabel.text = "SA reads: " + state;
				verdictLabel.color = state == "UNKNOWN" ? ReportUi.TextDim : ReportUi.Text;
			}
			catch (Exception)
			{
				// An SA too old to expose the verdict is no reason to break the
				// report: the buttons still work, the column is just empty.
				verdictLabel.text = "";
			}
		}

		private void OnLabelPressed(string label)
		{
			WeatherSample sample = WeatherProbe.Sample(FlightGlobals.ActiveVessel);
			bool written = WeatherReportCsvWriter.Write(label, noteField != null ? noteField.text : "", sample);
			// Immediate on-screen confirmation, the button itself giving no sign
			// that anything happened. Stock ScreenMessages, upper centre, where
			// the game's own quicksave and warp notices go.
			if (written)
			{
				ScreenMessages.PostScreenMessage("Weather Report: " + label.ToUpperInvariant() + " recorded", 2.5f, ScreenMessageStyle.UPPER_CENTER);
			}
			else
			{
				ScreenMessages.PostScreenMessage("<color=#ff6060>Weather Report: sample NOT recorded, see KSP.log</color>", 4f, ScreenMessageStyle.UPPER_CENTER);
			}
		}

		internal void Toggle()
		{
			if (gameObject.activeSelf) Hide();
			else Show();
		}

		private void Show()
		{
			gameObject.SetActive(true);
			RefreshVerdict();
		}

		internal void Hide() => gameObject.SetActive(false);
	}
}
