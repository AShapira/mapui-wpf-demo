using System.Diagnostics;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Simplify;
using OgcDemo.Core;

namespace TrafficDemo.Core;

/// <summary>Built once on a worker, then queried read-only by Mapsui and picking.</summary>
public sealed class FeatureIndex
{
    private readonly STRtree<IFeature> _tree = new();
    public IFeature[] Features { get; }
    public MRect? Extent { get; }
    public FeatureIndex(IEnumerable<IFeature> features)
    {
        Features = features.ToArray();
        var extent = new Envelope();
        foreach (var feature in Features)
        {
            if (feature.Extent is not { } box) continue;
            var envelope = new Envelope(box.MinX, box.MaxX, box.MinY, box.MaxY);
            _tree.Insert(envelope, feature); extent.ExpandToInclude(envelope);
        }
        _tree.Build();
        if (!extent.IsNull) Extent = new(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
    }
    public IEnumerable<IFeature> Query(MRect? box, double padding = 0) => box == null ? [] :
        _tree.Query(new Envelope(box.MinX - padding, box.MaxX + padding, box.MinY - padding, box.MaxY + padding));
    public static readonly FeatureIndex Empty = new([]);
}
public sealed class IndexedTrafficLayer(string name) : BaseLayer(name)
{
    private FeatureIndex _index = FeatureIndex.Empty;
    public FeatureIndex Index => Volatile.Read(ref _index);
    public override MRect? Extent => Index.Extent;
    public void Publish(FeatureIndex index) => Volatile.Write(ref _index, index);
    public override IEnumerable<IFeature> GetFeatures(MRect? rect, double resolution) => Index.Query(rect, 24 * resolution);
}
public sealed record RenderSnapshot(TrafficSnapshot Source, TrackView[] Visible, FeatureIndex Symbols, FeatureIndex Traces, double PreparationMs, long BuiltAt);

public sealed class TrafficStyles
{
    private readonly ImageStyle[,,] _symbols = new ImageStyle[2, 2, 72];
    private readonly VectorStyle[,] _traces = new VectorStyle[2, 2];
    public TrafficStyles()
    {
        var ship = new Mapsui.Styles.Image { Source = "svg-content://<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><path d='M12 1 L19 10 L18 22 L6 22 L5 10 Z' fill='#42d6dc' stroke='#102b36' stroke-width='1.5'/><path d='M9 10 H15 V17 H9Z' fill='#d9ffff'/></svg>" };
        var plane = new Mapsui.Styles.Image { Source = "svg-content://<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><path d='M11 1 H13 L14 9 L23 14 V17 L14 14 L14 20 L18 22 V23 L12 22 L6 23 V22 L10 20 L10 14 L1 17 V14 L10 9Z' fill='#ffce65' stroke='#553b10' stroke-width='0.8'/></svg>" };
        for (var kind = 0; kind < 2; kind++) for (var stale = 0; stale < 2; stale++)
        {
            for (var angle = 0; angle < 72; angle++) _symbols[kind, stale, angle] = new()
            { Image = kind == 0 ? ship : plane, SymbolRotation = angle * 5, SymbolScale = .75, RotateWithMap = true, Opacity = stale == 1 ? .35f : 1 };
            _traces[kind, stale] = new() { Line = new Pen(kind == 0 ? new Color(66, 214, 220, stale == 1 ? 55 : 130) : new Color(255, 206, 101, stale == 1 ? 55 : 130), 1.2), Fill = null, Outline = null };
        }
    }
    public IStyle Symbol(TrackView track) => _symbols[(int)track.Kind, track.Stale ? 1 : 0, (int)Math.Round(track.Report.Course / 5) % 72];
    public IStyle Trace(TrackView track) => _traces[(int)track.Kind, track.Stale ? 1 : 0];
}

/// <summary>Worker-owned feature cache. Only immutable published features cross into rendering.</summary>
public sealed class SnapshotBuilder
{
    private sealed record Cached(long Sequence, bool Stale, double Tolerance, DateTimeOffset? First, DateTimeOffset? Last, IFeature Symbol, IFeature? Trace);
    private readonly Dictionary<string, Cached> _cache = new();
    private readonly TrafficStyles _styles = new();
    private readonly GeometryFactory _geometry = new();
    public RenderSnapshot Build(TrafficSnapshot source, TrackFilter display, double resolution, bool traces)
    {
        var start = Stopwatch.GetTimestamp();
        var tolerance = Math.Pow(2, Math.Floor(Math.Log2(Math.Max(1, resolution)))) * .75;
        var visible = source.Tracks.Where(t => display.Matches(t.Report)).ToArray();
        var symbols = new List<IFeature>(visible.Length); var paths = new List<IFeature>(visible.Length);
        var active = new HashSet<string>();
        foreach (var t in visible)
        {
            active.Add(t.Key); _cache.TryGetValue(t.Key, out var cached);
            var first = t.Trace.FirstOrDefault()?.Time; var last = t.Trace.LastOrDefault()?.Time;
            var symbol = cached != null && cached.Sequence == t.Report.Sequence && cached.Stale == t.Stale ? cached.Symbol : CreateSymbol(t);
            var trace = traces ? cached != null && cached.Tolerance == tolerance && cached.First == first && cached.Last == last && cached.Stale == t.Stale
                ? cached.Trace : CreateTrace(t, tolerance) : null;
            _cache[t.Key] = new(t.Report.Sequence, t.Stale, traces ? tolerance : -1, first, last, symbol, trace);
            symbols.Add(symbol); if (trace != null) paths.Add(trace);
        }
        foreach (var key in _cache.Keys.Where(k => !active.Contains(k)).ToArray()) _cache.Remove(key);
        return new(source with { Counts = source.Counts with { Filtered = source.Tracks.Length - visible.Length } }, visible,
            new(symbols), new(paths), Stopwatch.GetElapsedTime(start).TotalMilliseconds, Stopwatch.GetTimestamp());
    }
    private IFeature CreateSymbol(TrackView t)
    {
        var p = OgcDemo.Core.Coordinates.Project(t.Report.Longitude, t.Report.Latitude);
        var feature = new PointFeature(p.X, p.Y) { ["key"] = t.Key, ["name"] = t.Name };
        feature.Styles.Add(_styles.Symbol(t)); return feature;
    }
    private IFeature? CreateTrace(TrackView t, double tolerance)
    {
        var segments = new List<LineString>(); var points = new List<Coordinate>();
        void Flush()
        {
            if (points.Count >= 2)
            {
                var line = _geometry.CreateLineString(points.ToArray());
                segments.Add((LineString)DouglasPeuckerSimplifier.Simplify(line, tolerance));
            }
            points.Clear();
        }
        foreach (var p in t.Trace)
        {
            if (p.BreakBefore) Flush();
            points.Add(new(p.X, p.Y));
        }
        Flush(); if (segments.Count == 0) return null;
        var feature = new GeometryFeature(_geometry.CreateMultiLineString(segments.ToArray())) { ["key"] = t.Key };
        feature.Styles.Add(_styles.Trace(t)); return feature;
    }
}
