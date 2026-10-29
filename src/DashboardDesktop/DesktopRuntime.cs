#if DASHBOARD_DESKTOP
using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard.DesktopHost
{
    internal static class DesktopRuntime
    {
        internal static RevitBridgeClient Client {get;private set;}
        internal static void Configure(RevitBridgeClient client)
        {
            Client=client??throw new ArgumentNullException(nameof(client));
            MyRevitAddin.Access.AccessWindow.ExternalRequire=RequireAccess;
            MyRevitAddin.Access.AccessWindow.ExternalManage=ManageAccess;
            DashboardUpdateEntry.Configure(client);
        }
        internal static T Wait<T>(Task<T> task)
        {
            if(task==null)throw new ArgumentNullException(nameof(task));
            if(!task.IsCompleted)
            {
                var dispatcher=Dispatcher.CurrentDispatcher;
                var frame=new DispatcherFrame();
                task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            return task.GetAwaiter().GetResult();
        }
        internal static void Wait(Task task)=>Wait(Complete(task));
        static async Task<bool> Complete(Task task){await task;return true;}
        static bool RequireAccess(Window owner)
        {
            bool enabled=owner?.IsEnabled??false;
            try
            {
                if(owner!=null)owner.IsEnabled=false;
                return Wait(Client.CallAsync<object,bool>("access.ensure",null));
            }
            catch(Exception error)
            {
                if(owner!=null)MessageBox.Show(owner,error.Message,"Dashboard access",MessageBoxButton.OK,MessageBoxImage.Information);
                else MessageBox.Show(error.Message,"Dashboard access",MessageBoxButton.OK,MessageBoxImage.Information);
                return false;
            }
            finally{if(owner!=null)owner.IsEnabled=enabled;}
        }
        static void ManageAccess(Window owner)
        {
            try{Wait(Client.CallAsync<object,bool>("access.manage",null));}
            catch(Exception error){MessageBox.Show(owner,error.Message,"Dashboard access",MessageBoxButton.OK,MessageBoxImage.Information);}
        }
        internal static Task<RevitSelectionSnapshot> ReadBeamSelectionAsync()=>Client.CallAsync<object,RevitSelectionSnapshot>("beam.read",null);
        internal static async Task<RevitWallSnapshot> ReadTop50SelectionAsync(Func<bool> alive)
        {RequireOpen(alive);var result=await Client.CallAsync<object,RevitWallSnapshot>("top50.read",null);RequireOpen(alive);return result;}
        internal static async Task<RevitWallSnapshot> RefreshTop50Async(RevitWallRequest request,Func<bool> alive)
        {RequireOpen(alive);var result=await Client.CallAsync<RevitWallRequest,RevitWallSnapshot>("top50.refresh",request);RequireOpen(alive);return result;}
        internal static async Task<RevitHighlightResult> HighlightTop50Async(RevitWallRequest request,Func<bool> alive)
        {RequireOpen(alive);var result=await Client.CallAsync<RevitWallRequest,RevitHighlightResult>("top50.highlight",request);RequireOpen(alive);return result;}
        static void RequireOpen(Func<bool> alive){if(alive==null||!alive())throw new ObjectDisposedException("Top50 Planner");}
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        internal static async Task ApplyHelpersAsync(Window owner,HelperApplyRequest request)
        {
            if(owner==null||!owner.IsLoaded)throw new ObjectDisposedException("Calculator");
            using(var cancelled=new CancellationTokenSource())
            {
                EventHandler closed=(sender,args)=>cancelled.Cancel();owner.Closed+=closed;
                try
                {
                    var context=await Client.CallAsync<object,ActiveDocumentInfo>("context",null,cancelled.Token);
                    cancelled.Token.ThrowIfCancellationRequested();if(!owner.IsLoaded)throw new ObjectDisposedException("Calculator");
                    request.DocumentKey=context?.DocumentKey;request.Validate();
                    bool visible=owner.IsVisible;
                    // The foreground desktop process explicitly permits its Revit
                    // session to foreground the native selection prompt.
                    AllowSetForegroundWindow(Client.HostProcessId);
                    if(visible)owner.Hide();
                    HelperApplyResult result;
                    try
                    {
                        cancelled.Token.ThrowIfCancellationRequested();if(!owner.IsLoaded)throw new ObjectDisposedException("Calculator");
                        result=await Client.CallAsync<HelperApplyRequest,HelperApplyResult>("helpers.apply",request,cancelled.Token);
                    }
                    finally{if(visible&&owner.IsLoaded){owner.Show();owner.Activate();}}
                    if(result!=null&&!result.Cancelled&&owner.IsLoaded)MessageBox.Show(owner,result.Message??("Updated: "+result.Updated+"\nSkipped: "+result.Skipped+"\nFailed: "+result.Failed),"Import summary",MessageBoxButton.OK,MessageBoxImage.Information);
                }
                finally{owner.Closed-=closed;}
            }
        }
    }
}
#endif
