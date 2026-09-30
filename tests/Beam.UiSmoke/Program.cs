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
   Check(import.IsEnabled,"initial import");Check(diagram.DrawnPropCount==4,"two props per primary line");Check(diagram.DrawnSlabCount==0,"no slabs by default");
   Enter("Depth","abc");Check(!vm.IsCalculationValid,"invalid text invalidates VM");Check(!import.IsEnabled,"invalid import disabled");Check(results.Visibility==Visibility.Collapsed,"stale results hidden");Check(Input("Depth").Text=="abc","invalid text preserved");
   Check(!window.TryGetValidCalculation(out var ignored),"import guard rejects conversion error");
   Enter("Depth","500");Check(vm.IsCalculationValid&&import.IsEnabled,"valid text recovers");
   Enter("Width","NaN");Check(!vm.IsCalculationValid&&!import.IsEnabled,"NaN rejected");Enter("Width","700");
   vm.UseCustomLoads=true;Pump();Enter("CustomLiveLoad","bad");Check(!vm.IsCalculationValid,"custom conversion error");vm.UseCustomLoads=false;Pump();Check(vm.IsCalculationValid,"inactive custom ignored");vm.UseCustomLoads=true;Pump();Check(!vm.IsCalculationValid,"reactivated bad custom rejected");Enter("CustomLiveLoad","2.25");vm.UseCustomLoads=false;
   vm.HasLeftSlab=true;Pump();Enter("LeftSlabThickness","0");Check(!vm.IsCalculationValid,"zero checked slab");Enter("LeftSlabThickness","200");vm.HasRightSlab=true;vm.NumberOfProps="3";Render();
   Check(diagram.DrawnPropCount==6,"three props per line");Check(diagram.DrawnSlabCount==2,"both slabs shown");
   vm.HasLeftSlab=false;Render();Check(diagram.DrawnSlabCount==1,"unchecked slab removed");vm.HasRightSlab=false;
   vm.NumberOfProps="4+";var combo=Find<ComboBox>(window).Single(c=>c.SelectedItem is BeamDeflectionOption);Check(combo.Items.Count==3,"typed deflection options loaded");combo.SelectedIndex=2;Pump();Check(vm.DeflectionLimit.Millimetres==10,"10mm selection maps numerically");Check(vm.MaximumPropSpacing==3000,"10mm spacing");
   Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");vm.Width=701;Render();Check(diagram.DrawnPropCount==8,"decimal comma vector diagram");Check(vm.IsCalculationValid,"decimal comma calculation");
   var props=Find<ComboBox>(window).Single(c=>c.SelectedItem is BeamPropOption);Check(props.Items.Count==BeamPropOption.All.Count,"prop dropdown shares catalog");props.SelectedItem=BeamPropOption.All.First(p=>p.CapacityKey=="DokaFit Prop 300 at top");Pump();Check(vm.SelectedProp.CapacityKey=="DokaFit Prop 300 at top","prop selection binding");Enter("FloorHeight","9999");Check(!vm.IsCalculationValid&&!import.IsEnabled,"unsupported extension blocks import");Enter("FloorHeight","3000");Check(vm.IsCalculationValid,"floor height recovery");
   Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("en-US");
   vm.Width=200;vm.HasLeftSlab=true;vm.HasRightSlab=true;vm.NumberOfProps="3";var picture=Render();
   vm.SlabStripWidthMm=450;Pump(); var saved=window.CaptureCalculation();double savedSpacing=vm.MaximumPropSpacing,savedLoad=vm.MaximumPropLoad;
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
   ((Button)window.FindName("TowerDiagramsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
   var charts=window.TowerDiagrams;Check(charts!=null && charts.IsVisible,"diagram button opens window");
   charts.RunSelector.SelectedIndex=1;charts.LineSelector.SelectedIndex=1;Pump();
   var piece=vm.TowerResult.StockPlan.Pieces[1];
   Check(Math.Abs(charts.Analysis.Reactions.Sum()-vm.TowerResult.RightLineLoad*piece.StockLengthMm/2000)<1e-8,"selected diagram stock and line load");
   var chartRoot=(FrameworkElement)charts.Content;chartRoot.Measure(new Size(980,1400));chartRoot.Arrange(new Rect(0,0,980,1400));chartRoot.UpdateLayout();
   Check(charts.Plan.DrawnEndDimensionCount==vm.TowerResult.StockPlan.Pieces.Length*2,"diagram selection plan shows offsets");
   foreach(var hit in charts.Plan.MemberTargets.ToArray()) {
    Check(charts.Plan.SelectAt(new Point(hit.Item1.X+hit.Item1.Width/2,hit.Item1.Y+hit.Item1.Height/2)),"plan member hit");
    Check(charts.RunSelector.SelectedIndex==hit.Item2 && charts.LineSelector.SelectedIndex==hit.Item3 && charts.MemberSelector.SelectedIndex==hit.Item4,"plan selection drives matching member diagram");
    var selected=vm.TowerResult.StockPlan.Pieces[hit.Item2];double q=hit.Item3==0?vm.TowerResult.LeftLineLoad:vm.TowerResult.RightLineLoad;
    Check(Math.Abs(charts.Analysis.Reactions.Sum()-q*selected.StockLengthMm/2000)<1e-8,"individual H20 load for selected member");
   }
   Check(!charts.Plan.SelectAt(new Point(-5,-5)),"empty plan area does not select a member");
   var mainHit=diagram.MemberTargets.First();diagram.SelectAt(new Point(mainHit.Item1.X+mainHit.Item1.Width/2,mainHit.Item1.Y+mainHit.Item1.Height/2));Pump();
   Check(window.TowerDiagrams.RunSelector.SelectedIndex==mainHit.Item2,"main plan also selects diagram");
   if(args.Length>0){var root=(FrameworkElement)((ScrollViewer)charts.Content).Content;root.Measure(new Size(940,double.PositiveInfinity));int chartHeight=(int)Math.Ceiling(root.DesiredSize.Height);root.Arrange(new Rect(0,0,940,chartHeight));root.UpdateLayout();var bitmap=new RenderTargetBitmap(940,chartHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.ChangeExtension(args[0],"diagrams.png")))encoder.Save(output);}
   Check(window.BuildReport().Contains("Support Schedule") && window.BuildReport().Contains("bounded") && window.BuildReport().Contains("Primary Stock Schedule"),"tower report schedule and scope");
   vm.BeamLengthMm=6500;Pump();Check(!vm.IsCalculationValid && vm.TowerResult==null,"editing clears tower");
   Check(window.TowerDiagrams==null && !charts.IsVisible,"editing closes stale diagrams");
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
   var old=towerSaved;old.Inputs.RemoveAll(f=>new[]{"UseTowers","BeamLengthMm","TowerLegCapacityKn","StockLengthsText","StockEndSetbackMm","MaximumConcreteEndOverhangMm"}.Contains(f.Name));
   window.RestoreCalculation(old);Pump();Check(!vm.UseTowers && vm.IsCalculationValid,"legacy prop record loads");
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
