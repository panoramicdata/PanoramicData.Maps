using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PanoramicData.Maps;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// Renders real captured vector tiles, so the reproduction for issue #10 is the data that actually
/// produced it rather than a synthetic approximation. See <c>TestData/README.md</c>.
/// </summary>
public class RealTileRenderingTests
{
	/// <summary>The flat green every landuse polygon used to be filled with, at every zoom.</summary>
	private static readonly SKColor OldFlatLandGreen = new(0xD6, 0xE3, 0xCE);

	/// <summary>The reference style's colour for park-like kinds (park, forest, nature reserve...).</summary>
	private static readonly SKColor ParkGreen = new(0x9C, 0xD3, 0xB4);

	/// <summary>The reference style's colour for the <c>wood</c> kind.</summary>
	private static readonly SKColor WoodGreen = new(0xA0, 0xD9, 0xA0);

	private static readonly SKColor Water = new(0xA0, 0xC8, 0xF0);

	/// <summary>Serves one captured tile for its own coordinates, and 404 (as the tile service does) for the rest.</summary>
	private sealed class CapturedTileHandler(int zoom, int x, int y) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (!request.RequestUri!.AbsolutePath.EndsWith($"/planet/{zoom}/{x}/{y}.mvt", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			var file = Path.Combine(AppContext.BaseDirectory, "TestData", $"planet-{zoom}-{x}-{y}.mvt");
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(File.ReadAllBytes(file))
			});
		}
	}

	private static SkiaSharpMapRenderer CreateRenderer(int zoom, int x, int y)
		=> new(
			new HttpClient(new CapturedTileHandler(zoom, x, y)),
			Options.Create(new MapsOptions { TilesStyleUrl = "https://tiles.example/style.json" }),
			NullLogger<SkiaSharpMapRenderer>.Instance);

	private static bool ContainsColor(SKBitmap bitmap, SKColor color, int tolerance = 6)
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
	public async Task RenderAsync_MarineNatureReserveAtZoom5_DrawsOpenSeaWithNoLandFill()
	{
		// Issue #10. Tile z5/14/11 is open Atlantic west of Galicia: an ocean polygon, plus a marine
		// 'nature_reserve' MultiPolygon of two rings - a rectangle at 22.7W-22.1W / 43.4N-43.8N and a
		// larger polygon at 12.3W-11.2W / 42.4N-43.5N. The view is centred on the second one. Both were
		// filled with a land green, so open sea rendered as land-coloured blobs.
		var renderer = CreateRenderer(5, 14, 11);
		var request = new MapRequest { Center = new GeoPoint(-11.9, 42.9), Zoom = 5, Width = 400, Height = 300 };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, Water).Should().BeTrue("the ocean polygon in the tile should still be drawn");
		ContainsColor(bmp, OldFlatLandGreen).Should().BeFalse("no landuse polygon may be filled with the old flat land green");
		ContainsColor(bmp, ParkGreen).Should().BeFalse("park-like landuse kinds have zero opacity at zoom 6 and below");
	}

	[Fact]
	public async Task RenderAsync_LandAtZoom11_PaintsParkKindsInTheirReferenceColours()
	{
		// The same rules above the opacity ramp, proving the fix suppresses by zoom rather than by
		// dropping the layer. Tile z11/1001/689 is eastern Dartmoor: 36 'wood', 24 'forest', 22 'scrub'
		// and 12 'grassland' polygons, alongside 88 'meadow' and 27 'farmland' the reference does not
		// paint at all.
		var renderer = CreateRenderer(11, 1001, 689);
		var request = new MapRequest { Center = new GeoPoint(-3.955, 50.569), Zoom = 11, Width = 300, Height = 200 };

		var image = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

		using var bmp = SKBitmap.Decode(image.Bytes);
		ContainsColor(bmp, WoodGreen).Should().BeTrue("'wood' is painted #a0d9a0 at zoom 11");
		ContainsColor(bmp, ParkGreen).Should().BeTrue("'forest' is painted #9cd3b4 at zoom 11");

		// Deliberately no "the old green is absent" assertion here: antialiased edges of a park fill
		// against the land background pass through colours within a few units of it, so such an
		// assertion would fail for a legitimate reason. The zoom-5 test above is where absence is
		// meaningful, because there nothing should be painted at all.
	}
}
