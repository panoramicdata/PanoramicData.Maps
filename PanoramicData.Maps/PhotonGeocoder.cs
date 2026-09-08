using Refit;

namespace PanoramicData.Maps;

/// <summary>
/// An <see cref="IGeocoder"/> backed by a self-hosted Photon instance (komoot/photon).
/// </summary>
public sealed class PhotonGeocoder : IGeocoder
{
	/// <summary>
	/// Resolve the interface's relative paths against the client's base address the way
	/// <see cref="HttpClient"/> itself does (RFC 3986), rather than Refit's legacy mode, which requires
	/// an absolute path and would therefore discard any path prefix in the configured Photon URL.
	/// </summary>
	private static readonly RefitSettings Settings = new() { UrlResolution = UrlResolutionMode.Rfc3986 };

	private readonly IPhotonApi _api;

	/// <summary>
	/// Creates a geocoder over an <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/>
	/// points at the Photon instance.
	/// </summary>
	/// <param name="httpClient">The client to call Photon with, typically from <c>IHttpClientFactory</c>.</param>
	public PhotonGeocoder(HttpClient httpClient) => _api = RestService.For<IPhotonApi>(httpClient, Settings);

	/// <inheritdoc />
	public async Task<GeocodeResult?> GeocodeAsync(string query, string? language = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		// Rewrite a bare country code / colloquial alias ("USA", "UK") to its canonical name so Photon
		// does not rank a tiny same-spelling place above the country (issue #4).
		var effective = Countries.ResolveName(query) ?? query;

		var response = await _api.SearchAsync(effective, 1, Language(language), cancellationToken).ConfigureAwait(false);
		return FirstFeature(response);
	}

	/// <inheritdoc />
	public async Task<GeocodeResult?> ReverseAsync(GeoPoint point, string? language = null, CancellationToken cancellationToken = default)
	{
		var response = await _api.ReverseAsync(point.Longitude, point.Latitude, Language(language), cancellationToken).ConfigureAwait(false);
		return FirstFeature(response);
	}

	/// <summary>Normalises a language to null when blank, so Refit omits the parameter entirely.</summary>
	private static string? Language(string? language)
		=> string.IsNullOrWhiteSpace(language) ? null : language.Trim();

	/// <summary>
	/// Reads the best match. A response with no features, or a first feature carrying no usable point,
	/// is "not found" rather than an error - that is what Photon returns for an unmatched query.
	/// </summary>
	private static GeocodeResult? FirstFeature(PhotonFeatureCollection? response)
	{
		if (response?.Features is not { Count: > 0 } features)
		{
			return null;
		}

		var feature = features[0];
		if (feature.Geometry?.Coordinates is not { Count: >= 2 } coordinates)
		{
			return null;
		}

		var location = new GeoPoint(coordinates[0], coordinates[1]);
		return new GeocodeResult(location, feature.Properties?.Name, feature.Properties?.Country);
	}
}
