using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class AdaptiveWiderReceiverCacheTests
{
    static int checks;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly MethodInfo solve=typeof(TransferNetworkOptimizer).GetMethod("Solve",hidden);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Adaptive wider receiver reuse: "+message);}
    static TowerInput Common(bool reuse=true)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=3900,LegCapacityKn=70,
        StockLengthsMm=new[] {1800.0,3900},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,
        IgnoreUplift=true,LocalNetworkSearch=true,CoordinateCrossingLayouts=false,ReuseReceiverSearches=reuse,
        Trace=new BeamSearchTrace("adaptive wider-only proof"),NetworkReceiverWork=new NetworkReceiverCache()};
    static TransferNetworkInput Network()=>new TransferNetworkInput {PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
        Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=400,DepthMm=800,BottomElevationMm=-800}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},
        CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1950,WidthMm=200,DepthMm=300,BottomElevationMm=-300,SupportMode=CrossBeamSupportMode.Automatic}}};
    static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int ceiling,Dictionary<double,TowerLayout> alternatives=null,double? width=null,double[] cover=null)
    {
        common.Diagnostics=new TowerSearchDiagnostics(270,trace:common.Trace,ignoreUplift:true);
        try{return (TowerLayout)solve.Invoke(null,new object[] {common,network,0,null,null,common.LengthMm,0.0,new[] {cover??new[] {3900.0}},
            new TransferredSlabOptimizer.GeometryCache(),ceiling,width,0.0,alternatives,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static ReceiverSolveKey OnlyKey(NetworkReceiverCache cache)
    {
        var wrapped=typeof(NetworkReceiverCache).GetField("entries",fields).GetValue(cache);
        var entries=(IDictionary)wrapped.GetType().GetField("entries",fields).GetValue(wrapped);
        Check(entries.Count==1,"fixture retains one exact mathematical receiving problem");return entries.Keys.Cast<ReceiverSolveKey>().Single();
    }
    static string Fingerprint(TowerLayout layout)=>layout==null ? "null" : string.Join("|",new[] {
        layout.WidthMm.ToString("R",CultureInfo.InvariantCulture),string.Join(",",layout.PositionsMm.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))),string.Join(",",layout.Joined),
        layout.Transfer.EngineerReceiverCount.ToString(),string.Join(";",layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(x=>x).Concat(layout.Transfer.UpperMembers).Select(m=>
            m.Name+":"+m.StartMm.ToString("R",CultureInfo.InvariantCulture)+":"+string.Join(",",new[] {m.LengthMm,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Utilization}.Concat(m.Analysis.Reactions).Select(x=>x.ToString("R",CultureInfo.InvariantCulture))))) });
    static void ValidateMembers(TowerLayout layout)
    {
        foreach(var member in layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(x=>x).Concat(layout.Transfer.UpperMembers))
        {
            var dense=LoadedBeamAnalysis.AnalyzeDenseReference(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,270);
            Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && Math.Abs(dense.MomentKnM-member.Analysis.MomentKnM)<1e-7
                && Math.Abs(dense.ShearKn-member.Analysis.ShearKn)<1e-7 && Math.Abs(dense.DeflectionMm-member.Analysis.DeflectionMm)<1e-7
                && dense.Reactions.Zip(member.Analysis.Reactions,(a,b)=>Math.Abs(a-b)).All(x=>x<1e-7),"cached candidate retains full independent member response/capacity checks: "+member.Name+" use="+member.Utilization+" moment="+dense.MomentKnM+"/"+member.Analysis.MomentKnM+" shear="+dense.ShearKn+"/"+member.Analysis.ShearKn+" deflection="+dense.DeflectionMm+"/"+member.Analysis.DeflectionMm+" reactiondelta="+dense.Reactions.Zip(member.Analysis.Reactions,(a,b)=>Math.Abs(a-b)).Max());
        }
    }
    static void Records()
    {
        var input=Common();input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=400,BeamLineLoadKnM=9,UpperSpacingMm=305,StockCovers=new[] {new[] {3900.0}}};
        var key=new ReceiverSolveKey(input,Network(),0,false,shareLegCeilings:true);var cache=new NetworkReceiverCache();
        var winner=new TowerLayout {PositionsMm=new double[6]};
        cache.Store(key,new NetworkReceiverCache.Entry {Best=winner,LegCeiling=12,WiderCeilingStable=true,MinimumLegCountProven=false,
            Alternatives=new Dictionary<double,TowerLayout>(),Diagnostics=new TowerSearchDiagnostics(270)});
        Check(cache.Get(key,14,out var wider) && ReferenceEquals(wider.Best,winner),"count-three success is reusable only toward wider station budgets");
        Check(!cache.Get(key,10,out _),"wider stability never fabricates tighter adaptive failure evidence");
        Check(cache.Get(key,12,out wider) && !wider.MinimumLegCountProven,"wide-only flag leaves minimum-tier provenance false");
        cache.Store(key,new NetworkReceiverCache.Entry {CompleteFailure=true,LegCeiling=10,Alternatives=new Dictionary<double,TowerLayout>(),Diagnostics=new TowerSearchDiagnostics(270)});
        Check(cache.Get(key,8,out var failed) && failed.Best==null,"independently exhausted tighter failure remains reusable");
        Check(cache.Get(key,14,out wider) && ReferenceEquals(wider.Best,winner),"tight failure coexists with wider-only success");
        cache=new NetworkReceiverCache();cache.Store(key,new NetworkReceiverCache.Entry {Best=winner,LegCeiling=12,WiderCeilingStable=false});
        Check(!cache.Get(key,14,out _),"later refined receiver counts still need their wider search");
    }
    static void Production(bool allWidths)
    {
        var network=Network();var cached=Common();var reference=Common(false);
        var initialChoices=allWidths ? new Dictionary<double,TowerLayout>() : null;
        var first=Solve(cached,network,8,initialChoices,allWidths ? (double?)null : 1524);
        Check(first!=null && first.Transfer.EngineerReceiverCount==3,"actual adaptive search succeeds at its first receiver count");
        var key=OnlyKey(cached.NetworkReceiverWork);Check(cached.NetworkReceiverWork.Get(key,8,out var entry),"actual completed receiver is stored");
        Check(entry.WiderCeilingStable && !entry.MinimumLegCountProven,"production publishes wider stability separately from adaptive minimum-leg proof");
        long before=cached.Trace.Count(BeamSearchCounter.NetworkSolves);
        var actualChoices=allWidths ? new Dictionary<double,TowerLayout>() : null;
        var actual=Solve(cached,network,12,actualChoices,allWidths ? (double?)null : 1524);
        var expectedChoices=allWidths ? new Dictionary<double,TowerLayout>() : null;
        var expected=Solve(reference,network,12,expectedChoices,allWidths ? (double?)null : 1524);
        Check(ReferenceEquals(actual,first) && cached.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"actual wider budget reuses its completed count-three search");
        Check(Fingerprint(actual)==Fingerprint(expected),"cached winner matches full wider geometry, loads, reactions, member limits and ranking");ValidateMembers(actual);
        if(allWidths)
        {
            Check(actualChoices.Keys.OrderBy(x=>x).SequenceEqual(expectedChoices.Keys.OrderBy(x=>x)),"ordinary multiwidth search retains every wider width alternative");
            foreach(var pair in actualChoices)Check(Fingerprint(pair.Value)==Fingerprint(expectedChoices[pair.Key]),"every retained width has the same complete wider numerical result");
        }
        var tight=Solve(cached,network,first.LegCount-2,allWidths ? new Dictionary<double,TowerLayout>() : null,allWidths ? (double?)null : 1524);
        var tightReference=Solve(reference,network,first.LegCount-2,allWidths ? new Dictionary<double,TowerLayout>() : null,allWidths ? (double?)null : 1524);
        Check(Fingerprint(tight)==Fingerprint(tightReference),"tightened adaptive search still follows the original refinement path");
    }
    static void CappedInitialTier()
    {
        TowerLayout Candidate(int ceiling)
        {
            var type=typeof(AdaptiveReceiverProofTests);
            var input=(TowerInput)type.GetMethod("Common",hidden).Invoke(null,null);
            var network=(TransferNetworkInput)type.GetMethod("Network",hidden).Invoke(null,new object[] {true});
            var geometry=type.GetMethod("Geometry",hidden);
            input.NetworkLegCeiling=ceiling;
            input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=3,UpperSpacingMm=305,PlywoodSheetLengthMm=2440,
                FixedCoveredLengthMm=2450,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {2450.0}},EngineerNetwork=network,EngineerBeam=0,
                SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache {Fixed=new[] {(TransferredSlabOptimizer.Geometry)geometry.Invoke(null,new object[] {false}),(TransferredSlabOptimizer.Geometry)geometry.Invoke(null,new object[] {true})}}};
            input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0,false,false);input.EngineerPlan.InitialLegs=6;
            input.TransferLayoutAcceptance=layout=>true;
            return TransferredSlabOptimizer.Optimize(input,true);
        }
        // Restrict geometry only to isolate the tier-order proof; restricted
        // searches themselves still cannot consume or publish complete cache evidence.
        var tight=Candidate(8);var wide=Candidate(12);
        Check(tight.Transfer.EngineerReceiverCount==3 && wide.Transfer.EngineerReceiverCount==3,"a binding first tier remains at receiver count three");
        Check(tight.SearchInitialLegsPerLine==6 && tight.PositionsMm.Length==4,"old leg ceiling genuinely caps its six-station heuristic tier to four");
        Check(Fingerprint(tight)==Fingerprint(wide),"new higher-priority heuristic tiers cannot erase the existing smaller passing geometry");ValidateMembers(tight);
    }
    internal static void Run()
    {
        Records();Production(false);Production(true);CappedInitialTier();
        AdaptiveReceiverProofTests.Run();
        Console.WriteLine("PASS "+checks+" adaptive wider-only receiver cache checks: independent tighter proofs, fixed/multiwidth full-search parity and original12-vs-8counterexample");
    }
}
