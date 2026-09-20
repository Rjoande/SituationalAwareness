using System;
using SituationalAwareness.Core;
using UnityEngine;

namespace SituationalAwareness.Extensibility
{
	/// <summary>
	/// SA's public UI-extension surface, and deliberately the only one: SaWindow,
	/// SaUi and SaDial stay internal, so an outside DLL only ever touches this
	/// class.
	/// </summary>
	public static class SaExtensionPoint
	{
		/// <summary>
		/// Fired every time the weather section under the dial is (re)built, with
		/// a Transform ready for external content. Present in Surface/TidalLock
		/// only, never in Orbit.
		/// Subscribers must (re)populate on every firing, not just once: SA
		/// destroys and rebuilds this Transform on every collapsed&lt;-&gt;extended
		/// toggle and every window re-open.
		/// </summary>
		public static event Action<Transform> OnWeatherHostBuilt;

		/// <summary>
		/// Fired alongside <see cref="OnWeatherHostBuilt"/>, with a small square
		/// Transform (14x14) anchored to the TOP-RIGHT CORNER of the weather
		/// section and excluded from its layout: it holds one compact button, not
		/// a panel. Content floats over the section rather than displacing it, so
		/// anything larger overlaps SA's own readout.
		/// While SA's "Enable weather report" setting is on AND someone subscribes
		/// here, the section stays visible as UNKNOWN on the surface of any body
		/// instead of hiding, so the button remains reachable. Never present in
		/// orbit or in the collapsed strip.
		/// Same re-subscribe contract as OnWeatherHostBuilt.
		/// </summary>
		public static event Action<Transform> OnWeatherCornerBuilt;

		internal static void Raise(Transform host) => OnWeatherHostBuilt?.Invoke(host);

		internal static void RaiseCorner(Transform corner) => OnWeatherCornerBuilt?.Invoke(corner);

		/// <summary>Whether anyone is listening for the corner slot: with no
		/// companion installed there is no reason to keep an UNKNOWN weather
		/// section on screen.</summary>
		internal static bool HasCornerSubscriber => OnWeatherCornerBuilt != null;

		/// <summary>
		/// What SA's classifier currently reports for this vessel, as a plain
		/// uppercase token ("CLEAR", "CLOUDY", "FOG", "RAIN", "SNOW",
		/// "THUNDERSTORM", "DUSTSTORM"), or "UNKNOWN" when SA has nothing to say
		/// (no EVE volumetric clouds installed, airless body, no vessel).
		///
		/// A string rather than the internal enum on purpose: the enum can gain
		/// states without breaking a companion built against an older SA.
		/// </summary>
		public static string CurrentWeather(Vessel vessel)
		{
			if (vessel == null) return "UNKNOWN";
			SaWeatherReadout readout = WeatherClassifier.Classify(vessel, Planetarium.GetUniversalTime());
			return readout.State == SaWeatherState.Unknown
				? "UNKNOWN"
				: readout.State.ToString().ToUpperInvariant();
		}

		/// <summary>
		/// The raw numbers behind <see cref="CurrentWeather"/>: the sky's optical
		/// depth and EVE's own precipitation gate at the vessel, the two
		/// quantities every threshold in the classifier is expressed in.
		/// </summary>
		public static void CurrentWeatherDetail(Vessel vessel, out double skyOpticalDepth,
			out double precipIntensity01, out string dominantLayer)
		{
			skyOpticalDepth = 0.0;
			precipIntensity01 = 0.0;
			dominantLayer = "";
			if (vessel == null) return;
			SaWeatherReadout readout = WeatherClassifier.Classify(vessel, Planetarium.GetUniversalTime());
			skyOpticalDepth = readout.SkyOpticalDepth;
			precipIntensity01 = readout.PrecipIntensity01;
			dominantLayer = readout.DominantLayer ?? "";
		}
	}
}
