using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SituationalAwareness.WeatherReport
{
	/// <summary>
	/// Minimal UI helpers for the companion.
	///
	/// SA's own SaUi is internal to its assembly and stays that way: the point of
	/// SaExtensionPoint is that a companion touches one public class, not SA's
	/// internals. The cost is this duplication — the palette below mirrors SaUi's
	/// so the window does not look foreign, and follows it by hand if it changes.
	/// </summary>
	internal static class ReportUi
	{
		internal static readonly Color Panel = FromHex("0f1517");
		internal static readonly Color PanelEdge = FromHex("1e2a2e");
		internal static readonly Color HeaderBg = FromHex("0a0e0f");
		internal static readonly Color Amber = FromHex("ffb000");
		internal static readonly Color Text = FromHex("d8e2e4");
		internal static readonly Color TextDim = FromHex("7d8e93");
		internal static readonly Color ButtonFill = FromHex("26200d");
		internal static readonly Color ButtonHover = FromHex("4d3814");
		internal static readonly Color ButtonPressed = FromHex("734d0d");
		internal static readonly Color FieldBg = FromHex("141414");

		private static Color FromHex(string hex)
		{
			return new Color(
				int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
				int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
				int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f);
		}

		internal static Font Font => Resources.GetBuiltinResource<Font>("Arial.ttf");

		internal static GameObject Go(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return go;
		}

		internal static Image Panel_(string name, Transform parent, Color color)
		{
			GameObject go = Go(name, parent);
			Image image = go.AddComponent<Image>();
			image.color = color;
			return image;
		}

		internal static Text Label(Transform parent, string text, int size, Color color,
			TextAnchor anchor = TextAnchor.MiddleLeft)
		{
			GameObject go = Go("Label", parent);
			Text label = go.AddComponent<Text>();
			label.text = text;
			label.font = Font;
			label.fontSize = size;
			label.color = color;
			label.alignment = anchor;
			label.horizontalOverflow = HorizontalWrapMode.Overflow;
			return label;
		}

		internal static VerticalLayoutGroup Vertical(GameObject go, int padding, float spacing)
		{
			VerticalLayoutGroup group = go.AddComponent<VerticalLayoutGroup>();
			group.padding = new RectOffset(padding, padding, padding, padding);
			group.spacing = spacing;
			group.childForceExpandWidth = true;
			group.childForceExpandHeight = false;
			return group;
		}

		internal static LayoutElement Size(GameObject go, float width = -1f, float height = -1f)
		{
			LayoutElement element = go.AddComponent<LayoutElement>();
			if (width > 0f) element.preferredWidth = width;
			if (height > 0f) element.preferredHeight = height;
			return element;
		}

		internal static Button TextButton(Transform parent, string text, int fontSize,
			UnityEngine.Events.UnityAction onClick)
		{
			GameObject go = Go("Btn_" + text, parent);
			Image background = go.AddComponent<Image>();
			background.color = ButtonFill;
			Button button = go.AddComponent<Button>();
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = ButtonHover * 2f;
			colors.pressedColor = ButtonPressed * 2f;
			button.colors = colors;
			button.targetGraphic = background;

			Text label = Label(go.transform, text, fontSize, Amber, TextAnchor.MiddleCenter);
			Stretch(label.rectTransform);
			button.onClick.AddListener(onClick);
			return button;
		}

		internal static void Stretch(RectTransform rect, float margin = 0f)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(margin, margin);
			rect.offsetMax = new Vector2(-margin, -margin);
		}
	}

	/// <summary>
	/// Drag-to-move for the report window, and the KSP input lock while the
	/// pointer is over the header it is attached to. Typing is covered
	/// separately, by a lock WeatherReportWindow holds while the note field has
	/// focus: the pointer is anywhere but the header at that moment.
	/// </summary>
	internal class ReportWindowDrag : MonoBehaviour, IDragHandler, IPointerEnterHandler, IPointerExitHandler
	{
		internal RectTransform Target;
		private const string LockId = "SA_WeatherReport_window";

		public void OnDrag(PointerEventData eventData)
		{
			if (Target == null) return;
			Target.anchoredPosition += eventData.delta / Mathf.Max(0.01f, Target.lossyScale.x);
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			InputLockManager.SetControlLock(ControlTypes.All, LockId);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			InputLockManager.RemoveControlLock(LockId);
		}

		private void OnDisable()
		{
			// Leaving the lock behind on a scene change or a window close would
			// wedge the player's controls permanently.
			InputLockManager.RemoveControlLock(LockId);
		}
	}
}
