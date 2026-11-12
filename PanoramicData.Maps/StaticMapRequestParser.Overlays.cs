namespace PanoramicData.Maps;

public static partial class StaticMapRequestParser
{
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
}
