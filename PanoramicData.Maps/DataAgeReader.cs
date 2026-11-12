using System.Globalization;
using System.Text.Json;

namespace PanoramicData.Maps;

/// <summary>Reads a dataset's age from a local path or a backend; every failure becomes an unknown reading.</summary>
internal static class DataAgeReader
{
	public static DataAge FromPath(string path, DateTimeOffset now)
	{
		try
		{
			if (File.Exists(path))
			{
				return DataAge.Known(path, File.GetLastWriteTimeUtc(path), now);
			}

			return Directory.Exists(path)
				? DataAge.Known(path, Directory.GetLastWriteTimeUtc(path), now)
				: DataAge.Unknown(path, "path not found");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return DataAge.Unknown(path, $"path not readable: {ex.Message}");
		}
	}

	public static async Task<DataAge> FromLastModifiedAsync(HttpClient http, string url, DateTimeOffset now, CancellationToken ct)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Head, url);
			using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return DataAge.Unknown(url, $"HTTP {(int)response.StatusCode}");
			}

			return response.Content.Headers.LastModified is { } modified
				? DataAge.Known(url, modified, now)
				: DataAge.Unknown(url, "no Last-Modified header");
		}
		catch (Exception ex) when (IsRecoverable(ex, ct))
		{
			return DataAge.Unknown(url, ex.Message);
		}
	}

	public static async Task<DataAge> FromPhotonStatusAsync(HttpClient http, string url, DateTimeOffset now, CancellationToken ct)
	{
		try
		{
			using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return DataAge.Unknown(url, $"HTTP {(int)response.StatusCode}");
			}

			var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			return ParseImportDate(url, json, now);
		}
		catch (Exception ex) when (IsRecoverable(ex, ct))
		{
			return DataAge.Unknown(url, ex.Message);
		}
	}

	private static DataAge ParseImportDate(string url, string json, DateTimeOffset now)
	{
		using var document = JsonDocument.Parse(json);
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty("import_date", out var element)
			|| element.ValueKind != JsonValueKind.String)
		{
			return DataAge.Unknown(url, "status has no import_date");
		}

		return DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var imported)
			? DataAge.Known(url, imported, now)
			: DataAge.Unknown(url, "import_date is not a date");
	}

	// The caller's own cancellation must still propagate; a timeout or transport fault must not.
	private static bool IsRecoverable(Exception ex, CancellationToken ct)
		=> ex is HttpRequestException or JsonException or IOException
			|| (ex is OperationCanceledException && !ct.IsCancellationRequested);
}
