using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Off = stock None (no connection at all, grey per stock convention).
	/// Red = stock Red (connected but very weak). Verified thresholds in
	/// notes/verifiche-api.md §7.
	/// </summary>
	public enum SaSignalLevel { Off, Red, Yellow, Green }

	/// <summary>
	/// All data the panel needs for one refresh, as pure numbers/enums — zero
	/// formatting, zero strings beyond raw names (design doc §7: the window
	/// only reads this, all math lives in Core). Only the fields relevant to
	/// .Mode are meaningful; the rest keep their default value.
	/// </summary>
	internal struct SaReadout
	{
		public bool Valid;
		public SaMode Mode;

		// Time
		public double UT;
		public double MissionTime;
		public int LocalHour;
		public int LocalMinute;
		public int LocalSecond;
		public int TimeZoneIndex;
		public int Sol;
		// Sol doesn't mean anything useful on the home body (design doc §3.4):
		// the window blanks that line there instead of showing it.
		public bool IsHomeBody;
		public string VesselName;

		// KSC clock: local time AT THE KSC'S ACTUAL COORDINATES on the home
		// body (SpaceCenter.Instance.Latitude/Longitude — dynamic, correct
		// even when a planet pack relocates the KSC), computed the same way
		// as any other local time, NOT "UT mod day" (that was a bug: it
		// showed the same value as UT and ignored the KSC's real longitude).
		public bool KscTimeValid;
		public int KscHour;
		public int KscMinute;
		public int KscSecond;
		public int KscTimeZoneIndex;

		// Position
		public double Latitude;
		public double Longitude;
		public double Altitude;
		public bool AltitudeAglValid;
		public double AltitudeAglM;
		public string BiomeName;
		// M3 point 5 (design doc §5.5/§9, go 2026-07-28): true above
		// SaReadoutProvider.NearPoleLatitudeThresholdDeg. With zero axial
		// tilt the sun still crosses the horizon at every latitude (no real
		// polar day/night to model), but longitude — and everything derived
		// from it (azimuth, local time, timezone, the day/night dial) —
		// becomes numerically unstable right at the pole, where meridians
		// converge. Computed once here instead of re-checking the latitude
		// threshold at every UI call site.
		public bool NearPole;

		// Sun / atmosphere
		public double SunElevationDeg;
		public double SunAzimuthDeg;
		public double SolarFluxWm2;
		public double ExternalTemperatureK;
		public double PressureKPa;

		// Phase / progress (only the field matching Mode is populated)
		public SaPhaseSurface PhaseSurface;
		public SaPhaseOrbit PhaseOrbit;
		public SaPhaseTidalLock PhaseTidalLock;
		public double DayProgress01;
		public double TimeToNextEventSec;
		public bool NextEventIsSunrise;
		// SOLAR TIME (M3 point 6, go 2026-07-28): TRUE/apparent solar time
		// at the vessel's EXACT longitude — unlike LocalHour/Minute/Second
		// above (mean time, zone-quantized), this is the live, continuous
		// DayFraction formula, never calibrated. Surface mode only, behind
		// the SA_settings_showSolarTime toggle (default OFF).
		public int SolarHour;
		public int SolarMinute;
		public int SolarSecond;
		// Mean-minus-true clock gap at the vessel's location/UT right now
		// (SolarMath.EquationOfTimeSeconds) — the SOLAR TIME row's second
		// click-cycled display format, "±mm:ss".
		public double EquationOfTimeSec;
		// Same alba/tramonto countdown as TimeToNextEventSec/NextEventIsSunrise,
		// but from the vessel's exact longitude instead of the zone center —
		// only used for display when SOLAR TIME is toggled on (user request
		// 2026-07-28: more precise than the zone-quantized default once you
		// have exact-position time available anyway).
		public double SolarTimeToNextEventSec;
		public bool SolarNextEventIsSunrise;
		public double OrbitLitFraction01;
		public bool NextOrbitEventIsEclipse;
		// Escape trajectory (retest 2026-07-27, user report: on an escape
		// path the eclipse-transition math is meaningless — the vessel
		// leaves this SoI before any predicted eclipse/light crossing can
		// happen, "un tempo inesistente"). When true, TimeToNextEventSec is
		// repurposed to count down to the SoI change instead (from
		// vessel.orbit.EndUT, the patched-conic solver's own live
		// prediction for the CURRENT patch — verified on the decompiled
		// PatchedConicSolver.Update(): patch 0 is always the vessel's
		// current orbit, recomputed every frame regardless of maneuver
		// nodes or map view), and OrbitPeriodSec is forced to
		// PositiveInfinity (a "period" that will never actually complete
		// isn't a real period, even though the stock UI keeps showing the
		// raw two-body value).
		public bool NextOrbitEventIsSoiChange;
		// Raw in-plane angles for the orbit ring dial (§6.2) — see
		// OrbitIllumination.Status doc comment for the frame convention.
		public double OrbitThetaNowRad;
		public double OrbitPhiRad;
		// Orbital period (design doc §5.1, M3 restyling): shown as "Period
		// HH:MM:SS" next to the countdown clock. See NextOrbitEventIsSoiChange
		// doc comment for the escape-trajectory override.
		public double OrbitPeriodSec;
		public double TerminatorDistanceKm;
		public double TerminatorDistanceDeg;
		public bool TerminatorToEast;
		// Signed hour angle from the subsolar meridian (0 = subsolar,
		// ±180 = antisolar) — drives both the phase classification in
		// SaReadoutProvider.BuildTidalLock and the timeline bar in SaDial.
		public double TidalLockHourAngleDeg;

		// Body / context
		public string BodyName;
		public string StarName;
		public double SolarDayLengthSec;
		public bool BodyTidallyLocked;
		public bool IsStarLocked;
		public bool BodyHasAtmosphere;
		// True when the orbited/landed body IS a star (e.g. orbiting Kerbol
		// or Grannus directly) — a star has no "solar day" relative to
		// itself, so the footer hides that segment (bug fix 2026-07-22).
		public bool BodyIsStar;
		// True only for the ROOT star specifically (CelestialBody.bodyName
		// == "Sun" — the internal identifier every planet pack must keep,
		// like "Kerbin" for the home body; independent of displayName/the
		// Kerbol rename toggle). NOT the same as BodyIsStar: a secondary
		// star (e.g. Grannus) orbits the root Sun for real and already has
		// a meaningful PSystemManager.OrbitRendererDataCache entry with its
		// own color — only the root Sun has none (bug fix 2026-07-24).
		public bool BodyIsSun;
		// Star -> ... -> current body, star INCLUDED (design doc §5/footer
		// rework: "STAR // PLANET // MOON"). Small (≤4 levels), rebuilt once
		// per refresh — negligible at the 10 Hz throttle (§6.3 refresh fix).
		public CelestialBody[] BodyChain;
		// Map/orbit-line color for this body, from
		// PSystemManager.OrbitRendererDataCache[body].orbitColor (bug fix
		// 2026-07-22: CelestialBody.orbitDriver.orbitColor, used before, is
		// an unrelated field on a different component that always sits at
		// its Color.grey default — Kopernicus writes the configured color
		// onto OrbitRenderer/OrbitRendererData instead, never OrbitDriver;
		// verified by decompiling Kopernicus.dll's OrbitLoader). Kopernicus
		// (and stock for un-patched bodies) already stores this at half the
		// configured icon brightness, so the UI applies it directly with no
		// extra attenuation (feature request, test M2 retest).
		public Color BodyMapColorRaw;

		// CommNet
		public SaSignalLevel SignalLevel;
		public bool IsConnected;

		// Gravity (M2 feature, verified against ModuleEnviroSensor/GRAV):
		// live = FlightGlobals.getGeeForceAtPosition, same range gate as the
		// stock sensorGravimeter (altitude <= referenceBody.Radius * 3);
		// ASL = body.GeeASL converted to m/s^2, a fixed per-body reference
		// value regardless of current altitude.
		public bool GravityLiveValid;
		public double GravityLiveMps2;
		public double GravityAslMps2;

		// Hull temperature (M3 restyling, user request; corrected in retest
		// 2026-07-27): skinThermalMass-weighted average of skinTemperature —
		// the OUTER layer, not the internal/core one — across every part.
		// "HULL" names the exterior shell, and the skin is what actually
		// responds to reentry/hypersonic heating (the core lags far behind
		// via slow internal conduction); a core-weighted average stayed
		// deceptively low right up to a fatal overheat. skinThermalMass is
		// its own distinct field on Part (not thermalMass, which KSP itself
		// treats as internal-only once corrected for skin — verified on the
		// decompiled FlightIntegrator: `thermalMass = Max(thermalMass -
		// skinThermalMass, 0.1)`). Plus the single worst part's
		// max(T/maxTemp, skinT/skinMaxTemp) ratio, used to color the row (a
		// relative "how close to melting" indicator — an absolute Kelvin/
		// Celsius threshold means nothing for a part designed to run hot,
		// like a heat shield); same quantity KSP's own HeatGaugeUpdate() and
		// overheat-explosion roll use. Always valid in flight, unlike
		// ExternalTemperatureK which reads a hardcoded constant in a vacuum
		// (see SaReadoutProvider.BuildHullTemperature doc comment).
		public double HullTempK;
		public double HullTempWorstRatio;
	}
}
