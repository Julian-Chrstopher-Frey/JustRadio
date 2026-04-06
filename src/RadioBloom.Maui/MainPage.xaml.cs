using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Maui.Layouts;

namespace RadioBloom.Maui;

public partial class MainPage : ContentPage
{
	private static readonly HttpClient MapTileClient = CreateMapTileClient();
	private static readonly ConcurrentDictionary<string, byte[]> MapTileCache = new(StringComparer.Ordinal);
	private readonly StationCatalogService _catalog = new();
	private readonly List<RadioStation> _stations = [];
	private readonly List<CountryOption> _countries;
	private readonly Dictionary<string, Button> _categoryButtons = new(StringComparer.OrdinalIgnoreCase);
	private readonly EqualizerDrawable _equalizerDrawable = new();
	private readonly RegionPreviewFallbackDrawable _mapFallbackDrawable = new();
	private readonly StreamMetadataReader _metadataReader = new();
	private readonly IDispatcherTimer _equalizerTimer;
	private readonly SemaphoreSlim _playbackGate = new(1, 1);
	private List<string> _regions = [];
	private CountryOption? _selectedCountry;
	private string _selectedRegion = StationCatalogService.AllRegionsLabel;
	private string _selectedCategory = "All";
	private LocationProfile? _currentLocation;
	private RadioStation? _selectedStation;
	private bool _isPlaying;
	private bool _suppressSelectionPlayback;
	private int _mapRevision;
	private int _weatherRevision;
	private int _playbackRevision;

	public MainPage()
	{
		InitializeComponent();
		_countries = StationCatalogService.GetCountryOptions();
		EqualizerView.Drawable = _equalizerDrawable;
		MapFallbackView.Drawable = _mapFallbackDrawable;
		AudioWebView.Source = new HtmlWebViewSource { Html = BuildAudioHostHtml() };
		_metadataReader.TrackInfoChanged += OnTrackInfoChanged;
		MapLayout.SizeChanged += (_, _) =>
		{
			if (_currentLocation != null)
			{
				UpdateMap(_currentLocation);
			}
		};
		_equalizerTimer = Dispatcher.CreateTimer();
		_equalizerTimer.Interval = TimeSpan.FromMilliseconds(120);
		_equalizerTimer.Tick += (_, _) =>
		{
			_equalizerDrawable.Tick();
			EqualizerView.Invalidate();
		};
		_equalizerTimer.Start();
		InitializeSelectors();
		Loaded += async (_, _) => await LoadAutomaticLocationAsync();
	}

	private void InitializeSelectors()
	{
		_selectedCountry = _countries.FirstOrDefault(c => c.CountryCode == "DE") ?? _countries.FirstOrDefault();
		PopulateRegions(_selectedCountry?.CountryCode);
		UpdateSelectorLabels();
	}

	private async Task LoadAutomaticLocationAsync()
	{
		SetLoading("Detecting location...");
		LocationProfile location = await _catalog.GetApproximateLocationAsync();
		await LoadLocationAsync(location);
		SyncPickers(location);
	}

	private async Task LoadLocationAsync(LocationProfile location)
	{
		SetLoading("Loading stations for " + location.DisplayName + "...");
		List<RadioStation> stations = await _catalog.LoadStationsAsync(location);
		_stations.Clear();
		_stations.AddRange(stations);
		BuildCategoryButtons();
		_selectedCategory = "All";
		RefreshStations();
		SummaryLabel.Text = $"{_stations.Count} stations for {location.DisplayName}.";
		LocationLabel.Text = location.Source;
		_currentLocation = location;
		UpdateMap(location);
		_ = RefreshWeatherAsync(location);
		StatusLabel.Text = "Ready";
	}

	private void SyncPickers(LocationProfile location)
	{
		CountryOption? country = _countries.FirstOrDefault(c => string.Equals(c.CountryCode, location.CountryCode, StringComparison.OrdinalIgnoreCase));
		if (country != null)
		{
			_selectedCountry = country;
			PopulateRegions(country.CountryCode);
			_selectedRegion = string.IsNullOrWhiteSpace(location.Region)
				? StationCatalogService.AllRegionsLabel
				: (_regions.FirstOrDefault(region => string.Equals(region, location.Region, StringComparison.OrdinalIgnoreCase)) ?? StationCatalogService.AllRegionsLabel);
		}
		UpdateSelectorLabels();
	}

	private void PopulateRegions(string? countryCode)
	{
		_regions = StationCatalogService.GetRegions(countryCode);
		_selectedRegion = StationCatalogService.AllRegionsLabel;
		UpdateSelectorLabels();
	}

	private void UpdateSelectorLabels()
	{
		CountryValueLabel.Text = _selectedCountry?.DisplayName ?? "Country";
		RegionValueLabel.Text = _selectedRegion;
	}

	private void BuildCategoryButtons()
	{
		CategoryPanel.Children.Clear();
		_categoryButtons.Clear();
		foreach (string category in StationCatalogService.BuildCategoryList(_stations))
		{
			Button button = new()
			{
				Text = category,
				HeightRequest = 38,
				MinimumWidthRequest = 78,
				CornerRadius = 16,
				Padding = new Thickness(16, 0),
				FontAttributes = FontAttributes.Bold,
				FontSize = 13
			};
			button.Clicked += (_, _) =>
			{
				_selectedCategory = category;
				UpdateCategoryStyles();
				RefreshStations();
			};
			_categoryButtons[category] = button;
			CategoryPanel.Children.Add(button);
		}
		UpdateCategoryStyles();
	}

	private void UpdateCategoryStyles()
	{
		foreach ((string category, Button button) in _categoryButtons)
		{
			bool active = string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase);
			button.BackgroundColor = Color.FromArgb(active ? "#111827" : "#F4F6F8");
			button.TextColor = Color.FromArgb(active ? "#FFFFFF" : "#1F2937");
			button.BorderColor = Color.FromArgb(active ? "#111827" : "#E5E7EB");
			button.BorderWidth = 1;
		}
	}

	private void RefreshStations()
	{
		IEnumerable<RadioStation> query = _stations;
		string search = SearchBox.Text?.Trim() ?? string.Empty;
		if (!string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
		{
			query = query.Where(station => station.MatchesCategory(_selectedCategory));
		}
		if (!string.IsNullOrWhiteSpace(search))
		{
			query = query.Where(station => station.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase));
		}

		List<RadioStation> visible = query
			.OrderByDescending(station => station.Featured)
			.ThenByDescending(station => station.PopularityScore)
			.ThenBy(station => station.Name)
			.ToList();

		StationCollection.ItemsSource = visible;
		if (_selectedStation == null || visible.All(station => station.Id != _selectedStation.Id))
		{
			_suppressSelectionPlayback = true;
			try
			{
				_selectedStation = visible.FirstOrDefault();
				StationCollection.SelectedItem = _selectedStation;
				UpdateDetails();
			}
			finally
			{
				_suppressSelectionPlayback = false;
			}
		}
	}

	private void UpdateDetails()
	{
		if (_selectedStation == null)
		{
			NowTitleLabel.Text = "Select a station";
			NowSubtitleLabel.Text = "Choose a station from the list.";
			TrackLabel.Text = "Artist and title will appear here when playback starts.";
			DescriptionLabel.Text = "This first MAUI version focuses on cross-platform layout, station loading, and basic playback.";
			SourceLabel.Text = "Source metadata will appear here.";
			PlayButton.IsEnabled = false;
			StopButton.IsEnabled = false;
			return;
		}

		NowTitleLabel.Text = _selectedStation.Name;
		NowSubtitleLabel.Text = $"{_selectedStation.Genre} | {_selectedStation.Location}";
		if (!_isPlaying)
		{
			TrackLabel.Text = "Artist and title will appear here when playback starts.";
		}
		DescriptionLabel.Text = _selectedStation.Description;
		SourceLabel.Text = _selectedStation.MetadataSummary;
		PlayButton.IsEnabled = true;
		PlayButton.Text = _isPlaying ? "Restart" : "Play";
		StopButton.IsEnabled = _isPlaying;
	}

	private async void OnStationPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.ZIndex = 10;
			border.BackgroundColor = Color.FromArgb("#FFFFFF");
			border.Stroke = Color.FromArgb("#D6E4F2");
			if (border.Shadow is Shadow shadow)
			{
				shadow.Opacity = 0.28f;
				shadow.Radius = 14;
				shadow.Offset = new Point(0, 8);
			}

			await Task.WhenAll(
				border.ScaleToAsync(1.018, 120, Easing.CubicOut),
				border.TranslateToAsync(0, -4, 120, Easing.CubicOut));
		}
	}

	private async void OnStationPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.ZIndex = 0;
			border.BackgroundColor = Color.FromArgb("#FCFDFE");
			border.Stroke = Color.FromArgb("#E6ECF3");
			if (border.Shadow is Shadow shadow)
			{
				shadow.Opacity = 0.16f;
				shadow.Radius = 8;
				shadow.Offset = new Point(0, 2);
			}

			await Task.WhenAll(
				border.ScaleToAsync(1.0, 120, Easing.CubicOut),
				border.TranslateToAsync(0, 0, 120, Easing.CubicOut));
		}
	}

	private async void OnApplyLocationClicked(object? sender, EventArgs e)
	{
		HideSelectorDropdown();
		if (_selectedCountry is not CountryOption country)
		{
			return;
		}

		string? region = _selectedRegion;
		if (string.Equals(region, StationCatalogService.AllRegionsLabel, StringComparison.OrdinalIgnoreCase))
		{
			region = null;
		}

		await StopPlaybackAsync();
		await LoadLocationAsync(new LocationProfile(country.CountryCode, country.DisplayName, region, "Manual override"));
	}

	private async void OnAutoLocationClicked(object? sender, EventArgs e)
	{
		HideSelectorDropdown();
		await StopPlaybackAsync();
		await LoadAutomaticLocationAsync();
	}

	private void OnCountrySelectorTapped(object? sender, TappedEventArgs e)
	{
		ShowSelectorDropdown(_countries.Select(country => country.DisplayName), selected =>
		{
			CountryOption? country = _countries.FirstOrDefault(option => string.Equals(option.DisplayName, selected, StringComparison.Ordinal));
			if (country == null)
			{
				return;
			}

			_selectedCountry = country;
			PopulateRegions(country.CountryCode);
		});
	}

	private void OnRegionSelectorTapped(object? sender, TappedEventArgs e)
	{
		if (_regions.Count == 0)
		{
			return;
		}

		ShowSelectorDropdown(_regions, selected =>
		{
			_selectedRegion = selected;
			UpdateSelectorLabels();
		});
	}

	private void ShowSelectorDropdown(IEnumerable<string> options, Action<string> onSelected)
	{
		SelectorOptionsPanel.Children.Clear();
		foreach (string option in options)
		{
			Button button = new()
			{
				Text = option,
				HeightRequest = 34,
				CornerRadius = 12,
				HorizontalOptions = LayoutOptions.Fill,
				BackgroundColor = Color.FromArgb("#FFFFFF"),
				BorderColor = Color.FromArgb("#EDF2F7"),
				BorderWidth = 1,
				TextColor = Color.FromArgb("#1F2937"),
				FontSize = 13,
				Padding = new Thickness(12, 0)
			};
			button.Clicked += (_, _) =>
			{
				onSelected(option);
				HideSelectorDropdown();
			};
			SelectorOptionsPanel.Children.Add(button);
		}

		SelectorDropdown.IsVisible = true;
	}

	private void HideSelectorDropdown()
	{
		SelectorDropdown.IsVisible = false;
		SelectorOptionsPanel.Children.Clear();
	}

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		RefreshStations();
	}

	private async void OnStationSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not RadioStation station)
		{
			return;
		}

		bool shouldSwitch = !_suppressSelectionPlayback && _selectedStation?.Id != station.Id;
		_selectedStation = station;
		UpdateDetails();
		if (shouldSwitch)
		{
			await PlaySelectedAsync();
		}
	}

	private async void OnPlayClicked(object? sender, EventArgs e)
	{
		await PlaySelectedAsync();
	}

	private async void OnStopClicked(object? sender, EventArgs e)
	{
		await StopPlaybackAsync();
	}

	private async Task PlaySelectedAsync()
	{
		RadioStation? station = _selectedStation;
		if (station == null)
		{
			return;
		}

		int revision = Interlocked.Increment(ref _playbackRevision);
		await _playbackGate.WaitAsync();
		try
		{
			if (revision != _playbackRevision)
			{
				return;
			}

			StatusLabel.Text = "Connecting";
			_equalizerDrawable.SetMode(EqualizerMode.Connecting);
			TrackLabel.Text = "Looking for artist and title...";
			PlayButton.IsEnabled = false;
			PlayButton.Text = "Connecting";
			StopButton.IsEnabled = false;
			_metadataReader.Stop();
			_isPlaying = false;

			await AudioWebView.EvaluateJavaScriptAsync("stopStation()");
			await Task.Delay(140);

			if (revision != _playbackRevision)
			{
				return;
			}

			string urlLiteral = JsonSerializer.Serialize(station.StreamUrl);
			string? result = null;
			try
			{
				result = await AudioWebView.EvaluateJavaScriptAsync($"playStation({urlLiteral})");
			}
			catch
			{
				// MAUI WebView can report a script/promise failure even after the audio
				// element has accepted the stream. Keep the UI tied to the user command.
			}

			if (revision != _playbackRevision)
			{
				return;
			}

			if (IsSupersededPlaybackResult(result))
			{
				return;
			}

			_isPlaying = true;
			_metadataReader.Start(station.StreamUrl);
			_equalizerDrawable.SetMode(EqualizerMode.Live);
			StatusLabel.Text = "Live";
			TrackLabel.Text = "Listening for artist and title...";
			PlayButton.Text = "Restart";
			StopButton.IsEnabled = true;
		}
		catch
		{
			if (revision == _playbackRevision)
			{
				_isPlaying = false;
				_metadataReader.Stop();
				_equalizerDrawable.SetMode(EqualizerMode.Stopped);
				StatusLabel.Text = "Ready";
				TrackLabel.Text = "Artist and title will appear here when playback starts.";
				PlayButton.Text = "Play";
				StopButton.IsEnabled = false;
			}
		}
		finally
		{
			if (revision == _playbackRevision)
			{
				PlayButton.IsEnabled = _selectedStation != null;
			}

			_playbackGate.Release();
		}
	}

	private async Task StopPlaybackAsync()
	{
		int revision = Interlocked.Increment(ref _playbackRevision);
		_isPlaying = false;
		_metadataReader.Stop();
		_equalizerDrawable.SetMode(EqualizerMode.Stopped);
		StatusLabel.Text = "Stopped";
		TrackLabel.Text = "Artist and title will appear here when playback starts.";
		PlayButton.Text = "Play";
		PlayButton.IsEnabled = _selectedStation != null;
		StopButton.IsEnabled = false;

		try
		{
			await AudioWebView.EvaluateJavaScriptAsync($"stopStation({revision})");
		}
		catch
		{
		}
	}

	private static bool IsSupersededPlaybackResult(string? result)
	{
		return !string.IsNullOrWhiteSpace(result)
			&& result.Contains("superseded", StringComparison.OrdinalIgnoreCase);
	}

	private void OnTrackInfoChanged(string? trackInfo)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (!_isPlaying)
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(trackInfo))
			{
				TrackLabel.Text = trackInfo;
			}
		});
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();
		await StopPlaybackAsync();
		_metadataReader.Dispose();
	}

	private void SetLoading(string text)
	{
		SummaryLabel.Text = text;
		StatusLabel.Text = "Loading";
	}

	private async Task RefreshWeatherAsync(LocationProfile location)
	{
		int revision = ++_weatherRevision;
		WeatherBadgeLabel.Text = "Syncing";
		WeatherTempLabel.Text = "--";
		WeatherConditionLabel.Text = "Loading local weather...";
		(double Latitude, double Longitude) coordinate = LocationCoordinateLookup.Resolve(location);
		WeatherSnapshot? weather = await WeatherService.GetCurrentAsync(coordinate.Latitude, coordinate.Longitude);
		if (revision != _weatherRevision)
		{
			return;
		}

		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (weather == null)
			{
				WeatherBadgeLabel.Text = "Offline";
				WeatherTempLabel.Text = "--";
				WeatherConditionLabel.Text = "Weather unavailable";
				return;
			}

			WeatherBadgeLabel.Text = weather.IsDay ? "Daylight" : "Night";
			WeatherTempLabel.Text = Math.Round(weather.TemperatureC).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "\u00B0";
			WeatherConditionLabel.Text = weather.ConditionLabel;
		});
	}

	private void UpdateMap(LocationProfile location)
	{
		(double Latitude, double Longitude) coordinate = LocationCoordinateLookup.Resolve(location);
		const double targetX = 0.52;
		const double targetY = 0.56;

		_mapFallbackDrawable.SetLocation(location.DisplayName);
		MapFallbackView.Invalidate();
		RenderMapTiles(location, coordinate.Latitude, coordinate.Longitude, targetX, targetY);
		MapLabelText.Text = location.DisplayName;
		AbsoluteLayout.SetLayoutBounds(MapPin, new Rect(targetX, targetY, 14, 14));
		AbsoluteLayout.SetLayoutBounds(MapLabel, new Rect(Math.Clamp(targetX - 0.02, 0.18, 0.82), Math.Clamp(targetY - 0.18, 0.18, 0.82), 190, 30));
	}

	private void RenderMapTiles(LocationProfile location, double latitude, double longitude, double targetX, double targetY)
	{
		int revision = ++_mapRevision;
		List<MapTileRequest> requests = BuildTileRequests(location, latitude, longitude, targetX, targetY);
		_ = RenderMapTilesAsync(requests, revision);
	}

	private async Task RenderMapTilesAsync(List<MapTileRequest> requests, int revision)
	{
		try
		{
			MapTileResult[] tiles = await Task.WhenAll(requests.Select(LoadMapTileAsync));
			if (revision != _mapRevision)
			{
				return;
			}

			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (revision != _mapRevision)
				{
					return;
				}

				MapTileLayer.Children.Clear();
				foreach (MapTileResult tile in tiles.Where(tile => tile.Bytes.Length > 0))
				{
					double mapWidth = Math.Max(1.0, MapLayout.Width);
					double mapHeight = Math.Max(1.0, MapLayout.Height);
					byte[] bytes = tile.Bytes;
					Image image = new()
					{
						Aspect = Aspect.Fill,
						Opacity = 0.94,
						Source = ImageSource.FromStream(() => new MemoryStream(bytes))
					};
					AbsoluteLayout.SetLayoutBounds(image, new Rect(
						tile.Left * mapWidth,
						tile.Top * mapHeight,
						(tile.Width * mapWidth) + 1.5,
						(tile.Height * mapHeight) + 1.5));
					AbsoluteLayout.SetLayoutFlags(image, AbsoluteLayoutFlags.None);
					MapTileLayer.Children.Add(image);
				}
			});
		}
		catch
		{
			// Keep the native fallback preview visible if tile loading fails.
		}
	}

	private static List<MapTileRequest> BuildTileRequests(LocationProfile location, double latitude, double longitude, double targetX, double targetY)
	{
		const double virtualWidth = 1200.0;
		const double virtualHeight = 300.0;
		const double tileSize = 256.0;
		const int tileColumns = 6;
		const int tileRows = 3;

		int zoom = GetMapTileZoom(location);
		double scale = Math.Pow(2.0, zoom);
		double clampedLatitude = Math.Clamp(latitude, -85.05112878, 85.05112878);
		double normalizedLongitude = NormalizeLongitude(longitude);
		double tileX = ((normalizedLongitude + 180.0) / 360.0) * scale;
		double latitudeRad = clampedLatitude * Math.PI / 180.0;
		double tileY = (1.0 - Math.Log(Math.Tan(latitudeRad) + 1.0 / Math.Cos(latitudeRad)) / Math.PI) * 0.5 * scale;
		int startTileX = (int)Math.Floor(tileX - (tileColumns / 2.0));
		int startTileY = Math.Clamp((int)Math.Floor(tileY - (tileRows / 2.0)), 0, Math.Max(0, (int)scale - tileRows));
		double targetPixelX = virtualWidth * targetX;
		double targetPixelY = virtualHeight * targetY;
		double localX = (tileX - startTileX) * tileSize;
		double localY = (tileY - startTileY) * tileSize;
		double leftOffset = targetPixelX - localX;
		double topOffset = targetPixelY - localY;

		List<MapTileRequest> requests = [];
		for (int row = 0; row < tileRows; row++)
		{
			int tileRow = Math.Clamp(startTileY + row, 0, (int)scale - 1);
			for (int column = 0; column < tileColumns; column++)
			{
				int tileColumn = Mod(startTileX + column, (int)scale);
				string subdomain = ((tileColumn + tileRow) % 3) switch
				{
					0 => "a",
					1 => "b",
					_ => "c"
				};
				double left = (leftOffset + (column * tileSize)) / virtualWidth;
				double top = (topOffset + (row * tileSize)) / virtualHeight;
				string url = $"https://{subdomain}.basemaps.cartocdn.com/rastertiles/voyager_nolabels/{zoom}/{tileColumn}/{tileRow}@2x.png";
				requests.Add(new MapTileRequest(url, left, top, tileSize / virtualWidth, tileSize / virtualHeight));
			}
		}

		return requests;
	}

	private static async Task<MapTileResult> LoadMapTileAsync(MapTileRequest request)
	{
		try
		{
			if (!MapTileCache.TryGetValue(request.Url, out byte[]? bytes))
			{
				bytes = await MapTileClient.GetByteArrayAsync(request.Url);
				MapTileCache[request.Url] = bytes;
			}

			return new MapTileResult(bytes, request.Left, request.Top, request.Width, request.Height);
		}
		catch
		{
			return new MapTileResult([], request.Left, request.Top, request.Width, request.Height);
		}
	}

	private static int GetMapTileZoom(LocationProfile location)
	{
		bool hasRegion = !string.IsNullOrWhiteSpace(location.Region);
		string countryCode = (location.CountryCode ?? string.Empty).ToUpperInvariant();

		return countryCode switch
		{
			"DE" or "FR" or "IT" or "ES" or "GB" or "GR" or "NL" or "BE" or "CH" or "AT" or "PT" or "PL" or "CZ" or "HU" => hasRegion ? 6 : 5,
			"JP" or "NZ" or "NO" or "SE" or "FI" or "ZA" or "IE" or "DK" => hasRegion ? 6 : 5,
			"US" or "CA" or "BR" or "RU" or "CN" or "IN" or "AR" or "MX" or "AU" => hasRegion ? 5 : 4,
			_ => hasRegion ? 5 : 4
		};
	}

	private static double NormalizeLongitude(double value)
	{
		while (value < -180.0)
		{
			value += 360.0;
		}

		while (value > 180.0)
		{
			value -= 360.0;
		}

		return value;
	}

	private static int Mod(int value, int modulo)
	{
		int result = value % modulo;
		return result < 0 ? result + modulo : result;
	}

	private static HttpClient CreateMapTileClient()
	{
		HttpClientHandler handler = new()
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
		};

		HttpClient client = new(handler)
		{
			Timeout = TimeSpan.FromSeconds(8)
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("JustRadio.Maui/0.1 (https://github.com/Julian-Chrstopher-Frey/JustRadio)");
		return client;
	}

	private sealed record MapTileRequest(string Url, double Left, double Top, double Width, double Height);

	private sealed record MapTileResult(byte[] Bytes, double Left, double Top, double Width, double Height);

	private static string BuildAudioHostHtml()
	{
		return """
			<!doctype html>
			<html>
			<head>
				<meta charset="utf-8">
				<meta name="viewport" content="width=device-width, initial-scale=1">
			</head>
			<body>
				<audio id="radio" crossorigin="anonymous"></audio>
				<script>
					const radio = document.getElementById('radio');
					let playToken = 0;
					function delay(ms) {
						return new Promise(resolve => setTimeout(resolve, ms));
					}
					window.playStation = function(url) {
						const token = ++playToken;
						radio.pause();
						radio.removeAttribute('src');
						radio.load();
						radio.src = url;
						radio.preload = 'auto';
						radio.load();
						const playPromise = radio.play();
						if (playPromise && typeof playPromise.catch === 'function') {
							playPromise.catch(error => {
								if (token === playToken) {
									console.log('Radio playback reported asynchronously:', error);
								}
							});
						}
						return 'started';
					};
					window.stopStation = function() {
						playToken++;
						radio.pause();
						radio.src = '';
						radio.removeAttribute('src');
						radio.load();
						return 'stopped';
					};
				</script>
			</body>
			</html>
			""";
	}
}
