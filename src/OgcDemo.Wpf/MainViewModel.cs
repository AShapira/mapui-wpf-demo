using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OgcDemo.Core;

namespace OgcDemo.Wpf;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DemoOptions _options;
    private readonly ILogger<MainViewModel> _log;
    private readonly List<ConnectedLayer> _connections = [];
    private readonly MemoryLayer _highlight = FeatureLayers.Highlight();
    private readonly LatestRequest _fetch = new();
    private readonly LatestRequest _identify = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public Map Map { get; } = new() { CRS = "EPSG:3857", BackColor = new Mapsui.Styles.Color(239, 244, 248) };
    public ObservableCollection<LayerEntry> Layers { get; } = [];
    public IReadOnlyList<ServiceOptions> Services { get; }
    [ObservableProperty] private LayerEntry? selectedLayer;
    [ObservableProperty] private ServiceOptions? selectedService;
    [ObservableProperty] private string status = "Local demo ready. Pan, zoom, and click a feature.";
    [ObservableProperty] private string details = "Click a local vector feature. To identify a WMS image, select its layer and click the map.";
    [ObservableProperty] private bool busy;

    public MainViewModel(DemoOptions options, ILogger<MainViewModel> log)
    {
        _options = options; _log = log;
        Services = options.Services.Where(s => s.Enabled).ToArray(); SelectedService = Services.FirstOrDefault();
        Map.Navigator.RotationLock = true; // GetFeatureInfo is calculated for an unrotated viewport.
        Map.Layers.Add(_highlight);
        Add(FeatureLayers.Graphics());
        Add(FeatureLayers.FromGeoJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "sites.geojson")), "Sample GeoJSON"));
        Map.Navigator.ZoomToBox(Coordinates.ProjectExtent(options.InitialExtent));
        Map.Navigator.ViewportChanged += ViewportChanged;
        Map.DataChanged += (_, e) =>
        {
            if (e.Error != null) Application.Current.Dispatcher.InvokeAsync(() => Report(e.Error, "Map rendering request failed"));
        };
        _log.LogInformation("Demo initialized with bundled synthetic features.");
    }
    private void Add(ILayer layer)
    {
        Map.Layers.Add(layer); Map.Layers.MoveToTop(_highlight);
        Layers.Insert(0, new LayerEntry(layer, () => { Map.Refresh(); _ = RefreshWfsAsync(); }));
    }
    [RelayCommand] private void Home() => Map.Navigator.ZoomToBox(Coordinates.ProjectExtent(_options.InitialExtent));
    [RelayCommand] private void ZoomIn() => Map.Navigator.ZoomIn();
    [RelayCommand] private void ZoomOut() => Map.Navigator.ZoomOut();
    [RelayCommand] private void ZoomLayer() { if (SelectedLayer?.Layer.Extent is { } e) Map.Navigator.ZoomToBox(e.Grow(Math.Max(100, e.Width * 0.05))); }
    [RelayCommand] private void AddGraphics() { Add(FeatureLayers.Graphics()); Status = "Added a synthetic graphics overlay."; }
    [RelayCommand] private void ClearSelection() { _identify.Dispose(); FeatureLayers.Select(_highlight, null); Details = "Selection cleared."; Map.RefreshGraphics(); }
    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);
    private void Move(int delta)
    {
        if (SelectedLayer is not { } selected) return;
        var i = Layers.IndexOf(selected); var j = i + delta; if (j < 0 || j >= Layers.Count) return;
        Layers.Move(i, j); Map.Layers.Move(Layers.Count - 1 - j, selected.Layer); Map.Layers.MoveToTop(_highlight); Map.RefreshGraphics();
    }
    [RelayCommand] private void RemoveLayer()
    {
        if (SelectedLayer is not { } selected) return;
        _fetch.Dispose(); _identify.Dispose(); ClearSelection();
        Map.Layers.Remove(selected.Layer); Layers.Remove(selected);
        var connection = _connections.FirstOrDefault(c => c.Layer == selected.Layer);
        if (connection != null) { _connections.Remove(connection); connection.Dispose(); } else selected.Layer.Dispose();
        SelectedLayer = null; Map.Refresh(); Status = "Layer removed.";
    }
    [RelayCommand] private async Task OpenGeoJsonAsync()
    {
        var dialog = new OpenFileDialog { Filter = "GeoJSON (*.geojson;*.json)|*.geojson;*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 8 * 1024 * 1024) throw new DemoException("GeoJSON exceeds the 8 MiB demo limit.");
            var text = await File.ReadAllTextAsync(dialog.FileName, _lifetime.Token);
            var layer = await Task.Run(() => FeatureLayers.FromGeoJson(text, Path.GetFileName(dialog.FileName)), _lifetime.Token);
            Add(layer); Status = "GeoJSON loaded.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Report(e, "GeoJSON import failed"); }
    }
    [RelayCommand] private async Task ConnectAsync()
    {
        if (SelectedService is not { } options) { Status = "Enable a service in appsettings.local.json, then restart."; return; }
        if (_connections.Any(c => c.Options == options)) { Status = "This service layer is already connected."; return; }
        OgcTransport? transport = null; Busy = true; Status = "Reading service capabilities…";
        try
        {
            options.Validate(); transport = new OgcTransport(options);
            var connection = await OgcLayers.ConnectAsync(options, transport, _lifetime.Token);
            if (_disposed) { connection.Dispose(); return; }
            transport = null; _connections.Add(connection); Add(connection.Layer);
            SelectedLayer = Layers.First(l => l.Layer == connection.Layer);
            Status = "Connected. Pan or use Refresh to load the current view.";
            _log.LogInformation("OGC layer connected.");
            await RefreshWfsAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Report(e, "Connection failed"); }
        finally { transport?.Dispose(); Busy = false; }
    }
    private void ViewportChanged(object? sender, ViewportChangedEventArgs e) => _ = RefreshWfsAsync();
    [RelayCommand] private async Task RefreshAsync() { Map.Refresh(); await RefreshWfsAsync(); }
    private async Task RefreshWfsAsync()
    {
        if (_disposed || !_connections.Any(c => c.Wfs != null) || Map.Navigator.Viewport.Width <= 0) return;
        var (generation, token) = _fetch.Begin();
        try
        {
            await Task.Delay(300, token); // Debounce continuous panning.
            var extent = Map.Navigator.Viewport.ToExtent();
            foreach (var c in _connections.Where(c => c.Wfs != null && c.Layer.Enabled).ToArray())
            {
                var features = await c.Wfs!.FetchAsync(extent, token);
                if (!_fetch.IsCurrent(generation) || _disposed) return;
                var layer = (MemoryLayer)c.Layer; layer.Features = features; layer.DataHasChanged();
                FeatureLayers.Select(_highlight, null);
                Status = features.Count >= c.Options.MaxFeatures ? $"Loaded {features.Count} features; limit reached. Zoom in for more detail." : $"Loaded {features.Count} WFS features in the current view.";
            }
            Map.RefreshGraphics();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (_fetch.IsCurrent(generation)) Report(e, "WFS refresh failed"); }
    }
    public async Task SelectAsync(IFeature? feature, double screenX, double screenY)
    {
        var (generation, token) = _identify.Begin();
        FeatureLayers.Select(_highlight, feature); Map.RefreshGraphics();
        if (feature != null) { Details = string.Join(Environment.NewLine, feature.Fields.Select(k => $"{k}: {feature[k]}")); return; }
        var connection = _connections.FirstOrDefault(c => c.Layer == SelectedLayer?.Layer && c.Wms != null && c.Layer.Enabled);
        if (connection == null) { Details = "No vector feature here. Select a WMS layer to identify its image."; return; }
        try
        {
            var v = Map.Navigator.Viewport;
            var result = await OgcLayers.IdentifyAsync(connection, v.ToExtent(), (int)v.Width, (int)v.Height, (int)screenX, (int)screenY, token);
            if (_identify.IsCurrent(generation)) Details = result;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (_identify.IsCurrent(generation)) Report(e, "WMS identify failed"); }
    }
    private void Report(Exception exception, string operation)
    {
        if (_disposed) return;
        Status = operation + ": " + (exception is DemoException ? exception.Message : "Check configuration and the documented supported formats.");
        _log.LogWarning("{Operation}; exception type {ExceptionType}", operation, exception.GetType().Name);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _lifetime.Cancel(); _fetch.Dispose(); _identify.Dispose();
        Map.Navigator.ViewportChanged -= ViewportChanged;
        foreach (var c in _connections) c.Transport.Dispose();
        Map.Dispose(); _lifetime.Dispose();
    }
}
