using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class StandaloneNearFailureBatchTests
{
    static int checks;
    static readonly MethodInfo planParity=typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnosticParity=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Standalone near failure batch: "+message);}
    static TowerInput Input(double length,bool custom=true,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20)
    {
        var input=new TowerInput {LengthMm=length,TotalLineLoad=12,LegCapacityKn=70,PrimaryMaterial=material,DeflectionDenominator=270,
            StockLengthsMm=new[] {1250.0,1333,1800,2450,2679,3900,4200,4901,6000},AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,
            StockEndSetbackMm=150,DiagnosticOffsetMm=.125,Trace=new BeamSearchTrace("near state counted rejection parity")};
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static void Compare(TowerInput expected,TowerInput actual,double[] positions,double load)
    {
        var a=TowerStockPlanner.Find(expected,positions,load,new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false));
        var cache=new TowerStockPlanner.FindWorkCache();var b=TowerStockPlanner.Find(actual,positions,load,cache);
        planParity.Invoke(null,new object[] {a,b});checks++;
        diagnosticParity.Invoke(null,new object[] {expected.Diagnostics,actual.Diagnostics});checks++;
        foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.PrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
            Check(expected.Trace.Count(counter)==actual.Trace.Count(counter),"every completed response/diagnostic count matches the original per-stock loop");
        var replay=actual.Diagnostics.Fork();actual.Diagnostics=replay;
        planParity.Invoke(null,new object[] {a,TowerStockPlanner.Find(actual,positions,load,cache)});checks++;
        diagnosticParity.Invoke(null,new object[] {expected.Diagnostics,replay});checks++;
        Check(cache.PlanHits==1,"complete-plan replay retains all counted rejection evidence");
    }
    static void FullFindParity()
    {
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(bool lazy in new[] {false,true})foreach(double setback in new[] {1e-9,PortableTestRuntime.NextDown(150.0),150.0,PortableTestRuntime.NextUp(150.0),300.0})
        foreach(double q in new[] {.125,20.0,35.0,90.0})
        {
            foreach(var positions in new[] {new[] {750.0,1750,3350,4350,5850},new[] {150.0,1150,3350,4350,5850}})
            {
                var a=Input(6000,custom,material);var b=Input(6000,custom,material);a.StockEndSetbackMm=b.StockEndSetbackMm=setback;
                a.Diagnostics.LazyLocations=b.Diagnostics.LazyLocations=lazy;Compare(a,b,positions,q);
            }
        }
        foreach(double length in new[] {PortableTestRuntime.NextDown(3900.0),3900.0,PortableTestRuntime.NextUp(3900.0)})
        foreach(double last in new[] {PortableTestRuntime.NextDown(3750.0),3750.0,PortableTestRuntime.NextUp(3750.0),3900.0})
        {
            var a=Input(length);var b=Input(length);Compare(a,b,new[] {750.0,1750,last},35);
        }
        foreach(double q in new[] {22/.75,PortableTestRuntime.NextDown(22/.75),PortableTestRuntime.NextUp(22/.75),22/.75*(1-2e-6),22/.75*(1+2e-6)})
            Compare(Input(3900),Input(3900),new[] {750.0,1750,3750},q);
        var noStocks=Input(1000);noStocks.StockLengthsMm=new[] {2450.0};var same=Input(1000);same.StockLengthsMm=new[] {2450.0};
        Compare(noStocks,same,new[] {750.0,900},90);
    }
    static void RecordedGeometryAndFaults()
    {
        var originalTrace=new BeamSearchTrace("ordinary diagnostics");var recordedTrace=new BeamSearchTrace("recorded diagnostics");
        var original=new TowerSearchDiagnostics(270,trace:originalTrace);var recorded=new TowerSearchDiagnostics(270,trace:recordedTrace);
        foreach(var d in new[] {original,recorded})
        {d.Geometry("other first","first unrelated failure","advice");d.Geometry("same","first tied example","first advice");}
        for(int i=0;i<513;i++)original.Geometry("same","later tied example","later advice");
        recorded.GeometryRecorded("same","later tied example","later advice",513);
        recorded.GeometryRecorded("absent","must not create a zero-count failure","advice",0);
        diagnosticParity.Invoke(null,new object[] {original,recorded});checks++;
        Check(originalTrace.Count(BeamSearchCounter.RejectionChecks)==recordedTrace.Count(BeamSearchCounter.RejectionChecks),"batch count has unchanged pending-counter provenance");
        originalTrace.Finish();recordedTrace.Finish();
        Check(originalTrace.Count(BeamSearchCounter.RejectionChecks)==recordedTrace.Count(BeamSearchCounter.RejectionChecks),"counter completion flush does not duplicate a recorded batch");
        using(var cancellation=new CancellationTokenSource())
        {
            originalTrace=new BeamSearchTrace("ordinary partial",cancellation.Token);recordedTrace=new BeamSearchTrace("recorded partial",cancellation.Token);
            original=new TowerSearchDiagnostics(270,trace:originalTrace);recorded=new TowerSearchDiagnostics(270,trace:recordedTrace);
            int completed=0;var injected=new OperationCanceledException("Original cancellation exception",cancellation.Token);
            Exception caught=null;
            try
            {
                try
                {
                    for(int i=0;i<37;i++)
                    {recorded.CheckCancellation();original.Geometry("same","partial example","advice");completed++;}
                    cancellation.Cancel();throw injected;
                }
                finally{recorded.GeometryRecorded("same","partial example","advice",completed);}
            }
            catch(Exception error){caught=error;}
            Check(ReferenceEquals(caught,injected),"flushing visited failures preserves the original exception object after cancellation");
            diagnosticParity.Invoke(null,new object[] {original,recorded});checks++;
            Check(originalTrace.Count(BeamSearchCounter.RejectionChecks)==37 && recordedTrace.Count(BeamSearchCounter.RejectionChecks)==37,"already visited partial failures survive cancellation exactly");
            bool stopped=false;try{recordedTrace.Increment(BeamSearchCounter.RejectionChecks);}catch(OperationCanceledException){stopped=true;}
            Check(stopped && recordedTrace.Count(BeamSearchCounter.RejectionChecks)==37,"ordinary counter increments still check cancellation before recording");
        }
        var parallel=new BeamSearchTrace("recorded thread counter flush");
        System.Threading.Tasks.Parallel.For(0,4,_=>{parallel.CheckCancellation();parallel.IncrementRecorded(BeamSearchCounter.RejectionChecks,513);});
        Check(parallel.Count(BeamSearchCounter.RejectionChecks)==2052,"separate worker batches contribute their exact counts before completion");
        parallel.Finish();Check(parallel.Count(BeamSearchCounter.RejectionChecks)==2052,"thread counter flush retains each event exactly once");
        var faultDiagnostic=new TowerSearchDiagnostics(270,trace:new BeamSearchTrace("faulted diagnostic batch"));
        var fault=new InvalidOperationException("Original stock traversal fault");Exception actualFault=null;
        try{try{faultDiagnostic.CheckCancellation();throw fault;}finally{faultDiagnostic.GeometryRecorded("visited","original visited failure","advice",1);}}
        catch(Exception error){actualFault=error;}
        Check(ReferenceEquals(fault,actualFault),"the diagnostic flush does not replace an original traversal fault");
    }
    static TowerInput Timed(CancellationTokenSource cancellation)
    {
        var input=Input(6000);BeamSearchTrace trace=null;
        trace=new BeamSearchTrace("original explicit-budget geometry recording",cancellation.Token,TimeSpan.FromHours(1),()=>
        {if(trace!=null && trace.Count(BeamSearchCounter.RejectionChecks)>=13)cancellation.Cancel();return 0;});
        input.Trace=trace;input.Diagnostics=new TowerSearchDiagnostics(270,trace:trace);return input;
    }
    static void ExplicitBudgetAndCancellation()
    {
        using(var a=new CancellationTokenSource())using(var b=new CancellationTokenSource())
        {
            var expected=Timed(a);var actual=Timed(b);var positions=new[] {750.0,1750,3350,4350,5850};
            bool stoppedA=false,stoppedB=false;
            try{TowerStockPlanner.Find(expected,positions,35,new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false));}catch(OperationCanceledException){stoppedA=true;}
            try{TowerStockPlanner.Find(actual,positions,35,new TowerStockPlanner.FindWorkCache());}catch(OperationCanceledException){stoppedB=true;}
            Check(stoppedA&&stoppedB,"both actual stock loops stop at the original diagnostic cancellation checkpoint");
            diagnosticParity.Invoke(null,new object[] {expected.Diagnostics,actual.Diagnostics});checks++;
            Check(expected.Trace.Count(BeamSearchCounter.RejectionChecks)==actual.Trace.Count(BeamSearchCounter.RejectionChecks),"explicit budgets preserve the original partial rejection sequence");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var input=Input(6000);input.Trace=new BeamSearchTrace("cancelled ordinary stock batch",cancellation.Token);cancellation.Cancel();
            bool stopped=false;try{TowerStockPlanner.Find(input,new[] {750.0,1750,5850},35,new TowerStockPlanner.FindWorkCache());}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"a cancelled stock search never reaches batched failure publication or a retained plan");
        }
    }
    static void FullSelectedLayouts()
    {
        var compare=typeof(LazyEngineerTierTests).GetMethod("Layout",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var expected=Input(material==PrimaryBeamMaterial.DoubleH20 ? 2450 : 2500,custom,material);
            expected.TotalLineLoad=9;expected.TransverseLoadMoment=.625;expected.EngineerSearch=true;expected.ReuseSearchWork=false;
            var actual=Input(expected.LengthMm,custom,material);actual.TotalLineLoad=9;actual.TransverseLoadMoment=.625;actual.EngineerSearch=true;
            var a=TowerOptimizer.Optimize(expected);var b=TowerOptimizer.Optimize(actual);
            compare.Invoke(null,new object[] {a,b});checks++;
        }
    }
    internal static void Run(){FullFindParity();RecordedGeometryAndFaults();ExplicitBudgetAndCancellation();FullSelectedLayouts();Console.WriteLine("PASS "+checks+" exact standalone near-failure batch checks: ULP geometry, stock permissions, materials, full plans, diagnostics/counts, replay, original budget path and cancellation provenance");}
}
