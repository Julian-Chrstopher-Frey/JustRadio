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
	private Action<string>? _selectorOptionSelected;
	private LocationProfile? _currentLocation;
	private RadioStation? _selectedStation;
	private bool _isPlaying;
	private bool _suppressSelectionPlayback;
	private int _mapRevision;
	private int _weatherRevision;
	private int _playbackRevision;
	private bool _startupLoadStarted;
	private bool _equalizerPollInFlight;

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
				_ = UpdateMapAsync(_currentLocation);
			}
		};
		_equalizerTimer = Dispatcher.CreateTimer();
		_equalizerTimer.Interval = TimeSpan.FromMilliseconds(120);
		_equalizerTimer.Tick += OnEqualizerTimerTick;
		_equalizerTimer.Start();
		InitializeSelectors();
		Loaded += OnMainPageLoaded;
	}

	private async void OnEqualizerTimerTick(object? sender, EventArgs e)
	{
		if (!_isPlaying || _equalizerPollInFlight)
		{
			_equalizerDrawable.Tick();
			EqualizerView.Invalidate();
			return;
		}

		_equalizerPollInFlight = true;
		try
		{
			string? result = await AudioWebView.EvaluateJavaScriptAsync("getEqualizerLevels()");
			if (TryParseEqualizerLevels(result, out float[] levels))
			{
				_equalizerDrawable.SetLiveLevels(levels);
			}
			else
			{
				_equalizerDrawable.Tick();
			}
		}
		catch
		{
			_equalizerDrawable.Tick();
		}
		finally
		{
			_equalizerPollInFlight = false;
			EqualizerView.Invalidate();
		}
	}

	private async void OnMainPageLoaded(object? sender, EventArgs e)
	{
		if (_startupLoadStarted)
		{
			return;
		}

		_startupLoadStarted = true;
		try
		{
			await LoadAutomaticLocationAsync();
		}
		catch
		{
			HandleStartupFailure();
		}
	}

	private void HandleStartupFailure()
	{
		_isPlaying = false;
		_equalizerDrawable.SetMode(EqualizerMode.Stopped);
		SummaryLabel.Text = "We couldn't load nearby stations.";
		LocationLabel.Text = "Use Auto or Apply to try again.";
		StatusLabel.Text = "Startup error";
		TrackLabel.Text = "Startup hit an error. Playback is still available after you retry loading stations.";
		NowTitleLabel.Text = "Select a station";
		NowSubtitleLabel.Text = "Startup recovery is active.";
		DescriptionLabel.Text = "The automatic location load failed, but you can still retry with Auto or pick a region manually.";
		SourceLabel.Text = "Station metadata will appear here after startup completes.";
		PlayButton.IsEnabled = false;
		StopButton.IsEnabled = false;
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
		_ = UpdateMapAsync(location);
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
		RadioStation? selectedStation = _selectedStation == null
			? visible.FirstOrDefault()
			: visible.FirstOrDefault(station => station.Id == _selectedStation.Id) ?? visible.FirstOrDefault();
		if (_selectedStation?.Id != selectedStation?.Id)
		{
			_suppressSelectionPlayback = true;
			try
			{
				ApplySelectedStation(selectedStation);
				UpdateDetails();
			}
			finally
			{
				_suppressSelectionPlayback = false;
			}
		}
		else
		{
			ApplySelectedStation(selectedStation);
			UpdateDetails();
		}
	}

	private void ApplySelectedStation(RadioStation? station)
	{
		_selectedStation = station;
		foreach (RadioStation item in _stations)
		{
			item.IsSelected = station != null && item.Id == station.Id;
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

	private async void OnStationTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is not RadioStation station)
		{
			return;
		}

		bool shouldSwitch = !_suppressSelectionPlayback && _selectedStation?.Id != station.Id;
		ApplySelectedStation(station);
		UpdateDetails();
		if (shouldSwitch)
		{
			await PlaySelectedAsync();
		}
	}

	private async void OnStationPointerEntered(object? sender, PointerEventArgs e)
	{
		Border? border = GetStationCardBorder(sender);
		if (border == null)
		{
			return;
		}

		if (border.BindingContext is RadioStation station)
		{
			station.IsHovered = true;
		}

		border.ZIndex = 10;
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

	private async void OnStationPointerExited(object? sender, PointerEventArgs e)
	{
		Border? border = GetStationCardBorder(sender);
		if (border == null)
		{
			return;
		}

		if (border.BindingContext is RadioStation station)
		{
			station.IsHovered = false;
		}

		border.ZIndex = 0;
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

	private static Border? GetStationCardBorder(object? sender)
	{
		return sender switch
		{
			Border border => border,
			Element { Parent: Border parentBorder } => parentBorder,
			_ => null
		};
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
		List<string> selectorOptions = options
			.Where(option => !string.IsNullOrWhiteSpace(option))
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (selectorOptions.Count == 0)
		{
			HideSelectorDropdown();
			return;
		}

		_selectorOptionSelected = onSelected;
		SelectorOptionsView.ItemsSource = selectorOptions;
		SelectorOptionsView.HeightRequest = Math.Min(180, selectorOptions.Count * 42);
		SelectorDropdown.IsVisible = true;
		SelectorOptionsView.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
	}

	private void HideSelectorDropdown()
	{
		SelectorDropdown.IsVisible = false;
		SelectorOptionsView.ItemsSource = null;
		SelectorOptionsView.HeightRequest = -1;
		_selectorOptionSelected = null;
	}

	private void OnSelectorOptionTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is not string selected || _selectorOptionSelected == null)
		{
			return;
		}

		_selectorOptionSelected(selected);
		HideSelectorDropdown();
	}

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		RefreshStations();
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

			string playbackCandidatesLiteral = JsonSerializer.Serialize(BuildPlaybackCandidates(station));
			string? result = null;
			try
			{
				result = await AudioWebView.EvaluateJavaScriptAsync($"playStation({playbackCandidatesLiteral})");
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
			string? playbackUrl = GetPlaybackUrlFromResult(result) ?? station.StreamUrl;
			_metadataReader.Start(BuildMetadataCandidates(station, playbackUrl), station.Name);
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
				StatusLabel.Text = "Playback failed";
				TrackLabel.Text = "This station did not start. Try Restart or choose another stream.";
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

	private static string[] BuildPlaybackCandidates(RadioStation station)
	{
		IEnumerable<string> baseUrls = station.PlaybackUrls.Count > 0
			? station.PlaybackUrls
			: [station.StreamUrl];

		List<string> normalizedBaseUrls = baseUrls
			.Where(url => !string.IsNullOrWhiteSpace(url))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (normalizedBaseUrls.Count == 0)
		{
			return [];
		}

		bool hasSecureVariant = normalizedBaseUrls.Any(IsSecureStreamUrl);
		List<string> candidates = [];
		foreach (string url in normalizedBaseUrls)
		{
			if (!hasSecureVariant && TryBuildSecureUpgradeCandidate(url, out string? secureCandidate))
			{
				candidates.Add(secureCandidate!);
			}

			candidates.Add(url);
		}

		return candidates
			.Where(candidate => !string.IsNullOrWhiteSpace(candidate))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static string[] BuildMetadataCandidates(RadioStation station, string? playbackUrl)
	{
		IEnumerable<string> baseUrls = station.PlaybackUrls.Count > 0
			? station.PlaybackUrls
			: [station.StreamUrl];

		List<string> normalizedBaseUrls = baseUrls
			.Where(url => !string.IsNullOrWhiteSpace(url))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		bool hasInsecureVariant = normalizedBaseUrls.Any(url => !IsSecureStreamUrl(url));
		List<string> candidates = [];
		foreach (string url in normalizedBaseUrls.OrderBy(url => IsSecureStreamUrl(url) ? 1 : 0))
		{
			candidates.Add(url);
			if (!hasInsecureVariant && TryBuildInsecureMetadataCandidate(url, out string? insecureCandidate))
			{
				candidates.Add(insecureCandidate!);
			}
		}

		if (!string.IsNullOrWhiteSpace(playbackUrl))
		{
			candidates.Add(playbackUrl);
			if (!hasInsecureVariant && TryBuildInsecureMetadataCandidate(playbackUrl, out string? insecureCandidate))
			{
				candidates.Add(insecureCandidate!);
			}
		}

		return candidates
			.Where(url => !string.IsNullOrWhiteSpace(url))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static bool TryBuildInsecureMetadataCandidate(string url, out string? insecureCandidate)
	{
		insecureCandidate = null;
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
			!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
			!(uri.IsDefaultPort || uri.Port == 443))
		{
			return false;
		}

		UriBuilder builder = new(uri)
		{
			Scheme = Uri.UriSchemeHttp,
			Port = -1
		};
		insecureCandidate = builder.Uri.AbsoluteUri;
		return true;
	}

	private static bool TryBuildSecureUpgradeCandidate(string url, out string? secureCandidate)
	{
		secureCandidate = null;
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
			!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
			!(uri.IsDefaultPort || uri.Port == 80))
		{
			return false;
		}

		UriBuilder builder = new(uri)
		{
			Scheme = Uri.UriSchemeHttps,
			Port = -1
		};
		secureCandidate = builder.Uri.AbsoluteUri;
		return true;
	}

	private static bool IsSecureStreamUrl(string url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
			&& string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
	}

	private static string? GetPlaybackUrlFromResult(string? result)
	{
		if (string.IsNullOrWhiteSpace(result))
		{
			return null;
		}

		string trimmed = result.Trim();
		if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
		{
			try
			{
				return JsonSerializer.Deserialize<string>(trimmed);
			}
			catch
			{
				return trimmed.Trim('"');
			}
		}

		return trimmed;
	}

	private static bool TryParseEqualizerLevels(string? result, out float[] levels)
	{
		levels = [];
		if (string.IsNullOrWhiteSpace(result))
		{
			return false;
		}

		string payload = result.Trim();
		if (payload.StartsWith('"') && payload.EndsWith('"'))
		{
			try
			{
				payload = JsonSerializer.Deserialize<string>(payload) ?? string.Empty;
			}
			catch
			{
				payload = payload.Trim('"');
			}
		}

		if (string.IsNullOrWhiteSpace(payload) || payload == "null" || !payload.StartsWith('['))
		{
			return false;
		}

		try
		{
			float[]? parsed = JsonSerializer.Deserialize<float[]>(payload);
			if (parsed == null || parsed.Length == 0 || parsed.All(level => level <= 0.001f))
			{
				return false;
			}

			levels = parsed;
			return true;
		}
		catch
		{
			return false;
		}
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
				return;
			}

			TrackLabel.Text = "Track info unavailable for this station.";
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
		(double Latitude, double Longitude)? coordinate = await LocationCoordinateLookup.ResolveAsync(location);
		if (revision != _weatherRevision)
		{
			return;
		}

		if (coordinate == null)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (revision != _weatherRevision)
				{
					return;
				}

				WeatherBadgeLabel.Text = "Offline";
				WeatherTempLabel.Text = "--";
				WeatherConditionLabel.Text = "Location unavailable";
			});
			return;
		}

		WeatherSnapshot? weather = await WeatherService.GetCurrentAsync(coordinate.Value.Latitude, coordinate.Value.Longitude);
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

	private async Task UpdateMapAsync(LocationProfile location)
	{
		int revision = ++_mapRevision;
		const double targetX = 0.52;
		const double targetY = 0.56;

		_mapFallbackDrawable.SetLocation(location.DisplayName);
		MapTileLayer.Children.Clear();
		MapFallbackView.Invalidate();
		MapLabelText.Text = location.DisplayName;
		AbsoluteLayout.SetLayoutBounds(MapPin, new Rect(targetX, targetY, 14, 14));
		AbsoluteLayout.SetLayoutBounds(MapLabel, new Rect(Math.Clamp(targetX - 0.02, 0.18, 0.82), Math.Clamp(targetY - 0.18, 0.18, 0.82), 190, 30));

		(double Latitude, double Longitude)? coordinate = await LocationCoordinateLookup.ResolveAsync(location);
		if (revision != _mapRevision || coordinate == null)
		{
			return;
		}

		RenderMapTiles(location, coordinate.Value.Latitude, coordinate.Value.Longitude, targetX, targetY, revision);
	}

	private void RenderMapTiles(LocationProfile location, double latitude, double longitude, double targetX, double targetY, int revision)
	{
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
				<audio id="radio"></audio>
				<script>
					const radio = document.getElementById('radio');
					let playToken = 0;
					let audioContext = null;
					let analyser = null;
					let sourceNode = null;
					let frequencyData = null;
					let analysisEnabled = false;
					function resetRadio() {
						radio.pause();
						radio.removeAttribute('src');
						radio.load();
					}
					function ensureAudioAnalyser() {
						try {
							const AudioContextType = window.AudioContext || window.webkitAudioContext;
							if (!AudioContextType) {
								return false;
							}
							if (!audioContext) {
								audioContext = new AudioContextType();
							}
							if (!analyser) {
								analyser = audioContext.createAnalyser();
								analyser.fftSize = 64;
								analyser.smoothingTimeConstant = 0.68;
								frequencyData = new Uint8Array(analyser.frequencyBinCount);
							}
							if (!sourceNode) {
								sourceNode = audioContext.createMediaElementSource(radio);
								sourceNode.connect(analyser);
								analyser.connect(audioContext.destination);
							}
							if (audioContext.state === 'suspended') {
								audioContext.resume().catch(error => console.log('Audio analysis resume failed:', error));
							}
							return true;
						} catch (error) {
							console.log('Audio analysis unavailable:', error);
							return false;
						}
					}
					function buildEqualizerLevels() {
						if (!analysisEnabled || !analyser || !frequencyData || radio.paused || radio.readyState < 2) {
							return '';
						}

						analyser.getByteFrequencyData(frequencyData);
						const bandCount = 18;
						const levels = [];
						let signal = 0;
						for (let band = 0; band < bandCount; band++) {
							const start = Math.floor(Math.pow(band / bandCount, 1.55) * frequencyData.length);
							const end = Math.max(start + 1, Math.floor(Math.pow((band + 1) / bandCount, 1.55) * frequencyData.length));
							let sum = 0;
							for (let index = start; index < Math.min(end, frequencyData.length); index++) {
								sum += frequencyData[index];
							}
							const average = sum / Math.max(1, end - start);
							const normalized = Math.max(0, Math.min(1, average / 255));
							levels.push(normalized);
							signal += normalized;
						}

						return signal > 0.015 ? JSON.stringify(levels) : '';
					}
					function setAnalysisMode(enabled) {
						analysisEnabled = enabled;
						if (enabled) {
							radio.crossOrigin = 'anonymous';
						} else {
							radio.crossOrigin = null;
							radio.removeAttribute('crossorigin');
						}
					}
					function tryPlayCandidate(url, token, analysisPreferred) {
						return new Promise((resolve, reject) => {
							let settled = false;
							const cleanup = () => {
								radio.removeEventListener('playing', onPlaying);
								radio.removeEventListener('loadedmetadata', onLoadedMetadata);
								radio.removeEventListener('loadeddata', onLoadedData);
								radio.removeEventListener('canplay', onCanPlay);
								radio.removeEventListener('error', onError);
								radio.removeEventListener('abort', onAbort);
								clearTimeout(timeoutId);
							};
							const finish = (error) => {
								if (settled) {
									return;
								}
								settled = true;
								cleanup();
								if (token !== playToken) {
									reject(new Error('superseded'));
									return;
								}
								if (error) {
									resetRadio();
									reject(error);
									return;
								}
								resolve(url);
							};
							const onPlaying = () => finish(null);
							const onLoadedMetadata = () => finish(null);
							const onLoadedData = () => finish(null);
							const onCanPlay = () => finish(null);
							const onError = () => finish(new Error('media error'));
							const onAbort = () => finish(new Error('aborted'));
							const timeoutId = setTimeout(() => finish(new Error('timeout')), 8000);
							radio.addEventListener('playing', onPlaying);
							radio.addEventListener('loadedmetadata', onLoadedMetadata);
							radio.addEventListener('loadeddata', onLoadedData);
							radio.addEventListener('canplay', onCanPlay);
							radio.addEventListener('error', onError);
							radio.addEventListener('abort', onAbort);
							resetRadio();
							setAnalysisMode(analysisPreferred);
							radio.preload = 'auto';
							radio.src = url;
							radio.load();
							ensureAudioAnalyser();
							const playPromise = radio.play();
							if (playPromise && typeof playPromise.catch === 'function') {
								playPromise.catch(error => finish(error));
							}
						});
					}
					window.playStation = async function(urls) {
						const token = ++playToken;
						const candidates = Array.isArray(urls) ? urls : [urls];
						for (const url of candidates) {
							try {
								return await tryPlayCandidate(url, token, true);
							} catch (error) {
								if (token === playToken) {
									console.log('Radio analysis-enabled candidate failed:', url, error);
								}
								if (error && error.message && error.message.includes('superseded')) {
									throw error;
								}
							}
							try {
								return await tryPlayCandidate(url, token, false);
							} catch (error) {
								if (token === playToken) {
									console.log('Radio compatibility candidate failed:', url, error);
								}
								if (error && error.message && error.message.includes('superseded')) {
									throw error;
								}
							}
						}
						throw new Error('No playable stream candidate');
					};
					window.getEqualizerLevels = function() {
						return buildEqualizerLevels();
					};
					window.stopStation = function() {
						playToken++;
						analysisEnabled = false;
						radio.src = '';
						resetRadio();
						return 'stopped';
					};
				</script>
			</body>
			</html>
			""";
	}
}
