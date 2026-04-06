using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;

namespace RadioBloom.WinUI;

public sealed partial class MainWindow : Window
{
	private readonly DispatcherQueueTimer _timer;

	private readonly NativeAudioPlayer _player;

	private readonly List<Button> _categoryButtons;

	private readonly StackPanel _categoryPanel;

	private readonly Grid _filterBarGrid;

	private readonly ScrollViewer _categoryScrollViewer;

	private readonly TextBox _searchBox;

	private readonly ComboBox _countryCombo;

	private readonly ComboBox _regionCombo;

	private readonly MapPreviewView _mapView;

	private readonly WeatherSummaryView _weatherView;

	private readonly ListView _stationList;

	private readonly TextBlock _summaryText;

	private readonly TextBlock _locationText;

	private readonly TextBlock _statusText;

	private readonly TextBlock _titleText;

	private readonly TextBlock _subtitleText;

	private readonly TextBlock _nowPlayingText;

	private readonly TextBlock _descriptionText;

	private readonly TextBlock _sourceText;

	private readonly Button _favoriteButton;

	private readonly Button _playButton;

	private readonly Button _stopButton;

	private readonly Slider _volumeSlider;

	private readonly StackPanel _detailsPanel;

	private readonly Button _applyLocationButton;

	private readonly Button _autoLocationButton;

	private readonly ComboBox _savedRegionCombo;

	private readonly Button _saveRegionButton;

	private readonly Button _syncButton;

	private readonly TextBlock _syncStatusText;

	private readonly List<Border> _visualizerBars;

	private readonly List<CountryOption> _countryOptions;

	private RadioBloomProfile _profile;

	private List<RadioStation> _stations;

	private LocationProfile _location;

	private string _catalogMode;

	private string _selectedCategory;

	private RadioStation? _selectedStation;

	private string? _activeStationId;

	private NativePlayerState _playerState;

	private bool _loaded;

	private int _loadRevision;

	private bool _suppressLocationEvents;

	private bool _suppressSavedRegionEvents;

	private double _visualizerLevel;

	private int _stationSwitchRevision;

	private const string AllRegionsLabel = "All states";

	public MainWindow()
	{
		base.Title = "RadioBloom";
		TryEnableSystemBackdrop();
		_timer = base.DispatcherQueue.CreateTimer();
		_timer.Interval = TimeSpan.FromMilliseconds(160.0);
		_timer.Tick += OnTimerTick;
		_player = new NativeAudioPlayer();
		_categoryButtons = new List<Button>();
		_countryOptions = StationCatalogService.GetCountryOptions();
		_profile = RadioBloomProfileStore.LoadProfile();
		_stations = new List<RadioStation>();
		_location = StationCatalogService.GetDefaultLocation();
		_catalogMode = "Curated";
		_selectedCategory = "All";
		_playerState = NativePlayerState.Ready;
		Grid content = BuildLayout(out _categoryPanel, out _filterBarGrid, out _categoryScrollViewer, out _searchBox, out _countryCombo, out _regionCombo, out _mapView, out _weatherView, out _stationList, out _summaryText, out _locationText, out _statusText, out _titleText, out _subtitleText, out _nowPlayingText, out _descriptionText, out _sourceText, out _favoriteButton, out _playButton, out _stopButton, out _volumeSlider, out _detailsPanel, out _applyLocationButton, out _autoLocationButton, out _savedRegionCombo, out _saveRegionButton, out _syncButton, out _syncStatusText, out _visualizerBars);
		base.Content = content;
		content.SizeChanged += delegate
		{
			UpdateFilterBarLayout();
		};
		UpdateFilterBarLayout();
		RenderLocationMap(_location, "Map loading...");
		_weatherView.SetLoading(_location);
		_player.TrackInfoChanged += OnTrackInfoChanged;
		base.Activated += OnWindowActivated;
		_searchBox.TextChanged += delegate
		{
			RefreshStationList();
		};
		_categoryScrollViewer.PointerWheelChanged += OnCategoryScrollViewerPointerWheelChanged;
		_countryCombo.SelectionChanged += OnCountrySelectionChanged;
		_stationList.ContainerContentChanging += OnStationContainerContentChanging;
		_stationList.SelectionChanged += OnSelectionChanged;
		_playButton.Click += delegate
		{
			TogglePlayback();
		};
		_stopButton.Click += delegate
		{
			StopPlayback();
		};
		_volumeSlider.ValueChanged += delegate(object _, RangeBaseValueChangedEventArgs e)
		{
			_player.SetVolume(e.NewValue / 100.0);
		};
		_applyLocationButton.Click += async delegate
		{
			await ApplyManualLocationAsync();
		};
		_autoLocationButton.Click += async delegate
		{
			await ResetToAutoLocationAsync();
		};
		_favoriteButton.Click += async delegate
		{
			await ToggleFavoriteAsync();
		};
		_saveRegionButton.Click += async delegate
		{
			await SaveCurrentRegionAsync();
		};
		_syncButton.Click += async delegate
		{
			await PersistProfileAsync("Library synced");
		};
		_savedRegionCombo.SelectionChanged += async delegate
		{
			await OnSavedRegionSelectionChangedAsync();
		};
		InitializeLocationControls();
		RefreshSavedRegions();
		SetSyncStatus("Ad-free profile sync ready");
		base.Closed += delegate
		{
			_timer.Stop();
			_player.Dispose();
		};
	}

	private void TryEnableSystemBackdrop()
	{
		try
		{
			base.SystemBackdrop = new MicaBackdrop();
		}
		catch
		{
			base.SystemBackdrop = null;
		}
	}

	private void OnTrackInfoChanged(string? trackInfo)
	{
		base.DispatcherQueue.TryEnqueue(delegate
		{
			_nowPlayingText.Text = (string.IsNullOrWhiteSpace(trackInfo) ? "Track info unavailable for this station." : trackInfo);
		});
	}

	private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
	{
		if (!_loaded)
		{
			_loaded = true;
			ApplyCatalog(StationCatalogService.GetFallbackStations(), StationCatalogService.GetDefaultLocation(), "Curated");
			await LoadNearbyStationsAsync();
		}
	}

	private async Task ApplyLocationCatalogAsync(LocationProfile location, int revision)
	{
		Task<List<RadioStation>> stationsTask = StationCatalogService.LoadStationsAsync(location);
		Task<LocationProfile> coordinatesTask = StationCatalogService.EnsureCoordinatesAsync(location);
		await Task.WhenAll(stationsTask, coordinatesTask);
		List<RadioStation> result = stationsTask.Result;
		location = coordinatesTask.Result;
		string mode = BuildCatalogMode(location);
		if (revision == _loadRevision)
		{
			ApplyCatalog(result, location, mode);
			await SyncLocationControlsAsync(location);
			RememberLocationSelection(location);
			SetLocationLoadingState(isLoading: false);
			await PersistProfileAsync();
			_ = RefreshWeatherAsync(location, revision);
		}
	}

	private async Task LoadNearbyStationsAsync()
	{
		int revision = ++_loadRevision;
		SetLocationLoadingState(isLoading: true, "Detecting location...");
		LocationProfile location = await StationCatalogService.GetApproximateLocationAsync();
		await ApplyLocationCatalogAsync(location, revision);
	}

	private async Task ApplyManualLocationAsync()
	{
		if (!(_countryCombo.SelectedItem is CountryOption countryOption))
		{
			await ResetToAutoLocationAsync();
			return;
		}
		string text = _regionCombo.SelectedItem as string;
		if (string.Equals(text, "All states", StringComparison.Ordinal))
		{
			text = null;
		}
		int revision = ++_loadRevision;
		string text2 = (string.IsNullOrWhiteSpace(text) ? countryOption.DisplayName : (text + ", " + countryOption.DisplayName));
		SetLocationLoadingState(isLoading: true, "Loading " + text2 + "...");
		LocationProfile location = new LocationProfile
		{
			Region = (string.IsNullOrWhiteSpace(text) ? null : text),
			CountryCode = countryOption.CountryCode,
			CountryName = countryOption.DisplayName,
			Label = text2,
			Source = "Manual override"
		};
		await ApplyLocationCatalogAsync(location, revision);
	}

	private async Task ResetToAutoLocationAsync()
	{
		await LoadNearbyStationsAsync();
	}

	private void SetLocationLoadingState(bool isLoading, string? summaryOverride = null)
	{
		_applyLocationButton.IsEnabled = !isLoading;
		_autoLocationButton.IsEnabled = !isLoading;
		_saveRegionButton.IsEnabled = !isLoading;
		_syncButton.IsEnabled = !isLoading;
		_savedRegionCombo.IsEnabled = !isLoading;
		_countryCombo.IsEnabled = !isLoading;
		_regionCombo.IsEnabled = !isLoading;
		if (!string.IsNullOrWhiteSpace(summaryOverride))
		{
			_summaryText.Text = summaryOverride;
		}
	}

	private void ApplyCatalog(List<RadioStation> stations, LocationProfile location, string mode)
	{
		string? previousSelectionId = _selectedStation?.Id;
		_stations = stations ?? new List<RadioStation>();
		_location = location ?? StationCatalogService.GetDefaultLocation();
		_catalogMode = mode ?? "Curated";
		ApplyProfileStateToStations();
		_selectedCategory = "All";
		_selectedStation = _stations.FirstOrDefault((RadioStation s) => string.Equals(s.Id, previousSelectionId, StringComparison.OrdinalIgnoreCase)) ??
			_stations.FirstOrDefault((RadioStation s) => s.IsFavorite) ??
			_stations.FirstOrDefault((RadioStation s) => s.IsCurated) ??
			_stations.FirstOrDefault((RadioStation s) => s.Featured) ??
			_stations.FirstOrDefault();
		BuildCategoryButtons();
		RefreshStationList();
		RenderLocationMap(_location);
		_weatherView.SetLoading(_location);
		UpdateDetails();
	}

	private async Task RefreshWeatherAsync(LocationProfile location, int revision)
	{
		WeatherSnapshot? weather = await StationCatalogService.GetWeatherAsync(location);
		if (revision != _loadRevision)
		{
			return;
		}

		base.DispatcherQueue.TryEnqueue(delegate
		{
			_weatherView.Render(location, weather);
		});
	}

	private void InitializeLocationControls()
	{
		_countryCombo.DisplayMemberPath = "DisplayName";
		_countryCombo.ItemsSource = _countryOptions;
		_countryCombo.SelectedItem = _countryOptions.FirstOrDefault((CountryOption c) => string.Equals(c.CountryCode, _location.CountryCode, StringComparison.OrdinalIgnoreCase)) ?? _countryOptions.FirstOrDefault();
		_savedRegionCombo.DisplayMemberPath = "DisplayLabel";
	}

	private async void OnCountrySelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_suppressLocationEvents)
		{
			if (!(_countryCombo.SelectedItem is CountryOption countryOption))
			{
				_regionCombo.ItemsSource = null;
			}
			else
			{
				await PopulateRegionsAsync(countryOption.CountryCode, null);
			}
		}
	}

	private async Task SyncLocationControlsAsync(LocationProfile location)
	{
		_suppressLocationEvents = true;
		try
		{
			CountryOption countryOption = _countryOptions.FirstOrDefault((CountryOption c) => string.Equals(c.CountryCode, location.CountryCode, StringComparison.OrdinalIgnoreCase));
			if (countryOption != null)
			{
				_countryCombo.SelectedItem = countryOption;
				await PopulateRegionsAsync(countryOption.CountryCode, location.Region);
			}
		}
		finally
		{
			_suppressLocationEvents = false;
		}
	}

	private async Task PopulateRegionsAsync(string countryCode, string? selectedRegion)
	{
		List<string> options = new List<string>(1) { "All states" };
		List<string> list = options;
		list.AddRange(await StationCatalogService.GetRegionsForCountryAsync(countryCode));
		_regionCombo.ItemsSource = options;
		_regionCombo.SelectedItem = (string.IsNullOrWhiteSpace(selectedRegion) ? "All states" : (options.FirstOrDefault((string region) => string.Equals(region, selectedRegion, StringComparison.OrdinalIgnoreCase)) ?? "All states"));
	}

	private static string BuildCatalogMode(LocationProfile location)
	{
		if (!string.Equals(location.CountryCode, "DE", StringComparison.OrdinalIgnoreCase))
		{
			return "Nearby";
		}
		return "Germany";
	}

	private void RenderLocationMap(LocationProfile location, string? placeholder = null)
	{
		_mapView.Render(location, placeholder);
	}

	private void ApplyProfileStateToStations()
	{
		HashSet<string> favorites = new(_profile.FavoriteStationIds, StringComparer.OrdinalIgnoreCase);
		foreach (RadioStation station in _stations)
		{
			station.IsFavorite = favorites.Contains(station.Id);
		}
	}

	private void RememberLocationSelection(LocationProfile location)
	{
		_profile.LastCountryCode = location.CountryCode;
		_profile.LastRegion = location.Region;
	}

	private async Task PersistProfileAsync(string? statusMessage = null)
	{
		if (_countryCombo.SelectedItem is CountryOption country)
		{
			_profile.LastCountryCode = country.CountryCode;
		}

		_profile.LastRegion = (_regionCombo.SelectedItem as string) switch
		{
			null => _profile.LastRegion,
			var region when string.Equals(region, AllRegionsLabel, StringComparison.OrdinalIgnoreCase) => null,
			var region => region
		};

		await RadioBloomProfileStore.SaveProfileAsync(_profile);
		SetSyncStatus(statusMessage ?? "Synced to Documents\\RadioBloom Sync");
	}

	private void SetSyncStatus(string text)
	{
		_syncStatusText.Text = text;
	}

	private void RefreshSavedRegions()
	{
		_suppressSavedRegionEvents = true;
		try
		{
			_savedRegionCombo.ItemsSource = _profile.SavedRegions.ToList();
			_savedRegionCombo.PlaceholderText = _profile.SavedRegions.Count == 0 ? "No saved regions yet" : "Jump to a saved region";
			_savedRegionCombo.SelectedItem = null;
		}
		finally
		{
			_suppressSavedRegionEvents = false;
		}
	}

	private async Task OnSavedRegionSelectionChangedAsync()
	{
		if (_suppressSavedRegionEvents || !(_savedRegionCombo.SelectedItem is SavedRegionEntry saved))
		{
			return;
		}

		int revision = ++_loadRevision;
		SetLocationLoadingState(isLoading: true, "Loading " + saved.DisplayLabel + "...");
		LocationProfile location = new()
		{
			Region = string.IsNullOrWhiteSpace(saved.Region) ? null : saved.Region,
			CountryCode = saved.CountryCode,
			CountryName = saved.CountryName,
			Label = saved.DisplayLabel,
			Source = "Saved region"
		};
		await ApplyLocationCatalogAsync(location, revision);
		SetSyncStatus("Loaded " + saved.DisplayLabel);
	}

	private async Task SaveCurrentRegionAsync()
	{
		SavedRegionEntry? entry = RadioBloomProfileStore.CreateSavedRegion(_location);
		if (entry == null)
		{
			SetSyncStatus("Nothing to save for this location");
			return;
		}

		SavedRegionEntry? existing = _profile.SavedRegions.FirstOrDefault(item =>
			string.Equals(item.CountryCode, entry.CountryCode, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.Region ?? string.Empty, entry.Region ?? string.Empty, StringComparison.OrdinalIgnoreCase));

		if (existing == null)
		{
			_profile.SavedRegions.Add(entry);
		}
		else
		{
			existing.CountryName = entry.CountryName;
			existing.Label = entry.Label;
		}

		_profile.SavedRegions = _profile.SavedRegions
			.OrderBy(item => item.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		RefreshSavedRegions();
		await PersistProfileAsync("Saved " + entry.DisplayLabel);
	}

	private async Task ToggleFavoriteAsync()
	{
		if (_selectedStation == null)
		{
			return;
		}

		if (_selectedStation.IsFavorite)
		{
			_profile.FavoriteStationIds.RemoveAll(id => string.Equals(id, _selectedStation.Id, StringComparison.OrdinalIgnoreCase));
		}
		else
		{
			_profile.FavoriteStationIds.Add(_selectedStation.Id);
		}

		_profile.FavoriteStationIds = _profile.FavoriteStationIds
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		ApplyProfileStateToStations();
		BuildCategoryButtons();
		RefreshStationList();
		UpdateDetails();
		await PersistProfileAsync(_selectedStation.IsFavorite ? "Favorite saved" : "Favorite removed");
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

	private void BuildCategoryButtons()
	{
		_categoryPanel.Children.Clear();
		_categoryButtons.Clear();
		foreach (string categoryName in StationCatalogService.BuildCategoryList(_stations))
		{
			Button button = new Button
			{
				Content = categoryName,
				MinWidth = 74.0,
				Height = 38.0,
				Padding = new Thickness(16.0, 0.0, 16.0, 0.0),
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
				CornerRadius = new CornerRadius(14.0),
				BorderThickness = new Thickness(1.0),
				FontSize = 13.0,
				FontWeight = FontWeights.SemiBold
			};
			button.Click += delegate
			{
				_selectedCategory = categoryName;
				UpdateCategoryStyles();
				RefreshStationList();
			};
			_categoryButtons.Add(button);
			_categoryPanel.Children.Add(button);
		}
		UpdateCategoryStyles();
	}

	private void UpdateFilterBarLayout()
	{
		if (_filterBarGrid.ActualWidth <= 0.0)
		{
			return;
		}

		bool compact = _filterBarGrid.ActualWidth < 860.0;
		if (_filterBarGrid.RowDefinitions.Count < 2 || _filterBarGrid.ColumnDefinitions.Count < 2)
		{
			return;
		}

		_filterBarGrid.ColumnDefinitions[1].Width = compact ? new GridLength(1.0, GridUnitType.Star) : new GridLength(280.0);
		_filterBarGrid.RowDefinitions[1].Height = compact ? GridLength.Auto : new GridLength(0.0);
		_filterBarGrid.RowSpacing = compact ? 10.0 : 0.0;
		_categoryScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;

		Grid.SetRow(_categoryScrollViewer, 0);
		Grid.SetColumn(_categoryScrollViewer, 0);
		Grid.SetColumnSpan(_categoryScrollViewer, compact ? 2 : 1);

		Grid.SetRow(_searchBox, compact ? 1 : 0);
		Grid.SetColumn(_searchBox, compact ? 0 : 1);
		Grid.SetColumnSpan(_searchBox, compact ? 2 : 1);
		_searchBox.Margin = compact ? new Thickness(0.0) : new Thickness(0.0);
	}

	private void UpdateCategoryStyles()
	{
		foreach (Button categoryButton in _categoryButtons)
		{
			bool flag = string.Equals(Convert.ToString(categoryButton.Content, CultureInfo.InvariantCulture), _selectedCategory, StringComparison.OrdinalIgnoreCase);
			categoryButton.Background = Brush(flag ? "#111827" : "#F4F6F8");
			categoryButton.Foreground = Brush(flag ? "#FFFFFF" : "#1F2937");
			categoryButton.BorderBrush = Brush(flag ? "#111827" : "#E5E7EB");
		}
	}

	private void RefreshStationList()
	{
		IEnumerable<RadioStation> source = _stations;
		string needle = (_searchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
		if (!string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
		{
			source = source.Where((RadioStation s) => MatchesSelectedCategory(s, _selectedCategory));
		}
		if (!string.IsNullOrWhiteSpace(needle))
		{
			source = source.Where((RadioStation station) => string.Join(" ", station.Name, station.Subtitle, station.Location, station.Genre, station.Tagline, station.Description, station.MetadataSummary, station.HomepageHost, station.LanguageLabel).ToLowerInvariant().Contains(needle));
		}
		List<RadioStation> list = (from s in source
			orderby s.IsFavorite descending, s.IsCurated descending, s.Featured descending, s.PopularityScore descending, s.Name
			select s).ToList();
		if (_selectedStation != null && !list.Any((RadioStation station) => string.Equals(station.Id, _selectedStation.Id, StringComparison.OrdinalIgnoreCase)))
		{
			_selectedStation = list.FirstOrDefault();
		}
		_stationList.ItemsSource = list;
		_stationList.SelectedItem = _selectedStation;
		base.DispatcherQueue.TryEnqueue(UpdateStationCardStates);
		string text = ((list.Count == 1) ? "station" : "stations");
		string text2 = ((!string.IsNullOrWhiteSpace(_location.Region)) ? (_location.Region + ", " + _location.CountryName) : _location.CountryName);
		if (string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
		{
			_summaryText.Text = list.Count.ToString(CultureInfo.InvariantCulture) + " " + text + " for " + text2 + ".";
		}
		else if (string.Equals(_selectedCategory, "Featured", StringComparison.OrdinalIgnoreCase))
		{
			_summaryText.Text = list.Count.ToString(CultureInfo.InvariantCulture) + " featured " + text + " for " + text2 + ".";
		}
		else
		{
			_summaryText.Text = list.Count.ToString(CultureInfo.InvariantCulture) + " " + _selectedCategory.ToLowerInvariant() + " " + text + " for " + text2 + ".";
		}
	}

	private static bool MatchesSelectedCategory(RadioStation station, string category)
	{
		return category switch
		{
			"Favorites" => station.IsFavorite,
			"Featured" => station.Featured,
			"Curated" => station.IsCurated,
			"Discovery" => station.IsDiscovery,
			_ => station.Categories.Contains(category)
		};
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_stationList.SelectedItem is RadioStation selectedStation)
		{
			_selectedStation = selectedStation;
			UpdateStationCardStates();
			UpdateDetails();
			if (!string.Equals(_activeStationId, _selectedStation.Id, StringComparison.OrdinalIgnoreCase) && (_playerState == NativePlayerState.Live || _playerState == NativePlayerState.Connecting || _playerState == NativePlayerState.Paused))
			{
				StartSelectedStation(deferForRapidChanges: true);
			}
		}
	}

	private void UpdateDetails()
	{
		if (_selectedStation == null)
		{
			_titleText.Text = "Select a station";
			_subtitleText.Text = "Modern radio for Windows";
			_nowPlayingText.Text = "Artist and title will appear here when available.";
			_descriptionText.Text = "Choose a station on the left to begin.";
			_sourceText.Text = "Source metadata will appear here.";
			_favoriteButton.Content = "Save favorite";
			_favoriteButton.IsEnabled = false;
			AnimateDetailsPanel();
			RefreshTransport();
		}
		else
		{
			_titleText.Text = _selectedStation.Name;
			_subtitleText.Text = _selectedStation.EditorialBadge + " | " + _selectedStation.Genre + " | " + _selectedStation.Location;
			_nowPlayingText.Text = (string.Equals(_activeStationId, _selectedStation.Id, StringComparison.OrdinalIgnoreCase) ? "Looking for artist and title..." : "Artist and title will appear here when playback starts.");
			_descriptionText.Text = _selectedStation.Description;
			_sourceText.Text = _selectedStation.SourceLabel + " | " + _selectedStation.MetadataSummary;
			_favoriteButton.Content = (_selectedStation.IsFavorite ? "Favorited" : "Save favorite");
			_favoriteButton.IsEnabled = true;
			AnimateDetailsPanel();
			RefreshTransport();
		}
	}

	private void TogglePlayback()
	{
		if (_selectedStation == null)
		{
			return;
		}
		try
		{
			if (!string.Equals(_activeStationId, _selectedStation.Id, StringComparison.OrdinalIgnoreCase))
			{
				StartSelectedStation();
			}
			else if (_playerState == NativePlayerState.Live || _playerState == NativePlayerState.Connecting)
			{
				CancelPendingStationSwitch();
				_player.Pause();
			}
			else if (_playerState == NativePlayerState.Paused)
			{
				CancelPendingStationSwitch();
				_player.Play();
			}
			else
			{
				StartSelectedStation();
			}
		}
		catch
		{
			_playerState = NativePlayerState.Error;
		}
		RefreshTransport();
	}

	private void StopPlayback()
	{
		try
		{
			CancelPendingStationSwitch();
			_player.Stop();
			_activeStationId = null;
			_playerState = NativePlayerState.Stopped;
			_nowPlayingText.Text = "Playback stopped.";
		}
		catch
		{
			_playerState = NativePlayerState.Error;
		}
		RefreshTransport();
	}

	private void OnTimerTick(DispatcherQueueTimer sender, object args)
	{
		RefreshTransport();
		RefreshVisualizer();
	}

	private async void StartSelectedStation(bool deferForRapidChanges = false)
	{
		RadioStation? pendingStation = _selectedStation;
		if (pendingStation == null || string.IsNullOrWhiteSpace(pendingStation.StreamUrl))
		{
			return;
		}

		int switchRevision = ++_stationSwitchRevision;
		if (deferForRapidChanges)
		{
			try
			{
				await Task.Delay(140);
			}
			catch
			{
			}
		}

		if (!IsLatestStationSwitchRequest(switchRevision, pendingStation))
		{
			return;
		}

		try
		{
			_player.Open(pendingStation.StreamUrl);
			if (!IsLatestStationSwitchRequest(switchRevision, pendingStation))
			{
				_player.Stop();
				return;
			}
			_player.Play();
			_activeStationId = pendingStation.Id;
			_playerState = NativePlayerState.Connecting;
			_nowPlayingText.Text = "Looking for artist and title...";
			if (!_timer.IsRunning)
			{
				_timer.Start();
			}
		}
		catch
		{
			if (!IsLatestStationSwitchRequest(switchRevision, pendingStation))
			{
				return;
			}
			_activeStationId = null;
			_playerState = NativePlayerState.Error;
			_nowPlayingText.Text = "Couldn't switch stations. Try again.";
			RefreshTransport();
		}
	}

	private void CancelPendingStationSwitch()
	{
		_stationSwitchRevision++;
	}

	private bool IsLatestStationSwitchRequest(int switchRevision, RadioStation pendingStation)
	{
		return switchRevision == _stationSwitchRevision && _selectedStation != null && string.Equals(_selectedStation.Id, pendingStation.Id, StringComparison.OrdinalIgnoreCase);
	}

	private void OnCategoryScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
	{
		if (_categoryScrollViewer.ScrollableWidth <= 0.0)
		{
			return;
		}

		int mouseWheelDelta = e.GetCurrentPoint(_categoryScrollViewer).Properties.MouseWheelDelta;
		if (mouseWheelDelta == 0)
		{
			return;
		}

		double targetOffset = Math.Clamp(_categoryScrollViewer.HorizontalOffset - Math.Sign(mouseWheelDelta) * 140.0, 0.0, _categoryScrollViewer.ScrollableWidth);
		_categoryScrollViewer.ChangeView(targetOffset, null, null, disableAnimation: true);
		e.Handled = true;
	}

	private void OnStationContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		if (args.ItemContainer is ListViewItem listViewItem && !args.InRecycleQueue)
		{
			listViewItem.PointerEntered -= OnStationPointerEntered;
			listViewItem.PointerExited -= OnStationPointerExited;
			listViewItem.PointerCanceled -= OnStationPointerExited;
			listViewItem.PointerCaptureLost -= OnStationPointerExited;
			listViewItem.Loaded -= OnStationContainerLoaded;
			listViewItem.PointerEntered += OnStationPointerEntered;
			listViewItem.PointerExited += OnStationPointerExited;
			listViewItem.PointerCanceled += OnStationPointerExited;
			listViewItem.PointerCaptureLost += OnStationPointerExited;
			listViewItem.Loaded += OnStationContainerLoaded;
			ApplyStationCardState(listViewItem, hovered: false);
		}
	}

	private void OnStationContainerLoaded(object sender, RoutedEventArgs e)
	{
		if (sender is ListViewItem container)
		{
			ApplyStationCardState(container, hovered: false);
		}
	}

	private void OnStationPointerEntered(object sender, PointerRoutedEventArgs e)
	{
		if (sender is ListViewItem listViewItem)
		{
			listViewItem.Tag = "hover";
			ApplyStationCardState(listViewItem, hovered: true);
		}
	}

	private void OnStationPointerExited(object sender, object e)
	{
		if (sender is ListViewItem listViewItem)
		{
			listViewItem.Tag = null;
			ApplyStationCardState(listViewItem, hovered: false);
		}
	}

	private void UpdateStationCardStates()
	{
		for (int i = 0; i < _stationList.Items.Count; i++)
		{
			if (_stationList.ContainerFromIndex(i) is ListViewItem listViewItem)
			{
				bool hovered = string.Equals(listViewItem.Tag as string, "hover", StringComparison.Ordinal);
				ApplyStationCardState(listViewItem, hovered);
			}
		}
	}

	private void ApplyStationCardState(ListViewItem container, bool hovered)
	{
		Border border = FindStationCard(container);
		if (!(border == null))
		{
			RadioStation radioStation = container.Content as RadioStation;
			bool isSelected = container.IsSelected;
			Border border2 = FindNamedElement<Border>(container, "GenreChip");
			TextBlock textBlock = FindNamedElement<TextBlock>(container, "GenreChipText");
			Border border3 = FindNamedElement<Border>(container, "SourcePill");
			TextBlock textBlock2 = FindNamedElement<TextBlock>(container, "SourcePillText");
			TextBlock textBlock3 = FindNamedElement<TextBlock>(container, "StationName");
			Border border4 = FindNamedElement<Border>(container, "EditorialPill");
			TextBlock textBlock4 = FindNamedElement<TextBlock>(container, "EditorialPillText");
			bool flag = radioStation?.IsFavorite ?? false;
			bool flag2 = radioStation?.IsCurated ?? false;
			bool flag3 = radioStation?.IsDiscovery ?? false;
			border.Background = Brush(isSelected ? "#F7FAFE" : (hovered ? "#FFFFFF" : (flag ? "#FBFDFF" : "#FCFDFE")));
			border.BorderBrush = Brush(isSelected ? "#D7E3F1" : (flag ? "#D7E5F8" : (hovered ? "#DCE5EF" : "#E9EEF5")));
			border.BorderThickness = new Thickness(1.0);
			border.Shadow = ((hovered || isSelected) ? new ThemeShadow() : null);
			ScaleTransform scaleTransform = border.RenderTransform as ScaleTransform;
			if ((object)scaleTransform == null)
			{
				scaleTransform = (ScaleTransform)(border.RenderTransform = new ScaleTransform
				{
					ScaleX = 1.0,
					ScaleY = 1.0
				});
				border.RenderTransformOrigin = new Point(0.5, 0.5);
			}
			scaleTransform.ScaleX = (hovered ? 1.02 : (isSelected ? 1.006 : 1.0));
			scaleTransform.ScaleY = (hovered ? 1.02 : (isSelected ? 1.006 : 1.0));
			border.Translation = (hovered ? new Vector3(0f, -6f, 14f) : (isSelected ? new Vector3(0f, -1.5f, 4f) : Vector3.Zero));
			if (border2 != null)
			{
				border2.Background = Brush(isSelected ? "#E8F0FB" : (hovered ? "#EEF3F8" : "#F3F5F8"));
				border2.BorderBrush = Brush(isSelected ? "#C7D8EE" : (hovered ? "#D5DFEA" : "#EFF2F6"));
			}
			if (textBlock != null)
			{
				textBlock.Foreground = Brush(isSelected ? "#22476A" : (hovered ? "#35526E" : "#607080"));
			}
			if (border3 != null)
			{
				border3.Background = Brush(isSelected ? "#F0F6FF" : (hovered ? "#F5F8FC" : "#F8FAFC"));
				border3.BorderBrush = Brush(isSelected ? "#D7E3F3" : (hovered ? "#E1E8F0" : "#ECF1F5"));
			}
			if (textBlock2 != null)
			{
				textBlock2.Foreground = Brush(isSelected ? "#3A5672" : "#66788A");
			}
			if (textBlock3 != null)
			{
				textBlock3.Foreground = Brush(isSelected ? "#111827" : (hovered ? "#182536" : "#1F2937"));
			}
			if (border4 != null)
			{
				border4.Background = Brush(flag ? "#EEF4FD" : (flag3 ? "#EDF7F4" : (flag2 ? "#F5F1FF" : "#F8FAFC")));
				border4.BorderBrush = Brush(flag ? "#D3E0F3" : (flag3 ? "#D7EDE4" : (flag2 ? "#E2D9FA" : "#ECF1F5")));
			}
			if (textBlock4 != null)
			{
				textBlock4.Foreground = Brush(flag ? "#31597C" : (flag3 ? "#2F6A58" : (flag2 ? "#5F4A8A" : "#66788A")));
			}
		}
	}

	private void AnimateDetailsPanel()
	{
		_detailsPanel.Opacity = 0.76;
		Storyboard storyboard = new Storyboard();
		DoubleAnimation doubleAnimation = new DoubleAnimation
		{
			From = 0.76,
			To = 1.0,
			Duration = new Duration(TimeSpan.FromMilliseconds(220.0))
		};
		Storyboard.SetTarget(doubleAnimation, _detailsPanel);
		Storyboard.SetTargetProperty(doubleAnimation, "Opacity");
		storyboard.Children.Add(doubleAnimation);
		storyboard.Begin();
	}

	private void RefreshVisualizer()
	{
		if (_visualizerBars.Count == 0)
		{
			return;
		}
		bool flag = _playerState == NativePlayerState.Live;
		bool flag2 = _playerState == NativePlayerState.Connecting;
		bool flag3 = _playerState == NativePlayerState.Paused;
		float[] array = (flag ? _player.GetSpectrumLevels() : Array.Empty<float>());
		double val = (flag ? Math.Clamp((double)_player.GetAudioLevel() * 2.2, 0.0, 1.0) : 0.0);
		_visualizerLevel = (flag ? Math.Max(val, _visualizerLevel * 0.72) : (flag3 ? (_visualizerLevel * 0.82) : 0.0));
		for (int i = 0; i < _visualizerBars.Count; i++)
		{
			Border border = _visualizerBars[i];
			double height = 7.0;
			string hex = "#D8E3EF";
			if (flag)
			{
				double num = Math.Clamp(((i < array.Length) ? ((double)array[i]) : 0.0) * 0.9 + _visualizerLevel * 0.28, 0.0, 1.0);
				height = 8.0 + num * 38.0;
				hex = ((num > 0.75) ? "#426D9E" : ((num > 0.45) ? "#6E93BE" : ((num > 0.2) ? "#9AB5D3" : "#C3D3E4")));
			}
			else if (flag2)
			{
				height = 8 + (i + DateTime.Now.Second % 4) % 4 * 7;
				hex = "#A8BED7";
			}
			else if (flag3)
			{
				height = ((i % 2 == 0) ? 9 : 13);
				hex = "#C8D5E2";
			}
			border.Height = height;
			border.Background = Brush(hex);
			border.Opacity = (flag ? 1.0 : (flag2 ? 0.88 : (flag3 ? 0.76 : 0.55)));
		}
	}

	private static Border? FindStationCard(DependencyObject root)
	{
		return FindNamedElement<Border>(root, "StationCard");
	}

	private static T? FindNamedElement<T>(DependencyObject root, string name) where T : FrameworkElement
	{
		if (root is T val && string.Equals(val.Name, name, StringComparison.Ordinal))
		{
			return val;
		}
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			T val2 = FindNamedElement<T>(VisualTreeHelper.GetChild(root, i), name);
			if (val2 != null)
			{
				return val2;
			}
		}
		return null;
	}

	private void RefreshTransport()
	{
		if (_playerState != NativePlayerState.Error)
		{
			_playerState = _player.GetState();
		}
		_statusText.Text = ((_playerState == NativePlayerState.Connecting) ? "Connecting" : _playerState.ToString());
		_playButton.Content = ((_playerState == NativePlayerState.Live || _playerState == NativePlayerState.Connecting) ? "Pause" : ((_playerState == NativePlayerState.Paused) ? "Resume" : "Play"));
		_stopButton.IsEnabled = _playerState == NativePlayerState.Live || _playerState == NativePlayerState.Paused || _playerState == NativePlayerState.Connecting;
		if (_playerState == NativePlayerState.Ready || _playerState == NativePlayerState.Stopped || _playerState == NativePlayerState.Error)
		{
			_activeStationId = null;
		}
	}

	private static Grid BuildLayout(out StackPanel categoryPanel, out Grid filterBarGrid, out ScrollViewer categoryScrollViewer, out TextBox searchBox, out ComboBox countryCombo, out ComboBox regionCombo, out MapPreviewView mapView, out WeatherSummaryView weatherView, out ListView stationList, out TextBlock summaryText, out TextBlock locationText, out TextBlock statusText, out TextBlock titleText, out TextBlock subtitleText, out TextBlock nowPlayingText, out TextBlock descriptionText, out TextBlock sourceText, out Button favoriteButton, out Button playButton, out Button stopButton, out Slider volumeSlider, out StackPanel detailsPanel, out Button applyLocationButton, out Button autoLocationButton, out ComboBox savedRegionCombo, out Button saveRegionButton, out Button syncButton, out TextBlock syncStatusText, out List<Border> visualizerBars)
	{
		Grid grid = new Grid
		{
			Padding = new Thickness(28.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(2.1, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(24.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		Grid grid2 = new Grid();
		grid2.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid2.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid2.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		Grid.SetColumn(grid2, 0);
		grid.Children.Add(grid2);
		Border border = CardBorder(new Thickness(0.0, 0.0, 0.0, 18.0), new Thickness(24.0));
		Grid grid3 = new Grid
		{
			ColumnSpacing = 24.0
		};
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.55, GridUnitType.Star)
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(0.95, GridUnitType.Star)
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(236.0)
		});
		summaryText = new TextBlock
		{
			Foreground = Brush("#425466"),
			FontSize = 15.0,
			FontWeight = FontWeights.SemiBold,
			Text = "Loading stations..."
		};
		locationText = new TextBlock
		{
			Visibility = Visibility.Collapsed
		};
		Border border2 = new Border
		{
			Background = Brush("#F8FAFC"),
			BorderBrush = Brush("#E7EDF3"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(20.0),
			Padding = new Thickness(12.0),
			Height = 188.0,
			MaxHeight = 188.0,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		Grid.SetColumn(border2, 0);
		Grid grid4 = new Grid
		{
			RowSpacing = 10.0
		};
		grid4.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid4.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		grid4.Children.Add(new TextBlock
		{
			Text = "Region Preview",
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brush("#8694A4"),
			CharacterSpacing = 120
		});
		mapView = new MapPreviewView
		{
			Height = 140.0,
			MinHeight = 140.0,
			MaxHeight = 140.0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		Grid.SetRow(mapView, 1);
		grid4.Children.Add(mapView);
		border2.Child = grid4;
		grid3.Children.Add(border2);
		Border border3 = new Border
		{
			Background = Brush("#F8FAFC"),
			BorderBrush = Brush("#E7EDF3"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(20.0),
			Padding = new Thickness(14.0),
			Height = 188.0,
			MaxHeight = 188.0,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		Grid.SetColumn(border3, 1);
		Grid grid5 = new Grid
		{
			RowSpacing = 10.0
		};
		grid5.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid5.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		grid5.Children.Add(new TextBlock
		{
			Text = "Weather",
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brush("#8694A4"),
			CharacterSpacing = 120
		});
		weatherView = new WeatherSummaryView
		{
			Height = 140.0,
			MinHeight = 140.0,
			MaxHeight = 140.0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		Grid.SetRow(weatherView, 1);
		grid5.Children.Add(weatherView);
		border3.Child = grid5;
		grid3.Children.Add(border3);
		Border border4 = new Border
		{
			Background = Brush("#F8FAFC"),
			BorderBrush = Brush("#E7EDF3"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(20.0),
			Padding = new Thickness(14.0),
			Width = 236.0,
			Height = 188.0,
			MaxHeight = 188.0,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Grid.SetColumn(border4, 2);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 8.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Station Region",
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brush("#8694A4"),
			CharacterSpacing = 120
		});
		countryCombo = new ComboBox
		{
			Height = 36.0,
			PlaceholderText = "Country",
			IsEditable = false,
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Background = Brush("#FFFFFF"),
			BorderBrush = Brush("#DCE5EE"),
			BorderThickness = new Thickness(1.0)
		};
		stackPanel.Children.Add(countryCombo);
		regionCombo = new ComboBox
		{
			Height = 36.0,
			PlaceholderText = "State / region",
			IsEditable = false,
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Background = Brush("#FFFFFF"),
			BorderBrush = Brush("#DCE5EE"),
			BorderThickness = new Thickness(1.0)
		};
		stackPanel.Children.Add(regionCombo);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		applyLocationButton = new Button
		{
			Content = "Apply",
			Height = 32.0,
			Padding = new Thickness(14.0, 0.0, 14.0, 0.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Brush("#111827"),
			Foreground = Brush("#FFFFFF"),
			BorderBrush = Brush("#111827")
		};
		autoLocationButton = new Button
		{
			Content = "Auto",
			Height = 32.0,
			Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Brush("#FFFFFF"),
			Foreground = Brush("#334155"),
			BorderBrush = Brush("#DCE5EE")
		};
		stackPanel2.Children.Add(applyLocationButton);
		stackPanel2.Children.Add(autoLocationButton);
		stackPanel.Children.Add(stackPanel2);
		border4.Child = stackPanel;
		grid3.Children.Add(border4);
		border.Child = grid3;
		grid2.Children.Add(border);
		Border border5 = CardBorder(new Thickness(0.0, 0.0, 0.0, 18.0), new Thickness(18.0));
		Grid.SetRow(border5, 1);
		StackPanel stackPanel3 = new StackPanel
		{
			Spacing = 12.0
		};
		stackPanel3.Children.Add(summaryText);
		Grid grid6 = new Grid
		{
			ColumnSpacing = 16.0,
			RowSpacing = 0.0
		};
		grid6.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid6.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(0.0)
		});
		grid6.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid6.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(280.0)
		});
		ScrollViewer scrollViewer = new ScrollViewer
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollMode = ScrollMode.Enabled,
			VerticalScrollMode = ScrollMode.Disabled,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		categoryPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		scrollViewer.Content = categoryPanel;
		grid6.Children.Add(scrollViewer);
		searchBox = new TextBox
		{
			Height = 42.0,
			PlaceholderText = "Search stations",
			CornerRadius = new CornerRadius(14.0),
			Padding = new Thickness(14.0, 0.0, 14.0, 0.0),
			Background = Brush("#FFFFFF"),
			BorderBrush = Brush("#DCE5EE"),
			BorderThickness = new Thickness(1.0)
		};
		Grid.SetColumn(searchBox, 1);
		grid6.Children.Add(searchBox);
		filterBarGrid = grid6;
		categoryScrollViewer = scrollViewer;
		stackPanel3.Children.Add(grid6);
		border5.Child = stackPanel3;
		grid2.Children.Add(border5);
		Border border6 = CardBorder(new Thickness(0.0), new Thickness(8.0));
		Grid.SetRow(border6, 2);
		stationList = new ListView
		{
			SelectionMode = ListViewSelectionMode.Single,
			ItemContainerStyle = CreateStationContainerStyle(),
			ItemContainerTransitions = 
			{
				(Transition)new EntranceThemeTransition
				{
					FromVerticalOffset = 18.0
				},
				(Transition)new RepositionThemeTransition()
			}
		};
		stationList.ItemTemplate = CreateStationTemplate();
		border6.Child = stationList;
		grid2.Children.Add(border6);
		Border border7 = CardBorder(new Thickness(0.0), new Thickness(24.0));
		Grid.SetColumn(border7, 2);
		grid.Children.Add(border7);
		Grid grid7 = new Grid();
		grid7.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid7.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		border7.Child = grid7;
		Border border8 = new Border
		{
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Background = Brush("#F3F7FC"),
			BorderBrush = Brush("#E1E9F2"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(999.0),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		statusText = new TextBlock
		{
			Text = "Ready",
			FontSize = 12.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brush("#31597C")
		};
		border8.Child = statusText;
		grid7.Children.Add(border8);
		ScrollViewer scrollViewer2 = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Margin = new Thickness(0.0, 20.0, 0.0, 0.0)
		};
		Grid.SetRow(scrollViewer2, 1);
		StackPanel stackPanel9 = new StackPanel
		{
			Spacing = 20.0
		};
		scrollViewer2.Content = stackPanel9;
		grid7.Children.Add(scrollViewer2);
		detailsPanel = new StackPanel
		{
			Spacing = 14.0,
			Opacity = 1.0
		};
		stackPanel9.Children.Add(detailsPanel);
		detailsPanel.Children.Add(new TextBlock
		{
			Text = "LISTENING NOW",
			FontSize = 11.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brush("#8A98A8"),
			CharacterSpacing = 160
		});
		titleText = new TextBlock
		{
			Text = "Select a station",
			FontSize = 34.0,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.WrapWholeWords
		};
		detailsPanel.Children.Add(titleText);
		subtitleText = new TextBlock
		{
			Text = "Modern radio for Windows",
			Foreground = Brush("#6B7280"),
			FontSize = 14.0
		};
		detailsPanel.Children.Add(subtitleText);
		Border border9 = new Border
		{
			Padding = new Thickness(16.0, 14.0, 16.0, 14.0),
			Background = Brush("#F5F8FC"),
			BorderBrush = Brush("#E4ECF4"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(18.0)
		};
		StackPanel stackPanel4 = new StackPanel
		{
			Spacing = 6.0
		};
		stackPanel4.Children.Add(new TextBlock
		{
			Text = "TRACK",
			FontSize = 11.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brush("#8A98A8"),
			CharacterSpacing = 140
		});
		nowPlayingText = new TextBlock
		{
			Text = "Artist and title will appear here when available.",
			Foreground = Brush("#111827"),
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.WrapWholeWords
		};
		stackPanel4.Children.Add(nowPlayingText);
		border9.Child = stackPanel4;
		detailsPanel.Children.Add(border9);
		Border border10 = new Border
		{
			Padding = new Thickness(16.0, 14.0, 16.0, 14.0),
			Background = Brush("#F8FAFD"),
			BorderBrush = Brush("#E8EEF5"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(18.0)
		};
		StackPanel stackPanel5 = new StackPanel
		{
			Spacing = 10.0
		};
		stackPanel5.Children.Add(new TextBlock
		{
			Text = "LIVE LEVEL",
			FontSize = 11.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brush("#8A98A8"),
			CharacterSpacing = 140
		});
		Grid grid8 = new Grid
		{
			Height = 52.0,
			VerticalAlignment = VerticalAlignment.Bottom
		};
		visualizerBars = new List<Border>();
		for (int i = 0; i < 14; i++)
		{
			grid8.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			if (i < 13)
			{
				grid8.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(6.0)
				});
			}
			Border border11 = new Border
			{
				Width = 10.0,
				Height = 8.0,
				CornerRadius = new CornerRadius(3.0),
				VerticalAlignment = VerticalAlignment.Bottom,
				Background = Brush("#D8E3EF")
			};
			Grid.SetColumn(border11, i * 2);
			grid8.Children.Add(border11);
			visualizerBars.Add(border11);
		}
		stackPanel5.Children.Add(grid8);
		border10.Child = stackPanel5;
		detailsPanel.Children.Add(border10);
		descriptionText = new TextBlock
		{
			Text = "Choose a station on the left to begin.",
			TextWrapping = TextWrapping.WrapWholeWords,
			Foreground = Brush("#4B5563"),
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		detailsPanel.Children.Add(descriptionText);
		favoriteButton = new Button
		{
			Content = "Save favorite",
			Height = 36.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			Padding = new Thickness(16.0, 0.0, 16.0, 0.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Brush("#F5F8FC"),
			Foreground = Brush("#31597C"),
			BorderBrush = Brush("#DCE5EE")
		};
		detailsPanel.Children.Add(favoriteButton);
		StackPanel stackPanel6 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 10.0,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		playButton = new Button
		{
			Content = "Play",
			MinWidth = 120.0,
			Height = 44.0,
			CornerRadius = new CornerRadius(16.0),
			Background = Brush("#111827"),
			Foreground = Brush("#FFFFFF"),
			BorderBrush = Brush("#111827")
		};
		stopButton = new Button
		{
			Content = "Stop",
			MinWidth = 96.0,
			Height = 44.0,
			CornerRadius = new CornerRadius(16.0),
			Background = Brush("#F9FAFB"),
			BorderBrush = Brush("#E5E7EB")
		};
		stackPanel6.Children.Add(playButton);
		stackPanel6.Children.Add(stopButton);
		detailsPanel.Children.Add(stackPanel6);
		StackPanel stackPanel7 = new StackPanel
		{
			Spacing = 10.0
		};
		stackPanel7.Children.Add(new TextBlock
		{
			Text = "Volume",
			FontSize = 12.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brush("#7B8794")
		});
		volumeSlider = new Slider
		{
			Width = 164.0,
			Minimum = 0.0,
			Maximum = 100.0,
			Value = 72.0
		};
		sourceText = new TextBlock
		{
			Text = "Source metadata will appear here.",
			Foreground = Brush("#6B7280"),
			TextWrapping = TextWrapping.WrapWholeWords,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		stackPanel7.Children.Add(volumeSlider);
		stackPanel7.Children.Add(sourceText);
		stackPanel7.Children.Add(new Border
		{
			Height = 1.0,
			Background = Brush("#EDF2F7"),
			Margin = new Thickness(0.0, 6.0, 0.0, 2.0)
		});
		stackPanel7.Children.Add(new TextBlock
		{
			Text = "Library",
			FontSize = 12.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brush("#7B8794")
		});
		savedRegionCombo = new ComboBox
		{
			Height = 36.0,
			PlaceholderText = "No saved regions yet",
			IsEditable = false,
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Background = Brush("#FFFFFF"),
			BorderBrush = Brush("#DCE5EE"),
			BorderThickness = new Thickness(1.0)
		};
		stackPanel7.Children.Add(savedRegionCombo);
		StackPanel stackPanel8 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		saveRegionButton = new Button
		{
			Content = "Save region",
			Height = 32.0,
			Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Brush("#F5F8FC"),
			Foreground = Brush("#31597C"),
			BorderBrush = Brush("#DCE5EE")
		};
		syncButton = new Button
		{
			Content = "Sync now",
			Height = 32.0,
			Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Brush("#FFFFFF"),
			Foreground = Brush("#334155"),
			BorderBrush = Brush("#DCE5EE")
		};
		stackPanel8.Children.Add(saveRegionButton);
		stackPanel8.Children.Add(syncButton);
		stackPanel7.Children.Add(stackPanel8);
		syncStatusText = new TextBlock
		{
			Text = "Ad-free sync mirrors your library to Documents.",
			Foreground = Brush("#7B8794"),
			TextWrapping = TextWrapping.WrapWholeWords,
			FontSize = 12.0
		};
		stackPanel7.Children.Add(syncStatusText);
		stackPanel9.Children.Add(stackPanel7);
		return grid;
	}

	private static Border CardBorder(Thickness margin, Thickness padding)
	{
		return new Border
		{
			Margin = margin,
			Padding = padding,
			Background = new SolidColorBrush(Colors.White)
			{
				Opacity = 0.92
			},
			BorderBrush = Brush("#E5E7EB"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(24.0)
		};
	}

	private static DataTemplate CreateStationTemplate()
	{
		return (DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n    <Border x:Name=\"StationCard\" Margin=\"0\" Padding=\"18,16\" Background=\"#FFFFFF\" BorderBrush=\"#E9EEF5\" BorderThickness=\"1\" CornerRadius=\"18\">\n        <Border.Transitions>\n            <TransitionCollection>\n                <ContentThemeTransition />\n            </TransitionCollection>\n        </Border.Transitions>\n        <StackPanel Spacing=\"10\">\n        <Grid ColumnSpacing=\"18\">\n            <Grid.ColumnDefinitions>\n                <ColumnDefinition Width=\"*\" />\n                <ColumnDefinition Width=\"Auto\" />\n            </Grid.ColumnDefinitions>\n            <StackPanel Spacing=\"5\">\n                <TextBlock x:Name=\"StationName\" FontSize=\"18\" FontWeight=\"SemiBold\" Foreground=\"#1F2937\" Text=\"{Binding Name}\" />\n                <TextBlock Foreground=\"#6B7280\" FontSize=\"13\" Text=\"{Binding Location}\" />\n            </StackPanel>\n            <Border x:Name=\"GenreChip\" Grid.Column=\"1\" Height=\"30\" MinWidth=\"82\" Padding=\"12,0\" Background=\"#F3F5F8\" BorderBrush=\"#EFF2F6\" BorderThickness=\"1\" CornerRadius=\"15\" VerticalAlignment=\"Top\">\n                <TextBlock x:Name=\"GenreChipText\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Foreground=\"#607080\" FontSize=\"12\" FontWeight=\"SemiBold\" Text=\"{Binding Genre}\" />\n            </Border>\n        </Grid>\n        <TextBlock Foreground=\"#4B5563\" TextWrapping=\"WrapWholeWords\" Text=\"{Binding Tagline}\" />\n        <TextBlock Foreground=\"#8A98A8\" FontSize=\"12\" TextWrapping=\"WrapWholeWords\" Text=\"{Binding MetadataSummary}\" />\n        <StackPanel Orientation=\"Horizontal\" Spacing=\"8\">\n            <Border x:Name=\"SourcePill\" Height=\"26\" Padding=\"10,0\" Background=\"#F8FAFC\" BorderBrush=\"#ECF1F5\" BorderThickness=\"1\" CornerRadius=\"13\" VerticalAlignment=\"Center\">\n                <TextBlock x:Name=\"SourcePillText\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Foreground=\"#66788A\" FontSize=\"12\" FontWeight=\"SemiBold\" Text=\"{Binding SourceLabel}\" />\n            </Border>\n            <Border x:Name=\"EditorialPill\" Height=\"26\" Padding=\"10,0\" Background=\"#F8FAFC\" BorderBrush=\"#ECF1F5\" BorderThickness=\"1\" CornerRadius=\"13\" VerticalAlignment=\"Center\">\n                <TextBlock x:Name=\"EditorialPillText\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Foreground=\"#66788A\" FontSize=\"12\" FontWeight=\"SemiBold\" Text=\"{Binding EditorialBadge}\" />\n            </Border>\n        </StackPanel>\n        </StackPanel>\n    </Border>\n</DataTemplate>");
	}

	private static Style CreateStationContainerStyle()
	{
		return (Style)XamlReader.Load("<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ListViewItem\">\n    <Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\" />\n    <Setter Property=\"VerticalContentAlignment\" Value=\"Stretch\" />\n    <Setter Property=\"UseSystemFocusVisuals\" Value=\"False\" />\n    <Setter Property=\"Padding\" Value=\"0\" />\n    <Setter Property=\"Margin\" Value=\"0,0,0,14\" />\n    <Setter Property=\"MinHeight\" Value=\"0\" />\n    <Setter Property=\"Template\">\n        <Setter.Value>\n            <ControlTemplate TargetType=\"ListViewItem\">\n                <Border\n                    x:Name=\"ItemChrome\"\n                    Padding=\"4\"\n                    Background=\"Transparent\"\n                    BorderBrush=\"Transparent\"\n                    BorderThickness=\"0\"\n                    CornerRadius=\"22\">\n                    <ContentPresenter\n                        Content=\"{TemplateBinding Content}\"\n                        ContentTemplate=\"{TemplateBinding ContentTemplate}\"\n                        HorizontalContentAlignment=\"Stretch\"\n                        VerticalContentAlignment=\"{TemplateBinding VerticalContentAlignment}\" />\n                </Border>\n            </ControlTemplate>\n        </Setter.Value>\n    </Setter>\n</Style>");
	}

	private static SolidColorBrush Brush(string hex)
	{
		hex = hex.TrimStart('#');
		byte a = byte.MaxValue;
		int num = 0;
		if (hex.Length == 8)
		{
			a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			num = 2;
		}
		byte r = byte.Parse(hex.Substring(num, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		byte g = byte.Parse(hex.Substring(num + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		byte b = byte.Parse(hex.Substring(num + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		return new SolidColorBrush(Color.FromArgb(a, r, g, b));
	}

}
