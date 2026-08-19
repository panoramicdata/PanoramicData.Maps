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
| `PanoramicData.Maps` | Core models + abstractions (`IMapRenderer`, `IGeocoder`, `PhotonGeocoder`, options). | NuGet: `PanoramicData.Maps` |
| `PanoramicData.Maps.Server` | ASP.NET Core HTTP service. | Docker Hub: `panoramicdata/maps` |
| `PanoramicData.Maps.Test` | xUnit v3 tests. | — |

## HTTP API

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Liveness, plus the running build: `{"status":"ok","version":"<semver>","commit":"<short sha>"}`. |
| `GET /` | Usage summary. |
| `GET /v1/geocode?q=London` | Forward geocode (via Photon). |
| `GET /v1/reverse?lon=-0.1278&lat=51.5074` | Reverse geocode. |
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

`icon:` is accepted for compatibility but **not yet drawn** - a default pin is drawn and a warning is
logged. See issue #12.

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

## Configuration (`Maps` section / env vars)

| Key | Default | Purpose |
|-----|---------|---------|
| `Maps__PhotonBaseUrl` | `https://photon.panoramicdata.com` | Photon geocoder base URL. |
| `Maps__TilesStyleUrl` | `https://tiles.panoramicdata.com/style.json` | MapLibre style JSON from the tile service. |
| `Maps__RequireApiKey` | `false` | When `true`, `/v1/*` requires an API key (`X-Api-Key` header or `?key=`). |
| `Maps__ApiKeys__0` … | — | Accepted API keys. |
| `Maps__MaxWidth` / `MaxHeight` / `MaxScale` | 2048 / 2048 / 2 | Output caps. |

API-key enforcement is **off by default** so the open-source image works out of the box; the
canonical hosted service enables it to meter and monetise access.

## Licence

MIT (code). Map data is © OpenStreetMap contributors (ODbL) and must be attributed in rendered
output; Protomaps basemap styles are CC0. MapLibre GL JS is BSD-3-Clause.
