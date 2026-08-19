using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PanoramicData.Maps;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// Measures the drawn marker, because issue #9 is about what the pin looks like: it had collapsed to
/// a small circle, the tail hidden inside the head, roughly 18x19 where Google draws 22x40.
/// </summary>
public class MarkerRenderingTests
{
	private sealed class NoTilesHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

	private static SkiaSharpMapRenderer CreateRenderer()
		=> new(
			new HttpClient(new NoTilesHandler()),
			Options.Create(new MapsOptions { TilesStyleUrl = "https://tiles.example/style.json" }),
			NullLogger<SkiaSharpMapRenderer>.Instance);

	private static bool IsMarkerRed(SKColor px) => px.Red > 180 && px.Green < 80 && px.Blue < 80;

	private static SKRectI MarkerBounds(SKBitmap bitmap)
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		for (var y = 0; y < bitmap.Height; y++)
		{
			for (var x = 0; x < bitmap.Width; x++)
			{
				if (!IsMarkerRed(bitmap.GetPixel(x, y)))
				{
					continue;
				}

				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}

		return minX == int.MaxValue ? SKRectI.Empty : new SKRectI(minX, minY, maxX + 1, maxY + 1);
	}

	private static async Task<SKBitmap> RenderCentredMarkerAsync(int scale = 1, double markerScale = 1)
	{
		var centre = new GeoPoint(-0.1278, 51.5074);
		var request = new MapRequest
		{
			Center = centre,
			Zoom = 12,
			Width = 200,
			Height = 200,
			Scale = scale,
			Markers = [new MarkerSpec { Location = centre, Color = "red", Scale = markerScale }]
		};

		var image = await CreateRenderer().RenderAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
		return SKBitmap.Decode(image.Bytes);
	}

	[Fact]
	public async Task Marker_IsDrawnAsATallPinRatherThanASmallDot()
	{
		using var bmp = await RenderCentredMarkerAsync();

		var bounds = MarkerBounds(bmp);
		bounds.Should().NotBe(SKRectI.Empty, "the marker should have been drawn");
		bounds.Width.Should().BeInRange(20, 24, "a 'normal' pin is 22 pixels wide");
		bounds.Height.Should().BeInRange(37, 42, "a 'normal' pin is 40 pixels tall");
		bounds.Height.Should().BeGreaterThan((int)(bounds.Width * 1.5), "a pin is markedly taller than it is wide");
	}

	[Fact]
	public async Task Marker_PointsAtItsCoordinate()
	{
		using var bmp = await RenderCentredMarkerAsync();

		// The marker sits at the map centre, so its anchor is the middle pixel.
		var anchorX = bmp.Width / 2;
		var anchorY = bmp.Height / 2;
		var bounds = MarkerBounds(bmp);

		// The taper narrows to a point, so its last two rows are antialiased below the "strong red"
		// threshold used here. The silhouette still has to cover the anchor pixel, which is asserted
		// separately below rather than by loosening this bound further.
		bounds.Bottom.Should().BeInRange(anchorY - 3, anchorY + 2, "the pin's tip is its anchor");
		bmp.GetPixel(anchorX, anchorY).Should().NotBe(new SKColor(0xF2, 0xEF, 0xE9), "the pin covers the coordinate it points at");
		bounds.Top.Should().BeLessThan(anchorY - 30, "the body of the pin is above the anchor");
		bounds.MidX.Should().BeInRange(anchorX - 2, anchorX + 2, "the pin is horizontally centred on its anchor");
	}

	[Fact]
	public async Task Marker_HonoursTheImageScale()
	{
		using var single = await RenderCentredMarkerAsync(scale: 1);
		using var retina = await RenderCentredMarkerAsync(scale: 2);

		MarkerBounds(retina).Height.Should().BeInRange(
			(int)(MarkerBounds(single).Height * 1.8),
			(int)(MarkerBounds(single).Height * 2.2),
			"an @2x image draws the pin at twice the pixel size");
	}

	[Fact]
	public async Task SmallerMarkerScale_DrawsASmallerPin()
	{
		using var normal = await RenderCentredMarkerAsync(markerScale: 1);
		using var tiny = await RenderCentredMarkerAsync(markerScale: MarkerMetrics.ScaleForSize("tiny"));

		MarkerBounds(tiny).Height.Should().BeLessThan(MarkerBounds(normal).Height);
		MarkerBounds(tiny).Height.Should().BeGreaterThan(8, "even the smallest pin must remain visible");
	}
}
