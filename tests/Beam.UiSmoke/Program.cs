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
   if(args.Length>0){
    SavePreview(args[0],1040,860);
    ((Expander)window.FindName("LoadInputsExpander")).IsExpanded=true;vm.UseCustomLoads=true;Pump();
    SavePreview(Path.ChangeExtension(args[0],"loads.png"),1040,860);
    SavePreview(Path.ChangeExtension(args[0],"compact.png"),900,620);
    Enter("Depth","abc");Check(!vm.IsCalculationValid&&!import.IsEnabled,"redesign invalid field blocks import");
    SavePreview(Path.ChangeExtension(args[0],"invalid.png"),1040,860);
   }
   window.Close();Console.WriteLine($"Passed {checks} beam WPF checks.");return 0;
  }
  catch(Exception ex){Console.Error.WriteLine(ex);window?.Close();return 1;}
 }
 static void SavePreview(string path,int width,int height){
  var bitmap=Render(width,height);var root=(FrameworkElement)window.Content;var import=(Button)window.FindName("ImportButton");
  var corner=import.TranslatePoint(new Point(import.ActualWidth,import.ActualHeight),root);Check(corner.X<=width+.1&&corner.Y<=height+.1,"footer stays inside resized window");
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
 }

}

