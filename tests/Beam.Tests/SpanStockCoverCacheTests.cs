using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class SpanStockCoverCacheTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Span stock-cover cache: "+message);}
    static TowerInput Input()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=6900,
        StockLengthsMm=new[] {1250.0,1800,2450,3300,3600,4900},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,
        AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.Both,LegCapacityKn=70,DeflectionDenominator=270,
        Trace=new BeamSearchTrace("exact local-span cover memo")};
    static string Fingerprint(double[][] covers)=>string.Join("|",covers.Select(c=>string.Join(",",c.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))));
    static double[][] Reference(TowerInput input)
    {var reference=input.CopyForSearch();reference.Trace=null;return TransferredSlabOptimizer.StockCovers(reference);}
    static double[][] Cached(TowerInput input,TransferredSlabOptimizer.GeometryCache geometry)=>TransferNetworkOptimizer.CachedSpanStockCovers(input,geometry);
    static void Dependencies()
    {
        var input=Input();var geometry=new TransferredSlabOptimizer.GeometryCache();
        var initial=Cached(input,geometry);string expected=Fingerprint(Reference(input));
        Check(Fingerprint(initial)==expected,"cached covers retain every original candidate in its exact original order");
        long plans=input.Trace.Count(BeamSearchCounter.CoveragePlans);
        Check(ReferenceEquals(initial,Cached(input,geometry)) && input.Trace.Count(BeamSearchCounter.CoveragePlans)==plans,"a repeated receiving span does not regenerate dynamic-program cover plans");
        input.LengthMm+=1e-9;Check(!ReferenceEquals(initial,Cached(input,geometry)),"span identity is exact and never rounded");input.LengthMm=6900;
        foreach(Action<TowerInput> change in new Action<TowerInput>[] {
            x=>x.LengthMm=7000,x=>x.MaximumConcreteEndOverhangMm=175,x=>x.StockEndSetbackMm=175,
            x=>x.StockLengthsMm=new[] {1250.0,1800,2450,3300,3900,4900},x=>x.AllowCustomH20Lengths=false,
            x=>x.CustomH20Scope=CustomH20LengthScope.SlabSupports,x=>x.PrimaryMaterial=PrimaryBeamMaterial.WS10})
        {
            var altered=Input();change(altered);var actual=Cached(altered,geometry);
            Check(Fingerprint(actual)==Fingerprint(Reference(altered)),"each span, allowance, setback, stock and primary-permission change matches original generation");
            Check(!ReferenceEquals(initial,actual),"a changed stock-cover universe cannot consume the old result");
        }
        input.StockLengthsMm=input.StockLengthsMm.Reverse().ToArray();
        Check(ReferenceEquals(initial,Cached(input,geometry)),"catalogue ordering is normalized exactly as the original generator sorts its stocks");
        input.LongitudinalStartMm=1234;input.LegCapacityKn=17;input.EngineerMinimumStations=12;input.NetworkLegCeiling=24;
        Check(ReferenceEquals(initial,Cached(input,geometry)),"origins and engineering budgets do not fragment an unchanged cover universe");
        Check(Fingerprint(initial)==expected,"other span/policy lookups never mutate retained cover arrays");
        input.ReuseSearchWork=false;var uncached=Cached(input,geometry);
        Check(!ReferenceEquals(initial,uncached) && Fingerprint(uncached)==expected,"the original no-reuse reference path remains available");
        input.ReuseSearchWork=true;
        Check(!ReferenceEquals(initial,Cached(input,new TransferredSlabOptimizer.GeometryCache())),"a different network work owner keeps its own memo lifetime");
        input.TransferredSlab=new TransferredSlabInput {StockCovers=new[] {new[] {4900.0,1800},new[] {3300.0,3600}}};
        Check(Fingerprint(Cached(input,geometry))==Fingerprint(input.TransferredSlab.StockCovers),"explicit restricted cover lists bypass the unrestricted memo");
        input.TransferredSlab.StockCovers=new[] {new[] {3600.0,3300}};
        Check(Fingerprint(Cached(input,geometry))=="3600,3300","a new restricted order/list cannot consume another search's covers");
    }
    static void ConcurrentAndInterrupted()
    {
        var input=Input();var geometry=new TransferredSlabOptimizer.GeometryCache();var results=new double[8][][];
        Parallel.For(0,results.Length,i=>results[i]=Cached(input,geometry));
        Check(results.All(value=>ReferenceEquals(value,results[0])),"concurrent scenario workers publish one completed immutable cover family");
        Check(input.Trace.Count(BeamSearchCounter.CoveragePlans)==results[0].Length,"the shared dynamic program executes once across concurrent workers");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();input.Trace=new BeamSearchTrace("cancelled cover hit",cancelled.Token);bool threw=false;
            try{Cached(input,geometry);}catch(OperationCanceledException){threw=true;}
            Check(threw,"a cached cover hit cannot bypass user cancellation");
        }
        foreach(bool deadline in new[] {false,true})
        {
            var interrupted=Input();var work=new TransferredSlabOptimizer.GeometryCache();int calls=0;
            interrupted.Trace=new BeamSearchTrace("interrupted cover generation",default,TimeSpan.FromSeconds(1),()=>
            {
                if(++calls<5)return 0;
                if(!deadline)throw new ArgumentException("deliberate generation fault");
                return 2*System.Diagnostics.Stopwatch.Frequency;
            });
            bool threw=false;try{Cached(interrupted,work);}catch(BeamSearchDeadlineException){threw=deadline;}catch(ArgumentException){threw=!deadline;}
            Check(threw,"an in-progress cover-generation fault or deadline propagates");
            interrupted.Trace=new BeamSearchTrace("retry completed cover generation");var repaired=Cached(interrupted,work);
            Check(Fingerprint(repaired)==Fingerprint(Reference(interrupted)) && interrupted.Trace.Count(BeamSearchCounter.CoveragePlans)>0,
                "interrupted lazy generation is removed and a later request executes the complete original generator");
        }
    }
    internal static void Run()
    {Dependencies();ConcurrentAndInterrupted();Console.WriteLine("PASS "+checks+" local-span stock-cover memo checks: exact ordered reference parity, all dependencies, concurrent single generation and interruption retries");}
}
