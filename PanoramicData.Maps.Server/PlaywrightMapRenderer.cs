using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using PanoramicData.Maps;

namespace PanoramicData.Maps.Server;

/// <summary>
/// Renders maps by driving MapLibre GL JS in a headless Chromium (via Playwright) and screenshotting
/// the result. A single browser instance is shared; each render uses a fresh page.
/// </summary>
public sealed class PlaywrightMapRenderer(IOptions<MapsOptions> options, ILogger<PlaywrightMapRenderer> logger)
	: IMapRenderer, IAsyncDisposable
{
	private readonly MapsOptions _options = options.Value;
	private readonly ILogger<PlaywrightMapRenderer> _logger = logger;
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private IPlaywright? _playwright;
	private IBrowser? _browser;

	private async Task<IBrowser> GetBrowserAsync()
	{
		if (_browser is not null)
		{
			return _browser;
		}

		await _initLock.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_browser is null)
			{
				_logger.LogInformation("Launching headless Chromium for map rendering");
				_playwright = await Playwright.CreateAsync().ConfigureAwait(false);
				_browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
				{
					Headless = true,
					Args = ["--no-sandbox", "--disable-dev-shm-usage"]
				}).ConfigureAwait(false);
			}
		}
		finally
		{
			_initLock.Release();
		}

		return _browser;
	}

	/// <inheritdoc />
	public async Task<MapImage> RenderAsync(MapRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var browser = await GetBrowserAsync().ConfigureAwait(false);
		var page = await browser.NewPageAsync(new BrowserNewPageOptions
		{
			ViewportSize = new ViewportSize { Width = request.Width, Height = request.Height },
			DeviceScaleFactor = request.Scale
		}).ConfigureAwait(false);

		try
		{
			var html = BuildHtml(request);
			await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load }).ConfigureAwait(false);
			await page.WaitForFunctionAsync("() => window.__mapReady === true", null,
				new PageWaitForFunctionOptions { Timeout = 30000 }).ConfigureAwait(false);

			var isPng = request.Format == MapImageFormat.Png;
			var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
			{
				Type = isPng ? ScreenshotType.Png : ScreenshotType.Jpeg,
				Quality = isPng ? null : 85
			}).ConfigureAwait(false);

			return new MapImage(bytes, isPng ? "image/png" : "image/jpeg");
		}
		finally
		{
			await page.CloseAsync().ConfigureAwait(false);
		}
	}

	private string BuildHtml(MapRequest request)
	{
		var styleUrl = request.StyleUrl ?? _options.TilesStyleUrl;
		var center = request.Center ?? new GeoPoint(0, 20);
		var zoom = request.Zoom ?? 2;

		// The overlay data is handed to the page as JSON and applied by the script below.
		var payload = JsonSerializer.Serialize(new
		{
			style = styleUrl,
			center = new[] { center.Longitude, center.Latitude },
			zoom,
			hasExplicitZoom = request.Zoom is not null,
			markers = request.Markers.Select(m => new { lon = m.Location.Longitude, lat = m.Location.Latitude, color = m.Color, label = m.Label }),
			paths = request.Paths.Select(p => new { coords = p.Points.Select(pt => new[] { pt.Longitude, pt.Latitude }), color = p.Color, width = p.Width, opacity = p.Opacity }),
			polygons = request.Polygons.Select(p => new { coords = p.Points.Select(pt => new[] { pt.Longitude, pt.Latitude }), fill = p.FillColor, fillOpacity = p.FillOpacity, stroke = p.StrokeColor, strokeWidth = p.StrokeWidth })
		});

		// Note: maplibre-gl is loaded from a CDN for now; a future revision should vendor it into the
		// image so rendering has no external dependency. The page template is a non-interpolated raw
		// string (JS braces are literal); the overlay payload is injected via a placeholder.
		const string template = """
			<!doctype html><html><head><meta charset="utf-8" />
			<link href="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css" rel="stylesheet" />
			<style>html,body,#map{margin:0;height:100%;width:100%}</style></head>
			<body><div id="map"></div>
			<script src="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js"></script>
			<script>
			const R = __PAYLOAD__;
			const map = new maplibregl.Map({ container:'map', style:R.style, center:R.center, zoom:R.zoom,
				interactive:false, attributionControl:{compact:false} });
			map.on('load', () => {
				R.polygons.forEach((p,i) => {
					map.addSource('pg'+i,{type:'geojson',data:{type:'Feature',geometry:{type:'Polygon',coordinates:[p.coords]}}});
					map.addLayer({id:'pgf'+i,type:'fill',source:'pg'+i,paint:{'fill-color':p.fill,'fill-opacity':p.fillOpacity}});
					map.addLayer({id:'pgl'+i,type:'line',source:'pg'+i,paint:{'line-color':p.stroke,'line-width':p.strokeWidth}});
				});
				R.paths.forEach((p,i) => {
					map.addSource('pa'+i,{type:'geojson',data:{type:'Feature',geometry:{type:'LineString',coordinates:p.coords}}});
					map.addLayer({id:'pal'+i,type:'line',source:'pa'+i,paint:{'line-color':p.color,'line-width':p.width,'line-opacity':p.opacity}});
				});
				const bounds = new maplibregl.LngLatBounds();
				let any = false;
				R.markers.forEach(m => {
					const mk = new maplibregl.Marker({color:m.color}).setLngLat([m.lon,m.lat]).addTo(map);
					if (m.label) { mk.getElement().title = m.label; }
					bounds.extend([m.lon,m.lat]); any = true;
				});
				if (!R.hasExplicitZoom && any) { try { map.fitBounds(bounds,{padding:60,maxZoom:16,duration:0}); } catch(e){} }
				map.once('idle', () => { window.__mapReady = true; });
			});
			map.on('error', e => { window.__mapError = String(e && e.error); });
			</script></body></html>
			""";

		return template.Replace("__PAYLOAD__", payload);
	}

	/// <summary>Disposes the shared browser and Playwright driver.</summary>
	public async ValueTask DisposeAsync()
	{
		if (_browser is not null)
		{
			await _browser.DisposeAsync().ConfigureAwait(false);
		}

		_playwright?.Dispose();
		_initLock.Dispose();
	}
}
