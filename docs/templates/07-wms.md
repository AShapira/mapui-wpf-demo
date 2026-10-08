# 07 — Connect WMS and identify map images

## Purpose and prerequisites

Display a raster map from an existing internal WMS and request information at a clicked pixel. Obtain its HTTPS endpoint, advertised layer name, supported style, and a layer that offers EPSG:3857. No server administration is part of this lesson.

## Step by step

1. Copy the entire base configuration to the private override:

```powershell
Copy-Item src/OgcDemo.Wpf/appsettings.json src/OgcDemo.Wpf/appsettings.local.json
notepad src/OgcDemo.Wpf/appsettings.local.json
```

Do this only once; do not overwrite an existing local configuration. Replace the WMS placeholder URL and layer name, set `Enabled` to true, and choose `Version` 1.3.0 or 1.1.1 according to the service. Keep `Crs` as EPSG:3857 and `Format` as an advertised image type.

2. Build again so the local configuration is copied to the output directory, then launch:

```powershell
dotnet build src/OgcDemo.Wpf --no-restore
dotnet run --project src/OgcDemo.Wpf --no-build --no-restore
```

3. Select the profile and click Connect selected service. The adapter downloads capabilities, checks the version/layer/CRS/format, and constructs a Mapsui WmsProvider and ImageLayer.
4. Move the WMS layer below the local overlays if you want it to act as a basemap. Pan and zoom; new GetMap requests include the viewport BBOX and image dimensions.
5. Select the WMS tree row and click a place without a local vector feature. If the layer advertises GetFeatureInfo with text/plain or XML output, its response appears as literal text in the details panel.
6. Run the WMS fixture tests:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~Wms|FullyQualifiedName~Authentication"
```

## Understand the requests

GetCapabilities describes the service. GetMap returns image pixels, with LAYERS, STYLES, CRS/SRS, BBOX, WIDTH, HEIGHT, and FORMAT. GetFeatureInfo adds QUERY_LAYERS, INFO_FORMAT, and I/J in WMS 1.3.0 or X/Y in 1.1.1. The identifier request must describe the same viewport as the displayed image.

Queryable and supported CRS can be inherited from a parent capabilities layer. The adapter checks that hierarchy. An ordinary `<Exception>` capabilities section advertises response formats; it is not itself a service error. Only an actual exception-report document triggers the service-error path.

A raster identify response does not contain guaranteed geometry. This demo displays text safely; it does not execute returned HTML or invent a vector highlight for a pixel. A local vector hit takes precedence over raster identification.

## Checkpoint

The WMS image aligns with the graphics, and identify works when advertised. A bad layer or unsupported CRS yields an actionable error. Do not confuse successfully reading capabilities with successfully rendering GetMap; verify both. Real work-service testing is a separate acceptance step from the bundled fixtures.
