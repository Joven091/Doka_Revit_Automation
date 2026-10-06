using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace EngineeringDashboard
{
    internal enum BeamSearchCounter
    {
        CoveragePlans, CoverageVariants, UpperAnalyses, LowerAnalyses, PrimaryAnalyses,
        TowerCandidates, TransferCandidates, NetworkSolves, NumericalChecks, RejectionChecks,
        GeometryBuilds, GeometryCacheHits, CrossCacheHits, LowerCaseCacheHits, PrimaryCacheHits, PrimaryMatrixCacheHits, LowerResponseCacheHits, EconomyPruned, PrimaryDeflectionSkipped, NetworkPrimaryCacheHits, TransferLayoutsBuilt, TransferLayoutsDeferred, NetworkBranchesPruned, NetworkTransverseCacheHits
    }
    // A single search writes progress; the UI reads atomic snapshots without touching solver collections.
    internal sealed class BeamSearchTrace
    {
        private static readonly string[] CounterNames = {"Primary cover plans generated","Coverage/phase variants visited","Slab H20 analyses","Lower secondary analyses","Primary member analyses","Beam-only tower candidates","Transferred layout candidates","Receiving-beam solves","Numerical checks","Recorded rejection checks (not unique layouts)","Tower geometry builds","Tower geometry cache hits","Transverse option cache hits","Lower row-case cache hits","Primary member cache hits","Primary matrix cache hits","Lower response cache hits","Candidates pruned by economy bounds","Primary deflection evaluations skipped after another failure","Network primary response cache hits","Detailed transfer layouts built","Detailed transfer layouts deferred","Network branches pruned by exact bounds","Network transverse response cache hits"};
        private readonly long[] counters=new long[CounterNames.Length];
        private readonly long started=Stopwatch.GetTimestamp();
        private long ended, catalogueTicks, customTicks;
        private string pass="Not started", work="Validating inputs", context="", outcome="Running", error="", result="", derivedInputs="";
        private string rejectedPass="";
        internal string Inputs {get;}
        internal DateTime StartedUtc {get;}=DateTime.UtcNow;
        internal CancellationToken CancellationToken {get;}
        internal BeamSearchTrace(string inputs,CancellationToken cancellationToken=default) {Inputs=inputs;CancellationToken=cancellationToken;}
        internal void CheckCancellation()=>CancellationToken.ThrowIfCancellationRequested();
        internal void Increment(BeamSearchCounter counter,long amount=1) {CheckCancellation();Interlocked.Add(ref counters[(int)counter],amount);}
        internal void Work(string name) {CheckCancellation();Volatile.Write(ref work,name);}
        internal void Context(string name)=>Volatile.Write(ref context,name);
        internal void DerivedInputs(string text)=>Volatile.Write(ref derivedInputs,text);
        internal T RunPass<T>(bool custom,Func<T> search)
        {
            CheckCancellation();Volatile.Write(ref pass,custom ? "Custom lengths enabled" : "Catalogue lengths");Context("");
            long start=Stopwatch.GetTimestamp();
            try {var found=search();CheckCancellation();return found;}
            catch(Exception ex) when (!(ex is OperationCanceledException))
            {
                string details=(custom ? "Custom" : "Catalogue")+" pass: "+ex.ToString()+(ex is TowerOptimizationException failure ? "\n"+failure.Details : "");
                Volatile.Write(ref rejectedPass,details.Length<=16000 ? details : details.Substring(0,16000)+"\n[Pass detail truncated]");throw;
            }
            finally {if(custom)Interlocked.Add(ref customTicks,Stopwatch.GetTimestamp()-start);else Interlocked.Add(ref catalogueTicks,Stopwatch.GetTimestamp()-start);}
        }
        internal void Complete(string status,string details="",string summary="")
        {
            Volatile.Write(ref error,details??"");Volatile.Write(ref result,summary??"");
            Interlocked.Exchange(ref ended,Stopwatch.GetTimestamp());Volatile.Write(ref outcome,status);
        }
        internal string Snapshot()
        {
            long end=Interlocked.Read(ref ended);var text=new StringBuilder();
            text.AppendLine("Search started UTC: "+StartedUtc.ToString("o",CultureInfo.InvariantCulture));
            text.AppendLine("Search outcome: "+Volatile.Read(ref outcome));
            text.AppendLine("Cancellation requested: "+CancellationToken.IsCancellationRequested);
            text.AppendLine("Elapsed seconds: "+((end==0 ? Stopwatch.GetTimestamp()-started : end-started)/(double)Stopwatch.Frequency).ToString("0.000",CultureInfo.InvariantCulture));
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
