# 12. AIS and ADS-B traffic viewer

The **Traffic Lab** is a third, independent WPF application in this solution. It
receives simulated decoded ship and aircraft reports and measures the cost of
tracking and drawing them in Mapsui. It does not decode radio messages or connect
to real receivers. Every identity and route is synthetic; the authored sea routes
are demonstration waypoints, not navigation data.

The engineering target is 10,000 simultaneous objects. **10,000 is not an enforced
maximum.** Higher nonnegative counts are accepted with a visible warning. The
mailbox and track capacity grow to the configured combined population, so increasing
counts also increases the memory budget. Counts must fit a signed 32-bit total;
this is a representation limit, not a promise that such a population is practical.

## Build and run on Windows

Use the existing .NET 10 SDK/internal NuGet feed instructions from lesson 02.
The Windows Desktop runtime is required; Linux can compile the Windows target but
cannot execute WPF. Keep the repository's pinned package versions and lock files.

```powershell
dotnet restore OgcDemo.slnx --configfile NuGet.local.Config --locked-mode
dotnet build OgcDemo.slnx -c Release --no-restore
dotnet run --project src/TrafficDemo.Wpf -c Release --no-build --no-restore
```

To open an interactive session with 5,000 ships and 5,000 aircraft already running:

```powershell
dotnet run --project src/TrafficDemo.Wpf -c Release --no-build --no-restore -- --ships 5000 --aircraft 5000 --start
```

`--start` begins simulation after the basemap connection attempt and leaves the window
open for interaction. Without it, press Start in the viewer.

To publish all supported full applications and run core checks:

```powershell
./scripts/Build.ps1 -Publish
./scripts/Start-TrafficDemo.ps1
```

The traffic executable is published separately to
`.artifacts/traffic-publish/TrafficDemo.Wpf.exe`. The existing OGC executable keeps
its original output location. Running from source connects to the basemap directly;
the launch helper additionally checks/starts the existing GeoServer deployment.

## GeoServer integration

The default service is `http://127.0.0.1:8602/geoserver`, from the sibling
`small-gis-demos/osm-basemap-demo` project. The server inspected for this work serves
the Eastern Europe bundle, including Greece and the Adriatic coast, with worldwide
Natural Earth overview context. It advertises English Dark Matter, OSM Bright,
Positron, Toner, MapTiler Basic and world layers. The default is
`omt:dark-matter-en`, WMTS 1.0.0, `EPSG:3857`, PNG. BruTile normalizes GeoServer's advertised empty style identifier to `default`; the viewer selects that normalized
style, which this server accepts.
Auxiliary WMTS ResourceURL entries such as TileJSON and FeatureInfo are removed from
the client parser input, leaving only tile templates for the selected image format.
This avoids BruTile 6 treating metadata responses as map images. WMS 1.3.0 is an explicit alternative. `None` selects overlay-only measurement.

The app shares `OgcDemo.Core` transport/capabilities code. Only this viewer opts
into HTTP for loopback endpoints; remote services require HTTPS. Credentials in URLs
and cross-origin capabilities endpoints remain disallowed. No public basemap,
font server, API key or live traffic service is required.

```powershell
# A healthy service is reused, without touching the container engine.
./scripts/Ensure-TrafficBasemap.ps1 -CheckOnly
./scripts/Start-TrafficDemo.ps1

# If the existing deployment is stopped inside WSL, name its distribution.
./scripts/Start-TrafficDemo.ps1 -WslDistribution RHEL10
```

On Linux/WSL, run `bash scripts/ensure-traffic-basemap.sh`. Its default existing
container is `east-europe-bundle_geoserver_1`, using Podman. Override
`TRAFFIC_BASEMAP_CONTAINER` and `TRAFFIC_CONTAINER_ENGINE` for an already prepared
deployment. Windows Podman defaults to `east-europe-bundle`; the PowerShell helper
also accepts an explicit `-ContainerName`. Helpers verify the container's configured
port binding before starting it, including when it is stopped. They do not build or
import a bundle, delete/recreate volumes, pull images, change firewall rules, or start
a Podman machine. If no prepared deployment exists, use that project's documented
bundle launcher first. Starting the app never implicitly provisions infrastructure.

A failed basemap connection leaves simulation usable against the plain background.
The status displays the failure and the **Connect / retry** button retries. A
successful capabilities connection alone is not proof of tile delivery; the benchmark
also records basemap request failures, and the captured map must be inspected.

## Operator workflow

1. Set ship and aircraft counts independently, or stage the 1,000 / 5,000 / 10,000
   mixed presets. Defaults are 500 of each. Preset buttons do not discard a running
   simulation until **Apply configuration and restart** is clicked.
2. Set the random seed, report intervals and simulation speed. Defaults are seed
   42, five seconds for ships, two seconds for aircraft and speed 1. Reports are
   staggered, not delivered as a single population-wide burst.
3. Choose the Adriatic/Greece regional scenario or the dense Ionian Sea scenario.
   The dense home view contains the complete population; there is no clustering.
4. Press **Start**. **Pause** freezes simulation time, route movement and ageing;
   **Resume** continues. **Reset** clears history/selection and leaves the new
   population paused. Applying configuration clears old state and starts afresh.
5. Click a symbol or table row. Details show identity, position, speed, course,
   aircraft altitude, report time/sequence and freshness. **Zoom to selected**
   centers a closer view. **Follow** recenters once per published snapshot. Panning,
   touch movement or mouse-wheel navigation suspends following; check Follow to resume.
6. Use the Filters tab to search identities/names or narrow traffic. The track table
   shows display-eligible tracks across the scenario, not just the current viewport.
7. Use Performance for counters and current-report export. A current-report export
   is not a completed benchmark or a soak test.

Symbols rotate in five-degree display increments using shared styles. Actual report
course remains unquantized in the details. Cyan ship silhouettes and amber aircraft
silhouettes distinguish types. Stale symbols/traces fade. Only the selected object
gets a label by default, avoiding thousands of overlapping text labels.

## Two kinds of filtering

Both panels support ship/aircraft admission, case-insensitive identity/name substring,
speed range in knots, aircraft altitude range in feet, and geographic bounds in
`west,south,east,north` longitude/latitude order. Blank values are unrestricted.
Altitude constraints apply only to aircraft. A west value greater than east denotes
a dateline-crossing geographic filter. Invalid or reversed ranges are rejected.

**Input admission** excludes reports before track updates and purges existing tracks
that no longer qualify. Clearing that filter reconstructs tracks only as new reports
arrive; their discarded history is not restored. The simulator still generates the
full configured population, making generated versus accepted load visible.

**Display filtering** hides symbols, traces and table rows while retaining ingestion,
tracking and bounded history. Clearing it restores the retained data. A selected
track hidden by a filter remains identified in the details panel, while its highlight
and following are suspended. Expired/input-purged selected tracks are similarly marked.
Input filtering and display filtering are separate benchmark operations: changing
one must never be reported as changing the other.

## Configuration and units

`src/TrafficDemo.Wpf/appsettings.json` contains a `Traffic` section. Copy it to
`appsettings.local.json` alongside it for private overrides; the latter is ignored by
Git and excluded from publish output. JSON configuration overlays individual keys.
Editing UI controls affects the current process, not the files on disk.

| Section | Important defaults |
| --- | --- |
| Simulation | Ships 500; Aircraft 500; Seed 42; ShipIntervalSeconds 5; AircraftIntervalSeconds 2; SpeedMultiplier 1; Dense false |
| History | MaximumPoints 150; RetentionSeconds 300; SampleSeconds 2; StaleSeconds 30; ExpireSeconds 120 |
| InputFilter / DisplayFilter | Ships and Aircraft true; Search empty; numeric limits and Bounds unset |
| Basemap | Url loopback port 8602; Layer omt:dark-matter-en; Protocol WMTS; Enabled true |

History intervals, feed timestamps, route progress and ageing use simulation time.
The one-second snapshot schedule, benchmark duration and latency measurements use
wall/monotonic time. At simulation speed 10, five simulated seconds pass in roughly
half a real second. Accelerated scenarios therefore increase report load as well as
movement. A report interval of at least 0.1 simulated seconds is required. History
requires at least two points, positive finite intervals and expiry greater than the
stale interval. Configure stale/expiry above your expected report cadence when using
unusually slow sources. MaximumPoints has a defensive 100,000-per-track bound.

The default map update cadence is once per second, with no millisecond animation or
continuous extrapolation. Aircraft move farther between reports than ships; zoom in
or use simulation speed to make ship movement apparent without falsifying their speed.
