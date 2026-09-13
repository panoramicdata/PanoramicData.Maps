using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// Draws everything the caller asked for on top of the base map - shaded regions, polygons, paths and
/// markers - plus the attribution the tile data's licence requires. These are the parts of the image
/// the caller controls, as opposed to what the tiles happen to contain.
/// </summary>
internal static class MapOverlays
{
	/// <summary>
	/// Sampling for sprite icons. They are drawn at a fractional scale factor, so nearest-neighbour
	/// (SkiaSharp's default when no options are given) would alias the glyph edges; linear filtering
	/// matches what a MapLibre client does with the same atlas.
	/// </summary>
	private static readonly SKSamplingOptions SpriteSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

	/// <summary>Shades the named countries, beneath the caller's other overlays.</summary>
	public static void DrawRegions(SKCanvas canvas, MapRequest request, Viewport viewport)
	{
		foreach (var region in request.Regions)
		{
			var alpha3 = Countries.ResolveAlpha3(region.Code);
			if (alpha3 is null || !RegionBoundaries.TryGet(alpha3, out var geometry))
			{
				continue; // the parser already rejects unknown/boundary-less codes with a 400
			}

			using var path = MapProjection.ToPath(geometry, viewport);
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

	/// <summary>Draws the caller's polygons, paths and markers, in that order.</summary>
	public static void Draw(SKCanvas canvas, MapRequest request, Viewport viewport, ILogger logger, SpriteSheet? sprites)
	{
		var scale = viewport.Scale;

		foreach (var poly in request.Polygons)
		{
			using var path = MapProjection.BuildLinePath([.. poly.Points.Select(p => new Coordinate(p.Longitude, p.Latitude))], viewport, close: true);
			using var fp = new SKPaint { Color = MapColors.Parse(poly.FillColor, new SKColor(0xF5, 0x9E, 0x0B)).WithAlpha((byte)(poly.FillOpacity * 255)), IsAntialias = true, Style = SKPaintStyle.Fill };
			canvas.DrawPath(path, fp);
			using var lp = new SKPaint { Color = MapColors.Parse(poly.StrokeColor, new SKColor(0xF5, 0x9E, 0x0B)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)poly.StrokeWidth * scale };
			canvas.DrawPath(path, lp);
		}

		foreach (var line in request.Paths)
		{
			using var path = MapProjection.BuildLinePath([.. line.Points.Select(p => new Coordinate(p.Longitude, p.Latitude))], viewport, close: false);
			using var p = new SKPaint { Color = MapColors.Parse(line.Color, new SKColor(0x00, 0x00, 0xFF)).WithAlpha((byte)(line.Opacity * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)line.Width * scale, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
			canvas.DrawPath(path, p);
		}

		DrawMarkers(canvas, request, viewport, logger, sprites);
	}

	private static void DrawMarkers(SKCanvas canvas, MapRequest request, Viewport viewport, ILogger logger, SpriteSheet? sprites)
	{
		var fallbackMarker = new SKColor(0xDC, 0x26, 0x26);
		foreach (var m in request.Markers)
		{
			var pt = MapProjection.Project(new Coordinate(m.Location.Longitude, m.Location.Latitude), viewport);
			var metrics = MarkerMetrics.For(m.Scale, viewport.Scale);
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

			DrawPin(canvas, pt, metrics, markerColor);
			DrawMarkerLabel(canvas, m.Label, markerColor, pt.X, metrics.HeadCenterY(pt.Y), metrics);
		}
	}

	private static void DrawPin(SKCanvas canvas, SKPoint anchor, MarkerMetrics metrics, SKColor markerColor)
	{
		using var pin = BuildPinPath(anchor.X, anchor.Y, metrics);
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

	/// <summary>Draws the OpenStreetMap attribution the tile data's licence requires.</summary>
	public static void DrawAttribution(SKCanvas canvas, int width, int height, int scale)
	{
		const string text = "© OpenStreetMap";
		using var font = new SKFont { Size = 11f * scale };
		using var bg = new SKPaint { Color = new SKColor(255, 255, 255, 190), IsAntialias = true };
		using var fg = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
		var w = font.MeasureText(text);
		canvas.DrawRect(width - w - 8 * scale, height - 16f * scale, w + 8 * scale, 16f * scale, bg);
		canvas.DrawText(text, width - w - 4 * scale, height - 4f * scale, SKTextAlign.Left, font, fg);
	}
}
