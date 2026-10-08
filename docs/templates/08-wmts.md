# 08 — Select a WMTS tile matrix set

## Purpose and prerequisites

Add a tile service without assuming its matrix identifiers look like public XYZ zoom levels. Obtain an internal WMTS 1.0.0 capabilities endpoint and an advertised EPSG:3857 matrix set.

## Step by step

1. Edit the existing private configuration. Enable the WMTS profile and fill in its URL, exact layer identifier, style identifier, image format, and matrix-set identifier.
2. Rebuild and launch from Windows PowerShell:

```powershell
dotnet build src/OgcDemo.Wpf --no-restore
dotnet run --project src/OgcDemo.Wpf --no-build --no-restore
```

3. Choose that profile and connect. The adapter parses capabilities with BruTile and selects a source by **layer + style + format + matrix set**. Human titles are not stable identifiers.
4. Move the tile layer beneath vector overlays. Zoom between several scales and observe tile replacement.
5. Temporarily use an invalid MatrixSet identifier, restart, and reconnect. The demo must reject it rather than silently choosing a different source.
6. Run the focused tests:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~Wmts|FullyQualifiedName~UnsupportedServiceCrs"
```

## How tile delivery works

A WMTS matrix defines scale denominator, origin, tile dimensions, matrix width/height, and a matrix identifier. BruTile translates viewport requests into those matrix coordinates and uses the advertised ResourceURL or KVP operation endpoint. This is why the application does not construct a guessed `/z/x/y.png` path.

The source wrapper retains BruTile's URL construction but routes tile bytes through the same bounded, authenticated transport used for capabilities. Authenticating only the capabilities request is insufficient; every protected tile request needs authentication too.

The demo supports 3857 raster matrix sets and rejects other CRSs. It does not reproject cached raster tiles. Persistent caching and disconnected prefetch are extension topics; ordinary in-memory rendering caches are not an offline dataset.

## Checkpoint

Tiles render at multiple scales, align with local vectors, and use the configured style and matrix set. Authentication tests verify both discovery and tile requests. Missing tiles may be outside advertised matrix limits or the data coverage; check capabilities and requested matrix identifiers before changing the origin arithmetic.
