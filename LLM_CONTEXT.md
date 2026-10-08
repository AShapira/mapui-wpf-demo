# Local LLM entry point

Goal: help a human build and understand a .NET 10 WPF GIS client. The server is a generic existing OGC provider. Dependencies come from internal Artifactory. Do not add public basemaps or runtime internet dependencies.

Read `docs/configuration.md`, `docs/api-reference.md`, then the relevant numbered lesson. If only one file can be ingested, use `docs/BUILD-GUIDE.md`; its source sections use exact repository-relative paths. Use individual chapters for a smaller context window. Never assume an entire manual fits a model's context.

## Project map

| Area | Source | Responsibility |
|---|---|---|
| Composition | src/OgcDemo.Wpf/App.xaml.cs | Host, DI, configuration, file log, window lifecycle |
| UI | src/OgcDemo.Wpf/MainWindow.xaml | Map, layer tree, controls, details |
| View adapter | src/OgcDemo.Wpf/MainWindow.xaml.cs | Map tapped and tree selection events only |
| State | src/OgcDemo.Wpf/MainViewModel.cs | Commands, layer state, async refresh, selection |
| Local geometry | src/OgcDemo.Core/FeatureLayers.cs | GeoJSON, graphics, highlight cloning |
| Projection | src/OgcDemo.Core/Coordinates.cs | 4326 to/from 3857, bbox axis order |
| OGC raster | src/OgcDemo.Core/OgcLayers.cs | Mapsui WMS and BruTile WMTS adapters |
| OGC vector | src/OgcDemo.Core/WfsClient.cs | Capabilities, schema request, bounded GetFeature |
| GML | src/OgcDemo.Core/GmlFeatures.cs | Explicit 2D geometry subset |
| Transport | src/OgcDemo.Core/OgcTransport.cs | Credentials, same-origin requests, limits, safe XML |
| Concurrency | src/OgcDemo.Core/LatestRequest.cs | Generation checks and cancellation |
| Tests | tests/OgcDemo.Tests | Synthetic loopback HTTP fixtures and behavior tests |
| Manual generation | tools/ManualBuilder | Source-derived lessons and complete manual |

## Invariants

- WPF runs on Windows. Core tests can run on Linux; that does not prove WPF behavior.
- Mapsui package versions are 5.1.0; BruTile is 6.0.0. Do not mix v4 samples with these APIs.
- The Windows target includes OS version 10.0.19041.0 for compatible SkiaSharp assets.
- All map coordinates are EPSG:3857 metres. GeoJSON is RFC 7946 longitude/latitude. Assigning an SRID does not transform coordinates.
- WMS/WMTS pixels are not vector features. A WMS identify result is text, not automatically a selectable geometry.
- The selection layer stays above all user layers and is excluded from picking.
- UI collection mutations occur on the WPF dispatcher. Network awaits must not block it.
- Canceled/stale WFS responses must never replace the newest viewport's data.
- Only HTTPS is accepted in application configuration. HTTP loopback permission exists only in tests.
- Credentials are process environment variables; private config is ignored and excluded from publish output. Do not log URLs, headers, server bodies, or feature attributes.
- No direct PostGIS access, GeoServer REST calls, server provisioning, persistent caches, or WFS transactions in the core.
- Never claim work-service compatibility from local fixtures alone.

## Commands (Windows PowerShell, repository root)

```powershell
dotnet restore OgcDemo.slnx --configfile NuGet.local.Config --locked-mode
dotnet build OgcDemo.slnx --no-restore
dotnet test tests/OgcDemo.Tests --no-restore
dotnet run --project src/OgcDemo.Wpf --no-build --no-restore
dotnet run --project tools/ManualBuilder --no-restore -- --check
```

After code changes regenerate the guide, build, and run relevant tests. Describe implemented behavior separately from extension proposals. Use existing configuration models rather than inventing unrelated settings. If a service fails, inspect its advertised capabilities and the supported subset before changing coordinate math.
