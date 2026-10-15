using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class WS10UiTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("WS10 UI: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T item)yield return item;
        foreach(object child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject obj)foreach(var found in Find<T>(obj))yield return found;
    }
    static void Click(BeamFormworkWorkspace view,string name){view.Buttons[name].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    static void Calculate(BeamFormworkWorkspace view)
    {
        var task=view.CalculateAsync();var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted){Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("WS10 UI calculation timeout");}
        task.GetAwaiter().GetResult();Pump();Check(view.Model.IsCalculationValid,"steel calculation returns a checked layout: "+view.Model.WarningMessage);
    }
    static void Preview(FrameworkElement element,string path)
    {
        if(path==null)return;element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);
    }
    internal static int Run(string output)
    {
        var model=new BeamCalculatorViewModel {BeamLengthMm=2500,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=75,AllowCustomH20Lengths=true,IgnoreUplift=true};
        var host=new BeamCalculatorWindow {DataContext=model,Width=1280,Height=860};
        try
        {
            host.Show();Pump();((Button)host.FindName("PickTowersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            var view=host.FormworkWorkspace;model.TransferSlabLoads=true;model.UseBeamNetwork=true;
            foreach(var beam in model.NetworkBeams){beam.WidthMm=300;beam.SetDepthKeepingTopMm(600);}
            model.NetworkBays[0].WidthMm=2900;model.NetworkBays[0].ThicknessMm=100;Pump();
            var selector=Find<ComboBox>(view.SharedProperties).Single(c=>c.Name=="PrimaryMaterialSelector");
            Check(Equals(selector.SelectedValue,PrimaryBeamMaterial.DoubleH20),"legacy/default material is double H20");
            selector.SelectedValue=PrimaryBeamMaterial.WS10;Pump();
            Check(model.PrimaryMaterial==PrimaryBeamMaterial.WS10 && !model.IsCalculationValid,"workspace selector updates material and invalidates stale results");
            Check(Equals(((ComboBox)host.FindName("PrimaryMaterialInput")).SelectedValue,PrimaryBeamMaterial.WS10),"classic binding stays synchronized");
            Check(Find<TextBlock>(view.SharedProperties).Any(t=>t.Text.Contains("uncut catalogue")),"stock-only rule appears beside the material choice");
            Click(view,"Undo");Check(model.PrimaryMaterial==PrimaryBeamMaterial.DoubleH20,"material change participates in undo");
            Click(view,"Redo");Check(model.PrimaryMaterial==PrimaryBeamMaterial.WS10,"material change participates in redo");
            Calculate(view);var result=model.NetworkResult;
            Check(result.WS10Count>0 && result.Beams.All(b=>b.Primary.IsWS10) && view.NetworkResultText=="" && view.SelectedResultText.Contains("Efficiency:"),"steel retains its quantities and primary profile while the selection card shows load and efficiency");
            Check(view.Drawing.AnnotationLabels.Any(t=>t.StartsWith("WS10 · ")),"plan length labels identify WS10");
            var colour=((SolidColorBrush)PrimaryDrawing.WS10Colour).Color;Check(colour.B>colour.R && colour!=Colors.Goldenrod,"steel has its own blue drawing colour");
            Preview(view.Surface,output==null ? null : Path.ChangeExtension(output,"ws10-plan.png"));
            var hit=view.Drawing.MemberTargets.First(t=>t.Item2==0 && t.Item3==0);view.ShowMember(hit.Item2,hit.Item3,hit.Item4);Pump();
            var member=view.Diagrams.SelectedTransferMember;
            Check(view.Diagrams.Title.Contains("WS10") && member.IsWS10 && ReferenceEquals(view.Diagrams.LoadedAnalysis,member.Analysis),"clicking a steel primary opens its actual response diagrams");
            var independent=LoadedBeamAnalysis.Analyze(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,865.2,350);
            Check(Math.Abs(independent.DeflectionMm-member.Analysis.DeflectionMm)<1e-8,"steel diagrams use WS10 stiffness");
            Check(Find<TextBlock>(view.Diagrams).Any(t=>t.Text.Contains("WS10 M/V interaction utilization")),"steel interaction and reduced end-zone capacities are explained");
            Preview((FrameworkElement)view.Diagrams.Content,output==null ? null : Path.ChangeExtension(output,"ws10-diagrams.png"));view.Diagrams.Close();
            Click(view,"Section");view.SectionPosition.Value=model.NetworkResult.Beams[0].Transfer.RowsMm[0];view.Section.UpdateLayout();Pump();
            Check(view.Section.DrawnWS10ProfileCount>0 && view.Section.DrawnH20ProfileCount>0,"section draws steel channels and timber secondaries separately");
            Preview(view.Surface,output==null ? null : Path.ChangeExtension(output,"ws10-section.png"));
            Click(view,"Geometry");var record=host.CaptureDrawingInputs();Check(record.Inputs.Single(f=>f.Name=="PrimaryMaterial").Value=="WS10","saved drawing records the material");
            for(int i=0;i<20;i++)model.AddNetworkBay();Pump();Check(model.NetworkBeams.Count==15 && !model.CanAddNetworkBay,"drawing editing stops at fifteen parallel beams");
            var fifteen=host.CaptureDrawingInputs();host.RestoreCalculation(record);host.RestoreCalculation(fifteen);Pump();
            Check(model.PrimaryMaterial==PrimaryBeamMaterial.WS10 && model.NetworkBeams.Count==15,"material and fifteen-beam drawing round-trip");
            var invalid=new CalculationRecord {Kind=record.Kind,Version=record.Version,Inputs=record.Inputs.Select(f=>new CalculationField {Name=f.Name,Value=f.Value}).ToList()};invalid.Inputs.Single(f=>f.Name=="PrimaryMaterial").Value="99";
            bool rejected=false;try {host.RestoreCalculation(invalid);}catch(ArgumentException){rejected=true;}
            Check(rejected && model.PrimaryMaterial==PrimaryBeamMaterial.WS10 && model.NetworkBeams.Count==15,"unknown saved material rejected before changing the live model");
            record.Inputs.RemoveAll(f=>f.Name=="PrimaryMaterial");host.RestoreCalculation(record);Pump();Check(model.PrimaryMaterial==PrimaryBeamMaterial.DoubleH20,"older files without material restore as double H20");
            Console.WriteLine($"Passed {checks} WS10 drawing and persistence UI checks.");return checks;
        }
        finally {host.Close();Pump();}
    }
}
