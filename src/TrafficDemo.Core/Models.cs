using System.Diagnostics;

namespace TrafficDemo.Core;

public enum TrackKind { Ship, Aircraft }
public sealed record TrackReport(TrackKind Kind, string Identity, string Name, DateTimeOffset Timestamp,
    long Sequence, double Longitude, double Latitude, double SpeedKnots, double Course, double? AltitudeFeet = null)
{
    public string Key => $"{Kind}:{Identity}";
    public bool IsValid => Enum.IsDefined(Kind) && !string.IsNullOrWhiteSpace(Identity) && Identity.Length <= 64 &&
        Name is { Length: <= 128 } && Sequence >= 0 && double.IsFinite(Longitude) && Longitude is >= -180 and <= 180 &&
        double.IsFinite(Latitude) && Latitude is >= -85 and <= 85 && double.IsFinite(SpeedKnots) && SpeedKnots is >= 0 and <= 2000 &&
        double.IsFinite(Course) && Course is >= 0 and < 360 &&
        (AltitudeFeet is null || double.IsFinite(AltitudeFeet.Value) && AltitudeFeet is >= -2000 and <= 100000);
}
public sealed record ReceivedReport(TrackReport Report, long ReceivedAt)
{
    public static ReceivedReport Now(TrackReport report) => new(report, Stopwatch.GetTimestamp());
}
public sealed record GeoBounds(double West, double South, double East, double North)
{
    public bool Contains(double lon, double lat) => lat >= South && lat <= North &&
        (West <= East ? lon >= West && lon <= East : lon >= West || lon <= East);
    public bool IsValid => new[] { West, South, East, North }.All(double.IsFinite) && West >= -180 && West <= 180 &&
        East >= -180 && East <= 180 && South >= -85 && North <= 85 && South <= North;
}
public sealed record TrackFilter
{
    public bool Ships { get; init; } = true;
    public bool Aircraft { get; init; } = true;
    public string Search { get; init; } = "";
    public double? MinimumSpeed { get; init; }
    public double? MaximumSpeed { get; init; }
    public double? MinimumAltitude { get; init; }
    public double? MaximumAltitude { get; init; }
    public GeoBounds? Bounds { get; init; }
    public void Validate()
    {
        foreach (var v in new[] { MinimumSpeed, MaximumSpeed, MinimumAltitude, MaximumAltitude })
            if (v.HasValue && !double.IsFinite(v.Value)) throw new ArgumentException("Filter limits must be finite.");
        if (MinimumSpeed > MaximumSpeed || MinimumAltitude > MaximumAltitude || Bounds is { IsValid: false })
            throw new ArgumentException("Filter limits/bounds are invalid.");
    }
    public bool Matches(TrackReport r) => (r.Kind == TrackKind.Ship ? Ships : Aircraft) &&
        (string.IsNullOrWhiteSpace(Search) || r.Identity.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) || r.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        (!MinimumSpeed.HasValue || r.SpeedKnots >= MinimumSpeed) && (!MaximumSpeed.HasValue || r.SpeedKnots <= MaximumSpeed) &&
        (r.Kind != TrackKind.Aircraft || ((!MinimumAltitude.HasValue || r.AltitudeFeet >= MinimumAltitude) && (!MaximumAltitude.HasValue || r.AltitudeFeet <= MaximumAltitude))) &&
        (Bounds == null || Bounds.Contains(r.Longitude, r.Latitude));
}
public sealed record SimulationOptions
{
    public int Ships { get; init; } = 500;
    public int Aircraft { get; init; } = 500;
    public int Seed { get; init; } = 42;
    public double ShipIntervalSeconds { get; init; } = 5;
    public double AircraftIntervalSeconds { get; init; } = 2;
    public double SpeedMultiplier { get; init; } = 1;
    public bool Dense { get; init; }
    public int Population => checked(Ships + Aircraft);
    public string Warning => Population > 10000 ? $"Warning: {Population:N0} objects exceeds the 10,000-object validation target; latency and memory may increase." : "";
    public void Validate()
    {
        if (Ships < 0 || Aircraft < 0 || (long)Ships + Aircraft > int.MaxValue) throw new ArgumentException("Counts must be nonnegative and fit in a 32-bit total.");
        if (!double.IsFinite(ShipIntervalSeconds) || !double.IsFinite(AircraftIntervalSeconds) || ShipIntervalSeconds < .1 || AircraftIntervalSeconds < .1)
            throw new ArgumentException("Report intervals must be finite and at least 0.1 seconds.");
        if (!double.IsFinite(SpeedMultiplier) || SpeedMultiplier <= 0 || SpeedMultiplier > 1000) throw new ArgumentException("Simulation speed must be greater than zero and at most 1000.");
    }
}
public sealed record HistoryOptions
{
    public int MaximumPoints { get; init; } = 150;
    public double RetentionSeconds { get; init; } = 300;
    public double SampleSeconds { get; init; } = 2;
    public double StaleSeconds { get; init; } = 30;
    public double ExpireSeconds { get; init; } = 120;
    public void Validate()
    {
        if (MaximumPoints < 2 || MaximumPoints > 100000 || new[] { RetentionSeconds, SampleSeconds, StaleSeconds, ExpireSeconds }.Any(x => !double.IsFinite(x) || x <= 0) || ExpireSeconds <= StaleSeconds)
            throw new ArgumentException("Invalid history limits; expiry must exceed stale time.");
    }
}
public sealed record TracePoint(DateTimeOffset Time, double X, double Y, bool BreakBefore);
public sealed record TrackView(TrackReport Report, long ReceivedAt, bool Stale, TracePoint[] Trace)
{
    public string Key => Report.Key;
    public string Identity => Report.Identity;
    public string Name => Report.Name;
    public TrackKind Kind => Report.Kind;
    public double SpeedKnots => Report.SpeedKnots;
    public double? AltitudeFeet => Report.AltitudeFeet;
}
public sealed record PipelineCounts(long Generated, long Accepted, long Rejected, long Coalesced, long CapacityRejected, int Pending, int Tracked, int Filtered, int HistoryPoints);
public sealed record TrafficSnapshot(long Version, DateTimeOffset SimulationTime, TrackView[] Tracks, PipelineCounts Counts);

/// <summary>Fixed-capacity chronological history; only the tracking owner mutates it.</summary>
public sealed class TraceRing(int capacity)
{
    private readonly TracePoint[] _items = new TracePoint[capacity];
    private int _start;
    public int Count { get; private set; }
    public TracePoint? Last => Count == 0 ? null : _items[(_start + Count - 1) % _items.Length];
    public void Add(TracePoint point)
    {
        if (Count == _items.Length) { _start = (_start + 1) % _items.Length; Count--; }
        _items[(_start + Count++) % _items.Length] = point;
    }
    public void Prune(DateTimeOffset before)
    {
        while (Count > 0 && _items[_start].Time < before) { _start = (_start + 1) % _items.Length; Count--; }
    }
    public TracePoint[] Copy()
    {
        var result = new TracePoint[Count];
        for (var i = 0; i < Count; i++) result[i] = _items[(_start + i) % _items.Length];
        return result;
    }
}
