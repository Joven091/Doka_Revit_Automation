using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
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
    static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) {var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    static IEnumerable<Drawing> Primitives(Drawing drawing)
    {
        if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var item in Primitives(child))yield return item;
        else yield return drawing;
    }
    static RenderTargetBitmap Capture(FrameworkElement element,int width,int height,string path=null)
    {
        element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        if(path!=null) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(path))encoder.Save(file);}return bitmap;
    }
    static string Preview(string path,string name)=>string.IsNullOrEmpty(path) ? null : System.IO.Path.ChangeExtension(path,name+".png");
    static void CoordinatedWorkspace(string previewPath)
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=6900,UpperSupportSpacingMm=275,TowerLegCapacityKn=70,StockLengthsText="1250; 1800; 2450; 2650; 2900; 3300; 3600; 3900; 4500; 4900",AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        while(vm.NetworkBeams.Count<8)vm.AddNetworkBay();
        for(int i=0;i<8;i++) {vm.NetworkBeams[i].WidthMm=i%2==0 ? 800 : 650;vm.NetworkBeams[i].DepthMm=i%2==0 ? 1750 : 1150;vm.NetworkBeams[i].BottomElevationMm=-vm.NetworkBeams[i].DepthMm;}
        var widths=new[] {2677.0,1875,3275,2275,2278,2272,2252};
        for(int i=0;i<7;i++)
        {
            vm.NetworkBays[i].WidthMm=widths[i];vm.NetworkBays[i].ThicknessMm=300;vm.AddCrossingBeam();
            var c=vm.CrossingBeams[i];c.BayNumber=i+1;c.PositionMm=3450;c.WidthMm=650;c.DepthMm=1150;c.BottomElevationMm=-1150;
        }
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>300)throw new Exception("Coordinated report UI timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        Check(vm.UpperSupportSpacingMm==275 && vm.NetworkResult.Input.UpperSpacingMm<=275 && vm.BuildDiagnosticReport().Contains("entered maximum 275"),"coordinated search preserves the entered maximum and reports its actual spacing");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Show();Pump();workspace.Drawing.Formwork=true;
        Capture(workspace.Drawing,2000,1100);workspace.Drawing.Fit();Capture(workspace.Drawing,2000,1100,Preview(previewPath,"coordinated-report-plan"));
        Check(workspace.Drawing.DrawnLegCount==vm.NetworkResult.LegCount && workspace.Drawing.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.H20Count),"coordinated plan draws every real tower leg and crossing member");
        workspace.Drawing.Select(FormworkObject.Slab,1);Pump();Check(workspace.SelectedResultText.Contains("entered maximum 275"),"slab inspector explains tighter junction spacing");
        workspace.Buttons["Section"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(int i=0;i<vm.NetworkResult.CrossSupports.Length;i++)
        {
            workspace.Section.SelectMember(-i-1,-1,0);Pump();Capture(workspace.Section,2000,420,Preview(previewPath,$"coordinated-report-X{i+1}-section"));
            Check(workspace.Section.DrawnConcreteEndZoneCount==1,$"X{i+1} section draws its direct concrete junction end zone");
        }
        int crossing=Array.FindIndex(vm.NetworkResult.CrossSupports,c=>Math.Abs(c.TowerSpanOffsetMm)>0);var support=vm.NetworkResult.CrossSupports[crossing];
        workspace.Section.SelectMember(-crossing-1,-1,0);Pump();Capture(workspace.Section,2000,420,Preview(previewPath,"coordinated-report-section"));
        var target=workspace.Section.MemberTargets.First(t=>t.Item2==-crossing-1 && support.Members[t.Item3].H20Count==2);
        workspace.Section.SelectAt(new Point(target.Item1.X+target.Item1.Width/2,target.Item1.Y+target.Item1.Height/2));Pump();
        Check(workspace.Diagrams!=null && Descendants(workspace.Diagrams).Count(c=>c.GetType().Name=="LoadedResponsePlot")==3,"translated crossing supports retain all three response diagrams");
        Check(workspace.Diagrams.DisplayedTransferMember.SupportsMm.SequenceEqual(support.Members[target.Item3].SupportsMm),"diagram uses the actual displaced support positions");
        var body=(FrameworkElement)((ScrollViewer)workspace.Diagrams.Content).Content;body.Measure(new Size(900,double.PositiveInfinity));body.Arrange(new Rect(0,0,900,body.DesiredSize.Height));body.UpdateLayout();Capture(body,900,(int)Math.Ceiling(body.DesiredSize.Height),Preview(previewPath,"coordinated-report-primary"));
        workspace.Close();
    }
    static void ReportedWorkspace(string previewPath)
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=10450,UpperSupportSpacingMm=275,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,MaximumConcreteEndOverhangMm=75,DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        vm.AddNetworkBay();for(int i=0;i<3;i++) {vm.NetworkBeams[i].WidthMm=i==2 ? 800 : 650;vm.NetworkBeams[i].DepthMm=i==2 ? 1750 : 1150;vm.NetworkBeams[i].BottomElevationMm=i==2 ? -1750 : -1150;}
        vm.NetworkBays[0].WidthMm=3550;vm.NetworkBays[1].WidthMm=2575;foreach(var bay in vm.NetworkBays)bay.ThicknessMm=300;
        vm.AddCrossingBeam();var crossing=vm.CrossingBeams[0];crossing.BayNumber=1;crossing.PositionMm=2500;crossing.WidthMm=300;crossing.DepthMm=600;crossing.BottomElevationMm=-600;
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Current report UI timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,"current three-beam drawing report solves");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Show();Pump();
        workspace.Buttons["Formwork"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));workspace.Drawing.AllDimensions=true;Pump();
        Capture(workspace.Drawing,1400,850);workspace.Drawing.Fit();Capture(workspace.Drawing,1400,850,Preview(previewPath,"current-report-formwork"));
        var plan=Primitives(VisualTreeHelper.GetDrawing(workspace.Drawing)).ToArray();
        var glyphs=plan.OfType<GlyphRunDrawing>().Select(g=>new {Text=new string(g.GlyphRun.Characters.ToArray()),Y=g.GlyphRun.BaselineOrigin.Y}).ToArray();
        Check(!glyphs.Any(g=>g.Text.Contains("Direct bearing")),"formwork omits direct-bearing annotations");
        foreach(string value in new[] {"450","575"})
        {
            var labels=glyphs.Where(g=>g.Text==value).ToArray();Check(labels.Length>=2,"reported overhang dimension labels are visible: "+value);
            Check(labels.GroupBy(g=>Math.Round(g.Y,3)).All(g=>g.Count()>=2),"overhang labels share a single baseline on each beam: "+value);
        }
        Check(workspace.Drawing.MemberTargets.Any(t=>t.Item2>=0 && t.Item3>=vm.NetworkResult.Beams[t.Item2].StockPlan.Pieces.Length),"main lower H20s use the same selectable plan strokes as X1 secondaries");
        Check(workspace.Drawing.DrawnLegCount==vm.NetworkResult.LegCount && workspace.Drawing.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.H20Count),"common plan rendering retains every actual leg and crossing H20");
        workspace.Buttons["Section"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Capture(workspace.Section,1200,420,Preview(previewPath,"current-report-section"));
        var section=Primitives(VisualTreeHelper.GetDrawing(workspace.Section)).ToArray();
        Check(!section.OfType<GeometryDrawing>().Any(g=>g.Geometry is EllipseGeometry && g.Brush is SolidColorBrush brush && brush.Color==Colors.DarkRed),"section omits crossing bearing dots");
        var support=vm.NetworkResult.CrossSupports.Single();
        foreach(int count in new[] {2,1})
        {
            var target=workspace.Section.MemberTargets.First(t=>t.Item2<0 && support.Members[t.Item3].H20Count==count);
            Check(workspace.Section.SelectAt(new Point(target.Item1.X+target.Item1.Width/2,target.Item1.Y+target.Item1.Height/2)),"section member click opens crossing diagrams");Pump();
            var window=workspace.Diagrams;Check(window!=null && window.IsVisible && window.SelectedTransferMember.H20Count==count,"selected crossing member displayed in the standard diagram window");
            Check(Descendants(window).Count(c=>c.GetType().Name=="LoadedResponsePlot")==3,"crossing has shear, moment and deflection plots");
            Check(window.LoadingDiagram.TranslatePoint(new Point(0,0),window).Y<300,"crossing loading chart is visible above its collapsed schedule");
            var schedule=Descendants(window).OfType<Expander>().Single(e=>Equals(e.Header,"Crossing support schedule and details"));Check(!schedule.IsExpanded,"long crossing schedule stays collapsed initially");
            if(count==2)Check(window.DisplayedTransferMember.Points.Length==0 && window.DisplayedTransferMember.Uniform.Length>0,"primary diagram shows the checked UDL without secondary-row point loads");
            var body=(FrameworkElement)((ScrollViewer)window.Content).Content;body.Measure(new Size(900,double.PositiveInfinity));body.Arrange(new Rect(0,0,900,body.DesiredSize.Height));body.UpdateLayout();
            Capture(body,900,(int)Math.Ceiling(body.ActualHeight),Preview(previewPath,count==2 ? "current-report-primary" : "current-report-secondary"));
            var next=workspace.Section.MemberTargets.First(t=>t.Item2<0 && support.Members[t.Item3].H20Count==count && t.Item3!=window.SelectedRun);
            workspace.Section.SelectAt(new Point(next.Item1.X+next.Item1.Width/2,next.Item1.Y+next.Item1.Height/2));Pump();
            Check(ReferenceEquals(workspace.Diagrams,window),"picking another crossing H20 reuses its diagram window");
        }
        workspace.Close();Console.WriteLine("Current report UI: all six drawing/diagram corrections checked.");
    }
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
        jointWorkspace.Close();ReportedWorkspace(previewPath);CoordinatedWorkspace(previewPath);Console.WriteLine("Cross UI: section, filler, actual diagrams, elevation editor and reported junctions passed.");return checks;
    }
}
