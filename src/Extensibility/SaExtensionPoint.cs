using System;
using SituationalAwareness.Core;
using UnityEngine;

namespace SituationalAwareness.Extensibility
{
	/// <summary>
	/// SA's public UI-extension surface (notes/indagine-meteo.md §8, community
	/// weather survey companion). Deliberately the ONLY public surface for
	/// outside DLLs: SaWindow/SaUi/SaDial stay internal, external code only
	/// ever touches this class.
	/// </summary>
	public static class SaExtensionPoint
	{
		/// <summary>
		/// Fired every time the "weather" section under the dial (Surface/
		/// TidalLock only, never Orbit — SA itself hides the host outside
		/// those modes) is (re)built, with a Transform ready for external
		/// content — the same area that will later host SA's own native
		/// weather icon too, not just a survey companion's button row.
		/// Subscribers must (re)populate on every firing, not just once: SA
		/// destroys and rebuilds this Transform on every collapsed&lt;-&gt;extended
		/// toggle and every window re-open.
		/// </summary>
		public static event Action<Transform> OnWeatherHostBuilt;

		/// <summary>
		/// Fired alongside <see cref="OnWeatherHostBuilt"/>, with a small square
		/// Transform (14x14) anchored to the TOP-RIGHT CORNER of SA's own
		/// weather section and excluded from its layout — meant for a single
		/// compact button (a companion opening its own window), not for a
		/// panel. Content here floats over the section instead of displacing
		/// it, so anything larger will overlap SA's own readout; toggling the
		/// slot on and off moves nothing.
		/// The section normally hides itself where SA has nothing to say
		/// (orbit, an ordinary airless moon); while SA's own "Enable weather
		/// report" setting is on AND someone subscribes here, SA keeps the
		/// section visible as UNKNOWN on the surface of any body instead, so
		/// the button stays reachable exactly where a report is most useful.
		/// Not present in orbit or in the collapsed strip.
		/// Same re-subscribe contract as OnWeatherHostBuilt: SA rebuilds this
		/// on every collapse toggle and window re-open.
		/// </summary>
		public static event Action<Transform> OnWeatherCornerBuilt;

		internal static void Raise(Transform host) => OnWeatherHostBuilt?.Invoke(host);

		internal static void RaiseCorner(Transform corner) => OnWeatherCornerBuilt?.Invoke(corner);

		/// <summary>Whether anyone is listening for the corner slot at all —
		/// without a companion installed there is no reason to keep an
		/// UNKNOWN weather section on screen for the report.</summary>
		internal static bool HasCornerSubscriber => OnWeatherCornerBuilt != null;

		/// <summary>
		/// What SA's weather classifier currently reports for this vessel, as a
		/// plain uppercase token ("CLEAR", "CLOUDY", "FOG", "RAIN", "SNOW",
		/// "THUNDERSTORM", "DUSTSTORM"), or "UNKNOWN" when SA has nothing to
		/// say (no EVE volumetric clouds installed, airless body, no vessel).
		///
		/// Exists for the survey companion (notes/indagine-meteo.md §8.6): once
		/// the companion is a diagnostic tool rather than a data drive, a report
		/// is only useful if it records what SA CLAIMED alongside what the
		/// player actually saw. A string rather than the internal enum on
		/// purpose — the enum can gain states without breaking a companion
		/// built against an older SA.
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
		/// The raw numbers behind <see cref="CurrentWeather"/>: the sky's
		/// optical depth and EVE's own precipitation gate at the vessel, the two
		/// quantities every threshold in the classifier is expressed in. A
		/// companion recording these makes a "the weather readout is wrong"
		/// report actionable instead of anecdotal.
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
