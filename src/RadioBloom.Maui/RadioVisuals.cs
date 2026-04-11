using Microsoft.Maui.Graphics;

namespace RadioBloom.Maui;

internal enum EqualizerMode
{
	Stopped,
	Connecting,
	Live
}

internal sealed class EqualizerDrawable : IDrawable
{
	private readonly Random _random = new();
	private readonly float[] _levels = new float[18];
	private EqualizerMode _mode;
	private double _phase;

	public void SetMode(EqualizerMode mode)
	{
		_mode = mode;
	}

	public void SetLiveLevels(IReadOnlyList<float> levels)
	{
		if (levels.Count == 0)
		{
			return;
		}

		_mode = EqualizerMode.Live;
		for (int i = 0; i < _levels.Length; i++)
		{
			int sourceIndex = (int)Math.Round(i * (levels.Count - 1) / (double)Math.Max(1, _levels.Length - 1));
			float normalized = Math.Clamp(levels[sourceIndex], 0f, 1f);
			float target = 0.08f + (MathF.Pow(normalized, 0.72f) * 0.92f);
			_levels[i] = (_levels[i] * 0.45f) + (target * 0.55f);
		}
	}

	public void Tick()
	{
		_phase += 0.32;
		for (int i = 0; i < _levels.Length; i++)
		{
			float target = _mode switch
			{
				EqualizerMode.Live => (float)Math.Clamp(0.16 + Math.Abs(Math.Sin(_phase + (i * 0.55))) * 0.56 + _random.NextDouble() * 0.22, 0.08, 1.0),
				EqualizerMode.Connecting => (float)(0.16 + ((i + (int)(_phase * 2)) % 5) * 0.08),
				_ => 0.08f
			};
			_levels[i] = (_levels[i] * 0.68f) + (target * 0.32f);
		}
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		canvas.SaveState();
		canvas.FillColor = Color.FromArgb("#F7FBFF");
		canvas.FillRoundedRectangle(dirtyRect, 18);

		float gap = 7;
		float barWidth = Math.Max(5, (dirtyRect.Width - (gap * (_levels.Length - 1))) / _levels.Length);
		float baseline = dirtyRect.Bottom - 12;
		for (int i = 0; i < _levels.Length; i++)
		{
			float height = 8 + (_levels[i] * (dirtyRect.Height - 28));
			float x = dirtyRect.Left + (i * (barWidth + gap));
			float y = baseline - height;
			canvas.FillColor = _mode == EqualizerMode.Live
				? Color.FromArgb(i % 3 == 0 ? "#31597C" : i % 3 == 1 ? "#6E93BE" : "#A8BED7")
				: Color.FromArgb("#CBD9E8");
			canvas.FillRoundedRectangle(x, y, barWidth, height, 3);
		}

		canvas.RestoreState();
	}
}

internal sealed class WorldPositionDrawable : IDrawable
{
	private string _label = "Automatic location";
	private double _latitude = 51.0;
	private double _longitude = 10.0;

	public void SetLocation(string label, double latitude, double longitude)
	{
		_label = label;
		_latitude = Math.Clamp(latitude, -85, 85);
		_longitude = Math.Clamp(longitude, -180, 180);
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		canvas.SaveState();
		canvas.FillColor = Color.FromArgb("#DBEDF7");
		canvas.FillRoundedRectangle(dirtyRect, 18);

		RectF mapRect = dirtyRect.Inflate(-12, -10);
		DrawWorld(canvas, mapRect);
		DrawPin(canvas, mapRect);
		DrawLabel(canvas, dirtyRect);
		canvas.RestoreState();
	}

	private static void DrawWorld(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#BFD3B0");
		DrawBlob(canvas, rect, [(-165, 72), (-130, 70), (-104, 54), (-96, 27), (-112, 12), (-145, 18), (-170, 45)]);
		DrawBlob(canvas, rect, [(-84, 12), (-54, 5), (-39, -17), (-51, -53), (-72, -39), (-81, -12)]);
		DrawBlob(canvas, rect, [(-10, 70), (40, 62), (78, 50), (104, 28), (78, 8), (28, 0), (-8, 20)]);
		DrawBlob(canvas, rect, [(-18, 34), (33, 31), (51, 10), (43, -35), (18, -35), (1, 2)]);
		DrawBlob(canvas, rect, [(70, 28), (128, 44), (150, 24), (135, -8), (92, -10), (76, 8)]);
		DrawBlob(canvas, rect, [(112, -12), (154, -18), (150, -40), (116, -42)]);
		DrawBlob(canvas, rect, [(-48, 75), (-20, 70), (-34, 60), (-58, 62)]);
	}

	private static void DrawBlob(ICanvas canvas, RectF rect, (double Longitude, double Latitude)[] points)
	{
		PathF path = new();
		for (int i = 0; i < points.Length; i++)
		{
			PointF point = Project(rect, points[i].Latitude, points[i].Longitude);
			if (i == 0)
			{
				path.MoveTo(point);
			}
			else
			{
				path.LineTo(point);
			}
		}
		path.Close();
		canvas.FillPath(path);
	}

	private void DrawPin(ICanvas canvas, RectF rect)
	{
		PointF point = Project(rect, _latitude, _longitude);
		canvas.FillColor = Color.FromArgb("#31597C");
		canvas.FillCircle(point.X, point.Y - 5, 7);
		PathF tip = new();
		tip.MoveTo(point.X - 5, point.Y - 1);
		tip.LineTo(point.X + 5, point.Y - 1);
		tip.LineTo(point.X, point.Y + 10);
		tip.Close();
		canvas.FillPath(tip);
		canvas.StrokeColor = Colors.White;
		canvas.StrokeSize = 2;
		canvas.DrawCircle(point.X, point.Y - 5, 8);
	}

	private void DrawLabel(ICanvas canvas, RectF rect)
	{
		canvas.FontSize = 12;
		canvas.FontColor = Color.FromArgb("#31597C");
		canvas.FillColor = Colors.White.WithAlpha(0.88f);
		float width = Math.Min(rect.Width - 28, Math.Max(150, _label.Length * 7.4f));
		canvas.FillRoundedRectangle(rect.Left + 16, rect.Top + 16, width, 30, 15);
		canvas.DrawString(_label, rect.Left + 28, rect.Top + 23, width - 24, 18, HorizontalAlignment.Left, VerticalAlignment.Center);
	}

	private static PointF Project(RectF rect, double latitude, double longitude)
	{
		float x = rect.Left + (float)((longitude + 180.0) / 360.0 * rect.Width);
		float y = rect.Top + (float)((90.0 - latitude) / 180.0 * rect.Height);
		return new PointF(x, y);
	}
}

internal sealed class RegionPreviewFallbackDrawable : IDrawable
{
	private readonly Random _random = new(14);
	private readonly List<(float X1, float Y1, float X2, float Y2, float Width)> _roads = [];
	private string _label = "Selected region";

	public RegionPreviewFallbackDrawable()
	{
		for (int i = 0; i < 32; i++)
		{
			_roads.Add((
				(float)_random.NextDouble(),
				(float)_random.NextDouble(),
				(float)_random.NextDouble(),
				(float)_random.NextDouble(),
				(float)(0.6 + _random.NextDouble() * 1.4)));
		}
	}

	public void SetLocation(string label)
	{
		_label = label;
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		canvas.SaveState();
		canvas.FillColor = Color.FromArgb("#DCEBF4");
		canvas.FillRoundedRectangle(dirtyRect, 16);

		canvas.FillColor = Color.FromArgb("#F7FBFC");
		canvas.FillRoundedRectangle(dirtyRect.Inflate(-10, -8), 12);

		canvas.StrokeColor = Color.FromArgb("#D9E6EE");
		canvas.StrokeSize = 1;
		for (int i = 0; i < 10; i++)
		{
			float y = dirtyRect.Top + 12 + (i * dirtyRect.Height / 9);
			canvas.DrawLine(dirtyRect.Left + 8, y, dirtyRect.Right - 8, y);
		}

		canvas.StrokeColor = Color.FromArgb("#C9D9E3");
		foreach ((float x1, float y1, float x2, float y2, float width) in _roads)
		{
			canvas.StrokeSize = width;
			canvas.DrawLine(
				dirtyRect.Left + (x1 * dirtyRect.Width),
				dirtyRect.Top + (y1 * dirtyRect.Height),
				dirtyRect.Left + (x2 * dirtyRect.Width),
				dirtyRect.Top + (y2 * dirtyRect.Height));
		}

		canvas.FontSize = 12;
		canvas.FontColor = Color.FromArgb("#31597C");
		canvas.FillColor = Colors.White.WithAlpha(0.9f);
		float labelWidth = Math.Min(dirtyRect.Width - 36, Math.Max(150, _label.Length * 7.0f));
		canvas.FillRoundedRectangle(dirtyRect.Left + 18, dirtyRect.Top + 16, labelWidth, 30, 15);
		canvas.DrawString(_label, dirtyRect.Left + 30, dirtyRect.Top + 23, labelWidth - 24, 18, HorizontalAlignment.Left, VerticalAlignment.Center);

		canvas.RestoreState();
	}
}
