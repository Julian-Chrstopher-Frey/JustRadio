using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace RadioBloom.WinUI;

internal sealed class MapPreviewView : Grid
{
    private const double WorldWidth = 2752.0;
    private const double WorldHeight = 1538.0;
    private const double ViewportWidth = 1200.0;
    private const double ViewportHeight = 300.0;
    private const int TilePixelSize = 256;
    private const int TileColumns = 6;
    private const int TileRows = 3;

    private static readonly HttpClient TileClient = CreateTileClient();
    private static readonly ConcurrentDictionary<string, byte[]> TileCache = new(StringComparer.Ordinal);

    private readonly Canvas _artboard;
    private readonly Image _mapImage;
    private readonly Canvas _tileLayer;
    private readonly Grid _fallbackPanel;
    private readonly TextBlock _fallbackText;
    private readonly TextBlock _labelText;
    private readonly Grid _pinHost;
    private int _renderRevision;

    public MapPreviewView()
    {
        Background = Brush("#F7FAFC");

        Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 249, 251, 253), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 243, 247, 250), Offset = 1.0 }
                }
            },
            CornerRadius = new CornerRadius(16)
        });

        Border viewportFrame = new()
        {
            Margin = new Thickness(14, 10, 14, 10),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 240, 245, 249), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 235, 241, 246), Offset = 1.0 }
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(0, 214, 224, 233)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            IsHitTestVisible = false
        };
        Children.Add(viewportFrame);

        Viewbox viewbox = new()
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };

        _artboard = new Canvas
        {
            Width = ViewportWidth,
            Height = ViewportHeight
        };
        _artboard.Clip = new RectangleGeometry
        {
            Rect = new Rect(0.0, 0.0, ViewportWidth, ViewportHeight)
        };
        _artboard.Children.Add(new Border
        {
            Width = ViewportWidth,
            Height = ViewportHeight,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 241, 246, 250), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 233, 239, 245), Offset = 1.0 }
                }
            }
        });

        _mapImage = new Image
        {
            Width = WorldWidth,
            Height = WorldHeight,
            Stretch = Stretch.Fill,
            Opacity = 0.96,
            Source = new BitmapImage(new Uri("ms-appx:///Assets/world-map-equirectangular-hires.png"))
        };
        _artboard.Children.Add(_mapImage);

        _tileLayer = new Canvas
        {
            Width = ViewportWidth,
            Height = ViewportHeight,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _artboard.Children.Add(_tileLayer);

        _artboard.Children.Add(new Border
        {
            Width = ViewportWidth,
            Height = ViewportHeight,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(3, 255, 255, 255), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 0.38 },
                    new GradientStop { Color = Color.FromArgb(2, 255, 255, 255), Offset = 1.0 }
                }
            }
        });

        _pinHost = BuildPin();
        _artboard.Children.Add(_pinHost);
        viewbox.Child = _artboard;
        viewportFrame.Child = viewbox;

        Border labelChip = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 12, 16, 0),
            Padding = new Thickness(12, 7, 12, 7),
            Background = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            BorderBrush = Brush("#D9E4EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999)
        };
        _labelText = new TextBlock
        {
            Foreground = Brush("#37536F"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        labelChip.Child = _labelText;
        Children.Add(labelChip);

        _fallbackText = new TextBlock
        {
            Foreground = Brush("#617689"),
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxWidth = 180
        };
        _fallbackPanel = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(126, 248, 251, 254))
        };
        StackPanel fallbackStack = new()
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        fallbackStack.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            Background = Brush("#2E5D89"),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        fallbackStack.Children.Add(_fallbackText);
        _fallbackPanel.Children.Add(fallbackStack);
        Children.Add(_fallbackPanel);
    }

    public void Render(LocationProfile location, string? placeholder = null)
    {
        _renderRevision++;

        string label = string.IsNullOrWhiteSpace(location.Region)
            ? location.CountryName
            : location.Region + ", " + location.CountryName;
        _labelText.Text = string.IsNullOrWhiteSpace(label) ? "Selected region" : label;

        if (!location.Latitude.HasValue || !location.Longitude.HasValue)
        {
            _tileLayer.Children.Clear();
            _tileLayer.Visibility = Visibility.Collapsed;
            _mapImage.Visibility = Visibility.Collapsed;
            _pinHost.Visibility = Visibility.Collapsed;
            _fallbackText.Text = string.IsNullOrWhiteSpace(placeholder)
                ? "Location unavailable for this selection."
                : placeholder;
            _fallbackPanel.Visibility = Visibility.Visible;
            return;
        }

        _fallbackPanel.Visibility = Visibility.Collapsed;
        _pinHost.Visibility = Visibility.Visible;
        UpdateViewport(location);
        _tileLayer.Visibility = Visibility.Collapsed;
        _ = LoadTilePreviewAsync(location, _renderRevision);
    }

    private void UpdateViewport(LocationProfile location)
    {
        _mapImage.Visibility = Visibility.Visible;

        double zoom = GetZoom(location);
        double scaledWidth = WorldWidth * zoom;
        double scaledHeight = WorldHeight * zoom;
        _mapImage.Width = scaledWidth;
        _mapImage.Height = scaledHeight;

        double sourceX = ((NormalizeLongitude(location.Longitude!.Value) + 180.0) / 360.0) * WorldWidth;
        double sourceY = ((90.0 - Math.Clamp(location.Latitude!.Value, -90.0, 90.0)) / 180.0) * WorldHeight;
        double scaledX = sourceX * zoom;
        double scaledY = sourceY * zoom;

        double targetX = ViewportWidth * 0.52;
        double targetY = ViewportHeight * 0.57;
        double left = targetX - scaledX;
        double top = targetY - scaledY;

        double minLeft = ViewportWidth - scaledWidth;
        double minTop = ViewportHeight - scaledHeight;
        left = Math.Clamp(left, minLeft, 0.0);
        top = Math.Clamp(top, minTop, 0.0);

        Canvas.SetLeft(_mapImage, left);
        Canvas.SetTop(_mapImage, top);

        double pinX = Math.Clamp(left + scaledX, 24.0, ViewportWidth - 24.0);
        double pinY = Math.Clamp(top + scaledY, 18.0, ViewportHeight - 18.0);
        Canvas.SetLeft(_pinHost, pinX - 15.0);
        Canvas.SetTop(_pinHost, pinY - 28.0);
    }

    private async Task LoadTilePreviewAsync(LocationProfile location, int revision)
    {
        TileRenderPlan? plan = await TryBuildTilePlanAsync(location).ConfigureAwait(false);
        if (plan is null || revision != _renderRevision)
        {
            return;
        }

        await EnqueueAsync(async () =>
        {
            if (revision != _renderRevision)
            {
                return;
            }

            await ApplyTilePlanAsync(plan).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    private async Task ApplyTilePlanAsync(TileRenderPlan plan)
    {
        _tileLayer.Children.Clear();

        foreach (TileSprite tile in plan.Tiles)
        {
            BitmapImage bitmap = new();
            using InMemoryRandomAccessStream stream = new();
            await stream.WriteAsync(tile.Bytes.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);

            Image image = new()
            {
                Width = TilePixelSize,
                Height = TilePixelSize,
                Stretch = Stretch.Fill,
                Opacity = 1.0,
                Source = bitmap
            };
            Canvas.SetLeft(image, tile.Left);
            Canvas.SetTop(image, tile.Top);
            _tileLayer.Children.Add(image);
        }

        _tileLayer.Visibility = Visibility.Visible;
        _mapImage.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_pinHost, plan.PinX - 15.0);
        Canvas.SetTop(_pinHost, plan.PinY - 28.0);
    }

    private static async Task<TileRenderPlan?> TryBuildTilePlanAsync(LocationProfile location)
    {
        if (!location.Latitude.HasValue || !location.Longitude.HasValue)
        {
            return null;
        }

        try
        {
            int zoom = GetTileZoom(location);
            double latitude = Math.Clamp(location.Latitude.Value, -85.05112878, 85.05112878);
            double longitude = NormalizeLongitude(location.Longitude.Value);
            double scale = Math.Pow(2.0, zoom);
            double tileX = ((longitude + 180.0) / 360.0) * scale;
            double latitudeRad = latitude * Math.PI / 180.0;
            double tileY = (1.0 - Math.Log(Math.Tan(latitudeRad) + 1.0 / Math.Cos(latitudeRad)) / Math.PI) * 0.5 * scale;

            int startTileX = (int)Math.Floor(tileX - (TileColumns / 2.0));
            int startTileY = Math.Clamp((int)Math.Floor(tileY - (TileRows / 2.0)), 0, Math.Max(0, (int)scale - TileRows));
            double targetX = ViewportWidth * 0.54;
            double targetY = ViewportHeight * 0.52;
            double localX = (tileX - startTileX) * TilePixelSize;
            double localY = (tileY - startTileY) * TilePixelSize;
            double leftOffset = targetX - localX;
            double topOffset = targetY - localY;

            List<(string Url, double Left, double Top)> requests = new(TileColumns * TileRows);
            for (int row = 0; row < TileRows; row++)
            {
                int tileRow = Math.Clamp(startTileY + row, 0, (int)scale - 1);
                for (int column = 0; column < TileColumns; column++)
                {
                    int rawTileColumn = startTileX + column;
                    int tileColumn = Mod(rawTileColumn, (int)scale);
                    string subdomain = ((tileColumn + tileRow) % 3) switch
                    {
                        0 => "a",
                        1 => "b",
                        _ => "c"
                    };
                    requests.Add((
                        $"https://{subdomain}.basemaps.cartocdn.com/rastertiles/voyager_nolabels/{zoom}/{tileColumn}/{tileRow}@2x.png",
                        leftOffset + (column * TilePixelSize),
                        topOffset + (row * TilePixelSize)));
                }
            }

            List<Task<TileSprite>> tasks = new(requests.Count);
            foreach ((string url, double left, double top) in requests)
            {
                tasks.Add(CreateTileSpriteAsync(url, left, top));
            }

            TileSprite[] tiles = await Task.WhenAll(tasks).ConfigureAwait(false);
            return new TileRenderPlan(tiles, targetX, targetY);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<TileSprite> CreateTileSpriteAsync(string url, double left, double top)
    {
        byte[] bytes = await GetTileBytesAsync(url).ConfigureAwait(false);
        return new TileSprite(bytes, left, top);
    }

    private static async Task<byte[]> GetTileBytesAsync(string url)
    {
        if (TileCache.TryGetValue(url, out byte[]? cached))
        {
            return cached;
        }

        byte[] bytes = await TileClient.GetByteArrayAsync(url).ConfigureAwait(false);
        TileCache[url] = bytes;
        return bytes;
    }

    private Task EnqueueAsync(Func<Task> action)
    {
        TaskCompletionSource<object?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            tcs.TrySetException(new InvalidOperationException("Unable to enqueue UI work."));
        }

        return tcs.Task;
    }

    private static double GetZoom(LocationProfile location)
    {
        bool hasRegion = !string.IsNullOrWhiteSpace(location.Region);
        string countryCode = (location.CountryCode ?? string.Empty).ToUpperInvariant();

        return countryCode switch
        {
            "AU" or "US" or "CA" or "BR" or "RU" or "CN" or "IN" or "AR" or "MX" => hasRegion ? 1.85 : 1.55,
            "ID" or "JP" or "NZ" or "NO" or "SE" or "FI" or "ZA" => hasRegion ? 2.0 : 1.7,
            "DE" or "FR" or "IT" or "ES" or "GB" or "GR" or "NL" or "BE" or "CH" or "AT" or "PT" or "PL" or "CZ" or "HU" => hasRegion ? 2.45 : 2.0,
            _ => hasRegion ? 2.15 : 1.8
        };
    }

    private static int GetTileZoom(LocationProfile location)
    {
        bool hasRegion = !string.IsNullOrWhiteSpace(location.Region);
        string countryCode = (location.CountryCode ?? string.Empty).ToUpperInvariant();

        return countryCode switch
        {
            "DE" or "FR" or "IT" or "ES" or "GB" or "GR" or "NL" or "BE" or "CH" or "AT" or "PT" or "PL" or "CZ" or "HU" => hasRegion ? 4 : 3,
            "JP" or "NZ" or "NO" or "SE" or "FI" or "ZA" or "IE" or "DK" => hasRegion ? 4 : 3,
            "US" or "CA" or "BR" or "RU" or "CN" or "IN" or "AR" or "MX" => hasRegion ? 4 : 3,
            "AU" => hasRegion ? 4 : 3,
            _ => hasRegion ? 4 : 3
        };
    }

    private static Grid BuildPin()
    {
        Grid pin = new()
        {
            Width = 30,
            Height = 36,
            IsHitTestVisible = false
        };

        pin.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            Background = Brush("#275B8D"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(236, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12, 12, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Shadow = new ThemeShadow(),
            RenderTransform = new RotateTransform { Angle = -45 },
            Margin = new Thickness(0, 4, 0, 0)
        });

        pin.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            Background = Brush("#FFFFFF"),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 0, 0)
        });

        return pin;
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

    private static HttpClient CreateTileClient()
    {
        HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RadioBloom.WinUI/1.0");
        return client;
    }

    private static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        int offset = 0;
        if (hex.Length == 8)
        {
            a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            offset = 2;
        }

        byte r = byte.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(hex.Substring(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(hex.Substring(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    private sealed record TileRenderPlan(IReadOnlyList<TileSprite> Tiles, double PinX, double PinY);

    private sealed record TileSprite(byte[] Bytes, double Left, double Top);
}
