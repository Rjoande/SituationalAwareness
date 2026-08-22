using SituationalAwareness.Extensibility;
using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.WeatherSurvey
{
	/// <summary>
	/// Fase 1 (prova locale, notes/indagine-meteo.md §8): hooks into SA's
	/// weather extension point and builds a minimal, utilitarian button row
	/// — no visual polish yet, that comes after the mechanism itself is
	/// validated in game. Same scene lifecycle as SA's own SaToolbarApp
	/// (re-instantiated every Flight scene load).
	/// </summary>
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	internal class WeatherSurveyApp : MonoBehaviour
	{
		// English, deliberately not localized (user request 2026-08-18) —
		// same string for the button and the CSV row, short enough on their
		// own to fit the 4x2 grid without a separate abbreviation.
		private static readonly (string Short, string Full)[] Labels =
		{
			("Clear", "Clear"), ("Cloud", "Cloud"), ("Fog", "Fog"), ("Rain", "Rain"),
			("Storm", "Storm"), ("Snow", "Snow"), ("Dust", "Dust"), ("Other", "Other")
		};

		private InputField noteField;

		private void Start()
		{
			// Re-subscribe on every firing, not once (SaExtensionPoint's own
			// contract): SA destroys and rebuilds the host Transform on
			// every collapse<->extended toggle and window re-open.
			SaExtensionPoint.OnWeatherHostBuilt += BuildRow;
		}

		private void OnDestroy()
		{
			// Critical: a static event without this leaks a handler bound to
			// a dead MonoBehaviour across every scene reload, and each leaked
			// handler would fire (and write a CSV row) on every future press.
			SaExtensionPoint.OnWeatherHostBuilt -= BuildRow;
		}

		private void BuildRow(Transform host)
		{
			if (host == null) return;

			GameObject row = new GameObject("WeatherSurveyRow", typeof(RectTransform));
			row.transform.SetParent(host, false);
			VerticalLayoutGroup vGroup = row.AddComponent<VerticalLayoutGroup>();
			vGroup.spacing = 4f;
			vGroup.childForceExpandWidth = true;
			vGroup.childForceExpandHeight = false;
			vGroup.padding = new RectOffset(0, 0, 4, 0);
			row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			// 3x3 grid (user request 2026-08-18, 3+3+2 for 8 buttons), not
			// 4x2 — more room per cell in the 138px-wide dial column.
			GameObject buttonsGo = new GameObject("Buttons", typeof(RectTransform));
			buttonsGo.transform.SetParent(row.transform, false);
			GridLayoutGroup grid = buttonsGo.AddComponent<GridLayoutGroup>();
			// 36x18 confirmed fine on further in-game check (user, 2026-08-18)
			// — the 3x3 layout itself was the fix, not a smaller cell.
			grid.cellSize = new Vector2(36f, 18f);
			grid.spacing = new Vector2(2f, 2f);
			grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			grid.constraintCount = 3;
			buttonsGo.AddComponent<LayoutElement>().preferredHeight = 58f;

			foreach ((string shortLabel, string fullLabel) in Labels)
			{
				AddButton(buttonsGo.transform, shortLabel, fullLabel);
			}

			GameObject notesCaption = new GameObject("NotesCaption", typeof(RectTransform));
			notesCaption.transform.SetParent(row.transform, false);
			notesCaption.AddComponent<LayoutElement>().preferredHeight = 12f;
			Text captionText = notesCaption.AddComponent<Text>();
			captionText.text = "Notes:";
			captionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			captionText.fontSize = 8;
			captionText.alignment = TextAnchor.MiddleLeft;
			captionText.color = new Color(0.49f, 0.56f, 0.58f);

			noteField = AddInputField(row.transform);
		}

		private void AddButton(Transform parent, string shortLabel, string fullLabel)
		{
			GameObject go = new GameObject("Btn_" + shortLabel, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			Image bg = go.AddComponent<Image>();
			bg.color = new Color(0.15f, 0.13f, 0.08f);
			Button btn = go.AddComponent<Button>();
			ColorBlock colors = btn.colors;
			colors.normalColor = new Color(0.15f, 0.13f, 0.08f);
			colors.highlightedColor = new Color(0.30f, 0.22f, 0.08f);
			colors.pressedColor = new Color(0.45f, 0.30f, 0.05f);
			btn.colors = colors;

			GameObject textGo = new GameObject("Text", typeof(RectTransform));
			textGo.transform.SetParent(go.transform, false);
			Text text = textGo.AddComponent<Text>();
			text.text = shortLabel;
			text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			text.fontSize = 8;
			text.alignment = TextAnchor.MiddleCenter;
			text.color = new Color(1f, 0.7f, 0.25f);
			RectTransform textRect = textGo.GetComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = Vector2.zero;
			textRect.offsetMax = Vector2.zero;

			btn.onClick.AddListener(() => OnLabelPressed(fullLabel));
		}

		private InputField AddInputField(Transform parent)
		{
			GameObject go = new GameObject("Note", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			go.AddComponent<LayoutElement>().preferredHeight = 18f;
			Image bg = go.AddComponent<Image>();
			bg.color = new Color(0.08f, 0.08f, 0.08f);
			InputField field = go.AddComponent<InputField>();
			field.targetGraphic = bg;

			GameObject textGo = new GameObject("Text", typeof(RectTransform));
			textGo.transform.SetParent(go.transform, false);
			Text text = textGo.AddComponent<Text>();
			text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			text.fontSize = 9;
			text.color = new Color(0.85f, 0.89f, 0.89f);
			text.supportRichText = false;
			RectTransform textRect = textGo.GetComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = new Vector2(4f, 2f);
			textRect.offsetMax = new Vector2(-4f, -2f);

			field.textComponent = text;
			field.lineType = InputField.LineType.SingleLine;
			return field;
		}

		private void OnLabelPressed(string label)
		{
			WeatherSample sample = WeatherProbe.Sample(FlightGlobals.ActiveVessel);
			string note = noteField != null ? noteField.text : "";
			WeatherSurveyCsvWriter.Write(label, note, sample);
		}
	}
}
