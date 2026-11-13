using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class LocalWarmRepairTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Local warm repair: "+message);}
    static readonly MethodInfo solve=typeof(TransferNetworkOptimizer).GetMethod("Solve",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo warm=typeof(TransferNetworkOptimizer).GetMethod("TryWarmNetwork",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo options=typeof(TransferNetworkOptimizer).GetMethod("Options",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo optimize=typeof(TransferNetworkOptimizer).GetMethod("OptimizeCore",BindingFlags.NonPublic|BindingFlags.Static);
    static object Call(MethodInfo method,params object[] args)
    {try{return method.Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    static TowerInput Input()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,
        StockLengthsMm=new[] {1250.0,1800,2450},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,
        DeflectionDenominator=270,IgnoreUplift=true,AllowSeparateSlabFallback=false,NetworkSearchWorkers=1,
        Trace=new BeamSearchTrace("local warm receiver repair"),Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true)};
    static TransferNetworkInput Network()=>new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
        UpperSpacingMm=250,BeamSpacingsMm=new[] {250.0,250},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=Array.Empty<double>(),
        Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=700,BottomElevationMm=-700}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}}};
    static TowerLayout Solve(TowerInput input,TransferNetworkInput network,int beam,UpperTransferPlan bay,double coverage,TowerLayout previous=null)
        =>(TowerLayout)Call(solve,input,network,beam,beam==0 ? null : bay,beam==0 ? bay : null,coverage,0.0,new[] {new[] {coverage}},
            new TransferredSlabOptimizer.GeometryCache(),int.MaxValue,(double?)2000,0.0,null,previous);
    static TransferNetworkResult Warm(TowerInput input,TransferNetworkInput network,double[][] covers=null)
        =>(TransferNetworkResult)Call(warm,input,network,covers??new[] {new[] {2450.0}},new TransferredSlabOptimizer.GeometryCache());
    static TransferNetworkResult Previous(TowerInput input,TransferNetworkInput network,double coverage=2450)
    {
        var plans=((System.Collections.Generic.IEnumerable<UpperTransferPlan>)Call(options,input,network,0,488.0,"independent warm fixture")).ToArray();
        var bay=plans.FirstOrDefault(p=>p.StockLengthMm==1800 && p.NearFootMm==350);
        Check(bay!=null,"the old slab plan belongs to the ordinary retained option set");
        var beams=Enumerable.Range(0,2).Select(i=>Solve(input,network,i,bay,coverage)).ToArray();
        if(beams.Any(b=>b==null))throw input.Diagnostics.Exception("Warm fixture receivers failed");
        var result=new TransferNetworkResult {Input=network,Beams=beams,Bays=new[] {bay},BeamCentresMm=new[] {0.0,3000},
            SlabCoveredLengthMm=coverage,RowPhaseMm=0,SlabLoadKn=beams.Sum(b=>b.Transfer.SlabLoadKn)};
        Check(CrossBeamSupportOptimizer.Complete(input,result),"the old wider receiver network passes complete checks");
        return result;
    }
    static string Geometry(TowerLayout layout)=>layout.WidthMm.ToString("R",CultureInfo.InvariantCulture)+"|"+layout.TransverseOffsetMm.ToString("R",CultureInfo.InvariantCulture)
        +"|"+string.Join(",",layout.PositionsMm.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+"|"+string.Join(",",layout.Joined);
    static void MaximaProbe()
    {
        var input=Input();var previous=Previous(input,Network());var requested=previous.Input.WithSpacings();requested.BeamSpacingsMm[0]=305;
        var probe=typeof(TransferNetworkOptimizer).GetMethod("TryWarmMaxima",BindingFlags.NonPublic|BindingFlags.Static);
        var restored=(TransferNetworkResult)Call(probe,input,requested,previous);
        Check(restored!=null && restored.Input.BeamSpacingsMm.SequenceEqual(requested.BeamSpacingsMm),"one warm-only probe can restore the entire requested spacing vector");
        Check(input.Trace.Count(BeamSearchCounter.WarmMaximaProbes)==1 && input.Trace.Count(BeamSearchCounter.WarmMaximaSuccesses)==1,"the all-max probe reports complete checked success");
        ControlledPerformanceTests.Validate(input,restored);
        var unavailable=requested.WithSpacings();unavailable.Bays=new[] {new TransferBayRow {WidthMm=2400,ThicknessMm=200}};
        var accepted=input.Diagnostics;long trials=input.Trace.Count(BeamSearchCounter.NetworkSpacingTrials);
        Check(Call(probe,input,unavailable,previous)==null,"a missing old slab option makes the speculative probe decline");
        Check(ReferenceEquals(input.Diagnostics,accepted) && input.Trace.Count(BeamSearchCounter.NetworkSpacingTrials)==trials,"a failed warm-only probe neither replaces accepted diagnostics nor launches a full network trial");
        var fallback=(TransferNetworkResult)Call(optimize,input,unavailable,false);
        Check(fallback!=null,"the ordinary full network path remains available after a rejected warm-only probe");
        ControlledPerformanceTests.Validate(input,fallback);
        var network=Network();network.BeamSpacingsMm[0]=305;
        network.CrossingBeams=new[] {700.0,1700}.Select(x=>new TransferCrossingBeamRow {BayNumber=1,PositionMm=x,WidthMm=500,DepthMm=200,BottomElevationMm=-200,SupportMode=CrossBeamSupportMode.LegacySlabRows}).ToArray();
        network.CrossingSpacingsMm=new[] {350.0,350};
        var fast=Input();var actual=TransferNetworkOptimizer.Optimize(fast,network);
        var reference=Input();reference.WarmStartNetwork=false;reference.LocalNetworkSearch=false;
        var expected=TransferNetworkOptimizer.Optimize(reference,network);
        Check(fast.Trace.Count(BeamSearchCounter.WarmMaximaSuccesses)==1,"spacing recovery accepts a complete all-max warm seed");
        Check(fast.Trace.Count(BeamSearchCounter.NetworkSpacingTrials)>=2
            && fast.Trace.Count(BeamSearchCounter.NetworkSpacingTrials)<reference.Trace.Count(BeamSearchCounter.NetworkSpacingTrials),"a successful all-max probe skips category trials while retaining the ordinary final ranked search");
        Check(ControlledPerformanceTests.Fingerprint(actual)==ControlledPerformanceTests.Fingerprint(expected)
            && actual.Input.BeamSpacingsMm.SequenceEqual(expected.Input.BeamSpacingsMm) && actual.Input.SlabSpacingsMm.SequenceEqual(expected.Input.SlabSpacingsMm),
            "all-max warm recovery retains the final ordinary ranked geometry, loads and spacings");
        ControlledPerformanceTests.Validate(fast,actual);
        Check(fast.Trace.Snapshot().Contains("Warm networks missing an old slab option:")
            && fast.Trace.Snapshot().Contains("Local warm receiver repair attempts:"),"appended warm counters appear in worker diagnostics");
        long now=0;var deadline=Input();deadline.Trace=new BeamSearchTrace("expired maximum-spacing probe",default,TimeSpan.FromSeconds(1),()=>now);
        now=2*System.Diagnostics.Stopwatch.Frequency;bool expired=false;try{Call(probe,deadline,requested,previous);}catch(BeamSearchDeadlineException){expired=true;}
        Check(expired,"the warm-only probe propagates an explicit deadline without converting it into structural failure");
    }
    internal static void Run()
    {
        var input=Input();var oldNetwork=Network();var previous=Previous(input,oldNetwork);
        var next=oldNetwork.WithSpacings();next.BeamSpacingsMm[0]=305;
        Check(Solve(input,next,0,previous.Bays[0],2450,previous.Beams[0])==null,"a coarser beam pitch rejects the previous fixed receiver");
        input.NetworkWarmStart=previous;input.FirstCompleteNetworkOnly=true;
        input.LocalNetworkSearch=false;Check(Warm(input,next)==null,"the original fixed-only warm path rejects that network");
        input.LocalNetworkSearch=true;var repaired=Warm(input,next);
        if(repaired==null)throw input.Diagnostics.Exception("Local warm fixture did not repair");
        Check(Geometry(repaired.Beams[0])!=Geometry(previous.Beams[0]),"repair changes the failed receiver geometry");
        Check(Geometry(repaired.Beams[1])==Geometry(previous.Beams[1]),"the passing neighbor keeps its original support geometry");
        Check(repaired.Beams[0].StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(previous.Beams[0].StockPlan.Pieces.Select(p=>p.StockLengthMm)),"repair retains the previous permitted stock topology");
        Check(!repaired.Beams[0].MinimumLegCountProven,"a local repair seed does not claim a global minimum leg count");
        Check(input.Trace.Count(BeamSearchCounter.WarmNetworkCandidates)>0,"only a complete checked repair is published as a warm seed");
        Check(input.Trace.Count(BeamSearchCounter.WarmReceiverRepairAttempts)>0 && input.Trace.Count(BeamSearchCounter.WarmReceiverRepairSuccesses)>0,"receiver repair attempts and local successes are counted separately");
        ControlledPerformanceTests.Validate(input,repaired);
        foreach(var member in repaired.Beams.SelectMany(b=>b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers)))
        {
            var dense=LoadedBeamAnalysis.AnalyzeDenseReference(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,270);
            Check(member.Analysis.DeflectionEvaluated && Math.Abs(member.Analysis.MomentKnM-dense.MomentKnM)<1e-7
                && Math.Abs(member.Analysis.ShearKn-dense.ShearKn)<1e-7 && Math.Abs(member.Analysis.DeflectionMm-dense.DeflectionMm)<1e-7,
                "every repaired member retains independent full numerical checks");
        }
        input.FirstCompleteNetworkOnly=false;Check(Warm(input,next)==null,"the final ranked pass does not use local warm repair");
        input.NetworkWarmStart=repaired;long warmBefore=input.Trace.Count(BeamSearchCounter.WarmNetworkCandidates),solvesBefore=input.Trace.Count(BeamSearchCounter.NetworkSolves);
        var ranked=(TransferNetworkResult)Call(optimize,input,next,false);
        Check(input.Trace.Count(BeamSearchCounter.WarmNetworkCandidates)==warmBefore && input.Trace.Count(BeamSearchCounter.NetworkSolves)>solvesBefore,"final optimization runs the ordinary receiver and network ranking path");
        var reference=Input();reference.WarmStartNetwork=false;reference.LocalNetworkSearch=false;
        var original=(TransferNetworkResult)Call(optimize,reference,next,false);
        Check(ControlledPerformanceTests.Fingerprint(ranked)==ControlledPerformanceTests.Fingerprint(original),"the final ranked geometry and loads match the ordinary exhaustive path");
        ControlledPerformanceTests.Validate(input,ranked);
        input.FirstCompleteNetworkOnly=true;input.NetworkWarmStart=previous;
        var unavailable=next.WithSpacings();
        unavailable.Bays=new[] {new TransferBayRow {WidthMm=2400,ThicknessMm=200}};
        Check(Warm(input,unavailable)==null,"an old slab plan absent from current Options cannot be invented by repair");
        Check(input.Trace.Count(BeamSearchCounter.WarmSlabOptionMissing)>0,"a missing old slab option is visible in diagnostics");
        var rejected=next.WithSpacings();rejected.Beams=next.Beams.Select(b=>new TransferBeamRow {WidthMm=b.WidthMm,DepthMm=20000,BottomElevationMm=-20000}).ToArray();
        Check(Warm(input,rejected)==null,"failed local repairs return to the ordinary network fallback");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();input.Trace=new BeamSearchTrace("cancelled local repair",cancellation.Token);bool threw=false;
            try{Warm(input,next);}catch(OperationCanceledException){threw=true;}
            Check(threw,"warm repair preserves user cancellation");
        }
        var custom=Input();custom.AllowCustomH20Lengths=true;custom.MaximumConcreteEndOverhangMm=50;
        var customPrevious=Previous(custom,oldNetwork,2400);custom.NetworkWarmStart=customPrevious;custom.FirstCompleteNetworkOnly=true;
        var maxima=typeof(TransferNetworkOptimizer).GetMethod("TryWarmMaxima",BindingFlags.NonPublic|BindingFlags.Static);
        var customRestored=(TransferNetworkResult)Call(maxima,custom,next,customPrevious);
        Check(customRestored!=null && customRestored.Beams.All(b=>b.StockPlan.Pieces.Any(p=>p.StockLengthMm==2400)),"the warm-only probe uses restored project custom permissions and their normal cover universe");
        custom.AllowCustomH20Lengths=false;
        Check(Warm(custom,next)==null,"a prior custom primary cover is not inserted into a catalogue-only repair");
        var spanInput=Input();spanInput.LengthMm=3000;
        var spans=next.WithSpacings();spans.Beams=next.Beams.Select(b=>new TransferBeamRow {WidthMm=b.WidthMm,DepthMm=b.DepthMm,BottomElevationMm=b.BottomElevationMm,SpanLengthMm=2450}).ToArray();
        var selected=typeof(TransferNetworkOptimizer).GetMethod("SolveReceiver",BindingFlags.NonPublic|BindingFlags.Static);
        var forbidden=Call(selected,spanInput,spans,0,null,previous.Bays[0],2450.0,0.0,new[] {new[] {2400.0}},
            new TransferredSlabOptimizer.GeometryCache(),int.MaxValue,(double?)2000,0.0,null,null,new[] {2400.0});
        Check(forbidden==null && spanInput.Trace.Count(BeamSearchCounter.NetworkSolves)==0,"an old stock sequence is filtered after per-beam span covers are regenerated");
        MaximaProbe();
        Console.WriteLine("PASS "+checks+" local warm repair checks: complete repaired seed, unchanged neighbor, ordinary final ranking, fallback, cancellation and catalogue permissions");
    }
}
