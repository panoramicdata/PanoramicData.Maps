namespace PanoramicData.Maps;

public static partial class StaticMapRequestParser
{
	/// <summary>
	/// Resolves the <c>style</c>/<c>maptype</c> selector to a style URL. A configured named style wins;
	/// the Google <c>roadmap</c>/<c>satellite</c>/<c>hybrid</c>/<c>terrain</c> values are accepted and
	/// alias to the default when not explicitly configured; anything else is rejected (issue #7).
	/// </summary>
	private static bool ResolveStyle(string? selector, MapsOptions options, out string? styleUrl, out string? error)
	{
		styleUrl = null;
		error = null;
		if (string.IsNullOrWhiteSpace(selector))
		{
			return true;
		}

		var name = selector.Trim();
		foreach (var kvp in options.Styles)
		{
			if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
			{
				styleUrl = kvp.Value;
				return true;
			}
		}

		switch (name.ToLowerInvariant())
		{
			case "roadmap":
			case "satellite": // no open global imagery - aliases to the road style
			case "hybrid":
			case "terrain": // achievable with open data, but aliases to default until a style is configured
				return true;
			default:
				error = $"Unknown map style '{name}'.";
				return false;
		}
	}


	private static MapImageFormat FormatOf(string? format)
		=> format is not null && (format.StartsWith("jpg", StringComparison.OrdinalIgnoreCase) || format.StartsWith("jpeg", StringComparison.OrdinalIgnoreCase))
			? MapImageFormat.Jpeg
			: MapImageFormat.Png;

	private static bool ParseScale(string? raw, MapsOptions options, out int scale, out string? error)
	{
		error = null;
		scale = 1;
		if (!TryInt(raw, out var s))
		{
			return true;
		}

		if (s > options.MaxScale)
		{
			error = $"scale {s} exceeds the maximum of {options.MaxScale}";
			return false;
		}

		scale = Math.Max(1, s);
		return true;
	}

	private static bool ParseSize(string? size, string? width, string? height, MapsOptions options, out int w, out int h, out string? error)
	{
		error = null;
		ReadRequestedSize(size, width, height, out w, out h);

		if (w > options.MaxWidth)
		{
			error = $"width {w} exceeds the maximum of {options.MaxWidth}";
			return false;
		}

		if (h > options.MaxHeight)
		{
			error = $"height {h} exceeds the maximum of {options.MaxHeight}";
			return false;
		}

		w = Math.Max(1, w);
		h = Math.Max(1, h);
		return true;
	}

	/// <summary>
	/// Reads the requested pixel dimensions from either the combined <c>size=WxH</c> form or the separate
	/// <c>width</c>/<c>height</c> parameters, leaving the defaults in place for anything unparseable.
	/// </summary>
	private static void ReadRequestedSize(string? size, string? width, string? height, out int w, out int h)
	{
		w = 800;
		h = 600;

		if (string.IsNullOrWhiteSpace(size))
		{
			if (int.TryParse(width, out var pw)) { w = pw; }
			if (int.TryParse(height, out var ph)) { h = ph; }
			return;
		}

		// Bound to Split(char[], StringSplitOptions) explicitly. The params overload reads as though it
		// might be one of the several near-identical ones - Split(char, int, StringSplitOptions) or
		// Split(string, StringSplitOptions) - and which one a reader assumes changes what they think the
		// arguments mean.
		var parts = size.Split(SizeSeparators, StringSplitOptions.None);
		if (parts.Length == 2 && int.TryParse(parts[0], out var sw) && int.TryParse(parts[1], out var sh))
		{
			w = sw;
			h = sh;
		}
	}
}
