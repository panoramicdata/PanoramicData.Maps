using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

public class MarkerMetricsTests
{
	[Fact]
	public void Default_MatchesTheGoogleStaticMapsPinFootprint()
	{
		var metrics = MarkerMetrics.For(markerScale: 1, imageScale: 1);

		metrics.Width.Should().Be(22, "a Google Static Maps 'normal' pin is 22 CSS pixels wide");
		metrics.Height.Should().Be(40, "...and 40 tall - it is a pin, not a dot");
		metrics.HeadRadius.Should().Be(11);
	}

	[Fact]
	public void TipSitsOnTheAnchor_AndTheBodyIsEntirelyAboveIt()
	{
		// Google anchors a pin at the bottom of its point, so the coordinate is where the pin points.
		var metrics = MarkerMetrics.For(1, 1);

		metrics.TipY(anchorY: 100).Should().Be(100);
		metrics.TopY(anchorY: 100).Should().Be(60);
		metrics.HeadCenterY(anchorY: 100).Should().Be(71, "the head is a circle resting at the top of the pin");
	}

	[Fact]
	public void ImageScale_MultipliesEveryDimension()
	{
		var retina = MarkerMetrics.For(markerScale: 1, imageScale: 2);

		retina.Width.Should().Be(44);
		retina.Height.Should().Be(80);
		retina.HeadRadius.Should().Be(22);
	}

	[Fact]
	public void MarkerScale_MultipliesEveryDimension()
	{
		var half = MarkerMetrics.For(markerScale: 0.5, imageScale: 1);

		half.Width.Should().Be(11);
		half.Height.Should().Be(20);
	}

	[Fact]
	public void TheFourGoogleSizes_ProduceStrictlyIncreasingPins()
	{
		// 'mid' used to map to the same scale as 'normal', so three of Google's four sizes were two.
		var heights = new[] { "tiny", "small", "mid", "normal" }
			.Select(size => MarkerMetrics.For(MarkerMetrics.ScaleForSize(size), 1).Height)
			.ToArray();

		heights.Should().BeInAscendingOrder();
		heights.Distinct().Should().HaveCount(4, "each documented size should be visibly different");
		heights[^1].Should().Be(40);
	}

	[Fact]
	public void UnknownSize_FallsBackToNormal()
		=> MarkerMetrics.ScaleForSize("enormous").Should().Be(1.0);

	[Fact]
	public void LabelFontSize_FitsTheHeadAndIsLegibleAtTheDefaultSize()
	{
		var metrics = MarkerMetrics.For(1, 1);

		metrics.LabelFontSize.Should().BeInRange(12, 16, "a single-character label should fill the head without spilling out of it");
		metrics.LabelFontSize.Should().BeLessThan(metrics.HeadRadius * 2);
	}
}
