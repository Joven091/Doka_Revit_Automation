using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class WorkspaceTests
{
    static int checks;
    static BeamFormworkWorkspace workspace;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception("Workspace: "+label);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    static void Click(string key){workspace.Buttons[key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    static void Render(string path=null,int width=1280,int height=860)
    {
        Pump();var root=(FrameworkElement)workspace.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        if(path!=null){var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,width,height));bitmap.Render(backdrop);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
    static void Calculate()
    {
        var task=workspace.CalculateAsync();var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted){Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>45)throw new Exception("Workspace calculation timeout");}
        task.GetAwaiter().GetResult();Pump();Render();
    }
    static void Enter(string key,string value)
    {workspace.Fields[key].Text=value;Check(workspace.CommitFields(),"valid field commits: "+key);Pump();Render();}
    static Point Centre(Rect r)=>new Point(r.Left+r.Width/2,r.Top+r.Height/2);
    static IEnumerable<T> Controls<T>(DependencyObject root) where T:DependencyObject
    {foreach(object item in LogicalTreeHelper.GetChildren(root))if(item is DependencyObject child){if(child is T match)yield return match;foreach(var value in Controls<T>(child))yield return value;}}
    internal static int Run(BeamCalculatorWindow parent,BeamCalculatorViewModel model,CalculationRecord customSetup,string output)
    {
        try
        {
            var opener=(Button)parent.FindName("DrawingWorkspaceButton");opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();workspace=parent.FormworkWorkspace;
            Check(workspace!=null && ReferenceEquals(workspace.Model,model) && workspace.WindowState==WindowState.Maximized,"opens a maximized workspace with shared state");
            opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(ReferenceEquals(workspace,parent.FormworkWorkspace),"opening again reuses the window");
            workspace.WindowState=WindowState.Normal;workspace.Width=1280;workspace.Height=860;Render();
            var originalResult=model.TowerResult;Click("Diagnose");var diagnosis=workspace.Diagnostics;
            Check(diagnosis!=null && diagnosis.IsVisible && diagnosis.ReportBox.IsReadOnly && diagnosis.ReportBox.Text.Contains("BEAM FORMWORK DIAGNOSTIC REPORT v1"),"diagnose opens copyable report window");
            Check(ReferenceEquals(model.TowerResult,originalResult),"opening diagnosis retains calculated result");
            diagnosis.RemarksBox.Text="Slow search after custom H20 checkbox";Click("Diagnose");
            Check(ReferenceEquals(diagnosis,workspace.Diagnostics) && diagnosis.ReportBox.Text.Contains("Slow search after custom H20 checkbox"),"repeat diagnose reuses window and retains remarks");
            string copied=null;var copyWindow=new BeamDiagnosticWindow(remarks=>model.BuildDiagnosticReport(remarks),text=>copied=text);
            copyWindow.RemarksBox.Text="Copy my remarks";copyWindow.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(copied==copyWindow.ReportBox.Text && copied.Contains("Copy my remarks") && copied.Contains("END BEAM FORMWORK"),"copy refreshes the complete report and remarks");copyWindow.Close();
            var drawing=workspace.Drawing;drawing.Fit();Render();
            Check(drawing.ObjectTargets.Count(t=>t.Item2==FormworkObject.Beam)==3 && drawing.ObjectTargets.Count(t=>t.Item2==FormworkObject.Slab)==2,"geometry is visible before formwork selection");
            var top=drawing.Screen(drawing.SceneBounds.Left,drawing.SceneBounds.Top);var bottom=drawing.Screen(drawing.SceneBounds.Right,drawing.SceneBounds.Bottom);
            Check(top.X>=0 && top.Y>=0 && bottom.X<=drawing.ActualWidth && bottom.Y<=drawing.ActualHeight,"fit includes the complete metric plan");
            var anchor=new Point(200,180);var world=drawing.World(anchor);double zoom=drawing.Zoom;drawing.ZoomAt(1.25,anchor);
            Check(Math.Abs(drawing.Zoom/zoom-1.25)<1e-9 && (drawing.World(anchor)-world).Length<1e-7,"zoom preserves the point under the cursor");
            var oldPan=drawing.Pan;drawing.PanBy(new Vector(32,-18));Check((drawing.Pan-oldPan-new Vector(32,-18)).Length<1e-8,"pan moves the view without changing dimensions");drawing.Fit();Render();
            Click("Formwork");Render(output==null ? null : Path.ChangeExtension(output,"workspace-overview.png"));
            Check(drawing.DrawnLegCount==model.NetworkResult.LegCount && drawing.MemberTargets.Count>0,"formwork overlays all calculated legs and primary pairs");
            Check(drawing.LegTargets.Count==drawing.DrawnLegCount && drawing.LegTargets.All(t=>t.Item2.Contains("/70 kN")),"every visible leg has load and permissible-capacity inspection");
            var hit=drawing.MemberTargets.First();Check(drawing.SelectAt(Centre(hit.Item1)),"primary H20 is selected directly from plan");Pump();Render();
            Check(workspace.Diagrams!=null && workspace.Diagrams.SelectedTransferMember.H20Count==2,"primary pair diagrams are embedded in workspace");
            Check(Math.Abs(workspace.Diagrams.LoadedAnalysis.TotalLoadKn-model.NetworkResult.Beams[hit.Item2].Transfer.PrimaryCases.Last()[hit.Item3].Analysis.TotalLoadKn)<1e-8,"plan selection displays actual member load");
            Click("Geometry");Render();var beam=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Beam && h.Item3==1);
            Check(drawing.SelectAt(Centre(beam.Item1)) && drawing.SelectedIndex==1 && workspace.Fields["DepthMm"].Text=="1200","click selects actual beam and editable depth");
            Enter("DepthMm","1325");Check(model.NetworkBeams[1].DepthMm==1325 && !model.IsCalculationValid && workspace.Diagrams==null && !workspace.Buttons["Export"].IsEnabled,"editing invalidates shared result and diagrams");
            Check(parent.CaptureDrawingInputs().Inputs.Any(f=>f.Name=="NetworkConfigurationXml") && workspace.Buttons["Save"].IsEnabled,"draft geometry can be saved before recalculation");
            Click("Undo");Check(model.NetworkBeams[1].DepthMm==1200,"undo restores beam depth");Click("Redo");Check(model.NetworkBeams[1].DepthMm==1325,"redo restores edited depth");Click("Undo");
            drawing.Select(FormworkObject.Beam,1);drawing.MoveSelection(42);Pump();Render();Check(model.NetworkBays[0].WidthMm==3950,"moving a beam edits its adjacent span in 25 mm steps");Click("Undo");Check(model.NetworkBays[0].WidthMm==3900,"undo restores dragged span");
            drawing.Select(FormworkObject.Slab,0);Render();var spanTarget=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Slab && h.Item3==0);
            Check(drawing.SelectAt(new Point(spanTarget.Item1.Right+40,spanTarget.Item1.Top+spanTarget.Item1.Height/2)) && workspace.Fields["WidthMm"].SelectedText=="3900","clicking a span dimension focuses its exact numeric input");
            Enter("WidthMm","3850");Check(model.NetworkBays[0].WidthMm==3850,"slab span edits target selected bay");Click("Undo");
            drawing.Select(FormworkObject.Beam,0);Click("AddCrossing");Render();var slab=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Slab && h.Item3==0);
            var place=drawing.Screen(3178,drawing.World(Centre(slab.Item1)).Y);Check(drawing.SelectAt(place),"crossing beam placed by clicking slab");Pump();Render();
            Check(model.CrossingBeams.Count==1 && model.CrossingBeams[0].BayNumber==1 && model.CrossingBeams[0].PositionMm==3175,"placement snaps position and attaches load to clicked bay");
            drawing.MoveSelection(42);Pump();Check(model.CrossingBeams[0].PositionMm==3225,"crossing beam movement snaps to 25 mm");Click("Undo");
            drawing.Select(FormworkObject.Beam,0);Click("AddAbove");
            Check(model.NetworkBeams.Count==4 && model.NetworkBays.Count==3 && model.CrossingBeams[0].BayNumber==2,"inserting beam/slab reindexes crossing load with existing bay");
            Click("Delete");Check(model.NetworkBeams.Count==3 && model.CrossingBeams[0].BayNumber==1,"removing first beam/slab preserves remaining crossing attachment");
            drawing.Select(FormworkObject.Crossing,0);Click("Delete");Check(model.CrossingBeams.Count==0,"selected crossing beam can be removed");
            Click("Undo");Check(model.CrossingBeams.Count==1,"undo restores crossing geometry");Click("Redo");Check(model.CrossingBeams.Count==0,"redo removes crossing geometry");
            var customH20=Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Allow custom H20 lengths (25 mm)"));
            Check(customH20.IsChecked==false,"custom H20 checkbox defaults off");customH20.IsChecked=true;customH20.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(model.AllowCustomH20Lengths && !model.IsCalculationValid,"workspace custom toggle updates shared model and invalidates result");
            Click("Undo");Check(!model.AllowCustomH20Lengths,"undo restores custom setting");Click("Redo");Check(model.AllowCustomH20Lengths,"redo restores custom setting");
            var customLengthsRecord=parent.CaptureDrawingInputs();Check(customLengthsRecord.Inputs.Single(f=>f.Name=="AllowCustomH20Lengths").Value=="True","custom setting is saved with drawing inputs");
            model.AllowCustomH20Lengths=false;parent.RestoreCalculation(customLengthsRecord);Pump();Check(model.AllowCustomH20Lengths,"custom setting loads from calculation file");
            Check(((CheckBox)parent.FindName("CustomH20LengthsCheckBox")).IsChecked==true,"calculator checkbox synchronizes with layout window");
            customLengthsRecord.Inputs.RemoveAll(f=>f.Name=="AllowCustomH20Lengths");parent.RestoreCalculation(customLengthsRecord);Pump();
            Check(!model.AllowCustomH20Lengths && Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Allow custom H20 lengths (25 mm)")).IsChecked==false,"legacy files restore disabled custom mode in both windows");
            var liveSearch=workspace.CalculateAsync();workspace.Buttons["Diagnose"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(workspace.Diagnostics.ReportBox.Text.Contains("Search running: True") && workspace.Buttons["Diagnose"].IsEnabled,"diagnose remains available during an actual search");
            var searchTimer=System.Diagnostics.Stopwatch.StartNew();while(!liveSearch.IsCompleted){Pump();Thread.Sleep(1);if(searchTimer.Elapsed.TotalSeconds>45)throw new Exception("Workspace diagnostic search timeout");}
            liveSearch.GetAwaiter().GetResult();Pump();Render();workspace.Diagnostics.RefreshReport();
            Check(workspace.Diagnostics.ReportBox.Text.Contains("Search outcome: Succeeded") && workspace.Diagnostics.ReportBox.Text.Contains("Receiving-beam solves:"),"refresh captures completed search counters");
            Check(model.IsCalculationValid && model.NetworkResult.Beams.Length==3,"edited geometry calculates with existing optimizer");

            Click("Section");Render();Check(workspace.Section.DrawnSecondaryLengthCount==3 && workspace.Section.DrawnTowerLegCount==0,"section is separate from plan and retains lower secondary lengths");
            double sectionZoom=workspace.SectionViewport.Scale;Click("ZoomIn");Check(workspace.SectionViewport.Scale>sectionZoom,"zoom controls operate on active section");workspace.SectionViewport.PanBy(new Vector(20,10));Click("Fit");Render();
            var sectionHit=workspace.Section.MemberTargets.First(h=>h.Item3>=model.NetworkResult.Beams[h.Item2].StockPlan.Pieces.Length+model.NetworkResult.Beams[h.Item2].Transfer.RowsMm.Length);
            Check(workspace.Section.SelectAt(Centre(sectionHit.Item1)),"slab secondary selected from section");Pump();Check(workspace.Diagrams.SelectedTransferMember.H20Count==1,"section selection uses single slab H20 diagrams");
            var record=parent.CaptureCalculation();parent.RestoreCalculation(record);Pump();Check(model.NetworkBeams[1].DepthMm==1200 && model.NetworkBays[0].WidthMm==3900,"drawing inputs round-trip through existing calculation files");
            parent.RestoreCalculation(customSetup);Pump();Render();Enter("UpperSupportSpacingMm","750");Calculate();
            Check(!model.IsCalculationValid && workspace.FailureDetails.Contains("Lower secondary") && workspace.FailureTargets.Any(),"failed calculation exposes member demands and drawing targets");
            Check(parent.CaptureDrawingInputs().Inputs.Any(f=>f.Name=="BeamLengthMm" && f.Value=="10600"),"no-solution geometry can be saved independently of acceptance");
            var failureTarget=workspace.FailureTargets.First();failureTarget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Render();Check(drawing.SelectedKind==FormworkObject.Beam && drawing.SelectedIndex==0,"failure selects affected beam without displaying a failed tower layout");
            Enter("UpperSupportSpacingMm","200");Calculate();Check(model.IsCalculationValid && drawing.DrawnLegCount==36 && model.NetworkResult.Bays[0].StockLengthMm==2175,"user setup calculates and fits all 36 legs with custom slab member");
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-custom.png"));
            Click("Section");Render(output==null ? null : Path.ChangeExtension(output,"workspace-section.png"));
            var customHit=workspace.Section.MemberTargets.Last();workspace.Section.SelectAt(Centre(customHit.Item1));Pump();
            Check(workspace.Diagrams.SelectedTransferMember.LengthMm==2175 && workspace.Diagrams.SelectedTransferMember.H20Count==1,"custom member is selectable in dedicated section");
            Click("Geometry");drawing.Select(FormworkObject.Beam,0);workspace.Fields["DepthMm"].Text="bad";Pump();Render();
            Check(!workspace.Buttons["Calculate"].IsEnabled && !model.CanOptimizeTowers && !parent.TryGetValidCalculation(out var ignored),"workspace conversion errors also block calculator and export guards");
            var exclusion=Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Exclude standalone 600 mm towers"));exclusion.IsChecked=false;exclusion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(exclusion.IsChecked==model.Exclude600Standalone && model.Exclude600Standalone,"invalid text cannot leave a tower-exclusion checkbox inconsistent with solver");
            var criterion=Controls<ComboBox>(workspace).Single(c=>c.SelectedItem is BeamDeflectionOption);var previousCriterion=model.DeflectionLimit;criterion.SelectedItem=model.DeflectionOptions.Last();Pump();
            Check(criterion.SelectedItem==previousCriterion && model.DeflectionLimit==previousCriterion,"invalid text cannot leave displayed deflection criterion inconsistent with solver");
            var next=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Beam && h.Item3==1);Check(!drawing.SelectAt(Centre(next.Item1)) && workspace.Fields["DepthMm"].Text=="bad","selection cannot discard invalid text");
            Enter("DepthMm","1750");workspace.Fields["StockLengthsText"].Text="bad";Pump();Check(!workspace.CommitFields() && !workspace.Buttons["Calculate"].IsEnabled,"invalid stocks cannot leave a stale result");
            Click("Diagnose");Check(workspace.Diagnostics.ReportBox.Text.Contains("StockLengthsText = bad") && workspace.Diagnostics.ReportBox.Text.Contains("text input errors: True"),"diagnose captures invalid pending stock text without committing it");
            workspace.Fields["StockLengthsText"].Text=model.StockLengthsText;Check(workspace.CommitFields() && model.CanOptimizeTowers,"corrected stock input releases shared validation");
            model.Exclude600Standalone=false;Pump();Check(Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Exclude standalone 600 mm towers")).IsChecked==false,"parent-window edits synchronize categorical settings");model.Exclude600Standalone=true;Pump();
            var calculatorDiagnose=(Button)parent.FindName("DiagnoseBeamButton");calculatorDiagnose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(parent.Diagnostics!=null && parent.Diagnostics.ReportBox.Text.Contains("Source: Beam calculator"),"calculator provides the same diagnostic report");parent.Diagnostics.Close();
            var closingDiagnosis=workspace.Diagnostics;
            var xml=model.NetworkConfigurationXml;workspace.Close();Pump();Check(parent.FormworkWorkspace==null && model.CanOptimizeTowers && model.NetworkConfigurationXml==xml,"closing releases validation and retains geometry");Check(!closingDiagnosis.IsVisible,"closing workspace closes its diagnosis window");workspace=null;
            Console.WriteLine($"Passed {checks} drawing workspace checks.");return checks;
        }
        finally {workspace?.Close();}
    }
}


