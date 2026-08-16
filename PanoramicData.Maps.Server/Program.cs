using System.Globalization;
using Microsoft.Extensions.Options;
using PanoramicData.Maps;
using PanoramicData.Maps.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPanoramicDataMaps(builder.Configuration);
builder.Services.AddSingleton<IMapRenderer, PlaywrightMapRenderer>();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<MapsOptions>>().Value;

// Optional API-key gate on /v1/* — off by default (open-source), on for the metered hosted service.
app.Use(async (context, next) =>
{
	if (options.RequireApiKey && context.Request.Path.StartsWithSegments("/v1"))
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
		"GET /v1/staticmap?center=51.5074,-0.1278&zoom=12&size=800x600&markers=51.5074,-0.1278,red,London",
		"POST /v1/staticmap  (application/json body: a MapRequest with markers/paths/polygons)"
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

app.MapGet("/v1/staticmap", async (HttpRequest req, IGeocoder geocoder, IMapRenderer renderer, CancellationToken ct) =>
{
	var q = req.Query;
	GeoPoint? center = TryParseLatLon(q["center"]);
	var location = q["location"].FirstOrDefault();

	if (center is null && !string.IsNullOrWhiteSpace(location))
	{
		var geo = await geocoder.GeocodeAsync(location!, ct);
		if (geo is null)
		{
			return Results.BadRequest(new { error = $"Could not geocode location '{location}'." });
		}

		center = geo.Location;
	}

	var markers = new List<MarkerSpec>();
	foreach (var m in q["markers"])
	{
		// lat,lon[,color[,label]]
		var parts = (m ?? string.Empty).Split(',');
		if (parts.Length >= 2
			&& double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
			&& double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
		{
			markers.Add(new MarkerSpec
			{
				Location = new GeoPoint(lon, lat),
				Color = parts.Length >= 3 && parts[2].Length > 0 ? parts[2] : "#dc2626",
				Label = parts.Length >= 4 ? parts[3] : null
			});
		}
	}

	var (width, height) = ParseSize(q["size"].FirstOrDefault(), q["width"].FirstOrDefault(), q["height"].FirstOrDefault(), options);

	var request = new MapRequest
	{
		Center = center,
		Zoom = double.TryParse(q["zoom"].FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : null,
		Width = width,
		Height = height,
		Scale = Math.Clamp(int.TryParse(q["scale"].FirstOrDefault(), out var s) ? s : 1, 1, options.MaxScale),
		Format = string.Equals(q["format"].FirstOrDefault(), "jpeg", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(q["format"].FirstOrDefault(), "jpg", StringComparison.OrdinalIgnoreCase)
			? MapImageFormat.Jpeg : MapImageFormat.Png,
		Markers = markers
	};

	var image = await renderer.RenderAsync(request, ct);
	return Results.File(image.Bytes, image.ContentType);
});

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

static GeoPoint? TryParseLatLon(string? value)
{
	if (string.IsNullOrWhiteSpace(value))
	{
		return null;
	}

	var parts = value.Split(',');
	return parts.Length == 2
		&& double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
		&& double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
		? new GeoPoint(lon, lat)
		: null;
}

static (int Width, int Height) ParseSize(string? size, string? width, string? height, MapsOptions options)
{
	var w = 800;
	var h = 600;
	if (!string.IsNullOrWhiteSpace(size) && size.Contains('x', StringComparison.OrdinalIgnoreCase))
	{
		var parts = size.Split('x', 'X');
		if (parts.Length == 2 && int.TryParse(parts[0], out var pw) && int.TryParse(parts[1], out var ph))
		{
			w = pw;
			h = ph;
		}
	}
	else
	{
		if (int.TryParse(width, out var pw))
		{
			w = pw;
		}

		if (int.TryParse(height, out var ph))
		{
			h = ph;
		}
	}

	return (Math.Clamp(w, 1, options.MaxWidth), Math.Clamp(h, 1, options.MaxHeight));
}

/// <summary>Program entry point marker (enables WebApplicationFactory in tests).</summary>
public partial class Program;
