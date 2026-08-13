using System;
using UnityEngine;

namespace SituationalAwareness.Core
{
	// Binary on purpose (user decision 2026-07-19): a "terminator" phase is a
	// SURFACE concept (crossing a line on the ground); in orbit there is no
	// such line, only sunlit or in-shadow. The ring dial still draws the
	// shadow band as a geometric visual (real umbra half-angle), it just
	// isn't exposed as a third named phase state.
	public enum SaPhaseOrbit { Sunlit, Eclipse }

	/// <summary>
	/// Orbital illumination (design doc §5.1). Verbatim port of RealBattery's
	/// OrbitalIlluminationStatus (post-2026-07-14 fix — avoids three bugs the
	/// earlier split implementation had: sub-solar/anti-solar mixup, mixing
	/// Orbit.GetOrbitNormal()'s internal frame with world-space vectors, and
	/// an unbounded true-anomaly-based dt). Deriving phase and time-to-event
	/// from the same in-plane frame means they can never disagree.
	/// </summary>
	internal static class OrbitIllumination
	{
		/// <summary>Terminator band half-width for the "TERMINATOR" phase (design doc §5.1), degrees.</summary>
		public const double TerminatorBandDeg = 2.0;

		private static double Clamp(double value, double min, double max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}

		private static double WrapTo2Pi(double x)
		{
			double t = x % (2.0 * Math.PI);
			if (t < 0) t += 2.0 * Math.PI;
			return t;
		}

		/// <summary>Sunlit fraction of one full orbit (design doc §5.1: 1 - shadow half-angle/π).</summary>
		public static double LitFraction(Vessel v, CelestialBody body, CelestialBody star)
		{
			// Orbiting the star itself (bug fix 2026-07-24): the formula
			// below assumes "body blocks light from a distant, separate
			// star" — meaningless when body IS the star (a star's own
			// radius is huge, so asin(R/a) would come out non-negligible
			// instead of the true answer, always lit). Same degeneracy
			// Status() already guards against via the antiSun vector
			// collapsing to zero when body == star.
			if (body == star) return 1.0;

			double R = body.Radius;
			double a = v.orbit.semiMajorAxis;
			double s = Clamp(R / Math.Max(a, R + 1.0), 0.0, 1.0);
			double theta = Math.Asin(s);
			return 1.0 - Clamp(theta / Math.PI, 0.0, 1.0);
		}

		/// <summary>
		/// Current phase, time to the next terminator crossing, whether the
		/// vessel is in shadow right now (for the "next event" label:
		/// eclipse vs light), and the raw in-plane angles — thetaNowRad is
		/// the vessel's angle from the shadow-center axis (0 = deepest
		/// shadow), phiRad is the shadow half-angle. Both exposed purely for
		/// the orbit ring dial (design doc §6.2): in this frame the shadow
		/// band is fixed at angle 0 by construction, only the vessel marker
		/// (at thetaNowRad) moves — no extra geometry needed on the UI side.
		/// </summary>
		public static void Status(Vessel v, CelestialBody body, CelestialBody star,
			out SaPhaseOrbit phase, out double timeToTransitionSec, out bool inEclipseNow,
			out double thetaNowRad, out double phiRad)
		{
			Vector3d r_world = v.GetWorldPos3D() - body.position;
			Vector3d antiSun = (body.position - star.position).normalized;

			Vector3d h = Vector3d.Cross(r_world, v.obt_velocity);
			if (h.sqrMagnitude < 1e-12)
			{
				phase = SaPhaseOrbit.Sunlit;
				timeToTransitionSec = double.PositiveInfinity;
				inEclipseNow = false;
				thetaNowRad = Math.PI;
				phiRad = 0.0;
				return;
			}
			h = h.normalized;

			Vector3d aProj = antiSun - Vector3d.Dot(antiSun, h) * h;
			if (aProj.sqrMagnitude < 1e-12)
			{
				phase = SaPhaseOrbit.Sunlit;
				timeToTransitionSec = double.PositiveInfinity;
				inEclipseNow = false;
				thetaNowRad = Math.PI;
				phiRad = 0.0;
				return;
			}
			Vector3d e1 = aProj.normalized;
			Vector3d e2 = Vector3d.Cross(h, e1);

			double x = Vector3d.Dot(r_world, e1);
			double y = Vector3d.Dot(r_world, e2);
			double theta_now = Math.Atan2(y, x);
			thetaNowRad = theta_now;

			double rmag = Math.Max(r_world.magnitude, body.Radius + 1.0);
			double phi = Math.Asin(Clamp(body.Radius / rmag, 0.0, 1.0));
			phiRad = phi;

			inEclipseNow = Math.Abs(theta_now) < phi;
			phase = inEclipseNow ? SaPhaseOrbit.Eclipse : SaPhaseOrbit.Sunlit;

			double d1 = WrapTo2Pi(phi - theta_now);
			double d2 = WrapTo2Pi(-phi - theta_now);
			double dTheta = Math.Min(d1, d2);

			double P = Math.Max(v.orbit.period, 1.0);
			double n = 2.0 * Math.PI / P;
			double dt = dTheta / Math.Max(n, 1e-6);
			timeToTransitionSec = Math.Max(1e-3, Math.Min(dt, P));
		}
	}
}
