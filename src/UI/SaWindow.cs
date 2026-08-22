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
	/// The single SA window (design doc §6): extended telemetry panel
	/// (header/dial+data/footer) that doubles as the collapsed strip
	/// (§6.7) — same window, toggled by double-clicking the header/strip,
	/// never two separate windows. Shell (canvas, drag, hover focus-lock)
	/// follows the KRAB/KRILL pattern.
	/// </summary>
	internal class SaWindow : MonoBehaviour
	{
		private const float ExtendedWidth = 430f;
		private const float StripWidth = 430f;
		// Named (bug fix, retest 2026-07-30): were magic numbers inline at
		// each use, making it easy for DataCol's fixed width to silently
		// drift out of sync with DialCol's if either changed independently.
		private const float DialColWidth = 138f;
		private const float VDividerWidth = 1f;
		private const string InputLockId = "SA_WINDOW";
		private const double LowTempAlertC = 0.0;
		private const double HighTempAlertC = 50.0;
		// EXT TEMP now 3-tier (M3 restyling, user request): warn between
		// HighTempAlertC and this, danger above it.
		private const double VeryHighTempAlertC = 100.0;
		// HULL TEMP color comes from the WORST part's T/maxTemp ratio, not
		// an absolute temperature (design discussion 2026-07-26): 1500K is
		// fine for a heat shield but already dangerous for a science
		// experiment or control surface — only a relative threshold means
		// the same thing for every part.
		private const double HullWarnRatio = 0.6;
		private const double HullDangerRatio = 0.8;
		// 10 Hz (RealBattery precedent, user feedback 2026-07-19): fast
		// enough to feel live, slow enough that decimal digits don't flicker.
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
		// Only meaningful in Surface mode (M3 restyling): with the
		// terminator row moved up into the position group and PRESSURE/EXT
		// TEMP reshuffled below, this divider stopped marking a real
		// boundary in Orbit/TidalLock.
		private GameObject positionDividerGo;
		// Visible in any body-surface context — Surface OR TidalLock, never
		// Orbit — AND only when the body has an atmosphere (M3 restyling;
		// corrected 2026-07-27 twice: first to mode+body instead of a live
		// vacuum reading, then to include TidalLock alongside Surface —
		// confirmed with a real case, a GEP body tidally locked on its star
		// AND atmospheric, matching mockup D). atmosphericTemperature reads
		// a hardcoded constant in a vacuum (PhysicsGlobals.SpaceTemperature,
		// verified on the decompiled FlightIntegrator.cs) so it's
		// meaningless there — but the condition is deliberately mode+body,
		// not "is the vessel currently reading zero pressure": if Surface
		// mode is later extended to sub-orbital hops (design doc §9 M3
		// point 4), a vessel briefly above the atmosphere but still in
		// Surface mode must keep showing "4K"/"VACUUM", not blink the rows
		// off. pressureRowGo shares this same visibility condition
		// (see ApplyExtended's showAtmosphericRows). HULL TEMP (always
		// visible) covers the vacuum/orbit case instead.
		private GameObject extTempRowGo;
		private GameObject pressureRowGo;
		// Weather extension host (notes/indagine-meteo.md §8): a designated
		// slot under the dial for external content — a survey companion's
		// button row today, SA's own weather icon later. Bug fix (in-game
		// test 2026-08-17): follows showAtmosphericRows exactly, same as
		// EXT TEMP/PRESSURE — Surface/TidalLock AND the body has an
		// atmosphere (weather/clouds are meaningless on an airless body),
		// not just "not Orbit".
		private GameObject weatherHostGo;
		// Surface mode only, hidden when the body IS the star (stellar
		// dive exception, retest 2026-07-28): "sun elevation/azimuth" is
		// meaningless when the vessel is at/in the light source itself.
		private GameObject sunRowGo;
		// SOLAR TIME (M3 point 6, go 2026-07-28): cached from the last
		// throttled refresh so Update()'s fast countdown-only path can
		// rebuild the full 3-line dial sub-label without needing a full
		// SaReadout — only the countdown itself needs per-frame freshness,
		// see BuildSurfaceSubText's doc comment.
		private double lastDayProgress01;
		private int lastSolarHour, lastSolarMinute, lastSolarSecond;
		private double lastEquationOfTimeSec;
		private double lastSolarDayLengthSec;
		// KSC's timezone VALUE is fixed for the whole session (its
		// coordinates are set once at game load — SpaceCenter.Instance never
		// moves without a restart, notes/verifiche-api.md M1 addendum), so
		// it's computed once. The label TEXT built from it is rebuilt every
		// refresh regardless (M3 restyling: whether it's even shown now
		// also depends on mode/home-body, see UpdateKscRow).
		private int? kscTimeZoneCached;

		public static void Open()
		{
			if (current != null) return;
			// Safety net (2026-07-22, home-clock drift investigation):
			// re-anchor every cached body's mean-time offset every time the
			// window opens, so any residual drift accumulated since the
			// last open never grows past "one open-to-open interval".
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
			// SA's own scale (retest 2026-07-27, "+50%, regolabile?") stacks on
			// top of the stock UI Scale rather than replacing it — ConstantPixelSize
			// + scaleFactor is a pixel-perfect canvas multiplier, no Transform-scale
			// blur regardless of the value. Re-synced each refresh tick in
			// FixedUpdate so a change made in the settings dialog takes effect
			// without reopening the window.
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

			// F2 hide-UI (bug fix — a custom overlay Canvas MUST honor this,
			// standard KSP-modding requirement) + pause-menu overlap are two
			// different triggers for the same "hide our canvas" outcome —
			// see Update().
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

			// DestroyImmediate, not Destroy: we need the old children truly
			// gone (not just scheduled for end-of-frame cleanup) before the
			// forced layout rebuild below, or it would measure stale content
			// still present in the hierarchy and mis-size the compensation.
			for (int i = shellHost.childCount - 1; i >= 0; i--)
			{
				DestroyImmediate(shellHost.GetChild(i).gameObject);
			}
			valueLabels.Clear();
			dial = null;
			// The kscTime row's label Text is about to be destroyed and
			// rebuilt from scratch (bare "KSC", no TZ) below — the cached
			// TZ *value* doesn't need recomputing, but the flag must reset
			// so UpdateKscRow re-applies it to the new Text object once.
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

			// Bug fix (test M2, fase 2): toggling collapsed<->extended used to
			// recenter the whole window instead of keeping the titlebar/strip
			// at a fixed height. Pivot (0.5,0.5) means a height change shifts
			// both edges symmetrically unless compensated — force the layout
			// NOW (not next frame) so the new height is known synchronously.
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

			// Led LAST (bug fix: it had drifted to the front, mockup has it
			// trailing) and round via SaVectorDot (bug fix: was a plain
			// square Image).
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

			// No frame on the dial (M3 restyling, mockup reconciliation):
			// plain Go(), not Bordered() — the window's own panel color
			// already shows through underneath (same fill color), only the
			// border outline is gone.
			GameObject dialCol = SaUi.Go("DialCol", body.transform);
			SaUi.Size(dialCol, DialColWidth, -1f);
			// A little more breathing room between the dial graphic and the
			// phase/sub labels below it (retest 2026-07-30, user request) —
			// this VerticalLayoutGroup's spacing is the ONLY thing
			// controlling that gap, and it's shared by all three modes by
			// construction (Surface/Orbit/TidalLock all reuse the same
			// dialCol), so there's no per-mode drift to worry about here.
			SaUi.Vertical(dialCol, 10, 8f);

			// Vertical divider dial<->data, full height of the body row
			// (M3 restyling) — a sibling in this same HorizontalLayoutGroup,
			// so it's excluded from header/footer by construction.
			Image vDivider = SaUi.Panel_("VDivider", body.transform, SaUi.PanelEdge);
			LayoutElement vDividerLe = SaUi.Size(vDivider.gameObject, VDividerWidth, -1f);
			vDividerLe.flexibleHeight = 1f;

			GameObject dataCol = SaUi.Go("DataCol", body.transform);
			// Fixed width, NOT auto/content-driven (bug fix, retest
			// 2026-07-30): with no explicit preferredWidth, this column's
			// layout size deferred to its children's own preferred width —
			// which, for clockMain's Text, scales with the STRING LENGTH.
			// A long "T−..." countdown could nudge dataCol's computed
			// preferred size around, visually reflowing the row it shares
			// with clockSub even though the outer window itself never
			// resizes (only its ContentSizeFitter's verticalFit is set).
			// Explicit width removes that dependency entirely.
			SaUi.Size(dataCol, ExtendedWidth - DialColWidth - VDividerWidth, -1f, 1f);
			// Horizontal padding moved OFF the column and onto each row
			// instead (M3 restyling, "layout a tabella"): dividers need to
			// bleed to the column's true left/right edges, which only works
			// if the column itself carries no horizontal padding of its own
			// — vertical padding (12) stays here, shared by every row.
			VerticalLayoutGroup dataColGroup = SaUi.Vertical(dataCol, 0, 6f);
			dataColGroup.padding = new RectOffset(0, 0, 12, 12);

			dial = SaDial.Build(dialCol.transform);
			// SOLAR TIME's click-to-cycle (M3 point 6, go 2026-07-28): the
			// dial's phase/sub area isn't a data row, so it has no built-in
			// click handling — same ClickCatcher used by AddClickableRow,
			// just attached directly to the dial's labels area instead.
			SaUi.ClickCatcher(dial.labelsArea, CycleSolarTimeFormat);

			// Weather extension host (notes/indagine-meteo.md §8): dialCol's
			// dedicated lower section — the dial (built above) is the upper
			// one. Bug fix (in-game test 2026-08-18): a plain SaUi.Go() has
			// no ContentSizeFitter, so it never reported its populated
			// children's real height back to dialCol's own VerticalLayoutGroup
			// and just overlapped the dial above it. Own Vertical+ContentSizeFitter
			// (same "hug my children" pattern as windowRect in Build()) makes
			// it collapse to true zero height when empty (no companion
			// subscribed) and size correctly once one populates it.
			weatherHostGo = SaUi.Go("WeatherHost", dialCol.transform);
			SaUi.Vertical(weatherHostGo, 0, 0f);
			weatherHostGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			SaExtensionPoint.Raise(weatherHostGo.transform);

			BuildRows(dataCol.transform);
			// Divider before the timeline (M3 restyling, mockup
			// reconciliation) — previously the timeline ran straight into
			// the last data row with no visual break.
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
			// Moved up from the environment group below (M3 restyling,
			// mockup D reconciliation): sits right under BIOME, in the
			// position block, not down with SUN/FLUX/GRAVITY.
			terminatorRowGo = AddClickableRow(parent, "#LOC_SA_row_terminator", "terminator", CycleTerminatorUnit);

			// Surface-only (M3 restyling, see positionDividerGo doc comment
			// at the field declaration) — toggled in FixedUpdate's
			// mode-change block.
			positionDividerGo = SaUi.Divider(parent);

			// sunRowGo, not just SetRow text (retest 2026-07-28, stellar
			// dive exception): "sun elevation/azimuth" is meaningless when
			// the vessel IS at/in the star itself — hidden entirely rather
			// than "—", same treatment as EXT TEMP/PRESSURE (a structural,
			// per-body fact, not a transient numerical edge case).
			sunRowGo = AddDataRow(parent, "#LOC_SA_row_sun", "sun");
			AddDataRow(parent, "#LOC_SA_row_flux", "flux");
			// HULL TEMP before EXT TEMP (test M3 retest, reordered
			// 2026-07-27) — always-visible row leads, the conditionally-
			// hidden one follows.
			AddClickableRow(parent, "#LOC_SA_row_hullTemp", "hullTemp", CycleTempUnit);
			// Same click-cycle action as HULL TEMP on purpose (design
			// discussion 2026-07-26): one shared unit for both rows, never
			// one in °C and the other in K.
			extTempRowGo = AddClickableRow(parent, "#LOC_SA_row_temperature", "temperature", CycleTempUnit);
			// Follows EXT TEMP's visibility now (user correction 2026-07-27,
			// see pressureRowGo doc comment).
			pressureRowGo = AddClickableRow(parent, "#LOC_SA_row_pressure", "pressure", CyclePressureUnit);
			AddClickableRow(parent, "#LOC_SA_row_gravity", "gravity", CycleGravityUnit);
		}

		// Left/right padding every row applies to itself now that dataCol's
		// own horizontal padding is zero (M3 restyling) — dividers (built
		// directly against dataCol, no row wrapper) bleed to the true edges.
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
			// Stashed for rows whose LABEL needs a per-session update (only
			// "kscTime" today — the timezone appended once, see UpdateKscRow).
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

			// Mini-dial FIRST (design doc §6.7, mockup B1/B2/B3 — implemented
			// 2026-07-25): a static generic icon stood in this spot for the
			// whole M2 cycle (registered placeholder, never a real dial).
			stripDial = SaDial.BuildStripIcon(bar);

			// Natural width, not a fixed 90px slot (bug fix, M3 restyling:
			// the leftover gap when the actual text was shorter than 90px
			// read as "too much space after the clock" — one of two causes
			// of the uneven spacing the user reported).
			stripHot = SaUi.Label(bar, "-", 16, SaUi.Amber, TextAnchor.MiddleLeft);
			SaUi.Size(stripHot.gameObject, -1f, 22f);

			// Local date/Sol, cyan, Surface-only (M3 restyling, mockup B1
			// reconciliation) — same dateLine concept as the extended view,
			// toggled active in ApplyStrip.
			stripDate = SaUi.Label(bar, "-", 10, SaUi.Cyan, TextAnchor.MiddleLeft);
			SaUi.Size(stripDate.gameObject, -1f, 22f);

			stripPhase = SaUi.Label(bar, "-", 10, SaUi.Text, TextAnchor.MiddleLeft);
			SaUi.Size(stripPhase.gameObject, -1f, 22f);

			// Single flexible spacer (bug fix, M3 restyling): replaces the
			// flexibleWidth that used to live on stripPhase — with that
			// removed, phase sits snug against its neighbors like every
			// other element, and this one dedicated spacer is the only
			// thing pushing tail+led to the right (mockup's "margin-left:
			// auto" on the tail alone, not a stretchy middle element).
			GameObject stripSpacer = SaUi.Go("Spacer", bar);
			SaUi.Size(stripSpacer, 0f, -1f, 1f);

			stripTail = SaUi.Label(bar, "-", 10, SaUi.TextDim, TextAnchor.MiddleRight);
			SaUi.Size(stripTail.gameObject, -1f, 22f);

			// Led LAST here too (bug fix: was first, mockup B1/B2/B3 has it trailing).
			ledDotStrip = NewLedDot(bar);

			AttachDrag(bar);
			AttachDoubleClick(bar, ToggleCollapsed);
		}

		// --------------------------------------------------------------- tick

		/// <summary>
		/// Visibility, every rendered frame (cheap, needs to feel instant):
		/// F2 hide-UI and the pause menu (Esc) both hide our canvas — bugs
		/// found in test M2 (fase 3, "critiche"): SA had no onHideUI/
		/// onShowUI hook at all, and rendered ON TOP of the pause menu
		/// because nothing ever hid it. PauseMenu.isOpen is a plain public
		/// static bool (verified on the decompiled source), cheap to poll —
		/// no event to hook, only a live property.
		///
		/// Orbit timer refresh also lives here, not FixedUpdate (retest
		/// 2026-07-28: still felt throttled from FixedUpdate — physics
		/// ticks and rendered frames aren't the same cadence, and what the
		/// player actually perceives is the rendered frame). Update() is
		/// the closest thing to "real time" a UI text field can have: once
		/// per rendered frame, uncapped by any accumulator. The underlying
		/// physics state (vessel position/velocity, orbit.period) only
		/// changes at the physics tick rate regardless of where we read it
		/// from, so reading it here loses nothing and can only refresh the
		/// displayed text sooner.
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

			// SOLAR TIME's alba/tramonto countdown (M3 point 6, go
			// 2026-07-28: same "will 10Hz keep up with a fast-moving
			// vessel" concern already solved for the orbit timer above —
			// exact longitude changes continuously, so this needs the same
			// unthrottled treatment). Only in extended Surface mode, only
			// when the toggle is actually on and the row isn't itself
			// overridden by the stellar-dive/near-pole exceptions (cheap
			// re-checks here, not a full readout rebuild). day%/solar-clock
			// digits reuse the last throttled values — see BuildClockAndPhase,
			// only the countdown itself needs per-frame freshness.
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
		/// Data refresh, tied to FixedUpdate + a 10 Hz accumulator (bug fix,
		/// test M2 fase 0/9): Planetarium.time — and therefore UT — only
		/// advances inside FixedUpdate (verified on the decompiled
		/// Planetarium.cs: `time += fixedDeltaTime * timeScale` lives in
		/// FixedUpdate, not Update). Sampling it from a rendered-frame
		/// Update() meant re-reading the same frozen value across several
		/// frames then jumping all at once — sampling here instead means we
		/// only ever look at UT exactly when the game itself just moved it.
		/// The accumulator on top caps the actual redraw to ~10/s (RealBattery
		/// precedent), independent of the physics tick rate.
		/// </summary>
		private void FixedUpdate()
		{
			// Orbit timer's own unthrottled refresh now lives in Update()
			// (retest 2026-07-28), not here — see that method's doc comment.
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
				// Surface-only (M3 restyling, see positionDividerGo field
				// doc comment).
				if (!SaPersist.Collapsed && positionDividerGo != null)
				{
					positionDividerGo.SetActive(lastMode == SaMode.Surface);
				}
			}
			if (!SaPersist.Collapsed && metRowGo != null)
			{
				// Checked every refresh, not just on mode change: the player
				// can flip the "show MET" setting mid-flight from the
				// difficulty dialog while the window stays open.
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
		/// TZ shown ONLY in Surface mode on the home body (M3 restyling,
		/// user correction 2026-07-26 — the earlier "always show TZ" was
		/// backwards: the default is hidden, the home-body-surface case is
		/// the exception). The TZ VALUE itself is still cached once
		/// (kscTimeZoneCached — genuinely session-stable, KSC's coordinates
		/// don't move), but the LABEL TEXT is now rebuilt every refresh
		/// since it depends on the current mode too, not just a one-time
		/// value — cheap (a string concat at 10 Hz), same reasoning as the
		/// hull-temperature computation.
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
			// "SA //" dim, vessel name appended (M3 restyling, mockup
			// reconciliation) — rich text span, base color (set once at
			// BuildHeader time) stays the fallback for the un-tagged part.
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
			// DMS is much wider than decimal (bug fix, test M2 retest: it
			// was overflowing sideways instead of growing the row) — switch
			// to a 2-line stacked value and grow the row's height instead of
			// its width, which disturbs the fixed-width dial column less.
			if (coordinatesRowGo != null)
			{
				LayoutElement coordLe = coordinatesRowGo.GetComponent<LayoutElement>();
				if (coordLe != null)
				{
					coordLe.preferredHeight = SaPersist.CoordUnit == SaCoordUnit.Dms ? 28f : 16f;
				}
			}
			SetRow("biome", r.BiomeName);
			// Stellar dive exception (retest 2026-07-28): SUN row hidden
			// when the body IS the star — "elevation of the sun" is
			// meaningless when the vessel is at/in the light source itself.
			bool onStarSurface = r.Mode == SaMode.Surface && r.BodyIsStar;
			if (sunRowGo != null) sunRowGo.SetActive(!onStarSurface);
			if (!onStarSurface)
			{
				// "EL"/"AZ" labels (M3 restyling, mockup reconciliation) —
				// orbit has no azimuth (design doc §4.2, sub-vessel point only).
				// Near a pole (M3 point 5, retest 2026-07-28): azimuth alone
				// becomes "—" — a bearing has no meaning at the pole itself,
				// and is numerically unstable close to it — while elevation
				// stays valid (it doesn't blow up there, just shrinks toward
				// 0°, zero-tilt model).
				SetRow("sun", r.Mode == SaMode.Orbit
					? "EL " + F(r.SunElevationDeg, "0.0") + "° (sub-v.)"
					: "EL " + F(r.SunElevationDeg, "0.0") + "° · AZ " + (r.NearPole ? "—" : F(r.SunAzimuthDeg, "0.0") + "°"));
			}
			SetRow("flux", F(r.SolarFluxWm2, "N0") + " W/m2");
			// EXT TEMP + PRESSURE visible in any body-surface context
			// (Surface OR TidalLock — NOT Orbit) AND the body has an
			// atmosphere (user correction 2026-07-27, confirmed with a
			// real case: a GEP body is tidally locked on its star AND has
			// an atmosphere, matching mockup D). Mode+body, deliberately
			// NOT a live-pressure/vacuum reading — see extTempRowGo field
			// doc comment for why. Checked every tick, not just on mode
			// change: BodyHasAtmosphere could theoretically differ if the
			// active vessel changes without a mode change, and it's cheap.
			bool showAtmosphericRows = (r.Mode == SaMode.Surface || r.Mode == SaMode.TidalLock) && r.BodyHasAtmosphere;
			if (extTempRowGo != null) extTempRowGo.SetActive(showAtmosphericRows);
			if (pressureRowGo != null) pressureRowGo.SetActive(showAtmosphericRows);
			// Weather extension host (bug fix 2026-08-17): same rule as
			// above, not just "not Orbit" — no clouds without an atmosphere.
			if (weatherHostGo != null) weatherHostGo.SetActive(showAtmosphericRows);
			if (showAtmosphericRows)
			{
				SetTemperatureRow(r.ExternalTemperatureK);
				SetRow("pressure", FormatPressure(r.PressureKPa));
			}
			SetHullTemperatureRow(r.HullTempK, r.HullTempWorstRatio);
			SetRow("gravity", FormatGravity(r));
			if (r.Mode == SaMode.TidalLock)
			{
				// Cyan (M3 restyling, mockup D reconciliation).
				SetRow("terminator", FormatTerminator(r.TerminatorDistanceKm, r.TerminatorDistanceDeg, r.TerminatorToEast), SaUi.Cyan);
			}

			string lockedBadge = r.BodyTidallyLocked
				? " · <color=" + LockedBadgeColorHex + ">" + Loc("#LOC_SA_val_locked") + "</color>"
				: "";
			// A star has no solar day relative to itself — omit the segment
			// entirely when orbiting one directly (bug fix 2026-07-22).
			// Timer format survey (retest 2026-07-28/29): "how long is this
			// body's day relative to UT" is the "relative local time"
			// concept — a plain duration in UT seconds, global timer
			// format. Bug fix (retest 2026-07-30): does NOT "fall out
			// naturally" for the home body as assumed — fmt.Day is
			// CALIBRATED to equal the home body's own solar day exactly, so
			// dividing gives exactly 1 (a circular, useless "1d 00h 00m")
			// instead of the intended "how many hours". Restored the
			// dedicated home-only exception the old bespoke
			// FormatSolarDayDuration had, via FormatHomeSolarDayDuration.
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
				// BodyChain stores raw CelestialBody refs (bypasses the
				// already-cleaned r.BodyName/StarName strings) — needs the
				// same gender-tag cleaning (test M2 retest; bug fix
				// 2026-07-24: switched to LocalizeRemoveGender, see
				// SaReadoutProvider.CleanDisplayName doc comment).
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
					// Stellar dive exception (retest 2026-07-28): the body
					// IS the star — no external sun to derive a local time/
					// day cycle from. Field-level override within Surface
					// mode (not a dedicated SaMode) so EXT TEMP/PRESSURE's
					// existing Mode==Surface condition keeps applying
					// automatically.
					if (r.BodyIsStar)
					{
						SetLabel("clockMain", Loc("#LOC_SA_val_stellarDive"), SaUi.Danger);
						SetLabel("clockSub", Loc("#LOC_SA_val_noLocalTime"), SaUi.TextDim);
						phaseText = "—";
						subText = "";
						tickLeft = "—"; tickMid = ""; tickRight = "—";
						break;
					}
					// Near-pole exception (M3 point 5, go 2026-07-28): NOT
					// dangerous (unlike stellar dive) — neutral color, not
					// Danger red. "POLAR ZONE" explains why the clock can't
					// be trusted (longitude is unstable right at the pole);
					// "MIDNIGHT SUN" on the phase line describes what's
					// actually happening (zero-tilt model: the sun still
					// technically sets, but the elevation swing shrinks to
					// nothing this close to the pole, so it never gets
					// meaningfully dark).
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
					// Sol means nothing extra on the home body (design doc §3.4).
					// Bug fix 2026-07-25: showing the stock Year/Day date there
					// (old behavior) just repeated the UT row verbatim, because
					// that date was UT's own calendar date, never actually
					// dependent on longitude. Real fix: THIS line shows the
					// true LOCAL calendar date (shifted by the current zone's
					// offset before formatting) — genuinely testable for the
					// date-line-equivalent crossing, unlike the old duplicate.
					// UT itself keeps showing its own date+time (user request,
					// M3 test: the two are conceptually distinct now, not a
					// duplicate, even though they usually agree in value).
					// Cyan (M3 restyling, mockup A reconciliation).
					string dateLine = r.IsHomeBody ? FormatLocalDate(r) : Loc("#LOC_SA_val_sol") + " " + r.Sol;
					string dateLineColored = string.IsNullOrEmpty(dateLine) ? "" : "<color=#" + SaUi.CyanHex + ">" + dateLine + "</color>";
					SetLabel("clockSub", Loc("#LOC_SA_val_localTime") + " · TZ" + (r.TimeZoneIndex >= 0 ? "+" : "") + r.TimeZoneIndex
						+ "\n" + dateLineColored, SaUi.TextDim);
					phaseText = Loc(PhaseKeySurface(r.PhaseSurface, r.BodyHasAtmosphere));
					// Two/three lines, arrow leading the countdown (M3
					// restyling, mockup reconciliation — matches what the
					// strip already did): "day 64%" / "↓ sunset 2h39m" /
					// optionally "SOLAR 12:34:56" (M3 point 6). Cache the
					// pieces Update()'s fast countdown-only path can't
					// afford to recompute every frame (see that method's
					// doc comment).
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
					// Binary day-side/night-side (M3 restyling, mockup D
					// reconciliation) replaces the 3-way phase name here —
					// that's still shown under the dial via phaseText below,
					// this line only needs day vs night, in cyan.
					bool isDaySide = Math.Abs(r.TidalLockHourAngleDeg) < 90.0;
					string sideText = "<color=#" + SaUi.CyanHex + ">" + Loc(isDaySide ? "#LOC_SA_val_daySide" : "#LOC_SA_val_nightSide") + "</color>";
					SetLabel("clockSub", Loc("#LOC_SA_val_noLocalTime") + "\n" + sideText, SaUi.TextDim);
					phaseText = Loc(PhaseKeyTidalLock(r.PhaseTidalLock));
					subText = Loc("#LOC_SA_val_sunFixed") + " EL " + F(r.SunElevationDeg, "0.0") + "°";
					tickLeft = Loc("#LOC_SA_val_subsolar"); tickMid = Loc("#LOC_SA_phase_terminatorLock"); tickRight = Loc("#LOC_SA_val_antisolar");
					break;
			}
		}

		/// <summary>Terms shown for MET specifically — always down to the second, user request (retest 2026-07-29), unlike every other timer's 3-term cap.</summary>
		private const int MetMaxTerms = 5;

		/// <summary>
		/// MET's own value (retest 2026-07-30): always preceded by a literal
		/// "T+" (mirrors orbit's "T−"), in either of two selectable formats
		/// — SaParams.MetStockalikeFormat picks between the default
		/// all-letters timer (FormatDurationYDHMS, always to the second)
		/// and a "stockalike" hybrid closer to stock's own mission-timer
		/// look: letters for year/day, colon HH:MM:SS below that
		/// ("1y 23d 03:14:09"). Year/day terms are OMITTED when zero
		/// (a fresh launch shouldn't show "0y 0d 00:14:09").
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
		/// Global "timer" formatter (unified, retest 2026-07-28/29): every
		/// term uses a localized unit-LETTER suffix, never a bare colon pair
		/// or zero-padding — "32y 311d 11h" / "311d 11h 39m" / "11h 39m 2s"
		/// / "39m 2s" / "2s". Shows the `maxTerms` largest terms starting
		/// from wherever the magnitude actually begins — short durations
		/// don't pad out to a fixed field count, they just show fewer terms
		/// (user expectation, retest 2026-07-29: a small EqT should read as
		/// "12m 34s", not "00h 12m 34s"). Uses KSPUtil.dateTimeFormatter's
		/// own Year/Day/Hour/Minute (seconds-per-unit) so tier boundaries
		/// follow the active clock (stock, JNSQ+Kronometer, RSS...) instead
		/// of a hardcoded 3600/86400 — same discipline as
		/// BodyClock.LocalHoursPerDay.
		///
		/// `shrinkWhenWide` (retest 2026-07-30, user request): the big
		/// clockMain "T−..." countdown, at 26px bold, could still overlap
		/// its row-mate ("Period"/clockSub) once the LEADING term reached 2
		/// digits — empirically fine under 10 of the leading unit
		/// ("T−9h 58m 43s"), too wide from 10 up. When set, drops one term
		/// from the cap whenever the leading value is ≥10, regardless of
		/// which unit that happens to be (the cause is character width, not
		/// a specific unit) — used only by the two "T−" call sites, not the
		/// smaller-font fields (Period, MET, EqT, footer) where no overlap
		/// was reported.
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
		/// Home body exception for the footer's solar-day value (bug fix,
		/// retest 2026-07-30): the global calendar's Day unit IS the home
		/// body's own solar day by calibration (dateTimeFormatter.Day comes
		/// FROM the home body's rotation), so feeding it through
		/// FormatDurationYDHMS gives a circular, useless "1d 00h 00m" — 1
		/// home day is trivially always exactly 1 calendar day. Skips the
		/// day/year tiers entirely, always hours+minutes ("12h 00m" for a
		/// 12h Kronometer day) — matches what the retired
		/// FormatSolarDayDuration did for this same case.
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
		/// "Proportional local" timer (EqT — retest 2026-07-28/29): d_loc/
		/// h_loc/m_loc/s_loc, where 1 d_loc = the TARGET body's own solar
		/// day (solarDayLengthSec) and 1 h_loc = d_loc / N (N =
		/// BodyClock.LocalHoursPerDay — the SAME global hour count LOCAL
		/// TIME's own clock digits already use, deliberately NOT a separate
		/// fixed value: user correction 2026-07-29, "la suddivisione in ore
		/// dipende sempre dallo standard adottato per Kerbin"). Deliberately
		/// NOT the global calendar's own Year/Day/Hour (FormatDurationYDHMS)
		/// — that represents "how long is HOME's day", meaningless for a
		/// body whose day is a different length. No local-year tier: an
		/// equation-of-time offset spanning local years would already be a
		/// red flag on its own (user note 2026-07-29: exceeding even a local
		/// HOUR is already anomalous), the 3-term cap is enough on its own.
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
			// Rounding the last term can carry into the next one at the
			// boundary (e.g. 59.6s -> 60) — cheap cascade, cosmetic only.
			if (s >= 60) { s = 0; m++; }
			if (m >= 60) { m = 0; h++; }
			if (h >= n) { h = 0; d++; }

			return JoinDurationTerms(new[] { d, h, m, s },
				new[] { TimeUnitDay, TimeUnitHour, TimeUnitMinute, TimeUnitSecond }, maxTerms);
		}

		/// <summary>
		/// Shared term-joiner for both timer formatters: finds the largest
		/// non-zero unit, shows up to `maxTerms` from there downward. No
		/// zero-padding on any term (bug fix, retest 2026-07-30: the
		/// letter suffix already disambiguates each field, unlike a colon
		/// pair — padding just added width for nothing, and on a high
		/// Kerbin orbit "T−65g 05o 58m" was long enough to clip against
		/// "Period 65g 08o 18m" next to it; "T−65g 5o 1m" is shorter with
		/// no loss of clarity). `shrinkWhenWide` drops one more term
		/// whenever the leading value has reached 2+ digits (empirically
		/// where clockMain's 26px font starts to overlap — see
		/// FormatDurationYDHMS's doc comment).
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
		/// Unit-letter symbols for duration formatting (retest 2026-07-28/29,
		/// user request): sourced from stock's own #autoLOC_600231x keys —
		/// verified on the decompiled KSPUtil.cs, DefaultDateTimeFormatter's
		/// own PrintTime uses these exact keys internally — instead of
		/// hardcoded English letters, so e.g. Italian shows "a/g/h/m/s"
		/// instead of "y/d/h/m/s". These are STOCK keys, not Kronometer's,
		/// but that's fine: Kronometer only changes HOW LONG an hour/day is
		/// (dateTimeFormatter.Hour/Day), never the WORD for it, so the same
		/// symbol source is correct with or without it installed. Falls back
		/// to SA's own #LOC_SA_unit_* keys only if the stock key doesn't
		/// resolve (Localizer.Format's own convention: returns the key
		/// itself unchanged when not found).
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
		/// Orbit mode's clock-sub two-line text: "NEXT <phase>"/"STAR-CENTRIC"/
		/// "SOI CHANGE" + "Period ..." in cyan. Single source of truth shared
		/// by the throttled BuildClockAndPhase and SaWindow's unthrottled
		/// per-tick orbit-timer refresh (retest 2026-07-27) — the two paths
		/// can never disagree on formatting. Priority: an imminent SoI change
		/// (escape trajectory) is the most time-critical fact and wins even
		/// when BodyIsStar is also true (a rare case: escaping a star's own
		/// SoI); star-centric is next (no eclipse geometry orbiting the light
		/// source itself); otherwise the normal eclipse/light countdown.
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
		/// Orbit lit-fraction display (bug fix, retest 2026-07-30): on a
		/// high enough orbit the shadow cone subtends such a tiny angle
		/// that the rounded percentage reads "100%" even though a real
		/// eclipse exists and can last hours there — numerically correct,
		/// navigationally misleading (a player might not expect any night
		/// at all). Shows ">99%" instead whenever the value would round to
		/// 100 but isn't genuinely, exactly full — the star-orbiting case
		/// (BodyIsStar, where LitFraction01 really is always exactly 1.0,
		/// no eclipse geometry applies at all) still shows a plain "100%".
		/// </summary>
		private static string FormatLitFraction(double litFraction01, bool bodyIsStar)
		{
			bool nearFullNotExact = !bodyIsStar && litFraction01 < 1.0 && litFraction01 >= 0.995;
			return nearFullNotExact ? Loc("#LOC_SA_val_orbitLitNearFull") : F(litFraction01 * 100.0, "0") + "%";
		}

		/// <summary>
		/// Surface mode's dial sub-label: day%/countdown (2 lines, always),
		/// plus SOLAR TIME as an optional 3rd line (M3 point 6, go
		/// 2026-07-28) when SaParams.ShowSolarTime is on — single source of
		/// truth shared by the throttled BuildClockAndPhase and SaWindow's
		/// unthrottled per-frame countdown refresh, same pattern as
		/// FormatOrbitClockSub. When SOLAR TIME is on, the countdown itself
		/// switches from the zone-quantized value to the exact-longitude one
		/// (user request 2026-07-28: more precise once exact-position time
		/// is available anyway) — day% stays zone-based regardless (LOCAL
		/// TIME's own fraction, unaffected).
		/// </summary>
		private static string BuildSurfaceSubText(double dayProgress01,
			bool nextEventIsSunrise, double timeToNextEventSec,
			bool solarNextEventIsSunrise, double solarTimeToNextEventSec,
			int solarHour, int solarMinute, int solarSecond, double equationOfTimeSec, double solarDayLengthSec)
		{
			bool showSolar = SaParams.ShowSolarTime;
			bool useSunrise = showSolar ? solarNextEventIsSunrise : nextEventIsSunrise;
			double countdownSec = showSolar ? solarTimeToNextEventSec : timeToNextEventSec;

			// Timer format survey (retest 2026-07-28/29): this countdown used to
			// delegate to stock's own PrintTimeCompact (FormatDuration) — a
			// different convention from every other "time to event" field in
			// the panel. Unified onto FormatDurationYDHMS like the rest.
			string text = Loc("#LOC_SA_val_dayProgress") + " " + F(dayProgress01 * 100.0, "0") + "%\n"
				+ (useSunrise ? "↑" : "↓") + " "
				+ (useSunrise ? Loc("#LOC_SA_val_sunrise") : Loc("#LOC_SA_val_sunset")) + " " + FormatDurationYDHMS(countdownSec);

			if (showSolar)
			{
				// Label cycles with the value (retest 2026-07-30, user
				// request): the two formats are DIFFERENT quantities (a
				// time-of-day vs a signed offset), not two units/views of
				// the same one — every other cyclable field in the panel
				// is the latter, so a fixed "SOLAR" prefix on both
				// invited reading EqT as "zone time minus solar time"
				// (it isn't — EqT doesn't depend on longitude at all,
				// zone or exact, see FormatEquationOfTime's doc comment).
				// "LAT" (Local Apparent Time) is the standard gnomonics/
				// astronomy term for exactly this quantity — the time a
				// sundial would show — paired with "EQT" the same way
				// LMT/EoT are paired in that literature.
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
		/// SOLAR TIME's second click-cycled format: signed, "proportional
		/// local" units (retest 2026-07-28/29, user-clarified design) — NOT
		/// the global calendar's own year/day/hour (that's "how long is
		/// HOME's day", irrelevant to a body whose day is a different
		/// length). 1 local day = the TARGET body's own solarDayLengthSec;
		/// 1 local hour = that / BodyClock.LocalHoursPerDay (the SAME N
		/// LOCAL TIME's own clock digits already use — not a separate fixed
		/// value); 60 local minutes/hour, 60 local seconds/minute. See
		/// FormatLocalDurationYDHMS.
		/// </summary>
		private static string FormatEquationOfTime(double seconds, double solarDayLengthSec)
		{
			string sign = seconds < 0.0 ? "-" : "+";
			return sign + FormatLocalDurationYDHMS(Math.Abs(seconds), solarDayLengthSec);
		}

		private void ApplyStrip(SaReadout r)
		{
			SaDial.UpdateStripIcon(stripDial, r);
			// Surface-only (M3 restyling, mockup B1) — also hidden on a
			// stellar dive or near a pole (retest 2026-07-28), no reliable
			// local date to show in either case.
			stripDate.gameObject.SetActive(r.Mode == SaMode.Surface && !r.BodyIsStar && !r.NearPole);
			switch (r.Mode)
			{
				case SaMode.Surface:
					// Stellar dive exception (retest 2026-07-28): see
					// BuildClockAndPhase's matching branch, same reasoning.
					if (r.BodyIsStar)
					{
						stripHot.text = Loc("#LOC_SA_val_stellarDive");
						stripHot.color = SaUi.Danger;
						stripPhase.text = "—";
						stripTail.text = "";
						break;
					}
					// Near-pole exception (M3 point 5, go 2026-07-28): see
					// BuildClockAndPhase's matching branch — neutral color,
					// not Danger (not hazardous, just unreliable).
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
					// Uppercase (M3 restyling, mockup reconciliation).
					stripPhase.text = Loc(PhaseKeySurface(r.PhaseSurface, r.BodyHasAtmosphere)).ToUpperInvariant();
					stripTail.text = (r.NextEventIsSunrise ? "↑" : "↓") + " " + FormatDurationYDHMS(r.TimeToNextEventSec);
					break;
				case SaMode.Orbit:
					stripHot.text = "T−" + FormatDurationYDHMS(r.TimeToNextEventSec, shrinkWhenWide: true);
					stripHot.color = SaUi.Amber;
					// Both words always shown, fixed order (M3 restyling,
					// mockup reconciliation): current phase bright, the
					// other dim — rather than swapping which single word is
					// shown, matches the mockup's "ECLIPSE SUNLIT" pair.
					{
						bool eclipseActive = r.PhaseOrbit == SaPhaseOrbit.Eclipse;
						string eclipseWord = Loc("#LOC_SA_phase_eclipse").ToUpperInvariant();
						string sunlitWord = Loc("#LOC_SA_phase_sunlit").ToUpperInvariant();
						string activeHex = eclipseActive ? SaUi.TextHex : SaUi.TextDimHex;
						string dimHex = eclipseActive ? SaUi.TextDimHex : SaUi.TextHex;
						stripPhase.text = "<color=#" + activeHex + ">" + eclipseWord + "</color> <color=#" + dimHex + ">" + sunlitWord + "</color>";
					}
					// "light" prefix, lowercase (M3 restyling, mockup B2).
					stripTail.text = Loc("#LOC_SA_val_lightLower") + " " + FormatLitFraction(r.OrbitLitFraction01, r.BodyIsStar);
					break;
				default:
					stripHot.text = Loc("#LOC_SA_val_tidalLock");
					stripHot.color = SaUi.Danger;
					stripPhase.text = Loc(PhaseKeyTidalLock(r.PhaseTidalLock)).ToUpperInvariant();
					// "TERM" prefix, cyan (M3 restyling, mockup B3).
					stripTail.text = "<color=#" + SaUi.CyanHex + ">" + Loc("#LOC_SA_val_termShort") + " "
						+ FormatTerminator(r.TerminatorDistanceKm, r.TerminatorDistanceDeg, r.TerminatorToEast) + "</color>";
					break;
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
		/// M3 point 6 (go 2026-07-28): no-op when SOLAR TIME isn't even
		/// shown — the click-catcher covers the whole dial labels area
		/// unconditionally (Surface/Orbit/TidalLock all share it), so a
		/// click there when the line isn't visible would otherwise silently
		/// flip a preference with no feedback.
		/// </summary>
		private void CycleSolarTimeFormat()
		{
			if (!SaParams.ShowSolarTime) return;
			SaPersist.SolarTimeFormat = SaPersist.SolarTimeFormat == SaSolarTimeFormat.Clock
				? SaSolarTimeFormat.EquationOfTime
				: SaSolarTimeFormat.Clock;
			SaPersist.Save();
		}

		/// <summary>EXT TEMP: 3-tier now (M3 restyling) — cyan cold, neutral, warn (50-100°C), danger (&gt;100°C). Absolute thresholds make sense here: it's the one true ambient reading, not a part-specific value.</summary>
		private void SetTemperatureRow(double kelvin)
		{
			double celsius = kelvin - 273.15;
			// N1 (thousands separator, retest 2026-07-28 — matches FLUX's
			// N0): HULL TEMP in particular routinely hits four figures on
			// reentry (1500-2000+ K), where a bare "1500.0" reads slower
			// than "1,500.0".
			string text = SaPersist.TempUnit == SaTempUnit.Kelvin
				? F(kelvin, "N1") + " K"
				: F(celsius, "N1") + " °C";

			Color c = SaUi.Text;
			if (celsius < LowTempAlertC) c = SaUi.Cyan;
			else if (celsius > VeryHighTempAlertC) c = SaUi.Danger;
			else if (celsius > HighTempAlertC) c = SaUi.Warn;
			SetRow("temperature", text, c);
		}

		/// <summary>HULL TEMP (M3 restyling): value is the thermal-mass-weighted average (SaReadoutProvider.BuildHullTemperature), but the COLOR comes from worstRatio — the single hottest part's T/maxTemp — so one part near its limit shows red even while the fleet-wide average still looks comfortable. Cold side stays absolute (sub-zero is sub-zero for any material).</summary>
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
		/// Live = FlightGlobals.getGeeForceAtPosition (same as stock
		/// sensorGravimeter, m/s^2, gated by the same altitude<=3*radius
		/// range check — verified on the decompiled ModuleEnviroSensor);
		/// fixed = body.GeeASL converted to m/s^2. Default is live
		/// (design doc feedback 2026-07-19); settings toggle switches to
		/// the fixed ASL value.
		/// </summary>
		private static string FormatGravity(SaReadout r)
		{
			bool useFixed = SaParams.UseFixedSurfaceGravity;
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
				// Two stacked lines (bug fix, test M2 retest): DMS is much
				// wider than decimal degrees, "lat / lon" on one line was
				// overflowing the row sideways.
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
		/// True local calendar date on the home body (bug fix 2026-07-25):
		/// shifts UT by the current timezone's offset before formatting, so
		/// it can genuinely differ by a day from UT near the zone opposite
		/// the reference meridian (the date-line equivalent) — unlike the
		/// old dateLine, which just formatted raw UT (never a function of
		/// longitude, hence the duplication with the UT row it replaced).
		/// Deliberately linear (`k * L`), not the equation-of-time-corrected
		/// MeanTimeCalibration offset: a calendar date only flips once per
		/// solar day, so the few-minutes-scale correction is invisible here
		/// except in the exact seconds around midnight.
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

		/// <summary>Dawn/Dusk collapse to a single "Terminator" label on airless bodies (user feedback 2026-07-19): without an atmosphere there's no optical twilight to tell them apart. The underlying Dawn/Dusk phase (and its sunrise/sunset event direction) is unchanged, only the displayed label merges.</summary>
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

		/// <summary>Binary now (user decision 2026-07-19): "terminator" is a surface concept, orbit only has Sunlit/Eclipse.</summary>
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
