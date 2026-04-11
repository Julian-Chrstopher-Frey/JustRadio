using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace RadioBloom.Maui;

internal static class LocationCoordinateLookup
{
	private static readonly HttpClient Client = CreateClient();
	private static readonly ConcurrentDictionary<string, Lazy<Task<(double Latitude, double Longitude)?>>> RemoteLookupTasks = new(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, (double Latitude, double Longitude)> CountryCenters = new(StringComparer.OrdinalIgnoreCase)
	{
		["DE"] = (51.16, 10.45),
		["US"] = (39.5, -98.35),
		["AT"] = (47.6, 14.1),
		["GR"] = (39.0, 22.0),
		["GB"] = (54.5, -2.5),
		["FR"] = (46.2, 2.2),
		["IT"] = (42.8, 12.6),
		["ES"] = (40.4, -3.7),
		["NL"] = (52.2, 5.3),
		["CH"] = (46.8, 8.2)
	};

	private static readonly Dictionary<string, (double Latitude, double Longitude)> Regions = new(StringComparer.OrdinalIgnoreCase)
	{
		["DE|Baden-Wurttemberg"] = (48.54, 9.04),
		["DE|Bavaria"] = (48.79, 11.50),
		["DE|Berlin"] = (52.52, 13.40),
		["DE|Hamburg"] = (53.55, 10.00),
		["DE|Hesse"] = (50.65, 9.16),
		["DE|Lower Saxony"] = (52.64, 9.85),
		["DE|North Rhine-Westphalia"] = (51.43, 7.66),
		["US|California"] = (36.78, -119.42),
		["US|Florida"] = (27.66, -81.52),
		["US|Illinois"] = (40.63, -89.40),
		["US|New York"] = (43.00, -75.50),
		["US|Texas"] = (31.00, -100.00),
		["AT|Lower Austria"] = (48.20, 15.70),
		["AT|Vienna"] = (48.21, 16.37),
		["GR|Attica"] = (38.00, 23.70),
		["GR|Crete"] = (35.24, 24.81)
	};

	public static Task<(double Latitude, double Longitude)?> ResolveAsync(LocationProfile location)
	{
		if (TryResolveKnown(location, out (double Latitude, double Longitude) coordinate))
		{
			return Task.FromResult<(double Latitude, double Longitude)?>(coordinate);
		}

		string key = BuildCacheKey(location);
		return RemoteLookupTasks.GetOrAdd(key, _ => new Lazy<Task<(double Latitude, double Longitude)?>>(() => ResolveRemoteAsync(location))).Value;
	}

	private static bool TryResolveKnown(LocationProfile location, out (double Latitude, double Longitude) coordinate)
	{
		string key = $"{location.CountryCode}|{location.Region}";
		if (!string.IsNullOrWhiteSpace(location.Region) && Regions.TryGetValue(key, out coordinate))
		{
			return true;
		}

		return CountryCenters.TryGetValue(location.CountryCode, out coordinate);
	}

	private static async Task<(double Latitude, double Longitude)?> ResolveRemoteAsync(LocationProfile location)
	{
		List<string> queries = [];
		if (!string.IsNullOrWhiteSpace(location.Region))
		{
			queries.Add($"{location.Region}, {location.CountryName}");
			queries.Add(location.Region);
		}

		queries.Add(location.CountryName);

		foreach (string query in queries.Where(query => !string.IsNullOrWhiteSpace(query)).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			(double Latitude, double Longitude)? coordinate = await QueryOpenMeteoAsync(query, location.CountryCode, preferCountry: string.IsNullOrWhiteSpace(location.Region));
			if (coordinate != null)
			{
				return coordinate;
			}
		}

		return null;
	}

	private static async Task<(double Latitude, double Longitude)?> QueryOpenMeteoAsync(string query, string countryCode, bool preferCountry)
	{
		try
		{
			string url = string.Format(
				CultureInfo.InvariantCulture,
				"https://geocoding-api.open-meteo.com/v1/search?name={0}&count=10&language=en&format=json&countryCode={1}",
				Uri.EscapeDataString(query),
				Uri.EscapeDataString(countryCode.ToUpperInvariant()));
			string json = await Client.GetStringAsync(url);
			using JsonDocument document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
			{
				return null;
			}

			JsonElement? bestMatch = null;
			int bestScore = int.MinValue;
			foreach (JsonElement result in results.EnumerateArray())
			{
				if (!TryReadCoordinate(result, out double latitude, out double longitude))
				{
					continue;
				}

				int score = ScoreResult(result, query, countryCode, preferCountry);
				if (score > bestScore)
				{
					bestScore = score;
					bestMatch = result;
				}
			}

			return bestMatch != null && TryReadCoordinate(bestMatch.Value, out double bestLatitude, out double bestLongitude)
				? (bestLatitude, bestLongitude)
				: null;
		}
		catch
		{
			return null;
		}
	}

	private static int ScoreResult(JsonElement result, string query, string countryCode, bool preferCountry)
	{
		int score = 0;
		string? resultCountryCode = TryGetString(result, "country_code");
		string? featureCode = TryGetString(result, "feature_code");
		string? name = TryGetString(result, "name");
		string? country = TryGetString(result, "country");
		string? admin1 = TryGetString(result, "admin1");

		if (string.Equals(resultCountryCode, countryCode, StringComparison.OrdinalIgnoreCase))
		{
			score += 100;
		}

		if (preferCountry)
		{
			if (string.Equals(featureCode, "PCLI", StringComparison.OrdinalIgnoreCase))
			{
				score += 80;
			}
			if (TextMatches(name, query) || TextMatches(country, query))
			{
				score += 40;
			}
		}
		else
		{
			if (string.Equals(featureCode, "ADM1", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(featureCode, "PPLA", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(featureCode, "PPLC", StringComparison.OrdinalIgnoreCase))
			{
				score += 40;
			}
			if (TextMatches(name, query) || TextMatches(admin1, query))
			{
				score += 40;
			}
		}

		if (result.TryGetProperty("population", out JsonElement populationElement) &&
			populationElement.ValueKind == JsonValueKind.Number &&
			populationElement.TryGetInt32(out int population))
		{
			score += Math.Min(25, population / 1_000_000);
		}

		return score;
	}

	private static bool TryReadCoordinate(JsonElement result, out double latitude, out double longitude)
	{
		latitude = 0;
		longitude = 0;
		return result.TryGetProperty("latitude", out JsonElement latitudeElement) &&
			result.TryGetProperty("longitude", out JsonElement longitudeElement) &&
			latitudeElement.TryGetDouble(out latitude) &&
			longitudeElement.TryGetDouble(out longitude);
	}

	private static bool TextMatches(string? value, string query)
	{
		return !string.IsNullOrWhiteSpace(value) &&
			(value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
				query.Contains(value, StringComparison.OrdinalIgnoreCase));
	}

	private static string? TryGetString(JsonElement result, string propertyName)
	{
		return result.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}

	private static string BuildCacheKey(LocationProfile location)
	{
		return $"{location.CountryCode}|{location.CountryName}|{location.Region}".ToUpperInvariant();
	}

	private static HttpClient CreateClient()
	{
		HttpClientHandler handler = new()
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};

		HttpClient client = new(handler)
		{
			Timeout = TimeSpan.FromSeconds(8)
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("JustRadio.Maui/0.1 (https://github.com/Julian-Chrstopher-Frey/JustRadio)");
		return client;
	}
}
