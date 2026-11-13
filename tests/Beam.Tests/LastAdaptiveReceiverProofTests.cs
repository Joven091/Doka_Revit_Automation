using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class LastAdaptiveReceiverProofTests
{
    static int checks;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Last adaptive receiver proof: "+message);}
    static ReceiverSolveKey Key()
    {
        var common=new TowerInput {LengthMm=2450,EngineerSearch=true,StockLengthsMm=new[] {2450.0},
            TransferredSlab=new TransferredSlabInput {BeamWidthMm=400,BeamLineLoadKnM=5,UpperSpacingMm=305,StockCovers=new[] {new[] {2450.0}}}};
        return new ReceiverSolveKey(common,new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=400,DepthMm=800}},Bays=Array.Empty<TransferBayRow>(),CrossingBeams=Array.Empty<TransferCrossingBeamRow>()},0,false,shareLegCeilings:true);
    }
    static NetworkReceiverCache.Entry Entry(int receiverCount)=>new NetworkReceiverCache.Entry {Best=new TowerLayout {PositionsMm=new double[6]},LegCeiling=16,
        TighterFailureMinimumProven=receiverCount==8,WiderCeilingStable=receiverCount==3,MinimumLegCountProven=false,
        Alternatives=new Dictionary<double,TowerLayout>(),Diagnostics=new TowerSearchDiagnostics(270)};
    static void Records()
    {
        var key=Key();
        foreach(int count in Enumerable.Range(3,6))
        {
            var cache=new NetworkReceiverCache();cache.Store(key,Entry(count));
            bool tight=cache.Get(key,10,out var value);
            Check(tight==(count==8),"only the fully exhausted final adaptive count proves a smaller budget below its winner cannot pass");
            if(tight)Check(value.Best==null && value.MinimumRequiredLegs==12,"derived failure records its actual required minimum legs");
            Check(cache.Get(key,12,out value) && value.Best!=null && !value.MinimumLegCountProven,"the original adaptive winner remains available without a nonadaptive proof");
            Check(cache.Get(key,18,out _)==(count==3),"a final-count minimum proof never claims wider-ceiling algorithm stability");
        }
        var both=new NetworkReceiverCache();both.Store(key,Entry(8));
        both.Store(key,new NetworkReceiverCache.Entry {CompleteFailure=true,LegCeiling=10,Alternatives=new Dictionary<double,TowerLayout>(),Diagnostics=new TowerSearchDiagnostics(270)});
        Check(both.Get(key,10,out var explicitFailure) && explicitFailure.Best==null && explicitFailure.MinimumRequiredLegs==0,"separate exhaustive failure evidence retains its own provenance");
        Check(both.Get(key,14,out var winner) && winner.Best!=null,"smaller completed failures cannot erase the last-count winner");
    }
    static TowerLayout ProtectedCandidate(int ceiling)
    {
        var type=typeof(AdaptiveReceiverProofTests);var input=(TowerInput)type.GetMethod("Common",hidden).Invoke(null,null);
        var network=(TransferNetworkInput)type.GetMethod("Network",hidden).Invoke(null,new object[] {true});var geometry=type.GetMethod("Geometry",hidden);
        input.NetworkLegCeiling=ceiling;
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=3,UpperSpacingMm=305,PlywoodSheetLengthMm=2440,
            FixedCoveredLengthMm=2450,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {2450.0}},EngineerNetwork=network,EngineerBeam=0,
            SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache {Fixed=new[] {(TransferredSlabOptimizer.Geometry)geometry.Invoke(null,new object[] {false}),(TransferredSlabOptimizer.Geometry)geometry.Invoke(null,new object[] {true})}}};
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0,false,false);input.EngineerPlan.InitialLegs=6;
        input.TransferLayoutAcceptance=layout=>layout.Transfer.EngineerReceiverCount==8 && layout.PositionsMm.Length>=4;
        return TransferredSlabOptimizer.Optimize(input,true);
    }
    static void FullRefinementProof()
    {
        // Fixed candidates isolate traversal proof only; they remain excluded
        // from unrestricted production cache publication by its existing guard.
        var selected=ProtectedCandidate(12);Check(selected.Transfer.EngineerReceiverCount==8 && selected.LegCount==8,"actual count-three-through-eight refinement reaches its last-count minimum");
        bool failed=false;try{ProtectedCandidate(6);}catch(TowerOptimizationException){failed=true;}
        Check(failed,"the original full refinement also rejects a budget below its selected minimum");
        AdaptiveReceiverProofTests.Run();
    }
    static ReceiverSolveKey OnlyKey(NetworkReceiverCache cache)
    {
        var wrapped=typeof(NetworkReceiverCache).GetField("entries",fields).GetValue(cache);
        var entries=(IDictionary)wrapped.GetType().GetField("entries",fields).GetValue(wrapped);
        Check(entries.Count==1,"actual local fixture stores one exact receiver problem");return entries.Keys.Cast<ReceiverSolveKey>().Single();
    }
    static TowerLayout Actual(TowerInput common,TransferNetworkInput network,int ceiling)
    {
        common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift);
        double edge=(network.Bays[0].WidthMm-3800)/2;
        var slab=new UpperTransferPlan {StockLengthMm=3800,EdgeMm=edge,NearFootMm=edge+650,FarFootMm=edge+650,IsCustomLength=true};
        var method=typeof(TransferNetworkOptimizer).GetMethod("Solve",hidden);
        try{return (TowerLayout)method.Invoke(null,new object[] {common,network,0,null,slab,5800.0,0.0,new[] {new[] {5800.0}},
            new TransferredSlabOptimizer.GeometryCache(),ceiling,(double?)1524,0.0,null,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void ActualProduction()
    {
        var child=ReportedScenarioTests.DiagnosticPart(1,2).Item2;var common=child.CreateTowerInput();var network=common.Network;
        common.CoordinateCrossingLayouts=true;common.LocalNetworkSearch=true;common.NetworkReceiverWork=new NetworkReceiverCache();common.Trace=new BeamSearchTrace("reported Area2 last receiver count proof");
        var selected=Actual(common,network,int.MaxValue);
        Check(selected!=null && selected.Transfer.EngineerReceiverCount==8,"reported Area2 local receiver succeeds only at its last adaptive count");
        var key=OnlyKey(common.NetworkReceiverWork);Check(common.NetworkReceiverWork.Get(key,selected.LegCount,out var entry),"completed actual local receiver is stored");
        Check(entry.TighterFailureMinimumProven && !entry.MinimumLegCountProven && !entry.WiderCeilingStable,"production records the distinct tighter-failure proof without claiming wider stability");
        int tight=selected.LegCount-2;long before=common.Trace.Count(BeamSearchCounter.NetworkSolves);
        var actual=Actual(common,network,tight);Check(actual==null && common.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"tighter actual receiver failure reuses the completed final-count proof");
        var reference=child.CreateTowerInput();reference.CoordinateCrossingLayouts=true;reference.LocalNetworkSearch=true;reference.ReuseReceiverSearches=false;reference.Trace=new BeamSearchTrace("uncached reported tighter receiver");
        Check(Actual(reference,network,tight)==null,"the untouched fresh full refinement independently fails at the same tighter budget");
        Check(common.Diagnostics.Exception("minimum proof").Details.Contains("requires at least"),"derived production failure retains its minimum-leg explanation");
    }
    internal static void Run(){Records();FullRefinementProof();ActualProduction();Console.WriteLine("PASS "+checks+" last adaptive count proof checks: narrower-only failure evidence, no wider claim, original counterexample and actual reported local failure parity");}
}
