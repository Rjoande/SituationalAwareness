using System;

namespace SituationalAwareness.Core
{
	public enum SaMode { Surface, Orbit, TidalLock }

	/// <summary>
	/// Selects the panel mode for a vessel (design doc §2). Single entry
	/// point on purpose — the M3 polishing items (AGL threshold so a low EVA
	/// hop on an airless body doesn't flip to Orbit, suborbital hops) hook in
	/// here without touching any other file.
	/// </summary>
	internal static class SaModeSelector
	{
		public static SaMode Select(Vessel vessel)
		{
			CelestialBody body = vessel.mainBody;

			// Tidal lock on the star only changes what "on the ground" means
			// (no local time, sun frozen in the sky) — it says nothing about
			// orbital mechanics. A vessel ORBITING such a body still has a
			// perfectly normal Sunlit/Terminator/Eclipse cycle (the body's
			// own axial lock doesn't affect its orbital motion or shadow).
			// Bug found in M1 test (fase 5): checking tidal lock before the
			// situation used to force TidalLock even in orbit.
			if (!IsSurfaceSituation(vessel.situation) && !IsLowSubOrbital(vessel, body))
			{
				return SaMode.Orbit;
			}

			return IsTidalLockedOnStar(body) ? SaMode.TidalLock : SaMode.Surface;
		}

		private static bool IsSurfaceSituation(Vessel.Situations situation)
		{
			switch (situation)
			{
				case Vessel.Situations.LANDED:
				case Vessel.Situations.SPLASHED:
				case Vessel.Situations.PRELAUNCH:
				case Vessel.Situations.FLYING:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// M3 point 4 (design doc §9, decided 2026-07-25, go 2026-07-28): a
		/// SUB_ORBITAL trajectory low enough still reads as Surface — a short
		/// hop, not a real orbital insertion. Deliberately keyed on the
		/// vessel's CURRENT altitude (ASL), not apoapsis: apoapsis describes
		/// where the arc is headed, not where the vessel actually is right
		/// now, and a panel about "what's around the vessel" should track the
		/// latter. Deliberately keyed on the stock SUB_ORBITAL situation, not
		/// altitude alone: a real ORBITING vessel is never touched by this
		/// rule regardless of altitude (an orbit that has already completed
		/// one revolution is not a "hop" no matter how low it is).
		///
		/// Threshold (refined 2026-07-28): `Max(body.Radius *
		/// SurfaceAltitudeThresholdMultiplier, body.atmosphereDepth)` — a
		/// body with a tall atmosphere must never flip to Orbit mode while
		/// still deep inside it on a suborbital arc (EXT TEMP/PRESSURE would
		/// vanish mid-reentry-style flight); a body with no atmosphere (or a
		/// thin one under the radius fraction) falls back to the plain
		/// radius-fraction reading. `atmosphereDepth` verified public on the
		/// decompiled CelestialBody.cs (0.0 when `atmosphere` is false).
		/// </summary>
		private static bool IsLowSubOrbital(Vessel vessel, CelestialBody body)
		{
			if (vessel.situation != Vessel.Situations.SUB_ORBITAL || body == null) return false;
			double threshold = Math.Max(body.Radius * SaParams.SurfaceAltitudeThresholdMultiplier, body.atmosphereDepth);
			return vessel.altitude <= threshold;
		}

		/// <summary>
		/// True only when the body is locked ONTO ITS STAR — the flag alone
		/// is not enough (a moon like Mun is tidallyLocked on its planet but
		/// has a normal solar day, design doc §2/§5.4).
		/// </summary>
		public static bool IsTidalLockedOnStar(CelestialBody body)
		{
			if (body == null || !body.tidallyLocked) return false;
			CelestialBody parent = body.referenceBody;
			return parent != null && parent.isStar;
		}
	}
}
