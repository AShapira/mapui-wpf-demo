# 14. Measuring traffic performance

Run Release builds on Windows. A successful build or core test cannot establish WPF
rendering performance. Measurements belong to the recorded hardware, viewport/DPI,
population, timing, history and basemap/cache configuration.

## Repeatable commands

```powershell
./scripts/Test-TrafficDesktop.ps1 -Report .artifacts/traffic/smoke-new.json
./scripts/Benchmark-Traffic.ps1 -Seconds 60
./scripts/Benchmark-Traffic.ps1 -Seconds 60 -FullMatrix -Soak
```

Each runner refuses an existing report/output location. The default matrix is 18
sequential cases: 1,000, 5,000 and 10,000 objects; all ships, all aircraft and mixed;
with GeoServer and overlay-only. It uses a dense viewport and traces. `-FullMatrix`
adds dispersed views and trace-disabled comparisons for all populations/types/basemap
modes. The scripted interaction cycle selects/follows a track, returns home, applies
and clears a display filter, and pans. Fixed-view runs are available directly through
the executable. `-Soak` adds a 30-minute, 10,000-object mixed dense run with traces and
GeoServer. Do not run multiple benchmark apps simultaneously for baseline evidence.
Keep the benchmark window visible, use a consistent window size/DPI, and avoid other
heavy workloads during a comparison. Minimizing or manually navigating the window
changes the rendering workload and should be recorded as a separate experiment.

```powershell
.artifacts/traffic-publish/TrafficDemo.Wpf.exe --report C:\Temp\traffic-10k.json --seconds 1800 --ships 5000 --aircraft 5000 --dense
.artifacts/traffic-publish/TrafficDemo.Wpf.exe --report C:\Temp\traffic-overlay.json --seconds 60 --ships 5000 --aircraft 5000 --dense --no-basemap
```

Additional arguments are `--no-traces`, `--exercise`, and `--smoke`. Native runs export
JSON, a CSV process time series and a final PNG. The interactive Performance tab can
export current JSON/CSV without completing a timed test. JSON records the configuration,
viewport, DPI, runtime, processor description, Mapsui version and cache conditions.
The server cache is deliberately not flushed; each new app starts with a new client
cache. This is a repeatable workload, not a claim of a cold server-cache comparison.

## What the measurements mean

| Measurement | Meaning |
| --- | --- |
| Generated | Reports actually offered by the simulator; cadence may fall under saturation |
| Accepted / rejected | Tracker admissions and validation/filter/order rejections |
| Coalesced | Pending reports superseded by a newer report for the same identity |
| CapacityRejected | New identities rejected by a full mailbox |
| Tracked / filtered / eligible | Current state, excluded display state, and eligible tracks |
| Drawn symbols / traces | Spatial-query candidates submitted to Mapsui for the viewport; overlapping symbols still count individually |
| Ingestion-to-render ms | Monotonic receive timestamp to first completed on-screen Mapsui render for a new visible report |
| Report age seconds | Simulation-time age of reports submitted in a completed frame |
| Preparation / publication ms | Worker geometry/index construction and batched UI publication respectively |
| Render ms | Time inside the actual Mapsui renderer's `Render` call |
| Dispatcher ms | Delay until a background-priority UI callback executes |
| Memory / allocation / GC | Private, working-set and managed bytes; cumulative allocated bytes and GC counts |
| CPU | Process CPU time; dashboard normalizes usage over logical processors |

Preparation timing starts after the tracking-state/history snapshot copy; ingestion-to-render
latency still includes that earlier work.

`MeasuredRenderer` forwards rendering, drawable creation, picking and bitmap export
to the pinned standard renderer. Timing is recorded **after** drawing returns; merely
queuing a refresh is not a completed-frame measurement. It does not measure physical
monitor scan-out. Bitmap export is not counted as an on-screen frame.

Ingestion latency is recorded once per newly rendered track sequence. Reports replaced
before rendering are not latency samples: coalescing and snapshot-replacement counters
must therefore accompany the percentile. Offscreen tracks are not drawn and do not
produce rendering-latency samples until in view. A newly visible report can already be
several seconds old. Report-age measurements include that staleness independently.

Metric buffers retain at most 20,000 recent samples per metric for exact dashboard
percentiles. Separate fixed histograms accumulate the whole run without retaining
individual samples. Benchmark acceptance uses `LifetimeP95UpperBound` and
`LifetimeP99UpperBound`: 1 ms buckets for timings, 0.01 s buckets for report age.
The overflow bucket is above 60 seconds for timings (600 seconds for report age);
a percentile falling there is null and fails the corresponding target. These are
conservative bucket upper bounds. Long-run stability must also be assessed using
the CSV, counters and scenario coverage, rather than a good final-window percentile. Process samples are bounded to
approximately 720 per timed run. Dashboard CPU and allocations include the application,
not just the renderer, and the renderer wrapper itself adds measurement overhead.

## Acceptance targets

For 10,000 objects with normal report intervals and bounded traces:

- p95 ingestion-to-render at most 3 seconds; p99 at most 5 seconds.
- p95 dispatcher response at most 250 ms during the interaction workload.
- No queue, track or retained-history count above its configured bound.
- The full configured population must be tracked; dense-view symbols must be checked
  against that population. Dispersed/zoomed views legitimately draw fewer symbols.
- A 30-minute soak with stable post-warm-up memory. The report calculates a private-byte
  linear trend after minute 10; at most 1 MiB/min is the explicit diagnostic threshold.
  Inspect the time series as well, because a threshold alone cannot prove absence of leaks.

`success` means the run completed its functional checks with a population and native
frames. `acceptance` reports latency, dispatcher, bounds and soak separately. A short
run has `soakEvaluated: false`, not a passing soak. Basemap failures are reported
separately and fail baseline acceptance; a working overlay cannot establish GeoServer success. A failure is not
fixed by silently disabling traces, clustering objects, reducing counts or changing
report intervals. Such changes are distinct, clearly labelled comparison cases.

## Test coverage and troubleshooting

Core tests cover deterministic simulation, speed/direction changes, pause/cancellation,
report validation and ordering, mailbox replacement/capacity, time/count history limits,
sequence/time/dateline gaps, stale/expiry transitions, input versus display semantics,
spatial queries for crossing traces, immutable render snapshots, trace simplification,
metric bounds, engine shutdown and intact 1k/5k/10k/12k populations.

The Windows smoke run verifies native rendering, symbol hit testing, trace publication,
selection, following suspension, pause, display filtering without state loss, input
purging, reset/start, and recoverable GeoServer failure. It is in-process integration,
not physical mouse/keyboard automation. Inspect its PNG for visual correctness and
record separately whether a human or desktop automation exercised physical input.

- **No window/report:** verify `dotnet --list-runtimes` contains both .NET 10 and
  Microsoft.WindowsDesktop.App. An empty leftover SDK directory is not an installation.
- **Plain background:** check both WMS capabilities and WMTS tile delivery from the
  Windows host. A WSL-only curl result does not establish Windows access.
- **No selected track after filtering:** the details explain whether it is hidden,
  purged or expired; clear the relevant filter and wait for new reports where necessary.
- **Traces appear short:** ships move slowly, retained history starts empty, and the
  trace ends at the latest sampled observation. Zoom in, wait, or deliberately change
  simulation speed. Higher speed also increases input load.
- **High memory:** inspect population ×MaximumPoints and GeoServer separately; private
  bytes include native drawing/cache allocations as well as managed objects.
- **High latency or coalescing:** compare preparation, publication and rendering costs;
  compare overlay-only versus GeoServer runs and inspect GC/CPU. Keep all configuration
  differences in the report rather than attributing every delay to Mapsui.

See [validation](../validation.md) for the actual checked-out implementation's results.

Timing/GC counters belong to the application session. Interactive Reset/Apply clears
simulation state and its admission counters, but retains profiler history. Start a
fresh process for configuration comparisons; the benchmark runner does this for every
case. Smoke tests intentionally pause/reset/filter and are functional checks, so their
latency acceptance fields are not representative steady-load performance results.
