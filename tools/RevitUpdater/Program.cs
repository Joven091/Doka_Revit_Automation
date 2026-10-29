using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MyRevitAddin.Updates;

internal sealed class WindowsUpdateEnvironment : IUpdateEnvironment
{
    public bool RevitIsRunning(UpdatePlan plan)
    {
        try
        {
            using(var host=Process.GetProcessById(plan.HostProcessId))
                if(host.StartTime.ToUniversalTime().Ticks==plan.HostStartedUtcTicks && !host.HasExited)return true;
        }
        catch(ArgumentException){}catch(InvalidOperationException){}
        int session=Process.GetCurrentProcess().SessionId;
        foreach(var process in Process.GetProcessesByName("Revit"))using(process)
        {
            try{if(process.SessionId==session && !process.HasExited)return true;}
            catch(InvalidOperationException){}catch(Win32Exception){return true;}
        }
        return false;
    }
    public bool FilesAreAvailable(UpdatePlan plan)
    {
        foreach(string name in new[] {Path.GetFileName(UpdateFiles.InstalledAssembly(plan)),"MyRevitAddin.dll","OxyPlot.dll","OxyPlot.Wpf.dll","OxyPlot.Wpf.Shared.dll"}.Distinct())
        {
            string path=Path.Combine(plan.InstallDirectory,name);if(!File.Exists(path))continue;
            try{using(var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }
            catch(IOException ex) when((ex.HResult&0xffff)==32 || (ex.HResult&0xffff)==33){return false;}
        }
        return true;
    }
    public int Install(UpdatePlan plan)
    {
        var current=InstalledVersion(plan);var requested=UpdateFiles.ParseVersion(plan.Release.Version);
        if(current==requested)return 0;
        if(current>requested)throw new IOException("A newer add-in version is already installed. Check for updates again.");
        if(RevitIsRunning(plan) || !FilesAreAvailable(plan))throw new IOException("Revit was opened again before installation. Close Revit and check for updates again.");
        string log=Path.Combine(Path.GetDirectoryName(plan.InstallerPath),"install.log");
        var start=new ProcessStartInfo(plan.InstallerPath) {UseShellExecute=false,CreateNoWindow=true,
            Arguments="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR=\""+plan.InstallDirectory.TrimEnd('\\')+"\" /LOG=\""+log+"\""};
        using(var installer=Process.Start(start)){installer.WaitForExit();return installer.ExitCode;}
    }
    public Version InstalledVersion(UpdatePlan plan)
        =>UpdateFiles.ParseVersion(FileVersionInfo.GetVersionInfo(UpdateFiles.InstalledAssembly(plan)).FileVersion);
}

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool headless=args.Contains("--headless");int at=Array.IndexOf(args,"--plan");
        try
        {
            if(at<0 || at+1>=args.Length)throw new InvalidDataException("A staged update plan is required.");
            string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaRevitAutomation","Updates");
            string installation=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins","2024");
            var plan=UpdateFiles.ReadPlan(Path.GetFullPath(args[at+1]),cache,installation);
            if(!UpdateFiles.SamePath(AppDomain.CurrentDomain.BaseDirectory,Path.GetDirectoryName(args[at+1])))throw new InvalidDataException("Run the staged updater beside its update plan.");
            string key;
            using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(installation.ToLowerInvariant()))).Replace("-","");
            bool owned;
            using(var mutex=new Mutex(true,@"Global\DokaRevitUpdate-"+key,out owned))
            {
                if(!owned)throw new InvalidOperationException("An add-in update is already waiting or installing.");
                try
                {
                    if(headless){DeferredUpdate.Apply(plan,new WindowsUpdateEnvironment(),CancellationToken.None,s=>Log(plan,s));return 0;}
                    var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};var window=new InstallWindow(plan);
                    app.Run(window);return window.ExitCode;
                }
                finally{mutex.ReleaseMutex();}
            }
        }
        catch(Exception ex)
        {
            if(!headless)MessageBox.Show(ex.Message,"Add-in update",MessageBoxButton.OK,MessageBoxImage.Warning);
            return 1;
        }
    }
    internal static void Log(UpdatePlan plan,string text)
    {
        try{File.AppendAllText(Path.Combine(Path.GetDirectoryName(plan.InstallerPath),"updater.log"),DateTime.UtcNow.ToString("O")+" "+text+Environment.NewLine);}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

internal sealed class InstallWindow : Window
{
    readonly CancellationTokenSource cancellation=new CancellationTokenSource();
    readonly TextBlock status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,20)};
    readonly Button close=new Button {Content="Cancel update",Padding=new Thickness(16,6,16,6),HorizontalAlignment=HorizontalAlignment.Right};
    bool installing,finished;
    internal int ExitCode=1;
    internal InstallWindow(UpdatePlan plan)
    {
        Title="Doka Revit Automation · update";Width=470;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new System.Windows.Media.FontFamily("Segoe UI");FontSize=13;
        var body=new StackPanel {Margin=new Thickness(24)};
        body.Children.Add(new TextBlock {Text="Update to "+plan.Release.Version,FontSize=20,FontWeight=FontWeights.SemiBold});body.Children.Add(status);body.Children.Add(close);Content=body;
        close.Click+=(s,e)=>Close();Closing+=(s,e)=>{if(installing && !finished){e.Cancel=true;return;}cancellation.Cancel();};
        Loaded+=async(s,e)=>
        {
            try
            {
                await Task.Run(()=>DeferredUpdate.Apply(plan,new WindowsUpdateEnvironment(),cancellation.Token,message=>
                {
                    Program.Log(plan,message);Dispatcher.Invoke(()=>{status.Text=message;if(message.StartsWith("Installing")){installing=true;close.IsEnabled=false;}});
                }));
                ExitCode=0;
            }
            catch(OperationCanceledException){return;}
            catch(Exception ex){Program.Log(plan,ex.Message);status.Text="The update was not completed. "+ex.Message;}
            finally{finished=true;close.IsEnabled=true;close.Content="Close";}
        };
    }
}
