using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

public class StaticMapRequestParserTests
{
	private static Dictionary<string, IReadOnlyList<string>> Q(params (string Key, string Value)[] pairs)
	{
		var d = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var g in pairs.GroupBy(p => p.Key))
		{
			d[g.Key] = g.Select(p => p.Value).ToArray();
		}

		return d;
	}

	private static readonly MapsOptions Options = new();

	[Fact]
	public void Parse_CenterAsLatLng_SetsCenterLonLat()
	{
		StaticMapRequestParser.TryParse(Q(("center", "51.5074,-0.1278"), ("zoom", "12")), Options, out var r, out var err).Should().BeTrue();
		err.Should().BeNull();
		r.Center.Should().NotBeNull();
		r.Center!.Value.Latitude.Should().BeApproximately(51.5074, 1e-6);
		r.Center.Value.Longitude.Should().BeApproximately(-0.1278, 1e-6);
		r.Location.Should().BeNull();
		r.Zoom.Should().Be(12);
	}

	[Fact]
	public void Parse_CenterAsPlace_DefersToLocation()
	{
		StaticMapRequestParser.TryParse(Q(("center", "London"), ("zoom", "10")), Options, out var r, out _).Should().BeTrue();
		r.Center.Should().BeNull();
		r.Location.Should().Be("London");
	}

	[Fact]
	public void Parse_Size_And_Format_And_Scale()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("size", "640x480"), ("format", "jpg"), ("scale", "2")), Options, out var r, out _).Should().BeTrue();
		r.Width.Should().Be(640);
		r.Height.Should().Be(480);
		r.Format.Should().Be(MapImageFormat.Jpeg);
		r.Scale.Should().Be(2);
	}

	[Fact]
	public void Parse_Size_ClampedToLimits()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("size", "99999x99999")), Options, out var r, out _).Should().BeTrue();
		r.Width.Should().Be(Options.MaxWidth);
		r.Height.Should().Be(Options.MaxHeight);
	}

	[Fact]
	public void Parse_MarkerGroup_StyledWithMultipleLocations()
	{
		StaticMapRequestParser.TryParse(Q(("markers", "color:blue|label:A|51.5,-0.12|52.0,0.0")), Options, out var r, out _).Should().BeTrue();
		r.Markers.Should().HaveCount(2);
		r.Markers[0].Color.Should().Be("blue");
		r.Markers[0].Label.Should().Be("A");
		r.Markers[0].Location.Latitude.Should().BeApproximately(51.5, 1e-6);
		r.Markers[1].Location.Longitude.Should().BeApproximately(0.0, 1e-6);
	}

	[Fact]
	public void Parse_MarkerSize_MapsToScale()
	{
		StaticMapRequestParser.TryParse(Q(("markers", "size:tiny|51.5,-0.12")), Options, out var r, out _).Should().BeTrue();
		r.Markers.Should().ContainSingle();
		r.Markers[0].Scale.Should().Be(0.5);
	}

	[Fact]
	public void Parse_MultipleMarkerParams_ProduceSeparateGroups()
	{
		StaticMapRequestParser.TryParse(Q(("markers", "color:red|51,0"), ("markers", "color:green|52,1")), Options, out var r, out _).Should().BeTrue();
		r.Markers.Should().HaveCount(2);
		r.Markers.Select(m => m.Color).Should().BeEquivalentTo(["red", "green"]);
	}

	[Fact]
	public void Parse_Path_WithoutFill_IsPolyline()
	{
		StaticMapRequestParser.TryParse(Q(("path", "color:0xff0000ff|weight:4|51,0|52,1")), Options, out var r, out _).Should().BeTrue();
		r.Paths.Should().ContainSingle();
		r.Polygons.Should().BeEmpty();
		r.Paths[0].Points.Should().HaveCount(2);
		r.Paths[0].Width.Should().Be(4);
	}

	[Fact]
	public void Parse_Path_WithFillColor_IsPolygon()
	{
		StaticMapRequestParser.TryParse(Q(("path", "fillcolor:green|color:red|51,0|52,1|52,0")), Options, out var r, out _).Should().BeTrue();
		r.Polygons.Should().ContainSingle();
		r.Paths.Should().BeEmpty();
		r.Polygons[0].Points.Should().HaveCount(3);
		r.Polygons[0].FillColor.Should().Be("green");
	}

	[Fact]
	public void Parse_NoRenderableContent_Fails()
	{
		StaticMapRequestParser.TryParse(Q(("zoom", "12")), Options, out _, out var err).Should().BeFalse();
		err.Should().NotBeNullOrEmpty();
	}

	[Theory]
	[InlineData("51.5,-0.12", true)]
	[InlineData("London", false)]
	[InlineData("91,0", false)]
	[InlineData("0,181", false)]
	public void TryLatLng_ValidatesRange(string input, bool expected)
		=> StaticMapRequestParser.TryLatLng(input, out _).Should().Be(expected);
}
