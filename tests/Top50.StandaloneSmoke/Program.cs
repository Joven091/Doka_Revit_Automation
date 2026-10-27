using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var assembly=typeof(Top50Window).Assembly;
            Check(Path.GetFileName(assembly.Location)=="Top50Planner.exe","tests load the shipping standalone executable");
            Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("Revit",StringComparison.OrdinalIgnoreCase)||a.Name=="MyRevitAddin"),"standalone executable has no Revit or add-in assembly reference");
            Check(!Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory,"*.addin").Any()&&!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"access-settings.json")),"portable payload contains no Revit manifest or access configuration");
            string marker=Path.Combine(output,"startup with spaces.txt");
            var start=new ProcessStartInfo(assembly.Location,"--startup-check \""+marker+"\""){UseShellExecute=false};
            using(var process=Process.Start(start))
            {
                if(!process.WaitForExit(30000)){process.Kill();throw new TimeoutException("Standalone startup did not complete.");}
                Check(process.ExitCode==0&&File.ReadAllText(marker).Contains("Empty planner and diagnosis ready"),"actual portable entry point opens and closes its WPF window");
            }
            var empty=new Top50ViewModel();
            Check(empty.Input==null&&empty.Proposals.Count==0&&empty.BuildDiagnosticReport().Contains("No input imported or added"),"tester starts empty, with diagnosis and no injected solution");
            string dxf=Path.Combine(output,"controlled-walls.dxf");File.WriteAllText(dxf,SyntheticDxf());
            var model=new Top50ViewModel();model.Import(dxf);
            Check(model.Input.Walls.Count==4&&model.Input.Walls.All(w=>w.Included&&w.LengthMicrometres==5000000),"portable DXF reader imports four measured 5000 mm walls");
            model.Settings.CatalogMm=new[]{5000};model.Settings.LongestCount=2;model.Settings.SearchEffort=5;
            Wait(model.OptimizeAsync());
            Check(model.Selected!=null&&model.Proposals.All(p=>Top50Validation.Validate(model.Input.Walls,p,model.Limits).Count==0),"bundled native solver returns independently validated proposals");
            Check(model.Proposals.All(p=>p.PanelCount==model.Selected.PanelCount),"client shortlist contains only the lowest validated panel-count tier");
            string signature=Signature(model);Wait(model.OptimizeAsync());
            Check(signature==Signature(model),"repeat optimization preserves complete stock and group allocations");
            string project=Path.Combine(output,"roundtrip.top50");model.Save(project);
            var loaded=new Top50ViewModel();loaded.Open(project,Path.Combine(output,"cache"));
            Check(Signature(loaded)==signature&&loaded.Settings.Version==2&&!loaded.Dirty,"version-2 project roundtrip preserves proposals and selected stock");
            string workbook=Path.Combine(output,"selected.xlsx");loaded.Export(workbook);
            using(var document=SpreadsheetDocument.Open(workbook,false))
            {
                Check(document.WorkbookPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Count()==9,"portable export contains all nine workbook sheets");
                Check(!new OpenXmlValidator().Validate(document).Any(),"portable workbook passes Open XML schema validation");
            }
            var before=loaded.Selected;var cancel=loaded.OptimizeAsync();loaded.Cancel();Wait(cancel);
            Check(!loaded.IsBusy&&ReferenceEquals(before,loaded.Selected),"cancelled search retains the accepted proposal");
            var window=new Top50Window(loaded);var root=(FrameworkElement)window.Content;
            for(int page=0;page<5;page++)
            {
                window.ShowPage(page);Render(root,1280,800,Path.Combine(output,"page-"+page+".png"));
                Check(Find<Button>(root).Any(b=>AutomationProperties.GetAutomationId(b)=="Top50Diagnose"&&b.IsEnabled),"Diagnose is available on planner page "+page);
            }
            window.Show();Pump();bool opened=false;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var dialog=app.Windows.OfType<Top50DiagnosticWindow>().Single();opened=true;
                var report=Find<TextBox>(dialog).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50DiagnosticReport");
                Check(report.IsReadOnly&&report.Text.Contains("Search effort uses repeatable")&&report.Text.Contains("Validation against CURRENT inputs/settings: passed"),"Diagnose opens the shipping quantity-first search report");
                File.WriteAllText(Path.Combine(output,"diagnosis.txt"),report.Text);
                Render((FrameworkElement)dialog.Content,1000,650,Path.Combine(output,"diagnosis.png"));dialog.Close();
            }));
            Find<Button>(root).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50Diagnose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(opened&&ReferenceEquals(before,loaded.Selected)&&!loaded.Dirty,"opening and closing diagnosis preserves project state");
            window.Close();app.Shutdown();
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Revit",StringComparison.OrdinalIgnoreCase)),"complete portable import/solve/export/UI flow loads no Revit assembly");
            File.WriteAllText(Path.Combine(output,"checks.txt"),checks+" standalone package checks passed.");
            Console.WriteLine(checks+" standalone package checks passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static string Signature(Top50ViewModel model)=>string.Join("\n",model.Proposals.Select(p=>p.Id+"|"+p.Rank+"|"+string.Join(",",p.Stock.OrderBy(s=>s.Key).Select(s=>s.Key+":"+s.Value))+"|"+string.Join(";",p.Groups.Select(g=>string.Join(",",g.Walls.Select(w=>w.WallId+":"+string.Join("/",w.Pattern.PanelsMm)+":"+string.Join("/",w.Pattern.InternalGapsMicrometres)))))));
    private static string SyntheticDxf()
    {
        var pairs=new List<string>{"0","SECTION","2","HEADER","9","$ACADVER","1","AC1015","9","$INSUNITS","70","4","0","ENDSEC","0","SECTION","2","ENTITIES"};
        for(int i=1;i<=4;i++)pairs.AddRange(new[]{"0","DIMENSION","5","A"+i,"8","Wall_Length","2","*D"+i,"10","0","20","0","30","0","70","0","13","0","23",(i*1000).ToString(CultureInfo.InvariantCulture),"33","0","14","5000","24",(i*1000).ToString(CultureInfo.InvariantCulture),"34","0","42","5000","50","0"});
        pairs.AddRange(new[]{"0","ENDSEC","0","EOF"});return string.Join("\n",pairs)+"\n";
    }
    private static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(45);while(!task.IsCompleted){if(DateTime.UtcNow>end)throw new TimeoutException("Portable smoke search exceeded 45 seconds.");Pump();Thread.Sleep(5);}task.GetAwaiter().GetResult();Pump();}
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Render(FrameworkElement root,int width,int height,string path)
    {root.Width=width;root.Height=height;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
    private static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T item)yield return item;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
}
