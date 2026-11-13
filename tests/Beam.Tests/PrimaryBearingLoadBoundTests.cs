using System;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class PrimaryBearingLoadBoundTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Primary-bearing uniform bound: "+message);}
    static TowerInput Input(int criterion=270)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,
        DeflectionDenominator=criterion,IgnoreUplift=true,NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache()};
    static string Signature(CrossBeamSupportResult result)=>result==null ? "failed" : string.Join(";",result.SecondaryStationsMm.Select(x=>x.ToString("R")))+"|"
        +string.Join(";",result.Members.Select(m=>m.StartMm.ToString("R")+","+m.LengthMm.ToString("R")+","+string.Join(",",m.SupportsMm.Select(x=>x.ToString("R")))+","+string.Join(",",m.Uniform.Select(q=>q.KnPerM.ToString("R")))+","+string.Join(",",new[] {m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization}.Concat(m.Analysis.Reactions).Select(x=>x.ToString("R")))))+"|"
        +string.Join(";",result.Bearings.Select(b=>b.Beam+","+b.Row+","+b.Xmm.ToString("R")+","+b.Ymm.ToString("R")+","+b.ReactionKn.ToString("R")));
    internal static void Run()
    {
        var input=Input();double[] supports={150,4750};var unit=CrossBeamSupportOptimizer.PrimaryBearingUnit(input,4900,supports);
        double use=Math.Max(unit.MomentKnM/(TowerOptimizer.PairMoment/2),Math.Max(unit.ShearKn/(TowerOptimizer.PairShear/2),unit.DeflectionUtilization));
        foreach(int count in new[] {2,3,8})foreach(int sign in new[] {-1,1})
        {
            Check(!CrossBeamSupportOptimizer.PrimaryBearingCountFails(unit,sign*count/use*(1+0.5e-6),count),"near-boundary loads retain the original complete checks");
            Check(CrossBeamSupportOptimizer.PrimaryBearingCountFails(unit,sign*count/use*(1+2e-6),count),"only a proven failure beyond the conservative margin prunes a count");
        }
        var random=new Random(491720);
        for(int trial=0;trial<300;trial++)
        {
            int count=2+trial%7;var values=Enumerable.Range(0,count).Select(i=>(random.NextDouble()-.5)*40).ToArray();
            if(!CrossBeamSupportOptimizer.PrimaryBearingCountFails(unit,values.Sum(),count))continue;
            bool rejected=values.Any(q=>
            {
                var actual=LoadedBeamAnalysis.AnalyzeSigned(4900,supports,new[] {new BeamDistributedLoad {StartMm=0,EndMm=4900,KnPerM=q}},Array.Empty<BeamPointLoad>(),TowerOptimizer.PairEI/2,270);
                return Math.Max(actual.MomentKnM/(TowerOptimizer.PairMoment/2),Math.Max(actual.ShearKn/(TowerOptimizer.PairShear/2),actual.DeflectionUtilization))>1+1e-8;
            });
            Check(rejected,"signed redistribution always leaves an actually failing row when its sum bound fails");
        }
        Check(!CrossBeamSupportOptimizer.PrimaryBearingCountFails(unit,0,2),"opposite signed rows cannot be rejected from their zero total");
        Check(ReferenceEquals(unit,CrossBeamSupportOptimizer.PrimaryBearingUnit(input,4900,new[] {150.0,4750})),"the exact unit shape is reused");
        var changed=Input(500);changed.NetworkNumerics=input.NetworkNumerics;
        Check(!ReferenceEquals(unit,CrossBeamSupportOptimizer.PrimaryBearingUnit(changed,4900,supports)),"a different deflection criterion gets its own shape");
        Check(!ReferenceEquals(unit,CrossBeamSupportOptimizer.PrimaryBearingUnit(input,4900,new[] {150.0,4750.000000001})),"precise support changes get their own shape");
        foreach(int criterion in new[] {270,500})foreach(double depth in new[] {500.0,1150,20000})foreach(bool ledger in new[] {false,true})foreach(bool joint in new[] {false,true})
        {
            var network=PrimaryBearingTests.Fixture();network.Input.CrossingBeams[0].DepthMm=depth;
            if(ledger){network.Bays[0]=new UpperTransferPlan();network.SlabCoveredLengthMm=6900;network.Beams[0].Transfer.SharedSlabRowsMm=Array.Empty<double>();}
            if(joint)foreach(var beam in network.Beams)beam.StockPlan.Pieces=new[] {new TowerStockPiece {StockLengthMm=3450},new TowerStockPiece {StartMm=3450,StockLengthMm=3450}};
            var common=Input(criterion);common.OptimizeCrossingSearch=false;var expected=CrossBeamSupportOptimizer.PrimaryBearing(common,network,0,out string expectedFailure);
            common.OptimizeCrossingSearch=true;var actual=CrossBeamSupportOptimizer.PrimaryBearing(common,network,0,out string actualFailure);
            Check(Signature(actual)==Signature(expected) && actualFailure==expectedFailure,"retained layout, every accepted member demand and bearing remain bit-for-bit identical");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();input.Trace=new BeamSearchTrace("cancelled unit response",cancellation.Token);
            try{CrossBeamSupportOptimizer.PrimaryBearingUnit(input,4900,supports);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            Check(true,"cancelled preparation stops even when its exact unit shape is already cached");
        }
        Console.WriteLine("PASS "+checks+" primary-bearing uniform lower-bound checks: signed loads, tolerance, exact accepted demands, criteria, supports and cancellation");
    }
}
