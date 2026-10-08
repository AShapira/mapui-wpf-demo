# Configuration contract

## Package feed

`NuGet.Config` clears inherited feeds and declares one placeholder Artifactory NuGet v3 endpoint. Copy it to ignored `NuGet.local.Config`, replace that endpoint, and pass `--configfile NuGet.local.Config` on restore. Keep `<clear />`. There is no public fallback. Package hashes and resolved versions are in committed `packages.lock.json` files.

Use your organization's credential provider or `NuGetPackageSourceCredentials_Artifactory` environment variable if the feed requires authentication. Do not put secrets into the template. NuGet's credential variable uses `Username=...;Password=...` and, when required by the feed, `ValidAuthenticationTypes=Basic`. Obtain credentials through your organization's normal procedure. Internal OGC credentials and Artifactory credentials are separate.

## Application settings

Copy the **whole** `src/OgcDemo.Wpf/appsettings.json` to `src/OgcDemo.Wpf/appsettings.local.json` before editing. JSON configuration arrays merge by numeric index; a short replacement Services array does not delete remaining base entries. Keep unused entries disabled. Restart after edits; configuration is read at startup.

| Setting | Meaning / default |
|---|---|
| Demo.InitialExtent | `[west,south,east,north]` in degrees; no dateline crossing |
| Services[].Name | Human-readable tree/selector label |
| Kind | Exactly `WMS`, `WMTS`, or `WFS` |
| Enabled | Exposes the profile in the selector; does not auto-connect |
| Url | Absolute HTTPS OGC endpoint, without username/password |
| Version | WMS 1.1.1/1.3.0; WMTS 1.0.0; WFS 1.1.0/2.0.0 |
| Layer | Advertised layer identifier or qualified WFS type name, case-sensitive |
| Style | WMS style (empty means default); exact advertised WMTS style |
| Crs | `EPSG:3857`; other service display CRSs are outside the core |
| MatrixSet | Exact WMTS matrix-set identifier, not a guessed zoom-level label |
| Format | WMS/WMTS advertised image type, normally `image/png`; WFS discovers GML output |
| Authentication | `Anonymous` or `Basic` |
| CredentialPrefix | Environment variable prefix, default `OGC_DEMO` |
| TimeoutSeconds | Whole-request deadline, 1–120; default 20 |
| MaxFeatures | WFS per-request cap, 1–10,000; default 1,000 |
| MaxResponseBytes | Per-response bound, 1 KiB–32 MiB; default 8 MiB |

Set `OGC_DEMO_USERNAME` and `OGC_DEMO_PASSWORD` only when using Basic with that prefix. The app reads them from its process environment. Do not enter passwords in commands saved to shell history; use an interactive credential prompt and assign its values to environment variables in the launching session. Clear those variables after the application exits.

Capabilities may advertise request endpoints different from the capabilities URL. The demo accepts a different path on the same scheme, host, and port; it rejects a different origin and HTTP redirects. Ask for a same-origin gateway if your deployment advertises multiple origins. It never forwards Basic credentials to an unapproved host.

## Data and scope limits

GeoJSON: FeatureCollection, RFC 7946 longitude/latitude, no legacy `crs` member, valid nonempty geometry, at most 8 MiB and 10,000 features. Latitude must be within approximately ±85.0511 degrees for Web Mercator.

WFS: HTTP GET, GML 3.1/3.2, 2D Point/LineString/Polygon (including holes), MultiPoint, MultiLineString/MultiCurve containing lines, and MultiPolygon/MultiSurface containing polygons. Curved segments, surface patches, complex nested attributes, remote XLinks, GML 2 `coordinates`, and 3D are unsupported. Only scalar feature properties are displayed. A layer at the feature cap is explicitly marked incomplete; the core does not silently paginate or claim complete coverage.

Logs: `%LOCALAPPDATA%\OgcDemo\logs\ogc-demo-YYYYMMDD.log`, UTC timestamps. Daily filenames are rotation, not retention; see the deployment lesson. The logger omits exception payloads; callers must still use authored messages, never sensitive state.
