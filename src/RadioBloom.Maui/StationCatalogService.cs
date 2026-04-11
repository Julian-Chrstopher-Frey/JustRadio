using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioBloom.Maui;

public sealed record CountryOption(string CountryCode, string DisplayName);

public sealed record LocationProfile(string CountryCode, string CountryName, string? Region, string Source)
{
	public string DisplayName => string.IsNullOrWhiteSpace(Region) ? CountryName : $"{Region}, {CountryName}";
}

public sealed class RadioStation : INotifyPropertyChanged
{
	private bool _isHovered;
	private bool _isSelected;

	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Location { get; set; } = string.Empty;
	public string Genre { get; set; } = string.Empty;
	public List<string> Categories { get; set; } = [];
	public bool Featured { get; set; }
	public string Tagline { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string StreamUrl { get; set; } = string.Empty;
	public List<string> PlaybackUrls { get; set; } = [];
	public string MetadataSummary { get; set; } = string.Empty;
	public int PopularityScore { get; set; }
	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if (_isSelected == value)
			{
				return;
			}

			_isSelected = value;
			OnPropertyChanged();
		}
	}
	public bool IsHovered
	{
		get => _isHovered;
		set
		{
			if (_isHovered == value)
			{
				return;
			}

			_isHovered = value;
			OnPropertyChanged();
		}
	}

	public string SearchText => string.Join(" ", Name, Location, Genre, Tagline, Description, MetadataSummary);

	public event PropertyChangedEventHandler? PropertyChanged;

	public bool MatchesCategory(string category)
	{
		return string.Equals(category, "Featured", StringComparison.OrdinalIgnoreCase)
			? Featured
			: Categories.Contains(category, StringComparer.OrdinalIgnoreCase);
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

[DataContract]
internal sealed class RadioBrowserStation
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
internal sealed class IpApiResponse
{
	[DataMember(Name = "region")] public string? Region { get; set; }
	[DataMember(Name = "country_code")] public string? CountryCode { get; set; }
	[DataMember(Name = "country_name")] public string? CountryName { get; set; }
}

public sealed class StationCatalogService
{
	public const string AllRegionsLabel = "All regions";
	private static readonly HttpClient Client = CreateClient();
	private static readonly HashSet<string> VariantNoiseTokens = new(StringComparer.OrdinalIgnoreCase)
	{
		"radio", "live", "stream", "hq", "hd", "aac", "aacp", "mp3", "ogg", "fm", "am",
		"kbps", "high", "low", "quality", "best", "direct", "player", "official", "alternative", "alt"
	};

	private static readonly List<CountryOption> Countries = BuildCountryOptions();

	private static readonly Dictionary<string, string[]> RegionOptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["DE"] = ["Baden-Wurttemberg", "Bavaria", "Berlin", "Brandenburg", "Bremen", "Hamburg", "Hesse", "Lower Saxony", "North Rhine-Westphalia", "Saxony", "Schleswig-Holstein"],
		["US"] = ["California", "Florida", "Illinois", "New York", "Texas", "Washington"],
		["AT"] = ["Burgenland", "Lower Austria", "Styria", "Tyrol", "Upper Austria", "Vienna"],
		["GR"] = ["Attica", "Central Macedonia", "Crete", "Thessaly"],
		["GB"] = ["England", "Northern Ireland", "Scotland", "Wales"],
		["CH"] = ["Bern", "Geneva", "Ticino", "Zurich"]
	};

	public static List<CountryOption> GetCountryOptions() => Countries.ToList();

	private static List<CountryOption> BuildCountryOptions()
	{
		string[] pinnedCodes = ["DE", "US", "AT", "GR", "GB", "FR", "IT", "ES", "NL", "CH"];
		Dictionary<string, string> countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
			.Select(culture =>
			{
				try
				{
					return new RegionInfo(culture.Name);
				}
				catch
				{
					return null;
				}
			})
			.Where(region => region != null && region.TwoLetterISORegionName.Length == 2)
			.GroupBy(region => region!.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				group => group.Key.ToUpperInvariant(),
				group => group.OrderBy(region => region!.EnglishName.Length).First()!.EnglishName,
				StringComparer.OrdinalIgnoreCase);

		foreach ((string code, string displayName) in new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["DE"] = "Germany",
			["US"] = "United States",
			["GB"] = "United Kingdom",
			["GR"] = "Greece"
		})
		{
			countries[code] = displayName;
		}

		List<CountryOption> pinned = pinnedCodes
			.Where(countries.ContainsKey)
			.Select(code => new CountryOption(code, countries[code]))
			.ToList();

		List<CountryOption> remaining = countries
			.Where(entry => !pinnedCodes.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
			.OrderBy(entry => entry.Value, StringComparer.CurrentCultureIgnoreCase)
			.Select(entry => new CountryOption(entry.Key, entry.Value))
			.ToList();

		pinned.AddRange(remaining);
		return pinned;
	}

	public static List<string> GetRegions(string? countryCode)
	{
		List<string> regions = [AllRegionsLabel];
		if (!string.IsNullOrWhiteSpace(countryCode) && RegionOptions.TryGetValue(countryCode, out string[]? entries))
		{
			regions.AddRange(entries);
		}

		return regions;
	}

	public async Task<LocationProfile> GetApproximateLocationAsync()
	{
		try
		{
			string json = await Client.GetStringAsync("https://ipapi.co/json/");
			IpApiResponse? response = Deserialize<IpApiResponse>(json);
			string code = response?.CountryCode?.ToUpperInvariant() ?? RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
			CountryOption? country = Countries.FirstOrDefault(option => string.Equals(option.CountryCode, code, StringComparison.OrdinalIgnoreCase));
			string countryName = response?.CountryName ?? country?.DisplayName ?? RegionInfo.CurrentRegion.EnglishName;
			string? region = NormalizeRegion(code, response?.Region);
			return new LocationProfile(code, countryName, region, "Approximate IP location");
		}
		catch
		{
			string code = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
			string name = Countries.FirstOrDefault(option => option.CountryCode == code)?.DisplayName ?? RegionInfo.CurrentRegion.EnglishName;
			return new LocationProfile(code, name, null, "System region");
		}
	}

	public async Task<List<RadioStation>> LoadStationsAsync(LocationProfile location)
	{
		try
		{
			List<RadioBrowserStation> raw = await QueryStationsAsync(location.CountryCode, location.Region, string.Equals(location.CountryCode, "DE", StringComparison.OrdinalIgnoreCase) ? 80 : 48);
			if (raw.Count == 0 && !string.IsNullOrWhiteSpace(location.Region))
			{
				raw = await QueryStationsAsync(location.CountryCode, null, 48);
			}

			List<List<RadioBrowserStation>> variantGroups = BuildVariantGroups(raw);
			List<RadioStation> stations = variantGroups
				.Take(64)
				.Select((group, index) => MapStation(
					ChooseDisplayVariant(group),
					location,
					index,
					BuildPlaybackUrls(group),
					group.Max(station => station.ClickCount + station.Votes)))
				.ToList();

			return stations.Count == 0 ? GetFallbackStations() : stations;
		}
		catch
		{
			return GetFallbackStations();
		}
	}

	public static List<string> BuildCategoryList(IEnumerable<RadioStation> stations)
	{
		List<string> categories = ["All"];
		string[] preferred = ["Featured", "News", "Talk", "Pop", "Rock", "Jazz", "Electronic", "Classical", "Sports", "Culture", "Local"];
		foreach (string category in preferred)
		{
			if (stations.Any(station => station.MatchesCategory(category)))
			{
				categories.Add(category);
			}
		}

		return categories;
	}

	private static async Task<List<RadioBrowserStation>> QueryStationsAsync(string countryCode, string? region, int limit)
	{
		StringBuilder uri = new("https://all.api.radio-browser.info/json/stations/search?hidebroken=true&order=clickcount&reverse=true");
		uri.Append("&countrycode=").Append(Uri.EscapeDataString(countryCode));
		uri.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		if (!string.IsNullOrWhiteSpace(region))
		{
			uri.Append("&state=").Append(Uri.EscapeDataString(region));
		}

		string json = await Client.GetStringAsync(uri.ToString());
		return Deserialize<List<RadioBrowserStation>>(json) ?? [];
	}

	private static RadioStation MapStation(RadioBrowserStation station, LocationProfile location, int rank, List<string> playbackUrls, int popularityScore)
	{
		string genre = GetPrimaryTag(station.Tags);
		string locationLabel = !string.IsNullOrWhiteSpace(station.State)
			? $"{NormalizeRegion(station.CountryCode, station.State)}, {station.CountryCode}"
			: station.Country ?? location.CountryName;

		return new RadioStation
		{
			Id = "rb-" + (station.StationUuid ?? station.Name),
			Name = station.Name ?? "Unknown station",
			Location = locationLabel,
			Genre = genre,
			Categories = GetCategories((station.Name ?? string.Empty) + " " + (station.Tags ?? string.Empty), rank < 10),
			Featured = rank < 10,
			Tagline = BuildTagline(genre, station.Homepage),
			Description = BuildDescription(station, locationLabel),
			StreamUrl = playbackUrls.FirstOrDefault() ?? station.UrlResolved ?? string.Empty,
			PlaybackUrls = playbackUrls,
			MetadataSummary = BuildMetadataSummary(station),
			PopularityScore = popularityScore
		};
	}

	private static bool IsPlayable(RadioBrowserStation station)
	{
		string codec = (station.Codec ?? string.Empty).ToUpperInvariant();
		return station.LastCheckOk == 1 &&
			station.Hls != 1 &&
			!string.IsNullOrWhiteSpace(station.Name) &&
			!string.IsNullOrWhiteSpace(station.UrlResolved) &&
			(codec == "MP3" || codec == "AAC" || codec == "AAC+" || codec == "AACP" || codec == "OGG");
	}

	private static List<List<RadioBrowserStation>> BuildVariantGroups(IEnumerable<RadioBrowserStation> stations)
	{
		List<List<RadioBrowserStation>> groups = [];
		Dictionary<string, List<RadioBrowserStation>> lookup = new(StringComparer.OrdinalIgnoreCase);
		foreach (RadioBrowserStation station in stations.Where(IsPlayable))
		{
			string key = BuildVariantGroupKey(station);
			if (!lookup.TryGetValue(key, out List<RadioBrowserStation>? group))
			{
				group = [];
				lookup[key] = group;
				groups.Add(group);
			}

			group.Add(station);
		}

		return groups;
	}

	private static RadioBrowserStation ChooseDisplayVariant(IEnumerable<RadioBrowserStation> variants)
	{
		return OrderByPlaybackPreference(variants).First();
	}

	private static List<string> BuildPlaybackUrls(IEnumerable<RadioBrowserStation> variants)
	{
		List<RadioBrowserStation> variantList = variants.ToList();
		return BuildOfficialPlaybackUrlOverrides(variantList)
			.Concat(OrderByPlaybackPreference(variantList).Select(variant => variant.UrlResolved!))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IOrderedEnumerable<RadioBrowserStation> OrderByPlaybackPreference(IEnumerable<RadioBrowserStation> variants)
	{
		RadioBrowserStation[] variantArray = variants
			.Where(variant => !string.IsNullOrWhiteSpace(variant.UrlResolved))
			.ToArray();
		string[] homepageHosts = variantArray
			.Select(variant => TryGetHost(variant.Homepage))
			.Where(host => !string.IsNullOrWhiteSpace(host))
			.Select(host => host!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return variantArray
			.OrderByDescending(GetBrowserPlaybackScore)
			.ThenByDescending(variant => IsSecureStreamUrl(variant.UrlResolved))
			.ThenByDescending(variant => GetHomepageHostAffinityScore(variant, homepageHosts))
			.ThenByDescending(variant => variant.Bitrate)
			.ThenByDescending(variant => variant.ClickCount + variant.Votes);
	}

	private static List<RadioStation> GetFallbackStations()
	{
		return
		[
			new RadioStation
			{
				Id = "fallback-dlf",
				Name = "Deutschlandfunk",
				Location = "Germany",
				Genre = "News",
				Categories = ["Featured", "News", "Talk"],
				Featured = true,
				Tagline = "Calm, information-dense public radio.",
				Description = "National public radio with news, features, and in-depth reporting.",
				StreamUrl = "https://st01.sslstream.dlf.de/dlf/01/128/mp3/stream.mp3",
				MetadataSummary = "Deutschlandradio | MP3 | 128 kbps",
				PopularityScore = 100
			},
			new RadioStation
			{
				Id = "fallback-kexp",
				Name = "KEXP 90.3",
				Location = "Seattle, US",
				Genre = "Indie",
				Categories = ["Featured", "Rock", "Culture"],
				Featured = true,
				Tagline = "Human-curated discovery radio.",
				Description = "A strong fallback for discovery and indie listening.",
				StreamUrl = "https://kexp.streamguys1.com/kexp160.aac",
				MetadataSummary = "KEXP | AAC | 160 kbps",
				PopularityScore = 80
			}
		];
	}

	private static string BuildMetadataSummary(RadioBrowserStation station)
	{
		List<string> parts = [];
		if (!string.IsNullOrWhiteSpace(station.Language))
		{
			parts.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(station.Language));
		}
		if (!string.IsNullOrWhiteSpace(station.Codec))
		{
			parts.Add(station.Codec.ToUpperInvariant());
		}
		if (station.Bitrate > 0)
		{
			parts.Add(station.Bitrate.ToString(CultureInfo.InvariantCulture) + " kbps");
		}
		string? host = TryGetHost(station.Homepage);
		if (!string.IsNullOrWhiteSpace(host))
		{
			parts.Add(host);
		}
		return string.Join(" | ", parts);
	}

	private static string BuildDescription(RadioBrowserStation station, string locationLabel)
	{
		string tags = string.IsNullOrWhiteSpace(station.Tags) ? "local radio" : station.Tags.Replace(",", ", ");
		return $"Broadcasting from {locationLabel}. Tags: {tags}.";
	}

	private static string BuildTagline(string genre, string? homepage)
	{
		string? host = TryGetHost(homepage);
		return string.IsNullOrWhiteSpace(host)
			? $"{genre} station from Radio Browser."
			: $"{genre} via {host}.";
	}

	private static List<string> GetCategories(string text, bool featured)
	{
		List<string> categories = [];
		string lowered = text.ToLowerInvariant();
		if (featured) categories.Add("Featured");
		if (lowered.Contains("news")) categories.Add("News");
		if (lowered.Contains("talk") || lowered.Contains("politic")) categories.Add("Talk");
		if (lowered.Contains("pop") || lowered.Contains("hits") || lowered.Contains("80s") || lowered.Contains("90s")) categories.Add("Pop");
		if (lowered.Contains("rock") || lowered.Contains("indie") || lowered.Contains("alternative")) categories.Add("Rock");
		if (lowered.Contains("jazz") || lowered.Contains("blues")) categories.Add("Jazz");
		if (lowered.Contains("electro") || lowered.Contains("dance") || lowered.Contains("house") || lowered.Contains("techno")) categories.Add("Electronic");
		if (lowered.Contains("classic")) categories.Add("Classical");
		if (lowered.Contains("sport")) categories.Add("Sports");
		if (lowered.Contains("culture") || lowered.Contains("community") || lowered.Contains("folk")) categories.Add("Culture");
		if (categories.Count == 0) categories.Add("Local");
		return categories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string GetPrimaryTag(string? tags)
	{
		if (string.IsNullOrWhiteSpace(tags))
		{
			return "Local Radio";
		}

		return tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.FirstOrDefault(tag => tag.Length > 2 && !tag.Contains("stream", StringComparison.OrdinalIgnoreCase))
			?? "Local Radio";
	}

	private static string? NormalizeRegion(string? countryCode, string? region)
	{
		if (string.IsNullOrWhiteSpace(region))
		{
			return null;
		}

		string value = region.Trim();
		if (RegionOptions.TryGetValue(countryCode ?? string.Empty, out string[]? regions))
		{
			return regions.FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) ?? value;
		}

		return value;
	}

	private static string? TryGetHost(string? url)
	{
		if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
		{
			return null;
		}

		return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
	}

	private static string BuildVariantGroupKey(RadioBrowserStation station)
	{
		string normalizedName = NormalizeStationName(station.Name);
		string normalizedHost = NormalizeKeyPart(TryGetHost(station.Homepage) ?? TryGetHost(station.UrlResolved));
		return string.Join("|", normalizedName, normalizedHost);
	}

	private static IEnumerable<string> BuildOfficialPlaybackUrlOverrides(IEnumerable<RadioBrowserStation> variants)
	{
		List<RadioBrowserStation> variantList = variants.ToList();
		string[] homepageHosts = variantList
			.Select(variant => TryGetHost(variant.Homepage))
			.Where(host => !string.IsNullOrWhiteSpace(host))
			.Select(host => host!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		string normalizedName = NormalizeStationName(ChooseDisplayVariant(variantList).Name);
		List<string> urls = [];

		if (homepageHosts.Contains("lora924.de", StringComparer.OrdinalIgnoreCase) &&
			normalizedName.Contains("lora", StringComparison.OrdinalIgnoreCase))
		{
			urls.Add("https://stream.lora924.de:8080/lora-mp3-256.mp3");
			urls.Add("https://stream.lora924.de:8080/lora-mp3-64.mp3");
			urls.Add("http://stream.lora924.de:8000/lora-mp3-256.mp3");
		}

		if (homepageHosts.Contains("radioarabella.de", StringComparer.OrdinalIgnoreCase) &&
			normalizedName.Contains("arabella", StringComparison.OrdinalIgnoreCase) &&
			normalizedName.Contains("munchen", StringComparison.OrdinalIgnoreCase))
		{
			urls.Add("https://live.stream.radioarabella.de/radioarabella-muenchen/stream/mp3?ref=radioarabella.de");
		}

		if (homepageHosts.Contains("radiogong.com", StringComparer.OrdinalIgnoreCase) &&
			normalizedName.Contains("gongwurzburg", StringComparison.OrdinalIgnoreCase))
		{
			urls.Add("http://frontend.streamonkey.net/gong-live/stream/mp3?aggregator=user");
		}

		if (homepageHosts.Contains("986charivari.de", StringComparer.OrdinalIgnoreCase) &&
			normalizedName.Contains("986charivari", StringComparison.OrdinalIgnoreCase))
		{
			urls.Add("http://frontend.streamonkey.net/fhn-986charivari?aggregator=fh-tinyurl");
		}

		return urls;
	}

	private static int GetBrowserPlaybackScore(RadioBrowserStation station)
	{
		string codec = (station.Codec ?? string.Empty).Trim().ToUpperInvariant();
		int score = codec switch
		{
			"MP3" => 50,
			"AAC" => 45,
			"AAC+" => 45,
			"AACP" => 45,
			"OGG" => 20,
			_ => 0
		};

		string url = station.UrlResolved ?? string.Empty;
		if (url.Contains(".mp3", StringComparison.OrdinalIgnoreCase))
		{
			score += 8;
		}
		if (url.Contains(".aac", StringComparison.OrdinalIgnoreCase) ||
			url.Contains(".m4a", StringComparison.OrdinalIgnoreCase) ||
			url.Contains(".mp4", StringComparison.OrdinalIgnoreCase))
		{
			score += 6;
		}
		if (url.Contains(".flac", StringComparison.OrdinalIgnoreCase))
		{
			score -= 15;
		}

		return score;
	}

	private static int GetHomepageHostAffinityScore(RadioBrowserStation station, IEnumerable<string> homepageHosts)
	{
		string? streamHost = TryGetHost(station.UrlResolved);
		if (string.IsNullOrWhiteSpace(streamHost))
		{
			return 0;
		}

		foreach (string homepageHost in homepageHosts)
		{
			if (string.Equals(streamHost, homepageHost, StringComparison.OrdinalIgnoreCase))
			{
				return 3;
			}
			if (streamHost.EndsWith("." + homepageHost, StringComparison.OrdinalIgnoreCase))
			{
				return 2;
			}
		}

		return 0;
	}

	private static string NormalizeKeyPart(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		StringBuilder builder = new(value.Length);
		foreach (char character in value)
		{
			if (char.IsLetterOrDigit(character))
			{
				builder.Append(char.ToLowerInvariant(character));
			}
		}

		return builder.ToString();
	}

	private static string NormalizeStationName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string normalized = value.Normalize(NormalizationForm.FormD);
		StringBuilder ascii = new(normalized.Length);
		foreach (char character in normalized)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
			{
				ascii.Append(character);
			}
		}

		return string.Concat(
			Regex.Matches(ascii.ToString(), "[A-Za-z0-9]+")
				.Select(match => match.Value.ToLowerInvariant())
				.Where(token => !VariantNoiseTokens.Contains(token)));
	}

	private static bool IsSecureStreamUrl(string? url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
			&& string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
	}

	private static T? Deserialize<T>(string json)
	{
		DataContractJsonSerializer serializer = new(typeof(T));
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
		return (T?)serializer.ReadObject(stream);
	}

	private static HttpClient CreateClient()
	{
		HttpClientHandler handler = new()
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};

		HttpClient client = new(handler);
		client.DefaultRequestHeaders.UserAgent.ParseAdd("JustRadio.Maui/0.1");
		return client;
	}
}
