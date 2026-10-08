using CommunityToolkit.Mvvm.ComponentModel;
using Mapsui.Layers;

namespace OgcDemo.Wpf;

public sealed class LayerEntry(ILayer layer, Action changed) : ObservableObject
{
    public ILayer Layer { get; } = layer;
    public string Name => Layer.Name;
    public bool Enabled { get => Layer.Enabled; set { Layer.Enabled = value; OnPropertyChanged(); changed(); } }
    public double Opacity { get => Layer.Opacity; set { Layer.Opacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); changed(); } }
}
