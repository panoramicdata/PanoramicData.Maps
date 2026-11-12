using System.Globalization;

namespace PanoramicData.Maps;

public static partial class StaticMapRequestParser
{
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
