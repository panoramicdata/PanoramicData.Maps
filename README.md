# PanoramicData.Maps

[![CI](https://github.com/panoramicdata/PanoramicData.Maps/actions/workflows/ci.yml/badge.svg)](https://github.com/panoramicdata/PanoramicData.Maps/actions/workflows/ci.yml)
[![Nuget](https://img.shields.io/nuget/v/PanoramicData.Maps)](https://www.nuget.org/packages/PanoramicData.Maps/)
[![Nuget](https://img.shields.io/nuget/dt/PanoramicData.Maps)](https://www.nuget.org/packages/PanoramicData.Maps/)
[![Docker](https://img.shields.io/docker/v/panoramicdata/maps?label=docker&sort=semver)](https://hub.docker.com/r/panoramicdata/maps)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/108a448491af41e89dde8c039bf14dce)](https://app.codacy.com/gh/panoramicdata/PanoramicData.Maps/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)

Open-source **static map image** rendering with markers, icons, lines and polygon overlays, plus a
thin **geocoding** passthrough — for a self-hosted [Photon](https://github.com/komoot/photon) +
[Protomaps](https://protomaps.com) / [MapLibre](https://maplibre.org) map stack. A drop-in,
self-hostable alternative to the Google Static Maps + Geocoding APIs.

Renders **natively with SkiaSharp — no headless browser, no Node.js.**

See **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)** for Kubernetes setup, dependencies and dataset
options (planet vs alternatives), and **[CONTRIBUTING.md](CONTRIBUTING.md)** to contribute.

Maps are rendered by driving **MapLibre GL JS** in headless Chromium (via Playwright) and
screenshotting the result, so any MapLibre style and overlay works exactly as it would in a browser.

> Status: early scaffold (0.x). The HTTP API and rendering work; the container image and CI are
> being finalised.

## Components

| Project | Purpose | Artifact |
|---------|---------|----------|
| `PanoramicData.Maps.Abstractions` | Request models and URL building (`MapRequest`, `MarkerSpec`, `StaticMapUrlBuilder`). No heavy dependencies. | NuGet: `PanoramicData.Maps.Abstractions` |
| `PanoramicData.Maps` | Renderer, geocoder and options (`IMapRenderer`, `IGeocoder`, `PhotonGeocoder`, `SkiaSharpMapRenderer`). | NuGet: `PanoramicData.Maps` |
| `PanoramicData.Maps.Blazor` | Blazor components - `<StaticMap>`. No JavaScript. | NuGet: `PanoramicData.Maps.Blazor` |
| `PanoramicData.Maps.Server` | ASP.NET Core HTTP service. | Docker Hub: `panoramicdata/maps` |
| `PanoramicData.Maps.Test` | xUnit v3 tests. | — |

## HTTP API

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Liveness, plus the running build: `{"status":"ok","version":"<semver>","commit":"<short sha>"}`. |
| `GET /` | Usage summary. |
| `GET /v1/geocode?q=London` | Forward geocode (via Photon). |
| `GET /v1/reverse?lon=-0.1278&lat=51.5074` | Reverse geocode. |
| `GET /v1/icons` | Named marker icons available to `markers=icon:<name>`. |
| `GET /v1/staticmap?center=51.5074,-0.1278&zoom=12&size=800x600&markers=51.5074,-0.1278,red,London` | Static map image (simple query form). |
| `POST /v1/staticmap` | Static map image (JSON `MapRequest` body — full markers/paths/polygons). |

`center`/marker coordinates in the query API are `lat,lon` (Google-compatible). Set `location=` (a
place name) instead of `center=` to have it geocoded. `format=png` (default) or `jpeg`.

### Markers

Markers are drawn as Google-style teardrop pins, anchored at the tip, so a coordinate is where the
pin points. `size:` accepts Google's four names, each a visibly different pin:

| `size:` | Pin (CSS pixels) |
|---------|------------------|
| `tiny`   | 9 x 16 |
| `small`  | 12 x 22 |
| `mid`    | 18 x 32 |
| `normal` (default) | 22 x 40 |

`scale:` sets an arbitrary relative size instead, and the image's own `scale=2` doubles everything for
@2x output. Two deliberate differences from Google: a `label:` is drawn at **every** size (Google drops
it on its two smallest), because a small marker in a report still needs its identity; and a label of
more than one character is shrunk to fit the pin head rather than spilling over it.

### Marker icons

`markers=icon:<name>|<lat>,<lng>` draws a named icon from the map style's own sprite sheet instead of a
pin - `cafe`, `train_station`, `museum`, `peak`, `bar` and about fifty others. `GET /v1/icons` lists
what the configured style offers, because the names are not guessable.

- An icon is **centred on its coordinate** (these are point glyphs, not pins), and `size:`/`scale:` size
  it just as they size a pin.
- A `label:` alongside an icon is drawn underneath it with a halo, rather than over the glyph.
- An unknown name falls back to a pin and logs a warning, so a typo does not silently change the map.
- **Remote icon URLs are rejected** with a 400. Fetching caller-supplied URLs from a public service is an
  SSRF exposure, not a convenience; the sprite sheet comes from the same host as the tiles and nothing
  else is ever contacted.

The sprite URL is normally discovered from the style JSON's `sprite` field. Set `Maps__SpriteUrl` when
that URL is not reachable from wherever the renderer runs (a common case when the style advertises a
public hostname and the renderer resolves an internal one).

### Example POST body

```json
{
  "location": "London",
  "zoom": 12, "width": 800, "height": 600,
  "markers": [{ "location": { "longitude": -0.1278, "latitude": 51.5074 }, "color": "#dc2626", "label": "HQ" }],
  "paths":   [{ "points": [{ "longitude": -0.16, "latitude": 51.507 }, { "longitude": -0.07, "latitude": 51.508 }], "color": "#7c3aed" }],
  "polygons":[{ "points": [{ "longitude": -0.16, "latitude": 51.49 }, { "longitude": -0.16, "latitude": 51.52 }, { "longitude": -0.10, "latitude": 51.52 }, { "longitude": -0.10, "latitude": 51.49 }] }]
}
```

## Building URLs from .NET

`StaticMapUrlBuilder` writes the query the service parses, so callers do not assemble descriptor
strings by hand. It lives in `PanoramicData.Maps.Abstractions`, which has no renderer dependencies -
safe to reference from a UI project or a report macro.

```csharp
var url = StaticMapUrlBuilder.Build("https://maps.panoramicdata.com", new MapRequest
{
    Center  = new GeoPoint(-0.1278, 51.5074),   // longitude, latitude
    Zoom    = 12,
    Width   = 800,
    Height  = 600,
    Markers = [new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "red", Label = "A" }]
});
```

The builder and the service's parser are tested against each other by round-trip - build a URL, parse
it back, compare - so the two halves of the grammar cannot drift apart.

It never writes an API key. Authentication is the caller's business, and a key in a URL is visible to
anything that can see the URL: send it as the `X-Api-Key` header, or put a proxy in front.

## Blazor

```razor
@using PanoramicData.Maps
@using PanoramicData.Maps.Blazor

<StaticMap BaseUrl="/api/maps"
           Location="Maidenhead, Berkshire"
           Zoom="13"
           Width="640" Height="360"
           class="rounded" />
```

Renders a plain `<img>` - no JavaScript, so none of the interop-lifetime and disposal hazards that
come with a scripted map. `Markers`, `Paths`, `Polygons` and `Regions` take the same models the URL
builder does; unmatched attributes (`class`, `style`, `data-*`) are applied to the image; `loading`
defaults to `lazy`; and `Alt` defaults to a description of the map rather than nothing. If there is
nothing to draw yet, or `BaseUrl` is not set, it renders nothing rather than a broken image - and
never throws, because an exception during render takes the Blazor circuit with it.

**There is no `ApiKey` parameter, deliberately.** A key placed in an image URL is published to every
browser that loads the page - including under Blazor Server, where it lands in the rendered HTML. Point
`BaseUrl` at a same-origin endpoint that adds the key server-side:

```csharp
// In the host application. The key stays on the server; the browser only ever sees /api/maps.
app.MapGet("/api/maps/staticmap", async (HttpRequest request, IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add("X-Api-Key", mapsApiKey);
    var upstream = await client.GetAsync($"https://maps.panoramicdata.com/staticmap{request.QueryString}", ct);
    return Results.Stream(await upstream.Content.ReadAsStreamAsync(ct), upstream.Content.Headers.ContentType?.ToString());
});
```

## Configuration (`Maps` section / env vars)

| Key | Default | Purpose |
|-----|---------|---------|
| `Maps__PhotonBaseUrl` | `https://photon.panoramicdata.com` | Photon geocoder base URL. |
| `Maps__TilesStyleUrl` | `https://tiles.panoramicdata.com/style.json` | MapLibre style JSON from the tile service. |
| `Maps__SpriteUrl` | — | Sprite base URL for named marker icons, without `.json`/`.png`. Discovered from the style when unset. |
| `Maps__RequireApiKey` | `false` | When `true`, `/v1/*` requires an API key (`X-Api-Key` header or `?key=`). |
| `Maps__ApiKeys__0` … | — | Accepted API keys. |
| `Maps__MaxWidth` / `MaxHeight` / `MaxScale` | 2048 / 2048 / 2 | Output caps. |

API-key enforcement is **off by default** so the open-source image works out of the box; the
canonical hosted service enables it to meter and monetise access.

## Licence

MIT (code). Map data is © OpenStreetMap contributors (ODbL) and must be attributed in rendered
output; Protomaps basemap styles are CC0. MapLibre GL JS is BSD-3-Clause.
