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

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/", () => Results.Ok(new
{
	name = "PanoramicData.Maps",
	endpoints = new[]
	{
		"GET /v1/geocode?q=London",
		"GET /v1/reverse?lon=-0.1278&lat=51.5074",
		"GET /staticmap?center=51.5074,-0.1278&zoom=12&size=800x600&markers=color:red|label:A|51.5074,-0.1278  (Google-compatible)",
		"GET /v1/staticmap?...  (same as /staticmap)",
		"POST /v1/staticmap  (application/json MapRequest body)"
	}
}));

app.MapGet("/v1/geocode", async (string q, IGeocoder geocoder, CancellationToken ct) =>
{
	var result = await geocoder.GeocodeAsync(q, ct);
	return result is null ? Results.NotFound(new { error = "No match." }) : Results.Ok(result);
});

app.MapGet("/v1/reverse", async (double lon, double lat, IGeocoder geocoder, CancellationToken ct) =>
{
	var result = await geocoder.ReverseAsync(new GeoPoint(lon, lat), ct);
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
		var geo = await geocoder.GeocodeAsync(request.Location!, ct);
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
	var resolved = request;
	if (request.Center is null && !string.IsNullOrWhiteSpace(request.Location))
	{
		var geo = await geocoder.GeocodeAsync(request.Location!, ct);
		if (geo is null)
		{
			return Results.BadRequest(new { error = $"Could not geocode location '{request.Location}'." });
		}

		resolved = request with { Center = geo.Location };
	}

	resolved = resolved with
	{
		Width = Math.Clamp(resolved.Width, 1, options.MaxWidth),
		Height = Math.Clamp(resolved.Height, 1, options.MaxHeight),
		Scale = Math.Clamp(resolved.Scale, 1, options.MaxScale)
	};

	var image = await renderer.RenderAsync(resolved, ct);
	return Results.File(image.Bytes, image.ContentType);
});

app.Run();

/// <summary>Program entry point marker (enables WebApplicationFactory in tests).</summary>
public partial class Program;
