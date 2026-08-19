using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// The fill rules are transcribed from the protomaps-light style JSON that the tile service itself
/// serves, so these tests pin our renderer to the reference a MapLibre client would apply.
/// </summary>
public class BaseMapStyleTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	public void LanduseParkKinds_AreNotPaintedAtZoom6AndBelow(double zoom)
	{
		// Issue #10: a marine nature_reserve was painted land-green over the ocean at z4/z5.
		// The reference style ramps fill-opacity from 0 at z6 to 1 at z11.
		BaseMapStyle.FillFor("landuse", "nature_reserve", zoom).Should().BeNull();
		BaseMapStyle.FillFor("landuse", "protected_area", zoom).Should().BeNull();
		BaseMapStyle.FillFor("landuse", "national_park", zoom).Should().BeNull();
		BaseMapStyle.FillFor("landuse", "park", zoom).Should().BeNull();
	}

	[Fact]
	public void LanduseParkKinds_AreFullyOpaqueAtZoom11AndAbove()
	{
		var fill = BaseMapStyle.FillFor("landuse", "nature_reserve", 11);

		fill.Should().NotBeNull();
		fill!.Value.Alpha.Should().Be(255);
		fill.Value.Red.Should().Be(0x9C);
		fill.Value.Green.Should().Be(0xD3);
		fill.Value.Blue.Should().Be(0xB4);

		BaseMapStyle.FillFor("landuse", "nature_reserve", 15)!.Value.Alpha.Should().Be(255);
	}

	[Fact]
	public void LanduseParkKinds_RampBetweenZoom6And11()
	{
		var half = BaseMapStyle.FillFor("landuse", "park", 8.5);

		half.Should().NotBeNull();
		half!.Value.Alpha.Should().BeInRange(120, 135, "zoom 8.5 is half way between the 0 and 1 stops");
	}

	[Theory]
	[InlineData("wood", 0xA0, 0xD9, 0xA0)]
	[InlineData("grass", 0x99, 0xD2, 0xBB)]
	[InlineData("glacier", 0xE7, 0xE7, 0xE7)]
	[InlineData("sand", 0xE2, 0xE0, 0xD7)]
	[InlineData("military", 0xC6, 0xDC, 0xDC)]
	public void LanduseKinds_UseTheReferenceColours(string kind, byte r, byte g, byte b)
	{
		var fill = BaseMapStyle.FillFor("landuse", kind, 12);

		fill.Should().NotBeNull();
		(fill!.Value.Red, fill.Value.Green, fill.Value.Blue).Should().Be((r, g, b));
	}

	[Theory]
	[InlineData("hospital", 0xE4, 0xDA, 0xD9)]
	[InlineData("industrial", 0xD1, 0xDD, 0xE1)]
	[InlineData("university", 0xE4, 0xDE, 0xD7)]
	[InlineData("beach", 0xE8, 0xE4, 0xD0)]
	[InlineData("aerodrome", 0xDA, 0xDB, 0xDF)]
	[InlineData("pier", 0xE0, 0xE0, 0xE0)]
	public void LanduseServiceKinds_ArePaintedAtEveryZoom(string kind, byte r, byte g, byte b)
	{
		// These reference layers carry no zoom ramp, so they are painted wherever the tile offers them.
		foreach (var zoom in new double[] { 5, 12, 15 })
		{
			var fill = BaseMapStyle.FillFor("landuse", kind, zoom);
			fill.Should().NotBeNull($"'{kind}' has a fill rule at zoom {zoom}");
			(fill!.Value.Red, fill.Value.Green, fill.Value.Blue).Should().Be((r, g, b));
			fill.Value.Alpha.Should().Be(255);
		}
	}

	[Theory]
	[InlineData("residential")]
	[InlineData("commercial")]
	[InlineData("railway")]
	[InlineData("other")]
	[InlineData("recreation_ground")]
	[InlineData(null)]
	public void UnstyledLanduseKinds_AreNotPainted(string? kind)
	{
		// 'residential' is the single most common landuse kind in a city tile and the reference style
		// gives it no fill at all - it stays the earth colour. Painting it green made towns look rural.
		BaseMapStyle.FillFor("landuse", kind, 12).Should().BeNull();
	}

	[Fact]
	public void Landcover_IsPaintedAtLowZoomAndFadesOutByZoom7()
	{
		// The inverse ramp of landuse: landcover is the coarse global cover, shown only when zoomed out.
		BaseMapStyle.FillFor("landcover", "farmland", 4).Should().NotBeNull();
		BaseMapStyle.FillFor("landcover", "farmland", 5)!.Value.Alpha.Should().Be(255);
		BaseMapStyle.FillFor("landcover", "farmland", 6)!.Value.Alpha.Should().BeInRange(120, 135);
		BaseMapStyle.FillFor("landcover", "farmland", 7).Should().BeNull();
		BaseMapStyle.FillFor("landcover", "farmland", 12).Should().BeNull();
	}

	[Theory]
	[InlineData("grassland", 210, 239, 207)]
	[InlineData("barren", 255, 243, 215)]
	[InlineData("urban_area", 230, 230, 230)]
	[InlineData("farmland", 216, 239, 210)]
	[InlineData("glacier", 255, 255, 255)]
	[InlineData("scrub", 234, 239, 210)]
	[InlineData("forest", 196, 231, 210)]
	public void LandcoverKinds_UseTheReferenceColours(string kind, byte r, byte g, byte b)
	{
		var fill = BaseMapStyle.FillFor("landcover", kind, 5);

		fill.Should().NotBeNull();
		(fill!.Value.Red, fill.Value.Green, fill.Value.Blue).Should().Be((r, g, b));
	}

	[Fact]
	public void UnknownLayer_IsNotPainted()
	{
		// 'natural' is not a layer in this tile schema at all; it used to be painted green regardless.
		BaseMapStyle.FillFor("natural", "wood", 12).Should().BeNull();
		BaseMapStyle.FillFor("something_else", "park", 12).Should().BeNull();
	}
}
