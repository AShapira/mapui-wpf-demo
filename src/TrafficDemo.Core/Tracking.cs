using OgcDemo.Core;

namespace TrafficDemo.Core;

/// <summary>Bounded latest-value mailbox. Capacity is configured population, not a 10k cap.</summary>
public sealed class LatestReportMailbox(int capacity)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ReceivedReport> _pending = new();
    public long Generated { get; private set; }
    public long Coalesced { get; private set; }
    public long Rejected { get; private set; }
    public long CapacityRejected { get; private set; }
    public int Count { get { lock (_gate) return _pending.Count; } }
    public bool Offer(ReceivedReport received)
    {
        lock (_gate)
        {
            Generated++;
            var r = received.Report;
            if (!r.IsValid) { Rejected++; return false; }
            if (_pending.TryGetValue(r.Key, out var old))
            {
                if (r.Sequence <= old.Report.Sequence || r.Timestamp < old.Report.Timestamp) { Rejected++; return false; }
                Coalesced++;
            }
            else if (_pending.Count >= capacity) { CapacityRejected++; return false; }
            _pending[r.Key] = received; return true;
        }
    }
    public ReceivedReport[] Drain()
    {
        lock (_gate) { var items = _pending.Values.ToArray(); _pending.Clear(); return items; }
    }
    public (long Generated, long Coalesced, long Rejected, long CapacityRejected, int Pending) Counters()
    { lock (_gate) return (Generated, Coalesced, Rejected, CapacityRejected, _pending.Count); }
}

public sealed class TrackStore
{
    private sealed class Entry(ReceivedReport latest, int capacity)
    {
        public ReceivedReport Latest = latest;
        public TraceRing History = new(capacity);
        public bool PendingBreak;
    }
    private readonly Dictionary<string, Entry> _tracks = new();
    private readonly HistoryOptions _history;
    private readonly SimulationOptions _simulation;
    private TrackFilter _input = new();
    public long Accepted { get; private set; }
    public long Rejected { get; private set; }
    public int Count => _tracks.Count;
    public int HistoryPointCount => _tracks.Values.Sum(e => e.History.Count);
    public TrackStore(HistoryOptions history, SimulationOptions simulation)
    { history.Validate(); simulation.Validate(); _history = history; _simulation = simulation; }
    public void SetInputFilter(TrackFilter filter)
    {
        filter.Validate(); _input = filter;
        foreach (var key in _tracks.Where(p => !filter.Matches(p.Value.Latest.Report)).Select(p => p.Key).ToArray()) _tracks.Remove(key);
    }
    public bool Accept(ReceivedReport received, DateTimeOffset now)
    {
        var r = received.Report;
        if (!r.IsValid || !_input.Matches(r) || r.Timestamp > now.AddSeconds(1) || (now - r.Timestamp).TotalSeconds >= _history.ExpireSeconds)
        { Rejected++; return false; }
        var exists = _tracks.TryGetValue(r.Key, out var entry);
        if (exists && (r.Sequence <= entry!.Latest.Report.Sequence || r.Timestamp < entry.Latest.Report.Timestamp)) { Rejected++; return false; }
        if (!exists)
        {
            if (_tracks.Count >= _simulation.Population) { Rejected++; return false; }
            entry = new(received, _history.MaximumPoints); _tracks.Add(r.Key, entry);
        }
        else
        {
            var interval = r.Kind == TrackKind.Ship ? _simulation.ShipIntervalSeconds : _simulation.AircraftIntervalSeconds;
            entry!.PendingBreak |= (r.Timestamp - entry.Latest.Report.Timestamp).TotalSeconds > Math.Max(interval * 2.5, _history.SampleSeconds * 2.5) || r.Sequence > entry.Latest.Report.Sequence + 1;
            entry.Latest = received;
        }
        entry!.History.Prune(now.AddSeconds(-_history.RetentionSeconds));
        var last = entry.History.Last;
        if (last == null || (r.Timestamp - last.Time).TotalSeconds >= _history.SampleSeconds)
        {
            var p = Coordinates.Project(r.Longitude, r.Latitude);
            entry.History.Add(new(r.Timestamp, p.X, p.Y, entry.PendingBreak || last != null && Math.Abs(p.X - last.X) > 20037508));
            entry.PendingBreak = false;
        }
        Accepted++; return true;
    }
    public void Age(DateTimeOffset now)
    {
        foreach (var pair in _tracks.ToArray())
        {
            if ((now - pair.Value.Latest.Report.Timestamp).TotalSeconds >= _history.ExpireSeconds) _tracks.Remove(pair.Key);
            else pair.Value.History.Prune(now.AddSeconds(-_history.RetentionSeconds));
        }
    }
    public TrackView[] Snapshot(DateTimeOffset now) => _tracks.Values.Select(e => new TrackView(e.Latest.Report, e.Latest.ReceivedAt,
        (now - e.Latest.Report.Timestamp).TotalSeconds >= _history.StaleSeconds, e.History.Copy())).ToArray();
}

/// <summary>A single background consumer owns the store. The producer only writes the mailbox.</summary>
public sealed class TrafficEngine : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly LatestReportMailbox _mailbox;
    private readonly TrackStore _store;
    private readonly ITrackFeed _feed;
    private readonly Task _producer;
    private readonly Task _consumer;
    private TrackFilter _input;
    private TrackFilter _appliedInput;
    private long _version;
    private Exception? _failure;
    public SimulationClock Clock { get; }
    public Exception? Failure => Volatile.Read(ref _failure);
    public event Action<TrafficSnapshot>? SnapshotReady;
    public TrafficEngine(SimulationOptions simulation, HistoryOptions history, TrackFilter? input = null)
    {
        simulation.Validate(); history.Validate(); _input = input ?? new(); _input.Validate(); _appliedInput = _input;
        Clock = new(simulation.SpeedMultiplier); _mailbox = new(simulation.Population); _store = new(history, simulation); _store.SetInputFilter(_input);
        _feed = new SimulatedFeed(simulation, Clock);
        _producer = Task.Run(Produce); _consumer = Task.Run(Consume);
    }
    public void SetInputFilter(TrackFilter filter) { filter.Validate(); Volatile.Write(ref _input, filter); }
    public void Pause(bool paused) => Clock.SetPaused(paused);
    private async Task Produce()
    {
        try { await foreach (var report in _feed.ReadAsync(_stop.Token)) _mailbox.Offer(ReceivedReport.Now(report)); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception e) { Volatile.Write(ref _failure, e); _stop.Cancel(); }
    }
    private async Task Consume()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var lastSnapshot = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                var input = Volatile.Read(ref _input);
                if (input != _appliedInput) { _store.SetInputFilter(input); _appliedInput = input; }
                var now = Clock.Now;
                foreach (var item in _mailbox.Drain()) _store.Accept(item, now);
                _store.Age(now);
                if (System.Diagnostics.Stopwatch.GetElapsedTime(lastSnapshot).TotalSeconds < 1) continue;
                lastSnapshot = System.Diagnostics.Stopwatch.GetTimestamp();
                var counts = _mailbox.Counters();
                var snapshot = new TrafficSnapshot(++_version, now, _store.Snapshot(now),
                    new(counts.Generated, _store.Accepted, counts.Rejected + _store.Rejected, counts.Coalesced, counts.CapacityRejected, counts.Pending, _store.Count, 0, _store.HistoryPointCount));
                SnapshotReady?.Invoke(snapshot);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception e) { Volatile.Write(ref _failure, e); _stop.Cancel(); }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); await Task.WhenAll(_producer, _consumer).ConfigureAwait(false); SnapshotReady = null; _stop.Dispose();
    }
}
