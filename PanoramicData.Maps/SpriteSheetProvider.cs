using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace PanoramicData.Maps;

/// <summary>
/// Fetches and caches the map style's sprite sheet, so named marker icons (issue #12) can be drawn
/// without contacting any host beyond the configured tile service, and without re-fetching the atlas
/// for every rendered map.
/// </summary>
public sealed class SpriteSheetProvider(HttpClient httpClient, ILogger<SpriteSheetProvider> logger)
{
	private readonly HttpClient _httpClient = httpClient;
	private readonly ConcurrentDictionary<string, Task<SpriteSheet?>> _cache = new(StringComparer.Ordinal);

	/// <summary>
	/// The sprite sheet for a style, or <see langword="null"/> when the style declares none or it
	/// cannot be fetched - in which case markers fall back to drawing a pin rather than failing.
	/// </summary>
	/// <param name="styleUrl">The MapLibre style JSON URL, used to discover the sprite URL.</param>
	/// <param name="spriteUrlOverride">An explicit sprite base URL, which skips reading the style.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The sheet, or <see langword="null"/>.</returns>
	public Task<SpriteSheet?> GetAsync(string styleUrl, string? spriteUrlOverride, CancellationToken cancellationToken = default)
	{
		var key = string.IsNullOrWhiteSpace(spriteUrlOverride) ? $"style:{styleUrl}" : $"sprite:{spriteUrlOverride}";

		// The task itself is cached, so concurrent first callers share one fetch rather than racing.
		return _cache.GetOrAdd(key, _ => LoadAsync(styleUrl, spriteUrlOverride, cancellationToken));
	}

	private async Task<SpriteSheet?> LoadAsync(string styleUrl, string? spriteUrlOverride, CancellationToken cancellationToken)
	{
		try
		{
			var spriteBase = string.IsNullOrWhiteSpace(spriteUrlOverride)
				? await DiscoverSpriteUrlAsync(styleUrl, cancellationToken).ConfigureAwait(false)
				: spriteUrlOverride.TrimEnd('/');

			if (string.IsNullOrWhiteSpace(spriteBase))
			{
				return null;
			}

			var indexUrl = spriteBase + ".json";
			using var index = await _httpClient.GetAsync(indexUrl, cancellationToken).ConfigureAwait(false);
			if (!index.IsSuccessStatusCode)
			{
				logger.LogWarning("Sprite index {Url} returned {StatusCode}; named marker icons are unavailable.", indexUrl, (int)index.StatusCode);
				return null;
			}

			var icons = ParseIndex(await index.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
			if (icons.Count == 0)
			{
				logger.LogWarning("Sprite index {Url} contained no usable icons.", indexUrl);
				return null;
			}

			var atlasUrl = spriteBase + ".png";
			using var atlasResponse = await _httpClient.GetAsync(atlasUrl, cancellationToken).ConfigureAwait(false);
			if (!atlasResponse.IsSuccessStatusCode)
			{
				logger.LogWarning("Sprite atlas {Url} returned {StatusCode}; named marker icons are unavailable.", atlasUrl, (int)atlasResponse.StatusCode);
				return null;
			}

			var atlasBytes = await atlasResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
			var atlas = SKBitmap.Decode(atlasBytes);
			if (atlas is null)
			{
				logger.LogWarning("Sprite atlas {Url} could not be decoded; named marker icons are unavailable.", atlasUrl);
				return null;
			}

			logger.LogInformation("Loaded {Count} sprite icons from {Url}.", icons.Count, spriteBase);
			return new SpriteSheet(atlas, icons);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Failed to load the sprite sheet; named marker icons are unavailable.");
			return null;
		}
	}

	private async Task<string?> DiscoverSpriteUrlAsync(string styleUrl, CancellationToken cancellationToken)
	{
		using var response = await _httpClient.GetAsync(styleUrl, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			logger.LogWarning("Style {Url} returned {StatusCode}; named marker icons are unavailable.", styleUrl, (int)response.StatusCode);
			return null;
		}

		using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
		if (!document.RootElement.TryGetProperty("sprite", out var sprite))
		{
			return null;
		}

		// MapLibre allows 'sprite' to be a string or an array of {id, url}; take the first URL either way.
		return sprite.ValueKind switch
		{
			JsonValueKind.String => sprite.GetString()?.TrimEnd('/'),
			JsonValueKind.Array when sprite.GetArrayLength() > 0 && sprite[0].TryGetProperty("url", out var first)
				=> first.GetString()?.TrimEnd('/'),
			_ => null,
		};
	}

	private static Dictionary<string, SpriteIcon> ParseIndex(string json)
	{
		var icons = new Dictionary<string, SpriteIcon>(StringComparer.OrdinalIgnoreCase);
		using var document = JsonDocument.Parse(json);
		if (document.RootElement.ValueKind != JsonValueKind.Object)
		{
			return icons;
		}

		foreach (var property in document.RootElement.EnumerateObject())
		{
			if (property.Value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var x = Int(property.Value, "x");
			var y = Int(property.Value, "y");
			var width = Int(property.Value, "width");
			var height = Int(property.Value, "height");
			if (width <= 0 || height <= 0)
			{
				continue;
			}

			var pixelRatio = property.Value.TryGetProperty("pixelRatio", out var ratio) && ratio.TryGetDouble(out var value) && value > 0
				? (float)value
				: 1f;

			icons[property.Name] = new SpriteIcon(new SKRectI(x, y, x + width, y + height), pixelRatio);
		}

		return icons;
	}

	private static int Int(JsonElement element, string name)
		=> element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
}
