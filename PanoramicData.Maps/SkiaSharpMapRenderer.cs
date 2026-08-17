using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// Renders maps natively (no headless browser): fetches Mapbox Vector Tiles from the tile service,
/// decodes them, projects to Web-Mercator screen space and draws them with SkiaSharp, then draws the
/// requested overlays. Draws point place-name labels from the tiles' label layers (issue #1) with
/// halos and greedy collision avoidance; full MapLibre style-JSON fidelity (curved labels, road
/// shields) remains a later milestone.
/// </summary>
public sealed class SkiaSharpMapRenderer(HttpClient httpClient, IOptions<MapsOptions> options, ILogger<SkiaSharpMapRenderer> logger)
	: IMapRenderer
{
	private readonly HttpClient _httpClient = httpClient;
	private readonly MapsOptions _options = options.Value;
	private readonly ILogger<SkiaSharpMapRenderer> _logger = logger;
	private readonly MapboxTileReader _reader = new();

	private static readonly string[] LabelLayers = ["places", "place", "poi", "pois"];

	private sealed record LabelCandidate(string Text, float X, float Y, float Size, double Importance, bool Bold);

	/// <inheritdoc />
	public async Task<MapImage> RenderAsync(MapRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var center = request.Center ?? new GeoPoint(0, 20);
		var zoom = (int)Math.Clamp(Math.Round(request.Zoom ?? 12), 0, 15);
		var scale = Math.Clamp(request.Scale, 1, 3);
		var width = request.Width * scale;
		var height = request.Height * scale;

		var world = WebMercator.WorldSize(zoom);
		var left = WebMercator.LongitudeToX(center.Longitude, world) - width / 2.0;
		var top = WebMercator.LatitudeToY(center.Latitude, world) - height / 2.0;

		var styleUrl = string.IsNullOrWhiteSpace(request.StyleUrl) ? _options.TilesStyleUrl : request.StyleUrl!;

		using var surface = SKSurface.Create(new SKImageInfo(width, height));
		var canvas = surface.Canvas;
		canvas.Clear(new SKColor(0xF2, 0xEF, 0xE9)); // land/background

		var maxTile = WebMercator.MaxTileIndex(zoom);
		var txMin = (int)Math.Floor(left / WebMercator.TileSize);
		var txMax = (int)Math.Floor((left + width) / WebMercator.TileSize);
		var tyMin = Math.Clamp((int)Math.Floor(top / WebMercator.TileSize), 0, maxTile);
		var tyMax = Math.Clamp((int)Math.Floor((top + height) / WebMercator.TileSize), 0, maxTile);

		var labels = new List<LabelCandidate>();
		for (var ty = tyMin; ty <= tyMax; ty++)
		{
			for (var tx = txMin; tx <= txMax; tx++)
			{
				var wrappedX = ((tx % (maxTile + 1)) + (maxTile + 1)) % (maxTile + 1); // wrap antimeridian
				await DrawTileAsync(canvas, styleUrl, wrappedX, ty, zoom, world, left, top, scale, width, height, labels, cancellationToken).ConfigureAwait(false);
			}
		}

		DrawRegions(canvas, request, world, left, top, scale);
		DrawPlaceLabels(canvas, labels, scale);
		DrawOverlays(canvas, request, world, left, top, scale);
		DrawAttribution(canvas, width, height, scale);

		using var image = surface.Snapshot();
		var isPng = request.Format == MapImageFormat.Png;
		using var data = image.Encode(isPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, isPng ? 100 : 85);
		return new MapImage(data.ToArray(), isPng ? "image/png" : "image/jpeg");
	}

	private async Task DrawTileAsync(SKCanvas canvas, string styleUrl, int tx, int ty, int zoom, double world, double left, double top, int scale, int width, int height, List<LabelCandidate> labels, CancellationToken ct)
	{
		var url = TileUrl(styleUrl, zoom, tx, ty);
		byte[] bytes;
		try
		{
			using var resp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
			{
				return; // missing tile (e.g. ocean) - background already drawn
			}

			bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Failed to fetch tile {Url}", url);
			return;
		}

		if (bytes.Length == 0)
		{
			return;
		}

		using var ms = new MemoryStream(Gunzip(bytes));
		var vectorTile = _reader.Read(ms, new NetTopologySuite.IO.VectorTiles.Tiles.Tile(tx, ty, zoom));

		DrawLayer(canvas, vectorTile, ["water"], world, left, top, fill: new SKColor(0xA0, 0xC8, 0xF0));
		DrawLayer(canvas, vectorTile, ["landuse", "landcover", "natural"], world, left, top, fill: new SKColor(0xD6, 0xE3, 0xCE));
		DrawLayer(canvas, vectorTile, ["buildings"], world, left, top, fill: new SKColor(0xE4, 0xDF, 0xD9), stroke: new SKColor(0xD0, 0xC9, 0xC0), strokeWidth: 0.5f * scale);
		DrawLayer(canvas, vectorTile, ["roads", "transit"], world, left, top, stroke: new SKColor(0xFF, 0xFF, 0xFF), strokeWidth: 1.5f * scale, casing: new SKColor(0xCF, 0xC9, 0xC2));
		DrawLayer(canvas, vectorTile, ["boundaries"], world, left, top, stroke: new SKColor(0x9E, 0x9C, 0xB0), strokeWidth: 1f * scale);

		CollectLabels(vectorTile, world, left, top, scale, width, height, labels);
	}

	private static void CollectLabels(NetTopologySuite.IO.VectorTiles.VectorTile tile, double world, double left, double top, int scale, int width, int height, List<LabelCandidate> labels)
	{
		foreach (var layer in tile.Layers)
		{
			if (Array.IndexOf(LabelLayers, layer.Name) < 0)
			{
				continue;
			}

			foreach (var feature in layer.Features)
			{
				if (feature.Geometry is not Point pt || feature.Attributes is null)
				{
					continue;
				}

				var name = (feature.Attributes.GetOptionalValue("name:en") ?? feature.Attributes.GetOptionalValue("name")) as string;
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				var sp = Project(pt.Coordinate, world, left, top);
				if (sp.X < 0 || sp.Y < 0 || sp.X > width || sp.Y > height)
				{
					continue;
				}

				var kind = (feature.Attributes.GetOptionalValue("kind") ?? feature.Attributes.GetOptionalValue("class")) as string;
				var population = ToDouble(feature.Attributes.GetOptionalValue("population"));
				var (size, bold, kindBonus) = StyleForKind(kind, scale);
				var importance = kindBonus + population;
				labels.Add(new LabelCandidate(name!, sp.X, sp.Y, size, importance, bold));
			}
		}
	}

	private static (float Size, bool Bold, double KindBonus) StyleForKind(string? kind, int scale)
		=> (kind?.ToLowerInvariant()) switch
		{
			"country" => (15f * scale, true, 1e12),
			"region" or "state" or "province" => (13f * scale, true, 1e11),
			"city" or "locality" => (12f * scale, false, 1e6),
			"town" => (11f * scale, false, 1e5),
			_ => (10.5f * scale, false, 0),
		};

	private static void DrawPlaceLabels(SKCanvas canvas, List<LabelCandidate> labels, int scale)
	{
		if (labels.Count == 0)
		{
			return;
		}

		var placed = new List<SKRect>();
		using var fill = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
		using var halo = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xE0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f * scale, StrokeJoin = SKStrokeJoin.Round };
		var pad = 2f * scale;

		foreach (var label in labels.OrderByDescending(l => l.Importance).ThenByDescending(l => l.Size))
		{
			using var font = new SKFont { Size = label.Size, Embolden = label.Bold };
			var textWidth = font.MeasureText(label.Text);
			var half = textWidth / 2f;
			var rect = new SKRect(label.X - half - pad, label.Y - label.Size / 2f - pad, label.X + half + pad, label.Y + label.Size / 2f + pad);

			if (placed.Any(r => r.IntersectsWith(rect)))
			{
				continue;
			}

			placed.Add(rect);
			var baseline = label.Y + label.Size * 0.35f;
			canvas.DrawText(label.Text, label.X, baseline, SKTextAlign.Center, font, halo);
			canvas.DrawText(label.Text, label.X, baseline, SKTextAlign.Center, font, fill);
		}
	}

	private void DrawRegions(SKCanvas canvas, MapRequest request, double world, double left, double top, int scale)
	{
		foreach (var region in request.Regions)
		{
			var alpha3 = Countries.ResolveAlpha3(region.Code);
			if (alpha3 is null || !RegionBoundaries.TryGet(alpha3, out var geometry))
			{
				continue; // the parser already rejects unknown/boundary-less codes with a 400
			}

			using var path = ToPath(geometry, world, left, top);
			if (path is null)
			{
				continue;
			}

			path.FillType = SKPathFillType.EvenOdd; // honour interior rings (e.g. Lesotho within South Africa)
			using var fp = new SKPaint { Color = MapColors.Parse(region.FillColor, new SKColor(0xDC, 0x26, 0x26)).WithAlpha((byte)(Math.Clamp(region.FillOpacity, 0, 1) * 255)), IsAntialias = true, Style = SKPaintStyle.Fill };
			canvas.DrawPath(path, fp);

			if (!string.IsNullOrWhiteSpace(region.StrokeColor))
			{
				using var sp = new SKPaint { Color = MapColors.Parse(region.StrokeColor, new SKColor(0xB0, 0x1F, 0x1F)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)region.StrokeWidth * scale, StrokeJoin = SKStrokeJoin.Round };
				canvas.DrawPath(path, sp);
			}
		}
	}

	private void DrawLayer(SKCanvas canvas, NetTopologySuite.IO.VectorTiles.VectorTile tile, string[] layerNames,
		double world, double left, double top, SKColor? fill = null, SKColor? stroke = null, float strokeWidth = 1f, SKColor? casing = null)
	{
		foreach (var layer in tile.Layers)
		{
			if (Array.IndexOf(layerNames, layer.Name) < 0)
			{
				continue;
			}

			foreach (var feature in layer.Features)
			{
				var path = ToPath(feature.Geometry, world, left, top);
				if (path is null)
				{
					continue;
				}

				using (path)
				{
					if (fill is { } f)
					{
						using var p = new SKPaint { Color = f, IsAntialias = true, Style = SKPaintStyle.Fill };
						canvas.DrawPath(path, p);
					}

					if (casing is { } c)
					{
						using var p = new SKPaint { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth + 2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
						canvas.DrawPath(path, p);
					}

					if (stroke is { } s)
					{
						using var p = new SKPaint { Color = s, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
						canvas.DrawPath(path, p);
					}
				}
			}
		}
	}

	private static SKPath? ToPath(Geometry? geometry, double world, double left, double top)
	{
		if (geometry is null || geometry.IsEmpty)
		{
			return null;
		}

		var path = new SKPath();
		AddGeometry(path, geometry, world, left, top);
		return path;
	}

	private static void AddGeometry(SKPath path, Geometry geometry, double world, double left, double top)
	{
		switch (geometry)
		{
			case Point pt:
				var sp = Project(pt.Coordinate, world, left, top);
				path.AddCircle(sp.X, sp.Y, 2f);
				break;
			case LineString ls:
				AddLine(path, ls.Coordinates, world, left, top, close: false);
				break;
			case Polygon poly:
				AddLine(path, poly.ExteriorRing.Coordinates, world, left, top, close: true);
				foreach (var hole in poly.InteriorRings)
				{
					AddLine(path, hole.Coordinates, world, left, top, close: true);
				}
				break;
			case GeometryCollection gc:
				foreach (var g in gc.Geometries)
				{
					AddGeometry(path, g, world, left, top);
				}
				break;
		}
	}

	private static void AddLine(SKPath path, Coordinate[] coords, double world, double left, double top, bool close)
	{
		if (coords.Length == 0)
		{
			return;
		}

		path.MoveTo(Project(coords[0], world, left, top));
		for (var i = 1; i < coords.Length; i++)
		{
			path.LineTo(Project(coords[i], world, left, top));
		}

		if (close)
		{
			path.Close();
		}
	}

	private static SKPoint Project(Coordinate c, double world, double left, double top)
		=> new((float)(WebMercator.LongitudeToX(c.X, world) - left), (float)(WebMercator.LatitudeToY(c.Y, world) - top));

	private static void DrawOverlays(SKCanvas canvas, MapRequest request, double world, double left, double top, int scale)
	{
		foreach (var poly in request.Polygons)
		{
			using var path = new SKPath();
			AddLine(path, poly.Points.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray(), world, left, top, close: true);
			using var fp = new SKPaint { Color = MapColors.Parse(poly.FillColor, new SKColor(0xF5, 0x9E, 0x0B)).WithAlpha((byte)(poly.FillOpacity * 255)), IsAntialias = true, Style = SKPaintStyle.Fill };
			canvas.DrawPath(path, fp);
			using var lp = new SKPaint { Color = MapColors.Parse(poly.StrokeColor, new SKColor(0xF5, 0x9E, 0x0B)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)poly.StrokeWidth * scale };
			canvas.DrawPath(path, lp);
		}

		foreach (var line in request.Paths)
		{
			using var path = new SKPath();
			AddLine(path, line.Points.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray(), world, left, top, close: false);
			using var p = new SKPaint { Color = MapColors.Parse(line.Color, new SKColor(0x00, 0x00, 0xFF)).WithAlpha((byte)(line.Opacity * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)line.Width * scale, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
			canvas.DrawPath(path, p);
		}

		var fallbackMarker = new SKColor(0xDC, 0x26, 0x26);
		foreach (var m in request.Markers)
		{
			var pt = Project(new Coordinate(m.Location.Longitude, m.Location.Latitude), world, left, top);
			var r = (float)(9 * m.Scale) * scale;
			var markerColor = MapColors.Parse(m.Color, fallbackMarker);
			using var body = new SKPaint { Color = markerColor, IsAntialias = true, Style = SKPaintStyle.Fill };
			using var outline = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f * scale };
			using var tail = new SKPath();
			tail.MoveTo(pt.X - r * 0.7f, pt.Y - r * 0.4f);
			tail.LineTo(pt.X, pt.Y);
			tail.LineTo(pt.X + r * 0.7f, pt.Y - r * 0.4f);
			tail.Close();
			canvas.DrawPath(tail, body);
			canvas.DrawCircle(pt.X, pt.Y - r, r, body);
			canvas.DrawCircle(pt.X, pt.Y - r, r, outline);

			DrawMarkerLabel(canvas, m.Label, markerColor, pt.X, pt.Y - r, r);
		}
	}

	private static void DrawMarkerLabel(SKCanvas canvas, string? label, SKColor markerColor, float cx, float cy, float radius)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		// Contrast against the pin fill: dark text on light fills (white/yellow), white on dark fills.
		var luminance = (0.299 * markerColor.Red) + (0.587 * markerColor.Green) + (0.114 * markerColor.Blue);
		var textColor = luminance > 150 ? SKColors.Black : SKColors.White;

		using var font = new SKFont { Size = radius * 1.1f, Embolden = true };
		using var paint = new SKPaint { Color = textColor, IsAntialias = true };
		var baseline = cy + radius * 0.38f;
		canvas.DrawText(label.Trim(), cx, baseline, SKTextAlign.Center, font, paint);
	}

	private static void DrawAttribution(SKCanvas canvas, int width, int height, int scale)
	{
		const string text = "© OpenStreetMap";
		using var font = new SKFont { Size = 11f * scale };
		using var bg = new SKPaint { Color = new SKColor(255, 255, 255, 190), IsAntialias = true };
		using var fg = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
		var w = font.MeasureText(text);
		canvas.DrawRect(width - w - 8 * scale, height - 16f * scale, w + 8 * scale, 16f * scale, bg);
		canvas.DrawText(text, width - w - 4 * scale, height - 4f * scale, SKTextAlign.Left, font, fg);
	}

	private string TileUrl(string styleUrl, int z, int x, int y)
	{
		var baseUrl = styleUrl.Replace("/style.json", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
		return $"{baseUrl}/planet/{z}/{x}/{y}.mvt";
	}

	private static double ToDouble(object? value) => value switch
	{
		null => 0,
		double d => d,
		float f => f,
		long l => l,
		int i => i,
		string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) => v,
		IConvertible c => SafeToDouble(c),
		_ => 0,
	};

	private static double SafeToDouble(IConvertible c)
	{
		try { return c.ToDouble(System.Globalization.CultureInfo.InvariantCulture); }
		catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
	}

	private static byte[] Gunzip(byte[] bytes)
	{
		if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b)
		{
			return bytes; // not gzip
		}

		using var input = new MemoryStream(bytes);
		using var gz = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
		using var output = new MemoryStream();
		gz.CopyTo(output);
		return output.ToArray();
	}
}
