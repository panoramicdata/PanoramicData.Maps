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
public static class StaticMapRequestParser
{
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

		if (!ParseSize(First(query, "size"), First(query, "width"), First(query, "height"), options, out var width, out var height, out error))
		{
			return false;
		}

		double? zoom = TryDouble(First(query, "zoom"), out var z) ? Math.Clamp(z, 0, 22) : null;

		if (!ParseScale(First(query, "scale"), options, out var scale, out error))
		{
			return false;
		}

		var format = FormatOf(First(query, "format"));

		if (!ResolveStyle(First(query, "style") ?? First(query, "maptype"), options, out var styleUrl, out error))
		{
			return false;
		}

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

		var regions = new List<RegionSpec>();
		foreach (var group in All(query, "region"))
		{
			if (!ParseRegionGroup(group, regions, out error))
			{
				return false;
			}
		}

		if (center is null && location is null && markers.Count == 0 && paths.Count == 0 && polygons.Count == 0 && regions.Count == 0)
		{
			error = "Provide 'center' (lat,lng or a place name) and 'zoom', or at least one 'markers'/'path'/'region'.";
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
			StyleUrl = styleUrl,
			Markers = markers,
			Paths = paths,
			Polygons = polygons,
			Regions = regions
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
			else if (TryDescriptor(part, "size", out var sz)) { markerScale = MarkerMetrics.ScaleForSize(sz); }
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

	/// <summary>
	/// Parses a <c>region</c> group, e.g. <c>code:GB|fill:red|opacity:0.5|stroke:black|weight:1</c>.
	/// Rejects (via <paramref name="error"/>) a region code that resolves to no country or has no
	/// available boundary, rather than rendering nothing (issue #6).
	/// </summary>
	private static bool ParseRegionGroup(string group, List<RegionSpec> into, out string? error)
	{
		error = null;
		string? code = null;
		string fill = "#dc2626";
		double opacity = 0.5;
		string? stroke = null;
		double weight = 1;

		foreach (var part in group.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (TryDescriptor(part, "code", out var cd)) { code = cd; }
			else if (TryDescriptor(part, "fill", out var f)) { fill = f; }
			else if (TryDescriptor(part, "fillcolor", out var fc)) { fill = fc; }
			else if (TryDescriptor(part, "opacity", out var o) && double.TryParse(o, NumberStyles.Float, CultureInfo.InvariantCulture, out var ov)) { opacity = Math.Clamp(ov, 0, 1); }
			else if (TryDescriptor(part, "stroke", out var st)) { stroke = st; }
			else if (TryDescriptor(part, "weight", out var w) && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var wv)) { weight = wv; }
			else if (code is null) { code = part; } // bare code (e.g. region=GB)
		}

		if (string.IsNullOrWhiteSpace(code))
		{
			error = "A 'region' must specify a country code, e.g. region=code:GB|fill:red.";
			return false;
		}

		var alpha3 = Countries.ResolveAlpha3(code);
		if (alpha3 is null)
		{
			error = $"Unknown region code '{code}'.";
			return false;
		}

		if (!RegionBoundaries.TryGet(alpha3, out _))
		{
			error = $"No boundary available for region '{code}' at the current dataset resolution.";
			return false;
		}

		into.Add(new RegionSpec { Code = code, FillColor = fill, FillOpacity = opacity, StrokeColor = stroke, StrokeWidth = weight });
		return true;
	}

	/// <summary>
	/// Resolves the <c>style</c>/<c>maptype</c> selector to a style URL. A configured named style wins;
	/// the Google <c>roadmap</c>/<c>satellite</c>/<c>hybrid</c>/<c>terrain</c> values are accepted and
	/// alias to the default when not explicitly configured; anything else is rejected (issue #7).
	/// </summary>
	private static bool ResolveStyle(string? selector, MapsOptions options, out string? styleUrl, out string? error)
	{
		styleUrl = null;
		error = null;
		if (string.IsNullOrWhiteSpace(selector))
		{
			return true;
		}

		var name = selector.Trim();
		foreach (var kvp in options.Styles)
		{
			if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
			{
				styleUrl = kvp.Value;
				return true;
			}
		}

		switch (name.ToLowerInvariant())
		{
			case "roadmap":
			case "satellite": // no open global imagery - aliases to the road style
			case "hybrid":
			case "terrain": // achievable with open data, but aliases to default until a style is configured
				return true;
			default:
				error = $"Unknown map style '{name}'.";
				return false;
		}
	}


	private static MapImageFormat FormatOf(string? format)
		=> format is not null && (format.StartsWith("jpg", StringComparison.OrdinalIgnoreCase) || format.StartsWith("jpeg", StringComparison.OrdinalIgnoreCase))
			? MapImageFormat.Jpeg
			: MapImageFormat.Png;

	private static bool ParseScale(string? raw, MapsOptions options, out int scale, out string? error)
	{
		error = null;
		scale = 1;
		if (!TryInt(raw, out var s))
		{
			return true;
		}

		if (s > options.MaxScale)
		{
			error = $"scale {s} exceeds the maximum of {options.MaxScale}";
			return false;
		}

		scale = Math.Max(1, s);
		return true;
	}

	private static bool ParseSize(string? size, string? width, string? height, MapsOptions options, out int w, out int h, out string? error)
	{
		error = null;
		w = 800;
		h = 600;
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

		if (w > options.MaxWidth)
		{
			error = $"width {w} exceeds the maximum of {options.MaxWidth}";
			return false;
		}

		if (h > options.MaxHeight)
		{
			error = $"height {h} exceeds the maximum of {options.MaxHeight}";
			return false;
		}

		w = Math.Max(1, w);
		h = Math.Max(1, h);
		return true;
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
