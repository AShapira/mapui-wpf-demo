using System.Diagnostics;
using System.IO;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Styles;
using Mapsui.Widgets;
using TrafficDemo.Core;

namespace TrafficDemo.Wpf;

/// <summary>Delegates all drawing/picking to Mapsui and measures completed on-screen render calls.</summary>
public sealed class MeasuredRenderer(TrafficMetrics metrics, Func<RenderSnapshot?> snapshot, Func<DateTimeOffset> simulationNow) : IMapRenderer
{
    private readonly IMapRenderer _inner = new Mapsui.Rendering.Skia.MapRenderer();
    private readonly Dictionary<string, long> _renderedSequence = new();
    private RenderSnapshot? _last;
    public int RenderedSymbols { get; private set; }
    public int RenderedTraces { get; private set; }
    public long Frames { get; private set; }
    public void Reset() { _renderedSequence.Clear(); _last = null; RenderedSymbols = 0; RenderedTraces = 0; }
    public void Render(object target, Viewport viewport, IEnumerable<ILayer> layers, IEnumerable<IWidget> widgets,
        RenderService service, Color? background = null, MRect? dirtyRegion = null, CoordinateSpace coordinateSpace = CoordinateSpace.World)
    {
        var current = snapshot(); var start = Stopwatch.GetTimestamp();
        _inner.Render(target, viewport, layers, widgets, service, background, dirtyRegion, coordinateSpace);
        var completed = Stopwatch.GetTimestamp(); metrics.RenderMs.Add(Stopwatch.GetElapsedTime(start, completed).TotalMilliseconds); Frames++;
        if (current == null) return;
        var extent = viewport.ToExtent();
        var features = current.Symbols.Query(extent, 24 * viewport.Resolution).ToArray();
        RenderedSymbols = features.Length; RenderedTraces = current.Traces.Query(extent).Count();
        if (ReferenceEquals(current, _last)) return;
        _last = current;
        var visible = current.Visible.ToDictionary(t => t.Key);
        var now = simulationNow();
        foreach (var feature in features)
        {
            var key = (string)feature["key"]!; var track = visible[key];
            metrics.ReportAgeSeconds.Add(Math.Max(0, (now - track.Report.Timestamp).TotalSeconds));
            if (!_renderedSequence.TryGetValue(key, out var sequence) || sequence != track.Report.Sequence)
            {
                metrics.IngestionToRenderMs.Add(Stopwatch.GetElapsedTime(track.ReceivedAt, completed).TotalMilliseconds);
                _renderedSequence[key] = track.Report.Sequence;
            }
        }
        foreach (var key in _renderedSequence.Keys.Where(k => !visible.ContainsKey(k)).ToArray()) _renderedSequence.Remove(key);
    }
    public void UpdateDrawables(Viewport viewport, ILayer layer, RenderService service) => _inner.UpdateDrawables(viewport, layer, service);
    public IDrawable? CreateDrawableForFeature(Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService service) => _inner.CreateDrawableForFeature(viewport, layer, feature, style, service);
    public MemoryStream RenderToBitmapStream(Viewport viewport, IEnumerable<ILayer> layers, RenderService service, Color? background = null, float pixelDensity = 1,
        IEnumerable<IWidget>? widgets = null, RenderFormat renderFormat = RenderFormat.Png, int quality = 100) => _inner.RenderToBitmapStream(viewport, layers, service, background, pixelDensity, widgets, renderFormat, quality);
    public bool TryGetWidgetRenderer(Type type, out IWidgetRenderer? renderer) => _inner.TryGetWidgetRenderer(type, out renderer);
    public bool TryGetStyleRenderer(Type type, out IStyleRenderer? renderer) => _inner.TryGetStyleRenderer(type, out renderer);
    public MapInfo GetMapInfo(ScreenPosition position, Viewport viewport, IEnumerable<ILayer> layers, RenderService service, int margin = 0) => _inner.GetMapInfo(position, viewport, layers, service, margin);
}
