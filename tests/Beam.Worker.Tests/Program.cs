using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using EngineeringDashboard;
internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==2 && args[0]=="--server")return Fake(args[1]);
            string worker=args[0],host=args.Length>1 && args[1]!="--full" ? args[1] : null;
            double integrated=0,directSeconds=0;
            using(var session=new BeamSolverSession(worker,host))
            {
                foreach(int area in args.Contains("--full") ? new[] {1,2,3,4,5} : new[] {5})
                {
                    var directInput=ReportedCombinedTests.Input(area);directInput.Trace=new BeamSearchTrace("Direct-runtime comparison");var baselineWatch=Stopwatch.StartNew();var direct=TransferNetworkOptimizer.Optimize(directInput,directInput.Network);directSeconds+=baselineWatch.Elapsed.TotalSeconds;
                    var input=ReportedCombinedTests.Input(area);input.Trace=new BeamSearchTrace("Worker transport regression");
                    var timer=Stopwatch.StartNew();var reply=session.Solve(new BeamSolverRequest {Main=input});
                    integrated+=timer.Elapsed.TotalSeconds;Console.WriteLine("WORKER area="+area+" seconds="+timer.Elapsed.TotalSeconds.ToString("F3"));
                    if(!ReferenceEquals(reply.Main,reply.Network.Beams[0]))throw new Exception("Lost main/network object identity");
                    for(int cross=0;cross<direct.CrossSupports.Length;cross++)for(int solid=0;solid<direct.CrossSupports[cross].Solids.Length;solid++)
                    {
                        var original=direct.CrossSupports[cross].Solids[solid].Member;if(original==null)continue;
                        int member=Array.IndexOf(direct.CrossSupports[cross].Members,original);
                        if(member>=0 && !ReferenceEquals(reply.Network.CrossSupports[cross].Solids[solid].Member,reply.Network.CrossSupports[cross].Members[member]))throw new Exception("Lost solid/member shared identity");
                    }
                    if(!input.Trace.Snapshot().Contains("complete result returned"))throw new Exception("Did not use actual worker: "+input.Trace.Snapshot());
                    ControlledPerformanceTests.Validate(input,reply.Network);
                    if(ControlledPerformanceTests.Fingerprint(direct)!=ControlledPerformanceTests.Fingerprint(reply.Network))throw new Exception("Selected network/demand fingerprint changed across process");
                    var a=direct.Beams.SelectMany(b=>b.Transfer.PrimaryCases.SelectMany(c=>c).Concat(b.Transfer.LowerCases.SelectMany(c=>c)).Concat(b.Transfer.UpperMembers)).Concat(direct.CrossSupports.SelectMany(c=>c.Members)).ToArray();
                    var b=reply.Network.Beams.SelectMany(l=>l.Transfer.PrimaryCases.SelectMany(c=>c).Concat(l.Transfer.LowerCases.SelectMany(c=>c)).Concat(l.Transfer.UpperMembers)).Concat(reply.Network.CrossSupports.SelectMany(c=>c.Members)).ToArray();
                    if(a.Length!=b.Length)throw new Exception("Lost result members");
                    for(int i=0;i<a.Length;i++)
                    {
                        if(a[i].Analysis.DeflectionEvaluated!=b[i].Analysis.DeflectionEvaluated || !a[i].Analysis.Reactions.SequenceEqual(b[i].Analysis.Reactions) || a[i].Utilization!=b[i].Utilization)throw new Exception("Changed exact member response");
                        foreach(double station in new[] {0.0,a[i].LengthMm/3,a[i].LengthMm})if(!a[i].Analysis.Response(station).SequenceEqual(b[i].Analysis.Response(station)))throw new Exception("Response/diagram coefficients did not round-trip");
                    }
                    Console.WriteLine("PASS area "+area+" complete geometry, loads, forces and "+a.Length+" member response/diagram round-trips");
                    ContractGuards(reply,input);
                }
            }
            Console.WriteLine("TOTAL integrated_seconds="+integrated.ToString("F3")+" direct_seconds="+directSeconds.ToString("F3")+" startup_ipc_results=included");
            Routes(worker,host);Modes(worker,host);Eligibility(worker,host);Cancellation(worker,host);Failures(host);StaleResults(host);
            // Missing worker is a deliberate, independently validated fallback.
            var simple=new TowerInput {LengthMm=3000,TotalLineLoad=10,LegCapacityKn=70,DeflectionDenominator=270,MaximumConcreteEndOverhangMm=150,Trace=new BeamSearchTrace("Missing worker fallback")};
            using(var missing=new BeamSolverSession(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe")))
            {
                var result=missing.Solve(new BeamSolverRequest {Main=simple});if(result.Main==null || !simple.Trace.Snapshot().Contains("Using .NET Framework fallback:"))throw new Exception("Missing worker fallback failed");
            }
            Console.WriteLine("PASS missing-worker fallback");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void MustReject(Action action)
    {
        try{action();}catch(InvalidDataException){return;}throw new Exception("Malformed worker result was accepted");
    }
    static void ContractGuards(BeamSolverReply reply,TowerInput input)
    {
        var request=new BeamSolverRequest {Main=input};var network=reply.Network;
        reply.Network=null;MustReject(()=>BeamSolverSession.Validate(reply,request));reply.Network=network;
        MustReject(()=>BeamSolverSession.Validate(reply,new BeamSolverRequest {Main=new TowerInput()}));
        var main=reply.Main;reply.Main=new TowerLayout();MustReject(()=>BeamSolverSession.Validate(reply,request));reply.Main=main;
        var transfer=main.Transfer;main.Transfer=null;MustReject(()=>BeamSolverSession.Validate(reply,request));main.Transfer=transfer;var primary=transfer.PrimaryCases[0];transfer.PrimaryCases[0]=null;MustReject(()=>BeamSolverSession.Validate(reply,request));transfer.PrimaryCases[0]=primary;
        var lower=transfer.LowerCases[0];transfer.LowerCases[0]=null;MustReject(()=>BeamSolverSession.Validate(reply,request));transfer.LowerCases[0]=lower;
        var analysis=primary[0].Analysis;var field=typeof(LoadedBeamAnalysis).GetField("elements",BindingFlags.Instance|BindingFlags.NonPublic);var elements=field.GetValue(analysis);
        field.SetValue(analysis,null);MustReject(()=>BeamSolverSession.Validate(reply,request));field.SetValue(analysis,elements);
        Console.WriteLine("PASS network mode/identity, nested null cases and missing private response data rejected");
    }
    static BeamCalculatorViewModel Model()=>new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,BeamLengthMm=3000,Depth=500,Width=600,FloorHeight=3000,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=150};
    static void Routes(string worker,string host)
    {
        using(var session=new BeamSolverSession(worker,host))
        {
            var model=Model();model.SharedSolverSession=session;model.OptimizeTowersAsync().GetAwaiter().GetResult();
            if(!model.IsCalculationValid || !model.BuildDiagnosticReport("test","").Contains("complete result returned"))throw new Exception("Normal calculation route did not use worker: "+model.WarningMessage);
            Func<double,TransferNetworkConfiguration> part=offset=>new TransferNetworkConfiguration {SupportFormatVersion=3,
                Beams=new[] {new TransferBeamRow {WidthMm=600,DepthMm=500,BottomElevationMm=-500,SpanLengthMm=3000}},Bays=Array.Empty<TransferBayRow>(),CrossingBeams=Array.Empty<TransferCrossingBeamRow>(),IndependentBeams=Array.Empty<IndependentBeamRow>(),
                RevitContext=new RevitImportContext {DocumentTitle="Detached combined worker fixture",OriginXmm=offset,DirectionX=1}};
            var outer=part(0);outer.SupportFormatVersion=4;outer.ActiveRevitDrawingKey="a";
            outer.RevitDrawings=new[] {new RevitDrawingDefinition {Key="a",Label="Area A",LengthMm=3000,ConfigurationXml=BeamCalculatorViewModel.SerializeNetwork(part(0))},new RevitDrawingDefinition {Key="b",Label="Area B",LengthMm=3000,ConfigurationXml=BeamCalculatorViewModel.SerializeNetwork(part(10000))}};
            var combined=Model();combined.NetworkConfigurationXml=BeamCalculatorViewModel.SerializeNetwork(outer);
            if(!combined.HasCombinedRevitDrawing)throw new Exception("Combined route fixture was not combined");combined.SharedSolverSession=session;combined.OptimizeTowersAsync().GetAwaiter().GetResult();
            if(!combined.IsCalculationValid || !combined.CombinedRevitDiagnostics().Contains("complete result returned"))throw new Exception("Combined calculation route did not use worker: "+combined.WarningMessage);
            Console.WriteLine("PASS normal and combined view-model calculation routes use actual worker");
        }
    }
    static TowerInput Simple()=>new TowerInput {LengthMm=3000,TotalLineLoad=10,LegCapacityKn=70,DeflectionDenominator=270,MaximumConcreteEndOverhangMm=150};
    static TowerInput Transfer()=>new TowerInput {LengthMm=2125,TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350,StockLengthsMm=new[] {1250.0},AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.Both,
        TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=2,UpperSpacingMm=250,RightBayMm=1800,RightAreaLoadKnM2=2}};
    static void EqualLayout(TowerLayout expected,TowerLayout actual)
    {
        if(expected.PrimaryMaterial!=actual.PrimaryMaterial || expected.Utilization!=actual.Utilization || !expected.PositionsMm.SequenceEqual(actual.PositionsMm) || !expected.LeftReactions.SequenceEqual(actual.LeftReactions) || !expected.RightReactions.SequenceEqual(actual.RightReactions)
            || !expected.StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(actual.StockPlan.Pieces.Select(p=>p.StockLengthMm)))throw new Exception("Focused material/mode result changed");
        if(expected.Transfer==null)return;
        var a=expected.Transfer.PrimaryCases.SelectMany(c=>c).Concat(expected.Transfer.LowerCases.SelectMany(c=>c)).Concat(expected.Transfer.UpperMembers).ToArray();
        var b=actual.Transfer.PrimaryCases.SelectMany(c=>c).Concat(actual.Transfer.LowerCases.SelectMany(c=>c)).Concat(actual.Transfer.UpperMembers).ToArray();
        if(a.Length!=b.Length)throw new Exception("Focused material/mode lost members");
        for(int i=0;i<a.Length;i++)if(!a[i].Analysis.Reactions.SequenceEqual(b[i].Analysis.Reactions) || !a[i].Analysis.Response(a[i].LengthMm/3).SequenceEqual(b[i].Analysis.Response(b[i].LengthMm/3)))throw new Exception("Focused material/mode response changed");
    }
    static void Modes(string worker,string host)
    {
        using(var session=new BeamSolverSession(worker,host))
        {
            foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
            {
                var input=Simple();input.PrimaryMaterial=material;var expected=TowerOptimizer.Optimize(input);input.Trace=new BeamSearchTrace("Material mode");
                var reply=session.Solve(new BeamSolverRequest {Main=input});EqualLayout(expected,reply.Main);
                if(!input.Trace.Snapshot().Contains("complete result returned"))throw new Exception("Material mode did not use worker");
            }
            var transfer=Transfer();var expectedTransfer=TowerOptimizer.Optimize(Transfer());transfer.Trace=new BeamSearchTrace("Transferred and independent mode");
            var independent=Transfer();independent.Trace=transfer.Trace;
            var combined=session.Solve(new BeamSolverRequest {Main=transfer,Independent=new[] {independent}});EqualLayout(expectedTransfer,combined.Main);EqualLayout(expectedTransfer,combined.Independent.Single());
            if(!combined.Main.StockPlan.Pieces.Any(p=>p.IsCustomLength) || !combined.Main.Transfer.LowerCustom)throw new Exception("Custom H20 project permission was lost");
            var catalogue=Transfer();catalogue.AllowCustomH20Lengths=false;catalogue.Trace=new BeamSearchTrace("Catalogue-only rejection");
            try{session.Solve(new BeamSolverRequest {Main=catalogue});throw new Exception("Catalogue-only request generated a custom result");}catch(ArgumentException){}
            if(catalogue.Trace.Snapshot().Contains("Using .NET Framework fallback:"))throw new Exception("Known catalogue input rejection unnecessarily repeated search");
        }
        Console.WriteLine("PASS H20/WS10, transferred single + independent results and custom/catalog project permissions");
    }
    static void Eligibility(string worker,string host)
    {
        foreach(string mode in new[] {"callback","timed","prepared"})using(var session=new BeamSolverSession(worker,host))
        {
            var main=Simple();main.Trace=new BeamSearchTrace("Specialized independent input");var independent=Transfer();independent.Trace=main.Trace;
            if(mode=="callback")independent.TransferLayoutAcceptance=layout=>true;
            if(mode=="timed")independent.Trace=new BeamSearchTrace("Timed independent input",searchTimeLimit:TimeSpan.FromSeconds(30));
            if(mode=="prepared")independent.TransferredSlab.SharedSearchWork=new TransferredSlabOptimizer.SearchWorkCache();
            var result=session.Solve(new BeamSolverRequest {Main=main,Independent=new[] {independent}});
            if(result.Independent.Length!=1 || session.OwnedProcessId!=0 || !main.Trace.Snapshot().Contains("Using .NET Framework fallback:"))throw new Exception("Specialized independent input was sent to worker: "+mode);
        }
        Console.WriteLine("PASS independent callbacks, time budgets and prepared-state retain local execution");
    }
    static void StaleResults(string host)
    {
        Environment.SetEnvironmentVariable("DOKA_BEAM_FAKE_MODE","slow-result");
        try
        {
            foreach(bool cancel in new[] {false,true})using(var session=new BeamSolverSession(typeof(Program).Assembly.Location,host))
            {
                var model=Model();model.SharedSolverSession=session;var task=model.OptimizeTowersAsync();
                if(!SpinWait.SpinUntil(()=>session.IsReady,10000))throw new Exception("Delayed worker did not start");Thread.Sleep(100);
                if(cancel)model.CancelOptimization();else model.Width+=10;
                task.GetAwaiter().GetResult();
                if(model.IsCalculationValid || model.TowerResult!=null || model.NetworkResult!=null || model.IndependentBeamResults.Length!=0)throw new Exception("Stale/cancelled worker result was applied");
                if(!model.BuildDiagnosticReport().Contains(cancel ? "Cancelled" : "Discarded: inputs changed during search"))throw new Exception("Stale/cancel outcome was not diagnosed");
            }
        }
        finally{Environment.SetEnvironmentVariable("DOKA_BEAM_FAKE_MODE",null);}
        Console.WriteLine("PASS view-model input revision and cancellation guards discard worker results");
    }
    static void Cancellation(string worker,string host)
    {
        using(var session=new BeamSolverSession(worker,host))using(var cancel=new CancellationTokenSource())
        {
            var input=ReportedCombinedTests.Input(1);input.Trace=new BeamSearchTrace("Cancellation regression",cancel.Token);
            var task=Task.Run(()=>session.Solve(new BeamSolverRequest {Main=input}));
            if(!SpinWait.SpinUntil(()=>session.IsReady,15000))throw new Exception("Worker did not become ready for cancellation test");
            Thread.Sleep(150);int pid=session.OwnedProcessId;var timer=Stopwatch.StartNew();cancel.Cancel();
            try{task.GetAwaiter().GetResult();throw new Exception("Cancellation returned a successful result");}catch(OperationCanceledException){}
            if(timer.Elapsed>TimeSpan.FromSeconds(3) || input.Trace.Snapshot().Contains("Using .NET Framework fallback:"))throw new Exception("Cancellation entered fallback or did not stop promptly");
            try{using(var p=Process.GetProcessById(pid)){if(!p.HasExited)throw new Exception("Owned worker survived cancellation");}}catch(ArgumentException){}
            Console.WriteLine("PASS active worker cancellation stops owned process without fallback");
        }
        using(var session=new BeamSolverSession(worker,host))using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();var input=ReportedCombinedTests.Input(5);input.Trace=new BeamSearchTrace("Pre-cancel",cancel.Token);
            try{session.Solve(new BeamSolverRequest {Main=input});throw new Exception("Pre-cancel did not stop");}catch(OperationCanceledException){}
            if(session.OwnedProcessId!=0)throw new Exception("Pre-cancel started worker");
        }
    }
    static void Failures(string host)
    {
        string own=typeof(Program).Assembly.Location;
        foreach(string mode in new[] {"identity","crash","corrupt","partial","stopped"})
        {
            Environment.SetEnvironmentVariable("DOKA_BEAM_FAKE_MODE",mode);
            try
            {
                var input=new TowerInput {LengthMm=3000,TotalLineLoad=10,LegCapacityKn=70,DeflectionDenominator=270,MaximumConcreteEndOverhangMm=150,Trace=new BeamSearchTrace("Failure regression")};
                using(var fake=new BeamSolverSession(own,host))
                {var result=fake.Solve(new BeamSolverRequest {Main=input});if(result.Main==null || !input.Trace.Snapshot().Contains("Using .NET Framework fallback:"))throw new Exception("Worker failure did not fall back: "+mode);}
            }
            finally{Environment.SetEnvironmentVariable("DOKA_BEAM_FAKE_MODE",null);}
        }
        Console.WriteLine("PASS incompatible, crashed, malformed, partial and unexpectedly stopped workers fall back with diagnostics");
        Environment.SetEnvironmentVariable("DOKA_BEAM_FAKE_MODE","slow-start");
        try
        {
            using(var fake=new BeamSolverSession(own,host))using(var cancel=new CancellationTokenSource())
            {
                var input=new TowerInput {Trace=new BeamSearchTrace("Startup cancellation",cancel.Token)};
                var task=Task.Run(()=>fake.Solve(new BeamSolverRequest {Main=input}));
                if(!SpinWait.SpinUntil(()=>fake.OwnedProcessId!=0,5000))throw new Exception("Slow-start child did not launch");int pid=fake.OwnedProcessId;cancel.Cancel();
                try{task.GetAwaiter().GetResult();throw new Exception("Startup cancellation returned result");}catch(OperationCanceledException){}
                if(input.Trace.Snapshot().Contains("Using .NET Framework fallback:"))throw new Exception("Startup cancellation entered fallback");
                try{using(var child=Process.GetProcessById(pid)){if(!child.HasExited)throw new Exception("Startup-cancelled child survived");}}catch(ArgumentException){}
            }
        }
        finally{Environment.SetEnvironmentVariable("DOKA_BEAM_FAKE_MODE",null);}
        Console.WriteLine("PASS cancellation during worker startup cleans up owned child without fallback");
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"roundtrip.bin");BeamWire.Write(path,new BeamSolverReply());var bytes=File.ReadAllBytes(path);File.WriteAllBytes(path,bytes.Take(bytes.Length-1).ToArray());
            try{BeamWire.Read<BeamSolverReply>(path);throw new Exception("Truncated response accepted");}catch(EndOfStreamException){}
            using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream))
            {
                Action<string> text=value=>{var data=System.Text.Encoding.UTF8.GetBytes(value);writer.Write(data.Length);writer.Write(data);};
                writer.Write(0x424D5731);writer.Write(BeamWire.Protocol);text(BeamWire.EngineIdentity);writer.Write((byte)9);writer.Write(0);
                text(string.Concat(Enumerable.Repeat("list(",80))+"System.Int32"+new string(')',80));
            }
            try{BeamWire.Read<BeamSolverReply>(path);throw new Exception("Nested wire type accepted");}catch(InvalidDataException){}
            foreach(string type in new[] {"System.Int32","System.String","EngineeringDashboard.PrimaryBeamMaterial"})
            {
                using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream))
                {
                    Action<string> text=value=>{var data=System.Text.Encoding.UTF8.GetBytes(value);writer.Write(data.Length);writer.Write(data);};
                    writer.Write(0x424D5731);writer.Write(BeamWire.Protocol);text(BeamWire.EngineIdentity);writer.Write((byte)9);writer.Write(0);text(type);
                }
                MustReject(()=>BeamWire.Read<object>(path));
            }
            Console.WriteLine("PASS truncated responses, excessive type nesting and object-tag primitive/string/enum rejected");
        }
        finally{Directory.Delete(root,true);}
    }
    static int Fake(string root)
    {
        string mode=Environment.GetEnvironmentVariable("DOKA_BEAM_FAKE_MODE");
        if(mode=="slow-start")Thread.Sleep(10000);
        File.WriteAllText(Path.Combine(root,"ready.txt"),mode=="identity" ? "incompatible" : "beam-worker/"+BeamWire.Protocol+"/"+BeamWire.EngineIdentity);
        string id=Console.ReadLine();if(id==null)return 0;if(mode=="crash")return 17;
        if(mode=="partial")
        {
            BeamWire.Write(Path.Combine(root,id,"response.bin"),new BeamSolverReply {Main=new TowerLayout(),Counters=new long[Enum.GetValues(typeof(BeamSearchCounter)).Length]});Console.ReadLine();
        }
        if(mode=="corrupt"){File.WriteAllBytes(Path.Combine(root,id,"response.bin"),new byte[] {1,2,3});Console.ReadLine();}
        if(mode=="stopped"){BeamWire.Write(Path.Combine(root,id,"response.bin"),new BeamSolverReply {ErrorKind="cancelled",Error="Unexpected worker interruption"});Console.ReadLine();}
        if(mode=="slow-result")
        {
            var request=BeamWire.Read<BeamSolverRequest>(Path.Combine(root,id,"request.bin"));var reply=BeamSolverSession.SolveLocally(request);reply.Counters=new long[Enum.GetValues(typeof(BeamSearchCounter)).Length];
            Thread.Sleep(500);string temporary=Path.Combine(root,id,"response.tmp");BeamWire.Write(temporary,reply);File.Move(temporary,Path.Combine(root,id,"response.bin"));Console.ReadLine();
        }
        return 0;
    }

}
