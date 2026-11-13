using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class ReceiverProofCacheTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Receiver proof cache: "+message);}
    static TowerInput Common(bool reuse=true)=>new TowerInput {
        EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,
        StockLengthsMm=new[] {1250.0,1800,2450},LegCapacityKn=15,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,
        DeflectionDenominator=270,AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=1,
        AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.Both,ReuseReceiverSearches=reuse,
        Trace=new BeamSearchTrace("complete receiver proof reuse"),NetworkReceiverWork=new NetworkReceiverCache()};
    static TransferNetworkInput Network()=>new TransferNetworkInput {
        PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
        BeamSpacingsMm=new[] {305.0,305,305},SlabSpacingsMm=new[] {488.0,488},CrossingSpacingsMm=Array.Empty<double>(),
        Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),
        Bays=Enumerable.Range(0,2).Select(i=>new TransferBayRow {WidthMm=2125,ThicknessMm=200}).ToArray()};
    static readonly MethodInfo solve=typeof(TransferNetworkOptimizer).GetMethod("Solve",BindingFlags.Static|BindingFlags.NonPublic);
    static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int ceiling,TransferredSlabOptimizer.GeometryCache geometry=null)
    {
        common.Diagnostics=new TowerSearchDiagnostics(270,trace:common.Trace,ignoreUplift:true);
        var plan=new UpperTransferPlan {StockLengthMm=1700,EdgeMm=212.5,NearFootMm=362.5,FarFootMm=362.5,IsCustomLength=true};
        try{return (TowerLayout)solve.Invoke(null,new object[] {common,network,0,null,plan,2450.0,0.0,new[] {new[] {2450.0}},
            geometry??new TransferredSlabOptimizer.GeometryCache(),ceiling,null,0.0,null,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static ReceiverSolveKey Key()
    {
        var input=Common();input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=800,BeamLineLoadKnM=20,UpperSpacingMm=305,
            StockCovers=new[] {new[] {2450.0}}};
        return new ReceiverSolveKey(input,Network(),0,false,shareLegCeilings:true);
    }
    static NetworkReceiverCache.Entry Entry(int ceiling,TowerLayout best=null,bool complete=true,bool proven=false)=>new NetworkReceiverCache.Entry {
        LegCeiling=ceiling,Best=best,CompleteFailure=complete,MinimumLegCountProven=proven,
        Diagnostics=new TowerSearchDiagnostics(270),Alternatives=new Dictionary<double,TowerLayout>()};
    static string Fingerprint(TowerLayout layout)=>string.Join("|",new[] {
        string.Join(",",layout.PositionsMm.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))),string.Join(",",layout.Joined),
        layout.WidthMm.ToString("R",CultureInfo.InvariantCulture),layout.TransverseOffsetMm.ToString("R",CultureInfo.InvariantCulture),
        string.Join(";",layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers).Select(m=>
            m.Name+":"+string.Join(",",new[] {m.StartMm,m.LengthMm,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Utilization}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))
            +":"+string.Join(",",m.Analysis.Reactions.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))))) });
    static void Records()
    {
        var key=Key();var cache=new NetworkReceiverCache();
        var winner=new TowerLayout {PositionsMm=new double[6]};
        cache.Store(key,Entry(40,winner));
        Check(cache.Get(key,40,out var positive) && ReferenceEquals(positive.Best,winner),"positive exact winner retained");
        Check(!cache.Get(key,10,out _),"an adaptive positive winner cannot exclude a tighter receiver-count search");
        cache.Store(key,Entry(10));cache.Store(key,Entry(8));
        Check(cache.Get(key,8,out var failed) && failed.Best==null && failed.LegCeiling==10,"largest complete negative ceiling survives smaller failures");
        Check(cache.Get(key,10,out failed) && failed.Best==null,"negative evidence coexists with the wider positive winner");
        Check(cache.Get(key,40,out positive) && ReferenceEquals(positive.Best,winner),"a tighter failure never erases a valid wider winner");
        Check(!cache.Get(key,42,out _),"a smaller completed search cannot prove a larger ceiling");
        cache=new NetworkReceiverCache();cache.Store(key,Entry(40,complete:false));
        Check(!cache.Get(key,40,out _),"a fault or interrupted search is not exhausted failure evidence");
        cache.Store(key,Entry(40,winner,proven:true));
        Check(cache.Get(key,10,out failed) && failed.Best==null && failed.MinimumRequiredLegs==12,"a complete nonadaptive winner proves smaller tiers fail");
        Check(cache.Get(key,12,out positive) && ReferenceEquals(positive.Best,winner),"the winning minimum leg tier remains eligible");
        Check(cache.Get(key,42,out positive) && ReferenceEquals(positive.Best,winner),"a proven complete nonadaptive winner survives a larger leg ceiling");
    }
    static void SearchParity()
    {
        var network=Network();var cached=Common();var reference=Common(false);
        var actual=Solve(cached,network,40);var expected=Solve(reference,network,40);
        Check(actual!=null && actual.LegCount>4,"fixture needs more than the topology minimum");
        Check(Fingerprint(actual)==Fingerprint(expected),"cached and original receiver search retain complete geometry and member responses");
        var tight=Common();var tightResult=Solve(tight,network,actual.LegCount);long tightSolves=tight.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(tightResult!=null && Fingerprint(tightResult)==Fingerprint(expected),"a tight successful nonadaptive search agrees with the larger original universe");
        var widened=Solve(tight,network,40);
        Check(ReferenceEquals(tightResult,widened) && tight.Trace.Count(BeamSearchCounter.NetworkSolves)==tightSolves,
            "a larger prefix leg budget reuses the proven local winner without repeating geometry search");
        long before=cached.Trace.Count(BeamSearchCounter.NetworkSolves);int ceiling=actual.LegCount-2;
        Check(Solve(cached,network,ceiling)==null && Solve(reference,network,ceiling)==null,"minimum-tier proof agrees with an original uncached search");
        Check(cached.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"the proven smaller tier needs no repeated receiver search");
        Check(cached.Diagnostics.Exception("minimum tier").Details.Contains("requires at least"),"the derived failure retains its minimum-leg explanation");
        foreach(var member in actual.Transfer.PrimaryCases.Concat(actual.Transfer.LowerCases).SelectMany(c=>c).Concat(actual.Transfer.UpperMembers))
        {
            var dense=LoadedBeamAnalysis.AnalyzeDenseReference(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,270);
            Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && Math.Abs(member.Analysis.MomentKnM-dense.MomentKnM)<1e-7
                && Math.Abs(member.Analysis.ShearKn-dense.ShearKn)<1e-7 && Math.Abs(member.Analysis.DeflectionMm-dense.DeflectionMm)<1e-7,"selected members retain independent numerical checks");
        }
        var impossible=Common();impossible.LegCapacityKn=1;var uncached=Common(false);uncached.LegCapacityKn=1;
        Check(Solve(impossible,network,12)==null,"full larger-ceiling search fails");before=impossible.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(impossible,network,10)==null && Solve(uncached,network,10)==null,"larger negative proof agrees with uncached smaller failure");
        Check(impossible.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"completed larger failure skips smaller geometry search");
        Check(Solve(impossible,network,14)==null && impossible.Trace.Count(BeamSearchCounter.NetworkSolves)==before,"a larger raw ceiling at the same physical maximum reuses the failed search");
        before=cached.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(cached,network,40,TransferredSlabOptimizer.FixedGeometry(actual))!=null
            && cached.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"fixed geometry cannot consume an unrestricted cached search winner");
        var restricted=Common();var fixedGeometry=TransferredSlabOptimizer.FixedGeometry(actual);
        Check(Solve(restricted,network,40,fixedGeometry)!=null,"fixed geometry can pass its own complete checks");before=restricted.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(restricted,network,40,fixedGeometry)!=null && restricted.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"fixed-geometry success cannot publish unrestricted proof");
        restricted=Common();
        Check(Solve(restricted,network,40,new TransferredSlabOptimizer.GeometryCache {Fixed=Array.Empty<TransferredSlabOptimizer.Geometry>()})==null,"empty fixed candidate universe fails locally");
        Check(Solve(restricted,network,40)!=null,"fixed-geometry failure cannot poison the unrestricted receiving search");
    }
    static void FaultAndInterruptionGuards()
    {
        var network=Network();var faulty=Common();faulty.StockEndSetbackMm=-1;
        Check(Solve(faulty,network,40)==null,"invalid geometry is reported as a fault");long before=faulty.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(faulty,network,40)==null && faulty.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"generic ArgumentException cannot publish exhaustive failure");
        var seeded=Common();seeded.NetworkSeedCompletion=new NetworkSeedCompletion();seeded.NetworkSeedCompletion.Complete();
        Check(Solve(seeded,network,40)!=null,"a completed neighboring seed does not alter local feasibility");before=seeded.Trace.Count(BeamSearchCounter.NetworkSolves);
        Check(Solve(seeded,network,40)!=null && seeded.Trace.Count(BeamSearchCounter.NetworkSolves)>before,"seed completion prevents publishing proof evidence");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();var interrupted=Common();interrupted.Trace=new BeamSearchTrace("cancelled receiver proof",cancellation.Token);
            bool threw=false;try{Solve(interrupted,network,40);}catch(OperationCanceledException){threw=true;}
            Check(threw,"cancellation propagates before any cached decision");
            interrupted.Trace=new BeamSearchTrace("repaired receiver proof");Check(Solve(interrupted,network,40)!=null,"cancellation leaves no false failure entry");
        }
        long now=0;var deadline=Common();deadline.Trace=new BeamSearchTrace("expired receiver proof",default,TimeSpan.FromSeconds(1),()=>now);
        now=2*System.Diagnostics.Stopwatch.Frequency;bool expired=false;try{Solve(deadline,network,40);}catch(BeamSearchDeadlineException){expired=true;}
        Check(expired,"deadline propagates before publishing receiver proof");
        deadline.Trace=new BeamSearchTrace("repaired deadline proof");Check(Solve(deadline,network,40)!=null,"deadline leaves no false failure entry");
    }
    static void TerminalFailures()
    {
        var input=Common();var network=Network();
        var result=new TransferNetworkResult {Input=network,Beams=network.Beams.Select(b=>new TowerLayout()).ToArray(),
            Bays=new UpperTransferPlan[network.Bays.Length],SlabCoveredLengthMm=2450};
        var key=new CheckedPrefixKey(input,result);var cache=new NetworkPrefixCache();
        var passed=new NetworkPrefixCache.Entry {Passed=true,Diagnostics=new TowerSearchDiagnostics(270)};
        cache.Store(key,passed);Check(cache.Get(key,false,out _),"ordinary passing prefix remains reusable");
        Check(!cache.Get(key,true,out _),"a terminal positive cannot replace resolved drawings");
        cache=new NetworkPrefixCache();cache.Store(key,passed,true);Check(!cache.Get(key,out _),"terminal positives are not stored as pass-only entries");
        cache.Store(key,new NetworkPrefixCache.Entry {Passed=false,Diagnostics=new TowerSearchDiagnostics(270)},true);
        Check(cache.Get(key,true,out var rejected) && !rejected.Passed,"terminal failure needs no drawing snapshot and is reusable");
        network.BeamSpacingsMm[0]=275;Check(!cache.Get(new CheckedPrefixKey(input,result),true,out _),"terminal failure retains exact spacing dependencies");
    }
    internal static void Run()
    {Records();SearchParity();FaultAndInterruptionGuards();TerminalFailures();Console.WriteLine("PASS "+checks.ToString(CultureInfo.InvariantCulture)+" receiver proof cache checks: exhausted ceilings, adaptive guards, exact uncached parity and terminal failures");}
}
