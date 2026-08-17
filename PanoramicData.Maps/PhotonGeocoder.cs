using System.Globalization;
using System.Text.Json;

namespace PanoramicData.Maps;

/// <summary>
/// An <see cref="IGeocoder"/> backed by a self-hosted Photon instance (komoot/photon).
/// </summary>
public sealed class PhotonGeocoder(HttpClient httpClient) : IGeocoder
{
	private readonly HttpClient _httpClient = httpClient;

	/// <inheritdoc />
	public async Task<GeocodeResult?> GeocodeAsync(string query, string? language = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		// Rewrite a bare country code / colloquial alias ("USA", "UK") to its canonical name so Photon
		// does not rank a tiny same-spelling place above the country (issue #4).
		var effective = Countries.ResolveName(query) ?? query;

		var url = $"api?q={Uri.EscapeDataString(effective)}&limit=1{LangSuffix(language)}";
		return await FirstFeatureAsync(url, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<GeocodeResult?> ReverseAsync(GeoPoint point, string? language = null, CancellationToken cancellationToken = default)
	{
		var lon = point.Longitude.ToString(CultureInfo.InvariantCulture);
		var lat = point.Latitude.ToString(CultureInfo.InvariantCulture);
		return await FirstFeatureAsync($"reverse?lon={lon}&lat={lat}{LangSuffix(language)}", cancellationToken).ConfigureAwait(false);
	}

	private static string LangSuffix(string? language)
		=> string.IsNullOrWhiteSpace(language) ? string.Empty : $"&lang={Uri.EscapeDataString(language.Trim())}";

	private async Task<GeocodeResult?> FirstFeatureAsync(string relativeUrl, CancellationToken cancellationToken)
	{
		using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		using var doc = JsonDocument.Parse(bytes);

		if (!doc.RootElement.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
		{
			return null;
		}

		var feature = features[0];
		var coords = feature.GetProperty("geometry").GetProperty("coordinates");
		var location = new GeoPoint(coords[0].GetDouble(), coords[1].GetDouble());

		string? name = null;
		string? country = null;
		if (feature.TryGetProperty("properties", out var props))
		{
			if (props.TryGetProperty("name", out var n))
			{
				name = n.GetString();
			}

			if (props.TryGetProperty("country", out var c))
			{
				country = c.GetString();
			}
		}

		return new GeocodeResult(location, name, country);
	}
}
