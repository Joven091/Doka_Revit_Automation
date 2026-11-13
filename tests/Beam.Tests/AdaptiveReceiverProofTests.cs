using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class AdaptiveReceiverProofTests
{
    static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly MethodInfo solve=typeof(TransferNetworkOptimizer).GetMethod("Solve",BindingFlags.NonPublic|BindingFlags.Static);
    static void Check(bool value,string message) {if(!value)throw new Exception("Adaptive receiver proof: "+message);}
    static TransferNetworkInput Network(bool adaptive)=>new TransferNetworkInput {
        PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
        Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=400,DepthMm=800,BottomElevationMm=-800}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},
        CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=200,DepthMm=300,BottomElevationMm=-300,
            SupportMode=adaptive ? CrossBeamSupportMode.Automatic : CrossBeamSupportMode.DedicatedTowers}}};
    static TransferredSlabOptimizer.Geometry Geometry(bool six)
    {
        double[] stations=six ? new[] {150.0,750,775,1375,1400,2000} : new[] {200.0,800,1650,2250};
        return new TransferredSlabOptimizer.Geometry {Positions=stations,Joined=six ? new[] {true,false,true,false,true} : new[] {true,false,true},
            Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=2450,FirstSupport=0,LastSupport=stations.Length-1}},
            MemberSupports=new[] {stations}};
    }
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=70,
        StockLengthsMm=new[] {1800.0,2450},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,
        IgnoreUplift=true,LocalNetworkSearch=true,CoordinateCrossingLayouts=false,Trace=new BeamSearchTrace("independent adaptive proof"),
        NetworkReceiverWork=new NetworkReceiverCache()};
    static TowerLayout Adaptive(int ceiling)
    {
        var input=Common();var network=Network(true);input.NetworkLegCeiling=ceiling;
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=3,UpperSpacingMm=305,PlywoodSheetLengthMm=2440,
            FixedCoveredLengthMm=2450,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {2450.0}},EngineerNetwork=network,EngineerBeam=0,
            SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache {Fixed=new[] {Geometry(false),Geometry(true)}}};
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0,false,false);
        // Force the broad search to begin at six stations. A tight ceiling caps
        // this to four, which lets its failed count-3 attempt refine to count 4.
        input.EngineerPlan.InitialLegs=6;
        input.TransferLayoutAcceptance=layout=>layout.Transfer.EngineerReceiverCount==3 ? layout.PositionsMm.Length>=6 : layout.PositionsMm.Length>=4;
        return TransferredSlabOptimizer.Optimize(input,true);
    }
    static TowerLayout Solve(TowerInput common,TransferNetworkInput network,TransferredSlabOptimizer.GeometryCache geometry)
    {
        common.Diagnostics=new TowerSearchDiagnostics(270,trace:common.Trace,ignoreUplift:true);
        try{return (TowerLayout)solve.Invoke(null,new object[] {common,network,0,null,null,2450.0,0.0,
            new[] {new[] {2450.0}},geometry,12,1524.0,0.0,null,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static ReceiverSolveKey SingleKey(NetworkReceiverCache cache)
    {
        var responses=typeof(NetworkReceiverCache).GetField("entries",Hidden).GetValue(cache);
        var dictionary=(IDictionary)responses.GetType().GetField("entries",Hidden).GetValue(responses);
        Check(dictionary.Count==1,"expected one exact receiving problem");return dictionary.Keys.Cast<ReceiverSolveKey>().Single();
    }
    static int Count(NetworkReceiverCache cache)
    {
        var responses=typeof(NetworkReceiverCache).GetField("entries",Hidden).GetValue(cache);
        return ((IDictionary)responses.GetType().GetField("entries",Hidden).GetValue(responses)).Count;
    }
    internal static void Run()
    {
        var broad=Adaptive(12);var tight=Adaptive(8);
        Check(broad.LegCount==12 && broad.Transfer.EngineerReceiverCount==3,"broad count-3 search must return twelve legs");
        Check(tight.LegCount==8 && tight.Transfer.EngineerReceiverCount==4,"tight search must refine to count 4 and return eight legs");
        Check(tight.LegCount<broad.LegCount,"a broad adaptive positive is not a lower-ceiling impossibility proof");
        foreach(bool adaptive in new[] {true,false})
        {
            var common=Common();var network=Network(adaptive);
            var selected=Solve(common,network,new TransferredSlabOptimizer.GeometryCache());Check(selected!=null,"production receiving solve must finish");
            var key=SingleKey(common.NetworkReceiverWork);Check(common.NetworkReceiverWork.Get(key,12,out var entry),"production winner must be retained");
            var field=entry.GetType().GetField("MinimumLegCountProven",Hidden|BindingFlags.Public);
            Check(field!=null,"missing minimum-tier proof provenance");bool proven=(bool)field.GetValue(entry);
            Check(proven==!adaptive,"production proof guard must match actual adaptive receiver eligibility");
            if(adaptive)Check(!common.NetworkReceiverWork.Get(key,selected.LegCount-2,out _),"adaptive winner cannot erase a tighter search");
        }
        var fixedCommon=Common();var fixedNetwork=Network(false);
        var fixedGeometry=new TransferredSlabOptimizer.GeometryCache {Fixed=new[] {Geometry(true)}};
        Check(Solve(fixedCommon,fixedNetwork,fixedGeometry)?.LegCount==12,"restricted geometry fixture must keep its selected stations");
        Check(Count(fixedCommon.NetworkReceiverWork)==0,"restricted geometry must publish no exhaustive/minimum-tier evidence");
        var unrestricted=Solve(fixedCommon,fixedNetwork,new TransferredSlabOptimizer.GeometryCache());
        Check(unrestricted!=null && unrestricted.LegCount<12,"unrestricted search must retain its smaller valid layout");
        long before=fixedCommon.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(fixedCommon,fixedNetwork,fixedGeometry)?.LegCount==12,"restricted search must not consume an unrelated broad cache winner");
        Check(fixedCommon.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"restricted geometry still needs its own check");
        Console.WriteLine("PASS actual adaptive broad/tight counterexample, production proof eligibility and restricted-geometry cache provenance");
    }
}
