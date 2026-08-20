using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// The builder writes the query grammar the parser reads, so the tests that matter most are
/// round-trips: build a URL from a request, parse it back, and compare. That test is only possible
/// because both halves live in the same repository (issue #16) - while the writing half lived in a
/// consumer, the two could disagree and nothing failed except a customer's map.
/// </summary>
public class StaticMapUrlBuilderTests
{
	private static readonly MapsOptions Options = new();

	private const string BaseUrl = "https://maps.example.com";

	private static MapRequest RoundTrip(MapRequest request)
	{
		var url = StaticMapUrlBuilder.Build(BaseUrl, request);
		var query = QueryOf(url);

		StaticMapRequestParser.TryParse(query, Options, out var parsed, out var error)
			.Should().BeTrue($"the builder must emit something the parser accepts, but it said: {error}");

		return parsed;
	}

	/// <summary>Splits a built URL back into the repeated-key form the parser expects.</summary>
	private static Dictionary<string, IReadOnlyList<string>> QueryOf(string url)
	{
		var query = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in new Uri(url).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var split = pair.Split('=', 2);
			var key = Uri.UnescapeDataString(split[0]);
			var value = split.Length > 1 ? Uri.UnescapeDataString(split[1]) : string.Empty;
			if (!query.TryGetValue(key, out var values))
			{
				values = [];
				query[key] = values;
			}

			values.Add(value);
		}

		return query.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);
	}

	[Fact]
	public void Build_TargetsTheStaticMapEndpointOnTheGivenBaseUrl()
	{
		var url = StaticMapUrlBuilder.Build("https://maps.example.com/", new MapRequest { Center = new GeoPoint(-0.1278, 51.5074), Zoom = 12 });

		url.Should().StartWith("https://maps.example.com/staticmap?", "a trailing slash on the base URL must not double up");
	}

	[Fact]
	public void Build_WritesCoordinatesInGoogleLatLngOrder()
	{
		var url = StaticMapUrlBuilder.Build(BaseUrl, new MapRequest { Center = new GeoPoint(-0.1278, 51.5074), Zoom = 12 });

		url.Should().Contain("center=51.5074%2C-0.1278", "the query API is lat,lng - reversing it puts London in the Atlantic");
	}

	[Fact]
	public void Build_UsesInvariantFormattingForCoordinates()
	{
		// A comma decimal separator would silently change the coordinate.
		var url = StaticMapUrlBuilder.Build(BaseUrl, new MapRequest { Center = new GeoPoint(-0.1278, 51.5074), Zoom = 12.5 });

		url.Should().Contain("51.5074").And.Contain("-0.1278").And.Contain("zoom=12.5");
	}

	[Fact]
	public void RoundTrip_CentreZoomAndSize()
	{
		var request = new MapRequest { Center = new GeoPoint(-0.1278, 51.5074), Zoom = 12, Width = 640, Height = 480 };

		var parsed = RoundTrip(request);

		parsed.Center!.Value.Latitude.Should().BeApproximately(51.5074, 1e-6);
		parsed.Center!.Value.Longitude.Should().BeApproximately(-0.1278, 1e-6);
		parsed.Zoom.Should().Be(12);
		parsed.Width.Should().Be(640);
		parsed.Height.Should().Be(480);
	}

	[Fact]
	public void RoundTrip_PlaceNameLocationInsteadOfCentre()
	{
		var parsed = RoundTrip(new MapRequest { Location = "Maidenhead, Berkshire", Zoom = 13 });

		parsed.Location.Should().Be("Maidenhead, Berkshire");
		parsed.Center.Should().BeNull();
	}

	[Fact]
	public void RoundTrip_ScaleAndFormat()
	{
		var parsed = RoundTrip(new MapRequest { Center = new GeoPoint(0, 0), Zoom = 4, Scale = 2, Format = MapImageFormat.Jpeg });

		parsed.Scale.Should().Be(2);
		parsed.Format.Should().Be(MapImageFormat.Jpeg);
	}

	[Fact]
	public void RoundTrip_MarkersKeepTheirColourLabelAndScale()
	{
		var request = new MapRequest
		{
			Center = new GeoPoint(-0.1278, 51.5074),
			Zoom = 12,
			Markers =
			[
				new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "red", Label = "A", Scale = MarkerMetrics.ScaleForSize("mid") },
				new MarkerSpec { Location = new GeoPoint(-0.09, 51.52), Color = "#00ff00", Label = "B" }
			]
		};

		var parsed = RoundTrip(request);

		parsed.Markers.Should().HaveCount(2);
		parsed.Markers[0].Color.Should().Be("red");
		parsed.Markers[0].Label.Should().Be("A");
		parsed.Markers[0].Scale.Should().Be(MarkerMetrics.ScaleForSize("mid"));
		parsed.Markers[1].Color.Should().Be("#00ff00");
		parsed.Markers[1].Location.Latitude.Should().BeApproximately(51.52, 1e-6);
	}

	[Fact]
	public void RoundTrip_MarkersWithAnIcon()
	{
		var request = new MapRequest
		{
			Center = new GeoPoint(-0.1278, 51.5074),
			Zoom = 14,
			Markers = [new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Icon = "cafe", Label = "Kiosk" }]
		};

		var parsed = RoundTrip(request);

		parsed.Markers.Should().ContainSingle();
		parsed.Markers[0].Icon.Should().Be("cafe");
		parsed.Markers[0].Label.Should().Be("Kiosk");
	}

	[Fact]
	public void RoundTrip_PathKeepsItsPointsColourAndWidth()
	{
		var request = new MapRequest
		{
			Center = new GeoPoint(-0.1278, 51.5074),
			Zoom = 12,
			Paths =
			[
				new PathSpec
				{
					Points = [new GeoPoint(-0.16, 51.507), new GeoPoint(-0.07, 51.508), new GeoPoint(-0.05, 51.51)],
					Color = "#7c3aed",
					Width = 6
				}
			]
		};

		var parsed = RoundTrip(request);

		parsed.Paths.Should().ContainSingle();
		parsed.Paths[0].Points.Should().HaveCount(3);
		parsed.Paths[0].Color.Should().Be("#7c3aed");
		parsed.Paths[0].Width.Should().Be(6);
	}

	[Fact]
	public void RoundTrip_PolygonBecomesAFilledPath()
	{
		// Google expresses a filled area as a path with fillcolor, and the parser follows that, so the
		// builder has to as well - a polygon that came back as a line would be a silent regression.
		var request = new MapRequest
		{
			Center = new GeoPoint(-0.13, 51.5),
			Zoom = 12,
			Polygons =
			[
				new PolygonSpec
				{
					Points = [new GeoPoint(-0.16, 51.49), new GeoPoint(-0.16, 51.52), new GeoPoint(-0.10, 51.52), new GeoPoint(-0.10, 51.49)],
					FillColor = "#f59e0b",
					StrokeColor = "#b45309",
					StrokeWidth = 3
				}
			]
		};

		var parsed = RoundTrip(request);

		parsed.Polygons.Should().ContainSingle();
		parsed.Polygons[0].Points.Should().HaveCount(4);
		parsed.Polygons[0].FillColor.Should().Be("#f59e0b");
		parsed.Polygons[0].StrokeColor.Should().Be("#b45309");
		parsed.Paths.Should().BeEmpty("a polygon must not also come back as a line");
	}

	[Fact]
	public void RoundTrip_RegionKeepsItsCodeFillAndStroke()
	{
		var request = new MapRequest
		{
			Center = new GeoPoint(-2, 54),
			Zoom = 5,
			Regions = [new RegionSpec { Code = "GB", FillColor = "red", FillOpacity = 0.35, StrokeColor = "black", StrokeWidth = 2 }]
		};

		var parsed = RoundTrip(request);

		parsed.Regions.Should().ContainSingle();
		parsed.Regions[0].Code.Should().Be("GB");
		parsed.Regions[0].FillColor.Should().Be("red");
		parsed.Regions[0].FillOpacity.Should().BeApproximately(0.35, 1e-9);
		parsed.Regions[0].StrokeColor.Should().Be("black");
		parsed.Regions[0].StrokeWidth.Should().Be(2);
	}

	[Fact]
	public void RoundTrip_EverythingAtOnce()
	{
		var request = new MapRequest
		{
			Center = new GeoPoint(-0.1278, 51.5074),
			Zoom = 11,
			Width = 1024,
			Height = 768,
			Scale = 2,
			Format = MapImageFormat.Png,
			Markers =
			[
				new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "red", Label = "A" },
				new MarkerSpec { Location = new GeoPoint(-0.09, 51.52), Icon = "train_station" }
			],
			Paths = [new PathSpec { Points = [new GeoPoint(-0.16, 51.507), new GeoPoint(-0.07, 51.508)], Color = "blue", Width = 4 }],
			Regions = [new RegionSpec { Code = "GB", FillColor = "green", FillOpacity = 0.2 }]
		};

		var parsed = RoundTrip(request);

		parsed.Markers.Should().HaveCount(2);
		parsed.Paths.Should().ContainSingle();
		parsed.Regions.Should().ContainSingle();
		parsed.Scale.Should().Be(2);
		parsed.Width.Should().Be(1024);
	}

	[Fact]
	public void Build_RejectsARequestWithNothingToDraw()
	{
		// The parser refuses this, so the builder should not manufacture a URL that is guaranteed to 400.
		var act = () => StaticMapUrlBuilder.Build(BaseUrl, new MapRequest());

		act.Should().Throw<ArgumentException>().WithMessage("*center*");
	}

	[Fact]
	public void Build_RejectsAnEmptyBaseUrl()
	{
		var act = () => StaticMapUrlBuilder.Build(" ", new MapRequest { Center = new GeoPoint(0, 0), Zoom = 1 });

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Build_DoesNotEmitAnApiKey()
	{
		// Authentication is the caller's business: a key in a URL is the wrong default, and the Blazor
		// component (issue #17) depends on the builder never putting one there.
		var url = StaticMapUrlBuilder.Build(BaseUrl, new MapRequest { Center = new GeoPoint(0, 0), Zoom = 1 });

		url.Should().NotContain("key=");
	}
}
