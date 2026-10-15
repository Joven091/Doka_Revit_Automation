using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class GeometryEditingUiTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Geometry editing: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    static IEnumerable<Drawing> Primitives(Drawing drawing)
    {
        if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var item in Primitives(child))yield return item;
        else yield return drawing;
    }
    static string[] Text(FrameworkElement element)=>Primitives(VisualTreeHelper.GetDrawing(element)).OfType<GlyphRunDrawing>().Select(g=>new string(g.GlyphRun.Characters.ToArray())).ToArray();
    static void Render(FrameworkElement element)
    {Pump();element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);}
    static void Click(BeamFormworkWorkspace view,string key)
    {view.Buttons[key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Render(view.Surface);}
    static Point Centre(Rect rect)=>new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2);
    static void Action(ContextMenu menu,string header)
    {var item=menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,header));Check(item.IsEnabled,"menu action enabled: "+header);item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;Pump();}
    internal static void Run(BeamCalculatorWindow host,BeamFormworkWorkspace view,BeamCalculatorViewModel model,string output)
    {
        var original=host.CaptureDrawingInputs();var drawing=view.Drawing;
        Click(view,"Formwork");drawing.Select(FormworkObject.Beam,0);drawing.Fit();Render(view.Surface);
        var layout=model.NetworkResult.Beams[0];
        var red=Primitives(VisualTreeHelper.GetDrawing(drawing)).OfType<GeometryDrawing>().Where(g=>g.Pen?.Brush is SolidColorBrush b && b.Color==Colors.Firebrick).Select(g=>g.Geometry).OfType<LineGeometry>().ToArray();
        Check(red.Any(l=>Math.Abs(l.StartPoint.X-l.EndPoint.X)<1e-7 && Math.Abs(Math.Abs(l.StartPoint.Y-l.EndPoint.Y)-layout.WidthMm*drawing.Zoom)<1e-7 && layout.PositionsMm.Any(x=>Math.Abs(l.StartPoint.X-(drawing.Screen(x,0).X-18))<1e-7)),"red frame dimension is beside an actual frame station");
        Check(Primitives(VisualTreeHelper.GetDrawing(drawing)).OfType<GlyphRunDrawing>().Any(g=>new string(g.GlyphRun.Characters.ToArray())==layout.WidthMm.ToString("0.###") && g.ForegroundBrush is SolidColorBrush b && b.Color==Colors.Firebrick),"frame dimension label is red");
        Click(view,"Section");Render(view.Section);
        var labels=Text(view.Section);
        Check(!labels.Any(t=>t.Contains("Section ·") || t.Contains("red:") || t.StartsWith("Edge ")),"section omits the explanatory header and Edge text");
        Check(labels.Contains("S1 "+model.NetworkBays[0].WidthMm.ToString("0.###")+" · t "+model.NetworkBays[0].ThicknessMm.ToString("0.###")),"section renders slab span and thickness in one external dimension label");
        Check(labels.Any(t=>t.StartsWith("B1 · D "+model.NetworkBeams[0].DepthMm.ToString("0.###"))),"section renders beam depth in its external callout");
        Click(view,"Geometry");drawing.Select(FormworkObject.Beam,1);Render(view.Surface);
        model.NetworkBeams[1].WidthMm=725;model.NetworkBeams[1].DepthMm=1375;model.NetworkBeams[1].BottomElevationMm=-1200;Pump();Render(view.Surface);
        var beforeCopy=model.NetworkConfigurationXml;
        var point=Centre(drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Beam && h.Item3==1).Item1);
        Check(drawing.OpenGeometryContextAt(point) && view.GeometryMenu.IsOpen,"geometry right-click opens the context menu");
        var menu=view.GeometryMenu;
        Check(new[] {"Insert bay above","Insert bay below","Insert cross beam","Copy","Paste"}.All(h=>menu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,h))),"geometry menu contains all requested actions");
        Action(menu,"Copy");
        drawing.Select(FormworkObject.Beam,0);Render(view.Surface);Check(drawing.OpenGeometryContextAt(Centre(drawing.ObjectTargets.First(h=>h.Item2==FormworkObject.Beam && h.Item3==0).Item1)),"context menu opens on paste destination");
        var paste=view.GeometryMenu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"Paste"));
        ((MenuItem)paste.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));view.GeometryMenu.IsOpen=false;Pump();Render(view.Surface);
        Check(model.NetworkBeams.Count==3 && model.NetworkBeams[1].WidthMm==725 && model.NetworkBeams[1].DepthMm==1375 && model.NetworkBeams[1].BottomElevationMm==-1200,"paste duplicates the copied beam's dimensions and level below the target");
        Click(view,"Undo");Check(model.NetworkConfigurationXml==beforeCopy,"one undo removes the pasted beam and bay together");
        Click(view,"Redo");Check(model.NetworkBeams.Count==3,"redo restores pasted geometry");Click(view,"Undo");
        drawing.Select(FormworkObject.Beam,1);drawing.Focus();
        foreach(var letter in new[] {Key.M,Key.V}) {var alias=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(view.Surface),Environment.TickCount,letter){RoutedEvent=Keyboard.PreviewKeyDownEvent};drawing.RaiseEvent(alias);Check(alias.Handled,"MV alias is routed from drawing focus");}
        Check(drawing.IsDrafting && drawing.DraftCommand=="MV","MV starts point-based move");
        var ys=drawing.Centres();var baseScreen=drawing.Screen(model.BeamLengthMm/2,ys[1]);var targetScreen=drawing.Screen(model.BeamLengthMm/2,ys[1]+400);var target=drawing.SnapAt(targetScreen);
        Check(drawing.SnapAt(baseScreen)==new Point(model.BeamLengthMm/2,ys[1]) && drawing.SnapName=="Midpoint","object snap acquires the exact beam midpoint");
        drawing.SelectAt(baseScreen);drawing.UpdateDraftPointer(targetScreen);Render(view.Surface);
        Check(drawing.DraftPreview.HasValue && Math.Abs(drawing.DraftPreview.Value.Top+drawing.DraftPreview.Value.Height/2-target.Y)<1e-8,"move previews the snapped destination before changing geometry");
        Check(model.NetworkConfigurationXml==beforeCopy,"preview does not mutate geometry");
        drawing.SelectAt(targetScreen);Render(view.Surface);
        Check(!drawing.IsDrafting && drawing.Centres()[0]==ys[0] && Math.Abs(drawing.Centres()[1]-target.Y)<1e-8,"move applies its snapped destination while retaining the other beam");
        Click(view,"Undo");Check(model.NetworkConfigurationXml==beforeCopy,"move has one undo entry");
        drawing.Select(FormworkObject.Beam,1);drawing.Focus();
        foreach(var letter in new[] {Key.C,Key.O}) {var alias=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(view.Surface),Environment.TickCount,letter){RoutedEvent=Keyboard.PreviewKeyDownEvent};drawing.RaiseEvent(alias);}
        Check(drawing.DraftCommand=="CO","CO alias starts copy with a base point");
        ys=drawing.Centres();baseScreen=drawing.Screen(model.BeamLengthMm/2,ys[1]);targetScreen=drawing.Screen(model.BeamLengthMm/2,ys[1]+2000);target=drawing.SnapAt(targetScreen);
        drawing.SelectAt(baseScreen);drawing.UpdateDraftPointer(targetScreen);drawing.SelectAt(targetScreen);Render(view.Surface);
        Check(model.NetworkBeams.Count==3 && model.NetworkBeams[2].DepthMm==1375 && Math.Abs(drawing.Centres()[2]-target.Y)<1e-8 && drawing.Centres()[1]==ys[1],"CO creates a matching beam at the picked location without moving the source");
        Click(view,"Undo");drawing.Select(FormworkObject.Beam,1);drawing.Focus();
        foreach(var letter in new[] {Key.C,Key.S}) {var alias=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(view.Surface),Environment.TickCount,letter){RoutedEvent=Keyboard.PreviewKeyDownEvent};drawing.RaiseEvent(alias);}
        Check(drawing.DraftCommand=="CS","CS alias starts create similar");
        ys=drawing.Centres();drawing.SelectAt(drawing.Screen(model.BeamLengthMm/2,(ys[0]+ys[1])/2));Render(view.Surface);
        Check(model.NetworkBeams.Count==3 && model.NetworkBeams[1].WidthMm==725 && model.NetworkBeams[1].DepthMm==1375 && drawing.Centres()[2]==ys[1],"CS places a matching beam inside a connected bay and preserves existing beam positions");
        Click(view,"Undo");drawing.Select(FormworkObject.Beam,1);view.BeginDrafting("CO");drawing.SelectAt(drawing.Screen(0,drawing.Centres()[1]));view.HandleGeometryKey(Key.Escape,ModifierKeys.None);
        Check(!drawing.IsDrafting && !drawing.DraftPreview.HasValue && drawing.SelectedObjects.Length==0 && model.NetworkConfigurationXml==beforeCopy,"Escape cancels drafting and clears selection without a geometry edit");
        Check(!view.HandleGeometryKey(Key.M,ModifierKeys.None,true),"command aliases leave text editing alone");
        point=Centre(drawing.ObjectTargets.First(h=>h.Item2==FormworkObject.Slab).Item1);drawing.OpenGeometryContextAt(point);Action(view.GeometryMenu,"Insert cross beam");Render(view.Surface);
        Check(model.CrossingBeams.Count==1 && model.CrossingBeams[0].BayNumber==1,"right-click in a bay inserts the cross beam in that bay");
        drawing.Select(FormworkObject.Crossing,0);Check(view.CopySelectedGeometry(),"cross beam can be copied");
        Check(view.PasteGeometry(false,Tuple.Create(0,2500.0)) && model.CrossingBeams.Count==2 && model.CrossingBeams[1].PositionMm==2500,"cross beam paste places a copy at the destination");
        Click(view,"Undo");drawing.Select(FormworkObject.Crossing,0);view.BeginDrafting("MV");
        ys=drawing.Centres();double midBay=(ys[0]+model.NetworkBeams[0].WidthMm/2+ys[1]-model.NetworkBeams[1].WidthMm/2)/2;
        double sourceX=model.CrossingBeams[0].PositionMm;baseScreen=drawing.Screen(sourceX,midBay);targetScreen=drawing.Screen(sourceX+400,midBay);target=drawing.SnapAt(targetScreen);
        drawing.SelectAt(baseScreen);drawing.SelectAt(targetScreen);Render(view.Surface);
        Check(model.CrossingBeams.Count==1 && model.CrossingBeams[0].PositionMm==target.X,"MV moves the selected crossing to its snapped point");Click(view,"Undo");
        drawing.Select(FormworkObject.Crossing,0);view.BeginDrafting("CO");baseScreen=drawing.Screen(sourceX,midBay);targetScreen=drawing.Screen(sourceX+900,midBay);target=drawing.SnapAt(targetScreen);
        drawing.SelectAt(baseScreen);drawing.SelectAt(targetScreen);Render(view.Surface);
        Check(model.CrossingBeams.Count==2 && model.CrossingBeams[0].PositionMm==sourceX && model.CrossingBeams[1].PositionMm==target.X,"CO copies a crossing with base-point placement");Click(view,"Undo");
        drawing.Select(FormworkObject.Crossing,0);view.BeginDrafting("CS");drawing.SelectAt(drawing.Screen(1875,midBay));Render(view.Surface);
        Check(model.CrossingBeams.Count==2 && model.CrossingBeams[1].PositionMm==1875 && model.CrossingBeams[1].SupportMode==model.CrossingBeams[0].SupportMode,"CS retains a crossing's support mode at the picked point");Click(view,"Undo");
        Click(view,"Undo");Check(model.NetworkConfigurationXml==beforeCopy,"undo restores the drawing before crossing placement and paste");
        drawing.SetSelection(new[] {Tuple.Create(FormworkObject.Beam,0),Tuple.Create(FormworkObject.Slab,0)},FormworkObject.Beam,0);Render(view.Surface);
        Check(view.Buttons["Delete"].IsEnabled,"beam and slab selection in a two-beam drawing can be deleted");
        drawing.Focus();var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(view.Surface),Environment.TickCount,Key.Delete){RoutedEvent=Keyboard.PreviewKeyDownEvent};drawing.RaiseEvent(key);Pump();Render(view.Surface);
        Check(key.Handled && model.NetworkBeams.Count==1 && model.NetworkBays.Count==0 && !model.IsNetworkMode && model.Width==725 && model.Depth==1375,"Delete key leaves the chosen survivor in beam-only mode");
        Check(drawing.ObjectTargets.Count(h=>h.Item2==FormworkObject.Beam)==1 && !drawing.ObjectTargets.Any(h=>h.Item2==FormworkObject.Slab),"standalone drawing has one beam and no slabs");
        var single=host.CaptureDrawingInputs();Click(view,"Undo");Check(model.IsNetworkMode && model.NetworkBeams.Count==2,"undo restores the two-beam network");
        host.RestoreCalculation(single);Pump();Render(view.Surface);
        Check(model.NetworkBeams.Count==1 && !model.IsNetworkMode && model.Width==725 && model.Depth==1375 && model.NetworkBeams[0].BottomElevationMm==-1200,"file restoration replaces an existing network with the standalone beam and its level");
        drawing.Select(FormworkObject.Beam,0);Check(!view.Buttons["Delete"].IsEnabled,"last beam remains protected");
        var task=view.CalculateAsync();var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted){Pump();Thread.Sleep(1);if(clock.Elapsed.TotalSeconds>45)throw new Exception("Standalone drawing calculation timeout");}task.GetAwaiter().GetResult();Render(view.Surface);
        Check(model.IsCalculationValid && model.TowerResult!=null && model.NetworkResult==null,"standalone drawing calculates with the single-beam solver");
        Click(view,"Geometry");drawing.Select(FormworkObject.Beam,0);Check(view.CopySelectedGeometry() && view.PasteGeometry(true),"a standalone beam can be copied into a connected two-beam layout");
        Check(model.IsNetworkMode && model.NetworkBeams.Count==2 && model.NetworkBays.Count==1 && model.NetworkBeams.All(b=>b.WidthMm==725 && b.DepthMm==1375 && b.BottomElevationMm==-1200),"paste from standalone preserves both beam profiles and levels");
        Click(view,"Undo");Check(model.NetworkBeams.Count==1 && !model.IsNetworkMode,"one undo returns a standalone paste to beam-only mode");
        host.RestoreCalculation(original);drawing.Select(FormworkObject.Beam,0);Pump();Render(view.Surface);
        IndependentDraftUiTests.Run(host,view,model);
        Console.WriteLine($"Passed {checks} geometry editing UI checks.");
    }
}
