using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using TrafficDemo.Core;
using Xunit;

namespace TrafficDemo.Tests;
public sealed class TrafficTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static TrackReport Report(long sequence = 1, double seconds = 0, TrackKind kind = TrackKind.Ship, string identity = "123") =>
        new(kind, identity, "SIM-TEST", Epoch.AddSeconds(seconds), sequence, 19, 38, 12, 90, kind == TrackKind.Aircraft ? 30000 : null);
    private static ReceivedReport Received(TrackReport report) => ReceivedReport.Now(report);
    [Fact] public void PopulationAboveTargetIsAllowedButWarned()
    {
        var o = new SimulationOptions { Ships = 15000, Aircraft = 5000 }; o.Validate(); Assert.Equal(20000, o.Population); Assert.Contains("Warning", o.Warning);
        Assert.Throws<ArgumentException>(() => new SimulationOptions { Ships = -1 }.Validate());
        Assert.Throws<ArgumentException>(() => new SimulationOptions { Ships = int.MaxValue, Aircraft = 1 }.Validate());
    }
    [Fact] public void SimulatorIsRepeatableAndMovementMatchesSpeed()
    {
        var o = new SimulationOptions { Ships = 10, Aircraft = 10 }; var a = new SimulatedFeed(o, new(1, Epoch)); var b = new SimulatedFeed(o, new(1, Epoch));
        for (var i = 0; i < 20; i++) Assert.Equal(a.ReportAt(i, 123, 5), b.ReportAt(i, 123, 5));
        var start = a.ReportAt(0, 0, 1); var end = a.ReportAt(0, 1, 2);
        Assert.NotEqual(start.Longitude, end.Longitude);
        var x = (end.Longitude - start.Longitude) * Math.Cos(start.Latitude * Math.PI / 180); var y = end.Latitude - start.Latitude;
        var distanceKm = Math.Sqrt(x * x + y * y) * 111.195;
        Assert.InRange(distanceKm, start.SpeedKnots * 1.852 / 3600 * .98, start.SpeedKnots * 1.852 / 3600 * 1.02);
        Assert.Null(start.AltitudeFeet); Assert.NotNull(a.ReportAt(19, 0, 1).AltitudeFeet);
    }
    [Fact] public void RouteTurnsWithoutTeleporting()
    {
        var route = new Route((19, 38), (19.1, 38)); var a = route.Position(route.LengthKm - .001); var b = route.Position(route.LengthKm + .001);
        Assert.Equal(a.Lon, b.Lon, 8); Assert.Equal(180, Math.Abs(a.Course - b.Course), 5);
    }
    [Fact] public async Task PauseFreezesTimeAndCancellationStopsFeed()
    {
        var clock = new SimulationClock(1, Epoch); var before = clock.Now; await Task.Delay(30); Assert.Equal(before, clock.Now);
        clock.SetPaused(false); await Task.Delay(30); Assert.True(clock.Now > before); clock.SetPaused(true);
        var paused = clock.Now; await Task.Delay(30); Assert.Equal(paused, clock.Now);
        using var cancel = new CancellationTokenSource(80); var feed = new SimulatedFeed(new() { Ships = 1, Aircraft = 0 }, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await foreach (var _ in feed.ReadAsync(cancel.Token)) { } });
    }
    [Fact] public void MailboxRetainsNewestPerKeyAndEnforcesCapacity()
    {
        var m = new LatestReportMailbox(1);
        Assert.True(m.Offer(Received(Report()))); Assert.True(m.Offer(Received(Report(2, 2))));
        Assert.False(m.Offer(Received(Report(1, 1)))); Assert.False(m.Offer(Received(Report(identity: "456"))));
        Assert.False(m.Offer(Received(Report() with { Longitude = double.NaN })));
        var one = Assert.Single(m.Drain()); Assert.Equal(2, one.Report.Sequence); Assert.Equal(0, m.Count);
        var counts = m.Counters(); Assert.Equal(5, counts.Generated); Assert.Equal(1, counts.Coalesced); Assert.Equal(2, counts.Rejected); Assert.Equal(1, counts.CapacityRejected);
    }
    [Fact] public void StoreRejectsDuplicatesBackwardTimeAndMalformedReports()
    {
        var store = new TrackStore(new(), new()); Assert.True(store.Accept(Received(Report()), Epoch));
        Assert.False(store.Accept(Received(Report()), Epoch)); Assert.False(store.Accept(Received(Report(2, -1)), Epoch));
        Assert.False(store.Accept(Received(Report(2, 2) with { Course = 360 }), Epoch.AddSeconds(2)));
        Assert.False(store.Accept(Received(Report(2, 100)), Epoch)); Assert.Equal(1, store.Count); Assert.Equal(4, store.Rejected);
    }
    [Fact] public void HistoryIsBoundedByTimeAndCountAndKeepsGaps()
    {
        var store = new TrackStore(new() { MaximumPoints = 3, RetentionSeconds = 10, SampleSeconds = 2 }, new());
        for (var i = 0; i < 8; i++) store.Accept(Received(Report(i + 1, i * 2)), Epoch.AddSeconds(i * 2));
        var t = Assert.Single(store.Snapshot(Epoch.AddSeconds(14))); Assert.Equal(3, t.Trace.Length); Assert.Equal(Epoch.AddSeconds(10), t.Trace[0].Time);
        store.Accept(Received(Report(11, 16)), Epoch.AddSeconds(16)); Assert.True(store.Snapshot(Epoch.AddSeconds(16))[0].Trace[^1].BreakBefore);
        store.Age(Epoch.AddSeconds(25)); Assert.Single(store.Snapshot(Epoch.AddSeconds(25))[0].Trace);
        store.Age(Epoch.AddSeconds(50)); Assert.True(store.Snapshot(Epoch.AddSeconds(50))[0].Stale); Assert.Equal(0, store.HistoryPointCount);
        store.Age(Epoch.AddSeconds(136)); Assert.Equal(0, store.Count);
    }
    [Fact] public void TimeGapAndDatelineStartNewSegments()
    {
        var store = new TrackStore(new(), new()); store.Accept(Received(Report()), Epoch);
        store.Accept(Received(Report(2, 20)), Epoch.AddSeconds(20)); Assert.True(store.Snapshot(Epoch.AddSeconds(20))[0].Trace[^1].BreakBefore);
        store.Accept(Received(Report(3, 22) with { Longitude = -179 }), Epoch.AddSeconds(22));
        store.Accept(Received(Report(4, 24) with { Longitude = 179 }), Epoch.AddSeconds(24)); Assert.True(store.Snapshot(Epoch.AddSeconds(24))[0].Trace[^1].BreakBefore);
    }
    [Fact] public void FiltersSeparateAdmissionFromDisplayAndValidateBounds()
    {
        var store = new TrackStore(new(), new()); store.Accept(Received(Report()), Epoch); store.Accept(Received(Report(kind: TrackKind.Aircraft)), Epoch);
        var filter = new TrackFilter { Ships = false, MinimumAltitude = 20000, MaximumSpeed = 20, Bounds = new(18, 37, 20, 39) };
        Assert.Single(store.Snapshot(Epoch), t => filter.Matches(t.Report)); Assert.Equal(2, store.Count);
        store.SetInputFilter(filter); Assert.Equal(1, store.Count); Assert.False(store.Accept(Received(Report(2, 1)), Epoch.AddSeconds(1)));
        Assert.True(new GeoBounds(170, -10, -170, 10).Contains(179, 0)); Assert.False(new GeoBounds(170, -10, -170, 10).Contains(0, 0));
        Assert.Throws<ArgumentException>(() => new TrackFilter { MinimumSpeed = 30, MaximumSpeed = 20 }.Validate());
        Assert.Throws<ArgumentException>(() => new TrackFilter { Bounds = new(0, 50, 20, 10) }.Validate());
    }
    [Fact] public void IndexFindsCrossingTraceWithEndpointsOutsideViewport()
    {
        var factory = new GeometryFactory(); var line = new GeometryFeature(factory.CreateLineString([new(-100, 0), new(100, 0)]));
        var outside = new PointFeature(100, 0); var index = new FeatureIndex([line, outside]);
        Assert.Same(line, Assert.Single(index.Query(new MRect(-10, -10, 10, 10)))); Assert.Empty(index.Query(null));
    }
    [Fact] public void SnapshotDoesNotMutatePublishedFeaturesAndRetainsCrossingTrace()
    {
        var points = new[] { new TracePoint(Epoch, -100, 0, false), new TracePoint(Epoch.AddSeconds(2), 100, 0, false) };
        var track = new TrackView(Report(), 0, false, points); var source = new TrafficSnapshot(1, Epoch, [track], new(1, 1, 0, 0, 0, 0, 1, 0, 2));
        var builder = new SnapshotBuilder(); var first = builder.Build(source, new(), 1, true);
        Assert.Single(first.Traces.Query(new MRect(-10, -10, 10, 10)));
        var initialExtent = first.Symbols.Features[0].Extent!.MinX;
        var second = builder.Build(source with { Tracks = [track with { Report = Report(2, 2) with { Longitude = 20 } }] }, new(), 1, true);
        Assert.Equal(initialExtent, first.Symbols.Features[0].Extent!.MinX); Assert.NotEqual(initialExtent, second.Symbols.Features[0].Extent!.MinX);
        var hidden = builder.Build(source, new() { Ships = false }, 1, true); Assert.Empty(hidden.Symbols.Features); Assert.Empty(hidden.Traces.Features);
        var restored = builder.Build(source, new(), 1, true); Assert.Single(restored.Traces.Features);
        var off = builder.Build(source, new(), 1, false); Assert.Empty(off.Traces.Features);
        var on = builder.Build(source, new(), 1, true); Assert.Single(on.Traces.Features);
    }
    [Fact] public void TraceGapsAreNotJoinedBySimplification()
    {
        var track = new TrackView(Report(), 0, false, [new(Epoch, 0, 0, false), new(Epoch.AddSeconds(2), 1, 0, false), new(Epoch.AddSeconds(4), 100, 0, true), new(Epoch.AddSeconds(6), 101, 0, false)]);
        var s = new TrafficSnapshot(1, Epoch, [track], new(1, 1, 0, 0, 0, 0, 1, 0, 4));
        var r = new SnapshotBuilder().Build(s, new(), 100, true); var f = Assert.IsType<GeometryFeature>(Assert.Single(r.Traces.Features));
        Assert.Equal(2, f.Geometry!.NumGeometries);
    }
    [Fact] public void MetricWindowRemainsBounded()
    {
        var m = new MetricWindow(3); foreach (var i in Enumerable.Range(1, 10)) m.Add(i);
        var s = m.Summary(); Assert.Equal(3, s.WindowSamples); Assert.Equal(10, s.TotalSamples); Assert.Equal(9, s.Mean); Assert.Equal(10, s.P99); Assert.Equal(10, s.LifetimeP95UpperBound);
        m.Add(70000); Assert.Null(m.Summary().LifetimeP99UpperBound);
    }
    [Fact] public async Task EngineStopsCleanlyAfterDeliveringSnapshots()
    {
        var ready = new TaskCompletionSource<TrafficSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var engine = new TrafficEngine(new() { Ships = 5, Aircraft = 5, ShipIntervalSeconds = .1, AircraftIntervalSeconds = .1 }, new());
        engine.SnapshotReady += s => ready.TrySetResult(s); engine.Pause(false);
        var snapshot = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(10, snapshot.Counts.Tracked); Assert.Null(engine.Failure);
    }
    [Theory] [InlineData(1000)] [InlineData(5000)] [InlineData(10000)] [InlineData(12000)]
    public void LargePopulationRemainsIntactWithoutClustering(int population)
    {
        var o = new SimulationOptions { Ships = population / 2, Aircraft = population - population / 2, Dense = true };
        var feed = new SimulatedFeed(o, new(1, Epoch)); var store = new TrackStore(new(), o);
        for (var n = 0; n < 3; n++) for (var i = 0; i < population; i++) store.Accept(Received(feed.ReportAt(i, n * 5, n + 1)), Epoch.AddSeconds(n * 5));
        var source = new TrafficSnapshot(1, Epoch.AddSeconds(10), store.Snapshot(Epoch.AddSeconds(10)), new(0, 0, 0, 0, 0, 0, store.Count, 0, store.HistoryPointCount));
        var prepared = new SnapshotBuilder().Build(source, new(), 100, true);
        Assert.Equal(population, prepared.Symbols.Features.Length); Assert.Equal(population, prepared.Traces.Features.Length); Assert.Equal(population * 3, store.HistoryPointCount);
    }
}
