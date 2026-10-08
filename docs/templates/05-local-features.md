# 05 — Build graphics overlays and load GeoJSON

## Purpose and prerequisites

Create visible vector data without contacting an OGC service. Complete the coordinate lesson first.

A Mapsui `MemoryLayer` holds `IFeature` instances. `GeometryFeature` wraps NetTopologySuite geometry. A layer style supplies default point, line, and polygon appearance. These are actual vector features; their attributes and geometry remain available after rendering.

## Step by step

1. Read `FeatureLayers.Graphics`. It constructs a point, a line, and a polygon from invented coordinates. It transforms them to 3857 and attaches a human-readable `name` attribute.
2. Click Add graphics. A new memory layer appears at the top of the user-layer list. Use the selected-layer controls to hide or remove it; the bundled source layer remains independent.
3. Open `samples/sites.geojson` in a text editor. Its three features illustrate a point, line, and polygon with a hole. All names and locations are invented training data.
4. Click Open GeoJSON and choose that file. The importer validates limits and geometry, copies attributes, and transforms all coordinates before adding the layer.
5. Click the polygon interior and compare with its hole. A hole is not a separate feature and should remain unfilled.
6. Test the importer:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~GeoJson|FullyQualifiedName~Highlight"
```

## Implementation details that matter

The importer accepts a FeatureCollection, not an arbitrary JSON document. The reference uses `NetTopologySuite.IO.GeoJSON4STJ` to avoid hand-parsing nested coordinate arrays. The constructor takes geometry that is already in map coordinates. A shared theme chooses SymbolStyle for points and VectorStyle for lines/polygons. Applying SymbolStyle to every geometry would draw vertex symbols instead of line strokes and polygon fills. Styles are reused rather than allocated for every frame.

The importer rejects a legacy `crs` member because RFC 7946 fixes the coordinate convention. Convert non-4326 source data before loading it. Unsupported or invalid data produces a visible import error without deleting existing layers.

Import and parsing occur asynchronously. There is an 8 MiB and 10,000-feature teaching limit. Large datasets need spatial indexing and viewport loading; do not remove these bounds merely to demonstrate a large file.

## Checkpoint

You can load the sample twice as independent layers, toggle either one, and select a geometry. Removing an imported layer does not change the file on disk. The map remains usable if a later import fails.
