# 10 — Authenticate, diagnose failures, and tune performance

## Purpose and prerequisites

Connect to protected internal services while preserving responsive behavior and useful diagnostics. You need the service's normal access permissions; this tutorial does not provision accounts or change server security.

## Step by step (Windows PowerShell)

1. Set `Authentication` to `Basic` and select a credential prefix in the private settings. Basic authentication requires HTTPS. Leave it as Anonymous when credentials are unnecessary.
2. Obtain credentials through a prompt instead of placing a password in command history:

```powershell
$ogcCredential = Get-Credential
$env:OGC_DEMO_USERNAME = $ogcCredential.UserName
$env:OGC_DEMO_PASSWORD = $ogcCredential.GetNetworkCredential().Password
dotnet run --project src/OgcDemo.Wpf --no-build --no-restore
Remove-Item Env:OGC_DEMO_USERNAME, Env:OGC_DEMO_PASSWORD
$ogcCredential = $null
```

This uses environment variables for a small demo, not a production credential vault. The variables must exist in the process that launches the application. Do not print them or export them into a tutorial bundle.

3. Connect, pan, identify WMS, and retrieve WFS features. All discovery and follow-up requests use the same transport policy.
4. Test an unavailable service or an incorrect profile. The status bar should show the failure without closing the local demo.
5. Run failure tests:

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~Failure|FullyQualifiedName~Unauthorized|FullyQualifiedName~Cancellation|FullyQualifiedName~Authentication"
```

## Troubleshooting

| Symptom | First checks |
|---|---|
| Restore fails | Correct internal v3 feed, feed access, required package/version present, lock hash |
| Startup fails | .NET Desktop Runtime/SDK, full published folder, valid JSON, samples present |
| HTTP 401/403 | Correct prefix variables, user permissions, advertised operation endpoint |
| TLS failure | Trusted enterprise CA and correct server name; do not disable validation |
| Different-origin error | Capabilities advertise another scheme/host/port; use an approved same-origin gateway |
| Empty WMS | Layer/style spelling, supported CRS, coverage, transparency, image format |
| Wrong placement | Source CRS, 4326 axis order, protocol version, not just the declared SRID |
| WMTS missing | Exact layer/style/matrix combination and matrix coverage |
| WFS unsupported | Advertised output format, GML encoding, dimension, geometry subset |
| Feature limit reached | Zoom in; the view is incomplete, not a full export |
| Freeze or excess requests | Look for UI-thread blocking, removed debounce, or repeated connection attempts |

## Performance decisions

Keep payload/feature limits; retrieve WFS by viewport. Do not load a whole enterprise dataset just to display one town. Keep shared styles, use WMTS when the server offers suitable tiles, and avoid unnecessary layer refreshes. For larger vectors, introduce spatial indexing and simplification with accuracy tests. Measure request time, response size, feature count, and UI responsiveness separately; this demo does not claim a throughput benchmark.

Each connection has a whole-request timeout and byte limit. Redirects are rejected, certificates are normally validated, XML DTDs are prohibited, and Basic credentials are confined to the configured origin. Safe log messages describe operation and exception type without echoing arbitrary server text.

## Checkpoint

Authentication reaches both capabilities and data requests. Failure tests pass, the log does not contain the synthetic secret payload, and an unavailable service leaves local layers usable. Air-gapped means no internet access; it does not mean internal OGC services are unavailable.
