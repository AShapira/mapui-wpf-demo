using System.Globalization;
using System.Xml.Linq;
using Mapsui;
using Mapsui.Nts;

namespace OgcDemo.Core;

public sealed class WfsClient(ServiceOptions options, OgcTransport transport)
{
    private string? _format;
    private string? _getFeatureUrl;
    public async Task InitializeAsync(CancellationToken token)
    {
        var bytes = await transport.GetAsync(OgcTransport.Query(options.Url, ("service", "WFS"), ("request", "GetCapabilities"), ("version", options.Version)), token);
        var doc = OgcTransport.ParseXml(bytes);
        if (doc.Root?.Attribute("version")?.Value != options.Version) throw new DemoException("WFS returned a different version than configured.");
        var type = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "FeatureType" && e.Elements().Any(n => n.Name.LocalName == "Name" && n.Value == options.Layer))
            ?? throw new DemoException("WFS feature type was not advertised. Check the qualified type name.");
        if (!type.Elements().Where(e => e.Name.LocalName is "DefaultSRS" or "OtherSRS" or "DefaultCRS" or "OtherCRS").Any(e => GmlFeatures.IsMercator(e.Value)))
            throw new DemoException("WFS feature type must advertise EPSG:3857.");
        var operation = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Operation" && (string?)e.Attribute("name") == "GetFeature")
            ?? throw new DemoException("WFS GetFeature operation is missing.");
        _getFeatureUrl = operation.Descendants().FirstOrDefault(e => e.Name.LocalName == "Get")?.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value
            ?? throw new DemoException("This demo requires WFS GetFeature HTTP GET support.");
        _format = operation.Descendants().Where(e => e.Name.LocalName == "Parameter" && (string?)e.Attribute("name") == "outputFormat")
            .SelectMany(e => e.Descendants().Where(n => n.Name.LocalName == "Value")).Select(e => e.Value)
            .Concat(type.Descendants().Where(e => e.Name.LocalName == "Format").Select(e => e.Value))
            .FirstOrDefault(v => v.Contains("gml", StringComparison.OrdinalIgnoreCase) && (v.Contains("3.2") || v.Contains("3.1") || v.Equals("GML3", StringComparison.OrdinalIgnoreCase)))
            ?? throw new DemoException("WFS must advertise GML 3.1 or 3.2 output.");
        // Check the published schema as a separate authenticated request, as a real WFS client does.
        var describe = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Operation" && (string?)e.Attribute("name") == "DescribeFeatureType");
        var schemaUrl = describe?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Get")?.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value ?? options.Url;
        var schema = OgcTransport.ParseXml(await transport.GetAsync(OgcTransport.Query(schemaUrl,
            ("service", "WFS"), ("version", options.Version), ("request", "DescribeFeatureType"), ("typeName", options.Layer)), token));
        if (schema.Root?.Name.LocalName != "schema") throw new DemoException("WFS DescribeFeatureType did not return an XML schema.");
    }
    public async Task<IReadOnlyList<GeometryFeature>> FetchAsync(MRect extent, CancellationToken token)
    {
        if (_format is null || _getFeatureUrl is null) throw new InvalidOperationException("Initialize WFS before fetching.");
        var url = OgcTransport.Query(_getFeatureUrl, ("service", "WFS"), ("version", options.Version), ("request", "GetFeature"),
            (options.Version == "2.0.0" ? "typeNames" : "typeName", options.Layer),
            (options.Version == "2.0.0" ? "count" : "maxFeatures", options.MaxFeatures.ToString(CultureInfo.InvariantCulture)),
            ("srsName", "EPSG:3857"), ("bbox", Coordinates.Bbox(extent, "EPSG:3857", "WFS", options.Version) + ",EPSG:3857"), ("outputFormat", _format));
        return GmlFeatures.Read(await transport.GetAsync(url, token), options.MaxFeatures);
    }
}
