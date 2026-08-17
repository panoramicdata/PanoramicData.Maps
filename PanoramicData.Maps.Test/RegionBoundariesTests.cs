using AwesomeAssertions;
using Xunit;

namespace PanoramicData.Maps.Test;

public class RegionBoundariesTests
{
	[Theory]
	[InlineData("GBR")]
	[InlineData("USA")]
	[InlineData("DEU")]
	[InlineData("gbr")] // case-insensitive
	public void TryGet_ReturnsGeometry_ForKnownCountries(string alpha3)
	{
		RegionBoundaries.TryGet(alpha3, out var geometry).Should().BeTrue();
		geometry.IsEmpty.Should().BeFalse();
	}

	[Theory]
	[InlineData("FRA")] // France: ISO_A3 and ISO_A3_EH are both -99 in this dataset - must still resolve via adm0_a3
	[InlineData("NOR")] // Norway: same -99 gotcha
	public void TryGet_HandlesMinus99IsoCodes(string alpha3)
	{
		RegionBoundaries.TryGet(alpha3, out var geometry).Should().BeTrue($"{alpha3} must be keyed on adm0_a3, not ISO_A3");
		geometry.IsEmpty.Should().BeFalse();
	}

	[Theory]
	[InlineData("ZZZ")]
	[InlineData("")]
	public void TryGet_ReturnsFalse_ForUnknown(string alpha3)
		=> RegionBoundaries.TryGet(alpha3, out _).Should().BeFalse();
}
