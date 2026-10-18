using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Configuration;

namespace TrafficDemo.Wpf;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.local.json", optional: true).Build();
            var options = configuration.GetSection("Traffic").Get<ViewerOptions>() ?? new();
            var run = RunOptions.Parse(e.Args); run.Apply(options);
            options.Simulation.Validate(); options.History.Validate(); options.InputFilter.Validate(); options.DisplayFilter.Validate();
            var vm = new MainViewModel(options, Dispatcher); var window = new MainWindow(vm); MainWindow = window; window.Show();
            await vm.ConnectBasemap();
            if (run.Report != null) await DesktopRun.RunAsync(window, run);
            else if (e.Args.Contains("--start")) await vm.StartCommand.ExecuteAsync(null);
        }
        catch (Exception error)
        {
            var reportIndex = Array.IndexOf(e.Args, "--report");
            if (reportIndex >= 0 && reportIndex + 1 < e.Args.Length)
            {
                var path = Path.GetFullPath(e.Args[reportIndex + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // A rejected existing --report must never be overwritten by the error path.
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(stream, new { success = false, error = error.ToString() });
                }
                catch (IOException) when (File.Exists(path)) { }
            }
            else MessageBox.Show(error.Message, "Traffic viewer startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
