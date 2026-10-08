using System.Globalization;
using Mapsui;
using Mapsui.Projections;
using NetTopologySuite.Geometries;

namespace OgcDemo.Core;

public static class Coordinates
{
    public const double MaxLatitude = 85.0511287798066;
    public static MPoint Project(double longitude, double latitude)
    {
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude) || longitude is < -180 or > 180 || Math.Abs(latitude) > MaxLatitude)
            throw new DemoException("Coordinates must be longitude/latitude within Web Mercator's valid range.");
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        return new MPoint(x, y);
    }
    public static MRect ProjectExtent(double[] bounds)
    {
        if (bounds.Length != 4 || bounds[0] >= bounds[2] || bounds[1] >= bounds[3])
            throw new DemoException("InitialExtent must be [west,south,east,north], without crossing the dateline.");
        var a = Project(bounds[0], bounds[1]); var b = Project(bounds[2], bounds[3]);
        return new MRect(a.X, a.Y, b.X, b.Y);
    }
    public static string Bbox(MRect b, string crs, string protocol, string version)
    {
        var swap = crs == "EPSG:4326" && (protocol == "WFS" || version == "1.3.0");
        double[] values = swap ? [b.MinY, b.MinX, b.MaxY, b.MaxX] : [b.MinX, b.MinY, b.MaxX, b.MaxY];
        return string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }
    public static Geometry ToMercator(Geometry input)
    {
        var copy = input.Copy();
        copy.Apply(new ProjectionFilter()); copy.GeometryChanged(); copy.SRID = 3857;
        return copy;
    }
    private sealed class ProjectionFilter : ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;
        public void Filter(CoordinateSequence seq, int i)
        {
            var p = Project(seq.GetX(i), seq.GetY(i));
            seq.SetX(i, p.X); seq.SetY(i, p.Y);
        }
    }
}
