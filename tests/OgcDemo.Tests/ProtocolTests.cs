using BruTile;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using OgcDemo.Core;
using Xunit;

namespace OgcDemo.Tests;

public class ProtocolTests
{
    private static ServiceOptions Options(OgcFixture f,string kind,string version) => new()
    { Url=f.Url,Kind=kind,Version=version,Layer="demo:sites",Style=kind=="WMTS"?"default":"", MatrixSet="WebMercatorQuad",MaxFeatures=10 };
    private static OgcTransport Transport(ServiceOptions o) => new(o,allowLoopbackHttp:true);
    [Theory] [InlineData("1.3.0")] [InlineData("1.1.1")]
    public async Task WmsCapabilitiesMapAndIdentify(string version)
    {
        using var f=new OgcFixture(); var o=Options(f,"WMS",version);
        using var t=Transport(o); using var layer=await OgcLayers.ConnectAsync(o,t,default);
        Assert.True(layer.Queryable);
        var url=layer.Wms!.GetRequestUrl(new MRect(0,0,1000,1000),256,256);
        Assert.Equal(OgcFixture.Png, await t.GetAsync(url));
        var text=await OgcLayers.IdentifyAsync(layer,new MRect(0,0,1000,1000),256,256,10,20,default);
        Assert.Contains("synthetic WMS",text);
        Assert.Contains(f.Requests,r=>r.Request=="GetFeatureInfo" && r.Query.Contains(version=="1.3.0"?"i=10":"x=10"));
    }
    [Theory] [InlineData("1.1.0")] [InlineData("2.0.0")]
    public async Task WfsCapabilitiesSchemaAndBoundedFeatures(string version)
    {
        using var f=new OgcFixture(); var o=Options(f,"WFS",version);
        using var t=Transport(o); var client=new WfsClient(o,t); await client.InitializeAsync(default);
        var features=await client.FetchAsync(new MRect(0,0,1000,1000),default); Assert.Single(features);
        Assert.Contains(f.Requests,r=>r.Request=="DescribeFeatureType");
        var featureRequest=f.Requests.Single(r=>r.Request=="GetFeature");
        Assert.Contains(version=="2.0.0"?"count=10":"maxFeatures=10",featureRequest.Query); Assert.Contains("bbox=",featureRequest.Query);
    }
    [Fact] public async Task WmtsUsesExplicitMatrixAndReturnsTile()
    {
        using var f=new OgcFixture(); var o=Options(f,"WMTS","1.0.0"); using var t=Transport(o);
        using var layer=await OgcLayers.ConnectAsync(o,t,default);
        var tiles=(TileLayer)layer.Layer;
        using var http=new HttpClient();
        var bytes=await ((BruTile.IHttpTileSource)tiles.TileSource).GetTileAsync(http,new TileInfo {Index=new TileIndex(0,0,0)});
        Assert.Equal(OgcFixture.Png,bytes);
        o.MatrixSet="not-advertised"; await Assert.ThrowsAsync<DemoException>(()=>OgcLayers.ConnectAsync(o,t,default));
    }
    [Fact] public async Task WmtsNeverRequestsTileJsonOrFeatureInfoAsAnImage()
    {
        using var f = new OgcFixture();
        f.ExtraWmtsResources = $"<ResourceURL format=\"application/json\" resourceType=\"TileJSON\" template=\"{f.Url}?request=GetTileJSON\"/>" +
            $"<ResourceURL format=\"text/plain\" resourceType=\"FeatureInfo\" template=\"{f.Url}?request=GetFeatureInfo\"/>" +
            $"<ResourceURL format=\"image/jpeg\" resourceType=\"tile\" template=\"{f.Url}?request=WrongFormat\"/>";
        var o = Options(f, "WMTS", "1.0.0"); using var transport = Transport(o);
        using var connection = await OgcLayers.ConnectAsync(o, transport, default);
        var source = (BruTile.IHttpTileSource)((TileLayer)connection.Layer).TileSource;
        using var http = new HttpClient();
        for (var i = 0; i < 32; i++) Assert.Equal(OgcFixture.Png, await source.GetTileAsync(http, new TileInfo { Index = new TileIndex(0, 0, 0) }));
        Assert.Equal(32, f.Requests.Count(r => r.Request == "GetTile"));
        Assert.DoesNotContain(f.Requests, r => r.Request is "GetTileJSON" or "GetFeatureInfo" or "WrongFormat");
    }
    [Theory] [InlineData("WMS","1.3.0")] [InlineData("WMTS","1.0.0")] [InlineData("WFS","2.0.0")]
    public async Task AuthenticationCoversDiscoveryAndSubsequentRequests(string kind,string version)
    {
        using var f=new OgcFixture {RequireAuth=true}; var o=Options(f,kind,version); o.Authentication="Basic";
        o.CredentialPrefix="OGC_TEST_"+Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(o.CredentialPrefix+"_USERNAME","demo"); Environment.SetEnvironmentVariable(o.CredentialPrefix+"_PASSWORD","pass");
        try
        {
            using var t=Transport(o); using var layer=await OgcLayers.ConnectAsync(o,t,default);
            if(kind=="WFS") await layer.Wfs!.FetchAsync(new MRect(0,0,1000,1000),default);
            if(kind=="WMS") { await t.GetAsync(layer.Wms!.GetRequestUrl(new MRect(0,0,1000,1000),100,100)); await OgcLayers.IdentifyAsync(layer,new MRect(0,0,1000,1000),100,100,10,10,default); }
            if(kind=="WMTS") { using var http=new HttpClient(); await ((BruTile.IHttpTileSource)((TileLayer)layer.Layer).TileSource).GetTileAsync(http,new TileInfo {Index=new TileIndex(0,0,0)}); }
            Assert.True(f.Requests.Count>=2); Assert.All(f.Requests,r=>Assert.Equal("Basic ZGVtbzpwYXNz",r.Authorization));
        }
        finally { Environment.SetEnvironmentVariable(o.CredentialPrefix+"_USERNAME",null); Environment.SetEnvironmentVariable(o.CredentialPrefix+"_PASSWORD",null); }
    }
    [Theory] [InlineData("WMS","1.3.0")] [InlineData("WFS","2.0.0")] [InlineData("WMTS","1.0.0")]
    public async Task UnsupportedServiceCrsRejected(string kind,string version)
    {
        using var f=new OgcFixture {Crs="EPSG:4326"}; var o=Options(f,kind,version); using var t=Transport(o);
        await Assert.ThrowsAsync<DemoException>(()=>OgcLayers.ConnectAsync(o,t,default));
    }
    [Theory] [InlineData("xml")] [InlineData("exception")] [InlineData("http")] [InlineData("large")] [InlineData("timeout")] [InlineData("stream")] [InlineData("slowbody")] [InlineData("redirect")]
    public async Task FailureIsBoundedAndSanitized(string failure)
    {
        using var f=new OgcFixture {Failure=failure}; var o=Options(f,"WMS","1.3.0"); o.MaxResponseBytes=1024; o.TimeoutSeconds=1;
        using var t=Transport(o); var e=await Assert.ThrowsAsync<DemoException>(()=>t.GetAsync(f.Url)); Assert.DoesNotContain("secret",e.Message);
    }
    [Fact] public async Task UnauthorizedFailsAndCrossOriginIsNeverSent()
    {
        using var f=new OgcFixture {RequireAuth=true}; using var t=Transport(Options(f,"WMS","1.3.0"));
        Assert.Contains("401",(await Assert.ThrowsAsync<DemoException>(()=>t.GetAsync(f.Url))).Message);
        await Assert.ThrowsAsync<DemoException>(()=>t.GetAsync("https://different.example.invalid/data")); Assert.Single(f.Requests);
    }
    [Fact] public async Task CallerCancellationStopsRequest()
    {
        using var f=new OgcFixture {Failure="timeout"}; using var t=Transport(Options(f,"WMS","1.3.0"));
        using var cts=new CancellationTokenSource(30); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>t.GetAsync(f.Url,cts.Token));
    }
}
