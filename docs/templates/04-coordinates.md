# 04 — Navigate and transform coordinates

## Purpose and prerequisites

Learn the difference between geographic coordinates, map coordinates, and screen coordinates. Use the running local demo. No remote service is needed.

GeoJSON positions use longitude then latitude in degrees. The map uses EPSG:3857 in metres. A mouse position uses pixels relative to the map control. Never pass one of these coordinate types where another is expected.

## Step by step

1. Inspect `Demo.InitialExtent`: west, south, east, north, in longitude/latitude degrees.
2. Follow `Coordinates.ProjectExtent`: validate the extent, transform both corners, create a Mapsui `MRect` in map metres.
3. Click Home. `Navigator.ZoomToBox` chooses a resolution that fits the transformed extent.
4. Pan and zoom with the mouse, then use the +/− toolbar buttons. Rotation is locked so WMS identification uses the same unrotated bounding box as the visible image.
5. Read `Coordinates.ToMercator`. It copies geometry and changes every coordinate through an NTS coordinate-sequence filter. Setting `SRID = 3857` alone would only relabel the geometry and is incorrect.
6. Run focused checks:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~Projection|FullyQualifiedName~AxisOrder|FullyQualifiedName~InvalidCoordinates"
```

## Axis order

For EPSG:4326, WMS 1.3.0 uses latitude/longitude axis order in BBOX; WMS 1.1.1 uses longitude/latitude. The core WFS 1.1/2.0 path also respects the CRS axis order. The actual remote-layer demo requests EPSG:3857, which keeps x/y ordering consistent. These rules do not change GeoJSON's longitude/latitude order.

The axis-order helper is tested even though service rendering is restricted to 3857. Do not solve axis problems by indiscriminately swapping all coordinates. Check the protocol version, CRS identifier, and encoding first.

Web Mercator excludes the poles and distorts distance and area. The application rejects latitude outside its valid range instead of producing infinities. The core has no measurement tool and does not present Web Mercator area as ground-truth area. Local national grids and dateline-crossing extents require explicit extensions.

## Checkpoint

Round-trip projection checks pass. A feature near longitude 35 degrees has an x coordinate of millions of metres, not 35 metres. Home restores the expected synthetic training area. If data appears far away, inspect source CRS before modifying navigation.
