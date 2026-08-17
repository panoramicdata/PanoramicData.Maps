using System.Net;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace PanoramicData.Maps.Test;

public class PhotonGeocoderTests
{
	private sealed class CapturingHandler(string json) : HttpMessageHandler
	{
		public Uri? LastRequestUri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequestUri = request.RequestUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			});
		}
	}

	private const string LondonJson = """
		{"type":"FeatureCollection","features":[{"type":"Feature",
		"geometry":{"type":"Point","coordinates":[-0.1277653,51.5074456]},
		"properties":{"name":"London","country":"United Kingdom"}}]}
		""";

	[Fact]
	public async Task GeocodeAsync_ParsesFirstFeature()
	{
		using var http = new HttpClient(new CapturingHandler(LondonJson)) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		var result = await geocoder.GeocodeAsync("London", cancellationToken: TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.Name.Should().Be("London");
		result.Country.Should().Be("United Kingdom");
		result.Location.Longitude.Should().BeApproximately(-0.1277653, 1e-6);
		result.Location.Latitude.Should().BeApproximately(51.5074456, 1e-6);
	}

	[Fact]
	public async Task GeocodeAsync_ReturnsNull_WhenNoFeatures()
	{
		const string json = """{"type":"FeatureCollection","features":[]}""";
		using var http = new HttpClient(new CapturingHandler(json)) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		var result = await geocoder.GeocodeAsync("Nowhere", cancellationToken: TestContext.Current.CancellationToken);

		result.Should().BeNull();
	}

	[Theory]
	[InlineData("USA", "United States")]
	[InlineData("UK", "United Kingdom")]
	[InlineData("US", "United States")]
	[InlineData("GB", "United Kingdom")]
	[InlineData("UAE", "United Arab Emirates")]
	[InlineData("DE", "Germany")]
	public async Task GeocodeAsync_RewritesCountryCodeAlias(string input, string expectedQuery)
	{
		var handler = new CapturingHandler(LondonJson);
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		await geocoder.GeocodeAsync(input, cancellationToken: TestContext.Current.CancellationToken);

		Uri.UnescapeDataString(handler.LastRequestUri!.Query).Should().Contain($"q={expectedQuery}");
	}

	[Fact]
	public async Task GeocodeAsync_DoesNotRewriteOrdinaryPlaceNames()
	{
		var handler = new CapturingHandler(LondonJson);
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		await geocoder.GeocodeAsync("Paris", cancellationToken: TestContext.Current.CancellationToken);

		Uri.UnescapeDataString(handler.LastRequestUri!.Query).Should().Contain("q=Paris");
	}

	[Fact]
	public async Task GeocodeAsync_AppendsLanguage_WhenProvided()
	{
		var handler = new CapturingHandler(LondonJson);
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		await geocoder.GeocodeAsync("Tokyo", "en", TestContext.Current.CancellationToken);

		handler.LastRequestUri!.Query.Should().Contain("lang=en");
	}

	[Fact]
	public async Task GeocodeAsync_OmitsLanguage_WhenNotProvided()
	{
		var handler = new CapturingHandler(LondonJson);
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		await geocoder.GeocodeAsync("Tokyo", cancellationToken: TestContext.Current.CancellationToken);

		handler.LastRequestUri!.Query.Should().NotContain("lang=");
	}

	[Fact]
	public async Task ReverseAsync_AppendsLanguage_WhenProvided()
	{
		var handler = new CapturingHandler(LondonJson);
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		await geocoder.ReverseAsync(new GeoPoint(-0.1278, 51.5074), "fr", TestContext.Current.CancellationToken);

		handler.LastRequestUri!.Query.Should().Contain("lang=fr");
	}
}
