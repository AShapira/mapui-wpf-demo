# Pinned APIs and references

Verified against Mapsui tag `5.1.0` and the restored packages. Documentation pages may advance beyond this release; the package lock and matching source tag take precedence.

| Task | API used |
|---|---|
| WPF control | `Mapsui.UI.Wpf.MapControl`, XAML assembly `Mapsui.UI.Wpf` |
| Map event | `Map.Tapped`, `MapEventArgs.GetMapInfo(layers)` |
| Layer picking | Explicit vector-layer list; do not copy v4 `IsMapInfoLayer` examples |
| Vector storage | `MemoryLayer.Features`, `DataHasChanged()` |
| NTS geometry | `Mapsui.Nts.GeometryFeature` |
| WMS | `Mapsui.Providers.Wms.WmsProvider(XmlDocument, getBytesAsync)` in Mapsui core |
| WMS image layer | `Mapsui.Layers.ImageLayer` |
| WMTS discovery | `BruTile.Wmts.WmtsCapabilitiesParser.Parse(Stream)` |
| WMTS selection | `WmtsTileSchema.Layer`, `Style`, `TileMatrixSet`, `Format`, `Srs` |
| Authenticated tiles | Override `HttpTileSource.GetTileAsync(HttpClient, TileInfo, CancellationToken?)` |
| Vector projection | NTS `ICoordinateSequenceFilter` and Mapsui `SphericalMercator` |

The WFS adapter owns HTTP requests rather than using Mapsui's built-in `WFSProvider`. Inspection of the pinned implementation found a fixed HTTP timeout and no caller cancellation/response-size interface. The small adapter gives this tutorial consistent transport limits and predictable generic-service behavior; it intentionally supports a documented GML subset, not all WFS encodings.

Direct package choices: Mapsui/Mapsui.Nts/Mapsui.Tiling/Mapsui.Wpf 5.1.0; BruTile 6.0.0; NetTopologySuite 2.6.0; GeoJSON4STJ 4.0.0; Microsoft.Extensions.Hosting and Logging.Abstractions 10.0.0; CommunityToolkit.Mvvm 8.4.0. Exact transitive versions and content hashes are recorded per project. The project files are the authoritative package list.

Primary references:

- [Mapsui 5.1.0 source](https://github.com/Mapsui/Mapsui/tree/5.1.0)
- [Mapsui WPF package](https://www.nuget.org/packages/Mapsui.Wpf/5.1.0)
- [BruTile 6.0.0 source](https://github.com/BruTile/BruTile/tree/6.0.0)
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [WMS standard](https://www.ogc.org/standards/wms/)
- [WMTS standard](https://www.ogc.org/standards/wmts/)
- [WFS standard](https://www.ogc.org/standards/wfs/)
- [GeoJSON RFC 7946](https://www.rfc-editor.org/rfc/rfc7946)

External references are supplementary. Core instructions, code, fixtures, configuration, and expected results are included locally for air-gapped use.

## Traffic Lab contracts

`ITrackFeed` supplies cancellable decoded `TrackReport` values. `TrafficEngine` owns
background simulation/tracking and emits immutable `TrafficSnapshot` values.
`SnapshotBuilder` creates read-only `RenderSnapshot` geometry/indexes.
`IndexedTrafficLayer` provides viewport queries to the standard Mapsui renderer.
`MeasuredRenderer` forwards renderer operations and measures completed drawing.
`TrackFilter` is shared by separate admission and display paths. See
[lesson 13](chapters/13-traffic-data.md) for ownership and ordering rules and
[lesson 14](chapters/14-traffic-performance.md) for JSON/CSV evidence semantics.
