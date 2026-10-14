using System;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Threading;

namespace EngineeringDashboard
{
    internal enum BeamSearchCounter
    {
        CoveragePlans, CoverageVariants, UpperAnalyses, LowerAnalyses, PrimaryAnalyses,
        TowerCandidates, TransferCandidates, NetworkSolves, NumericalChecks, RejectionChecks,
        GeometryBuilds, GeometryCacheHits, CrossCacheHits, LowerCaseCacheHits, PrimaryCacheHits, PrimaryMatrixCacheHits, LowerResponseCacheHits, EconomyPruned, PrimaryDeflectionSkipped, NetworkPrimaryCacheHits, TransferLayoutsBuilt, TransferLayoutsDeferred, NetworkBranchesPruned, NetworkTransverseCacheHits, CompleteNetworkCandidates, CrossingLegClashesRejected
    }
    // Deadline expiry is separate from user cancellation and structural rejection.
    internal sealed class BeamSearchDeadlineException : Exception
    {
        internal BeamSearchDeadlineException():base("Search time limit reached before a complete valid solution was found. This does not prove that no feasible layout exists.") { }
    }
    internal enum BeamSearchStage { InputValidation, SolverWall, StockCovers, UpperOptions, ReceivingBeams, Geometry, TransverseAnalysis, PrimaryAnalysis, CrossingSupports, Movement, ResultApplication, DrawingPreparation }
    // Workers write atomic counters; the UI can read without touching solver collections.
    internal sealed class BeamSearchTrace
    {
        private static readonly string[] CounterNames = {"Primary cover plans generated","Coverage/phase variants visited","Slab H20 analyses","Lower secondary analyses","Primary member analyses","Beam-only tower candidates","Transferred layout candidates","Receiving-beam solves","Numerical checks","Recorded rejection checks (not unique layouts)","Tower geometry builds","Tower geometry cache hits","Transverse option cache hits","Lower row-case cache hits","Primary member cache hits","Primary matrix cache hits","Lower response cache hits","Candidates pruned by economy bounds","Primary deflection evaluations skipped after another failure","Network primary response cache hits","Detailed transfer layouts built","Detailed transfer layouts deferred","Network branches pruned by exact bounds","Network transverse response cache hits","Complete network candidates","Crossing leg clashes rejected before member checks"};
        private readonly long[] counters=new long[CounterNames.Length];
        private readonly Func<long> timestamp;
        private readonly long started;
        private readonly double timeLimitSeconds;
        private int budgetReached, workers;
        private readonly ConcurrentDictionary<BeamSearchStage,StageTime> stages=new ConcurrentDictionary<BeamSearchStage,StageTime>();
        private sealed class StageTime { internal long Ticks,Calls; }
        private sealed class Timing : IDisposable
        {
            private readonly BeamSearchTrace trace;private readonly StageTime stage;private readonly long start;
            internal Timing(BeamSearchTrace trace,StageTime stage) {this.trace=trace;this.stage=stage;start=trace.timestamp();}
            public void Dispose() {Interlocked.Add(ref stage.Ticks,trace.timestamp()-start);Interlocked.Increment(ref stage.Calls);}
        }
        private long ended, catalogueTicks, customTicks;
        private long firstCompleteNetworkTicks=-1;
        private string pass="Not started", work="Validating inputs", context="", outcome="Running", error="", result="", derivedInputs="";
        private string rejectedPass="";
        internal string Inputs {get;}
        internal DateTime StartedUtc {get;}=DateTime.UtcNow;
        internal CancellationToken CancellationToken {get;}
        internal BeamSearchTrace(string inputs,CancellationToken cancellationToken=default,TimeSpan? searchTimeLimit=null,Func<long> timestamp=null)
        {
            if(searchTimeLimit.HasValue && searchTimeLimit.Value<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(searchTimeLimit));
            this.timestamp=timestamp??Stopwatch.GetTimestamp;started=this.timestamp();
            timeLimitSeconds=searchTimeLimit?.TotalSeconds??0;Inputs=inputs;CancellationToken=cancellationToken;
        }
        internal bool HasTimeLimit=>timeLimitSeconds>0;
        internal bool BudgetReached=>Volatile.Read(ref budgetReached)!=0;
        internal void CheckUserCancellation()=>CancellationToken.ThrowIfCancellationRequested();
        internal void CheckCancellation()
        {
            CheckUserCancellation();
            if(timeLimitSeconds>0 && (timestamp()-started)/(double)Stopwatch.Frequency>=timeLimitSeconds)
            {Volatile.Write(ref budgetReached,1);throw new BeamSearchDeadlineException();}
        }
        internal IDisposable Measure(BeamSearchStage stage)
        {
            // Keep the unrestricted production-runtime traversal free of per-member
            // timing allocations; full searches still record enclosing stages/counters.
            if(!HasTimeLimit && (stage==BeamSearchStage.PrimaryAnalysis || stage==BeamSearchStage.TransverseAnalysis))return null;
            return new Timing(this,stages.GetOrAdd(stage,_=>new StageTime()));
        }
        internal void Workers(int count)=>Volatile.Write(ref workers,count);
        internal long Count(BeamSearchCounter counter)=>Interlocked.Read(ref counters[(int)counter]);
        internal void Increment(BeamSearchCounter counter,long amount=1)
        {
            if(HasTimeLimit)CheckCancellation();else CancellationToken.ThrowIfCancellationRequested();
            Interlocked.Add(ref counters[(int)counter],amount);
            if(counter==BeamSearchCounter.CompleteNetworkCandidates && amount>0)
                Interlocked.CompareExchange(ref firstCompleteNetworkTicks,timestamp(),-1);
        }
        internal void Work(string name) {CheckCancellation();Volatile.Write(ref work,name);}
        internal void Context(string name)=>Volatile.Write(ref context,name);
        internal void DerivedInputs(string text)=>Volatile.Write(ref derivedInputs,text);
        internal T RunPass<T>(bool custom,Func<T> search)
        {
            CheckCancellation();Volatile.Write(ref pass,custom ? "Custom lengths enabled" : "Catalogue lengths");Context("");
            long start=timestamp();
            try {var found=search();CheckUserCancellation();return found;}
            catch(Exception ex) when (!(ex is OperationCanceledException) && !(ex is BeamSearchDeadlineException))
            {
                string details=(custom ? "Custom" : "Catalogue")+" pass: "+ex.ToString()+(ex is TowerOptimizationException failure ? "\n"+failure.Details : "");
                Volatile.Write(ref rejectedPass,details.Length<=16000 ? details : details.Substring(0,16000)+"\n[Pass detail truncated]");throw;
            }
            finally {if(custom)Interlocked.Add(ref customTicks,timestamp()-start);else Interlocked.Add(ref catalogueTicks,timestamp()-start);}
        }
        internal void Complete(string status,string details="",string summary="")
        {
            Volatile.Write(ref error,details??"");Volatile.Write(ref result,summary??"");
            Interlocked.Exchange(ref ended,timestamp());Volatile.Write(ref outcome,status);
        }
        internal void Finish()=>Interlocked.Exchange(ref ended,timestamp());
        internal double ElapsedSeconds
        {
            get {long end=Interlocked.Read(ref ended);return ((end==0 ? timestamp() : end)-started)/(double)Stopwatch.Frequency;}
        }
        internal string ProgressText
        {
            get
            {
                string current=Volatile.Read(ref work);
                if(current.IndexOf("cross",StringComparison.OrdinalIgnoreCase)>=0)return "Checking crossing supports";
                if(current.IndexOf("Separate slab",StringComparison.OrdinalIgnoreCase)>=0)return "Checking separate slab support options";
                if(current.IndexOf("upper",StringComparison.OrdinalIgnoreCase)>=0 || current.IndexOf("slab H20",StringComparison.OrdinalIgnoreCase)>=0)return "Checking slab H20s";
                if(current.IndexOf("lower",StringComparison.OrdinalIgnoreCase)>=0)return "Checking secondary H20s";
                if(current.IndexOf("primary",StringComparison.OrdinalIgnoreCase)>=0)return "Checking primary H20s and stock lengths";
                if(current.IndexOf("movement",StringComparison.OrdinalIgnoreCase)>=0)return "Checking slab movement";
                if(current.IndexOf("validat",StringComparison.OrdinalIgnoreCase)>=0)return "Preparing the calculation";
                return "Checking tower and H20 arrangements";
            }
        }
        internal string Snapshot()
        {
            long end=Interlocked.Read(ref ended);var text=new StringBuilder();
            text.AppendLine("Search started UTC: "+StartedUtc.ToString("o",CultureInfo.InvariantCulture));
            text.AppendLine("Search outcome: "+Volatile.Read(ref outcome));
            text.AppendLine("Cancellation requested: "+CancellationToken.IsCancellationRequested);
            text.AppendLine("Elapsed seconds: "+((end==0 ? timestamp()-started : end-started)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture));
            text.AppendLine("Search budget seconds: "+(timeLimitSeconds==0 ? "Unlimited" : timeLimitSeconds.ToString("0.###",CultureInfo.InvariantCulture)));
            text.AppendLine("Search budget reached: "+BudgetReached);
            text.AppendLine("Network workers: "+Volatile.Read(ref workers));
            long firstComplete=Interlocked.Read(ref firstCompleteNetworkTicks);
            text.AppendLine("First complete network candidate seconds: "+(firstComplete<0 ? "Not found" : ((firstComplete-started)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture)));
            foreach(var stage in stages.OrderBy(x=>x.Key))
                text.AppendLine("Stage "+stage.Key+": "+(Interlocked.Read(ref stage.Value.Ticks)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture)+" seconds; calls="+Interlocked.Read(ref stage.Value.Calls));
            if(!HasTimeLimit)text.AppendLine("Per-member numerical timing is disabled for full searches; enclosing stage times and numerical counters are retained.");
            text.AppendLine("Stage times include completed calls only; nested stages and parallel worker times overlap and must not be added as wall time.");
            text.AppendLine("Current/last pass: "+Volatile.Read(ref pass));text.AppendLine("Current/last work: "+Volatile.Read(ref work));
            text.AppendLine("Current/last context: "+Volatile.Read(ref context));
            text.AppendLine("Completed catalogue-pass seconds: "+(Interlocked.Read(ref catalogueTicks)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture));
            text.AppendLine("Completed custom-pass seconds: "+(Interlocked.Read(ref customTicks)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture));
            for(int i=0;i<counters.Length;i++)text.AppendLine(CounterNames[i]+": "+Interlocked.Read(ref counters[i]).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Live counters are approximate snapshots; completed pass times exclude an in-progress pass.");
            text.AppendLine("Derived solver inputs:\n"+Volatile.Read(ref derivedInputs));
            text.AppendLine("Search result summary:\n"+Volatile.Read(ref result));
            text.AppendLine("Most recent rejected pass (may precede a successful fallback):\n"+Volatile.Read(ref rejectedPass));
            text.AppendLine("Search error/failure details:\n"+Volatile.Read(ref error));return text.ToString();
        }
    }
}
