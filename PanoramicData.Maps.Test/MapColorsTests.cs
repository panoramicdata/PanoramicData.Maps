using AwesomeAssertions;
using PanoramicData.Maps;
using SkiaSharp;
using Xunit;

namespace PanoramicData.Maps.Test;

public class MapColorsTests
{
	[Theory]
	[InlineData("red", 0xFF, 0x00, 0x00, 0xFF)]
	[InlineData("blue", 0x00, 0x00, 0xFF, 0xFF)]
	[InlineData("green", 0x00, 0x80, 0x00, 0xFF)]
	[InlineData("0xff0000", 0xFF, 0x00, 0x00, 0xFF)]
	[InlineData("0x0000FFFF", 0x00, 0x00, 0xFF, 0xFF)]
	[InlineData("0x00ff0080", 0x00, 0xFF, 0x00, 0x80)]
	[InlineData("#00ff00", 0x00, 0xFF, 0x00, 0xFF)]
	[InlineData("#f00", 0xFF, 0x00, 0x00, 0xFF)]
	public void TryParse_ParsesSupportedFormats(string input, byte r, byte g, byte b, byte a)
	{
		MapColors.TryParse(input, out var c).Should().BeTrue();
		c.Red.Should().Be(r);
		c.Green.Should().Be(g);
		c.Blue.Should().Be(b);
		c.Alpha.Should().Be(a);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("notacolour")]
	[InlineData("0xZZZ")]
	[InlineData("#12")]
	public void TryParse_RejectsInvalid(string? input)
		=> MapColors.TryParse(input, out _).Should().BeFalse();

	[Fact]
	public void Parse_ReturnsFallbackOnInvalid()
		=> MapColors.Parse("nope", SKColors.Magenta).Should().Be(SKColors.Magenta);
}
