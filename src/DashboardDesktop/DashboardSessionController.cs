#if DASHBOARD_DESKTOP
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace EngineeringDashboard.DesktopHost
{
    // A separate, versioned recovery format: never writes the user's project file.
    public sealed class DashboardSessionSnapshot
    {
        public int SchemaVersion {get;set;}=1;
        public string Version {get;set;}
        public string Tool {get;set;}
        public CalculationRecord Beam {get;set;}
        public bool BeamSupportSelected {get;set;}
        public CalculationRecord Slab {get;set;}
        public Top50RestartState Top50 {get;set;}
        public BackproppingRestartState Backpropping {get;set;}
        public string SteelShape {get;set;}
        public double Width {get;set;}
        public double Height {get;set;}
        public bool Maximized {get;set;}
    }
    public static class DashboardSessionController
    {
        static LauncherWindow launcher;
        static string prepared;
        static bool capturing;
        static Window[] frozen=Array.Empty<Window>();
        public static bool IsUpdateRestart {get;private set;}
        internal static void Register(LauncherWindow owner){launcher=owner;prepared=null;IsUpdateRestart=false;}
        static bool WorkBusy=>capturing||(DesktopRuntime.Client?.IsBusy??false)||(launcher?.RestartBusy??false);
        public static bool HasBusyWork=>prepared!=null||WorkBusy;
        public static bool TryPrepareForUpdate(out string checkpointPath,out string error)
        {
            checkpointPath=null;error=null;
            if(Application.Current==null||!Application.Current.Dispatcher.CheckAccess()){error="Prepare the update from the Dashboard window.";return false;}
            if(HasBusyWork){error="Finish or cancel the current calculation or Revit request before updating.";return false;}
            var windows=Application.Current.Windows.Cast<Window>().Where(w=>w.IsEnabled).ToArray();
            try
            {
                launcher?.ValidateRestartEditors();
                capturing=true;
                // Snapshot capture can pump the browser dispatcher. Freeze editing until it is complete.
                foreach(var window in windows)window.IsEnabled=false;
                var state=launcher==null?new DashboardSessionSnapshot{Tool="updates",Version=Assembly.GetExecutingAssembly().GetName().Version.ToString()}:launcher.CaptureRestartState();
                var bytes=BridgeProtocol.Encode(state);
                Validate(BridgeProtocol.Decode<DashboardSessionSnapshot>(bytes));
                LauncherWindow.ValidateRestartState(state);
                Directory.CreateDirectory(DashboardPaths.CheckpointsDirectory);
                string path=Path.Combine(DashboardPaths.CheckpointsDirectory,Guid.NewGuid().ToString("N")+".json"),temporary=path+".tmp";
                try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}File.Move(temporary,path);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
                prepared=path;checkpointPath=path;frozen=windows;return true;
            }
            catch(Exception failure){prepared=null;error="The Dashboard could not preserve the current work: "+failure.Message;return false;}
            finally{if(checkpointPath==null)foreach(var window in windows)if(window.IsLoaded)window.IsEnabled=true;capturing=false;}
        }
        public static void BeginUpdateRestart(string checkpointPath)
        {
            if(prepared==null||!DashboardUpdatePlan.SamePath(prepared,checkpointPath)||!File.Exists(prepared)||WorkBusy)throw new InvalidOperationException("The Dashboard update needs a completed recovery checkpoint.");
            IsUpdateRestart=true;
        }
        public static void CancelUpdateRestart(){IsUpdateRestart=false;prepared=null;foreach(var window in frozen)if(window.IsLoaded)window.IsEnabled=true;frozen=Array.Empty<Window>();}
        public static void Restore(string checkpointPath)
        {
            if(Application.Current==null||!Application.Current.Dispatcher.CheckAccess())throw new InvalidOperationException("Restore the Dashboard on its UI thread.");
            var state=ReadCheckpoint(checkpointPath);
            if(state.Tool=="updates"){prepared=null;IsUpdateRestart=false;return;}
            if(launcher==null)throw new InvalidOperationException("Open the Dashboard before restoring a session.");
            launcher.RestoreRestartState(state);prepared=null;IsUpdateRestart=false;
        }
        internal static bool IsUpdatesOnlyCheckpoint(string checkpointPath)=>ReadCheckpoint(checkpointPath).Tool=="updates";
        static DashboardSessionSnapshot ReadCheckpoint(string checkpointPath)
        {
            string path=Path.GetFullPath(checkpointPath??""),root=Path.GetFullPath(DashboardPaths.CheckpointsDirectory);
            if(!DashboardUpdatePlan.SamePath(Path.GetDirectoryName(path),root)||!Regex.IsMatch(Path.GetFileName(path),@"\A[0-9a-f]{32}\.json\z")||!File.Exists(path)||new FileInfo(path).Length>BridgeProtocol.MaxFrameBytes
                ||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid Dashboard recovery checkpoint.");
            var state=BridgeProtocol.Decode<DashboardSessionSnapshot>(File.ReadAllBytes(path));Validate(state);LauncherWindow.ValidateRestartState(state);
            return state;
        }
        static void Validate(DashboardSessionSnapshot state)
        {
            Version version;
            if(state==null||state.SchemaVersion!=1||(!Version.TryParse(state.Version,out version)||!Regex.IsMatch(state.Version??"",@"\A\d+\.\d+\.\d+(?:\.\d+)?\z"))||!new[]{"launcher","beam","slab","slab-choice","top50","backpropping","steel","updates"}.Contains(state.Tool))throw new InvalidDataException("Unsupported Dashboard recovery format.");
            if(state.Tool!="updates"&&(!Finite(state.Width)||!Finite(state.Height)||state.Width<300||state.Height<300||state.Width>20000||state.Height>20000))throw new InvalidDataException("Invalid Dashboard window dimensions.");
            if(state.Tool=="steel"&&string.IsNullOrEmpty(state.SteelShape))throw new InvalidDataException("The Steel Shape recovery inputs are missing.");
        }
        static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
    }
}
#endif
