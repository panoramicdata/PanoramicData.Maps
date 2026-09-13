using System.Globalization;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>One place name that could be drawn, and how it ranks against the others competing for space.</summary>
/// <param name="Text">The name to draw.</param>
/// <param name="X">Device-pixel X of the place.</param>
/// <param name="Y">Device-pixel Y of the place.</param>
/// <param name="Size">Font size in device pixels.</param>
/// <param name="Importance">Higher wins a collision; combines the kind's rank with population.</param>
/// <param name="Bold">Whether the name is drawn bold.</param>
internal sealed record LabelCandidate(string Text, float X, float Y, float Size, double Importance, bool Bold);

/// <summary>
/// Collects point place-names from the tiles' label layers and draws the ones that fit (issue #1).
/// Placement is greedy rather than optimal: candidates are sorted by importance and each is drawn only
/// if its box misses everything already placed, which is what keeps a capital from being crowded out by
/// the villages around it.
/// </summary>
internal static class MapPlaceLabels
{
	private static readonly string[] LabelLayers = ["places", "place", "poi", "pois"];

	/// <summary>Adds every drawable place name in one tile to the running candidate list.</summary>
	public static void Collect(NetTopologySuite.IO.VectorTiles.VectorTile tile, Viewport viewport, List<LabelCandidate> labels)
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

	/// <summary>Draws the collected names, most important first, skipping any that would overlap.</summary>
	public static void Draw(SKCanvas canvas, List<LabelCandidate> labels, int scale)
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

	/// <summary>
	/// Turns one label-layer feature into a placement candidate, or <see langword="null"/> when it is not
	/// a named point inside the image. Importance combines the kind's rank with population, so a capital
	/// beats a village when the two collide.
	/// </summary>
	private static LabelCandidate? TryReadLabel(IFeature feature, Viewport viewport)
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

		var sp = MapProjection.Project(pt.Coordinate, viewport);
		if (IsOutsideImage(sp, viewport))
		{
			return null;
		}

		var kind = (feature.Attributes.GetOptionalValue("kind") ?? feature.Attributes.GetOptionalValue("class")) as string;
		var population = ToDouble(feature.Attributes.GetOptionalValue("population"));
		var (size, bold, kindBonus) = StyleForKind(kind, viewport.Scale);
		return new LabelCandidate(name, sp.X, sp.Y, size, kindBonus + population, bold);
	}

	/// <summary>Whether a projected point falls outside the image, and so has no label to place.</summary>
	private static bool IsOutsideImage(SKPoint point, Viewport viewport)
		=> point.X < 0 || point.Y < 0 || point.X > viewport.Width || point.Y > viewport.Height;

	private static (float Size, bool Bold, double KindBonus) StyleForKind(string? kind, int scale)
		=> (kind?.ToLowerInvariant()) switch
		{
			"country" => (15f * scale, true, 1e12),
			"region" or "state" or "province" => (13f * scale, true, 1e11),
			"city" or "locality" => (12f * scale, false, 1e6),
			"town" => (11f * scale, false, 1e5),
			_ => (10.5f * scale, false, 0),
		};

	/// <summary>
	/// Reads a tile attribute as a number. Vector tiles are loosely typed, so population can arrive as any
	/// numeric type or as text; anything unreadable counts as zero rather than failing the whole tile.
	/// </summary>
	private static double ToDouble(object? value) => value switch
	{
		null => 0,
		double d => d,
		float f => f,
		long l => l,
		int i => i,
		string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
		IConvertible c => SafeToDouble(c),
		_ => 0,
	};

	private static double SafeToDouble(IConvertible c)
	{
		try { return c.ToDouble(CultureInfo.InvariantCulture); }
		catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
	}
}
