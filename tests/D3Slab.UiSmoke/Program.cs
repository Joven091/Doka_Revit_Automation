using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;
static class Program
{
 static int checks;
 static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
 static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
 static T Get<T>(object o,string name)=>(T)o.GetType().GetProperty(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
 static object Call(object o,string name,params object[] values)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,values);
 static void Optimize(D3SlabCalculatorViewModel vm){var task=vm.OptimizeAsync();var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){Pump();Thread.Sleep(1);if(watch.Elapsed.TotalSeconds>60)throw new Exception("Search timeout");}task.GetAwaiter().GetResult();Pump();}
 static void Render(Window window,string path=null,int width=1160,int height=860)
 {var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();Pump();if(path!=null){var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle(window.Background,null,new Rect(0,0,width,height));image.Render(background);image.Render(root);SaveImage(image,path);}}
 static void SaveImage(RenderTargetBitmap image,string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))png.Save(file);}
 static void LauncherChecks()
 {
  using var approvedSession=new ApprovedLauncherSession(typeof(LauncherWindow).Assembly);
  var launcher=new LauncherWindow();launcher.Show();
  var dome=(Button)launcher.FindName("BtnDomeCalculator");
  Check(!dome.IsEnabled && System.Windows.Automation.AutomationProperties.GetName(dome).Contains("Locked"),"Dome Beta is visibly locked");
  dome.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
  Check(!Application.Current.Windows.OfType<EngineeringDashboard.Dome.MainWindow>().Any(w=>w.IsVisible)&&launcher.IsVisible,"Dome lock blocks opening even if its click event is raised");
  ((Button)launcher.FindName("BtnFlexCalculator")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
  var choice=Application.Current.Windows.OfType<SlabSystemWindow>().Single(w=>w.IsVisible);Check(choice.Owner==launcher,"slab choices owned by launcher");
  var towers=(Button)choice.FindName("PickTowersButton");
  Check(!towers.IsEnabled && System.Windows.Automation.AutomationProperties.GetName(towers).Contains("Coming soon"),"D3 slab is disabled and labelled Coming soon");
  towers.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
  Check(!choice.UseD3 && choice.IsVisible && !Application.Current.Windows.OfType<D3SlabCalculatorWindow>().Any(w=>w.IsVisible),"D3 slab click cannot select or launch the locked system");
  ((Button)choice.FindName("PickPropsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
  var flex=Application.Current.Windows.OfType<FlexCalculatorWindow>().Single(w=>w.IsVisible);Check(flex.Owner==launcher,"Dokaflex remains available modeless");
  Call(flex,"ChangeSystem_Click",null,new RoutedEventArgs());Pump();Check(flex.ChangeSystemRequested,"Dokaflex can return to system choices");
  var second=Application.Current.Windows.OfType<SlabSystemWindow>().Single(w=>w.IsVisible);
  Check(!((Button)second.FindName("PickTowersButton")).IsEnabled,"change-system picker keeps D3 slab locked");second.Close();Pump();
  Check(launcher.IsVisible,"closing choices returns to launcher");launcher.Close();
 }
 [STAThread]static int Main(string[] args)
 {
  D3SlabCalculatorWindow window=null;
  try
  {
   NativeHostResourceChecks.Run();
   RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
   new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
   if(args.Contains("--release-availability")){LauncherChecks();Console.WriteLine("PASS first-release calculator availability");return 0;}
   var vm=new D3SlabCalculatorViewModel();window=new D3SlabCalculatorWindow(vm);Render(window);
   var button=Get<Button>(window,"OptimizeButton");Check(button.IsEnabled,"initial inputs allow optimization");Check(!Get<Button>(window,"SaveButton").IsEnabled,"save waits for calculation");
   var inputs=(System.Collections.Generic.Dictionary<string,TextBox>)window.GetType().GetField("Inputs",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);
   var length=inputs[nameof(vm.LengthMm)];length.Text="bad";length.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(!button.IsEnabled,"conversion error disables search");
   length.Text="6000";length.GetBindingExpression(TextBox.TextProperty).UpdateSource();Pump();Check(button.IsEnabled,"valid input recovers search");
   Optimize(vm);Check(vm.HasResult,"optimization completes");var plan=Get<D3SlabPlan>(window,"Plan");Render(window);
   Check(plan.DrawnLegCount==vm.Result.LegCount,"plan draws each calculated leg");Check(plan.DrawnPrimaryCount==vm.Result.PrimaryH20Count,"double primary stock drawn");Check(plan.DrawnSecondaryCount==vm.Result.SecondaryH20Count,"secondary stock drawn");Check(plan.DrawnDimensionCount>vm.Result.StationsMm.Length,"plan includes dimensions in both directions");
   var scroll=Get<ScrollViewer>(window,"PlanScroll");Check(scroll.ScrollableWidth>0&&scroll.ScrollableHeight>0,"large plan scrolls both directions");
   scroll.ScrollToRightEnd();scroll.ScrollToBottom();Pump();Check(scroll.HorizontalOffset>0&&scroll.VerticalOffset>0,"far plan edges accessible");scroll.ScrollToHome();Pump();
   var record=(CalculationRecord)Call(window,"CaptureCalculation");Check(record.Kind=="D3Slab","dedicated file kind");Check(((string)Call(window,"BuildReport")).Contains("Covered Area")||((string)Call(window,"BuildReport")).Contains("CoveredArea"),"report includes covered area");
   if(args.Length>0){Render(window,args[0]);File.WriteAllText(Path.ChangeExtension(args[0],"html"),(string)Call(window,"BuildReport"));var bitmap=new RenderTargetBitmap((int)plan.ActualWidth,(int)plan.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(plan);SaveImage(bitmap,Path.ChangeExtension(args[0],"plan.png"));}
   vm.ThicknessMm=210;Pump();Render(window);Check(!vm.HasResult&&!Get<Button>(window,"SaveButton").IsEnabled&&plan.DrawnLegCount==0,"input clears old result");
   Call(window,"RestoreCalculation",record);Pump();Check(vm.ThicknessMm==200&&!vm.HasResult,"file restores inputs and requires fresh search");Optimize(vm);Check(vm.HasResult,"restored record calculates");
   var invalid=new CalculationRecord{Kind="Beam",Inputs=record.Inputs};bool rejected=false;try{Call(window,"RestoreCalculation",invalid);}catch(TargetInvocationException e){rejected=e.InnerException is ArgumentException;}Check(rejected&&vm.HasResult,"wrong file preserves current layout");
   var pending=vm.OptimizeAsync();vm.WidthMm=5100;while(!pending.IsCompleted){Pump();Thread.Sleep(1);}pending.GetAwaiter().GetResult();Check(vm.Result==null,"edit cancels stale asynchronous result");
   var picker=new SlabSystemWindow();Render(picker,args.Length>0?Path.ChangeExtension(args[0],"picker.png"):null,820,520);Check(picker.FindName("PickPropsButton") is Button && picker.FindName("PickTowersButton") is Button,"both illustrated slab system choices exist");picker.Close();
   window.Close();LauncherChecks();Console.WriteLine("Passed "+checks+" D3 slab WPF checks.");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);window?.Close();return 1;}
 }
}
