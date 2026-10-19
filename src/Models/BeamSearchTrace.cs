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
        GeometryBuilds, GeometryCacheHits, CrossCacheHits, LowerCaseCacheHits, PrimaryCacheHits, PrimaryMatrixCacheHits, LowerResponseCacheHits, EconomyPruned, PrimaryDeflectionSkipped, NetworkPrimaryCacheHits, TransferLayoutsBuilt, TransferLayoutsDeferred, NetworkBranchesPruned, NetworkTransverseCacheHits, CompleteNetworkCandidates, CrossingLegClashesRejected, NetworkSpacingTrials, UniformPrimaryCacheHits, PrimaryLoadCacheHits, SeedNetworkCandidates, PreparedPrimaryCacheHits, CrossingGeometryCacheHits
    }
    // Deadline expiry is separate from user cancellation and structural rejection.
    internal sealed class BeamSearchDeadlineException : Exception
    {
        internal BeamSearchDeadlineException():base("Search time limit reached before a complete valid solution was found. This does not prove that no feasible layout exists.") { }
    }
    internal enum BeamHotStage { LoadPreparation, PrimaryResponse, TransverseResponse, SpanScreen, LayoutAcceptance, CrossingRecheck, SharedCrossing, PrimaryRecheck }
    internal enum BeamSearchStage { InputValidation, SolverWall, StockCovers, UpperOptions, ReceivingBeams, Geometry, TransverseAnalysis, PrimaryAnalysis, CrossingSupports, Movement, ResultApplication, DrawingPreparation }
    // Workers write atomic counters; the UI can read without touching solver collections.
    internal sealed class BeamSearchTrace
    {
        private static readonly string[] CounterNames = {"Primary cover plans generated","Coverage/phase variants visited","Slab H20 analyses","Lower secondary analyses","Primary member analyses","Beam-only tower candidates","Transferred layout candidates","Receiving-beam solves","Numerical checks","Recorded rejection checks (not unique layouts)","Tower geometry builds","Tower geometry cache hits","Transverse option cache hits","Lower row-case cache hits","Primary member cache hits","Primary matrix cache hits","Lower response cache hits","Candidates pruned by economy bounds","Primary deflection evaluations skipped after another failure","Network primary response cache hits","Detailed transfer layouts built","Detailed transfer layouts deferred","Network branches pruned by exact bounds","Network transverse response cache hits","Complete network candidates","Crossing leg clashes rejected before member checks","Network spacing vectors searched","Uniform primary shape cache hits","Primary load-data cache hits","Seed network candidates","Prepared primary check cache hits","Crossing geometry cache hits"};
        // Sampling is opt-in for benchmark runs. No per-check stopwatch,
        // timer allocation or profiling collection is used by normal calculation.
        internal bool EnableHotProfiling=false;
        internal sealed class HotBucket {internal long Calls,Samples,Ticks;internal uint Random=31973;}
        private readonly ThreadLocal<HotBucket[]> hotBuckets=new ThreadLocal<HotBucket[]>(()=>Enum.GetValues(typeof(BeamHotStage)).Cast<BeamHotStage>().Select(x=>new HotBucket {Random=31973+(uint)x*7111}).ToArray(),true);
        internal struct HotTimer : IDisposable
        {
            private readonly HotBucket bucket;private readonly long start;
            internal HotTimer(HotBucket bucket){this.bucket=bucket;start=Stopwatch.GetTimestamp();}
            public void Dispose(){if(bucket!=null){bucket.Ticks+=Stopwatch.GetTimestamp()-start;bucket.Samples++;}}
        }
        internal HotTimer Hot(BeamHotStage stage)
        {
            if(!EnableHotProfiling)return default(HotTimer);
            var bucket=hotBuckets.Value[(int)stage];bucket.Calls++;
            unchecked{bucket.Random=bucket.Random*1664525+1013904223;}
            return (bucket.Random>>23)==0 ? new HotTimer(bucket) : default(HotTimer);
        }
        private long lastImprovementTicks=-1,improvements;
        private string improvementScore="";
        internal void NetworkImproved(string score)
        {
            Interlocked.Increment(ref improvements);Interlocked.Exchange(ref lastImprovementTicks,timestamp());Volatile.Write(ref improvementScore,score);
        }
        private readonly long[] counters=new long[CounterNames.Length];
        // Hot diagnostic events are accumulated by their owning worker. UI reads
        // include pending counts; completion flushes them for an exact final report.
        private readonly ThreadLocal<long[]> pendingCounters=new ThreadLocal<long[]>(()=>new long[CounterNames.Length],true);
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
        private string crossingRejection="";
        internal void CrossingRejected(string reason)=>Volatile.Write(ref crossingRejection,reason.Length<=4000 ? reason : reason.Substring(0,4000));
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
        internal long Count(BeamSearchCounter counter)
        {
            int index=(int)counter;long value=Interlocked.Read(ref counters[index]);
            foreach(var pending in pendingCounters.Values)value+=Interlocked.Read(ref pending[index]);
            return value;
        }
        private void FlushCounters()
        {
            foreach(var pending in pendingCounters.Values)for(int i=0;i<pending.Length;i++)
            {long value=Interlocked.Exchange(ref pending[i],0);if(value!=0)Interlocked.Add(ref counters[i],value);}
        }
        internal void Increment(BeamSearchCounter counter,long amount=1)
        {
            if(HasTimeLimit)CheckCancellation();else CancellationToken.ThrowIfCancellationRequested();
            if(counter==BeamSearchCounter.NumericalChecks || counter==BeamSearchCounter.RejectionChecks || counter==BeamSearchCounter.PreparedPrimaryCacheHits || counter==BeamSearchCounter.PrimaryLoadCacheHits || counter==BeamSearchCounter.UniformPrimaryCacheHits)
            {
                var pending=pendingCounters.Value;int index=(int)counter;pending[index]+=amount;
                if(pending[index]>=256)Interlocked.Add(ref counters[index],Interlocked.Exchange(ref pending[index],0));
            }
            else Interlocked.Add(ref counters[(int)counter],amount);
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
            FlushCounters();
            Volatile.Write(ref error,details??"");Volatile.Write(ref result,summary??"");
            Interlocked.Exchange(ref ended,timestamp());Volatile.Write(ref outcome,status);
        }
        internal void Finish(){FlushCounters();Interlocked.Exchange(ref ended,timestamp());}
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
            if(EnableHotProfiling)
            {
                long improved=Interlocked.Read(ref lastImprovementTicks);
                text.AppendLine("Last network improvement seconds: "+(improved<0 ? "Not found" : ((improved-started)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture)));
                text.AppendLine("Network improvements: "+Interlocked.Read(ref improvements)+"; score: "+Volatile.Read(ref improvementScore));
                foreach(BeamHotStage stage in Enum.GetValues(typeof(BeamHotStage)))
                {
                    long calls=0,samples=0,ticks=0;
                    foreach(var worker in hotBuckets.Values){var bucket=worker[(int)stage];calls+=Interlocked.Read(ref bucket.Calls);samples+=Interlocked.Read(ref bucket.Samples);ticks+=Interlocked.Read(ref bucket.Ticks);}
                    double estimate=samples==0 ? 0 : ticks/(double)samples*calls/Stopwatch.Frequency;
                    text.AppendLine("Profile "+stage+": calls="+calls+"; samples="+samples+"; estimated worker seconds="+estimate.ToString("0.000",CultureInfo.InvariantCulture));
                }
                text.AppendLine("Sampled worker times are estimates, overlap when nested, and are not additive wall times.");
            }
            foreach(var stage in stages.OrderBy(x=>x.Key))
                text.AppendLine("Stage "+stage.Key+": "+(Interlocked.Read(ref stage.Value.Ticks)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture)+" seconds; calls="+Interlocked.Read(ref stage.Value.Calls));
            if(!HasTimeLimit)text.AppendLine("Per-member numerical timing is disabled for full searches; enclosing stage times and numerical counters are retained.");
            text.AppendLine("Stage times include completed calls only; nested stages and parallel worker times overlap and must not be added as wall time.");
            text.AppendLine("Current/last pass: "+Volatile.Read(ref pass));text.AppendLine("Current/last work: "+Volatile.Read(ref work));
            text.AppendLine("Current/last context: "+Volatile.Read(ref context));
            if(!string.IsNullOrEmpty(Volatile.Read(ref crossingRejection)))text.AppendLine("Latest crossing rejection: "+Volatile.Read(ref crossingRejection));
            text.AppendLine("Completed catalogue-pass seconds: "+(Interlocked.Read(ref catalogueTicks)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture));
            text.AppendLine("Completed custom-pass seconds: "+(Interlocked.Read(ref customTicks)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture));
            for(int i=0;i<counters.Length;i++)text.AppendLine(CounterNames[i]+": "+Count((BeamSearchCounter)i).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Live counters are approximate snapshots; completed pass times exclude an in-progress pass.");
            text.AppendLine("Derived solver inputs:\n"+Volatile.Read(ref derivedInputs));
            text.AppendLine("Search result summary:\n"+Volatile.Read(ref result));
            text.AppendLine("Most recent rejected pass (may precede a successful fallback):\n"+Volatile.Read(ref rejectedPass));
            text.AppendLine("Search error/failure details:\n"+Volatile.Read(ref error));return text.ToString();
        }
    }
}
