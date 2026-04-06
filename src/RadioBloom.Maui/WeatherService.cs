using System.Globalization;
using System.Net;
using System.Text.Json;

namespace RadioBloom.Maui;

internal sealed record WeatherSnapshot(double TemperatureC, bool IsDay, string ConditionLabel);

internal static class WeatherService
{
	private static readonly HttpClient Client = CreateClient();

	public static async Task<WeatherSnapshot?> GetCurrentAsync(double latitude, double longitude)
	{
		try
		{
			string url = string.Format(
				CultureInfo.InvariantCulture,
				"https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}&current=temperature_2m,weather_code,is_day&timezone=auto",
				latitude,
				longitude);
			string json = await Client.GetStringAsync(url);
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement current = document.RootElement.GetProperty("current");
			double temperature = current.GetProperty("temperature_2m").GetDouble();
			int code = current.TryGetProperty("weather_code", out JsonElement codeElement) ? codeElement.GetInt32() : -1;
			bool isDay = !current.TryGetProperty("is_day", out JsonElement dayElement) || dayElement.GetInt32() == 1;
			return new WeatherSnapshot(temperature, isDay, DescribeWeatherCode(code));
		}
		catch
		{
			return null;
		}
	}

	private static string DescribeWeatherCode(int code)
	{
		return code switch
		{
			0 => "Clear sky",
			1 or 2 => "Mostly clear",
			3 => "Overcast",
			45 or 48 => "Fog",
			51 or 53 or 55 => "Drizzle",
			56 or 57 => "Freezing drizzle",
			61 or 63 or 65 => "Rain",
			66 or 67 => "Freezing rain",
			71 or 73 or 75 => "Snow",
			77 => "Snow grains",
			80 or 81 or 82 => "Rain showers",
			85 or 86 => "Snow showers",
			95 => "Thunderstorm",
			96 or 99 => "Storm with hail",
			_ => "Local conditions"
		};
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
		client.DefaultRequestHeaders.UserAgent.ParseAdd("JustRadio.Maui/0.1");
		return client;
	}
}
