using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OgcDemo.Tests;

/// <summary>Real loopback HTTP, generated OGC documents, no work endpoints or data.</summary>
public sealed class OgcFixture : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    public ConcurrentQueue<(string Request, string? Authorization, string Query)> Requests { get; } = new();
    public string Url { get; }
    public bool RequireAuth { get; set; }
    public string Crs { get; set; } = "EPSG:3857";
    public string Geometry { get; set; } = "<gml:Point srsName=\"EPSG:3857\"><gml:pos>100 200</gml:pos></gml:Point>";
    public string? Failure { get; set; }
    public string ExtraWmtsResources { get; set; } = "";
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGOQzFvwHwAD8gInMMAcvAAAAABJRU5ErkJggg==");
    public OgcFixture()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        Url = $"http://127.0.0.1:{port}/ogc";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/"); _listener.Start(); _ = ServeAsync();
    }
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); } catch { break; }
            _ = RespondAsync(context);
        }
    }
    private async Task RespondAsync(HttpListenerContext c)
    {
        try
        {
            var q = c.Request.QueryString;
            var request = q["request"] ?? "GetTile";
            var version = q["version"] ?? "1.3.0";
            Requests.Enqueue((request, c.Request.Headers["Authorization"], c.Request.Url!.Query));
            if (RequireAuth && c.Request.Headers["Authorization"] != "Basic ZGVtbzpwYXNz") { c.Response.StatusCode = 401; return; }
            if (Failure == "timeout") { await Task.Delay(2500, _stop.Token); return; }
            if (Failure == "redirect") { c.Response.StatusCode=302; c.Response.RedirectLocation="https://different.example.invalid/"; return; }
            if (Failure == "slowbody") { c.Response.SendChunked=true; await c.Response.OutputStream.WriteAsync(new byte[]{120}); await c.Response.OutputStream.FlushAsync(); await Task.Delay(2500,_stop.Token); return; }
            if (Failure == "http") { c.Response.StatusCode = 503; return; }
            string? text = Failure switch
            {
                "xml" => "<broken>",
                "exception" => "<ServiceExceptionReport><ServiceException>secret server detail</ServiceException></ServiceExceptionReport>",
                "large" or "stream" => new string('x', 4096),
                _ => request switch
                {
                    "GetCapabilities" when q["service"] == "WMTS" => Wmts(),
                    "GetCapabilities" when q["service"] == "WFS" => Wfs(version),
                    "GetCapabilities" => Wms(version),
                    "DescribeFeatureType" => "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" targetNamespace=\"urn:demo\"><xs:element name=\"sites\" type=\"xs:anyType\"/></xs:schema>",
                    "GetFeature" => Collection(Geometry, version),
                    "GetFeatureInfo" => "name: synthetic WMS result",
                    _ => null
                }
            };
            var bytes = text is null ? Png : Encoding.UTF8.GetBytes(text);
            c.Response.ContentType = text is null ? "image/png" : request == "GetFeatureInfo" || Failure is "large" or "stream" ? "text/plain" : "text/xml";
            if (Failure=="stream") c.Response.SendChunked=true; else c.Response.ContentLength64 = bytes.Length; await c.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
        catch (HttpListenerException) { }
        finally { c.Response.Close(); }
    }
    public string Wms(string version) => $$"""
        <{{(version == "1.3.0" ? "WMS_Capabilities" : "WMT_MS_Capabilities")}} version="{{version}}" {{(version == "1.3.0" ? "xmlns=\"http://www.opengis.net/wms\"" : "")}} xmlns:xlink="http://www.w3.org/1999/xlink">
          <Service><Name>WMS</Name><Title>Fixture</Title><OnlineResource xlink:href="{{Url}}"/></Service>
          <Capability><Request>
            <GetCapabilities><Format>text/xml</Format><DCPType><HTTP><Get><OnlineResource xlink:href="{{Url}}"/></Get></HTTP></DCPType></GetCapabilities>
            <GetMap><Format>image/png</Format><DCPType><HTTP><Get><OnlineResource xlink:href="{{Url}}"/></Get></HTTP></DCPType></GetMap>
            <GetFeatureInfo><Format>text/plain</Format><DCPType><HTTP><Get><OnlineResource xlink:href="{{Url}}"/></Get></HTTP></DCPType></GetFeatureInfo>
          </Request><Exception><Format>XML</Format></Exception>
          <Layer queryable="1"><Title>Fixture layers</Title><{{(version == "1.3.0" ? "CRS" : "SRS")}}>{{Crs}}</{{(version == "1.3.0" ? "CRS" : "SRS")}}>
          <Layer><Name>demo:sites</Name><Title>Sites</Title><BoundingBox {{(version == "1.3.0" ? "CRS" : "SRS")}}="{{Crs}}" minx="0" miny="0" maxx="1000" maxy="1000"/></Layer></Layer>
          </Capability>
        </{{(version == "1.3.0" ? "WMS_Capabilities" : "WMT_MS_Capabilities")}}>
        """;
    public string Wfs(string version) => $$"""
        <wfs:WFS_Capabilities xmlns:wfs="http://www.opengis.net/wfs{{(version == "2.0.0" ? "/2.0" : "")}}" xmlns:ows="http://www.opengis.net/ows/1.1" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:demo="urn:demo" version="{{version}}">
        <ows:OperationsMetadata>
          <ows:Operation name="DescribeFeatureType"><ows:DCP><ows:HTTP><ows:Get xlink:href="{{Url}}"/></ows:HTTP></ows:DCP></ows:Operation>
          <ows:Operation name="GetFeature"><ows:DCP><ows:HTTP><ows:Get xlink:href="{{Url}}"/></ows:HTTP></ows:DCP>
          <ows:Parameter name="outputFormat"><ows:AllowedValues><ows:Value>{{(version == "2.0.0" ? "application/gml+xml; version=3.2" : "text/xml; subtype=gml/3.1.1")}}</ows:Value></ows:AllowedValues></ows:Parameter></ows:Operation>
        </ows:OperationsMetadata>
        <wfs:FeatureTypeList><wfs:FeatureType><wfs:Name>demo:sites</wfs:Name><wfs:{{(version == "2.0.0" ? "DefaultCRS" : "DefaultSRS")}}>{{Crs}}</wfs:{{(version == "2.0.0" ? "DefaultCRS" : "DefaultSRS")}}></wfs:FeatureType></wfs:FeatureTypeList>
        </wfs:WFS_Capabilities>
        """;
    public string Wmts() => $$"""
        <Capabilities xmlns="http://www.opengis.net/wmts/1.0" xmlns:ows="http://www.opengis.net/ows/1.1" xmlns:xlink="http://www.w3.org/1999/xlink" version="1.0.0">
          <ows:ServiceIdentification><ows:Title>Fixture</ows:Title><ows:ServiceType>OGC WMTS</ows:ServiceType><ows:ServiceTypeVersion>1.0.0</ows:ServiceTypeVersion></ows:ServiceIdentification>
          <Contents><Layer><ows:Title>Fixture tiles</ows:Title><ows:Identifier>demo:sites</ows:Identifier>
            <Style isDefault="true"><ows:Identifier>default</ows:Identifier></Style><Format>image/png</Format>
            <TileMatrixSetLink><TileMatrixSet>WebMercatorQuad</TileMatrixSet></TileMatrixSetLink>
            <ResourceURL format="image/png" resourceType="tile" template="{{Url}}?request=GetTile&amp;TileMatrix={TileMatrix}&amp;TileRow={TileRow}&amp;TileCol={TileCol}"/>
            {{ExtraWmtsResources}}
          </Layer><TileMatrixSet><ows:Identifier>WebMercatorQuad</ows:Identifier><ows:SupportedCRS>{{Crs}}</ows:SupportedCRS>
            <TileMatrix><ows:Identifier>0</ows:Identifier><ScaleDenominator>559082264.0287178</ScaleDenominator><TopLeftCorner>-20037508.3427892 20037508.3427892</TopLeftCorner><TileWidth>256</TileWidth><TileHeight>256</TileHeight><MatrixWidth>1</MatrixWidth><MatrixHeight>1</MatrixHeight></TileMatrix>
          </TileMatrixSet></Contents>
        </Capabilities>
        """;
    public static string Collection(string geometry, string version = "2.0.0") => $$"""
        <wfs:FeatureCollection xmlns:wfs="http://www.opengis.net/wfs{{(version == "2.0.0" ? "/2.0" : "")}}" xmlns:gml="http://www.opengis.net/gml{{(version == "2.0.0" ? "/3.2" : "")}}" xmlns:demo="urn:demo"><{{(version == "2.0.0" ? "wfs:member" : "gml:featureMember")}}><demo:sites gml:id="site.1"><demo:name>Fixture site</demo:name><demo:geometry>{{geometry}}</demo:geometry></demo:sites></{{(version == "2.0.0" ? "wfs:member" : "gml:featureMember")}}></wfs:FeatureCollection>
        """;
    public void Dispose() { _stop.Cancel(); _listener.Close(); _stop.Dispose(); }
}
