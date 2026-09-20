using System;
using System.Collections.Generic;
using System.Globalization;
using KSP.Localization;
using SituationalAwareness.Core;
using SituationalAwareness.Extensibility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// The single SA window (design doc §6): an extended telemetry panel
	/// (header, dial + data, footer) that doubles as the collapsed strip (§6.7).
	/// One window toggled by double-clicking the header, never two.
	/// </summary>
	internal class SaWindow : MonoBehaviour
	{
		private const float ExtendedWidth = 430f;
		private const float StripWidth = 430f;
		// Named rather than inline, so the data column's fixed width cannot
		// silently drift out of sync with the dial column's.
		private const float DialColWidth = 138f;
		private const float VDividerWidth = 1f;
		private const string InputLockId = "SA_WINDOW";

		// Strip weather block: icon plus external temperature, with no state name
		// and no severity badge (see BuildStrip/ApplyStrip).
		private GameObject stripWeatherGo;
		private Image stripWeatherIcon;
		private Text stripWeatherTemp;
		private const double LowTempAlertC = 0.0;
		private const double HighTempAlertC = 50.0;
		// EXT TEMP is 3-tier: warn between HighTempAlertC and this, danger above.
		private const double VeryHighTempAlertC = 100.0;
		// HULL TEMP colour comes from the worst part's T/maxTemp ratio rather than
		// an absolute temperature: 1500 K is fine for a heat shield and already
		// fatal for a science part, so only a relative threshold means the same
		// thing everywhere.
		private const double HullWarnRatio = 0.6;
		private const double HullDangerRatio = 0.8;
		// 10 Hz: fast enough to feel live, slow enough that decimals do not
		// flicker.
		private const float RefreshInterval = 0.1f;
		private const string LockedBadgeColorHex = "#ff5c4d";

		private static SaWindow current;
		public static System.Action OnClosed;

		private Canvas canvas;
		private CanvasScaler scaler;
		private RectTransform windowRect;
		private Transform shellHost;
		private SaDial.Handle dial;
		private SaMode lastMode = (SaMode)(-1);
		private float refreshAccumulator;
		private bool hiddenByGameUI;

		private readonly Dictionary<string, Text> valueLabels = new Dictionary<string, Text>();
		private Text stripHot, stripPhase, stripTail;
		private SaVectorDot ledDotExtended, ledDotStrip;
		private Text chipLabel;
		private Image chipBorderImage;
		private Text titleLabel;
		private Text footerLeft, footerRight;
		private SaDial.StripHandle stripDial;
		private Text stripDate;
		private GameObject metRowGo;
		private GameObject terminatorRowGo;
		private GameObject coordinatesRowGo;
		// Only marks a real boundary in Surface mode.
		private GameObject positionDividerGo;
		// Shown in any body-surface context (Surface or TidalLock, never Orbit)
		// and only where the body has an atmosphere, since atmosphericTemperature
		// reads a fixed constant in a vacuum. The condition is mode plus body
		// rather than a live pressure reading on purpose: a vessel briefly above
		// the atmosphere on a suborbital hop stays in Surface mode and must keep
		// showing "VACUUM" instead of blinking the rows off. pressureRowGo shares
		// it, and HULL TEMP covers the vacuum case instead.
		private GameObject extTempRowGo;
		private GameObject pressureRowGo;
		// WEATHER lives in the LEFT column under the dial rather than among the
		// data rows: it is a state to glance at, not a number to read, and the
		// icon needs room the value column has not. It needs both an atmosphere
		// and EVE installed, so an install without EVE sees no section rather
		// than an empty one.
		private GameObject weatherSectionGo;
		private Image weatherIcon;
		private Text weatherLabel;
		// Forecast line under the state, deliberately secondary: smaller, dimmer,
		// lowercase, a blank line away. Both parts stay inactive when there is
		// nothing to forecast, so the section keeps its height and stays centred.
		private GameObject weatherForecastSpacerGo;
		private Text weatherForecastLabel;
		// The badge always carries the text glyph; the image is only present when
		// an alert texture is installed, and only ever draws the "!".
		private Image weatherBadgeImage;
		private Text weatherBadgeText;
		private string lastWeatherIconPath;
		// Slot under the dial for a companion's own content. Follows
		// showAtmosphericRows exactly, like EXT TEMP and PRESSURE.
		private GameObject weatherHostGo;
		// Surface mode only, and hidden when the body IS the star: sun elevation
		// and azimuth mean nothing from inside the light source.
		private GameObject sunRowGo;
		// Cached from the last throttled refresh, so Update()'s fast
		// countdown-only path can rebuild the whole dial sub-label without a full
		// SaReadout (see BuildSurfaceSubText).
		private double lastDayProgress01;
		private int lastSolarHour, lastSolarMinute, lastSolarSecond;
		private double lastEquationOfTimeSec;
		private double lastSolarDayLengthSec;
		// The KSC's timezone VALUE is fixed for the session, its coordinates
		// being set once at game load, so it is computed once. The label built
		// from it is rebuilt every refresh, since whether it shows at all depends
		// on mode and body (see UpdateKscRow).
		private int? kscTimeZoneCached;

		public static void Open()
		{
			if (current != null) return;
			// Re-anchor every cached body's mean-time offset on each open, so any
			// residual drift never grows past one open-to-open interval.
			MeanTimeCalibration.Reset();
			GameObject host = new GameObject("SaWindow");
			current = host.AddComponent<SaWindow>();
			current.Build();
		}

		public static void CloseCurrent()
		{
			current?.Close();
		}

		private void Close()
		{
			Destroy(gameObject);
		}

		private void OnDestroy()
		{
			GameEvents.onHideUI.Remove(OnHideUI);
			GameEvents.onShowUI.Remove(OnShowUI);
			InputLockManager.RemoveControlLock(InputLockId);
			SavePosition();
			if (current == this)
			{
				current = null;
				OnClosed?.Invoke();
			}
		}

		private static string Loc(string key) => Localizer.Format(key);

		// ------------------------------------------------------------------ build

		private void Build()
		{
			SaPersist.EnsureLoaded();

			canvas = gameObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 900;
			scaler = gameObject.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			// SA's own scale stacks on top of the stock UI Scale rather than
			// replacing it. ConstantPixelSize + scaleFactor is a pixel-perfect
			// canvas multiplier, so no value blurs the text. Re-synced every
			// refresh tick, so a change in the settings dialog lands at once.
			scaler.scaleFactor = GameSettings.UI_SCALE * SaParams.UiScale;
			gameObject.AddComponent<GraphicRaycaster>();

			windowRect = SaUi.Bordered("Window", transform, SaUi.Panel, SaUi.PanelEdge);
			windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
			windowRect.pivot = new Vector2(0.5f, 0.5f);
			windowRect.anchoredPosition = SaPersist.HasWindowPosition ? SaPersist.WindowPosition : new Vector2(0f, 40f);
			windowRect.sizeDelta = new Vector2(ExtendedWidth, 100f);
			FocusLock focus = windowRect.gameObject.AddComponent<FocusLock>();
			focus.lockId = InputLockId;

			SaUi.Vertical(windowRect.gameObject, 0, 0f);
			ContentSizeFitter fitter = windowRect.gameObject.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			shellHost = SaUi.Go("Shell", windowRect).transform;
			SaUi.Vertical(shellHost.gameObject, 0, 0f);

			// A custom overlay Canvas must honour F2; that and the pause menu are
			// two triggers for the same "hide our canvas" outcome (see Update).
			GameEvents.onHideUI.Add(OnHideUI);
			GameEvents.onShowUI.Add(OnShowUI);

			RebuildShell(preserveTopEdge: false);
		}

		private void OnHideUI()
		{
			hiddenByGameUI = true;
		}

		private void OnShowUI()
		{
			hiddenByGameUI = false;
		}

		private void ToggleCollapsed()
		{
			SaPersist.Collapsed = !SaPersist.Collapsed;
			SaPersist.Save();
			RebuildShell(preserveTopEdge: true);
		}

		private void RebuildShell(bool preserveTopEdge)
		{
			float oldHeight = windowRect.rect.height;

			// DestroyImmediate, not Destroy: the old children must be gone, not
			// merely scheduled for end-of-frame cleanup, before the forced layout
			// rebuild below measures the hierarchy.
			for (int i = shellHost.childCount - 1; i >= 0; i--)
			{
				DestroyImmediate(shellHost.GetChild(i).gameObject);
			}
			valueLabels.Clear();
			dial = null;
			// The KSC row's label is about to be rebuilt bare, without its TZ
			// suffix. The cached value needs no recomputing, but the flag must
			// reset so UpdateKscRow re-applies it to the new Text once.
			kscTimeZoneCached = null;
			windowRect.sizeDelta = new Vector2(SaPersist.Collapsed ? StripWidth : ExtendedWidth, windowRect.sizeDelta.y);

			if (SaPersist.Collapsed)
			{
				BuildStrip();
			}
			else
			{
				BuildHeader();
				BuildBody();
				BuildFooter();
			}
			lastMode = (SaMode)(-1);

			// With a centred pivot a height change moves both edges, so collapsing
			// would recentre the window instead of keeping the titlebar put. Force
			// the layout NOW, not next frame, so the new height is known
			// synchronously and can be compensated for.
			LayoutRebuilder.ForceRebuildLayoutImmediate(windowRect);
			if (preserveTopEdge)
			{
				float newHeight = windowRect.rect.height;
				Vector2 pos = windowRect.anchoredPosition;
				windowRect.anchoredPosition = new Vector2(pos.x, pos.y + (oldHeight - newHeight) / 2f);
			}
		}

		// ------------------------------------------------------------- header

		private void BuildHeader()
		{
			RectTransform bar = SaUi.Bordered("Header", shellHost, SaUi.HeaderBg, SaUi.PanelEdge);
			SaUi.Size(bar.gameObject, -1f, 30f);
			SaUi.Horizontal(bar.gameObject, 8, 8f);

			titleLabel = SaUi.Label(bar, "SA", 11, SaUi.Amber, TextAnchor.MiddleLeft);
			SaUi.Size(titleLabel.gameObject, -1f, 20f, 1f);

			RectTransform chipBack = SaUi.Bordered("Chip", bar, SaUi.Panel, SaUi.Cyan);
			SaUi.Size(chipBack.gameObject, 80f, 18f);
			chipBorderImage = chipBack.GetComponent<Image>();
			chipLabel = SaUi.Label(chipBack, "-", 9, SaUi.Cyan, TextAnchor.MiddleCenter);
			SaUi.Stretch(chipLabel.rectTransform);

			// Led last in the row, and round via SaVectorDot rather than a square
			// Image.
			ledDotExtended = NewLedDot(bar);

			AttachDrag(bar);
			AttachDoubleClick(bar, ToggleCollapsed);
		}

		private SaVectorDot NewLedDot(Transform parent)
		{
			GameObject go = SaUi.Go("Led", parent);
			SaUi.Size(go, 8f, 8f);
			SaVectorDot dot = go.AddComponent<SaVectorDot>();
			dot.raycastTarget = false;
			dot.SetPosition(Vector2.zero, 4f);
			return dot;
		}

		// --------------------------------------------------------------- body

		private void BuildBody()
		{
			GameObject body = SaUi.Go("Body", shellHost);
			SaUi.Horizontal(body, 0, 0);

			// Plain Go(), not Bordered(): the window's own panel colour shows
			// through underneath, so the dial has no frame of its own.
			GameObject dialCol = SaUi.Go("DialCol", body.transform);
			// flexibleHeight so the column fills the body row rather than hugging
			// its content: the two halves below can only share leftover space if
			// there is any, and the row's height comes from the data column.
			SaUi.Size(dialCol, DialColWidth, -1f).flexibleHeight = 1f;
			// This group's spacing is the only thing setting the gap between the
			// dial graphic and the labels below it, and all three modes reuse the
			// same dialCol, so there is no per-mode drift.
			SaUi.Vertical(dialCol, 10, 8f);

			// Vertical divider between dial and data, a sibling in this same
			// HorizontalLayoutGroup so header and footer are excluded from it.
			Image vDivider = SaUi.Panel_("VDivider", body.transform, SaUi.PanelEdge);
			LayoutElement vDividerLe = SaUi.Size(vDivider.gameObject, VDividerWidth, -1f);
			vDividerLe.flexibleHeight = 1f;

			GameObject dataCol = SaUi.Go("DataCol", body.transform);
			// Fixed width, never content-driven: without an explicit preferredWidth
			// the column would defer to its children, and the clock Text's
			// preferred width scales with the STRING LENGTH, so a long countdown
			// would reflow the row it shares with the sub-label.
			SaUi.Size(dataCol, ExtendedWidth - DialColWidth - VDividerWidth, -1f, 1f);
			// Horizontal padding lives on each row, not here: dividers must bleed
			// to the column's true edges, which only works while the column itself
			// carries none. The vertical padding stays, shared by every row.
			VerticalLayoutGroup dataColGroup = SaUi.Vertical(dataCol, 0, 6f);
			dataColGroup.padding = new RectOffset(0, 0, 12, 12);

			// Two equal halves, dial above and weather below, each centring its own
			// content: equal flexibleHeight splits whatever the data column leaves
			// over, and MiddleCenter keeps each block in the middle of its half
			// rather than both piling up at the top.
			GameObject dialHalf = SaUi.Go("DialHalf", dialCol.transform);
			SaUi.Vertical(dialHalf, 0, 8f).childAlignment = TextAnchor.MiddleCenter;
			SaUi.Size(dialHalf, -1f, -1f).flexibleHeight = 1f;

			GameObject weatherHalf = SaUi.Go("WeatherHalf", dialCol.transform);
			SaUi.Vertical(weatherHalf, 0, 4f).childAlignment = TextAnchor.MiddleCenter;
			SaUi.Size(weatherHalf, -1f, -1f).flexibleHeight = 1f;

			dial = SaDial.Build(dialHalf.transform);
			// The dial's phase/sub area is not a data row and has no click handling
			// of its own, so it gets the same ClickCatcher AddClickableRow uses.
			SaUi.ClickCatcher(dial.labelsArea, CycleSolarTimeFormat);

			// The companion slot needs its own Vertical + ContentSizeFitter: a
			// plain Go() would never report its children's height back to dialCol
			// and would overlap the dial above. With them it collapses to zero
			// when empty and sizes correctly once a companion populates it.
			BuildWeatherSection(weatherHalf.transform);

			weatherHostGo = SaUi.Go("WeatherHost", weatherHalf.transform);
			SaUi.Vertical(weatherHostGo, 0, 0f);
			weatherHostGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			SaExtensionPoint.Raise(weatherHostGo.transform);

			BuildRows(dataCol.transform);
			// Divider, so the timeline does not run straight into the last row.
			SaUi.Divider(dataCol.transform);
			SaDial.BuildTimeline(dial, dataCol.transform);
		}

		private void BuildRows(Transform parent)
		{
			// Clock/countdown/alert block
			GameObject clockRow = SaUi.Go("Clock", parent);
			HorizontalLayoutGroup clockGroup = SaUi.Horizontal(clockRow, 0, 6);
			clockGroup.padding = RowPadding;
			Text clockMain = AddLabel(clockRow.transform, "clockMain", "-", 26, SaUi.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
			SaUi.Size(clockMain.gameObject, -1f, 30f, 1f);
			AddLabel(clockRow.transform, "clockSub", "-", 10, SaUi.TextDim, TextAnchor.MiddleRight);

			SaUi.Divider(parent);

			AddDataRow(parent, "#LOC_SA_row_ut", "ut");
			AddDataRow(parent, "#LOC_SA_row_kscTime", "kscTime");
			metRowGo = AddDataRow(parent, "#LOC_SA_row_met", "met");

			SaUi.Divider(parent);
			coordinatesRowGo = AddClickableRow(parent, "#LOC_SA_row_coordinates", "coordinates", CycleCoordUnit);
			AddDataRow(parent, "#LOC_SA_row_biome", "biome");
			// Belongs to the position block, under BIOME, not down with SUN/FLUX.
			terminatorRowGo = AddClickableRow(parent, "#LOC_SA_row_terminator", "terminator", CycleTerminatorUnit);

			// Surface-only, toggled in FixedUpdate's mode-change block.
			positionDividerGo = SaUi.Divider(parent);

			// The whole row is hidden rather than dashed out when the vessel is at
			// the star itself: a structural per-body fact, like EXT TEMP/PRESSURE,
			// not a transient numerical edge case.
			sunRowGo = AddDataRow(parent, "#LOC_SA_row_sun", "sun");
			AddDataRow(parent, "#LOC_SA_row_flux", "flux");
			// The always-visible row leads, the conditionally hidden one follows.
			AddClickableRow(parent, "#LOC_SA_row_hullTemp", "hullTemp", CycleTempUnit);
			// Same click action as HULL TEMP on purpose: one shared unit for both
			// rows, never one in °C and the other in K.
			extTempRowGo = AddClickableRow(parent, "#LOC_SA_row_temperature", "temperature", CycleTempUnit);
			// Shares EXT TEMP's visibility condition.
			pressureRowGo = AddClickableRow(parent, "#LOC_SA_row_pressure", "pressure", CyclePressureUnit);
			AddClickableRow(parent, "#LOC_SA_row_gravity", "gravity", CycleGravityUnit);
		}

		/// <summary>
		/// The WEATHER section in the dial column's lower half: a tinted icon with
		/// the state name beneath it, plus a small top-right slot a companion can
		/// drop a button into.
		///
		/// It collapses to nothing when there is no weather to report (see
		/// ApplyExtended), so an airless body — or an install with no EVE — loses
		/// the section rather than showing a dead label.
		/// </summary>
		private void BuildWeatherSection(Transform parent)
		{
			// The Image below is a new object on every rebuild while this field is
			// not: left stale, the "same path as last time" shortcut in
			// SetWeatherSection would skip assigning the sprite and the icon would
			// vanish after the first collapse toggle.
			lastWeatherIconPath = null;

			weatherSectionGo = SaUi.Go("WeatherSection", parent);
			SaUi.Vertical(weatherSectionGo, 0, 3f);
			weatherSectionGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			// No divider and no caption above the icon: it reads as weather on its
			// own, and the line was one more edge in an already busy column.
			// #LOC_SA_row_weather stays in the loc files, commented out, in case
			// the caption ever comes back.

			// The icon needs a box of its OWN size, not a full-width row: the
			// alert badge anchors to that box's bottom-left corner, and with
			// the column-wide default it would sit against the panel edge
			// instead of against the glyph.
			GameObject iconRow = SaUi.Go("IconRow", weatherSectionGo.transform);
			SaUi.Horizontal(iconRow, 0, 0f, TextAnchor.MiddleCenter);
			SaUi.Size(iconRow, -1f, WeatherIconSize);

			GameObject iconGo = SaUi.Go("Icon", iconRow.transform);
			SaUi.Size(iconGo, WeatherIconSize, WeatherIconSize);
			weatherIcon = iconGo.AddComponent<Image>();
			weatherIcon.preserveAspect = true;
			// Starts hidden: no state classified yet, and possibly no art
			// installed at all.
			weatherIcon.enabled = false;

			BuildWeatherBadge(iconGo.transform);

			weatherLabel = SaUi.Label(weatherSectionGo.transform, "-", 11, SaUi.Text, TextAnchor.MiddleCenter);
			// A flavor name ("EXPLODIUM RAIN") is far longer than "RAIN" and the
			// column is only 138px wide, so let it wrap rather than clip.
			weatherLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
			SaUi.Size(weatherLabel.gameObject, DialColWidth - 12f, -1f);
			ContentSizeFitter labelFitter = weatherLabel.gameObject.AddComponent<ContentSizeFitter>();
			labelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			// Forecast: a spacer standing in for a blank line, since a literal
			// "\n" would make the label's own height lie to the layout; then the
			// sentence itself, two points smaller and dim.
			weatherForecastSpacerGo = SaUi.Go("ForecastSpacer", weatherSectionGo.transform);
			SaUi.Size(weatherForecastSpacerGo, -1f, WeatherForecastSpacerHeight);
			weatherForecastSpacerGo.SetActive(false);

			weatherForecastLabel = SaUi.Label(weatherSectionGo.transform, "-", 9, SaUi.TextDim, TextAnchor.MiddleCenter);
			weatherForecastLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
			SaUi.Size(weatherForecastLabel.gameObject, DialColWidth - 12f, -1f);
			weatherForecastLabel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			weatherForecastLabel.gameObject.SetActive(false);

			// Companion slot: anchored to the section's top-right corner and
			// excluded from the layout group, so whatever an external DLL puts
			// here floats over the section instead of pushing the icon down, and
			// can be switched on and off without moving anything. The section
			// itself stays visible as UNKNOWN whenever the report is on (see
			// ApplyExtended), so the slot never needs to outlive it.
			GameObject corner = SaUi.Go("WeatherCorner", weatherSectionGo.transform);
			corner.AddComponent<LayoutElement>().ignoreLayout = true;
			RectTransform cornerRect = corner.GetComponent<RectTransform>();
			cornerRect.anchorMin = new Vector2(1f, 1f);
			cornerRect.anchorMax = new Vector2(1f, 1f);
			cornerRect.pivot = new Vector2(1f, 1f);
			cornerRect.anchoredPosition = new Vector2(-2f, -4f);
			cornerRect.sizeDelta = new Vector2(WeatherCornerSize, WeatherCornerSize);
			SaExtensionPoint.RaiseCorner(corner.transform);
		}

		/// <summary>
		/// The severity mark in the icon's bottom-left corner: a dedicated
		/// SA_weather_alert texture when one is installed, otherwise a bold "!"
		/// from SA's own font, so the feature works before the art exists.
		/// </summary>
		private void BuildWeatherBadge(Transform iconTransform)
		{
			GameObject badge = SaUi.Go("AlertBadge", iconTransform);
			badge.AddComponent<LayoutElement>().ignoreLayout = true;
			RectTransform rect = badge.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
			rect.pivot = new Vector2(0f, 0f);
			rect.anchoredPosition = new Vector2(-1f, -1f);
			rect.sizeDelta = new Vector2(WeatherBadgeSize, WeatherBadgeSize);

			Sprite sprite = SaWeatherIcons.Load(SaWeatherIcons.AlertPath);
			if (sprite != null)
			{
				weatherBadgeImage = badge.AddComponent<Image>();
				weatherBadgeImage.sprite = sprite;
				weatherBadgeImage.preserveAspect = true;
			}
			// Plain bold glyph from SA's own font, no outline: it fights the
			// icon's strokes when magnified but reads cleanly at the real 32px.
			// Always built, since even with an alert texture installed a locked
			// section's "?" is drawn from here.
			weatherBadgeText = SaUi.Label(badge.transform, "!", 13, SaUi.Warn,
				TextAnchor.MiddleCenter, FontStyle.Bold);
			SaUi.Stretch(weatherBadgeText.rectTransform);
			badge.SetActive(false);
		}

		private const float WeatherIconSize = 32f;
		/// <summary>The strip's own weather icon, smaller than the extended
		/// widget's: the strip row is 22px of content inside a 30px bar, the same
		/// size class as the mini-dial's icon box.</summary>
		private const float StripWeatherIconSize = 18f;
		private const float StripWeatherTempWidth = 62f;
		private const float WeatherForecastSpacerHeight = 6f;
		private const float WeatherBadgeSize = 13f;
		private const float WeatherCornerSize = 14f;

		// Left/right padding every row applies to itself, dataCol's own being
		// zero; dividers are built directly against dataCol with no row wrapper,
		// so they bleed to the true edges.
		private static readonly RectOffset RowPadding = new RectOffset(12, 12, 0, 0);

		private GameObject AddDataRow(Transform parent, string labelKey, string id)
		{
			GameObject row = SaUi.Go("Row_" + id, parent);
			HorizontalLayoutGroup rowGroup = SaUi.Horizontal(row, 0, 6f);
			rowGroup.padding = RowPadding;
			SaUi.Size(row, -1f, 16f);
			Text k = SaUi.Label(row.transform, Loc(labelKey), 10, SaUi.TextDim);
			SaUi.Size(k.gameObject, 100f, 14f);
			Text v = SaUi.Label(row.transform, "-", 10, SaUi.Text, TextAnchor.MiddleRight);
			SaUi.Size(v.gameObject, -1f, 14f, 1f);
			valueLabels["row_" + id] = v;
			// Stashed for rows whose LABEL needs a per-session update: only
			// "kscTime" today, whose timezone is appended once (see UpdateKscRow).
			valueLabels["row_" + id + "_label"] = k;
			return row;
		}

		private GameObject AddClickableRow(Transform parent, string labelKey, string id, UnityEngine.Events.UnityAction onClick)
		{
			GameObject row = AddDataRow(parent, labelKey, id);
			SaUi.ClickCatcher(row.transform, onClick);
			return row;
		}

		private Text AddLabel(Transform parent, string id, string text, int size, Color color,
			TextAnchor anchor, FontStyle style = FontStyle.Normal)
		{
			Text t = SaUi.Label(parent, text, size, color, anchor, style);
			valueLabels[id] = t;
			return t;
		}

		// ------------------------------------------------------------- footer

		private void BuildFooter()
		{
			RectTransform bar = SaUi.Bordered("Footer", shellHost, SaUi.HeaderBg, SaUi.PanelEdge);
			SaUi.Size(bar.gameObject, -1f, 26f);
			SaUi.Horizontal(bar.gameObject, 8, 8f);

			footerLeft = SaUi.Label(bar, "-", 9, SaUi.TextDim, TextAnchor.MiddleLeft);
			SaUi.Size(footerLeft.gameObject, -1f, 18f, 1f);
			footerRight = SaUi.Label(bar, "-", 9, SaUi.TextDim, TextAnchor.MiddleRight);
			SaUi.Size(footerRight.gameObject, -1f, 18f);
		}

		// -------------------------------------------------------------- strip

		private void BuildStrip()
		{
			RectTransform bar = SaUi.Bordered("Strip", shellHost, SaUi.Panel, SaUi.PanelEdge);
			SaUi.Size(bar.gameObject, -1f, 30f);
			SaUi.Horizontal(bar.gameObject, 8, 8f);

			// Mini-dial first (design doc §6.7).
			stripDial = SaDial.BuildStripIcon(bar);

			// Natural width, not a fixed slot: a fixed one leaves a gap after the
			// clock whenever the text comes out shorter.
			stripHot = SaUi.Label(bar, "-", 16, SaUi.Amber, TextAnchor.MiddleLeft);
			SaUi.Size(stripHot.gameObject, -1f, 22f);

			// Local date/Sol, Surface-only: the extended view's dateLine, toggled
			// active in ApplyStrip.
			stripDate = SaUi.Label(bar, "-", 10, SaUi.Cyan, TextAnchor.MiddleLeft);
			SaUi.Size(stripDate.gameObject, -1f, 22f);

			stripPhase = SaUi.Label(bar, "-", 10, SaUi.Text, TextAnchor.MiddleLeft);
			SaUi.Size(stripPhase.gameObject, -1f, 22f);

			// One dedicated spacer rather than flexibleWidth on stripPhase: every
			// element then sits snug against its neighbours, and this is the only
			// thing pushing tail and led to the right.
			GameObject stripSpacer = SaUi.Go("Spacer", bar);
			SaUi.Size(stripSpacer, 0f, -1f, 1f);

			// Weather icon + external temperature, after the spacer so a hidden
			// block leaves no gap and a shown one reads as its own group instead
			// of crowding the "next event" text. No state name and no severity
			// badge here; SaWeatherVisibility holds the show/hide/lock rule this
			// shares with the extended widget.
			stripWeatherGo = SaUi.Go("Weather", bar);
			SaUi.Horizontal(stripWeatherGo, 0, 4f, TextAnchor.MiddleCenter);
			SaUi.Size(stripWeatherGo, -1f, 22f);

			// Temperature first, icon last. Fixed width and right alignment so the
			// figure's right edge, and with it the icon, never shifts as digits
			// come and go; sized for the longest expected text.
			stripWeatherTemp = SaUi.Label(stripWeatherGo.transform, "-", 10, SaUi.Text, TextAnchor.MiddleRight);
			SaUi.Size(stripWeatherTemp.gameObject, StripWeatherTempWidth, 22f);

			GameObject stripWeatherIconGo = SaUi.Go("Icon", stripWeatherGo.transform);
			SaUi.Size(stripWeatherIconGo, StripWeatherIconSize, StripWeatherIconSize);
			stripWeatherIcon = stripWeatherIconGo.AddComponent<Image>();
			stripWeatherIcon.preserveAspect = true;
			stripWeatherIcon.enabled = false;

			// Starts hidden like every other conditional strip element: nothing is
			// classified yet on the first frame.
			stripWeatherGo.SetActive(false);

			stripTail = SaUi.Label(bar, "-", 10, SaUi.TextDim, TextAnchor.MiddleRight);
			SaUi.Size(stripTail.gameObject, -1f, 22f);

			// Led last here too, trailing the row.
			ledDotStrip = NewLedDot(bar);

			AttachDrag(bar);
			AttachDoubleClick(bar, ToggleCollapsed);
		}

		// --------------------------------------------------------------- tick

		/// <summary>
		/// Visibility, every rendered frame, since it has to feel instant: F2 and
		/// the pause menu both hide the canvas. PauseMenu.isOpen is a public
		/// static bool with no event behind it, so it is polled rather than hooked.
		///
		/// The orbit timer refreshes here rather than in FixedUpdate: physics ticks
		/// and rendered frames are not the same cadence, and the rendered frame is
		/// what the player perceives. The underlying state only changes at the
		/// physics rate wherever it is read, so reading it here loses nothing and
		/// can only show the new value sooner.
		/// </summary>
		private void Update()
		{
			bool visible = !hiddenByGameUI && !PauseMenu.isOpen;
			if (canvas != null && canvas.enabled != visible)
			{
				canvas.enabled = visible;
			}

			if (visible && lastMode == SaMode.Orbit && SaReadoutProvider.TryBuildOrbitTimerFast(FlightGlobals.ActiveVessel,
				out double fastTimeToNext, out bool fastIsEclipse, out bool fastIsSoiChange, out double fastPeriod, out bool fastBodyIsStar))
			{
				string countdown = "T−" + FormatDurationYDHMS(fastTimeToNext, shrinkWhenWide: true);
				if (SaPersist.Collapsed)
				{
					if (stripHot != null) stripHot.text = countdown;
				}
				else
				{
					SetLabel("clockMain", countdown, SaUi.Amber);
					SetLabel("clockSub", FormatOrbitClockSub(fastBodyIsStar, fastIsSoiChange, fastIsEclipse, fastPeriod), SaUi.TextDim);
				}
			}

			// SOLAR TIME's sunrise/sunset countdown, unthrottled for the same
			// reason as the orbit timer above: exact longitude changes
			// continuously. Only in extended Surface mode, only with the toggle on
			// and the row not overridden by the stellar-dive or near-pole cases —
			// cheap re-checks, not a full readout rebuild. The day percentage and
			// clock digits reuse the last throttled values.
			if (visible && !SaPersist.Collapsed && lastMode == SaMode.Surface && SaParams.ShowSolarTime && dial.subLabel != null)
			{
				Vessel v = FlightGlobals.ActiveVessel;
				if (v != null && v.mainBody != null && !v.mainBody.isStar
					&& Math.Abs(v.latitude) <= SaReadoutProvider.NearPoleLatitudeThresholdDeg
					&& SaReadoutProvider.TryBuildSolarCountdownFast(v, out double fastSolarCountdown, out bool fastSolarIsSunrise))
				{
					dial.subLabel.text = BuildSurfaceSubText(lastDayProgress01,
						fastSolarIsSunrise, fastSolarCountdown, fastSolarIsSunrise, fastSolarCountdown,
						lastSolarHour, lastSolarMinute, lastSolarSecond, lastEquationOfTimeSec, lastSolarDayLengthSec);
				}
			}
		}

		/// <summary>
		/// Data refresh, on FixedUpdate plus a 10 Hz accumulator: Planetarium.time,
		/// and therefore UT, only advances inside FixedUpdate. Sampling it from a
		/// rendered frame would re-read the same frozen value several times and
		/// then jump; sampling here looks at UT exactly when the game moved it.
		/// The accumulator caps the redraw at ~10/s, whatever the physics rate.
		/// </summary>
		private void FixedUpdate()
		{
			// The orbit timer's unthrottled refresh lives in Update(), not here.
			refreshAccumulator += Time.fixedDeltaTime;
			if (refreshAccumulator < RefreshInterval) return;
			refreshAccumulator = 0f;

			if (scaler != null)
			{
				scaler.scaleFactor = GameSettings.UI_SCALE * SaParams.UiScale;
			}

			Vessel vessel = FlightGlobals.ActiveVessel;
			SaReadout r = SaReadoutProvider.Build(vessel);
			if (!r.Valid) return;

			if (r.Mode != lastMode)
			{
				lastMode = r.Mode;
				if (!SaPersist.Collapsed && terminatorRowGo != null)
				{
					terminatorRowGo.SetActive(lastMode == SaMode.TidalLock);
				}
				// Surface-only, see the positionDividerGo field.
				if (!SaPersist.Collapsed && positionDividerGo != null)
				{
					positionDividerGo.SetActive(lastMode == SaMode.Surface);
				}
			}
			if (!SaPersist.Collapsed && metRowGo != null)
			{
				// Checked every refresh, not only on a mode change: the setting can
				// be flipped mid-flight while the window stays open.
				metRowGo.SetActive(SaParams.ShowMissionTime);
			}

			if (SaPersist.Collapsed) ApplyStrip(r); else ApplyExtended(r);
			ApplyLed(SaPersist.Collapsed ? ledDotStrip : ledDotExtended, r.SignalLevel);
		}

		private void ApplyLed(SaVectorDot dot, SaSignalLevel level)
		{
			if (dot == null) return;
			Color c;
			switch (level)
			{
				case SaSignalLevel.Green: c = SaUi.LedGreen; break;
				case SaSignalLevel.Yellow: c = SaUi.LedYellow; break;
				case SaSignalLevel.Red: c = SaUi.Danger; break;
				default: c = SaUi.LedOff; break;
			}
			dot.color = c;
		}

		/// <summary>
		/// The TZ suffix shows only in Surface mode on the home body; hidden is
		/// the default. Its value is cached once, the KSC's coordinates being
		/// session-stable, but the label text is rebuilt every refresh because it
		/// also depends on the current mode — a string concat at 10 Hz.
		/// </summary>
		private void UpdateKscRow(SaReadout r)
		{
			if (!r.KscTimeValid)
			{
				SetRow("kscTime", "-");
				return;
			}
			if (kscTimeZoneCached == null)
			{
				kscTimeZoneCached = r.KscTimeZoneIndex;
			}
			if (valueLabels.TryGetValue("row_kscTime_label", out Text kscLabel))
			{
				bool showTz = r.Mode == SaMode.Surface && r.IsHomeBody;
				kscLabel.text = showTz
					? "KSC (TZ" + (kscTimeZoneCached >= 0 ? "+" : "") + kscTimeZoneCached + ")"
					: "KSC";
			}
			SetRow("kscTime", string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", r.KscHour, r.KscMinute, r.KscSecond));
		}

		private void ApplyExtended(SaReadout r)
		{
			// A rich-text span dims the "SA //" prefix; the label's own colour,
			// set once at build time, covers the un-tagged remainder.
			titleLabel.text = "<color=#" + SaUi.TextDimHex + ">SA //</color> "
				+ r.BodyName.ToUpperInvariant() + " · " + r.VesselName;
			chipLabel.text = Loc(ChipKey(r.Mode));
			Color chipColor = r.Mode == SaMode.TidalLock ? SaUi.Danger : SaUi.Cyan;
			chipLabel.color = chipColor;
			chipBorderImage.color = chipColor;

			string phaseText, subText, tickLeft, tickMid, tickRight;
			BuildClockAndPhase(r, out phaseText, out subText, out tickLeft, out tickMid, out tickRight);
			SaDial.Update(dial, r, phaseText, subText, tickLeft, tickMid, tickRight);

			SetRow("ut", KSPUtil.dateTimeFormatter != null ? KSPUtil.dateTimeFormatter.PrintDateCompact(r.UT, true, true) : F(r.UT, "0"));
			UpdateKscRow(r);
			SetRow("met", "T+" + FormatMet(r.MissionTime));

			SetRow("coordinates", FormatCoordinates(r.Latitude, r.Longitude));
			// DMS is much wider than decimal, so it stacks onto two lines and grows
			// the row's height instead of overflowing sideways.
			if (coordinatesRowGo != null)
			{
				LayoutElement coordLe = coordinatesRowGo.GetComponent<LayoutElement>();
				if (coordLe != null)
				{
					coordLe.preferredHeight = SaPersist.CoordUnit == SaCoordUnit.Dms ? 28f : 16f;
				}
			}
			SetRow("biome", r.BiomeName);
			// The SUN row hides when the body IS the star: an elevation of the sun
			// means nothing from inside the light source.
			bool onStarSurface = r.Mode == SaMode.Surface && r.BodyIsStar;
			if (sunRowGo != null) sunRowGo.SetActive(!onStarSurface);
			if (!onStarSurface)
			{
				// Orbit has no azimuth, only the sub-vessel point (design doc §4.2).
				// Near a pole the azimuth alone goes to "—": a bearing means
				// nothing at the pole and is unstable close to it, while elevation
				// stays valid and merely shrinks toward 0°.
				SetRow("sun", r.Mode == SaMode.Orbit
					? "EL " + F(r.SunElevationDeg, "0.0") + "° (sub-v.)"
					: "EL " + F(r.SunElevationDeg, "0.0") + "° · AZ " + (r.NearPole ? "—" : F(r.SunAzimuthDeg, "0.0") + "°"));
			}
			SetRow("flux", F(r.SolarFluxWm2, "N0") + " W/m2");
			// Checked every tick rather than on a mode change: BodyHasAtmosphere
			// can change when the active vessel does, and the check is cheap.
			bool showAtmosphericRows = SaWeatherVisibility.ShowAtmosphericRows(r);
			if (extTempRowGo != null) extTempRowGo.SetActive(showAtmosphericRows);
			if (pressureRowGo != null) pressureRowGo.SetActive(showAtmosphericRows);
			// The companion slot follows the same rule: no clouds without air.
			if (weatherHostGo != null) weatherHostGo.SetActive(showAtmosphericRows);
			// The weather section is NOT tied to showAtmosphericRows: an airless
			// body can show weather from inside a plume, and the classifier
			// reports Unknown for anything less, so the section still hides itself
			// on an ordinary airless moon. With the Weather Report on and its
			// companion installed, Unknown is shown instead as a locked section,
			// so the companion's corner button stays reachable exactly where "SA
			// shows nothing here" is the report worth sending. SA reads only its
			// own setting here; consent is the companion's business.
			SaWeatherVisibility.Level weatherVis = SaWeatherVisibility.Compute(r);
			bool showWeather = weatherVis != SaWeatherVisibility.Level.Hidden;
			if (weatherSectionGo != null) weatherSectionGo.SetActive(showWeather);
			// A value not yet measured on this body shows "???" in dim text, the
			// row staying so the layout does not jump. "???" rather than the "—"
			// used elsewhere: that dash means "no such value here", this one means
			// "there is one, and you do not know it".
			if (showAtmosphericRows)
			{
				if (r.ExtTempUnlocked) SetTemperatureRow(r.ExternalTemperatureK);
				else SetRow("temperature", Loc("#LOC_SA_val_gated"), SaUi.TextDim);
				if (r.PressureUnlocked) SetRow("pressure", FormatPressure(r.PressureKPa), SaUi.Text);
				else SetRow("pressure", Loc("#LOC_SA_val_gated"), SaUi.TextDim);
			}
			if (showWeather)
			{
				// Both locked cases — the science gate, and an unknown sky kept on
				// screen for the report — share the padlock face and carry no badge.
				switch (weatherVis)
				{
					case SaWeatherVisibility.Level.LockedNoReading:
					case SaWeatherVisibility.Level.LockedGated: SetWeatherLocked(); break;
					default: SetWeatherSection(r.Weather, r.BodyNameInternal, r.SunElevationDeg, r.UT); break;
				}
			}
			SetHullTemperatureRow(r.HullTempK, r.HullTempWorstRatio);
			SetRow("gravity", FormatGravity(r, out Color gravityColor), gravityColor);
			if (r.Mode == SaMode.TidalLock)
			{
				SetRow("terminator", FormatTerminator(r.TerminatorDistanceKm, r.TerminatorDistanceDeg, r.TerminatorToEast), SaUi.Cyan);
			}

			string lockedBadge = r.BodyTidallyLocked
				? " · <color=" + LockedBadgeColorHex + ">" + Loc("#LOC_SA_val_locked") + "</color>"
				: "";
			// A star has no solar day relative to itself, so the segment is omitted
			// when orbiting one directly. The home body needs its own formatter:
			// fmt.Day is calibrated to equal its solar day exactly, so the global
			// duration format would render a circular "1d 00h 00m".
			string solarDaySegment = r.BodyIsStar
				? ""
				: " · " + Loc("#LOC_SA_footer_solarDay") + " "
					+ (r.IsStarLocked ? Loc("#LOC_SA_val_infinite")
						: r.IsHomeBody ? FormatHomeSolarDayDuration(r.SolarDayLengthSec) : FormatDurationYDHMS(r.SolarDayLengthSec));
			footerLeft.text = BuildFooterChain(r) + solarDaySegment + lockedBadge;

			if (r.Mode == SaMode.Orbit)
			{
				footerRight.text = "ALT " + F(r.Altitude / 1000.0, "N1") + " km";
			}
			else
			{
				string agl = r.AltitudeAglValid ? " · AGL " + F(r.AltitudeAglM, "N0") + " m" : "";
				footerRight.text = "ASL " + F(r.Altitude, "N0") + " m" + agl;
			}
		}

		private static string BuildFooterChain(SaReadout r)
		{
			if (r.BodyChain == null || r.BodyChain.Length == 0)
			{
				return r.BodyName.ToUpperInvariant();
			}
			string[] parts = new string[r.BodyChain.Length];
			for (int i = 0; i < r.BodyChain.Length; i++)
			{
				// BodyChain holds raw CelestialBody refs, bypassing the already
				// cleaned r.BodyName/StarName, so the gender tag must be stripped
				// here too (see SaReadoutProvider.CleanDisplayName).
				parts[i] = SaReadoutProvider.CleanDisplayName(r.BodyChain[i].displayName).ToUpperInvariant();
			}
			return string.Join(" // ", parts);
		}

		private void BuildClockAndPhase(SaReadout r, out string phaseText, out string subText,
			out string tickLeft, out string tickMid, out string tickRight)
		{
			switch (r.Mode)
			{
				case SaMode.Surface:
					// Stellar dive: the body IS the star, with no external sun to
					// derive a local time or day cycle from. An override inside
					// Surface mode rather than a mode of its own, so the rows
					// keyed on Mode == Surface keep applying.
					if (r.BodyIsStar)
					{
						SetLabel("clockMain", Loc("#LOC_SA_val_stellarDive"), SaUi.Danger);
						SetLabel("clockSub", Loc("#LOC_SA_val_noLocalTime"), SaUi.TextDim);
						phaseText = "—";
						subText = "";
						tickLeft = "—"; tickMid = ""; tickRight = "—";
						break;
					}
					// Near the pole: neutral colour, nothing being dangerous here.
					// "POLAR ZONE" says why the clock cannot be trusted, and
					// "MIDNIGHT SUN" on the phase line says what is happening —
					// the sun still technically sets, but its elevation swing
					// shrinks to nothing this close to the pole.
					if (r.NearPole)
					{
						SetLabel("clockMain", Loc("#LOC_SA_val_polarZone"), SaUi.TextDim);
						SetLabel("clockSub", Loc("#LOC_SA_val_noLocalTime"), SaUi.TextDim);
						phaseText = Loc("#LOC_SA_val_midnightSun");
						subText = "";
						tickLeft = "—"; tickMid = ""; tickRight = "—";
						break;
					}
					SetLabel("clockMain", string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", r.LocalHour, r.LocalMinute, r.LocalSecond), SaUi.Amber);
					// Sol means nothing extra on the home body (design doc §3.4), so
					// this line shows the true LOCAL calendar date there, shifted
					// by the current zone's offset before formatting. The UT row
					// keeps its own date: the two are distinct concepts even when
					// they agree in value.
					string dateLine = r.IsHomeBody ? FormatLocalDate(r) : Loc("#LOC_SA_val_sol") + " " + r.Sol;
					string dateLineColored = string.IsNullOrEmpty(dateLine) ? "" : "<color=#" + SaUi.CyanHex + ">" + dateLine + "</color>";
					SetLabel("clockSub", Loc("#LOC_SA_val_localTime") + " · TZ" + (r.TimeZoneIndex >= 0 ? "+" : "") + r.TimeZoneIndex
						+ "\n" + dateLineColored, SaUi.TextDim);
					phaseText = Loc(PhaseKeySurface(r.PhaseSurface, r.BodyHasAtmosphere));
					// Two or three lines, the arrow leading the countdown: "day 64%"
					// / "↓ sunset 2h39m" / optionally "SOLAR 12:34:56". The pieces
					// Update()'s fast path cannot recompute per frame are cached
					// below.
					subText = BuildSurfaceSubText(r.DayProgress01, r.NextEventIsSunrise, r.TimeToNextEventSec,
						r.SolarNextEventIsSunrise, r.SolarTimeToNextEventSec,
						r.SolarHour, r.SolarMinute, r.SolarSecond, r.EquationOfTimeSec, r.SolarDayLengthSec);
					lastDayProgress01 = r.DayProgress01;
					lastSolarHour = r.SolarHour;
					lastSolarMinute = r.SolarMinute;
					lastSolarSecond = r.SolarSecond;
					lastEquationOfTimeSec = r.EquationOfTimeSec;
					lastSolarDayLengthSec = r.SolarDayLengthSec;
					int nHours = BodyClock.LocalHoursPerDay;
					tickLeft = "00"; tickMid = (nHours / 2).ToString("00", CultureInfo.InvariantCulture); tickRight = nHours.ToString("00", CultureInfo.InvariantCulture);
					break;
				case SaMode.Orbit:
					SetLabel("clockMain", "T−" + FormatDurationYDHMS(r.TimeToNextEventSec, shrinkWhenWide: true), SaUi.Amber);
					SetLabel("clockSub", FormatOrbitClockSub(r.BodyIsStar, r.NextOrbitEventIsSoiChange, r.NextOrbitEventIsEclipse, r.OrbitPeriodSec), SaUi.TextDim);
					phaseText = Loc(PhaseKeyOrbit(r.PhaseOrbit));
					subText = Loc("#LOC_SA_val_orbitLit") + " " + FormatLitFraction(r.OrbitLitFraction01, r.BodyIsStar);
					tickLeft = Loc("#LOC_SA_val_eclipse"); tickMid = ""; tickRight = Loc("#LOC_SA_val_light");
					break;
				default:
					SetLabel("clockMain", Loc("#LOC_SA_val_tidalLock"), SaUi.Danger);
					// Binary day-side/night-side here; the 3-way phase name is still
					// shown under the dial through phaseText below.
					bool isDaySide = Math.Abs(r.TidalLockHourAngleDeg) < 90.0;
					string sideText = "<color=#" + SaUi.CyanHex + ">" + Loc(isDaySide ? "#LOC_SA_val_daySide" : "#LOC_SA_val_nightSide") + "</color>";
					SetLabel("clockSub", Loc("#LOC_SA_val_noLocalTime") + "\n" + sideText, SaUi.TextDim);
					phaseText = Loc(PhaseKeyTidalLock(r.PhaseTidalLock));
					subText = Loc("#LOC_SA_val_sunFixed") + " EL " + F(r.SunElevationDeg, "0.0") + "°";
					tickLeft = Loc("#LOC_SA_val_subsolar"); tickMid = Loc("#LOC_SA_phase_terminatorLock"); tickRight = Loc("#LOC_SA_val_antisolar");
					break;
			}
		}

		/// <summary>Terms shown for MET specifically: always down to the second,
		/// unlike every other timer's 3-term cap.</summary>
		private const int MetMaxTerms = 5;

		/// <summary>
		/// MET's value, in either of two formats: the default all-letters timer,
		/// or a stockalike hybrid with letters for year and day and a colon
		/// HH:MM:SS below ("1y 23d 03:14:09"). Year and day are omitted when zero,
		/// so a fresh launch does not read "0y 0d 00:14:09".
		/// </summary>
		private static string FormatMet(double seconds)
		{
			if (!SaParams.MetStockalikeFormat) return FormatDurationYDHMS(seconds, MetMaxTerms);

			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 1e12) return Loc("#LOC_SA_val_infinite");
			seconds = Math.Max(0.0, seconds);
			IDateTimeFormatter fmt = KSPUtil.dateTimeFormatter;
			if (fmt == null || fmt.Minute <= 0 || fmt.Hour <= 0 || fmt.Day <= 0 || fmt.Year <= 0)
				return F(seconds, "0") + " s";

			long total = (long)seconds;
			long y = total / fmt.Year;
			long remY = total % fmt.Year;
			long d = remY / fmt.Day;
			long remD = remY % fmt.Day;
			long h = remD / fmt.Hour;
			long remH = remD % fmt.Hour;
			long m = remH / fmt.Minute;
			long s = remH % fmt.Minute;

			string prefix = "";
			if (y > 0) prefix = y + TimeUnitYear + " " + d + TimeUnitDay + " ";
			else if (d > 0) prefix = d + TimeUnitDay + " ";
			return prefix + string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", h, m, s);
		}

		/// <summary>
		/// The global timer format: every term carries a localized unit LETTER,
		/// never a bare colon pair or zero-padding — "32y 311d 11h", "11h 39m 2s",
		/// "2s". It shows the `maxTerms` largest terms from wherever the magnitude
		/// begins, so a short duration reads "12m 34s" rather than "00h 12m 34s".
		/// Tier boundaries come from dateTimeFormatter, not a hardcoded 3600/86400,
		/// so they follow the active clock.
		///
		/// `shrinkWhenWide` drops one term from the cap whenever the leading value
		/// reaches two digits, whatever its unit: the big 26px countdown would
		/// otherwise overlap its row-mate, and the cause is character width rather
		/// than any particular unit. Only the "T−" call sites pass it.
		/// </summary>
		private static string FormatDurationYDHMS(double seconds, int maxTerms = 3, bool shrinkWhenWide = false)
		{
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 1e12) return Loc("#LOC_SA_val_infinite");
			seconds = Math.Max(0.0, seconds);
			IDateTimeFormatter fmt = KSPUtil.dateTimeFormatter;
			if (fmt == null || fmt.Minute <= 0 || fmt.Hour <= 0 || fmt.Day <= 0 || fmt.Year <= 0)
				return F(seconds, "0") + " s";

			long total = (long)seconds;
			long y = total / fmt.Year;
			long remY = total % fmt.Year;
			long d = remY / fmt.Day;
			long remD = remY % fmt.Day;
			long h = remD / fmt.Hour;
			long remH = remD % fmt.Hour;
			long m = remH / fmt.Minute;
			long s = remH % fmt.Minute;

			return JoinDurationTerms(new[] { y, d, h, m, s },
				new[] { TimeUnitYear, TimeUnitDay, TimeUnitHour, TimeUnitMinute, TimeUnitSecond }, maxTerms, shrinkWhenWide);
		}

		/// <summary>
		/// Home-body exception for the footer's solar-day value: the calendar's Day
		/// unit IS the home body's own solar day by calibration, so the global
		/// format would render a circular "1d 00h 00m". Skips the day and year
		/// tiers and always shows hours and minutes.
		/// </summary>
		private static string FormatHomeSolarDayDuration(double seconds)
		{
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 1e12) return Loc("#LOC_SA_val_infinite");
			seconds = Math.Max(0.0, seconds);
			IDateTimeFormatter fmt = KSPUtil.dateTimeFormatter;
			if (fmt == null || fmt.Minute <= 0 || fmt.Hour <= 0) return F(seconds, "0") + " s";

			long h = (long)seconds / fmt.Hour;
			long remH = (long)seconds % fmt.Hour;
			long m = remH / fmt.Minute;

			return JoinDurationTerms(new[] { h, m }, new[] { TimeUnitHour, TimeUnitMinute }, 2);
		}

		/// <summary>
		/// "Proportional local" timer: one local day is the TARGET body's own solar
		/// day and one local hour is that divided by BodyClock.LocalHoursPerDay,
		/// the same global hour count LOCAL TIME's digits use. Not the global
		/// calendar's tiers, which measure the home body's day and mean nothing on
		/// a body whose day is a different length. There is no local-year tier: an
		/// equation-of-time offset even an hour wide is already anomalous.
		/// </summary>
		private static string FormatLocalDurationYDHMS(double seconds, double solarDayLengthSec, int maxTerms = 3)
		{
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 1e12) return Loc("#LOC_SA_val_infinite");
			seconds = Math.Max(0.0, seconds);
			if (solarDayLengthSec <= 0.0) return F(seconds, "0") + " s";

			int n = Math.Max(1, BodyClock.LocalHoursPerDay);
			double hourLen = solarDayLengthSec / n;
			double minuteLen = hourLen / 60.0;
			double secondLen = minuteLen / 60.0;

			long d = (long)(seconds / solarDayLengthSec);
			double remAfterDay = seconds - d * solarDayLengthSec;
			long h = (long)(remAfterDay / hourLen);
			double remAfterHour = remAfterDay - h * hourLen;
			long m = (long)(remAfterHour / minuteLen);
			double remAfterMinute = remAfterHour - m * minuteLen;
			long s = (long)Math.Round(remAfterMinute / secondLen);
			// Rounding the last term can carry into the next at a boundary
			// (59.6s -> 60): a cheap cascade, cosmetic only.
			if (s >= 60) { s = 0; m++; }
			if (m >= 60) { m = 0; h++; }
			if (h >= n) { h = 0; d++; }

			return JoinDurationTerms(new[] { d, h, m, s },
				new[] { TimeUnitDay, TimeUnitHour, TimeUnitMinute, TimeUnitSecond }, maxTerms);
		}

		/// <summary>
		/// Shared term-joiner for both timer formatters: finds the largest non-zero
		/// unit and shows up to `maxTerms` from there down. No zero-padding, the
		/// letter suffix already disambiguating each field where a colon pair
		/// would not — padding only added width, enough to clip a long countdown
		/// against the value beside it. `shrinkWhenWide` drops one more term when
		/// the leading value reaches two digits.
		/// </summary>
		private static string JoinDurationTerms(long[] values, string[] symbols, int maxTerms, bool shrinkWhenWide = false)
		{
			int topIdx = values.Length - 1;
			for (int i = 0; i < values.Length; i++)
			{
				if (values[i] > 0) { topIdx = i; break; }
			}

			int effectiveMaxTerms = (shrinkWhenWide && values[topIdx] >= 10) ? Math.Max(1, maxTerms - 1) : maxTerms;
			int lastIdx = Math.Min(values.Length - 1, topIdx + effectiveMaxTerms - 1);
			string result = "";
			for (int i = topIdx; i <= lastIdx; i++)
			{
				if (i > topIdx) result += " ";
				result += values[i].ToString(CultureInfo.InvariantCulture) + symbols[i];
			}
			return result;
		}

		/// <summary>
		/// Unit-letter symbols for duration formatting, taken from the stock
		/// #autoLOC_600231x keys the game's own PrintTime uses, so a translated
		/// game shows its own letters rather than hardcoded English ones. Stock
		/// keys are right with or without Kronometer, which changes how long an
		/// hour is but never the word for it. Falls back to SA's own
		/// #LOC_SA_unit_* keys when a stock key does not resolve.
		/// </summary>
		private static string TimeUnitYear => AutoLocOrFallback("#autoLOC_6002321", "#LOC_SA_unit_year");
		private static string TimeUnitDay => AutoLocOrFallback("#autoLOC_6002320", "#LOC_SA_unit_day");
		private static string TimeUnitHour => AutoLocOrFallback("#autoLOC_6002319", "#LOC_SA_unit_hour");
		private static string TimeUnitMinute => AutoLocOrFallback("#autoLOC_6002318", "#LOC_SA_unit_minute");
		private static string TimeUnitSecond => AutoLocOrFallback("#autoLOC_6002317", "#LOC_SA_unit_second");

		private static string AutoLocOrFallback(string autoLocKey, string fallbackLocKey)
		{
			string s = Localizer.Format(autoLocKey);
			return s == autoLocKey ? Loc(fallbackLocKey) : s;
		}

		/// <summary>
		/// Orbit mode's clock-sub text, shared by the throttled BuildClockAndPhase
		/// and the unthrottled orbit-timer refresh so neither can disagree on
		/// formatting. Priority: an imminent SoI change is the most time-critical
		/// fact and wins even over BodyIsStar; star-centric comes next, there
		/// being no eclipse geometry around the light source; otherwise the
		/// ordinary eclipse/light countdown.
		/// </summary>
		private static string FormatOrbitClockSub(bool bodyIsStar, bool nextEventIsSoiChange, bool nextEventIsEclipse, double orbitPeriodSec)
		{
			string nextLine = nextEventIsSoiChange
				? Loc("#LOC_SA_val_soiChange")
				: bodyIsStar
					? Loc("#LOC_SA_val_starCentric")
					: Loc("#LOC_SA_val_next") + " " + (nextEventIsEclipse ? Loc("#LOC_SA_val_eclipse") : Loc("#LOC_SA_val_light"));
			return nextLine + "\n<color=#" + SaUi.CyanHex + ">" + Loc("#LOC_SA_val_period") + " " + FormatDurationYDHMS(orbitPeriodSec) + "</color>";
		}

		/// <summary>
		/// Orbit lit-fraction display. On a high orbit the shadow cone subtends so
		/// small an angle that the percentage rounds to 100 while a real eclipse
		/// still exists and can last hours: correct, but misleading. Such a value
		/// shows ">99%" instead, while the star-orbiting case, genuinely and
		/// exactly full, keeps a plain "100%".
		/// </summary>
		private static string FormatLitFraction(double litFraction01, bool bodyIsStar)
		{
			bool nearFullNotExact = !bodyIsStar && litFraction01 < 1.0 && litFraction01 >= 0.995;
			return nearFullNotExact ? Loc("#LOC_SA_val_orbitLitNearFull") : F(litFraction01 * 100.0, "0") + "%";
		}

		/// <summary>
		/// Surface mode's dial sub-label: day percentage and countdown on two
		/// lines, plus SOLAR TIME as an optional third. Shared by the throttled
		/// BuildClockAndPhase and the unthrottled per-frame countdown refresh,
		/// like FormatOrbitClockSub. With SOLAR TIME on, the countdown switches
		/// from the zone-quantized value to the exact-longitude one, while the
		/// day percentage stays zone-based, being LOCAL TIME's own fraction.
		/// </summary>
		private static string BuildSurfaceSubText(double dayProgress01,
			bool nextEventIsSunrise, double timeToNextEventSec,
			bool solarNextEventIsSunrise, double solarTimeToNextEventSec,
			int solarHour, int solarMinute, int solarSecond, double equationOfTimeSec, double solarDayLengthSec)
		{
			bool showSolar = SaParams.ShowSolarTime;
			bool useSunrise = showSolar ? solarNextEventIsSunrise : nextEventIsSunrise;
			double countdownSec = showSolar ? solarTimeToNextEventSec : timeToNextEventSec;

			// Same formatter as every other "time to event" field in the panel,
			// rather than stock's own PrintTimeCompact convention.
			string text = Loc("#LOC_SA_val_dayProgress") + " " + F(dayProgress01 * 100.0, "0") + "%\n"
				+ (useSunrise ? "↑" : "↓") + " "
				+ (useSunrise ? Loc("#LOC_SA_val_sunrise") : Loc("#LOC_SA_val_sunset")) + " " + FormatDurationYDHMS(countdownSec);

			if (showSolar)
			{
				// The label cycles with the value, because the two formats are
				// DIFFERENT quantities — a time of day and a signed offset — not
				// two views of one, unlike every other cyclable field here. A
				// fixed prefix on both invited reading EqT as "zone time minus
				// solar time", which it is not. "LAT" (Local Apparent Time) is the
				// standard term for what a sundial shows, paired with "EQT" the
				// way LMT and EoT are paired in that literature.
				bool isClock = SaPersist.SolarTimeFormat == SaSolarTimeFormat.Clock;
				string prefix = Loc(isClock ? "#LOC_SA_val_solarTimePrefix" : "#LOC_SA_val_eqtPrefix");
				string line3 = isClock
					? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", solarHour, solarMinute, solarSecond)
					: FormatEquationOfTime(equationOfTimeSec, solarDayLengthSec);
				text += "\n<color=#" + SaUi.CyanHex + ">" + prefix + " " + line3 + "</color>";
			}
			return text;
		}

		/// <summary>
		/// SOLAR TIME's second click-cycled format: signed, in the proportional
		/// local units of <see cref="FormatLocalDurationYDHMS"/> rather than the
		/// global calendar's, which measures the home body's day and means nothing
		/// on a body whose day is a different length.
		/// </summary>
		private static string FormatEquationOfTime(double seconds, double solarDayLengthSec)
		{
			string sign = seconds < 0.0 ? "-" : "+";
			return sign + FormatLocalDurationYDHMS(Math.Abs(seconds), solarDayLengthSec);
		}

		private void ApplyStrip(SaReadout r)
		{
			SaDial.UpdateStripIcon(stripDial, r);
			// Surface-only, and hidden on a stellar dive or near a pole: neither
			// has a reliable local date to show.
			stripDate.gameObject.SetActive(r.Mode == SaMode.Surface && !r.BodyIsStar && !r.NearPole);
			switch (r.Mode)
			{
				case SaMode.Surface:
					// Stellar dive: see BuildClockAndPhase's matching branch.
					if (r.BodyIsStar)
					{
						stripHot.text = Loc("#LOC_SA_val_stellarDive");
						stripHot.color = SaUi.Danger;
						stripPhase.text = "—";
						stripTail.text = "";
						break;
					}
					// Near the pole: neutral colour, not Danger — unreliable
					// rather than hazardous.
					if (r.NearPole)
					{
						stripHot.text = Loc("#LOC_SA_val_polarZone");
						stripHot.color = SaUi.TextDim;
						stripPhase.text = Loc("#LOC_SA_val_midnightSun").ToUpperInvariant();
						stripTail.text = "";
						break;
					}
					stripHot.text = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", r.LocalHour, r.LocalMinute, r.LocalSecond);
					stripHot.color = SaUi.Amber;
					stripDate.text = r.IsHomeBody ? FormatLocalDate(r) : Loc("#LOC_SA_val_sol") + " " + r.Sol;
					stripPhase.text = Loc(PhaseKeySurface(r.PhaseSurface, r.BodyHasAtmosphere)).ToUpperInvariant();
					stripTail.text = (r.NextEventIsSunrise ? "↑" : "↓") + " " + FormatDurationYDHMS(r.TimeToNextEventSec);
					break;
				case SaMode.Orbit:
					stripHot.text = "T−" + FormatDurationYDHMS(r.TimeToNextEventSec, shrinkWhenWide: true);
					stripHot.color = SaUi.Amber;
					// Both words always shown in a fixed order, the current phase
					// bright and the other dim, rather than swapping which single
					// word appears.
					{
						bool eclipseActive = r.PhaseOrbit == SaPhaseOrbit.Eclipse;
						string eclipseWord = Loc("#LOC_SA_phase_eclipse").ToUpperInvariant();
						string sunlitWord = Loc("#LOC_SA_phase_sunlit").ToUpperInvariant();
						string activeHex = eclipseActive ? SaUi.TextHex : SaUi.TextDimHex;
						string dimHex = eclipseActive ? SaUi.TextDimHex : SaUi.TextHex;
						stripPhase.text = "<color=#" + activeHex + ">" + eclipseWord + "</color> <color=#" + dimHex + ">" + sunlitWord + "</color>";
					}
					stripTail.text = Loc("#LOC_SA_val_lightLower") + " " + FormatLitFraction(r.OrbitLitFraction01, r.BodyIsStar);
					break;
				default:
					stripHot.text = Loc("#LOC_SA_val_tidalLock");
					stripHot.color = SaUi.Danger;
					stripPhase.text = Loc(PhaseKeyTidalLock(r.PhaseTidalLock)).ToUpperInvariant();
					stripTail.text = "<color=#" + SaUi.CyanHex + ">" + Loc("#LOC_SA_val_termShort") + " "
						+ FormatTerminator(r.TerminatorDistanceKm, r.TerminatorDistanceDeg, r.TerminatorToEast) + "</color>";
					break;
			}
			ApplyStripWeather(r);
		}

		/// <summary>
		/// The strip's weather icon and external temperature, on the same
		/// SaWeatherVisibility the extended widget uses so the two views can never
		/// show it in different situations. No state name — the icon carries it —
		/// and no severity badge: just the glyph, in the widget's neutral grey
		/// when unlocked and its dim colour when not. The locked icon is a
		/// padlock, so no separate mark is needed to say so.
		/// </summary>
		private void ApplyStripWeather(SaReadout r)
		{
			SaWeatherVisibility.Level vis = SaWeatherVisibility.Compute(r);
			bool show = vis != SaWeatherVisibility.Level.Hidden;
			if (stripWeatherGo != null) stripWeatherGo.SetActive(show);
			if (!show) return;

			string iconPath;
			Color iconColor;
			if (vis == SaWeatherVisibility.Level.Shown)
			{
				bool isNight = r.SunElevationDeg < 0.0;
				// Flavor still overrides the icon here, through the same lookup
				// the widget uses; the name is discarded, the strip having no
				// label for it.
				SaWeatherFlavor.Resolve(r.BodyNameInternal, r.Weather.State, isNight, null,
					SaWeatherIcons.PathFor(r.Weather.State, isNight), out _, out iconPath);
				iconColor = SaUi.Text;
			}
			else
			{
				iconPath = SaWeatherIcons.LockedPath;
				iconColor = SaUi.TextDim;
			}
			if (stripWeatherIcon != null)
			{
				Sprite sprite = SaWeatherIcons.Load(iconPath);
				stripWeatherIcon.sprite = sprite;
				stripWeatherIcon.enabled = sprite != null;
				stripWeatherIcon.color = iconColor;
			}

			// Same rule as the extended EXT TEMP row: an airless body showing
			// weather only from inside a plume has no ambient temperature to
			// speak of, so the icon stands alone there.
			bool showTemp = SaWeatherVisibility.ShowAtmosphericRows(r);
			if (stripWeatherTemp != null)
			{
				stripWeatherTemp.gameObject.SetActive(showTemp);
				if (showTemp)
				{
					if (r.ExtTempUnlocked)
					{
						FormatExtTemp(r.ExternalTemperatureK, out string text, out Color c);
						stripWeatherTemp.text = text;
						stripWeatherTemp.color = c;
					}
					else
					{
						stripWeatherTemp.text = Loc("#LOC_SA_val_gated");
						stripWeatherTemp.color = SaUi.TextDim;
					}
				}
			}
		}

		// ------------------------------------------------------------- units

		private void CycleTempUnit()
		{
			SaPersist.TempUnit = SaPersist.TempUnit == SaTempUnit.Celsius ? SaTempUnit.Kelvin : SaTempUnit.Celsius;
			SaPersist.Save();
		}

		private void CyclePressureUnit()
		{
			SaPersist.PressureUnit = SaPersist.PressureUnit == SaPressureUnit.Kpa ? SaPressureUnit.Atm : SaPressureUnit.Kpa;
			SaPersist.Save();
		}

		private void CycleTerminatorUnit()
		{
			SaPersist.TerminatorUnit = SaPersist.TerminatorUnit == SaTerminatorUnit.Km ? SaTerminatorUnit.Deg : SaTerminatorUnit.Km;
			SaPersist.Save();
		}

		private void CycleGravityUnit()
		{
			SaPersist.GravityUnit = SaPersist.GravityUnit == SaGravityUnit.G ? SaGravityUnit.Mps2 : SaGravityUnit.G;
			SaPersist.Save();
		}

		private void CycleCoordUnit()
		{
			SaPersist.CoordUnit = SaPersist.CoordUnit == SaCoordUnit.Decimal ? SaCoordUnit.Dms : SaCoordUnit.Decimal;
			SaPersist.Save();
		}

		/// <summary>
		/// A no-op while SOLAR TIME is not shown: the click-catcher covers the
		/// whole dial labels area in every mode, so a click there would otherwise
		/// flip a preference silently, with nothing on screen to show for it.
		/// </summary>
		private void CycleSolarTimeFormat()
		{
			if (!SaParams.ShowSolarTime) return;
			SaPersist.SolarTimeFormat = SaPersist.SolarTimeFormat == SaSolarTimeFormat.Clock
				? SaSolarTimeFormat.EquationOfTime
				: SaSolarTimeFormat.Clock;
			SaPersist.Save();
		}

		/// <summary>EXT TEMP, in three tiers: cyan cold, neutral, warn, danger.
		/// Absolute thresholds make sense here, this being the one true ambient
		/// reading rather than a part-specific value.</summary>
		private void SetTemperatureRow(double kelvin)
		{
			FormatExtTemp(kelvin, out string text, out Color c);
			SetRow("temperature", text, c);
		}

		/// <summary>
		/// The text and colour SetTemperatureRow shows, pulled out so the strip's
		/// weather block can show the same figure without a second copy of the
		/// thresholds to drift out of sync.
		/// </summary>
		private static void FormatExtTemp(double kelvin, out string text, out Color color)
		{
			double celsius = kelvin - 273.15;
			// Thousands separator, as FLUX has: HULL TEMP routinely hits four
			// figures on reentry, where "1500.0" reads slower than "1,500.0".
			text = SaPersist.TempUnit == SaTempUnit.Kelvin
				? F(kelvin, "N1") + " K"
				: F(celsius, "N1") + " °C";

			color = SaUi.Text;
			if (celsius < LowTempAlertC) color = SaUi.Cyan;
			else if (celsius > VeryHighTempAlertC) color = SaUi.Danger;
			else if (celsius > HighTempAlertC) color = SaUi.Warn;
		}

		/// <summary>HULL TEMP: the value is the thermal-mass-weighted average, but
		/// the COLOUR comes from worstRatio, the single hottest part's T/maxTemp,
		/// so one part near its limit shows red while the average still looks
		/// comfortable. The cold side stays absolute.</summary>
		private void SetHullTemperatureRow(double kelvin, double worstRatio)
		{
			double celsius = kelvin - 273.15;
			string text = SaPersist.TempUnit == SaTempUnit.Kelvin
				? F(kelvin, "N1") + " K"
				: F(celsius, "N1") + " °C";

			Color c = SaUi.Text;
			if (celsius < LowTempAlertC) c = SaUi.Cyan;
			else if (worstRatio > HullDangerRatio) c = SaUi.Danger;
			else if (worstRatio > HullWarnRatio) c = SaUi.Warn;
			SetRow("hullTemp", text, c);
		}

		/// <summary>
		/// Paints the weather section. Colour carries the same meaning as
		/// everywhere else in the panel: dim text for "nothing going on", plain
		/// text for cloud, cyan for anything falling, danger red for the one
		/// condition that can actually hurt a vessel on the ground.
		///
		/// Body name and sun elevation come in because both can change the
		/// presentation without the state itself changing — a flavor entry
		/// renames per body, and Clear/Cloudy have optional night icons.
		/// </summary>
		private void SetWeatherSection(SaWeatherReadout weather, string bodyName, double sunElevationDeg, double ut)
		{
			SaWeatherState state = weather.State;
			string key;
			switch (state)
			{
				case SaWeatherState.Cloudy: key = "#LOC_SA_weather_cloudy"; break;
				case SaWeatherState.Fog: key = "#LOC_SA_weather_fog"; break;
				case SaWeatherState.Rain: key = "#LOC_SA_weather_rain"; break;
				case SaWeatherState.Snow: key = "#LOC_SA_weather_snow"; break;
				case SaWeatherState.Thunderstorm: key = "#LOC_SA_weather_thunderstorm"; break;
				case SaWeatherState.DustStorm: key = "#LOC_SA_weather_dustStorm"; break;
				default: key = "#LOC_SA_weather_clear"; break;
			}

			// One neutral colour for every state: severity lives in the corner
			// badge rather than the glyph's tint, so the icons read as one family
			// and the mark is the only thing competing for attention.
			Color c = SaUi.Text;

			// Flavor overrides the name and the icon, never the state, so a body
			// whose rain is not water can say so. The generic loc KEY goes in as
			// the fallback rather than its translation: Loc() below resolves
			// whichever wins, and Localizer.Format returns a non-key string
			// unchanged, so an entry may supply either a key or a literal.
			bool isNight = sunElevationDeg < 0.0;
			SaWeatherFlavor.Resolve(bodyName, state, isNight, key,
				SaWeatherIcons.PathFor(state, isNight),
				out string name, out string iconPath);

			if (weatherLabel != null)
			{
				weatherLabel.text = Loc(name);
				weatherLabel.color = c;
			}

			// Sprite lookup only when the path actually changed: Load() caches,
			// but this runs at the panel's refresh rate and the common case is
			// "same weather as last tick".
			if (weatherIcon != null && iconPath != lastWeatherIconPath)
			{
				lastWeatherIconPath = iconPath;
				Sprite sprite = SaWeatherIcons.Load(iconPath);
				weatherIcon.sprite = sprite;
				weatherIcon.enabled = sprite != null;
			}
			if (weatherIcon != null) weatherIcon.color = c;

			switch (SaWeatherStates.SeverityOf(state))
			{
				case SaWeatherSeverity.Warning: SetWeatherBadge("!", SaUi.Danger); break;
				case SaWeatherSeverity.Caution: SetWeatherBadge("!", SaUi.Warn); break;
				default: SetWeatherBadge(null, SaUi.Warn); break;
			}
			SetWeatherForecast(weather.Forecast, ut);
		}

		/// <summary>
		/// The locked face of the section: the padlock icon in dim grey under
		/// UNKNOWN, the one weather label that is uppercase. Used both by the
		/// science gate and by a genuinely unknown sky kept visible for the
		/// Weather Report. No forecast either way — a sky you cannot read has no
		/// tomorrow.
		/// </summary>
		private void SetWeatherLocked()
		{
			if (weatherLabel != null)
			{
				weatherLabel.text = Loc("#LOC_SA_weather_locked");
				weatherLabel.color = SaUi.TextDim;
			}
			string iconPath = SaWeatherIcons.LockedPath;
			if (weatherIcon != null && iconPath != lastWeatherIconPath)
			{
				lastWeatherIconPath = iconPath;
				Sprite sprite = SaWeatherIcons.Load(iconPath);
				weatherIcon.sprite = sprite;
				weatherIcon.enabled = sprite != null;
			}
			if (weatherIcon != null) weatherIcon.color = SaUi.TextDim;
			// No badge in either case: the padlock icon and the UNKNOWN label
			// already say it.
			SetWeatherBadge(null, SaUi.Cyan);
			SetWeatherForecast(default(SaWeatherForecast), 0.0);
		}

		/// <summary>Hidden for a null glyph. Only "!" can use the alert texture;
		/// any other glyph is always the font's.</summary>
		private void SetWeatherBadge(string glyph, Color color)
		{
			if (weatherBadgeText == null) return;
			GameObject badge = weatherBadgeText.transform.parent.gameObject;
			if (glyph == null)
			{
				badge.SetActive(false);
				return;
			}
			badge.SetActive(true);
			bool useImage = weatherBadgeImage != null && glyph == "!";
			if (weatherBadgeImage != null)
			{
				weatherBadgeImage.enabled = useImage;
				weatherBadgeImage.color = color;
			}
			weatherBadgeText.enabled = !useImage;
			weatherBadgeText.text = glyph;
			weatherBadgeText.color = color;
		}

		/// <summary>
		/// The forecast sentence (WeatherForecaster): lowercase, dim, two
		/// points smaller than the state, after a blank line. Hidden — spacer
		/// included — when there is nothing to say, so an unforecastable body
		/// looks exactly as it did before the feature existed.
		/// </summary>
		private void SetWeatherForecast(SaWeatherForecast forecast, double ut)
		{
			if (weatherForecastLabel == null || weatherForecastSpacerGo == null) return;
			bool show = forecast.Kind != SaForecastKind.None;
			weatherForecastSpacerGo.SetActive(show);
			weatherForecastLabel.gameObject.SetActive(show);
			if (!show) return;

			string when = FormatForecastDuration(forecast.Seconds);
			string text;
			switch (forecast.Kind)
			{
				case SaForecastKind.Ends:
					text = Localizer.Format("#LOC_SA_forecast_ends", ForecastStateName(forecast.State), when);
					break;
				case SaForecastKind.Risk:
					text = Localizer.Format("#LOC_SA_forecast_risk", ForecastStateName(forecast.State), when);
					break;
				case SaForecastKind.Possible:
					text = Localizer.Format("#LOC_SA_forecast_possible", ForecastStateName(forecast.State), when);
					break;
				case SaForecastKind.Changeable:
					// Three wordings for the same fact, rotated per weather
					// window and not per tick: the seed is the UT of the next
					// transition, constant until the front changes.
					long seed = (long)((ut + forecast.Seconds) / 60.0);
					int variant = (int)(seed % ChangeableWordings) + 1;
					text = Loc("#LOC_SA_forecast_changeable_" + variant);
					break;
				default:
					// The horizon is a round number of days: "3d+", not "3d 0h+".
					text = Localizer.Format("#LOC_SA_forecast_stable", FormatForecastDuration(forecast.Seconds, 1));
					break;
			}
			weatherForecastLabel.text = text;
		}

		private const int ChangeableWordings = 3;

		/// <summary>
		/// Short, lowercase state names for the forecast sentence, on their own
		/// keys rather than the state label's: a full name does not fit a 9px line
		/// with a time after it. Never the flavor name either, for the same reason.
		/// </summary>
		private static string ForecastStateName(SaWeatherState state)
		{
			switch (state)
			{
				case SaWeatherState.Rain: return Loc("#LOC_SA_forecast_rain");
				case SaWeatherState.Snow: return Loc("#LOC_SA_forecast_snow");
				case SaWeatherState.Thunderstorm: return Loc("#LOC_SA_forecast_thunderstorm");
				case SaWeatherState.DustStorm: return Loc("#LOC_SA_forecast_dustStorm");
				default: return Loc("#LOC_SA_weather_clear");
			}
		}

		/// <summary>
		/// Forecast times use the same global-calendar formatter as every other
		/// countdown in the panel, capped at two terms and never showing seconds:
		/// weather is not accurate to the second, and the sentence has to fit a
		/// narrow line. Under a minute reads "&lt;1m" rather than "0m".
		/// </summary>
		private static string FormatForecastDuration(double seconds, int maxTerms = 2)
		{
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 1e12) return Loc("#LOC_SA_val_infinite");
			seconds = Math.Max(0.0, seconds);
			IDateTimeFormatter fmt = KSPUtil.dateTimeFormatter;
			if (fmt == null || fmt.Minute <= 0 || fmt.Hour <= 0 || fmt.Day <= 0 || fmt.Year <= 0)
				return F(seconds, "0") + " s";
			if (seconds < fmt.Minute) return "<1" + TimeUnitMinute;

			long total = (long)seconds;
			long y = total / fmt.Year;
			long remY = total % fmt.Year;
			long d = remY / fmt.Day;
			long remD = remY % fmt.Day;
			long h = remD / fmt.Hour;
			long m = (remD % fmt.Hour) / fmt.Minute;
			return JoinDurationTerms(new[] { y, d, h, m },
				new[] { TimeUnitYear, TimeUnitDay, TimeUnitHour, TimeUnitMinute }, maxTerms);
		}

		private static string FormatPressure(double kPa)
		{
			if (kPa <= 1e-6) return Loc("#LOC_SA_val_vacuum");
			if (SaPersist.PressureUnit == SaPressureUnit.Atm)
			{
				return F(kPa / 101.325, "0.0000") + " atm";
			}
			if (kPa < 1.0) return F(kPa * 1000.0, "0") + " Pa";
			return F(kPa, "0.00") + " kPa";
		}

		private static string FormatTerminator(double km, double deg, bool toEast)
		{
			string dir = toEast ? "E" : "W";
			return SaPersist.TerminatorUnit == SaTerminatorUnit.Km
				? F(km, "0.0") + " km " + dir
				: F(deg, "0.0") + "° " + dir;
		}

		/// <summary>
		/// Live = getGeeForceAtPosition, as the stock gravimeter reads it and under
		/// the same altitude &lt;= 3*radius range check; fixed = body.GeeASL in
		/// m/s^2. Live by default, with a setting to switch to the ASL value.
		/// </summary>
		private static string FormatGravity(SaReadout r, out Color color)
		{
			color = SaUi.Text;
			bool useFixed = SaParams.UseFixedSurfaceGravity;
			// Only the LIVE reading is gated: the body's ASL reference is a known
			// constant and stays visible, dimmed to say it is a reference rather
			// than a measurement.
			if (!useFixed && !r.GravityUnlocked)
			{
				useFixed = true;
				color = SaUi.TextDim;
			}
			if (!useFixed && !r.GravityLiveValid)
			{
				return Loc("#LOC_SA_val_outOfRange");
			}
			double mps2 = useFixed ? r.GravityAslMps2 : r.GravityLiveMps2;
			if (SaPersist.GravityUnit == SaGravityUnit.G)
			{
				return F(mps2 / PhysicsGlobals.GravitationalAcceleration, "0.00") + " g";
			}
			return F(mps2, "0.00") + " m/s2";
		}

		private static string FormatCoordinates(double lat, double lon)
		{
			if (SaPersist.CoordUnit == SaCoordUnit.Dms)
			{
				// Two stacked lines: DMS is much wider than decimal degrees, and
				// "lat / lon" on one line overflows the row sideways.
				return FormatDms(lat, isLat: true) + "\n" + FormatDms(lon, isLat: false);
			}
			return F(lat, "0.00") + "° / " + F(lon, "0.00") + "°";
		}

		private static string FormatDms(double deg, bool isLat)
		{
			string hemi = isLat ? (deg >= 0.0 ? "N" : "S") : (deg >= 0.0 ? "E" : "W");
			double a = Math.Abs(deg);
			int d = (int)a;
			double mFull = (a - d) * 60.0;
			int m = (int)mFull;
			double s = (mFull - m) * 60.0;
			return d + "° " + m.ToString("00", CultureInfo.InvariantCulture) + "' "
				+ F(s, "00") + "\" " + hemi;
		}

		/// <summary>
		/// True local calendar date on the home body: UT shifted by the current
		/// timezone's offset before formatting, so it can genuinely differ by a
		/// day near the zone opposite the reference meridian, the date-line
		/// equivalent. Deliberately linear rather than the equation-of-time
		/// corrected offset: a date only flips once per solar day, so a
		/// few-minutes correction is invisible except right at midnight.
		/// </summary>
		private static string FormatLocalDate(SaReadout r)
		{
			if (KSPUtil.dateTimeFormatter == null) return "";
			int n = BodyClock.LocalHoursPerDay;
			if (n <= 0 || r.SolarDayLengthSec <= 0.0) return "";

			double zoneOffsetSec = r.TimeZoneIndex * (r.SolarDayLengthSec / n);
			return KSPUtil.dateTimeFormatter.PrintDateCompact(r.UT + zoneOffsetSec, false);
		}

		// ------------------------------------------------------------ helpers

		private void SetRow(string id, string value, Color? color = null)
		{
			if (valueLabels.TryGetValue("row_" + id, out Text t))
			{
				t.text = value;
				if (color.HasValue) t.color = color.Value;
			}
		}

		private void SetLabel(string id, string value, Color color)
		{
			if (valueLabels.TryGetValue(id, out Text t))
			{
				t.text = value;
				t.color = color;
			}
		}

		private static string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

		private static string ChipKey(SaMode mode)
		{
			switch (mode)
			{
				case SaMode.Surface: return "#LOC_SA_chip_surface";
				case SaMode.Orbit: return "#LOC_SA_chip_orbit";
				default: return "#LOC_SA_chip_tidalLock";
			}
		}

		/// <summary>Dawn and Dusk collapse to a single "Terminator" label on an
		/// airless body, there being no optical twilight to tell them apart. Only
		/// the label merges: the underlying phase, and its sunrise/sunset event
		/// direction, are unchanged.</summary>
		private static string PhaseKeySurface(SaPhaseSurface p, bool hasAtmosphere)
		{
			switch (p)
			{
				case SaPhaseSurface.Dawn: return hasAtmosphere ? "#LOC_SA_phase_dawn" : "#LOC_SA_phase_terminatorSurface";
				case SaPhaseSurface.Morning: return "#LOC_SA_phase_morning";
				case SaPhaseSurface.Noon: return "#LOC_SA_phase_noon";
				case SaPhaseSurface.Afternoon: return "#LOC_SA_phase_afternoon";
				case SaPhaseSurface.Dusk: return hasAtmosphere ? "#LOC_SA_phase_dusk" : "#LOC_SA_phase_terminatorSurface";
				default: return "#LOC_SA_phase_night";
			}
		}

		/// <summary>Binary: "terminator" is a surface concept, and orbit has only
		/// Sunlit and Eclipse.</summary>
		private static string PhaseKeyOrbit(SaPhaseOrbit p)
		{
			return p == SaPhaseOrbit.Sunlit ? "#LOC_SA_phase_sunlit" : "#LOC_SA_phase_eclipse";
		}

		private static string PhaseKeyTidalLock(SaPhaseTidalLock p)
		{
			switch (p)
			{
				case SaPhaseTidalLock.Day: return "#LOC_SA_phase_day";
				case SaPhaseTidalLock.Terminator: return "#LOC_SA_phase_terminatorLock";
				default: return "#LOC_SA_phase_nightLock";
			}
		}

		// ------------------------------------------------------------ drag / focus

		private void AttachDrag(RectTransform bar)
		{
			DragHandler drag = bar.gameObject.AddComponent<DragHandler>();
			drag.target = windowRect;
			drag.onDragEnd = SavePosition;
		}

		private void AttachDoubleClick(RectTransform bar, System.Action onDouble)
		{
			DoubleClickHandler dc = bar.gameObject.AddComponent<DoubleClickHandler>();
			dc.onDoubleClick = onDouble;
		}

		private void SavePosition()
		{
			if (windowRect == null) return;
			SaPersist.HasWindowPosition = true;
			SaPersist.WindowPosition = windowRect.anchoredPosition;
			SaPersist.Save();
		}

		private class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
		{
			public RectTransform target;
			public System.Action onDragEnd;
			private Vector2 offset;

			public void OnBeginDrag(PointerEventData eventData)
			{
				RectTransformUtility.ScreenPointToLocalPointInRectangle(
					(RectTransform)target.parent, eventData.position, eventData.pressEventCamera, out Vector2 point);
				offset = target.anchoredPosition - point;
			}

			public void OnDrag(PointerEventData eventData)
			{
				if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
					(RectTransform)target.parent, eventData.position, eventData.pressEventCamera, out Vector2 point))
				{
					target.anchoredPosition = point + offset;
				}
			}

			public void OnEndDrag(PointerEventData eventData)
			{
				onDragEnd?.Invoke();
			}
		}

		private class DoubleClickHandler : MonoBehaviour, IPointerClickHandler
		{
			public System.Action onDoubleClick;
			private const float Threshold = 0.35f;
			private float lastClickTime = -10f;

			public void OnPointerClick(PointerEventData eventData)
			{
				float now = Time.unscaledTime;
				if (now - lastClickTime < Threshold)
				{
					onDoubleClick?.Invoke();
					lastClickTime = -10f;
				}
				else
				{
					lastClickTime = now;
				}
			}
		}

		private class FocusLock : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			public string lockId;

			public void OnPointerEnter(PointerEventData eventData)
			{
				InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, lockId);
			}

			public void OnPointerExit(PointerEventData eventData)
			{
				InputLockManager.RemoveControlLock(lockId);
			}

			private void OnDisable()
			{
				InputLockManager.RemoveControlLock(lockId);
			}
		}
	}
}
