# 13. Bounded traffic pipeline and map rendering

The design separates decoded observations, current track state, bounded history,
renderable map data and WPF controls. No simulation report creates a WPF element.

```text
SimulatedFeed (background producer)
  → validation + latest-report mailbox (one pending report per identity)
  → TrackStore (one background consumer; admission and order checks)
  → bounded trace rings + current state
  → immutable TrafficSnapshot, once per wall-clock second
  → display filter + zoom simplification + spatial indexes (background)
  → one replaceable pending RenderSnapshot
  → one queued Dispatcher publication
  → indexed Mapsui symbol/trace layers → standard Skia renderer
                                      → completed-render measurements
```

## Contracts and ownership

`ITrackFeed.ReadAsync(CancellationToken)` supplies decoded `TrackReport` values. A
report contains type, identity, name, UTC/simulation timestamp, sequence,
longitude/latitude, knots, course degrees and optional altitude feet. Its key combines
type and identity, so a ship and aircraft with the same textual ID remain distinct.
Real wire-protocol adapters are a future extension behind this interface; the supplied
viewer constructs `SimulatedFeed` and is not a raw AIS/ADS-B decoder.

`ReceivedReport` adds a monotonic ingestion timestamp. The report timestamp determines
ordering/history/age; the monotonic timestamp measures elapsed processing time without
wall-clock adjustment errors. `SimulationClock` advances from monotonic elapsed time
and freezes on pause. Equal seed, configuration, epoch and evaluation time produce
identical simulated positions. The live scheduler naturally has operating-system
jitter; determinism refers to route/attribute evaluation, not exact thread scheduling.

The feed writes only the mailbox. The consumer alone owns `TrackStore` and its trace
rings. Filter configurations are immutable values published atomically. Snapshots
contain detached history arrays; code treats all published data and geometry as
read-only. Geometry/index construction happens on the consumer worker. WPF receives
only a completed snapshot and performs one batched replacement and graphics refresh.
Mapsui owns its own render caches; the app does not dispose native resources while
Mapsui might be drawing them. Shutdown cancels and joins workers before disposing map
resources. Resets replace the engine and use generation checks to discard old work.

## Overload and ordering

The mailbox has capacity equal to the configured population, including populations
above 10,000. A newer report replaces a pending report for the same key. Replacement
increments `Coalesced`; an already full mailbox rejects a new identity and increments
`CapacityRejected`. Invalid reports and non-increasing sequences/backward timestamps
are rejected. The tracker repeats ordering validation against the last accepted state,
rejects future/expired observations, and enforces its configured track capacity.

The simulator never emits an unbounded catch-up burst after a scheduling stall. It
emits the current observation and schedules the next due report. This means the
configured interval is a target cadence, not a promise that every report is generated
under saturation. Generated/accepted counters and process CPU must be considered
alongside latency. Coalescing deliberately loses intermediate observations; it is
appropriate for a current-position viewer, not a lossless recorder.

The UI has one pending snapshot slot and at most one queued publication callback.
A newer snapshot replaces an older unpublished one and increments `ReplacedSnapshots`.
The dispatcher probe also awaits its previous callback, preventing a monitoring
backlog from becoming another load source. A slow renderer can increase latency and
coalescing, but cannot create an unbounded queue of UI updates.

## Trace storage and gaps

Each track has a chronological ring capped by both age and number of points. Default
retention is the last five simulated minutes, at most 150 samples, with a minimum
two-second sample spacing. At the 10,000-object target, the strict upper bound is
**1.5 million retained history points**. Five-second ship reporting normally uses
fewer points than the bound. Memory also includes track metadata, detached snapshot
arrays, symbols, line geometry, spatial indexes, WPF rows and Mapsui/Skia caches; do
not equate the point count with total process memory.

Sequence gaps, sufficiently long report gaps and dateline discontinuities mark a
new trace segment. Unsampled reports still update current position; a pending gap
marker survives until the next retained sample. Rendering never joins these segments
across missing observations. Traces end at the latest retained sample, which can lag
the current symbol by the configured sampling interval.

No report for 30 simulated seconds marks a track stale; 120 seconds expires it and
releases its history. History ages out even when no new reports arrive. Pause freezes
these rules rather than making every paused object stale. Clearing input filters does
not recover discarded samples.

## Rendering choices and costs

Two `IndexedTrafficLayer` instances separate symbols and traces. Their immutable
`FeatureIndex` uses an NTS STRtree, built on the worker and queried with the current
viewport. Symbol queries include a screen-size margin. Trace indexes cover the full
polyline envelope, so a line crossing the viewport remains eligible even when its
current symbol and both endpoints are outside it. Envelope intersection is a broad
phase; Mapsui performs final drawing/clipping.

This replaces the per-query collection scan in the pinned
[Mapsui 5.1.0 MemoryLayer implementation](https://raw.githubusercontent.com/Mapsui/Mapsui/5.1.0/Mapsui/Layers/MemoryLayer.cs).
The one-object selection highlight still uses `MemoryLayer`; its collection has at
most one feature, so indexing it would add unnecessary work.

The standard Mapsui renderer draws the actual points and NTS multiline geometries.
This tests Mapsui's map/navigation/rendering path rather than a separate overlay canvas.
No per-track WPF control is placed on the map. The data grid enables row/column
virtualization and recycling; it does not instantiate 10,000 visible rows.

Only two SVG image resources are registered. Symbol styles are shared across type,
staleness and 72 heading buckets; trace styles are also shared. This prevents a
per-report image/style registration leak. Per-track immutable feature caching reuses
unchanged symbols and traces. Cache entries are removed when the track leaves the
eligible set; Mapsui's rendering caches have their own bounded eviction policy.

Douglas–Peucker simplification is applied separately to each displayed trace segment.
The tolerance is 0.75 times a power-of-two bucket of map resolution, in projected
units. The original retained history is never simplified. Resolution bucketing avoids
rebuilding all lines for tiny zoom changes; the next snapshot uses the new zoom bucket.
Selected labels are independent of the traffic style cache. Map rotation is handled
by Mapsui's directional style support.

Per report, current-state/history work is approximately constant. At each snapshot,
filtering and index construction inspect the configured track set; line simplification
cost depends on retained points. Viewport rendering cost depends on the eligible
geometry in view. Spatial indexing helps a zoomed-in view, but cannot eliminate the
cost of drawing every object in a dense 10,000-object view. All eligible symbols remain
present, even when their pixels overlap. The performance harness must expose that cost.

`DataHasChanged` and graphics refresh update the overlays. Routine traffic ticks do
not call `Map.Refresh`, which would request basemap data again. Pan/zoom and explicit
basemap connection changes legitimately trigger tile/image requests. GeoServer remains
a basemap server; moving traffic and history are not uploaded to it.

The retained-point count is a **storage** measurement. It is not the number of vertices
submitted to the renderer: zoom-dependent simplification can reduce a straight trace
to its endpoints. Traces-disabled runs still maintain bounded tracking history, which
isolates much of the drawing/geometry cost from source and history ingestion cost.
