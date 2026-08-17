namespace PanoramicData.Maps;

/// <summary>
/// Web-Mercator (EPSG:3857) projection helpers, in the "world pixel" convention used by slippy
/// maps: at a given integer zoom the whole world is <c>TileSize * 2^zoom</c> pixels square.
/// </summary>
public static class WebMercator
{
	/// <summary>The tile edge length in pixels used for the world-pixel convention (512, MapLibre-style).</summary>
	public const int TileSize = 512;

	/// <summary>The side length of the whole world, in pixels, at the given zoom.</summary>
	/// <param name="zoom">Zoom level.</param>
	/// <returns>World size in pixels.</returns>
	public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

	/// <summary>Converts a longitude to an absolute world-pixel X at the given world size.</summary>
	/// <param name="longitude">Longitude in degrees.</param>
	/// <param name="worldSize">World size in pixels (see <see cref="WorldSize"/>).</param>
	/// <returns>World-pixel X.</returns>
	public static double LongitudeToX(double longitude, double worldSize)
		=> (longitude + 180.0) / 360.0 * worldSize;

	/// <summary>Converts a latitude to an absolute world-pixel Y at the given world size.</summary>
	/// <param name="latitude">Latitude in degrees (clamped to the Web-Mercator limit ±85.05113°).</param>
	/// <param name="worldSize">World size in pixels (see <see cref="WorldSize"/>).</param>
	/// <returns>World-pixel Y.</returns>
	public static double LatitudeToY(double latitude, double worldSize)
	{
		var lat = Math.Clamp(latitude, -85.05112878, 85.05112878);
		var s = Math.Sin(lat * Math.PI / 180.0);
		return (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * worldSize;
	}

	/// <summary>The maximum tile index (x or y) at the given integer zoom.</summary>
	/// <param name="zoom">Integer zoom level.</param>
	/// <returns><c>2^zoom - 1</c>.</returns>
	public static int MaxTileIndex(int zoom) => (1 << zoom) - 1;
}
