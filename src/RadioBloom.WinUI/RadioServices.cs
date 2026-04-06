using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RadioBloom.WinUI;

[DataContract]
public sealed class RadioBrowserStation
{
    [DataMember(Name = "stationuuid")] public string? StationUuid { get; set; }
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "url_resolved")] public string? UrlResolved { get; set; }
    [DataMember(Name = "homepage")] public string? Homepage { get; set; }
    [DataMember(Name = "language")] public string? Language { get; set; }
    [DataMember(Name = "country")] public string? Country { get; set; }
    [DataMember(Name = "countrycode")] public string? CountryCode { get; set; }
    [DataMember(Name = "state")] public string? State { get; set; }
    [DataMember(Name = "codec")] public string? Codec { get; set; }
    [DataMember(Name = "bitrate")] public int Bitrate { get; set; }
    [DataMember(Name = "lastcheckok")] public int LastCheckOk { get; set; }
    [DataMember(Name = "hls")] public int Hls { get; set; }
    [DataMember(Name = "tags")] public string? Tags { get; set; }
    [DataMember(Name = "clickcount")] public int ClickCount { get; set; }
    [DataMember(Name = "votes")] public int Votes { get; set; }
}

[DataContract]
public sealed class IpApiResponse
{
    [DataMember(Name = "city")] public string? City { get; set; }
    [DataMember(Name = "region")] public string? Region { get; set; }
    [DataMember(Name = "country_code")] public string? CountryCode { get; set; }
    [DataMember(Name = "country_name")] public string? CountryName { get; set; }
    [DataMember(Name = "latitude")] public double? Latitude { get; set; }
    [DataMember(Name = "longitude")] public double? Longitude { get; set; }
}

public sealed class RadioStation
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = new();
    public bool Featured { get; set; }
    public string Tagline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string MetadataSummary { get; set; } = string.Empty;
    public string LanguageLabel { get; set; } = string.Empty;
    public string HomepageHost { get; set; } = string.Empty;
    public string EditorialBadge { get; set; } = string.Empty;
    public int PopularityScore { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsCurated { get; set; }
    public bool IsDiscovery { get; set; }
}

public sealed class LocationProfile
{
    public string? City { get; set; }
    public string? Region { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class WeatherSnapshot
{
    public double TemperatureC { get; set; }
    public double ApparentTemperatureC { get; set; }
    public int? RelativeHumidity { get; set; }
    public double WindSpeedKmh { get; set; }
    public double? HighC { get; set; }
    public double? LowC { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; }
    public string ConditionLabel { get; set; } = string.Empty;
    public string LocalTimeLabel { get; set; } = string.Empty;
}

[DataContract]
internal sealed class GeocodeResult
{
    [DataMember(Name = "lat")] public string? Latitude { get; set; }
    [DataMember(Name = "lon")] public string? Longitude { get; set; }
}

[DataContract]
internal sealed class OpenMeteoForecastResponse
{
    [DataMember(Name = "timezone_abbreviation")] public string? TimezoneAbbreviation { get; set; }
    [DataMember(Name = "current")] public OpenMeteoCurrent? Current { get; set; }
    [DataMember(Name = "daily")] public OpenMeteoDaily? Daily { get; set; }
}

[DataContract]
internal sealed class OpenMeteoCurrent
{
    [DataMember(Name = "time")] public string? Time { get; set; }
    [DataMember(Name = "temperature_2m")] public double TemperatureC { get; set; }
    [DataMember(Name = "apparent_temperature")] public double ApparentTemperatureC { get; set; }
    [DataMember(Name = "relative_humidity_2m")] public int? RelativeHumidity { get; set; }
    [DataMember(Name = "weather_code")] public int WeatherCode { get; set; }
    [DataMember(Name = "wind_speed_10m")] public double WindSpeedKmh { get; set; }
    [DataMember(Name = "is_day")] public int IsDay { get; set; }
}

[DataContract]
internal sealed class OpenMeteoDaily
{
    [DataMember(Name = "temperature_2m_max")] public List<double>? TemperatureMax { get; set; }
    [DataMember(Name = "temperature_2m_min")] public List<double>? TemperatureMin { get; set; }
}

public sealed class CountryOption
{
    public string CountryCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

[DataContract]
internal sealed class CountryRegionCatalogEntry
{
    [DataMember(Name = "countryName")] public string CountryName { get; set; } = string.Empty;
    [DataMember(Name = "countryShortCode")] public string CountryShortCode { get; set; } = string.Empty;
    [DataMember(Name = "regions")] public List<CountryRegionCatalogRegion> Regions { get; set; } = [];
}

[DataContract]
internal sealed class CountryRegionCatalogRegion
{
    [DataMember(Name = "name")] public string Name { get; set; } = string.Empty;
    [DataMember(Name = "shortCode")] public string ShortCode { get; set; } = string.Empty;
}

public enum NativePlayerState
{
    Ready,
    Connecting,
    Live,
    Paused,
    Stopped,
    Error
}

public static class StationCatalogService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly Lazy<Dictionary<string, string>> CountryLookup = new(BuildCountryLookup);
    private static readonly ConcurrentDictionary<string, List<string>> RegionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (double Latitude, double Longitude)> CoordinateCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (WeatherSnapshot Snapshot, DateTimeOffset FetchedAt)> WeatherCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Dictionary<string, CountryRegionCatalogEntry>> RegionCatalog = new(LoadRegionCatalog);
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> RegionAliasLookup = new(BuildRegionAliasLookup);

    static StationCatalogService()
    {
        ServicePointManager.SecurityProtocol =
            (SecurityProtocolType)3072 |
            SecurityProtocolType.Tls11 |
            SecurityProtocolType.Tls;
    }

    public static LocationProfile GetDefaultLocation()
    {
        string cultureName = CultureInfo.CurrentCulture.Name;
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            cultureName = "en-US";
        }

        RegionInfo region = new(cultureName);
        return new LocationProfile
        {
            CountryCode = region.TwoLetterISORegionName.ToUpperInvariant(),
            CountryName = region.EnglishName,
            Label = region.EnglishName,
            Source = "System region"
        };
    }

    public static async Task<LocationProfile> GetApproximateLocationAsync()
    {
        LocationProfile fallback = GetDefaultLocation();

        try
        {
            string json = await Client.GetStringAsync("https://ipapi.co/json/");
            IpApiResponse? data = Deserialize<IpApiResponse>(json);
            if (data == null || string.IsNullOrWhiteSpace(data.CountryCode))
            {
                return fallback;
            }

            return new LocationProfile
            {
                City = data.City,
                Region = NormalizeRegionName(data.CountryCode, data.Region),
                CountryCode = data.CountryCode.ToUpperInvariant(),
                CountryName = data.CountryName ?? fallback.CountryName,
                Label = BuildLocationLabel(data.City, NormalizeRegionName(data.CountryCode, data.Region), data.CountryCode.ToUpperInvariant()),
                Source = "Approximate IP location",
                Latitude = data.Latitude,
                Longitude = data.Longitude
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static async Task<LocationProfile> EnsureCoordinatesAsync(LocationProfile location)
    {
        if (location == null)
        {
            return GetDefaultLocation();
        }

        if (location.Latitude.HasValue && location.Longitude.HasValue)
        {
            return location;
        }

        string cacheKey = BuildCoordinateCacheKey(location);
        if (CoordinateCache.TryGetValue(cacheKey, out (double Latitude, double Longitude) cached))
        {
            location.Latitude = cached.Latitude;
            location.Longitude = cached.Longitude;
            return location;
        }

        string query = BuildGeocodeQuery(location);
        if (string.IsNullOrWhiteSpace(query))
        {
            return location;
        }

        try
        {
            StringBuilder uri = new("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&addressdetails=0");
            if (!string.IsNullOrWhiteSpace(location.CountryCode))
            {
                uri.Append("&countrycodes=").Append(Uri.EscapeDataString(location.CountryCode.ToLowerInvariant()));
            }

            uri.Append("&q=").Append(Uri.EscapeDataString(query));
            string json = await Client.GetStringAsync(uri.ToString());
            List<GeocodeResult>? results = Deserialize<List<GeocodeResult>>(json);
            GeocodeResult? result = results?.FirstOrDefault();

            if (result != null &&
                double.TryParse(result.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) &&
                double.TryParse(result.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
            {
                location.Latitude = latitude;
                location.Longitude = longitude;
                CoordinateCache[cacheKey] = (latitude, longitude);
            }
        }
        catch
        {
        }

        return location;
    }

    public static List<CountryOption> GetCountryOptions()
    {
        if (RegionCatalog.Value.Count > 0)
        {
            return RegionCatalog.Value.Values
                .Select(entry => new CountryOption
                {
                    CountryCode = entry.CountryShortCode.ToUpperInvariant(),
                    DisplayName = entry.CountryName
                })
                .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        Dictionary<string, CountryOption> options = new(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                RegionInfo region = new(culture.Name);
                string code = region.TwoLetterISORegionName.ToUpperInvariant();
                if (!options.ContainsKey(code))
                {
                    options[code] = new CountryOption
                    {
                        CountryCode = code,
                        DisplayName = region.EnglishName
                    };
                }
            }
            catch
            {
            }
        }

        return options.Values.OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<WeatherSnapshot?> GetWeatherAsync(LocationProfile location)
    {
        if (location == null)
        {
            return null;
        }

        location = await EnsureCoordinatesAsync(location);
        if (!location.Latitude.HasValue || !location.Longitude.HasValue)
        {
            return null;
        }

        string cacheKey = string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.00}|{1:0.00}",
            Math.Round(location.Latitude.Value, 2),
            Math.Round(location.Longitude.Value, 2));

        if (WeatherCache.TryGetValue(cacheKey, out (WeatherSnapshot Snapshot, DateTimeOffset FetchedAt) cached) &&
            (DateTimeOffset.UtcNow - cached.FetchedAt) < TimeSpan.FromMinutes(12))
        {
            return cached.Snapshot;
        }

        try
        {
            string url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:0.0000}&longitude={1:0.0000}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto",
                location.Latitude.Value,
                location.Longitude.Value);

            string json = await Client.GetStringAsync(url);
            OpenMeteoForecastResponse? data = Deserialize<OpenMeteoForecastResponse>(json);
            if (data?.Current == null)
            {
                return null;
            }

            WeatherSnapshot snapshot = new()
            {
                TemperatureC = data.Current.TemperatureC,
                ApparentTemperatureC = data.Current.ApparentTemperatureC,
                RelativeHumidity = data.Current.RelativeHumidity,
                WindSpeedKmh = data.Current.WindSpeedKmh,
                HighC = data.Daily?.TemperatureMax?.FirstOrDefault(),
                LowC = data.Daily?.TemperatureMin?.FirstOrDefault(),
                WeatherCode = data.Current.WeatherCode,
                IsDay = data.Current.IsDay == 1,
                ConditionLabel = DescribeWeatherCode(data.Current.WeatherCode, data.Current.IsDay == 1),
                LocalTimeLabel = FormatWeatherTime(data.Current.Time, data.TimezoneAbbreviation)
            };

            WeatherCache[cacheKey] = (snapshot, DateTimeOffset.UtcNow);
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<List<string>> GetRegionsForCountryAsync(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return [];
        }

        if (RegionCatalog.Value.TryGetValue(countryCode.ToUpperInvariant(), out CountryRegionCatalogEntry? catalogEntry) &&
            catalogEntry.Regions.Count > 0)
        {
            return catalogEntry.Regions
                .Select(region => region.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(region => region, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        if (RegionCache.TryGetValue(countryCode, out List<string>? cached))
        {
            return cached;
        }

        List<RadioBrowserStation> stations = await QueryStationsAsync(countryCode, null, 500);
        List<string> regions = stations
            .Select(s => NormalizeRegionName(countryCode, s.State))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToList();

        RegionCache[countryCode] = regions;
        return regions;
    }

    public static Task<LocationProfile> ResolveLocationOverrideAsync(string query, LocationProfile fallback)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(fallback);
        }

        string trimmed = query.Trim();
        string[] parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? region = null;
        string? countryCode = null;
        string? countryName = null;

        if (parts.Length > 1)
        {
            (countryCode, countryName) = ResolveCountry(parts[^1]);
            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                region = string.Join(", ", parts.Take(parts.Length - 1)).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            (countryCode, countryName) = ResolveCountry(trimmed);
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            countryCode = fallback.CountryCode;
            countryName = fallback.CountryName;
            region = trimmed;
        }

        string label = string.IsNullOrWhiteSpace(region)
            ? (countryName ?? countryCode ?? trimmed)
            : region + ", " + (countryName ?? countryCode ?? string.Empty);

        return Task.FromResult(new LocationProfile
        {
            Region = string.IsNullOrWhiteSpace(region) ? null : region,
            CountryCode = countryCode ?? fallback.CountryCode,
            CountryName = countryName ?? fallback.CountryName,
            Label = label,
            Source = "Manual override"
        });
    }

    public static List<RadioStation> GetFallbackStations()
    {
        return
        [
            new RadioStation { Id = "dlf", Name = "Deutschlandfunk", Subtitle = "News", Location = "Germany", Genre = "News", Mood = "News", Categories = ["Curated", "Featured", "News"], Featured = true, IsCurated = true, EditorialBadge = "Curated", Tagline = "Calm, information-dense public radio.", Description = "National public radio with news, features, and in-depth reporting.", StreamUrl = "https://st01.sslstream.dlf.de/dlf/01/128/mp3/stream.mp3", Quality = "MP3 | 128 kbps", SourceLabel = "Deutschlandradio", SourceUrl = "https://www.deutschlandfunk.de/", MetadataSummary = "German | MP3 | 128 kbps | deutschlandfunk.de", LanguageLabel = "German", HomepageHost = "deutschlandfunk.de", PopularityScore = 980 },
            new RadioStation { Id = "swr3", Name = "SWR3", Subtitle = "Pop", Location = "Baden-Wurttemberg, DE", Genre = "Pop", Mood = "Pop", Categories = ["Curated", "Featured", "Pop"], Featured = true, IsCurated = true, EditorialBadge = "Curated", Tagline = "Modern mainstream radio with national reach.", Description = "Popular hits, presenters, and a clean full-service radio mix.", StreamUrl = "https://liveradio.swr.de/sw282p3/swr3/", Quality = "MP3 | 128 kbps", SourceLabel = "SWR", SourceUrl = "https://www.swr3.de/", MetadataSummary = "German | MP3 | 128 kbps | swr3.de", LanguageLabel = "German", HomepageHost = "swr3.de", PopularityScore = 910 },
            new RadioStation { Id = "1live", Name = "1LIVE", Subtitle = "Pop", Location = "North Rhine-Westphalia, DE", Genre = "Pop", Mood = "Pop", Categories = ["Curated", "Featured", "Pop"], Featured = true, IsCurated = true, EditorialBadge = "Curated", Tagline = "Big youth radio with broad national appeal.", Description = "Contemporary music, presenters, and youth-oriented German radio programming.", StreamUrl = "http://wdr-1live-live.icecast.wdr.de/wdr/1live/live/mp3/128/stream.mp3", Quality = "MP3 | 128 kbps", SourceLabel = "WDR", SourceUrl = "https://www1.wdr.de/radio/1live/", MetadataSummary = "German | MP3 | 128 kbps | wdr.de", LanguageLabel = "German", HomepageHost = "wdr.de", PopularityScore = 890 },
            new RadioStation { Id = "kexp", Name = "KEXP 90.3", Subtitle = "Indie", Location = "Seattle, US", Genre = "Indie", Mood = "Rock", Categories = ["Curated", "Discovery", "Featured", "Rock"], Featured = true, IsCurated = true, IsDiscovery = true, EditorialBadge = "Discovery", Tagline = "Human-curated discovery radio.", Description = "Strong fallback for discovery and indie listening.", StreamUrl = "https://kexp.streamguys1.com/kexp160.aac", Quality = "AAC | 160 kbps", SourceLabel = "KEXP", SourceUrl = "https://www.kexp.org/mobile/kexp-livestreams/", MetadataSummary = "English | AAC | 160 kbps | kexp.org", LanguageLabel = "English", HomepageHost = "kexp.org", PopularityScore = 870 }
        ];
    }

    public static async Task<List<RadioStation>> LoadStationsAsync(LocationProfile location)
    {
        try
        {
            List<RadioBrowserStation> raw = await LoadRadioBrowserStationsAsync(location);
            if (raw.Count == 0)
            {
                return GetFallbackStations();
            }

            List<RadioStation> results = new();
            for (int i = 0; i < raw.Count; i++)
            {
                results.Add(MapStation(raw[i], location, i));
            }

            return results;
        }
        catch
        {
            return GetFallbackStations();
        }
    }

    public static List<string> BuildCategoryList(IEnumerable<RadioStation> stations)
    {
        List<string> categories = ["All"];
        string[] preferred = ["Favorites", "Curated", "Discovery", "Featured", "News", "Talk", "Pop", "Rock", "Jazz", "Electronic", "Classical", "Sports", "Culture", "Local"];

        foreach (string category in preferred)
        {
            if (stations.Any(s => MatchesCategory(s, category)))
            {
                categories.Add(category);
            }

            if (categories.Count >= 10)
            {
                break;
            }
        }

        return categories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<RadioBrowserStation>> LoadRadioBrowserStationsAsync(LocationProfile location)
    {
        List<RadioBrowserStation> country = await QueryStationsAsync(location.CountryCode, null, 220);
        List<RadioBrowserStation> regional = [];

        if (!string.IsNullOrWhiteSpace(location.Region))
        {
            regional = country
                .Where(station => string.Equals(NormalizeRegionName(location.CountryCode, station.State), location.Region, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (regional.Count == 0)
            {
                List<RadioBrowserStation> exactQuery = await QueryStationsAsync(location.CountryCode, location.Region, 180);
                regional = exactQuery
                    .Where(station => string.Equals(NormalizeRegionName(location.CountryCode, station.State), location.Region, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        int target = string.Equals(location.CountryCode, "DE", StringComparison.OrdinalIgnoreCase) ? 48 : 28;
        List<RadioBrowserStation> combined = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (RadioBrowserStation station in regional)
        {
            if (!string.IsNullOrWhiteSpace(station.StationUuid) && seen.Add(station.StationUuid))
            {
                combined.Add(station);
            }
        }

        foreach (RadioBrowserStation station in country)
        {
            if (!string.IsNullOrWhiteSpace(station.StationUuid) && seen.Add(station.StationUuid))
            {
                combined.Add(station);
            }

            if (combined.Count >= target)
            {
                break;
            }
        }

        return combined;
    }

    private static async Task<List<RadioBrowserStation>> QueryStationsAsync(string countryCode, string? region, int limit)
    {
        StringBuilder uri = new();
        uri.Append("https://all.api.radio-browser.info/json/stations/search?hidebroken=true&order=clickcount&reverse=true");
        uri.Append("&countrycode=").Append(Uri.EscapeDataString(countryCode));
        uri.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(region))
        {
            uri.Append("&state=").Append(Uri.EscapeDataString(region));
        }

        string json = await Client.GetStringAsync(uri.ToString());
        List<RadioBrowserStation> stations = Deserialize<List<RadioBrowserStation>>(json) ?? [];
        return stations.Where(IsPlayable).OrderByDescending(s => s.ClickCount).ThenByDescending(s => s.Votes).ToList();
    }

    private static bool IsPlayable(RadioBrowserStation station)
    {
        if (string.IsNullOrWhiteSpace(station.Name) || string.IsNullOrWhiteSpace(station.UrlResolved))
        {
            return false;
        }

        string codec = (station.Codec ?? string.Empty).ToUpperInvariant();
        if (station.LastCheckOk != 1 || station.Hls == 1)
        {
            return false;
        }

        return codec is "MP3" or "AAC" or "AAC+" or "AACP";
    }

    private static RadioStation MapStation(RadioBrowserStation station, LocationProfile location, int rank)
    {
        string genre = GetPrimaryTag(station.Tags);
        bool featured = rank < 8;
        bool curated = IsCuratedStation(station, rank);
        bool discovery = IsDiscoveryStation(station, genre, rank);
        List<string> categories = GetCategories((station.Name ?? string.Empty) + " " + (station.Tags ?? string.Empty), featured, curated, discovery);
        string? normalizedRegion = NormalizeRegionName(station.CountryCode, station.State);
        string locationLabel = !string.IsNullOrWhiteSpace(normalizedRegion)
            ? normalizedRegion + ", " + station.CountryCode
            : (!string.IsNullOrWhiteSpace(station.State) ? station.State + ", " + station.CountryCode : (station.Country ?? location.CountryName));
        string quality = BuildQualityLabel(station.Codec, station.Bitrate);
        string tagline = BuildStationTagline(station, locationLabel, genre);
        string description = BuildStationDescription(station, locationLabel, genre, quality);
        string languageLabel = string.IsNullOrWhiteSpace(station.Language) ? "Unknown" : ToTitleLike(station.Language);
        string homepageHost = GetSourceHostLabel(station.Homepage) ?? "radio-browser.info";
        int popularityScore = Math.Max(0, station.ClickCount * 2) + Math.Max(0, station.Votes);
        string editorialBadge = discovery ? "Discovery" : (curated ? "Curated" : (featured ? "Featured" : "Live"));

        return new RadioStation
        {
            Id = "rb-" + station.StationUuid,
            Name = station.Name ?? "Unnamed Station",
            Subtitle = genre,
            Location = locationLabel,
            Genre = genre,
            Mood = categories.FirstOrDefault(c => c is not "Featured" and not "Curated" and not "Discovery" and not "Local") ?? "Local",
            Categories = categories,
            Featured = featured,
            IsCurated = curated,
            IsDiscovery = discovery,
            Tagline = tagline,
            Description = description,
            StreamUrl = station.UrlResolved ?? string.Empty,
            Quality = quality,
            SourceLabel = "Radio Browser",
            SourceUrl = string.IsNullOrWhiteSpace(station.Homepage) ? "https://www.radio-browser.info/" : station.Homepage,
            MetadataSummary = BuildMetadataSummary(station, languageLabel, quality, homepageHost),
            LanguageLabel = languageLabel,
            HomepageHost = homepageHost,
            EditorialBadge = editorialBadge,
            PopularityScore = popularityScore
        };
    }

    private static List<string> GetCategories(string text, bool featured, bool curated, bool discovery)
    {
        List<string> categories = [];
        string lowered = text.ToLowerInvariant();

        if (featured) categories.Add("Featured");
        if (curated) categories.Add("Curated");
        if (discovery) categories.Add("Discovery");
        if (lowered.Contains("news")) categories.Add("News");
        if (lowered.Contains("talk") || lowered.Contains("podcast") || lowered.Contains("politic")) categories.Add("Talk");
        if (lowered.Contains("pop") || lowered.Contains("hits") || lowered.Contains("80s") || lowered.Contains("90s")) categories.Add("Pop");
        if (lowered.Contains("rock") || lowered.Contains("alternative") || lowered.Contains("indie") || lowered.Contains("metal")) categories.Add("Rock");
        if (lowered.Contains("jazz") || lowered.Contains("blues") || lowered.Contains("swing")) categories.Add("Jazz");
        if (lowered.Contains("electro") || lowered.Contains("electronic") || lowered.Contains("dance") || lowered.Contains("house") || lowered.Contains("trance") || lowered.Contains("techno")) categories.Add("Electronic");
        if (lowered.Contains("classical") || lowered.Contains("orchestra")) categories.Add("Classical");
        if (lowered.Contains("sport") || lowered.Contains("football")) categories.Add("Sports");
        if (lowered.Contains("culture") || lowered.Contains("community") || lowered.Contains("folk")) categories.Add("Culture");
        if (categories.Count == 0 || (categories.Count == 1 && categories.Contains("Featured"))) categories.Add("Local");

        return categories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool MatchesCategory(RadioStation station, string category)
    {
        return category switch
        {
            "Favorites" => station.IsFavorite,
            "Curated" => station.IsCurated || station.Categories.Contains("Curated"),
            "Discovery" => station.IsDiscovery || station.Categories.Contains("Discovery"),
            "Featured" => station.Featured || station.Categories.Contains("Featured"),
            _ => station.Categories.Contains(category)
        };
    }

    private static bool IsCuratedStation(RadioBrowserStation station, int rank)
    {
        string lowered = ((station.Name ?? string.Empty) + " " + (station.Tags ?? string.Empty)).ToLowerInvariant();
        string sourceHost = (GetSourceHostLabel(station.Homepage) ?? string.Empty).ToLowerInvariant();
        bool trustedSource = sourceHost.Contains("ard") ||
                             sourceHost.Contains("wdr") ||
                             sourceHost.Contains("swr") ||
                             sourceHost.Contains("ndr") ||
                             sourceHost.Contains("orf") ||
                             sourceHost.Contains("srf") ||
                             sourceHost.Contains("bbc") ||
                             sourceHost.Contains("kexp") ||
                             sourceHost.Contains("somafm");
        bool richMetadata = !string.IsNullOrWhiteSpace(station.Language) &&
                            !string.IsNullOrWhiteSpace(station.Homepage) &&
                            station.Bitrate >= 96;
        bool editorialFormat = lowered.Contains("kultur") ||
                               lowered.Contains("culture") ||
                               lowered.Contains("public") ||
                               lowered.Contains("jazz") ||
                               lowered.Contains("classical") ||
                               lowered.Contains("indie");

        return rank < 10 || trustedSource || (richMetadata && editorialFormat) || (station.Votes >= 90 && station.Bitrate >= 96);
    }

    private static bool IsDiscoveryStation(RadioBrowserStation station, string genre, int rank)
    {
        string lowered = ((station.Name ?? string.Empty) + " " + (station.Tags ?? string.Empty) + " " + genre).ToLowerInvariant();
        bool exploratoryGenre = lowered.Contains("indie") ||
                                lowered.Contains("alternative") ||
                                lowered.Contains("jazz") ||
                                lowered.Contains("electronic") ||
                                lowered.Contains("ambient") ||
                                lowered.Contains("classical") ||
                                lowered.Contains("culture") ||
                                lowered.Contains("world");
        bool mainstreamSpeech = lowered.Contains("news") || lowered.Contains("talk") || lowered.Contains("sport");

        return exploratoryGenre || (!mainstreamSpeech && station.Bitrate >= 96 && rank >= 4 && rank < 18);
    }

    private static string BuildMetadataSummary(RadioBrowserStation station, string languageLabel, string quality, string homepageHost)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(languageLabel) && !string.Equals(languageLabel, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(languageLabel);
        }

        parts.Add(quality);

        if (station.Votes > 0)
        {
            parts.Add(station.Votes.ToString(CultureInfo.InvariantCulture) + " votes");
        }

        if (!string.IsNullOrWhiteSpace(homepageHost))
        {
            parts.Add(homepageHost);
        }

        return string.Join(" | ", parts);
    }

    private static string GetPrimaryTag(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return "Local Radio";
        }

        foreach (string part in tags.Split(','))
        {
            string value = part.Trim();
            if (value.Length >= 3 && !value.Contains("stream", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return "Local Radio";
    }

    private static string BuildStationTagline(RadioBrowserStation station, string locationLabel, string genre)
    {
        List<string> tags = GetDisplayTags(station.Tags, genre, 3);
        string? sourceHost = GetSourceHostLabel(station.Homepage);

        if (tags.Count >= 2 && !string.IsNullOrWhiteSpace(sourceHost))
        {
            return JoinNatural(tags.Take(2).ToList()) + " via " + sourceHost + ".";
        }

        if (tags.Count >= 2)
        {
            return JoinNatural(tags.Take(2).ToList()) + " from " + locationLabel + ".";
        }

        if (tags.Count == 1 && !string.IsNullOrWhiteSpace(sourceHost))
        {
            return tags[0] + " stream via " + sourceHost + ".";
        }

        if (tags.Count == 1)
        {
            return tags[0] + " radio from " + locationLabel + ".";
        }

        if (!string.IsNullOrWhiteSpace(sourceHost))
        {
            return "Official live stream via " + sourceHost + ".";
        }

        return genre + " station from " + locationLabel + ".";
    }

    private static string BuildStationDescription(RadioBrowserStation station, string locationLabel, string genre, string quality)
    {
        List<string> tags = GetDisplayTags(station.Tags, genre, 4);
        List<string> parts = [];

        if (tags.Count > 0)
        {
            parts.Add("Sounds like " + JoinNatural(tags) + ".");
        }

        if (!string.IsNullOrWhiteSpace(station.Language))
        {
            parts.Add("Language: " + ToTitleLike(station.Language) + ".");
        }

        parts.Add("Broadcasting from " + locationLabel + ".");
        parts.Add(quality + ".");

        string? sourceHost = GetSourceHostLabel(station.Homepage);
        if (!string.IsNullOrWhiteSpace(sourceHost))
        {
            parts.Add("Homepage: " + sourceHost + ".");
        }

        return string.Join(" ", parts);
    }

    private static string BuildQualityLabel(string? codec, int bitrate)
    {
        string codecLabel = string.IsNullOrWhiteSpace(codec) ? "Stream" : codec.ToUpperInvariant();
        return bitrate > 0
            ? codecLabel + " | " + bitrate.ToString(CultureInfo.InvariantCulture) + " kbps"
            : codecLabel;
    }

    private static List<string> GetDisplayTags(string? tags, string genre, int maxCount)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];

        foreach (string raw in (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string cleaned = ToDisplayTag(raw);
            if (IsGenericTag(cleaned) || !seen.Add(cleaned))
            {
                continue;
            }

            result.Add(cleaned);
            if (result.Count >= maxCount)
            {
                break;
            }
        }

        string genreLabel = ToDisplayTag(genre);
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(genreLabel) && !IsGenericTag(genreLabel))
        {
            result.Add(genreLabel);
        }

        return result;
    }

    private static bool IsGenericTag(string value)
    {
        string normalized = NormalizeLookupKey(value);
        return string.IsNullOrWhiteSpace(normalized) ||
               normalized is "radio" or "music" or "stream" or "webradio" or "online" or "fm" or "station" or "localradio";
    }

    private static string ToDisplayTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string cleaned = Regex.Replace(value.Trim(), @"[_/]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        return cleaned.Any(char.IsLower)
            ? ToTitleLike(cleaned)
            : cleaned;
    }

    private static string ToTitleLike(string value)
    {
        TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(value.ToLowerInvariant());
    }

    private static string? GetSourceHostLabel(string? homepage)
    {
        if (string.IsNullOrWhiteSpace(homepage) || !Uri.TryCreate(homepage, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string[] parts = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[^2] + "." + parts[^1];
        }

        return uri.Host;
    }

    private static string JoinNatural(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => items[0] + " and " + items[1],
            _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1]
        };
    }

    private static string BuildLocationLabel(string? city, string? region, string countryCode)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(city)) parts.Add(city);
        if (!string.IsNullOrWhiteSpace(region)) parts.Add(region);
        if (!string.IsNullOrWhiteSpace(countryCode)) parts.Add(countryCode);
        return parts.Count == 0 ? "Nearby" : string.Join(", ", parts);
    }

    private static string FormatWeatherTime(string? time, string? timezoneAbbreviation)
    {
        if (!string.IsNullOrWhiteSpace(time) &&
            DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
        {
            string clock = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(timezoneAbbreviation)
                ? "Updated " + clock
                : "Updated " + clock + " " + timezoneAbbreviation;
        }

        return "Current local conditions";
    }

    private static string DescribeWeatherCode(int code, bool isDay)
    {
        return code switch
        {
            0 => isDay ? "Clear sky" : "Clear night",
            1 => isDay ? "Mostly clear" : "Mostly clear night",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Foggy",
            51 or 53 or 55 => "Drizzle",
            56 or 57 => "Freezing drizzle",
            61 or 63 or 65 => "Rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 => "Snow",
            77 => "Snow grains",
            80 or 81 or 82 => "Rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Storm and hail",
            _ => "Local weather"
        };
    }

    private static string BuildCoordinateCacheKey(LocationProfile location)
    {
        return string.Join("|",
            location.CountryCode?.Trim().ToUpperInvariant() ?? string.Empty,
            location.Region?.Trim() ?? string.Empty,
            location.City?.Trim() ?? string.Empty);
    }

    private static string BuildGeocodeQuery(LocationProfile location)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(location.City))
        {
            parts.Add(location.City);
        }

        if (!string.IsNullOrWhiteSpace(location.Region))
        {
            parts.Add(location.Region);
        }

        if (!string.IsNullOrWhiteSpace(location.CountryName))
        {
            parts.Add(location.CountryName);
        }
        else if (!string.IsNullOrWhiteSpace(location.CountryCode))
        {
            parts.Add(location.CountryCode);
        }

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static (string? CountryCode, string? CountryName) ResolveCountry(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        string key = NormalizeLookupKey(text);
        if (CountryLookup.Value.TryGetValue(key, out string? code))
        {
            try
            {
                RegionInfo region = new(code);
                return (region.TwoLetterISORegionName.ToUpperInvariant(), region.EnglishName);
            }
            catch
            {
                return (code.ToUpperInvariant(), code.ToUpperInvariant());
            }
        }

        return (null, null);
    }

    private static string NormalizeLookupKey(string value)
    {
        string normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(normalized.Length);
        foreach (char ch in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string? NormalizeRegionName(string? countryCode, string? rawRegion)
    {
        if (string.IsNullOrWhiteSpace(rawRegion))
        {
            return null;
        }

        string cleaned = CleanRegionText(rawRegion);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        string code = (countryCode ?? string.Empty).ToUpperInvariant();
        string? catalogMatch = NormalizeFromCatalog(code, cleaned);
        if (!string.IsNullOrWhiteSpace(catalogMatch))
        {
            return catalogMatch;
        }

        return code switch
        {
            "US" => NormalizeMappedRegion(cleaned, GetAliasLookup(code)),
            "DE" => NormalizeMappedRegion(cleaned, GetAliasLookup(code)),
            "GR" => NormalizeMappedRegion(cleaned, GetAliasLookup(code)),
            "AT" => NormalizeMappedRegion(cleaned, GetAliasLookup(code)),
            _ => cleaned
        };
    }

    private static string? NormalizeMappedRegion(string rawRegion, Dictionary<string, string> lookup)
    {
        string key = NormalizeLookupKey(rawRegion);
        if (lookup.TryGetValue(key, out string? canonical))
        {
            return canonical;
        }

        string[] commaParts = rawRegion.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = commaParts.Length - 1; i >= 0; i--)
        {
            string commaKey = NormalizeLookupKey(commaParts[i]);
            if (lookup.TryGetValue(commaKey, out canonical))
            {
                return canonical;
            }
        }

        string[] tokenParts = rawRegion.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokenParts.Length > 0)
        {
            string tailKey = NormalizeLookupKey(tokenParts[^1]);
            if (lookup.TryGetValue(tailKey, out canonical))
            {
                return canonical;
            }
        }

        return null;
    }

    private static string CleanRegionText(string rawRegion)
    {
        string cleaned = rawRegion.Trim();
        cleaned = Regex.Replace(cleaned, @"\s+\d{4,}$", string.Empty).Trim();
        cleaned = Regex.Replace(cleaned, @"\b(greece|griechenland|hellas|usa|united states|united states of america|deutschland|germany)\b", string.Empty, RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ',', '-', '/');
        return cleaned;
    }

    private static Dictionary<string, CountryRegionCatalogEntry> LoadRegionCatalog()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "country-region-data.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, CountryRegionCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            }

            List<CountryRegionCatalogEntry> entries = Deserialize<List<CountryRegionCatalogEntry>>(File.ReadAllText(path)) ?? [];
            return entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.CountryShortCode))
                .ToDictionary(entry => entry.CountryShortCode.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, CountryRegionCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? NormalizeFromCatalog(string countryCode, string rawRegion)
    {
        Dictionary<string, string> aliases = GetAliasLookup(countryCode);
        if (aliases.Count > 0 && NormalizeMappedRegion(rawRegion, aliases) is string aliasMatch)
        {
            return aliasMatch;
        }

        if (!RegionCatalog.Value.TryGetValue(countryCode, out CountryRegionCatalogEntry? entry) || entry.Regions.Count == 0)
        {
            return null;
        }

        string normalized = NormalizeLookupKey(rawRegion);
        foreach (CountryRegionCatalogRegion region in entry.Regions)
        {
            if (string.Equals(normalized, NormalizeLookupKey(region.Name), StringComparison.OrdinalIgnoreCase))
            {
                return region.Name;
            }

            if (!string.IsNullOrWhiteSpace(region.ShortCode) &&
                string.Equals(normalized, NormalizeLookupKey(region.ShortCode), StringComparison.OrdinalIgnoreCase))
            {
                return region.Name;
            }
        }

        return null;
    }

    private static Dictionary<string, Dictionary<string, string>> BuildRegionAliasLookup()
    {
        Dictionary<string, Dictionary<string, string>> lookup = new(StringComparer.OrdinalIgnoreCase);

        void Add(string countryCode, string canonical, params string[] aliases)
        {
            if (!lookup.TryGetValue(countryCode, out Dictionary<string, string>? countryAliases))
            {
                countryAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                lookup[countryCode] = countryAliases;
            }

            countryAliases[NormalizeLookupKey(canonical)] = canonical;
            foreach (string alias in aliases)
            {
                countryAliases[NormalizeLookupKey(alias)] = canonical;
            }
        }

        void AddByShortCode(string countryCode, string shortCode, string fallbackName, params string[] aliases)
        {
            Add(countryCode, GetCanonicalRegionName(countryCode, shortCode, fallbackName), aliases);
        }

        AddByShortCode("US", "CA", "California", "CA");
        AddByShortCode("US", "TX", "Texas", "TX");
        AddByShortCode("US", "IL", "Illinois", "IL");
        AddByShortCode("US", "OH", "Ohio", "OH");
        AddByShortCode("US", "FL", "Florida", "FL");
        AddByShortCode("US", "GA", "Georgia", "GA");
        AddByShortCode("US", "LA", "Louisiana", "LA");
        AddByShortCode("US", "MD", "Maryland", "MD");
        AddByShortCode("US", "NJ", "New Jersey", "NJ");
        AddByShortCode("US", "DC", "District of Columbia", "DC");

        AddByShortCode("DE", "BW", "Baden-Württemberg", "badenwuerttemberg", "badenwurttemberg", "baden-wurttemberg");
        AddByShortCode("DE", "BY", "Bayern", "bavaria");
        AddByShortCode("DE", "HE", "Hessen", "hesse");
        AddByShortCode("DE", "NI", "Niedersachsen", "lower saxony");
        AddByShortCode("DE", "NW", "Nordrhein-Westfalen", "north rhine-westphalia", "nrw");
        AddByShortCode("DE", "RP", "Rheinland-Pfalz", "rhineland-palatinate");
        AddByShortCode("DE", "SN", "Sachsen", "saxony");
        AddByShortCode("DE", "ST", "Sachsen-Anhalt", "saxony-anhalt");
        AddByShortCode("DE", "TH", "Thüringen", "thuringia", "thueringen", "thuringen");

        AddByShortCode("AT", "3", "Niederösterreich", "lower austria");
        AddByShortCode("AT", "4", "Oberösterreich", "upper austria");
        AddByShortCode("AT", "9", "Wien", "vienna");
        AddByShortCode("AT", "6", "Steiermark", "styria");
        AddByShortCode("AT", "2", "Kärnten", "carinthia", "karnten");

        AddByShortCode("GR", "I", "Attica", "attica", "attiki", "athens", "athina", "athens greece", "attica athens", "athens attica");
        AddByShortCode("GR", "M", "Crete", "crete", "kriti");
        AddByShortCode("GR", "G", "Western Greece", "western greece");
        AddByShortCode("GR", "C", "Western Macedonia", "western macedonia");
        AddByShortCode("GR", "A", "East Macedonia and Thrace", "east macedonia and thrace");
        AddByShortCode("GR", "B", "Central Macedonia", "central macedonia");
        AddByShortCode("GR", "D", "Epirus", "epirus", "ipeiros");
        AddByShortCode("GR", "F", "Ionian Islands", "ionian islands");
        AddByShortCode("GR", "L", "South Aegean", "south aegean");
        AddByShortCode("GR", "K", "North Aegean", "north aegean");
        AddByShortCode("GR", "J", "Peloponnese", "peloponnese");
        AddByShortCode("GR", "H", "Central Greece", "central greece", "sterea ellada");
        AddByShortCode("GR", "E", "Thessaly", "thessaly");

        return lookup;
    }

    private static Dictionary<string, string> GetAliasLookup(string countryCode)
    {
        return RegionAliasLookup.Value.TryGetValue(countryCode, out Dictionary<string, string>? aliases)
            ? aliases
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetCanonicalRegionName(string countryCode, string shortCode, string fallbackName)
    {
        if (RegionCatalog.Value.TryGetValue(countryCode, out CountryRegionCatalogEntry? entry))
        {
            CountryRegionCatalogRegion? region = entry.Regions.FirstOrDefault(r => string.Equals(r.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase));
            if (region != null && !string.IsNullOrWhiteSpace(region.Name))
            {
                return region.Name;
            }
        }

        return fallbackName;
    }

    private static Dictionary<string, string> BuildCountryLookup()
    {
        Dictionary<string, string> lookup = new(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = "DE",
            ["deutschland"] = "DE",
            ["germany"] = "DE",
            ["us"] = "US",
            ["usa"] = "US",
            ["unitedstates"] = "US",
            ["unitedstatesofamerica"] = "US",
            ["vereinigtestaaten"] = "US",
            ["uk"] = "GB",
            ["greatbritain"] = "GB",
            ["unitedkingdom"] = "GB"
        };

        foreach (CountryOption option in GetCountryOptions())
        {
            lookup[NormalizeLookupKey(option.CountryCode)] = option.CountryCode;
            lookup[NormalizeLookupKey(option.DisplayName)] = option.CountryCode;
        }

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                RegionInfo region = new(culture.Name);
                lookup[NormalizeLookupKey(region.TwoLetterISORegionName)] = region.TwoLetterISORegionName.ToUpperInvariant();
                lookup[NormalizeLookupKey(region.EnglishName)] = region.TwoLetterISORegionName.ToUpperInvariant();
                lookup[NormalizeLookupKey(region.DisplayName)] = region.TwoLetterISORegionName.ToUpperInvariant();
                lookup[NormalizeLookupKey(region.NativeName)] = region.TwoLetterISORegionName.ToUpperInvariant();
            }
            catch
            {
            }
        }

        return lookup;
    }

    private static T? Deserialize<T>(string json)
    {
        DataContractJsonSerializer serializer = new(typeof(T));
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using MemoryStream stream = new(bytes);
        return (T?)serializer.ReadObject(stream);
    }

    private static HttpClient CreateClient()
    {
        HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        HttpClient client = new(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RadioBloom.WinUI/1.0");
        return client;
    }
}

public sealed class NativeAudioPlayer : IDisposable
{
    private static readonly HttpClient MetadataClient = CreateMetadataClient();
    private IWavePlayer? _player;
    private MediaFoundationReader? _reader;
    private SpectrumSampleProvider? _spectrumProvider;
    private VolumeSampleProvider? _volumeProvider;
    private CancellationTokenSource? _metadataCts;
    private string? _currentTrackInfo;
    private bool _hasSource;
    private double _volume;

    public event Action<string?>? TrackInfoChanged;

    public NativeAudioPlayer()
    {
        _volume = 0.72;
        _player = null;
        _hasSource = false;
    }

    public void SetVolume(double volume)
    {
        _volume = volume;
        if (_volumeProvider != null)
        {
            _volumeProvider.Volume = (float)volume;
        }
    }

    public void Open(string url)
    {
        ResetPlayer();
        _reader = new MediaFoundationReader(url);
        _spectrumProvider = new SpectrumSampleProvider(_reader.ToSampleProvider(), 14);
        _volumeProvider = new VolumeSampleProvider(_spectrumProvider)
        {
            Volume = (float)_volume
        };
        _player = new WaveOutEvent();
        _player.Init(_volumeProvider.ToWaveProvider());
        _hasSource = true;
        StartMetadataMonitoring(url);
    }

    public void Play() => _player?.Play();
    public void Pause() => _player?.Pause();
    public void Stop() => ResetPlayer();

    public float GetAudioLevel()
    {
        return _spectrumProvider?.PeakLevel ?? 0f;
    }

    public float[] GetSpectrumLevels()
    {
        return _spectrumProvider?.GetSpectrumLevels() ?? [];
    }

    public NativePlayerState GetState()
    {
        if (_player == null || !_hasSource) return NativePlayerState.Ready;
        return _player.PlaybackState switch
        {
            PlaybackState.Playing => NativePlayerState.Live,
            PlaybackState.Paused => NativePlayerState.Paused,
            PlaybackState.Stopped => NativePlayerState.Stopped,
            _ => NativePlayerState.Stopped
        };
    }

    public void Dispose()
    {
        StopMetadataMonitoring();
        SafeDisposePlayer();
    }

    private void ResetPlayer()
    {
        StopMetadataMonitoring();
        SafeDisposePlayer();
        _hasSource = false;
    }

    private void StartMetadataMonitoring(string url)
    {
        StopMetadataMonitoring();
        _metadataCts = new CancellationTokenSource();
        NotifyTrackInfoChanged(null);
        _ = PollTrackInfoAsync(url, _metadataCts.Token);
    }

    private void StopMetadataMonitoring()
    {
        if (_metadataCts != null)
        {
            try
            {
                _metadataCts.Cancel();
            }
            catch
            {
            }

            _metadataCts.Dispose();
            _metadataCts = null;
        }

        NotifyTrackInfoChanged(null);
    }

    private async Task PollTrackInfoAsync(string url, CancellationToken cancellationToken)
    {
        bool announcedUnavailable = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                string? trackInfo = await TryReadTrackInfoAsync(url, cancellationToken);
                if (!string.IsNullOrWhiteSpace(trackInfo))
                {
                    announcedUnavailable = false;
                    NotifyTrackInfoChanged(trackInfo);
                    await Task.Delay(TimeSpan.FromSeconds(18), cancellationToken);
                    continue;
                }

                if (!announcedUnavailable)
                {
                    NotifyTrackInfoChanged(null);
                    announcedUnavailable = true;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (!announcedUnavailable)
                {
                    NotifyTrackInfoChanged(null);
                    announcedUnavailable = true;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<string?> TryReadTrackInfoAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

        using HttpResponseMessage response = await MetadataClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!TryGetMetaInt(response, out int metaInt) || metaInt <= 0)
        {
            return null;
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await SkipExactAsync(stream, metaInt, cancellationToken);

        int lengthByte = stream.ReadByte();
        if (lengthByte <= 0)
        {
            return null;
        }

        int metadataLength = lengthByte * 16;
        byte[] metadataBuffer = await ReadExactAsync(stream, metadataLength, cancellationToken);
        string metadata = Encoding.UTF8.GetString(metadataBuffer).Trim('\0', ' ', '\r', '\n', '\t');
        return ParseStreamTitle(metadata);
    }

    private static bool TryGetMetaInt(HttpResponseMessage response, out int value)
    {
        value = 0;
        IEnumerable<string>? values = null;

        if (response.Headers.TryGetValues("icy-metaint", out values) ||
            response.Headers.TryGetValues("Ice-Metaint", out values) ||
            response.Content.Headers.TryGetValues("icy-metaint", out values))
        {
            string? raw = values.FirstOrDefault();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static async Task SkipExactAsync(Stream stream, int byteCount, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[Math.Min(byteCount, 8192)];
        int remaining = byteCount;
        while (remaining > 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while skipping audio payload.");
            }

            remaining -= read;
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int byteCount, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[byteCount];
        int offset = 0;
        while (offset < byteCount)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, byteCount - offset), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        if (offset == byteCount)
        {
            return buffer;
        }

        return buffer[..offset];
    }

    private static string? ParseStreamTitle(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        Match match = Regex.Match(metadata, "StreamTitle='(?<title>.*?)';", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        string title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (string.Equals(title, "-", StringComparison.Ordinal))
        {
            return null;
        }

        return title;
    }

    private void NotifyTrackInfoChanged(string? trackInfo)
    {
        string? normalized = string.IsNullOrWhiteSpace(trackInfo) ? null : trackInfo.Trim();
        if (string.Equals(_currentTrackInfo, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _currentTrackInfo = normalized;
        TrackInfoChanged?.Invoke(normalized);
    }

    private void SafeDisposePlayer()
    {
        _spectrumProvider = null;

        try
        {
            _player?.Stop();
        }
        catch
        {
        }

        _player?.Dispose();
        _player = null;
        _reader?.Dispose();
        _reader = null;
        _volumeProvider = null;
    }

    private static HttpClient CreateMetadataClient()
    {
        HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RadioBloom.WinUI/1.0");
        client.DefaultRequestHeaders.ConnectionClose = false;
        return client;
    }
}

internal sealed class SpectrumSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _fftLength;
    private readonly int _fftM;
    private readonly int _bandCount;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _window;
    private readonly float[] _bandLevels;
    private readonly object _sync = new();
    private int _fftPos;

    public SpectrumSampleProvider(ISampleProvider source, int bandCount)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _fftLength = 1024;
        _fftM = 10;
        _bandCount = bandCount;
        _fftBuffer = new Complex[_fftLength];
        _window = BuildHannWindow(_fftLength);
        _bandLevels = new float[_bandCount];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float PeakLevel { get; private set; }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read <= 0)
        {
            PeakLevel *= 0.82f;
            DecayBands();
            return read;
        }

        float peak = 0f;
        int end = offset + read;

        for (int n = offset; n < end; n += _channels)
        {
            float mono = 0f;
            int availableChannels = Math.Min(_channels, end - n);
            for (int c = 0; c < availableChannels; c++)
            {
                mono += buffer[n + c];
            }

            mono /= Math.Max(1, availableChannels);
            float abs = Math.Abs(mono);
            if (abs > peak)
            {
                peak = abs;
            }

            _fftBuffer[_fftPos].X = mono * _window[_fftPos];
            _fftBuffer[_fftPos].Y = 0f;
            _fftPos++;

            if (_fftPos >= _fftLength)
            {
                AnalyzeFft();
                _fftPos = 0;
            }
        }

        PeakLevel = Math.Clamp(Math.Max(peak, PeakLevel * 0.68f), 0f, 1f);
        return read;
    }

    public float[] GetSpectrumLevels()
    {
        lock (_sync)
        {
            return (float[])_bandLevels.Clone();
        }
    }

    private void AnalyzeFft()
    {
        Complex[] working = new Complex[_fftLength];
        Array.Copy(_fftBuffer, working, _fftLength);
        FastFourierTransform.FFT(true, _fftM, working);

        float[] nextBands = new float[_bandCount];
        int usefulBins = _fftLength / 2;
        double minFreq = 35.0;
        double maxFreq = Math.Min(16000.0, WaveFormat.SampleRate / 2.0);

        for (int band = 0; band < _bandCount; band++)
        {
            double startFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)band / _bandCount);
            double endFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)(band + 1) / _bandCount);
            int startBin = Math.Clamp((int)Math.Floor(startFreq / WaveFormat.SampleRate * _fftLength), 1, usefulBins - 1);
            int endBin = Math.Clamp((int)Math.Ceiling(endFreq / WaveFormat.SampleRate * _fftLength), startBin + 1, usefulBins);

            double energy = 0;
            int samples = 0;
            for (int bin = startBin; bin < endBin; bin++)
            {
                double re = working[bin].X;
                double im = working[bin].Y;
                double magnitude = Math.Sqrt((re * re) + (im * im));
                energy += magnitude;
                samples++;
            }

            double average = samples == 0 ? 0 : energy / samples;
            double normalized = Math.Log10(1 + (average * 90.0)) / 1.28;
            nextBands[band] = (float)Math.Clamp(normalized, 0.0, 1.0);
        }

        lock (_sync)
        {
            for (int i = 0; i < _bandCount; i++)
            {
                _bandLevels[i] = Math.Clamp(Math.Max(nextBands[i], _bandLevels[i] * 0.72f), 0f, 1f);
            }
        }
    }

    private void DecayBands()
    {
        lock (_sync)
        {
            for (int i = 0; i < _bandLevels.Length; i++)
            {
                _bandLevels[i] *= 0.8f;
            }
        }
    }

    private static float[] BuildHannWindow(int length)
    {
        float[] window = new float[length];
        for (int i = 0; i < length; i++)
        {
            window[i] = (float)(0.5 * (1 - Math.Cos((2 * Math.PI * i) / (length - 1))));
        }

        return window;
    }
}
