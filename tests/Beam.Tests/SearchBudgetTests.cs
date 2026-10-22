using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class SearchBudgetTests
{
    static int checks;
    static void Check(bool condition,string message) {checks++;if(!condition)throw new Exception("Search budget: "+message);}
    static void Deadline(Action action,string message)
    {
        try {action();throw new Exception("Deadline was ignored: "+message);}
        catch(BeamSearchDeadlineException) {Check(true,message);}
    }
    static TowerInput Common()=>new TowerInput {LengthMm=2500,StockLengthsMm=new[] {1250.0,1800,2500},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=350};
    static TransferNetworkInput Network()=>new TransferNetworkInput {UpperSpacingMm=250,
        Beams=Enumerable.Range(0,3).Select(_=>new TransferBeamRow {WidthMm=300,DepthMm=300}).ToArray(),
        Bays=Enumerable.Range(0,2).Select(_=>new TransferBayRow {WidthMm=1800,ThicknessMm=100}).ToArray()};
    static void Engineering(TransferNetworkResult result)
    {
        Check(result.Beams.Length==3 && result.Bays.Length==2 && result.Bays.All(b=>b!=null),"retained result includes the complete network");
        Check(result.SlabMovementMm>0 && !double.IsNaN(result.SlabMovementMm),"movement is finalized after exploration stops");
        foreach(var beam in result.Beams)
        {
            Check(beam.SearchTimeLimitReached && !beam.MinimumLegCountProven,"deadline result cannot claim a proven minimum");
            Check(beam.MaxLegKn<=70+1e-8 && beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7),"retained legs pass capacity and uplift checks");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"retained members have complete passing analyses");
                Check(Math.Abs(member.Analysis.Reactions.Sum()-member.Analysis.TotalLoadKn)<1e-7*Math.Max(1,member.Analysis.TotalLoadKn),"retained member equilibrium");
            }
        }
        Check(result.Summary.Contains("search time limit"),"result discloses the search limit");
    }
    static void RetainNetwork(int workers)
    {
        var input=Common();input.NetworkSearchWorkers=workers;input.AllowCustomH20Lengths=true;
        BeamSearchTrace trace=null;
        trace=new BeamSearchTrace("complete network",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>trace!=null && trace.Count(BeamSearchCounter.CompleteNetworkCandidates)>0 ? 2*Stopwatch.Frequency : 0);
        input.Trace=trace;
        var result=TransferNetworkOptimizer.Optimize(input,Network());
        Check(trace.BudgetReached && result.SearchTimeLimitReached && input.AllowCustomH20Lengths,"network expiry retains a checked solution and restores the custom flag");
        Engineering(result);
        string snapshot=trace.Snapshot();
        Check(snapshot.Contains("Stage ReceivingBeams:") && snapshot.Contains("Stage Movement:") && snapshot.Contains("Stage PrimaryAnalysis:") && snapshot.Contains("Network workers: "+workers),"diagnostics identify worker count and expensive stages");
        Check(!snapshot.Contains("Custom pass:"),"deadline does not start a second custom pass");
    }
    static void RetainStandaloneLayouts()
    {
        foreach(double endAllowance in new[] {0.0,75})
        {
            var beam=new TowerInput {LengthMm=2500,StockLengthsMm=new[] {2500.0},TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350,MaximumConcreteEndOverhangMm=endAllowance};
            BeamSearchTrace trace=null;
            trace=new BeamSearchTrace("beam deadline",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>trace!=null && trace.Count(BeamSearchCounter.TowerCandidates)>=2 ? 2*Stopwatch.Frequency : 0);
            beam.Trace=trace;var result=TowerOptimizer.Optimize(beam);
            Check(trace.BudgetReached && result.SearchTimeLimitReached && !result.MinimumLegCountProven && result.Utilization<=1+1e-8,"beam deadline retains a fully checked layout with and without end allowance");
        }
        foreach(bool tiered in new[] {false,true})
        {
            var transfer=Common();transfer.SearchByLegTier=tiered;
            transfer.TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=2,UpperSpacingMm=250,RightBayMm=1800,RightAreaLoadKnM2=2.5};
            BeamSearchTrace trace=null;long firstPassingAttempt=-1;
            trace=new BeamSearchTrace("transfer deadline",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>
            {
                if(trace!=null && trace.Count(BeamSearchCounter.TransferLayoutsBuilt)>0)
                {
                    long attempts=trace.Count(BeamSearchCounter.TransferCandidates);
                    if(firstPassingAttempt<0)firstPassingAttempt=attempts;
                    if(attempts>firstPassingAttempt)return 2*Stopwatch.Frequency;
                }
                return 0;
            });
            transfer.Trace=trace;var result=TransferredSlabOptimizer.Optimize(transfer);
            Check(trace.BudgetReached && result.SearchTimeLimitReached && result.Utilization<=1+1e-8 && result.Transfer.SlabMovementMm>0,"both transfer traversals retain complete checked layouts and finalized movement");
        }
    }
    public static void Run()
    {
        var batched=new BeamSearchTrace("parallel candidate counters");
        System.Threading.Tasks.Parallel.For(0,4,worker=>
        {
            for(int i=0;i<513;i++)
            {batched.Increment(BeamSearchCounter.TransferCandidates);batched.Increment(BeamSearchCounter.TransferLayoutsDeferred);}
        });
        Check(batched.Count(BeamSearchCounter.TransferCandidates)==2052 && batched.Count(BeamSearchCounter.TransferLayoutsDeferred)==2052,"live diagnostic counts include each worker's unflushed candidate events");
        batched.Complete("Succeeded");
        Check(batched.Count(BeamSearchCounter.TransferCandidates)==2052 && batched.Count(BeamSearchCounter.TransferLayoutsDeferred)==2052,"completion flush preserves exact parallel candidate counts");
        long now=0;var trace=new BeamSearchTrace("clock",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>now);
        using(trace.Measure(BeamSearchStage.Geometry))now=Stopwatch.Frequency/2;
        Check(trace.Snapshot().Contains("Stage Geometry: 0.500 seconds; calls=1"),"stage timing uses the monotonic clock");
        now=Stopwatch.Frequency;Deadline(trace.CheckCancellation,"deadline expires at the boundary");
        Check(trace.BudgetReached && !trace.Snapshot().Contains("Search outcome: Cancelled"),"deadline is distinct from cancellation");
        using(var source=new CancellationTokenSource())
        {
            var cancelled=new BeamSearchTrace("cancel",source.Token,TimeSpan.FromSeconds(1),()=>now);
            now+=2*Stopwatch.Frequency;source.Cancel();
            try {cancelled.CheckCancellation();throw new Exception("User cancellation was ignored");}
            catch(OperationCanceledException) {Check(!cancelled.BudgetReached,"user cancellation takes precedence over an expired deadline");}
        }
        var noResult=Common();trace=null;
        trace=new BeamSearchTrace("partial network",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>trace!=null && trace.Count(BeamSearchCounter.NetworkSolves)>0 ? 2*Stopwatch.Frequency : 0);
        noResult.Trace=trace;
        Deadline(()=>TransferNetworkOptimizer.Optimize(noResult,Network()),"incomplete network is never returned or converted to separate-slab fallback");
        Check(trace.Count(BeamSearchCounter.CompleteNetworkCandidates)==0 && !trace.Snapshot().Contains("Catalogue pass:"),"timeout is not misreported as structural infeasibility");
        RetainNetwork(1);RetainNetwork(4);RetainStandaloneLayouts();
        // A complete stock result remains available if a later custom pass expires.
        var stock=TowerOptimizer.Optimize(new TowerInput {LengthMm=2500,StockLengthsMm=new[] {2500.0},TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350});
        var custom=Common();custom.AllowCustomH20Lengths=true;now=0;custom.Trace=new BeamSearchTrace("custom expiry",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>now);
        int passes=0;
        var retained=TowerStockPlanner.SearchWithCustom(custom,()=> {passes++;if(passes==1)return stock;now=2*Stopwatch.Frequency;custom.Trace.CheckCancellation();return stock;},(a,b)=>false);
        Check(ReferenceEquals(retained,stock) && retained.SearchTimeLimitReached && !retained.MinimumLegCountProven && passes==2 && custom.AllowCustomH20Lengths,"custom deadline keeps the complete catalogue result");
        // Custom search may itself retain a result after expiry. If catalogue ranks
        // better, that catalogue result must still disclose the truncated comparison.
        stock.SearchTimeLimitReached=false;stock.MinimumLegCountProven=true;now=0;passes=0;
        custom.Trace=new BeamSearchTrace("catalogue still better",searchTimeLimit:TimeSpan.FromSeconds(1),timestamp:()=>now);
        retained=TowerStockPlanner.SearchWithCustom(custom,()=>
        {
            passes++;if(passes==1)return stock;
            now=2*Stopwatch.Frequency;
            try {custom.Trace.CheckCancellation();}catch(BeamSearchDeadlineException) { }
            return new TowerLayout();
        },(a,b)=>false);
        Check(ReferenceEquals(retained,stock) && retained.SearchTimeLimitReached && !retained.MinimumLegCountProven && passes==2,"a better catalogue layout discloses an interrupted custom comparison");
        var vm=new BeamCalculatorViewModel {UseTowers=true,Depth=200,Width=300,BeamLengthMm=2500,StockLengthsText="2500",MaximumConcreteEndOverhangMm=0};
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && vm.BuildDiagnosticReport().Contains("Search budget seconds: Unlimited") && vm.BuildDiagnosticReport().Contains("Search configuration:"),"normal calculation runs without an elapsed-time limit");
        typeof(BeamCalculatorViewModel).GetField("beamSearchTrace",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm,new BeamSearchTrace("later alternatives"));
        Check(vm.BuildDiagnosticReport().Contains("LAST COMPLETED FULL SEARCH") && vm.BuildDiagnosticReport().Contains("Search outcome: Succeeded"),"a later operation cannot erase the completed full-search trace");
        vm.TowerSearchTimeLimit=TimeSpan.FromTicks(1);vm.OptimizeTowersAsync(true).GetAwaiter().GetResult();
        Check(!vm.IsOptimizing && !vm.IsCalculationValid && vm.TowerResult==null && vm.BuildDiagnosticReport().Contains("Time limit reached: no complete solution"),"interactive timeout clears old results and explains absence of a complete solution");
        Check(vm.CanSearchLonger,"deadline exposes an explicit full-search action");
        vm.OptimizeTowersAsync(false).GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && !vm.CanSearchLonger && vm.BuildDiagnosticReport().Contains("Search budget seconds: Unlimited"),"explicit longer search can finish a layout and reports its unlimited budget");
        long candidateClock=0;
        var candidateTrace=new BeamSearchTrace("candidate timing",timestamp:()=>candidateClock);
        Check(candidateTrace.Snapshot().Contains("First complete network candidate seconds: Not found"),"first-candidate timing does not imply an unchecked result exists");
        candidateTrace.Increment(BeamSearchCounter.CompleteNetworkCandidates);
        candidateClock=3*Stopwatch.Frequency;candidateTrace.Increment(BeamSearchCounter.CompleteNetworkCandidates);
        Check(candidateTrace.Snapshot().Contains("First complete network candidate seconds: 0.000"),"first-candidate timing retains the earliest completion, including a zero-time test clock");
        vm.StartNewSession();Check(!vm.BuildDiagnosticReport().Contains("LAST COMPLETED FULL SEARCH"),"new session clears retained diagnostics");
        Console.WriteLine($"Passed {checks} search budget and completed-result checks.");
    }
}
