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
        int reads=0;var w=new BeamFormworkWorkspace(model,null,null,null,readRevitSelection:()=> {reads++;return RevitStructureImportTests.Sample();});w.Show();Pump();
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
        readFailure.ShowRevitImportPreview=dialog=> {Check(dialog.DiagnoseButton.IsEnabled && dialog.BuildDiagnosticReport("").Contains("Revit reader fixture"),"reader exceptions still open a diagnosable import window");dialog.Close();return false;};
        Click(readFailure,"FromRevit");readFailure.Close();
        var missing=RevitStructureImportTests.Sample();missing.Beams[0].WidthMm=0;
        var edit=new RevitSelectionImportWindow(missing);Check(!edit.ImportButton.IsEnabled,"missing dimension blocks import");missing.Beams[0].WidthMm=800;edit.RefreshPreview();Check(edit.ImportButton.IsEnabled,"reviewed dimension enables import");edit.Close();
        var longWall=RevitStructureImportTests.Sample();longWall.Boundaries[0].Outline=RevitStructureImportTests.Rect(-200,300,-100000,100000);
        var fit=new RevitSelectionImportWindow(longWall);fit.Show();Pump();fit.PreviewCanvas.Fit();Pump();Check(fit.PreviewCanvas.SceneBounds.Height<6000,"a long boundary wall cannot squash the imported beam view");fit.Close();
        w.ConfirmNewSession=()=>true;w.Buttons["NewSession"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();Check(model.RevitContext==null && w.Drawing.DrawnReferenceCount==0,"new session clears boundary context");w.Close();
        Console.WriteLine("Passed "+checks+" Revit selection preview/canvas checks.");
    }
}
