using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class RevitSelectionUiTests
{
    private static int checks;
    private static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    private static void Click(BeamFormworkWorkspace w,string name)=>w.Buttons[name].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        var empty=new RevitSelectionImportWindow(new RevitSelectionSnapshot());Check(!empty.ImportButton.IsEnabled && empty.PreviewCanvas==null,"empty selection cannot replace inputs");empty.Close();
        var missing=RevitStructureImportTests.Sample();missing.Beams[0].WidthMm=0;
        var edit=new RevitSelectionImportWindow(missing);Check(!edit.ImportButton.IsEnabled,"missing dimension blocks import");missing.Beams[0].WidthMm=800;edit.RefreshPreview();Check(edit.ImportButton.IsEnabled,"reviewed dimension enables import");edit.Close();
        w.ConfirmNewSession=()=>true;w.Buttons["NewSession"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();Check(model.RevitContext==null && w.Drawing.DrawnReferenceCount==0,"new session clears boundary context");w.Close();
        Console.WriteLine("Passed "+checks+" Revit selection preview/canvas checks.");
    }
}
