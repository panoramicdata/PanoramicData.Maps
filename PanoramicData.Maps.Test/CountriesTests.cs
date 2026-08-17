using AwesomeAssertions;
using Xunit;

namespace PanoramicData.Maps.Test;

public class CountriesTests
{
	[Theory]
	[InlineData("USA", "United States")]
	[InlineData("UK", "United Kingdom")]
	[InlineData("US", "United States")]
	[InlineData("GB", "United Kingdom")]
	[InlineData("UAE", "United Arab Emirates")]
	[InlineData("DE", "Germany")]
	[InlineData("FR", "France")]
	[InlineData(" us ", "United States")]
	public void ResolveName_RewritesCodesAndAliases(string input, string expected)
		=> Countries.ResolveName(input).Should().Be(expected);

	[Theory]
	[InlineData("Paris")]
	[InlineData("London")]
	[InlineData("New York")]
	[InlineData("")]
	[InlineData(null)]
	public void ResolveName_LeavesOrdinaryQueriesAlone(string? input)
		=> Countries.ResolveName(input).Should().BeNull();

	[Theory]
	[InlineData("GB", "GBR")]
	[InlineData("UK", "GBR")]
	[InlineData("GBR", "GBR")]
	[InlineData("United Kingdom", "GBR")]
	[InlineData("US", "USA")]
	[InlineData("USA", "USA")]
	[InlineData("FR", "FRA")]
	[InlineData("France", "FRA")]
	public void ResolveAlpha3_ResolvesCodesNamesAndAliases(string input, string expected)
		=> Countries.ResolveAlpha3(input).Should().Be(expected);

	[Theory]
	[InlineData("ZZ")]
	[InlineData("XYZ")]
	[InlineData("Atlantis")]
	[InlineData(null)]
	public void ResolveAlpha3_ReturnsNull_ForUnknown(string? input)
		=> Countries.ResolveAlpha3(input).Should().BeNull();
}
