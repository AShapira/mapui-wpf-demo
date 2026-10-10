# Mapsui OGC Desktop Lab

A documented .NET 10 WPF GIS application for an air-gapped Windows workstation with internal Artifactory and existing OGC services. Start with invented local features, then connect WMS, WMTS, and WFS. No public basemap, database, or server installation is required.

**Start reading:** [Complete build guide](docs/BUILD-GUIDE.md). For shorter reading sessions use [lesson 01](docs/chapters/01-foundations.md). Local coding models should start with [LLM context](LLM_CONTEXT.md).

## Hello World: just a map

For the smallest example, open [HelloMap.Wpf](src/HelloMap.Wpf/HelloMap.Wpf.csproj). It is a separate WPF app with one map control, one WMS basemap, and one in-memory polyline layer. Four white circles mark the line's vertices. All map setup is in [MainWindow.xaml.cs](src/HelloMap.Wpf/MainWindow.xaml.cs); there is no view model, DI, or configuration framework. See the [Hello World README](src/HelloMap.Wpf/README.md) for setup and an explanation of its NuGet dependencies.

1. In `src/HelloMap.Wpf/MainWindow.xaml.cs`, replace `WmsUrl` and `WmsLayer` with your internal WMS endpoint and advertised layer name. The checked-in URL is a placeholder. This minimal example uses anonymous WMS 1.3.0 with `EPSG:3857` and `image/png`.
2. Edit the four `(longitude, latitude)` pairs if your basemap covers another area; the example line is near Tel Aviv. The code projects them to Web Mercator and zooms to the line.
3. Configure `NuGet.local.Config` as described below, then run on Windows:

```powershell
dotnet restore src/HelloMap.Wpf --configfile NuGet.local.Config --locked-mode
dotnet run --project src/HelloMap.Wpf --no-restore
```

Drag to pan and use the mouse wheel to zoom. The polyline appears immediately; the basemap loads asynchronously. If the WMS capabilities request fails, a message explains the failure and the polyline remains usable. A configured, reachable service covering the vertices is required to see the basemap. Use the full app below for authentication and other OGC features.

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
