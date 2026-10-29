using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using EngineeringDashboard.DesktopHost;
using EngineeringDashboard.Top50;
using MyRevitAddin.Access;

namespace EngineeringDashboard
{
    internal sealed class RevitBridgeController : IDisposable
    {
        readonly RevitRequestDispatcher dispatcher=new RevitRequestDispatcher();
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly object gate=new object();
        readonly string session=Guid.NewGuid().ToString("N"),token,pipeName,root=DashboardPaths.Root;
        readonly int hostPid;readonly long hostStarted;readonly IntPtr revitWindow;
        readonly List<StagedDashboard> permitted=new List<StagedDashboard>();
        NamedPipeServerStream listening,connected;Process launched;
        int peerPid;long peerStarted;bool disposed;
        readonly EventWaitHandle activate,updates;
        internal RevitBridgeController(IntPtr revitWindow)
        {
            this.revitWindow=revitWindow;
            using(var p=Process.GetCurrentProcess()){hostPid=p.Id;hostStarted=p.StartTime.ToUniversalTime().Ticks;}
            using(var random=RandomNumberGenerator.Create()){var bytes=new byte[32];random.GetBytes(bytes);token=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
            pipeName="DokaDashboard-"+hostPid+"-"+session;
            activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DokaDashboardActivate-"+session);
            updates=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DokaDashboardUpdates-"+session);
            Task.Run((Action)AcceptLoop);
        }
        internal void LaunchOrActivate(bool updateOnly)
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("Revit integration");
                if(Alive(peerPid,peerStarted)||ProcessAlive(launched))
                {(updateOnly?updates:activate).Set();return;}
                if(UpdateHelperOwnsRoot())throw new InvalidOperationException("The Dashboard update is still finishing. Wait for the updater completion message, then try again.");
                var active=DashboardPackages.ReadActive(root)??throw new InvalidOperationException("The Dashboard runtime is not installed. Run the new Dashboard bootstrap installation, then restart Revit once.");
                DashboardPackages.ValidateInstalled(root,active);permitted.Add(active);
                var start=new ProcessStartInfo(DashboardPackages.EntryPath(root,active)){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(DashboardPackages.EntryPath(root,active)),Arguments=updateOnly?"--updates":""};
                Set(start,"DOKA_DASHBOARD_ROOT",root);Set(start,"DOKA_DASHBOARD_SETTINGS_ROOT",Path.GetDirectoryName(typeof(RevitBridgeController).Assembly.Location));
                Set(start,"DOKA_DASHBOARD_BRIDGE_PIPE",pipeName);Set(start,"DOKA_DASHBOARD_BRIDGE_TOKEN",token);Set(start,"DOKA_DASHBOARD_BRIDGE_SESSION",session);
                Set(start,"DOKA_DASHBOARD_BRIDGE_HOST_PID",hostPid.ToString(System.Globalization.CultureInfo.InvariantCulture));Set(start,"DOKA_DASHBOARD_BRIDGE_HOST_START_TICKS",hostStarted.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Set(start,"DOKA_DASHBOARD_ACTIVATE_EVENT","Local\\DokaDashboardActivate-"+session);Set(start,"DOKA_DASHBOARD_UPDATE_EVENT","Local\\DokaDashboardUpdates-"+session);
                launched?.Dispose();launched=Process.Start(start)??throw new InvalidOperationException("Windows could not start the Dashboard.");
            }
        }
        static void Set(ProcessStartInfo start,string name,string value)=>start.EnvironmentVariables[name]=value;
        static bool Alive(int id,long ticks){try{using(var p=Process.GetProcessById(id))return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==ticks;}catch{return false;}}
        static bool ProcessAlive(Process p){try{return p!=null&&!p.HasExited;}catch{return false;}}
        NamedPipeServerStream NewPipe()
        {
            var user=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException("Windows user identity is unavailable.");
            var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.SetOwner(user);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(user,PipeAccessRights.FullControl,AccessControlType.Allow));
            return new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
        }
        void AcceptLoop()
        {
            while(!lifetime.IsCancellationRequested)
            {
                try
                {
                    using(var pipe=NewPipe())
                    {
                        lock(gate){if(disposed)return;listening=pipe;}
                        pipe.WaitForConnection();lifetime.Token.ThrowIfCancellationRequested();
                        uint id;if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out id))throw new IOException("Cannot verify the Dashboard process.");
                        long started;using(var p=Process.GetProcessById((int)id))
                        {started=p.StartTime.ToUniversalTime().Ticks;ValidatePeer(p.MainModule.FileName);}
                        lock(gate){if(disposed)throw new OperationCanceledException();connected=pipe;listening=null;}
                        Serve(pipe,(int)id,started);
                    }
                }
                catch(Exception error)when(error is IOException||error is ArgumentException||error is InvalidOperationException||error is UnauthorizedAccessException||error is System.ComponentModel.Win32Exception||error is OperationCanceledException||error is System.Runtime.Serialization.SerializationException)
                { /* A disconnected or incompatible peer never ends the Revit-session bridge. */ }
                finally{lock(gate){listening=null;connected=null;peerPid=0;peerStarted=0;}}
            }
        }
        void ValidatePeer(string executable)
        {
            lock(gate)
            {
                var active=DashboardPackages.ReadActive(root);if(active!=null&&!permitted.Any(p=>p.PackageId==active.PackageId))permitted.Add(active);
                var payload=permitted.FirstOrDefault(p=>string.Equals(Path.GetFullPath(Path.Combine(root,p.RelativeDirectory,DashboardPackages.EntryFile)),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase));
                if(payload==null)throw new InvalidOperationException("The connecting process is not an installed Dashboard runtime.");
                DashboardPackages.ValidateInstalled(root,payload);
            }
        }
        void Serve(NamedPipeServerStream pipe,int id,long started)
        {
            using(var disconnected=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                var writeGate=new object();int pending=0;var connectionToken=disconnected.Token;bool authenticated=false;
                try
                {
                    // Read remains active while an ExternalEvent is queued. EOF cancels
                    // the callback BEFORE it may touch a document or start a transaction.
                    while(!disconnected.IsCancellationRequested)
                    {
                        var request=BridgeProtocol.Decode<BridgeRequest>(BridgeProtocol.ReadFrame(pipe));BridgeProtocol.ValidateRequest(request,session,token);
                        if(!authenticated)
                        {
                            lock(gate)
                            {
                                if(disposed)throw new OperationCanceledException();RequirePeer(connectionToken,id,started);
                                // Remember the authenticated peer beyond this pipe. An RPC
                                // error must not make the ribbon spawn a duplicate live UI.
                                var current=Process.GetProcessById(id);
                                if(current.HasExited||current.StartTime.ToUniversalTime().Ticks!=started){current.Dispose();throw new OperationCanceledException();}
                                launched?.Dispose();launched=current;peerPid=id;peerStarted=started;authenticated=true;
                            }
                        }
                        if(Interlocked.CompareExchange(ref pending,1,0)!=0)throw new IOException("Only one Revit request may be outstanding.");
                        Task.Run(async()=>
                        {
                            var reply=new BridgeReply{RequestId=request.RequestId,SessionId=session};
                            try
                            {
                                RequirePeer(connectionToken,id,started);
                                if(request.Operation=="hello")reply.Payload=Hello();
                                else if(request.Operation=="access.ensure"&&AccessWindow.IsSessionApproved)reply.Payload=BridgeProtocol.Json(true);
                                else reply.Payload=await dispatcher.Request(app=>
                                {
                                    RequirePeer(connectionToken,id,started);
                                    string value=Execute(app,request,connectionToken,()=>RequirePeer(connectionToken,id,started));
                                    RequirePeer(connectionToken,id,started);return value;
                                }).ConfigureAwait(false);
                            }
                            catch(Exception error){reply.Error=error is OperationCanceledException?"The Dashboard request was cancelled.":error.Message;}
                            finally{Interlocked.Exchange(ref pending,0);}
                            try{lock(writeGate){if(!disconnected.IsCancellationRequested)BridgeProtocol.WriteFrame(pipe,BridgeProtocol.Encode(reply));}}
                            catch(IOException){disconnected.Cancel();try{pipe.Dispose();}catch(ObjectDisposedException){}}
                            catch(ObjectDisposedException){}
                        });
                    }
                }
                finally{disconnected.Cancel();}
            }
        }
        bool UpdateHelperOwnsRoot()
        {
            string key=Path.GetFullPath(root).ToLowerInvariant();using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-","");
            Mutex mutex;if(!Mutex.TryOpenExisting("Local\\DokaDashboardUpdate-"+key,out mutex))return false;
            using(mutex)
            {
                bool acquired=false;try{try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}return !acquired;}
                finally{if(acquired)mutex.ReleaseMutex();}
            }
        }
        string Hello()=>BridgeProtocol.Json(new BridgeHello{ProtocolVersion=BridgeProtocol.Version,BridgeVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(RevitBridgeController).Assembly.Location).FileVersion,Authorized=AccessWindow.IsSessionApproved});
        static void RequirePeer(CancellationToken cancellation,int id,long started)
        {cancellation.ThrowIfCancellationRequested();if(!Alive(id,started))throw new OperationCanceledException("The initiating Dashboard has closed.");}
        string Execute(UIApplication app,BridgeRequest request,CancellationToken cancellation,Action guard)
        {
            if(request.Operation=="hello")return Hello();
            if(request.Operation=="updates.integration"){IntegrationUpdateWindow.Open(revitWindow);return BridgeProtocol.Json(true);}
            if(request.Operation=="access.manage"){AccessWindow.Manage(null);guard();return BridgeProtocol.Json(AccessWindow.IsSessionApproved);}
            bool authorized=AccessWindow.Require();guard();if(request.Operation=="access.ensure")return BridgeProtocol.Json(authorized);
            if(!authorized)throw new InvalidOperationException("Sign in and obtain access approval before using this tool.");
            switch(request.Operation)
            {
                case "context":var doc=app.ActiveUIDocument?.Document??throw new InvalidOperationException("Open a Revit project first.");return BridgeProtocol.Json(new ActiveDocumentInfo{DocumentKey=Top50RevitSelection.DocumentKey(doc),Title=doc.Title});
                case "beam.read":return BridgeProtocol.Json(RevitStructureSelection.Read(app));
                case "top50.read":return BridgeProtocol.Json(Top50RevitSelection.ReadSelected(app));
                case "top50.refresh":return BridgeProtocol.Json(Top50RevitSelection.Refresh(app,WallRequest(request.Payload)));
                case "top50.highlight":return BridgeProtocol.Json(Top50RevitSelection.Highlight(app,WallRequest(request.Payload)));
                case "helpers.apply":var apply=BridgeProtocol.FromJson<HelperApplyRequest>(request.Payload);apply?.Validate();if(apply==null)throw new InvalidDataException("Missing helper values.");SetForegroundWindow(revitWindow);return BridgeProtocol.Json(RevitHelperApply.Apply(app,apply,guard));
                default:throw new InvalidDataException("Unsupported Revit operation.");
            }
        }
        static RevitWallRequest WallRequest(string payload)
        {
            var value=BridgeProtocol.FromJson<RevitWallRequest>(payload);
            if(value==null||string.IsNullOrWhiteSpace(value.DocumentKey)||value.UniqueIds==null||value.UniqueIds.Length>100000||value.UniqueIds.Any(id=>string.IsNullOrWhiteSpace(id)||id.Length>256))throw new InvalidDataException("Invalid original wall references.");return value;
        }
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;lifetime.Cancel();listening?.Dispose();connected?.Dispose();launched?.Dispose();activate.Dispose();updates.Dispose();}
            // OnShutdown is a Revit API context. Closing a Dashboard never gets here.
            dispatcher.Dispose();
        }
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint processId);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    }
}
