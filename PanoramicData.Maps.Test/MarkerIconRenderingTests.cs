using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PanoramicData.Maps;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// Issue #12: <c>icon:</c> was parsed and then silently ignored, so a caller asking for an icon got a
/// default pin and no indication that its request had been dropped.
/// </summary>
public class MarkerIconRenderingTests
{
	private static readonly SKColor IconMagenta = new(0xFF, 0x00, 0xFF);
	private static readonly SKColor PinRed = new(0xDC, 0x26, 0x26);

	private const string IndexJson = """
		{ "cafe": { "x": 0, "y": 0, "width": 16, "height": 16, "pixelRatio": 1 } }
		""";

	/// <summary>A 16x16 atlas holding one solid magenta 'cafe' icon - a colour no pin or map fill uses.</summary>
	private static byte[] AtlasPng()
	{
		using var bmp = new SKBitmap(16, 16);
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(IconMagenta);
		using var image = SKImage.FromBitmap(bmp);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}

	/// <summary>404s every tile, serves the style and the sprite sheet.</summary>
	private sealed class SpriteAndNoTilesHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var url = request.RequestUri!.ToString();

			if (url.EndsWith("style.json", StringComparison.Ordinal))
			{
				return Ok(new StringContent("{ \"version\": 8, \"sprite\": \"https://tiles.example/sprites/light\" }", Encoding.UTF8, "application/json"));
			}

			if (url.EndsWith("sprites/light.json", StringComparison.Ordinal))
			{
				return Ok(new StringContent(IndexJson, Encoding.UTF8, "application/json"));
			}

			if (url.EndsWith("sprites/light.png", StringComparison.Ordinal))
			{
				return Ok(new ByteArrayContent(AtlasPng()));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		private static Task<HttpResponseMessage> Ok(HttpContent content)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
	}

	private sealed class CapturingLogger : ILogger<SkiaSharpMapRenderer>
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			=> Entries.Add((logLevel, formatter(state, exception)));
	}

	private static SkiaSharpMapRenderer CreateRenderer(ILogger<SkiaSharpMapRenderer>? logger = null)
	{
		var http = new HttpClient(new SpriteAndNoTilesHandler());
		var options = Options.Create(new MapsOptions { TilesStyleUrl = "https://tiles.example/style.json" });
		var sprites = new SpriteSheetProvider(http, NullLogger<SpriteSheetProvider>.Instance);
		return new SkiaSharpMapRenderer(http, options, logger ?? new CapturingLogger(), sprites);
	}

	private static MapRequest Request(string? icon, string? label = null) => new()
	{
		Center = new GeoPoint(-0.1278, 51.5074),
		Zoom = 12,
		Width = 160,
		Height = 160,
		Markers = [new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "#dc2626", Icon = icon, Label = label }]
	};

	private static bool ContainsColor(SKBitmap bitmap, SKColor color, int tolerance = 8)
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
	public async Task NamedIcon_IsDrawnInsteadOfThePin()
	{
		var image = await CreateRenderer().RenderAsync(Request("cafe"), TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, IconMagenta).Should().BeTrue("the sprite icon should have been drawn");
		ContainsColor(bmp, PinRed).Should().BeFalse("an icon replaces the pin rather than being drawn on top of it");
	}

	[Fact]
	public async Task NamedIcon_IsCentredOnItsCoordinate()
	{
		// These sprites are point glyphs, not pins, so the coordinate is the centre of the icon.
		var image = await CreateRenderer().RenderAsync(Request("cafe"), TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		bmp.GetPixel(bmp.Width / 2, bmp.Height / 2).Should().Be(IconMagenta);
		bmp.GetPixel(2, 2).Should().NotBe(IconMagenta, "the icon is 16 pixels, not the whole image");
	}

	[Fact]
	public async Task UnknownIconName_FallsBackToThePinAndSaysSo()
	{
		var logger = new CapturingLogger();

		var image = await CreateRenderer(logger).RenderAsync(Request("no_such_icon"), TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, PinRed).Should().BeTrue("a marker must still be drawn");
		logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("no_such_icon", StringComparison.Ordinal));
	}

	[Fact]
	public async Task IconWithALabel_DrawsBoth()
	{
		var plain = await CreateRenderer().RenderAsync(Request("cafe"), TestContext.Current.CancellationToken);
		var labelled = await CreateRenderer().RenderAsync(Request("cafe", "Kiosk"), TestContext.Current.CancellationToken);

		using var withLabel = SKBitmap.Decode(labelled.Bytes);
		ContainsColor(withLabel, IconMagenta).Should().BeTrue("the icon is still drawn");
		labelled.Bytes.SequenceEqual(plain.Bytes).Should().BeFalse("the label should have been drawn as well");
	}

	[Fact]
	public async Task NoIcon_StillDrawsThePinWithoutTouchingTheSpriteSheet()
	{
		var image = await CreateRenderer().RenderAsync(Request(icon: null), TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, PinRed).Should().BeTrue();
		ContainsColor(bmp, IconMagenta).Should().BeFalse();
	}

	[Fact]
	public async Task RendererWithoutASpriteProvider_FallsBackToThePin()
	{
		// The renderer is usable without the provider (its constructor parameter is optional), and must
		// then behave as it did before named icons existed.
		var http = new HttpClient(new SpriteAndNoTilesHandler());
		var renderer = new SkiaSharpMapRenderer(
			http,
			Options.Create(new MapsOptions { TilesStyleUrl = "https://tiles.example/style.json" }),
			NullLogger<SkiaSharpMapRenderer>.Instance);

		var image = await renderer.RenderAsync(Request("cafe"), TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, PinRed).Should().BeTrue();
	}
}
