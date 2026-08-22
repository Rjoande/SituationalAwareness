using System;
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

		internal static void Raise(Transform host) => OnWeatherHostBuilt?.Invoke(host);
	}
}
