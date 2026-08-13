using System;
using System.Collections.Generic;
using SituationalAwareness.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// Vector dial (design doc §6.2, three variants — surface arc / orbit
	/// ring / tidal-lock horizon) and the progress timeline (§6.3). All
	/// shapes are built once and toggled/repositioned per refresh — never
	/// glyphs (lesson from KRAB's non-rendering ⟳, design doc §6.2).
	/// </summary>
	internal static class SaDial
	{
		private const float Rad2Deg = 180f / Mathf.PI;
		// Bumped from 24 (bug fix, test M2 retest): each segment is an
		// independent quad with no miter join, so the stroke shows small
		// gaps at every joint — visible as speckled dots where a
		// differently-colored layer sits underneath (orbit ring's shadow
		// band over the lit track). More/smaller segments shrink the gaps
		// proportionally; cheap regardless, this is a tiny 2D UI mesh.
		private const int ArcSegments = 90;
		// Hardcoded (2026-07-23, user request; scoped to the root Sun only
		// 2026-07-24 — see SaReadout.BodyIsSun): the root star has no
		// orbit-color of its own (it doesn't orbit anything, no sensible
		// entry in PSystemManager.OrbitRendererDataCache for it) — not
		// something a planet pack cfg can define either way, so a fixed
		// gold tone stands in for it. A secondary star (e.g. Grannus) DOES
		// orbit the root Sun for real and keeps its own configured color.
		private static readonly Color StarDiscColor = new Color(1f, 0.8f, 0.2f, 1f);
		private static readonly List<Vector2> EmptyPoints = new List<Vector2>();

		public class Handle
		{
			// Surface (dial area centered on svg-like 110x70, horizon at y=-25)
			public GameObject surfaceGroup;
			public SaVectorLine surfaceTrack, surfaceFill, surfaceHorizon;
			public SaVectorDot surfaceSun;

			// Orbit (ring centered at origin, radius 30)
			public GameObject orbitGroup;
			public SaVectorLine orbitTrack, orbitShadow;
			public SaVectorDot orbitMarker;
			public SaVectorDot orbitPlanet;

			// Tidal lock (same area as surface, static horizon)
			public GameObject lockGroup;
			public SaVectorLine lockHorizon;
			public SaVectorDot lockSun;

			public Text phaseLabel;
			public Text subLabel;
			// Parent of phaseLabel/subLabel (M3 point 6, go 2026-07-28) —
			// exposed so SaWindow can attach a click-catcher over the whole
			// area for the SOLAR TIME line's click-to-cycle, without SaDial
			// itself needing to know about click handlers (SaWindow owns
			// all click behavior, SaDial only builds visuals).
			public Transform labelsArea;

			// Timeline
			public RectTransform timelineDay;
			public RectTransform timelineCursor;
			public Text timelineTickLeft, timelineTickMid, timelineTickRight;
		}

		private static Vector2 SurfaceP(float svgX, float svgY) => new Vector2(svgX - 55f, 35f - svgY);

		/// <summary>
		/// Builds the dial only (dial column). The timeline is built
		/// separately via BuildTimeline, called by the window AFTER the data
		/// rows so it lands at the bottom of the data column, not the top
		/// (design doc §6.3: timeline sits below the environment rows).
		/// </summary>
		public static Handle Build(Transform dialParent)
		{
			Handle h = new Handle();

			GameObject area = SaUi.Go("DialArea", dialParent);
			SaUi.Size(area, 106f, 64f);
			RectTransform areaRect = (RectTransform)area.transform;
			areaRect.pivot = new Vector2(0.5f, 0.5f);

			// --- surface arc ---
			h.surfaceGroup = SaUi.Go("Surface", areaRect);
			CenterRect(h.surfaceGroup);
			h.surfaceTrack = NewLine(h.surfaceGroup.transform, SaUi.PanelEdge, 5f, false);
			h.surfaceHorizon = NewLine(h.surfaceGroup.transform, SaUi.PanelEdge, 1.5f, true);
			h.surfaceFill = NewLine(h.surfaceGroup.transform, SaUi.Amber, 5f, false);
			h.surfaceSun = NewDot(h.surfaceGroup.transform, SaUi.Amber);
			SetPoints(h.surfaceTrack, ArcPoints(new Vector2(0f, -25f), 45f, 180f, 0f));

			// --- orbit ring ---
			h.orbitGroup = SaUi.Go("Orbit", areaRect);
			CenterRect(h.orbitGroup);
			// Color set dynamically each refresh (UpdateOrbit) — default vs
			// per-body map color depends on a settings toggle, so the Build-
			// time color here is just a placeholder.
			h.orbitPlanet = NewDot(h.orbitGroup.transform, SaUi.OrbitPlanetDefault);
			h.orbitPlanet.SetPosition(Vector2.zero, 14f);
			// Lit/shadow bands (bug fix, test M2 fase 8: two similarly-dark
			// grays were indistinguishable) — pale blue for lit, dark navy
			// for shadow, white marker.
			h.orbitTrack = NewLine(h.orbitGroup.transform, SaUi.OrbitLit, 4f, false);
			SetPoints(h.orbitTrack, ArcPoints(Vector2.zero, 30f, 0f, 360f));
			h.orbitShadow = NewLine(h.orbitGroup.transform, SaUi.OrbitShadow, 4f, false);
			h.orbitMarker = NewDot(h.orbitGroup.transform, SaUi.OrbitMarker);

			// --- tidal lock ---
			h.lockGroup = SaUi.Go("TidalLock", areaRect);
			CenterRect(h.lockGroup);
			h.lockHorizon = NewLine(h.lockGroup.transform, SaUi.PanelEdge, 1.5f, true);
			SetPoints(h.lockHorizon, new List<Vector2> { SurfaceP(5f, 60f), SurfaceP(105f, 60f) });
			h.lockSun = NewDot(h.lockGroup.transform, SaUi.Amber);

			GameObject labels = SaUi.Go("Labels", dialParent);
			// Spacing bumped from 2f (user feedback 2026-07-19, fase 0/8:
			// phase label and the sub-line below it read as visually fused).
			SaUi.Vertical(labels, 0, 8f);
			h.phaseLabel = SaUi.Label(labels.transform, "-", 12, SaUi.Text, TextAnchor.MiddleCenter);
			h.subLabel = SaUi.Label(labels.transform, "-", 10, SaUi.TextDim, TextAnchor.MiddleCenter);
			h.labelsArea = labels.transform;

			return h;
		}

		/// <summary>
		/// Mini-dial for the collapsed strip (design doc §6.7, mockup
		/// B1/B2/B3 — implemented 2026-07-25). A simplified version of
		/// <see cref="Handle"/>: only the elements that stay legible at
		/// icon size survive — surface drops the progress fill and the
		/// dashed horizon (just the track arc + sun dot), orbit drops the
		/// central planet disc (just the ring, shadow arc, and marker).
		/// One shared instance per window; only one of the three groups is
		/// active at a time, same SetActive pattern as <see cref="Handle"/>.
		/// </summary>
		public class StripHandle
		{
			public GameObject surfaceGroup;
			public SaVectorLine surfaceTrack;
			public SaVectorDot surfaceSun;

			public GameObject orbitGroup;
			public SaVectorLine orbitTrack, orbitShadow;
			public SaVectorDot orbitMarker;

			public GameObject lockGroup;
			public SaVectorLine lockHorizon;
			public SaVectorDot lockSun;
		}

		private const float StripSurfaceRadius = 11f;
		private const float StripSurfaceCenterY = -6f;
		private const float StripOrbitRadius = 10f;

		public static StripHandle BuildStripIcon(Transform parent)
		{
			StripHandle h = new StripHandle();

			GameObject area = SaUi.Go("StripIcon", parent);
			SaUi.Size(area, 26f, 22f);
			RectTransform areaRect = (RectTransform)area.transform;
			areaRect.pivot = new Vector2(0.5f, 0.5f);

			Vector2 surfaceCenter = new Vector2(0f, StripSurfaceCenterY);

			h.surfaceGroup = SaUi.Go("Surface", areaRect);
			CenterRect(h.surfaceGroup);
			h.surfaceTrack = NewLine(h.surfaceGroup.transform, SaUi.PanelEdge, 3f, false);
			SetPoints(h.surfaceTrack, ArcPoints(surfaceCenter, StripSurfaceRadius, 180f, 0f));
			h.surfaceSun = NewDot(h.surfaceGroup.transform, SaUi.Amber);

			h.orbitGroup = SaUi.Go("Orbit", areaRect);
			CenterRect(h.orbitGroup);
			// Same palette as the extended dial (bug fix 2026-07-25, test M3:
			// first pass used generic UI colors instead of the orbit-specific
			// ones) — pale blue lit / dark navy shadow / white marker.
			h.orbitTrack = NewLine(h.orbitGroup.transform, SaUi.OrbitLit, 3f, false);
			SetPoints(h.orbitTrack, ArcPoints(Vector2.zero, StripOrbitRadius, 0f, 360f));
			h.orbitShadow = NewLine(h.orbitGroup.transform, SaUi.OrbitShadow, 3f, false);
			h.orbitMarker = NewDot(h.orbitGroup.transform, SaUi.OrbitMarker);

			h.lockGroup = SaUi.Go("TidalLock", areaRect);
			CenterRect(h.lockGroup);
			h.lockHorizon = NewLine(h.lockGroup.transform, SaUi.PanelEdge, 2f, true);
			SetPoints(h.lockHorizon, new List<Vector2>
			{
				surfaceCenter + new Vector2(-StripSurfaceRadius, 0f),
				surfaceCenter + new Vector2(StripSurfaceRadius, 0f)
			});
			h.lockSun = NewDot(h.lockGroup.transform, SaUi.Amber);

			return h;
		}

		/// <summary>Same per-mode formulas as <see cref="Update"/>'s surface/orbit/tidal-lock branches, just at the strip's smaller radii — kept in sync by reusing the same SaReadout fields, not a separate re-derivation.</summary>
		public static void UpdateStripIcon(StripHandle h, SaReadout r)
		{
			h.surfaceGroup.SetActive(r.Mode == SaMode.Surface);
			h.orbitGroup.SetActive(r.Mode == SaMode.Orbit);
			h.lockGroup.SetActive(r.Mode == SaMode.TidalLock);

			switch (r.Mode)
			{
				case SaMode.Surface:
				{
					// Stellar dive exception (retest 2026-07-28): no
					// discrete sun position makes sense when the vessel is
					// at/in the star itself — track ring turns danger red
					// instead (same "surrounded by the star" signal as the
					// extended dial's full red arc; the strip icon has no
					// separate fill element to reuse).
					if (r.BodyIsStar)
					{
						h.surfaceTrack.color = SaUi.Danger;
						h.surfaceSun.gameObject.SetActive(false);
						break;
					}
					h.surfaceTrack.color = SaUi.PanelEdge;
					// Near-pole exception (M3 point 5, go 2026-07-28): ring
					// stays its normal neutral color (not hazardous), just
					// no sun-position dot — same unreliable-longitude reason
					// as the extended dial.
					if (r.NearPole)
					{
						h.surfaceSun.gameObject.SetActive(false);
						break;
					}

					Vector2 center = new Vector2(0f, StripSurfaceCenterY);
					double hourAngle = SolarMath.HourAngleDeg(r.DayProgress01);
					bool isDay = Math.Abs(hourAngle) <= 90.0;
					double t = isDay ? Mathf.Clamp01((float)((hourAngle + 90.0) / 180.0)) : 0.0;
					h.surfaceSun.gameObject.SetActive(isDay);
					if (isDay)
					{
						h.surfaceSun.SetPosition(ArcPoint(center, StripSurfaceRadius, 180f - (float)t * 180f), 2.5f);
					}
					break;
				}
				case SaMode.Orbit:
				{
					float thetaDeg = (float)(r.OrbitThetaNowRad * Rad2Deg);
					float phiDeg = (float)(r.OrbitPhiRad * Rad2Deg);
					if (phiDeg > 0.05f)
					{
						SetPoints(h.orbitShadow, ArcPoints(Vector2.zero, StripOrbitRadius, -90f - phiDeg, -90f + phiDeg));
					}
					else
					{
						SetPoints(h.orbitShadow, EmptyPoints);
					}
					h.orbitMarker.SetPosition(ArcPoint(Vector2.zero, StripOrbitRadius, -90f + thetaDeg), 2.5f);
					break;
				}
				default:
				{
					float elevRad = (float)(r.SunElevationDeg * Mathf.Deg2Rad);
					float y = Mathf.Clamp(StripSurfaceCenterY + Mathf.Sin(elevRad) * StripSurfaceRadius,
						StripSurfaceCenterY - StripSurfaceRadius, StripSurfaceCenterY + StripSurfaceRadius);
					h.lockSun.SetPosition(new Vector2(0f, y), 3f);
					break;
				}
			}
		}

		/// <summary>Builds the progress timeline (design doc §6.3) in its own parent — call after the data rows so it lands at the bottom.</summary>
		public static void BuildTimeline(Handle h, Transform parent)
		{
			GameObject wrap = SaUi.Go("Timeline", parent);
			VerticalLayoutGroup wrapGroup = SaUi.Vertical(wrap, 0, 2f);
			// Left/right padding (bug fix, test M3 retest): the timeline
			// sits directly in dataCol, which lost its own horizontal
			// padding when it moved onto individual rows instead (M3
			// restyling, so dividers could bleed to the true edges) — the
			// timeline isn't a divider, it needs the same 12px indent as
			// every other row (SaWindow.RowPadding), or it renders flush
			// against both side edges of the window. Small top padding
			// (further refinement, same retest): even after the L/R fix the
			// track bar still hugged the divider above it more tightly than
			// its own bottom margin — a few px nudges it down to match.
			wrapGroup.padding = new RectOffset(12, 12, 4, 0);

			RectTransform track = SaUi.Bordered("Track", wrap.transform, SaUi.Inset, SaUi.PanelEdge);
			SaUi.Size(track.gameObject, -1f, 6f);

			Image day = SaUi.Panel_("Day", track, SaUi.AmberDim);
			RectTransform dayRect = day.rectTransform;
			dayRect.pivot = new Vector2(0f, 0.5f);
			dayRect.anchorMin = new Vector2(0f, 0f);
			dayRect.anchorMax = new Vector2(0f, 1f);
			dayRect.offsetMin = Vector2.zero;
			dayRect.offsetMax = Vector2.zero;
			h.timelineDay = dayRect;

			Image cursor = SaUi.Panel_("Cursor", track, SaUi.Amber);
			RectTransform cursorRect = cursor.rectTransform;
			cursorRect.pivot = new Vector2(0.5f, 0.5f);
			cursorRect.anchorMin = new Vector2(0f, 0f);
			cursorRect.anchorMax = new Vector2(0f, 1f);
			cursorRect.sizeDelta = new Vector2(2f, 4f);
			h.timelineCursor = cursorRect;

			GameObject ticks = SaUi.Go("Ticks", wrap.transform);
			SaUi.Horizontal(ticks, 0, 0);
			h.timelineTickLeft = SaUi.Label(ticks.transform, "-", 9, SaUi.TextDim, TextAnchor.MiddleLeft);
			SaUi.Size(h.timelineTickLeft.gameObject, -1f, 12f, 1f);
			h.timelineTickMid = SaUi.Label(ticks.transform, "-", 9, SaUi.TextDim, TextAnchor.MiddleCenter);
			SaUi.Size(h.timelineTickMid.gameObject, -1f, 12f, 1f);
			h.timelineTickRight = SaUi.Label(ticks.transform, "-", 9, SaUi.TextDim, TextAnchor.MiddleRight);
			SaUi.Size(h.timelineTickRight.gameObject, -1f, 12f, 1f);
		}

		public static void Update(Handle h, SaReadout r, string phaseText, string subText,
			string tickLeft, string tickMid, string tickRight)
		{
			h.surfaceGroup.SetActive(r.Mode == SaMode.Surface);
			h.orbitGroup.SetActive(r.Mode == SaMode.Orbit);
			h.lockGroup.SetActive(r.Mode == SaMode.TidalLock);

			// Uppercase (M3 restyling, mockup reconciliation) — single point
			// of truth here rather than at each of the three call sites in
			// SaWindow.BuildClockAndPhase.
			h.phaseLabel.text = phaseText.ToUpperInvariant();
			h.subLabel.text = subText;
			h.timelineTickLeft.text = tickLeft;
			h.timelineTickMid.text = tickMid;
			h.timelineTickRight.text = tickRight;

			switch (r.Mode)
			{
				case SaMode.Surface:
					UpdateSurface(h, r);
					break;
				case SaMode.Orbit:
					UpdateOrbit(h, r);
					break;
				case SaMode.TidalLock:
					UpdateTidalLock(h, r);
					break;
			}
		}

		private static void UpdateSurface(Handle h, SaReadout r)
		{
			// Stellar dive exception (retest 2026-07-28): the vessel is
			// AT/IN the star itself, there's no external light source to
			// cast a day/night arc from. A full arc in danger red — not an
			// empty one — communicates "surrounded by the star", the
			// opposite of what an empty (night-style) arc would imply.
			if (r.BodyIsStar)
			{
				h.surfaceFill.color = SaUi.Danger;
				SetPoints(h.surfaceFill, ArcPoints(new Vector2(0f, -25f), 45f, 180f, 0f));
				h.surfaceSun.gameObject.SetActive(false);
				// No day-progress cursor to place — there's no day cycle to
				// track a position within (unlike surfaceSun, the day band
				// itself is harmless/decorative left in its default spot).
				h.timelineCursor.gameObject.SetActive(false);
				SetTimeline(h, 0.25f, 0.75f, 0f);
				return;
			}
			// Near-pole exception (M3 point 5, go 2026-07-28): NOT
			// dangerous (unlike stellar dive) — empty arc, same as night,
			// rather than a colored full one. DayProgress01 itself is
			// derived from the same unstable longitude, so there's no
			// trustworthy position to draw a cursor at either.
			if (r.NearPole)
			{
				h.surfaceFill.color = SaUi.Amber;
				SetPoints(h.surfaceFill, EmptyPoints);
				h.surfaceSun.gameObject.SetActive(false);
				h.timelineCursor.gameObject.SetActive(false);
				SetTimeline(h, 0.25f, 0.75f, 0f);
				return;
			}
			h.timelineCursor.gameObject.SetActive(true);
			h.surfaceFill.color = SaUi.Amber;

			double hourAngle = SolarMath.HourAngleDeg(r.DayProgress01);
			bool isDay = Math.Abs(hourAngle) <= 90.0;
			// Bug fix (test M2 retest): t was clamped to [0,1] without ever
			// resetting after dusk, so the fill arc stayed visually "full"
			// all through the night and only snapped empty at the
			// hourAngle +180/-180 wraparound (midnight) instead of at dusk
			// where the daylight arc actually ends.
			double t = isDay ? Mathf.Clamp01((float)((hourAngle + 90.0) / 180.0)) : 0.0;

			// Bug fix (test M3 retest): a stray amber dot showed up at the
			// dial's start (180°) at night — same root cause as the earlier
			// orbit-shadow fix. At t=0 (night, or the exact sunrise instant)
			// the arc spans 180..180, a single repeated point; the round-join
			// discs (closing gaps on a real curve) painted a filled disc on
			// top of it instead. Feed no points at all when there's no real
			// arc to draw, same pattern as SaDial.UpdateOrbit's shadow arc.
			if (isDay && t > 0.0)
			{
				SetPoints(h.surfaceFill, ArcPoints(new Vector2(0f, -25f), 45f, 180f, 180f - (float)t * 180f));
			}
			else
			{
				SetPoints(h.surfaceFill, EmptyPoints);
			}
			h.surfaceSun.gameObject.SetActive(isDay);
			if (isDay)
			{
				h.surfaceSun.SetPosition(ArcPoint(new Vector2(0f, -25f), 45f, 180f - (float)t * 180f), 4.5f);
			}

			SetTimeline(h, 0.25f, 0.75f, (float)r.DayProgress01);
		}

		private static void UpdateOrbit(Handle h, SaReadout r)
		{
			// Planet disc color (feature request, test M2 retest): default
			// is a brighter neutral than before ("ancora molto dim"); opt-in
			// setting swaps it for the body's real map/orbit-line color.
			// No extra attenuation here (bug fix 2026-07-22): the color now
			// comes from PSystemManager.OrbitRendererDataCache, which Kopernicus
			// already stores at half the configured icon brightness — dimming
			// it again on top made it too dark/muddy.
			h.orbitPlanet.color = SaParams.UseBodyMapColorForDial
				? (r.BodyIsSun ? StarDiscColor : r.BodyMapColorRaw)
				: SaUi.OrbitPlanetDefault;

			float thetaDeg = (float)(r.OrbitThetaNowRad * Rad2Deg);
			float phiDeg = (float)(r.OrbitPhiRad * Rad2Deg);

			// No shadow to draw when orbiting the star itself (bug fix
			// 2026-07-24): phiRad collapses to exactly 0 there
			// (OrbitIllumination.Status's own degenerate-vector guard), so
			// the arc would span -90..-90 — every polyline point lands on
			// the same spot, and the round-join discs (all superimposed)
			// showed up as a lone stray dot. Below the threshold, feed no
			// points at all instead of a zero-width arc.
			if (phiDeg > 0.05f)
			{
				SetPoints(h.orbitShadow, ArcPoints(Vector2.zero, 30f, -90f - phiDeg, -90f + phiDeg));
			}
			else
			{
				SetPoints(h.orbitShadow, EmptyPoints);
			}
			h.orbitMarker.SetPosition(ArcPoint(Vector2.zero, 30f, -90f + thetaDeg), 4f);

			double phiRad = r.OrbitPhiRad;
			double shadowFrac = phiRad / Math.PI;
			double cursorFrac = Wrap01((r.OrbitThetaNowRad + phiRad) / (2.0 * Math.PI));
			SetTimeline(h, (float)shadowFrac, 1f, (float)cursorFrac);
		}

		private static void UpdateTidalLock(Handle h, SaReadout r)
		{
			float elevRad = (float)(r.SunElevationDeg * Mathf.Deg2Rad);
			float y = Mathf.Clamp(-25f + Mathf.Sin(elevRad) * 45f, -30f, 30f);
			h.lockSun.SetPosition(new Vector2(0f, y), 5f);

			// Bar position from the same hour angle that drives the phase
			// classification (design doc §5.3): |hourAngle|/180, subsolar=0
			// (day side), antisolar=1 — E/W side is shown separately by the
			// TERMINATORE row, the bar only cares about distance from noon.
			float frac = Mathf.Clamp01((float)(Math.Abs(r.TidalLockHourAngleDeg) / 180.0));
			SetTimeline(h, 0f, 0.5f, frac);
		}

		private static void SetTimeline(Handle h, float dayStart01, float dayEnd01, float cursor01)
		{
			dayStart01 = Mathf.Clamp01(dayStart01);
			dayEnd01 = Mathf.Clamp01(dayEnd01);
			h.timelineDay.anchorMin = new Vector2(dayStart01, 0f);
			h.timelineDay.anchorMax = new Vector2(dayEnd01, 1f);
			h.timelineDay.offsetMin = Vector2.zero;
			h.timelineDay.offsetMax = Vector2.zero;

			cursor01 = Mathf.Clamp01(cursor01);
			h.timelineCursor.anchorMin = new Vector2(cursor01, 0f);
			h.timelineCursor.anchorMax = new Vector2(cursor01, 1f);
			h.timelineCursor.anchoredPosition = Vector2.zero;
		}

		private static double Wrap01(double f)
		{
			f %= 1.0;
			if (f < 0.0) f += 1.0;
			return f;
		}

		/// <summary>
		/// Explicitly sets all four RectTransform fields to "zero-size,
		/// centered on the parent" (KRAB lesson, CLAUDE.md 2026-07-15: a
		/// GameObject created via Go() defaults to Unity's implicit
		/// anchorMin/anchorMax = bottom-left of the parent, not the center —
		/// leaving even one of the four fields implicit breaks the local-
		/// space math these hand-drawn dials rely on, silently).
		/// </summary>
		private static void CenterRect(GameObject go)
		{
			RectTransform rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = Vector2.zero;
			rt.anchoredPosition = Vector2.zero;
		}

		private static SaVectorLine NewLine(Transform parent, Color color, float thickness, bool dashed)
		{
			GameObject go = SaUi.Go("Line", parent);
			RectTransform rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = Vector2.zero;
			SaVectorLine line = go.AddComponent<SaVectorLine>();
			line.color = color;
			line.thickness = thickness;
			line.dashed = dashed;
			line.raycastTarget = false;
			return line;
		}

		private static SaVectorDot NewDot(Transform parent, Color color)
		{
			GameObject go = SaUi.Go("Dot", parent);
			RectTransform rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = Vector2.zero;
			SaVectorDot dot = go.AddComponent<SaVectorDot>();
			dot.color = color;
			dot.raycastTarget = false;
			return dot;
		}

		private static void SetPoints(SaVectorLine line, List<Vector2> points) => line.SetPoints(points);

		private static Vector2 ArcPoint(Vector2 center, float radius, float angleDeg)
		{
			float rad = angleDeg * Mathf.Deg2Rad;
			return center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
		}

		private static List<Vector2> ArcPoints(Vector2 center, float radius, float fromDeg, float toDeg)
		{
			List<Vector2> pts = new List<Vector2>(ArcSegments + 1);
			for (int i = 0; i <= ArcSegments; i++)
			{
				float a = Mathf.Lerp(fromDeg, toDeg, i / (float)ArcSegments);
				pts.Add(ArcPoint(center, radius, a));
			}
			return pts;
		}
	}
}
