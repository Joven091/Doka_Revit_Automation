using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;
internal static class Program
{
 static int checks;
 static BeamCalculatorWindow window;
 static BeamCalculatorViewModel vm;
 static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
 {foreach(object item in LogicalTreeHelper.GetChildren(root))if(item is DependencyObject child){if(child is T match)yield return match;foreach(var t in Find<T>(child))yield return t;}}
 static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));}
 static TextBox Input(string name)=>Find<TextBox>(window).Single(t=>t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path==name);
 static void Enter(string name,string text){Input(name).Text=text;Input(name).GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();}
 static IEnumerable<TextBox> VisualInputs(DependencyObject root) {if(root is TextBox box)yield return box;if(root is Visual)for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var value in VisualInputs(VisualTreeHelper.GetChild(root,i)))yield return value;}
 static RenderTargetBitmap Render(int width=1040,int height=860)
 {
  Pump();var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);return bitmap;
 }
 [STAThread] static int Main(string[] args)
 {
  try
  {
   System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
   SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
   window=new BeamCalculatorWindow();vm=new BeamCalculatorViewModel();window.DataContext=vm;Render();
   var import=(Button)window.FindName("ImportButton");var results=(Border)window.FindName("ResultsBorder");var diagram=(BeamSupportDiagram)window.FindName("SupportDiagram");
   var picker=(Grid)window.FindName("SupportSystemPicker");var workspace=(Grid)window.FindName("CalculatorWorkspace");
   Check(picker.Visibility==Visibility.Visible && workspace.Visibility==Visibility.Collapsed,"fresh beam calculator starts with visual system choice");
   Check(!import.IsEnabled && !window.TryGetValidCalculation(out var unselected),"no import or capture before choosing supports");
   if(args.Length>0) SavePreview(Path.ChangeExtension(args[0],"support-picker.png"),1040,860);
   ((Button)window.FindName("PickTowersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   Check(vm.HasSelectedSupportSystem && vm.UseTowers && workspace.Visibility==Visibility.Visible && picker.Visibility==Visibility.Collapsed,"tower card reveals tower inputs");
   Check(Input("TowerLegCapacityKn").IsEnabled && !Input("FloorHeight").IsEnabled,"tower card enables only tower support inputs");
   ((Button)Find<Button>(window).Single(t=>Equals(t.Content,"Change system"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   Check(picker.Visibility==Visibility.Visible && !import.IsEnabled,"change system returns to visual choices");
   ((Button)window.FindName("PickPropsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render();
   Check(vm.HasSelectedSupportSystem && !vm.UseTowers && picker.Visibility==Visibility.Collapsed,"prop card reveals prop calculator");
   Check(import.IsEnabled,"initial import");Check(diagram.DrawnPropCount==4,"two props per primary line");Check(diagram.DrawnSlabCount==0,"no slabs by default");
   Enter("Depth","abc");Check(!vm.IsCalculationValid,"invalid text invalidates VM");Check(!import.IsEnabled,"invalid import disabled");Check(results.Visibility==Visibility.Collapsed,"stale results hidden");Check(Input("Depth").Text=="abc","invalid text preserved");
   Check(!window.TryGetValidCalculation(out var ignored),"import guard rejects conversion error");
   Enter("Depth","500");Check(vm.IsCalculationValid&&import.IsEnabled,"valid text recovers");
   Enter("Width","NaN");Check(!vm.IsCalculationValid&&!import.IsEnabled,"NaN rejected");Enter("Width","700");
   vm.UseCustomLoads=true;Pump();Enter("CustomLiveLoad","bad");Check(!vm.IsCalculationValid,"custom conversion error");vm.UseCustomLoads=false;Pump();Check(vm.IsCalculationValid,"inactive custom ignored");vm.UseCustomLoads=true;Pump();Check(!vm.IsCalculationValid,"reactivated bad custom rejected");Enter("CustomLiveLoad","2.25");vm.UseCustomLoads=false;
   vm.HasLeftSlab=true;Pump();Enter("LeftSlabThickness","0");Check(!vm.IsCalculationValid,"zero checked slab");Enter("LeftSlabThickness","200");vm.HasRightSlab=true;vm.NumberOfProps="3";Render();
   Check(diagram.DrawnPropCount==6,"three props per line");Check(diagram.DrawnSlabCount==2,"both slabs shown");
   vm.HasLeftSlab=false;Render();Check(diagram.DrawnSlabCount==1,"unchecked slab removed");vm.HasRightSlab=false;
   vm.NumberOfProps="4+";var combo=Find<ComboBox>(window).Single(c=>c.SelectedItem is BeamDeflectionOption);Check(combo.Items.Count==3,"typed deflection options loaded");combo.SelectedIndex=2;Pump();Check(vm.DeflectionLimit.Denominator==270,"L/270 selection maps numerically");Check(vm.PrimaryDeflectionUtilization<=1,"ratio criteria control rounded spacing");
   Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");vm.Width=701;Render();Check(diagram.DrawnPropCount==8,"decimal comma vector diagram");Check(vm.IsCalculationValid,"decimal comma calculation");
   var props=Find<ComboBox>(window).Single(c=>c.SelectedItem is BeamPropOption);Check(props.Items.Count==BeamPropOption.All.Count,"prop dropdown shares catalog");props.SelectedItem=BeamPropOption.All.First(p=>p.CapacityKey=="DokaFit Prop 300 at top");Pump();Check(vm.SelectedProp.CapacityKey=="DokaFit Prop 300 at top","prop selection binding");Enter("FloorHeight","9999");Check(!vm.IsCalculationValid&&!import.IsEnabled,"unsupported extension blocks import");Enter("FloorHeight","3000");Check(vm.IsCalculationValid,"floor height recovery");
   Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("en-US");
   vm.Width=200;vm.HasLeftSlab=true;vm.HasRightSlab=true;vm.NumberOfProps="3";var picture=Render();
   vm.SlabStripWidthMm=450;Pump(); var saved=window.CaptureCalculation();double savedSpacing=vm.MaximumPropSpacing,savedLoad=vm.MaximumPropLoad;
   Check(saved.Inputs.Any(f=>f.Name=="DeflectionCriterion"&&f.Value=="L/270") && !saved.Inputs.Any(f=>f.Name=="DeflectionLimitMm"),"ratio saved by criterion without fake fixed limit");
   string recordPath=Path.GetTempFileName();
   try { CalculationFiles.Write(recordPath,saved);vm.SlabStripWidthMm=150;window.RestoreCalculation(CalculationFiles.Read<CalculationRecord>(recordPath));Pump();Check(vm.SlabStripWidthMm==450 && vm.MaximumPropSpacing==savedSpacing && Math.Abs(vm.MaximumPropLoad-savedLoad)<1e-9,"beam file roundtrip"); }
   finally { File.Delete(recordPath); }
   saved.Kind="Slab";bool refused=false;try{window.RestoreCalculation(saved);}catch(ArgumentException){refused=true;}Check(refused && vm.SlabStripWidthMm==450,"wrong file leaves beam intact");
   Enter("SlabStripWidthMm","bad");Check(!vm.IsCalculationValid,"strip text validation");vm.HasLeftSlab=false;vm.HasRightSlab=false;Pump();Check(vm.IsCalculationValid,"inactive strip text ignored");Enter("SlabStripWidthMm","450");vm.HasLeftSlab=true;vm.HasRightSlab=true;Pump();
   Check(window.BuildReport().Contains("450") && window.BuildReport().Contains("Results and checks"),"beam report includes editable width and results");
   if(args.Length>0){
    File.WriteAllText(Path.ChangeExtension(args[0],"report.html"),window.BuildReport());
    SavePreview(args[0],1040,860);
    ((Expander)window.FindName("LoadInputsExpander")).IsExpanded=true;vm.UseCustomLoads=true;Pump();
    SavePreview(Path.ChangeExtension(args[0],"loads.png"),1040,860);
    SavePreview(Path.ChangeExtension(args[0],"compact.png"),900,620);
    Enter("Depth","abc");Check(!vm.IsCalculationValid&&!import.IsEnabled,"redesign invalid field blocks import");
    SavePreview(Path.ChangeExtension(args[0],"invalid.png"),1040,860);
   }
   Enter("Depth","500");vm.HasLeftSlab=false;vm.HasRightSlab=false;vm.UseCustomLoads=false;
   vm.MaximumConcreteEndOverhangMm=0;vm.UseTowers=true;Pump();Check(!vm.IsCalculationValid && !import.IsEnabled,"tower awaits optimization");
   Enter("FloorHeight","bad");Check(!Input("FloorHeight").IsEnabled,"inactive prop height disabled");
   Enter("TowerLegCapacityKn","bad");Check(!vm.CanOptimizeTowers,"tower conversion error blocks optimization");
   Enter("TowerLegCapacityKn","30");Enter("BeamLengthMm","6100");Optimize();Render();
   Check(vm.IsCalculationValid && !import.IsEnabled,"valid tower report-only");
   Check(diagram.DrawnTowerLegCount==vm.TowerResult.LegCount && diagram.DrawnPropCount==0,"tower plan renders real leg count");
   Check(diagram.DrawnStockPieceCount==vm.TowerResult.StockPlan.TotalH20Count,"stock members drawn individually");
   Enter("StockLengthsText","1250; invalid");Optimize();Check(!vm.IsCalculationValid && vm.TowerResult==null,"bad stock text clears and rejects result");
   Enter("StockLengthsText",TowerStockPlanner.DefaultLengths);Enter("StockEndSetbackMm","bad");Check(!vm.CanOptimizeTowers,"bad setback conversion blocks search");
   Enter("StockEndSetbackMm","150");Optimize();Render();
   Check(((Border)window.FindName("TowerResultsBorder")).Visibility==Visibility.Visible && results.Visibility==Visibility.Collapsed,"correct result panel");
   Check(diagram.DrawnEndDimensionCount==vm.TowerResult.StockPlan.Pieces.Length*2,"both H20 end offsets are dimensioned");
   var towerSaved=window.CaptureCalculation();var coordinates=vm.TowerResult.PositionsMm.ToArray();
   var retainedLayout=vm.TowerResult;double retainedWidth=vm.Width;
   Find<Button>(window).Single(t=>Equals(t.Content,"Change system")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   ((Button)window.FindName("PickTowersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render();
   Check(ReferenceEquals(vm.TowerResult,retainedLayout) && vm.Width==retainedWidth,"reselecting same system retains geometry and optimized layout");

   Check(!diagram.SelectAt(new Point(diagram.MemberTargets[0].Item1.X+2,diagram.MemberTargets[0].Item1.Y+4)),"selection waits for diagram button");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   Check(window.TowerDiagrams==null && diagram.IsSelectionEnabled,"button enables main-plan selection without a duplicate plan");
   var planScroll=(ScrollViewer)window.FindName("TowerPlanScroll");Render();
   Check(planScroll.ScrollableWidth>0 && planScroll.ExtentWidth>=diagram.ActualWidth-.1,"full plan exposes horizontal scrolling");
   planScroll.ScrollToRightEnd();Pump();
   var lastHit=diagram.MemberTargets.Last();
   var visiblePoint=diagram.TranslatePoint(new Point(lastHit.Item1.Right-2,lastHit.Item1.Y+4),planScroll);
   Check(visiblePoint.X>=0 && visiblePoint.X<=planScroll.ViewportWidth,"last H20 end is reachable by sideways scrolling");
   var firstHit=diagram.MemberTargets.First();
   diagram.SelectAt(new Point(firstHit.Item1.X+firstHit.Item1.Width/2,firstHit.Item1.Y+4));Pump();
   var charts=window.TowerDiagrams;Check(charts!=null && charts.IsVisible,"main-plan click opens diagrams");
   Check(!Find<ComboBox>(charts).Any() && !Find<BeamSupportDiagram>(charts).Any(),"response window contains no dropdowns or duplicate plan");
   foreach(var hit in diagram.MemberTargets.ToArray()) {
    Check(diagram.SelectAt(new Point(hit.Item1.X+hit.Item1.Width/2,hit.Item1.Y+hit.Item1.Height/2)),"main-plan member hit");
    Check(charts.SelectedRun==hit.Item2 && charts.SelectedLine==hit.Item3,"main-plan selection drives matching pair diagram");
    var selected=vm.TowerResult.StockPlan.Pieces[hit.Item2];double q=hit.Item3==0?vm.TowerResult.LeftLineLoad:vm.TowerResult.RightLineLoad;
    Check(Math.Abs(charts.Analysis.Reactions.Sum()-q*selected.StockLengthMm/1000)<1e-8,"double-H20 load for selected pair");
    double ratio=q/Math.Max(vm.TowerResult.LeftLineLoad,vm.TowerResult.RightLineLoad);
    Check(Math.Abs(charts.Analysis.Moment-selected.MomentKnM*ratio)<1e-8,"pair diagram moment matches optimizer");
    Check(Math.Abs(charts.Analysis.Shear-selected.ShearKn*ratio)<1e-8,"pair diagram shear matches optimizer");
    Check(Math.Abs(charts.Analysis.DeflectionMm-selected.DeflectionMm*ratio)<1e-8,"pair diagram retains optimizer deflection");
   }
   Check(Find<TextBlock>(charts).Any(t=>t.Text.StartsWith("Deflection (mm)")&&t.Text.Contains("L/270")&&t.Text.Contains("utilization")),"diagram shows selected ratio and utilization");
   Check(Find<TextBlock>(charts).Any(t=>t.Text.StartsWith("Shear (kN)") && t.Text.Contains("22.000")),"pair shear limit shown as 22 kN");
   Check(Find<TextBlock>(charts).Any(t=>t.Text.StartsWith("Moment (kNm)") && t.Text.Contains("10.000")),"pair moment limit shown as 10 kNm");
   Check(!diagram.SelectAt(new Point(-5,-5)),"empty plan area does not select a member");
   charts.Close();Pump();Check(window.TowerDiagrams==null,"closing diagrams clears window reference");
   diagram.SelectAt(new Point(firstHit.Item1.X+firstHit.Item1.Width/2,firstHit.Item1.Y+4));Pump();charts=window.TowerDiagrams;
   Check(charts!=null && charts.IsVisible,"main-plan click reopens diagrams after closing");
   if(args.Length>0){var root=(FrameworkElement)((ScrollViewer)charts.Content).Content;root.Measure(new Size(940,double.PositiveInfinity));int chartHeight=(int)Math.Ceiling(root.DesiredSize.Height);root.Arrange(new Rect(0,0,940,chartHeight));root.UpdateLayout();var bitmap=new RenderTargetBitmap(940,chartHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.ChangeExtension(args[0],"diagrams.png")))encoder.Save(output);}
   Check(window.BuildReport().Contains("Support Schedule") && window.BuildReport().Contains("bounded") && window.BuildReport().Contains("Primary Stock Schedule"),"tower report schedule and scope");
   vm.BeamLengthMm=6500;Pump();Check(!vm.IsCalculationValid && vm.TowerResult==null,"editing clears tower");
   Check(window.TowerDiagrams==null && !charts.IsVisible && !diagram.IsSelectionEnabled,"editing closes stale diagrams and selection");
   window.RestoreCalculation(towerSaved);Pump();Check(vm.UseTowers && vm.BeamLengthMm==6100 && !vm.IsCalculationValid,"tower restore requires fresh search");Optimize();
   Check(vm.TowerResult.PositionsMm.SequenceEqual(coordinates),"tower record reproduces layout");
   Check(vm.StockLengthsText==TowerStockPlanner.DefaultLengths && vm.StockEndSetbackMm==150,"stock settings restored");
   Enter("BeamLengthMm","6001");Optimize();Check(!vm.IsCalculationValid && vm.WarningMessage.Contains("exactly"),"no exact stock cover is shown as failure");
   Enter("BeamLengthMm","6100");Optimize();
   Enter("MaximumConcreteEndOverhangMm","75");Enter("BeamLengthMm","6750");Enter("TowerLegCapacityKn","70");Optimize();Render();
   Check(vm.TowerResult.LegCount==8,"6750 eight-leg UI result");
   Check(diagram.DrawnBayDimensionCount==vm.TowerResult.BayCount,"plan dimensions all cross bays");
   var overhangSaved=window.CaptureCalculation();window.RestoreCalculation(overhangSaved);Pump();
   Check(vm.MaximumConcreteEndOverhangMm==75,"end allowance survives save/load");Optimize();
   var actualLayout=vm.TowerResult;
   var gapLayout=TowerOptimizer.Evaluate(new TowerInput { LengthMm=6600, TotalLineLoad=vm.TotalLineLoad, LegCapacityKn=70, DeflectionLimitMm=10, StockLengthsMm=new[]{3300.0} }, new[]{650.0,2650,3950,5950},new[]{true,false,true},1524,false);
   typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,gapLayout);diagram.SetModel(vm);Render();
   Check(diagram.DrawnGapDimensionCount==1 && diagram.DrawnBayDimensionCount==2,"isolated tower gap and bay dimensions");
   if(args.Length>0) SavePreview(Path.ChangeExtension(args[0],"gap-plan.png"),1040,860);
   var rotatedLayout=TowerOptimizer.Evaluate(new TowerInput { LengthMm=3300, TotalLineLoad=vm.TotalLineLoad, LegCapacityKn=70, DeflectionLimitMm=10, StockLengthsMm=new[]{3300.0} }, new[]{888.0,2412},new[]{true},1000,true);
   typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,rotatedLayout);diagram.SetModel(vm);Render();
   Check(diagram.DrawnBayDimensionCount==1 && diagram.DrawnGapDimensionCount==0,"rotated frame span dimension");
   typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,actualLayout);diagram.SetModel(vm);Render();
   if(args.Length>0){File.WriteAllText(Path.ChangeExtension(args[0],"tower-report.html"),window.BuildReport());SavePreview(Path.ChangeExtension(args[0],"tower.png"),1040,860);}
   var exclusionBox=(CheckBox)window.FindName("Exclude600StandaloneCheckBox");
   Check(exclusionBox.IsChecked==false,"standalone 600 checkbox defaults off");
   exclusionBox.IsChecked=true;Pump();Check(vm.Exclude600Standalone && !vm.IsCalculationValid,"checkbox binds and clears stale result");Optimize();
   var exclusionRecord=window.CaptureCalculation();Check(window.BuildReport().Contains("Exclude600Standalone") || window.BuildReport().Contains("Exclude600 Standalone"),"report records standalone exclusion");
   vm.Exclude600Standalone=false;window.RestoreCalculation(exclusionRecord);Pump();Check(vm.Exclude600Standalone && !vm.IsCalculationValid,"checkbox save/load round trip");
   exclusionRecord.Inputs.RemoveAll(f=>f.Name=="Exclude600Standalone");window.RestoreCalculation(exclusionRecord);Pump();Check(!vm.Exclude600Standalone,"legacy files retain prior unrestricted behavior");
   var old=towerSaved;old.Inputs.RemoveAll(f=>new[]{"UseTowers","BeamLengthMm","TowerLegCapacityKn","StockLengthsText","StockEndSetbackMm","MaximumConcreteEndOverhangMm"}.Contains(f.Name));
   window.RestoreCalculation(old);Pump();Check(!vm.UseTowers && vm.IsCalculationValid,"legacy prop record loads");
   var reopened=new BeamCalculatorWindow { DataContext=vm };Pump();
   Check(((Grid)reopened.FindName("SupportSystemPicker")).Visibility==Visibility.Collapsed,"reopening retained model preserves chosen system");reopened.Close();
   vm.HasSelectedSupportSystem=false;window.RestoreCalculation(old);Pump();
   Check(vm.HasSelectedSupportSystem && picker.Visibility==Visibility.Collapsed,"loading saved calculation selects its support system");
   foreach(string fixedLimit in new[]{"3","5","10"}) {
    var legacy=window.CaptureCalculation();legacy.Inputs.RemoveAll(f=>f.Name=="DeflectionCriterion" || f.Name=="DeflectionLimitMm");legacy.Inputs.Add(CalculationFiles.Field("DeflectionLimitMm",double.Parse(fixedLimit,CultureInfo.InvariantCulture)));
    window.RestoreCalculation(legacy);Pump();Check(vm.DeflectionLimit.Denominator==0 && vm.DeflectionLimit.Millimetres==double.Parse(fixedLimit,CultureInfo.InvariantCulture),"legacy fixed criterion preserved");
    Check(combo.Items.Count==4 && combo.SelectedItem==vm.DeflectionLimit,"legacy criterion visible without losing selection");
    var roundtrip=window.CaptureCalculation();window.RestoreCalculation(roundtrip);Pump();Check(vm.DeflectionLimit.Denominator==0,"legacy fixed re-save remains fixed");
   }
   combo.SelectedIndex=1;Pump();Check(vm.DeflectionLimit.Label=="L/350"&&combo.Items.Count==3,"switching legacy to ratio removes legacy-only option");
   var invalidCriterion=window.CaptureCalculation();invalidCriterion.Inputs.Single(f=>f.Name=="DeflectionCriterion").Value="L/0";
   bool criterionRejected=false;try {window.RestoreCalculation(invalidCriterion);}catch(ArgumentException){criterionRejected=true;}
   Check(criterionRejected&&vm.DeflectionLimit.Label=="L/350","invalid saved criterion leaves current calculation intact");
   vm.UseTowers=true;Pump();Check(vm.TowerResult==null && !vm.IsCalculationValid,"switching support modes invalidates old results");
   vm.Depth=1750;vm.Width=1100;vm.BeamLengthMm=6750;vm.TowerLegCapacityKn=70;vm.LeftSlabThickness=300;vm.HasLeftSlab=true;vm.HasRightSlab=false;vm.TransferSlabLoads=true;vm.UpperSupportSpacingMm=275;Pump();
   Check(!Input("SlabStripWidthMm").IsEnabled && Input("LeftSlabBayMm").IsEnabled && !Input("RightSlabBayMm").IsEnabled,"transfer mode enables only applicable bay inputs");
   Optimize();Check(vm.IsCalculationValid && vm.TowerResult.Transfer!=null,"transferred load optimization through WPF");
   Render();var transferred=vm.TowerResult.Transfer;
   Check(diagram.DrawnTransferRowCount==0 && diagram.DrawnFootCount==0,"plan omits transverse H20s and upper feet for a cleaner tower view");
   Check(diagram.MinWidth>1000,"transfer plan can scroll with readable row spacing");
   var transferRecord=window.CaptureCalculation();Check(window.BuildReport().Contains("UpperSupportSpacingMm") || window.BuildReport().Contains("Upper Support Spacing Mm"),"report contains transfer inputs");
   Check(window.BuildReport().Contains("Beam + left slab") && window.BuildReport().Contains("receiving beam slab load"),"report contains discrete transfer cases and load coverage");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var lowerHit=diagram.MemberTargets.First(h=>h.Item2>=vm.TowerResult.StockPlan.Pieces.Length && h.Item2<vm.TowerResult.StockPlan.Pieces.Length+transferred.RowsMm.Length);
   Check(diagram.SelectAt(new Point(lowerHit.Item1.X+lowerHit.Item1.Width/2,lowerHit.Item1.Y+lowerHit.Item1.Height/2)),"select lower paired bearer in main plan");Pump();
   Check(window.TowerDiagrams.LoadedAnalysis!=null && window.TowerDiagrams.Analysis==null,"transfer diagrams use point-loaded analysis");
   window.TowerDiagrams.SelectMember(0,0,-1);Pump();Check(window.TowerDiagrams.LoadedAnalysis==transferred.PrimaryCases.Last()[0].Analysis,"selected primary plots exact transferred point reactions");
   var upperHit=diagram.MemberTargets.First(h=>h.Item2>=vm.TowerResult.StockPlan.Pieces.Length+transferred.RowsMm.Length);
   Check(diagram.SelectAt(new Point(upperHit.Item1.X+upperHit.Item1.Width/2,upperHit.Item1.Y+upperHit.Item1.Height/2)),"select upper pair from main section");Pump();Check(window.TowerDiagrams.Title.Contains("Upper left"),"upper pair diagram selection");
   if(args.Length>0){SavePreview(Path.ChangeExtension(args[0],"transferred.png"),1040,860);File.WriteAllText(Path.ChangeExtension(args[0],"transferred-report.html"),window.BuildReport());
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(diagram.ActualWidth),(int)Math.Ceiling(diagram.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(diagram);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"transfer-plan.png")))encoder.Save(file);}
   vm.TransferSlabLoads=false;window.RestoreCalculation(transferRecord);Pump();Check(vm.IsTransferMode && vm.UpperSupportSpacingMm==275 && !vm.IsCalculationValid,"transfer input round trip invalidates stale results");
   Enter("UpperSupportSpacingMm","oops");Check(!vm.CanOptimizeTowers,"invalid active transfer text blocks optimization");vm.TransferSlabLoads=false;Pump();Check(vm.CanOptimizeTowers,"inactive transfer text ignored in strip mode");
   Enter("UpperSupportSpacingMm","275");vm.TransferSlabLoads=true;Pump();
   ((Button)window.FindName("AddBeamBayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render();
   Check(vm.IsNetworkMode && vm.NetworkBeams.Count==3 && vm.NetworkBays.Count==2,"add button extends to three beams and two slab bays");
   Check(!Input("Depth").IsEnabled && !Input("LeftSlabBayMm").IsEnabled,"network uses individual beam and bay inputs");
   vm.NetworkBeams[1].DepthMm=1200;vm.NetworkBeams[2].DepthMm=1500;vm.NetworkBeams[2].WidthMm=900;vm.NetworkBays[1].WidthMm=3500;vm.NetworkBays[1].ThicknessMm=250;Render();
   var depthInput=VisualInputs((DependencyObject)window.Content).Single(t=>t.DataContext==vm.NetworkBeams[1] && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="DepthMm");
   Check(depthInput.Text=="1200","each beam depth is editable in the UI");
   depthInput.Text="oops";depthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(!vm.CanOptimizeTowers,"invalid templated beam depth blocks optimization");
   depthInput.Text="1200";depthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Optimize();Check(vm.IsCalculationValid && vm.NetworkResult!=null,"network optimization from WPF");
   Render();var networkPlan=(TransferNetworkPlan)window.FindName("NetworkPlan");
   Check(networkPlan.DrawnTowerLegCount==vm.NetworkResult.LegCount && networkPlan.DrawnTransversePlanCount==0,"full network plan shows all towers and no transverse pairs");
   Check(networkPlan.DrawnPrimaryLengthCount==vm.NetworkResult.Beams.Sum(b=>b.StockPlan.Pieces.Length*2),"each longitudinal primary stock length labelled in plan");
   Check(networkPlan.DrawnSecondaryLengthCount==3,"secondary stock length shown below each beam in section");
   var networkSaved=window.CaptureCalculation();var networkReport=window.BuildReport();Check(networkReport.Contains("Network") && networkReport.Contains("1200") && networkReport.Contains("shutter"),"report includes complete network and distinct beam depths");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var networkHit=networkPlan.MemberTargets.First(h=>h.Item2==1 && h.Item3==0 && h.Item4==0);
   Check(networkPlan.SelectAt(new Point(networkHit.Item1.X+networkHit.Item1.Width/2,networkHit.Item1.Y+networkHit.Item1.Height/2)),"select B2 primary directly from full plan");Pump();
   Check(vm.SelectedNetworkBeam==1 && window.TowerDiagrams.LoadedAnalysis==vm.NetworkResult.Beams[1].Transfer.PrimaryCases.Last()[0].Analysis,"diagrams use the selected beam's own load analysis");
   for(int selectedBeam=0;selectedBeam<vm.NetworkResult.Beams.Length;selectedBeam++)
   {
    var layout=vm.NetworkResult.Beams[selectedBeam];int runs=layout.StockPlan.Pieces.Length;
    var primaryHit=networkPlan.MemberTargets.First(h=>h.Item2==selectedBeam && h.Item3==0 && h.Item4==1);
    Check(networkPlan.SelectAt(new Point(primaryHit.Item1.X+primaryHit.Item1.Width/2,primaryHit.Item1.Y+primaryHit.Item1.Height/2)),"select each network beam's right primary from plan");Pump();
    Check(vm.SelectedNetworkBeam==selectedBeam && window.TowerDiagrams.LoadedAnalysis==layout.Transfer.PrimaryCases.Last()[runs].Analysis,"primary diagrams match the actual selected beam and line");
    Check(window.TowerDiagrams.SelectedTransferMember.H20Count==2 && window.TowerDiagrams.SelectedTransferMember.ShearLimitKn==22,"primary diagrams keep paired capacity");
    var beamCase=Find<Button>(window.TowerDiagrams).Single(b=>Equals(b.Content,"Beam"));beamCase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
    Check(window.TowerDiagrams.SelectedLoadCase==0 && window.TowerDiagrams.LoadedAnalysis==layout.Transfer.PrimaryCases[0][runs].Analysis,"beam-only load-case button changes actual plotted analysis");
    var allCase=Find<Button>(window.TowerDiagrams).Single(b=>Equals(b.Content,layout.Transfer.CaseNames.Last()));allCase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
    var secondaryHit=networkPlan.MemberTargets.First(h=>h.Item2==selectedBeam && h.Item3>=runs && h.Item3<runs+layout.Transfer.RowsMm.Length);
    Check(networkPlan.SelectAt(new Point(secondaryHit.Item1.X+secondaryHit.Item1.Width/2,secondaryHit.Item1.Y+secondaryHit.Item1.Height/2)),"select each lower transverse pair from section");Pump();
    Check(window.TowerDiagrams.LoadedAnalysis==layout.Transfer.LowerCases.Last()[secondaryHit.Item3-runs].Analysis && window.TowerDiagrams.SelectedTransferMember.H20Count==2,"lower transverse diagrams use their point loads and paired capacity");
   }
   for(int bay=0;bay<vm.NetworkResult.Bays.Length;bay++)
   {
    var layout=vm.NetworkResult.Beams[bay];int firstUpper=layout.StockPlan.Pieces.Length+layout.Transfer.RowsMm.Length;
    var slabHit=networkPlan.MemberTargets.First(h=>h.Item2==bay && h.Item3>=firstUpper);
    Check(networkPlan.SelectAt(new Point(slabHit.Item1.X+slabHit.Item1.Width/2,slabHit.Item1.Y+slabHit.Item1.Height/2)),"select single slab H20 directly from main section");Pump();
    var original=layout.Transfer.UpperMembers.Single(m=>m.Name=="Upper right");var selected=window.TowerDiagrams.SelectedTransferMember;
    Check(window.TowerDiagrams.Title.Contains("single H20") && selected.H20Count==1 && selected.ShearLimitKn==11 && selected.MomentLimitKnM==5,"slab diagram title and capacities are single H20");
    Check(Math.Abs(window.TowerDiagrams.LoadedAnalysis.DeflectionMm-original.Analysis.DeflectionMm)<1e-8 && Math.Abs(selected.Uniform[0].KnPerM-original.Uniform[0].KnPerM)<1e-8,"slab diagram uses the optimizer's loaded row and single-member deflection");
    Check(!Find<Button>(window.TowerDiagrams).Any(),"slab selection clears lower-member loading-stage buttons");
   }
   if(args.Length>0){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(networkPlan.ActualWidth),(int)Math.Ceiling(networkPlan.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(networkPlan);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"network-plan.png")))encoder.Save(file);File.WriteAllText(Path.ChangeExtension(args[0],"network-report.html"),networkReport);}
   vm.NetworkBeams[1].DepthMm=1000;Check(!vm.IsCalculationValid && window.TowerDiagrams==null,"editing any beam invalidates network diagrams");
   window.RestoreCalculation(networkSaved);Pump();Check(vm.IsNetworkMode && vm.NetworkBeams.Count==3 && vm.NetworkBeams[1].DepthMm==1200,"network save/load round trip retains per-beam depth and all bays");
   ((Button)window.FindName("RemoveBeamBayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(vm.NetworkBeams.Count==2 && vm.NetworkBays.Count==1,"remove last bay button");
   window.Close();Console.WriteLine($"Passed {checks} beam WPF checks.");return 0;
  }
  catch(Exception ex){Console.Error.WriteLine(ex);window?.Close();return 1;}
 }
 static void SavePreview(string path,int width,int height){
  var bitmap=Render(width,height);var root=(FrameworkElement)window.Content;var import=(Button)window.FindName("ImportButton");
  var corner=import.TranslatePoint(new Point(import.ActualWidth,import.ActualHeight),root);Check(corner.X<=width+.1&&corner.Y<=height+.1,"footer stays inside resized window");
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
 }
 static void Optimize(){var task=vm.OptimizeTowersAsync();var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){Pump();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>30)throw new Exception("Tower search timeout");}task.GetAwaiter().GetResult();Pump();}

}
