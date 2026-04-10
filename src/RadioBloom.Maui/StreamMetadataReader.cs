using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioBloom.Maui;

internal sealed class StreamMetadataReader : IDisposable
{
	private static readonly HttpClient Client = CreateClient();
	private CancellationTokenSource? _metadataCts;
	private string? _currentTrackInfo;

	public event Action<string?>? TrackInfoChanged;

	public void Start(string url)
	{
		Start([url], null);
	}

	public void Start(IEnumerable<string> urls, string? stationName)
	{
		Stop();
		_currentTrackInfo = string.Empty;
		List<string> candidates = urls
			.Where(url => !string.IsNullOrWhiteSpace(url))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (candidates.Count == 0)
		{
			return;
		}

		_metadataCts = new CancellationTokenSource();
		_ = PollTrackInfoAsync(candidates, stationName, _metadataCts.Token);
	}

	public void Stop()
	{
		if (_metadataCts != null)
		{
			try
			{
				_metadataCts.Cancel();
			}
			catch
			{
			}

			_metadataCts.Dispose();
			_metadataCts = null;
		}

		NotifyTrackInfoChanged(null);
	}

	public void Dispose()
	{
		Stop();
	}

	private async Task PollTrackInfoAsync(IReadOnlyList<string> urls, string? stationName, CancellationToken cancellationToken)
	{
		bool announcedUnavailable = false;

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				string? trackInfo = null;
				foreach (string url in urls)
				{
					trackInfo = await TryReadTrackInfoAsync(url, stationName, cancellationToken);
					if (!string.IsNullOrWhiteSpace(trackInfo))
					{
						break;
					}
				}

				if (!string.IsNullOrWhiteSpace(trackInfo))
				{
					announcedUnavailable = false;
					NotifyTrackInfoChanged(trackInfo);
					await Task.Delay(TimeSpan.FromSeconds(18), cancellationToken);
					continue;
				}

				if (!announcedUnavailable)
				{
					NotifyTrackInfoChanged(null);
					announcedUnavailable = true;
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
				if (!announcedUnavailable)
				{
					NotifyTrackInfoChanged(null);
					announcedUnavailable = true;
				}
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private static async Task<string?> TryReadTrackInfoAsync(string url, string? stationName, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

		using HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();

		if (!TryGetMetaInt(response, out int metaInt) || metaInt <= 0)
		{
			return null;
		}

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		for (int attempt = 0; attempt < 24; attempt++)
		{
			await SkipExactAsync(stream, metaInt, cancellationToken);

			int lengthByte = await ReadByteAsync(stream, cancellationToken);
			if (lengthByte < 0)
			{
				return null;
			}

			if (lengthByte == 0)
			{
				continue;
			}

			int metadataLength = lengthByte * 16;
			byte[] metadataBuffer = await ReadExactAsync(stream, metadataLength, cancellationToken);
			string metadata = DecodeMetadata(metadataBuffer);
			string? title = ParseStreamTitle(metadata);
			if (IsUsefulTrackInfo(title, stationName))
			{
				return title;
			}
		}

		return null;
	}

	private static bool TryGetMetaInt(HttpResponseMessage response, out int value)
	{
		value = 0;
		IEnumerable<string>? values = null;

		if (response.Headers.TryGetValues("icy-metaint", out values) ||
			response.Headers.TryGetValues("Ice-Metaint", out values) ||
			response.Content.Headers.TryGetValues("icy-metaint", out values))
		{
			string? raw = values.FirstOrDefault();
			if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
			{
				value = parsed;
				return true;
			}
		}

		return false;
	}

	private static async Task SkipExactAsync(Stream stream, int byteCount, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[Math.Min(byteCount, 8192)];
		int remaining = byteCount;
		while (remaining > 0)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
			if (read <= 0)
			{
				throw new EndOfStreamException("Unexpected end of stream while skipping audio payload.");
			}

			remaining -= read;
		}
	}

	private static async Task<byte[]> ReadExactAsync(Stream stream, int byteCount, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[byteCount];
		int offset = 0;
		while (offset < byteCount)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(offset, byteCount - offset), cancellationToken);
			if (read <= 0)
			{
				break;
			}

			offset += read;
		}

		return offset == byteCount ? buffer : buffer[..offset];
	}

	private static async Task<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[1];
		int read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
		return read == 1 ? buffer[0] : -1;
	}

	private static string? ParseStreamTitle(string metadata)
	{
		if (string.IsNullOrWhiteSpace(metadata))
		{
			return null;
		}

		Match match = Regex.Match(
			metadata,
			@"StreamTitle\s*=\s*(?:(['""])(?<title>.*?)\1|(?<title>[^;]*))\s*;?",
			RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return null;
		}

		string title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
		return string.IsNullOrWhiteSpace(title) || string.Equals(title, "-", StringComparison.Ordinal)
			? null
			: title;
	}

	private static bool IsUsefulTrackInfo(string? trackInfo, string? stationName)
	{
		if (string.IsNullOrWhiteSpace(trackInfo))
		{
			return false;
		}

		string normalizedTrack = NormalizeForComparison(trackInfo);
		if (normalizedTrack.Length == 0)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(stationName))
		{
			string normalizedStation = NormalizeForComparison(stationName);
			if (normalizedStation.Length > 0 &&
				string.Equals(normalizedTrack, normalizedStation, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return normalizedTrack is not "unknown" and not "onair" and not "live";
	}

	private static string NormalizeForComparison(string value)
	{
		string normalized = value.Normalize(NormalizationForm.FormD);
		StringBuilder builder = new(normalized.Length);
		foreach (char character in normalized)
		{
			UnicodeCategory category = char.GetUnicodeCategory(character);
			if (category == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}

			if (char.IsLetterOrDigit(character))
			{
				builder.Append(char.ToLowerInvariant(character));
			}
		}

		return builder.ToString();
	}

	private static string DecodeMetadata(byte[] metadataBuffer)
	{
		string metadata = Encoding.UTF8.GetString(metadataBuffer).Trim('\0', ' ', '\r', '\n', '\t');
		if (metadata.Contains('\uFFFD'))
		{
			metadata = Encoding.Latin1.GetString(metadataBuffer).Trim('\0', ' ', '\r', '\n', '\t');
		}

		return metadata;
	}

	private void NotifyTrackInfoChanged(string? trackInfo)
	{
		string? normalized = string.IsNullOrWhiteSpace(trackInfo) ? null : trackInfo.Trim();
		if (string.Equals(_currentTrackInfo, normalized, StringComparison.Ordinal))
		{
			return;
		}

		_currentTrackInfo = normalized;
		TrackInfoChanged?.Invoke(normalized);
	}

	private static HttpClient CreateClient()
	{
		HttpClientHandler handler = new()
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};

		HttpClient client = new(handler)
		{
			Timeout = TimeSpan.FromSeconds(12)
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("JustRadio.Maui/0.1");
		return client;
	}
}
