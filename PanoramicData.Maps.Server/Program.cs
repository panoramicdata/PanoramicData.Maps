using Microsoft.Extensions.Options;
using PanoramicData.Maps;

var builder = WebApplication.CreateBuilder(args);

// Registers options, the Photon geocoder and the native SkiaSharp renderer.
builder.Services.AddPanoramicDataMaps(builder.Configuration);

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<MapsOptions>>().Value;

// Optional API-key gate on the map/geocode endpoints - off by default (open-source), on for the
// metered hosted service.
app.Use(async (context, next) =>
{
	var path = context.Request.Path;
	var guarded = path.StartsWithSegments("/v1") || path.StartsWithSegments("/staticmap");
	if (options.RequireApiKey && guarded)
	{
		var key = context.Request.Headers["X-Api-Key"].FirstOrDefault()
			?? context.Request.Query["key"].FirstOrDefault();
		if (key is null || !options.ApiKeys.Contains(key))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			await context.Response.WriteAsJsonAsync(new { error = "A valid API key is required." });
			return;
		}
	}

	await next();
});

// The running build's identity belongs on the health surface: this service runs in two clusters and
// is consumed from every Magic Suite environment, so "which build is answering?" has to be a request
// away rather than a cluster inspection (issue #11).
var serviceInfo = MapsServiceInfo.Current;

app.MapGet("/health", () => Results.Ok(new
{
	status = "ok",
	version = serviceInfo.Version,
	commit = serviceInfo.Commit
}));

app.MapGet("/", () => Results.Ok(new
{
	name = "PanoramicData.Maps",
	version = serviceInfo.Version,
	endpoints = new[]
	{
		"GET /v1/geocode?q=London&lang=en",
		"GET /v1/reverse?lon=-0.1278&lat=51.5074&lang=en",
		"GET /v1/limits  (max width/height/scale)",
		"GET /staticmap?center=51.5074,-0.1278&zoom=12&size=800x600&markers=color:red|label:A|51.5074,-0.1278  (Google-compatible)",
		"GET /staticmap?...&maptype=terrain&region=code:GB|fill:red|opacity:0.5",
		"GET /v1/staticmap?...  (same as /staticmap)",
		"POST /v1/staticmap  (application/json MapRequest body)"
	}
}));

app.MapGet("/v1/limits", () => Results.Ok(new
{
	maxWidth = options.MaxWidth,
	maxHeight = options.MaxHeight,
	maxScale = options.MaxScale
}));

app.MapGet("/v1/geocode", async (string q, string? lang, IGeocoder geocoder, CancellationToken ct) =>
{
	var result = await geocoder.GeocodeAsync(q, lang ?? options.DefaultLanguage, ct);
	return result is null ? Results.NotFound(new { error = "No match." }) : Results.Ok(result);
});

app.MapGet("/v1/reverse", async (double lon, double lat, string? lang, IGeocoder geocoder, CancellationToken ct) =>
{
	var result = await geocoder.ReverseAsync(new GeoPoint(lon, lat), lang ?? options.DefaultLanguage, ct);
	return result is null ? Results.NotFound(new { error = "No match." }) : Results.Ok(result);
});

// Google-Static-Maps-compatible endpoint (and a /v1 alias).
var staticMap = async (HttpRequest req, IGeocoder geocoder, IMapRenderer renderer, CancellationToken ct) =>
{
	var query = req.Query.ToDictionary(
		kvp => kvp.Key,
		kvp => (IReadOnlyList<string>)kvp.Value.Where(s => s is not null).Select(s => s!).ToArray(),
		StringComparer.OrdinalIgnoreCase);

	if (!StaticMapRequestParser.TryParse(query, options, out var request, out var error))
	{
		return Results.BadRequest(new { error });
	}

	if (request.Center is null && !string.IsNullOrWhiteSpace(request.Location))
	{
		var geo = await geocoder.GeocodeAsync(request.Location!, options.DefaultLanguage, ct);
		if (geo is null)
		{
			return Results.BadRequest(new { error = $"Could not geocode location '{request.Location}'." });
		}

		request = request with { Center = geo.Location };
	}

	var image = await renderer.RenderAsync(request, ct);
	return Results.File(image.Bytes, image.ContentType);
};

app.MapGet("/staticmap", staticMap);
app.MapGet("/v1/staticmap", staticMap);

app.MapPost("/v1/staticmap", async (MapRequest request, IGeocoder geocoder, IMapRenderer renderer, CancellationToken ct) =>
{
	// Reject over-limit requests rather than silently shrinking them (issue #3).
	if (request.Width > options.MaxWidth)
	{
		return Results.BadRequest(new { error = $"width {request.Width} exceeds the maximum of {options.MaxWidth}" });
	}

	if (request.Height > options.MaxHeight)
	{
		return Results.BadRequest(new { error = $"height {request.Height} exceeds the maximum of {options.MaxHeight}" });
	}

	if (request.Scale > options.MaxScale)
	{
		return Results.BadRequest(new { error = $"scale {request.Scale} exceeds the maximum of {options.MaxScale}" });
	}

	var resolved = request;
	if (request.Center is null && !string.IsNullOrWhiteSpace(request.Location))
	{
		var geo = await geocoder.GeocodeAsync(request.Location!, options.DefaultLanguage, ct);
		if (geo is null)
		{
			return Results.BadRequest(new { error = $"Could not geocode location '{request.Location}'." });
		}

		resolved = request with { Center = geo.Location };
	}

	resolved = resolved with
	{
		Width = Math.Max(1, resolved.Width),
		Height = Math.Max(1, resolved.Height),
		Scale = Math.Max(1, resolved.Scale)
	};

	var image = await renderer.RenderAsync(resolved, ct);
	return Results.File(image.Bytes, image.ContentType);
});

app.Run();

/// <summary>Program entry point marker (enables WebApplicationFactory in tests).</summary>
public partial class Program;
