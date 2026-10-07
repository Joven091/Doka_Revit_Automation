using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;
internal static class CrossSupportUiTests
{
    static void Check(bool value,string message) {if(!value)throw new Exception("Cross UI: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    public static void Run()
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,HasRightSlab=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=2450,UpperSupportSpacingMm=200,MaximumConcreteEndOverhangMm=0,TowerLegCapacityKn=70};
        foreach(var b in vm.NetworkBeams) {b.WidthMm=300;b.DepthMm=600;}
        vm.NetworkBays[0].WidthMm=3900;vm.NetworkBays[0].ThicknessMm=100;
        vm.AddCrossingBeam();var cross=vm.CrossingBeams[0];cross.PositionMm=1225;cross.WidthMm=400;cross.DepthMm=150;cross.BottomElevationMm=-250;cross.SupportMode=CrossBeamSupportMode.ExistingSupportsOnly;
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Cross UI search timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        var section=new TransferNetworkPlan {Width=1200,SectionOnly=true,IsSelectionEnabled=true};section.SetResult(vm.NetworkResult);section.Measure(new Size(1200,420));section.Arrange(new Rect(0,0,1200,420));section.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1200,420,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(section);
        Check(section.MemberTargets.Any(t=>t.Item2<0),"cross members selectable in section");
        var bearing=vm.NetworkResult.CrossSupports[0].Bearings[0];Check(bearing.FillerHeightMm==150,"section filler uses the independently computed gap");
        var diagrams=new TowerDiagramWindow(vm,vm.NetworkResult.CrossSupports[0]);diagrams.SelectMember(0,0,-1);
        Check(diagrams.SelectedTransferMember==vm.NetworkResult.CrossSupports[0].Members[0] && diagrams.ActualLoadedAnalysis==diagrams.LoadedAnalysis,"cross diagram uses its actual concrete and formwork loads");diagrams.Close();
        var canvas=new BeamFormworkCanvas(vm) {Width=1200,Height=600,Formwork=true};canvas.Measure(new Size(1200,600));canvas.Arrange(new Rect(0,0,1200,600));canvas.Fit();canvas.UpdateLayout();
        var planBitmap=new RenderTargetBitmap(1200,600,96,96,System.Windows.Media.PixelFormats.Pbgra32);planBitmap.Render(canvas);
        Check(canvas.MemberTargets.Any(t=>t.Item2<0) && canvas.DrawnLegCount==vm.NetworkResult.LegCount && canvas.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.H20Count),"workspace cross H20 targets and calculated tower legs drawn");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Drawing.Select(FormworkObject.Crossing,0);workspace.Show();Pump();
        Check(workspace.Fields.ContainsKey("BottomElevationMm"),"cross bottom elevation exposed in workspace");
        workspace.Close();Console.WriteLine("Cross UI: section, filler, actual diagrams and elevation editor passed.");
    }
}
