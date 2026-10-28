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
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var assembly=typeof(Top50Window).Assembly;
            Check(Path.GetFileName(assembly.Location)=="Top50Planner.exe"&&!assembly.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("Revit",StringComparison.OrdinalIgnoreCase)||a.Name=="MyRevitAddin"),"shipping executable runs independently of Revit");
            string marker=Path.Combine(output,"startup with spaces.txt");
            using(var process=Process.Start(new ProcessStartInfo(assembly.Location,"--startup-check \""+marker+"\""){UseShellExecute=false}))
            {if(!process.WaitForExit(30000)){process.Kill();throw new TimeoutException("Standalone startup timed out.");}Check(process.ExitCode==0&&File.ReadAllText(marker).Contains("Empty planner and diagnosis ready"),"actual entry point opens and closes the empty planner");}
            var empty=new Top50ViewModel();Check(empty.Input==null&&empty.Proposals.Count==0&&!empty.CanUndo&&!empty.CanRedo,"empty tester has no injected solution or editing history");
            var settingsWindow=new Top50Window(empty);settingsWindow.ShowPage(1);var settingsRoot=(FrameworkElement)settingsWindow.Content;Render(settingsRoot,1280,800,Path.Combine(output,"settings-fixed.png"));
            var method=Find<ComboBox>(settingsRoot).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50OfferingMethod");
            var fixedInputs=Find<StackPanel>(settingsRoot).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50FixedMethodInputs");var percentInputs=Find<StackPanel>(settingsRoot).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50PercentageMethodInputs");
            Check(fixedInputs.Visibility==Visibility.Visible&&percentInputs.Visibility==Visibility.Collapsed,"fixed method hides percentage inputs");method.SelectedIndex=1;Check(fixedInputs.Visibility==Visibility.Collapsed&&percentInputs.Visibility==Visibility.Visible,"percentage method hides fixed-set inputs");method.SelectedIndex=0;
            Check(Find<TextBlock>(settingsRoot).Any(t=>t.Text=="Filler settings")&&!Find<TextBlock>(settingsRoot).Any(t=>new[]{"Search effort","Candidate display limit","Maximum panel components","Connectors"}.Contains(t.Text))&&!Find<CheckBox>(settingsRoot).Any(c=>(c.Content as string)?.Contains("connector")==true),"only user-facing filler settings remain; search and connector controls are absent");
            empty.AddManual(1000,"Close settings check");empty.Save(Path.Combine(output,"settings.top50"));settingsWindow.Close();
            string dxf=Path.Combine(output,"controlled-walls.dxf");File.WriteAllText(dxf,SyntheticDxf());var model=new Top50ViewModel();model.Import(dxf);
            Check(model.Input.Walls.Count==4&&model.Input.Walls.All(w=>w.LengthMicrometres==5000000),"DXF import preserves source wall lengths");
            model.EditSettings(s=>{s.CatalogMm=new[]{5000};s.LongestCount=2;s.SearchEffort=.001;s.CandidateDisplayLimit=99;s.ConnectionFaces=1;s.ConnectionLevels=7;s.ConnectionReviewed=true;s.AdjustableOnDirect=false;});Wait(model.OptimizeAsync());
            Check(model.Selected!=null&&model.Proposals.All(p=>Top50Validation.Validate(model.Input.Walls,p,model.Limits).Count==0),"native thorough search produces independently validated proposals");
            Check(model.Limits.SearchEffort==Top50PlannerPolicy.ThoroughSearchEffort&&model.Settings.CandidateDisplayLimit==3&&model.Proposals.Count<=3,"legacy effort and display tuning cannot override thorough search or the three-option limit");
            string signature=Signature(model);Wait(model.OptimizeAsync());Check(Signature(model)==signature,"unchanged input repeats complete native allocations");
            string path=Path.Combine(output,"roundtrip.top50");model.Save(path);var loaded=new Top50ViewModel();loaded.Open(path,Path.Combine(output,"cache"));
            Check(Signature(loaded)==signature&&loaded.Settings.Version==4&&!loaded.Dirty,"V4 project roundtrip preserves per-face allocations and both-face policy");
            Check(Top50PlannerPolicy.TotalPanels(loaded.Selected)==4&&Top50PlannerPolicy.Inventory(loaded.Selected).Sum(row=>row.Quantity)==4&&Top50PlannerPolicy.OfferedLmPerFace(loaded.Selected)==10,"two-panel allocation needs four physical panels while offered length stays ten LM per face");
            var connectorCase=new Proposal("connectors",new Dictionary<int,int>{{500,9}},new[]{new CastingGroup(new[]{new WallArrangement("direct",new PanelPattern(Enumerable.Repeat(500,9),Enumerable.Repeat(0L,8)))}),new CastingGroup(new[]{new WallArrangement("fillers",new PanelPattern(Enumerable.Repeat(500,6),Enumerable.Repeat(100000L,5)))})});
            var connectors=ConnectorAllocation.Calculate(connectorCase,new NativeTop50Project{ConnectionLevels=7,ConnectionFaces=1,ConnectionReviewed=true,AdjustableOnDirect=false});
            Check(connectors.Levels==1&&connectors.Faces==2&&connectors.StandardStock==6&&connectors.AdjustableStock==10,"connectors cover one level and both faces with adjustable reuse always enabled");
            Check(Enumerable.Range(0,connectors.DirectUnits.Length).All(i=>connectors.FillerUnits[i]<=connectors.AdjustableStock&&connectors.DirectUnits[i]<=connectors.StandardStock+connectors.AdjustableStock-connectors.FillerUnits[i]),"reusable connectors cover each casting group simultaneously");
            bool exportBlocked=false;string workbook=Path.Combine(output,"disabled.xlsx");try{loaded.Export(workbook);}catch(NotSupportedException){exportBlocked=true;}Check(exportBlocked&&!File.Exists(workbook),"Excel export is unavailable and produces no file");
            var window=new Top50Window(loaded);var root=(FrameworkElement)window.Content;
            Check(window.Resources.MergedDictionaries.Any(d=>d.Source.OriginalString.Contains("CalculatorStyles.xaml")),"shared dashboard resource dictionary is loaded");
            Check(ReferenceEquals(Find<Button>(root).Single(b=>Equals(b.Content,"Optimize")).Style,window.FindResource("PrimaryAction")),"Optimize uses the dashboard primary-action style");
            var tabs=Find<Button>(root).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("Top50Page",StringComparison.Ordinal)).ToArray();Check(tabs.Select(b=>b.Content as string).SequenceEqual(new[]{"Import","Settings","Results","Casting groups"}),"four workflow tabs remain; Reports is removed");
            Check(Find<MenuItem>(root).Where(m=>m.Header is string).All(m=>!(m.Header as string).Contains("Export")),"File and Tools do not offer an export command");
            for(int page=0;page<4;page++)
            {
                Find<Button>(root).Single(b=>AutomationProperties.GetAutomationId(b)=="Top50Page"+page).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render(root,1280,800,Path.Combine(output,"page-"+page+".png"));
                Check(window.CurrentPage==page&&Command(root,"Top50Diagnose").IsEnabled,"tab navigation and diagnosis work on page "+page);
                if(page==0)
                {var table=Find<DataGrid>(root).Single();Check(table.Columns.Select(c=>c.Header as string).SequenceEqual(new[]{"Include","Wall","Length (mm)","Status"})&&table.SelectionMode==DataGridSelectionMode.Extended,"Import is a concise four-column table with multi-wall selection");}
                if(page==2)
                {
                    var inventory=Find<DataGrid>(root).Single(t=>AutomationProperties.GetAutomationId(t)=="Top50PanelInventory");var connectorTable=Find<DataGrid>(root).Single(t=>AutomationProperties.GetAutomationId(t)=="Top50ConnectorInventory");
                    Check(inventory.Columns.Take(2).Select(c=>c.Header as string).SequenceEqual(new[]{"Panel length (m)","Qty"}),"inventory uses Panel length and Qty");
                    Check(inventory.Items.Cast<PanelInventoryRow>().Sum(r=>r.Quantity)==4&&Find<TextBlock>(root).Any(t=>t.Text=="10.00 LM"),"visible inventory covers both faces and offered LM remains per face");
                    Check(Math.Abs(inventory.TransformToAncestor(root).Transform(new Point()).Y-connectorTable.TransformToAncestor(root).Transform(new Point()).Y)<2&&connectorTable.ActualHeight<=114,"connector table is top-aligned and fits only its two compact rows");
                    Check(inventory.Columns[1].CellStyle!=null&&connectorTable.Columns[2].CellStyle!=null,"quantity columns receive subtle emphasis");
                    var alternatives=Find<Expander>(root).Single(e=>AutomationProperties.GetAutomationId(e)=="Top50Alternatives");Check(!alternatives.IsExpanded,"alternatives are secondary and collapsed initially");alternatives.IsExpanded=true;Render(root,1280,800,Path.Combine(output,"results-alternatives.png"));
                    var comparison=Find<DataGrid>(root).Single(t=>t.Columns.Any(c=>Equals(c.Header,"Rank")));Check(Equals(comparison.Columns.Last().Header,"Status")&&comparison.Items.Count<=3,"comparison uses Status and contains at most three ranked options");
                    Check(connectorTable.Items.Cast<object>().Select(r=>(string)r.GetType().GetProperty("Article").GetValue(r)).SequenceEqual(new[]{"587533000","587532000"}),"connector rows retain verified Doka article numbers");
                }
                if(page==3)
                {
                    Check(!Find<DataGrid>(root).Any(),"casting-group table is removed");var selector=Find<ComboBox>(root).Single(c=>AutomationProperties.GetAutomationId(c)=="Top50CastingGroupSelector");
                    Check(selector.Items.Count==loaded.Selected.Groups.Count&&selector.SelectedItem.ToString()!=null,"dropdown contains every casting group");
                    selector.SelectedIndex=selector.Items.Count-1;Render(root,1280,800,Path.Combine(output,"casting-selected.png"));var diagram=Find<Top50Diagram>(root).Single();Check(loaded.SelectedGroupIndex==selector.SelectedIndex&&AutomationProperties.GetName(diagram).Contains("W-")&&AutomationProperties.GetName(diagram).Contains("both faces"),"group selection updates the schematic with readable wall names and face basis");
                }
            }
            window.Show();Pump();window.ShowPage(0);Pump();root=(FrameworkElement)window.Content;
            var importTable=Find<DataGrid>(root).Single();importTable.SelectedItems.Add(importTable.Items[0]);importTable.SelectedItems.Add(importTable.Items[1]);
            var originalLabels=new Top50WallLabels(loaded.Input.AllWalls);string retainedId=loaded.Input.Walls[2].Id,retainedLabel=originalLabels[retainedId];
            Check(window.TryEditShortcut(System.Windows.Input.Key.Delete,System.Windows.Input.ModifierKeys.None,false),"Delete shortcut is handled for selected import walls");Pump();
            Check(loaded.Input.Walls.Count==2&&loaded.Input.RemovedWalls.Count==2&&loaded.IsStale,"Delete removes both selected walls and invalidates existing results");
            Check(new Top50WallLabels(loaded.Input.AllWalls)[retainedId]==retainedLabel,"deletion preserves remaining wall labels");
            Check(window.TryEditShortcut(System.Windows.Input.Key.Z,System.Windows.Input.ModifierKeys.Control,false),"Ctrl+Z invokes project Undo");Pump();Check(loaded.Input.Walls.Count==4&&!loaded.IsStale,"Undo restores all walls and matching validated results");
            Check(window.TryEditShortcut(System.Windows.Input.Key.Y,System.Windows.Input.ModifierKeys.Control,false),"Ctrl+Y invokes project Redo");Pump();Check(loaded.Input.Walls.Count==2&&loaded.IsStale,"Redo reapplies deletion");
            Check(!window.TryEditShortcut(System.Windows.Input.Key.Z,System.Windows.Input.ModifierKeys.Control,true)&&!window.TryEditShortcut(System.Windows.Input.Key.Delete,System.Windows.Input.ModifierKeys.None,true),"project shortcuts leave text editing to the text box");
            loaded.Save(Path.Combine(output,"deleted.top50"));var reopened=new Top50ViewModel();reopened.Open(Path.Combine(output,"deleted.top50"),Path.Combine(output,"deleted-cache"));Check(reopened.Input.Walls.Count==2&&reopened.Input.RemovedWalls.Count==2,"deleted imported walls remain deleted after project reopen");
            Find<Button>(root).Single(b=>Equals(b.Content,"Undo")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(loaded.Input.Walls.Count==4&&loaded.Dirty,"Undo button restores walls after Save and correctly marks the project unsaved");Find<Button>(root).Single(b=>Equals(b.Content,"Redo")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(!loaded.Dirty,"Redo button restores the saved state and clean flag");loaded.Undo();Pump();
            var before=loaded.Selected;var busy=loaded.OptimizeAsync();Check(!Find<Button>(root).Single(b=>Equals(b.Content,"Undo")).IsEnabled&&!Find<Button>(root).Single(b=>Equals(b.Content,"Redo")).IsEnabled&&!Find<Button>(root).Single(b=>Equals(b.Content,"Delete")).IsEnabled&&Command(root,"Top50Diagnose").IsEnabled,"all edit commands are disabled during optimization while Diagnose stays available");loaded.Cancel();Wait(busy);
            Check(ReferenceEquals(before,loaded.Selected),"Cancel retains the accepted allocation");loaded.Save(path);bool diagnosed=false;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{var dialog=app.Windows.OfType<Top50DiagnosticWindow>().Single();diagnosed=true;var report=Find<TextBox>(dialog).Single(t=>AutomationProperties.GetAutomationId(t)=="Top50DiagnosticReport");Check(report.Text.Contains("both wall faces")&&report.Text.Contains("DXF-A1")&&report.IsReadOnly,"diagnosis retains source evidence and explains physical quantity basis");File.WriteAllText(Path.Combine(output,"diagnosis.txt"),report.Text);dialog.Close();}));
            Command(root,"Top50Diagnose").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(diagnosed&&!loaded.Dirty,"Tools > Diagnose opens without changing the project");
            loaded.EditSettings(s=>s.RequireIdenticalKits=true);Wait(loaded.OptimizeAsync());window.ShowPage(2);Render(root,1280,800,Path.Combine(output,"identical-sets.png"));
            Check(loaded.Selected.IdenticalKitCount==2&&Top50PlannerPolicy.Inventory(loaded.Selected).All(row=>row.QuantityPerSet*2==row.Quantity),"identical-set quantities also cover both faces");loaded.Save(path);window.Close();
            var preview=new Top50ViewModel();var lengths=new[]{1600m,2810m,7200m,9200m,19200m,19204.608m};var counts=new[]{9,2,10,10,21,11};
            for(int n=0;n<lengths.Length;n++)for(int k=0;k<counts[n];k++)preview.AddManual(lengths[n],"Controlled preview distribution");
            Wait(preview.OptimizeAsync());Check(preview.Selected!=null&&preview.Proposals.All(p=>Top50Validation.Validate(preview.Input.Walls,p,preview.Limits).Count==0),"typical multi-size preview comes from a fresh validated thorough solve");
            var previewWindow=new Top50Window(preview);var previewRoot=(FrameworkElement)previewWindow.Content;previewWindow.ShowPage(2);Render(previewRoot,1500,900,Path.Combine(output,"results-typical.png"));previewWindow.ShowPage(3);Render(previewRoot,1500,900,Path.Combine(output,"casting-typical.png"));preview.Save(Path.Combine(output,"controlled-preview.top50"));previewWindow.Close();app.Shutdown();
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Revit",StringComparison.OrdinalIgnoreCase)),"complete flow loads no Revit assemblies");
            File.WriteAllText(Path.Combine(output,"checks.txt"),checks+" standalone package checks passed.");Console.WriteLine(checks+" standalone package checks passed.");return 0;
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
    private static MenuItem Command(DependencyObject root,string id)=>Find<Menu>(root).SelectMany(menu=>menu.Items.OfType<MenuItem>()).SelectMany(menu=>menu.Items.OfType<MenuItem>()).Single(item=>AutomationProperties.GetAutomationId(item)==id);
    private static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T item)yield return item;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
}
