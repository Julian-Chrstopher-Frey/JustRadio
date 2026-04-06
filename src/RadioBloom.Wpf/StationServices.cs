using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace RadioBloom.Wpf
{
    [DataContract]
    public sealed class RadioBrowserStation
    {
        [DataMember(Name = "stationuuid")] public string StationUuid { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "url_resolved")] public string UrlResolved { get; set; }
        [DataMember(Name = "homepage")] public string Homepage { get; set; }
        [DataMember(Name = "country")] public string Country { get; set; }
        [DataMember(Name = "countrycode")] public string CountryCode { get; set; }
        [DataMember(Name = "state")] public string State { get; set; }
        [DataMember(Name = "codec")] public string Codec { get; set; }
        [DataMember(Name = "bitrate")] public int Bitrate { get; set; }
        [DataMember(Name = "lastcheckok")] public int LastCheckOk { get; set; }
        [DataMember(Name = "hls")] public int Hls { get; set; }
        [DataMember(Name = "tags")] public string Tags { get; set; }
        [DataMember(Name = "clickcount")] public int ClickCount { get; set; }
        [DataMember(Name = "votes")] public int Votes { get; set; }
    }

    [DataContract]
    public sealed class IpApiResponse
    {
        [DataMember(Name = "city")] public string City { get; set; }
        [DataMember(Name = "region")] public string Region { get; set; }
        [DataMember(Name = "country_code")] public string CountryCode { get; set; }
        [DataMember(Name = "country_name")] public string CountryName { get; set; }
    }

    public sealed class RadioStation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public string Location { get; set; }
        public string Genre { get; set; }
        public string Mood { get; set; }
        public List<string> Categories { get; set; }
        public bool Featured { get; set; }
        public string Tagline { get; set; }
        public string Description { get; set; }
        public string StreamUrl { get; set; }
        public string Quality { get; set; }
        public string SourceLabel { get; set; }
        public string SourceUrl { get; set; }
    }

    public sealed class LocationProfile
    {
        public string City { get; set; }
        public string Region { get; set; }
        public string CountryCode { get; set; }
        public string CountryName { get; set; }
        public string Label { get; set; }
        public string Source { get; set; }
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

            RegionInfo region = new RegionInfo(cultureName);
            return new LocationProfile
            {
                CountryCode = region.TwoLetterISORegionName.ToUpperInvariant(),
                CountryName = region.EnglishName,
                Label = region.EnglishName,
                Source = "System region"
            };
        }

        public static LocationProfile GetApproximateLocation()
        {
            LocationProfile fallback = GetDefaultLocation();

            try
            {
                string json = Client.GetStringAsync("https://ipapi.co/json/").Result;
                IpApiResponse data = Deserialize<IpApiResponse>(json);
                if (data == null || string.IsNullOrWhiteSpace(data.CountryCode))
                {
                    return fallback;
                }

                return new LocationProfile
                {
                    City = data.City,
                    Region = data.Region,
                    CountryCode = data.CountryCode.ToUpperInvariant(),
                    CountryName = data.CountryName,
                    Label = BuildLocationLabel(data.City, data.Region, data.CountryCode.ToUpperInvariant()),
                    Source = "Approximate IP location"
                };
            }
            catch
            {
                return fallback;
            }
        }

        public static List<RadioStation> GetFallbackStations()
        {
            return new List<RadioStation>
            {
                new RadioStation { Id = "dlf", Name = "Deutschlandfunk", Subtitle = "News", Location = "Germany", Genre = "News", Mood = "News", Categories = new List<string> { "Featured", "News", "Nearby" }, Featured = true, Tagline = "Calm, information-dense public radio.", Description = "National public radio with news, features, and in-depth reporting.", StreamUrl = "https://st01.sslstream.dlf.de/dlf/01/128/mp3/stream.mp3", Quality = "MP3 | 128 kbps", SourceLabel = "Deutschlandradio", SourceUrl = "https://www.deutschlandfunk.de/" },
                new RadioStation { Id = "swr3", Name = "SWR3", Subtitle = "Pop", Location = "Baden-Wurttemberg, DE", Genre = "Pop", Mood = "Pop", Categories = new List<string> { "Featured", "Pop", "Nearby" }, Featured = true, Tagline = "Modern mainstream radio with national reach.", Description = "Popular hits, presenters, and a clean full-service radio mix.", StreamUrl = "https://liveradio.swr.de/sw282p3/swr3/", Quality = "MP3 | 128 kbps", SourceLabel = "SWR", SourceUrl = "https://www.swr3.de/" },
                new RadioStation { Id = "1live", Name = "1LIVE", Subtitle = "Pop", Location = "North Rhine-Westphalia, DE", Genre = "Pop", Mood = "Pop", Categories = new List<string> { "Featured", "Pop", "Nearby" }, Featured = true, Tagline = "Big youth radio with broad national appeal.", Description = "Contemporary music, presenters, and youth-oriented German radio programming.", StreamUrl = "http://wdr-1live-live.icecast.wdr.de/wdr/1live/live/mp3/128/stream.mp3", Quality = "MP3 | 128 kbps", SourceLabel = "WDR", SourceUrl = "https://www1.wdr.de/radio/1live/" },
                new RadioStation { Id = "kexp", Name = "KEXP 90.3", Subtitle = "Indie", Location = "Seattle, US", Genre = "Indie", Mood = "Rock", Categories = new List<string> { "Featured", "Rock" }, Featured = true, Tagline = "Human-curated discovery radio.", Description = "Strong fallback for discovery and indie listening.", StreamUrl = "https://kexp.streamguys1.com/kexp160.aac", Quality = "AAC | 160 kbps", SourceLabel = "KEXP", SourceUrl = "https://www.kexp.org/mobile/kexp-livestreams/" }
            };
        }

        public static List<RadioStation> LoadStations(LocationProfile location)
        {
            try
            {
                List<RadioBrowserStation> raw = LoadRadioBrowserStations(location);
                if (raw.Count == 0)
                {
                    return GetFallbackStations();
                }

                List<RadioStation> results = new List<RadioStation>();
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
            List<string> categories = new List<string> { "All" };
            string[] preferred = { "Nearby", "Featured", "News", "Talk", "Pop", "Rock", "Jazz", "Electronic", "Classical", "Sports", "Culture", "Local" };

            foreach (string category in preferred)
            {
                if (stations.Any(s => s.Categories != null && s.Categories.Contains(category)))
                {
                    categories.Add(category);
                }

                if (categories.Count >= 9)
                {
                    break;
                }
            }

            return categories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<RadioBrowserStation> LoadRadioBrowserStations(LocationProfile location)
        {
            List<RadioBrowserStation> regional = new List<RadioBrowserStation>();
            List<RadioBrowserStation> country = QueryStations(location.CountryCode, null, 180);

            if (!string.IsNullOrWhiteSpace(location.Region))
            {
                regional = QueryStations(location.CountryCode, location.Region, 180);
            }

            int target = string.Equals(location.CountryCode, "DE", StringComparison.OrdinalIgnoreCase) ? 48 : 28;
            List<RadioBrowserStation> combined = new List<RadioBrowserStation>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RadioBrowserStation station in regional)
            {
                if (seen.Add(station.StationUuid))
                {
                    combined.Add(station);
                }
            }

            foreach (RadioBrowserStation station in country)
            {
                if (seen.Add(station.StationUuid))
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

        private static List<RadioBrowserStation> QueryStations(string countryCode, string region, int limit)
        {
            StringBuilder uri = new StringBuilder();
            uri.Append("https://all.api.radio-browser.info/json/stations/search?hidebroken=true&order=clickcount&reverse=true");
            uri.Append("&countrycode=").Append(Uri.EscapeDataString(countryCode));
            uri.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(region))
            {
                uri.Append("&state=").Append(Uri.EscapeDataString(region));
            }

            string json = Client.GetStringAsync(uri.ToString()).Result;
            List<RadioBrowserStation> stations = Deserialize<List<RadioBrowserStation>>(json) ?? new List<RadioBrowserStation>();

            return stations.Where(IsPlayable).OrderByDescending(s => s.ClickCount).ThenByDescending(s => s.Votes).ToList();
        }

        private static bool IsPlayable(RadioBrowserStation station)
        {
            if (station == null || string.IsNullOrWhiteSpace(station.Name) || string.IsNullOrWhiteSpace(station.UrlResolved))
            {
                return false;
            }

            string codec = (station.Codec ?? string.Empty).ToUpperInvariant();
            if (station.LastCheckOk != 1 || station.Hls == 1)
            {
                return false;
            }

            return codec == "MP3" || codec == "AAC" || codec == "AAC+" || codec == "AACP";
        }

        private static RadioStation MapStation(RadioBrowserStation station, LocationProfile location, int rank)
        {
            string genre = GetPrimaryTag(station.Tags);
            List<string> categories = GetCategories((station.Name ?? string.Empty) + " " + (station.Tags ?? string.Empty), rank < 8);
            string locationLabel = !string.IsNullOrWhiteSpace(station.State) ? station.State + ", " + station.CountryCode : station.Country;

            return new RadioStation
            {
                Id = "rb-" + station.StationUuid,
                Name = station.Name,
                Subtitle = genre,
                Location = locationLabel,
                Genre = genre,
                Mood = categories.FirstOrDefault(c => c != "Featured" && c != "Nearby" && c != "Local") ?? "Nearby",
                Categories = categories,
                Featured = rank < 8,
                Tagline = string.Equals(location.CountryCode, "DE", StringComparison.OrdinalIgnoreCase) ? "Deutschlandweit relevant, regional priorisiert." : "Nearby station for " + location.Label + ".",
                Description = "Codec " + station.Codec + " at " + station.Bitrate.ToString(CultureInfo.InvariantCulture) + " kbps. Located in " + locationLabel + ".",
                StreamUrl = station.UrlResolved,
                Quality = station.Codec + " | " + station.Bitrate.ToString(CultureInfo.InvariantCulture) + " kbps",
                SourceLabel = "Radio Browser",
                SourceUrl = string.IsNullOrWhiteSpace(station.Homepage) ? "https://www.radio-browser.info/" : station.Homepage
            };
        }

        private static List<string> GetCategories(string text, bool featured)
        {
            List<string> categories = new List<string> { "Nearby" };
            string lowered = (text ?? string.Empty).ToLowerInvariant();

            if (featured) categories.Add("Featured");
            if (lowered.Contains("news")) categories.Add("News");
            if (lowered.Contains("talk") || lowered.Contains("podcast") || lowered.Contains("politic")) categories.Add("Talk");
            if (lowered.Contains("pop") || lowered.Contains("hits") || lowered.Contains("80s") || lowered.Contains("90s")) categories.Add("Pop");
            if (lowered.Contains("rock") || lowered.Contains("alternative") || lowered.Contains("indie") || lowered.Contains("metal")) categories.Add("Rock");
            if (lowered.Contains("jazz") || lowered.Contains("blues") || lowered.Contains("swing")) categories.Add("Jazz");
            if (lowered.Contains("electro") || lowered.Contains("electronic") || lowered.Contains("dance") || lowered.Contains("house") || lowered.Contains("trance") || lowered.Contains("techno")) categories.Add("Electronic");
            if (lowered.Contains("classical") || lowered.Contains("orchestra")) categories.Add("Classical");
            if (lowered.Contains("sport") || lowered.Contains("football")) categories.Add("Sports");
            if (lowered.Contains("culture") || lowered.Contains("community") || lowered.Contains("folk")) categories.Add("Culture");
            if (categories.Count == 1) categories.Add("Local");

            return categories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string GetPrimaryTag(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
            {
                return "Local Radio";
            }

            foreach (string part in tags.Split(','))
            {
                string value = part.Trim();
                if (value.Length >= 3 && !value.Contains("stream"))
                {
                    return value;
                }
            }

            return "Local Radio";
        }

        private static string BuildLocationLabel(string city, string region, string countryCode)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(city)) parts.Add(city);
            if (!string.IsNullOrWhiteSpace(region)) parts.Add(region);
            if (!string.IsNullOrWhiteSpace(countryCode)) parts.Add(countryCode);
            return parts.Count == 0 ? "Nearby" : string.Join(", ", parts);
        }

        private static T Deserialize<T>(string json)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RadioBloom.Wpf/1.0");
            return client;
        }
    }

    public sealed class NativeAudioPlayer : IDisposable
    {
        private MediaPlayer _player;
        private double _volume;

        public NativeAudioPlayer()
        {
            _volume = 0.72;
            _player = CreatePlayer();
        }

        public void SetVolume(double volume)
        {
            _volume = volume;
            _player.Volume = volume;
        }

        public void Open(string url)
        {
            ResetPlayer();
            _player.Source = MediaSource.CreateFromUri(new Uri(url));
        }

        public void Play() { _player.Play(); }
        public void Pause() { _player.Pause(); }
        public void Stop() { ResetPlayer(); }

        public NativePlayerState GetState()
        {
            if (_player.Source == null) return NativePlayerState.Ready;
            MediaPlaybackState state = _player.PlaybackSession.PlaybackState;
            if (state == MediaPlaybackState.Playing) return NativePlayerState.Live;
            if (state == MediaPlaybackState.Buffering || state == MediaPlaybackState.Opening) return NativePlayerState.Connecting;
            if (state == MediaPlaybackState.Paused) return NativePlayerState.Paused;
            return NativePlayerState.Stopped;
        }

        public void Dispose()
        {
            SafeDisposePlayer();
        }

        private MediaPlayer CreatePlayer()
        {
            MediaPlayer player = new MediaPlayer();
            player.Volume = _volume;
            player.AutoPlay = false;
            return player;
        }

        private void ResetPlayer()
        {
            SafeDisposePlayer();
            _player = CreatePlayer();
        }

        private void SafeDisposePlayer()
        {
            if (_player == null)
            {
                return;
            }

            try
            {
                _player.Pause();
            }
            catch
            {
            }

            try
            {
                _player.Source = null;
            }
            catch
            {
            }

            _player.Dispose();
            _player = null;
        }
    }
}
