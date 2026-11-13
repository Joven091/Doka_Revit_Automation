using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class UniformStockSuffixTests
{
    static int checks;
    static readonly MethodInfo original=typeof(TowerStockPlanner).GetMethod("UniformMemberForcesFitAt",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo exceeds=typeof(TowerStockPlanner).GetMethod("ExceedsNecessaryBound",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo planParity=typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnosticParity=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Uniform stock suffix: "+message);}
    static TowerInput Input(double length,bool custom=true,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20,double capacity=70)
    {
        var input=new TowerInput {LengthMm=length,LegCapacityKn=capacity,PrimaryMaterial=material,DeflectionDenominator=270,
            StockLengthsMm=new[] {1250.0,1333,1800,2000,2450,3900,4200,4901,6000},AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,
            StockEndSetbackMm=150,Trace=new BeamSearchTrace("exact monotone stock necessary bounds")};
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static bool Pass(TowerInput input,double stock,double[] positions,int start,int first,int last,double q)
        =>(bool)original.Invoke(null,new object[] {input,stock/1000,positions,start,first,last,q});
    static void Prefix(TowerInput input,double[] stocks,double[] positions,int start,int first,int last,double q)
    {
        bool certified=TowerStockPlanner.TryUniformFailureSuffix(input,stocks,positions,start,first,last,0,stocks.Length,q,input.Diagnostics,out int failed);
        Check(certified,"finite nonnegative H20 interval is certified");
        Check(failed>=0&&failed<=stocks.Length,"suffix lies inside its original stock interval");
        for(int i=0;i<stocks.Length;i++)Check(Pass(input,stocks[i],positions,start,first,last,q)==(i<failed),"every original exact necessary predicate agrees with the complete passing prefix/failed suffix");
    }
    static double[] Around(double center)
    {
        double lower=center,upper=center;var values=new double[65];values[32]=center;
        for(int i=1;i<=32;i++){lower=Math.BitDecrement(lower);upper=Math.BitIncrement(upper);values[32-i]=lower;values[32+i]=upper;}return values;
    }
    static void PredicateProof()
    {
        var random=new Random(918204);
        for(int sample=0;sample<240;sample++)
        {
            int count=2+sample%6,start=25*(sample%5);double span=800+random.NextDouble()*4000;
            var positions=Enumerable.Range(0,count).Select(i=>start+150+span*i/(count-1)).ToArray();
            var stocks=Enumerable.Range(0,48).Select(i=>span+300+i*25.125).ToArray();
            double q=new[] {0.0,double.Epsilon,.125,8.125,35.0,90.0,1e100}[sample%7];
            Prefix(Input(10000,capacity:new[] {.125,.5,35.0,70.0}[sample%4]),stocks,positions,start,0,count-1,q);
        }
        foreach(double q in new[] {.125,6.125,20.0,35.0})foreach(double capacity in new[] {.125,.5,35.0,70.0})
        {
            var input=Input(10000,capacity:capacity);var positions=new[] {150.0,1150,2150};double last=2.15;
            double[] boundaries={3*Math.Min(capacity,44)/(q*(1-1e-6))*1000,
                (last+22/(q*(1-1e-6)))*1000,(last+Math.Sqrt(20/(q*(1-1e-6))))*1000};
            foreach(double boundary in boundaries)
            {
                var values=Around(boundary).Where(x=>x>=2300&&x<50000).ToArray();if(values.Length>0)Prefix(input,values,positions,0,0,2,q);
            }
        }
        var finite=(bool)exceeds.Invoke(null,new object[] {1e100,44.0});var overflow=(bool)exceeds.Invoke(null,new object[] {double.PositiveInfinity,44.0});
        Check(finite&&!overflow,"the old infinity predicate really is nonmonotone and requires explicit overflow fallback");
        foreach(double q in new[] {double.MaxValue,1e308,double.PositiveInfinity,double.NaN,-1.0})
        {
            var input=Input(50000);bool certified=TowerStockPlanner.TryUniformFailureSuffix(input,new[] {12000.0,25000,50000},new[] {150.0,1150},0,0,1,0,3,q,input.Diagnostics,out _);
            Check(!certified,"nonfinite/negative input or overflowing original intermediate preserves the original per-stock path");
        }
        var ws=Input(6000,material:PrimaryBeamMaterial.WS10);
        Check(!TowerStockPlanner.TryUniformFailureSuffix(ws,new[] {2450.0,3900},new[] {150.0,1150},0,0,1,0,2,8.125,ws.Diagnostics,out _),"stock-dependent WS10 self weight never uses the H20 proof");
    }
    static void Compare(TowerInput a,TowerInput b,double[] positions,double q)
    {
        var expected=TowerStockPlanner.Find(a,positions,q,new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false));
        var cache=new TowerStockPlanner.FindWorkCache();var actual=TowerStockPlanner.Find(b,positions,q,cache);
        planParity.Invoke(null,new object[] {expected,actual});checks++;diagnosticParity.Invoke(null,new object[] {a.Diagnostics,b.Diagnostics});checks++;
        foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.PrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
            Check(a.Trace.Count(counter)==b.Trace.Count(counter),"passing member work and all complete diagnostic totals retain original order/counts");
        var replay=b.Diagnostics.Fork();b.Diagnostics=replay;planParity.Invoke(null,new object[] {expected,TowerStockPlanner.Find(b,positions,q,cache)});checks++;
        diagnosticParity.Invoke(null,new object[] {a.Diagnostics,replay});checks++;
    }
    static void FullSearchParity()
    {
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(double q in new[] {.125,8.125,20.0,35.0,90.0})foreach(double setback in new[] {double.Epsilon,1e-9,150.0,300.0})
        foreach(var positions in new[] {new[] {150.0,1150,3350,4350,5850},new[] {150.0,1150,1600,2250,3000,3600,4050,4850,5850}})
        {
            var a=Input(6000,custom,material);var b=Input(6000,custom,material);a.StockEndSetbackMm=b.StockEndSetbackMm=setback;Compare(a,b,positions,q);
        }
        var first=Input(3900,false);var second=Input(3900,false);
        var stocks=Enumerable.Range(1,30).Select(i=>25.0*i).Concat(new[] {3875-1e-7,3875.0,Math.BitDecrement(3875+1e-7),3875+1e-7}).ToArray();
        first.StockLengthsMm=second.StockLengthsMm=stocks;first.StockEndSetbackMm=second.StockEndSetbackMm=double.Epsilon;
        Compare(first,second,new[] {1.0,24,150,1150,3900},20);
        // A mix of passing/failed member intervals makes first diagnostic-key
        // insertion order observable; suffix recording must follow its prefix.
        Compare(Input(6000),Input(6000),new[] {150.0,1150,1600,2250,3000,3600,4050,4850,5850},21.375);
    }
    static void BudgetsAndCancellation()
    {
        var positions=new[] {150.0,1150,3350,4350,5850};
        foreach(bool diagnosticBudget in new[] {false,true})
        {
            var a=Input(6000);var b=Input(6000);
            var traceA=new BeamSearchTrace("old suffix budgetA",searchTimeLimit:TimeSpan.FromHours(1));var traceB=new BeamSearchTrace("old suffix budgetB",searchTimeLimit:TimeSpan.FromHours(1));
            if(diagnosticBudget){a.Diagnostics=new TowerSearchDiagnostics(270,trace:traceA);b.Diagnostics=new TowerSearchDiagnostics(270,trace:traceB);}
            else {a.Trace=traceA;b.Trace=traceB;a.Diagnostics=new TowerSearchDiagnostics(270,trace:traceA);b.Diagnostics=new TowerSearchDiagnostics(270,trace:traceB);}
            Compare(a,b,positions,20);
        }
        using(var source=new CancellationTokenSource())
        {
            var input=Input(6000);input.Trace=new BeamSearchTrace("cancelled before suffix",source.Token);source.Cancel();bool stopped=false;
            try{TowerStockPlanner.TryUniformFailureSuffix(input,new[] {2450.0,3900,6000},positions,0,0,1,0,3,20,input.Diagnostics,out _);}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"classification cannot bypass cancellation at its bounded predicate probes");
        }
    }
    internal static void Run(){PredicateProof();FullSearchParity();BudgetsAndCancellation();Console.WriteLine("PASS "+checks+" uniform-stock suffix checks: exact finite predicate prefixes, ULP boundaries, overflow/WS10 fallback, original passing physics/ranks, diagnostic order/totals, replay, budgets and cancellation");}
}
