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

	/// <summary>
	/// Parses one <c>markers</c> group. A remote <c>icon:</c> URL is rejected rather than quietly
	/// replaced with a pin: fetching caller-supplied URLs from a public service is a decision about
	/// SSRF exposure, and named sprite icons cover the useful cases (issue #12).
	/// </summary>
	private static bool ParseMarkerGroup(string group, List<MarkerSpec> into, out string? error)
	{
		error = null;
		var marker = new MarkerDescriptors();
		foreach (var part in Descriptors(group))
		{
			marker.Apply(part);
		}

		if (marker.Icon is not null && LooksLikeUrl(marker.Icon))
		{
			error = $"A marker 'icon' must name an icon from the map style's sprite sheet (for example icon:cafe); "
				+ $"remote icon URLs such as '{marker.Icon}' are not supported. GET /v1/icons lists the available names.";
			return false;
		}

		foreach (var loc in marker.Locations)
		{
			into.Add(new MarkerSpec { Location = loc, Color = marker.Color, Label = marker.Label, Icon = marker.Icon, Scale = marker.Scale });
		}

		return true;
	}

	/// <summary>
	/// The state one <c>markers</c> group accumulates as its descriptors are read left to right. The
	/// descriptors are applied in the order written, so a later one overrides an earlier one - which is
	/// why this is a running state rather than a lookup over the whole group.
	/// </summary>
	private sealed class MarkerDescriptors
	{
		public string Color { get; private set; } = "red";

		public string? Label { get; private set; }

		public string? Icon { get; private set; }

		public double Scale { get; private set; } = 1.0;

		public List<GeoPoint> Locations { get; } = [];

		public void Apply(string part)
		{
			if (TryDescriptor(part, "color", out var c)) { Color = c; }
			else if (TryDescriptor(part, "label", out var l)) { Label = l; }
			else if (TryDescriptor(part, "icon", out var i)) { Icon = i; }
			else if (TryDescriptor(part, "scale", out var sc)) { Scale = Number(sc, Scale); }
			else if (TryDescriptor(part, "size", out var sz)) { Scale = MarkerMetrics.ScaleForSize(sz); }
			else if (TryLatLng(part, out var gp)) { Locations.Add(gp); }
			// non-lat,lng location tokens (place names) are not supported per-marker yet - ignored.
		}
	}

	private static bool LooksLikeUrl(string icon)
		=> icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| icon.StartsWith("//", StringComparison.Ordinal);

	private static void ParsePathGroup(string group, List<PathSpec> paths, List<PolygonSpec> polygons)
	{
		var path = new PathDescriptors();
		foreach (var part in Descriptors(group))
		{
			path.Apply(part);
		}

		if (path.Points.Count < 2)
		{
			return;
		}

		// A fill colour is what distinguishes a polygon from a line in this grammar.
		if (path.FillColor is not null)
		{
			polygons.Add(new PolygonSpec { Points = path.Points, FillColor = path.FillColor, FillOpacity = 0.4, StrokeColor = path.Color, StrokeWidth = path.Weight });
		}
		else
		{
			paths.Add(new PathSpec { Points = path.Points, Color = path.Color, Width = path.Weight });
		}
	}

	/// <summary>The running state of one <c>path</c> group. See <see cref="MarkerDescriptors"/>.</summary>
	private sealed class PathDescriptors
	{
		public string Color { get; private set; } = "#0000ff";

		public string? FillColor { get; private set; }

		public double Weight { get; private set; } = 5;

		public List<GeoPoint> Points { get; } = [];

		public void Apply(string part)
		{
			if (TryDescriptor(part, "color", out var c)) { Color = c; }
			else if (TryDescriptor(part, "fillcolor", out var fc)) { FillColor = fc; }
			else if (TryDescriptor(part, "weight", out var w)) { Weight = Number(w, Weight); }
			else if (TryDescriptor(part, "geodesic", out _)) { /* accepted, ignored */ }
			else if (TryLatLng(part, out var gp)) { Points.Add(gp); }
		}
	}

	/// <summary>
	/// Parses a <c>region</c> group, e.g. <c>code:GB|fill:red|opacity:0.5|stroke:black|weight:1</c>.
	/// Rejects (via <paramref name="error"/>) a region code that resolves to no country or has no
	/// available boundary, rather than rendering nothing (issue #6).
	/// </summary>
	private static bool ParseRegionGroup(string group, List<RegionSpec> into, out string? error)
	{
		var region = new RegionDescriptors();
		foreach (var part in Descriptors(group))
		{
			region.Apply(part);
		}

		if (!TryResolveRegionCode(region.Code, out error))
		{
			return false;
		}

		into.Add(new RegionSpec
		{
			Code = region.Code!,
			FillColor = region.Fill,
			FillOpacity = region.Opacity,
			StrokeColor = region.Stroke,
			StrokeWidth = region.Weight
		});
		return true;
	}

	/// <summary>
	/// Checks that a region code names a country this renderer actually has a boundary for. Reporting
	/// the two failures separately matters: an unknown code is the caller's typo, while a known country
	/// with no boundary is a limit of the dataset (issue #6).
	/// </summary>
	private static bool TryResolveRegionCode(string? code, out string? error)
	{
		error = null;
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

		return true;
	}

	/// <summary>The running state of one <c>region</c> group. See <see cref="MarkerDescriptors"/>.</summary>
	private sealed class RegionDescriptors
	{
		public string? Code { get; private set; }

		public string Fill { get; private set; } = "#dc2626";

		public double Opacity { get; private set; } = 0.5;

		public string? Stroke { get; private set; }

		public double Weight { get; private set; } = 1;

		public void Apply(string part)
		{
			if (TryDescriptor(part, "code", out var cd)) { Code = cd; }
			else if (TryDescriptor(part, "fill", out var f)) { Fill = f; }
			else if (TryDescriptor(part, "fillcolor", out var fc)) { Fill = fc; }
			else if (TryDescriptor(part, "opacity", out var o)) { Opacity = Math.Clamp(Number(o, Opacity), 0, 1); }
			else if (TryDescriptor(part, "stroke", out var st)) { Stroke = st; }
			else if (TryDescriptor(part, "weight", out var w)) { Weight = Number(w, Weight); }
			else if (Code is null) { Code = part; } // bare code (e.g. region=GB)
		}
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
		ReadRequestedSize(size, width, height, out w, out h);

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

	/// <summary>
	/// Reads the requested pixel dimensions from either the combined <c>size=WxH</c> form or the separate
	/// <c>width</c>/<c>height</c> parameters, leaving the defaults in place for anything unparseable.
	/// </summary>
	private static void ReadRequestedSize(string? size, string? width, string? height, out int w, out int h)
	{
		w = 800;
		h = 600;

		if (string.IsNullOrWhiteSpace(size))
		{
			if (int.TryParse(width, out var pw)) { w = pw; }
			if (int.TryParse(height, out var ph)) { h = ph; }
			return;
		}

		var parts = size.Split(['x', 'X']);
		if (parts.Length == 2 && int.TryParse(parts[0], out var sw) && int.TryParse(parts[1], out var sh))
		{
			w = sw;
			h = sh;
		}
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

	/// <summary>Splits a pipe-delimited group into its descriptor tokens, in the order written.</summary>
	private static string[] Descriptors(string group)
		=> group.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	/// <summary>
	/// Reads a descriptor's numeric value, keeping the current one when the text is not a number - so a
	/// malformed 'weight:wide' is ignored rather than resetting the value to a default.
	/// </summary>
	private static double Number(string value, double fallback)
		=> double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

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
