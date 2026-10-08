# 09 — Retrieve WFS features within the current view

## Purpose and prerequisites

Load server-side vector geometry for selection and attribute inspection. Obtain an internal WFS 1.1.0 or 2.0.0 endpoint offering a qualified feature type, EPSG:3857, and GML 3.1/3.2 output. The service must support HTTP GET for the operations used here.

## Step by step

1. Enable and configure the WFS profile in the private settings. Use the exact type name, including its namespace prefix when advertised. Start with MaxFeatures 1000 and the default response-size bound.
2. Rebuild, launch, and connect as in lesson 07.
3. Observe the initialization requests: GetCapabilities verifies version, type, CRS, and output format; DescribeFeatureType retrieves and checks that an XML schema was returned. The demo does not attempt a general schema-driven feature editor.
4. Pan to the data coverage and wait briefly. After a 300 ms debounce, GetFeature requests the current BBOX in 3857 with `count` (2.0) or `maxFeatures` (1.1).
5. Click a returned vector feature. Its geometry is highlighted and its scalar attributes are displayed, just like a local feature.
6. Pan rapidly. Old requests are canceled, and generation checks prevent late completions from replacing the new viewport data.
7. Run focused tests:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~Wfs|FullyQualifiedName~Gml|FullyQualifiedName~OldRequest"
```

## Supported GML and limits

The parser accepts GML 3.1/3.2 `pos` and `posList` with two-dimensional Point, LineString, Polygon (holes included), MultiPoint, MultiLineString/MultiCurve containing lines, and MultiPolygon/MultiSurface containing polygons. It deliberately rejects 3D, curved segments, surface patches, and GML 2 encodings. Only scalar feature properties are copied; complex application schemas need a dedicated adapter.

The response must use the requested EPSG:3857 coordinates. Setting a geometry SRID does not repair a server that returned degrees. An explicit unexpected CRS is rejected. The HTTP payload bound protects parsing, and a server returning more than the requested feature limit is rejected. A result at the cap is labeled as potentially incomplete; zoom in rather than treating it as a complete dataset.

WFS network and XML handling are in a small application adapter. Mapsui/NTS still own the resulting vector layer and rendering. This avoids the fixed transport behavior of the pinned built-in WFS provider while retaining a readable teaching implementation.

## Checkpoint

Features follow the current viewport, are selectable, and obey the configured limits. Hiding/removing a WFS layer does not allow an outstanding response to restore it. Server exceptions and unsupported encodings produce a visible error while existing local layers remain usable.
