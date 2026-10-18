using TrafficDemo.Core;
namespace TrafficDemo.Wpf;
public sealed class ViewerOptions
{
    public SimulationOptions Simulation { get; set; } = new();
    public HistoryOptions History { get; set; } = new();
    public TrackFilter InputFilter { get; set; } = new();
    public TrackFilter DisplayFilter { get; set; } = new();
    public BasemapOptions Basemap { get; set; } = new();
}
public sealed class BasemapOptions
{
    public string Url { get; set; } = "http://127.0.0.1:8602/geoserver";
    public string Layer { get; set; } = "omt:dark-matter-en";
    public string Protocol { get; set; } = "WMTS";
    public bool Enabled { get; set; } = true;
}
