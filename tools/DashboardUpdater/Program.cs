using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EngineeringDashboard.DesktopHost;

internal sealed class WindowsDashboardUpdateEnvironment : IDashboardUpdateEnvironment,IDashboardFailedStartupCloser,IDashboardUpdateAbortSignal
{
    readonly Dictionary<int,Tuple<long,string>> children=new Dictionary<int,Tuple<long,string>>();
    public string AbortFile {get;set;}
    public bool IsAbortRequested=>AbortFile!=null&&File.Exists(AbortFile);
    public static bool IsProcess(int pid,long started,string executable=null)
    {
        try {using(var p=Process.GetProcessById(pid)){return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==started&&(executable==null||DashboardUpdatePlan.SamePath(p.MainModule?.FileName,executable));}}
        catch(Exception error)when(error is ArgumentException||error is InvalidOperationException||error is System.ComponentModel.Win32Exception){return false;}
    }
    public bool IsDashboardRunning(DashboardUpdatePlan plan)=>IsProcess(plan.ParentProcessId,plan.ParentStartedUtcTicks,plan.ParentExecutable);
    public bool IsRevitRunning(DashboardUpdatePlan plan)=>IsProcess(plan.RevitProcessId,plan.RevitStartedUtcTicks);
    // These exact children were launched here from the validated executable.
    // Module metadata can be unavailable while Windows initializes the process;
    // its recorded PID/start time already identifies the owned process safely.
    public bool HasOwnedLiveDashboard=>children.Any(p=>IsProcess(p.Key,p.Value.Item1));
    public DashboardStartedProcess StartDashboard(string executable,string checkpointPath,string acknowledgementPath,string nonce)
    {
        var start=new ProcessStartInfo(executable,"--restore-session \""+checkpointPath+"\""){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)};
        if(acknowledgementPath!=null){start.EnvironmentVariables["DOKA_DASHBOARD_STARTUP_ACK"]=acknowledgementPath;start.EnvironmentVariables["DOKA_DASHBOARD_STARTUP_NONCE"]=nonce;}
        else {start.EnvironmentVariables.Remove("DOKA_DASHBOARD_STARTUP_ACK");start.EnvironmentVariables.Remove("DOKA_DASHBOARD_STARTUP_NONCE");}
        using(var process=Process.Start(start))
        {var result=new DashboardStartedProcess{ProcessId=process.Id,StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks};children[result.ProcessId]=Tuple.Create(result.StartedUtcTicks,executable);return result;}
    }
    public bool IsStartedDashboardRunning(DashboardStartedProcess process)
    {Tuple<long,string> expected;return process!=null&&children.TryGetValue(process.ProcessId,out expected)&&expected.Item1==process.StartedUtcTicks&&IsProcess(process.ProcessId,process.StartedUtcTicks);}
    public bool RequestCloseFailedStartup(DashboardStartedProcess process)
    {
        if(!IsStartedDashboardRunning(process))return true;
        try
        {
            using(var child=Process.GetProcessById(process.ProcessId))
            {
                if(child.HasExited||child.StartTime.ToUniversalTime().Ticks!=process.StartedUtcTicks)return true;
                child.CloseMainWindow();child.WaitForExit(3000);
            }
        }
        catch(Exception error)when(error is ArgumentException||error is InvalidOperationException){ /* The owned child exited during the close request. */ }
        return !IsStartedDashboardRunning(process);
    }
    public void Wait(TimeSpan duration,CancellationToken token){if(token.WaitHandle.WaitOne(duration))token.ThrowIfCancellationRequested();}
}
internal sealed class DashboardInstallWindow : Window
{
    readonly DashboardUpdatePlan plan;readonly string directory;readonly CancellationTokenSource cancel=new CancellationTokenSource();
    readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,16)};
    readonly Button close=new Button{Content="Cancel",HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(12,6,12,6)};
    bool running=true;public int ExitCode {get;private set;}=1;
    public DashboardInstallWindow(DashboardUpdatePlan value,string planDirectory)
    {
        plan=value;directory=planDirectory;Title="Engineering Dashboard update";Width=530;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var body=new StackPanel{Margin=new Thickness(24)};body.Children.Add(new TextBlock{Text="Updating the Dashboard",FontSize=21,FontWeight=FontWeights.SemiBold});body.Children.Add(status);body.Children.Add(close);Content=body;
        close.Click+=(s,e)=>{if(running){cancel.Cancel();close.IsEnabled=false;status.Text="Cancelling and retaining your recovery checkpoint…";}else Close();};
        Closing+=(s,e)=>{if(running){e.Cancel=true;cancel.Cancel();}};Loaded+=async(s,e)=>await Run();
    }
    async Task Run()
    {
        var environment=new WindowsDashboardUpdateEnvironment{AbortFile=Path.Combine(directory,"cancel.json")};
        try
        {
            var progress=new Progress<string>(message=>{status.Text=message;Log(message);});
            await Task.Run(()=>DashboardUpdateTransaction.Apply(plan,environment,cancel.Token,message=>((IProgress<string>)progress).Report(message)));
            status.Text="Dashboard updated. Revit stayed open and your work was restored.";ExitCode=0;
        }
        catch(Exception error)
        {
            // A failure/cancel can race the original UI's clean shutdown. Allow
            // that owned process to finish before deciding whether to restore it.
            var deadline=Stopwatch.StartNew();while(environment.IsDashboardRunning(plan)&&deadline.Elapsed<TimeSpan.FromSeconds(3))await Task.Delay(100);
            if(!environment.IsDashboardRunning(plan)&&!environment.HasOwnedLiveDashboard&&environment.IsRevitRunning(plan))
            {try{environment.StartDashboard(plan.ParentExecutable,plan.CheckpointPath,null,null);}catch(Exception restoreError)when(restoreError is IOException||restoreError is InvalidOperationException||restoreError is System.ComponentModel.Win32Exception){Log("Previous Dashboard could not reopen: "+restoreError.Message);}}
            status.Text=(error is OperationCanceledException?"Update cancelled.":"Dashboard update did not complete: "+error.Message)+" Your recovery checkpoint was retained. Revit was not closed.";Log(status.Text);
        }
        finally{running=false;close.IsEnabled=true;close.Content="Close";}
    }
    void Log(string message){try{File.AppendAllText(Path.Combine(directory,"update.log"),DateTime.UtcNow.ToString("O")+" "+message+Environment.NewLine);}catch(IOException){}}
}
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if(args.Length!=2||args[0]!="--plan")throw new InvalidDataException("A Dashboard update plan is required.");
            string path=Path.GetFullPath(args[1]),root=DashboardPaths.Root,handoffs=Path.Combine(root,"handoffs")+Path.DirectorySeparatorChar;
            if(!path.StartsWith(handoffs,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!="update-plan.json")throw new InvalidDataException("Run the Dashboard updater with its local handoff plan.");
            var plan=DashboardPackages.ReadJson<DashboardUpdatePlan>(path,128*1024);plan.Validate(root);
            string own=Process.GetCurrentProcess().MainModule.FileName;
            if(!DashboardUpdatePlan.SamePath(Path.GetDirectoryName(own),Path.GetDirectoryName(plan.ParentExecutable)))throw new InvalidDataException("The Dashboard helper does not match its running version.");
            var environment=new WindowsDashboardUpdateEnvironment();if(!environment.IsDashboardRunning(plan)||!environment.IsRevitRunning(plan))throw new InvalidDataException("The original Dashboard or Revit process has closed.");
            string key=Path.GetFullPath(root).ToLowerInvariant();using(var sha=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-","");
            bool owned;using(var mutex=new Mutex(true,"Local\\DokaDashboardUpdate-"+key,out owned))
            {
                if(!owned)throw new InvalidOperationException("Another Dashboard update is already in progress.");
                try
                {
                    var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var window=new DashboardInstallWindow(plan,Path.GetDirectoryName(path));
                    DashboardPackages.WriteJson(Path.Combine(Path.GetDirectoryName(path),"helper-ready.json"),new{Ready=true});app.Run(window);return window.ExitCode;
                }
                finally{mutex.ReleaseMutex();}
            }
        }
        catch(Exception error){MessageBox.Show(error.Message,"Dashboard update",MessageBoxButton.OK,MessageBoxImage.Warning);return 1;}
    }
}
