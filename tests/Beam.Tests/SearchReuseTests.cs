using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class SearchReuseTests
{
    static int checks;
    static void Check(bool condition,string message) {checks++;if(!condition)throw new Exception("Search reuse: "+message);}
    static long Counter(BeamSearchTrace trace,string name) => long.Parse(trace.Snapshot().Split('\n').Single(x=>x.StartsWith(name+": ")).Substring(name.Length+2));
    static TowerInput Common(bool reuse,bool custom=false) => new TowerInput {LengthMm=2450,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1800,2450},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=270,AllowCustomH20Lengths=custom,ReuseSearchWork=reuse,SearchByLegTier=false,PruneNetworkBounds=false,ReuseTransverseNumerics=false,LazySearchDiagnostics=false,Trace=new BeamSearchTrace("reuse regression")};
    static TransferNetworkInput Network(bool crossing=false) => new TransferNetworkInput {UpperSpacingMm=200,CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=350,DepthMm=250},new TransferBeamRow {WidthMm=300,DepthMm=200}},Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100},new TransferBayRow {WidthMm=1850,ThicknessMm=100}},CrossingBeams=crossing ? new[] {new TransferCrossingBeamRow {SupportMode=CrossBeamSupportMode.LegacySlabRows,BayNumber=1,PositionMm=1200,WidthMm=500,DepthMm=150}} : new TransferCrossingBeamRow[0]};
    static string Signature(TransferNetworkResult result) => string.Join("\n",result.Beams.Select(b=>b.StockSchedule+"\n"+b.Transfer.Schedule+"\n"+string.Join(";",b.PositionsMm)+"\n"+string.Join(";",b.Joined)))+"\n"+result.Summary;
    static double[] Demands(TransferNetworkResult result) => result.Beams.SelectMany(b=>b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers)).SelectMany(m=>new[] {m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization,m.Analysis.TotalLoadKn}.Concat(m.Analysis.Reactions)).ToArray();
    static void Compare(bool custom,bool crossing)
    {
        var plain=Common(false,custom);var cached=Common(true,custom);var network=Network(crossing);
        // Exercise reuse separately from preference pruning, which can skip duplicates.
        plain.PruneUneconomicCandidates=false;cached.PruneUneconomicCandidates=false;
        var a=TransferNetworkOptimizer.Optimize(plain,network);var b=TransferNetworkOptimizer.Optimize(cached,network);
        Check(Signature(a)==Signature(b),"selected layouts, member demands, quantities and movement match without reuse");
        var before=Demands(a);var after=Demands(b);
        Check(before.Length==after.Length && before.Zip(after,(x,y)=>Math.Abs(x-y)<=1e-12*Math.Max(1,Math.Abs(x))).All(x=>x),"unrounded member demands and reactions match");
        Check(a.Tested==b.Tested,"same bounded candidates and ranking traversal");
        Check(Counter(cached.Trace,"Tower geometry cache hits")>0,"geometry reused across receiving beams");
        Check(Counter(cached.Trace,"Lower row-case cache hits")>0,"checked row cases reused across tower layouts");
        // Symmetric feet can remove duplicate responses within one beam solve.
        // The pass-wide cache still reuses exact responses across receiving beams.
        long primaryHits=Counter(cached.Trace,"Primary member cache hits")+Counter(cached.Trace,"Network primary response cache hits");
        Check(primaryHits>0 && Counter(cached.Trace,"Primary member analyses")<Counter(plain.Trace,"Primary member analyses"),"identical complete primary member checks reused within or between receiving-beam solves");
        Check(Counter(cached.Trace,"Primary matrix cache hits")>0,"new primary load vectors reuse checked structural preparation");
        Check(Counter(cached.Trace,"Lower response cache hits")>0,"identical exact row tributaries reuse transverse responses");
        Check(Counter(cached.Trace,"Numerical checks")<Counter(plain.Trace,"Numerical checks"),"repeated numerical checks reduced");
        Check(b.Beams.All(x=>x.Utilization<=1+1e-8 && x.MaxLegKn<=cached.LegCapacityKn+1e-8),"engineering limits retained");
        // Reusing the caller's input for another invocation must create a fresh pass cache.
        network.Beams[1].DepthMm=275;
        Check(Signature(TransferNetworkOptimizer.Optimize(cached,network))==Signature(TransferNetworkOptimizer.Optimize(plain,network)),"changed depth cannot reuse stale calculations");
    }
    public static void Run()
    {
        BandedAnalysisTests.Run();
        CompareLazyFailures();
        CheckTransverseKeys();
        CompareNextSearch();
        CompareNetworkSpeed();
        Compare(false,false);Compare(true,false);Compare(true,true);
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})ComparePruning(custom,crossing);
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})CompareStaging(custom,crossing);
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})CompareWorkers(custom,crossing);
        foreach(bool reuse in new[] {false,true})
        {
            var input=Common(reuse);input.LegCapacityKn=.01;bool rejected=false;
            try {TransferNetworkOptimizer.Optimize(input,Network());}catch(TowerOptimizationException ex) {rejected=ex.Details.Contains("leg load");}
            Check(rejected,"overloaded arrangements still reject and retain failure diagnostics");
        }
        using(var source=new CancellationTokenSource())
        {
            var input=Common(true,true);input.Trace=new BeamSearchTrace("cancel regression",source.Token);source.Cancel();bool cancelled=false;
            try {TransferNetworkOptimizer.Optimize(input,Network());}catch(OperationCanceledException) {cancelled=true;}
            Check(cancelled,"cancellation remains observable with reuse enabled");
        }
        Console.WriteLine($"Passed {checks} search reuse checks.");
    }
    static void CompareLazyFailures()
    {
        foreach(bool custom in new[] {false,true})
        {
            var eager=Common(true,custom);eager.LegCapacityKn=.01;eager.AllowSeparateSlabFallback=false;
            var lazy=Common(true,custom);lazy.LegCapacityKn=.01;lazy.AllowSeparateSlabFallback=false;lazy.LazySearchDiagnostics=true;
            string a="",b="";
            try {TransferNetworkOptimizer.Optimize(eager,Network());}catch(TowerOptimizationException ex){a=ex.Details;}
            try {TransferNetworkOptimizer.Optimize(lazy,Network());}catch(TowerOptimizationException ex){b=ex.Details;}
            Check(a.Length>0 && a==b,"lazy locations preserve every rejected-check count, closest example and complete failure detail");
        }
    }
    static void CheckTransverseKeys()
    {
        var input=Common(true);input.ReuseTransverseNumerics=true;input.NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache();
        var supports=new[] {150.0,2300};var uniform=new[] {new BeamDistributedLoad {StartMm=400,EndMm=2000,KnPerM=10}};
        var points=new[] {new BeamPointLoad {PositionMm=1200,Kn=5}};
        var first=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,2450,supports,uniform,points,TowerOptimizer.PairEI/2,270);
        var cached=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,2450,supports,uniform,points,TowerOptimizer.PairEI/2,270);
        Check(ReferenceEquals(first,cached) && Counter(input.Trace,"Network transverse response cache hits")==1,"an exact full transverse response is reused");
        points[0].Kn+=1e-10;
        var changed=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,2450,supports,uniform,points,TowerOptimizer.PairEI/2,270);
        Check(!ReferenceEquals(first,changed),"forces are not rounded in transverse keys");
        var otherCriterion=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,2450,supports,uniform,points,TowerOptimizer.PairEI/2,350);
        Check(!ReferenceEquals(changed,otherCriterion) && changed.DeflectionUtilization!=otherCriterion.DeflectionUtilization,"the deflection criterion belongs to the response key");
        var pair=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,2450,supports,uniform,points,TowerOptimizer.PairEI,270);
        Check(!ReferenceEquals(changed,pair) && Math.Abs(pair.DeflectionMm*2-changed.DeflectionMm)<1e-10,"single and paired stiffness responses cannot share a cached result");
        uniform[0].KnPerM+=1e-10;
        var otherUniform=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,2450,supports,uniform,points,TowerOptimizer.PairEI/2,270);
        Check(!ReferenceEquals(changed,otherUniform),"distributed forces also retain exact key values");
    }
    static void CompareNextSearch()
    {
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})
        {
            var original=Common(true,custom);original.NetworkSearchWorkers=1;
            var network=Network(crossing);var reference=TransferNetworkOptimizer.Optimize(original,network);
            foreach(int variant in new[] {1,2,4,8,15})foreach(int workers in new[] {1,4})
            {
                var next=Common(true,custom);next.NetworkSearchWorkers=workers;
                next.SearchByLegTier=(variant&1)!=0;next.PruneNetworkBounds=(variant&2)!=0;next.ReuseTransverseNumerics=(variant&4)!=0;next.LazySearchDiagnostics=(variant&8)!=0;
                var result=TransferNetworkOptimizer.Optimize(next,network);
                Check(Signature(reference)==Signature(result) && Demands(reference).SequenceEqual(Demands(result)),"leg tiers, exact bounds and transverse reuse/lazy diagnostics individually/combined preserve exact selected results with serial/parallel workers");
                Check(result.Tested<=reference.Tested,"exact bounds never add network transition solves");
                network.Beams[1].DepthMm=275;
                var edited=TransferNetworkOptimizer.Optimize(next,network);
                var editedReference=TransferNetworkOptimizer.Optimize(original,network);
                Check(Signature(edited)==Signature(editedReference) && Demands(edited).SequenceEqual(Demands(editedReference)),"transverse response reuse cannot cross an edited-input pass");
                network.Beams[1].DepthMm=250;
            }
        }
        var capped=Common(true);capped.NetworkLegCeiling=2;
        capped.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=300,BeamLineLoadKnM=1.5,UpperSpacingMm=200};
        bool failed=false;try {TransferredSlabOptimizer.Optimize(capped);}catch(TowerOptimizationException){failed=true;}
        Check(failed,"a constrained solve cannot return a layout above its leg ceiling");
        capped.NetworkLegCeiling=int.MaxValue;
        Check(TransferredSlabOptimizer.Optimize(capped).LegCount>=4,"an earlier constrained failure does not poison a fresh unconstrained search");
    }
    static void CompareNetworkSpeed()
    {
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})
        {
            var plain=Common(true,custom);plain.ReuseNetworkNumerics=false;plain.DeferTransferLayout=false;plain.NetworkSearchWorkers=1;
            var network=Network(crossing);var reference=TransferNetworkOptimizer.Optimize(plain,network);
            foreach(int variant in new[] {1,2,3})
            {
                var optimized=Common(true,custom);optimized.ReuseNetworkNumerics=(variant&1)!=0;optimized.DeferTransferLayout=(variant&2)!=0;optimized.NetworkSearchWorkers=1;
                var result=TransferNetworkOptimizer.Optimize(optimized,network);
                Check(Signature(reference)==Signature(result) && Demands(reference).SequenceEqual(Demands(result)),"each speed feature preserves exact schedules, quantities and demands");
                Check(reference.Tested==result.Tested,"speed features retain the complete candidate traversal");
                Check(plain.Diagnostics.Exception("comparison").Details==optimized.Diagnostics.Exception("comparison").Details,"wide cache retains beam-specific diagnostic examples and counts");
            }
        }
    }
    static void CompareWorkers(bool custom,bool crossing)
    {
        var serial=Common(true,custom);serial.NetworkSearchWorkers=1;
        var parallel=Common(true,custom);parallel.NetworkSearchWorkers=4;
        var network=Network(crossing);
        var a=TransferNetworkOptimizer.Optimize(serial,network);var b=TransferNetworkOptimizer.Optimize(parallel,network);
        Check(Signature(a)==Signature(b),"serial and parallel schedules, quantities and movement match");
        var before=Demands(a);var after=Demands(b);
        Check(before.Length==after.Length && before.Zip(after,(x,y)=>Math.Abs(x-y)<=1e-12*Math.Max(1,Math.Abs(x))).All(x=>x),"serial and parallel unrounded demands match");
        Check(a.Tested==b.Tested,"serial and parallel searches visit the same candidates");
        Check(serial.Diagnostics.Exception("comparison").Details==parallel.Diagnostics.Exception("comparison").Details,"parallel diagnostic merge preserves closest examples and counts");
        parallel.LegCapacityKn=.01;serial.LegCapacityKn=.01;
        string failureA="",failureB="";
        try {TransferNetworkOptimizer.Optimize(serial,network);}catch(TowerOptimizationException ex) {failureA=ex.Details;}
        try {TransferNetworkOptimizer.Optimize(parallel,network);}catch(TowerOptimizationException ex) {failureB=ex.Details;}
        Check(failureA.Length>0 && failureA==failureB,"parallel rejected search retains the serial failure report");
        if(!custom || !crossing)return;
        using(var source=new CancellationTokenSource())
        {
            var input=Common(true,true);input.NetworkSearchWorkers=4;input.Trace=new BeamSearchTrace("active parallel cancellation",source.Token);
            var cancel=Task.Run(()=> {SpinWait.SpinUntil(()=>Counter(input.Trace,"Receiving-beam solves")>0,1000);source.Cancel();});
            bool cancelled=false;
            try {TransferNetworkOptimizer.Optimize(input,Network(true));}catch(OperationCanceledException) {cancelled=true;}
            cancel.GetAwaiter().GetResult();Check(cancelled,"cancellation while workers are running stays an OperationCanceledException");
        }
    }
    static void ComparePruning(bool custom,bool crossing)
    {
        var plain=Common(true,custom);plain.PruneUneconomicCandidates=false;plain.NetworkSearchWorkers=1;
        var pruned=Common(true,custom);pruned.NetworkSearchWorkers=1;
        var network=Network(crossing);
        var a=TransferNetworkOptimizer.Optimize(plain,network);var b=TransferNetworkOptimizer.Optimize(pruned,network);
        Check(Signature(a)==Signature(b),"economy bound preserves exact layout and ranking");
        Check(Demands(a).SequenceEqual(Demands(b)),"economy bound preserves unrounded selected member demands");
        Check(a.Tested==b.Tested && Counter(pruned.Trace,"Candidates pruned by economy bounds")>0,"all ranked candidates still visited; unable-to-win candidates skip member solving");
    }
    static void CompareStaging(bool custom,bool crossing)
    {
        var full=Common(true,custom);full.StagePrimaryChecks=false;full.NetworkSearchWorkers=1;
        var staged=Common(true,custom);staged.NetworkSearchWorkers=1;
        var network=Network(crossing);var a=TransferNetworkOptimizer.Optimize(full,network);var b=TransferNetworkOptimizer.Optimize(staged,network);
        Check(Signature(a)==Signature(b) && Demands(a).SequenceEqual(Demands(b)),"staged rejection preserves selected layouts and all exact member demands");
        Check(b.Beams.SelectMany(x=>x.Transfer.LowerCases.Concat(x.Transfer.PrimaryCases).SelectMany(c=>c).Concat(x.Transfer.UpperMembers)).All(m=>m.Analysis.DeflectionEvaluated),"every selected member has complete deflection checks");
    }
    public static void Benchmark()
    {
        using(var source=new CancellationTokenSource(TimeSpan.FromMinutes(3)))
        {
            var input=new TowerInput {LengthMm=10600,LegCapacityKn=70,StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths),StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,Trace=new BeamSearchTrace("reported 2026-10-05 case",source.Token)};
            var network=new TransferNetworkInput {UpperSpacingMm=275,Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=2252,ThicknessMm=300},new TransferBayRow {WidthMm=2299,ThicknessMm=300}}};
            var timer=Stopwatch.StartNew();
            try
            {
                var result=TransferNetworkOptimizer.Optimize(input,network);
                Check(result.Beams.All(b=>b.Utilization<=1+1e-8 && b.MaxLegKn<=70+1e-8),"reported result meets member and leg limits");
                input.Trace.Complete("Succeeded",summary:result.Summary);
            }
            catch(TowerOptimizationException ex) {input.Trace.Complete("No valid bounded candidate",ex.Details);}
            catch(OperationCanceledException) {input.Trace.Complete("Cancelled at benchmark limit");Console.WriteLine(input.Trace.Snapshot());throw;}
            Console.WriteLine($"Reported case completed in {timer.Elapsed.TotalSeconds:0.000}s.\n"+input.Trace.Snapshot());
        }
    }
}
