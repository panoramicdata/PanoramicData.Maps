using AwesomeAssertions;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

public class SkiaSharpMapRendererTests
{
	// Returns 404 for every tile, so the renderer draws the no-data background + overlays only -
	// enough to exercise projection, overlay drawing and PNG encoding without a real tile server.
	private static SkiaSharpMapRenderer CreateRenderer()
		=> TestRenderers.Create(new StatusOnlyHandler());

	[Fact]
	public async Task RenderAsync_ProducesPngOfRequestedSize()
	{
		var renderer = CreateRenderer();
		var request = new MapRequest { Center = new GeoPoint(-0.1278, 51.5074), Zoom = 12, Width = 200, Height = 150 };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		image.ContentType.Should().Be("image/png");
		using var bmp = SKBitmap.Decode(image.Bytes);
		bmp.Should().NotBeNull();
		bmp.Width.Should().Be(200);
		bmp.Height.Should().Be(150);

		// No tile could be fetched here, so the corner is the no-data colour. It is deliberately neither
		// land nor sea: painting the background land-coloured invented land in the ocean (issue #13).
		bmp.GetPixel(1, 1).Should().Be(new SKColor(0xCC, 0xCC, 0xCC));
	}

	[Fact]
	public async Task RenderAsync_HonoursScale()
	{
		var renderer = CreateRenderer();
		var request = new MapRequest { Center = new GeoPoint(0, 0), Zoom = 4, Width = 100, Height = 100, Scale = 2 };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		bmp.Width.Should().Be(200);
		bmp.Height.Should().Be(200);
	}

	[Fact]
	public async Task RenderAsync_DrawsMarker()
	{
		var renderer = CreateRenderer();
		var request = new MapRequest
		{
			Center = new GeoPoint(-0.1278, 51.5074),
			Zoom = 12,
			Width = 200,
			Height = 200,
			Markers = [new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "red" }]
		};

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		PixelProbe.ContainsStrongRed(bmp).Should().BeTrue("the red marker should have been drawn");
	}

	[Fact]
	public async Task RenderAsync_DrawsMarkerLabel()
	{
		var renderer = CreateRenderer();
		var baseRequest = new MapRequest
		{
			Center = new GeoPoint(-0.1278, 51.5074),
			Zoom = 12,
			Width = 200,
			Height = 200,
			Markers = [new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "yellow", Scale = 3 }]
		};
		var labelled = baseRequest with
		{
			Markers = [new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "yellow", Scale = 3, Label = "A" }]
		};

		var plain = await renderer.RenderAsync(baseRequest, TestContext.Current.CancellationToken);
		var withLabel = await renderer.RenderAsync(labelled, TestContext.Current.CancellationToken);

		// Drawing the label must change the output; a yellow pin gets dark ("A") pixels it lacked before.
		withLabel.Bytes.SequenceEqual(plain.Bytes).Should().BeFalse("the marker label should have been rendered");
	}

	[Fact]
	public async Task RenderAsync_ShadesNamedRegion()
	{
		var renderer = CreateRenderer();
		var request = new MapRequest
		{
			Center = new GeoPoint(2.5, 46.5), // France
			Zoom = 5,
			Width = 400,
			Height = 300,
			Regions = [new RegionSpec { Code = "FR", FillColor = "red", FillOpacity = 1.0 }]
		};

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		PixelProbe.ContainsStrongRed(bmp).Should().BeTrue("France should have been shaded red from the embedded boundary data");
	}

	[Fact]
	public async Task RenderAsync_JpegFormat()
	{
		var renderer = CreateRenderer();
		var request = new MapRequest { Center = new GeoPoint(0, 0), Zoom = 2, Width = 64, Height = 64, Format = MapImageFormat.Jpeg };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		image.ContentType.Should().Be("image/jpeg");
		using var bmp = SKBitmap.Decode(image.Bytes);
		bmp.Width.Should().Be(64);
	}
}
