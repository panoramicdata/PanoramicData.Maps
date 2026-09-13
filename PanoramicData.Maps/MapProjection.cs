using NetTopologySuite.Geometries;
using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// The projection and pixel extent of the image being drawn - everything needed to turn a geographic
/// coordinate into a device pixel. These six values travel together through every drawing routine;
/// passing them separately gave those routines argument lists long enough to hide a transposition.
/// </summary>
/// <param name="World">The width of the whole world in pixels at this zoom.</param>
/// <param name="Left">World-pixel X of the image's left edge.</param>
/// <param name="Top">World-pixel Y of the image's top edge.</param>
/// <param name="Scale">Device scale factor (1 or 2), which every stroke width and font size multiplies by.</param>
/// <param name="Width">Image width in device pixels.</param>
/// <param name="Height">Image height in device pixels.</param>
internal sealed record Viewport(double World, double Left, double Top, int Scale, int Width, int Height);

/// <summary>
/// Turns NetTopologySuite geometry into SkiaSharp paths in device pixels. This is the one place that
/// knows how a coordinate becomes a pixel, so the base map, the place labels and the caller's overlays
/// cannot drift apart in how they project.
/// </summary>
internal static class MapProjection
{
	/// <summary>Projects a geographic coordinate to a device pixel within the image.</summary>
	public static SKPoint Project(Coordinate c, Viewport viewport)
		=> new(
			(float)(WebMercator.LongitudeToX(c.X, viewport.World) - viewport.Left),
			(float)(WebMercator.LatitudeToY(c.Y, viewport.World) - viewport.Top));

	/// <summary>
	/// Builds a path for a whole geometry, or <see langword="null"/> when there is nothing to draw. The
	/// caller owns the returned path.
	/// </summary>
	public static SKPath? ToPath(Geometry? geometry, Viewport viewport)
	{
		if (geometry is null || geometry.IsEmpty)
		{
			return null;
		}

		using var builder = new SKPathBuilder();
		AddGeometry(builder, geometry, viewport);
		return builder.Detach();
	}

	/// <summary>
	/// Builds a standalone path for one ring or line. SkiaSharp 4 makes <see cref="SKPath"/> immutable,
	/// so geometry is accumulated in a builder and detached once.
	/// </summary>
	public static SKPath BuildLinePath(Coordinate[] coords, Viewport viewport, bool close)
	{
		using var builder = new SKPathBuilder();
		AddLine(builder, coords, viewport, close);
		return builder.Detach();
	}

	private static void AddGeometry(SKPathBuilder builder, Geometry geometry, Viewport viewport)
	{
		switch (geometry)
		{
			case Point pt:
				var sp = Project(pt.Coordinate, viewport);
				builder.AddCircle(sp.X, sp.Y, 2f);
				break;
			case LineString ls:
				AddLine(builder, ls.Coordinates, viewport, close: false);
				break;
			case Polygon poly:
				AddLine(builder, poly.ExteriorRing.Coordinates, viewport, close: true);
				foreach (var hole in poly.InteriorRings)
				{
					AddLine(builder, hole.Coordinates, viewport, close: true);
				}
				break;
			case GeometryCollection gc:
				foreach (var g in gc.Geometries)
				{
					AddGeometry(builder, g, viewport);
				}
				break;
			default:
				// Every geometry type the vector-tile decoder emits is handled above. Anything else
				// contributes nothing to the path rather than throwing: one odd feature in a tile
				// should not cost the caller the whole map.
				break;
		}
	}

	private static void AddLine(SKPathBuilder builder, Coordinate[] coords, Viewport viewport, bool close)
	{
		if (coords.Length == 0)
		{
			return;
		}

		builder.MoveTo(Project(coords[0], viewport));
		for (var i = 1; i < coords.Length; i++)
		{
			builder.LineTo(Project(coords[i], viewport));
		}

		if (close)
		{
			builder.Close();
		}
	}
}
