using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OgcDemo.Core;

namespace OgcDemo.Wpf;

public partial class App : Application
{
    private IHost? _host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory, Args = [] });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), false)
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"), true);
            var options = builder.Configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
            builder.Services.AddSingleton(options);
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OgcDemo", "logs")));
            builder.Services.AddSingleton<MainViewModel>(); builder.Services.AddSingleton<MainWindow>();
            _host = builder.Build(); await _host.StartAsync();
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            Mapsui.Logging.Logger.LogDelegate = (level, _, exception) =>
            {
                if (level is Mapsui.Logging.LogLevel.Error or Mapsui.Logging.LogLevel.Warning)
                    logger.LogWarning("Mapsui event; severity {Severity}, exception type {ExceptionType}", level, exception?.GetType().Name ?? "none");
            };
            MainWindow = _host.Services.GetRequiredService<MainWindow>(); MainWindow.Show();
            if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
                await DesktopSmoke.RunAsync((MainWindow)MainWindow, _host.Services.GetRequiredService<MainViewModel>(), e.Args[1]);
        }
        catch (Exception ex)
        {
            if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
            {
                File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.ToString() }));
                Shutdown(1); return;
            }
            MessageBox.Show("Startup failed. Check appsettings.json, the sample files, and the prerequisites.\n" + ex.GetType().Name,
                "OGC demo", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Mapsui.Logging.Logger.LogDelegate = null;
        _host?.Dispose(); base.OnExit(e);
    }
}
