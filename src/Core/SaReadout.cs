using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>Off = stock None (no connection at all), Red = stock Red
	/// (connected but very weak).</summary>
	public enum SaSignalLevel { Off, Red, Yellow, Green }

	/// <summary>
	/// All data the panel needs for one refresh, as pure numbers and enums: no
	/// formatting, no strings beyond raw names (design doc §7, all math lives in
	/// Core). Only the fields relevant to .Mode are meaningful.
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
		// Sol means nothing useful on the home body (design doc §3.4): the window
		// blanks that line there.
		public bool IsHomeBody;
		public string VesselName;

		// KSC clock: local time at the KSC's ACTUAL coordinates on the home body
		// (SpaceCenter.Instance, so it follows a pack that relocates the KSC),
		// computed like any other local time rather than as "UT mod day".
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
		// True above SaReadoutProvider.NearPoleLatitudeThresholdDeg (design doc
		// §5.5). With zero axial tilt there is no polar day/night to model, but
		// longitude — and azimuth, local time, timezone, the day/night dial with
		// it — turns numerically unstable where the meridians converge.
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
		// SOLAR TIME: TRUE/apparent solar time at the vessel's exact longitude.
		// Unlike LocalHour/Minute/Second above (mean time, zone-quantized) this is
		// the live continuous DayFraction formula, never calibrated. Surface mode
		// only, behind the showSolarTime toggle.
		public int SolarHour;
		public int SolarMinute;
		public int SolarSecond;
		// Mean-minus-true clock gap here and now: the SOLAR TIME row's second
		// click-cycled format, "±mm:ss".
		public double EquationOfTimeSec;
		// The sunrise/sunset countdown of TimeToNextEventSec, but from the
		// vessel's exact longitude instead of the zone centre. Shown only while
		// SOLAR TIME is on, where exact-position time is available anyway.
		public double SolarTimeToNextEventSec;
		public bool SolarNextEventIsSunrise;
		public double OrbitLitFraction01;
		public bool NextOrbitEventIsEclipse;
		// Escape trajectory: the eclipse-transition math is meaningless there, as
		// the vessel leaves this SoI before any predicted crossing. When true,
		// TimeToNextEventSec counts down to the SoI change instead (vessel.orbit
		// .EndUT, the solver's live prediction for the current patch) and
		// OrbitPeriodSec is forced to infinity, a period that never completes.
		public bool NextOrbitEventIsSoiChange;
		// Raw in-plane angles for the orbit ring dial (design doc §6.2); frame
		// convention in OrbitIllumination.Status.
		public double OrbitThetaNowRad;
		public double OrbitPhiRad;
		// Orbital period (design doc §5.1), shown next to the countdown clock;
		// overridden on an escape trajectory, see NextOrbitEventIsSoiChange.
		public double OrbitPeriodSec;
		public double TerminatorDistanceKm;
		public double TerminatorDistanceDeg;
		public bool TerminatorToEast;
		// Signed hour angle from the subsolar meridian (0 = subsolar, ±180 =
		// antisolar): drives both the tidal-lock phase and SaDial's timeline bar.
		public double TidalLockHourAngleDeg;

		// Body / context
		public string BodyName;
		// CelestialBody.bodyName, the INTERNAL identifier, not the display name
		// above: config-driven per-body lookups key on this, because display names
		// are localized and packs rename them freely.
		public string BodyNameInternal;
		public string StarName;
		public double SolarDayLengthSec;
		public bool BodyTidallyLocked;
		public bool IsStarLocked;
		public bool BodyHasAtmosphere;
		// True when the orbited/landed body IS a star: it has no solar day
		// relative to itself, so the footer hides that segment.
		public bool BodyIsStar;
		// True only for the ROOT star (bodyName "Sun", the internal identifier
		// every planet pack keeps). NOT the same as BodyIsStar: a secondary star
		// really orbits the root Sun and has its own OrbitRendererDataCache entry,
		// whereas the root Sun has none.
		public bool BodyIsSun;
		// Star -> ... -> current body, star INCLUDED (design doc §5, footer
		// "STAR // PLANET // MOON"). At most ~4 levels, rebuilt once per refresh.
		public CelestialBody[] BodyChain;
		// Map/orbit-line colour, from PSystemManager.OrbitRendererDataCache: this
		// is where Kopernicus writes the configured colour, never onto OrbitDriver
		// (whose orbitColor stays at its grey default). Already stored at half the
		// configured icon brightness, so the UI applies it without attenuation.
		public Color BodyMapColorRaw;

		// CommNet
		public SaSignalLevel SignalLevel;
		public bool IsConnected;

		// Live = getGeeForceAtPosition, under the same range gate as the stock
		// gravimeter (altitude <= referenceBody.Radius * 3); ASL = body.GeeASL in
		// m/s^2, a fixed per-body reference whatever the current altitude.
		public bool GravityLiveValid;
		public double GravityLiveMps2;
		public double GravityAslMps2;

		// Hull temperature: skinThermalMass-weighted average of skinTemperature,
		// the OUTER shell rather than the core. The skin is what responds to
		// reentry heating, while the core lags behind through slow conduction and
		// stays deceptively low right up to a fatal overheat.
		// HullTempWorstRatio is the single worst part's max(T/maxTemp,
		// skinT/skinMaxTemp), the same quantity KSP's own heat gauge uses: a
		// relative "how close to melting" figure, since an absolute temperature
		// means nothing for a part designed to run hot. Always valid in flight,
		// unlike ExternalTemperatureK, which reads a constant in a vacuum.
		public double HullTempK;
		public double HullTempWorstRatio;

		// State stays Unknown — and the section hides — without EVE volumetric
		// clouds installed, or on a body with no atmosphere.
		public SaWeatherReadout Weather;

		// Whether the panel may SHOW each gated readout on this body. The values
		// above are always computed: the gate is presentation only, so companions
		// and the extension point keep seeing real numbers.
		public bool ExtTempUnlocked;
		public bool PressureUnlocked;
		public bool GravityUnlocked;
		public bool WeatherUnlocked;
	}
}
