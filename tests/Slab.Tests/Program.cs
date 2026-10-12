using System;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;
using MyRevitAddin.Models;
class Program
{
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Equal(double expected,double actual,string name)=>Check(!double.IsNaN(actual)&&Math.Abs(expected-actual)<1e-7,name+": "+actual);
 static void Invalid(FlexCalculatorViewModel v,string name){Check(!v.IsCalculationValid,name);Equal(0,v.SuggestedPrimaryBeamSpacing,name);Check(v.PrimaryDiagramViewModel==null&&v.SecondaryDiagramViewModel==null,name+" diagrams");Check(!v.ViewPrimaryBeamDiagramCommand.CanExecute(null),name+" command");}
 static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject{foreach(var item in LogicalTreeHelper.GetChildren(root))if(item is DependencyObject d){if(d is T t)yield return t;foreach(var x in Find<T>(d))yield return x;}}
 static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
 static FlexCalculatorWindow window;
 static void Enter(string path,string text){var b=Find<TextBox>(window).Single(t=>t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path==path);b.Text=text;b.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();}
 [STAThread] static int Main(string[] args){try{Run(args);Console.WriteLine($"Passed {checks} slab calculation/catalog/WPF checks.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{window?.Close();}}
 static void Run(string[] args)
 {
  var fileWindow=new FlexCalculatorWindow();
  Check(!Find<ComboBox>(fileWindow).Any(c=>c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path=="SelectedH20"),"member-length selector removed");
  var fileVm=(FlexCalculatorViewModel)fileWindow.DataContext;var record=fileWindow.CaptureCalculation();double originalSpacing=fileVm.SuggestedPrimaryBeamSpacing;
  Check(!record.Inputs.Any(f=>f.Name=="SelectedH20"),"new records omit member length");
  record.Inputs.Add(CalculationFiles.Field("SelectedH20","2.90m")); // Earlier saved files remain loadable; obsolete length is ignored.
  string recordPath=Path.GetTempFileName();
  try {
   CalculationFiles.Write(recordPath,record);fileVm.SlabThickness=400;
   var culture=CultureInfo.CurrentCulture;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
   fileWindow.RestoreCalculation(CalculationFiles.Read<CalculationRecord>(recordPath));CultureInfo.CurrentCulture=culture;
   Equal(300,fileVm.SlabThickness,"slab file roundtrip thickness");Equal(originalSpacing,fileVm.SuggestedPrimaryBeamSpacing,"slab file roundtrip result");
   Check(fileWindow.BuildReport().Contains("Secondary H20 checks") && fileWindow.BuildReport().Contains("minimum reaction"),"slab report includes structural checks");
   if(args.Length>0)File.WriteAllText(Path.ChangeExtension(args[0],"report.html"),fileWindow.BuildReport());
   record.Version=999;bool rejected=false;try{fileWindow.RestoreCalculation(record);}catch(ArgumentException){rejected=true;}Check(rejected && fileVm.SlabThickness==300,"unsupported record preserves current state");
   File.WriteAllText(recordPath,"<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///not-read'>]><CalculationRecord>&e;</CalculationRecord>");
   rejected=false;try{CalculationFiles.Read<CalculationRecord>(recordPath);}catch(InvalidOperationException){rejected=true;}catch(System.Xml.XmlException){rejected=true;}Check(rejected,"external XML entities prohibited");
   string report=CalculationFiles.Report("Slab",new[]{CalculationFiles.Field("Floor","<script>alert(1)</script>")},new[]{CalculationFiles.Field("Spacing (mm)",originalSpacing)},"Adopted assumptions");
   Check(!report.Contains("<script>") && report.Contains("&lt;script&gt;") && report.Contains("Spacing (mm)"),"report encodes names and contains results");
  } finally { File.Delete(recordPath);fileWindow.Close(); }
  // Reference cells were extracted from the cited source PDFs, including merged cells.
  foreach(var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory,"prop-capacity-reference.csv")).Skip(1)){
   var c=line.Split(',');double ext=double.Parse(c[1],CultureInfo.InvariantCulture);double? actual=PropDataHelper.GetCapacity(ext,c[2]);
   if(c[3]=="")Check(!actual.HasValue,"unsupported reference cell");else{double expected=double.Parse(c[3],CultureInfo.InvariantCulture);Equal(expected,actual.Value,"reference capacity");Equal(expected,PropDataHelper.CapacityAtExtensionMm(ext*1000,c[2],out var row),"exact row");Equal(ext*1000,row,"exact lookup");}
  }
  foreach(var table in new[]{PropDataHelper.PropCapacities,PropDataHelper.DokaFitPropCapacities})foreach(var key in table.Values.SelectMany(r=>r.Keys).Distinct()){
   var rows=table.Where(r=>r.Value.TryGetValue(key,out var c)&&c.HasValue).OrderBy(r=>r.Key).ToList();
   foreach(double bad in new[]{rows.First().Key*1000-1,rows.Last().Key*1000+1,double.NaN,double.PositiveInfinity}){bool rejected=false;try{PropDataHelper.CapacityAtExtensionMm(bad,key,out var r);}catch(ArgumentException){rejected=true;}Check(rejected,"extension outside bounds");}
   foreach(var row in rows)foreach(int h in new[]{100,150,200,250,300,350,400,450,500,600,700,800}){double mm=(h+418+row.Key*1000)-h-418;PropDataHelper.CapacityAtExtensionMm(mm,key,out var lookup);Equal(row.Key*1000,lookup,"arithmetic boundary");}
  }
  Equal(16.8,PropDataHelper.GetCapacity(4,"DokaFit Prop 400 at top").Value,"published DokaFit");Check(!PropDataHelper.GetCapacity(2.1,"DokaFit Prop 350 at top").HasValue,"350 minimum");Check(!PropDataHelper.GetCapacity(3,"DokaFit Prop 300 at fixed").HasValue,"unverified fixed removed");
  Equal(22.9,SlabLoadHelper.CalculateTotalLoad(800),"heaping cap");Equal(9.15,SlabLoadHelper.CalculateTotalLoad(290),"load precision");Equal(5.003125,SlabLoadHelper.CalculateTotalLoad(124.125),"decimal slab thickness");
  Check(new FlexCalculatorViewModel{PropCount="5+",PropSpacing=1000,PrimaryBeamCantilever=500}.IsCalculationValid,"continuous layout is not restricted to stock H20 length");
  var v=new FlexCalculatorViewModel();Check(v.IsCalculationValid,"default valid");Equal(2282,v.PropExtensionMm,"automatic extension");
  foreach(string name in new[]{"SlabThickness","FloorHeight","PropSpacing"})foreach(double bad in new[]{0,-1,double.NaN,double.PositiveInfinity}){v=new FlexCalculatorViewModel();typeof(FlexCalculatorViewModel).GetProperty(name).SetValue(v,bad);Invalid(v,"invalid "+name);}
  foreach(string name in new[]{"SecondaryBeamCantilever","PrimaryBeamCantilever","CustomLiveLoad","CustomFormworkLoad"})foreach(double bad in new[]{-1,double.NaN,double.PositiveInfinity}){v=new FlexCalculatorViewModel{UseCustomLoads=true};typeof(FlexCalculatorViewModel).GetProperty(name).SetValue(v,bad);Invalid(v,"invalid "+name);}
  v=new FlexCalculatorViewModel{CustomLiveLoad=double.NaN};Check(v.IsCalculationValid,"inactive custom ignored");v.UseCustomLoads=true;Invalid(v,"custom activated");
  v=new FlexCalculatorViewModel{FloorHeight=10000};Invalid(v,"stale diagrams cleared");v.FloorHeight=3000;Check(v.IsCalculationValid&&v.PrimaryDiagramViewModel!=null,"diagram recovery");
  v=new FlexCalculatorViewModel{SlabThickness=100,FloorHeight=4118,PropType="400"};Equal(3600,v.PropLookupExtensionMm,"no spurious next row");
  v=new FlexCalculatorViewModel{PropSpacing=double.Epsilon,PrimaryBeamCantilever=0};Invalid(v,"underflow geometry");
  v=new FlexCalculatorViewModel{FloorHeight=2500};Invalid(v,"actual extension below table");
  // Closed-form independent calibration; zero end moments: 5wL^4/(384EI).
  Equal(5*6.27948*Math.Pow(2.5,4)/(384*449.8756),ContinuousSpanDeflection.Maximum(2,2.5,0,6.27948,449.8756),"simple span deflection");
  v=new FlexCalculatorViewModel{SlabThickness=150,FloorHeight=2568,PropCount="2",PropSpacing=2500,PrimaryBeamCantilever=0,DeflectionLimit="L/500"};Invalid(v,"old over-deflected layout rejected");v.PropSpacing=2000;Check(v.IsCalculationValid,"shorter prop span recovers");Check(v.PrimaryDeflectionMm<=4,"primary deflection enforced");
  Check(v.PrimaryBeamCountOptions.SequenceEqual(new[]{"2","3","4","5+"}),"secondary supports options");Check(v.PropCountOptions.SequenceEqual(v.PrimaryBeamCountOptions),"primary supports options");
  FivePlusTests.Run(Check,Equal);
  UpliftTests.Run(Check,Equal);
  CantileverTests.Run(Check,Equal);
  SchematicTests.Run(Check);
  v=new FlexCalculatorViewModel{SlabThickness=100,FloorHeight=4518,PropModel="DokaFit Prop",PropType="400",PrimaryBeamCount="5+",PropCount="5+",PrimaryBeamCantilever=250,SecondaryBeamCantilever=0,PropSpacing=1000};
  Check(v.IsCalculationValid,"5+ overload regression valid revised layout");Check(v.SuggestedPrimaryBeamSpacing<3100,"5+ formerly overloaded spacing reduced");
  double secondaryReaction=ContinuousBeamResponse.Analyze(5,v.SuggestedPrimaryBeamSpacing/1000,0,4.4,449.8756).Reaction;
  Check(ContinuousBeamResponse.Analyze(5,1,.25,secondaryReaction,449.8756).Reaction<=16,"independent five-support load within prop capacity");
  Check(v.PrimaryDiagramViewModel.DiagramRows.Count == 8,"5+ displays both internal cases");
  var cases = MyRevitAddin.ViewModels.DiagramWindowViewModel.ForSelection(5,1,0,5,449.8756,"Secondary beam");
  Check(cases.DiagramRows[0].CaseText.Contains("Governing reaction"),"five governs reaction");
  Check(!cases.DiagramRows[3].CaseText.Contains("Governing magnitude"),"five does not govern deflection");
  Check(cases.DiagramRows[7].CaseText.Contains("6 supports") && cases.DiagramRows[7].CaseText.Contains("Governing magnitude"),"six governs deflection within 5+");
  Check(cases.DiagramRows[7].MaxValueText.Contains("Maximum downward: 0.07 mm"),"downward deflection sign and units");
  Check(cases.DiagramRows[0].GeometryText.Contains("1000 mm"),"dimensions have units");
  foreach(int j in new[]{1,2,3,5,6,7}) Check(cases.DiagramRows[j].ChartModel.Annotations.OfType<OxyPlot.Annotations.LineAnnotation>().Count(a=>a.Type==OxyPlot.Annotations.LineAnnotationType.Vertical)==(j<4?5:6),"support guides match case");
  int accepted=0;
  foreach(int h in new[]{100,150,200,300,400,600})foreach(int lp in new[]{500,750,1000,1250,1500,2000})foreach(string n in new[]{"2","3","4","5+"})foreach(string limit in new[]{"L/500","L/350","L/270"}){
   v=new FlexCalculatorViewModel{SlabThickness=h,FloorHeight=h+2418,PropSpacing=lp,PropCount=n,DeflectionLimit=limit};
   if(!v.IsCalculationValid){Check(v.SuggestedPrimaryBeamSpacing==0,"invalid clears spacing");continue;}accepted++;Check(v.PrimaryDeflectionMm<=lp/double.Parse(limit.Substring(2))+1e-7,"sweep primary deflection");Check(v.SuggestedPrimaryBeamSpacing%25==0,"output grid");
   var row=v.PrimaryDiagramViewModel.DiagramRows[3];var line=(OxyPlot.Series.LineSeries)row.ChartModel.Series[0];double a=v.PrimaryBeamCantilever/1000,last=a+(n=="5+"?4:int.Parse(n)-1)*lp/1000.0;double actual=line.Points.Where(p=>p.X>=a&&p.X<=last).Max(p=>Math.Abs(p.Y));Check(actual<=v.PrimaryDeflectionMm+1e-5,"diagram independently integrated span");
  }
  Console.WriteLine($"Sweep: 432 input combinations; {accepted} valid layouts.");
  RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;window=new FlexCalculatorWindow();var visualRoot=(FrameworkElement)window.Content;visualRoot.Measure(new Size(1040,860));visualRoot.Arrange(new Rect(0,0,1040,860));visualRoot.UpdateLayout();Pump();v=(FlexCalculatorViewModel)window.DataContext;var import=(Button)window.FindName("ImportButton");Check(import.IsEnabled,"initial import");
  var plywood=(ComboBox)window.FindName("PlywoodSelector");
  Check(plywood.Items.Count==3 && plywood.SelectedItem==PlywoodCatalog.Default,"slab plywood selector default");
  var secondary=(TextBox)window.FindName("CalculatedSecondarySpacingInput");
  Check(secondary.IsReadOnly && !secondary.IsEnabled && secondary.GetBindingExpression(TextBox.TextProperty).ParentBinding.Mode==System.Windows.Data.BindingMode.OneWay,"slab secondary spacing is locked");
  v.SlabThickness=150;plywood.SelectedItem=PlywoodCatalog.All[0];Pump();Check(v.SecondaryBeamSpacing==275 && v.IsCalculationValid,"slab selector recalculates sheet spacing");
  var savedPlywood=window.CaptureCalculation();plywood.SelectedItem=PlywoodCatalog.All[2];window.RestoreCalculation(savedPlywood);Pump();Check(v.SelectedPlywood.ThicknessMm==9,"slab plywood survives save/load");
  savedPlywood.Inputs.Single(f=>f.Name=="SecondaryBeamSpacing").Value="999";savedPlywood.Inputs.RemoveAll(f=>f.Name=="PlywoodProfileId");window.RestoreCalculation(savedPlywood);Pump();Check(v.SelectedPlywood==PlywoodCatalog.Default && v.SecondaryBeamSpacing==488,"legacy manual slab spacing cannot override calculation");
  savedPlywood.Inputs.Add(CalculationFiles.Field("PlywoodProfileId","unknown"));bool invalidPlywood=false;try{window.RestoreCalculation(savedPlywood);}catch(ArgumentException){invalidPlywood=true;}Check(invalidPlywood && v.SelectedPlywood==PlywoodCatalog.Default,"unknown slab plywood leaves retained inputs unchanged");
  v.SlabThickness=300;Pump();
  Enter("SlabThickness","abc");Invalid(v,"text conversion");Check(!import.IsEnabled&&!window.TryGetValidCalculation(out var ignored),"import guard");Check(((Border)window.FindName("ResultsBorder")).Visibility==Visibility.Collapsed,"results hidden");
  Enter("SlabThickness","300");Check(v.IsCalculationValid&&import.IsEnabled,"text repair");v.UseCustomLoads=true;Pump();Enter("CustomLiveLoad","bad");Invalid(v,"custom text");v.UseCustomLoads=false;Pump();Check(v.IsCalculationValid,"inactive conversion ignored");v.UseCustomLoads=true;Pump();Invalid(v,"reactivated conversion");Enter("CustomLiveLoad","2.25");v.UseCustomLoads=false;
  Enter("PrimaryBeamCantilever","1000");Invalid(v,"UI uplift");Check(!import.IsEnabled&&!window.TryGetValidCalculation(out var upliftIgnored),"uplift import blocked");Check(((Border)window.FindName("ResultsBorder")).Visibility==Visibility.Collapsed,"uplift results hidden");Enter("PrimaryBeamCantilever","500");Check(v.IsCalculationValid&&import.IsEnabled,"UI uplift recovery");
  Enter("FloorHeight","10000");Invalid(v,"UI extension");Enter("FloorHeight","3000");Check(v.IsCalculationValid,"UI extension repair");
  v.PropSpacing=750;var propCount=Find<ComboBox>(window).Single(c=>c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path=="PropCount");propCount.SelectedItem="5+";Pump();Check(v.PropCount=="5+"&&v.IsCalculationValid,"UI 5+ selection");
  var primaryCount=Find<ComboBox>(window).Single(c=>c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path=="PrimaryBeamCount");primaryCount.SelectedItem="5+";Pump();Check(v.PrimaryBeamCount=="5+"&&v.IsCalculationValid,"UI secondary 5+ selection");
  v.PropModel="DokaFit Prop";Pump();Check(!v.PropOrientationOptions.Contains("at fixed"),"orientation choices");v.PropModel="Prop Type Eurex eco 20";Pump();
  if(args.Length>1 && args[1]=="uplift"){Enter("PrimaryBeamCantilever","900");Invalid(v,"rendered uplift state");}
  if(args.Length>1 && args[1]=="tip"){v.PropCount="2";v.PropSpacing=1000;v.PrimaryBeamCount="3";Enter("PrimaryBeamCantilever","1000");Invalid(v,"rendered tip state");Check(!import.IsEnabled&&!window.TryGetValidCalculation(out var tipIgnored),"tip import blocked");}
  if(args.Length>0){
   SavePreview(args[0],1040,860);
   if(v.IsCalculationValid){
    int lines=v.PrimaryBeamCount=="5+"?5:int.Parse(v.PrimaryBeamCount), supports=v.PropCount=="5+"?5:int.Parse(v.PropCount);
    Check(((SlabSupportDiagram)window.FindName("SupportDiagram")).DrawnPropCount==lines*supports,"live support schematic");
    if(v.PrimaryBeamCount=="5+"||v.PropCount=="5+")Check(((TextBlock)window.FindName("DiagramCaption")).Text.Contains("five supports"),"five-plus schematic labelled");
   }
   ((Expander)window.FindName("LoadInputsExpander")).IsExpanded=true;v.UseCustomLoads=true;Pump();
   SavePreview(Path.ChangeExtension(args[0],"loads.png"),1040,860);
   SavePreview(Path.ChangeExtension(args[0],"compact.png"),900,620);
   Enter("SlabThickness","abc");Invalid(v,"redesign invalid field");Check(!import.IsEnabled,"redesign import blocked");
   SavePreview(Path.ChangeExtension(args[0],"invalid.png"),1040,860);
   Check(((SlabSupportDiagram)window.FindName("SupportDiagram")).DrawnPropCount==0,"live invalid schematic cleared");
  }
 }
 static void SavePreview(string path,int width,int height){
  Pump();var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
  var import=(Button)window.FindName("ImportButton");var corner=import.TranslatePoint(new Point(import.ActualWidth,import.ActualHeight),root);
  Check(corner.X<=width+.1&&corner.Y<=height+.1,"footer stays inside resized window");
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);
 }

}