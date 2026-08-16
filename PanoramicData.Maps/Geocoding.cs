namespace PanoramicData.Maps;

/// <summary>
/// A single geocoding result.
/// </summary>
/// <param name="Location">The resolved coordinate.</param>
/// <param name="Name">The place name, if any.</param>
/// <param name="Country">The country, if any.</param>
public sealed record GeocodeResult(GeoPoint Location, string? Name, string? Country);

/// <summary>
/// Resolves place names to coordinates and vice versa.
/// </summary>
public interface IGeocoder
{
	/// <summary>
	/// Forward-geocodes a free-text query to its best match.
	/// </summary>
	/// <param name="query">The place/address text.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The best match, or <see langword="null"/> if nothing was found.</returns>
	Task<GeocodeResult?> GeocodeAsync(string query, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reverse-geocodes a coordinate to the nearest place.
	/// </summary>
	/// <param name="point">The coordinate.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The nearest place, or <see langword="null"/> if nothing was found.</returns>
	Task<GeocodeResult?> ReverseAsync(GeoPoint point, CancellationToken cancellationToken = default);
}
