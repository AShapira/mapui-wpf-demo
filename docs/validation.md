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

## Traffic Lab validation — 2026-10-17

The traffic viewer is a separate application; the earlier OGC results above remain
historical evidence for that application. New traffic evidence is retained locally in
`.artifacts/traffic/`, with native Windows run reports, CSV samples and captured PNGs
under `.artifacts/traffic/windows/`. The test workstation was verified as an AMD Ryzen
9 9950X3D (16 cores / 32 logical processors), with approximately 61.3 GiB usable RAM,
Windows 10.0.26300, .NET 10.0.12 and Mapsui 5.1.0. The measured map viewport was
approximately 1106 ×607 WPF logical pixels at 120 DPI. These are workstation-specific
results, not a guarantee for other hardware or arbitrary history/report-rate settings.

### Build and functional checks

- Linux cross-build and native Windows Release build/publish succeeded. Core tests
  cover 18 traffic scenarios, including unclustered 12,000-object data. The existing
  OGC tests passed; an additional WMTS resource-selection regression raises that
  suite from 49 to 50 tests.
- Native smoke verified rendered symbols and picking, trace publication, selected
  details, following suspension, pause, separate input/display filters, reset/start,
  and recoverable service failure. The process exited cleanly after the shutdown fix.
  These are in-process checks, not physical mouse/keyboard automation.
- The running Eastern Europe OSM GeoServer was reused. Both Windows and WSL could
  fetch its capabilities; the corrected WMTS integration received actual map images.
  The helper's healthy-service path and script syntax were checked. The stopped-server
  start path was not exercised against the live service; it was unnecessary to stop it.
- Visual inspection of native captures confirmed directional cyan ships, amber
  aircraft, the OSM basemap and the population table. Overlap in the dense scenario
  is intentional; counts are not reduced by clustering.

The 30-second dense 10,000-object check with traces and GeoServer recorded all 10,000
symbols, zero coalesced/capacity-rejected reports, zero basemap failures, p95/p99
whole-run ingestion-to-render upper bounds of **1.131 / 1.212 seconds**, p95 render
upper bound **127 ms**, and p95 dispatcher upper bound **102 ms**. All applicable
short-run targets passed. This short run alone does **not** establish soak stability.

### Integration defects found during validation

The implementation was corrected using actual native/service evidence:

1. GeoServer advertises an empty WMTS style identifier; BruTile normalizes it to
   `default`, so the viewer selects that normalized value.
2. GeoServer also advertises TileJSON/FeatureInfo resources. BruTile 6 could select
   those templates as tile requests, producing incomplete basemaps. The shared adapter
   now passes only tile resources for the configured image format to the parser.
   A fixture regression verifies that auxiliary endpoints are never requested as tiles.
3. A synchronously completed disposal could reenter the WPF Closing event. Final
   close is now dispatched after the event returns; runners check process exit codes.

Earlier diagnostic runs with missing tiles or failed shutdown are not final acceptance
results. Their existence is not evidence of a passing GeoServer integration.

Native execution used a Windows-local source copy and a portable SDK. A copied
cross-built DLL was rejected by this workstation's Application Control during an
intermediate attempt; the native Windows build ran successfully. No security settings
were changed. Use the documented Windows build workflow when reproducing these results.

### Completed 30-minute soak

The native Release fixed-view run used 5,000 ships plus 5,000 aircraft, dense mode,
normal 5 s / 2 s reporting, five-minute / 150-point histories, traces enabled and the
live GeoServer WMTS basemap. It completed in 1,800.17 seconds and exited with code 0.
Evidence: `.artifacts/traffic/windows/soak-10k-1800s.{json,csv,png}`.

| Whole-run measurement | Result |
| --- | ---: |
| Generated and accepted reports | 6,224,391 each |
| Rejected / coalesced / capacity-rejected reports | 0 / 0 / 0 |
| Tracked and dense-view symbols observed | 10,000 / 10,000 |
| Retained history points at completion | 1,036,453 |
| Ingestion-to-render p95 / p99 upper bounds | 1,259 / 1,333 ms |
| Dispatcher p95 upper bound | 111 ms |
| Mapsui render p95 upper bound | 136 ms |
| Snapshot preparation p95 upper bound | 155 ms |
| UI publication p95 upper bound | 6 ms |
| Displayed-report age p95 / p99 upper bounds | 4.77 / 5.17 s |
| Basemap image responses / request failures | 32 / 0 |
| Replaced pending snapshots | 0 |
| Private memory at completion | 489.7 MiB |
| Private-memory trend after minute 10 | +0.539 MiB/min |

All recorded bounds and latency targets passed. The memory trend passed the explicit
1 MiB/min diagnostic threshold; post-minute-10 private bytes ranged approximately
470–521 MiB and fluctuated around a plateau. This does not prove the absence of every
possible leak. Displayed-report age includes the five-second ship cadence and is
separate from ingestion latency. Stored history was not disabled; simplification
reduces the displayed vertices independently of the retained-point count.

The application allocated about 164 GiB cumulatively over this run (roughly 93 MiB/s),
while retained process memory stayed bounded. This distinguishes allocation churn
from a retention leak and is a useful optimization target for future work. The run
used about 601 CPU seconds over 1,800 wall seconds. No other traffic benchmark was
run concurrently. Server cache state was retained; the application began with an
empty client cache. The PNG shows all symbols submitted in the dense viewport, with
intentional heavy overlap along the synthetic corridors.

### Population/type and basemap comparisons

All **18 native Release cases passed** functional checks, full-population/dense-view
checks, latency/dispatcher targets, bounded-state checks and basemap health. Each
case ran for 30 seconds in a fresh process, with dense mode and traces enabled.
The early interaction steps select/follow a track and return home. The full interaction
cycle is checked separately in a longer run. The committed runner defaults to
60 seconds; this recorded comparison explicitly used `-Seconds 30`.

Evidence: `.artifacts/traffic/windows/matrix/`, including `summary.json` and separate
JSON/CSV/PNG files for every case. Values below are conservative whole-run histogram
upper bounds in milliseconds. The dispatcher column is the worse p95 of the two
basemap modes for that row.

| Objects | Population | GeoServer ingestion p95 / p99 | Overlay ingestion p95 / p99 | Dispatcher p95 max |
| ---: | --- | ---: | ---: | ---: |
| 1,000 | ships | 1085 / 3739 | 1085 / 3513 | 14 |
| 1,000 | aircraft | 1116 / 1775 | 1071 / 1572 | 15 |
| 1,000 | mixed | 1077 / 1427 | 1077 / 1426 | 9 |
| 5,000 | ships | 1148 / 3832 | 1157 / 3920 | 53 |
| 5,000 | aircraft | 1183 / 1865 | 1185 / 1801 | 46 |
| 5,000 | mixed | 1116 / 1862 | 1138 / 2122 | 44 |
| 10,000 | ships | 1217 / 4013 | 1209 / 3946 | 104 |
| 10,000 | aircraft | 1231 / 1907 | 1234 / 1889 | 97 |
| 10,000 | mixed | 1158 / 1975 | 1191 / 1994 | 130 |

The all-ships interaction p99 is higher than the fixed-view soak: returning home
introduces reports that were offscreen during the focused view, with a five-second
source cadence. These samples remain included. No objects or traces were silently
removed to pass a target. Short comparison runs do not replace the 30-minute soak.

### Additional views, traces and interaction checks

These final native Release runs all passed functional and applicable performance
checks. Values are whole-run conservative upper bounds in milliseconds. Each is a
fresh process. The six 20-second 10k comparisons complete the dense/dispersed,
traces-on/off and GeoServer/overlay combinations for the mixed 10,000-object case;
they are not a claim that the optional full 72-case Cartesian matrix was executed.

| Case / evidence stem | Seconds | Ingestion p95 / p99 | Dispatcher p95 | Result |
| --- | ---: | ---: | ---: | --- |
| dense-10k-interactions | 65.3 | 1267 / 3378 | 115 | Pass |
| dispersed-10k-map | 20.1 | 1126 / 1212 | 130 | Pass |
| dispersed-10k-overlay | 20.1 | 1188 / 1264 | 120 | Pass |
| dense-10k-no-traces-map | 20.1 | 1141 / 1209 | 121 | Pass |
| dense-10k-no-traces-overlay | 20.1 | 1148 / 1209 | 74 | Pass |
| dispersed-10k-no-traces-map | 20.2 | 1176 / 1215 | 84 | Pass |
| dispersed-10k-no-traces-overlay | 20.1 | 1135 / 1195 | 72 | Pass |
| dense-12k-map | 20.3 | 1203 / 1248 | 135 | Pass |

The 65-second interaction run includes selection/following, zoom-to-object, home,
display filtering, restoring the full display, and panning. The 12,000-object run
tracked and drew all 12,000 objects; the visible warning did not impose a 10,000 cap.
The native functional smoke (`smoke-final.json`) passed **14 checks** and exited 0.
Its deliberate pause/reset and failed-service injection make its latency and basemap
health acceptance fields fail; it is not a steady-load performance acceptance run.
The baseline comparisons and soak above have zero basemap failures.

Final Windows build: **zero warnings/errors; 68 tests passed** (18 traffic, 50 OGC).
The final WPF binary includes an additional startup-only guard that refuses to overwrite
an existing benchmark report even through the error path. The soak and baseline matrix
predate that guard; tracking, rendering, measurement and OGC logic are unchanged.
The core DLL hashes are identical; source/binary hashes and build/test evidence are
retained in `run-manifest.json` and the native logs/TRX files.

The final live **WMS** check (1,000 objects, regional view, 20 seconds) also passed
with image responses and zero basemap failures (`wms-final.json`). The original OGC
application's newly published native smoke passed **all 16 checks** (`ogc-final.json`).
A separate startup regression preserved an existing report byte-for-byte and returned
the expected refusal exit code 1 (`report-preservation-check.json`).

Generated documentation and full source listings are checked against the current
files. Private configuration, binaries and machine-specific reports remain ignored;
the unrelated pre-existing `docs/nuget-packages.md` was not changed. Physical
mouse/keyboard/file-dialog testing remains unverified because the available Computer
Use runtime rejected this WSL workspace path. Programmatic native interactions and
rendered captures are the evidence reported here. The live GeoServer was not stopped
to exercise the optional startup path.
