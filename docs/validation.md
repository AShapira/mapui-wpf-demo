# Validation record — 8 October 2026

## Verified results

| Check | Result | Evidence retained locally |
|---|---|---|
| Native Windows locked restore | Passed with the explicit development-validation feed | `.artifacts/windows/restore-windows.log` |
| Native Windows Release solution build | Passed, zero warnings and zero errors | `.artifacts/windows/build-windows.log` |
| Core and real loopback OGC fixture tests on Windows | **49 passed**, zero failed/skipped | `.artifacts/windows/windows-tests.trx` and `test-windows.log` |
| Framework-dependent win-x64 publish | Passed | `.artifacts/windows/publish-windows.log`; binaries in `.artifacts/publish` |
| Published Windows application integration smoke | **16 checks passed** | `.artifacts/windows/desktop-release.json` |
| Actual WPF render exported during smoke run | Inspected: points, line strokes, filled polygons, a polygon hole, selection highlight | `.artifacts/windows/desktop-release.png` |
| Generated documentation/source consistency | Passed on Linux and Windows; normalized LF output | `.artifacts/docs-check.log` and `.artifacts/windows/docs-windows.log` |
| Manual source listing comparison | All source blocks matched their current files after newline normalization | `.artifacts/manual-source-check.json` |

Windows validation used SDK 10.0.401, runtime 10.0.12, Windows x64 (OS version 10.0.26300). The SDK was extracted to a task-specific temporary directory after verifying its official SHA-512 hash; it was not installed system-wide. Linux cross-builds and earlier core tests also ran, but the table reports the final native Windows results. The Linux host lacked ICU, so early Linux tooling used invariant globalization; the native Windows tests did not use that workaround.

## What the tests exercise

Tests cover known-coordinate projection round trips, invalid coordinates, WMS/WFS axis order, JSON configuration binding, GeoJSON projection and polygon holes, geometry-specific styles, independent highlighting, layer order, stale-generation rejection, safe log behavior, and the supported GML geometry subset in both 3.1 and 3.2 encodings.

The fixture is a real loopback HTTP listener with synthetic XML, GML, and valid PNG responses. Protocol tests exercise both WMS versions, WMTS source selection and tile retrieval, both WFS versions, DescribeFeatureType, BBOX/feature limits, authentication on discovery and data requests, wrong CRS, malformed XML, service exceptions, HTTP errors, redirect rejection, header/body timeouts, oversized fixed-length and chunked responses, cancellation, and cross-origin rejection. Application configuration permits HTTPS only; loopback HTTP is explicitly enabled by the test harness.

The desktop smoke check creates a real WPF window. It verifies rendered point picking, line-interior picking, polygon-fill picking, selection/details/highlight behavior, clear-selection, visibility, opacity, layer ordering, addition/removal, and recovery from an unavailable service. It runs against the **published** application, not just the compiler output.

## Boundaries and remaining acceptance work

- **Manual physical-input walkthrough: pending.** The Computer Use runtime rejected this WSL workspace path (`sandboxCwd is not a local file URI`). Desktop coverage here is an in-process integration test plus inspection of its exported rendering. File-dialog interaction, physical mouse/keyboard navigation, display scaling, and accessibility still need the human checklist in lesson 11.
- **Internal Artifactory: not tested.** No internal endpoint was supplied. Development validation used an explicit ignored public NuGet configuration. The distributed configuration remains Artifactory-only with no fallback; lock files were preserved during the native Windows restore.
- **Actual internal OGC services and PKI: not tested.** No work URLs, credentials, or datasets were supplied. Fixtures establish the documented client behavior, not compatibility with every server, application schema, or enterprise certificate/proxy configuration.
- **Self-contained publishing: documented option, not the validated output.** The verified package is framework-dependent and needs the .NET 10 Desktop Runtime. Installers, signing, enterprise authentication beyond Basic, and production performance qualification remain extensions.

No work-server state was changed. Temporary build tools, binaries, logs, and reports are ignored by source control. Real credentials and private settings are not included in the project or generated manual.
