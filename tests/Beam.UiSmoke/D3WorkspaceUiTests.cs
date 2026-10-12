using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard;

internal static class D3WorkspaceUiTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("D3 workspace: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    static void Click(BeamFormworkWorkspace view,string key){view.Buttons[key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    public static int Run(string output)
    {
        var model=new BeamCalculatorViewModel {BeamLengthMm=6750,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=75,AllowCustomH20Lengths=true,IgnoreUplift=true};
        var host=new BeamCalculatorWindow {DataContext=model,Width=1280,Height=860};
        try
        {
            host.Show();Pump();
            ((Button)host.FindName("PickTowersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            var view=host.FormworkWorkspace;
            Check(view!=null && ReferenceEquals(host.Content,view.Surface) && view.HostWindow==host,"D3 selection opens the drawing in the existing calculator window");
            Check(view.Surface.IsVisible && host.OwnedWindows.Count==0,"there is one visible D3 calculator with no duplicate workspace window");
            Check(view.Surface.TryFindResource("PrimaryAction") is Style,"drawing uses the shared calculator theme");
            model.TransferSlabLoads=true;model.UseBeamNetwork=true;
            foreach(var beam in model.NetworkBeams){beam.WidthMm=650;beam.SetDepthKeepingTopMm(1150);}
            model.NetworkBays[0].WidthMm=3550;model.NetworkBays[0].ThicknessMm=300;Pump();
            var task=view.CalculateAsync();var clock=System.Diagnostics.Stopwatch.StartNew();
            while(!task.IsCompleted){Pump();Thread.Sleep(1);if(clock.Elapsed.TotalSeconds>90)throw new Exception("D3 calculation timeout");}task.GetAwaiter().GetResult();Pump();
            Check(model.IsCalculationValid,"drawing calculates a checked D3 network");
            var result=model.NetworkResult;
            Check(view.Drawing.DrawnSecondaryH20Count==0,"secondary H20s start hidden");
            Click(view,"SecondaryH20s");view.Drawing.UpdateLayout();Pump();
            Check(view.Drawing.DrawnSecondaryH20Count>0 && ReferenceEquals(result,model.NetworkResult),"visibility button reveals members without recalculation");
            var hit=view.Drawing.MemberTargets.First(t=>t.Item2>=0 && t.Item3>=result.Beams[t.Item2].StockPlan.Pieces.Length);
            view.ShowMember(hit.Item2,hit.Item3,hit.Item4);Pump();
            Check(view.Diagrams.Owner==host && view.Diagrams.SelectedTransferMember.H20Count==1 && view.Diagrams.LoadedAnalysis.DeflectionEvaluated,"a visible secondary opens its checked diagrams owned by the calculator");
            view.Diagrams.Close();Click(view,"SecondaryH20s");view.Drawing.UpdateLayout();Pump();
            Check(view.Drawing.DrawnSecondaryH20Count==0 && ReferenceEquals(result,model.NetworkResult),"hide preserves the checked solution");
            if(output!=null)
            {
                var surface=view.Surface;surface.UpdateLayout();int width=(int)surface.ActualWidth,height=(int)surface.ActualHeight;
                var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
                using(var file=File.Create(Path.ChangeExtension(output,"d3-main-workspace.png")))png.Save(file);
            }
            model.BeamLengthMm=4500;model.CustomH20Scope=CustomH20LengthScope.SlabSupports;model.Exclude600Standalone=true;
            foreach(var beam in model.NetworkBeams){beam.WidthMm=800;beam.SetDepthKeepingTopMm(1750);}
            model.NetworkBays[0].WidthMm=2175;
            model.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=1,PositionMm=2250,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.DedicatedTowers});Pump();
            task=view.CalculateAsync();clock.Restart();
            while(!task.IsCompleted){Pump();Thread.Sleep(1);if(clock.Elapsed.TotalSeconds>90)throw new Exception("Local shutter calculation timeout");}task.GetAwaiter().GetResult();Pump();
            view.Drawing.Fit();view.Drawing.InvalidateVisual();view.Drawing.UpdateLayout();Pump();
            Check(model.IsCalculationValid && model.NetworkResult.CrossSupports.Single().HasReducedShutterClearance,"drawing calculates the local exception through the normal model flow");
            Check(view.Drawing.AnnotationLabels.Any(t=>t.Contains("shutter allowance") && t.Contains("175") && t.Contains("exception")),"175 mm exception is visible on the plan with ordinary dimensions hidden");
            Check(model.NetworkSchedule.Contains("175 mm") && model.NetworkSchedule.Contains("local side-shutter exception"),"schedule reports the exact local allowance");
            if(output!=null)
            {
                var surface=view.Surface;surface.UpdateLayout();var image=new RenderTargetBitmap((int)surface.ActualWidth,(int)surface.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
                using(var file=File.Create(Path.ChangeExtension(output,"local-shutter-exception.png")))png.Save(file);
            }
            model.CustomH20Scope=CustomH20LengthScope.BeamSupports;Pump();
            Check(Equals(((ComboBox)host.FindName("CustomH20ScopeSelector")).SelectedValue,CustomH20LengthScope.BeamSupports),"calculator bindings remain synchronized while its drawing surface is displayed");
            Click(view,"ChangeSystem");
            Check(host.FormworkWorkspace==null && ReferenceEquals(host.Content,host.ClassicCalculatorContent) && !model.HasSelectedSupportSystem,"change system restores the support picker and releases the workspace");
            ((Button)host.FindName("PickPropsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(!model.UseTowers && model.HasSelectedSupportSystem && host.FormworkWorkspace==null,"props keep their calculator flow");
            Console.WriteLine($"Passed {checks} D3 main workspace UI checks.");return checks;
        }
        finally {host.Close();Pump();}
    }
}
