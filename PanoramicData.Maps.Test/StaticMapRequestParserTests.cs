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
	public void Parse_Size_OverLimit_Rejected()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("size", "99999x99999")), Options, out _, out var err).Should().BeFalse();
		err.Should().Contain("width 99999").And.Contain($"maximum of {Options.MaxWidth}");
	}

	[Fact]
	public void Parse_HeightOverLimit_Rejected()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("size", $"100x{Options.MaxHeight + 1}")), Options, out _, out var err).Should().BeFalse();
		err.Should().Contain("height").And.Contain($"maximum of {Options.MaxHeight}");
	}

	[Fact]
	public void Parse_ScaleOverLimit_Rejected()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("scale", "3")), Options, out _, out var err).Should().BeFalse();
		err.Should().Be($"scale 3 exceeds the maximum of {Options.MaxScale}");
	}

	[Fact]
	public void Parse_SizeWithinLimits_Succeeds()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("size", "640x480")), Options, out var r, out _).Should().BeTrue();
		r.Width.Should().Be(640);
		r.Height.Should().Be(480);
	}

	[Fact]
	public void Parse_MapType_Terrain_ResolvesConfiguredStyle()
	{
		var options = new MapsOptions();
		options.Styles["terrain"] = "https://tiles.example/terrain.json";
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("maptype", "terrain")), options, out var r, out _).Should().BeTrue();
		r.StyleUrl.Should().Be("https://tiles.example/terrain.json");
	}

	[Theory]
	[InlineData("roadmap")]
	[InlineData("satellite")]
	[InlineData("hybrid")]
	[InlineData("terrain")]
	public void Parse_GoogleMapTypes_AreAccepted_AliasToDefault(string mapType)
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("maptype", mapType)), Options, out var r, out var err).Should().BeTrue();
		err.Should().BeNull();
		r.StyleUrl.Should().BeNull(); // aliases to the default configured style
	}

	[Fact]
	public void Parse_UnknownStyle_Rejected()
	{
		StaticMapRequestParser.TryParse(Q(("center", "0,0"), ("zoom", "3"), ("style", "nonsense")), Options, out _, out var err).Should().BeFalse();
		err.Should().Contain("Unknown map style 'nonsense'");
	}

	[Fact]
	public void Parse_Region_ParsesCodeFillOpacity()
	{
		StaticMapRequestParser.TryParse(Q(("region", "code:GB|fill:red|opacity:0.5")), Options, out var r, out _).Should().BeTrue();
		r.Regions.Should().ContainSingle();
		r.Regions[0].Code.Should().Be("GB");
		r.Regions[0].FillColor.Should().Be("red");
		r.Regions[0].FillOpacity.Should().Be(0.5);
	}

	[Fact]
	public void Parse_Region_UnknownCode_Rejected()
	{
		StaticMapRequestParser.TryParse(Q(("region", "code:ZZ|fill:red")), Options, out _, out var err).Should().BeFalse();
		err.Should().Contain("Unknown region code 'ZZ'");
	}

	[Fact]
	public void Parse_Region_BareCode_Works()
	{
		StaticMapRequestParser.TryParse(Q(("region", "FR")), Options, out var r, out _).Should().BeTrue();
		r.Regions.Should().ContainSingle();
		r.Regions[0].Code.Should().Be("FR");
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

	[Theory]
	[InlineData("tiny", 0.4)]
	[InlineData("small", 0.55)]
	[InlineData("mid", 0.8)]
	[InlineData("normal", 1.0)]
	public void Parse_MarkerSize_MapsToTheSharedScale(string size, double expected)
	{
		// One source of truth with the renderer: 'mid' used to map to the same scale as 'normal', so
		// Magic Suite's default marker size drew a 'normal' pin (issue #9).
		StaticMapRequestParser.TryParse(Q(("markers", $"size:{size}|51.5,-0.12")), Options, out var r, out _).Should().BeTrue();
		r.Markers.Should().ContainSingle();
		r.Markers[0].Scale.Should().Be(expected).And.Be(MarkerMetrics.ScaleForSize(size));
	}

	[Fact]
	public void Parse_MarkerSize_IsCaseInsensitiveAndFallsBackToNormal()
	{
		StaticMapRequestParser.TryParse(Q(("markers", "size:MID|51.5,-0.12")), Options, out var upper, out _).Should().BeTrue();
		upper.Markers[0].Scale.Should().Be(0.8);

		StaticMapRequestParser.TryParse(Q(("markers", "size:gigantic|51.5,-0.12")), Options, out var unknown, out _).Should().BeTrue();
		unknown.Markers[0].Scale.Should().Be(1.0);
	}

	[Fact]
	public void Parse_NamedMarkerIcon_IsAccepted()
	{
		StaticMapRequestParser.TryParse(Q(("markers", "icon:cafe|51.5,-0.12")), Options, out var r, out _).Should().BeTrue();
		r.Markers.Should().ContainSingle();
		r.Markers[0].Icon.Should().Be("cafe");
	}

	[Theory]
	[InlineData("https://example.com/pin.png")]
	[InlineData("http://example.com/pin.png")]
	[InlineData("//example.com/pin.png")]
	public void Parse_RemoteMarkerIconUrl_IsRejectedWithAnActionableError(string icon)
	{
		// Issue #12: fetching caller-supplied URLs from a public service is an SSRF decision, not a
		// feature to slip in. Rejecting says so; silently drawing a pin instead did not.
		StaticMapRequestParser.TryParse(Q(("markers", $"icon:{icon}|51.5,-0.12")), Options, out _, out var error).Should().BeFalse();
		error.Should().NotBeNull();
		error.Should().Contain("icon");
		error.Should().Contain("sprite", "the error should point at the supported alternative");
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
