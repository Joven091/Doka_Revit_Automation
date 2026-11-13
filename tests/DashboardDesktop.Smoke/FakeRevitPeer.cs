using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard.DesktopHost;

// A local contract peer, not Autodesk Revit and not a Revit API stand-in.
internal sealed class FakeRevitPeer : IDisposable
{
    internal readonly string Pipe="DokaDashboard-qa-"+Guid.NewGuid().ToString("N");
    internal readonly string Token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
    internal readonly string Session=Guid.NewGuid().ToString("N");
    internal readonly ConcurrentQueue<BridgeRequest> Requests=new ConcurrentQueue<BridgeRequest>();
    internal readonly ManualResetEventSlim ContextEntered=new ManualResetEventSlim(false),ReleaseContext=new ManualResetEventSlim(true);
    readonly CancellationTokenSource stop=new CancellationTokenSource();
    readonly Task server;
    NamedPipeServerStream current;
    internal Exception Failure;
    internal FakeRevitPeer(){server=Task.Run(Serve);}
    internal void Configure(Action<string,string> set,string root)
    {
        string settings=Path.Combine(root,"settings"),feed=Path.Combine(root,"empty-local-feed");Directory.CreateDirectory(settings);Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(settings,"update-settings.json"),"{\"schemaVersion\":1,\"sourceDirectory\":"+TestSupport.Json.Serialize(feed)+"}");
        using(var own=Process.GetCurrentProcess())
        {
            set("DOKA_DASHBOARD_ROOT",root);set("DOKA_DASHBOARD_SETTINGS_ROOT",Path.Combine(root,"settings"));
            set("DOKA_DASHBOARD_BRIDGE_PIPE",Pipe);set("DOKA_DASHBOARD_BRIDGE_TOKEN",Token);set("DOKA_DASHBOARD_BRIDGE_SESSION",Session);
            set("DOKA_DASHBOARD_BRIDGE_HOST_PID",own.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            set("DOKA_DASHBOARD_BRIDGE_HOST_START_TICKS",own.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            set("DOKA_DASHBOARD_ACTIVATE_EVENT",null);set("DOKA_DASHBOARD_UPDATE_EVENT",null);set("DOKA_DASHBOARD_STARTUP_ACK",null);set("DOKA_DASHBOARD_STARTUP_NONCE",null);
        }
    }
    async Task Serve()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using(var pipe=new NamedPipeServerStream(Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
                {
                    current=pipe;await pipe.WaitForConnectionAsync(stop.Token);
                    while(!stop.IsCancellationRequested&&pipe.IsConnected)
                    {
                        var request=BridgeProtocol.Decode<BridgeRequest>(await Task.Run(()=>BridgeProtocol.ReadFrame(pipe)));
                        BridgeProtocol.ValidateRequest(request,Session,Token);Requests.Enqueue(request);
                        if(request.Operation=="context"){ContextEntered.Set();ReleaseContext.Wait(stop.Token);}
                        var response=new BridgeReply{RequestId=request.RequestId,SessionId=Session,Payload=Reply(request)};
                        BridgeProtocol.WriteFrame(pipe,BridgeProtocol.Encode(response));
                    }
                }
            }
            catch(Exception error)when(error is EndOfStreamException||error is IOException||error is ObjectDisposedException||error is OperationCanceledException){}
            catch(Exception error){Failure=error;return;}
            finally{current=null;}
        }
    }
    static string Reply(BridgeRequest request)
    {
        switch(request.Operation)
        {
            case "hello":return "{\"ProtocolVersion\":1,\"BridgeVersion\":\"QA-contract-peer\",\"Authorized\":true}";
            case "access.ensure":case "access.manage":return "true";
            case "context":return "{\"DocumentKey\":\"qa-project\",\"Title\":\"QA local contract fixture\"}";
            case "beam.read":return BeamSelection;
            case "top50.read":case "top50.refresh":return WallSelection;
            case "top50.highlight":return "{\"Requested\":2,\"Found\":2,\"Message\":\"QA local contract fixture\"}";
            case "helpers.apply":return "{\"Cancelled\":true,\"Updated\":0,\"Skipped\":0,\"Failed\":0}";
            case "updates.integration":return "true";
            default:throw new InvalidDataException("Unexpected contract operation: "+request.Operation);
        }
    }
    internal int Count(string operation)=>System.Linq.Enumerable.Count(Requests,r=>r.Operation==operation);
    public void Dispose(){stop.Cancel();ReleaseContext.Set();current?.Dispose();try{server.Wait(3000);}catch(AggregateException){}stop.Dispose();ContextEntered.Dispose();ReleaseContext.Dispose();}
    internal const string WallSelection="{\"DocumentKey\":\"qa-project\",\"DocumentTitle\":\"QA controlled walls\",\"DocumentPath\":\"\",\"RevitVersion\":\"QA peer\",\"ViewName\":\"QA fixture\",\"CapturedUtc\":\"2026-10-29T00:00:00Z\",\"SelectedIds\":[\"901\",\"902\"],\"Walls\":[{\"UniqueId\":\"qa-wall-901\",\"ElementId\":\"901\",\"Label\":\"QA first wall\",\"CentreLengthMm\":5900,\"ExteriorLengthMm\":6200,\"InteriorLengthMm\":5800,\"GeometryDetails\":\"QA controlled planar faces\"},{\"UniqueId\":\"qa-wall-902\",\"ElementId\":\"902\",\"Label\":\"QA second wall\",\"CentreLengthMm\":1000,\"ExteriorLengthMm\":1000,\"InteriorLengthMm\":1000,\"GeometryDetails\":\"QA controlled planar faces\"}],\"Notes\":[]}";
    // Dimensions are independently authored rectangular geometry in millimetres.
    internal const string BeamSelection="{\"DocumentTitle\":\"QA controlled beam bay\",\"RevitVersion\":\"QA peer\",\"RevitBuild\":\"\",\"ActiveView\":\"QA fixture\",\"ReadException\":\"\",\"SelectedCount\":4,\"SelectedIds\":[\"701\",\"702\",\"703\",\"704\"],\"Elements\":[],\"Beams\":[{\"SourceId\":\"701\",\"Label\":\"QA beam one\",\"Start\":{\"X\":0,\"Y\":0},\"End\":{\"X\":7000,\"Y\":0},\"WidthMm\":600,\"DepthMm\":1200,\"BottomMm\":8800},{\"SourceId\":\"702\",\"Label\":\"QA beam two\",\"Start\":{\"X\":0,\"Y\":2800},\"End\":{\"X\":7000,\"Y\":2800},\"WidthMm\":600,\"DepthMm\":1200,\"BottomMm\":8800},{\"SourceId\":\"703\",\"Label\":\"QA crossing\",\"Start\":{\"X\":3100,\"Y\":0},\"End\":{\"X\":3100,\"Y\":2800},\"WidthMm\":500,\"DepthMm\":900,\"BottomMm\":9100}],\"Slabs\":[{\"SourceId\":\"704\",\"Label\":\"QA slab\",\"Outline\":[{\"X\":-300,\"Y\":-300},{\"X\":7300,\"Y\":-300},{\"X\":7300,\"Y\":3100},{\"X\":-300,\"Y\":3100}],\"ThicknessMm\":250,\"TopMm\":10000}],\"Boundaries\":[],\"Problems\":[],\"Notes\":[]}";
}
