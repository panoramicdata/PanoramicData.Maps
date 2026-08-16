# Copilot Instructions

## Project Overview

**PanoramicData.Maps** is an open-source static-map + geocoding microservice: it renders map images
with markers, icons, lines and polygon overlays, and proxies geocoding. It is hard-tied (for now) to
a self-hosted [Photon](https://github.com/komoot/photon) geocoder and a
[Protomaps](https://protomaps.com)/[MapLibre](https://maplibre.org) tile service — a self-hostable
alternative to Google Static Maps + Geocoding. Maps are rendered by driving MapLibre GL JS in
headless Chromium (Playwright) and screenshotting.

## Solution Structure

| Project | Purpose |
|---------|---------|
| `PanoramicData.Maps` | Core library — models, `IMapRenderer`, `IGeocoder`/`PhotonGeocoder`, `MapsOptions`, DI. Published as a NuGet package. |
| `PanoramicData.Maps.Server` | ASP.NET Core HTTP service + `PlaywrightMapRenderer`. Published as a Docker Hub image (`panoramicdata/maps`). Not packable. |
| `PanoramicData.Maps.Test` | xUnit v3 tests. |

## Conventions (match PanoramicData.NugetManagement)

- **Target**: .NET 10 (`net10.0`); SDK pinned via `global.json`.
- **CPM**: all versions in `Directory.Packages.props`; `<PackageReference>` without `Version`.
- **Directory.Build.props**: `TreatWarningsAsErrors`, `Nullable enable`, `GenerateDocumentationFile`,
  `NuGetAuditMode All`. XML doc comments are therefore **required** on public members.
- **Versioning**: Nerdbank.GitVersioning (`version.json`).
- **Style**: tabs (4-wide), file-scoped namespaces, implicit usings — enforced by `.editorconfig`
  (copied from NugetManagement). `.editorconfig` enables **CA2007** (ConfigureAwait) as an error in
  libraries; the ASP.NET app project disables CA2007 (`NoWarn`) since it has no SynchronizationContext.
- **Licence**: MIT.
- **Tests**: xUnit v3 (`OutputType=Exe`, `GenerateDocumentationFile=false`), AwesomeAssertions,
  coverlet. Use `TestContext.Current.CancellationToken` in async tests.

## Build & Test

```
dotnet build --configuration Release
dotnet test  --configuration Release
```

Build/test are fast; there is no need to run Playwright/Chromium for unit tests (the geocoder tests
use a stub `HttpMessageHandler`). Rendering is exercised end-to-end only in the container.

## Design Notes / TODO

- **Rendering** is behind `IMapRenderer` so a native (non-browser) renderer could replace Playwright.
- **maplibre-gl** is currently loaded from a CDN in the render page — vendor it into the image to
  remove the runtime dependency.
- **Dockerfile** bases the runtime on the official Playwright .NET image; keep its tag aligned with
  the `Microsoft.Playwright` package version and a .NET 10-capable variant.
- **Auth**: `Maps:RequireApiKey` is off by default (OSS-friendly); the hosted canonical service turns
  it on to meter/monetise.
- **Icons from the sprite sheet** and richer marker labels are not yet implemented (markers, lines,
  polygons are).
