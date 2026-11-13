using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class PreparedUniformScalarTests
{
    static readonly MethodInfo prepare=typeof(CrossBeamSupportOptimizer).GetMethod("PrepareRecheck",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo compareDiagnostics=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Prepared uniform scalar: "+message);}
    sealed class Fixture
    {
        internal TowerInput Input;
        internal TransferNetworkInput Network;
        internal TowerLayout Probe;
        internal CrossBeamSupportResult[] Supports;
        internal TransferredSlabOptimizer.SearchWorkCache Work;
        internal BeamDistributedLoad[] Uniform;
        internal LoadedBeamAnalysis Unit;
        internal readonly double[] Positions={450,3450};
        internal TransferredSlabOptimizer.Geometry Geometry(double[] positions)=>new TransferredSlabOptimizer.Geometry {Positions=positions,Joined=Enumerable.Repeat(true,positions.Length-1).ToArray(),
            Stock=new[] {new TowerStockPiece {StockLengthMm=3900,FirstSupport=0,LastSupport=positions.Length-1}},MemberSupports=new[] {positions}};
        internal Func<TransferredSlabOptimizer.Geometry,double,bool> Prepare()
        {
            try{return (Func<TransferredSlabOptimizer.Geometry,double,bool>)prepare.Invoke(null,new object[] {Input,Network,0,Probe,Supports});}
            catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        internal TransferredSlabOptimizer.PreparedPrimaryKey Key(double[] positions=null)=>new TransferredSlabOptimizer.PreparedPrimaryKey(new TransferredSlabOptimizer.PrimaryKey(3900,positions??Positions,Uniform),Input);
        internal void ResetDiagnostics()=>Input.Diagnostics=new TowerSearchDiagnostics(Input.DeflectionDenominator,trace:Input.Trace,ignoreUplift:false);
    }
    static Fixture Create(bool optimize,bool deferred,int criterion)
    {
        var work=new TransferredSlabOptimizer.SearchWorkCache();
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,DeferCrossingChecks=deferred,LengthMm=6900,LegCapacityKn=70,DeflectionDenominator=criterion,
            OptimizeCrossingSearch=optimize,NetworkNumerics=work,Trace=new BeamSearchTrace("prepared cached uniform scalar parity")};
        var crossing=new TransferCrossingBeamRow {BayNumber=1,PositionMm=5000,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150};
        var network=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150,BottomElevationMm=-1150},new TransferBeamRow {WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}},
            Bays=new[] {new TransferBayRow {WidthMm=2000,ThicknessMm=200}},CrossingBeams=new[] {crossing}};
        var plan=EngineerPrimaryPlan.Create(input,network,0,false,false);
        var probe=new TowerLayout {LengthMm=6900,PositionsMm=new[] {450.0,6450},Joined=new[] {true},WidthMm=1524,
            StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StockLengthMm=6900,FirstSupport=0,LastSupport=1}}},
            Transfer=new TransferredSlabResult {PrimaryPlan=plan,RowsMm=Array.Empty<double>(),CaseNames=new[] {"Full"},LowerCases=new[] {Array.Empty<TransferMember>()},PrimaryCases=new[] {Array.Empty<TransferMember>()},UpperMembers=Array.Empty<TransferMember>()}};
        // A resolved zero transfer outside this member isolates its own uniform
        // beam load while still exercising the real receiver preparation path.
        var supports=new[] {new CrossBeamSupportResult {Input=crossing,Bearings=Array.Empty<CrossBeamBearing>()}};
        var local=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[] {network.Beams[0]}},Beams=new[] {probe},BeamCentresMm=new[] {0.0}};
        var resolved=plan.Resolve(input,local,0,supports);var uniform=resolved.Loads(0,3900,0);
        Check(uniform.Length==1 && uniform[0].StartMm==0 && uniform[0].EndMm==3900,"receiver fixture resolves one complete uniform stock load");
        var fixture=new Fixture {Input=input,Network=network,Probe=probe,Supports=supports,Work=work,Uniform=uniform};fixture.ResetDiagnostics();
        fixture.Unit=LoadedBeamAnalysis.AnalyzeCombinedCached(3900,fixture.Positions,new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=1}},Array.Empty<BeamPointLoad>(),TowerOptimizer.PairEI,criterion,null);
        work.UniformShapes.Store(new TransferredSlabOptimizer.UniformShapeKey(3900,fixture.Positions,TowerOptimizer.PairEI,criterion),fixture.Unit);
        return fixture;
    }
    internal static void Run()
    {
        foreach(bool deferred in new[] {false,true})foreach(int criterion in new[] {270,500})
        {
            var expected=Create(false,deferred,criterion);var actual=Create(true,deferred,criterion);
            var reference=expected.Prepare();var optimized=actual.Prepare();
            Check(!ReferenceEquals(optimized,CrossBeamSupportOptimizer.FailedReceiver),"fixture reaches geometry-dependent prepared checking");
            Check(!reference(expected.Geometry(expected.Positions),0) && !optimized(actual.Geometry(actual.Positions),0),"provisional deferral never approves a failed resolved member");
            compareDiagnostics.Invoke(null,new object[] {expected.Input.Diagnostics,actual.Input.Diagnostics});
            Check(actual.Work.PreparedPrimaryChecks.TryGetValue(actual.Key(),out var scalar) && scalar.UniformFailure.HasValue && scalar.Analysis==null,"the shared failure retains exact scalars without a response object");
            // Simulate the previous full-response cache record in a fresh
            // diagnostic scope, then compare its ordinary replay with scalar replay.
            Check(expected.Work.PreparedPrimaryChecks.TryGetValue(expected.Key(),out var full),"reference stores the same exact failed member key");
            full.Analysis=expected.Unit.Scale(expected.Uniform[0].KnPerM);expected.Input.OptimizeCrossingSearch=true;
            expected.ResetDiagnostics();actual.ResetDiagnostics();
            Check(!expected.Prepare()(expected.Geometry(expected.Positions),0) && !actual.Prepare()(actual.Geometry(actual.Positions),0),"fresh preparations reuse the identical failed member");
            compareDiagnostics.Invoke(null,new object[] {expected.Input.Diagnostics,actual.Input.Diagnostics});
            Check(actual.Input.Trace.Count(BeamSearchCounter.PreparedPrimaryCacheHits)>0,"a new diagnostic scope replays cached scalar provenance");
            var close=new[] {450.0,1950,3450};
            Check(expected.Prepare()(expected.Geometry(close),0)==actual.Prepare()(actual.Geometry(close),0),"another support geometry receives its full independent checks");
            // A new passing shape needs only scalar decisions in preparation;
            // the selected result still builds its actual member responses later.
            expected=Create(false,deferred,criterion);actual=Create(true,deferred,criterion);
            foreach(var fixture in new[] {expected,actual})
            {
                var unit=LoadedBeamAnalysis.AnalyzeCombinedCached(3900,close,new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=1}},Array.Empty<BeamPointLoad>(),TowerOptimizer.PairEI,criterion,null);
                fixture.Work.UniformShapes.Store(new TransferredSlabOptimizer.UniformShapeKey(3900,close,TowerOptimizer.PairEI,criterion),unit);
            }
            Check(expected.Prepare()(expected.Geometry(close),0) && actual.Prepare()(actual.Geometry(close),0),"complete cached scalar decisions retain a passing resolved geometry");
            compareDiagnostics.Invoke(null,new object[] {expected.Input.Diagnostics,actual.Input.Diagnostics});
            Check(actual.Work.PreparedPrimaryChecks.TryGetValue(actual.Key(close),out var accepted) && accepted.Passed && accepted.Analysis==null && !accepted.UniformFailure.HasValue,"passing preparation retains its decision without a throwaway coefficient graph");
            Check(TransferredSlabOptimizer.TryCheckUniformPrimary(actual.Input,3900,close,actual.Uniform,Array.Empty<BeamPointLoad>(),actual.Work,70,out var values,out bool passed) && passed,"passing scalar unit is eligible and complete");
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++)TransferredSlabOptimizer.TryCheckUniformPrimary(actual.Input,3900,close,actual.Uniform,Array.Empty<BeamPointLoad>(),actual.Work,70,out values,out passed);
            Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warm passing scalar checks allocate no response, reaction or coefficient graph");
            using(var cancellation=new CancellationTokenSource())
            {
                actual.Input.Trace=new BeamSearchTrace("cancelled prepared scalar replay",cancellation.Token);var check=actual.Prepare();cancellation.Cancel();bool cancelled=false;
                try{check(actual.Geometry(actual.Positions),0);}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled,"cancellation precedes a reused prepared scalar failure");
            }
        }
        Console.WriteLine("PASS "+checks+" prepared uniform scalar checks: resolved/deferred acceptance, exact diagnostic replay, changed supports and cancellation");
    }
}
