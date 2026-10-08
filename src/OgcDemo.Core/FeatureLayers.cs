using IFeature = Mapsui.IFeature;
using System.Text.Json;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Styles.Thematics;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace OgcDemo.Core;

public static class FeatureLayers
{
    public static MemoryLayer FromGeoJson(string text, string name)
    {
        if (text.Length > 8 * 1024 * 1024) throw new DemoException("GeoJSON exceeds the 8 MiB demo limit.");
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty("crs", out _))
            throw new DemoException("Use RFC 7946 longitude/latitude GeoJSON without a legacy crs member.");
        var options = new JsonSerializerOptions(); options.Converters.Add(new GeoJsonConverterFactory());
        var collection = JsonSerializer.Deserialize<FeatureCollection>(text, options)
            ?? throw new DemoException("Expected a GeoJSON FeatureCollection.");
        if (collection.Count > 10000) throw new DemoException("GeoJSON exceeds the 10,000 feature demo limit.");
        var features = collection.Select(f =>
        {
            if (f.Geometry is null || f.Geometry.IsEmpty || !f.Geometry.IsValid)
                throw new DemoException("GeoJSON features must have nonempty, valid geometry.");
            var item = new GeometryFeature(Coordinates.ToMercator(f.Geometry));
            foreach (var key in f.Attributes.GetNames()) item[key] = f.Attributes[key];
            return item;
        }).ToArray();
        return Create(name, features);
    }
    public static MemoryLayer Create(string name, IEnumerable<IFeature> features) => new(name)
    {
        Features = features.ToArray(),
        Style = GeometryStyle(false)
    };
    public static IStyle GeometryStyle(bool selected)
    {
        var color = selected ? new Color(255,150,0) : new Color(12,75,112);
        var fill = selected ? new Color(255,200,30,90) : new Color(40,130,180,100);
        var vector = new VectorStyle { Fill = new Brush(fill), Outline = new Pen(color, selected ? 4 : 2), Line = new Pen(color, selected ? 5 : 3) };
        var point = new SymbolStyle { SymbolScale = selected ? 0.8 : 0.55, Fill = new Brush(fill), Outline = new Pen(color, selected ? 4 : 2) };
        // SymbolStyle on a line/polygon draws vertex symbols, not its stroke/fill.
        return new ThemeStyle(f => f is GeometryFeature { Geometry: Point or MultiPoint } ? point : vector);
    }
    public static MemoryLayer Graphics()
    {
        var factory = new GeometryFactory();
        var p = Coordinates.Project(34.82, 32.07);
        var point = new GeometryFeature(factory.CreatePoint(new Coordinate(p.X, p.Y))) { ["name"] = "Synthetic observation" };
        var line = new GeometryFeature(Coordinates.ToMercator(factory.CreateLineString([
            new Coordinate(34.78,32.03),new Coordinate(34.85,32.08),new Coordinate(34.92,32.04)]))) { ["name"] = "Synthetic route" };
        var polygon = new GeometryFeature(Coordinates.ToMercator(factory.CreatePolygon([
            new Coordinate(34.94,32.10),new Coordinate(35.01,32.10),new Coordinate(35.01,32.15),new Coordinate(34.94,32.15),new Coordinate(34.94,32.10)]))) { ["name"] = "Synthetic area" };
        return Create("Graphics: points, lines, polygons", [point, line, polygon]);
    }
    public static MemoryLayer Highlight() => new("Selection") { Style = GeometryStyle(true) };
    public static void Select(MemoryLayer highlight, IFeature? feature)
    {
        if (feature is GeometryFeature g)
        {
            var clone = new GeometryFeature(g); clone.Styles.Clear(); highlight.Features = [clone];
        }
        else highlight.Features = [];
        highlight.DataHasChanged();
    }
}
