using System.Text.Json.Serialization;
using Refit;

namespace PanoramicData.Maps;

/// <summary>
/// The subset of the Photon (komoot/photon) HTTP API this library uses, as a typed Refit interface.
/// <para>
/// Declaring the calls rather than assembling URLs by hand is what keeps query construction honest:
/// Refit escapes every value and omits a null one, so a null <c>lang</c> disappears from the query
/// instead of arriving as the literal text "lang=".
/// </para>
/// <para>
/// Paths are relative, so a <see cref="HttpClient.BaseAddress"/> carrying a path prefix - a Photon
/// behind a reverse proxy at <c>/geocoder/</c>, say - keeps that prefix.
/// </para>
/// </summary>
internal interface IPhotonApi
{
	/// <summary>Forward-geocodes free text.</summary>
	[Get("api")]
	Task<PhotonFeatureCollection> SearchAsync(
		[AliasAs("q")] string query,
		[AliasAs("limit")] int limit,
		[AliasAs("lang")] string? language,
		CancellationToken cancellationToken);

	/// <summary>Reverse-geocodes a coordinate.</summary>
	[Get("reverse")]
	Task<PhotonFeatureCollection> ReverseAsync(
		[AliasAs("lon")] double longitude,
		[AliasAs("lat")] double latitude,
		[AliasAs("lang")] string? language,
		CancellationToken cancellationToken);
}

/// <summary>A GeoJSON feature collection as returned by Photon.</summary>
/// <param name="Features">The matches, best first. Absent or empty when nothing was found.</param>
internal sealed record PhotonFeatureCollection(
	[property: JsonPropertyName("features")] IReadOnlyList<PhotonFeature>? Features);

/// <summary>One GeoJSON feature.</summary>
/// <param name="Geometry">The feature's geometry; only points are expected here.</param>
/// <param name="Properties">The feature's attributes.</param>
internal sealed record PhotonFeature(
	[property: JsonPropertyName("geometry")] PhotonGeometry? Geometry,
	[property: JsonPropertyName("properties")] PhotonProperties? Properties);

/// <summary>A GeoJSON geometry.</summary>
/// <param name="Coordinates">Coordinates in GeoJSON order - longitude first, then latitude.</param>
internal sealed record PhotonGeometry(
	[property: JsonPropertyName("coordinates")] IReadOnlyList<double>? Coordinates);

/// <summary>The attributes Photon returns that this library reads.</summary>
/// <param name="Name">The place name.</param>
/// <param name="Country">The country the place is in.</param>
internal sealed record PhotonProperties(
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("country")] string? Country);
