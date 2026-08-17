# Contributing to PanoramicData.Maps

Thanks for your interest in contributing!

## Getting started

```bash
dotnet build
dotnet test
```

The unit tests are fast and require no external services (the renderer tests use a stub tile source).

## Ground rules

- **Target framework**: .NET 10 (`net10.0`); the SDK is pinned in `global.json`.
- **Central Package Management**: add/adjust versions in `Directory.Packages.props`; never put a
  `Version` on a `<PackageReference>`.
- **Warnings are errors** (`TreatWarningsAsErrors`), nullable is enabled, and XML doc comments are
  required on public members. Keep the build clean.
- **Style**: tabs (4-wide), file-scoped namespaces — enforced by `.editorconfig`. `ConfigureAwait`
  is required in the library (CA2007); the ASP.NET server project opts out of CA2007.
- **Tests**: xUnit v3 with AwesomeAssertions. Add tests for new behaviour; use
  `TestContext.Current.CancellationToken` in async tests.
- **Rendering fidelity**: prefer changes that improve MapLibre-style fidelity while keeping the
  renderer native (no headless browser / no Node).

## Pull requests

1. Branch from `main`.
2. Keep the build and tests green (`dotnet build && dotnet test`).
3. Describe the change and, for rendering changes, attach a before/after image.

## Reporting issues

Use GitHub issues for bugs and feature requests. For security issues, see [SECURITY.md](SECURITY.md).
