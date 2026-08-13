using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SituationalAwareness.UI
{
	/// <summary>
	/// Draws a polyline through local-space points as a triangle strip in
	/// OnPopulateMesh — pure UGUI, no external asset (ported pattern from
	/// KRAB's KrabCurveLine.cs, design doc §6.2: dials are drawn, never
	/// Unicode glyphs — lesson from KRAB's non-rendering ⟳).
	/// </summary>
	internal class SaVectorLine : MaskableGraphic
	{
		private readonly List<Vector2> points = new List<Vector2>();
		public float thickness = 3f;
		public bool dashed;
		public float dashLength = 4f;
		public float gapLength = 3f;

		public void SetPoints(IList<Vector2> newPoints)
		{
			points.Clear();
			points.AddRange((IEnumerable<Vector2>)newPoints);
			SetVerticesDirty();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();
			if (points.Count < 2) return;

			if (!dashed)
			{
				for (int i = 0; i < points.Count - 1; i++)
				{
					AddSegment(vh, points[i], points[i + 1]);
				}
                // Round joins at interior vertices (bug fix 2026-07-22, orbit
                // dial "puntini"): each segment is its own quad, extruded
                // perpendicular to ITS OWN direction. On a curved polyline
                // consecutive segments point in slightly different
                // directions, so their quads don't share an edge at the
                // joint — a wedge-shaped gap opens at every vertex, sized by
                // the local turn angle. More segments (24->90, earlier fix)
                // shrinks each gap but can't remove it; a small disc at each
                // joint closes it regardless of turn direction. Previously
                // this let whatever was drawn underneath (e.g. the lit-color
                // base ring under the shadow arc) show through as dots.
                for (int i = 1; i < points.Count - 1; i++)
                {
					AddJoin(vh, points[i]);
				}
				return;
			}

			float carry = 0f;
			for (int i = 0; i < points.Count - 1; i++)
			{
				Vector2 a = points[i];
				Vector2 b = points[i + 1];
				Vector2 seg = b - a;
				float segLen = seg.magnitude;
				if (segLen < 1e-4f) continue;
				Vector2 dir = seg / segLen;
				float pos = -carry;
				bool draw = carry < dashLength;
				while (pos < segLen)
				{
					float next = Mathf.Min(pos + (draw ? dashLength - Mathf.Max(0f, carry) : gapLength), segLen);
					if (draw && next > 0f)
					{
						AddSegment(vh, a + dir * Mathf.Max(pos, 0f), a + dir * next);
					}
					pos = next;
					draw = !draw;
					carry = 0f;
				}
				carry = draw ? 0f : segLen - pos + gapLength;
			}
		}

		private void AddSegment(VertexHelper vh, Vector2 a, Vector2 b)
		{
			Vector2 dir = b - a;
			if (dir.sqrMagnitude < 1e-6f) return;
			dir.Normalize();
			Vector2 normal = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);

			UIVertex v0 = UIVertex.simpleVert; v0.color = color; v0.position = a - normal;
			UIVertex v1 = UIVertex.simpleVert; v1.color = color; v1.position = a + normal;
			UIVertex v2 = UIVertex.simpleVert; v2.color = color; v2.position = b + normal;
			UIVertex v3 = UIVertex.simpleVert; v3.color = color; v3.position = b - normal;

			int idx = vh.currentVertCount;
			vh.AddVert(v0); vh.AddVert(v1); vh.AddVert(v2); vh.AddVert(v3);
			vh.AddTriangle(idx, idx + 1, idx + 2);
			vh.AddTriangle(idx, idx + 2, idx + 3);
		}

		/// <summary>Small filled disc (fan, same pattern as SaVectorDot) covering a segment joint's gap.</summary>
		private void AddJoin(VertexHelper vh, Vector2 center)
		{
			const int JoinSegments = 8;
			float r = thickness * 0.5f;

			int idx = vh.currentVertCount;
			UIVertex mid = UIVertex.simpleVert; mid.color = color; mid.position = center;
			vh.AddVert(mid);
			for (int i = 0; i <= JoinSegments; i++)
			{
				float ang = i * Mathf.PI * 2f / JoinSegments;
				UIVertex v = UIVertex.simpleVert;
				v.color = color;
				v.position = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
				vh.AddVert(v);
				if (i > 0) vh.AddTriangle(idx, idx + i, idx + i + 1);
			}
		}
	}

	/// <summary>Filled circle marker (sun/vessel dot) as a triangle fan — same no-asset approach as SaVectorLine.</summary>
	internal class SaVectorDot : MaskableGraphic
	{
		public Vector2 center;
		public float radius = 4f;
		private const int Segments = 20;

		public void SetPosition(Vector2 newCenter, float newRadius)
		{
			center = newCenter;
			radius = newRadius;
			SetVerticesDirty();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();
			if (radius <= 0f) return;

			UIVertex mid = UIVertex.simpleVert; mid.color = color; mid.position = center;
			vh.AddVert(mid);
			for (int i = 0; i <= Segments; i++)
			{
				float ang = i * Mathf.PI * 2f / Segments;
				UIVertex v = UIVertex.simpleVert;
				v.color = color;
				v.position = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
				vh.AddVert(v);
				if (i > 0) vh.AddTriangle(0, i, i + 1);
			}
		}
	}
}
