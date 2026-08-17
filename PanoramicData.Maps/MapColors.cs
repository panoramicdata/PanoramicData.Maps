using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// Parses colours in the formats accepted by the static-map API: Google Static Maps named colours
/// and <c>0xRRGGBB[AA]</c>, plus CSS <c>#RGB</c>/<c>#RRGGBB</c>/<c>#RRGGBBAA</c> and named CSS colours.
/// </summary>
public static class MapColors
{
	// The 12 named colours Google Static Maps supports.
	private static readonly Dictionary<string, SKColor> Named = new(StringComparer.OrdinalIgnoreCase)
	{
		["black"] = new(0x00, 0x00, 0x00),
		["brown"] = new(0xA5, 0x2A, 0x2A),
		["green"] = new(0x00, 0x80, 0x00),
		["purple"] = new(0x80, 0x00, 0x80),
		["yellow"] = new(0xFF, 0xFF, 0x00),
		["blue"] = new(0x00, 0x00, 0xFF),
		["gray"] = new(0x80, 0x80, 0x80),
		["grey"] = new(0x80, 0x80, 0x80),
		["orange"] = new(0xFF, 0xA5, 0x00),
		["red"] = new(0xFF, 0x00, 0x00),
		["white"] = new(0xFF, 0xFF, 0xFF)
	};

	/// <summary>
	/// Parses a colour, returning <paramref name="fallback"/> if it cannot be understood.
	/// </summary>
	/// <param name="value">The colour string.</param>
	/// <param name="fallback">Colour to use when parsing fails.</param>
	/// <returns>The parsed colour.</returns>
	public static SKColor Parse(string? value, SKColor fallback)
		=> TryParse(value, out var c) ? c : fallback;

	/// <summary>
	/// Attempts to parse a colour.
	/// </summary>
	/// <param name="value">The colour string (named, <c>0xRRGGBB[AA]</c>, or <c>#…</c>).</param>
	/// <param name="color">The parsed colour on success.</param>
	/// <returns><see langword="true"/> if parsed.</returns>
	public static bool TryParse(string? value, out SKColor color)
	{
		color = SKColors.Black;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var v = value.Trim();

		if (Named.TryGetValue(v, out var named))
		{
			color = named;
			return true;
		}

		// Normalise 0x-prefixed hex (Google) to bare hex.
		var hex = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v[2..]
			: v.StartsWith('#') ? v[1..]
			: v;

		if (!IsHex(hex))
		{
			return false;
		}

		switch (hex.Length)
		{
			case 6: // RRGGBB
				color = new SKColor(B(hex, 0), B(hex, 2), B(hex, 4));
				return true;
			case 8: // RRGGBBAA (Google / CSS8 order: alpha last)
				color = new SKColor(B(hex, 0), B(hex, 2), B(hex, 4), B(hex, 6));
				return true;
			case 3: // RGB shorthand
				color = new SKColor(Nyb(hex, 0), Nyb(hex, 1), Nyb(hex, 2));
				return true;
			default:
				return false;
		}
	}

	private static bool IsHex(string s) => s.Length > 0 && s.All(Uri.IsHexDigit);

	private static byte B(string hex, int i) => Convert.ToByte(hex.Substring(i, 2), 16);

	private static byte Nyb(string hex, int i)
	{
		var n = Convert.ToByte(hex.Substring(i, 1), 16);
		return (byte)(n << 4 | n);
	}
}
