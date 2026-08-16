namespace PanoramicData.Maps;

/// <summary>
/// A geographic coordinate in WGS84 decimal degrees.
/// </summary>
/// <param name="Longitude">Longitude in decimal degrees (-180 to 180).</param>
/// <param name="Latitude">Latitude in decimal degrees (-90 to 90).</param>
public readonly record struct GeoPoint(double Longitude, double Latitude);
