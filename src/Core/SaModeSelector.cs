using System;

namespace SituationalAwareness.Core
{
	public enum SaMode { Surface, Orbit, TidalLock }

	/// <summary>
	/// Selects the panel mode for a vessel (design doc §2). Single entry point
	/// on purpose, so altitude/situation refinements hook in here alone.
	/// </summary>
	internal static class SaModeSelector
	{
		public static SaMode Select(Vessel vessel)
		{
			CelestialBody body = vessel.mainBody;

			// Situation first, tidal lock second: the lock only changes what "on
			// the ground" means (no local time, frozen sun) and says nothing about
			// orbital mechanics — a vessel ORBITING such a body still has a normal
			// Sunlit/Terminator/Eclipse cycle.
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
		/// A low enough SUB_ORBITAL trajectory still reads as Surface: a hop, not
		/// an orbital insertion (design doc §9). Keyed on CURRENT altitude rather
		/// than apoapsis, because the panel describes where the vessel is, not
		/// where its arc is headed; and on the stock SUB_ORBITAL situation, so a
		/// real orbit is never caught by this rule however low it flies.
		///
		/// The atmosphereDepth half of the threshold (0 on an airless body) keeps
		/// a vessel deep inside a tall atmosphere out of Orbit mode, where EXT
		/// TEMP/PRESSURE would vanish mid-flight.
		/// </summary>
		private static bool IsLowSubOrbital(Vessel vessel, CelestialBody body)
		{
			if (vessel.situation != Vessel.Situations.SUB_ORBITAL || body == null) return false;
			double threshold = Math.Max(body.Radius * SaParams.SurfaceAltitudeThresholdMultiplier, body.atmosphereDepth);
			return vessel.altitude <= threshold;
		}

		/// <summary>
		/// True only when the body is locked ONTO ITS STAR: the flag alone is not
		/// enough, since a moon like Mun is tidallyLocked on its planet yet has a
		/// normal solar day (design doc §2/§5.4).
		/// </summary>
		public static bool IsTidalLockedOnStar(CelestialBody body)
		{
			if (body == null || !body.tidallyLocked) return false;
			CelestialBody parent = body.referenceBody;
			return parent != null && parent.isStar;
		}
	}
}
