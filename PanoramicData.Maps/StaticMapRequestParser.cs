using System.Globalization;

namespace PanoramicData.Maps;

/// <summary>
/// Parses Google-Static-Maps-style query parameters into a <see cref="MapRequest"/>.
/// Coordinates in the query use Google's <c>lat,lng</c> order. Parsing is synchronous; if
/// <see cref="MapRequest.Location"/> is set instead of <see cref="MapRequest.Center"/>, the caller
/// geocodes it. Supported: <c>center</c>/<c>location</c>, <c>zoom</c>, <c>size</c>/<c>width</c>/<c>height</c>,
/// <c>scale</c>, <c>format</c>, repeatable <c>markers</c> and <c>path</c> (styled, pipe-delimited).
/// </summary>
public static class StaticMapRequestParser
{
	/// <summary>
	/// Attempts to parse a query into a <see cref="MapRequest"/>.
	/// </summary>
	/// <param name="query">The query parameters (key -> values).</param>
	/// <param name="options">Limits (max width/height/scale).</param>
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

		GeoPoint? center = null;
		string? location = null;
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
		}

		if (center is null && location is null)
		{
			var loc = First(query, "location");
			if (!string.IsNullOrWhiteSpace(loc))
			{
				location = loc;
			}
		}

		var (width, height) = ParseSize(First(query, "size"), First(query, "width"), First(query, "height"), options);
		double? zoom = TryDouble(First(query, "zoom"), out var z) ? Math.Clamp(z, 0, 22) : null;
		var scale = Math.Clamp(TryInt(First(query, "scale"), out var s) ? s : 1, 1, options.MaxScale);
		var format = FormatOf(First(query, "format"));

		var markers = new List<MarkerSpec>();
		foreach (var group in All(query, "markers"))
		{
			ParseMarkerGroup(group, markers);
		}

		var paths = new List<PathSpec>();
		var polygons = new List<PolygonSpec>();
		foreach (var group in All(query, "path"))
		{
			ParsePathGroup(group, paths, polygons);
		}

		if (center is null && location is null && markers.Count == 0 && paths.Count == 0 && polygons.Count == 0)
		{
			error = "Provide 'center' (lat,lng or a place name) and 'zoom', or at least one 'markers'/'path'.";
			return false;
		}

		request = new MapRequest
		{
			Center = center,
			Location = location,
			Zoom = zoom,
			Width = width,
			Height = height,
			Scale = scale,
			Format = format,
			Markers = markers,
			Paths = paths,
			Polygons = polygons
		};
		return true;
	}

	private static void ParseMarkerGroup(string group, List<MarkerSpec> into)
	{
		string color = "red";
		string? label = null;
		double markerScale = 1.0;
		string? icon = null;
		var locations = new List<GeoPoint>();

		foreach (var part in group.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (TryDescriptor(part, "color", out var c)) { color = c; }
			else if (TryDescriptor(part, "label", out var l)) { label = l; }
			else if (TryDescriptor(part, "icon", out var i)) { icon = i; }
			else if (TryDescriptor(part, "scale", out var sc) && double.TryParse(sc, NumberStyles.Float, CultureInfo.InvariantCulture, out var scv)) { markerScale = scv; }
			else if (TryDescriptor(part, "size", out var sz)) { markerScale = SizeToScale(sz); }
			else if (TryLatLng(part, out var gp)) { locations.Add(gp); }
			// non-lat,lng location tokens (place names) are not supported per-marker yet - ignored.
		}

		foreach (var loc in locations)
		{
			into.Add(new MarkerSpec { Location = loc, Color = color, Label = label, Icon = icon, Scale = markerScale });
		}
	}

	private static void ParsePathGroup(string group, List<PathSpec> paths, List<PolygonSpec> polygons)
	{
		string color = "#0000ff";
		string? fillColor = null;
		double weight = 5;
		var points = new List<GeoPoint>();

		foreach (var part in group.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (TryDescriptor(part, "color", out var c)) { color = c; }
			else if (TryDescriptor(part, "fillcolor", out var fc)) { fillColor = fc; }
			else if (TryDescriptor(part, "weight", out var w) && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var wv)) { weight = wv; }
			else if (TryDescriptor(part, "geodesic", out _)) { /* accepted, ignored */ }
			else if (TryLatLng(part, out var gp)) { points.Add(gp); }
		}

		if (points.Count < 2)
		{
			return;
		}

		if (fillColor is not null)
		{
			polygons.Add(new PolygonSpec { Points = points, FillColor = fillColor, FillOpacity = 0.4, StrokeColor = color, StrokeWidth = weight });
		}
		else
		{
			paths.Add(new PathSpec { Points = points, Color = color, Width = weight });
		}
	}

	private static double SizeToScale(string size) => size.ToLowerInvariant() switch
	{
		"tiny" => 0.5,
		"small" => 0.7,
		"mid" => 1.0,
		"normal" => 1.0,
		_ => 1.0
	};

	private static MapImageFormat FormatOf(string? format)
		=> format is not null && (format.StartsWith("jpg", StringComparison.OrdinalIgnoreCase) || format.StartsWith("jpeg", StringComparison.OrdinalIgnoreCase))
			? MapImageFormat.Jpeg
			: MapImageFormat.Png;

	private static (int Width, int Height) ParseSize(string? size, string? width, string? height, MapsOptions options)
	{
		var w = 800;
		var h = 600;
		if (!string.IsNullOrWhiteSpace(size))
		{
			var parts = size.Split('x', 'X');
			if (parts.Length == 2 && int.TryParse(parts[0], out var pw) && int.TryParse(parts[1], out var ph))
			{
				w = pw;
				h = ph;
			}
		}
		else
		{
			if (int.TryParse(width, out var pw)) { w = pw; }
			if (int.TryParse(height, out var ph)) { h = ph; }
		}

		return (Math.Clamp(w, 1, options.MaxWidth), Math.Clamp(h, 1, options.MaxHeight));
	}

	/// <summary>Parses a Google <c>lat,lng</c> pair into a <see cref="GeoPoint"/> (which stores lon,lat).</summary>
	/// <param name="value">The <c>lat,lng</c> text.</param>
	/// <param name="point">The parsed point.</param>
	/// <returns><see langword="true"/> if parsed.</returns>
	public static bool TryLatLng(string? value, out GeoPoint point)
	{
		point = default;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var parts = value.Split(',');
		if (parts.Length == 2
			&& double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
			&& double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
			&& lat is >= -90 and <= 90 && lng is >= -180 and <= 180)
		{
			point = new GeoPoint(lng, lat);
			return true;
		}

		return false;
	}

	private static bool TryDescriptor(string part, string key, out string value)
	{
		value = string.Empty;
		if (part.Length > key.Length + 1
			&& part.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
		{
			value = part[(key.Length + 1)..];
			return true;
		}

		return false;
	}

	private static string? First(IReadOnlyDictionary<string, IReadOnlyList<string>> q, string key)
		=> q.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

	private static IEnumerable<string> All(IReadOnlyDictionary<string, IReadOnlyList<string>> q, string key)
		=> q.TryGetValue(key, out var v) ? v.Where(s => !string.IsNullOrWhiteSpace(s)) : [];

	private static bool TryDouble(string? s, out double value)
		=> double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

	private static bool TryInt(string? s, out int value)
		=> int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
