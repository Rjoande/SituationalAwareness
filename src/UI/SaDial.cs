using System;
using System.Collections.Generic;
using SituationalAwareness.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// Vector dial in three variants (surface arc, orbit ring, tidal-lock
	/// horizon) plus the progress timeline, design doc §6.2/§6.3. Shapes are
	/// built once and toggled or repositioned per refresh, and are always drawn
	/// rather than set as glyphs a stock font may not have.
	/// </summary>
	internal static class SaDial
	{
		private const float Rad2Deg = 180f / Mathf.PI;
		// High, because each segment is an independent quad and the join discs
		// only hide so much: smaller segments shrink the remaining gaps, and this
		// is a tiny 2D UI mesh either way.
		private const int ArcSegments = 90;
		// The root star orbits nothing, so it has no configurable orbit colour of
		// its own and a fixed gold tone stands in. A secondary star really does
		// orbit the root Sun and keeps its own colour (see SaReadout.BodyIsSun).
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
			// Parent of phaseLabel/subLabel, exposed so SaWindow can stretch a
			// click-catcher over the whole area: SaWindow owns all click
			// behaviour, SaDial only builds visuals.
			public Transform labelsArea;

			// Timeline
			public RectTransform timelineDay;
			public RectTransform timelineCursor;
			public Text timelineTickLeft, timelineTickMid, timelineTickRight;
		}

		private static Vector2 SurfaceP(float svgX, float svgY) => new Vector2(svgX - 55f, 35f - svgY);

		/// <summary>
		/// Builds the dial alone. The timeline is built separately by
		/// BuildTimeline, which the window calls AFTER the data rows so it lands
		/// at the bottom of the data column (design doc §6.3).
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
			// Colour is set each refresh in UpdateOrbit, since default vs per-body
			// map colour depends on a setting: this one is a placeholder.
			h.orbitPlanet = NewDot(h.orbitGroup.transform, SaUi.OrbitPlanetDefault);
			h.orbitPlanet.SetPosition(Vector2.zero, 14f);
			// Lit/shadow bands: pale blue lit, dark navy shadow, white marker.
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
			// Enough spacing that the phase label and the sub-line below it do not
			// read as one block.
			SaUi.Vertical(labels, 0, 8f);
			h.phaseLabel = SaUi.Label(labels.transform, "-", 12, SaUi.Text, TextAnchor.MiddleCenter);
			h.subLabel = SaUi.Label(labels.transform, "-", 10, SaUi.TextDim, TextAnchor.MiddleCenter);
			h.labelsArea = labels.transform;

			return h;
		}

		/// <summary>
		/// Mini-dial for the collapsed strip (design doc §6.7): a <see cref="Handle"/>
		/// reduced to what stays legible at icon size, so surface keeps only the
		/// track arc and sun dot, and orbit drops the central planet disc. One
		/// shared instance per window, one group active at a time.
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
			// Same palette as the extended dial: pale blue lit, dark navy shadow,
			// white marker.
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

		/// <summary>The same per-mode formulas as <see cref="Update"/>, at the
		/// strip's smaller radii: the same SaReadout fields are reused rather than
		/// re-derived, so the two views cannot disagree.</summary>
		public static void UpdateStripIcon(StripHandle h, SaReadout r)
		{
			h.surfaceGroup.SetActive(r.Mode == SaMode.Surface);
			h.orbitGroup.SetActive(r.Mode == SaMode.Orbit);
			h.lockGroup.SetActive(r.Mode == SaMode.TidalLock);

			switch (r.Mode)
			{
				case SaMode.Surface:
				{
					// Stellar dive: no discrete sun position makes sense when the
					// vessel is at or in the star itself, so the track ring turns
					// danger red instead — the strip has no separate fill element
					// to paint like the extended dial's full red arc.
					if (r.BodyIsStar)
					{
						h.surfaceTrack.color = SaUi.Danger;
						h.surfaceSun.gameObject.SetActive(false);
						break;
					}
					h.surfaceTrack.color = SaUi.PanelEdge;
					// Near the pole the ring keeps its neutral colour — nothing is
					// hazardous here — and simply loses the sun dot, longitude
					// being unreliable there.
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

		/// <summary>Builds the progress timeline (design doc §6.3) in its own
		/// parent; call after the data rows so it lands at the bottom.</summary>
		public static void BuildTimeline(Handle h, Transform parent)
		{
			GameObject wrap = SaUi.Go("Timeline", parent);
			VerticalLayoutGroup wrapGroup = SaUi.Vertical(wrap, 0, 2f);
			// The timeline sits directly in dataCol, which carries no horizontal
			// padding of its own (dividers must bleed to the true edges), so it
			// needs the same indent as every row or it renders flush against both
			// window edges. The top padding keeps it off the divider above.
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

			// Uppercased here rather than at each of the three call sites.
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
			// Stellar dive: the vessel is at or in the star itself, with no
			// external light source to cast a day/night arc from. A FULL arc in
			// danger red says "surrounded by the star", where an empty one would
			// read as night, the opposite.
			if (r.BodyIsStar)
			{
				h.surfaceFill.color = SaUi.Danger;
				SetPoints(h.surfaceFill, ArcPoints(new Vector2(0f, -25f), 45f, 180f, 0f));
				h.surfaceSun.gameObject.SetActive(false);
				// No cursor to place: there is no day cycle to track a position
				// within. The day band itself is harmless left where it is.
				h.timelineCursor.gameObject.SetActive(false);
				SetTimeline(h, 0.25f, 0.75f, 0f);
				return;
			}
			// Near the pole: not dangerous, unlike the stellar dive above, so an
			// empty arc as at night rather than a coloured full one. DayProgress01
			// comes from the same unstable longitude, leaving no trustworthy
			// position for a cursor either.
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
			// Zero outside daylight, not just clamped: a clamped value would keep
			// the fill arc full all night and only empty it at the ±180 wrap
			// (midnight) instead of at dusk, where the daylight arc really ends.
			double t = isDay ? Mathf.Clamp01((float)((hourAngle + 90.0) / 180.0)) : 0.0;

			// No points at all when there is no arc to draw: a degenerate
			// 180..180 span is a single repeated point, and the round-join discs
			// would pile up there as a stray dot. Same guard as UpdateOrbit's
			// shadow arc.
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
			// Planet disc: a neutral default, or the body's real map colour behind
			// an opt-in setting. No extra attenuation, since that colour is
			// already stored at half the configured icon brightness.
			h.orbitPlanet.color = SaParams.UseBodyMapColorForDial
				? (r.BodyIsSun ? StarDiscColor : r.BodyMapColorRaw)
				: SaUi.OrbitPlanetDefault;

			float thetaDeg = (float)(r.OrbitThetaNowRad * Rad2Deg);
			float phiDeg = (float)(r.OrbitPhiRad * Rad2Deg);

			// No shadow when orbiting the star itself: phiRad collapses to 0 there,
			// and a zero-width arc would pile every join disc on one spot, drawing
			// a stray dot. Feed no points at all instead.
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
			// classification (design doc §5.3): 0 at the subsolar point, 1 at the
			// antisolar one. Which side is east is the terminator row's job.
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
		/// Explicitly sets all four RectTransform fields to zero-size, centred on
		/// the parent. Go() leaves Unity's implicit anchors at the parent's
		/// bottom-left, and leaving even one field implicit silently breaks the
		/// local-space math these hand-drawn dials rely on.
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
