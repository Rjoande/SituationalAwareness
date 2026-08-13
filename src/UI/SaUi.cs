using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// Code-built UGUI factory for the M2 telemetry skin (design doc §6.8):
	/// amber-on-dark avionics palette, Consolas with a stock-font fallback.
	/// </summary>
	internal static class SaUi
	{
		// Palette (design doc §6.8, from the approved mockup).
		public static readonly Color Panel = FromHex("0f1517");
		public static readonly Color PanelEdge = FromHex("1e2a2e");
		public static readonly Color HeaderBg = FromHex("0a0e0f");
		public static readonly Color Inset = FromHex("16202a");
		public static readonly Color Amber = FromHex("ffb000");
		public static readonly Color AmberDim = FromHex("8a5f00");
		public static readonly Color Cyan = FromHex("4fd1c5");
		public static readonly Color Text = FromHex("d8e2e4");
		public static readonly Color TextDim = FromHex("7d8e93");
		public static readonly Color Danger = FromHex("ff5c4d");
		public static readonly Color LedGreen = FromHex("5ad46a");
		public static readonly Color LedYellow = FromHex("ffd23e");
		public static readonly Color LedOff = FromHex("3a444a");
		// Semantic alias for LedYellow (M3 restyling): a "getting close, not
		// critical yet" state on data rows (temperature warm bands) — same
		// hex, but named for what it means here rather than "led yellow".
		public static readonly Color Warn = FromHex("ffd23e");

		// Hex strings for inline rich-text spans (Text.text = "<color=#..>"),
		// kept in sync with the Color constants above by construction — one
		// literal per color, not scattered magic strings at each call site.
		public const string TextHex = "d8e2e4";
		public const string TextDimHex = "7d8e93";
		public const string CyanHex = "4fd1c5";

		// Orbit ring dial (bug fix, test M2 fase 8: lit/shadow used two
		// similarly-dark grays, indistinguishable at a glance).
		public static readonly Color OrbitLit = FromHex("9fd0f0");
		public static readonly Color OrbitShadow = FromHex("152238");
		public static readonly Color OrbitMarker = Color.white;
		// Brightened from the original Inset (#16202a — "ancora molto dim",
		// test M2 retest) for the orbit ring's planet disc default.
		public static readonly Color OrbitPlanetDefault = FromHex("324254");

		// Test M2 (design doc §6.8): edit this to a garbage name (e.g.
		// "NoSuchFontXYZ") and rebuild to exercise the fallback path in
		// game — restore to "Consolas" afterwards. See notes/test-m2.md.
		private const string PrimaryFontName = "Consolas";

		private static Font font;

		public static Font Font
		{
			get
			{
				if (font == null)
				{
					font = ResolveFont();
				}
				return font;
			}
		}

		private static Font ResolveFont()
		{
			Font f = UnityEngine.Font.CreateDynamicFontFromOSFont(PrimaryFontName, 14);
			if (f != null && f.dynamic)
			{
				return f;
			}
			if (UISkinManager.defaultSkin != null && UISkinManager.defaultSkin.font != null)
			{
				return UISkinManager.defaultSkin.font;
			}
			return Resources.GetBuiltinResource<Font>("Arial.ttf");
		}

		private static Color FromHex(string hex)
		{
			byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
			byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
			byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
			return new Color32(r, g, b, 255);
		}

		public static GameObject Go(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return go;
		}

		public static Image Panel_(string name, Transform parent, Color color)
		{
			Image image = Go(name, parent).AddComponent<Image>();
			image.color = color;
			return image;
		}

		/// <summary>Panel with a 1px border: border-colored back + inset front.</summary>
		public static RectTransform Bordered(string name, Transform parent, Color fill, Color border)
		{
			Image back = Panel_(name, parent, border);
			Image front = Panel_("Fill", back.transform, fill);
			Stretch(front.rectTransform, 1f);
			front.raycastTarget = false;
			front.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
			return back.rectTransform;
		}

		public static void Stretch(RectTransform rect, float margin = 0f)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.offsetMin = new Vector2(margin, margin);
			rect.offsetMax = new Vector2(-margin, -margin);
		}

		public static Text Label(Transform parent, string text, int size, Color color,
			TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
		{
			Text label = Go("Label", parent).AddComponent<Text>();
			label.font = Font;
			label.fontSize = size;
			label.fontStyle = style;
			label.color = color;
			label.alignment = anchor;
			label.horizontalOverflow = HorizontalWrapMode.Overflow;
			label.verticalOverflow = VerticalWrapMode.Overflow;
			label.text = text;
			label.raycastTarget = false;
			return label;
		}

		public static Button TextButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick,
			Color background, Color textColor, int fontSize = 13, float width = 0f, float height = 24f)
		{
			Image image = Panel_("Button", parent, background);
			Button button = image.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			button.onClick.AddListener(onClick);
			HorizontalLayoutGroup inner = image.gameObject.AddComponent<HorizontalLayoutGroup>();
			inner.padding = new RectOffset(8, 8, 2, 2);
			inner.childAlignment = TextAnchor.MiddleCenter;
			inner.childForceExpandWidth = false;
			inner.childForceExpandHeight = false;
			inner.childControlWidth = true;
			inner.childControlHeight = true;
			Label(image.transform, text, fontSize, textColor, TextAnchor.MiddleCenter);
			LayoutElement le = image.gameObject.AddComponent<LayoutElement>();
			le.preferredHeight = height;
			if (width > 0f) le.preferredWidth = width;
			return button;
		}

		/// <summary>A row that reacts to clicks (unit cycling, design doc §6.4) — invisible button stretched over the whole row. Must sit outside the row's own HorizontalLayoutGroup sizing (ignoreLayout) or the layout group would squeeze it into its own slot instead of covering the row.</summary>
		public static Button ClickCatcher(Transform parent, UnityEngine.Events.UnityAction onClick)
		{
			Image image = Go("ClickCatcher", parent).AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0f);
			Stretch(image.rectTransform);
			image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
			Button button = image.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			button.onClick.AddListener(onClick);
			ColorBlock colors = button.colors;
			colors.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
			colors.pressedColor = new Color(1f, 1f, 1f, 0.12f);
			colors.normalColor = Color.white;
			button.colors = colors;
			return button;
		}

		public static VerticalLayoutGroup Vertical(GameObject go, int padding, float spacing,
			bool childForceExpandWidth = true)
		{
			VerticalLayoutGroup group = go.AddComponent<VerticalLayoutGroup>();
			group.padding = new RectOffset(padding, padding, padding, padding);
			group.spacing = spacing;
			group.childForceExpandWidth = childForceExpandWidth;
			group.childForceExpandHeight = false;
			group.childControlWidth = true;
			group.childControlHeight = true;
			return group;
		}

		public static HorizontalLayoutGroup Horizontal(GameObject go, int padding, float spacing,
			TextAnchor align = TextAnchor.MiddleLeft)
		{
			HorizontalLayoutGroup group = go.AddComponent<HorizontalLayoutGroup>();
			group.padding = new RectOffset(padding, padding, padding, padding);
			group.spacing = spacing;
			group.childForceExpandWidth = false;
			group.childForceExpandHeight = false;
			group.childControlWidth = true;
			group.childControlHeight = true;
			group.childAlignment = align;
			return group;
		}

		public static LayoutElement Size(GameObject go, float width = -1f, float height = -1f, float flexibleWidth = -1f)
		{
			LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
			if (width >= 0f) le.preferredWidth = width;
			if (height >= 0f) le.preferredHeight = height;
			if (flexibleWidth >= 0f) le.flexibleWidth = flexibleWidth;
			return le;
		}

		public static GameObject Divider(Transform parent)
		{
			Image img = Panel_("Divider", parent, PanelEdge);
			Size(img.gameObject, -1f, 1f);
			return img.gameObject;
		}
	}
}
