using System.Globalization;
using System.Text;

namespace PanoramicData.Maps;

/// <summary>
/// Builds the Google-Static-Maps-compatible query for a <see cref="MapRequest"/> - the inverse of the
/// parser the service applies to it.
/// <para>
/// This lives beside the request model, and deliberately not in each caller: the grammar was defined
/// in one place and written in another, and the two drifted (issue #16). Callers that need a URL - a
/// report macro, a UI component, a test - should use this rather than assembling descriptors by hand.
/// </para>
/// <para>
/// Authentication is not the builder's business. It never writes an API key, because a key in a URL is
/// visible to anything that can see the URL; supply it as a header, or put a proxy in front.
/// </para>
/// </summary>
public static class StaticMapUrlBuilder
{
	/// <summary>
	/// Builds the full static-map URL for a request.
	/// </summary>
	/// <param name="baseUrl">The service base URL, for example <c>https://maps.panoramicdata.com</c>.</param>
	/// <param name="request">The map to render.</param>
	/// <returns>An absolute URL for the <c>/staticmap</c> endpoint.</returns>
	/// <exception cref="ArgumentException">
	/// The base URL is empty, or the request specifies nothing to draw - neither a centre, a location,
	/// nor any marker, path, polygon or region. The service rejects such a request, so building a URL
	/// for it would only produce a 400 later, further from the mistake.
	/// </exception>
	public static string Build(string baseUrl, MapRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			throw new ArgumentException("A base URL is required, for example https://maps.panoramicdata.com.", nameof(baseUrl));
		}

		if (request.Center is null
			&& string.IsNullOrWhiteSpace(request.Location)
			&& request.Markers.Count == 0
			&& request.Paths.Count == 0
			&& request.Polygons.Count == 0
			&& request.Regions.Count == 0)
		{
			throw new ArgumentException(
				"A request must set 'center' or 'location', or supply at least one marker, path, polygon or region.",
				nameof(request));
		}

		var builder = new StringBuilder(baseUrl.TrimEnd('/')).Append("/staticmap?");
		var first = true;

		if (request.Center is { } center)
		{
			Append(builder, ref first, "center", LatLng(center));
		}
		else if (!string.IsNullOrWhiteSpace(request.Location))
		{
			Append(builder, ref first, "center", request.Location!);
		}

		if (request.Zoom is { } zoom)
		{
			Append(builder, ref first, "zoom", Number(zoom));
		}

		Append(builder, ref first, "size", $"{Number(request.Width)}x{Number(request.Height)}");

		if (request.Scale > 1)
		{
			Append(builder, ref first, "scale", Number(request.Scale));
		}

		if (request.Format == MapImageFormat.Jpeg)
		{
			Append(builder, ref first, "format", "jpeg");
		}

		if (!string.IsNullOrWhiteSpace(request.StyleUrl))
		{
			Append(builder, ref first, "style", request.StyleUrl!);
		}

		foreach (var marker in request.Markers)
		{
			Append(builder, ref first, "markers", MarkerGroup(marker));
		}

		foreach (var path in request.Paths)
		{
			Append(builder, ref first, "path", PathGroup(path));
		}

		foreach (var polygon in request.Polygons)
		{
			Append(builder, ref first, "path", PolygonGroup(polygon));
		}

		foreach (var region in request.Regions)
		{
			Append(builder, ref first, "region", RegionGroup(region));
		}

		return builder.ToString();
	}

	private static string MarkerGroup(MarkerSpec marker)
	{
		var descriptors = new List<string>();

		if (!string.IsNullOrWhiteSpace(marker.Color))
		{
			descriptors.Add($"color:{marker.Color}");
		}

		if (!string.IsNullOrWhiteSpace(marker.Label))
		{
			descriptors.Add($"label:{marker.Label}");
		}

		if (!string.IsNullOrWhiteSpace(marker.Icon))
		{
			descriptors.Add($"icon:{marker.Icon}");
		}

		// 'scale' rather than 'size': it carries the exact value, where a size name would round it to
		// the nearest of four and lose a caller's deliberate 1.5.
		if (Math.Abs(marker.Scale - 1.0) > 1e-9)
		{
			descriptors.Add($"scale:{Number(marker.Scale)}");
		}

		descriptors.Add(LatLng(marker.Location));
		return string.Join('|', descriptors);
	}

	private static string PathGroup(PathSpec path)
	{
		var descriptors = new List<string>();

		if (!string.IsNullOrWhiteSpace(path.Color))
		{
			descriptors.Add($"color:{path.Color}");
		}

		descriptors.Add($"weight:{Number(path.Width)}");
		descriptors.AddRange(path.Points.Select(LatLng));
		return string.Join('|', descriptors);
	}

	private static string PolygonGroup(PolygonSpec polygon)
	{
		// A polygon is a path with a fill colour - that is how the query grammar expresses one, and how
		// the parser recognises it.
		var descriptors = new List<string> { $"fillcolor:{polygon.FillColor}" };

		if (!string.IsNullOrWhiteSpace(polygon.StrokeColor))
		{
			descriptors.Add($"color:{polygon.StrokeColor}");
		}

		descriptors.Add($"weight:{Number(polygon.StrokeWidth)}");
		descriptors.AddRange(polygon.Points.Select(LatLng));
		return string.Join('|', descriptors);
	}

	private static string RegionGroup(RegionSpec region)
	{
		var descriptors = new List<string>
		{
			$"code:{region.Code}",
			$"fill:{region.FillColor}",
			$"opacity:{Number(region.FillOpacity)}"
		};

		if (!string.IsNullOrWhiteSpace(region.StrokeColor))
		{
			descriptors.Add($"stroke:{region.StrokeColor}");
			descriptors.Add($"weight:{Number(region.StrokeWidth)}");
		}

		return string.Join('|', descriptors);
	}

	private static void Append(StringBuilder builder, ref bool first, string key, string value)
	{
		if (!first)
		{
			builder.Append('&');
		}

		first = false;
		builder.Append(key).Append('=').Append(Uri.EscapeDataString(value));
	}

	private static string LatLng(GeoPoint point)
		=> $"{Number(point.Latitude)},{Number(point.Longitude)}";

	private static string Number(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

	private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
