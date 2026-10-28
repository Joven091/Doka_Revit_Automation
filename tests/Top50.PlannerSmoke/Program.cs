using System;
using System.Collections.Generic;
using System.Globalization;
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
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
    [STAThread]
    private static int Main(string[] args)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        var empty=new Top50ViewModel();var emptyReport=empty.BuildDiagnosticReport();
        Check(emptyReport.Contains("No input imported or added")&&emptyReport.EndsWith("=== END TOP50 PLANNER DIAGNOSTIC REPORT ==="+Environment.NewLine),"diagnosis available before input exists");
        Fails(()=>empty.Import(Path.Combine(output,"missing-source.dxf")),"Import missing DXF");
        Check(empty.BuildDiagnosticReport().Contains("FileNotFoundException")&&empty.BuildDiagnosticReport().Contains("missing-source.dxf"),"failed first import retains operation, filename and exception");
        Fails(()=>Wait(empty.OptimizeAsync()),"Optimize without input");
        Check(empty.BuildDiagnosticReport().Contains("Import measured walls first")&&empty.BuildDiagnosticReport().Contains("Outcome: Failed"),"pre-search validation failure remains diagnosable");

        // Controlled measurements only. These are not the original Top50 parity/reference drawing.
        var vm=new Top50ViewModel();for(int i=0;i<4;i++)vm.AddManual(5000,"Controlled planner diagnosis check");
        vm.Settings.CatalogMm=new[]{5000};vm.Settings.LongestCount=2;vm.Settings.Seconds=5;
        Wait(vm.OptimizeAsync());
        Check(vm.Proposals.Count>0&&vm.Proposals.All(p=>Top50Validation.Validate(vm.Input.Walls,p,vm.Limits).Count==0),"fresh native solver proposals independently validate");
        var selected=vm.Selected;var proposals=vm.Proposals;var original=vm.Input.Walls.Select(w=>w.LengthMicrometres).ToArray();bool dirty=vm.Dirty;
        var report=vm.BuildDiagnosticReport();
        foreach(var expected in new[]{typeof(Top50ViewModel).Assembly.GetName().Version.ToString(),"MVID:","CatalogMm: 5000","Attempted inventories:","Outcome: Accepted","Retained model status:","Stock (per face; size mm x quantity):","Casting Group C-01","MANUAL-001","total mm: 5000","Validation against CURRENT inputs/settings: passed"})Check(report.Contains(expected),"report contains "+expected);
        Check(ReferenceEquals(selected,vm.Selected)&&ReferenceEquals(proposals,vm.Proposals)&&vm.Dirty==dirty&&vm.Input.Walls.Select(w=>w.LengthMicrometres).SequenceEqual(original),"diagnosis leaves input, selected proposal, allocations and dirty state unchanged");
        File.WriteAllText(Path.Combine(output,"validated-planner-diagnosis.txt"),report);
        var culture=CultureInfo.CurrentCulture;CultureInfo.CurrentCulture=new CultureInfo("de-DE");
        try{Check(vm.BuildDiagnosticReport().Contains("Stock length mm: 10000"),"report uses invariant engineering units and numbers");}finally{CultureInfo.CurrentCulture=culture;}

        var cancellation=vm.OptimizeAsync();Check(vm.IsBusy,"search becomes busy before cancellation");
        Check(vm.BuildDiagnosticReport().Contains("Outcome: Running"),"diagnosis can capture an in-flight search");vm.Cancel();Wait(cancellation);
        Check(!vm.IsBusy&&ReferenceEquals(selected,vm.Selected)&&vm.BuildDiagnosticReport().Contains("Outcome: Cancelled"),"cancellation is distinguished from failure and retains previous validated proposal");
        vm.Settings.MaxFillers=-1;vm.Changed();Fails(()=>Wait(vm.OptimizeAsync()),"Invalid search settings");
        Check(vm.BuildDiagnosticReport().Contains("MaxFillers: -1")&&vm.BuildDiagnosticReport().Contains("Outcome: Failed")&&vm.BuildDiagnosticReport().Contains("stale: True"),"invalid settings reported alongside clearly stale retained proposals");
        vm.Settings.MaxFillers=1;vm.Settings.CatalogMm=new[]{6000};vm.Changed();Wait(vm.OptimizeAsync());
        Check(vm.BuildDiagnosticReport().Contains("Outcome: No complete proposal found")&&vm.BuildDiagnosticReport().Contains("Attempted inventories: 0"),"no-result search preserves actual returned counters and bounded-search outcome");
        Fails(()=>vm.Export(Path.Combine(output,"stale.xlsx")),"Stale export");
        Check(vm.BuildDiagnosticReport().Contains("Export workbook")&&!File.Exists(Path.Combine(output,"stale.xlsx")),"blocked export is recorded without producing a workbook");
        Fails(()=>vm.Open(Path.Combine(output,"missing-project.top50"),Path.Combine(output,"cache")),"Missing project");
        Fails(()=>vm.Save(output),"Unwritable project target");
        Check(vm.BuildDiagnosticReport().Contains("Open project")&&vm.BuildDiagnosticReport().Contains("Save project"),"project open/save failures retain their contexts");
        vm.Settings.CatalogMm=new[]{5000};vm.Settings.MaxFillers=12;vm.Changed();Wait(vm.OptimizeAsync());
        Check(!vm.IsBusy&&!vm.IsStale&&vm.BuildDiagnosticReport().Contains("MaxFillers: -1"),"successful retry retains earlier failed-attempt evidence");
        var project=Path.Combine(output,"planner.top50");vm.Save(project);var restored=new Top50ViewModel();restored.Open(project,Path.Combine(output,"cache"));
        Check(restored.BuildDiagnosticReport().Contains("No optimization executed in this session")&&restored.Selected!=null,"reopened proposals are identified separately from current-session searches");
        Fails(()=>restored.Export(Path.Combine(output,"planner.xlsx")),"Disabled workbook export");Check(!File.Exists(Path.Combine(output,"planner.xlsx")),"disabled export produces no workbook");

        var dxf=Path.Combine(output,"controlled-wall-dimensions.dxf");File.WriteAllText(dxf,SyntheticDxf());
        var imported=new Top50ViewModel();imported.Import(dxf);
        Check(imported.Input.Walls.Count==3&&imported.Input.Walls.Count(w=>w.Included)==2,"controlled DXF retains included and flagged rows");
        var importedReport=imported.BuildDiagnosticReport();
        foreach(var expected in new[]{"DXF-A1","DXF-A2","DXF-A3","Raw DXF codes:","stored measurement mm: 7200","Source SHA-256 at import:","Wall_Length"})Check(importedReport.Contains(expected),"DXF diagnosis contains "+expected);
        imported.Input.Walls.Last().Review(7200,true,"Explicit field review");imported.Changed();
        Check(imported.BuildDiagnosticReport().Contains("Review reason: Explicit field review"),"reviewed geometry and reason retained");
        File.Delete(dxf);Check(imported.BuildDiagnosticReport().Contains("DXF-A3"),"diagnosis survives unavailable original DXF without rereading it");

        var launcher=new LauncherWindow();var launchRoot=(FrameworkElement)launcher.Content;
        var panel=(Panel)((FrameworkElement)launcher.FindName("BtnTop50")).Parent;
        int planners=panel.Children.IndexOf((UIElement)launcher.FindName("PlannersSection"));int planner=panel.Children.IndexOf((UIElement)launcher.FindName("BtnTop50"));
        Check(planners>=0&&planner==planners+1,"Top50 belongs to its own Planners section");
        foreach(var name in new[]{"BtnFlexCalculator","BtnBeamCalculator","BtnBackpropping","BtnSteelShape"})Check(panel.Children.IndexOf((UIElement)launcher.FindName(name))<planners,"planner appears below "+name);
        Check(!((Button)launcher.FindName("BtnDomeCalculator")).IsEnabled&&AutomationProperties.GetName((Button)launcher.FindName("BtnDomeCalculator")).Contains("Beta"),"Dome Beta is locked");
        Check(panel.Children.IndexOf((UIElement)launcher.FindName("BetaSection"))>planner&&panel.Children.IndexOf((UIElement)launcher.FindName("BtnDomeCalculator"))>panel.Children.IndexOf((UIElement)launcher.FindName("BetaSection")),"Dome Beta appears below Top50 Planner");
        Check(launcher.Title=="Engineering Dashboard"&&AutomationProperties.GetName((Button)launcher.FindName("BtnTop50"))=="Top50 Planner","dashboard and accessible entry call Top50 a planner");
        Render(launchRoot,620,840,1,Path.Combine(output,"dashboard-planners.png"));
        var window=new Top50Window(restored);var root=(FrameworkElement)window.Content;
        Check(window.Title.StartsWith("Top50 Planner"),"planner window terminology consistent");
        Check(!app.Windows.OfType<Top50DiagnosticWindow>().Any(),"diagnosis does not open automatically");
        for(int page=0;page<4;page++)
        {
            window.ShowPage(page);var diagnose=Command(root,"Top50Diagnose");
            Check(diagnose.IsEnabled,"diagnose available on page "+page);
            foreach(double scale in new[]{1d,1.25,1.5})
            {
                double width=scale==1?1000:1600/scale,height=scale==1?680:1000/scale;
                Render(root,width,height,scale,Path.Combine(output,"planner-page-"+page+"-"+scale.ToString(CultureInfo.InvariantCulture)+".png"));
                foreach(var menu in Find<Menu>(root).SelectMany(m=>m.Items.OfType<MenuItem>()))
                {var position=menu.TransformToAncestor(root).Transform(new Point(0,0));Check(position.X>=0&&position.X+menu.ActualWidth<=width+1,"D3-style menu "+menu.Header+" visible at "+scale+" on page "+page);}
                foreach(var action in Find<Button>(root).Where(b=>new[]{"Optimize","Cancel"}.Contains(b.Content as string)&&b.Visibility==Visibility.Visible))
                {var position=action.TransformToAncestor(root).Transform(new Point(0,0));Check(position.X>=0&&position.X+action.ActualWidth<=width+1,"header "+action.Content+" visible at "+scale+" on page "+page);}
            }
        }
        root.ClearValue(FrameworkElement.WidthProperty);root.ClearValue(FrameworkElement.HeightProperty);window.Show();Pump();
        var captureBefore=restored.Selected;bool sawDialog=false;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            var dialog=app.Windows.OfType<Top50DiagnosticWindow>().Single();sawDialog=true;
            var box=Find<TextBox>(dialog).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50DiagnosticReport");
            Check(box.IsReadOnly&&box.Text.Contains("MANUAL-001"),"routed Diagnose click opens readable report for active planner");
            Check(Find<Button>(dialog).Any(b=>(string)b.Content=="Copy report")&&Find<Button>(dialog).Any(b=>(string)b.Content=="Save report…"),"report provides copy and save actions");
            Render((FrameworkElement)dialog.Content,1000,650,1,Path.Combine(output,"top50-diagnosis.png"));
            foreach(var button in Find<Button>(dialog))
            {
                var position=button.TransformToAncestor((FrameworkElement)dialog.Content).Transform(new Point(0,0));
                Check(button.ActualWidth>0&&button.ActualHeight>0&&position.X>=0&&position.Y>=0&&position.X+button.ActualWidth<=1001&&position.Y+button.ActualHeight<=651,"diagnosis action visible: "+button.Content+" at "+position+", size "+button.ActualWidth+" x "+button.ActualHeight);
            }
            var first=box.Text;Find<Button>(dialog).Single(b=>(string)b.Content=="Refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(box.Text.Contains("MANUAL-001")&&box.Text.Length>=first.Length-10,"refresh retains complete active planner evidence");
            dialog.Close();
        }));
        Command(root,"Top50Diagnose").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
        Check(sawDialog&&ReferenceEquals(captureBefore,restored.Selected)&&!restored.Dirty,"opening, refreshing and closing diagnosis preserve the project");
        var busy=restored.OptimizeAsync();Check(Command(root,"Top50Diagnose").IsEnabled,"diagnosis stays enabled while optimize runs");restored.Cancel();Wait(busy);
        restored.Save(project);window.Close();launcher.Close();
        File.WriteAllText(Path.Combine(output,"planner-checks.txt"),checks+" checks passed. Controlled inputs; actual Revit2024 host acceptance and original Top50 solver parity are not established.");
        Console.WriteLine(checks+" Top50 planner/diagnostic/production-dashboard checks passed.");return 0;
    }
    private static string SyntheticDxf()
    {
        var pairs=new List<string>{"0","SECTION","2","HEADER","9","$ACADVER","1","AC1015","9","$INSUNITS","70","4","0","ENDSEC","0","SECTION","2","ENTITIES"};
        for(int i=1;i<=3;i++)pairs.AddRange(new[]{"0","DIMENSION","5","A"+i,"8","Wall_Length","2","*D"+i,"10","0","20","0","30","0","70",i==3?"1":"0","13","0","23",(i*1000).ToString(CultureInfo.InvariantCulture),"33","0","14",i==3?"0":"5000","24",(i*1000+(i==3?7200:0)).ToString(CultureInfo.InvariantCulture),"34","0","42",i==3?"7200":"5000","50","0"});
        pairs.AddRange(new[]{"0","ENDSEC","0","EOF"});return string.Join("\n",pairs)+"\n";
    }
    private static void Fails(Action action,string name){try{action();throw new Exception("Expected failure: "+name);}catch(Exception e)when(!e.Message.StartsWith("Expected failure:")){checks++;Console.WriteLine("PASS "+name+" fails with "+e.GetType().Name);}}
    private static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(30);while(!task.IsCompleted){if(DateTime.UtcNow>end)throw new TimeoutException("Planner did not finish");Pump();Thread.Sleep(5);}task.GetAwaiter().GetResult();Pump();}
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Render(FrameworkElement root,double width,double height,double scale,string path)
    {root.Width=width;root.Height=height;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();var image=new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
    private static MenuItem Command(DependencyObject root,string id)=>Find<Menu>(root).SelectMany(menu=>menu.Items.OfType<MenuItem>()).SelectMany(menu=>menu.Items.OfType<MenuItem>()).Single(item=>AutomationProperties.GetAutomationId(item)==id);
    private static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T item)yield return item;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
}
