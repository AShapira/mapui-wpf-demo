using System.Windows;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Providers.Wms;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace HelloMap.Wpf;

public partial class MainWindow : Window
{
    // Change these two values to your WMS endpoint and advertised layer name.
    private const string WmsUrl = "https://ogc.example.invalid/wms";
    private const string WmsLayer = "demo:basemap";
    private readonly ImageLayer _basemap = new("WMS basemap");

    public MainWindow()
    {
        InitializeComponent();

        // Longitude/latitude vertices near Tel Aviv, projected to the map's CRS.
        var vertices = new[] { (34.78, 32.03), (34.82, 32.08), (34.87, 32.05), (34.92, 32.10) }
            .Select(vertex =>
            {
                var (x, y) = SphericalMercator.FromLonLat(vertex.Item1, vertex.Item2);
                return new Coordinate(x, y);
            }).ToArray();

        var line = new GeometryFeature { Geometry = new LineString(vertices) };
        var overlay = new MemoryLayer("Polyline with vertices")
        {
            Features = new[] { line },
            Style = new StyleCollection
            {
                Styles =
                {
                    new VectorStyle { Line = new Pen(Color.Red, 3) },
                    // SymbolStyle draws a circle at each vertex of the same geometry.
                    new SymbolStyle { Fill = new Brush(Color.White), Outline = new Pen(Color.Red, 2), SymbolScale = 0.5 }
                }
            }
        };

        var map = new Map { CRS = "EPSG:3857" };
        map.Layers.Add(_basemap); // First layer is underneath.
        map.Layers.Add(overlay);
        map.Navigator.ZoomToBox(overlay.Extent!.Grow(2000));
        MapView.Map = map;
        Closed += (_, _) => MapView.Dispose();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowLoaded;
        try
        {
            var wms = await WmsProvider.CreateAsync(WmsUrl, "1.3.0");
            wms.CRS = "EPSG:3857";
            wms.AddLayer(WmsLayer);
            wms.SetImageFormat("image/png");
            if (!IsLoaded) return;
            _basemap.DataSource = wms;
            MapView.RefreshData();
        }
        catch (Exception ex)
        {
            if (IsLoaded)
                MessageBox.Show(this, "Could not load the WMS basemap. Check WmsUrl and WmsLayer.\n" + ex.Message, "Hello Map");
        }
    }
}
