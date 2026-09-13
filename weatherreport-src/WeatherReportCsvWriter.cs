using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SituationalAwareness.WeatherReport
{
	/// <summary>
	/// Multi-row-per-press CSV writer (notes/indagine-meteo.md §7): one
	/// "press" row (the human label + full context) plus one "layer" row
	/// per EVE cloud layer sampled, tied together by a shared PressId. Never
	/// the KSP.log — structured, survives sessions, opens in a spreadsheet.
	/// </summary>
	internal static class WeatherReportCsvWriter
	{
		private static readonly string BaseDir =
			KSPUtil.ApplicationRootPath + "GameData/SituationalAwareness/PluginData/WeatherReport/";
		private const string BaseName = "weather-report";
		private const string Extension = ".csv";

		// Resolved once per session by ResolveFilePath: a file whose header no
		// longer matches this build's columns is left alone and a new numbered
		// one is started beside it.
		private static string resolvedPath;

		private static readonly string[] Header =
		{
			"PressId", "RowType", "Label", "Note", "UT", "Body", "Biome",
			"Latitude", "Longitude", "AltitudeAsl", "AltitudeAgl", "Situation",
			"SunElevationDeg", "SolarFluxWm2", "WdspTransmittance", "WeatherImpactFactor",
			"CameraIsIva", "CameraX", "CameraY", "CameraZ", "VesselX", "VesselY", "VesselZ",
			"SaWeatherState", "SaSkyOpticalDepth", "SaPrecipIntensity", "SaDominantLayer",
			"LayerName", "CovHere", "CovSky", "CloudTypeRaw", "TypeName",
			"CloudTypeDensity", "FxOnly",
			"ParticleFieldDensity", "DropletsDensity", "LightningFrequency", "WetSurfacesIntensity",
			"Fade", "MinAltitudeM", "MaxAltitudeM",
			"ParticleFieldName", "ParticleFallSpeed", "ParticleCount", "ParticleStretch", "PrecipIntensity"
		};

		// Excel reads a BOM-less UTF-8 CSV as ANSI, so accented notes come out
		// as mojibake ("visibilità" -> "visibilit?"). Everything else reads
		// UTF-8 either way, so the BOM costs nothing.
		private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);

		internal static void Write(string label, string note, WeatherSample sample)
		{
			if (sample == null) return;

			string filePath = ResolveFilePath();
			bool isNewFile = !File.Exists(filePath);

			string pressId = Guid.NewGuid().ToString("N").Substring(0, 8);
			StringBuilder sb = new StringBuilder();
			if (isNewFile)
			{
				sb.Append(string.Join(",", Header)).Append('\n');
			}

			sb.Append(Row(
				pressId, "press", label, note,
				F(sample.UT), sample.BodyName, sample.BiomeName,
				F(sample.Latitude), F(sample.Longitude), F(sample.AltitudeAsl),
				sample.AltitudeAgl.HasValue ? F(sample.AltitudeAgl.Value) : "",
				sample.Situation,
				F(sample.SunElevationDeg), F(sample.SolarFluxWm2),
				sample.WdspTransmittance.HasValue ? F(sample.WdspTransmittance.Value) : "",
				sample.WeatherImpactFactor.HasValue ? F(sample.WeatherImpactFactor.Value) : "",
				sample.CameraIsIva.ToString(),
				F(sample.CameraPosition.x), F(sample.CameraPosition.y), F(sample.CameraPosition.z),
				F(sample.VesselPosition.x), F(sample.VesselPosition.y), F(sample.VesselPosition.z),
				sample.SaWeatherState ?? "", F(sample.SaSkyOpticalDepth), F(sample.SaPrecipIntensity),
				sample.SaDominantLayer ?? "",
				"", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", ""
			)).Append('\n');

			foreach (WeatherLayerSample layer in sample.Layers)
			{
				sb.Append(Row(
					pressId, "layer", "", "",
					"", "", "", "", "", "", "", "", "", "", "", "",
					"", "", "", "", "", "", "",
					"", "", "", "",
					layer.LayerName, F(layer.CovHere), F(layer.CovSky), F(layer.CloudTypeRaw), layer.TypeName,
					F(layer.CloudTypeDensity), layer.FxOnly.ToString(),
					F(layer.ParticleFieldDensity), F(layer.DropletsDensity), F(layer.LightningFrequency), F(layer.WetSurfacesIntensity),
					F(layer.Fade), F(layer.MinAltitudeM), F(layer.MaxAltitudeM),
					layer.ParticleFieldName, F(layer.ParticleFallSpeed), F(layer.ParticleCount),
					F(layer.ParticleStretch), F(layer.PrecipIntensity)
				)).Append('\n');
			}

			File.AppendAllText(filePath, sb.ToString(), Utf8WithBom);
			Debug.Log("[SA_WeatherReport] press " + pressId + " label=" + label + " layers=" + sample.Layers.Count);
			WriteInstalledModsOnce();
		}

		private static bool modsWritten;

		/// <summary>
		/// The loaded-plugins list (notes/indagine-meteo.md §8.2): names and
		/// versions only, no paths, nothing else. One file per session, and
		/// only once a report has actually been written — the disclaimer
		/// promises nothing is read or written until the player reports.
		/// </summary>
		private static void WriteInstalledModsOnce()
		{
			if (modsWritten) return;
			modsWritten = true;
			try
			{
				StringBuilder sb = new StringBuilder();
				foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
				{
					if (a == null || a.assembly == null) continue;
					sb.Append(a.name).Append(' ').Append(a.assembly.GetName().Version).Append('\n');
				}
				File.WriteAllText(BaseDir + "installed-mods.txt", sb.ToString(), Utf8WithBom);
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] could not write installed-mods.txt: " + e.Message);
			}
		}

		/// <summary>
		/// The file to append to. An existing report whose header predates the
		/// current column set must NOT be appended to: the writer only emits a
		/// header when it creates the file, so new wider rows would land under
		/// an old narrower header and quietly produce a ragged CSV that no
		/// spreadsheet or script reads correctly. When that happens the old file
		/// is left untouched — it is collected data — and a numbered sibling is
		/// started instead (weather-report-2.csv, -3, ...).
		/// </summary>
		private static string ResolveFilePath()
		{
			if (resolvedPath != null) return resolvedPath;

			Directory.CreateDirectory(BaseDir);
			string expected = string.Join(",", Header);

			for (int index = 1; index < 1000; index++)
			{
				string candidate = BaseDir + BaseName + (index == 1 ? "" : "-" + index) + Extension;
				if (!File.Exists(candidate))
				{
					resolvedPath = candidate;
					return resolvedPath;
				}
				if (HeaderMatches(candidate, expected))
				{
					resolvedPath = candidate;
					return resolvedPath;
				}
				Debug.Log("[SA_WeatherReport] " + Path.GetFileName(candidate)
					+ " was written by a different version (column layout changed); leaving it alone and trying the next name.");
			}

			// Absurd but harmless fallback: append to the base file rather than
			// silently dropping the player's press.
			resolvedPath = BaseDir + BaseName + Extension;
			return resolvedPath;
		}

		private static bool HeaderMatches(string path, string expected)
		{
			try
			{
				using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
				{
					string first = reader.ReadLine();
					if (first == null) return true; // empty file: reusable as-is
					// StreamReader normally eats the BOM itself; trimmed here too
					// so a file written by some other tool still compares equal.
					return string.Equals(first.TrimStart('﻿'), expected, StringComparison.Ordinal);
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning("[SA_WeatherReport] could not read " + path + ": " + e.Message);
				return false;
			}
		}

		private static string F(double v) => v.ToString(CultureInfo.InvariantCulture);

		private static string Row(params string[] fields)
		{
			for (int i = 0; i < fields.Length; i++)
			{
				fields[i] = Escape(fields[i]);
			}
			return string.Join(",", fields);
		}

		private static string Escape(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
			return "\"" + s.Replace("\"", "\"\"") + "\"";
		}
	}
}
