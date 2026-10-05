using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class NetworkSizeComparison
{
    static TowerInput Common(CancellationToken token) => new TowerInput {LengthMm=10600,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,Trace=new BeamSearchTrace("2026-10-05 network size comparison",token)};
    static double Coverage(TransferNetworkResult r) => r.Beams[0].LengthMm-2*r.Beams[0].ConcreteEndOverhangMm;
    static double Phase(TransferNetworkResult r) => r.Beams[0].Transfer.RowsMm[0]-(10600-(r.Beams[0].Transfer.RowsMm.Length-1)*275)/2;
    static void Describe(string name,TransferNetworkResult r,Stopwatch timer)
    {
        Console.WriteLine($"COMPARISON {name}: {timer.Elapsed.TotalSeconds:0.000}s; legs={r.LegCount}; upper feet={r.UpperFootCount}; objective={r.LegCount+r.UpperFootCount}; timber={r.TimberMm:R}; coverage={Coverage(r):R}; phase={Phase(r):R}; legs by beam={string.Join(",",r.Beams.Select(b=>b.LegCount))}");
        Console.WriteLine("SUMMARY "+r.Summary);
        for(int i=0;i<r.Bays.Length;i++)Console.WriteLine($"BAY S{i+1}: stock={r.Bays[i].StockLengthMm:R}; edge={r.Bays[i].EdgeMm:R}; near={r.Bays[i].NearFootMm:R}; far={r.Bays[i].FarFootMm:R}");
        var b=r.Beams[0];Console.WriteLine($"B1: primary width={b.WidthMm:R}; lower stock={b.Transfer.LowerStockLengthMm:R}; lower start={b.Transfer.LowerStockStartMm:R}; peak leg={b.MaxLegKn:R}; utilization={b.Utilization:R}; beam+slab load={b.Transfer.LowerCases.Last().Sum(c=>c.Analysis.TotalLoadKn):R}; runs={string.Join(",",b.StockPlan.Pieces.Select(p=>p.StockLengthMm))}");
        Console.WriteLine("B1 LEG POSITIONS "+string.Join(",",b.PositionsMm));
    }
    static void Replay(string name,TransferNetworkInput network,TransferNetworkResult grid,UpperTransferPlan plan,CancellationToken token)
    {
        var common=Common(token);var b=network.Beams[0];common.DiagnosticScope="B1 · ";
        common.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=b.WidthMm,BeamLineLoadKnM=network.AreaLoad(b.DepthMm)*b.WidthMm/1000,UpperSpacingMm=275,RightBayMm=network.Bays[0].WidthMm,RightAreaLoadKnM2=network.AreaLoad(network.Bays[0].ThicknessMm),RightPlan=plan,FixedCoveredLengthMm=Coverage(grid),FixedRowPhaseMm=Phase(grid)};
        try {var result=TransferredSlabOptimizer.Optimize(common,true);Console.WriteLine($"REPLAY {name}: legs={result.LegCount}; primary width={result.WidthMm:R}; peak={result.MaxLegKn:R}");}
        catch(TowerOptimizationException ex) {Console.WriteLine("REPLAY "+name+": rejected "+ex.Message);}
    }
    public static void Run()
    {
        using(var source=new CancellationTokenSource(TimeSpan.FromMinutes(8)))
        {
            var beams=Enumerable.Range(0,8).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray();
            var bays=new[] {2677.0,1874,3276,2274,2278,2272,2252}.Select(x=>new TransferBayRow {WidthMm=x,ThicknessMm=300}).ToArray();
            var small=new TransferNetworkInput {Beams=beams.Take(3).ToArray(),Bays=bays.Take(2).ToArray(),UpperSpacingMm=275};
            var large=new TransferNetworkInput {Beams=beams,Bays=bays,UpperSpacingMm=275};
            var timer=Stopwatch.StartNew();var three=TransferNetworkOptimizer.Optimize(Common(source.Token),small);Describe("B1-B3",three,timer);
            timer.Restart();var eight=TransferNetworkOptimizer.Optimize(Common(source.Token),large);Describe("B1-B8",eight,timer);
            Replay("small feet + small grid",small,three,three.Bays[0],source.Token);
            Replay("small feet + large grid",large,eight,three.Bays[0],source.Token);
            Replay("large feet + small grid",large,three,eight.Bays[0],source.Token);
            Replay("large feet + large grid",large,eight,eight.Bays[0],source.Token);
            large.ComparisonCoveredLengthMm=Coverage(three);large.ComparisonRowPhaseMm=Phase(three);large.ComparisonFirstBay=three.Bays[0];
            timer.Restart();try {var pinned=TransferNetworkOptimizer.Optimize(Common(source.Token),large);Describe("B1-B8 with small-network grid and S1 feet",pinned,timer);}catch(TowerOptimizationException ex) {Console.WriteLine("PINNED NETWORK rejected: "+ex.Message);}
        }
    }
}
