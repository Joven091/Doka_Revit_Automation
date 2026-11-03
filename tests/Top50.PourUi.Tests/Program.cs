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
    static int checks;
    static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject{if(root is T match)yield return match;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
    static Button Button(DependencyObject root,string id)=>Find<Button>(root).Single(b=>AutomationProperties.GetAutomationId(b)==id);
    static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump();}
    static RevitWallSnapshot Source(params int[] ids)=>new RevitWallSnapshot{DocumentKey="ui-project",DocumentTitle="Controlled UI selection",CapturedUtc="2026-11-03T00:00:00Z",SelectedIds=ids.Select(n=>n.ToString()).ToArray(),Walls=ids.Select(n=>new RevitWallRecord{UniqueId="unique-"+n,ElementId=n.ToString(),CentreLengthMm=n==3?9000:1600,ExteriorLengthMm=n==3?9000:1600,InteriorLengthMm=n==3?9000:1600}).ToArray()};
    static void Wait(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(60);while(!ready()){if(DateTime.UtcNow>end)throw new TimeoutException("Native UI check timed out");Pump();Thread.Sleep(5);}Pump();}
    [STAThread] static int Main(string[] args)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        var vm=new Top50ViewModel();vm.ImportRevit(Source(1,2));vm.EditSettings(s=>s.LongestCount=1);
        RevitWallSnapshot selection=Source(2,3);RevitWallRequest highlighted=null;
        var connection=new Top50RevitConnection{ReadSelection=()=>Task.FromResult(selection.Copy()),Highlight=request=>{highlighted=request;return Task.FromResult(new RevitHighlightResult{Requested=request.UniqueIds.Length,Found=request.UniqueIds.Length,Message="Controlled highlight"});}};
        var window=new Top50Window(vm,connection);window.Show();Pump();
        Check(!Find<DataGrid>(window).Any(),"Import page contains no wall table");
        Check(Button(window,"Top50HighlightWalls").IsEnabled&&Button(window,"Top50AddWall").IsEnabled,"Import page has both requested visual checking and additive import buttons");
        Click(Button(window,"Top50HighlightWalls"));Check(highlighted.UniqueIds.SequenceEqual(new[]{"unique-1","unique-2"})&&!highlighted.Locate,"Highlight walls selects every included imported wall without moving the view");
        Click(Button(window,"Top50AddWall"));Wait(()=>vm.Input.Walls.Count==3);Check(vm.Input.Walls.Count==3,"Add wall appends a missing Revit wall and skips the duplicate");
        Click(Button(window,"Top50Page4"));Click(Button(window,"Top50AddPour"));string p1=vm.Pours.Single().Id;selection=Source(1);
        Click(Button(window,"Top50AssignRevitPour"));Wait(()=>vm.Pours.Single().WallIds.Length==1);Pump();
        Check(vm.Pours.Single().WallIds.Single()==vm.Input.Walls[0].Id,"Assign Revit selection uses the current original wall identity");
        Check(Find<DataGrid>(window).Single().Items.Count==1,"Pour details display assigned walls after native layout without unassigning them");
        string p2=vm.AddPour();vm.SelectedPourId=p1;window.ShowPage(4);Pump();
        var combo=Find<ComboBox>(window).Single();DependencyObject parent=combo;while(parent!=null&&!(parent is DataGridRow))parent=VisualTreeHelper.GetParent(parent);var bounds=combo.TransformToAncestor((DataGridRow)parent).TransformBounds(new Rect(combo.RenderSize));
        Check(bounds.Top>=0&&bounds.Bottom<=((DataGridRow)parent).ActualHeight,"Assignment dropdown fits within its row without clipping text");
        combo.SelectedValue=p2;Pump();
        Check(vm.Pours.Single(p=>p.Id==p1).WallIds.Length==0&&vm.Pours.Single(p=>p.Id==p2).WallIds.Length==1,"Assignment dropdown moves a wall to the chosen pour exactly once");
        Click(Button(window,"Top50Undo"));Check(vm.Pours.Single(p=>p.Id==p1).WallIds.Length==1,"Toolbar Undo restores a wall assignment");
        Click(Button(window,"Top50Redo"));Check(vm.Pours.Single(p=>p.Id==p2).WallIds.Length==1,"Toolbar Redo reapplies a wall assignment");
        vm.AssignWalls(p2,vm.Input.Walls.Select(w=>w.Id));vm.SelectedPourId=p2;window.ShowPage(4);Pump();
        Click(Find<Button>(window).Single(b=>Equals(b.Content,"Check pours")));Wait(()=>!vm.IsBusy&&vm.PourChecks.Count==2);
        Check(vm.PourChecks.Single(p=>p.PourId==p2).Group!=null&&Find<Top50Diagram>(window).Any(),"Check pours produces a validated arrangement diagram in the active sequence page");
        Click(Find<Button>(window).Single(b=>Equals(b.Content,"Show in Revit")));Check(highlighted.Locate&&highlighted.UniqueIds.Length==3,"Show in Revit locates the selected pour rather than an automatic casting group");
        foreach(var size in new[]{new Size(1000,680),new Size(1500,960)})
        {
            window.Width=size.Width;window.Height=size.Height;Pump();var root=(FrameworkElement)window.Content;root.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,"pour-sequence-"+(int)size.Width+".png")))encoder.Save(stream);
            Check(Find<TextBlock>(window).Any(t=>t.Text=="Equipment can be reused after the previous pour is stripped."),"Native sequence view renders at "+size.Width+" px with explicit reuse assumption");
        }
        window.ShowPage(0);Pump();var importRoot=(FrameworkElement)window.Content;var importBitmap=new RenderTargetBitmap((int)importRoot.ActualWidth,(int)importRoot.ActualHeight,96,96,PixelFormats.Pbgra32);importBitmap.Render(importRoot);var image=new PngBitmapEncoder();image.Frames.Add(BitmapFrame.Create(importBitmap));using(var stream=File.Create(Path.Combine(output,"import-simplified.png")))image.Save(stream);
        Check(!Find<DataGrid>(window).Any(),"Returning to Import keeps the wall table hidden");
        vm.Save(Path.Combine(output,"ui-pours.top50"));window.Close();Pump();
        File.WriteAllText(Path.Combine(output,"acceptance.txt"),checks+" native pour/import UI checks passed; Revit callbacks used a controlled contract peer, not an Autodesk host.");
        Console.WriteLine("PASS "+checks+" native pour/import UI checks.");return 0;
    }
}
