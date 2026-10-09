using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class PresentationUiTests
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception("Presentation: "+label);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    static RenderTargetBitmap Capture(FrameworkElement view,int width,int height,string path=null)
    {
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
        if(path!=null){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}return bitmap;
    }
    static void SetBusy(BeamCalculatorViewModel vm,bool busy)
    {
        typeof(BeamCalculatorViewModel).GetProperty(nameof(vm.IsOptimizing)).SetValue(vm,busy);
        typeof(BeamCalculatorViewModel).GetMethod("NotifyTower",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vm,null);
    }
    public static int Run(string output)
    {
        // Check visible flange/web geometry independently of labels or draw counters.
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())H20Drawing.EndSection(dc,new Rect(10,10,80,200));
        var profile=new RenderTargetBitmap(100,220,96,96,PixelFormats.Pbgra32);profile.Render(drawing);
        var pixels=new byte[100*220*4];profile.CopyPixels(pixels,400,0);
        Func<int,int,byte> alpha=(x,y)=>pixels[(y*100+x)*4+3];
        Check(alpha(15,20)>0 && alpha(15,200)>0 && alpha(50,110)>0 && alpha(15,110)==0 && alpha(85,110)==0,"I profile has two solid flanges, a central web and clear side recesses");
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true};
        var workspace=new BeamFormworkWorkspace(vm,null,null,null) {WindowState=WindowState.Normal,Width=1280,Height=860};workspace.Show();Pump();
        // Start the search clock after loading the window: WPF initialization on a
        // busy CI runner can itself take longer than the mascot's one-second delay.
        var trace=new BeamSearchTrace("presentation fixture");trace.Work("Transfer tower/support geometry");
        typeof(BeamCalculatorViewModel).GetField("beamSearchTrace",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm,trace);
        SetBusy(vm,true);Pump();
        Check(workspace.Waiting.Visibility==Visibility.Visible && workspace.Waiting.Hugo.Visibility==Visibility.Hidden,"search stage appears immediately, mascot is delayed to avoid flashing");
        while(trace.ElapsedSeconds<1.2){Pump();Thread.Sleep(5);}workspace.Waiting.RefreshProgress();
        Check(workspace.Waiting.Hugo.Visibility==Visibility.Visible && workspace.Waiting.IsTimerRunning,"Hugo appears after one second and stays in the drawing overlay");
        trace.Work("Transfer slab and lower secondary options");workspace.Waiting.RefreshProgress();
        Check(workspace.Waiting.Stage.Text=="Checking secondary H20s" && workspace.Waiting.Elapsed.Text.Contains("seconds elapsed"),"waiting card shows live stage and elapsed time without a fabricated percentage");
        Check(!workspace.Waiting.Cancel.IsEnabled,"waiting card cannot cancel a fixture with no active solver");
        Capture((FrameworkElement)workspace.Content,1280,860,output==null ? null : Path.ChangeExtension(output,"workspace-hugo.png"));
        SetBusy(vm,false);Pump();
        Check(workspace.Waiting.Visibility==Visibility.Collapsed && !workspace.Waiting.IsTimerRunning,"completion removes the overlay and stops animation");
        SetBusy(vm,true);Pump();Check(workspace.Waiting.IsTimerRunning,"a later search restarts the waiting card");
        workspace.Close();Pump();Check(!workspace.Waiting.IsTimerRunning,"closing a drawing window releases its animation timer");
        // A fresh loaded view can observe the same running model after an earlier view closes.
        var next=new BeamFormworkWorkspace(vm,null,null,null);next.Show();Pump();
        Check(next.Waiting.Visibility==Visibility.Visible && next.Waiting.IsTimerRunning,"new drawing view attaches to an already running search");
        next.Close();SetBusy(vm,false);Pump();Check(!next.Waiting.IsTimerRunning,"unloaded view remains detached from later model events");
        Console.WriteLine($"Passed {checks} presentation UI checks.");return checks;
    }
}
