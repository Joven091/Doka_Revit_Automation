using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EngineeringDashboard.DesktopHost
{
    public sealed class RevitBridgeClient : IDisposable
    {
        readonly string pipeName,token,session;readonly SemaphoreSlim serial=new SemaphoreSlim(1,1);
        NamedPipeClientStream stream;bool disposed;int busy;
        public int HostProcessId {get;private set;}
        public long HostStartedUtcTicks {get;private set;}
        public bool IsBusy=>Volatile.Read(ref busy)>0;
        RevitBridgeClient(string pipe,string secret,string sessionId,int pid,long started){pipeName=pipe;token=secret;session=sessionId;HostProcessId=pid;HostStartedUtcTicks=started;}
        public static RevitBridgeClient FromEnvironment()
        {
            string pipe=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_BRIDGE_PIPE"),secret=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_BRIDGE_TOKEN"),session=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_BRIDGE_SESSION");
            int pid;long started;Guid id;
            if(string.IsNullOrEmpty(pipe)||!pipe.StartsWith("DokaDashboard-",StringComparison.Ordinal)||pipe.Length>100||secret==null||secret.Length!=64||!Guid.TryParseExact(session,"N",out id)
                ||!int.TryParse(Environment.GetEnvironmentVariable("DOKA_DASHBOARD_BRIDGE_HOST_PID"),out pid)||pid<=0||!long.TryParse(Environment.GetEnvironmentVariable("DOKA_DASHBOARD_BRIDGE_HOST_START_TICKS"),out started)||started<=0)
                throw new InvalidOperationException("Open the Engineering Dashboard from Revit to connect it to your project.");
            return new RevitBridgeClient(pipe,secret,session,pid,started);
        }
        public Task<BridgeHello> HelloAsync()=>CallAsync<object,BridgeHello>("hello",null);
        public async Task<TReply> CallAsync<TRequest,TReply>(string operation,TRequest request,CancellationToken cancellationToken=default(CancellationToken))
        {
            if(disposed)throw new ObjectDisposedException("Dashboard Revit connection");
            await serial.WaitAsync(cancellationToken).ConfigureAwait(false);Interlocked.Increment(ref busy);
            try
            {
                if(disposed)throw new ObjectDisposedException("Dashboard Revit connection");
                RequireHost();
                if(stream==null)
                {
                    stream=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
                    await stream.ConnectAsync(5000,cancellationToken).ConfigureAwait(false);
                    uint pid;if(!GetNamedPipeServerProcessId(stream.SafePipeHandle.DangerousGetHandle(),out pid)||pid!=(uint)HostProcessId)throw new InvalidDataException("The Dashboard pipe belongs to a different process.");
                }
                string id=Guid.NewGuid().ToString("N");var message=new BridgeRequest{RequestId=id,SessionId=session,Token=token,Operation=operation,Payload=BridgeProtocol.Json(request)};
                BridgeProtocol.ValidateRequest(message,session,token);
                using(cancellationToken.Register(()=>{try{stream?.Dispose();}catch(ObjectDisposedException){}}))
                {
                    var reply=await Task.Run(()=>{BridgeProtocol.WriteFrame(stream,BridgeProtocol.Encode(message));return BridgeProtocol.Decode<BridgeReply>(BridgeProtocol.ReadFrame(stream));},cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();RequireHost();BridgeProtocol.ValidateReply(reply,id,session);
                    if(!string.IsNullOrEmpty(reply.Error))throw new InvalidOperationException(reply.Error);
                    return BridgeProtocol.FromJson<TReply>(reply.Payload);
                }
            }
            catch {stream?.Dispose();stream=null;if(cancellationToken.IsCancellationRequested)throw new OperationCanceledException(cancellationToken);throw;}
            finally{Interlocked.Decrement(ref busy);serial.Release();}
        }
        void RequireHost()
        {try{using(var host=Process.GetProcessById(HostProcessId)){if(host.HasExited||host.StartTime.ToUniversalTime().Ticks!=HostStartedUtcTicks)throw new InvalidOperationException();}}catch(Exception e)when(e is ArgumentException||e is InvalidOperationException){throw new InvalidOperationException("The original Revit session has closed. Reopen the Dashboard from Revit.");}}
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint processId);
        public void Dispose(){disposed=true;stream?.Dispose();}
    }
}
