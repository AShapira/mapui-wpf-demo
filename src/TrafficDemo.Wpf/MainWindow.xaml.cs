using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace TrafficDemo.Wpf;
public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private bool _closing;
    private bool _shutdownStarted;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); ViewModel = viewModel; DataContext = viewModel;
        MapView.Map = viewModel.Map; MapView.SetMapRenderer(viewModel.Renderer);
        viewModel.Map.Tapped += (_, e) =>
        {
            var info = e.GetMapInfo([viewModel.Symbols]);
            if (info.Feature?["key"] is string key) viewModel.SelectKey(key);
        };
        MapView.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed) viewModel.SuspendFollowing(); };
        MapView.PreviewMouseWheel += (_, _) => viewModel.SuspendFollowing();
        MapView.TouchMove += (_, _) => viewModel.SuspendFollowing();
        Closing += async (sender, e) =>
        {
            if (_closing) return; e.Cancel = true;
            if (_shutdownStarted) return;
            _shutdownStarted = true; IsEnabled = false;
            // Yield even when disposal completes synchronously: Close cannot reenter Closing.
            await Task.Yield();
            await viewModel.DisposeAsync(); MapView.Dispose(); _closing = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        };
    }
    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON report|*.json", FileName = $"traffic-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) == true) BenchmarkReport.Write(dialog.FileName, this, [], 0, "Interactive snapshot; not a completed benchmark or soak.");
    }
}
