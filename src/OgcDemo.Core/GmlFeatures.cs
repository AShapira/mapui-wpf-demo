using System.Globalization;
using System.Xml.Linq;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace OgcDemo.Core;

/// <summary>A deliberately bounded GML 3.1/3.2 reader; not a general GML implementation.</summary>
public static class GmlFeatures
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 3857);
    private static bool IsGml(XElement e) => e.Name.NamespaceName is "http://www.opengis.net/gml" or "http://www.opengis.net/gml/3.2";
    private static readonly string[] GeometryNames = ["Point", "LineString", "Polygon", "MultiPoint", "MultiCurve", "MultiSurface", "MultiLineString", "MultiPolygon"];
    public static IReadOnlyList<GeometryFeature> Read(byte[] bytes, int limit)
    {
        var doc = OgcTransport.ParseXml(bytes);
        if (doc.Root?.Name.LocalName != "FeatureCollection") throw new DemoException("Expected a WFS FeatureCollection.");
        var members = doc.Root.Elements().Where(e => e.Name.LocalName is "member" or "featureMember").SelectMany(e => e.Elements()).ToList();
        members.AddRange(doc.Root.Elements().Where(e => e.Name.LocalName == "featureMembers").SelectMany(e => e.Elements()));
        if (members.Count > limit) throw new DemoException("The WFS ignored the feature limit. Zoom in or configure server limits.");
        return members.Select(f =>
        {
            var geometry = f.Descendants().FirstOrDefault(e => IsGml(e) && GeometryNames.Contains(e.Name.LocalName));
            if (geometry is null) throw new DemoException("WFS geometry is null or unsupported. Use simple 2D GML geometry.");
            foreach (var parent in geometry.Ancestors())
            {
                if (parent.Attribute("srsName") is { } srs && !IsMercator(srs.Value)) throw new DemoException("WFS returned an unexpected inherited CRS.");
                if (parent.Attribute("srsDimension") is { Value: not "2" }) throw new DemoException("The demo accepts only 2D GML coordinates.");
            }
            var parsed = ReadGeometry(geometry);
            if (parsed.IsEmpty || !parsed.IsValid) throw new DemoException("WFS returned empty or invalid geometry.");
            var feature = new GeometryFeature(parsed);
            feature["id"] = f.Attributes().FirstOrDefault(a => a.Name.LocalName is "id" or "fid")?.Value ?? "";
            foreach (var p in f.Elements().Where(e => !e.HasElements)) feature[p.Name.LocalName] = p.Value;
            return feature;
        }).ToArray();
    }
    private static Geometry ReadGeometry(XElement e)
    {
        foreach (var node in e.DescendantsAndSelf())
        {
            var srs = node.Attribute("srsName")?.Value;
            if (srs != null && !IsMercator(srs)) throw new DemoException("WFS returned a CRS other than requested EPSG:3857.");
            if (node.Attribute("srsDimension") is { Value: not "2" }) throw new DemoException("The demo accepts only 2D GML coordinates.");
        }
        return e.Name.LocalName switch
        {
            "Point" => Factory.CreatePoint(ReadCoordinates(e).Single()),
            "LineString" => Factory.CreateLineString(ReadCoordinates(e)),
            "Polygon" => ReadPolygon(e),
            "MultiPoint" => Factory.CreateMultiPoint(e.Descendants().Where(n => IsGml(n) && n.Name.LocalName == "Point").Select(n => (Point)ReadGeometry(n)).ToArray()),
            "MultiCurve" or "MultiLineString" => ReadMultiLine(e),
            "MultiSurface" or "MultiPolygon" => ReadMultiPolygon(e),
            _ => throw new DemoException("Unsupported GML geometry; curves, surfaces, and 3D need a dedicated extension.")
        };
    }
    private static MultiLineString ReadMultiLine(XElement e)
    {
        if (e.Descendants().Any(n => n.Name.LocalName is "Curve" or "Arc")) throw new DemoException("Curved GML geometry is unsupported.");
        return Factory.CreateMultiLineString(e.Descendants().Where(n => IsGml(n) && n.Name.LocalName == "LineString").Select(n => (LineString)ReadGeometry(n)).ToArray());
    }
    private static MultiPolygon ReadMultiPolygon(XElement e)
    {
        if (e.Descendants().Any(n => n.Name.LocalName == "Surface")) throw new DemoException("GML Surface patches are unsupported; request polygons.");
        return Factory.CreateMultiPolygon(e.Descendants().Where(n => IsGml(n) && n.Name.LocalName == "Polygon").Select(n => (Polygon)ReadGeometry(n)).ToArray());
    }
    private static Polygon ReadPolygon(XElement e)
    {
        var outer = e.Elements().FirstOrDefault(n => n.Name.LocalName == "exterior") ?? throw new DemoException("Polygon exterior is missing.");
        var shell = Factory.CreateLinearRing(ReadCoordinates(outer));
        var holes = e.Elements().Where(n => n.Name.LocalName == "interior").Select(n => Factory.CreateLinearRing(ReadCoordinates(n))).ToArray();
        return Factory.CreatePolygon(shell, holes);
    }
    private static Coordinate[] ReadCoordinates(XElement e)
    {
        var elements = e.DescendantsAndSelf().Where(n => IsGml(n) && n.Name.LocalName is "pos" or "posList").ToArray();
        if (elements.Length == 0) throw new DemoException("Expected GML pos or posList coordinates (GML 3.1/3.2).");
        var values = string.Join(" ", elements.Select(n => n.Value)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length % 2 != 0 || values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 20037508.35))
            throw new DemoException("GML contains invalid 2D Web Mercator coordinates.");
        return Enumerable.Range(0, values.Length / 2).Select(i => new Coordinate(values[i*2], values[i*2+1])).ToArray();
    }
    public static bool IsMercator(string text) => text.Equals("EPSG:3857", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("urn:ogc:def:crs:EPSG::3857", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("http://www.opengis.net/def/crs/EPSG/0/3857", StringComparison.OrdinalIgnoreCase);
}
