namespace PanoramicData.Maps;

/// <summary>
/// Output image format for a rendered map.
/// </summary>
public enum MapImageFormat
{
	/// <summary>PNG (lossless, supports transparency).</summary>
	Png,

	/// <summary>JPEG (smaller, lossy).</summary>
	Jpeg
}

/// <summary>
/// A marker ("pin") to draw on the map.
/// </summary>
public sealed class MarkerSpec
{
	/// <summary>The marker location.</summary>
	public required GeoPoint Location { get; init; }

	/// <summary>CSS colour of the marker (e.g. <c>#dc2626</c> or <c>red</c>).</summary>
	public string Color { get; init; } = "#dc2626";

	/// <summary>Optional short label drawn on/near the marker.</summary>
	public string? Label { get; init; }

	/// <summary>Optional named icon (from the tile style's sprite sheet) to use instead of a plain pin.</summary>
	public string? Icon { get; init; }

	/// <summary>Relative marker scale (1.0 = default).</summary>
	public double Scale { get; init; } = 1.0;
}

/// <summary>
/// A polyline overlay (e.g. a route).
/// </summary>
public sealed class PathSpec
{
	/// <summary>Ordered points of the line.</summary>
	public required IReadOnlyList<GeoPoint> Points { get; init; }

	/// <summary>CSS stroke colour.</summary>
	public string Color { get; init; } = "#7c3aed";

	/// <summary>Stroke width in pixels.</summary>
	public double Width { get; init; } = 4;

	/// <summary>Stroke opacity (0-1).</summary>
	public double Opacity { get; init; } = 0.85;
}

/// <summary>
/// A polygon (filled area) overlay.
/// </summary>
public sealed class PolygonSpec
{
	/// <summary>Ordered vertices of the polygon's outer ring.</summary>
	public required IReadOnlyList<GeoPoint> Points { get; init; }

	/// <summary>CSS fill colour.</summary>
	public string FillColor { get; init; } = "#f59e0b";

	/// <summary>Fill opacity (0-1).</summary>
	public double FillOpacity { get; init; } = 0.2;

	/// <summary>CSS stroke colour.</summary>
	public string StrokeColor { get; init; } = "#f59e0b";

	/// <summary>Stroke width in pixels.</summary>
	public double StrokeWidth { get; init; } = 2;
}

/// <summary>
/// A named region (country/state) to shade, with the geometry resolved server-side from the region
/// <see cref="Code"/> — the caller does not supply vertices. See issue #6.
/// </summary>
public sealed class RegionSpec
{
	/// <summary>Region code: ISO alpha-2, alpha-3, a colloquial alias (e.g. <c>UK</c>) or a full country name.</summary>
	public required string Code { get; init; }

	/// <summary>CSS fill colour.</summary>
	public string FillColor { get; init; } = "#dc2626";

	/// <summary>Fill opacity (0-1).</summary>
	public double FillOpacity { get; init; } = 0.5;

	/// <summary>Optional CSS stroke colour for the region outline.</summary>
	public string? StrokeColor { get; init; }

	/// <summary>Stroke width in pixels (used only when <see cref="StrokeColor"/> is set).</summary>
	public double StrokeWidth { get; init; } = 1;
}

/// <summary>
/// A request to render a static map image. Either <see cref="Center"/> or <see cref="Location"/>
/// must be supplied (if both are given, <see cref="Center"/> wins); when only <see cref="Location"/>
/// is set it is resolved to coordinates via the configured geocoder.
/// </summary>
public sealed record MapRequest
{
	/// <summary>Explicit map centre. Takes precedence over <see cref="Location"/>.</summary>
	public GeoPoint? Center { get; init; }

	/// <summary>A place/address name to geocode into the map centre when <see cref="Center"/> is not set.</summary>
	public string? Location { get; init; }

	/// <summary>Zoom level (0 = whole world). If not set and markers are supplied, the view is fit to the markers.</summary>
	public double? Zoom { get; init; }

	/// <summary>Image width in CSS pixels.</summary>
	public int Width { get; init; } = 800;

	/// <summary>Image height in CSS pixels.</summary>
	public int Height { get; init; } = 600;

	/// <summary>Device scale factor (2 = retina / @2x output).</summary>
	public int Scale { get; init; } = 1;

	/// <summary>Output image format.</summary>
	public MapImageFormat Format { get; init; } = MapImageFormat.Png;

	/// <summary>Optional override MapLibre style URL. Defaults to the configured tile style.</summary>
	public string? StyleUrl { get; init; }

	/// <summary>Markers to draw.</summary>
	public IReadOnlyList<MarkerSpec> Markers { get; init; } = [];

	/// <summary>Polyline overlays to draw.</summary>
	public IReadOnlyList<PathSpec> Paths { get; init; } = [];

	/// <summary>Polygon overlays to draw.</summary>
	public IReadOnlyList<PolygonSpec> Polygons { get; init; } = [];

	/// <summary>Named regions (countries/states) to shade, geometry resolved server-side.</summary>
	public IReadOnlyList<RegionSpec> Regions { get; init; } = [];
}
