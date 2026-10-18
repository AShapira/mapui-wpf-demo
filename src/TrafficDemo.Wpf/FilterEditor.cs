using System.Globalization;
using TrafficDemo.Core;
namespace TrafficDemo.Wpf;
public sealed class FilterEditor
{
    public bool Ships { get; set; } = true;
    public bool Aircraft { get; set; } = true;
    public string Search { get; set; } = "";
    public string MinimumSpeed { get; set; } = "";
    public string MaximumSpeed { get; set; } = "";
    public string MinimumAltitude { get; set; } = "";
    public string MaximumAltitude { get; set; } = "";
    public string Bounds { get; set; } = "";
    public TrackFilter Build()
    {
        static double? Number(string value) => string.IsNullOrWhiteSpace(value) ? null : double.Parse(value, CultureInfo.InvariantCulture);
        GeoBounds? box = null;
        if (!string.IsNullOrWhiteSpace(Bounds))
        {
            var values = Bounds.Split(',').Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (values.Length != 4) throw new ArgumentException("Bounds require west,south,east,north.");
            box = new(values[0], values[1], values[2], values[3]);
        }
        var filter = new TrackFilter { Ships = Ships, Aircraft = Aircraft, Search = Search, MinimumSpeed = Number(MinimumSpeed),
            MaximumSpeed = Number(MaximumSpeed), MinimumAltitude = Number(MinimumAltitude), MaximumAltitude = Number(MaximumAltitude), Bounds = box };
        filter.Validate(); return filter;
    }
    public static FilterEditor From(TrackFilter f) => new() { Ships = f.Ships, Aircraft = f.Aircraft, Search = f.Search,
        MinimumSpeed = f.MinimumSpeed?.ToString(CultureInfo.InvariantCulture) ?? "", MaximumSpeed = f.MaximumSpeed?.ToString(CultureInfo.InvariantCulture) ?? "",
        MinimumAltitude = f.MinimumAltitude?.ToString(CultureInfo.InvariantCulture) ?? "", MaximumAltitude = f.MaximumAltitude?.ToString(CultureInfo.InvariantCulture) ?? "",
        Bounds = f.Bounds == null ? "" : FormattableString.Invariant($"{f.Bounds.West},{f.Bounds.South},{f.Bounds.East},{f.Bounds.North}") };
}
