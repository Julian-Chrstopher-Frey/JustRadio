using System.Text.Json;
using Microsoft.Maui.Storage;

namespace RadioBloom.Maui;

internal sealed class FavouriteStationStore
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	private static string StorePath => Path.Combine(FileSystem.AppDataDirectory, "favourites.json");

	public List<RadioStation> Load()
	{
		try
		{
			string path = StorePath;
			if (!File.Exists(path))
			{
				return [];
			}

			string json = File.ReadAllText(path);
			List<FavouriteStationSnapshot>? snapshots = JsonSerializer.Deserialize<List<FavouriteStationSnapshot>>(json, JsonOptions);
			return snapshots?
				.Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Id))
				.Select(snapshot => snapshot.ToStation())
				.GroupBy(station => station.Id, StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())
				.OrderBy(station => station.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList() ?? [];
		}
		catch
		{
			return [];
		}
	}

	public async Task SaveAsync(IEnumerable<RadioStation> stations)
	{
		try
		{
			string path = StorePath;
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			List<FavouriteStationSnapshot> snapshots = stations
				.Where(station => !string.IsNullOrWhiteSpace(station.Id))
				.GroupBy(station => station.Id, StringComparer.OrdinalIgnoreCase)
				.Select(group => FavouriteStationSnapshot.FromStation(group.First()))
				.OrderBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();

			string json = JsonSerializer.Serialize(snapshots, JsonOptions);
			await File.WriteAllTextAsync(path, json);
		}
		catch
		{
			// Favourites are a convenience feature, so storage errors should not break playback.
		}
	}
}

internal sealed class FavouriteStationSnapshot
{
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

	public static FavouriteStationSnapshot FromStation(RadioStation station)
	{
		return new FavouriteStationSnapshot
		{
			Id = station.Id,
			Name = station.Name,
			Location = station.Location,
			Genre = station.Genre,
			Categories = station.Categories.ToList(),
			Featured = station.Featured,
			Tagline = station.Tagline,
			Description = station.Description,
			StreamUrl = station.StreamUrl,
			PlaybackUrls = station.PlaybackUrls.ToList(),
			MetadataSummary = station.MetadataSummary,
			PopularityScore = station.PopularityScore
		};
	}

	public RadioStation ToStation()
	{
		return new RadioStation
		{
			Id = Id,
			Name = Name,
			Location = Location,
			Genre = Genre,
			Categories = Categories.ToList(),
			Featured = Featured,
			Tagline = Tagline,
			Description = Description,
			StreamUrl = StreamUrl,
			PlaybackUrls = PlaybackUrls.ToList(),
			MetadataSummary = MetadataSummary,
			PopularityScore = PopularityScore,
			IsFavorite = true
		};
	}
}
