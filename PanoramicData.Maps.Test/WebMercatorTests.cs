using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

public class WebMercatorTests
{
	[Theory]
	[InlineData(0, 512)]
	[InlineData(1, 1024)]
	[InlineData(2, 2048)]
	[InlineData(10, 512 * 1024)]
	public void WorldSize_ScalesWithZoom(int zoom, double expected)
		=> WebMercator.WorldSize(zoom).Should().Be(expected);

	[Theory]
	[InlineData(-180, 0)]
	[InlineData(0, 256)]
	[InlineData(180, 512)]
	[InlineData(90, 384)]
	public void LongitudeToX_MapsAcrossTheWorld(double lon, double expectedX)
		=> WebMercator.LongitudeToX(lon, 512).Should().BeApproximately(expectedX, 1e-9);

	[Fact]
	public void LatitudeToY_EquatorIsCentre()
		=> WebMercator.LatitudeToY(0, 512).Should().BeApproximately(256, 1e-9);

	[Fact]
	public void LatitudeToY_NorthIsAboveSouth()
		=> WebMercator.LatitudeToY(51.5, 512).Should().BeLessThan(WebMercator.LatitudeToY(-51.5, 512));

	[Fact]
	public void LatitudeToY_ClampsBeyondMercatorLimit()
	{
		var y = WebMercator.LatitudeToY(90, 512);
		y.Should().BeInRange(-0.001, 1); // clamped near the top, still finite (not -infinity)
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 1)]
	[InlineData(2, 3)]
	[InlineData(15, 32767)]
	public void MaxTileIndex_Is2PowZoomMinus1(int zoom, int expected)
		=> WebMercator.MaxTileIndex(zoom).Should().Be(expected);
}
