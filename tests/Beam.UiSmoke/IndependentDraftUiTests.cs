using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Threading;
using EngineeringDashboard;

internal static class IndependentDraftUiTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Independent drafting: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    static void Render(FrameworkElement element)
    {Pump();element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);}
    static void Click(BeamFormworkWorkspace view,string key)
    {view.Buttons[key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Render(view.Surface);}
    static IEnumerable<Tuple<GlyphRunDrawing,Matrix>> Glyphs(Drawing drawing,Matrix matrix)
    {
        if(drawing is DrawingGroup group)
        {
            if(group.Transform!=null)matrix=Matrix.Multiply(group.Transform.Value,matrix);
            foreach(var child in group.Children)foreach(var item in Glyphs(child,matrix))yield return item;
        }
        else if(drawing is GlyphRunDrawing glyph)yield return Tuple.Create(glyph,matrix);
    }
    static void Calculate(BeamFormworkWorkspace view)
    {
        var task=view.CalculateAsync();var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted){Pump();Thread.Sleep(1);if(clock.Elapsed.TotalSeconds>60)throw new Exception("Independent drawing calculation timeout");}task.GetAwaiter().GetResult();Render(view.Surface);
    }
    internal static void Run(BeamCalculatorWindow host,BeamFormworkWorkspace view,BeamCalculatorViewModel model)
    {
        var original=host.CaptureDrawingInputs();var drawing=view.Drawing;
        Click(view,"Geometry");drawing.Select(FormworkObject.Beam,0);drawing.Fit();drawing.ZoomAt(.5,new Point(drawing.ActualWidth/2,drawing.ActualHeight/2));Render(view.Surface);
        Check(host.FindName("IgnoreUpliftCheckBox")==null && model.IgnoreUplift,"uplift exclusion has no classic toggle");
        Check(view.BeginDrafting("CS"),"create similar starts from a main beam");
        var space=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(view.Surface),Environment.TickCount,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent};drawing.RaiseEvent(space);
        drawing.UpdateDraftPointer(drawing.Screen(-1000,300));Render(view.Surface);
        Check(space.Handled && drawing.DraftPreview.HasValue && drawing.DraftPreview.Value.Height>drawing.DraftPreview.Value.Width,"Space rotates the placement preview to a perpendicular beam");
        drawing.SelectAt(drawing.Screen(-1000,300));Check(drawing.IsDrafting && model.IndependentBeams.Count==0,"first click chooses the independent start without creating partial geometry");
        drawing.UpdateDraftPointer(drawing.Screen(-1000,2875));drawing.SelectAt(drawing.Screen(-1000,2875));Render(view.Surface);
        Check(model.IndependentBeams.Count==1 && !drawing.IsDrafting,"second endpoint creates one independent beam");
        var beam=model.IndependentBeams[0];
        Check(beam.IsVertical && beam.StartXmm==-1000 && beam.StartYmm==300 && beam.LengthMm==2575 && beam.EndYmm==2875 && model.BeamLengthMm==6750,"perpendicular beam keeps its own endpoints and length outside the main beam extents");
        var concrete=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Independent && h.Item3==0).Item1;Check(Math.Abs(concrete.Width-650*drawing.Zoom)<1e-7 && Math.Abs(concrete.Height-2575*drawing.Zoom)<1e-7,"perpendicular concrete retains its full width and independent length");
        Check(view.Fields.ContainsKey("LengthMm") && !view.Fields.ContainsKey("StartXmm") && !view.Fields.ContainsKey("StartYmm") && drawing.ObjectTargets.Any(h=>h.Item2==FormworkObject.Independent),"independent geometry has its own editable fields and selection target");
        string single=model.NetworkConfigurationXml;Click(view,"Undo");Check(model.IndependentBeams.Count==0,"one undo removes the complete endpoint placement");Click(view,"Redo");Check(model.NetworkConfigurationXml==single,"redo restores geometry and orientation exactly");
        drawing.Select(FormworkObject.Independent,0);Check(view.BeginDrafting("MV"),"independent beam can move");
        drawing.SelectAt(drawing.Screen(beam.StartXmm,beam.StartYmm));drawing.SelectAt(drawing.Screen(-1800,500));Render(view.Surface);
        Check(model.IndependentBeams[0].StartXmm==-1800 && model.IndependentBeams[0].StartYmm==500 && model.IndependentBeams[0].LengthMm==2575,"move uses both axes and never clamps to the parallel beam length");
        Click(view,"Undo");drawing.Select(FormworkObject.Independent,0);Check(view.BeginDrafting("CO"),"independent beam can copy");
        drawing.SelectAt(drawing.Screen(-1000,300));drawing.SelectAt(drawing.Screen(-2200,300));Render(view.Surface);
        Check(model.IndependentBeams.Count==2 && model.IndependentBeams[1].StartXmm==-2200 && model.IndependentBeams[1].LengthMm==2575 && model.IndependentBeams[0].StartXmm==-1000,"copy retains the source and its own length");Click(view,"Undo");
        drawing.Select(FormworkObject.Independent,0);Check(view.BeginDrafting("CS") && view.RotateDraft(),"Space rotation is available for similar independent beams");
        drawing.UpdateDraftPointer(drawing.Screen(-3000,200));Check(drawing.DraftPreview.Value.Width>drawing.DraftPreview.Value.Height,"a vertical source rotates back to horizontal");view.HandleGeometryKey(Key.Escape,ModifierKeys.None);
        Check(model.NetworkConfigurationXml==single && !drawing.IsDrafting,"Escape cancels independent placement without adding an undo edit");
        var saved=host.CaptureDrawingInputs();model.ClearIndependentBeams();host.RestoreCalculation(saved);Render(view.Surface);
        Check(model.IndependentBeams.Count==1 && model.IndependentBeams[0].StartXmm==-1000 && model.IndependentBeams[0].LengthMm==2575,"file restore preserves the independent drawing");
        model.TowerLegCapacityKn=70;Calculate(view);
        Check(model.IsCalculationValid && model.IndependentBeamResults.Length==1 && model.IndependentBeamResults[0].LengthMm==2575,"calculation uses the independent length");
        var first=drawing.Screen(drawing.SceneBounds.Left,drawing.SceneBounds.Top);var last=drawing.Screen(drawing.SceneBounds.Right,drawing.SceneBounds.Bottom);Check(first.X>=0 && first.Y>=0 && last.X<=drawing.ActualWidth && last.Y<=drawing.ActualHeight,"fit includes the entire drawing to the left of the parallel beams");
        Check(drawing.DrawnLegCount==model.NetworkResult.LegCount+model.IndependentBeamResults[0].LegCount,"formwork renders the additional checked legs");
        drawing.Select(FormworkObject.Independent,0);Render(view.Surface);Check(view.SelectedResultText.Contains("I1") && view.SelectedResultText.Contains("independent beam") && view.SelectedResultText.Contains("Slab loads remain"),"selected result states the independent load scope");
        var target=drawing.MemberTargets.First(h=>h.Item2==BeamFormworkCanvas.IndependentMemberId(0));view.ShowMember(target.Item2,target.Item3,target.Item4);Pump();
        Check(view.Diagrams.SelectedTransferMember!=null && view.Diagrams.LoadedAnalysis==view.Diagrams.SelectedTransferMember.Analysis,"independent members open their own exact checked response");view.Diagrams.Close();
        var glyphs=Glyphs(VisualTreeHelper.GetDrawing(drawing),Matrix.Identity).ToArray();
        Check(glyphs.Any(g=>new string(g.Item1.GlyphRun.Characters.ToArray()).StartsWith("Beam length 2575") && Math.Abs(g.Item2.M12)>.9),"vertical beam-length text runs along its dimension line");
        int horizontal=drawing.AnnotationLabels.FindIndex(t=>t.StartsWith("B1 beam length 6750"));
        double lineY=drawing.Pan.Y+drawing.SceneBounds.Bottom*drawing.Zoom+42;Check(horizontal>=0 && drawing.AnnotationBounds[horizontal].Bottom<lineY,"horizontal beam-length text is above its dimension line");
        Click(view,"Geometry");drawing.Select(FormworkObject.Independent,0);view.RemoveSelected();Render(view.Surface);Check(model.IndependentBeams.Count==0 && model.NetworkBeams.Count==2,"independent deletion preserves connected geometry");Click(view,"Undo");Check(model.IndependentBeams.Count==1,"independent deletion has one undo entry");
        host.RestoreCalculation(original);drawing.Select(FormworkObject.Beam,0);Render(view.Surface);
        Console.WriteLine($"Passed {checks} independent drafting UI checks.");
    }
}
