using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// One icon within a sprite sheet.
/// </summary>
/// <param name="Source">The icon's rectangle within the atlas image, in atlas pixels.</param>
/// <param name="PixelRatio">
/// The atlas pixels per logical pixel. A <c>@2x</c> sheet describes a 20-pixel-wide icon that is
/// meant to occupy 10 logical pixels.
/// </param>
public readonly record struct SpriteIcon(SKRectI Source, float PixelRatio)
{
	/// <summary>The icon's width in logical pixels.</summary>
	public float LogicalWidth => Source.Width / Math.Max(PixelRatio, 0.01f);

	/// <summary>The icon's height in logical pixels.</summary>
	public float LogicalHeight => Source.Height / Math.Max(PixelRatio, 0.01f);
}

/// <summary>
/// A MapLibre sprite sheet: one atlas image plus an index of named icons within it. Used to draw
/// named marker icons (issue #12) from the same style the base map is drawn from, so no host outside
/// the configured tile service is ever contacted.
/// </summary>
public sealed class SpriteSheet : IDisposable
{
	private readonly Dictionary<string, SpriteIcon> _icons;

	internal SpriteSheet(SKBitmap atlas, Dictionary<string, SpriteIcon> icons)
	{
		Atlas = atlas;
		_icons = icons;
	}

	/// <summary>The atlas image every icon is cut from.</summary>
	public SKBitmap Atlas { get; }

	/// <summary>The names of the available icons, in alphabetical order.</summary>
	public IReadOnlyCollection<string> Names => [.. _icons.Keys.OrderBy(name => name, StringComparer.Ordinal)];

	/// <summary>Looks up an icon by name, case-insensitively.</summary>
	/// <param name="name">The icon name, for example <c>cafe</c>.</param>
	/// <param name="icon">The icon's placement within the atlas, when found.</param>
	/// <returns><see langword="true"/> when the sheet contains the icon.</returns>
	public bool TryGet(string? name, out SpriteIcon icon)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			icon = default;
			return false;
		}

		return _icons.TryGetValue(name.Trim(), out icon);
	}

	/// <inheritdoc />
	public void Dispose() => Atlas.Dispose();
}
