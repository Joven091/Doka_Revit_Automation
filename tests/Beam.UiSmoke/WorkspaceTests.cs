using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    static void Click(string key){var control=workspace.Buttons[key];control.RaiseEvent(new RoutedEventArgs(control is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));Pump();}
    static void Render(string path=null,int width=1280,int height=860)
    {
        Pump();var root=workspace.Surface;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        if(path!=null){int imageWidth=(int)Math.Ceiling(root.ActualWidth),imageHeight=(int)Math.Ceiling(root.ActualHeight);var bitmap=new RenderTargetBitmap(imageWidth,imageHeight,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,imageWidth,imageHeight));bitmap.Render(backdrop);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
    static void RenderDiagrams(string path=null)
    {
        Pump();var content=(FrameworkElement)((ScrollViewer)workspace.Diagrams.Content).Content;
        content.Measure(new Size(900,double.PositiveInfinity));content.Arrange(new Rect(0,0,900,content.DesiredSize.Height));content.UpdateLayout();
        if(path!=null){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
    static bool NoOverlaps(IEnumerable<Rect> boxes)
    {var values=boxes.ToArray();return !values.Where((r,i)=>values.Skip(i+1).Any(r.IntersectsWith)).Any();}
    static void DenseSection(TransferNetworkResult actual,string output)
    {
        // Eight-beam drawing fixture reuses checked members; this does not run another network search.
        var fixture=new TransferNetworkResult {Beams=Enumerable.Repeat(actual.Beams[1],8).ToArray(),Bays=Enumerable.Repeat(actual.Bays[0],7).ToArray(),BeamCentresMm=Enumerable.Range(0,8).Select(i=>i*4500.0).ToArray(),Input=new TransferNetworkInput {Beams=Enumerable.Repeat(actual.Input.Beams[1],8).ToArray(),Bays=Enumerable.Repeat(actual.Input.Bays[0],7).ToArray()}};
        var section=new TransferNetworkPlan {Width=1200,SectionOnly=true};section.SetResult(fixture);section.Measure(new Size(1200,420));section.Arrange(new Rect(0,0,1200,420));section.UpdateLayout();
        Action<string> capture=path=> {var bitmap=new RenderTargetBitmap(1200,420,96,96,PixelFormats.Pbgra32);bitmap.Render(section);if(path!=null){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}};
        capture(output==null ? null : Path.ChangeExtension(output,"workspace-dense-section.png"));
        Check(section.MemberTargets.Count==47 && section.DrawnSecondaryLengthCount==8 && section.DrawnPrimarySectionCount==32,"eight-beam section retains secondary, shared slab and both paired primary section targets");
        Check(NoOverlaps(section.AnnotationBounds),"eight-beam section annotation rectangles do not overlap");
        section.ShowAllDimensions=true;section.InvalidateVisual();section.UpdateLayout();capture(null);
        Check(NoOverlaps(section.AnnotationBounds),"full section dimensions omit labels that would overlap");
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
    {if(root is BeamFormworkWorkspace view)root=view.Surface;foreach(object item in LogicalTreeHelper.GetChildren(root))if(item is DependencyObject child){if(child is T match)yield return match;foreach(var value in Controls<T>(child))yield return value;}}
    static void CadSelection(BeamCalculatorViewModel model)
    {
        var drawing=workspace.Drawing;var original=model.NetworkConfigurationXml;
        Click("Geometry");drawing.ClearSelection();Render();
        var beam=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Beam && h.Item3==1).Item1;
        drawing.HighlightAt(Centre(beam));Render();
        Check(drawing.HoveredObject.Item1==FormworkObject.Beam && drawing.HoveredObject.Item2==1 && drawing.SelectedObjects.Length==0,"hover preselection never creates a deletion selection");
        Check(workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None) && model.NetworkConfigurationXml==original,"delete with no selection retains all geometry");
        drawing.SelectWindow(new Point(beam.Left-2,beam.Top-2),new Point(beam.Right+2,beam.Bottom+2));Render();
        Check(drawing.SelectedObjects.Length==1 && drawing.SelectedObjects[0].Equals(Tuple.Create(FormworkObject.Beam,1)),"left-to-right window selects fully enclosed objects");
        var centre=Centre(beam);var left=new Point(centre.X-15,centre.Y-2);var right=new Point(centre.X+15,centre.Y+2);
        drawing.SelectWindow(left,right);Render();Check(drawing.SelectedObjects.Length==0,"window excludes partially enclosed objects");
        drawing.SelectWindow(right,left);Render();Check(drawing.SelectedObjects.Length==1 && drawing.SelectedIndex==1,"right-to-left crossing selects touched objects");
        var slab=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Slab && h.Item3==0).Item1;
        Check(drawing.SelectAt(Centre(slab),true) && drawing.SelectedObjects.Length==2,"shift-click adds a slab to the selection");Render();
        Check(drawing.SelectAt(Centre(beam),true) && drawing.SelectedObjects.Length==1 && drawing.SelectedKind==FormworkObject.Slab,"shift-click removes an already selected object");Render();
        drawing.SelectWindow(new Point(beam.Left-2,beam.Top-2),new Point(beam.Right+2,beam.Bottom+2),true);Render();
        Check(drawing.SelectedObjects.Length==2,"additive window selection retains previous objects");
        Check(workspace.HandleGeometryKey(Key.Escape,ModifierKeys.None) && drawing.SelectedObjects.Length==0,"escape clears selection");Render();
        model.AddCrossingBeam();model.CrossingBeams.Last().BayNumber=1;
        model.AddCrossingBeam();model.CrossingBeams.Last().BayNumber=2;Pump();Render();
        var withCrossings=model.NetworkConfigurationXml;
        drawing.Select(FormworkObject.Beam,1);Render();
        slab=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Slab && h.Item3==0).Item1;
        drawing.SelectAt(new Point(slab.Left+10,slab.Top+slab.Height/2),true);Render();
        Check(workspace.RemovalPreview.Contains("B2") && workspace.RemovalPreview.Contains("S1") && workspace.RemovalPreview.Contains("X1") && !workspace.RemovalPreview.Contains("X2"),"deletion preview names coupled geometry and dependent crossing beams");
        // Exercise the actual WPF preview-key route with drawing focus.
        drawing.Focus();var delete=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(workspace.Surface),Environment.TickCount,Key.Delete){RoutedEvent=Keyboard.PreviewKeyDownEvent};drawing.RaiseEvent(delete);Pump();Render();
        Check(delete.Handled && model.NetworkBeams.Count==2 && model.NetworkBays.Count==1,"delete key removes a selected beam/slab pair only once");
        Check(model.CrossingBeams.Count==1 && model.CrossingBeams[0].BayNumber==1 && drawing.SelectedObjects.Length==0,"dependent crossing is removed and the retained crossing is correctly reindexed");
        Click("Undo");Render();Check(model.NetworkConfigurationXml==withCrossings && drawing.SelectedObjects.Length==2,"one undo restores the entire batch and its selection");
        Click("Redo");Render();Check(model.NetworkBeams.Count==2 && model.CrossingBeams.Count==1,"one redo reapplies the entire batch");
        var two=model.NetworkConfigurationXml;drawing.Select(FormworkObject.Beam,0);Render();
        Check(!workspace.Buttons["Delete"].IsEnabled && workspace.RemovalPreview.Contains("at least two"),"minimum connected-network geometry blocks deletion visibly");
        workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None);Check(model.NetworkConfigurationXml==two,"minimum-network deletion never partially removes geometry");
        Click("Undo");Render();Check(model.NetworkConfigurationXml==withCrossings,"rejected deletion creates no extra undo entry");
        drawing.Select(FormworkObject.Beam,1);Render();var depth=workspace.Fields["DepthMm"];
        depth.Focus();var textDelete=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(workspace.Surface),Environment.TickCount,Key.Delete){RoutedEvent=Keyboard.PreviewKeyDownEvent};depth.RaiseEvent(textDelete);Pump();
        Check(model.NetworkConfigurationXml==withCrossings,"delete while editing numeric text does not remove drawing geometry");
        Check(!workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None,true),"text editing bypasses drawing shortcuts");
        depth.Text="bad";Pump();drawing.Focus();workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None);
        Check(model.NetworkConfigurationXml==withCrossings && drawing.SelectedObjects.Length==1,"invalid pending text prevents a deletion batch");
        depth.Text=model.NetworkBeams[1].DepthMm.ToString();Check(workspace.CommitFields(),"corrected text releases deletion validation");Pump();Render();
        drawing.Formwork=true;Check(!workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None) && model.NetworkConfigurationXml==withCrossings,"member inspection cannot delete geometry");drawing.Formwork=false;
        model.AddNetworkBay();model.AddNetworkBay();Pump();Render();
        var five=model.NetworkConfigurationXml;
        drawing.SetSelection(new[] {Tuple.Create(FormworkObject.Beam,1),Tuple.Create(FormworkObject.Beam,3)},FormworkObject.Beam,3);Render();
        Check(workspace.RemovalPreview.Contains("S1") && workspace.RemovalPreview.Contains("S3") && !workspace.RemovalPreview.Contains("S2"),"batch deletion previews each removed original slab bay");
        workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None);Pump();Render();
        Check(model.NetworkBeams.Count==3 && model.NetworkBays.Count==2 && model.CrossingBeams.Count==1 && model.CrossingBeams[0].BayNumber==1,"distinct selected beams are removed without shifted-index errors");
        Click("Undo");Render();Check(model.NetworkConfigurationXml==five && drawing.SelectedObjects.Length==2,"one undo restores distinct beams and crossing attachments");
        drawing.SetSelection(Enumerable.Range(0,5).Select(i=>Tuple.Create(FormworkObject.Beam,i)).Concat(new[] {Tuple.Create(FormworkObject.Crossing,0)}),FormworkObject.Beam,4);Render();
        workspace.HandleGeometryKey(Key.Delete,ModifierKeys.None);Pump();Render();
        Check(model.NetworkConfigurationXml==five,"oversized selection preserves selected crossing beams as well as the network");
        model.NetworkConfigurationXml=original;drawing.Select(FormworkObject.Beam,0);Pump();Render();
        Check(model.NetworkConfigurationXml==original,"CAD interaction checks restore the original drawing inputs");
    }
    internal static int Run(BeamCalculatorWindow parent,BeamCalculatorViewModel model,CalculationRecord customSetup,string output)
    {
        try
        {
            parent.Width=1280;parent.Show();Pump();
            var opener=(Button)parent.FindName("DrawingWorkspaceButton");opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();workspace=parent.FormworkWorkspace;
            Check(workspace!=null && ReferenceEquals(workspace.Model,model) && ReferenceEquals(parent.Content,workspace.Surface) && parent.Title.Contains("D3 shoring"),"D3 opens the drawing as the calculator's main UI with shared state");
            opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(ReferenceEquals(workspace,parent.FormworkWorkspace),"opening again reuses the window");
            workspace.WindowState=WindowState.Normal;workspace.Width=1280;workspace.Height=860;Render();
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-geometry.png"));
            workspace.InspectorTabs.SelectedIndex=1;Render(output==null ? null : Path.ChangeExtension(output,"workspace-settings.png"));
            workspace.Width=980;workspace.Height=620;Render(output==null ? null : Path.ChangeExtension(output,"workspace-compact.png"),980,620);
            workspace.Width=1280;workspace.Height=860;workspace.InspectorTabs.SelectedIndex=0;Render();
            Check(workspace.Buttons["Save"] is MenuItem && workspace.Buttons["Load"] is MenuItem && workspace.Buttons["AddAbove"] is MenuItem,"file and add actions use compact menus");
            Check(workspace.InspectorTabs.Items.Count==2 && ((TabItem)workspace.InspectorTabs.Items[1]).Header.Equals("Shared settings"),"object properties and shared settings have separate inspector tabs");
            var plywoodSelector=Controls<ComboBox>(workspace).Single(c=>c.SelectedItem is PlywoodProfile);
            Check(plywoodSelector.Items.Count==3 && plywoodSelector.SelectedItem==model.SelectedPlywood,"drawing workspace shares plywood selector");
            var originalPlywood=model.SelectedPlywood;plywoodSelector.SelectedItem=model.PlywoodOptions.Single(p=>p.ThicknessMm==21);Pump();
            Check(model.SelectedPlywood.ThicknessMm==21 && !model.IsCalculationValid && workspace.Fields["UpperSupportSpacingMm"].IsReadOnly,"workspace plywood change recalculates locked spacing and invalidates layout");
            Click("Undo");Check(model.SelectedPlywood==originalPlywood,"undo restores plywood choice");Calculate();
            var plywoodLimit=Controls<ComboBox>(workspace).Single(c=>c.Name=="WorkspacePlywoodDeflectionSelector");var originalPlywoodLimit=model.PlywoodDeflectionLimit;var originalH20=model.DeflectionLimit;
            plywoodLimit.SelectedItem="L/500";Pump();Check(model.PlywoodDeflectionLimit=="L/500" && model.DeflectionLimit==originalH20 && !model.IsCalculationValid,"workspace plywood limit changes independently and invalidates stale layout");
            Click("Undo");Check(model.PlywoodDeflectionLimit==originalPlywoodLimit && model.DeflectionLimit==originalH20,"workspace undo restores independent plywood criterion");Calculate();
            var originalResult=model.TowerResult;Click("Diagnose");var diagnosis=workspace.Diagnostics;
            Check(diagnosis!=null && diagnosis.IsVisible && diagnosis.ReportBox.IsReadOnly && diagnosis.ReportBox.Text.Contains("BEAM FORMWORK DIAGNOSTIC REPORT v1"),"diagnose opens copyable report window");
            Check(ReferenceEquals(model.TowerResult,originalResult),"opening diagnosis retains calculated result");
            diagnosis.RemarksBox.Text="Slow search after custom H20 checkbox";Click("Diagnose");
            Check(ReferenceEquals(diagnosis,workspace.Diagnostics) && diagnosis.ReportBox.Text.Contains("Slow search after custom H20 checkbox"),"repeat diagnose reuses window and retains remarks");
            string copied=null;var copyWindow=new BeamDiagnosticWindow(remarks=>model.BuildDiagnosticReport(remarks),text=>copied=text);
            copyWindow.RemarksBox.Text="Copy my remarks";copyWindow.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(copied==copyWindow.ReportBox.Text && copied.Contains("Copy my remarks") && copied.Contains("END BEAM FORMWORK"),"copy refreshes the complete report and remarks");copyWindow.Close();
            var drawing=workspace.Drawing;drawing.Fit();Render();
            Click("Formwork");Render();var layout=model.NetworkResult;
            Check(drawing.DrawnSecondaryH20Count==0,"secondary H20s start hidden in plan");
            Click("SecondaryH20s");Render();
            Check(drawing.DrawnSecondaryH20Count>0 && ReferenceEquals(layout,model.NetworkResult),"secondary H20 toggle shows real members without changing the solution");
            foreach(var target in drawing.MemberTargets.Where(h=>h.Item2<model.NetworkResult.Beams.Length && h.Item3>=model.NetworkResult.Beams[h.Item2].StockPlan.Pieces.Length && h.Item3<model.NetworkResult.Beams[h.Item2].StockPlan.Pieces.Length+model.NetworkResult.Beams[h.Item2].Transfer.RowsMm.Length))
            {
                double centreY=drawing.Screen(0,model.NetworkResult.BeamCentresMm[target.Item2]).Y;
                Check(Math.Abs(target.Item1.Top+target.Item1.Height/2-centreY)<1e-6,"secondary plan midpoint aligns with concrete centreline");
            }
            var lowerTarget=drawing.MemberTargets.First(h=>h.Item2>=0 && h.Item3>=layout.Beams[h.Item2].StockPlan.Pieces.Length);
            workspace.ShowMember(lowerTarget.Item2,lowerTarget.Item3,lowerTarget.Item4);Pump();
            Check(workspace.Diagrams.SelectedTransferMember.H20Count==1 && workspace.Diagrams.LoadedAnalysis.DeflectionEvaluated,"visible secondary selection opens checked single-H20 diagrams");
            Check(workspace.SelectedResultText.Contains("Selected beam · B"+(lowerTarget.Item2+1)),"secondary picking selects its actual receiving beam");
            workspace.Diagrams.Close();Click("SecondaryH20s");Render();
            Check(drawing.DrawnSecondaryH20Count==0 && ReferenceEquals(layout,model.NetworkResult),"hiding secondaries leaves the layout and calculated checks intact");
            drawing.Select(FormworkObject.Beam,0);Pump();
            Click("Geometry");Render();
            Check(workspace.NetworkResultText.Contains(model.NetworkResult.LegCount+" tower legs") && workspace.SelectedResultText.Contains("Selected beam · B1") && workspace.SelectedResultText.Contains(model.NetworkResult.Beams[0].LegCount+" tower legs"),"network totals and selected beam quantities have explicit independent labels");
            var details=Controls<Expander>(workspace).Single(e=>Equals(e.Header,"Details"));
            Check(!details.IsExpanded && !workspace.NetworkResultText.Contains("peak leg") && workspace.NetworkResultText.Contains(model.NetworkResult.H20Count+" H20s"),"network card defaults to support and timber counts with extra quantities collapsed");
            Check(Controls<TextBlock>(details).Any(t=>t.Text.Contains("upper feet") && t.Text.Contains("Slab movement")),"full engineering totals remain available under Details");
            Check(drawing.ObjectTargets.Count(t=>t.Item2==FormworkObject.Beam)==3 && drawing.ObjectTargets.Count(t=>t.Item2==FormworkObject.Slab)==2,"geometry is visible before formwork selection");
            var top=drawing.Screen(drawing.SceneBounds.Left,drawing.SceneBounds.Top);var bottom=drawing.Screen(drawing.SceneBounds.Right,drawing.SceneBounds.Bottom);
            Check(top.X>=0 && top.Y>=0 && bottom.X<=drawing.ActualWidth && bottom.Y<=drawing.ActualHeight,"fit includes the complete metric plan");
            var anchor=new Point(200,180);var world=drawing.World(anchor);double zoom=drawing.Zoom;drawing.ZoomAt(1.25,anchor);
            Check(Math.Abs(drawing.Zoom/zoom-1.25)<1e-9 && (drawing.World(anchor)-world).Length<1e-7,"zoom preserves the point under the cursor");
            var oldPan=drawing.Pan;drawing.PanBy(new Vector(32,-18));Check((drawing.Pan-oldPan-new Vector(32,-18)).Length<1e-8,"pan moves the view without changing dimensions");drawing.Fit();Render();
            Click("Formwork");Render(output==null ? null : Path.ChangeExtension(output,"workspace-overview.png"));
            Check(drawing.DrawnLegCount==model.NetworkResult.LegCount && drawing.MemberTargets.Count>0,"formwork overlays all calculated legs and primary pairs");
            Check(drawing.LegTargets.Count==drawing.DrawnLegCount && drawing.LegTargets.All(t=>t.Item2.Contains("/70 kN")),"every visible leg has load and permissible-capacity inspection");
            var optionRecord=parent.CaptureCalculation();
            var optionTask=model.GetNetworkBeamOptionsAsync(1);var optionDeadline=DateTime.UtcNow.AddMinutes(2);
            while(!optionTask.IsCompleted && DateTime.UtcNow<optionDeadline){Pump();Thread.Sleep(10);}
            Check(optionTask.IsCompleted && optionTask.Result.Any(o=>ReferenceEquals(o.Result,model.NetworkResult)),"alternative search retains the selected full-load arrangement");
            // Restored slab end loads can leave only the original heavy arrangement.
            // Test Option 2 on a separate lighter case, without admitting unsafe options.
            bool lighterOptions=optionTask.Result.Count(o=>o.IsAvailable)==1;
            if(lighterOptions)
            {
                foreach(var b in model.NetworkBeams)b.SetDepthKeepingTopMm(500);
                foreach(var b in model.NetworkBays)b.ThicknessMm=100;
                Calculate();
                optionTask=model.GetNetworkBeamOptionsAsync(1);optionDeadline=DateTime.UtcNow.AddMinutes(2);
                while(!optionTask.IsCompleted && DateTime.UtcNow<optionDeadline){Pump();Thread.Sleep(10);}
            }
            var optionSource=model.NetworkResult;
            if(!optionTask.IsCompleted || optionTask.Result.Count(o=>o.IsAvailable)<=1)Console.WriteLine(model.BuildDiagnosticReport());
            Check(optionTask.IsCompleted && optionTask.Result.Count(o=>o.IsAvailable)>1,"beam alternatives are generated without blocking the drawing dispatcher");
            var beamTarget=drawing.ObjectTargets.First(t=>t.Item2==FormworkObject.Beam && t.Item3==1);
            Check(drawing.OpenBeamOptionsAt(Centre(beamTarget.Item1)),"right click targets B2's beam geometry");Pump();
            var optionMenu=drawing.ContextMenu;
            var optionItems=optionMenu.Items.OfType<MenuItem>().Where(i=>i.IsCheckable).ToArray();
            Check(optionItems.Length==optionTask.Result.Count(o=>o.IsAvailable) && optionItems[0].IsChecked,"context menu lists checked complete options and marks the selected one");
            optionItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();Render();
            Check(ReferenceEquals(model.NetworkResult,optionTask.Result.Where(o=>o.IsAvailable).ElementAt(1).Result) && model.SelectedNetworkBeam==1 && ReferenceEquals(model.TowerResult,model.NetworkResult.Beams[1]),"Option 2 updates complete network and inspected beam together");
            Check(drawing.DrawnLegCount==model.NetworkResult.LegCount && workspace.NetworkResultText.Contains(model.NetworkResult.LegCount+" tower legs"),"drawing and quantities refresh after selecting an alternate");
            optionItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));optionMenu.IsOpen=false;Pump();Render();
            Check(ReferenceEquals(model.NetworkResult,optionSource),"Option 1 restores the original verified layout");
            if(lighterOptions){parent.RestoreCalculation(optionRecord);Pump();Calculate();}
            double planHeight=drawing.ActualHeight;var hit=drawing.MemberTargets.First();Check(drawing.SelectAt(Centre(hit.Item1)),"primary H20 is selected directly from plan");Pump();Render();
            Check(workspace.Diagrams!=null && workspace.Diagrams.IsVisible && workspace.Diagrams.Owner==parent && workspace.Diagrams.SelectedTransferMember.H20Count==2,"primary pair opens a diagram owned by the main D3 calculator");
            Check(Math.Abs(workspace.Diagrams.LoadedAnalysis.TotalLoadKn-model.NetworkResult.Beams[hit.Item2].Transfer.PrimaryCases[workspace.Diagrams.SelectedLoadCase][hit.Item3].Analysis.TotalLoadKn)<1e-8,"plan selection displays actual member load");
            Check(Controls<Border>(workspace.Diagrams).Count(b=>b.CornerRadius.TopLeft==8)==4 && !Controls<Border>(workspace).Any(b=>b.CornerRadius.TopLeft==8),"loading and response cards are outside the workspace");
            Check(Math.Abs(drawing.ActualHeight-planHeight)<.01,"opening diagrams does not shrink the drawing canvas");
            RenderDiagrams();Check(workspace.Diagrams.LoadingDiagram.DrawnSupportCount==workspace.Diagrams.SelectedTransferMember.SupportsMm.Length && workspace.Diagrams.LoadingDiagram.DrawnUniformCount==workspace.Diagrams.LoadingDiagram.CombinedUniform.Length,"loading schematic renders every support and combined distributed design interval");
            Check(workspace.Diagrams.LoadingDiagram.DrawnReactionLabelCount==workspace.Diagrams.SelectedTransferMember.SupportsMm.Length,"loading diagram displays every reaction value");
            Check(workspace.Diagrams.DisplayedTransferMember==workspace.Diagrams.SelectedTransferMember && workspace.Diagrams.DisplayedTransferMember.Points.Length==0,"primary shows the optimizer UDL model directly");
            Check(!Controls<CheckBox>(workspace.Diagrams).Any(),"primary has no point-load approximation switch");
            var udlDetails=Controls<Expander>(workspace.Diagrams).Single(e=>Equals(e.Header,"UDL support reactions"));
            Check(!udlDetails.IsExpanded,"UDL support details remain collapsed above plots");
            Check(workspace.SelectedResultText.Contains("Selected beam · B"+(hit.Item2+1)),"member picking updates selected results");
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-diagrams.png"));
            RenderDiagrams(output==null ? null : Path.ChangeExtension(output,"workspace-diagram-window.png"));
            var diagramWindow=workspace.Diagrams;var bodyHit=drawing.ObjectTargets.First(h=>h.Item2==FormworkObject.Beam && h.Item3==hit.Item2);
            Check(drawing.SelectAt(Centre(bodyHit.Item1)),"beam body click also opens its primary diagrams");Pump();Render();
            Check(ReferenceEquals(workspace.Diagrams,diagramWindow),"picking another primary on the same beam reuses the diagram window");
            diagramWindow.Close();Pump();Check(workspace.Diagrams==null && model.IsCalculationValid,"closing diagram window clears its reference and retains valid results");
            drawing.SelectAt(Centre(bodyHit.Item1));Pump();Check(workspace.Diagrams!=null && workspace.Diagrams.IsVisible,"beam click reopens the closed diagram window");
            Check(NoOverlaps(drawing.AnnotationBounds),"plan annotation rectangles do not overlap");
            DenseSection(model.NetworkResult,output);
            Click("Geometry");Render();var beam=drawing.ObjectTargets.Single(h=>h.Item2==FormworkObject.Beam && h.Item3==1);
            Check(drawing.SelectAt(Centre(beam.Item1)) && drawing.SelectedIndex==1 && workspace.Fields["DepthMm"].Text=="1200","click selects actual beam and editable depth");
            Check(workspace.SelectedResultText.Contains("Selected beam · B2") && workspace.SelectedResultText.Contains(model.NetworkResult.Beams[1].LegCount+" tower legs"),"each geometry beam selection displays that beam's own result");
            workspace.InspectorTabs.SelectedIndex=1;Render();var advanced=Controls<Expander>(workspace.SharedProperties).Single(e=>Equals(e.Header,"Stock and load settings"));advanced.IsExpanded=true;
            drawing.Select(FormworkObject.Beam,0);Render();Check(workspace.InspectorTabs.SelectedIndex==0 && Controls<Expander>(workspace.SharedProperties).Single(e=>Equals(e.Header,"Stock and load settings")).IsExpanded,"selection returns to object properties and preserves expanded shared settings");
            drawing.Select(FormworkObject.Beam,1);Render();
            Enter("DepthMm","1325");Check(model.NetworkBeams[1].DepthMm==1325 && !model.IsCalculationValid && workspace.Diagrams==null && !workspace.Buttons["Export"].IsEnabled,"editing invalidates shared result and diagrams");
            Check(workspace.NetworkResultText=="" && workspace.SelectedResultText=="","an edit hides all stale result quantities");
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
            CadSelection(model);
            model.AllowCustomH20Lengths=false;Pump();
            var customH20=Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Allow custom H20 lengths (25 mm)"));
            Check(customH20.IsChecked==false,"custom H20 checkbox reflects disabled setting");customH20.IsChecked=true;customH20.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(model.AllowCustomH20Lengths && !model.IsCalculationValid,"workspace custom toggle updates shared model and invalidates result");
            Click("Undo");Check(!model.AllowCustomH20Lengths,"undo restores custom setting");Click("Redo");Check(model.AllowCustomH20Lengths,"redo restores custom setting");
            var customScope=Controls<ComboBox>(workspace).Single(c=>c.Name=="CustomH20ScopeSelector");
            Check(customScope.IsEnabled && customScope.Items.Count==3 && Equals(customScope.SelectedValue,CustomH20LengthScope.Both),"workspace offers both, beam-only and slab-only custom scope");
            customScope.SelectedValue=CustomH20LengthScope.BeamSupports;Pump();
            Check(model.CustomH20Scope==CustomH20LengthScope.BeamSupports && !model.IsCalculationValid,"scope change updates model and clears result");
            Click("Undo");Check(model.CustomH20Scope==CustomH20LengthScope.Both,"undo restores custom scope");Click("Redo");Check(model.CustomH20Scope==CustomH20LengthScope.BeamSupports,"redo restores custom scope");
            Check(Equals(((ComboBox)parent.FindName("CustomH20ScopeSelector")).SelectedValue,CustomH20LengthScope.BeamSupports),"calculator and workspace scope selectors synchronize");
            var customLengthsRecord=parent.CaptureDrawingInputs();Check(customLengthsRecord.Inputs.Single(f=>f.Name=="AllowCustomH20Lengths").Value=="True","custom setting is saved with drawing inputs");
            Check(customLengthsRecord.Inputs.Single(f=>f.Name=="CustomH20Scope").Value=="BeamSupports","custom scope saved with drawing inputs");
            model.AllowCustomH20Lengths=false;model.CustomH20Scope=CustomH20LengthScope.SlabSupports;parent.RestoreCalculation(customLengthsRecord);Pump();Check(model.AllowCustomH20Lengths && model.CustomH20Scope==CustomH20LengthScope.BeamSupports,"custom setting and scope load from calculation file");
            var scopeField=customLengthsRecord.Inputs.Single(f=>f.Name=="CustomH20Scope");scopeField.Value="99";
            bool badScope=false;try {parent.RestoreCalculation(customLengthsRecord);}catch(ArgumentException) {badScope=true;}
            Check(badScope && model.CustomH20Scope==CustomH20LengthScope.BeamSupports,"invalid saved scope rejected without changing live inputs");
            customLengthsRecord.Inputs.Remove(scopeField);parent.RestoreCalculation(customLengthsRecord);Pump();
            Check(model.AllowCustomH20Lengths && model.CustomH20Scope==CustomH20LengthScope.Both,"legacy custom-enabled files retain both scopes");
            Check(((CheckBox)parent.FindName("CustomH20LengthsCheckBox")).IsChecked==true,"calculator checkbox synchronizes with layout window");
            customLengthsRecord.Inputs.RemoveAll(f=>f.Name=="AllowCustomH20Lengths");parent.RestoreCalculation(customLengthsRecord);Pump();
            Check(!model.AllowCustomH20Lengths && Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Allow custom H20 lengths (25 mm)")).IsChecked==false,"legacy files restore disabled custom mode in both windows");
            Check(!Controls<ComboBox>(workspace).Single(c=>c.Name=="CustomH20ScopeSelector").IsEnabled,"scope selector disables when custom master switch is off");
            var uplift=Controls<CheckBox>(workspace).Single(c=>Equals(c.Content,"Exclude uplift check (connected plywood / deadweight assumed)"));
            Check(uplift.IsChecked==false,"workspace uplift exclusion defaults off");uplift.IsChecked=true;uplift.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(model.IgnoreUplift && !model.IsCalculationValid,"workspace uplift opt-in invalidates the shared result");
            Click("Undo");Check(!model.IgnoreUplift,"undo restores bearing-only assumptions");Click("Redo");Check(model.IgnoreUplift,"redo restores project uplift assumption");
            var upliftRecord=parent.CaptureDrawingInputs();Check(upliftRecord.Inputs.Single(f=>f.Name=="IgnoreUplift").Value=="True","workspace uplift assumption saved");
            model.IgnoreUplift=false;parent.RestoreCalculation(upliftRecord);Pump();Check(model.IgnoreUplift && ((CheckBox)parent.FindName("IgnoreUpliftCheckBox")).IsChecked==true,"loaded uplift assumption synchronizes both windows");
            upliftRecord.Inputs.RemoveAll(f=>f.Name=="IgnoreUplift");parent.RestoreCalculation(upliftRecord);Pump();Check(!model.IgnoreUplift,"legacy workspace file restores bearing-only assumptions");
            Check(!model.CanCancelOptimization && !workspace.Buttons["Cancel"].IsEnabled,"cancel is disabled without a running analysis");
            var cancelledSearch=workspace.CalculateAsync();
            Check(model.CanCancelOptimization,"running analysis can be cancelled");
            Pump(); // Workspace controls refresh on the dispatcher after model notifications.
            Check(!workspace.Buttons["NewSession"].IsEnabled,"new session is disabled until the running search stops");
            Check(workspace.Waiting.Visibility==Visibility.Visible && workspace.Waiting.IsTimerRunning && workspace.Waiting.Cancel.IsEnabled,"drawing overlay follows an actual running analysis");
            Check(!workspace.SharedProperties.IsEnabled && !workspace.Fields["BeamLengthMm"].IsEnabled,"shared inputs are locked during a running search");
            var calculatorCancel=(Button)parent.FindName("CancelBeamAnalysisButton");
            calculatorCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!model.CanCancelOptimization && model.IsOptimizing,"cancel request disables repeated cancellation until worker stops");
            var cancelTimer=System.Diagnostics.Stopwatch.StartNew();while(!cancelledSearch.IsCompleted){Pump();Thread.Sleep(1);if(cancelTimer.Elapsed.TotalSeconds>10)throw new Exception("Workspace cancellation timeout");}
            cancelledSearch.GetAwaiter().GetResult();Pump();workspace.Diagnostics.RefreshReport();
            Check(workspace.Waiting.Visibility==Visibility.Collapsed && !workspace.Waiting.IsTimerRunning,"cancellation removes Hugo and stops its UI timer");
            Check(!model.IsOptimizing && !model.IsCalculationValid && model.TowerResult==null && model.NetworkResult==null && model.CanOptimizeTowers,"cancel stops analysis without publishing a partial layout and allows recalculation");
            Check(workspace.SharedProperties.IsEnabled && workspace.Fields["BeamLengthMm"].IsEnabled,"cancellation restores input editing");
            Check(workspace.Diagnostics.ReportBox.Text.Contains("Search outcome: Cancelled") && workspace.Diagnostics.ReportBox.Text.Contains("Cancellation requested: True"),"diagnosis retains cancelled search details");
            var secondCancel=workspace.CalculateAsync();workspace.Buttons["Cancel"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            cancelTimer.Restart();while(!secondCancel.IsCompleted){Pump();Thread.Sleep(1);if(cancelTimer.Elapsed.TotalSeconds>10)throw new Exception("Workspace second cancellation timeout");}
            secondCancel.GetAwaiter().GetResult();Pump();Check(!model.IsOptimizing && model.BuildDiagnosticReport().Contains("Search outcome: Cancelled"),"drawing toolbar cancel also stops the shared analysis");
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
            parent.RestoreCalculation(customSetup);Pump();Render();
            var spacingField=workspace.Fields["UpperSupportSpacingMm"];
            Check(spacingField.IsReadOnly && !spacingField.IsEnabled && spacingField.Text==model.UpperSupportSpacingMm.ToString("0.###",CultureInfo.CurrentCulture),"workspace shows locked calculated row spacing");
            var legacySpacing=parent.CaptureDrawingInputs();legacySpacing.Inputs.Single(f=>f.Name=="UpperSupportSpacingMm").Value="750";
            parent.RestoreCalculation(legacySpacing);Pump();Check(model.UpperSupportSpacingMm==model.MaximumSecondarySpacingMm && model.UpperSupportSpacingMm!=750,"saved manual spacing cannot override the calculated maximum");
            model.NetworkBeams[0].DepthMm=900;model.NetworkBeams[1].DepthMm=900;Pump();Calculate();
            Check(model.IsCalculationValid && drawing.DrawnLegCount==model.NetworkResult.LegCount && model.NetworkResult.Bays[0].IsCustomLength,"custom setup calculates at the automatic spacing and draws every selected leg");
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-custom.png"));
            Click("Section");Render(output==null ? null : Path.ChangeExtension(output,"workspace-section.png"));
            var customHit=workspace.Section.MemberTargets.Last();workspace.Section.SelectAt(Centre(customHit.Item1));Pump();
            Check(workspace.Diagrams.SelectedTransferMember.LengthMm==model.NetworkResult.Bays[0].StockLengthMm && workspace.Diagrams.SelectedTransferMember.H20Count==1,"custom member is selectable in dedicated section");
            Click("Geometry");drawing.Select(FormworkObject.Slab,0);Render();Check(workspace.SelectedResultText.Contains("Selected slab · S1") && workspace.SelectedResultText.Contains(model.NetworkResult.Bays[0].StockLengthMm.ToString("0.###")+" mm custom"),"slab selection reports its own H20 stock and receiving beams");
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
            var beforeFallback=parent.CaptureDrawingInputs();
            model.AllowCustomH20Lengths=false;model.BeamLengthMm=3300;model.TowerLegCapacityKn=70;
            model.DeflectionLimit=model.DeflectionOptions.Single(d=>d.Label=="L/350");model.Exclude600Standalone=false;
            while(model.NetworkBeams.Count<3)model.AddNetworkBay();while(model.NetworkBeams.Count>3)model.RemoveNetworkBay();
            foreach(var crossing in model.CrossingBeams.ToArray())model.RemoveCrossingBeam(crossing);
            foreach(var fallbackBeam in model.NetworkBeams){fallbackBeam.WidthMm=400;fallbackBeam.DepthMm=500;}
            model.NetworkBays[0].WidthMm=10000;model.NetworkBays[1].WidthMm=2500;
            foreach(var bay in model.NetworkBays)bay.ThicknessMm=300;
            Pump();Render();Calculate();
            Check(model.IsCalculationValid && model.NetworkResult.Bays[0]==null && model.NetworkResult.Bays[1]!=null,"catalogue failure falls back to separate S1 while retaining S2");
            Check(workspace.SeparateSlabNotice.Visibility==Visibility.Visible && workspace.SeparateSlabNotice.Notice.Text.Contains("S1") && parent.BuildReport().Contains("design are excluded") && parent.BuildReport().Contains("Full Transfer Failure Details") && parent.BuildReport().Contains("no permitted slab H20 length"),"mixed result and export explicitly identify the excluded slab design");
            Check(!workspace.SeparateSlabNotice.Explanation.IsExpanded && workspace.SeparateSlabNotice.EvidenceText.Contains("10000 mm"),"separate slab notice shows actual reason with details initially collapsed");
            Click("Geometry");drawing.Select(FormworkObject.Slab,0);Render();
            Check(workspace.SeparateSlabNotice.Explanation.IsExpanded && workspace.SeparateSlabNotice.EvidenceText==model.NetworkResult.SeparateSlabExplanationForBay(0),"selecting the amber slab opens its specific explanation");
            Check(workspace.SelectedResultText.Contains("Separate slab formwork required") && workspace.SelectedResultText.Contains("No slab load transfers"),"selected independent slab displays its actual scope");
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-separate-slab.png"));
            Click("Section");Render();
            int upperStart=model.NetworkResult.Beams[0].StockPlan.Pieces.Length+model.NetworkResult.Beams[0].Transfer.RowsMm.Length;
            Check(!workspace.Section.MemberTargets.Any(h=>h.Item2==0 && h.Item3>=upperStart),"separate slab creates no fabricated upper member targets");
            var retainedUpper=workspace.Section.MemberTargets.First(h=>h.Item2==1 && h.Item3>=model.NetworkResult.Beams[1].StockPlan.Pieces.Length+model.NetworkResult.Beams[1].Transfer.RowsMm.Length);
            workspace.Section.SelectAt(Centre(retainedUpper.Item1));Pump();Check(workspace.Diagrams.SelectedTransferMember.H20Count==1,"retained S2 diagrams remain selectable after fallback");
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-separate-section.png"));
            var retainedDiagram=workspace.Diagrams;var previousXml=model.NetworkConfigurationXml;var previousResult=model.NetworkResult;
            workspace.ConfirmNewSession=()=>false;Click("NewSession");
            Check(model.NetworkConfigurationXml==previousXml && ReferenceEquals(model.NetworkResult,previousResult) && ReferenceEquals(workspace.Diagrams,retainedDiagram),"declining new session preserves inputs, result and open diagrams");
            workspace.ConfirmNewSession=()=>true;Click("NewSession");Render();
            var defaults=new BeamCalculatorViewModel();
            Check(workspace.Surface.IsVisible && ReferenceEquals(workspace.Model,model) && model.HasSelectedSupportSystem && model.UseTowers && model.UseBeamNetwork && model.TransferSlabLoads,"new session retains the shared model, selected mode and drawing workspace");
            Check(model.NetworkBeams.Count==2 && model.NetworkBays.Count==1 && model.CrossingBeams.Count==0 && model.SelectedNetworkBeam==0,"new session starts a fresh two-beam network without old crossing beams");
            Check(model.NetworkBeams.All(b=>b.WidthMm==defaults.Width && b.DepthMm==defaults.Depth) && model.BeamLengthMm==defaults.BeamLengthMm && model.StockLengthsText==defaults.StockLengthsText && model.TowerLegCapacityKn==defaults.TowerLegCapacityKn && model.UpperSupportSpacingMm==defaults.UpperSupportSpacingMm,"new session resets geometry, stock, capacity and spacing to defaults");
            Check(workspace.SeparateSlabNotice.Visibility==Visibility.Collapsed,"new session clears the previous independent-slab explanation");
            Check(model.TowerResult==null && model.NetworkResult==null && !model.IsCalculationValid && !model.HasTowerFailure && workspace.Diagrams==null && !retainedDiagram.IsVisible && workspace.Diagnostics==null,"new session clears results, failure and child windows");
            Check(!model.BuildDiagnosticReport().Contains("Search outcome: Succeeded") && workspace.NetworkResultText=="" && !workspace.Buttons["Export"].IsEnabled,"new session has no old search trace, visible quantities or exportable result");
            Check(!workspace.Buttons["Undo"].IsEnabled && !workspace.Buttons["Redo"].IsEnabled && !drawing.Formwork && workspace.Buttons["Calculate"].IsEnabled,"new session clears undo history and returns to editable geometry");
            Render(output==null ? null : Path.ChangeExtension(output,"workspace-new-session.png"));
            model.AddCrossingBeam();Pump();Render();workspace.Fields["DepthMm"].Text="bad";Pump();
            Check(workspace.Buttons["NewSession"].IsEnabled && !workspace.Buttons["Calculate"].IsEnabled,"new session remains available when pending input is invalid");
            Click("NewSession");Render();
            Check(model.CrossingBeams.Count==0 && workspace.Fields["DepthMm"].Text==defaults.Depth.ToString("0.###") && model.CanOptimizeTowers && workspace.Buttons["Calculate"].IsEnabled,"new session discards invalid pending text and clears crossing geometry");
            parent.RestoreCalculation(beforeFallback);Pump();Render();
            Click("Diagnose");var closingDiagnosis=workspace.Diagnostics;
            var xml=model.NetworkConfigurationXml;workspace.Close();Pump();Check(parent.FormworkWorkspace==null && model.CanOptimizeTowers && model.NetworkConfigurationXml==xml,"closing releases validation and retains geometry");Check(!closingDiagnosis.IsVisible,"closing workspace closes its diagnosis window");workspace=null;
            Console.WriteLine($"Passed {checks} drawing workspace checks.");return checks;
        }
        finally {workspace?.Close();}
    }
}



