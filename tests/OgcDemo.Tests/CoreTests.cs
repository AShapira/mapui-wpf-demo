using Microsoft.Extensions.Configuration;
using Coordinates = OgcDemo.Core.Coordinates;
using System.Text;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OgcDemo.Core;
using Xunit;

namespace OgcDemo.Tests;

public class CoreTests
{
    [Fact] public void ConfigurationDoesNotAppendDuplicateExtentDefaults()
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes("{\"Demo\":{\"InitialExtent\":[34.7,31.9,35.1,32.2]}}"));
        var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddJsonStream(input).Build();
        var options=config.GetSection("Demo").Get<DemoOptions>()!;
        Assert.Equal(4,options.InitialExtent.Length);
        Assert.True(Coordinates.ProjectExtent(options.InitialExtent).Width>0);
    }

    [Theory]
    [InlineData(0,0)] [InlineData(34.82,32.07)] [InlineData(-123.5,-40.2)]
    public void ProjectionRoundtrip(double lon, double lat)
    {
        var p = Coordinates.Project(lon, lat); var ll = SphericalMercator.ToLonLat(p.X,p.Y);
        Assert.Equal(lon, ll.lon, 7); Assert.Equal(lat,ll.lat,7);
    }
    [Theory]
    [InlineData(200,0)] [InlineData(0,90)] [InlineData(double.NaN,0)]
    public void InvalidCoordinatesAreRejected(double lon,double lat) => Assert.Throws<DemoException>(() => Coordinates.Project(lon,lat));
    [Theory]
    [InlineData("WMS","1.3.0","20,10,40,30")]
    [InlineData("WMS","1.1.1","10,20,30,40")]
    [InlineData("WFS","2.0.0","20,10,40,30")]
    public void GeographicAxisOrder(string protocol,string version,string expected) => Assert.Equal(expected,Coordinates.Bbox(new MRect(10,20,30,40),"EPSG:4326",protocol,version));
    [Fact] public void GeoJsonProjectsFeaturesAndPreservesPolygonHole()
    {
        var layer = FeatureLayers.FromGeoJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"samples","sites.geojson")),"test");
        Assert.Equal(3,layer.Features.Count());
        var area = Assert.IsType<Polygon>(Assert.IsType<GeometryFeature>(layer.Features.Last()).Geometry);
        Assert.Equal(1,area.NumInteriorRings); Assert.Equal(3857,area.SRID); Assert.True(area.EnvelopeInternal.MinX > 1000000);
    }
    [Fact] public void GeometryStylesDrawLinesAndPolygonsRatherThanVertices()
    {
        var style=(Mapsui.Styles.Thematics.ThemeStyle)FeatureLayers.GeometryStyle(false);
        var features=FeatureLayers.Graphics().Features.ToArray();
        Assert.IsType<Mapsui.Styles.SymbolStyle>(style.GetStyle(features[0],new Viewport()));
        Assert.IsType<Mapsui.Styles.VectorStyle>(style.GetStyle(features[1],new Viewport()));
        Assert.IsType<Mapsui.Styles.VectorStyle>(style.GetStyle(features[2],new Viewport()));
    }
    [Fact] public void HighlightClonesAndClearsWithoutChangingOriginal()
    {
        var features = FeatureLayers.Graphics(); var item = (GeometryFeature)features.Features.First();
        using var highlight = FeatureLayers.Highlight(); FeatureLayers.Select(highlight,item);
        Assert.Single(highlight.Features); Assert.NotSame(item,highlight.Features.First());
        Assert.NotSame(item.Geometry,((GeometryFeature)highlight.Features.First()).Geometry);
        FeatureLayers.Select(highlight,null); Assert.Empty(highlight.Features); Assert.Equal(3,features.Features.Count());
    }
    [Fact] public void LayerOrderingIsBottomToTop()
    {
        using var map = new Map(); var a=FeatureLayers.Graphics(); var b=FeatureLayers.Highlight();
        map.Layers.Add(a); map.Layers.Add(b); map.Layers.MoveToTop(a); Assert.Same(a,map.Layers.Last());
        map.Layers.Remove(a); Assert.Single(map.Layers);
    }
    [Fact] public void OldRequestCannotCommitAfterNewerRequestOrDisposal()
    {
        using var latest = new LatestRequest(); var first=latest.Begin(); var second=latest.Begin();
        Assert.True(first.Token.IsCancellationRequested); Assert.False(latest.IsCurrent(first.Generation)); Assert.True(latest.IsCurrent(second.Generation));
        latest.Dispose(); Assert.False(latest.IsCurrent(second.Generation));
    }
    [Fact] public void FileLogExcludesExceptionPayload()
    {
        var folder=Path.Combine(Path.GetTempPath(),"ogc-log-test-"+Guid.NewGuid());
        try
        {
            using var provider=new FileLoggerProvider(folder); var logger=provider.CreateLogger("test");
            logger.LogWarning(new Exception("PASSWORD-secret-url"),"Transport failed");
            var text=File.ReadAllText(Directory.GetFiles(folder).Single()); Assert.Contains("Transport failed",text); Assert.DoesNotContain("PASSWORD",text);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact] public void InvalidConfigurationFailsBeforeNetworking()
    {
        Assert.Throws<DemoException>(() => new ServiceOptions {Url="http://internal/wms"}.Validate());
        Assert.Throws<DemoException>(() => new ServiceOptions {Crs="EPSG:2039"}.Validate());
        Assert.Throws<DemoException>(() => new ServiceOptions {Kind="WMTS",Version="1.0.0"}.Validate());
    }
    [Fact] public void ExcessFeaturesAndInheritedWrongCrsAreRejected()
    {
        var xml=OgcFixture.Collection("<gml:Point><gml:pos>10 20</gml:pos></gml:Point>");
        Assert.Throws<DemoException>(()=>GmlFeatures.Read(Encoding.UTF8.GetBytes(xml),0));
        xml=xml.Replace("<wfs:FeatureCollection ","<wfs:FeatureCollection srsName=\"EPSG:4326\" ");
        Assert.Throws<DemoException>(()=>GmlFeatures.Read(Encoding.UTF8.GetBytes(xml),10));
    }
    [Fact] public void XmlExternalEntitiesAreRejected()
    {
        var xml="<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///missing\">]><x>&e;</x>";
        Assert.Throws<DemoException>(()=>OgcTransport.ParseXml(Encoding.UTF8.GetBytes(xml)));
    }
    public static IEnumerable<object[]> Geometries()
    {
        var p="<gml:Point><gml:pos>10 20</gml:pos></gml:Point>";
        var l="<gml:LineString><gml:posList>0 0 10 10</gml:posList></gml:LineString>";
        var a="<gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>0 0 10 0 10 10 0 0</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon>";
        yield return [p,"Point"]; yield return [l,"LineString"]; yield return [a,"Polygon"];
        yield return [$"<gml:MultiPoint><gml:pointMember>{p}</gml:pointMember></gml:MultiPoint>","MultiPoint"];
        yield return [$"<gml:MultiCurve><gml:curveMember>{l}</gml:curveMember></gml:MultiCurve>","MultiLineString"];
        yield return [$"<gml:MultiSurface><gml:surfaceMember>{a}</gml:surfaceMember></gml:MultiSurface>","MultiPolygon"];
    }
    [Theory, MemberData(nameof(Geometries))]
    public void GmlGeometrySubset(string xml,string type)
    {
        foreach(var version in new[]{"1.1.0","2.0.0"})
        {
            var features=GmlFeatures.Read(Encoding.UTF8.GetBytes(OgcFixture.Collection(xml,version)),10);
            Assert.Single(features); Assert.Equal(type,features[0].Geometry!.GeometryType); Assert.Equal("Fixture site",features[0]["name"]);
        }
    }
    [Theory]
    [InlineData("<gml:Point srsName=\"EPSG:4326\"><gml:pos>10 20</gml:pos></gml:Point>")]
    [InlineData("<gml:Point srsDimension=\"3\"><gml:pos>10 20 30</gml:pos></gml:Point>")]
    [InlineData("<gml:Curve><gml:posList>0 0 10 10</gml:posList></gml:Curve>")]
    public void UnsupportedGmlFailsClearly(string xml) => Assert.Throws<DemoException>(()=>GmlFeatures.Read(Encoding.UTF8.GetBytes(OgcFixture.Collection(xml)),10));
}
