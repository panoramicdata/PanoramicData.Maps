namespace PanoramicData.Maps;

/// <summary>
/// A single geocoding result.
/// </summary>
/// <param name="Location">The resolved coordinate.</param>
/// <param name="Name">The place name, if any.</param>
/// <param name="Country">The country, if any.</param>
public sealed record GeocodeResult(GeoPoint Location, string? Name, string? Country);

/// <summary>
/// What to forward-geocode.
/// </summary>
/// <param name="Query">The place/address text.</param>
/// <param name="Language">
/// Result language ("en", "de", "fr", "it") passed to the backend so names come back in a consistent
/// script, or <see langword="null"/> to use the backend default.
/// </param>
public sealed record GeocodeRequest(string Query, string? Language);

/// <summary>
/// What to reverse-geocode.
/// </summary>
/// <param name="Location">The coordinate to resolve.</param>
/// <param name="Language">Result language, or <see langword="null"/> (see <see cref="GeocodeRequest.Language"/>).</param>
public sealed record ReverseGeocodeRequest(GeoPoint Location, string? Language);

/// <summary>
/// Resolves place names to coordinates and vice versa.
/// <para>
/// Both calls take a request object and an explicit cancellation token rather than a list of optional
/// parameters, so adding a knob later does not change the signature and every caller is deliberate
/// about cancellation.
/// </para>
/// </summary>
public interface IGeocoder
{
	/// <summary>
	/// Forward-geocodes a free-text query to its best match.
	/// </summary>
	/// <param name="request">What to look up.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The best match, or <see langword="null"/> if nothing was found.</returns>
	Task<GeocodeResult?> GeocodeAsync(GeocodeRequest request, CancellationToken cancellationToken);

	/// <summary>
	/// Reverse-geocodes a coordinate to the nearest place.
	/// </summary>
	/// <param name="request">Which coordinate to resolve.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The nearest place, or <see langword="null"/> if nothing was found.</returns>
	Task<GeocodeResult?> ReverseAsync(ReverseGeocodeRequest request, CancellationToken cancellationToken);
}
