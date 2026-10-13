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
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=6900,TowerLegCapacityKn=70,StockLengthsText="1250; 1800; 2450; 2650; 2900; 3300; 3600; 3900; 4500; 4900",AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        while(vm.NetworkBeams.Count<8)vm.AddNetworkBay();
        for(int i=0;i<8;i++) {vm.NetworkBeams[i].WidthMm=i%2==0 ? 800 : 650;vm.NetworkBeams[i].DepthMm=i%2==0 ? 1750 : 1150;vm.NetworkBeams[i].BottomElevationMm=-vm.NetworkBeams[i].DepthMm;}
        var widths=new[] {2677.0,1875,3275,2275,2278,2272,2252};
        for(int i=0;i<7;i++)
        {
            vm.NetworkBays[i].WidthMm=widths[i];vm.NetworkBays[i].ThicknessMm=300;vm.AddCrossingBeam();
            var c=vm.CrossingBeams[i];c.BayNumber=i+1;c.PositionMm=3450;c.WidthMm=650;c.DepthMm=1150;c.BottomElevationMm=-1150;
        }
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();
        // The full coordinated fixture also runs on .NET Framework; allow its
        // production-runtime search to complete before checking the rendered values.
        while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>600)throw new Exception("Coordinated report UI timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        Check(vm.NetworkResult.CrossSupports.Where(c=>c.Towers?.Transfer!=null).All(c=>PlywoodSpacing.StandardSpacingsMm.Contains(c.Towers.Transfer.EnteredUpperSpacingMm)),"crossing formwork uses only permitted sheet spacings");
        Check(vm.UpperSupportSpacingMm==vm.MaximumSecondarySpacingMm && vm.NetworkResult.Input.UpperSpacingMm<=vm.MaximumSecondarySpacingMm && vm.BuildDiagnosticReport().Contains("UpperSupportSpacingMm"),"coordinated search preserves the entered maximum and reports its actual spacing");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Show();Pump();workspace.Drawing.Formwork=true;
        Capture(workspace.Drawing,2000,1100);workspace.Drawing.Fit();Capture(workspace.Drawing,2000,1100,Preview(previewPath,"coordinated-report-plan"));
        Check(workspace.Drawing.DrawnLegCount==vm.NetworkResult.LegCount && workspace.Drawing.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.Members.Where(m=>m.H20Count==2).Sum(m=>m.H20Count)),"coordinated plan draws every real tower leg and crossing member");
        Check(workspace.Drawing.MemberTargets.All(t=>t.Item2<0 ? vm.NetworkResult.CrossSupports[-t.Item2-1].Members[t.Item3].H20Count==2 : t.Item3<vm.NetworkResult.Beams[t.Item2].StockPlan.Pieces.Length),"coordinated plan omits all secondary H20 strokes and targets");
        Check(workspace.Drawing.DrawnBracedBayCount==vm.NetworkResult.Beams.Concat(vm.NetworkResult.CrossSupports.Select(c=>c.Towers)).Sum(l=>l.Joined.Count(j=>j)),"Doka symbols brace only connected tower bays");
        foreach(var leg in CrossBeamSupportOptimizer.NetworkSolids(vm.NetworkResult).Where(s=>s.IsLeg))
            Check(workspace.Drawing.LegTargets.Any(target=>target.Item1.Contains(workspace.Drawing.Screen((leg.X0+leg.X1)/2,(leg.Y0+leg.Y1)/2))),"plan leg markers use the actual coordinated tower centres");
        var towerStrokes=Primitives(VisualTreeHelper.GetDrawing(workspace.Drawing)).OfType<GeometryDrawing>().ToArray();
        Check(towerStrokes.Any(g=>g.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.Gold) && towerStrokes.Any(g=>g.Geometry is RectangleGeometry && g.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.DarkCyan),"tower symbols use yellow crosses and cyan rectangular leg markers");
        for(int i=0;i<7;i++)
        {
            workspace.Drawing.Select(FormworkObject.Crossing,i);Pump();Capture(workspace.Drawing,2000,1100);
            var setting=workspace.Drawing.CrossingSettingOut.Where(d=>d.Item1==i).ToArray();
            Check(setting.Any(d=>d.Item2.Contains("concrete face to first")) && setting.Any(d=>d.Item2.Contains("concrete face") && d.Item2.Contains("Last")),"crossing legs are dimensioned from both parallel concrete faces");
            Check(Math.Abs(setting.Where(d=>d.Item2.Contains("concrete face") || d.Item2=="Braced tower bay" || d.Item2=="Gap between tower groups").Sum(d=>d.Item3)-vm.NetworkBays[i].WidthMm)<1e-7,"setting-out chain closes to the entered bay width");
            Check(setting.Where(d=>d.Item2.Contains("H20 overhang")).All(d=>d.Item3>=150-1e-7),"crossing dimensions use actual stock end setbacks");
        }
        workspace.Drawing.Select(FormworkObject.Crossing,0);Pump();var primary=vm.NetworkResult.CrossSupports[0].Solids.Where(s=>s.Member?.H20Count==2).ToArray();
        var focus=workspace.Drawing.Screen((primary.Min(s=>s.X0)+primary.Max(s=>s.X1))/2,(primary.Min(s=>s.Y0)+primary.Max(s=>s.Y1))/2);workspace.Drawing.PanBy(new Vector(1000-focus.X,550-focus.Y));workspace.Drawing.ZoomAt(3,new Point(1000,550));
        Capture(workspace.Drawing,2000,1100,Preview(previewPath,"coordinated-report-setting-out"));
        var settingLabels=Primitives(VisualTreeHelper.GetDrawing(workspace.Drawing)).OfType<GlyphRunDrawing>().Select(g=>new string(g.GlyphRun.Characters.ToArray())).ToArray();
        var missingSettingOut=workspace.Drawing.CrossingSettingOut.Where(d=>d.Item1==0 && !settingLabels.Contains(d.Item3.ToString("0.###"))).ToArray();
        Check(missingSettingOut.Length==0,"focused crossing displays every setting-out value, including both H20 overhangs; missing: "+string.Join(", ",missingSettingOut.Select(d=>d.Item2+" = "+d.Item3.ToString("0.###"))));
        Check(!workspace.Drawing.AnnotationBounds.Where((r,i)=>workspace.Drawing.AnnotationBounds.Skip(i+1).Any(r.IntersectsWith)).Any(),"focused crossing labels remain separate after moving crowded dimensions");
        workspace.Drawing.Select(FormworkObject.Slab,1);Pump();Check(workspace.SelectedResultText.Contains("Slab spacing "+vm.NetworkResult.Input.BaySpacing(1).ToString("0",System.Globalization.CultureInfo.InvariantCulture)) && workspace.SelectedResultText.Contains("limit "+vm.NetworkBays[1].MaximumSecondarySpacingMm.ToString("0",System.Globalization.CultureInfo.InvariantCulture)),"slab inspector explains tighter junction spacing");
        workspace.Buttons["Section"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(int i=0;i<vm.NetworkResult.CrossSupports.Length;i++)
        {
            workspace.Section.SelectMember(-i-1,-1,0);Pump();Capture(workspace.Section,2000,420,Preview(previewPath,$"coordinated-report-X{i+1}-section"));
            Check(workspace.Section.DrawnConcreteEndZoneCount==vm.NetworkResult.CrossSupports[i].Bearings.Where(b=>b.IsConcreteEndZone).Select(b=>b.Beam).Distinct().Count(),$"X{i+1} section draws each direct or shutter-supported junction end zone");
        }
        int crossing=Array.FindIndex(vm.NetworkResult.CrossSupports,c=>Math.Abs(c.TowerSpanOffsetMm)>0);var support=vm.NetworkResult.CrossSupports[crossing];
        workspace.Section.SelectMember(-crossing-1,-1,0);Pump();Capture(workspace.Section,2000,420,Preview(previewPath,"coordinated-report-section"));
        var target=workspace.Section.MemberTargets.First(t=>t.Item2==-crossing-1 && support.Members[t.Item3].H20Count==2);
        workspace.Section.SelectAt(new Point(target.Item1.X+target.Item1.Width/2,target.Item1.Y+target.Item1.Height/2));Pump();
        Check(workspace.Diagrams!=null && Descendants(workspace.Diagrams).Count(c=>c.GetType().Name=="LoadedResponsePlot")==3,"translated crossing supports retain all three response diagrams");
        Check(workspace.Diagrams.DisplayedTransferMember.SupportsMm.SequenceEqual(support.Members[target.Item3].SupportsMm),"diagram uses the actual displaced support positions");
        var body=(FrameworkElement)((ScrollViewer)workspace.Diagrams.Content).Content;body.Measure(new Size(900,double.PositiveInfinity));body.Arrange(new Rect(0,0,900,body.DesiredSize.Height));body.UpdateLayout();Capture(body,900,(int)Math.Ceiling(body.DesiredSize.Height),Preview(previewPath,"coordinated-report-primary"));
        vm.SelectNetworkBeam(5);var cases=new TowerDiagramWindow(vm);cases.SelectMember(0,0,-1);cases.Show();Pump();
        Check(cases.LoadCaseSelector.Items.Count==16 && cases.CheckedCasesTable.Items.Count==16 && !cases.CheckedCasesDetails.IsExpanded,"reported B6 retains sixteen checks behind one dropdown and collapsed details");
        var labels=cases.LoadCaseSelector.Items.Cast<ComboBoxItem>().Select(item=>item.Content.ToString()).ToArray();
        Check(labels.Any(label=>label=="B6 + S6 + X6") && labels.All(label=>!label.Contains("left slab") && !label.Contains("right slab") && !label.Contains("both slabs")),"reported case labels identify actual beams and slab bays");
        var members=vm.NetworkResult.Beams[5].Transfer.PrimaryCases.Select(stage=>stage[0]).ToArray();
        Check(cases.SelectedTransferMember.Utilization==members.Max(m=>m.Utilization),"reported B6 defaults to its actual governing member case");
        // The designer ranking can change B6's stock breaks and governing case.
        // Select an actual overlapping-load member, rather than assuming P1 carries a crossing.
        int runs=vm.NetworkResult.Beams[5].StockPlan.Pieces.Length;
        var overlapping=vm.NetworkResult.Beams[5].Transfer.PrimaryCases.SelectMany((stage,index)=>stage.Take(runs).Select((member,run)=>new {Member=member,Case=index,Run=run}))
            .First(item=>item.Member.Uniform.SelectMany((q,i)=>item.Member.Uniform.Skip(i+1).Select(other=>Math.Min(q.EndMm,other.EndMm)>Math.Max(q.StartMm,other.StartMm)+1e-7)).Any(overlap=>overlap));
        cases.SelectMember(overlapping.Run,0,-1);Pump();cases.LoadCaseSelector.SelectedIndex=overlapping.Case;Pump();
        Check(ReferenceEquals(cases.SelectedTransferMember,overlapping.Member),"loading diagram selects the member carrying overlapping crossing loads");
        Capture(cases.LoadingDiagram,900,270,Preview(previewPath,"reported-B6-loading"));
        var source=cases.LoadingDiagram.SourceUniform;var combined=cases.LoadingDiagram.CombinedUniform.OrderBy(q=>q.StartMm).ToArray();
        Check(cases.LoadingDiagram.DrawnUniformCount==combined.Length && combined.Skip(1).Select((q,i)=>q.StartMm>=combined[i].EndMm-1e-7).All(clear=>clear),"crossing loads render as non-overlapping intensity segments");
        var cuts=source.Concat(combined).SelectMany(q=>new[] {q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();
        for(int i=1;i<cuts.Length;i++)
        {
            double middle=(cuts[i-1]+cuts[i])/2;
            double expected=source.Where(q=>q.StartMm<middle && q.EndMm>middle).Sum(q=>q.KnPerM);
            double shown=combined.Where(q=>q.StartMm<middle && q.EndMm>middle).Sum(q=>q.KnPerM);
            Check(Math.Abs(shown-expected)<1e-7*Math.Max(1,Math.Abs(expected)),"combined crossing intensity matches the actual member loads");
        }
        double sourceForce=source.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),shownForce=combined.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000);
        Check(Math.Abs(sourceForce-shownForce)<1e-7*Math.Max(1,Math.Abs(sourceForce)),"loading diagram preserves total crossing force");
        var caseBody=(FrameworkElement)((ScrollViewer)cases.Content).Content;caseBody.Measure(new Size(900,double.PositiveInfinity));caseBody.Arrange(new Rect(0,0,900,caseBody.DesiredSize.Height));caseBody.UpdateLayout();Capture(caseBody,900,(int)Math.Ceiling(caseBody.DesiredSize.Height),Preview(previewPath,"reported-B6-diagrams"));
        cases.CheckedCasesDetails.IsExpanded=true;Pump();caseBody.Measure(new Size(900,double.PositiveInfinity));caseBody.Arrange(new Rect(0,0,900,caseBody.DesiredSize.Height));caseBody.UpdateLayout();Capture(caseBody,900,(int)Math.Ceiling(caseBody.DesiredSize.Height),Preview(previewPath,"reported-B6-checked-cases"));cases.Close();
        workspace.Close();
    }
    static void ReportedWorkspace(string previewPath)
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=10450,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,MaximumConcreteEndOverhangMm=75,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        vm.AddNetworkBay();for(int i=0;i<3;i++) {vm.NetworkBeams[i].WidthMm=i==2 ? 800 : 650;vm.NetworkBeams[i].DepthMm=i==2 ? 1750 : 1150;vm.NetworkBeams[i].BottomElevationMm=i==2 ? -1750 : -1150;}
        vm.NetworkBays[0].WidthMm=3550;vm.NetworkBays[1].WidthMm=2575;foreach(var bay in vm.NetworkBays)bay.ThicknessMm=300;
        vm.AddCrossingBeam();var crossing=vm.CrossingBeams[0];crossing.BayNumber=1;crossing.PositionMm=2500;crossing.WidthMm=600;crossing.DepthMm=600;crossing.BottomElevationMm=-600;
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Current report UI timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,"current three-beam drawing report solves");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Show();Pump();
        workspace.Buttons["Formwork"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));workspace.Drawing.AllDimensions=true;Pump();
        Capture(workspace.Drawing,1400,850);workspace.Drawing.Fit();Capture(workspace.Drawing,1400,850,Preview(previewPath,"current-report-formwork"));
        var plan=Primitives(VisualTreeHelper.GetDrawing(workspace.Drawing)).ToArray();
        var glyphs=plan.OfType<GlyphRunDrawing>().Select(g=>new {Text=new string(g.GlyphRun.Characters.ToArray()),Y=g.GlyphRun.BaselineOrigin.Y}).ToArray();
        Check(!glyphs.Any(g=>g.Text.Contains("Direct bearing")),"formwork omits direct-bearing annotations");
        // Shorter optimized overhangs are intentionally hidden at the overview scale.
        // Inspect each primary at a readable zoom, just as a user would in the plan.
        Capture(workspace.Drawing,2400,850);
        workspace.Drawing.ZoomAt(.35/workspace.Drawing.Zoom,new Point(1200,425));
        for(int index=0;index<vm.NetworkResult.Beams.Length;index++)
        {
            var beam=vm.NetworkResult.Beams[index];
            foreach(var piece in beam.StockPlan.Pieces)
            {
                var focus=workspace.Drawing.Screen((piece.StartMm+piece.EndMm)/2,vm.NetworkResult.BeamCentresMm[index]+beam.TransverseOffsetMm+beam.WidthMm/2);
                workspace.Drawing.PanBy(new Vector(1200-focus.X,350-focus.Y));
                Capture(workspace.Drawing,2400,850);
                var focused=Primitives(VisualTreeHelper.GetDrawing(workspace.Drawing)).OfType<GlyphRunDrawing>()
                    .Select(g=>new {Text=new string(g.GlyphRun.Characters.ToArray()),Y=g.GlyphRun.BaselineOrigin.Y}).ToArray();
                string near=(beam.PositionsMm[piece.FirstSupport]-piece.StartMm).ToString("0.###");
                string far=(piece.EndMm-beam.PositionsMm[piece.LastSupport]).ToString("0.###");
                Check(focused.Any(g=>g.Text==near) && focused.Any(g=>g.Text==far),"actual optimized overhang dimensions are visible: "+near+" / "+far);
                Check(focused.GroupBy(g=>Math.Round(g.Y,3)).Any(group=>near==far ? group.Count(g=>g.Text==near)>=2 : group.Any(g=>g.Text==near) && group.Any(g=>g.Text==far)),"near and far overhang labels share a single baseline");
            }
        }
        Check(workspace.Drawing.MemberTargets.All(t=>t.Item2<0 ? vm.NetworkResult.CrossSupports[-t.Item2-1].Members[t.Item3].H20Count==2 : t.Item3<vm.NetworkResult.Beams[t.Item2].StockPlan.Pieces.Length),"plan hides main and crossing secondaries; section keeps their selectable diagrams");
        Check(workspace.Drawing.DrawnLegCount==vm.NetworkResult.LegCount && workspace.Drawing.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.Members.Where(m=>m.H20Count==2).Sum(m=>m.H20Count)),"common plan rendering retains every actual leg and crossing H20");
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
        foreach(bool rotated in new[] {false,true})
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
            {
                TowerPlanDrawing.Station(dc,new Point(0,0),new Point(0,100),rotated);
                TowerPlanDrawing.BracedBay(dc,new Point(0,0),new Point(0,100),new Point(150,0),new Point(150,100),rotated);
            }
            var cyan=Primitives(visual.Drawing).OfType<GeometryDrawing>().Where(g=>g.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.DarkCyan).Select(g=>g.Geometry).OfType<LineGeometry>().ToArray();
            Check(cyan.Length>0 && cyan.All(l=>rotated ? Math.Abs(l.StartPoint.Y-l.EndPoint.Y)<.01 : Math.Abs(l.StartPoint.X-l.EndPoint.X)<.01),"cyan frame planes follow actual tower rotation");
        }
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,HasRightSlab=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=2450,MaximumConcreteEndOverhangMm=0,TowerLegCapacityKn=70,SelectedPlywood=PlywoodCatalog.Default,UseCustomLoads=true,CustomLiveLoad=0,CustomFormworkLoad=0,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        foreach(var b in vm.NetworkBeams) {b.WidthMm=300;b.DepthMm=600;}
        vm.NetworkBays[0].WidthMm=3900;vm.NetworkBays[0].ThicknessMm=100;
        vm.AddCrossingBeam();var cross=vm.CrossingBeams[0];cross.PositionMm=1225;cross.WidthMm=400;cross.DepthMm=50;cross.BottomElevationMm=-250;cross.SupportMode=CrossBeamSupportMode.ExistingSupportsOnly;
        var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Cross UI search timeout");}task.GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        Check(vm.NetworkResult.CrossSupports.Where(c=>c.Towers?.Transfer!=null).All(c=>PlywoodSpacing.StandardSpacingsMm.Contains(c.Towers.Transfer.EnteredUpperSpacingMm)),"crossing formwork uses only permitted sheet spacings");
        var section=new TransferNetworkPlan {Width=1200,SectionOnly=true,IsSelectionEnabled=true};section.SetResult(vm.NetworkResult);section.Measure(new Size(1200,420));section.Arrange(new Rect(0,0,1200,420));section.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1200,420,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(section);
        Check(section.DrawnH20ProfileCount>=vm.NetworkResult.Beams.Length*5 && section.DrawnPrimarySectionCount==vm.NetworkResult.Beams.Length*4,"section shows real flange/web profiles and both paired primary members");
        Check(section.MemberTargets.Any(t=>t.Item2<0),"cross members selectable in section");
        var bearing=vm.NetworkResult.CrossSupports[0].Bearings[0];Check(bearing.FillerHeightMm==150,"section filler uses the independently computed gap");
        var diagrams=new TowerDiagramWindow(vm,vm.NetworkResult.CrossSupports[0]);diagrams.SelectMember(0,0,-1);
        Check(diagrams.SelectedTransferMember==vm.NetworkResult.CrossSupports[0].Members[0] && diagrams.ActualLoadedAnalysis==diagrams.LoadedAnalysis,"cross diagram uses its actual concrete and formwork loads");diagrams.Close();
        var canvas=new BeamFormworkCanvas(vm) {Width=1200,Height=600,Formwork=true};canvas.Measure(new Size(1200,600));canvas.Arrange(new Rect(0,0,1200,600));canvas.Fit();canvas.UpdateLayout();
        var planBitmap=new RenderTargetBitmap(1200,600,96,96,System.Windows.Media.PixelFormats.Pbgra32);planBitmap.Render(canvas);
        Check(!canvas.MemberTargets.Any(t=>t.Item2<0) && canvas.DrawnLegCount==vm.NetworkResult.LegCount && canvas.DrawnCrossH20Count==vm.NetworkResult.CrossSupports.Sum(c=>c.Members.Where(m=>m.H20Count==2).Sum(m=>m.H20Count)),"workspace cross H20 targets and calculated tower legs drawn");
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Drawing.Select(FormworkObject.Crossing,0);workspace.Show();Pump();
        Check(workspace.Fields.ContainsKey("BottomElevationMm"),"cross bottom elevation exposed in workspace");
        workspace.Drawing.Select(FormworkObject.Slab,0);Pump();
        Check(workspace.SelectedResultText.Contains("Equal foot setback") && workspace.SelectedResultText.Contains("repeat every row") && workspace.SelectedResultText.Contains(vm.NetworkResult.SecondarySpacingNotice.Length==0 ? "Maximum row spacing "+vm.NetworkResult.Input.UpperSpacingMm.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture) : vm.NetworkResult.SecondarySpacingNotice),"workspace exposes the repeated symmetric slab support detail and spacing");
        var slab=vm.NetworkResult.Beams[0].Transfer.UpperMembers.Single(m=>m.Name=="Upper right");
        Check(Math.Abs(slab.SupportsMm[0]+slab.SupportsMm[1]-slab.LengthMm)<1e-7 && Math.Abs(slab.Analysis.Reactions[0]-slab.Analysis.Reactions[1])<1e-7,"drawn slab member has symmetric supports and actual equal uniform-load reactions");
        workspace.Close();
        var junction=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=10800,MaximumConcreteEndOverhangMm=75,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        junction.NetworkBeams[0].WidthMm=650;junction.NetworkBeams[0].DepthMm=1150;junction.NetworkBeams[0].BottomElevationMm=-1150;
        junction.NetworkBeams[1].WidthMm=800;junction.NetworkBeams[1].DepthMm=1750;junction.NetworkBeams[1].BottomElevationMm=-1750;
        junction.NetworkBays[0].WidthMm=2575;junction.NetworkBays[0].ThicknessMm=300;junction.AddCrossingBeam();junction.CrossingBeams[0].PositionMm=5375;junction.CrossingBeams[0].WidthMm=650;junction.CrossingBeams[0].DepthMm=1150;junction.CrossingBeams[0].BottomElevationMm=-1150;
        task=junction.OptimizeTowersAsync();timer.Restart();while(!task.IsCompleted) {Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>90)throw new Exception("Junction UI search timeout");}task.GetAwaiter().GetResult();
        Check(junction.IsCalculationValid,junction.WarningMessage+" "+junction.TowerFailureDetails);
        var jointWorkspace=new BeamFormworkWorkspace(junction,null,null,null);jointWorkspace.Show();jointWorkspace.Drawing.Formwork=true;jointWorkspace.Drawing.Select(FormworkObject.Crossing,0);Pump();
        var jointCanvas=jointWorkspace.Drawing;jointCanvas.Measure(new Size(1200,600));jointCanvas.Arrange(new Rect(0,0,1200,600));jointCanvas.Fit();jointCanvas.UpdateLayout();
        var jointBitmap=new RenderTargetBitmap(1200,600,96,96,System.Windows.Media.PixelFormats.Pbgra32);jointBitmap.Render(jointCanvas);
        Check(jointCanvas.DrawnLegCount==junction.NetworkResult.LegCount && jointCanvas.DrawnCrossH20Count==junction.NetworkResult.CrossSupports.Sum(c=>c.Members.Where(m=>m.H20Count==2).Sum(m=>m.H20Count)),"junction plan draws the actual central tower and trimmed members with no duplicate end-zone timber");
        Check(jointCanvas.LegTargets.Any(t=>t.Item2.Contains("concrete end zone on B1")),"plan end-zone targets describe concrete loads rather than timber bearings");
        Check(jointWorkspace.SelectedResultText.Contains("Dedicated central span") && jointWorkspace.SelectedResultText.Contains("Concrete end zone on B1"),"selected crossing explains which load reaches the main beam");
        Check(jointWorkspace.SelectedResultText.Contains("existing side shutter") && jointWorkspace.SelectedResultText.Contains("100 mm"),"crossing inspector explains the same shutter load path as slab edges");
        Check(jointCanvas.LegTargets.Any(t=>t.Item2.Contains("side shutter")),"plan junction selection identifies the existing shutter support");
        var jointSection=new TransferNetworkPlan {Width=1200,SectionOnly=true,IsSelectionEnabled=true};jointSection.SetResult(junction.NetworkResult);jointSection.Measure(new Size(1200,420));jointSection.Arrange(new Rect(0,0,1200,420));jointSection.UpdateLayout();var jointSectionBitmap=new RenderTargetBitmap(1200,420,96,96,System.Windows.Media.PixelFormats.Pbgra32);jointSectionBitmap.Render(jointSection);Check(jointSection.MemberTargets.Any(t=>t.Item2<0),"junction central members are selectable in section");
        Check(jointSection.DrawnConcreteEndZoneCount==junction.NetworkResult.CrossSupports[0].Bearings.Where(b=>b.IsConcreteEndZone).Select(b=>b.Beam).Distinct().Count(),"section draws the direct and side-shutter end zones on the receiving beams");
        jointWorkspace.Drawing.Select(FormworkObject.Beam,0);Pump();jointWorkspace.Fields["DepthMm"].Text="1200";Check(jointWorkspace.CommitFields() && junction.NetworkBeams[0].BottomElevationMm==-1200,"workspace depth edit keeps the concrete top at zero");
        if(!string.IsNullOrEmpty(previewPath))
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(jointBitmap));using(var file=System.IO.File.Create(System.IO.Path.ChangeExtension(previewPath,"junction-plan.png")))encoder.Save(file);
            encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(jointSectionBitmap));using(var file=System.IO.File.Create(System.IO.Path.ChangeExtension(previewPath,"junction-section.png")))encoder.Save(file);
        }
        jointWorkspace.Close();ReportedWorkspace(previewPath);CoordinatedWorkspace(previewPath);Console.WriteLine("Cross UI: section, filler, actual diagrams, elevation editor and reported junctions passed.");return checks;
    }
}
