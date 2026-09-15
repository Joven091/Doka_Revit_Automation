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
  var v=new FlexCalculatorViewModel();Check(v.IsCalculationValid,"default valid");Equal(950,v.RecommendedPropSpacing,"advisory in mm");Equal(2282,v.PropExtensionMm,"automatic extension");
  v.SelectedH20="2.90m";Invalid(v,"member does not fit");v.SelectedH20="3.30m";Check(v.IsCalculationValid,"longer member recovers");
  foreach(string name in new[]{"SlabThickness","FloorHeight","SecondaryBeamSpacing","PropSpacing"})foreach(double bad in new[]{0,-1,double.NaN,double.PositiveInfinity}){v=new FlexCalculatorViewModel();typeof(FlexCalculatorViewModel).GetProperty(name).SetValue(v,bad);Invalid(v,"invalid "+name);}
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
  v=new FlexCalculatorViewModel{SlabThickness=100,FloorHeight=4518,PropModel="DokaFit Prop",PropType="400",SelectedH20="4.90m",PrimaryBeamCount="5+",PropCount="5+",PrimaryBeamCantilever=250,SecondaryBeamCantilever=0,PropSpacing=1000};
  Check(v.IsCalculationValid,"5+ overload regression valid revised layout");Check(v.SuggestedPrimaryBeamSpacing<3100,"5+ formerly overloaded spacing reduced");
  double secondaryReaction=ContinuousBeamResponse.Analyze(5,v.SuggestedPrimaryBeamSpacing/1000,0,4.4,449.8756).Reaction;
  Check(ContinuousBeamResponse.Analyze(5,1,.25,secondaryReaction,449.8756).Reaction<=16,"independent five-support load within prop capacity");
  Check(v.PrimaryDiagramViewModel.DiagramRows[0].ChartModel.Title.Contains("5+ envelope"),"representative diagram labelled");
  int accepted=0;
  foreach(int h in new[]{100,150,200,300,400,600})foreach(int lp in new[]{500,750,1000,1250,1500,2000})foreach(string n in new[]{"2","3","4","5+"})foreach(string limit in new[]{"L/500","L/350","L/270"}){
   v=new FlexCalculatorViewModel{SlabThickness=h,FloorHeight=h+2418,PropSpacing=lp,PropCount=n,SelectedH20="4.90m",DeflectionLimit=limit};
   if(!v.IsCalculationValid){Check(v.SuggestedPrimaryBeamSpacing==0,"invalid clears spacing");continue;}accepted++;Check(v.PrimaryDeflectionMm<=lp/double.Parse(limit.Substring(2))+1e-7,"sweep primary deflection");Check(v.SuggestedPrimaryBeamSpacing%25==0,"output grid");
   var row=v.PrimaryDiagramViewModel.DiagramRows[3];var line=(OxyPlot.Series.LineSeries)row.ChartModel.Series[0];double a=v.PrimaryBeamCantilever/1000,last=a+(n=="5+"?4:int.Parse(n)-1)*lp/1000.0;double actual=line.Points.Where(p=>p.X>=a&&p.X<=last).Max(p=>Math.Abs(p.Y));Check(actual<=v.PrimaryDeflectionMm+1e-5,"diagram independently integrated span");
  }
  Console.WriteLine($"Sweep: 432 input combinations; {accepted} valid layouts.");
  RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;window=new FlexCalculatorWindow();var visualRoot=(FrameworkElement)window.Content;visualRoot.Measure(new Size(560,1160));visualRoot.Arrange(new Rect(0,0,560,1160));visualRoot.UpdateLayout();Pump();v=(FlexCalculatorViewModel)window.DataContext;var import=(Button)window.FindName("ImportButton");Check(import.IsEnabled,"initial import");
  Enter("SlabThickness","abc");Invalid(v,"text conversion");Check(!import.IsEnabled&&!window.TryGetValidCalculation(out var ignored),"import guard");Check(((Border)window.FindName("ResultsBorder")).Visibility==Visibility.Collapsed,"results hidden");
  Enter("SlabThickness","300");Check(v.IsCalculationValid&&import.IsEnabled,"text repair");v.UseCustomLoads=true;Pump();Enter("CustomLiveLoad","bad");Invalid(v,"custom text");v.UseCustomLoads=false;Pump();Check(v.IsCalculationValid,"inactive conversion ignored");v.UseCustomLoads=true;Pump();Invalid(v,"reactivated conversion");Enter("CustomLiveLoad","2.25");v.UseCustomLoads=false;
  Enter("FloorHeight","10000");Invalid(v,"UI extension");Enter("FloorHeight","3000");Check(v.IsCalculationValid,"UI extension repair");
  var choice=Find<ComboBox>(window).Single(c=>c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path=="SelectedH20");choice.SelectedItem="2.90m";Pump();Invalid(v,"UI H20 fit");choice.SelectedItem="3.30m";Pump();Check(v.IsCalculationValid,"UI H20 recovery");
  v.SelectedH20="4.90m";v.PropSpacing=750;var propCount=Find<ComboBox>(window).Single(c=>c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path=="PropCount");propCount.SelectedItem="5+";Pump();Check(v.PropCount=="5+"&&v.IsCalculationValid,"UI 5+ selection");Check(v.MemberFitHint.Contains("at most 6 props"),"5+ member fit hint");
  var primaryCount=Find<ComboBox>(window).Single(c=>c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path=="PrimaryBeamCount");primaryCount.SelectedItem="5+";Pump();Check(v.PrimaryBeamCount=="5+"&&v.IsCalculationValid,"UI secondary 5+ selection");
  v.PropModel="DokaFit Prop";Pump();Check(!v.PropOrientationOptions.Contains("at fixed"),"orientation choices");v.PropModel="Prop Type Eurex eco 20";Pump();
  if(args.Length>0){var root=(FrameworkElement)window.Content;root.Measure(new Size(560,1160));root.Arrange(new Rect(0,0,560,1160));root.UpdateLayout();var bitmap=new RenderTargetBitmap(560,1160,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(args[0]))png.Save(file);}
 }
}
