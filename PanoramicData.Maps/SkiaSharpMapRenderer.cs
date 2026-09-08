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
public sealed class SkiaSharpMapRenderer(
	HttpClient httpClient,
	IOptions<MapsOptions> options,
	ILogger<SkiaSharpMapRenderer> logger,
	SpriteSheetProvider? spriteSheetProvider = null)
	: IMapRenderer
{
	private readonly HttpClient _httpClient = httpClient;
	private readonly MapsOptions _options = options.Value;
	private readonly ILogger<SkiaSharpMapRenderer> _logger = logger;
	private readonly SpriteSheetProvider? _spriteSheetProvider = spriteSheetProvider;
	private readonly MapboxTileReader _reader = new();

	private static readonly string[] LabelLayers = ["places", "place", "poi", "pois"];

	/// <summary>The land fill, from the reference style's <c>earth</c> layer.</summary>
	private static readonly SKColor EarthColor = new(0xE2, 0xDF, 0xDA);

	/// <summary>
	/// Drawn where no tile could be fetched. The reference style's own background colour, chosen because
	/// it reads as neither land nor sea - a missing tile should look like missing data, not like geography.
	/// </summary>
	private static readonly SKColor NoDataColor = new(0xCC, 0xCC, 0xCC);

	/// <summary>
	/// Sampling for sprite icons. They are drawn at a fractional scale factor, so nearest-neighbour
	/// (SkiaSharp's default when no options are given) would alias the glyph edges; linear filtering
	/// matches what a MapLibre client does with the same atlas.
	/// </summary>
	private static readonly SKSamplingOptions SpriteSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

	private sealed record LabelCandidate(string Text, float X, float Y, float Size, double Importance, bool Bold);

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
	private sealed record Viewport(double World, double Left, double Top, int Scale, int Width, int Height);

	/// <summary>Identifies one vector tile in the slippy-map scheme.</summary>
	private sealed record TileAddress(int X, int Y, int Zoom);

	/// <summary>
	/// How one source layer is painted. A null colour means that pass is skipped, so a layer can be
	/// fill-only (water), stroke-only (boundaries) or stroked over a wider casing (roads).
	/// </summary>
	private sealed record LayerPaint(SKColor? Fill = null, SKColor? Stroke = null, float StrokeWidth = 1f, SKColor? Casing = null);

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

		// Neither land nor sea: land comes from the tiles' 'earth' layer and sea from 'water', exactly as
		// in the reference style, whose background is this same neutral grey. Clearing to the land colour
		// instead - as this renderer used to - fabricated land wherever a tile failed to arrive (issue #13).
		canvas.Clear(NoDataColor);

		var maxTile = WebMercator.MaxTileIndex(zoom);
		var txMin = (int)Math.Floor(left / WebMercator.TileSize);
		var txMax = (int)Math.Floor((left + width) / WebMercator.TileSize);
		var tyMin = Math.Clamp((int)Math.Floor(top / WebMercator.TileSize), 0, maxTile);
		var tyMax = Math.Clamp((int)Math.Floor((top + height) / WebMercator.TileSize), 0, maxTile);

		var viewport = new Viewport(world, left, top, scale, width, height);
		var labels = new List<LabelCandidate>();
		var requested = 0;
		var failed = 0;
		for (var ty = tyMin; ty <= tyMax; ty++)
		{
			for (var tx = txMin; tx <= txMax; tx++)
			{
				var wrappedX = ((tx % (maxTile + 1)) + (maxTile + 1)) % (maxTile + 1); // wrap antimeridian
				requested++;
				if (!await DrawTileAsync(canvas, styleUrl, new TileAddress(wrappedX, ty, zoom), viewport, labels, cancellationToken).ConfigureAwait(false))
				{
					failed++;
				}
			}
		}

		if (failed > 0)
		{
			// The tile service returns data for every tile of the planet, so a failure is a fault rather
			// than an area with no coverage - and the resulting no-data patches are otherwise silent.
			_logger.LogWarning("{Failed} of {Requested} tiles could not be fetched at zoom {Zoom}; those areas are drawn as no-data.", failed, requested, zoom);
		}

		// Only reach for the sprite sheet when a marker actually asks for an icon: most maps do not, and
		// the atlas is a separate fetch (cached thereafter).
		SpriteSheet? sprites = null;
		if (_spriteSheetProvider is not null && request.Markers.Any(marker => !string.IsNullOrWhiteSpace(marker.Icon)))
		{
			sprites = await _spriteSheetProvider.GetAsync(styleUrl, _options.SpriteUrl, cancellationToken).ConfigureAwait(false);
		}

		DrawRegions(canvas, request, viewport);
		DrawPlaceLabels(canvas, labels, scale);
		DrawOverlays(canvas, request, viewport, _logger, sprites);
		DrawAttribution(canvas, width, height, scale);

		using var image = surface.Snapshot();
		var isPng = request.Format == MapImageFormat.Png;
		using var data = image.Encode(isPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, isPng ? 100 : 85);
		return new MapImage(data.ToArray(), isPng ? "image/png" : "image/jpeg");
	}

	/// <summary>Fetches and draws one tile. Returns false when the tile could not be fetched or was empty.</summary>
	private async Task<bool> DrawTileAsync(SKCanvas canvas, string styleUrl, TileAddress tile, Viewport viewport, List<LabelCandidate> labels, CancellationToken ct)
	{
		var url = TileUrl(styleUrl, tile.Zoom, tile.X, tile.Y);
		byte[] bytes;
		try
		{
			using var resp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
			{
				_logger.LogDebug("Tile {Url} returned {StatusCode}", url, (int)resp.StatusCode);
				return false;
			}

			bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "Failed to fetch tile {Url}", url);
			return false;
		}

		if (bytes.Length == 0)
		{
			return false;
		}

		using var ms = new MemoryStream(Gunzip(bytes));
		var vectorTile = _reader.Read(ms, new NetTopologySuite.IO.VectorTiles.Tiles.Tile(tile.X, tile.Y, tile.Zoom));

		var scale = viewport.Scale;
		DrawLayer(canvas, vectorTile, ["earth"], viewport, new LayerPaint(Fill: EarthColor));
		DrawLayer(canvas, vectorTile, ["water"], viewport, new LayerPaint(Fill: new SKColor(0xA0, 0xC8, 0xF0)));
		DrawStyledFills(canvas, vectorTile, "landcover", viewport, tile.Zoom);
		DrawStyledFills(canvas, vectorTile, "landuse", viewport, tile.Zoom);
		DrawLayer(canvas, vectorTile, ["buildings"], viewport, new LayerPaint(Fill: new SKColor(0xE4, 0xDF, 0xD9), Stroke: new SKColor(0xD0, 0xC9, 0xC0), StrokeWidth: 0.5f * scale));
		DrawLayer(canvas, vectorTile, ["roads", "transit"], viewport, new LayerPaint(Stroke: new SKColor(0xFF, 0xFF, 0xFF), StrokeWidth: 1.5f * scale, Casing: new SKColor(0xCF, 0xC9, 0xC2)));
		DrawLayer(canvas, vectorTile, ["boundaries"], viewport, new LayerPaint(Stroke: new SKColor(0x9E, 0x9C, 0xB0), StrokeWidth: 1f * scale));

		CollectLabels(vectorTile, viewport, labels);
		return true;
	}

	private static void CollectLabels(NetTopologySuite.IO.VectorTiles.VectorTile tile, Viewport viewport, List<LabelCandidate> labels)
	{
		foreach (var layer in tile.Layers)
		{
			if (Array.IndexOf(LabelLayers, layer.Name) < 0)
			{
				continue;
			}

			foreach (var feature in layer.Features)
			{
				if (TryReadLabel(feature, viewport) is { } candidate)
				{
					labels.Add(candidate);
				}
			}
		}
	}

	/// <summary>Whether a projected point falls outside the image, and so has no label to place.</summary>
	private static bool IsOutsideImage(SKPoint point, Viewport viewport)
		=> point.X < 0 || point.Y < 0 || point.X > viewport.Width || point.Y > viewport.Height;

	/// <summary>
	/// Turns one label-layer feature into a placement candidate, or <see langword="null"/> when it is not
	/// a named point inside the image. Importance combines the kind's rank with population, so a capital
	/// beats a village when the two collide.
	/// </summary>
	private static LabelCandidate? TryReadLabel(NetTopologySuite.Features.IFeature feature, Viewport viewport)
	{
		if (feature.Geometry is not Point pt || feature.Attributes is null)
		{
			return null;
		}

		var name = (feature.Attributes.GetOptionalValue("name:en") ?? feature.Attributes.GetOptionalValue("name")) as string;
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		var sp = Project(pt.Coordinate, viewport);
		if (IsOutsideImage(sp, viewport))
		{
			return null;
		}

		var kind = (feature.Attributes.GetOptionalValue("kind") ?? feature.Attributes.GetOptionalValue("class")) as string;
		var population = ToDouble(feature.Attributes.GetOptionalValue("population"));
		var (size, bold, kindBonus) = StyleForKind(kind, viewport.Scale);
		return new LabelCandidate(name, sp.X, sp.Y, size, kindBonus + population, bold);
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

	private void DrawRegions(SKCanvas canvas, MapRequest request, Viewport viewport)
	{
		foreach (var region in request.Regions)
		{
			var alpha3 = Countries.ResolveAlpha3(region.Code);
			if (alpha3 is null || !RegionBoundaries.TryGet(alpha3, out var geometry))
			{
				continue; // the parser already rejects unknown/boundary-less codes with a 400
			}

			using var path = ToPath(geometry, viewport);
			if (path is null)
			{
				continue;
			}

			path.FillType = SKPathFillType.EvenOdd; // honour interior rings (e.g. Lesotho within South Africa)
			using var fp = new SKPaint { Color = MapColors.Parse(region.FillColor, new SKColor(0xDC, 0x26, 0x26)).WithAlpha((byte)(Math.Clamp(region.FillOpacity, 0, 1) * 255)), IsAntialias = true, Style = SKPaintStyle.Fill };
			canvas.DrawPath(path, fp);

			if (!string.IsNullOrWhiteSpace(region.StrokeColor))
			{
				using var sp = new SKPaint { Color = MapColors.Parse(region.StrokeColor, new SKColor(0xB0, 0x1F, 0x1F)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)region.StrokeWidth * viewport.Scale, StrokeJoin = SKStrokeJoin.Round };
				canvas.DrawPath(path, sp);
			}
		}
	}

	/// <summary>
	/// Draws one source layer's polygons, styling each feature by its <c>kind</c> and the current zoom
	/// through <see cref="BaseMapStyle"/> - the rules the tile service's own style JSON applies. A
	/// feature the reference style paints nothing for is skipped, which is what keeps marine protected
	/// areas out of the ocean at low zoom (issue #10) and towns from rendering as parkland.
	/// </summary>
	private static void DrawStyledFills(SKCanvas canvas, NetTopologySuite.IO.VectorTiles.VectorTile tile,
		string layerName, Viewport viewport, double zoom)
	{
		foreach (var layer in tile.Layers)
		{
			if (!string.Equals(layer.Name, layerName, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var feature in layer.Features)
			{
				var kind = feature.Attributes?.GetOptionalValue("kind") as string;
				if (BaseMapStyle.FillFor(layerName, kind, zoom) is not { } color)
				{
					continue;
				}

				var path = ToPath(feature.Geometry, viewport);
				if (path is null)
				{
					continue;
				}

				using (path)
				{
					path.FillType = SKPathFillType.EvenOdd; // honour interior rings (e.g. a lake in a park)
					using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
					canvas.DrawPath(path, paint);
				}
			}
		}
	}

	private void DrawLayer(SKCanvas canvas, NetTopologySuite.IO.VectorTiles.VectorTile tile, string[] layerNames,
		Viewport viewport, LayerPaint paint)
	{
		foreach (var layer in tile.Layers)
		{
			if (Array.IndexOf(layerNames, layer.Name) < 0)
			{
				continue;
			}

			foreach (var feature in layer.Features)
			{
				var path = ToPath(feature.Geometry, viewport);
				if (path is null)
				{
					continue;
				}

				using (path)
				{
					if (paint.Fill is { } f)
					{
						using var p = new SKPaint { Color = f, IsAntialias = true, Style = SKPaintStyle.Fill };
						canvas.DrawPath(path, p);
					}

					if (paint.Casing is { } c)
					{
						using var p = new SKPaint { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = paint.StrokeWidth + 2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
						canvas.DrawPath(path, p);
					}

					if (paint.Stroke is { } s)
					{
						using var p = new SKPaint { Color = s, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = paint.StrokeWidth, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
						canvas.DrawPath(path, p);
					}
				}
			}
		}
	}

	private static SKPath? ToPath(Geometry? geometry, Viewport viewport)
	{
		if (geometry is null || geometry.IsEmpty)
		{
			return null;
		}

		using var builder = new SKPathBuilder();
		AddGeometry(builder, geometry, viewport);
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

	/// <summary>
	/// Builds a standalone path for one ring or line. SkiaSharp 4 makes <see cref="SKPath"/> immutable,
	/// so geometry is accumulated in a builder and detached once.
	/// </summary>
	private static SKPath BuildLinePath(Coordinate[] coords, Viewport viewport, bool close)
	{
		using var builder = new SKPathBuilder();
		AddLine(builder, coords, viewport, close);
		return builder.Detach();
	}

	private static SKPoint Project(Coordinate c, Viewport viewport)
		=> new(
			(float)(WebMercator.LongitudeToX(c.X, viewport.World) - viewport.Left),
			(float)(WebMercator.LatitudeToY(c.Y, viewport.World) - viewport.Top));

	private static void DrawOverlays(SKCanvas canvas, MapRequest request, Viewport viewport, ILogger logger, SpriteSheet? sprites)
	{
		var scale = viewport.Scale;

		foreach (var poly in request.Polygons)
		{
			using var path = BuildLinePath([.. poly.Points.Select(p => new Coordinate(p.Longitude, p.Latitude))], viewport, close: true);
			using var fp = new SKPaint { Color = MapColors.Parse(poly.FillColor, new SKColor(0xF5, 0x9E, 0x0B)).WithAlpha((byte)(poly.FillOpacity * 255)), IsAntialias = true, Style = SKPaintStyle.Fill };
			canvas.DrawPath(path, fp);
			using var lp = new SKPaint { Color = MapColors.Parse(poly.StrokeColor, new SKColor(0xF5, 0x9E, 0x0B)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)poly.StrokeWidth * scale };
			canvas.DrawPath(path, lp);
		}

		foreach (var line in request.Paths)
		{
			using var path = BuildLinePath([.. line.Points.Select(p => new Coordinate(p.Longitude, p.Latitude))], viewport, close: false);
			using var p = new SKPaint { Color = MapColors.Parse(line.Color, new SKColor(0x00, 0x00, 0xFF)).WithAlpha((byte)(line.Opacity * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)line.Width * scale, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
			canvas.DrawPath(path, p);
		}

		var fallbackMarker = new SKColor(0xDC, 0x26, 0x26);
		foreach (var m in request.Markers)
		{
			var pt = Project(new Coordinate(m.Location.Longitude, m.Location.Latitude), viewport);
			var metrics = MarkerMetrics.For(m.Scale, scale);
			var markerColor = MapColors.Parse(m.Color, fallbackMarker);

			if (!string.IsNullOrWhiteSpace(m.Icon))
			{
				if (sprites is not null && sprites.TryGet(m.Icon, out var icon))
				{
					DrawSpriteMarker(canvas, sprites, icon, pt, metrics, m.Label);
					continue;
				}

				// Fall back to a pin, but never silently: an icon that was asked for and not drawn is the
				// caller's business (issue #12).
				logger.LogWarning(
					"Marker icon '{Icon}' is not in the map style's sprite sheet; drawing the default pin instead.",
					m.Icon);
			}

			using var pin = BuildPinPath(pt.X, pt.Y, metrics);
			using var body = new SKPaint { Color = markerColor, IsAntialias = true, Style = SKPaintStyle.Fill };
			using var outline = new SKPaint
			{
				Color = SKColors.White,
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = Math.Max(1f, metrics.Width * 0.09f),
				StrokeJoin = SKStrokeJoin.Round
			};
			// Outline first, body over it: a centred stroke drawn last would eat into the sharp tip, so
			// the pin would stop short of the coordinate it is meant to point at.
			canvas.DrawPath(pin, outline);
			canvas.DrawPath(pin, body);

			DrawMarkerLabel(canvas, m.Label, markerColor, pt.X, metrics.HeadCenterY(pt.Y), metrics);
		}
	}

	/// <summary>
	/// Draws a named sprite icon in place of the pin, centred on the coordinate. These sprites are point
	/// glyphs rather than pins, so the coordinate is their centre - the same placement a MapLibre client
	/// gives them. A label, if supplied, goes underneath with a halo so it does not obscure the glyph.
	/// </summary>
	private static void DrawSpriteMarker(SKCanvas canvas, SpriteSheet sprites, SpriteIcon icon, SKPoint anchor, MarkerMetrics metrics, string? label)
	{
		var width = icon.LogicalWidth * metrics.ScaleFactor;
		var height = icon.LogicalHeight * metrics.ScaleFactor;
		var destination = new SKRect(anchor.X - (width / 2f), anchor.Y - (height / 2f), anchor.X + (width / 2f), anchor.Y + (height / 2f));

		using var paint = new SKPaint { IsAntialias = true };
		canvas.DrawBitmap(sprites.Atlas, icon.Source, destination, SpriteSampling, paint);

		if (string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		using var font = new SKFont { Size = Math.Max(9f, height * 0.5f), Embolden = true };
		using var halo = new SKPaint
		{
			Color = new SKColor(0xFF, 0xFF, 0xFF, 0xE0),
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = Math.Max(1.5f, height * 0.12f),
			StrokeJoin = SKStrokeJoin.Round
		};
		using var text = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
		var baseline = destination.Bottom + font.Size;
		canvas.DrawText(label.Trim(), anchor.X, baseline, SKTextAlign.Center, font, halo);
		canvas.DrawText(label.Trim(), anchor.X, baseline, SKTextAlign.Center, font, text);
	}

	/// <summary>
	/// Builds the teardrop pin: a circular head with a tapering tail down to the anchor, unioned so the
	/// white outline traces the silhouette rather than cutting a chord across the head. The tail's base
	/// sits below the head centre, which is what makes it read as a pin - drawing it above the centre
	/// hid it inside the head and left a bare dot (issue #9).
	/// </summary>
	private static SKPath BuildPinPath(float anchorX, float anchorY, MarkerMetrics metrics)
	{
		var radius = metrics.HeadRadius;
		var headCenterY = metrics.HeadCenterY(anchorY);

		using var headBuilder = new SKPathBuilder();
		headBuilder.AddCircle(anchorX, headCenterY, radius);
		using var head = headBuilder.Detach();

		using var tailBuilder = new SKPathBuilder();
		tailBuilder.MoveTo(anchorX - (radius * 0.80f), headCenterY + (radius * 0.60f));
		tailBuilder.LineTo(anchorX, metrics.TipY(anchorY));
		tailBuilder.LineTo(anchorX + (radius * 0.80f), headCenterY + (radius * 0.60f));
		tailBuilder.Close();
		using var tail = tailBuilder.Detach();

		if (head.Op(tail, SKPathOp.Union) is { } union)
		{
			return union;
		}

		// Path arithmetic is optional in Skia builds; falling back to both subpaths still draws a pin,
		// with a faint chord where they meet.
		using var combined = new SKPathBuilder();
		combined.AddPath(head);
		combined.AddPath(tail);
		return combined.Detach();
	}

	/// <summary>
	/// Draws a marker's label inside the pin head, sized from the head rather than the whole pin.
	/// Google suppresses labels on its two smallest sizes; this renderer draws them at every size on
	/// purpose, so a small marker still carries its identity in a report.
	/// </summary>
	private static void DrawMarkerLabel(SKCanvas canvas, string? label, SKColor markerColor, float cx, float headCenterY, MarkerMetrics metrics)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		// Contrast against the pin fill: dark text on light fills (white/yellow), white on dark fills.
		var luminance = (0.299 * markerColor.Red) + (0.587 * markerColor.Green) + (0.114 * markerColor.Blue);
		var textColor = luminance > 150 ? SKColors.Black : SKColors.White;

		var text = label.Trim();
		using var font = new SKFont { Size = metrics.LabelFontSize, Embolden = true };

		// A multi-character label would otherwise overflow the head; shrink it to fit the way a map pin
		// has to, rather than letting it spill over the outline.
		var maxWidth = metrics.HeadRadius * 1.6f;
		var measured = font.MeasureText(text);
		if (measured > maxWidth && measured > 0)
		{
			font.Size *= maxWidth / measured;
		}

		using var paint = new SKPaint { Color = textColor, IsAntialias = true };
		canvas.DrawText(text, cx, headCenterY + (font.Size * 0.36f), SKTextAlign.Center, font, paint);
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
