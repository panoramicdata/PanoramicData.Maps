using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PanoramicData.Maps;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// Issue #13: land is a data layer (<c>earth</c>). The renderer used to clear the canvas to the land
/// colour and rely on water polygons covering the sea, so any tile that failed to load fabricated
/// land - the same symptom as issue #10 reached by a different route.
/// </summary>
public class EarthLayerRenderingTests
{
	private static readonly SKColor Earth = new(0xE2, 0xDF, 0xDA);
	private static readonly SKColor NoData = new(0xCC, 0xCC, 0xCC);
	private static readonly SKColor Water = new(0xA0, 0xC8, 0xF0);
	private static readonly SKColor OldLandBackground = new(0xF2, 0xEF, 0xE9);

	private sealed class CapturedTileHandler(int zoom, int x, int y) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (!request.RequestUri!.AbsolutePath.EndsWith($"/planet/{zoom}/{x}/{y}.mvt", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			var file = Path.Combine(AppContext.BaseDirectory, "TestData", $"planet-{zoom}-{x}-{y}.mvt");
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) });
		}
	}

	private sealed class NoTilesHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
	}

	private sealed class CapturingLogger : ILogger<SkiaSharpMapRenderer>
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			=> Entries.Add((logLevel, formatter(state, exception)));
	}

	private static SkiaSharpMapRenderer CreateRenderer(HttpMessageHandler handler, ILogger<SkiaSharpMapRenderer>? logger = null)
		=> new(
			new HttpClient(handler),
			Options.Create(new MapsOptions { TilesStyleUrl = "https://tiles.example/style.json" }),
			logger ?? new CapturingLogger());

	private static bool ContainsColor(SKBitmap bitmap, SKColor color, int tolerance = 4)
	{
		for (var y = 0; y < bitmap.Height; y++)
		{
			for (var x = 0; x < bitmap.Width; x++)
			{
				var px = bitmap.GetPixel(x, y);
				if (Math.Abs(px.Red - color.Red) <= tolerance
					&& Math.Abs(px.Green - color.Green) <= tolerance
					&& Math.Abs(px.Blue - color.Blue) <= tolerance)
				{
					return true;
				}
			}
		}

		return false;
	}

	[Fact]
	public async Task RenderAsync_DrawsLandFromTheEarthLayer()
	{
		// Tile z5/14/12 is ocean containing Madeira, so it has both an 'earth' polygon and an 'ocean' one.
		var renderer = CreateRenderer(new CapturedTileHandler(5, 14, 12));
		var request = new MapRequest { Center = new GeoPoint(-16.9, 32.75), Zoom = 5, Width = 300, Height = 220 };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, Earth).Should().BeTrue("Madeira comes from the tile's 'earth' layer");
		ContainsColor(bmp, Water).Should().BeTrue("the ocean around it comes from the 'water' layer");
	}

	[Fact]
	public async Task RenderAsync_WithNoTiles_DrawsNeitherLandNorSea()
	{
		// The point of the fix: a failed tile fetch must not invent land (or sea) where there is no data.
		var renderer = CreateRenderer(new NoTilesHandler());
		var request = new MapRequest { Center = new GeoPoint(-16.9, 32.75), Zoom = 5, Width = 120, Height = 120 };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		bmp.GetPixel(2, 2).Should().Be(NoData, "an area with no tile is drawn as no-data, not as land");
		ContainsColor(bmp, OldLandBackground).Should().BeFalse("the land colour must come from data, never from the background");
		ContainsColor(bmp, Earth).Should().BeFalse("no earth polygon was available to draw");
	}

	[Fact]
	public async Task RenderAsync_LogsOnceWhenTilesCannotBeFetched()
	{
		// Real tiles never 404 for lack of coverage - every request to the tile service returns data - so
		// a failed fetch is a fault worth reporting rather than an expected gap.
		var logger = new CapturingLogger();
		var renderer = CreateRenderer(new NoTilesHandler(), logger);
		var request = new MapRequest { Center = new GeoPoint(-16.9, 32.75), Zoom = 5, Width = 600, Height = 600 };

		await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
		warnings.Should().ContainSingle("one summary per render, not one line per tile");
		warnings[0].Message.Should().Contain("could not be fetched");
	}

	[Fact]
	public async Task RenderAsync_DoesNotLogWhenEveryTileArrives()
	{
		var logger = new CapturingLogger();
		var renderer = CreateRenderer(new CapturedTileHandler(5, 14, 12), logger);

		// A window well inside the single available tile, so no other tile is requested.
		var request = new MapRequest { Center = new GeoPoint(-16.9, 36.4), Zoom = 5, Width = 200, Height = 200 };
		await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
	}
}
