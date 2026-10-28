using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    private static void Wait(Func<bool> done){var watch=System.Diagnostics.Stopwatch.StartNew();while(!done()){Pump();Thread.Sleep(5);if(watch.Elapsed.TotalSeconds>20)throw new TimeoutException("UI operation did not finish");}Pump();}
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {
        var found=new List<T>();var seen=new HashSet<DependencyObject>();Action<DependencyObject> visit=null;
        visit=node=>{if(node==null||!seen.Add(node))return;if(node is T item)found.Add(item);foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())visit(child);if(node is Visual||node is System.Windows.Media.Media3D.Visual3D)for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)visit(VisualTreeHelper.GetChild(node,i));};visit(parent);return found;
    }
    private static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    private static void Menu(MenuItem item){item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();}
    private static void Toolbar(Window window)
    {
        window.Show();Pump();var menus=Find<MenuItem>(window).ToArray();Check(menus.Any(m=>Equals(m.Header,"_File"))&&menus.Any(m=>Equals(m.Header,"_Tools")),window.Title+" has shared File and Tools");
        Check(!Find<Button>(window).Any(b=>b.Content is string s&&s.Contains("Export report"))&&!menus.Any(m=>m.Header is string s&&s.Contains("Export report")),window.Title+" has no report-export action");
    }
    private static RevitWallSnapshot Source(decimal first=6000)=>new RevitWallSnapshot{DocumentKey="controlled-project",DocumentTitle="Controlled Revit source",CapturedUtc=DateTime.UtcNow.ToString("O"),SelectedIds=new[]{"101","102"},Walls=new[]{new RevitWallRecord{UniqueId="unique-101",ElementId="101",CentreLengthMm=5900,ExteriorLengthMm=first,InteriorLengthMm=5800,GeometryDetails="Controlled planar face geometry"},new RevitWallRecord{UniqueId="unique-102",ElementId="102",CentreLengthMm=1000,ExteriorLengthMm=1000,InteriorLengthMm=1000}}};
    [STAThread] private static int Main(string[] args)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        bool success=false;string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        try
        {
            var slab=new FlexCalculatorWindow();Toolbar(slab);slab.Close();
            var back=new BackproppingWindow();Toolbar(back);var remove=(Button)back.FindName("RemoveLowest");Check(!(remove.Content is string)&&AutomationProperties.GetName(remove)=="Delete"&&!remove.IsEnabled,"backpropping delete icon retains the lowest-slab rule and disabled state");back.Close();
            var beam=new BeamCalculatorWindow{DataContext=new BeamCalculatorViewModel()};Toolbar(beam);Click((Button)beam.FindName("PickTowersButton"));Pump();
            foreach(var name in new[]{"Undo","Redo","Delete"}){var button=Find<Button>(beam).Single(b=>AutomationProperties.GetName(b)==name);Check(!(button.Content is string)&&button.ToolTip!=null,"D3 "+name+" uses a named icon with tooltip");}
            Check(!Find<MenuItem>(beam).Any(m=>m.Header is string s&&s.Contains("Export")),"D3 File menu has no report export");beam.Close();
            using(var session=new ApprovedLauncherSession(typeof(LauncherWindow).Assembly))
            {var launcher=new LauncherWindow();launcher.Show();Pump();var dome=(Button)launcher.FindName("BtnDomeCalculator");Check(!dome.IsEnabled&&AutomationProperties.GetName(dome).Contains("Locked"),"Dome Beta remains visible and locked");Click(dome);Check(!app.Windows.OfType<EngineeringDashboard.Dome.MainWindow>().Any(w=>w.IsVisible),"Dome cannot be opened through a raised click");launcher.Close();}
            var requests=new List<RevitWallRequest>();var pending=new TaskCompletionSource<RevitHighlightResult>();bool delay=true;
            var connection=new Top50RevitConnection{ReadSelection=()=>Task.FromResult(Source()),Refresh=request=>Task.FromResult(Source(6250)),Highlight=request=>{requests.Add(request);return delay?pending.Task:Task.FromResult(new RevitHighlightResult{Requested=request.UniqueIds.Length,Found=request.UniqueIds.Length,Message="Controlled walls selected"});}};
            var model=new Top50ViewModel();var planner=new Top50Window(model,connection);Toolbar(planner);Click(Find<Button>(planner).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50UseRevitSelection"));Wait(()=>model.Input!=null);
            Check(model.Input.Walls.Count==2&&model.Input.Walls.Sum(w=>w.LengthMm)==7000&&!app.Windows.OfType<Window>().Any(w=>w.Title.Contains("Create drawing")),"one click imports forming-face lengths into one planner without another page");
            var ids=model.Input.Walls.Select(w=>w.Id).ToArray();var proposal=new Proposal("controlled-offer",new Dictionary<int,int>{{6000,1},{1000,1}},new[]{new CastingGroup(new[]{new WallArrangement(ids[0],new PanelPattern(new[]{6000},Array.Empty<long>()))}),new CastingGroup(new[]{new WallArrangement(ids[1],new PanelPattern(new[]{1000},Array.Empty<long>()))})});
            var project=NativeProjectStore.Capture(model.Input,new[]{proposal},proposal.Id);project.LongestCount=1;project.SearchPolicyVersion=Top50PlannerPolicy.SearchPolicyVersion;string path=Path.Combine(output,"controlled-source.top50");NativeProjectStore.Save(project,path);model.Open(path,Path.Combine(output,"cache"));Pump();
            Click(Find<Button>(planner).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50Page3"));Check(requests.Count==1&&!requests[0].Locate,"selecting the casting-group page requests highlight without zoom");
            var selector=Find<ComboBox>(planner).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50CastingGroupSelector");string choice=(string)selector.SelectedItem.GetType().GetProperty("Choice").GetValue(selector.SelectedItem);
            Check(choice.Contains("C-01")&&choice.Contains("85.71% utilization"),"casting selector shows actual installed-panel utilization");
            selector.SelectedIndex=1;Pump();delay=false;pending.SetResult(new RevitHighlightResult{Requested=1,Found=1,Message="First group selected"});Wait(()=>requests.Count==2);
            // Use LINQ explicitly: the installed host deliberately has no binding redirects for span extensions.
            Check(Enumerable.SequenceEqual(requests.Last().UniqueIds,new[]{"unique-102"})&&!requests.Last().Locate,"rapid group changes eventually highlight the latest group");
            Menu(Find<MenuItem>(planner).Single(m=>AutomationProperties.GetAutomationId(m)=="Top50LocateRevit"));Wait(()=>requests.Count==3);Check(requests.Last().Locate,"Locate explicitly opts into changing the Revit view");
            SaveImage(planner,Path.Combine(output,"casting-utilization.png"));
            Menu(Find<MenuItem>(planner).Single(m=>AutomationProperties.GetAutomationId(m)=="Top50RefreshRevit"));Wait(()=>model.IsStale);Check(model.Input.Walls.First().LengthMm==6250&&model.Input.Walls.First().Id==ids[0],"refresh updates dimensions, preserves identity and invalidates the old offer");
            Menu(Find<MenuItem>(planner).Single(m=>AutomationProperties.GetAutomationId(m)=="Top50Diagnose"));Pump();var diagnosis=app.Windows.OfType<Top50DiagnosticWindow>().Single();var report=Find<TextBox>(diagnosis).Single(t=>AutomationProperties.GetAutomationId(t)=="Top50DiagnosticReport").Text;
            Check(report.Contains("controlled-project")&&report.Contains("unique-101")&&report.Contains("longer straight forming face")&&report.Contains("6250"),"Tools diagnosis contains Revit project identity, source walls and measured geometry");diagnosis.Close();
            model.Save(path);planner.Close();
            File.WriteAllText(Path.Combine(output,"checks.txt"),checks+" dashboard toolbar, locked-feature and Top50 Revit interaction checks passed. Actual Revit host acceptance remains unrun.");Console.WriteLine(checks+" dashboard release checks passed.");success=true;return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());return 1;}
        finally{if(!success)Environment.Exit(1);app.Shutdown();}
    }
    private static void SaveImage(Window window,string path)
    {window.UpdateLayout();var root=(FrameworkElement)window.Content;root.UpdateLayout();var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file);}
}
