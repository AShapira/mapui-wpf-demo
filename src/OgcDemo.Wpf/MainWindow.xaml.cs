using System.Windows;
using Mapsui;
using OgcDemo.Core;

namespace OgcDemo.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); _viewModel = viewModel; DataContext = viewModel;
        MapView.Map = viewModel.Map; viewModel.Map.Tapped += MapTapped;
        Closed += (_, _) => { viewModel.Map.Tapped -= MapTapped; viewModel.Dispose(); };
    }
    private void LayerSelected(object sender, RoutedPropertyChangedEventArgs<object> e) => _viewModel.SelectedLayer = e.NewValue as LayerEntry;
    private async void MapTapped(object? sender, MapEventArgs e)
    {
        var info = e.GetMapInfo(_viewModel.Layers.Where(l => l.Enabled && l.Layer is Mapsui.Layers.MemoryLayer).Select(l => l.Layer));
        await _viewModel.SelectAsync(info.Feature, e.ScreenPosition.X, e.ScreenPosition.Y);
    }
}
