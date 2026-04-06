using System;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RadioBloom.WinUI;

internal sealed class WeatherSummaryView : Grid
{
    private readonly Border _accentChip;
    private readonly TextBlock _accentText;
    private readonly TextBlock _temperatureText;
    private readonly TextBlock _conditionText;
    private readonly TextBlock _rangeText;
    private readonly TextBlock _detailsText;
    private readonly TextBlock _timeText;

    public WeatherSummaryView()
    {
        Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(255, 245, 249, 253), Offset = 0.0 },
                new GradientStop { Color = Color.FromArgb(255, 236, 243, 249), Offset = 1.0 }
            }
        };

        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0)
        });

        StackPanel stack = new()
        {
            Spacing = 10,
            Padding = new Thickness(18, 16, 18, 16)
        };

        Grid topRow = new();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.Children.Add(new TextBlock
        {
            Text = "CURRENT",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#8A98A8"),
            CharacterSpacing = 140
        });

        _accentChip = new Border
        {
            Background = Brush("#F0F6FC"),
            BorderBrush = Brush("#D8E5F1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _accentText = new TextBlock
        {
            Text = "Loading",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#45637E")
        };
        _accentChip.Child = _accentText;
        Grid.SetColumn(_accentChip, 1);
        topRow.Children.Add(_accentChip);
        stack.Children.Add(topRow);

        _temperatureText = new TextBlock
        {
            Text = "--",
            FontSize = 42,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#152433")
        };
        stack.Children.Add(_temperatureText);

        _conditionText = new TextBlock
        {
            Text = "Loading local weather...",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#334155")
        };
        stack.Children.Add(_conditionText);

        _rangeText = new TextBlock
        {
            Text = "High --  Low --",
            FontSize = 13,
            Foreground = Brush("#5B6B7C")
        };
        stack.Children.Add(_rangeText);

        _detailsText = new TextBlock
        {
            Text = "Wind --  Feels like --",
            FontSize = 13,
            Foreground = Brush("#5B6B7C")
        };
        stack.Children.Add(_detailsText);

        _timeText = new TextBlock
        {
            Text = "Waiting for coordinates",
            FontSize = 12,
            Foreground = Brush("#8A98A8"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(_timeText);

        Children.Add(stack);
    }

    public void SetLoading(LocationProfile location)
    {
        _accentText.Text = string.IsNullOrWhiteSpace(location.Source) ? "Syncing" : "Syncing";
        _temperatureText.Text = "--";
        _conditionText.Text = "Loading local weather...";
        _rangeText.Text = BuildLocationLine(location);
        _detailsText.Text = "Wind --  Feels like --";
        _timeText.Text = "Fetching conditions";
    }

    public void Render(LocationProfile location, WeatherSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            _accentText.Text = "Offline";
            _temperatureText.Text = "--";
            _conditionText.Text = "Weather unavailable";
            _rangeText.Text = BuildLocationLine(location);
            _detailsText.Text = "No live conditions returned";
            _timeText.Text = string.IsNullOrWhiteSpace(location.Source) ? "Location selected" : location.Source;
            return;
        }

        _accentText.Text = snapshot.IsDay ? "Daylight" : "Night";
        _temperatureText.Text = Math.Round(snapshot.TemperatureC).ToString(CultureInfo.InvariantCulture) + "°";
        _conditionText.Text = snapshot.ConditionLabel;
        _rangeText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "High {0}°  Low {1}°",
            FormatTemperature(snapshot.HighC),
            FormatTemperature(snapshot.LowC));

        string humidityPart = snapshot.RelativeHumidity.HasValue
            ? snapshot.RelativeHumidity.Value.ToString(CultureInfo.InvariantCulture) + "% humidity"
            : "Humidity --";
        _detailsText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Wind {0:0} km/h  Feels like {1}°  {2}",
            snapshot.WindSpeedKmh,
            Math.Round(snapshot.ApparentTemperatureC),
            humidityPart);

        string source = string.IsNullOrWhiteSpace(location.Source) ? "Selected region" : location.Source;
        _timeText.Text = snapshot.LocalTimeLabel + "  |  " + source;
    }

    private static string BuildLocationLine(LocationProfile location)
    {
        if (!string.IsNullOrWhiteSpace(location.Region))
        {
            return location.Region + ", " + location.CountryName;
        }

        return location.CountryName;
    }

    private static string FormatTemperature(double? value)
    {
        return value.HasValue
            ? Math.Round(value.Value).ToString(CultureInfo.InvariantCulture)
            : "--";
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
}
