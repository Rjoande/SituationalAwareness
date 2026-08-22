using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SituationalAwareness.WeatherSurvey
{
	/// <summary>
	/// Multi-row-per-press CSV writer (notes/indagine-meteo.md §7): one
	/// "press" row (the human label + full context) plus one "layer" row
	/// per EVE cloud layer sampled, tied together by a shared PressId. Never
	/// the KSP.log — structured, survives sessions, opens in a spreadsheet.
	/// </summary>
	internal static class WeatherSurveyCsvWriter
	{
		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/SituationalAwareness/PluginData/WeatherSurvey/weather-survey.csv";

		private static readonly string[] Header =
		{
			"PressId", "RowType", "Label", "Note", "UT", "Body", "Biome",
			"Latitude", "Longitude", "AltitudeAsl", "AltitudeAgl", "Situation",
			"SunElevationDeg", "SolarFluxWm2", "WdspTransmittance", "WeatherImpactFactor",
			"CameraIsIva", "CameraX", "CameraY", "CameraZ", "VesselX", "VesselY", "VesselZ",
			"LayerName", "CovHere", "CovSky", "CloudTypeRaw", "TypeName",
			"ParticleFieldDensity", "DropletsDensity", "LightningFrequency", "WetSurfacesIntensity",
			"Fade", "MinAltitudeM", "MaxAltitudeM"
		};

		internal static void Write(string label, string note, WeatherSample sample)
		{
			if (sample == null) return;

			string dir = Path.GetDirectoryName(FilePath);
			Directory.CreateDirectory(dir);
			bool isNewFile = !File.Exists(FilePath);

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
				"", "", "", "", "", "", "", "", "", "", "", ""
			)).Append('\n');

			foreach (WeatherLayerSample layer in sample.Layers)
			{
				sb.Append(Row(
					pressId, "layer", "", "",
					"", "", "", "", "", "", "", "", "", "", "", "",
					"", "", "", "", "", "", "",
					layer.LayerName, F(layer.CovHere), F(layer.CovSky), F(layer.CloudTypeRaw), layer.TypeName,
					F(layer.ParticleFieldDensity), F(layer.DropletsDensity), F(layer.LightningFrequency), F(layer.WetSurfacesIntensity),
					F(layer.Fade), F(layer.MinAltitudeM), F(layer.MaxAltitudeM)
				)).Append('\n');
			}

			File.AppendAllText(FilePath, sb.ToString());
			Debug.Log("[SA_WeatherSurvey] press " + pressId + " label=" + label + " layers=" + sample.Layers.Count);
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
