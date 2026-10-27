using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
    [STAThread]
    private static int Main(string[] args)
    {
        var application=new Application();string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        var vm=new Top50ViewModel();vm.Import(args[0]);vm.Settings.MaxGroups=15;vm.Settings.Seconds=10;
        if(args.Contains("--interactive")){var interactive=new Top50Window(vm);interactive.Title="Top50 ISOLATED ACCEPTANCE - disposable test state";application.Run(interactive);return 0;}
        // Recorded data is confined to this acceptance fixture, never production candidate discovery.
        var proposal=Top50RecordedFixture.Create(vm.Input);
        vm.AcceptValidatedRun(new SearchRun{Proposals=new[]{proposal},Log=new[]{"Recorded reference fixture: group memberships and aggregate metrics transcribed from video. Unshown individual patterns reconstructed to those constraints. Original algorithm equivalence is not claimed."}});
        string project=Path.Combine(output,"roundtrip.top50");vm.Save(project);
        var loaded=new Top50ViewModel();loaded.Open(project,Path.Combine(output,"source-cache"));
        Check(loaded.Input.Walls.Count==65&&loaded.Selected.Groups.Count==11&&loaded.Selected.StockMicrometres==96250000,"project source, selected stock and validated allocation round trip");
        string workbook=Path.Combine(output,"selected.xlsx");loaded.Export(workbook);
        using(var document=SpreadsheetDocument.Open(workbook,false))
        {
            Check(document.WorkbookPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Count()==9,"nine native workbook sheets");
            var issues=new OpenXmlValidator().Validate(document).ToArray();Check(issues.Length==0,"Open XML workbook schema validation: "+string.Join("; ",issues.Select(i=>i.Description)));
            Check(document.WorkbookPart.WorksheetParts.Any(p=>p.Worksheet.InnerText.Contains("S3")),"selected alternative represented in workbook");
        }
        loaded.SelectedGroupIndex=2;var window=new Top50Window(loaded);var root=(FrameworkElement)window.Content;
        string[] names={"import","settings","results","casting-groups","reports"};
        foreach(var scale in new[]{1d,1.25,1.5})
        {
            int width=scale==1?2536:1600,height=scale==1?1350:1000;
            for(int page=0;page<5;page++)
            {
                window.ShowPage(page);root.Width=width/scale;root.Height=height/scale;root.Measure(new Size(width/scale,height/scale));root.Arrange(new Rect(0,0,width/scale,height/scale));root.UpdateLayout();
                Check(!Find<TextBlock>(root).Any(t=>t.Text==Top50Window.Disclaimer),"no persistent review footer "+names[page]+" at "+(scale*100)+"%");
                foreach(var action in Find<Button>(root).Where(b=>new[]{"Open project","Save project","Optimize","Cancel"}.Contains(b.Content as string))){var origin=action.TransformToAncestor(root).Transform(new Point(0,0));Check(origin.X>=0&&origin.X+action.ActualWidth<=width/scale+1,"header "+action.Content+" remains visible");}
                var bitmap=new RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(root);
                var corner=new byte[4];bitmap.CopyPixels(new Int32Rect(width-2,height-2,1,1),corner,4,0);Check(corner[3]==255&&corner[2]==242&&corner[1]==243&&corner[0]==246,"opaque original main background "+names[page]+" at "+(scale*100)+"%");
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));string path=Path.Combine(output,names[page]+"-"+(int)(scale*100)+".png");using(var stream=File.Create(path))encoder.Save(stream);
                Check(root.ActualWidth>0&&root.ActualHeight>0,"render "+names[page]+" at "+(scale*100)+"% DPI");
            }
        }
        // Exercise actual native HWND, routed navigation events and asynchronous cancellation.
        root.ClearValue(FrameworkElement.WidthProperty);root.ClearValue(FrameworkElement.HeightProperty);
        window.Show();Pump();SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        Check(new System.Windows.Interop.WindowInteropHelper(window).Handle!=IntPtr.Zero,"native WPF HWND created");
        for(int cycle=0;cycle<3;cycle++)for(int page=0;page<5;page++)
        {
            var nav=Find<Button>(root).Single(b=>(b.Content as string)?.StartsWith((page+1).ToString("00")+"   ")==true);
            nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(window.CurrentPage==page,"routed navigation cycle "+cycle+" page "+page);
        }
        foreach(var size in new[]{new Size(1000,680),new Size(1500,960),new Size(1900,1100)})
        {window.Width=size.Width;window.Height=size.Height;Pump();Check(root.ActualWidth>0&&root.ActualHeight>0,"native resize "+size);}
        var cancelRun=loaded.OptimizeAsync();loaded.Cancel();Wait(cancelRun);
        Check(!loaded.IsBusy,"cancel returns view model to idle");
        Check(loaded.Selected!=null,"cancellation preserves previously validated proposal");
        loaded.Settings.Seconds=1;var retry=loaded.OptimizeAsync();Wait(retry);
        Check(!loaded.IsBusy&&loaded.Proposals.Count>0&&!loaded.IsStale,"retry accepts validated fresh proposals");
        loaded.Save(Path.Combine(output,"interactive-retry.top50"));Check(!loaded.Dirty,"save clears dirty state after retry");
        window.Close();Pump();Check(!window.IsVisible,"clean saved window closes");
        // Dirty/stale exports are blocked, with the existing cached result retained.
        loaded.Changed();Check(loaded.IsStale&&loaded.Selected!=null,"stale proposal retained after input edit");
        try{loaded.Export(Path.Combine(output,"must-not-export.xlsx"));throw new Exception("stale export accepted");}catch(InvalidOperationException){checks++;}
        var manual=new Top50ViewModel();manual.AddManual(1600,"Reviewed test wall");string manualProject=Path.Combine(output,"manual.top50");manual.Save(manualProject);var restoredManual=new Top50ViewModel();restoredManual.Open(manualProject,Path.Combine(output,"manual-cache"));Check(restoredManual.Input.Walls.Single().LengthMm==1600&&restoredManual.Input.Walls.Single().Acknowledged,"manual reviewed source project round trip");
        File.WriteAllText(Path.Combine(output,"ui-checks.txt"),checks+" checks passed. Rendered source-linked production Top50 views; not an in-Revit acceptance run.\n");
        Console.WriteLine(checks+" native UI/persistence/export checks passed.");return 0;
    }
    private static void Pump(){var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);}
    private static void Wait(System.Threading.Tasks.Task task){var until=DateTime.UtcNow.AddSeconds(40);while(!task.IsCompleted){if(DateTime.UtcNow>until)throw new TimeoutException("UI work did not finish");Pump();Thread.Sleep(5);}task.GetAwaiter().GetResult();Pump();}
    private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match)yield return match;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
}
