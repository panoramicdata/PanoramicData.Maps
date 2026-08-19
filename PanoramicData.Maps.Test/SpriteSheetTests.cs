using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PanoramicData.Maps;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

public class SpriteSheetTests
{
	private const string IndexJson = """
		{
		  "cafe":   { "x": 0,  "y": 0, "width": 10, "height": 10, "pixelRatio": 1 },
		  "peak":   { "x": 10, "y": 0, "width": 10, "height": 10, "pixelRatio": 1 },
		  "retina": { "x": 0,  "y": 10, "width": 20, "height": 20, "pixelRatio": 2 }
		}
		""";

	/// <summary>A 20x30 atlas: red 'cafe' top-left, green 'peak' beside it, blue 'retina' beneath.</summary>
	private static byte[] AtlasPng()
	{
		using var bmp = new SKBitmap(20, 30);
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.Transparent);
		using var red = new SKPaint { Color = SKColors.Red };
		using var green = new SKPaint { Color = SKColors.Lime };
		using var blue = new SKPaint { Color = SKColors.Blue };
		canvas.DrawRect(0, 0, 10, 10, red);
		canvas.DrawRect(10, 0, 10, 10, green);
		canvas.DrawRect(0, 10, 20, 20, blue);
		using var image = SKImage.FromBitmap(bmp);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}

	private sealed class SpriteHandler : HttpMessageHandler
	{
		public int JsonRequests { get; private set; }

		public int PngRequests { get; private set; }

		public int StyleRequests { get; private set; }

		public string SpriteUrlInStyle { get; init; } = "https://tiles.example/sprites/v4/light";

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var url = request.RequestUri!.ToString();
			if (url.EndsWith("style.json", StringComparison.Ordinal))
			{
				StyleRequests++;
				return Ok(new StringContent($"{{ \"version\": 8, \"sprite\": \"{SpriteUrlInStyle}\" }}", Encoding.UTF8, "application/json"));
			}

			if (url.EndsWith(".json", StringComparison.Ordinal))
			{
				JsonRequests++;
				return Ok(new StringContent(IndexJson, Encoding.UTF8, "application/json"));
			}

			if (url.EndsWith(".png", StringComparison.Ordinal))
			{
				PngRequests++;
				return Ok(new ByteArrayContent(AtlasPng()));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		private static Task<HttpResponseMessage> Ok(HttpContent content)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
	}

	private static SpriteSheetProvider CreateProvider(HttpMessageHandler handler)
		=> new(new HttpClient(handler), NullLogger<SpriteSheetProvider>.Instance);

	[Fact]
	public async Task GetAsync_DiscoversTheSpriteUrlFromTheStyleJson()
	{
		var handler = new SpriteHandler();

		var sheet = await CreateProvider(handler).GetAsync("https://tiles.example/style.json", null, TestContext.Current.CancellationToken);

		sheet.Should().NotBeNull();
		handler.StyleRequests.Should().Be(1);
		sheet!.Names.Should().BeEquivalentTo(["cafe", "peak", "retina"]);
	}

	[Fact]
	public async Task GetAsync_UsesAnExplicitSpriteUrlWithoutReadingTheStyle()
	{
		var handler = new SpriteHandler();

		var sheet = await CreateProvider(handler).GetAsync("https://tiles.example/style.json", "https://tiles.example/sprites/v4/light", TestContext.Current.CancellationToken);

		sheet.Should().NotBeNull();
		handler.StyleRequests.Should().Be(0, "an explicit sprite URL needs no style lookup");
	}

	[Fact]
	public async Task GetAsync_FetchesEachResourceOnceAndThenServesFromCache()
	{
		// Every rendered map with an icon would otherwise re-fetch a 16 KB atlas.
		var handler = new SpriteHandler();
		var provider = CreateProvider(handler);

		for (var i = 0; i < 4; i++)
		{
			(await provider.GetAsync("https://tiles.example/style.json", null, TestContext.Current.CancellationToken)).Should().NotBeNull();
		}

		handler.StyleRequests.Should().Be(1);
		handler.JsonRequests.Should().Be(1);
		handler.PngRequests.Should().Be(1);
	}

	[Fact]
	public async Task TryGet_ResolvesIconsAndHonoursPixelRatio()
	{
		var sheet = (await CreateProvider(new SpriteHandler()).GetAsync("https://tiles.example/style.json", null, TestContext.Current.CancellationToken))!;

		sheet.TryGet("cafe", out var cafe).Should().BeTrue();
		cafe.Source.Width.Should().Be(10);
		cafe.LogicalWidth.Should().Be(10, "a pixelRatio of 1 means the source pixels are the logical size");

		sheet.TryGet("retina", out var retina).Should().BeTrue();
		retina.Source.Width.Should().Be(20);
		retina.LogicalWidth.Should().Be(10, "a pixelRatio of 2 means the icon is half its source size on screen");

		sheet.TryGet("no_such_icon", out _).Should().BeFalse();
	}

	[Fact]
	public async Task TryGet_IsCaseInsensitive()
	{
		var sheet = (await CreateProvider(new SpriteHandler()).GetAsync("https://tiles.example/style.json", null, TestContext.Current.CancellationToken))!;

		sheet.TryGet("CAFE", out _).Should().BeTrue("callers should not have to match the sprite sheet's casing");
	}

	private sealed class BrokenHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
	}

	[Fact]
	public async Task GetAsync_ReturnsNullWhenTheSpriteSheetIsUnavailable()
	{
		// A map must still render if the sprite sheet cannot be fetched; markers fall back to pins.
		var sheet = await CreateProvider(new BrokenHandler()).GetAsync("https://tiles.example/style.json", null, TestContext.Current.CancellationToken);

		sheet.Should().BeNull();
	}
}
