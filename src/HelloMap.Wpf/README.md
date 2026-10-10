# Hello Map: a minimal WPF demo

One window, one Mapsui map control, one WMS basemap, and one polyline layer with four visible vertex markers. Map setup lives in [MainWindow.xaml.cs](MainWindow.xaml.cs); the window layout is [MainWindow.xaml](MainWindow.xaml). This example uses ordinary code-behind, without MVVM, dependency injection, or a configuration framework.

## Configure and run

Use Windows with the .NET 10 SDK. Run the commands below from the **repository root**; the project also uses the root `global.json` and `Directory.Build.props`.

1. In `MainWindow.xaml.cs`, replace `WmsUrl` and `WmsLayer` with your internal WMS endpoint and its advertised layer name. The checked-in URL is a placeholder. The service must support anonymous WMS 1.3.0, `EPSG:3857`, and `image/png`.
2. The four `(longitude, latitude)` pairs describe a line near Tel Aviv. Change them if your service covers another area. The app projects them to Web Mercator and zooms to the line.
3. If you have not already configured a private NuGet feed, copy and edit the root configuration:

```powershell
Copy-Item NuGet.Config NuGet.local.Config
notepad NuGet.local.Config
# Replace the example Artifactory URL with your internal NuGet v3 feed.
```

Restore and run:

```powershell
dotnet restore src/HelloMap.Wpf --configfile NuGet.local.Config --locked-mode
dotnet run --project src/HelloMap.Wpf --no-restore
```

Drag to pan; use the mouse wheel to zoom. The red polyline and white vertex circles appear immediately. The WMS basemap loads asynchronously underneath them. If loading WMS capabilities fails, the app displays an error and keeps the polyline usable. Seeing the basemap requires a reachable service with data covering the line.

## Why does such a small demo have 36 NuGet packages?

The [project file](HelloMap.Wpf.csproj) declares only **two direct package references**, both version **5.1.0**:

| Direct package | What this demo uses |
| --- | --- |
| `Mapsui.Wpf` | The WPF `MapControl` and its mapping/rendering dependencies. |
| `Mapsui.Nts` | `GeometryFeature`, which connects a NetTopologySuite `LineString` to a Mapsui layer. |

Those libraries declare their own dependencies. NuGet follows those declarations recursively, producing **34 transitive packages**, for **36 packages total** in the current [packages.lock.json](packages.lock.json). These counts describe this checked-in version and target framework; upgrades can change them.

“Direct” means explicitly listed in the project file, not “called by our code.” For example, this code uses types from `Mapsui` and `NetTopologySuite`, although both arrive transitively. `Mapsui.Nts` also arrives through `Mapsui.Rendering.Skia`; its explicit reference documents this demo's geometry dependency. Removing that reference alone would not remove it from the dependency tree.

| Transitive package or family | Why it appears |
| --- | --- |
| `Mapsui`, `NetTopologySuite` | Map, WMS provider, layers, styling, projection, and line/coordinate types used by the demo. |
| `Mapsui.Rendering.Skia`, `SkiaSharp`, `SkiaSharp.Views.WPF`, `SkiaSharp.Views.Desktop.Common` | Mapsui's drawing engine and WPF integration. |
| `Mapsui.Tiling`, `BruTile` | Tiling support included by `Mapsui.Wpf`. This demo uses an untiled WMS `ImageLayer`. |
| `NetTopologySuite.IO.GeoJSON4STJ`, `NetTopologySuite.Features` | GeoJSON/feature support included upstream. This demo constructs its line in code and does not read GeoJSON. |
| `Svg.Skia`, `Svg.Model`, `Svg.Custom`, `ShimSkiaSharp`, `ExCSS` | SVG and styling support included by the rendering stack. The demo supplies no custom SVG assets. |
| `HarfBuzzSharp`, `SkiaSharp.HarfBuzz`, `Topten.RichTextKit` | Text shaping and rich-text support included by the rendering stack. |
| `OpenTK`, `OpenTK.GLWpfControl`, and the other `OpenTK.*` packages | Dependencies brought in through `SkiaSharp.Views.WPF`. The OpenTK dependency tree includes graphics, input, windowing, compute, and audio components; the demo itself does not use audio or compute APIs. |
| `SkiaSharp.NativeAssets.*`, `HarfBuzzSharp.NativeAssets.*` | Platform-specific native binaries declared upstream. Linux entries can appear in this Windows project's lock file; Linux native libraries do not execute on Windows. |

The lock file records the **resolved dependency graph**, including versions and package hashes. It is not a trace of code executed at runtime, nor a list of files necessarily loaded by Windows. Asset selection depends on the target framework and runtime; rendering may also use library features internally.

## Restoring in an air-gapped environment

Your internal feed or prepared NuGet cache must provide the complete resolved package graph, including transitive packages. Mirroring only `Mapsui.Wpf` and `Mapsui.Nts` is insufficient. Windows/.NET targeting packs must also be available as required by the installed SDK; the lock file is not a complete offline SDK installation manifest.

Keep `packages.lock.json` under source control. `--locked-mode` checks that restore agrees with the committed dependency graph. Do not delete entries by hand to make the list shorter: upstream dependencies still request those packages. To change dependencies, edit package references deliberately, restore to regenerate the lock file, then review and validate the result.

Inspect the resolved package list after restoring, from the repository root:

```powershell
dotnet list src/HelloMap.Wpf/HelloMap.Wpf.csproj package --include-transitive --no-restore
```

For authentication, other OGC protocols, and layer controls, see the [full application README](../../README.md).
