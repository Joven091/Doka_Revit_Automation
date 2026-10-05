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
    static TransferNetworkResult Solve(bool independent)
    {
        var common=Common(independent);var network=Network();var timer=Stopwatch.StartNew();
        using(var source=new CancellationTokenSource(TimeSpan.FromMinutes(6)))
        {
            common.Trace=new BeamSearchTrace("eight-beam economy regression",source.Token);
            var result=TransferNetworkOptimizer.Optimize(common,network);
            Console.WriteLine($"ECONOMY independent={independent} seconds={timer.Elapsed.TotalSeconds:0.000} legs={result.LegCount} feet={result.UpperFootCount} timber={result.TimberMm:0.###} coverage={result.SlabCoveredLengthMm} phase={result.RowPhaseMm} legsByBeam={string.Join(",",result.Beams.Select(b=>b.LegCount))}");
            foreach(var beam in result.Beams)
            {
                var index=Array.IndexOf(result.Beams,beam);double beamLength=common.LengthMm-2*beam.ConcreteEndOverhangMm;
                Near(beam.Transfer.RowsMm.Sum(),result.Beams[0].Transfer.RowsMm.Sum(),"same absolute slab rows");
                Check(beam.Transfer.RowsMm.SequenceEqual(result.Beams[0].Transfer.RowsMm),"shared rows align");
                Check(beamLength>=result.SlabCoveredLengthMm-1e-7 && beam.ConcreteEndOverhangMm<=75,"primary supports shared coverage within end allowance");
                Near(beam.Transfer.TributaryWidthsMm.Sum(),beamLength,"actual primary coverage reaches beam end tributaries");
                double beamLoad=network.AreaLoad(network.Beams[index].DepthMm)*network.Beams[index].WidthMm/1000*beamLength/1000;
                Near(beam.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn),beamLoad,"beam concrete force conserved");
                Near(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn),beamLoad+beam.Transfer.SlabLoadKn,"beam and slab forces conserved through primaries");
                Check(beam.MaxLegKn<=70+1e-8 && beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7 && r<=70+1e-8),"individual leg capacity and no uplift");
                foreach(var member in beam.Transfer.LowerCases.Concat(beam.Transfer.PrimaryCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
                {
                    Check(member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"all members pass every loading stage");
                    Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"member equilibrium");
                }
                Console.WriteLine($"ECONOMY B{index+1} legs={beam.LegCount} primaryCoverage={beamLength} primaryRuns={string.Join(",",beam.StockPlan.Pieces.Select(p=>p.StockLengthMm))} maxBeamTributary={beam.Transfer.TributaryWidthsMm.Max():0.###}");
            }
            Near(result.SlabLoadKn,network.Bays.Sum(b=>network.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*result.SlabCoveredLengthMm/1000,"shared full bay slab load conserved");
            Console.WriteLine(common.Trace.Snapshot());
            return result;
        }
    }
    public static void Run()
    {
        var before=Solve(false);var after=Solve(true);
        Check(before.LegCount==158 && before.Beams[0].LegCount==28,"reported baseline reproduced");
        Check(after.LegCount==144 && after.Beams[0].LegCount==20 && after.UpperFootCount==532,"full network retains economical B1 and reduces total supports");
        Near(after.TimberMm,1429400,"verified economical timber quantity retained");
        Check(after.LegCount+after.UpperFootCount<=before.LegCount+before.UpperFootCount,"network supports do not increase");
        Check(after.LegCount+after.UpperFootCount<before.LegCount+before.UpperFootCount || after.TimberMm<=before.TimberMm,"network cost ranking improves or retains baseline");
        Console.WriteLine("Network economy checks passed.");
    }
}
