namespace TrafficDemo.Core;

/// <summary>Bounded recent sample window. Percentiles describe this window, not all past samples.</summary>
public sealed class MetricWindow(int capacity = 20000, double bucketWidth = 1)
{
    private readonly object _gate = new();
    private readonly double[] _values = new double[capacity];
    private int _next, _count;
    private readonly long[] _histogram = new long[60002];
    public long TotalSamples { get; private set; }
    public void Add(double value)
    {
        if (!double.IsFinite(value)) return;
        lock (_gate) { _values[_next] = value; _next = (_next + 1) % capacity; _count = Math.Min(_count + 1, capacity); TotalSamples++; _histogram[Math.Clamp((int)Math.Min(60001, Math.Ceiling(Math.Max(0, value) / bucketWidth)), 0, 60001)]++; }
    }
    public MetricSummary Summary()
    {
        lock (_gate)
        {
            if (_count == 0) return new(0, TotalSamples, null, null, null, null, null, null);
            var sorted = _values.Take(_count).Order().ToArray();
            double P(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
            double? Lifetime(double percentile)
            {
                var rank = (long)Math.Ceiling(TotalSamples * percentile); long cumulative = 0;
                for (var i = 0; i < _histogram.Length; i++)
                {
                    cumulative += _histogram[i];
                    if (cumulative >= rank) return i == 60001 ? null : i * bucketWidth;
                }
                return null;
            }
            return new(_count, TotalSamples, sorted.Average(), P(.95), P(.99), sorted[^1], Lifetime(.95), Lifetime(.99));
        }
    }
}
public sealed record MetricSummary(int WindowSamples, long TotalSamples, double? Mean, double? P95, double? P99, double? Max, double? LifetimeP95UpperBound, double? LifetimeP99UpperBound);
public sealed class TrafficMetrics
{
    public MetricWindow IngestionToRenderMs { get; } = new();
    public MetricWindow ReportAgeSeconds { get; } = new(bucketWidth: .01);
    public MetricWindow RenderMs { get; } = new();
    public MetricWindow PreparationMs { get; } = new();
    public MetricWindow PublicationMs { get; } = new();
    public MetricWindow DispatcherMs { get; } = new();
    public long BasemapFailures;
    public long BasemapImageResponses;
    public long ReplacedSnapshots;
    public object Summary() => new { ingestionToRenderMs = IngestionToRenderMs.Summary(), reportAgeSeconds = ReportAgeSeconds.Summary(),
        renderMs = RenderMs.Summary(), preparationMs = PreparationMs.Summary(), publicationMs = PublicationMs.Summary(), dispatcherMs = DispatcherMs.Summary(),
        basemapImageResponses = Interlocked.Read(ref BasemapImageResponses), basemapFailures = Interlocked.Read(ref BasemapFailures), replacedSnapshots = Interlocked.Read(ref ReplacedSnapshots) };
}
