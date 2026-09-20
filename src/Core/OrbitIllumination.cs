using System;
using UnityEngine;

namespace SituationalAwareness.Core
{
	// Binary on purpose: a terminator phase is a SURFACE concept (crossing a line
	// on the ground), and in orbit there is only sunlit or in-shadow. The ring
	// dial still draws the shadow band, just not as a third named phase.
	public enum SaPhaseOrbit { Sunlit, Eclipse }

	/// <summary>
	/// Orbital illumination (design doc §5.1), a verbatim port of RealBattery's
	/// OrbitalIlluminationStatus. Phase and time-to-event come from the same
	/// in-plane frame, so they can never disagree.
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
			// Orbiting the star itself: the formula below assumes a body blocking
			// light from a distant separate star, so asin(R/a) on the star's own
			// huge radius would shadow an orbit that is in fact always lit.
			if (body == star) return 1.0;

			double R = body.Radius;
			double a = v.orbit.semiMajorAxis;
			double s = Clamp(R / Math.Max(a, R + 1.0), 0.0, 1.0);
			double theta = Math.Asin(s);
			return 1.0 - Clamp(theta / Math.PI, 0.0, 1.0);
		}

		/// <summary>
		/// Current phase, time to the next terminator crossing, whether the vessel
		/// is in shadow right now, and the raw in-plane angles: thetaNowRad is the
		/// vessel's angle from the shadow-center axis (0 = deepest shadow), phiRad
		/// the shadow half-angle. The angles exist for the ring dial (design doc
		/// §6.2), where the shadow band sits at angle 0 by construction and only
		/// the vessel marker moves.
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
