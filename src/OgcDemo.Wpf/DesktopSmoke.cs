using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using OgcDemo.Core;

namespace OgcDemo.Wpf;

/// <summary>Opt-in in-process Windows integration check; does not simulate physical mouse/keyboard input.</summary>
internal static class DesktopSmoke
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm, string reportPath)
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        var success = false; string? error = null;
        try
        {
            await Task.Delay(1500); window.UpdateLayout();
            Check(window.IsVisible, "WPF window is visible");
            Check(vm.Layers.Count == 2, "Bundled graphics and GeoJSON layers loaded");
            Check(vm.Map.Navigator.Viewport.Width > 0, "Map viewport initialized");
            var graphics = (MemoryLayer)vm.Layers.Last().Layer;
            var point = graphics.Features.First();
            var position = vm.Map.Navigator.Viewport.WorldToScreen(point.Extent!.Centroid);
            var hit = window.MapView.GetMapInfo(position, [graphics]);
            Check(hit.Feature != null, "Rendered point hit test finds a feature");
            var line = (Mapsui.Nts.GeometryFeature)graphics.Features.Skip(1).First();
            var coords = line.Geometry!.Coordinates;
            var linePosition = vm.Map.Navigator.Viewport.WorldToScreen(new MPoint((coords[0].X+coords[1].X)/2,(coords[0].Y+coords[1].Y)/2));
            Check(window.MapView.GetMapInfo(linePosition,[graphics]).Feature != null,"Rendered line is pickable between vertices");
            var polygon = (Mapsui.Nts.GeometryFeature)graphics.Features.Last();
            var polygonPosition = vm.Map.Navigator.Viewport.WorldToScreen(polygon.Extent!.Centroid);
            Check(window.MapView.GetMapInfo(polygonPosition,[graphics]).Feature != null,"Rendered polygon is pickable inside its fill");
            await vm.SelectAsync(hit.Feature,position.X,position.Y);
            Check(vm.Details.Contains("Synthetic observation"), "Map selection updates feature details");
            Check(((MemoryLayer)vm.Map.Layers.Last()).Features.Any(), "Selection highlight is populated");
            vm.ClearSelectionCommand.Execute(null);
            Check(!((MemoryLayer)vm.Map.Layers.Last()).Features.Any(), "Selection highlight clears");
            vm.SelectedLayer = vm.Layers[0]; vm.SelectedLayer.Enabled=false;
            Check(!vm.SelectedLayer.Layer.Enabled,"Visibility binds to map layer");
            vm.SelectedLayer.Enabled=true; vm.SelectedLayer.Opacity=0.4;
            Check(Math.Abs(vm.SelectedLayer.Layer.Opacity-0.4)<0.001,"Opacity binds to map layer"); vm.SelectedLayer.Opacity=1;
            var selected=vm.SelectedLayer; vm.MoveDownCommand.Execute(null);
            Check(vm.Layers.Last()==selected && vm.Map.Layers.First()==selected.Layer,"Tree order matches map drawing order");
            vm.MoveUpCommand.Execute(null);
            vm.AddGraphicsCommand.Execute(null); Check(vm.Layers.Count==3,"Dynamic graphics addition works");
            vm.SelectedLayer=vm.Layers[0]; vm.RemoveLayerCommand.Execute(null); Check(vm.Layers.Count==2,"Dynamic removal works");
            vm.HomeCommand.Execute(null); vm.SelectedLayer=vm.Layers.Last();
            await vm.SelectAsync(point,position.X,position.Y);
            await Task.Delay(500); window.UpdateLayout();
            vm.Status="Local vector selection verified. Synthetic training data only.";
            var surface=(FrameworkElement)window.Content; surface.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)surface.ActualWidth,(int)surface.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(surface); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            using (var stream=File.Create(Path.ChangeExtension(reportPath,".png"))) encoder.Save(stream);
            vm.SelectedService=new ServiceOptions {Url="https://unavailable.example.invalid/wms",TimeoutSeconds=1};
            await vm.ConnectCommand.ExecuteAsync(null);
            Check(vm.Status.StartsWith("Connection failed:"),"Unavailable service is reported without closing the application");
            Check(window.IsVisible && vm.Layers.Count==2,"Local layers remain usable after service failure");
            success=true;
        }
        catch(Exception e) { error=e.GetType().Name+": "+e.Message; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath,JsonSerializer.Serialize(new {success,checks,error,coverage="In-process WPF integration; not physical-input automation",utc=DateTimeOffset.UtcNow},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(success?0:1);
    }
}
