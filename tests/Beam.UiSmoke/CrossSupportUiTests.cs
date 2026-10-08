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
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Cross UI: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    public static int Run(string previewPath=null)
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,HasRightSlab=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=2450,UpperSupportSpacingMm=100,MaximumConcreteEndOverhangMm=0,TowerLegCapacityKn=70};
        foreach(var b in vm.NetworkBeams) {b.WidthMm=300;b.DepthMm=600;}
        vm.NetworkBays[0].WidthMm=3900;vm.NetworkBays[0].ThicknessMm=100;
        vm.AddCrossingBeam();var cross=vm.CrossingBeams[0];cross.PositionMm=1225;cross.WidthMm=400;cross.DepthMm=150;cross.BottomElevationMm=-250;cross.SupportMode=CrossBeamSupportMode.ExistingSupportsOnly;
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Cross UI search timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        var section=new TransferNetworkPlan {Width=1200,SectionOnly=true,IsSelectionEnabled=true};section.SetResult(vm.NetworkResult);section.Measure(new Size(1200,420));section.Arrange(new Rect(0,0,1200,420));section.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1200,420,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(section);
        Check(section.DrawnH20ProfileCount>=vm.NetworkResult.Beams.Length*5 && section.DrawnPrimarySectionCount==vm.NetworkResult.Beams.Length*4,"section shows real flange/web profiles and both paired primary members");
        Check(section.MemberTargets.Any(t=>t.Item2<0),"cross members selectable in section");
        var bearing=vm.NetworkResult.CrossSupports[0].Bearings[0];Check(bearing.FillerHeightMm==150,"section filler uses the independently computed gap");
        var diagrams=new TowerDiagramWindow(vm,vm.NetworkResult.CrossSupports[0]);diagrams.SelectMember(0,0,-1);
        Check(diagrams.SelectedTransferMember==vm.NetworkResult.CrossSupports[0].Members[0] && diagrams.ActualLoadedAnalysis==diagrams.LoadedAnalysis,"cross diagram uses its actual concrete and formwork loads");diagrams.Close();
        var canvas=new BeamFormworkCanvas(vm) {Width=1200,Height=600,Formwork=true};canvas.Measure(new Size(1200,600));canvas.Arrange(new Rect(0,0,1200,600));canvas.Fit();canvas.UpdateLayout();
        var planBitmap=new RenderTargetBitmap(1200,600,96,96,System.Windows.Media.PixelFormats.Pbgra32);planBitmap.Render(canvas);
        Check(canvas.MemberTargets.Any(t=>t.Item2<0) && canvas.DrawnLegCount==vm.NetworkResult.LegCount && canvas.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.H20Count),"workspace cross H20 targets and calculated tower legs drawn");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Drawing.Select(FormworkObject.Crossing,0);workspace.Show();Pump();
        Check(workspace.Fields.ContainsKey("BottomElevationMm"),"cross bottom elevation exposed in workspace");
        workspace.Drawing.Select(FormworkObject.Slab,0);Pump();
        Check(workspace.SelectedResultText.Contains("Equal foot setback") && workspace.SelectedResultText.Contains("repeat every row") && workspace.SelectedResultText.Contains("Maximum row spacing 100"),"workspace exposes the repeated symmetric slab support detail and spacing");
        var slab=vm.NetworkResult.Beams[0].Transfer.UpperMembers.Single(m=>m.Name=="Upper right");
        Check(Math.Abs(slab.SupportsMm[0]+slab.SupportsMm[1]-slab.LengthMm)<1e-7 && Math.Abs(slab.Analysis.Reactions[0]-slab.Analysis.Reactions[1])<1e-7,"drawn slab member has symmetric supports and actual equal uniform-load reactions");
        workspace.Close();
        var junction=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=10800,UpperSupportSpacingMm=275,MaximumConcreteEndOverhangMm=75,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        junction.NetworkBeams[0].WidthMm=650;junction.NetworkBeams[0].DepthMm=1150;junction.NetworkBeams[0].BottomElevationMm=-1150;
        junction.NetworkBeams[1].WidthMm=800;junction.NetworkBeams[1].DepthMm=1750;junction.NetworkBeams[1].BottomElevationMm=-1750;
        junction.NetworkBays[0].WidthMm=2575;junction.NetworkBays[0].ThicknessMm=300;junction.AddCrossingBeam();junction.CrossingBeams[0].PositionMm=5375;junction.CrossingBeams[0].WidthMm=650;junction.CrossingBeams[0].DepthMm=1150;junction.CrossingBeams[0].BottomElevationMm=-1150;
        task=junction.OptimizeTowersAsync();timer.Restart();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Junction UI search timeout");}task.GetAwaiter().GetResult();
        Check(junction.IsCalculationValid,junction.WarningMessage+" "+junction.TowerFailureDetails);
        var jointWorkspace=new BeamFormworkWorkspace(junction,null,null,null);jointWorkspace.Show();jointWorkspace.Drawing.Formwork=true;jointWorkspace.Drawing.Select(FormworkObject.Crossing,0);Pump();
        var jointCanvas=jointWorkspace.Drawing;jointCanvas.Measure(new Size(1200,600));jointCanvas.Arrange(new Rect(0,0,1200,600));jointCanvas.Fit();jointCanvas.UpdateLayout();
        var jointBitmap=new RenderTargetBitmap(1200,600,96,96,System.Windows.Media.PixelFormats.Pbgra32);jointBitmap.Render(jointCanvas);
        Check(jointCanvas.DrawnLegCount==junction.NetworkResult.LegCount && jointCanvas.DrawnCrossH20Count==junction.NetworkResult.CrossSupports.Sum(c=>c.H20Count),"junction plan draws the actual central tower and trimmed members with no duplicate end-zone timber");
        Check(jointCanvas.LegTargets.Any(t=>t.Item2.Contains("concrete end zone on B1")),"plan end-zone targets describe concrete loads rather than timber bearings");
        Check(jointWorkspace.SelectedResultText.Contains("Dedicated central span") && jointWorkspace.SelectedResultText.Contains("Concrete end zone on B1"),"selected crossing explains which load reaches the main beam");
        var jointSection=new TransferNetworkPlan {Width=1200,SectionOnly=true,IsSelectionEnabled=true};jointSection.SetResult(junction.NetworkResult);jointSection.Measure(new Size(1200,420));jointSection.Arrange(new Rect(0,0,1200,420));jointSection.UpdateLayout();var jointSectionBitmap=new RenderTargetBitmap(1200,420,96,96,System.Windows.Media.PixelFormats.Pbgra32);jointSectionBitmap.Render(jointSection);Check(jointSection.MemberTargets.Any(t=>t.Item2<0),"junction central members are selectable in section");
        Check(jointSection.DrawnConcreteEndZoneCount==1,"section draws one end zone on the receiving beam");
        jointWorkspace.Drawing.Select(FormworkObject.Beam,0);Pump();jointWorkspace.Fields["DepthMm"].Text="1200";Check(jointWorkspace.CommitFields() && junction.NetworkBeams[0].BottomElevationMm==-1200,"workspace depth edit keeps the concrete top at zero");
        if(!string.IsNullOrEmpty(previewPath))
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(jointBitmap));using(var file=System.IO.File.Create(System.IO.Path.ChangeExtension(previewPath,"junction-plan.png")))encoder.Save(file);
            encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(jointSectionBitmap));using(var file=System.IO.File.Create(System.IO.Path.ChangeExtension(previewPath,"junction-section.png")))encoder.Save(file);
        }
        jointWorkspace.Close();Console.WriteLine("Cross UI: section, filler, actual diagrams, elevation editor and reported junction passed.");return checks;
    }
}
