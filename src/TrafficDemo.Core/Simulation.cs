using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TrafficDemo.Core;

public interface ITrackFeed
{
    IAsyncEnumerable<TrackReport> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Simulation time freezes on pause; elapsed time uses a monotonic clock.</summary>
public sealed class SimulationClock(double speed = 1, DateTimeOffset? epoch = null)
{
    private readonly object _gate = new();
    private long _last = Stopwatch.GetTimestamp();
    private double _seconds;
    private bool _paused = true;
    public DateTimeOffset Epoch { get; } = epoch ?? DateTimeOffset.UtcNow;
    public bool Paused { get { lock (_gate) return _paused; } }
    public double Seconds { get { lock (_gate) { Advance(); return _seconds; } } }
    public DateTimeOffset Now => Epoch.AddSeconds(Seconds);
    private void Advance()
    {
        var now = Stopwatch.GetTimestamp();
        if (!_paused) _seconds += Stopwatch.GetElapsedTime(_last, now).TotalSeconds * speed;
        _last = now;
    }
    public void SetPaused(bool paused) { lock (_gate) { Advance(); _paused = paused; } }
}

public sealed class SimulatedFeed : ITrackFeed
{
    private sealed record Vessel(TrackKind Kind, string Id, string Name, Route Route, double OffsetKm, double Speed, double Altitude, double Interval, double Phase);
    private readonly Vessel[] _objects;
    private readonly SimulationClock _clock;
    public SimulatedFeed(SimulationOptions options, SimulationClock clock)
    {
        options.Validate(); _clock = clock;
        var random = new Random(options.Seed);
        _objects = new Vessel[options.Population];
        for (var i = 0; i < _objects.Length; i++)
        {
            var ship = i < options.Ships;
            var index = ship ? i : i - options.Ships;
            var routes = ship ? (options.Dense ? Routes.DenseSea : Routes.Sea) : (options.Dense ? Routes.DenseAir : Routes.Air);
            var route = routes[random.Next(routes.Length)];
            var speed = ship ? 8 + random.NextDouble() * 18 : 240 + random.NextDouble() * 240;
            var interval = ship ? options.ShipIntervalSeconds : options.AircraftIntervalSeconds;
            _objects[i] = new(ship ? TrackKind.Ship : TrackKind.Aircraft,
                ship ? (200000000L + index).ToString() : (0xA00000L + index).ToString("X6"),
                $"SIM-{(ship ? "SHIP" : "AIR")}-{index + 1:00000}", route, random.NextDouble() * route.LengthKm,
                speed, ship ? 0 : 12000 + random.Next(27) * 1000, interval, random.NextDouble() * interval);
        }
    }
    // Public deterministic evaluator is also the benchmark/test oracle; it does not alter feed state.
    public TrackReport ReportAt(int index, double seconds, long sequence)
    {
        var v = _objects[index];
        var p = v.Route.Position(v.OffsetKm + seconds * v.Speed * 1.852 / 3600);
        return new(v.Kind, v.Id, v.Name, _clock.Epoch.AddSeconds(seconds), sequence, p.Lon, p.Lat, v.Speed, p.Course,
            v.Kind == TrackKind.Aircraft ? v.Altitude : null);
    }
    public async IAsyncEnumerable<TrackReport> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = _objects.Select(v => v.Phase).ToArray();
        var sequences = new long[_objects.Length];
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_clock.Paused) continue;
            var time = _clock.Seconds;
            for (var i = 0; i < next.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (time < next[i]) continue;
                // Emit the current observation, never an unbounded catch-up burst after suspension.
                next[i] = time + _objects[i].Interval;
                yield return ReportAt(i, time, ++sequences[i]);
            }
        }
    }
}

public sealed class Route(params (double Lon, double Lat)[] points)
{
    private readonly double[] _lengths = Enumerable.Range(0, points.Length - 1).Select(i => Distance(points[i], points[i + 1])).ToArray();
    public double LengthKm => _lengths.Sum();
    public (double Lon, double Lat, double Course) Position(double distanceKm)
    {
        // Traverse back and forth along the authored route: no teleport at its endpoints.
        var length = LengthKm; var distance = distanceKm % (2 * length); var reverse = distance > length;
        if (reverse) distance = 2 * length - distance;
        var segment = 0;
        while (segment < _lengths.Length - 1 && distance > _lengths[segment]) distance -= _lengths[segment++];
        var a = points[segment]; var b = points[segment + 1]; var t = distance / _lengths[segment];
        var course = Math.Atan2((b.Lon - a.Lon) * Math.Cos((a.Lat + b.Lat) * Math.PI / 360), b.Lat - a.Lat) * 180 / Math.PI;
        return (a.Lon + (b.Lon - a.Lon) * t, a.Lat + (b.Lat - a.Lat) * t, (course + (reverse ? 180 : 0) + 360) % 360);
    }
    private static double Distance((double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        var x = (b.Lon - a.Lon) * Math.Cos((a.Lat + b.Lat) * Math.PI / 360); var y = b.Lat - a.Lat;
        return Math.Sqrt(x * x + y * y) * 111.195;
    }
}
public static class Routes
{
    // Offshore demonstration waypoints, not navigational charts or certified routing.
    public static readonly Route[] Sea = [new((13.3,44.3),(15.3,42.5),(18.4,40.5),(19.0,38.2),(20.4,36.2)),
        new((23.7,37.5),(24.0,36.9),(24.5,36.2),(25.5,35.6)), new((19.5,39.4),(19.0,38.3),(19.8,37.0))];
    public static readonly Route[] Air = [new((16.0,45.8),(18.4,43.9),(21.0,41.0),(23.95,37.94)),
        new((12.3,45.5),(16.5,41.2),(20.8,38.0),(24.0,35.4)),new((20.3,44.8),(23.0,40.5),(25.2,37.5))];
    public static readonly Route[] DenseSea = [new((18.8,38.0),(19.3,38.4),(19.8,38.0),(19.3,37.6),(18.8,38.0))];
    public static readonly Route[] DenseAir = [new((18.5,37.5),(20.0,38.5),(18.6,38.4),(20.0,37.5))];
}
