using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    static int checks;
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[0]),installed=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Check(string.Equals(Path.GetFullPath(typeof(Top50ViewModel).Assembly.Location),Path.Combine(installed,"MyRevitAddin.dll"),StringComparison.OrdinalIgnoreCase),"production assembly is loaded from the isolated installed payload");
            string project=Path.Combine(output,"installed-project.top50");
            var model=new Top50ViewModel();
            if(args.Contains("--restart"))
            {
                model.Open(project,Path.Combine(output,"cache"));
                Check(model.Selected!=null && !model.IsStale && model.Input.Walls.Count==2,"a fresh process reopens the installed saved project and validated results");
            }
            else
            {
                model.AddManual(1500,"Installed planning check");model.AddManual(1000,"Installed planning check");
                model.EditSettings(s=>{s.CatalogMm=new[] {500,750};s.LongestCount=2;s.Preset=(int)PlanningPreset.EconomicalOffer;s.MaxFillers=1;s.MaxFillerMm=350;});
            }
            Wait(model.OptimizeAsync());
            Check(model.Selected!=null && !model.IsStale && model.Selected.Groups.Count==1,"installed native planner solves the simultaneous two-wall fixture");
            Check(Top50PlannerPolicy.TotalPanels(model.Selected)==8 && Top50PlannerPolicy.OfferedLmPerFace(model.Selected)==2.25m,"installed inventory is eight physical panels and 2.25 LM per face");
            var connectors=ConnectorAllocation.Calculate(model.Selected);
            Check(connectors.StandardStock==2 && connectors.AdjustableStock==2 && connectors.Levels==1 && connectors.Faces==2,"direct and filler joints require two standard and two adjustable connectors for one level on both faces");
            Check(model.Selected.Groups.Single().Walls.Sum(w=>w.Pattern.DirectLocations)==1 && model.Selected.Groups.Single().Walls.Sum(w=>w.Pattern.FillerLocations)==1,"installed arrangement includes a real direct joint and a 250 mm filler joint");
            var window=new Top50Window(model);window.ShowPage(2);
            Render((FrameworkElement)window.Content,Path.Combine(output,args.Contains("--restart")?"installed-restarted-results.png":"installed-results.png"));
            window.ShowPage(3);Render((FrameworkElement)window.Content,Path.Combine(output,"installed-casting.png"));
            Check(model.BuildDiagnosticReport().Contains("TOP50 PLANNER DIAGNOSTIC REPORT"),"installed diagnosis remains available");
            File.WriteAllText(Path.Combine(output,"installed-diagnosis.txt"),model.BuildDiagnosticReport());model.Save(project);window.Close();
            var beam=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,BeamLengthMm=3000,Depth=500,Width=600,FloorHeight=3000,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=150};
            Wait(beam.OptimizeTowersAsync());
            Check(beam.IsCalculationValid && beam.TowerResult!=null,"installed dashboard beam calculation remains functional");
            string beamReport=beam.BuildDiagnosticReport();
            Check(beamReport.Contains(".NET 10 worker; complete result returned") && !beamReport.Contains("Using .NET Framework fallback:"),"the installed production dashboard uses its bundled worker without silently falling back");
            File.WriteAllText(Path.Combine(output,"installed-worker-diagnosis.txt"),beamReport);
            app.Shutdown();File.WriteAllText(Path.Combine(output,args.Contains("--restart")?"restart-checks.txt":"checks.txt"),checks+" installed production-payload checks passed. Actual Revit host execution is separate.\n");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Wait(Task task)
    {
        var end=DateTime.UtcNow.AddSeconds(60);
        while(!task.IsCompleted)
        {
            if(DateTime.UtcNow>end)throw new TimeoutException("Isolated installed-payload test exceeded its guard; production has no new deadline.");
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
    static void Render(FrameworkElement root,string path)
    {
        root.Width=1200;root.Height=800;root.Measure(new Size(1200,800));root.Arrange(new Rect(0,0,1200,800));root.UpdateLayout();
        var image=new RenderTargetBitmap(1200,800,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);
    }
}
