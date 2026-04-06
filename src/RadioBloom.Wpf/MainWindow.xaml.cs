using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RadioBloom.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly NativeAudioPlayer _player;
        private readonly List<Button> _categoryButtons;

        private List<RadioStation> _stations;
        private LocationProfile _location;
        private string _catalogMode;
        private string _selectedCategory;
        private RadioStation _selectedStation;
        private string _activeStationId;
        private NativePlayerState _playerState;

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _timer.Tick += OnTimerTick;

            _player = new NativeAudioPlayer();
            _categoryButtons = new List<Button>();
            _stations = new List<RadioStation>();
            _selectedCategory = "All";
            _catalogMode = "Curated";
            _activeStationId = null;
            _playerState = NativePlayerState.Ready;

            SearchBox.TextChanged += delegate { RefreshStationList(); };
            StationList.SelectionChanged += OnSelectionChanged;
            PlayButton.Click += delegate { TogglePlayback(); };
            StopButton.Click += delegate { StopPlayback(); };
            VolumeSlider.ValueChanged += delegate { _player.SetVolume(VolumeSlider.Value / 100.0); };

            Loaded += async delegate
            {
                ApplyCatalog(StationCatalogService.GetFallbackStations(), StationCatalogService.GetDefaultLocation(), "Curated");
                await LoadNearbyStationsAsync();
            };

            Closing += delegate
            {
                _timer.Stop();
                _player.Dispose();
            };
        }

        private async Task LoadNearbyStationsAsync()
        {
            await Task.Run(delegate
            {
                LocationProfile location = StationCatalogService.GetApproximateLocation();
                List<RadioStation> stations = StationCatalogService.LoadStations(location);
                string mode = string.Equals(location.CountryCode, "DE", StringComparison.OrdinalIgnoreCase) ? "Germany" : "Nearby";
                Dispatcher.Invoke(delegate { ApplyCatalog(stations, location, mode); });
            });
        }

        private void ApplyCatalog(List<RadioStation> stations, LocationProfile location, string mode)
        {
            _stations = stations ?? new List<RadioStation>();
            _location = location ?? StationCatalogService.GetDefaultLocation();
            _catalogMode = mode ?? "Curated";
            _selectedCategory = "All";
            _selectedStation = _stations.FirstOrDefault(s => s.Featured) ?? _stations.FirstOrDefault();

            BuildCategoryButtons();
            RefreshStationList();
            UpdateDetails();
        }

        private void BuildCategoryButtons()
        {
            CategoryPanel.Children.Clear();
            _categoryButtons.Clear();

            foreach (string categoryName in StationCatalogService.BuildCategoryList(_stations))
            {
                Button button = new Button
                {
                    Content = categoryName,
                    Style = (Style)FindResource("FilterButtonStyle")
                };

                button.Click += delegate
                {
                    _selectedCategory = categoryName;
                    UpdateCategoryStyles();
                    RefreshStationList();
                };

                _categoryButtons.Add(button);
                CategoryPanel.Children.Add(button);
            }

            UpdateCategoryStyles();
        }

        private void UpdateCategoryStyles()
        {
            foreach (Button button in _categoryButtons)
            {
                bool active = string.Equals(Convert.ToString(button.Content, CultureInfo.InvariantCulture), _selectedCategory, StringComparison.OrdinalIgnoreCase);
                button.Background = active ? Brush("#111827") : Brush("#F3F4F6");
                button.Foreground = active ? Brushes.White : Brush("#374151");
                button.BorderBrush = active ? Brush("#111827") : Brush("#E5E7EB");
            }
        }

        private void RefreshStationList()
        {
            IEnumerable<RadioStation> items = _stations;
            string needle = (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();

            if (!string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(_selectedCategory, "Featured", StringComparison.OrdinalIgnoreCase))
                {
                    items = items.Where(s => s.Featured);
                }
                else
                {
                    items = items.Where(s => s.Categories != null && s.Categories.Contains(_selectedCategory));
                }
            }

            if (!string.IsNullOrWhiteSpace(needle))
            {
                items = items.Where(delegate (RadioStation station)
                {
                    string surface = string.Join(" ", new[]
                    {
                        station.Name,
                        station.Subtitle,
                        station.Location,
                        station.Genre,
                        station.Tagline,
                        station.Description
                    }).ToLowerInvariant();

                    return surface.Contains(needle);
                });
            }

            List<RadioStation> filtered = items.OrderByDescending(s => s.Featured).ThenBy(s => s.Name).ToList();
            StationList.ItemsSource = filtered;
            StationList.SelectedItem = _selectedStation;

            string categoryLabel = string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase)
                ? "all stations"
                : _selectedCategory.ToLowerInvariant();

            if (string.Equals(_catalogMode, "Germany", StringComparison.OrdinalIgnoreCase))
            {
                SummaryText.Text = filtered.Count.ToString(CultureInfo.InvariantCulture) + " stations across Germany in " + categoryLabel + ".";
            }
            else if (string.Equals(_catalogMode, "Nearby", StringComparison.OrdinalIgnoreCase))
            {
                SummaryText.Text = filtered.Count.ToString(CultureInfo.InvariantCulture) + " nearby stations for " + _location.Label + " in " + categoryLabel + ".";
            }
            else
            {
                SummaryText.Text = filtered.Count.ToString(CultureInfo.InvariantCulture) + " curated stations in " + categoryLabel + ".";
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RadioStation station = StationList.SelectedItem as RadioStation;
            if (station == null)
            {
                return;
            }

            _selectedStation = station;
            UpdateDetails();

            if (!string.Equals(_activeStationId, _selectedStation.Id, StringComparison.OrdinalIgnoreCase) &&
                (_playerState == NativePlayerState.Live || _playerState == NativePlayerState.Connecting || _playerState == NativePlayerState.Paused))
            {
                StartSelectedStation();
            }
        }

        private void UpdateDetails()
        {
            if (_selectedStation == null)
            {
                TitleText.Text = "Select a station";
                SubtitleText.Text = "Minimal radio for Windows";
                DescriptionText.Text = "Choose a station on the left to begin.";
                SourceText.Text = "Source metadata will appear here.";
                RefreshTransport();
                return;
            }

            TitleText.Text = _selectedStation.Name;
            SubtitleText.Text = _selectedStation.Genre + " | " + _selectedStation.Location;
            DescriptionText.Text = _selectedStation.Description;
            SourceText.Text = _selectedStation.SourceLabel + " | " + _selectedStation.Quality;
            RefreshTransport();
        }

        private void TogglePlayback()
        {
            if (_selectedStation == null)
            {
                return;
            }

            try
            {
                bool sameStation = string.Equals(_activeStationId, _selectedStation.Id, StringComparison.OrdinalIgnoreCase);

                if (!sameStation)
                {
                    StartSelectedStation();
                }
                else if (_playerState == NativePlayerState.Live || _playerState == NativePlayerState.Connecting)
                {
                    _player.Pause();
                }
                else if (_playerState == NativePlayerState.Paused)
                {
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
                _player.Stop();
                _activeStationId = null;
                _playerState = NativePlayerState.Stopped;
            }
            catch
            {
                _playerState = NativePlayerState.Error;
            }

            RefreshTransport();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            RefreshTransport();
        }

        private void StartSelectedStation()
        {
            if (_selectedStation == null || string.IsNullOrWhiteSpace(_selectedStation.StreamUrl))
            {
                return;
            }

            _player.Open(_selectedStation.StreamUrl);
            _player.Play();
            _activeStationId = _selectedStation.Id;
            _playerState = NativePlayerState.Connecting;
            _timer.Start();
        }

        private void RefreshTransport()
        {
            if (_playerState != NativePlayerState.Error)
            {
                _playerState = _player.GetState();
            }

            StatusText.Text = _playerState == NativePlayerState.Connecting ? "Connecting" : _playerState.ToString();
            PlayButton.Content = _playerState == NativePlayerState.Live || _playerState == NativePlayerState.Connecting
                ? "Pause"
                : (_playerState == NativePlayerState.Paused ? "Resume" : "Play");
            StopButton.IsEnabled = _playerState == NativePlayerState.Live || _playerState == NativePlayerState.Paused || _playerState == NativePlayerState.Connecting;

            if (_playerState == NativePlayerState.Ready || _playerState == NativePlayerState.Stopped || _playerState == NativePlayerState.Error)
            {
                _activeStationId = null;
            }
        }

        private static SolidColorBrush Brush(string color)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(color);
        }
    }
}
