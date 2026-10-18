using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using OgcDemo.Core;
using TrafficDemo.Core;

namespace TrafficDemo.Wpf;

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private readonly SemaphoreSlim _basemapGate = new(1, 1);
    private ConnectedLayer? _basemap;
    private RenderSnapshot? _pending;
    private int _publicationQueued;
    private int _generation;
    private double _resolution = 1000;
    private TrackFilter _display;
    private bool _drawTraces = true;
    private TrafficEngine? _engine;
    private readonly MemoryLayer _selection = new("Selected object") { Style = null };
    private string? _selectedKey;
    private TrackView? _lastSelected;
    private bool _updatingRows;
    private readonly Task _probe;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private long _lastCpuAt = Stopwatch.GetTimestamp();
    public ViewerOptions Options { get; }
    public TrafficMetrics Metrics { get; } = new();
    public Map Map { get; } = new() { CRS = "EPSG:3857", BackColor = new Color(19, 30, 43) };
    public IndexedTrafficLayer Symbols { get; } = new("Traffic symbols") { Style = null };
    public IndexedTrafficLayer Traces { get; } = new("Recent traces") { Style = null };
    public RenderSnapshot? Current { get; private set; }
    public TrafficEngine? Engine => _engine;
    public MeasuredRenderer Renderer { get; }
    public string[] BasemapLayers { get; } = ["omt:dark-matter-en", "omt:osm-bright-en", "omt:positron-en", "omt:toner-en", "omt:maptiler-basic-en", "omt:world-en"];
    public string[] Protocols { get; } = ["WMTS", "WMS", "None"];
    [ObservableProperty] private int _ships;
    [ObservableProperty] private int _aircraft;
    [ObservableProperty] private int _seed;
    [ObservableProperty] private double _shipInterval;
    [ObservableProperty] private double _aircraftInterval;
    [ObservableProperty] private double _simulationSpeed;
    [ObservableProperty] private bool _dense;
    [ObservableProperty] private bool _showTraces = true;
    [ObservableProperty] private bool _following;
    [ObservableProperty] private string _status = "Ready. Configure population, then Start.";
    [ObservableProperty] private string _basemapStatus = "Basemap not connected.";
    [ObservableProperty] private string _performance = "Waiting for simulation.";
    [ObservableProperty] private string _details = "Select a ship or aircraft.";
    [ObservableProperty] private string _basemapLayer;
    [ObservableProperty] private string _basemapProtocol;
    [ObservableProperty] private TrackView[] _rows = [];
    [ObservableProperty] private TrackView? _selectedTrack;
    [ObservableProperty] private FilterEditor _inputEditor;
    [ObservableProperty] private FilterEditor _displayEditor;
    public string PopulationWarning => (long)Ships + Aircraft > 10000 ? "Above the 10,000-object validation target. Increased latency/memory is possible." : "";
    public MainViewModel(ViewerOptions options, Dispatcher dispatcher)
    {
        Options = options; _dispatcher = dispatcher; _display = options.DisplayFilter;
        var s = options.Simulation; Ships = s.Ships; Aircraft = s.Aircraft; Seed = s.Seed; ShipInterval = s.ShipIntervalSeconds;
        AircraftInterval = s.AircraftIntervalSeconds; SimulationSpeed = s.SpeedMultiplier; Dense = s.Dense;
        _inputEditor = FilterEditor.From(options.InputFilter); _displayEditor = FilterEditor.From(options.DisplayFilter);
        _basemapLayer = options.Basemap.Layer; _basemapProtocol = options.Basemap.Enabled ? options.Basemap.Protocol : "None";
        Map.Layers.Add(Traces); Map.Layers.Add(Symbols); Map.Layers.Add(_selection);
        Map.Navigator.ViewportChanged += (_, _) => Volatile.Write(ref _resolution, Map.Navigator.Viewport.Resolution);
        Renderer = new(Metrics, () => Current, () => _engine?.Clock.Now ?? DateTimeOffset.UtcNow);
        Home(); _probe = Task.Run(ProbeDispatcher);
    }
    partial void OnShipsChanged(int value) => OnPropertyChanged(nameof(PopulationWarning));
    partial void OnAircraftChanged(int value) => OnPropertyChanged(nameof(PopulationWarning));
    partial void OnShowTracesChanged(bool value) => Volatile.Write(ref _drawTraces, value);
    partial void OnFollowingChanged(bool value) { if (value) UpdateSelection(); }
    partial void OnSelectedTrackChanged(TrackView? value)
    {
        if (_updatingRows || value == null) return;
        _selectedKey = value.Key; _lastSelected = value; UpdateSelection();
    }
    public void SelectKey(string key)
    {
        _selectedKey = key; _lastSelected = Current?.Source.Tracks.FirstOrDefault(t => t.Key == key) ?? _lastSelected;
        SelectedTrack = Rows.FirstOrDefault(t => t.Key == key); UpdateSelection();
    }
    public void SuspendFollowing() { if (Following) { Following = false; Status = "Following suspended by map interaction. Use Follow to resume."; } }
    public SimulationOptions ReadSimulation() => new() { Ships = Ships, Aircraft = Aircraft, Seed = Seed, ShipIntervalSeconds = ShipInterval,
        AircraftIntervalSeconds = AircraftInterval, SpeedMultiplier = SimulationSpeed, Dense = Dense };
    [RelayCommand] private async Task Start()
    {
        if (_engine == null) await ResetEngine(true);
        else { _engine.Pause(false); Status = "Running. " + Options.Simulation.Warning; }
    }
    [RelayCommand] private void Pause() { _engine?.Pause(true); Status = "Paused: simulation time and track ageing are frozen."; }
    [RelayCommand] private void Resume() { _engine?.Pause(false); Status = "Running. " + Options.Simulation.Warning; }
    [RelayCommand] private Task Reset() => ResetEngine(false);
    [RelayCommand] private Task ApplyConfiguration() => ResetEngine(true);
    private async Task ResetEngine(bool start)
    {
        await _resetGate.WaitAsync();
        try
        {
            var simulation = ReadSimulation(); simulation.Validate(); Options.History.Validate(); var input = InputEditor.Build();
            Options.Simulation = simulation; Options.InputFilter = input;
            Interlocked.Increment(ref _generation);
            var old = _engine; _engine = null; if (old != null) await old.DisposeAsync();
            Interlocked.Exchange(ref _pending, null); Current = null; Rows = []; _selectedKey = null; _lastSelected = null;
            Symbols.Publish(FeatureIndex.Empty); Traces.Publish(FeatureIndex.Empty); Renderer.Reset(); Following = false; UpdateSelection();
            var builder = new SnapshotBuilder(); var generation = _generation;
            // Potentially large simulator population is allocated on a worker, never on the UI thread.
            _engine = await Task.Run(() => new TrafficEngine(simulation, Options.History, input));
            _engine.SnapshotReady += source =>
            {
                var prepared = builder.Build(source, Volatile.Read(ref _display), Volatile.Read(ref _resolution), Volatile.Read(ref _drawTraces));
                if (generation != Volatile.Read(ref _generation) || _stop.IsCancellationRequested) return;
                Metrics.PreparationMs.Add(prepared.PreparationMs);
                if (Interlocked.Exchange(ref _pending, prepared) != null) Interlocked.Increment(ref Metrics.ReplacedSnapshots);
                QueuePublication();
            };
            _engine.Pause(!start); Home(); Map.RefreshGraphics();
            Status = (start ? "Running. " : "Reset and paused. ") + simulation.Warning;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { Status = e.Message; }
        finally { _resetGate.Release(); }
    }
    private void QueuePublication()
    {
        if (_stop.IsCancellationRequested || Interlocked.CompareExchange(ref _publicationQueued, 1, 0) != 0) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                var prepared = Interlocked.Exchange(ref _pending, null);
                if (prepared == null || _stop.IsCancellationRequested) return;
                var start = Stopwatch.GetTimestamp(); Current = prepared;
                Symbols.Publish(prepared.Symbols); Traces.Publish(prepared.Traces);
                _updatingRows = true;
                try { Rows = prepared.Visible; SelectedTrack = Rows.FirstOrDefault(t => t.Key == _selectedKey); }
                finally { _updatingRows = false; }
                UpdateSelection();
                // Overlay-only invalidation. No Map.Refresh(), which would refetch the basemap.
                Symbols.DataHasChanged(); Traces.DataHasChanged(); Map.RefreshGraphics();
                Metrics.PublicationMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                UpdatePerformance();
            }
            finally { Interlocked.Exchange(ref _publicationQueued, 0); if (Volatile.Read(ref _pending) != null) QueuePublication(); }
        }));
    }
    private void UpdateSelection()
    {
        var selected = Current?.Source.Tracks.FirstOrDefault(t => t.Key == _selectedKey);
        if (selected != null) _lastSelected = selected;
        if (_lastSelected == null) { Details = "Select a ship or aircraft."; _selection.Features = []; _selection.DataHasChanged(); return; }
        var t = _lastSelected; var shown = selected != null && Volatile.Read(ref _display).Matches(t.Report);
        if (!shown) Following = false;
        Details = $"{t.Name}\n{t.Kind} · {t.Identity}\n{t.Report.Latitude:F5}, {t.Report.Longitude:F5}\n{t.SpeedKnots:F1} kn · course {t.Report.Course:F0}°" +
            (t.AltitudeFeet.HasValue ? $" · {t.AltitudeFeet:N0} ft" : "") + $"\nReport {t.Report.Timestamp:HH:mm:ss} UTC · sequence {t.Report.Sequence}\n" +
            (selected == null ? "Not currently tracked (input-filtered, expired or reset)." : !shown ? "Hidden by display filter; following suspended." : t.Stale ? "Stale report" : "Current report");
        if (shown)
        {
            var p = Coordinates.Project(t.Report.Longitude, t.Report.Latitude);
            var feature = new PointFeature(p.X, p.Y);
            feature.Styles.Add(new SymbolStyle { SymbolScale = 1.15, Fill = null, Outline = new Pen(Color.White, 2) });
            feature.Styles.Add(new LabelStyle { Text = t.Name, ForeColor = Color.White, BackColor = new Brush(new Color(20, 35, 50, 220)), Offset = new Offset(0, 24) });
            _selection.Features = [feature];
            if (Following) Map.Navigator.CenterOn(new MPoint(p.X, p.Y), 0);
        }
        else _selection.Features = [];
        _selection.DataHasChanged();
    }
    [RelayCommand] private void Focus()
    {
        if (_lastSelected is not { } t || !Volatile.Read(ref _display).Matches(t.Report)) return;
        var p = Coordinates.Project(t.Report.Longitude, t.Report.Latitude); Map.Navigator.CenterOnAndZoomTo(new MPoint(p.X, p.Y), 200, 0);
    }
    [RelayCommand] private void Home()
    {
        var a = Coordinates.Project(Dense ? 18.3 : 11.5, Dense ? 37.2 : 34.5); var b = Coordinates.Project(Dense ? 20.3 : 26.5, Dense ? 38.8 : 46.5);
        Map.Navigator.ZoomToBox(new MRect(a.X, a.Y, b.X, b.Y), duration: 0);
    }
    [RelayCommand] private void Preset(string value)
    {
        if (!int.TryParse(value, out var count)) return; Ships = count / 2; Aircraft = count - Ships;
        Status = "Preset staged. Apply configuration to reset and run.";
    }
    [RelayCommand] private void ApplyInputFilter()
    {
        try { var filter = InputEditor.Build(); Options.InputFilter = filter; _engine?.SetInputFilter(filter); Status = "Input filter applied; excluded tracks are purged on the next processing cycle."; }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { Status = "Input filter: " + e.Message; }
    }
    [RelayCommand] private void ApplyDisplayFilter()
    {
        try { var filter = DisplayEditor.Build(); Options.DisplayFilter = filter; Volatile.Write(ref _display, filter); UpdateSelection(); Status = "Display filter applied; tracking and retained history continue."; }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { Status = "Display filter: " + e.Message; }
    }
    [RelayCommand] private void ClearDisplayFilter() { DisplayEditor = new(); ApplyDisplayFilter(); }
    [RelayCommand] private void ClearInputFilter() { InputEditor = new(); ApplyInputFilter(); }
    [RelayCommand] public async Task ConnectBasemap()
    {
        await _basemapGate.WaitAsync();
        try
        {
            if (_basemap != null) { Map.Layers.Remove(_basemap.Layer); _basemap.Dispose(); _basemap = null; }
            Options.Basemap.Layer = BasemapLayer; Options.Basemap.Protocol = BasemapProtocol; Options.Basemap.Enabled = BasemapProtocol != "None";
            if (BasemapProtocol == "None") { BasemapStatus = "Overlay-only mode."; Map.RefreshGraphics(); return; }
            var baseUrl = new Uri(Options.Basemap.Url.TrimEnd('/') + "/");
            if (baseUrl.UserInfo.Length != 0 || !(baseUrl.Scheme == "https" || baseUrl.Scheme == "http" && baseUrl.IsLoopback)) throw new ArgumentException("Basemap requires HTTPS or loopback HTTP, without URL credentials.");
            var o = new ServiceOptions { Name = "OSM basemap", Kind = BasemapProtocol, Version = BasemapProtocol == "WMTS" ? "1.0.0" : "1.3.0",
                Url = new Uri(baseUrl, BasemapProtocol == "WMTS" ? "gwc/service/wmts" : "wms").AbsoluteUri, Layer = BasemapLayer, MatrixSet = "EPSG:3857", Style = BasemapProtocol == "WMTS" ? "default" : "", TimeoutSeconds = 15 };
            BasemapStatus = "Connecting to local GeoServer…";
            var transport = new OgcTransport(o, new BasemapHandler(Metrics), allowLoopbackHttp: true);
            try { _basemap = await OgcLayers.ConnectAsync(o, transport, _stop.Token); }
            catch { transport.Dispose(); throw; }
            _basemap.Layer.DataChanged += (_, e) => { if (e?.Error != null) Interlocked.Increment(ref Metrics.BasemapFailures); };
            Map.Layers.Insert(0, _basemap.Layer); Map.Refresh(); BasemapStatus = $"{BasemapProtocol} · {BasemapLayer}";
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception e)
        {
            Interlocked.Increment(ref Metrics.BasemapFailures); BasemapStatus = $"Basemap unavailable: {(e is DemoException or ArgumentException ? e.Message : e.GetType().Name)} Simulation remains usable. Retry Connect.";
        }
        finally { _basemapGate.Release(); }
    }
    private async Task ProbeDispatcher()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                var start = Stopwatch.GetTimestamp();
                await _dispatcher.InvokeAsync(() =>
                {
                    Metrics.DispatcherMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    if (_engine?.Failure is { } failure) Status = $"Simulation stopped: {failure.GetType().Name}: {failure.Message}";
                }, DispatcherPriority.Background, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private void UpdatePerformance()
    {
        if (Current == null) return; var c = Current.Source.Counts; _process.Refresh();
        var now = Stopwatch.GetTimestamp(); var cpu = _process.TotalProcessorTime;
        var percent = (cpu - _lastCpu).TotalSeconds / Math.Max(.001, Stopwatch.GetElapsedTime(_lastCpuAt, now).TotalSeconds) / Environment.ProcessorCount * 100;
        _lastCpu = cpu; _lastCpuAt = now;
        var latency = Metrics.IngestionToRenderMs.Summary(); var render = Metrics.RenderMs.Summary();
        Performance = $"Generated {c.Generated:N0} · accepted {c.Accepted:N0}\nRejected {c.Rejected:N0} · coalesced {c.Coalesced:N0} · capacity rejected {c.CapacityRejected:N0}\n" +
            $"Tracked {c.Tracked:N0} · filtered {c.Filtered:N0} · eligible {Current.Visible.Length:N0}\nDrawn symbols {Renderer.RenderedSymbols:N0} · traces {Renderer.RenderedTraces:N0}\n" +
            $"History {c.HistoryPoints:N0} pts · mailbox {c.Pending:N0} · pending snapshot {(_pending == null ? 0 : 1)}\n" +
            $"Ingest→render p95 {latency.P95:F0} / p99 {latency.P99:F0} ms\nReport age p95 {Metrics.ReportAgeSeconds.Summary().P95:F1} s\n" +
            $"Render p95 {render.P95:F1} ms · UI delay p95 {Metrics.DispatcherMs.Summary().P95:F1} ms\n" +
            $"Prepare {Current.PreparationMs:F1} ms · publish p95 {Metrics.PublicationMs.Summary().P95:F1} ms\n" +
            $"CPU {percent:F1}% · private {_process.PrivateMemorySize64 / 1048576:N0} MiB · managed {GC.GetTotalMemory(false) / 1048576:N0} MiB\n" +
            $"Allocated {GC.GetTotalAllocatedBytes() / 1048576:N0} MiB · GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n" +
            $"Basemap images {Metrics.BasemapImageResponses} · failures {Metrics.BasemapFailures} · replaced snapshots {Metrics.ReplacedSnapshots}";
        if (_engine?.Failure is { } error) Status = $"Simulation stopped: {error.GetType().Name}: {error.Message}";
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); Interlocked.Increment(ref _generation);
        await _resetGate.WaitAsync();
        try { if (_engine != null) await _engine.DisposeAsync(); }
        finally { _resetGate.Release(); }
        await _probe; await _basemapGate.WaitAsync();
        try { if (_basemap != null) { Map.Layers.Remove(_basemap.Layer); _basemap.Dispose(); } }
        finally { _basemapGate.Release(); }
        Map.Dispose(); _process.Dispose(); _stop.Dispose();
    }
}
