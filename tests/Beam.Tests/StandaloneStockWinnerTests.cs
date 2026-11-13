using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class StandaloneStockWinnerTests
{
    static int checks;
    static readonly MethodInfo planParity=typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnosticParity=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Standalone DP winner: "+message);}
    static TowerInput Input(double length,bool custom=false,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20,int criterion=270)=>new TowerInput
    {
        LengthMm=length,TotalLineLoad=12,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1800,2100,2400,2450,3600,3900,4200,4900},
        AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,PrimaryMaterial=material,DeflectionDenominator=criterion,
        StockEndSetbackMm=150,IgnoreUplift=true,Trace=new BeamSearchTrace("Exact DP winner materialization")
    };
    static void Plan(TowerStockPlan expected,TowerStockPlan actual)
    {
        planParity.Invoke(null,new object[] {expected,actual});checks++;
        if(expected!=null)
        {
            Check(expected.PrimaryMaterial==actual.PrimaryMaterial,"material provenance");
            Check(expected.Schedule==actual.Schedule,"complete selected schedule and governing metadata");
        }
    }
    static void Parity()
    {
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(int criterion in new[] {270,500})foreach(double q in new[] {double.Epsilon,.125,6.125,35.0})
        foreach(double[] positions in new[] {new[] {150.0,1150,2750,3750},new[] {150.0,1150,1600,2250,3000,3600,4050,4850,5850}})
        {
            double length=positions[positions.Length-1]+150;
            var expectedInput=Input(length,custom,material,criterion);var actualInput=Input(length,custom,material,criterion);
            expectedInput.Diagnostics=new TowerSearchDiagnostics(criterion,trace:expectedInput.Trace,ignoreUplift:true);
            actualInput.Diagnostics=new TowerSearchDiagnostics(criterion,trace:actualInput.Trace,ignoreUplift:true);
            var baseline=new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false);var optimized=new TowerStockPlanner.FindWorkCache();
            var expected=TowerStockPlanner.Find(expectedInput,positions,q,baseline);var actual=TowerStockPlanner.Find(actualInput,positions,q,optimized);
            Plan(expected,actual);diagnosticParity.Invoke(null,new object[] {expectedInput.Diagnostics,actualInput.Diagnostics});checks++;
            foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.PrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
                Check(expectedInput.Trace.Count(counter)==actualInput.Trace.Count(counter),"all member analysis/cache/diagnostic calls remain in their original order");
            Plan(TowerStockPlanner.Find(Input(length,custom,material,criterion),positions,q),actual);
            var replayDiagnostic=new TowerSearchDiagnostics(criterion,trace:actualInput.Trace,ignoreUplift:true);actualInput.Diagnostics=replayDiagnostic;
            Plan(expected,TowerStockPlanner.Find(actualInput,positions,q,optimized));diagnosticParity.Invoke(null,new object[] {expectedInput.Diagnostics,replayDiagnostic});checks++;
        }
    }
    static void StableTiesAndOwnership()
    {
        var positions=new[] {150.0,1150,1600,2250,3000,3600,4050,4850,5850};var input=Input(6000);
        var baseline=new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false);var optimized=new TowerStockPlanner.FindWorkCache();
        var expected=TowerStockPlanner.Find(input,positions,double.Epsilon,baseline);var actual=TowerStockPlanner.Find(input,positions,double.Epsilon,optimized);Plan(expected,actual);
        Check(actual!=null && actual.Pieces.Length==2,"tie fixture has multiple passing two-stock paths");
        Check(actual.Pieces[0].StockLengthMm==1800 && actual.Pieces[1].StockLengthMm==4200,"equal utilization retains the original first DP path");
        Check(BitConverter.DoubleToInt64Bits(actual.Utilization)==0,"positive subnormal-load paths exercise exact utilization ties");
        actual.Reactions[0]+=1;actual.Pieces[0].StartMm+=25;Plan(expected,TowerStockPlanner.Find(input,positions,double.Epsilon,optimized));
        Check(optimized.PlanHits==1,"returned mutable plan cannot alter the cached winner");
        // At positive subnormal H20 load every feasible member passes. The interval path
        // performs the same member/diagnostic work, while losing transitions
        // no longer allocate reaction copies and piece histories.
        TowerStockPlanner.Find(input,positions,double.Epsilon,new TowerStockPlanner.FindWorkCache());
        long start=GC.GetAllocatedBytesForCurrentThread();TowerStockPlanner.Find(input,positions,double.Epsilon,new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false));
        long eager=GC.GetAllocatedBytesForCurrentThread()-start;
        start=GC.GetAllocatedBytesForCurrentThread();TowerStockPlanner.Find(input,positions,double.Epsilon,new TowerStockPlanner.FindWorkCache());long delayed=GC.GetAllocatedBytesForCurrentThread()-start;
        Check(delayed+1000<eager,"losing-state metadata is measurably omitted in a passing exact-tie fixture");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();input.Trace=new BeamSearchTrace("Cancelled winner cache hit",cancellation.Token);
            bool stopped=false;try{TowerStockPlanner.Find(input,positions,double.Epsilon,optimized);}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"cancellation still precedes cached winners");
        }
    }
    internal static void Run()
    {
        Parity();StableTiesAndOwnership();
        Console.WriteLine("PASS "+checks+" standalone DP winner checks: unchanged member/diagnostic order, exact selected metadata, stable ties, allocation, replay ownership and cancellation");
    }
}
