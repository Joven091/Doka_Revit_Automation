using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class ReceiverCeilingNormalizationTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Receiver ceiling normalization: "+message);}
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=70,
        StockLengthsMm=new[] {1800.0,2450},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,
        IgnoreUplift=true,LocalNetworkSearch=true,CoordinateCrossingLayouts=false,Trace=new BeamSearchTrace("normalized adaptive receiver ceiling"),
        NetworkReceiverWork=new NetworkReceiverCache()};
    static TransferNetworkInput Network()=>new TransferNetworkInput {PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
        Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=400,DepthMm=800,BottomElevationMm=-800}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},
        CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=200,DepthMm=300,BottomElevationMm=-300}}};
    static readonly MethodInfo solve=typeof(TransferNetworkOptimizer).GetMethod("Solve",BindingFlags.NonPublic|BindingFlags.Static);
    static TowerLayout Solve(TowerInput input,int ceiling)
    {
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);
        try{return (TowerLayout)solve.Invoke(null,new object[] {input,Network(),0,null,null,2450.0,0.0,new[] {new[] {2450.0}},
            new TransferredSlabOptimizer.GeometryCache(),ceiling,(double?)1524,0.0,null,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static ReceiverSolveKey Key(NetworkReceiverCache cache)
    {
        var responses=typeof(NetworkReceiverCache).GetField("entries",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cache);
        var entries=(IDictionary)responses.GetType().GetField("entries",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(responses);
        Check(entries.Count==1,"one unchanged mathematical receiving problem is retained");return entries.Keys.Cast<ReceiverSolveKey>().Single();
    }
    static TowerLayout Adaptive(int ceiling)
    {
        var method=typeof(AdaptiveReceiverProofTests).GetMethod("Adaptive",BindingFlags.NonPublic|BindingFlags.Static);
        try{return (TowerLayout)method.Invoke(null,new object[] {ceiling});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    internal static void Run()
    {
        var input=Common();int stations=EngineerTowerSearch.MaximumStations(input),cap=2*stations;
        foreach(int minimum in new[] {2,4,stations+1})foreach(int raw in new[] {4,5,8,9,cap,cap+1,100,int.MaxValue})
        {
            input.EngineerMinimumStations=minimum;input.NetworkLegCeiling=raw;
            Check(TransferNetworkOptimizer.EffectiveReceiverLegCeiling(input)==2*Math.Min(stations,raw/2),
                "normalization matches the actual OptimizeEngineer maximum including odd ceilings and an excessive minimum station request");
        }
        input.EngineerSearch=false;input.NetworkLegCeiling=13;
        Check(TransferNetworkOptimizer.EffectiveReceiverLegCeiling(input)==13,"historical nonengineer searches retain their original budget");
        var broad=Adaptive(cap+100);var bounded=Adaptive(cap);var odd=Adaptive(9);var tight=Adaptive(8);
        Check(broad.LegCount==bounded.LegCount && broad.Transfer.EngineerReceiverCount==bounded.Transfer.EngineerReceiverCount
            && broad.PositionsMm.SequenceEqual(bounded.PositionsMm),"actual adaptive OptimizeEngineer searches above the physical cap are identical");
        Check(odd.LegCount==tight.LegCount && odd.Transfer.EngineerReceiverCount==tight.Transfer.EngineerReceiverCount
            && odd.PositionsMm.SequenceEqual(tight.PositionsMm),"actual adaptive OptimizeEngineer searches at adjacent even/odd budgets are identical");
        Check(broad.LegCount==12 && broad.Transfer.EngineerReceiverCount==3 && tight.LegCount==8 && tight.Transfer.EngineerReceiverCount==4,
            "distinct effective ceilings preserve the real adaptive receiver-count counterexample");
        input=Common();var original=Solve(input,cap+1);Check(original!=null,"adaptive production receiver passes");long before=input.Trace.Count(BeamSearchCounter.NetworkSolves);
        var expanded=Solve(input,cap+100);
        Check(ReferenceEquals(original,expanded) && input.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"equivalent wider raw budgets reuse an adaptive production receiver");
        var key=Key(input.NetworkReceiverWork);
        Check(input.NetworkReceiverWork.Get(key,cap,out var entry) && entry.LegCeiling==cap && !entry.MinimumLegCountProven,
            "the cached adaptive result records only the effective cap and makes no nonadaptive minimum proof");
        Check(input.NetworkReceiverWork.Get(key,cap+100,out _)==entry.WiderCeilingStable,"only the distinct count-three wider-stability proof can reuse adaptive success beyond its recorded cap");
        input=Common();var tightProduction=Solve(input,8);Check(tightProduction!=null,"a tighter adaptive production receiver passes");before=input.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(input,9)!=null && input.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"odd ceiling normalization avoids a repeated equivalent receiver solve");
        Solve(input,cap);Check(tightProduction.Transfer.EngineerReceiverCount==3 ? input.Trace.Count(BeamSearchCounter.NetworkSolves)==before : input.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"larger effective ceilings reuse only stable count-three adaptive results");
        input=Common();input.EngineerMinimumStations=stations+1;Check(Solve(input,cap+1)==null,"the full physical adaptive universe can fail");before=input.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(input,cap+100)==null && input.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"equivalent capped negative searches are reused");
        key=Key(input.NetworkReceiverWork);
        Check(input.NetworkReceiverWork.Get(key,cap,out entry) && entry.LegCeiling==cap
            && !input.NetworkReceiverWork.Get(key,cap+100,out _),"negative evidence is recorded at the effective cap rather than the raw budget");
        input=Common();input.EngineerMinimumStations=4;
        Check(Solve(input,4)==null,"a tight maximum cannot acquire the requested minimum station count");before=input.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(input,5)==null && input.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"minimum station constraints remain part of the exact odd/even capped problem");
        Check(Solve(input,cap+100)!=null && input.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"a failed tight maximum cannot poison a larger feasible adaptive universe");
        Console.WriteLine("PASS "+checks+" receiver ceiling normalization checks: actual adaptive cap/odd equivalence, distinct-ceiling counterexample and bounded proof metadata");
    }
}
