using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;
using EngineeringDashboard;

internal static class RevitSelectionUiTests
{
    private static int checks;
    private static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    private static void Click(BeamFormworkWorkspace w,string name)=>w.Buttons[name].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T value)yield return value;foreach(var nested in Find<T>(child))yield return nested;}}
    internal static void Run(string png)
    {
        var model=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,UseBeamNetwork=true,TransferSlabLoads=true,AllowCustomH20Lengths=false,TowerLegCapacityKn=50,FloorHeight=4000};
        string original=model.NetworkConfigurationXml;double originalLength=model.BeamLengthMm;
        var standalone=new BeamFormworkWorkspace(model,null,null,null);Check(!standalone.Buttons.ContainsKey("FromRevit"),"standalone has no live Revit command");standalone.Close();
        int reads=0;bool columnSelection=false;var w=new BeamFormworkWorkspace(model,null,null,null,readRevitSelection:()=> {reads++;return columnSelection ? RevitImportFixtures.Columns() : RevitStructureImportTests.Sample();});w.Show();Pump();
        Check(w.Buttons.ContainsKey("FromRevit"),"Revit host has visible import button");
        w.ShowRevitImportPreview=dialog=> {Check(dialog.Result.CanImport && dialog.ImportButton.IsEnabled,"valid preview ready");dialog.Close();return false;};
        Click(w,"FromRevit");Check(reads==1 && model.NetworkConfigurationXml==original && model.BeamLengthMm==originalLength,"cancel does not replace canvas");
        w.ShowRevitImportPreview=dialog=>
        {
            dialog.Show();Pump();dialog.RefreshPreview();Pump();dialog.PreviewCanvas.Fit();Pump();
            Check(dialog.PreviewCanvas.DrawnReferenceCount==2,"preview draws both wall references");
            Check(dialog.PreviewCanvas.AnnotationLabels.Any(t=>t.Contains("Wall start")),"boundary labels appear in preview");
            Check(dialog.PreviewCanvas.AnnotationLabels.Any(t=>t.StartsWith("S1")),"slab label remains visible beside boundary references");
            Check(dialog.PreviewCanvas.Model.BeamLengthMm*dialog.PreviewCanvas.Zoom>dialog.PreviewCanvas.ActualHeight*.65,"compact preview uses the available drawing area");
            Check(!dialog.PreviewCanvas.AnnotationLabels.Any(t=>t.StartsWith("Click:")),"read-only preview has no drawing-edit hints");
            if(png!=null)
            {
                var content=(FrameworkElement)dialog.Content;content.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(png))encoder.Save(file);
            }
            dialog.Close();return true;
        };
        Click(w,"FromRevit");Pump();
        Check(model.BeamLengthMm==7000 && model.CrossingBeams.Single().PositionMm==3350,"accepted geometry reaches shared solver inputs");
        Check(model.RevitContext.References.Length==2 && w.Drawing.DrawnReferenceCount==2,"canvas keeps visible boundary references");
        Check(!model.AllowCustomH20Lengths && model.TowerLegCapacityKn==50 && model.FloorHeight==4000,"project settings preserved");
        Check(!model.IsCalculationValid && w.Buttons["Undo"].IsEnabled,"import invalidates results and can be undone");
        Click(w,"Undo");Pump();Check(model.NetworkConfigurationXml==original && model.BeamLengthMm==originalLength && model.RevitContext==null,"undo restores complete old drawing");
        Click(w,"Redo");Pump();Check(model.RevitContext.References.Length==2 && model.BeamLengthMm==7000,"redo restores import and references");
        w.Drawing.Select(FormworkObject.Beam,0);Pump();
        Check(w.TryEditDimension("BeamLengthMm","6500",out string dimensionError),"selected beam length edit: "+dimensionError);Pump();
        Check(model.BeamSpanLength(0)==6500 && model.BeamSpanLength(1)==7000,"dimension changes only the selected parallel beam");
        Click(w,"Undo");Pump();Check(model.BeamSpanLength(0)==7000 && model.BeamSpanLength(1)==7000,"undo restores per-beam length");
        Click(w,"Redo");Pump();Check(model.BeamSpanLength(0)==6500,"redo restores selected length edit");
        Check(!w.TryEditDimension("BeamLengthMm","3000",out dimensionError),"cannot cut off a connected crossing");
        Click(w,"Undo");Pump();
        var variable=RevitStructureImport.Build(IndividualSpanTests.ReportedGeometry(),"beam1");w.ApplyRevitImport(variable);Pump();w.Drawing.Fit();Pump();
        Check(model.NetworkBays[0].Outline.Length==8 && model.BeamSpanStart(0)>149,"canvas accepts different ends and actual cutbacks");
        Check(w.Drawing.AnnotationLabels.Any(t=>t.Contains("3901.832")),"canvas labels the actual short beam length");
        if(png!=null)
        {
            w.UpdateLayout();var content=(FrameworkElement)w.Content;content.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(Path.GetDirectoryName(png),"individual-spans.png")))encoder.Save(file);
        }
        columnSelection=true;
        w.ShowRevitImportPreview=dialog=>
        {
            dialog.Show();Pump();dialog.RefreshPreview();Pump();dialog.PreviewCanvas.Fit();Pump();
            Check(dialog.ImportButton.IsEnabled && dialog.Result.CanImport,"reported columns in slab cutbacks enable import");
            Check(dialog.PreviewCanvas.DrawnReferenceCount==4,"all four reported columns appear in the preview");
            Check(dialog.PreviewCanvas.Model.NetworkBays[0].Outline.Length>=8 && dialog.PreviewCanvas.Model.NetworkBays[3].Outline.Length>=8,"preview retains both actual corner outlines");
            Check(dialog.BuildDiagnosticReport("").Contains("Preview status: Ready to import"),"reported selection now has a successful diagnostic");
            if(png!=null)
            {
                var content=(FrameworkElement)dialog.Content;content.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(Path.GetDirectoryName(png),"column-cutbacks.png")))encoder.Save(file);
            }
            dialog.Close();return true;
        };
        Click(w,"FromRevit");Pump();Check(model.RevitContext.References.Length==4 && model.NetworkBeams.Count==5,"reported thirteen-element selection reaches canvas");
        Check(!model.AllowCustomH20Lengths && model.TowerLegCapacityKn==50,"column fix preserves project stock and capacity settings");
        Click(w,"Undo");Pump();Check(model.RevitContext.References.Length==0,"undo restores the earlier drawing without columns");
        Click(w,"Redo");Pump();Check(model.RevitContext.References.Length==4 && model.NetworkBays[0].Outline.Length>=8,"redo preserves columns and cutbacks");
        var group=RevitStructureImport.Build(RevitImportFixtures.Areas(),"523766");
        var combinedPreview=new RevitSelectionImportWindow(RevitImportFixtures.Areas());combinedPreview.Show();Pump();combinedPreview.RefreshPreview();Pump();combinedPreview.PreviewCanvas.Fit();Pump();
        Check(combinedPreview.ImportButton.IsEnabled && combinedPreview.Result.Areas.Length==9,"the full multi-span selection enables one combined import");
        Check(combinedPreview.PreviewCanvas.DrawnRevitPartCount==9 && combinedPreview.PreviewCanvas.DrawnReferenceCount==10,"all four areas, five column beams and ten columns share one preview");
        Check(combinedPreview.BuildDiagnosticReport("").Contains("523933") && combinedPreview.BuildDiagnosticReport("").Contains("Combined drawing part:"),"full source mapping remains diagnosable");
        SaveImage(combinedPreview,"combined-import.png",png);combinedPreview.Close();
        w.ApplyRevitImport(group);Pump();w.Drawing.Fit();Pump();
        Check(model.RevitDrawings.Length==9 && w.Drawing.DrawnRevitPartCount==9 && w.Drawing.DrawnReferenceCount==10,"accepted combined drawing retains every part");
        Check((string)((Button)w.Buttons["Calculate"]).Content=="Calculate all" && w.RevitPartSelector.Items.Count==9,"combined calculation and active-part inspector are explicit");
        var target=w.Drawing.CombinedObjectTargets.Last(h=>h.Item3==FormworkObject.Beam);var point=new Point(target.Item2.Left+target.Item2.Width/2,target.Item2.Top+target.Item2.Height/2);
        Check(w.Drawing.SelectAt(point) && model.ActiveRevitDrawingKey==target.Item1,"clicking a beam activates its own component without hiding the others");Pump();
        Check(w.Drawing.DrawnRevitPartCount==9,"selection retains the whole canvas");
        Click(w,"Undo");Pump();Check(!model.HasCombinedRevitDrawing && model.NetworkBeams.Count==5,"Undo restores the previous full drawing");
        Click(w,"Redo");Pump();Check(model.RevitDrawings.Length==9 && w.Drawing.DrawnRevitPartCount==9,"Redo restores all combined geometry");
        model.TowerLegCapacityKn=70;model.MaximumConcreteEndOverhangMm=500;
        var calculation=w.CalculateAsync();var timeout=System.Diagnostics.Stopwatch.StartNew();
        while(!calculation.IsCompleted && timeout.Elapsed.TotalSeconds<180){Pump();System.Threading.Thread.Sleep(10);}
        if(!calculation.IsCompleted){model.CancelOptimization();throw new Exception("Combined canvas calculation exceeded the UI test timeout.");}
        calculation.GetAwaiter().GetResult();Pump();
        Check(model.IsCalculationValid && model.RevitSceneModels().All(p=>p.Item2.IsCalculationValid),"Calculate all verifies every area and column beam");
        Check(w.Drawing.Formwork && w.Drawing.DrawnRevitPartCount==9 && w.Drawing.DrawnLegCount==model.CombinedLegCount,"one formwork canvas renders all checked towers");
        Check(!w.Drawing.AnnotationBounds.SelectMany((a,i)=>w.Drawing.AnnotationBounds.Skip(i+1).Select(b=>a.IntersectsWith(b))).Any(overlap=>overlap),"labels share one collision check across every drawing part");
        SaveImage(w,"combined-formwork.png",png);
        var member=w.Drawing.CombinedObjectTargets.Last(h=>h.Item3==FormworkObject.Beam);
        Check(w.Drawing.SelectAt(new Point(member.Item2.Left+member.Item2.Width/2,member.Item2.Top+member.Item2.Height/2)),"solved column beam is clickable");Pump();
        Check(w.Diagrams!=null && model.IsCalculationValid,"selecting a component opens its existing member diagrams and preserves results");
        var aligned=RevitStructureImport.Build(RevitAlignmentFixtures.AlignedAreas(),"523732");
        var alignedPreview=new RevitSelectionImportWindow(RevitAlignmentFixtures.AlignedAreas());alignedPreview.Show();Pump();alignedPreview.RefreshPreview();Pump();alignedPreview.PreviewCanvas?.Fit();Pump();
        Check(alignedPreview.ImportButton.IsEnabled && alignedPreview.PreviewCanvas.DrawnRevitPartCount==11 && alignedPreview.PreviewCanvas.DrawnReferenceCount==12,"5 mm alignment repair previews all five clear areas, six column beams and twelve columns");
        Check(alignedPreview.BuildDiagnosticReport("").Contains("Geometry tolerance (mm): 5"),"fixed alignment allowance is diagnosable without an import setting");
        SaveImage(alignedPreview,"aligned-import.png",png);alignedPreview.Close();
        w.ApplyRevitImport(aligned);Pump();w.Drawing.Fit();Pump();
        Check(w.Drawing.DrawnRevitPartCount==11 && model.RevitDrawings.Length==11 && w.RevitPartSelector.Items.Count==11 && (string)((Button)w.Buttons["Calculate"]).Content=="Calculate all","alignment repair reaches the combined working canvas");
        Click(w,"Undo");Pump();Check(w.Drawing.DrawnRevitPartCount==9,"Undo restores the complete previous combined drawing");
        Click(w,"Redo");Pump();Check(w.Drawing.DrawnRevitPartCount==11 && !model.AllowCustomH20Lengths && model.TowerLegCapacityKn==70,"Redo retains all aligned parts and project settings");
        var columnSource=RevitAlignmentFixtures.ColumnTerminations();var columnGroup=RevitStructureImport.Build(columnSource,"523456");
        var perimeterPreview=new RevitSelectionImportWindow(columnSource);perimeterPreview.Show();Pump();perimeterPreview.RefreshPreview();Pump();perimeterPreview.PreviewCanvas?.Fit();Pump();
        Check(perimeterPreview.ImportButton.IsEnabled && perimeterPreview.Result.Areas.Length==12 && perimeterPreview.PreviewCanvas.DrawnRevitPartCount==12 && perimeterPreview.PreviewCanvas.DrawnReferenceCount==12,"76-element selection previews all members and columns together");
        Check(perimeterPreview.BuildDiagnosticReport("").Contains("Detached calculation-part geometry"),"split geometry remains inspectable in Diagnose");
        SaveImage(perimeterPreview,"perimeter-import.png",png);perimeterPreview.Close();
        w.ApplyRevitImport(columnGroup);Pump();w.Drawing.Fit();Pump();
        Check(w.Drawing.DrawnRevitPartCount==12 && w.RevitPartSelector.Items.Count==12,"all column-supported members reach the working canvas");
        string untouchedGeometry=model.NetworkConfigurationXml;
        Check(w.CommitFields() && model.NetworkConfigurationXml==untouchedGeometry,"rounded display values cannot reject or change an untouched span meeting a crossing face");
        var columnTarget=w.Drawing.CombinedObjectTargets.First(h=>h.Item1=="perimeter:523745" && h.Item3==FormworkObject.Beam);
        Check(w.Drawing.SelectAt(new Point(columnTarget.Item2.Left+columnTarget.Item2.Width/2,columnTarget.Item2.Top+columnTarget.Item2.Height/2)),"523745 is selectable as a column-supported member");Pump();
        Check(model.ActiveRevitDrawingKey=="perimeter:523745" && model.NetworkBeams.Count==1 && model.StandaloneDefinition().RevitSource.Contains("523745") && Math.Abs(model.BeamSpanLength(0)-13600)<.001,"selected member retains its Revit ID and 13.6 m column-face span");
        Check(w.Drawing.DrawnRevitPartCount==12,"selecting a perimeter member keeps the complete canvas visible");
        SaveImage(w,"column-member-selected.png",png);
        Click(w,"Undo");Pump();Check(w.Drawing.DrawnRevitPartCount==11,"Undo restores the full earlier alignment drawing");
        Click(w,"Redo");Pump();Check(w.Drawing.DrawnRevitPartCount==12 && model.TowerLegCapacityKn==70 && !model.AllowCustomH20Lengths,"Redo restores every column-supported part and shared settings");
        var junctionSource=RevitJunctionFixtures.Reported();var junctionGroup=RevitStructureImport.Build(junctionSource,"523475");
        var junctionPreview=new RevitSelectionImportWindow(junctionSource);junctionPreview.Show();Pump();junctionPreview.RefreshPreview();Pump();junctionPreview.PreviewCanvas?.Fit();Pump();
        Check(junctionPreview.ImportButton.IsEnabled && junctionPreview.Result.Areas.Length==11 && junctionPreview.PreviewCanvas.DrawnRevitPartCount==11 && junctionPreview.PreviewCanvas.DrawnReferenceCount==12,"75-element junction report previews all five areas, six column beams and twelve columns");
        Check(junctionPreview.BuildDiagnosticReport("").Contains("fills the junction") && junctionPreview.BuildDiagnosticReport("").Contains("Preview status: Ready to import"),"both recognized junctions are ready and explained in Diagnose");
        SaveImage(junctionPreview,"junction-import.png",png);
        var junctionDirection=Find<ComboBox>(junctionPreview).Single(b=>b.SelectedValuePath=="SourceId");junctionDirection.SelectedValue="523885";Pump();junctionPreview.RefreshPreview();Pump();
        Check(junctionPreview.ImportButton.IsEnabled && junctionPreview.PreviewCanvas.DrawnRevitPartCount==11 && string.IsNullOrEmpty(junctionPreview.LastPreviewException),"orthogonal direction previews a large connected area without a saved-identity exception");
        junctionDirection.SelectedValue="523475";Pump();junctionPreview.RefreshPreview();Pump();
        Check(junctionPreview.ImportButton.IsEnabled && junctionPreview.Result.Areas.Count(a=>a.Result.Configuration.Bays.Length>0)==5,"changing direction back restores all five clear areas");junctionPreview.Close();
        w.ApplyRevitImport(junctionGroup);Pump();w.Drawing.Fit();Pump();
        Check(w.Drawing.DrawnRevitPartCount==11 && w.RevitPartSelector.Items.Count==11,"junctions reach the combined working canvas");
        var junctionArea=junctionGroup.Areas.Single(a=>a.SourceIds.Contains("523625"));
        var junctionTarget=w.Drawing.CombinedObjectTargets.First(h=>h.Item1==junctionArea.Key && h.Item3==FormworkObject.Beam && h.Item4==1);
        Check(w.Drawing.SelectAt(new Point(junctionTarget.Item2.Left+junctionTarget.Item2.Width/2,junctionTarget.Item2.Top+junctionTarget.Item2.Height/2)),"joined middle row is selectable");Pump();
        Check(model.NetworkBeams.Count==3 && model.NetworkBays.Count==2 && model.NetworkBeams[1].RevitSource.Contains("523625") && model.NetworkBeams[1].RevitSource.Contains("523706"),"selection shows one middle row with both original Revit IDs");
        Check(w.Drawing.DrawnRevitPartCount==11 && w.CommitFields(),"selecting the joined row keeps every part visible and its dimensions valid");
        SaveImage(w,"junction-row-selected.png",png);
        Click(w,"Undo");Pump();Check(w.Drawing.DrawnRevitPartCount==12,"junction import Undo restores the previous twelve-part drawing");
        Click(w,"Redo");Pump();Check(w.Drawing.DrawnRevitPartCount==11 && model.TowerLegCapacityKn==70 && !model.AllowCustomH20Lengths,"junction import Redo retains every part and project setting");
        var largeSource=RevitSelectionSizeFixtures.Reported();var largeGroup=RevitStructureImport.Build(largeSource,"523462");
        var largePreview=new RevitSelectionImportWindow(largeSource);largePreview.Show();Pump();
        var largeDirection=Find<ComboBox>(largePreview).Single(b=>b.SelectedValuePath=="SourceId");largeDirection.SelectedValue="523462";Pump();largePreview.RefreshPreview();Pump();largePreview.PreviewCanvas?.Fit();Pump();
        Check(largePreview.ImportButton.IsEnabled && largePreview.Result.Areas.Length==12 && largePreview.PreviewCanvas.DrawnReferenceCount==12,"90-element selection previews all twelve parts and columns without the old 45-beam gate");
        Check(largePreview.PreviewCanvas.DrawnRevitPartCount==12 && largePreview.BuildDiagnosticReport("").Contains("524539"),"merged crossing sources remain diagnosable in the complete preview");
        SaveImage(largePreview,"large-selection-import.png",png);largeDirection.SelectedValue="523473";Pump();largePreview.RefreshPreview();Pump();
        Check(largePreview.ImportButton.IsEnabled && largePreview.PreviewCanvas.DrawnRevitPartCount==12 && string.IsNullOrEmpty(largePreview.LastPreviewException),"switching the main direction preserves the complete large selection");largePreview.Close();
        w.ApplyRevitImport(largeGroup);Pump();w.Drawing.Fit();Pump();
        Check(w.Drawing.DrawnRevitPartCount==12 && w.RevitPartSelector.Items.Count==12,"all twelve large-selection parts reach the working canvas");
        var mergedArea=largeGroup.Areas.Single(a=>a.Result.Configuration.CrossingBeams.Any(c=>c.RevitSource.Contains("524539")));
        var mergedTarget=w.Drawing.CombinedObjectTargets.First(h=>h.Item1==mergedArea.Key && h.Item3==FormworkObject.Crossing && h.Item4==0);
        Check(w.Drawing.SelectAt(new Point(mergedTarget.Item2.Left+mergedTarget.Item2.Width/2,mergedTarget.Item2.Top+mergedTarget.Item2.Height/2)),"joined crossing is selectable on the combined canvas");Pump();
        Check(model.ActiveRevitDrawingKey==mergedArea.Key && model.CrossingBeams.Single().RevitSource.Contains("523473") && model.CrossingBeams.Single().RevitSource.Contains("524539"),"crossing selection retains both original Revit pieces");
        SaveImage(w,"large-crossing-selected.png",png);
        Click(w,"Undo");Pump();Check(w.Drawing.DrawnRevitPartCount==11,"large-selection Undo restores the complete previous canvas");
        Click(w,"Redo");Pump();Check(w.Drawing.DrawnRevitPartCount==12 && model.TowerLegCapacityKn==70 && !model.AllowCustomH20Lengths,"large-selection Redo retains every component and project setting");
        var obstructed=RevitColumnImportTests.ReportedSelection();foreach(var p in obstructed.Boundaries[1].Outline)p.Y+=20;
        var obstacle=new RevitSelectionImportWindow(obstructed);Check(!obstacle.ImportButton.IsEnabled && obstacle.BuildDiagnosticReport("").Contains("intersects the bay interior"),"genuine interior obstruction remains blocked and diagnosable");obstacle.Close();
        var empty=new RevitSelectionImportWindow(new RevitSelectionSnapshot());Check(!empty.ImportButton.IsEnabled && empty.PreviewCanvas==null,"empty selection cannot replace inputs");empty.Close();
        string copied=null;
        var blockedSource=RevitStructureImportTests.Sample();blockedSource.Beams[0].WidthMm=0;
        var blocked=new RevitSelectionImportWindow(blockedSource) {CopyDiagnosticText=text=>copied=text};blocked.Show();Pump();
        Check(blocked.DiagnoseButton.IsEnabled && !blocked.ImportButton.IsEnabled,"Diagnose stays enabled when geometry is blocked");
        blocked.DiagnoseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(blocked.Diagnostics!=null && blocked.Diagnostics.Title=="Revit drawing import diagnosis","Diagnose opens the import report window");
        blocked.Diagnostics.RemarksBox.Text="Cannot import this selection";blocked.Diagnostics.CopyReport();
        Check(copied.Contains("enter a valid width") && copied.Contains("Cannot import this selection") && copied.Contains("101"),"copy includes selection, rejection and remarks");
        var diagnostic=blocked.Diagnostics;blocked.DiagnoseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(ReferenceEquals(diagnostic,blocked.Diagnostics),"repeat Diagnose reuses its report window");
        blocked.Activate();var table=Find<DataGrid>((DependencyObject)blocked.Content).First();table.SelectedItem=table.Items[0];table.CurrentCell=new DataGridCellInfo(table.Items[0],table.Columns.Single(c=>Equals(c.Header,"Width (mm)")));table.Focus();Check(table.BeginEdit(),"review cell can be edited");Pump();
        var rawInput=Find<TextBox>(table).Single(box=>box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="WidthMm");rawInput.Text="invalid width";rawInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();
        Check(blocked.BuildDiagnosticReport("").Contains("raw text: invalid width") && !blocked.ImportButton.IsEnabled,"diagnose captures invalid text rather than just its last valid numeric value");
        blocked.Close();Pump();Check(!diagnostic.IsVisible,"closing import closes its diagnostic child");
        var fault=new RevitSelectionImportWindow(RevitStructureImportTests.Sample()) {BuildPreview=(s,id)=>throw new InvalidOperationException("Unexpected preview fixture")};
        fault.RefreshPreview();Check(!fault.ImportButton.IsEnabled && fault.DiagnoseButton.IsEnabled,"unexpected preview failure retains Diagnose");
        Check(fault.BuildDiagnosticReport("").Contains("Unexpected preview fixture"),"unexpected exception is copyable without a preview result");fault.Close();
        var readFailure=new BeamFormworkWorkspace(model,null,null,null,readRevitSelection:()=>throw new InvalidOperationException("Revit reader fixture"));readFailure.Show();Pump();
        readFailure.ShowRevitImportPreview=dialog=> {Check(dialog.DiagnoseButton.IsEnabled && dialog.BuildDiagnosticReport("").Contains("Revit reader fixture"),"reader exceptions still open a diagnosable import window");Check(!dialog.BuildDiagnosticReport("").Contains("Select at least one straight"),"a reader failure cannot ask the user to reselect a beam that was never read");dialog.Close();return false;};
        Click(readFailure,"FromRevit");readFailure.Close();
        var missing=RevitStructureImportTests.Sample();missing.Beams[0].WidthMm=0;
        var edit=new RevitSelectionImportWindow(missing);Check(!edit.ImportButton.IsEnabled,"missing dimension blocks import");missing.Beams[0].WidthMm=800;edit.RefreshPreview();Check(edit.ImportButton.IsEnabled,"reviewed dimension enables import");edit.Close();
        var longWall=RevitStructureImportTests.Sample();longWall.Boundaries[0].Outline=RevitStructureImportTests.Rect(-200,300,-100000,100000);
        var fit=new RevitSelectionImportWindow(longWall);fit.Show();Pump();fit.PreviewCanvas.Fit();Pump();Check(fit.PreviewCanvas.SceneBounds.Height<6000,"a long boundary wall cannot squash the imported beam view");fit.Close();
        w.ConfirmNewSession=()=>true;w.Buttons["NewSession"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();Check(model.RevitContext==null && w.Drawing.DrawnReferenceCount==0,"new session clears boundary context");w.Close();
        Console.WriteLine("Passed "+checks+" Revit selection preview/canvas checks.");
    }
    private static void SaveImage(Window window,string name,string sample)
    {
        if(sample==null)return;
        var content=(FrameworkElement)window.Content;content.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(Path.GetDirectoryName(sample),name)))encoder.Save(file);
    }
}
