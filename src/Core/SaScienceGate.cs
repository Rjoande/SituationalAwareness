using System;
using System.Collections.Generic;
using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Ties a readout to science actually done on the body you are at: the panel
	/// shows the outside temperature once a thermometer reading from this body has
	/// been credited, pressure after a barometer reading, and so on. A number you
	/// have not measured yet shows as "???".
	///
	/// Config-driven, one node per (field, experiment) pair, several nodes on the
	/// same field OR together, so a pack that adds its own instruments can open a
	/// field with a ModuleManager patch and no code change:
	///
	/// <code>
	/// SA_SCIENCE_GATE
	/// {
	///     field = extTemp             // extTemp | pressure | gravity | weather
	///     experiment = temperatureScan // EXPERIMENT_DEFINITION id
	/// }
	/// </code>
	///
	/// "Done" means CREDITED (ScienceSubject.science &gt; 0), not merely run: KSP
	/// registers a subject the moment an experiment is deployed, so existence
	/// alone would open a field for a result that was then thrown away. Situation
	/// and biome are ignored: one thermometer reading anywhere on Duna is enough
	/// to know Duna's air.
	///
	/// Off in Sandbox (no R&amp;D to hold subjects), off when the player turns it
	/// off, and open for any field no config gates.
	/// </summary>
	internal static class SaScienceGate
	{
		internal const string NodeName = "SA_SCIENCE_GATE";

		internal const string FieldExtTemp = "extTemp";
		internal const string FieldPressure = "pressure";
		internal const string FieldGravity = "gravity";
		internal const string FieldWeather = "weather";

		/// <summary>field -> experiment ids that open it (OR).</summary>
		private static Dictionary<string, List<string>> gates;

		// Per-body answers, rebuilt on any science event: GetSubjects() copies the
		// whole subject dictionary every call, too costly at the refresh rate.
		private static readonly Dictionary<string, bool> Answers = new Dictionary<string, bool>();
		private static string answersBody;
		private static bool eventsHooked;
		private static string[] situationNames;

		internal static bool IsUnlocked(string field, CelestialBody body)
		{
			if (!SaParams.GateOnScience) return true;
			if (body == null) return true;
			// Sandbox: no R&D instance, nothing to gate on.
			if (ResearchAndDevelopment.Instance == null) return true;

			if (gates == null) Load();
			if (!gates.TryGetValue(field, out List<string> experiments) || experiments.Count == 0) return true;

			if (answersBody != body.bodyName)
			{
				Answers.Clear();
				answersBody = body.bodyName;
			}
			if (Answers.TryGetValue(field, out bool cached)) return cached;

			HookEvents();
			bool unlocked = false;
			try
			{
				unlocked = AnyCredited(experiments, body);
			}
			catch (Exception e)
			{
				// A gate that cannot be evaluated opens, rather than hiding a
				// readout because of a bug of ours.
				Debug.LogWarning("[SA] science gate lookup failed for " + field + ": " + e.Message);
				unlocked = true;
			}
			Answers[field] = unlocked;
			return unlocked;
		}

		/// <summary>
		/// Subject ids are "experimentId@BodyNameSituationBiome". A situation name
		/// must follow the body name, so that a body whose name is a prefix of
		/// another's does not match its subjects.
		/// </summary>
		private static bool AnyCredited(List<string> experiments, CelestialBody body)
		{
			List<ScienceSubject> subjects = ResearchAndDevelopment.GetSubjects();
			if (subjects == null) return false;
			if (situationNames == null) situationNames = Enum.GetNames(typeof(ExperimentSituations));

			foreach (string experiment in experiments)
			{
				string prefix = experiment + "@" + body.bodyName;
				foreach (ScienceSubject subject in subjects)
				{
					if (subject == null || subject.science <= 0f || string.IsNullOrEmpty(subject.id)) continue;
					if (!subject.id.StartsWith(prefix, StringComparison.Ordinal)) continue;
					string rest = subject.id.Substring(prefix.Length);
					foreach (string situation in situationNames)
					{
						if (rest.StartsWith(situation, StringComparison.Ordinal)) return true;
					}
				}
			}
			return false;
		}

		private static void Load()
		{
			gates = new Dictionary<string, List<string>>();
			if (GameDatabase.Instance == null) return;
			foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(NodeName))
			{
				string field = node.GetValue("field");
				string experiment = node.GetValue("experiment");
				if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(experiment)) continue;
				if (!gates.TryGetValue(field, out List<string> list))
				{
					list = new List<string>();
					gates[field] = list;
				}
				if (!list.Contains(experiment)) list.Add(experiment);
			}
		}

		private static void HookEvents()
		{
			if (eventsHooked) return;
			eventsHooked = true;
			GameEvents.OnScienceRecieved.Add(OnScienceReceived);
			GameEvents.OnScienceChanged.Add(OnScienceChanged);
		}

		private static void OnScienceReceived(float amount, ScienceSubject subject, ProtoVessel source, bool reverseEngineered)
			=> Answers.Clear();

		private static void OnScienceChanged(float amount, TransactionReasons reason) => Answers.Clear();

		/// <summary>Drops the config, picking up a mid-session ModuleManager
		/// reload, along with every cached answer.</summary>
		internal static void Reset()
		{
			gates = null;
			Answers.Clear();
			answersBody = null;
		}
	}
}
