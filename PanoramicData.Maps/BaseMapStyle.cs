using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// The polygon fill rules for the base map, transcribed from the protomaps-light style JSON that the
/// tile service serves alongside the tiles.
/// <para>
/// Styling a vector tile means styling each <em>feature</em> by its <c>kind</c>, not each layer by
/// its name. The reference style splits the single <c>landuse</c> source layer across a dozen fill
/// layers with different colours, and gives two of the groups zoom-dependent opacity: the park-like
/// kinds fade in between zoom 6 and 11, and the coarse global <c>landcover</c> fades out between
/// zoom 5 and 7. Ignoring both was issue #10 - a <em>marine</em> <c>nature_reserve</c> in the
/// Atlantic was filled with a land green at zoom 4 and 5, where a MapLibre client would have drawn
/// nothing at all, so the sea appeared to contain land.
/// </para>
/// </summary>
public static class BaseMapStyle
{
	// Kinds carried by the reference 'landuse_park' layer, which fades in from zoom 6 to zoom 11.
	private static readonly string[] ParkRampKinds =
	[
		"national_park", "park", "cemetery", "protected_area", "nature_reserve", "forest",
		"golf_course", "wood", "scrub", "grassland", "grass", "glacier", "sand", "military",
		"naval_base", "airfield"
	];

	/// <summary>
	/// The fill for one tile feature, or <see langword="null"/> when the reference style paints
	/// nothing for it at this zoom - either because the kind has no fill rule (for example
	/// <c>residential</c>, the commonest kind in a city tile, which stays the earth colour) or
	/// because its layer's opacity has ramped to zero.
	/// </summary>
	/// <param name="layerName">The tile layer name, for example <c>landuse</c> or <c>landcover</c>.</param>
	/// <param name="kind">The feature's <c>kind</c> attribute, if it has one.</param>
	/// <param name="zoom">The zoom level being rendered.</param>
	/// <returns>The fill colour, with the layer's zoom-dependent opacity already applied, or <see langword="null"/>.</returns>
	public static SKColor? FillFor(string layerName, string? kind, double zoom)
	{
		if (string.IsNullOrEmpty(kind))
		{
			return null;
		}

		return layerName switch
		{
			"landuse" => LanduseFill(kind, zoom),
			"landcover" => LandcoverFill(kind, zoom),
			_ => null,
		};
	}

	private static SKColor? LanduseFill(string kind, double zoom)
	{
		if (Array.IndexOf(ParkRampKinds, kind) >= 0)
		{
			// fill-opacity: interpolate(linear, zoom, 6 -> 0, 11 -> 1)
			var alpha = Alpha(Interpolate(zoom, 6, 0, 11, 1));
			return alpha == 0 ? null : ParkGroupColor(kind).WithAlpha(alpha);
		}

		return kind switch
		{
			// landuse_urban_green, drawn at a fixed 0.7 opacity.
			"allotments" or "village_green" or "playground" => new SKColor(0x9C, 0xD3, 0xB4, 179),
			"hospital" => new SKColor(0xE4, 0xDA, 0xD9),
			"industrial" => new SKColor(0xD1, 0xDD, 0xE1),
			"school" or "university" or "college" => new SKColor(0xE4, 0xDE, 0xD7),
			"beach" => new SKColor(0xE8, 0xE4, 0xD0),
			"zoo" => new SKColor(0xC6, 0xDC, 0xDC),
			"aerodrome" => new SKColor(0xDA, 0xDB, 0xDF),
			"runway" or "taxiway" => new SKColor(0xE9, 0xE9, 0xED),
			"pedestrian" or "dam" => new SKColor(0xE3, 0xE0, 0xD4),
			"pier" => new SKColor(0xE0, 0xE0, 0xE0),
			_ => null,
		};
	}

	private static SKColor ParkGroupColor(string kind) => kind switch
	{
		"national_park" or "park" or "cemetery" or "protected_area" or "nature_reserve" or "forest" or "golf_course"
			=> new SKColor(0x9C, 0xD3, 0xB4),
		"wood" => new SKColor(0xA0, 0xD9, 0xA0),
		"scrub" or "grassland" or "grass" => new SKColor(0x99, 0xD2, 0xBB),
		"glacier" => new SKColor(0xE7, 0xE7, 0xE7),
		"sand" => new SKColor(0xE2, 0xE0, 0xD7),
		"military" or "naval_base" or "airfield" => new SKColor(0xC6, 0xDC, 0xDC),
		_ => new SKColor(0xE2, 0xDF, 0xDA), // the reference style's fallback colour
	};

	private static SKColor? LandcoverFill(string kind, double zoom)
	{
		// fill-opacity: interpolate(linear, zoom, 5 -> 1, 7 -> 0)
		var alpha = Alpha(Interpolate(zoom, 5, 1, 7, 0));
		if (alpha == 0)
		{
			return null;
		}

		var color = kind switch
		{
			"grassland" => new SKColor(210, 239, 207),
			"barren" => new SKColor(255, 243, 215),
			"urban_area" => new SKColor(230, 230, 230),
			"farmland" => new SKColor(216, 239, 210),
			"glacier" => new SKColor(255, 255, 255),
			"scrub" => new SKColor(234, 239, 210),
			_ => new SKColor(196, 231, 210),
		};

		return color.WithAlpha(alpha);
	}

	/// <summary>
	/// MapLibre's <c>interpolate</c> with a <c>linear</c> curve over two stops, which clamps to the
	/// end values outside the stop range.
	/// </summary>
	private static double Interpolate(double zoom, double z0, double v0, double z1, double v1)
		=> zoom <= z0 ? v0
			: zoom >= z1 ? v1
			: v0 + ((v1 - v0) * ((zoom - z0) / (z1 - z0)));

	private static byte Alpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
}
