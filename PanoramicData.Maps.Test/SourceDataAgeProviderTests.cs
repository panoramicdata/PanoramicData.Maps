using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>Issue #8: the age of the tile archive and the Photon index, for staleness monitoring.</summary>
public class SourceDataAgeProviderTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<(HttpMethod Method, Uri? Uri)> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Requests.Add((request.Method, request.RequestUri));
			return Task.FromResult(respond(request));
		}
	}

	private static HttpResponseMessage Healthy(HttpRequestMessage request)
	{
		var path = request.RequestUri!.AbsolutePath;
		if (path.EndsWith("/status", StringComparison.Ordinal))
		{
			return Json("""{"status":"Ok","import_date":"2026-08-04T03:12:44Z"}""");
		}

		var response = new HttpResponseMessage(HttpStatusCode.OK);
		response.Content = new ByteArrayContent([]);
		response.Content.Headers.LastModified = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
		return response;
	}

	private static HttpResponseMessage Json(string json)
		=> new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	private static SourceDataAgeProvider Create(HttpMessageHandler handler, Action<MapsOptions>? configure = null)
	{
		var options = new MapsOptions
		{
			PhotonBaseUrl = "https://photon.example",
			TilesStyleUrl = "https://tiles.example/style.json"
		};
		configure?.Invoke(options);
		return new SourceDataAgeProvider(new HttpClient(handler), Options.Create(options), new FixedTimeProvider(Now));
	}

	[Fact]
	public async Task Photon_AgeComesFromTheStatusImportDate()
	{
		var report = await Create(new RoutingHandler(Healthy)).GetAsync(TestContext.Current.CancellationToken);

		report.Photon.Status.Should().Be("ok");
		report.Photon.LastUpdatedUtc.Should().Be(new DateTimeOffset(2026, 8, 4, 3, 12, 44, TimeSpan.Zero));
		report.Photon.AgeDays.Should().Be(100);
	}

	[Fact]
	public async Task Tiles_AgeComesFromTheLastModifiedHeader()
	{
		var report = await Create(new RoutingHandler(Healthy)).GetAsync(TestContext.Current.CancellationToken);

		report.Tiles.Status.Should().Be("ok");
		report.Tiles.LastUpdatedUtc.Should().Be(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
		report.Tiles.AgeDays.Should().Be(40);
	}

	[Fact]
	public async Task DefaultUrls_AreDerivedFromTheConfiguredBackends()
	{
		var handler = new RoutingHandler(Healthy);

		await Create(handler).GetAsync(TestContext.Current.CancellationToken);

		handler.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.Uri!.ToString() == "https://photon.example/status");
		handler.Requests.Should().Contain(r => r.Method == HttpMethod.Head && r.Uri!.ToString() == "https://tiles.example/planet.json");
	}

	[Fact]
	public async Task ExplicitUrls_OverrideTheDerivedOnes()
	{
		var handler = new RoutingHandler(Healthy);
		var provider = Create(handler, o =>
		{
			o.PhotonStatusUrl = "https://other.example/photon-status";
			o.TilesDataUrl = "https://other.example/planet.pmtiles";
		});

		await provider.GetAsync(TestContext.Current.CancellationToken);

		handler.Requests.Select(r => r.Uri!.ToString()).Should().BeEquivalentTo("https://other.example/photon-status", "https://other.example/planet.pmtiles");
	}

	[Fact]
	public async Task UnreachableBackends_ReportUnknownRatherThanThrowing()
	{
		var handler = new RoutingHandler(_ => throw new HttpRequestException("connection refused"));

		var report = await Create(handler).GetAsync(TestContext.Current.CancellationToken);

		report.Tiles.Status.Should().Be("unknown");
		report.Tiles.AgeDays.Should().BeNull();
		report.Photon.Status.Should().Be("unknown");
		report.Photon.Note.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public async Task ErrorStatusCodes_ReportUnknown()
	{
		var report = await Create(new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)))
			.GetAsync(TestContext.Current.CancellationToken);

		report.Tiles.Status.Should().Be("unknown");
		report.Photon.Status.Should().Be("unknown");
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("""{"status":"Ok"}""")]
	[InlineData("""{"import_date":"yesterday-ish"}""")]
	[InlineData("""{"import_date":42}""")]
	public async Task UnreadablePhotonStatus_ReportsUnknown(string body)
	{
		var report = await Create(new RoutingHandler(request =>
				request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal) ? Json(body) : Healthy(request)))
			.GetAsync(TestContext.Current.CancellationToken);

		report.Photon.Status.Should().Be("unknown");
		report.Tiles.Status.Should().Be("ok");
	}

	[Fact]
	public async Task TilesWithoutALastModifiedHeader_ReportUnknown()
	{
		var report = await Create(new RoutingHandler(request =>
				request.Method == HttpMethod.Head ? new HttpResponseMessage(HttpStatusCode.OK) : Healthy(request)))
			.GetAsync(TestContext.Current.CancellationToken);

		report.Tiles.Status.Should().Be("unknown");
	}

	[Fact]
	public async Task ConfiguredPaths_UseTheFileTimestampInsteadOfTheNetwork()
	{
		var tiles = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pmtiles");
		var photonDir = Directory.CreateTempSubdirectory().FullName;
		try
		{
			await File.WriteAllBytesAsync(tiles, [1], TestContext.Current.CancellationToken);
			File.SetLastWriteTimeUtc(tiles, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			Directory.SetLastWriteTimeUtc(photonDir, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
			var handler = new RoutingHandler(Healthy);

			var report = await Create(handler, o =>
			{
				o.TilesDataPath = tiles;
				o.PhotonIndexPath = photonDir;
			}).GetAsync(TestContext.Current.CancellationToken);

			handler.Requests.Should().BeEmpty();
			report.Tiles.AgeDays.Should().Be(315);
			report.Photon.AgeDays.Should().Be(10);
		}
		finally
		{
			File.Delete(tiles);
			Directory.Delete(photonDir);
		}
	}

	[Fact]
	public async Task MissingPath_ReportsUnknown()
	{
		var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pmtiles");

		var report = await Create(new RoutingHandler(Healthy), o => o.TilesDataPath = missing).GetAsync(TestContext.Current.CancellationToken);

		report.Tiles.Status.Should().Be("unknown");
		report.Tiles.Note.Should().Contain("not found");
	}

	[Fact]
	public async Task Results_AreCached_SoAHealthProbeDoesNotHammerTheBackends()
	{
		var handler = new RoutingHandler(Healthy);
		var provider = Create(handler);

		await provider.GetAsync(TestContext.Current.CancellationToken);
		await provider.GetAsync(TestContext.Current.CancellationToken);

		handler.Requests.Should().HaveCount(2, "one request per backend, however many health checks arrive");
	}
}
