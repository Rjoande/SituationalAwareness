using System;
using System.Collections.Generic;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Resolves the correct star for a given body by walking the referenceBody
	/// chain and checking Kopernicus template=Sun. Ported from RealBattery's
	/// KopernicusStarResolver (design doc §4.1) — no Kopernicus dependency,
	/// works with any planet pack, falls back to Planetarium.fetch.Sun when no
	/// Kopernicus data is present (stock system).
	/// </summary>
	internal static class StarResolver
	{
		private static readonly Dictionary<string, (string starName, double luminosity, bool ok)> _cache
			= new Dictionary<string, (string, double, bool)>(StringComparer.OrdinalIgnoreCase);

		private static bool _initialized;
		private static Dictionary<string, ConfigNode> _kopBodyByName;

		/// <summary>
		/// Try resolving the star for a body. luminosity is the Kopernicus
		/// ScaledVersion/Light/luminosity value if present (design doc §4.3:
		/// its exact physical meaning relative to PhysicsGlobals.SolarLuminosity
		/// is NOT verified — do not build a flux formula from it without an
		/// in-game cross-check, see notes/verifiche-api.md §6 and M3).
		/// </summary>
		public static bool TryResolveStar(CelestialBody startBody, out CelestialBody starBody, out double luminosity)
		{
			starBody = null;
			luminosity = 1.0;

			if (startBody == null) return false;

			if (_cache.TryGetValue(startBody.bodyName, out var c))
			{
				if (!c.ok) return false;
				starBody = FlightGlobals.GetBodyByName(c.starName);
				luminosity = c.luminosity;
				return starBody != null;
			}

			EnsureIndex();

			if (_kopBodyByName == null || _kopBodyByName.Count == 0)
			{
				_cache[startBody.bodyName] = (null, 1.0, false);
				return false;
			}

			CelestialBody cur = startBody;
			while (cur != null)
			{
				if (_kopBodyByName.TryGetValue(cur.bodyName, out var kopBodyNode))
				{
					ConfigNode template = kopBodyNode.GetNode("Template");
					if (template != null)
					{
						string tName = template.GetValue("name");
						if (!string.IsNullOrEmpty(tName) && string.Equals(tName, "Sun", StringComparison.OrdinalIgnoreCase))
						{
							starBody = cur;
							luminosity = ReadLuminosityOrDefault(kopBodyNode, 1360.0);
							_cache[startBody.bodyName] = (starBody.bodyName, luminosity, true);
							return true;
						}
					}
				}

				cur = cur.referenceBody;
			}

			_cache[startBody.bodyName] = (null, 1.0, false);
			return false;
		}

		private static void EnsureIndex()
		{
			if (_initialized) return;
			_initialized = true;

			try
			{
				_kopBodyByName = new Dictionary<string, ConfigNode>(StringComparer.OrdinalIgnoreCase);

				ConfigNode[] roots = GameDatabase.Instance.GetConfigNodes("Kopernicus");
				if (roots == null || roots.Length == 0) return;

				foreach (ConfigNode root in roots)
				{
					if (root == null) continue;

					ConfigNode[] bodies = root.GetNodes("Body");
					if (bodies == null) continue;

					foreach (ConfigNode b in bodies)
					{
						if (b == null) continue;

						string bodyName = b.GetValue("name");
						if (string.IsNullOrEmpty(bodyName)) continue;

						if (!_kopBodyByName.ContainsKey(bodyName))
							_kopBodyByName[bodyName] = b;
					}
				}
			}
			catch (Exception)
			{
				_kopBodyByName = null;
			}
		}

		private static double ReadLuminosityOrDefault(ConfigNode kopBodyNode, double def)
		{
			try
			{
				ConfigNode scaled = kopBodyNode.GetNode("ScaledVersion");
				ConfigNode light = scaled?.GetNode("Light");
				string lumStr = light?.GetValue("luminosity");
				if (!string.IsNullOrEmpty(lumStr) && double.TryParse(lumStr, out double lum) && lum > 0.0)
					return lum;
			}
			catch { /* ignore, use default */ }

			return def;
		}
	}
}
