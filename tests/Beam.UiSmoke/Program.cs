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
 static IEnumerable<Button> VisualButtons(DependencyObject root) {if(root is Button button)yield return button;if(root is Visual)for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var value in VisualButtons(VisualTreeHelper.GetChild(root,i)))yield return value;}
 static RenderTargetBitmap Render(int width=1040,int height=860)
 {
  Pump();var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);return bitmap;
 }
 static void CheckUniformPrimary(TowerDiagramWindow diagrams,int denominator)
 {
  var design=diagrams.SelectedTransferMember;var shown=diagrams.DisplayedTransferMember;
  Check(shown==design && diagrams.LoadedAnalysis==design.Analysis,"primary displays the exact optimizer design response");
  Check(shown.Points.Length==0 && shown.Uniform.Length>0,"primary uses only distributed design loads");
  var reference=LoadedBeamAnalysis.Analyze(shown.LengthMm,shown.SupportsMm,shown.Uniform,null,TowerOptimizer.PairEI,denominator);
  Check(Math.Abs(shown.Analysis.MomentKnM-reference.MomentKnM)<1e-8 && Math.Abs(shown.Analysis.ShearKn-reference.ShearKn)<1e-8 && Math.Abs(shown.Analysis.DeflectionMm-reference.DeflectionMm)<1e-8,"plotted UDL demands agree with an independent calculation");
  Check(shown.Utilization<=1+1e-8,"displayed primary passes design limits");
  Check(Find<TextBlock>(diagrams).Any(t=>t.Text.StartsWith("Secondary beam spacing:")) && Find<TextBlock>(diagrams).Count(t=>t.Text.StartsWith("UDL design · "))==3,"compact summary retains labelled UDL response plots");
  Check(!Find<CheckBox>(diagrams).Any(),"no primary point-load or approximate-UDL switch remains");
  Check(Find<TextBlock>(diagrams).Any(t=>t.Text.StartsWith("Loading diagram")) && diagrams.LoadingDiagram!=null,"primary loading diagram remains available");
 }
 [STAThread] static int Main(string[] args)
 {
  try
  {
   System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
   SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
   if(args.Contains("--preview-smoke"))return StandalonePreview.Run(true);
   if(args.Contains("--preview"))return StandalonePreview.Run();
   WorkspacePresentationTests.Run();
   LoadingPlotTests.Run(args.FirstOrDefault(a=>a.EndsWith(".png")));
   if(args.Contains("--equivalent-loading-ui")){EquivalentLoadingUiTests.Run(args.FirstOrDefault(a=>a.EndsWith(".png")));return 0;}if(args.Contains("--ws10-ui")){WS10UiTests.Run(args.FirstOrDefault(a=>a.EndsWith(".png")));return 0;}
   if(args.Contains("--drawing-ui")){D3WorkspaceUiTests.Run(args.FirstOrDefault(a=>a.EndsWith(".png")));PresentationUiTests.Run(args.FirstOrDefault(a=>a.EndsWith(".png")));DimensionLaneUiTests.Run();return 0;}
   window=new BeamCalculatorWindow();vm=new BeamCalculatorViewModel();window.DataContext=vm;Render();
   var secondaryText=(TextBlock)window.FindName("SecondarySpacingText");
   Check(secondaryText.Text=="407 mm" && !secondaryText.Text.Contains("chart"),"secondary result is bound to shared plywood model");
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
   Check(!Find<Expander>(window).Any(e=>Equals(e.Header,"Calculation assumptions")),"Dokaflex calculation assumptions dropdown removed");
   Check(diagram.IsSelectionEnabled && diagram.MemberTargets.Count==2,"Dokaflex primary H20s selectable without arming a button");
   for(int line=0;line<2;line++)
   {
    var bounds=diagram.MemberTargets.Single(t=>t.Item3==line).Item1;bool clicked=false;
    for(double y=bounds.Top;y<=bounds.Bottom && !clicked;y+=2)
     for(double x=bounds.Left;x<=bounds.Right && !clicked;x+=2)
      if(diagram.SelectAt(new Point(x,y)) && window.TowerDiagrams.SelectedLine==line)clicked=true;
    Check(clicked && window.TowerDiagrams.Title.Contains("single H20"),"click primary opens the selected Dokaflex single-H20 response");
    var analysis=window.TowerDiagrams.Analysis;var expected=vm.AnalyzePropPrimary(line,2);
    Check(Math.Abs(analysis.Moment-expected.Moment)<1e-9 && window.TowerDiagrams.LoadingDiagram!=null,"Dokaflex diagram uses actual line response and includes loading plot");
   }
   vm.Width+=25;Pump();Check(window.TowerDiagrams==null && diagram.IsSelectionEnabled,"valid input edit closes stale response and leaves primaries clickable");
   var plywoodSelector=(ComboBox)window.FindName("PlywoodSelector");
   Check(plywoodSelector.Items.Count==3 && plywoodSelector.SelectedItem==PlywoodCatalog.Default,"plywood selector exposes supplied products with 18 mm default");
   var plywoodLimit=(ComboBox)window.FindName("PlywoodDeflectionSelector");var towerPlywoodLimit=(ComboBox)window.FindName("TowerPlywoodDeflectionSelector");
   Check(plywoodLimit.Items.Count==3 && Equals(plywoodLimit.SelectedItem,"L/350"),"compact independent plywood criterion default");
   var originalH20=vm.DeflectionLimit;plywoodLimit.SelectedItem="L/500";Pump();
   Check(vm.PlywoodCriterion=="L/500" && vm.MaximumSecondarySpacingMm==350 && vm.DeflectionLimit==originalH20 && Equals(towerPlywoodLimit.SelectedItem,"L/500"),"plywood limit updates both modes without changing H20");
   var limitRecord=window.CaptureCalculation();plywoodLimit.SelectedItem="L/270";Pump();window.RestoreCalculation(limitRecord);Pump();
   Check(vm.PlywoodDeflectionLimit=="L/500" && vm.DeflectionLimit==originalH20,"distinct beam limits survive save/load");
   var badLimit=window.CaptureCalculation();badLimit.Inputs.Single(f=>f.Name=="PlywoodDeflectionLimit").Value="L/100";
   bool badLimitRejected=false;try {window.RestoreCalculation(badLimit);}catch(ArgumentException){badLimitRejected=true;}
   Check(badLimitRejected && vm.PlywoodDeflectionLimit=="L/500","invalid saved plywood criterion preserves retained model");
   limitRecord.Inputs.RemoveAll(f=>f.Name=="PlywoodDeflectionLimit");window.RestoreCalculation(limitRecord);Pump();
   Check(vm.PlywoodDeflectionLimit=="L/"+originalH20.Denominator,"older beam record migrates its H20 ratio to plywood");
   vm.PlywoodDeflectionLimit="L/350";Pump();
   Check(!Find<TextBlock>(window).Any(t=>t.Text.Contains("Chart maximum") || t.Text.Contains("approximate chart reading")),"plywood chart prose removed from beam UI");
   plywoodSelector.SelectedItem=vm.PlywoodOptions.Single(p=>p.ThicknessMm==9);Pump();
   Check(!vm.IsCalculationValid && vm.MaximumSecondarySpacingMm==0,"9 mm cannot round an unsafe beam spacing up to 250 mm");
   vm.Depth=150;Pump();
   Check(vm.MaximumSecondarySpacingMm==275 && vm.FormworkDepthMm==409,"plywood selection updates spacing and buildup");
   var plywoodRecord=window.CaptureCalculation();plywoodSelector.SelectedItem=vm.PlywoodOptions.Single(p=>p.ThicknessMm==21);Pump();window.RestoreCalculation(plywoodRecord);Pump();
   Check(vm.SelectedPlywood.ThicknessMm==9,"plywood selection survives save/load");
   var unknownPlywood=window.CaptureCalculation();unknownPlywood.Inputs.Single(f=>f.Name=="PlywoodProfileId").Value="unknown";
   bool plywoodRejected=false;try {window.RestoreCalculation(unknownPlywood);}catch(ArgumentException){plywoodRejected=true;}
   Check(plywoodRejected && vm.SelectedPlywood.ThicknessMm==9,"unknown saved plywood does not alter retained calculation");
   plywoodRecord.Inputs.RemoveAll(f=>f.Name=="PlywoodProfileId");window.RestoreCalculation(plywoodRecord);Pump();
   Check(vm.SelectedPlywood==PlywoodCatalog.Default,"old calculations without plywood choice retain 18 mm");
   vm.Depth=500;Pump();
   vm.HasLeftSlab=true;Render();
   Check(plywoodSelector.TranslatePoint(new Point(0,0),(FrameworkElement)window.Content).Y>((TextBox)window.FindName("SlabStripWidthInput")).TranslatePoint(new Point(0,0),(FrameworkElement)window.Content).Y,"Dokaflex plywood follows adjacent slab strip width");
   vm.HasLeftSlab=false;Pump();
   Check(Equals(import.Content,"Select helper lines"),"Dokaflex beam action uses helper-line selection");
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
   var families=(ComboBox)window.FindName("PropFamilyInput");var sizes=(ComboBox)window.FindName("PropSizeInput");var positions=(ComboBox)window.FindName("OuterTubePositionInput");
   Check(families.Items.Count==2 && Equals(families.SelectedItem,"Prop Type Eurex eco 20"),"separate prop family dropdown shares catalog");
   families.SelectedItem="DokaFit Prop";Pump();sizes.SelectedItem="300";Pump();positions.SelectedItem="at top";Pump();
   Check(vm.SelectedProp.CapacityKey=="DokaFit Prop 300 at top","three prop selectors update existing capacity selection");
   Check(!positions.Items.Contains("fixed"),"unsupported outer tube position excluded");
   vm.SelectedProp=BeamPropOption.All.First(p=>p.CapacityKey=="400 at bottom");Pump();
   Check(Equals(families.SelectedItem,"Prop Type Eurex eco 20") && Equals(sizes.SelectedItem,"400") && Equals(positions.SelectedItem,"at bottom"),"restored prop configuration synchronizes all selectors");
   vm.SelectedProp=BeamPropOption.All.First(p=>p.CapacityKey=="DokaFit Prop 300 at top");Pump();Enter("FloorHeight","9999");Check(!vm.IsCalculationValid&&!import.IsEnabled,"unsupported extension blocks import");Enter("FloorHeight","3000");Check(vm.IsCalculationValid,"floor height recovery");
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
   Check(diagram.DrawnAcrossDimensionCount==1,"rotated plan has the transverse frame dimension");
   typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,actualLayout);diagram.SetModel(vm);Render();
   if(args.Length>0){File.WriteAllText(Path.ChangeExtension(args[0],"tower-report.html"),window.BuildReport());SavePreview(Path.ChangeExtension(args[0],"tower.png"),1040,860);}
   var exclusionBox=(CheckBox)window.FindName("Exclude600StandaloneCheckBox");
   Check(exclusionBox.IsChecked==false,"standalone 600 checkbox defaults off");
   exclusionBox.IsChecked=true;Pump();Check(vm.Exclude600Standalone && !vm.IsCalculationValid,"checkbox binds and clears stale result");Optimize();
   var exclusionRecord=window.CaptureCalculation();Check(window.BuildReport().Contains("Exclude600Standalone") || window.BuildReport().Contains("Exclude600 Standalone"),"report records standalone exclusion");
   vm.Exclude600Standalone=false;window.RestoreCalculation(exclusionRecord);Pump();Check(vm.Exclude600Standalone && !vm.IsCalculationValid,"checkbox save/load round trip");
   exclusionRecord.Inputs.RemoveAll(f=>f.Name=="Exclude600Standalone");window.RestoreCalculation(exclusionRecord);Pump();Check(!vm.Exclude600Standalone,"legacy files retain prior unrestricted behavior");
   Check(window.FindName("IgnoreUpliftCheckBox")==null && vm.IgnoreUplift,"uplift exclusion is fixed and has no editable checkbox");
   Optimize();var upliftRecord=window.CaptureCalculation();
   vm.IgnoreUplift=false;Check(vm.IgnoreUplift && vm.IsCalculationValid,"legacy setter cannot disable exclusion or alter a checked result");
   window.RestoreCalculation(upliftRecord);Pump();Check(vm.IgnoreUplift && !vm.IsCalculationValid,"restored drawing retains fixed exclusion and requires recalculation");
   upliftRecord.Inputs.RemoveAll(f=>f.Name=="IgnoreUplift");window.RestoreCalculation(upliftRecord);Pump();Check(vm.IgnoreUplift,"older files retain the fixed uplift exclusion");
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
   Check(secondaryText.Text==vm.MaximumSecondarySpacingMm.ToString("0",CultureInfo.CurrentCulture)+" mm","D3 displays compact shared plywood result before optimization");
   vm.Depth=900;vm.Width=800;vm.BeamLengthMm=10600;vm.MaximumConcreteEndOverhangMm=75;vm.TowerLegCapacityKn=70;vm.AllowCustomH20Lengths=true;vm.LeftSlabBayMm=2575;vm.LeftSlabThickness=300;vm.HasLeftSlab=true;vm.HasRightSlab=false;vm.TransferSlabLoads=true;Pump();
   Check(!Input("SlabStripWidthMm").IsEnabled && Input("LeftSlabBayMm").IsEnabled && !Input("RightSlabBayMm").IsEnabled,"transfer mode enables only applicable bay inputs");
   Optimize();Check(vm.IsCalculationValid && vm.TowerResult.Transfer!=null,"transferred load optimization through WPF: "+vm.WarningMessage+" · spacing "+vm.UpperSupportSpacingMm+" · end "+vm.MaximumConcreteEndOverhangMm);
   Render();var transferred=vm.TowerResult.Transfer;
   Check(diagram.DrawnTransferRowCount==0 && diagram.DrawnFootCount==0,"plan omits transverse H20s and upper feet for a cleaner tower view");
   Check(diagram.MinWidth>=1000,"transfer plan retains its minimum readable width for scrolling");
   var transferRecord=window.CaptureCalculation();Check(window.BuildReport().Contains("UpperSupportSpacingMm") || window.BuildReport().Contains("Upper Support Spacing Mm"),"report contains transfer inputs");
   Check(window.BuildReport().Contains("MaximumSecondarySpacingMm") || window.BuildReport().Contains("Maximum Secondary Spacing Mm"),"tower report includes plywood spacing");
   Check(window.BuildReport().Contains("Beam + left slab") && window.BuildReport().Contains("physical slab reference including external end supports"),"report contains discrete transfer cases and load coverage");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var lowerHit=diagram.MemberTargets.First(h=>h.Item2>=vm.TowerResult.StockPlan.Pieces.Length && h.Item2<vm.TowerResult.StockPlan.Pieces.Length+transferred.RowsMm.Length);
   Check(diagram.SelectAt(new Point(lowerHit.Item1.X+lowerHit.Item1.Width/2,lowerHit.Item1.Y+lowerHit.Item1.Height/2)),"select lower paired bearer in main plan");Pump();
   Check(window.TowerDiagrams.LoadedAnalysis!=null && window.TowerDiagrams.Analysis==null,"transfer diagrams use distributed primary design analysis");
   window.TowerDiagrams.SelectMember(0,0,-1);Pump();CheckUniformPrimary(window.TowerDiagrams,vm.DeflectionLimit.Denominator);Check(window.TowerDiagrams.ActualLoadedAnalysis==transferred.PrimaryCases[window.TowerDiagrams.SelectedLoadCase][0].Analysis,"selected primary retains exact design reactions alongside its UDL view");
   if(args.Length>0){var content=(FrameworkElement)((ScrollViewer)window.TowerDiagrams.Content).Content;content.Measure(new Size(868,double.PositiveInfinity));content.Arrange(new Rect(0,0,868,content.DesiredSize.Height));content.UpdateLayout();
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"primary-udl.png")))encoder.Save(file);}
   var upperHit=diagram.MemberTargets.First(h=>h.Item2>=vm.TowerResult.StockPlan.Pieces.Length+transferred.RowsMm.Length);
   Check(diagram.SelectAt(new Point(upperHit.Item1.X+upperHit.Item1.Width/2,upperHit.Item1.Y+upperHit.Item1.Height/2)),"select upper pair from main section");Pump();Check(window.TowerDiagrams.Title.Contains("Upper left"),"upper pair diagram selection");
   if(args.Length>0){SavePreview(Path.ChangeExtension(args[0],"transferred.png"),1040,860);File.WriteAllText(Path.ChangeExtension(args[0],"transferred-report.html"),window.BuildReport());
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(diagram.ActualWidth),(int)Math.Ceiling(diagram.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(diagram);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"transfer-plan.png")))encoder.Save(file);}
   vm.TransferSlabLoads=false;window.RestoreCalculation(transferRecord);Pump();Check(vm.IsTransferMode && vm.UpperSupportSpacingMm==vm.MaximumSecondarySpacingMm && !vm.IsCalculationValid,"transfer input round trip invalidates stale results");
   var spacingInput=Input("UpperSupportSpacingMm");Check(spacingInput.IsReadOnly && !spacingInput.IsEnabled && spacingInput.GetBindingExpression(TextBox.TextProperty).ParentBinding.Mode==System.Windows.Data.BindingMode.OneWay,"automatic secondary spacing is locked and greyed out");
   vm.Depth=vm.Depth+25;Pump();Check(spacingInput.Text==vm.UpperSupportSpacingMm.ToString(CultureInfo.CurrentCulture),"locked field refreshes when geometry changes");vm.Depth=vm.Depth-25;Pump();
   // The designer may choose a closer listed spacing within the plywood maximum.
   vm.SelectedPlywood=PlywoodCatalog.Default;vm.LeftSlabBayMm=3900;vm.Depth=1500;vm.Width=400;
   ((Button)window.FindName("AddBeamBayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render();
   Check(vm.IsNetworkMode && vm.NetworkBeams.Count==3 && vm.NetworkBays.Count==2,"add button extends to three beams and two slab bays");
   Check(!Input("Depth").IsEnabled && !Input("LeftSlabBayMm").IsEnabled,"network uses individual beam and bay inputs");
   vm.NetworkBeams[1].DepthMm=1200;vm.NetworkBeams[2].DepthMm=1500;vm.NetworkBeams[2].WidthMm=900;vm.NetworkBays[1].WidthMm=3500;vm.NetworkBays[1].ThicknessMm=250;Render();
   var depthInput=VisualInputs((DependencyObject)window.Content).Single(t=>t.DataContext==vm.NetworkBeams[1] && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="DepthMm");
   Check(depthInput.Text=="1200","each beam depth is editable in the UI");
   var beamSpacingInput=VisualInputs((DependencyObject)window.Content).Single(t=>t.DataContext==vm.NetworkBeams[1] && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="MaximumSecondarySpacingMm");
   var slabSpacingInput=VisualInputs((DependencyObject)window.Content).Single(t=>t.DataContext==vm.NetworkBays[0] && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="MaximumSecondarySpacingMm");
   Check(beamSpacingInput.IsReadOnly && !beamSpacingInput.IsEnabled && slabSpacingInput.IsReadOnly && !slabSpacingInput.IsEnabled,"individual beam and slab spacing fields are locked and greyed out");
   Check(beamSpacingInput.Text==vm.NetworkBeams[1].MaximumSecondarySpacingMm.ToString(CultureInfo.CurrentCulture) && slabSpacingInput.Text==vm.NetworkBays[0].MaximumSecondarySpacingMm.ToString(CultureInfo.CurrentCulture),"dimension rows display their own calculated plywood limits");
   Check(vm.BuildDiagnosticReport().Contains(vm.NetworkBays[0].Label+": slab"),"diagnostics identify each slab's own limit");
   var widthInput=VisualInputs((DependencyObject)window.Content).Single(t=>t.DataContext==vm.NetworkBeams[2] && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="WidthMm");
   widthInput.Text="950";widthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(vm.NetworkBeams[2].WidthMm==950 && !vm.IsCalculationValid,"each beam width is editable and invalidates the result");
   widthInput.Text="oops";widthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(!vm.CanOptimizeTowers,"invalid individual beam width blocks optimization");
   widthInput.Text="900";widthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();
   depthInput.Text="oops";depthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(!vm.CanOptimizeTowers,"invalid templated beam depth blocks optimization");
   depthInput.Text="1200";depthInput.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Optimize();Check(vm.IsCalculationValid && vm.NetworkResult!=null,"network optimization from WPF: "+vm.WarningMessage);
   Render();var networkPlan=(TransferNetworkPlan)window.FindName("NetworkPlan");
   Check(networkPlan.DrawnTowerLegCount==vm.NetworkResult.LegCount && networkPlan.DrawnTransversePlanCount==0,"full network plan shows all towers and no transverse pairs");
   Check(networkPlan.DrawnPrimaryLengthCount==vm.NetworkResult.Beams.Sum(b=>b.StockPlan.Pieces.Length),"each paired primary stock length labelled once in plan");
   Check(networkPlan.SectionRows.All(r=>Math.Abs(r.Item3-networkPlan.ActualSectionPositionMm)<=40+1e-7),"section uses a single shared cut position");
   var networkSaved=window.CaptureCalculation();var networkReport=window.BuildReport();Check(networkReport.Contains("Network") && networkReport.Contains("1200") && networkReport.Contains("shutter"),"report includes complete network and distinct beam depths");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var networkHit=networkPlan.MemberTargets.First(h=>h.Item2==1 && h.Item3==0 && h.Item4==0);
   Check(networkPlan.SelectAt(new Point(networkHit.Item1.X+networkHit.Item1.Width/2,networkHit.Item1.Y+networkHit.Item1.Height/2)),"select B2 primary directly from full plan");Pump();
   Check(vm.SelectedNetworkBeam==1 && window.TowerDiagrams.ActualLoadedAnalysis==vm.NetworkResult.Beams[1].Transfer.PrimaryCases[window.TowerDiagrams.SelectedLoadCase][0].Analysis,"diagrams retain the selected beam's own design analysis");
   for(int selectedBeam=0;selectedBeam<vm.NetworkResult.Beams.Length;selectedBeam++)
   {
    var layout=vm.NetworkResult.Beams[selectedBeam];int runs=layout.StockPlan.Pieces.Length;
    var primaryHit=networkPlan.MemberTargets.First(h=>h.Item2==selectedBeam && h.Item3==0 && h.Item4==1);
    Check(networkPlan.SelectAt(new Point(primaryHit.Item1.X+primaryHit.Item1.Width/2,primaryHit.Item1.Y+primaryHit.Item1.Height/2)),"select each network beam's right primary from plan");Pump();
    Check(vm.SelectedNetworkBeam==selectedBeam && window.TowerDiagrams.ActualLoadedAnalysis==layout.Transfer.PrimaryCases[window.TowerDiagrams.SelectedLoadCase][runs].Analysis,"primary design data matches the actual selected beam and line");
    Check(window.TowerDiagrams.SelectedTransferMember.H20Count==2 && window.TowerDiagrams.SelectedTransferMember.ShearLimitKn==22,"primary diagrams keep paired capacity");
    CheckUniformPrimary(window.TowerDiagrams,vm.DeflectionLimit.Denominator);
    window.TowerDiagrams.LoadCaseSelector.SelectedIndex=0;Pump();
    Check(window.TowerDiagrams.SelectedLoadCase==0 && window.TowerDiagrams.ActualLoadedAnalysis==layout.Transfer.PrimaryCases[0][runs].Analysis,"beam-only load-case selector changes the retained actual analysis");
    CheckUniformPrimary(window.TowerDiagrams,vm.DeflectionLimit.Denominator);
    window.TowerDiagrams.LoadCaseSelector.SelectedIndex=layout.Transfer.CaseNames.Length-1;Pump();
    networkPlan.SectionPositionMm=layout.Transfer.RowsMm[0];networkPlan.InvalidateVisual();Render();
    var secondaryHit=networkPlan.MemberTargets.First(h=>h.Item2==selectedBeam && h.Item3>=runs && h.Item3<runs+layout.Transfer.RowsMm.Length);
    Check(networkPlan.SelectAt(new Point(secondaryHit.Item1.X+secondaryHit.Item1.Width/2,secondaryHit.Item1.Y+secondaryHit.Item1.Height/2)),"select each single lower secondary from section");Pump();
    Check(window.TowerDiagrams.LoadedAnalysis==layout.Transfer.LowerCases[window.TowerDiagrams.SelectedLoadCase][secondaryHit.Item3-runs].Analysis && window.TowerDiagrams.SelectedTransferMember.H20Count==1 && window.TowerDiagrams.SelectedTransferMember.ShearLimitKn==11 && window.TowerDiagrams.SelectedTransferMember.MomentLimitKnM==5,"lower secondary diagrams use their point loads and single capacity");
   }
   for(int bay=0;bay<vm.NetworkResult.Bays.Length;bay++)
   {
    var layout=vm.NetworkResult.Beams[bay];int firstUpper=layout.StockPlan.Pieces.Length+layout.Transfer.RowsMm.Length;
    networkPlan.SectionPositionMm=layout.Transfer.RowsMm[Array.IndexOf(layout.Transfer.RightUpperScales,layout.Transfer.RightUpperScales.Max())];networkPlan.InvalidateVisual();Render();
    var slabHit=networkPlan.MemberTargets.First(h=>h.Item2==bay && h.Item3>=firstUpper);
    Check(networkPlan.SelectAt(new Point(slabHit.Item1.X+slabHit.Item1.Width/2,slabHit.Item1.Y+slabHit.Item1.Height/2)),"select single slab H20 directly from main section");Pump();
    var original=layout.Transfer.UpperMembers.Single(m=>m.Name=="Upper right");var selected=window.TowerDiagrams.SelectedTransferMember;
    Check(window.TowerDiagrams.Title.Contains("single H20") && selected.H20Count==1 && selected.ShearLimitKn==11 && selected.MomentLimitKnM==5,"slab diagram title and capacities are single H20");
    Check(Math.Abs(window.TowerDiagrams.LoadedAnalysis.DeflectionMm-original.Analysis.DeflectionMm)<1e-8 && Math.Abs(selected.Uniform[0].KnPerM-original.Uniform[0].KnPerM)<1e-8,"slab diagram uses the optimizer's loaded row and single-member deflection");
    Check(!Find<Button>(window.TowerDiagrams).Any(),"slab selection clears lower-member loading-stage buttons");
   }
   if(args.Length>0){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(networkPlan.ActualWidth),(int)Math.Ceiling(networkPlan.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(networkPlan);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"network-plan.png")))encoder.Save(file);File.WriteAllText(Path.ChangeExtension(args[0],"network-report.html"),networkReport);}
   Find<Expander>(window).Single(e=>Equals(e.Header,"Crossing beams")).IsExpanded=true;Render();
   ((Button)window.FindName("AddCrossingBeamButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render();
   Check(vm.CrossingBeams.Count==1 && !vm.IsCalculationValid && window.TowerDiagrams==null,"adding crossing beam invalidates stale plans and diagrams");
   var crossing=vm.CrossingBeams[0];Check(crossing.SupportMode==CrossBeamSupportMode.Automatic,"new crossing defaults to Automatic");crossing.SupportMode=CrossBeamSupportMode.LegacySlabRows;crossing.PositionMm=3175;crossing.WidthMm=400;crossing.DepthMm=350;Render();
   var crossingWidth=VisualInputs((DependencyObject)window.Content).Single(t=>t.DataContext==crossing && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="WidthMm");
   crossingWidth.Text="oops";crossingWidth.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(!vm.CanOptimizeTowers,"invalid crossing width blocks optimization");
   crossingWidth.Text="400";crossingWidth.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Optimize();Render();
   Check(vm.IsCalculationValid && networkPlan.DrawnCrossingBeamCount==1 && window.BuildReport().Contains("extra bay load"),"crossing beam shown in plan and load schedule");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var tCross=vm.NetworkResult.Beams[0].Transfer;int crossRow=Array.IndexOf(tCross.RightUpperScales,tCross.RightUpperScales.Max());
   networkPlan.SectionPositionMm=tCross.RowsMm[crossRow];networkPlan.InvalidateVisual();Render();
   int crossEncoded=vm.NetworkResult.Beams[0].StockPlan.Pieces.Length+tCross.RowsMm.Length+crossRow*2+1;
   var crossingHit=networkPlan.MemberTargets.Single(h=>h.Item2==0 && h.Item3==crossEncoded);
   Check(networkPlan.SelectAt(new Point(crossingHit.Item1.X+crossingHit.Item1.Width/2,crossingHit.Item1.Y+crossingHit.Item1.Height/2)),"select governing crossing-loaded slab H20 in main section");Pump();
   Check(Math.Abs(window.TowerDiagrams.LoadedAnalysis.TotalLoadKn-tCross.UpperMembers.Single(m=>m.Name=="Upper right").Analysis.TotalLoadKn)<1e-8 && window.TowerDiagrams.SelectedTransferMember.H20Count==1,"crossing diagram uses actual governing row load and single capacity");
   var crossedRecord=window.CaptureCalculation();window.RestoreCalculation(crossedRecord);Pump();Check(vm.CrossingBeams.Count==1 && vm.CrossingBeams[0].PositionMm==3175 && vm.CrossingBeams[0].DepthMm==350,"crossing input save/load round trip");Optimize();Render();
   if(args.Length>0){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(networkPlan.ActualWidth),(int)Math.Ceiling(networkPlan.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(networkPlan);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"crossing-plan.png")))encoder.Save(file);}
   Render();var removeCross=VisualButtons((DependencyObject)window.Content).Single(b=>b.Tag==vm.CrossingBeams[0]);
   removeCross.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   Check(vm.CrossingBeams.Count==0 && !vm.IsCalculationValid,"remove crossing beam clears old result");
   window.RestoreCalculation(networkSaved);Pump();Optimize();Render();
   vm.NetworkBeams[1].DepthMm=1000;Check(!vm.IsCalculationValid && window.TowerDiagrams==null,"editing any beam invalidates network diagrams");
   window.RestoreCalculation(networkSaved);Pump();Check(vm.IsNetworkMode && vm.NetworkBeams.Count==3 && vm.NetworkBeams[1].DepthMm==1200 && vm.NetworkBeams[2].WidthMm==900,"network save/load round trip retains per-beam width/depth and all bays");
   ((Button)window.FindName("RemoveBeamBayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(vm.NetworkBeams.Count==2 && vm.NetworkBays.Count==1,"remove last bay button");
   vm.NetworkBeams[0].WidthMm=800;vm.NetworkBeams[0].DepthMm=1750;vm.NetworkBeams[1].WidthMm=650;vm.NetworkBeams[1].DepthMm=1150;vm.NetworkBays[0].WidthMm=2575;vm.NetworkBays[0].ThicknessMm=300;
   vm.AllowCustomH20Lengths=true;vm.BeamLengthMm=10600;vm.TowerLegCapacityKn=1;vm.Exclude600Standalone=true;Optimize();Render();
   var failureExpander=(Expander)window.FindName("FailureDetailsExpander");var failureText=(TextBox)window.FindName("FailureDetailsText");
   Check(!vm.IsCalculationValid && vm.HasTowerFailure && failureExpander.Visibility==Visibility.Visible && networkPlan.DrawnTowerLegCount==0,"failed network shows diagnostic control and clears stale plan");
   failureExpander.IsExpanded=true;Render();
   Check(failureText.IsReadOnly && failureText.Text.Contains("leg"),"copyable failure detail shows leg capacity failure with automatic spacing");
   if(args.Length>0){SavePreview(Path.ChangeExtension(args[0],"failure.png"),1040,860);File.WriteAllText(Path.ChangeExtension(args[0],"failure.txt"),failureText.Text);}
   vm.TowerLegCapacityKn=70;vm.NetworkBeams[0].DepthMm=900;vm.NetworkBeams[1].DepthMm=900;Optimize();Render();
   Check(vm.IsCalculationValid && !vm.HasTowerFailure && failureExpander.Visibility==Visibility.Collapsed && networkPlan.DrawnTowerLegCount==vm.NetworkResult.LegCount,"custom slab length produces a valid tower plan at the calculated maximum");
   Check(vm.NetworkResult.Bays[0].StockLengthMm%25==0 && vm.NetworkResult.Bays[0].IsCustomLength && window.BuildReport().Contains(vm.NetworkResult.Bays[0].StockLengthMm.ToString("0.###")+" mm custom"),"custom length is labelled in calculation report");
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var customTransfer=vm.NetworkResult.Beams[0].Transfer;int customRow=Array.IndexOf(customTransfer.RightUpperScales,customTransfer.RightUpperScales.Max());
   networkPlan.SectionPositionMm=customTransfer.RowsMm[customRow];networkPlan.InvalidateVisual();Render();
   int customEncoded=vm.NetworkResult.Beams[0].StockPlan.Pieces.Length+customTransfer.RowsMm.Length+customRow*2+1;
   var customHit=networkPlan.MemberTargets.Single(h=>h.Item2==0 && h.Item3==customEncoded);
   Check(networkPlan.SelectAt(new Point(customHit.Item1.X+customHit.Item1.Width/2,customHit.Item1.Y+customHit.Item1.Height/2)),"custom slab H20 can be selected directly for diagrams");Pump();
   Check(window.TowerDiagrams.SelectedTransferMember.LengthMm==vm.NetworkResult.Bays[0].StockLengthMm && window.TowerDiagrams.SelectedTransferMember.H20Count==1,"custom slab diagrams use actual length and single H20 capacity");
   if(args.Length>0){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(networkPlan.ActualWidth),(int)Math.Ceiling(networkPlan.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(networkPlan);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(args[0],"custom-plan.png")))encoder.Save(file);File.WriteAllText(Path.ChangeExtension(args[0],"custom.txt"),vm.NetworkResult.Summary+"\n\n"+vm.NetworkResult.Schedule);}
   var customWorkspaceRecord=window.CaptureCalculation();
   window.RestoreCalculation(networkSaved);Pump();Check(!vm.HasTowerFailure && failureExpander.Visibility==Visibility.Collapsed,"restoring inputs clears stale failure details");Optimize();Render();Check(vm.IsCalculationValid && !vm.HasTowerFailure,"successful retry hides failure report");
   checks+=WS10UiTests.Run(args.Length>0 ? args[0] : null);
   checks+=D3WorkspaceUiTests.Run(args.Length>0 ? args[0] : null);
   checks+=PresentationUiTests.Run(args.Length>0 ? args[0] : null);
   WorkspaceTests.Run(window,vm,customWorkspaceRecord,args.Length>0 ? args[0] : null);
   checks+=CaseDiagramUiTests.Run();EquivalentLoadingUiTests.Run();
   SearchBudgetUiTests.Run();checks+=CrossSupportUiTests.Run(args.Length>0 ? args[0] : null);
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



