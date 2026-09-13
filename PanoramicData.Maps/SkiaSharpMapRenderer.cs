using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// Renders maps natively (no headless browser): fetches Mapbox Vector Tiles from the tile service,
/// decodes them, projects to Web-Mercator screen space and draws them with SkiaSharp, then draws the
/// requested overlays. Draws point place-name labels from the tiles' label layers (issue #1) with
/// halos and greedy collision avoidance; full MapLibre style-JSON fidelity (curved labels, road
/// shields) remains a later milestone.
/// <para>
/// This type owns the base map - fetching tiles and painting their layers. Projection lives in
/// <see cref="MapProjection"/>, place names in <see cref="MapPlaceLabels"/>, and everything the caller
/// asked to be drawn on top in <see cref="MapOverlays"/>.
/// </para>
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

	/// <summary>The land fill, from the reference style's <c>earth</c> layer.</summary>
	private static readonly SKColor EarthColor = new(0xE2, 0xDF, 0xDA);

	/// <summary>
	/// Drawn where no tile could be fetched. The reference style's own background colour, chosen because
	/// it reads as neither land nor sea - a missing tile should look like missing data, not like geography.
	/// </summary>
	private static readonly SKColor NoDataColor = new(0xCC, 0xCC, 0xCC);

	/// <summary>Identifies one vector tile in the slippy-map scheme.</summary>
	private sealed record TileAddress(int X, int Y, int Zoom);

	/// <summary>
	/// How one source layer is painted. A null colour means that pass is skipped, so a layer can be
	/// fill-only (water), stroke-only (boundaries) or stroked over a wider casing (roads).
	/// </summary>
	private sealed record LayerPaint(SKColor? Fill = null, SKColor? Stroke = null, float StrokeWidth = 1f, SKColor? Casing = null);

	/// <inheritdoc />
	public async Task<MapImage> RenderAsync(MapRequest request, CancellationToken cancellationToken)
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

		var viewport = new Viewport(world, left, top, scale, width, height);
		var labels = new List<LabelCandidate>();
		await DrawBaseMapAsync(canvas, styleUrl, zoom, viewport, labels, cancellationToken).ConfigureAwait(false);

		// Only reach for the sprite sheet when a marker actually asks for an icon: most maps do not, and
		// the atlas is a separate fetch (cached thereafter).
		SpriteSheet? sprites = null;
		if (_spriteSheetProvider is not null && request.Markers.Any(marker => !string.IsNullOrWhiteSpace(marker.Icon)))
		{
			sprites = await _spriteSheetProvider.GetAsync(new SpriteSheetRequest(styleUrl, _options.SpriteUrl), cancellationToken).ConfigureAwait(false);
		}

		MapOverlays.DrawRegions(canvas, request, viewport);
		MapPlaceLabels.Draw(canvas, labels, scale);
		MapOverlays.Draw(canvas, request, viewport, _logger, sprites);
		MapOverlays.DrawAttribution(canvas, width, height, scale);

		using var image = surface.Snapshot();
		var isPng = request.Format == MapImageFormat.Png;
		using var data = image.Encode(isPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, isPng ? 100 : 85);
		return new MapImage(data.ToArray(), isPng ? "image/png" : "image/jpeg");
	}

	/// <summary>
	/// Draws every tile the image covers, collecting label candidates as it goes. Tiles that fail are
	/// counted rather than thrown: one missing tile should leave a no-data patch, not lose the map.
	/// </summary>
	private async Task DrawBaseMapAsync(
		SKCanvas canvas,
		string styleUrl,
		int zoom,
		Viewport viewport,
		List<LabelCandidate> labels,
		CancellationToken cancellationToken)
	{
		var maxTile = WebMercator.MaxTileIndex(zoom);
		var txMin = (int)Math.Floor(viewport.Left / WebMercator.TileSize);
		var txMax = (int)Math.Floor((viewport.Left + viewport.Width) / WebMercator.TileSize);
		var tyMin = Math.Clamp((int)Math.Floor(viewport.Top / WebMercator.TileSize), 0, maxTile);
		var tyMax = Math.Clamp((int)Math.Floor((viewport.Top + viewport.Height) / WebMercator.TileSize), 0, maxTile);

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
	}

	/// <summary>Fetches and draws one tile. Returns false when the tile could not be fetched or was empty.</summary>
	private async Task<bool> DrawTileAsync(SKCanvas canvas, string styleUrl, TileAddress tile, Viewport viewport, List<LabelCandidate> labels, CancellationToken ct)
	{
		var url = TileUrl(styleUrl, tile.Zoom, tile.X, tile.Y);
		var bytes = await FetchTileAsync(url, ct).ConfigureAwait(false);
		if (bytes is null || bytes.Length == 0)
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

		MapPlaceLabels.Collect(vectorTile, viewport, labels);
		return true;
	}

	/// <summary>
	/// Fetches one tile, or <see langword="null"/> when it could not be fetched. A tile failure is logged
	/// at debug and reported to the caller as an absence; cancellation still propagates.
	/// </summary>
	private async Task<byte[]?> FetchTileAsync(string url, CancellationToken ct)
	{
		try
		{
			using var resp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
			{
				_logger.LogDebug("Tile {Url} returned {StatusCode}", url, (int)resp.StatusCode);
				return null;
			}

			return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "Failed to fetch tile {Url}", url);
			return null;
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

				var path = MapProjection.ToPath(feature.Geometry, viewport);
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

	private static void DrawLayer(SKCanvas canvas, NetTopologySuite.IO.VectorTiles.VectorTile tile, string[] layerNames,
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
				var path = MapProjection.ToPath(feature.Geometry, viewport);
				if (path is null)
				{
					continue;
				}

				using (path)
				{
					DrawFeature(canvas, path, paint);
				}
			}
		}
	}

	/// <summary>Paints one feature's path: fill, then casing, then stroke over the casing.</summary>
	private static void DrawFeature(SKCanvas canvas, SKPath path, LayerPaint paint)
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

	private static string TileUrl(string styleUrl, int z, int x, int y)
	{
		var baseUrl = styleUrl.Replace("/style.json", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
		return $"{baseUrl}/planet/{z}/{x}/{y}.mvt";
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
