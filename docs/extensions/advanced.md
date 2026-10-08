# Advanced extension lessons

These are extension designs and exercises. They are deliberately **not implemented** by the core demo. Finish the working OGC/local-feature lessons first. Each extension has a concrete integration point and an acceptance test so a human or local model can continue without confusing proposed behavior with existing functionality.

## SQLite — persist user preferences

**Goal:** restore user-created layer profiles and visibility/order after restarting. Start with ordinary application tables, not spatial storage.

1. Add a pinned SQLite provider available in Artifactory to a separate persistence project. Keep native runtime assets explicit for win-x64.
2. Introduce an `ILayerProfileStore` with asynchronous load/save operations. Store profile identifiers, display settings, and a schema version; exclude passwords and access tokens.
3. Use parameterized SQL, transactions for a whole workspace save, and a migration table. Place the database under `%LOCALAPPDATA%\OgcDemo`, never beside a read-only installed EXE.
4. Load persisted settings at startup before adding layers. Save only explicit user settings, not transient highlights or all fetched WFS data.
5. Test first launch, restart, interrupted writes, schema migration, and a read-only directory. A corrupt store must not prevent the bundled local demo from starting.

## GeoPackage — read an offline vector dataset

**Goal:** load a selected GeoPackage feature table by viewport. A `.gpkg` is a SQLite container with spatial metadata and geometry encoding; it is not equivalent to an arbitrary table of WKB.

1. Select a vetted provider that understands GeoPackage metadata, geometry headers, and spatial indexes. Verify its support on the chosen .NET/Windows versions before adding it.
2. Read `gpkg_contents`, geometry-column metadata, source CRS, and bounds. Reject a table with an unknown CRS rather than assuming 4326.
3. Add a data-source adapter returning `GeometryFeature` objects in the map CRS. Use a spatial index and a bounded query for the current extent.
4. Keep tiles and vector tables as separate capabilities; a provider supporting vector tables does not necessarily support GeoPackage raster tiles.
5. Test multiple geometry types, polygon holes, empty/null geometry, missing indexes, multiple tables, and a non-4326 dataset. Compare transformed control points with a trusted GIS tool.

For small teaching inputs, converting a selected table to RFC 7946 GeoJSON through an approved conversion tool is a simpler first exercise. Do not claim native GeoPackage support merely because an export can be loaded.

## GDAL — controlled preprocessing

**Goal:** prepare large or unfamiliar source formats outside the UI rendering loop.

1. Use the organization's approved GDAL distribution. Keep its executables, native libraries, PROJ data, and driver versions together. Verify `ogrinfo --version` and the relevant driver's availability.
2. Inspect a source layer and its CRS before converting. Example commands below are **GDAL shell commands**, not application features:

```powershell
ogrinfo -so -al source.gpkg
ogr2ogr -f GeoJSON -t_srs EPSG:4326 -lco RFC7946=YES training-export.geojson source.gpkg selected_table
```

Use a new destination filename. Inspect feature count, bounds, and representative geometries before importing into the demo.

3. If later integrating conversion into the app, execute a configured tool path with `ProcessStartInfo.ArgumentList`; do not concatenate untrusted filenames into a shell command.
4. Run conversion off the dispatcher, capture bounded diagnostic output, allow cancellation, and preserve the original source.
5. Test filenames with spaces, invalid CRS, missing projection grids, unavailable drivers, cancellation, and nonzero exit codes. Log operation results without recording sensitive paths or features.

## Persistent offline cache — separate network isolation from disconnection

**Goal:** allow a deliberately prepared map area to remain available when internal services are unreachable.

1. Define user-selected area, scales, layers, storage limit, expiry, and whether the service permits caching. Air-gapped access to an internal server does not require prefetching by itself.
2. Start with a WMTS tile cache; include service/profile identity, layer, style, format, matrix-set identifier, matrix level, row/column, and authorization scope in the cache key.
3. Store a manifest with extent, CRS, creation time, provenance, and completeness. Never show a partial package as a complete offline map.
4. Separate authenticated users' caches. Credentials are never cache keys or stored payload metadata. Define invalidation and deletion behavior explicitly.
5. Add explicit online/offline mode and a visible missing-data indication. Bound disk use and concurrent downloads; do not launch an unbounded tile crawl.
6. Test disconnected restart, missing tiles, expired entries, changed styles, user changes, interrupted prefetch, and full disk. Check both rendered coverage and manifest counts.

WFS snapshots need their own data-version and edit-conflict policies. WMS viewport-image caching is not interchangeable with WMTS tile caching.

## Search — visible-data search first

**Goal:** find features by their displayed attributes and navigate to them.

1. Add an `IFeatureSearch` interface and a local implementation over loaded vector features. Return a stable feature reference, layer identity, label, and extent.
2. Clearly label the first implementation "Search loaded features"; a bounded WFS viewport is not a complete catalog.
3. Keep filtering and result limits explicit. For server search, discover supported filters and use standard OGC Filter Encoding rather than assuming GeoServer CQL support.
4. Debounce input, cancel previous requests, and reuse the generation-check pattern. Do not block typing on network work.
5. Test duplicate labels, missing attributes, Unicode text, empty results, stale requests, and selecting a result whose layer was removed.

## Editing — local geometry before remote transactions

**Goal:** edit a local copy with undo/redo, validation, and explicit save.

1. Keep selection, editing, and persisted source geometry separate. Begin with adding/moving points; extend to lines/polygons only after undo is reliable.
2. Define commands containing old/new geometry, stable feature identity, and layer identity. Reproject at input/output boundaries.
3. Validate topology before committing edits. Invalid polygon rings must not silently reach the data store.
4. Save a new local GeoJSON file with confirmed destination semantics before attempting server writes. The core demo currently never writes back to an input dataset.
5. Add WFS-T only when the service advertises it and a separate explicit authorization/conflict design exists. Reading WFS does not imply write access.
6. Test undo/redo, canceled edits, invalid geometry, lost selection, save failure, concurrent modification, and CRS round trips.

## Enterprise server integration and deployment

The generic client contract is capabilities plus data requests. Ask the provider's administrator for advertised CRS/format support, layer permissions, bounded WFS access, correct externally advertised operation URLs, and reliable HTTPS. GeoServer can satisfy this contract, but the client does not need its REST API, configuration files, or a PostGIS account.

Production extensions include Windows Integrated Authentication or OAuth, enterprise proxy handling, credential-vault integration, signed packaging, automated update policy, log retention, and accessibility/scaling testing. Implement each against the actual organization's contract and validate it separately. Do not add server provisioning to this client tutorial.
