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
	public async Task<GeocodeResult?> GeocodeAsync(string query, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);
		var url = $"api?q={Uri.EscapeDataString(query)}&limit=1";
		return await FirstFeatureAsync(url, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<GeocodeResult?> ReverseAsync(GeoPoint point, CancellationToken cancellationToken = default)
	{
		var lon = point.Longitude.ToString(CultureInfo.InvariantCulture);
		var lat = point.Latitude.ToString(CultureInfo.InvariantCulture);
		return await FirstFeatureAsync($"reverse?lon={lon}&lat={lat}", cancellationToken).ConfigureAwait(false);
	}

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
