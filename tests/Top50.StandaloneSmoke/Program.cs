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
            Check(Top50OfferPresentation.Ordinal(1)=="1st"&&Top50OfferPresentation.Ordinal(11)=="11th"&&Top50OfferPresentation.Ordinal(22)=="22nd","rank suffixes work beyond the first ten alternatives");
            var connectorCase=new Proposal("connector-check",new Dictionary<int,int>{{500,9}},new[]{new CastingGroup(new[]{new WallArrangement("direct",new PanelPattern(Enumerable.Repeat(500,9),Enumerable.Repeat(0L,8)))}),new CastingGroup(new[]{new WallArrangement("fillers",new PanelPattern(Enumerable.Repeat(500,6),Enumerable.Repeat(100000L,5)))})});
            var reusable=ConnectorAllocation.Calculate(connectorCase,new NativeTop50Project());
            Check(reusable.Determined&&reusable.AssumedSingleLevel&&reusable.AdjustableStock==5&&reusable.StandardStock==3,"different casting peaks reuse five universal plus three direct connectors, rather than summing peaks");
            Check(Enumerable.Range(0,reusable.DirectUnits.Length).All(i=>reusable.FillerUnits[i]<=reusable.AdjustableStock&&reusable.DirectUnits[i]<=reusable.StandardStock+reusable.AdjustableStock-reusable.FillerUnits[i]),"reusable connector stock covers every casting simultaneously");
            var reviewed=ConnectorAllocation.Calculate(connectorCase,new NativeTop50Project{ConnectionReviewed=true,ConnectionLevels=2,ConnectionFaces=2});
            Check(!reviewed.AssumedSingleLevel&&reviewed.StandardStock==12&&reviewed.AdjustableStock==20,"saved reviewed connector multipliers remain supported");
            var settingsModel=new Top50ViewModel();var settingsWindow=new Top50Window(settingsModel);settingsWindow.ShowPage(1);
            var settingsRoot=(FrameworkElement)settingsWindow.Content;Render(settingsRoot,1280,800,Path.Combine(output,"method-fixed.png"));
            var method=Find<ComboBox>(settingsRoot).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50OfferingMethod");
            var fixedInputs=Find<StackPanel>(settingsRoot).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50FixedMethodInputs");
            var percentageInputs=Find<StackPanel>(settingsRoot).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50PercentageMethodInputs");
            Check(fixedInputs.Visibility==Visibility.Visible&&percentageInputs.Visibility==Visibility.Collapsed,"fixed method hides percentage-only inputs");
            method.SelectedIndex=1;Render(settingsRoot,1280,800,Path.Combine(output,"method-percentage.png"));
            Check(fixedInputs.Visibility==Visibility.Collapsed&&percentageInputs.Visibility==Visibility.Visible,"switching to percentage hides fixed-set inputs immediately");
            method.SelectedIndex=0;Check(settingsModel.Settings.LongestCount==5&&settingsModel.Settings.Percentage==45,"changing method preserves hidden input values");
            settingsModel.AddManual(1000,"Settings visibility check");settingsModel.Save(Path.Combine(output,"settings.top50"));settingsWindow.Close();
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
            var labels=new Top50WallLabels(loaded.Input.Walls);var reorderedLabels=new Top50WallLabels(Enumerable.Reverse(loaded.Input.Walls));
            Check(labels[loaded.Input.Walls[0].Id]=="W-01 (Length = 5.00 m)"&&labels[loaded.Input.Walls[1].Id]=="W-02 (Length = 5.00 m)","equal-length walls have separate readable names");
            Check(loaded.Input.Walls.All(w=>labels[w.Id]==reorderedLabels[w.Id]&&labels[w.Id]==new Top50WallLabels(model.Input.Walls)[w.Id]),"wall names survive row reordering and project reopen");
            var precise=new Top50ViewModel();precise.AddManual(19204.608m,"Presentation precision check");
            Check(new Top50WallLabels(precise.Input.Walls)[precise.Input.Walls[0].Id]=="W-01 (Length = 19.20 m)"&&precise.Input.Walls[0].LengthMicrometres==19204608,"metre labels round to two decimals without changing exact geometry");
            string workbook=Path.Combine(output,"selected.xlsx");loaded.Export(workbook);
            using(var document=SpreadsheetDocument.Open(workbook,false))
            {
                Check(document.WorkbookPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Count()==9,"portable export contains all nine workbook sheets");
                Check(!new OpenXmlValidator().Validate(document).Any(),"portable workbook passes Open XML schema validation");
                var text=string.Join("\n",document.WorkbookPart.WorksheetParts.Select(p=>p.Worksheet.InnerText));
                Check(text.Contains("W-01 (Length = 5.00 m)")&&text.Contains("Casting Group")&&!text.Contains("DXF-A1"),"workbook uses the same wall names and casting-group terminology as the planner");
            }
            var before=loaded.Selected;var cancel=loaded.OptimizeAsync();loaded.Cancel();Wait(cancel);
            Check(!loaded.IsBusy&&ReferenceEquals(before,loaded.Selected),"cancelled search retains the accepted proposal");
            var window=new Top50Window(loaded);var root=(FrameworkElement)window.Content;
            for(int page=0;page<5;page++)
            {
                window.ShowPage(page);Render(root,1280,800,Path.Combine(output,"page-"+page+".png"));
                Check(Find<Button>(root).Any(b=>AutomationProperties.GetAutomationId(b)=="Top50Diagnose"&&b.IsEnabled),"Diagnose is available on planner page "+page);
                Check(!Find<TextBlock>(root).Any(t=>t.Text.Contains("LOCAL DESKTOP / V1")||t.Text.Contains("No AutoCAD installation required")||t.Text.Contains("Offline by design")||t.Text.Contains("Native review build:")),"removed sidebar, subtitle and footer notes stay absent on page "+page);
                if(page==0)
                {
                    var imported=Find<DataGrid>(root).Single();
                    Check(imported.Items.Cast<object>().All(row=>((string)row.GetType().GetProperty("Id").GetValue(row)).StartsWith("W-",StringComparison.Ordinal)),"import rows show readable wall names");
                    Check(!Find<TextBlock>(root).Any(t=>t.Text.Contains("valid walls")||t.Text.Contains("Edit length (mm) to record")||t.Text.StartsWith("Source:",StringComparison.Ordinal)),"import page omits the marked summary, instruction and source-audit text");
                }
                if(page==2)
                {
                    var comparison=Find<DataGrid>(root).Single(t=>t.Columns.Any(c=>Equals(c.Header,"Rank")));
                    Check(comparison.Columns.Select(c=>c.Header as string).SequenceEqual(new[]{"Rank","Types","Panels","Stock m","Casting Groups","Why this rank"}),"comparison retains only requested client-facing columns");
                    Check(comparison.Items.Cast<object>().Select(row=>(string)row.GetType().GetProperty("Rank").GetValue(row)).First()=="1st"&&comparison.Items.Cast<object>().All(row=>!string.IsNullOrWhiteSpace((string)row.GetType().GetProperty("Reason").GetValue(row))),"results show ordinal ranks and individual explanations");
                    Check(Find<DataGrid>(root).Any(t=>t.Columns.Any(c=>Equals(c.Header,"Connector type")))&&!Find<DataGrid>(root).Any(t=>t.Columns.Any(c=>Equals(c.Header,"Panels in each identical set"))),"connector results are visible and unused identical-set columns are hidden");
                    Check(!Find<TextBox>(root).Any(t=>t.IsReadOnly&&t.Text.Contains("Retained model objective:"))&&!Find<TextBlock>(root).Any(t=>t.Text.Contains("Average installed-panel")||t.Text.Contains("Peak joint LOCATIONS")),"results omit the utilization notice and solver log box");
                }
                if(page==3)
                {
                    var table=Find<DataGrid>(root).Single();var diagram=Find<Top50Diagram>(root).Single();
                    Check(Equals(table.Columns[0].Header,"Casting Group")&&table.Items.Cast<object>().All(row=>((string)row.GetType().GetProperty("Ids").GetValue(row)).Contains("(Length = 5.00 m)")),"casting table uses readable wall names and Casting Group header");
                    Check(table.Items.Cast<object>().Select((row,index)=>(string)row.GetType().GetProperty("Id").GetValue(row)==Top50OfferPresentation.CastingGroup(index)).All(equal=>equal),"casting group names use the C-01 series");
                    Check(AutomationProperties.GetName(diagram).Contains("W-")&&!AutomationProperties.GetName(diagram).Contains("DXF-"),"casting diagram uses the same readable wall names");
                    Check(!Find<TextBlock>(root).Any(t=>t.Text.Contains("Equipment allocation groups")||t.Text.Contains("Used / idle by type")),"casting page omits the marked allocation and connector notes");
                }
            }
            window.Show();Pump();bool opened=false;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var dialog=app.Windows.OfType<Top50DiagnosticWindow>().Single();opened=true;
                var report=Find<TextBox>(dialog).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50DiagnosticReport");
                Check(report.IsReadOnly&&report.Text.Contains("Search effort uses repeatable")&&report.Text.Contains("Validation against CURRENT inputs/settings: passed"),"Diagnose opens the shipping quantity-first search report");
                Check(report.Text.Contains("W-01 (Length = 5.00 m) | source ID: DXF-A1"),"diagnosis maps each readable name back to its original source ID");
                File.WriteAllText(Path.Combine(output,"diagnosis.txt"),report.Text);
                Render((FrameworkElement)dialog.Content,1000,650,Path.Combine(output,"diagnosis.png"));dialog.Close();
            }));
            Find<Button>(root).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50Diagnose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(opened&&ReferenceEquals(before,loaded.Selected)&&!loaded.Dirty,"opening and closing diagnosis preserves project state");
            Check(Signature(loaded)==signature&&loaded.Input.Walls.All(w=>w.Id.StartsWith("DXF-",StringComparison.Ordinal)),"presentation changes preserve source identity, stock and complete casting allocations");
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
