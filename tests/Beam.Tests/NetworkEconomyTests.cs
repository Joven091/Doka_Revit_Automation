using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class NetworkEconomyTests
{
    static void Check(bool value,string message) {if(!value)throw new Exception("Network economy: "+message);}
    static void Near(double actual,double expected,string message) => Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message);
    static TowerInput Common(bool independent) => new TowerInput {LengthMm=10600,LegCapacityKn=70,StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths),StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,IndependentNetworkPrimaryCoverage=independent};
    static TransferNetworkInput Network() => new TransferNetworkInput {UpperSpacingMm=275,Beams=Enumerable.Range(0,8).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),Bays=new[] {2677.0,1874,3276,2274,2278,2272,2252}.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray()};
    static TransferNetworkResult Solve(bool independent,int workers)
    {
        var common=Common(independent);common.NetworkSearchWorkers=workers;var network=Network();var timer=Stopwatch.StartNew();
        using(var source=new CancellationTokenSource(TimeSpan.FromMinutes(6)))
        {
            common.Trace=new BeamSearchTrace("eight-beam economy regression",source.Token);
            var result=TransferNetworkOptimizer.Optimize(common,network);
            Console.WriteLine($"ECONOMY independent={independent} workers={workers} processors={Environment.ProcessorCount} seconds={timer.Elapsed.TotalSeconds:0.000} legs={result.LegCount} feet={result.UpperFootCount} timber={result.TimberMm:0.###} coverage={result.SlabCoveredLengthMm} phase={result.RowPhaseMm} legsByBeam={string.Join(",",result.Beams.Select(b=>b.LegCount))}");
            foreach(var beam in result.Beams)
            {
                var index=Array.IndexOf(result.Beams,beam);double beamLength=common.LengthMm-2*beam.ConcreteEndOverhangMm;
                Check(beam.Transfer.SlabRowIndices.Select(i=>beam.Transfer.RowsMm[i]).SequenceEqual(result.SlabRowsMm),"shared slab rows align despite local beam rows");
                Check(beamLength>=result.SlabCoveredLengthMm-1e-7 && beam.ConcreteEndOverhangMm<=75,"primary supports shared coverage within end allowance");
                Near(beam.Transfer.TributaryWidthsMm.Sum(),common.LengthMm,"actual primary coverage reaches beam end tributaries");
                double beamLoad=network.AreaLoad(network.Beams[index].DepthMm)*network.Beams[index].WidthMm/1000*common.LengthMm/1000;
                Check(beam.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn)>=beamLoad-1e-7,"secondary design retains full tributaries at ends");
                Check(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)>=beamLoad+beam.Transfer.SlabLoadKn-1e-7,"primary UDL covers physical beam and slab load");
                Check(beam.MaxLegKn<=70+1e-8 && beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7 && r<=70+1e-8),"individual leg capacity and no uplift");
                foreach(var member in beam.Transfer.LowerCases.Concat(beam.Transfer.PrimaryCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
                {
                    Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"all members pass every loading stage, including complete deflection evaluation");
                    Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"member equilibrium");
                }
                Console.WriteLine($"ECONOMY B{index+1} legs={beam.LegCount} primaryCoverage={beamLength} primaryRuns={string.Join(",",beam.StockPlan.Pieces.Select(p=>p.StockLengthMm))} maxBeamTributary={beam.Transfer.TributaryWidthsMm.Max():0.###}");
            }
            Near(result.SlabLoadKn,network.Bays.Sum(b=>network.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*result.Beams[0].LengthMm/1000,"shared full bay slab load conserved");
            Console.WriteLine(common.Trace.Snapshot());
            return result;
        }
    }
    public static void Run()
    {
        var before=Solve(false,1);var serial=Solve(true,1);var after=Solve(true,Math.Min(4,Environment.ProcessorCount));
        Check(serial.LayoutDiagnostics==after.LayoutDiagnostics && serial.Schedule==after.Schedule && serial.Summary==after.Summary,"exact case has identical selected layout with serial or automatic worker counts");
        var serialMembers=serial.Beams.SelectMany(b=>b.Transfer.LowerCases.Concat(b.Transfer.PrimaryCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers)).ToArray();
        var parallelMembers=after.Beams.SelectMany(b=>b.Transfer.LowerCases.Concat(b.Transfer.PrimaryCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers)).ToArray();
        Check(serialMembers.Length==parallelMembers.Length,"exact case member count matches");
        for(int i=0;i<serialMembers.Length;i++)
        {
            var a=serialMembers[i].Analysis;var b=parallelMembers[i].Analysis;
            Check(a.MomentKnM==b.MomentKnM && a.ShearKn==b.ShearKn && a.DeflectionMm==b.DeflectionMm && a.DeflectionUtilization==b.DeflectionUtilization && a.Reactions.SequenceEqual(b.Reactions),"exact case has identical unrounded member demands with serial or automatic worker counts");
        }
        Check(after.Beams.SelectMany(b=>b.Transfer.PrimaryCases.SelectMany(c=>c)).All(m=>m.Points.Length==0 && m.Uniform.Length>0 && m.Utilization<=1+1e-8),"economy ranking uses only passing UDL primary members");
        Check(after.EconomyScoreMm<=before.EconomyScoreMm+.01,"full-network economic ranking improves or retains the baseline with separately weighted steel supports");
        Console.WriteLine("Network economy checks passed.");
    }
}
