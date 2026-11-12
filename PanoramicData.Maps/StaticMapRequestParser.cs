using System.Globalization;

namespace PanoramicData.Maps;

/// <summary>
/// Parses Google-Static-Maps-style query parameters into a <see cref="MapRequest"/>.
/// Coordinates in the query use Google's <c>lat,lng</c> order. Parsing is synchronous; if
/// <see cref="MapRequest.Location"/> is set instead of <see cref="MapRequest.Center"/>, the caller
/// geocodes it. Supported: <c>center</c>/<c>location</c>, <c>zoom</c>, <c>size</c>/<c>width</c>/<c>height</c>,
/// <c>scale</c>, <c>format</c>, <c>maptype</c>/<c>style</c>, repeatable <c>markers</c>, <c>path</c> and
/// <c>region</c> (styled, pipe-delimited). Requests exceeding the configured size/scale limits are
/// rejected (issue #3) rather than silently clamped.
/// </summary>
public static partial class StaticMapRequestParser
{
	/// <summary>The separators accepted between the two halves of <c>size=WxH</c>.</summary>
	private static readonly char[] SizeSeparators = ['x', 'X'];

	/// <summary>
	/// Attempts to parse a query into a <see cref="MapRequest"/>.
	/// </summary>
	/// <param name="query">The query parameters (key -> values).</param>
	/// <param name="options">Limits (max width/height/scale) and named styles.</param>
	/// <param name="request">The parsed request on success.</param>
	/// <param name="error">A human-readable error on failure.</param>
	/// <returns><see langword="true"/> if a renderable request was produced.</returns>
	public static bool TryParse(
		IReadOnlyDictionary<string, IReadOnlyList<string>> query,
		MapsOptions options,
		out MapRequest request,
		out string? error)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(options);
		error = null;
		request = new MapRequest();

		ResolveCenter(query, out var center, out var location);

		if (!TryParseView(query, options, out var view, out error)
			|| !TryParseOverlays(query, out var overlays, out error))
		{
			return false;
		}

		if (NothingToDraw(center, location, overlays))
		{
			error = "Provide 'center' (lat,lng or a place name) and 'zoom', or at least one 'markers'/'path'/'region'.";
			return false;
		}

		request = new MapRequest
		{
			Center = center,
			Location = location,
			Zoom = view.Zoom,
			Width = view.Width,
			Height = view.Height,
			Scale = view.Scale,
			Format = view.Format,
			StyleUrl = view.StyleUrl,
			Markers = overlays.Markers,
			Paths = overlays.Paths,
			Polygons = overlays.Polygons,
			Regions = overlays.Regions
		};
		return true;
	}

	/// <summary>The view parameters: how big the image is, how far in, and how it is styled and encoded.</summary>
	private sealed record ViewSettings(int Width, int Height, double? Zoom, int Scale, MapImageFormat Format, string? StyleUrl);

	/// <summary>Everything drawn on top of the base map.</summary>
	private sealed record Overlays(List<MarkerSpec> Markers, List<PathSpec> Paths, List<PolygonSpec> Polygons, List<RegionSpec> Regions);

	/// <summary>
	/// Reads the view parameters, rejecting a request that exceeds a configured limit or names an unknown
	/// style rather than silently clamping or substituting one (issues #3 and #7).
	/// </summary>
	private static bool TryParseView(
		IReadOnlyDictionary<string, IReadOnlyList<string>> query,
		MapsOptions options,
		out ViewSettings view,
		out string? error)
	{
		view = null!;

		if (!ParseSize(First(query, "size"), First(query, "width"), First(query, "height"), options, out var width, out var height, out error)
			|| !ParseScale(First(query, "scale"), options, out var scale, out error)
			|| !ResolveStyle(First(query, "style") ?? First(query, "maptype"), options, out var styleUrl, out error))
		{
			return false;
		}

		view = new ViewSettings(
			width,
			height,
			ParseZoom(First(query, "zoom")),
			scale,
			FormatOf(First(query, "format")),
			styleUrl);
		return true;
	}

	/// <summary>
	/// Reads every overlay group. Markers and regions can be rejected - a remote icon URL, an unknown
	/// country - whereas a path that names fewer than two points is simply not drawn.
	/// </summary>
	private static bool TryParseOverlays(
		IReadOnlyDictionary<string, IReadOnlyList<string>> query,
		out Overlays overlays,
		out string? error)
	{
		var markers = new List<MarkerSpec>();
		var paths = new List<PathSpec>();
		var polygons = new List<PolygonSpec>();
		var regions = new List<RegionSpec>();
		overlays = new Overlays(markers, paths, polygons, regions);

		if (!ParseGroups(query, "markers", markers, ParseMarkerGroup, out error))
		{
			return false;
		}

		foreach (var group in All(query, "path"))
		{
			ParsePathGroup(group, paths, polygons);
		}

		return ParseGroups(query, "region", regions, ParseRegionGroup, out error);
	}

	/// <summary>
	/// Reads the requested centre. Google's <c>center</c> carries either a <c>lat,lng</c> pair or a place
	/// name; a name is left for the caller to geocode. <c>location</c> is the fallback spelling.
	/// </summary>
	private static void ResolveCenter(IReadOnlyDictionary<string, IReadOnlyList<string>> query, out GeoPoint? center, out string? location)
	{
		center = null;
		location = null;

		var centerRaw = First(query, "center");
		if (!string.IsNullOrWhiteSpace(centerRaw))
		{
			if (TryLatLng(centerRaw, out var gp))
			{
				center = gp;
			}
			else
			{
				location = centerRaw;
			}

			return;
		}

		var loc = First(query, "location");
		if (!string.IsNullOrWhiteSpace(loc))
		{
			location = loc;
		}
	}

	private static double? ParseZoom(string? raw)
		=> TryDouble(raw, out var z) ? Math.Clamp(z, 0, 22) : null;

	/// <summary>
	/// Runs a group parser over every repetition of a query key, stopping at the first rejection so the
	/// caller reports the offending group rather than the last one.
	/// </summary>
	private delegate bool GroupParser<T>(string group, List<T> into, out string? error);

	private static bool ParseGroups<T>(
		IReadOnlyDictionary<string, IReadOnlyList<string>> query,
		string key,
		List<T> into,
		GroupParser<T> parse,
		out string? error)
	{
		error = null;
		foreach (var group in All(query, key))
		{
			if (!parse(group, into, out error))
			{
				return false;
			}
		}

		return true;
	}

	private static bool NothingToDraw(GeoPoint? center, string? location, Overlays overlays)
		=> center is null
			&& location is null
			&& overlays.Markers.Count == 0
			&& overlays.Paths.Count == 0
			&& overlays.Polygons.Count == 0
			&& overlays.Regions.Count == 0;
}
