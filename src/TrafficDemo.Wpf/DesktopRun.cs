using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mapsui;
using Mapsui.Extensions;
using TrafficDemo.Core;

namespace TrafficDemo.Wpf;
public sealed record RunOptions(string? Report, int Seconds, int? Ships, int? Aircraft, bool Dense, bool NoBasemap, bool NoTraces, bool Smoke, bool Exercise)
{
    public static RunOptions Parse(string[] args)
    {
        string? Value(string key) { var i = Array.IndexOf(args, key); return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + key + " value"); }
        int? Number(string key) => Value(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;
        var result = new RunOptions(Value("--report"), Number("--seconds") ?? 60, Number("--ships"), Number("--aircraft"), args.Contains("--dense"),
            args.Contains("--no-basemap"), args.Contains("--no-traces"), args.Contains("--smoke"), args.Contains("--exercise"));
        if (result.Seconds < 5 || result.Seconds > 86400) throw new ArgumentException("Benchmark duration must be 5–86400 seconds.");
        if (result.Report != null && File.Exists(result.Report)) throw new ArgumentException("Choose a new report path; refusing to reuse old evidence.");
        return result;
    }
    public void Apply(ViewerOptions options)
    {
        options.Simulation = options.Simulation with { Ships = Ships ?? options.Simulation.Ships, Aircraft = Aircraft ?? options.Simulation.Aircraft, Dense = Dense || options.Simulation.Dense };
        if (NoBasemap) options.Basemap.Enabled = false;
    }
}
public sealed record ProcessSample(double Seconds, long PrivateBytes, long WorkingSetBytes, long ManagedBytes, long AllocatedBytes, double CpuSeconds,
    int Gen0, int Gen1, int Gen2, int Tracked, int RetainedPoints, int Mailbox, int RenderedSymbols, int RenderedTraces);

public static class BenchmarkReport
{
    public static void Write(string path, MainWindow window, IReadOnlyList<ProcessSample> samples, double duration, string coverage, string? error = null, IReadOnlyList<string>? checks = null)
    {
        var vm = window.ViewModel; var current = vm.Current; var latency = vm.Metrics.IngestionToRenderMs.Summary(); var ui = vm.Metrics.DispatcherMs.Summary();
        var warm = samples.Where(s => s.Seconds >= 600).ToArray();
        double? memorySlope = null;
        if (warm.Length >= 3)
        {
            var x = warm.Average(s => s.Seconds); var y = warm.Average(s => (double)s.PrivateBytes);
            var denominator = warm.Sum(s => Math.Pow(s.Seconds - x, 2));
            memorySlope = denominator > 0 ? warm.Sum(s => (s.Seconds - x) * (s.PrivateBytes - y)) / denominator * 60 / 1048576 : 0;
        }
        bool? latencyPass = latency.WindowSamples > 0 ? latency.LifetimeP95UpperBound <= 3000 && latency.LifetimeP99UpperBound <= 5000 : null;
        bool? uiPass = ui.WindowSamples > 0 ? ui.LifetimeP95UpperBound <= 250 : null;
        var boundsPass = samples.Count > 0 && samples.All(s => s.Tracked <= vm.Options.Simulation.Population && s.Mailbox <= vm.Options.Simulation.Population &&
            (long)s.RetainedPoints <= (long)vm.Options.Simulation.Population * vm.Options.History.MaximumPoints);
        var allSymbolsObserved = samples.Any(s => s.RenderedSymbols == vm.Options.Simulation.Population);
        var fullPopulation = samples.Any(s => s.Tracked == vm.Options.Simulation.Population);
        var soakEvaluated = duration >= 1800 && memorySlope != null;
        var basemapReady = !vm.Options.Basemap.Enabled || vm.Metrics.BasemapImageResponses > 0;
        var basemapHealthy = !vm.Options.Basemap.Enabled || basemapReady && vm.Metrics.BasemapFailures == 0;
        var dpi = VisualTreeHelper.GetDpi(window.MapView);
        var report = new
        {
            schemaVersion = 1, success = error == null && vm.Engine?.Failure == null && fullPopulation && vm.Renderer.Frames > 0,
            error = error ?? vm.Engine?.Failure?.Message, utc = DateTimeOffset.UtcNow, durationSeconds = duration, coverage, checks,
            options = vm.Options, tracesEnabled = vm.ShowTraces,
            environment = new { os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                machine = Environment.MachineName, cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), mapsui = typeof(Map).Assembly.GetName().Version?.ToString(),
                width = window.MapView.ActualWidth, height = window.MapView.ActualHeight, dpiX = dpi.PixelsPerInchX, dpiY = dpi.PixelsPerInchY,
                basemap = vm.BasemapStatus, cache = "Existing GeoServer cache; client cache starts empty per process. Server cache not flushed." },
            counts = current?.Source.Counts, eligible = current?.Visible.Length, renderedSymbols = vm.Renderer.RenderedSymbols, renderedTraces = vm.Renderer.RenderedTraces,
            frames = vm.Renderer.Frames, measurements = vm.Metrics.Summary(),
            acceptance = new { fullPopulation, allSymbolsObserved, basemapReady, basemapHealthy, boundedState = boundsPass, latencyPass, dispatcherPass = uiPass,
                soakEvaluated, memorySlopeMiBPerMinute = memorySlope, memoryStable = soakEvaluated ? memorySlope <= 1 : (bool?)null,
                performanceTargetsMet = latencyPass == true && uiPass == true && boundsPass && basemapHealthy && (!vm.Options.Simulation.Dense || allSymbolsObserved),
                note = "Success denotes functional completion. Performance targets and soak are separate. Memory stability uses <=1 MiB/min linear trend after minute 10; inspect CSV for steps/leaks. Acceptance uses bounded whole-run histograms (1 ms buckets; percentile overflow above 60 s is null/failure). Exact recent-window percentiles are also reported." },
            samples
        };
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        using var writer = new StreamWriter(Path.ChangeExtension(path, ".csv"));
        writer.WriteLine("seconds,private_bytes,working_set_bytes,managed_bytes,allocated_bytes,cpu_seconds,gc0,gc1,gc2,tracked,history_points,mailbox,rendered_symbols,rendered_traces");
        foreach (var s in samples) writer.WriteLine(FormattableString.Invariant($"{s.Seconds:F3},{s.PrivateBytes},{s.WorkingSetBytes},{s.ManagedBytes},{s.AllocatedBytes},{s.CpuSeconds:F3},{s.Gen0},{s.Gen1},{s.Gen2},{s.Tracked},{s.RetainedPoints},{s.Mailbox},{s.RenderedSymbols},{s.RenderedTraces}"));
    }
}
public static class DesktopRun
{
    public static async Task RunAsync(MainWindow window, RunOptions options)
    {
        var vm = window.ViewModel; var samples = new List<ProcessSample>(); var checks = new List<string>(); string? error = null;
        var watch = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        try
        {
            vm.ShowTraces = !options.NoTraces; await vm.StartCommand.ExecuteAsync(null);
            var nextSample = 0d; var nextInteraction = 15d; var interaction = 0;
            while (watch.Elapsed.TotalSeconds < options.Seconds)
            {
                await Task.Delay(250);
                if (vm.Engine?.Failure != null) throw new InvalidOperationException("Pipeline failure", vm.Engine.Failure);
                if (watch.Elapsed.TotalSeconds >= nextSample)
                {
                    nextSample += Math.Max(1, options.Seconds / 720d); process.Refresh(); var c = vm.Current?.Source.Counts;
                    samples.Add(new(watch.Elapsed.TotalSeconds, process.PrivateMemorySize64, process.WorkingSet64, GC.GetTotalMemory(false), GC.GetTotalAllocatedBytes(),
                        process.TotalProcessorTime.TotalSeconds, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), c?.Tracked ?? 0, c?.HistoryPoints ?? 0,
                        c?.Pending ?? 0, vm.Renderer.RenderedSymbols, vm.Renderer.RenderedTraces));
                }
                if (options.Exercise && watch.Elapsed.TotalSeconds >= nextInteraction && vm.Rows.Length > 0)
                {
                    nextInteraction += 10;
                    switch (interaction++ % 5)
                    {
                        case 0: vm.SelectKey(vm.Rows[0].Key); vm.FocusCommand.Execute(null); vm.Following = true; break;
                        case 1: vm.SuspendFollowing(); vm.HomeCommand.Execute(null); break;
                        case 2: vm.DisplayEditor.Search = "0001"; vm.ApplyDisplayFilterCommand.Execute(null); break;
                        case 3: vm.ClearDisplayFilterCommand.Execute(null); break;
                        case 4:
                            var p = vm.Map.Navigator.Viewport; vm.Map.Navigator.CenterOn(p.CenterX + p.Resolution * 20, p.CenterY, 0); break;
                    }
                }
            }
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
            Check(vm.Current != null && vm.Current.Source.Counts.Tracked == vm.Options.Simulation.Population, "Configured population is tracked");
            Check(vm.Renderer.Frames > 0 && window.MapView.ActualWidth > 0, "Native WPF map rendered frames");
            Check(options.NoBasemap || vm.Metrics.BasemapImageResponses > 0, "Basemap image responses received, or explicit overlay-only mode");
            Check(options.NoBasemap || vm.Metrics.BasemapFailures == 0, "Basemap loaded without request errors");
            if (options.Smoke)
            {
                vm.ClearDisplayFilterCommand.Execute(null); vm.HomeCommand.Execute(null); await Task.Delay(1500);
                var selected = vm.Rows.First(); vm.SelectKey(selected.Key); vm.FocusCommand.Execute(null); vm.Following = true; await Task.Delay(1500);
                Check(vm.Details.Contains(selected.Identity), "Selection details identify the track");
                selected = vm.Rows.First(t => t.Key == selected.Key);
                var point = OgcDemo.Core.Coordinates.Project(selected.Report.Longitude, selected.Report.Latitude);
                var screen = vm.Map.Navigator.Viewport.WorldToScreen(new MPoint(point.X, point.Y));
                Check(window.MapView.GetMapInfo(screen, [vm.Symbols]).Feature != null, "Rendered traffic symbol is pickable");
                Check(!vm.ShowTraces || vm.Current!.Traces.Features.Length > 0, "Trace geometry is published");
                vm.SuspendFollowing(); Check(!vm.Following, "Manual interaction suspends following");
                var before = vm.Engine!.Clock.Seconds; vm.PauseCommand.Execute(null); await Task.Delay(1200);
                Check(vm.Engine.Clock.Seconds - before < .2, "Pause freezes simulation time"); vm.ResumeCommand.Execute(null);
                vm.DisplayEditor.Ships = false; vm.ApplyDisplayFilterCommand.Execute(null); await Task.Delay(1800);
                Check(vm.Rows.All(t => t.Kind == TrackKind.Aircraft), "Display filter hides ships");
                Check(vm.Current!.Source.Counts.Tracked == vm.Options.Simulation.Population, "Display filtering preserves track state");
                vm.ClearDisplayFilterCommand.Execute(null); vm.InputEditor.Aircraft = false; vm.ApplyInputFilterCommand.Execute(null); await Task.Delay(1800);
                Check(vm.Current!.Source.Tracks.All(t => t.Kind == TrackKind.Ship), "Input filter purges aircraft");
                vm.ClearInputFilterCommand.Execute(null); await vm.ResetCommand.ExecuteAsync(null); await vm.StartCommand.ExecuteAsync(null);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(vm.ShipInterval, vm.AircraftInterval) + 2));
                Check(vm.Current!.Source.Counts.Tracked == vm.Options.Simulation.Population, "Reset/start rebuilds the configured population");
                var original = vm.Options.Basemap.Url; vm.Options.Basemap.Url = "http://127.0.0.1:1/geoserver"; vm.BasemapProtocol = "WMTS";
                await vm.ConnectBasemap(); Check(vm.BasemapStatus.Contains("unavailable"), "Basemap failure is recoverable");
                vm.Options.Basemap.Url = original; vm.BasemapProtocol = options.NoBasemap ? "None" : "WMTS"; await vm.ConnectBasemap();
                vm.HomeCommand.Execute(null); await Task.Delay(1000);
            }
            Capture(window, Path.ChangeExtension(options.Report!, ".png"));
        }
        catch (Exception e) { error = e.ToString(); }
        BenchmarkReport.Write(options.Report!, window, samples, watch.Elapsed.TotalSeconds,
            options.Smoke ? "Native WPF in-process smoke; no physical input automation." : options.Exercise ? "Native WPF benchmark with scripted navigation, following and display filtering." : "Native WPF fixed-view benchmark.", error, checks);
        window.Close();
    }
    private static void Capture(MainWindow window, string path)
    {
        var surface = (System.Windows.FrameworkElement)window.Content; surface.UpdateLayout();
        var image = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
