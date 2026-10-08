# Mapsui OGC Desktop Lab

A documented .NET 10 WPF GIS application for an air-gapped Windows workstation with internal Artifactory and existing OGC services. Start with invented local features, then connect WMS, WMTS, and WFS. No public basemap, database, or server installation is required.

**Start reading:** [Complete build guide](docs/BUILD-GUIDE.md). For shorter reading sessions use [lesson 01](docs/chapters/01-foundations.md). Local coding models should start with [LLM context](LLM_CONTEXT.md).

## Run on Windows

Use Windows PowerShell in a Windows-local copy of the project. Install your organization's .NET 10 SDK through its approved software channel first.

```powershell
Copy-Item NuGet.Config NuGet.local.Config
notepad NuGet.local.Config
# Replace only the example Artifactory URL with your internal NuGet v3 feed.
dotnet restore OgcDemo.slnx --configfile NuGet.local.Config --locked-mode
dotnet build OgcDemo.slnx --no-restore
dotnet run --project src/OgcDemo.Wpf --no-build --no-restore
```

The two local layers load immediately. Drag to pan, wheel to zoom, and click a shape to inspect its attributes. The orange overlay marks the selected geometry. Enable your endpoints in a private copy of the configuration as described in the [configuration reference](docs/configuration.md).

## What is implemented

- WPF/MVVM, DI, JSON configuration, daily file logging, layer tree and layer controls.
- Graphics and GeoJSON, points/lines/polygons, selection and independent highlighting.
- WMS 1.1.1/1.3.0, WMTS 1.0.0, WFS 1.1.0/2.0.0 with a bounded GML 3.1/3.2 subset.
- Capabilities checks, anonymous/Basic authentication, request limits, cancellation, debounced WFS fetching, and stale-response protection.
- EPSG:4326 local data transformed to EPSG:3857 display; service layers must advertise EPSG:3857.

[Advanced extensions](docs/extensions/advanced.md) describe SQLite, GeoPackage, GDAL, persistent caching, search, and editing. These are design lessons, not completed core features. See [validation](docs/validation.md) for precisely what was tested.

## Maintain the guide

```powershell
dotnet run --project tools/ManualBuilder --no-restore
dotnet run --project tools/ManualBuilder --no-restore -- --check
```

Edit authored prose under `docs/templates`, then regenerate. Lesson listings and the combined manual are generated from the actual source. The combined guide includes all source and configuration files needed to reconstruct the reference project; it is not dependent on opening external documentation.

Local-LLM documentation is project context only. This application does not contact or embed a model.
