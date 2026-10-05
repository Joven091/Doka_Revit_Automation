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
    static TowerInput Common(bool reuse,bool custom=false) => new TowerInput {LengthMm=2450,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1800,2450},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=270,AllowCustomH20Lengths=custom,ReuseSearchWork=reuse,Trace=new BeamSearchTrace("reuse regression")};
    static TransferNetworkInput Network(bool crossing=false) => new TransferNetworkInput {UpperSpacingMm=200,CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=350,DepthMm=250},new TransferBeamRow {WidthMm=300,DepthMm=200}},Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100},new TransferBayRow {WidthMm=1850,ThicknessMm=100}},CrossingBeams=crossing ? new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1200,WidthMm=500,DepthMm=150}} : new TransferCrossingBeamRow[0]};
    static string Signature(TransferNetworkResult result) => string.Join("\n",result.Beams.Select(b=>b.StockSchedule+"\n"+b.Transfer.Schedule+"\n"+string.Join(";",b.PositionsMm)+"\n"+string.Join(";",b.Joined)))+"\n"+result.Summary;
    static double[] Demands(TransferNetworkResult result) => result.Beams.SelectMany(b=>b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers)).SelectMany(m=>new[] {m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization,m.Analysis.TotalLoadKn}.Concat(m.Analysis.Reactions)).ToArray();
    static void Compare(bool custom,bool crossing)
    {
        var plain=Common(false,custom);var cached=Common(true,custom);var network=Network(crossing);
        var a=TransferNetworkOptimizer.Optimize(plain,network);var b=TransferNetworkOptimizer.Optimize(cached,network);
        Check(Signature(a)==Signature(b),"selected layouts, member demands, quantities and movement match without reuse");
        var before=Demands(a);var after=Demands(b);
        Check(before.Length==after.Length && before.Zip(after,(x,y)=>Math.Abs(x-y)<=1e-12*Math.Max(1,Math.Abs(x))).All(x=>x),"unrounded member demands and reactions match");
        Check(a.Tested==b.Tested,"same bounded candidates and ranking traversal");
        Check(Counter(cached.Trace,"Tower geometry cache hits")>0,"geometry reused across receiving beams");
        Check(Counter(cached.Trace,"Lower row-case cache hits")>0,"checked row cases reused across tower layouts");
        Check(Counter(cached.Trace,"Primary member cache hits")>0 && Counter(cached.Trace,"Primary member analyses")<Counter(plain.Trace,"Primary member analyses"),"identical complete primary member checks reused");
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
        Compare(false,false);Compare(true,false);Compare(true,true);
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})ComparePruning(custom,crossing);
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
