using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace RadioBloom.WinUI;

[DataContract]
public sealed class RadioBloomProfile
{
    [DataMember(Name = "favoriteStationIds")] public List<string> FavoriteStationIds { get; set; } = [];
    [DataMember(Name = "savedRegions")] public List<SavedRegionEntry> SavedRegions { get; set; } = [];
    [DataMember(Name = "lastCountryCode")] public string? LastCountryCode { get; set; }
    [DataMember(Name = "lastRegion")] public string? LastRegion { get; set; }
    [DataMember(Name = "updatedUtc")] public string? UpdatedUtc { get; set; }
}

[DataContract]
public sealed class SavedRegionEntry
{
    [DataMember(Name = "countryCode")] public string CountryCode { get; set; } = string.Empty;
    [DataMember(Name = "countryName")] public string CountryName { get; set; } = string.Empty;
    [DataMember(Name = "region")] public string? Region { get; set; }
    [DataMember(Name = "label")] public string Label { get; set; } = string.Empty;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label)
        ? (string.IsNullOrWhiteSpace(Region) ? CountryName : Region + ", " + CountryName)
        : Label;
}

internal static class RadioBloomProfileStore
{
    private static readonly string LocalFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RadioBloom");

    private static readonly string SyncFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "RadioBloom Sync");

    private static readonly string LocalProfilePath = Path.Combine(LocalFolder, "profile.json");
    private static readonly string SyncProfilePath = Path.Combine(SyncFolder, "profile.json");

    public static RadioBloomProfile LoadProfile()
    {
        RadioBloomProfile local = LoadFromPath(LocalProfilePath);
        RadioBloomProfile sync = LoadFromPath(SyncProfilePath);

        DateTimeOffset localStamp = GetTimestamp(local, LocalProfilePath);
        DateTimeOffset syncStamp = GetTimestamp(sync, SyncProfilePath);
        RadioBloomProfile selected = syncStamp > localStamp ? sync : local;
        return Normalize(selected);
    }

    public static async Task SaveProfileAsync(RadioBloomProfile profile)
    {
        RadioBloomProfile normalized = Normalize(profile);
        normalized.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        string json = Serialize(normalized);
        Directory.CreateDirectory(LocalFolder);
        await File.WriteAllTextAsync(LocalProfilePath, json, Encoding.UTF8);

        Directory.CreateDirectory(SyncFolder);
        await File.WriteAllTextAsync(SyncProfilePath, json, Encoding.UTF8);
    }

    public static string GetSyncPath()
    {
        return SyncProfilePath;
    }

    public static SavedRegionEntry? CreateSavedRegion(LocationProfile location)
    {
        if (location == null || string.IsNullOrWhiteSpace(location.CountryCode) || string.IsNullOrWhiteSpace(location.CountryName))
        {
            return null;
        }

        string label = string.IsNullOrWhiteSpace(location.Region)
            ? location.CountryName
            : location.Region + ", " + location.CountryName;

        return new SavedRegionEntry
        {
            CountryCode = location.CountryCode.ToUpperInvariant(),
            CountryName = location.CountryName,
            Region = string.IsNullOrWhiteSpace(location.Region) ? null : location.Region,
            Label = label
        };
    }

    private static RadioBloomProfile LoadFromPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new RadioBloomProfile();
            }

            return Normalize(Deserialize<RadioBloomProfile>(File.ReadAllText(path)) ?? new RadioBloomProfile());
        }
        catch
        {
            return new RadioBloomProfile();
        }
    }

    private static RadioBloomProfile Normalize(RadioBloomProfile profile)
    {
        profile ??= new RadioBloomProfile();
        profile.FavoriteStationIds = profile.FavoriteStationIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        profile.SavedRegions = profile.SavedRegions
            .Where(entry => !string.IsNullOrWhiteSpace(entry.CountryCode) && !string.IsNullOrWhiteSpace(entry.CountryName))
            .GroupBy(entry => BuildSavedRegionKey(entry), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return profile;
    }

    private static string BuildSavedRegionKey(SavedRegionEntry entry)
    {
        return (entry.CountryCode ?? string.Empty).Trim().ToUpperInvariant() + "|" + (entry.Region ?? string.Empty).Trim();
    }

    private static DateTimeOffset GetTimestamp(RadioBloomProfile profile, string path)
    {
        if (!string.IsNullOrWhiteSpace(profile.UpdatedUtc) &&
            DateTimeOffset.TryParse(profile.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTimeOffset.MinValue;
    }

    private static string Serialize<T>(T value)
    {
        DataContractJsonSerializer serializer = new(typeof(T));
        using MemoryStream stream = new();
        serializer.WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static T? Deserialize<T>(string json)
    {
        DataContractJsonSerializer serializer = new(typeof(T));
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using MemoryStream stream = new(bytes);
        return (T?)serializer.ReadObject(stream);
    }
}
