using System.Net;
using System.Text;
using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

public class PhotonGeocoderTests
{
	private sealed class StubHandler(string json) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			});
	}

	[Fact]
	public async Task GeocodeAsync_ParsesFirstFeature()
	{
		const string json = """
			{"type":"FeatureCollection","features":[{"type":"Feature",
			"geometry":{"type":"Point","coordinates":[-0.1277653,51.5074456]},
			"properties":{"name":"London","country":"United Kingdom"}}]}
			""";
		using var http = new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		var result = await geocoder.GeocodeAsync("London", TestContext.Current.CancellationToken);

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
		using var http = new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("https://photon.example/") };
		var geocoder = new PhotonGeocoder(http);

		var result = await geocoder.GeocodeAsync("Nowhere", TestContext.Current.CancellationToken);

		result.Should().BeNull();
	}
}
