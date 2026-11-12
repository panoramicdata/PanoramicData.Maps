using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace PanoramicData.Maps.Test;

/// <summary>Answers every request with one fixed status and no body.</summary>
internal sealed class StatusOnlyHandler(HttpStatusCode status = HttpStatusCode.NotFound) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(new HttpResponseMessage(status));
	}
}

/// <summary>Serves one captured tile for its own coordinates, and 404 (as the tile service does) for the rest.</summary>
internal sealed class CapturedTileHandler(int zoom, int x, int y) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!request.RequestUri!.AbsolutePath.EndsWith($"/planet/{zoom}/{x}/{y}.mvt", StringComparison.Ordinal))
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		var file = Path.Combine(AppContext.BaseDirectory, "TestData", $"planet-{zoom}-{x}-{y}.mvt");
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) });
	}
}

internal sealed class CapturingLogger : ILogger<SkiaSharpMapRenderer>
{
	public List<(LogLevel Level, string Message, EventId EventId)> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		ArgumentNullException.ThrowIfNull(state);
		return null;
	}

	public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		=> Entries.Add((logLevel, formatter(state, exception), eventId));
}

internal static class TestRenderers
{
	public static SkiaSharpMapRenderer Create(HttpMessageHandler handler, ILogger<SkiaSharpMapRenderer>? logger = null)
		=> new(
			new HttpClient(handler),
			Options.Create(new MapsOptions { TilesStyleUrl = "https://tiles.example/style.json", FontPath = TestFonts.SansRegular }),
			logger ?? NullLogger<SkiaSharpMapRenderer>.Instance);
}

internal static class PixelProbe
{
	/// <summary>True when any pixel above the bottom <paramref name="skipBottomRows"/> rows satisfies the predicate.</summary>
	public static bool Any(SKBitmap bitmap, Func<SKColor, bool> predicate, int skipBottomRows = 0)
	{
		var height = Math.Max(1, bitmap.Height - skipBottomRows);
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < bitmap.Width; x++)
			{
				if (predicate(bitmap.GetPixel(x, y)))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool ContainsColor(SKBitmap bitmap, SKColor color, int tolerance, int skipBottomRows = 0)
		=> Any(
			bitmap,
			px => Math.Abs(px.Red - color.Red) <= tolerance
				&& Math.Abs(px.Green - color.Green) <= tolerance
				&& Math.Abs(px.Blue - color.Blue) <= tolerance,
			skipBottomRows);

	public static bool ContainsStrongRed(SKBitmap bitmap)
		=> Any(bitmap, px => px.Red > 200 && px.Green < 70 && px.Blue < 70);
}
