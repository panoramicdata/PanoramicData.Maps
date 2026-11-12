using Microsoft.Extensions.Options;

namespace PanoramicData.Maps;

/// <summary>
/// Reports how old the tile archive and the Photon index are, so the health surface can feed the
/// Ops Dashboard's staleness thresholds (issue #8). Never throws for a missing or unreachable source:
/// that source is reported <c>unknown</c>. Readings are cached for <see cref="MapsOptions.DataAgeCacheSeconds"/>.
/// </summary>
/// <param name="http">The client used to ask the backends.</param>
/// <param name="options">The maps options.</param>
/// <param name="timeProvider">The clock; the system clock when null.</param>
public sealed class SourceDataAgeProvider(HttpClient http, IOptions<MapsOptions> options, TimeProvider? timeProvider = null)
{
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

	private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private SourceDataAge? _cached;
	private DateTimeOffset _cachedUntil;

	/// <summary>Reads (or returns the cached) age of both datasets.</summary>
	/// <param name="ct">Cancellation.</param>
	/// <returns>The report; individual sources may be <c>unknown</c>.</returns>
	public async Task<SourceDataAge> GetAsync(CancellationToken ct)
	{
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			var now = _time.GetUtcNow();
			if (_cached is not null && now < _cachedUntil)
			{
				return _cached;
			}

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(RequestTimeout);

			_cached = new SourceDataAge(
				await ReadTilesAsync(now, timeout.Token).ConfigureAwait(false),
				await ReadPhotonAsync(now, timeout.Token).ConfigureAwait(false));
			_cachedUntil = now.AddSeconds(Math.Max(0, options.Value.DataAgeCacheSeconds));
			return _cached;
		}
		finally
		{
			_gate.Release();
		}
	}

	private Task<DataAge> ReadTilesAsync(DateTimeOffset now, CancellationToken ct)
	{
		var settings = options.Value;
		if (!string.IsNullOrWhiteSpace(settings.TilesDataPath))
		{
			return Task.FromResult(DataAgeReader.FromPath(settings.TilesDataPath, now));
		}

		var url = string.IsNullOrWhiteSpace(settings.TilesDataUrl) ? DerivedTilesUrl(settings.TilesStyleUrl) : settings.TilesDataUrl;
		return DataAgeReader.FromLastModifiedAsync(http, url, now, ct);
	}

	private Task<DataAge> ReadPhotonAsync(DateTimeOffset now, CancellationToken ct)
	{
		var settings = options.Value;
		if (!string.IsNullOrWhiteSpace(settings.PhotonIndexPath))
		{
			return Task.FromResult(DataAgeReader.FromPath(settings.PhotonIndexPath, now));
		}

		var url = string.IsNullOrWhiteSpace(settings.PhotonStatusUrl)
			? settings.PhotonBaseUrl.TrimEnd('/') + "/status"
			: settings.PhotonStatusUrl;
		return DataAgeReader.FromPhotonStatusAsync(http, url, now, ct);
	}

	private static string DerivedTilesUrl(string styleUrl)
		=> styleUrl[..(styleUrl.LastIndexOf('/') + 1)] + "planet.json";
}
