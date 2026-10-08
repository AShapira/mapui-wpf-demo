using System.Globalization;
using System.Text;
using System.Xml;
using BruTile;
using BruTile.Web;
using BruTile.Wmts;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Providers.Wms;
using Mapsui.Tiling.Layers;

namespace OgcDemo.Core;

public sealed class ConnectedLayer(ServiceOptions options, ILayer layer, OgcTransport transport) : IDisposable
{
    public ServiceOptions Options { get; } = options;
    public ILayer Layer { get; } = layer;
    public OgcTransport Transport { get; } = transport;
    public WmsProvider? Wms { get; init; }
    public WfsClient? Wfs { get; init; }
    public bool Queryable { get; init; }
    public string? InfoUrl { get; init; }
    public string? InfoFormat { get; init; }
    public void Dispose() { Layer.Dispose(); Transport.Dispose(); }
}

public static class OgcLayers
{
    public static async Task<ConnectedLayer> ConnectAsync(ServiceOptions o, OgcTransport transport, CancellationToken token)
    {
        if (o.Kind == "WFS")
        {
            var client = new WfsClient(o, transport); await client.InitializeAsync(token);
            return new ConnectedLayer(o, FeatureLayers.Create(o.Name, []), transport) { Wfs = client };
        }
        var bytes = await transport.GetAsync(OgcTransport.Query(o.Url, ("service", o.Kind), ("request", "GetCapabilities"), ("version", o.Version)), token);
        var capabilities = OgcTransport.ParseXml(bytes);
        if (capabilities.Root?.Attribute("version")?.Value != o.Version) throw new DemoException("Service capabilities version differs from configuration.");
        if (o.Kind == "WMTS")
        {
            using var stream = new MemoryStream(bytes);
            var sources = WmtsCapabilitiesParser.Parse(stream);
            var source = sources.FirstOrDefault(s => s.Schema is WmtsTileSchema w && w.Layer == o.Layer && w.TileMatrixSet == o.MatrixSet && w.Style == o.Style && w.Format == o.Format)
                ?? throw new DemoException("WMTS layer/style/format/matrix-set combination was not advertised.");
            if (!GmlFeatures.IsMercator(source.Schema.Srs)) throw new DemoException("WMTS matrix set must use EPSG:3857.");
            return new ConnectedLayer(o, new TileLayer(new AuthenticatedTileSource(source, transport)) { Name = o.Name }, transport);
        }
        var xml = new XmlDocument { XmlResolver = null }; using (var reader = capabilities.CreateReader()) xml.Load(reader);
        var provider = new WmsProvider(xml, url => transport.GetAsync(url)) { CRS = o.Crs, Transparent = true, ContinueOnError = false };
        provider.AddLayer(o.Layer); if (!string.IsNullOrEmpty(o.Style)) provider.AddStyle(o.Style);
        provider.SetImageFormat(o.Format);
        var advertised = capabilities.Descendants().FirstOrDefault(e => e.Name.LocalName == "Layer" && e.Elements().Any(n => n.Name.LocalName == "Name" && n.Value == o.Layer))
            ?? throw new DemoException("WMS layer was not advertised.");
        var hierarchy = advertised.AncestorsAndSelf().Where(e => e.Name.LocalName == "Layer").ToArray();
        if (!hierarchy.SelectMany(e => e.Elements()).Where(e => e.Name.LocalName is "CRS" or "SRS").Any(e => e.Value.Split(' ').Any(GmlFeatures.IsMercator)))
            throw new DemoException("WMS layer must advertise EPSG:3857.");
        var queryable = hierarchy.Select(e => (string?)e.Attribute("queryable")).FirstOrDefault(v => v != null) is "1" or "true";
        var info = capabilities.Descendants().FirstOrDefault(e => e.Name.LocalName == "GetFeatureInfo");
        var format = info?.Elements().FirstOrDefault(e => e.Name.LocalName == "Format" && e.Value == "text/plain")?.Value
            ?? info?.Elements().FirstOrDefault(e => e.Name.LocalName == "Format" && e.Value.Contains("xml"))?.Value;
        var infoUrl = info?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Get")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "OnlineResource")?.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
        return new ConnectedLayer(o, new ImageLayer(o.Name) { DataSource = provider }, transport)
            { Wms = provider, Queryable = queryable && format != null && infoUrl != null, InfoFormat = format, InfoUrl = infoUrl };
    }
    public static async Task<string> IdentifyAsync(ConnectedLayer layer, MRect extent, int width, int height, int x, int y, CancellationToken token)
    {
        if (!layer.Queryable || layer.Wms == null) return "This WMS layer does not advertise a supported GetFeatureInfo format.";
        var o = layer.Options;
        var url = OgcTransport.Query(layer.InfoUrl!, ("service", "WMS"), ("version", o.Version), ("request", "GetFeatureInfo"),
            ("layers", o.Layer), ("query_layers", o.Layer), ("styles", o.Style), ("format", o.Format), ("info_format", layer.InfoFormat!),
            (o.Version == "1.3.0" ? "crs" : "srs", o.Crs), ("bbox", Coordinates.Bbox(extent, o.Crs, "WMS", o.Version)),
            ("width", width.ToString(CultureInfo.InvariantCulture)), ("height", height.ToString(CultureInfo.InvariantCulture)),
            (o.Version == "1.3.0" ? "i" : "x", x.ToString(CultureInfo.InvariantCulture)), (o.Version == "1.3.0" ? "j" : "y", y.ToString(CultureInfo.InvariantCulture)), ("feature_count", "10"));
        var text = Encoding.UTF8.GetString(await layer.Transport.GetAsync(url, token));
        return text.Length > 12000 ? text[..12000] + "\n[Display truncated]" : text;
    }
    private sealed class AuthenticatedTileSource(HttpTileSource source, OgcTransport transport)
        : HttpTileSource(source.Schema, source, source.Name, attribution: source.Attribution)
    {
        public override async Task<byte[]?> GetTileAsync(HttpClient client, TileInfo info, CancellationToken? token = null) =>
            await transport.GetAsync(source.GetUrl(info).AbsoluteUri, token ?? CancellationToken.None);
    }
}
