using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace EngineeringDashboard.DesktopHost
{
    public sealed class DashboardUpdatePlan
    {
        public int SchemaVersion {get;set;}=1;
        public string Root {get;set;}
        public StagedDashboard Staged {get;set;}
        public string ExpectedGeneration {get;set;}
        public int ParentProcessId {get;set;} public long ParentStartedUtcTicks {get;set;}
        public string ParentRelativeDirectory {get;set;} public string ParentExecutable {get;set;}
        public int RevitProcessId {get;set;} public long RevitStartedUtcTicks {get;set;}
        public string CheckpointPath {get;set;} public string CheckpointSha256 {get;set;}
        public void Validate(string expectedRoot)
        {
            Guid generation;
            if(SchemaVersion!=1||Root==null||!SamePath(Root,expectedRoot)||Staged==null||ParentProcessId<=0||ParentStartedUtcTicks<=0||RevitProcessId<=0||RevitStartedUtcTicks<=0
                ||(!string.IsNullOrEmpty(ExpectedGeneration)&&!Guid.TryParseExact(ExpectedGeneration,"N",out generation)))throw new InvalidDataException("Invalid Dashboard restart plan.");
            if(ParentRelativeDirectory==null||!Regex.IsMatch(ParentRelativeDirectory,@"\Aversions/\d+\.\d+\.\d+(?:\.\d+)?-[0-9a-f]{12}\z")||!SamePath(ParentExecutable,Path.Combine(Root,ParentRelativeDirectory.Replace('/',Path.DirectorySeparatorChar),DashboardPackages.EntryFile)))throw new InvalidDataException("The Dashboard restart does not match its running payload.");
            string sessions=Path.Combine(Path.GetFullPath(Root),"Sessions");
            if(CheckpointPath==null||!SamePath(Path.GetDirectoryName(CheckpointPath),sessions)||!Regex.IsMatch(Path.GetFileName(CheckpointPath),@"\A[0-9a-f]{32}\.json\z")
                ||!File.Exists(CheckpointPath)||new FileInfo(CheckpointPath).Length>BridgeProtocol.MaxFrameBytes||(File.GetAttributes(CheckpointPath)&FileAttributes.ReparsePoint)!=0
                ||(File.GetAttributes(sessions)&FileAttributes.ReparsePoint)!=0||DashboardPackages.Hash(CheckpointPath)!=CheckpointSha256)throw new InvalidDataException("The Dashboard recovery checkpoint is missing or changed.");
            DashboardPackages.ValidateInstalled(Root,Staged);
        }
        public static bool SamePath(string a,string b)=>a!=null&&b!=null&&string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
    }
    public sealed class DashboardStartedProcess { public int ProcessId {get;set;} public long StartedUtcTicks {get;set;} }
    public sealed class DashboardStartupReceipt
    {public string Nonce {get;set;} public string Version {get;set;} public int ProcessId {get;set;} public long StartedUtcTicks {get;set;}}
    public interface IDashboardUpdateEnvironment
    {
        bool IsDashboardRunning(DashboardUpdatePlan plan);
        bool IsRevitRunning(DashboardUpdatePlan plan);
        DashboardStartedProcess StartDashboard(string executable,string checkpointPath,string acknowledgementPath,string nonce);
        bool IsStartedDashboardRunning(DashboardStartedProcess process);
        void Wait(TimeSpan duration,CancellationToken token);
    }
    public interface IDashboardFailedStartupCloser
    { bool RequestCloseFailedStartup(DashboardStartedProcess process); }
    public interface IDashboardUpdateAbortSignal { bool IsAbortRequested {get;} }
    public static class DashboardUpdateTransaction
    {
        public static ActiveDashboard Apply(DashboardUpdatePlan plan,IDashboardUpdateEnvironment environment,CancellationToken token,Action<string> status=null,TimeSpan? startupTimeout=null,TimeSpan? poll=null)
        {
            Action checkCancelled=()=>{token.ThrowIfCancellationRequested();if(environment is IDashboardUpdateAbortSignal abort&&abort.IsAbortRequested)throw new OperationCanceledException("Dashboard update handoff was cancelled.");};
            checkCancelled();plan.Validate(plan.Root);var delay=poll??TimeSpan.FromMilliseconds(250);var timeout=startupTimeout??TimeSpan.FromSeconds(120);
            while(environment.IsDashboardRunning(plan))
            {checkCancelled();if(!environment.IsRevitRunning(plan))throw new InvalidOperationException("The original Revit session has closed; the Dashboard update was not activated.");status?.Invoke("Waiting for the Dashboard to close. Revit and its models stay open.");environment.Wait(delay,token);}
            checkCancelled();if(!environment.IsRevitRunning(plan))throw new InvalidOperationException("The original Revit session has closed; reopen the Dashboard from Revit.");
            plan.Validate(plan.Root);
            string handoffs=Path.Combine(plan.Root,"handoffs");Directory.CreateDirectory(handoffs);
            if((File.GetAttributes(handoffs)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Dashboard handoff paths cannot be links.");
            string handoff=Path.Combine(handoffs,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(handoff);
            string acknowledgement=Path.Combine(handoff,"startup.json"),nonce=Guid.NewGuid().ToString("N");
            checkCancelled();var activation=DashboardPackages.Activate(plan.Root,plan.Staged,plan.ExpectedGeneration);
            DashboardStartedProcess started=null;
            try
            {
                status?.Invoke("Opening the updated Dashboard and restoring your work…");
                started=environment.StartDashboard(DashboardPackages.EntryPath(plan.Root,activation.Current),plan.CheckpointPath,acknowledgement,nonce);
                var clock=Stopwatch.StartNew();
                while(clock.Elapsed<timeout)
                {
                    checkCancelled();
                    if(!environment.IsRevitRunning(plan))throw new InvalidOperationException("The Revit session closed while the Dashboard was restarting.");
                    if(File.Exists(acknowledgement))
                    {
                        if((File.GetAttributes(acknowledgement)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(handoff)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Dashboard acknowledgements cannot be links.");
                        var receipt=DashboardPackages.ReadJson<DashboardStartupReceipt>(acknowledgement,32768);
                        if(receipt==null||receipt.Nonce!=nonce||DashboardPackages.ParseVersion(receipt.Version)!=DashboardPackages.ParseVersion(activation.Current.Version)||receipt.ProcessId!=started.ProcessId||receipt.StartedUtcTicks!=started.StartedUtcTicks)
                            throw new InvalidDataException("The Dashboard startup acknowledgement did not match this update.");
                        if(!environment.IsStartedDashboardRunning(started))throw new InvalidOperationException("The updated Dashboard closed before activation completed.");
                        status?.Invoke("Dashboard updated. Revit stayed open and your work was restored.");return activation.Current;
                    }
                    if(!environment.IsStartedDashboardRunning(started))throw new InvalidOperationException("The updated Dashboard could not start or restore the checkpoint.");
                    environment.Wait(delay,token);
                }
                throw new TimeoutException("The updated Dashboard did not confirm that your work was restored.");
            }
            catch
            {
                bool restored=DashboardPackages.Rollback(plan.Root,activation);
                bool canRestart=started==null||!environment.IsStartedDashboardRunning(started);
                if(!canRestart&&environment is IDashboardFailedStartupCloser closer)canRestart=closer.RequestCloseFailedStartup(started);
                if(restored&&activation.Previous!=null&&environment.IsRevitRunning(plan)&&canRestart)
                {
                    status?.Invoke("The previous Dashboard version was restored. Your recovery checkpoint was retained.");
                    try{environment.StartDashboard(DashboardPackages.EntryPath(plan.Root,activation.Previous),plan.CheckpointPath,null,null);}catch(Exception rollbackError)when(rollbackError is IOException||rollbackError is InvalidOperationException||rollbackError is System.ComponentModel.Win32Exception){}
                }
                throw;
            }
        }
    }
    public static class StartupAcknowledgement
    {
        public static void WriteFromEnvironment()
        {
            string path=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_STARTUP_ACK"),nonce=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_STARTUP_NONCE");
            if(string.IsNullOrEmpty(path))return;
            string root=Path.Combine(DashboardPaths.Root,"handoffs")+Path.DirectorySeparatorChar;
            if(nonce==null||!Regex.IsMatch(nonce,@"\A[0-9a-f]{32}\z")||!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!="startup.json")throw new InvalidDataException("Invalid Dashboard startup receipt path.");
            using(var self=Process.GetCurrentProcess())
            {
                string version=FileVersionInfo.GetVersionInfo(self.MainModule.FileName).FileVersion;
                DashboardPackages.WriteJson(path,new DashboardStartupReceipt{Nonce=nonce,Version=version,ProcessId=self.Id,StartedUtcTicks=self.StartTime.ToUniversalTime().Ticks});
            }
            Environment.SetEnvironmentVariable("DOKA_DASHBOARD_STARTUP_ACK",null);Environment.SetEnvironmentVariable("DOKA_DASHBOARD_STARTUP_NONCE",null);
        }
    }
}
