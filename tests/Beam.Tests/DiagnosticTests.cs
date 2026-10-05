using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class DiagnosticTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Diagnosis: "+message);}
    public static void Run()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,Depth=200,Width=300,BeamLengthMm=2125,StockLengthsText="1250",MaximumConcreteEndOverhangMm=0};
        var before=vm.BuildDiagnosticReport("taking too long","StockLengthsText = not-a-length");
        Check(before.Contains("No tower search has been started") && before.Contains("taking too long") && before.Contains("not-a-length"),"pre-search report includes remarks and raw field text");
        Check(!vm.IsOptimizing && vm.TowerResult==null,"diagnosis does not run a search");
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();var failed=vm.BuildDiagnosticReport();
        Check(!vm.IsCalculationValid && failed.Contains("Search outcome: Rejected input/search") && failed.Contains("cannot cover"),"failed search inputs and error are retained");
        vm.AllowCustomH20Lengths=true;vm.OptimizeTowersAsync().GetAwaiter().GetResult();var result=vm.TowerResult;var report=vm.BuildDiagnosticReport("slow\nsecond line");
        Check(vm.IsCalculationValid && report.Contains("Search outcome: Succeeded") && report.Contains("slow\nsecond line"),"successful capture includes multiline remarks");
        Check(report.Contains("Completed catalogue-pass seconds:") && report.Contains("Completed custom-pass seconds:") && report.Contains("Primary member analyses:"),"custom phases and work counters included");
        Check(report.Contains("AllowCustomH20Lengths = True") && report.Contains("Catalogue pass:"),"immutable start inputs preserve enabled option and rejected catalogue pass");
        Check(ReferenceEquals(result,vm.TowerResult) && vm.IsCalculationValid,"report generation preserves current result");
        vm.BeamLengthMm=2250;var stale=vm.BuildDiagnosticReport();
        Check(stale.Contains("Current model inputs differ from search inputs: True") && stale.Contains("BeamLengthMm = 2125") && stale.Contains("BeamLengthMm = 2250"),"changed inputs distinguished from captured search inputs");
        var oldCulture=CultureInfo.CurrentCulture;
        try {CultureInfo.CurrentCulture=new CultureInfo("fr-FR");vm.Width=300.125;vm.StockLengthsText="bad";vm.Depth=double.NaN;var invalid=vm.BuildDiagnosticReport();Check(invalid.Contains("Width = 300.125") && invalid.Contains("Depth = NaN") && invalid.Contains("StockLengthsText = bad"),"invalid values captured without validation, using invariant numbers");}
        finally {CultureInfo.CurrentCulture=oldCulture;}
        var traced=new TowerInput {LengthMm=2500,StockLengthsMm=new[] {2500.0},TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350,Trace=new BeamSearchTrace("captured input")};
        var plain=new TowerInput {LengthMm=2500,StockLengthsMm=new[] {2500.0},TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350};
        var a=TowerOptimizer.Optimize(traced);var b=TowerOptimizer.Optimize(plain);
        Check(a.LegCount==b.LegCount && a.BayCount==b.BayCount && a.PositionsMm.SequenceEqual(b.PositionsMm) && a.StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(b.StockPlan.Pieces.Select(p=>p.StockLengthMm)) && Math.Abs(a.Utilization-b.Utilization)<1e-12,"instrumentation preserves optimizer result and ranking");
        var trace=new BeamSearchTrace("thread-safe test");
        var write=Task.Run(()=> {for(int i=0;i<10000;i++){trace.Increment(BeamSearchCounter.NetworkSolves);trace.Work("Network checks");}});
        for(int i=0;i<100;i++)Check(trace.Snapshot().Contains("Search outcome: Running"),"running snapshot can be read concurrently");
        write.GetAwaiter().GetResult();trace.Complete("Succeeded",summary:"test result");var done=trace.Snapshot();
        Check(done.Contains("Receiving-beam solves: 10000") && done.Contains("test result") && done.Contains("Search outcome: Succeeded"),"atomic counters and final outcome retained");
        TestCancellation();
        Console.WriteLine($"Passed {checks} beam diagnosis checks.");
    }
    static void TestCancellation()
    {
        var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Func<TowerInput> input=()=>new TowerInput {LengthMm=2500,StockLengthsMm=new[] {2500.0},TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350,AllowCustomH20Lengths=true,Trace=new BeamSearchTrace("cancelled input",cancellation.Token)};
        Action<Action,string> cancelled=(run,name)=> {try {run();throw new Exception(name+" ignored cancellation");}catch(OperationCanceledException){Check(true,name+" honors cancellation");}};
        cancelled(()=>TowerOptimizer.Optimize(input()),"beam-only search");
        var transfer=input();transfer.TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=2,RightBayMm=3900,RightAreaLoadKnM2=8,UpperSpacingMm=750};
        cancelled(()=>TransferredSlabOptimizer.Optimize(transfer),"transferred slab search");
        var network=new TransferNetworkInput {Beams=new[] {new TransferBeamRow(),new TransferBeamRow()},Bays=new[] {new TransferBayRow()},UpperSpacingMm=750};
        cancelled(()=>TransferNetworkOptimizer.Optimize(input(),network),"network search");
        cancelled(()=>TowerStockPlanner.CoverageCounts(50000,new[] {25.0,1250.0},cancellation.Token),"coverage DP");
        var active=new CancellationTokenSource();var search=input();search.Trace=new BeamSearchTrace("running pass",active.Token);int passes=0;
        cancelled(()=>TowerStockPlanner.SearchWithCustom(search,()=> {passes++;active.Cancel();search.Trace.Increment(BeamSearchCounter.PrimaryAnalyses);return new object();},(a,b)=>false),"cancellation inside catalogue pass");
        Check(passes==1 && search.AllowCustomH20Lengths && !search.Trace.Snapshot().Contains("Catalogue pass: System.OperationCanceledException"),"cancellation exits without custom fallback or rejected-pass classification");
        active.Dispose();cancellation.Dispose();
    }

}
