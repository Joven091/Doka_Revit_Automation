using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime;
using System.Threading;
using EngineeringDashboard;

internal static class NetworkSpeedBenchmark
{
    sealed class Trial
    {
        internal double Seconds;
        internal TransferNetworkResult Result;
    }
    static int checks;
    static void Check(bool condition,string message) {checks++;if(!condition)throw new Exception("Network speed benchmark: "+message);}
    static void Near(double actual,double expected,string message) => Check(Math.Abs(actual-expected)<=1e-6*Math.Max(1,Math.Abs(expected)),message);
    static TowerInput Common(string scenario,int variant,CancellationToken token) => new TowerInput
    {
        LengthMm=scenario=="warm" ? 4900 : 10600,LegCapacityKn=70,
        StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths),
        StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,
        DeflectionDenominator=270,AllowCustomH20Lengths=true,ReuseNetworkNumerics=true,DeferTransferLayout=true,
        RespectNetworkCatalogueLengths=variant!=0,SearchByLegTier=(variant&1)!=0,PruneNetworkBounds=(variant&2)!=0,ReuseTransverseNumerics=(variant&2)!=0 || variant==4,LazySearchDiagnostics=(variant&2)!=0 || variant==8,
        NetworkSearchWorkers=Math.Min(4,Environment.ProcessorCount),
        Trace=new BeamSearchTrace("network speed: "+scenario,token)
    };
    static TransferNetworkInput Network(string scenario)
    {
        if(scenario=="warm")return new TransferNetworkInput {UpperSpacingMm=275,
            Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1450},new TransferBeamRow {WidthMm=650,DepthMm=1150}},
            Bays=new[] {new TransferBayRow {WidthMm=2500,ThicknessMm=300}}};
        int count=scenario=="eight" ? 8 : 3;
        var widths=scenario=="eight" ? new[] {2677.0,1874,3276,2274,2278,2272,2252} : new[] {2252.0,2299};
        return new TransferNetworkInput {UpperSpacingMm=275,
            Beams=Enumerable.Range(0,count).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),
            Bays=widths.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray()};
    }
    static long Counter(BeamSearchTrace trace,string name) => long.Parse(trace.Snapshot().Split('\n').Single(x=>x.StartsWith(name+": ")).Substring(name.Length+2),CultureInfo.InvariantCulture);
    static TransferMember[] Members(TowerLayout beam) => beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers).ToArray();
    static void Same(TransferNetworkResult a,TransferNetworkResult b)
    {
        Check(a.LayoutDiagnostics==b.LayoutDiagnostics && a.Schedule==b.Schedule && a.Summary==b.Summary,"selected schedules, quantities, coverage and movement remain identical");
        for(int i=0;i<a.Beams.Length;i++)
        {
            var x=a.Beams[i];var y=b.Beams[i];
            Check(x.PositionsMm.SequenceEqual(y.PositionsMm) && x.Joined.SequenceEqual(y.Joined) && x.Transfer.RowsMm.SequenceEqual(y.Transfer.RowsMm) && x.Transfer.TributaryWidthsMm.SequenceEqual(y.Transfer.TributaryWidthsMm),"exact support/tower positions and tributaries remain identical");
            var first=Members(x);var second=Members(y);Check(first.Length==second.Length,"member counts match");
            for(int j=0;j<first.Length;j++)
            {
                var p=first[j];var q=second[j];var u=p.Analysis;var v=q.Analysis;
                Check(p.LengthMm==q.LengthMm && p.StartMm==q.StartMm && p.SupportsMm.SequenceEqual(q.SupportsMm),"member geometries match exactly");
                Check(u.TotalLoadKn==v.TotalLoadKn && u.MomentKnM==v.MomentKnM && u.ShearKn==v.ShearKn && u.DeflectionMm==v.DeflectionMm && u.DeflectionUtilization==v.DeflectionUtilization && u.Reactions.SequenceEqual(v.Reactions),"all unrounded member demands and reactions match exactly");
            }
        }
    }
    static void Engineering(TowerInput common,TransferNetworkInput network,TransferNetworkResult result,string scenario)
    {
        for(int i=0;i<result.Beams.Length;i++)
        {
            var beam=result.Beams[i];double coverage=common.LengthMm-2*beam.ConcreteEndOverhangMm;
            Check(beam.Transfer.RowsMm.SequenceEqual(result.Beams[0].Transfer.RowsMm),"shared slab rows align");
            Check(coverage>=result.SlabCoveredLengthMm-1e-7 && beam.ConcreteEndOverhangMm<=75,"existing coverage bounds respected");
            Near(beam.Transfer.TributaryWidthsMm.Sum(),coverage,"existing covered-length load model conserved");
            double beamPerM=network.AreaLoad(network.Beams[i].DepthMm)*network.Beams[i].WidthMm/1000;
            Near(beam.Transfer.BeamLoadKn,beamPerM*coverage/1000,"physical covered beam load conserved");
            double designTributary=beam.Transfer.TributaryWidthsMm.Sum(w=>Math.Max(w,network.UpperSpacingMm));
            Near(beam.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn),beamPerM*designTributary/1000,"lower beam rows retain the full-spacing design load");
            double slabPerM=0;
            if(i>0 && result.Bays[i-1]!=null)slabPerM+=network.AreaLoad(network.Bays[i-1].ThicknessMm)*network.Bays[i-1].WidthMm/2000;
            if(i<result.Bays.Length && result.Bays[i]!=null)slabPerM+=network.AreaLoad(network.Bays[i].ThicknessMm)*network.Bays[i].WidthMm/2000;
            Near(beam.Transfer.SlabLoadKn,slabPerM*result.SlabCoveredLengthMm/1000,"physical slab load conserved through symmetric feet and shutters");
            Near(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn),(beamPerM+slabPerM)*coverage/1000,"primary UDL retains beam and slab loads per metre over its complete stock");
            Check(beam.MaxLegKn<=70+1e-8 && beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7 && r<=70+1e-8),"actual leg limits and no uplift retained");
            foreach(var member in Members(beam))
            {
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"every selected member has full passing checks");
                Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"member force equilibrium");
            }
        }
        Near(result.SlabLoadKn,network.Bays.Sum(b=>network.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*result.SlabCoveredLengthMm/1000,"modelled slab load conserved");
        if(scenario=="eight")
        {
            Check(result.Beams.SelectMany(b=>b.Transfer.PrimaryCases.SelectMany(c=>c)).All(m=>m.Points.Length==0 && m.Uniform.Length>0 && m.Utilization<=1+1e-8),"eight-beam benchmark uses passing UDL primaries");
        }
    }
    static Trial Solve(string scenario,int variant,int repeat)
    {
        using(var source=new CancellationTokenSource(TimeSpan.FromMinutes(6)))
        {
            var common=Common(scenario,variant,source.Token);var network=Network(scenario);
            // Start both variants with clean managed heaps; collection time is excluded.
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            int gen0=GC.CollectionCount(0),gen1=GC.CollectionCount(1),gen2=GC.CollectionCount(2);
            var timer=Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,network);timer.Stop();
            Engineering(common,network,result,scenario);
            Console.WriteLine(FormattableString.Invariant($"SPEED scenario={scenario} variant={variant} repeat={repeat} workers={common.NetworkSearchWorkers} processors={Environment.ProcessorCount} seconds={timer.Elapsed.TotalSeconds:0.000} legs={result.LegCount} feet={result.UpperFootCount} timber={result.TimberMm:0.###} B1={result.Beams[0].LegCount} primary={Counter(common.Trace,"Primary member analyses")} shared={Counter(common.Trace,"Network primary response cache hits")} built={Counter(common.Trace,"Detailed transfer layouts built")} deferred={Counter(common.Trace,"Detailed transfer layouts deferred")} branches={Counter(common.Trace,"Network branches pruned by exact bounds")} transverseHits={Counter(common.Trace,"Network transverse response cache hits")} lower={Counter(common.Trace,"Lower secondary analyses")} candidates={Counter(common.Trace,"Transferred layout candidates")} solves={Counter(common.Trace,"Receiving-beam solves")} gc={GC.CollectionCount(0)-gen0}/{GC.CollectionCount(1)-gen1}/{GC.CollectionCount(2)-gen2}"));
            return new Trial {Seconds=timer.Elapsed.TotalSeconds,Result=result};
        }
    }
    static void Case(string scenario)
    {
        var baseline=new List<double>();var guided=new List<double>();TransferNetworkResult reference=null;
        // ABBA balances execution order. Each solve owns fresh per-pass caches.
        foreach(int variant in new[] {0,3,3,0})
        {
            var samples=variant==0 ? baseline : guided;var trial=Solve(scenario,variant,samples.Count+1);
            samples.Add(trial.Seconds);if(reference==null)reference=trial.Result;else Same(reference,trial.Result);
        }
        Console.WriteLine(FormattableString.Invariant($"SPEED SUMMARY scenario={scenario} baseline_mean={baseline.Average():0.000} guided_mean={guided.Average():0.000} reduction_percent={(baseline.Average()-guided.Average())/baseline.Average()*100:0.00} baseline_range={baseline.Min():0.000}-{baseline.Max():0.000} guided_range={guided.Min():0.000}-{guided.Max():0.000}"));
    }
    public static void Run()
    {
        Console.WriteLine($"SPEED runtime={Environment.Version} processors={Environment.ProcessorCount} serverGC={GCSettings.IsServerGC}; Lowest-leg-first traversal, exact network bounds and exact transverse response reuse; actual leg capacity remains 70 kN. Existing end-load model is unchanged.");
        NetworkFallbackTests.Run();
        var warm=Solve("warm",0,0);foreach(int variant in new[] {1,2,3,4,8})Same(warm.Result,Solve("warm",variant,0).Result);
        using(var source=new CancellationTokenSource())
        {
            source.Cancel();bool cancelled=false;
            try {TransferNetworkOptimizer.Optimize(Common("warm",3,source.Token),Network("warm"));}catch(OperationCanceledException) {cancelled=true;}
            Check(cancelled,"optimized search retains cancellation");
        }
        Case("three");Case("eight");
        Console.WriteLine($"SPEED Passed {checks} layout, exact-demand, load-conservation, capacity and cancellation checks.");
    }
}
