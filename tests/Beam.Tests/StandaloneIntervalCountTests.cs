using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class StandaloneIntervalCountTests
{
    static int checks;
    static readonly MethodInfo next=typeof(TowerStockPlanner).GetMethod("NextStockInterval",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo count=typeof(TowerStockPlanner).GetMethod("EligibleStockIntervalCount",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo planParity=typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnosticParity=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Standalone interval count: "+message);}
    static bool Eligible(double stock,double[] positions,int start,int first,double length,double setback,out int last)
    {
        double end=start+stock;last=first;
        if(end>length+1e-7 || positions[first]-start<setback-1e-7)return false;
        while(last+1<positions.Length && positions[last+1]<end-1e-7)last++;
        if(last<=first || end-positions[last]<setback-1e-7)return false;
        if(last+1<positions.Length && positions[last+1]<end+setback-1e-7)return false;
        return !(end<length-1e-7 && last+1==positions.Length || Math.Abs(end-length)<1e-7 && last+1!=positions.Length);
    }
    static void Enumeration(double[] stocks,double[] positions,int start,int first,double length,double setback)
    {
        Check(positions[first]-start>=setback-1e-7,"the original fixed first-support setback guard holds before interval counting");
        int expected=stocks.Count(stock=>Eligible(stock,positions,start,first,length,setback,out _)),actual=0,last=first,previousEnd=0;
        while(true)
        {
            var args=new object[] {stocks,positions,start,length,setback,last,0,0,null};bool found=(bool)next.Invoke(null,args);last=(int)args[5];if(!found)break;
            int begin=(int)args[6],end=(int)args[7];Check(begin>=previousEnd && begin<end,"source intervals remain disjoint and ordered");previousEnd=end;
            int precise=0;
            for(int i=begin;i<end;i++)if(Eligible(stocks[i],positions,start,first,length,setback,out int originalLast))
            {Check(last==originalLast,"source interval retains the original exact last support");precise++;}
            int fast=(int)count.Invoke(null,new object[] {stocks,start,length,begin,end,last+1==positions.Length});
            Check(fast==precise,"logarithmic range count matches all original full guards, including the open absolute tolerance exclusion");actual+=fast;
        }
        Check(actual==expected,"all and only original stock failures contribute to the complete count");
    }
    static void CountProof()
    {
        var random=new Random(761209);
        for(int sample=0;sample<250;sample++)
        {
            double length=1500+random.NextDouble()*7000;int n=2+sample%8;
            var positions=Enumerable.Range(0,n).Select(i=>150+(length-300)*i/(n-1)).ToArray();
            int first=random.Next(n-1),start=(int)(positions[first]/3),stockCount=40+sample%80;
            var stocks=Enumerable.Range(0,stockCount).Select(_=>25+random.NextDouble()*(length-25)).OrderBy(x=>x).ToArray();
            double setback=new[] {double.Epsilon,1e-9,Math.BitDecrement(150.0),150.0,Math.BitIncrement(150.0),300.0}[sample%6];
            if(positions[first]-start>=setback-1e-7)Enumeration(stocks,positions,start,first,length,setback);
        }
        foreach(double length in new[] {Math.BitDecrement(3900.0),3900.0,Math.BitIncrement(3900.0)})
        foreach(double setback in new[] {double.Epsilon,1e-9,Math.BitDecrement(150.0),150.0,Math.BitIncrement(150.0)})
        {
            int start=25;var positions=new[] {1.0,24,200,1150,length};
            var edges=new[] {length-start-1e-7,length-start,length-start+1e-7};
            var stocks=Enumerable.Range(1,155).Select(i=>25.0*i).Concat(edges.SelectMany(x=>new[] {Math.BitDecrement(x),x,x,Math.BitIncrement(x)})).OrderBy(x=>x).ToArray();
            Enumeration(stocks,positions,start,2,length,setback);
        }
        // Here the remaining abs guard excludes a middle range while its exact
        // upper tolerance endpoint can pass. A one-sided suffix trim is wrong.
        var special=new[] {3875-1e-7,3875.0,Math.BitDecrement(3875+1e-7),3875+1e-7,Math.BitIncrement(3875+1e-7)};
        Enumeration(special,new[] {1.0,24,150,1150,3900},25,2,3900,double.Epsilon);
    }
    static TowerInput Input(double length,bool custom=true,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20)
    {
        var input=new TowerInput {LengthMm=length,LegCapacityKn=70,PrimaryMaterial=material,DeflectionDenominator=270,
            StockLengthsMm=new[] {1250.0,1333,1800,2000,2450,3900,4200,4901,6000},AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,
            StockEndSetbackMm=150,Trace=new BeamSearchTrace("exact failed-stock interval count")};
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static void Compare(TowerInput a,TowerInput b,double[] positions,double q)
    {
        var expected=TowerStockPlanner.Find(a,positions,q,new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false));
        var cache=new TowerStockPlanner.FindWorkCache();var actual=TowerStockPlanner.Find(b,positions,q,cache);
        planParity.Invoke(null,new object[] {expected,actual});checks++;diagnosticParity.Invoke(null,new object[] {a.Diagnostics,b.Diagnostics});checks++;
        foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.PrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
            Check(a.Trace.Count(counter)==b.Trace.Count(counter),"complete candidate/analysis/diagnostic counts match the per-stock reference");
        var replay=b.Diagnostics.Fork();b.Diagnostics=replay;planParity.Invoke(null,new object[] {expected,TowerStockPlanner.Find(b,positions,q,cache)});checks++;
        diagnosticParity.Invoke(null,new object[] {a.Diagnostics,replay});checks++;
    }
    static void FullPlansAndDiagnostics()
    {
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(double setback in new[] {double.Epsilon,1e-9,150.0,300.0})foreach(double q in new[] {.125,20.0,35.0,90.0})
        foreach(var stations in new[] {new[] {750.0,1750,3350,4350,5850},new[] {150.0,1150,3350,4350,5850}})
        {
            var a=Input(6000,custom,material);var b=Input(6000,custom,material);a.StockEndSetbackMm=b.StockEndSetbackMm=setback;Compare(a,b,stations,q);
        }
        var rareStocks=Enumerable.Range(1,30).Select(i=>25.0*i).Concat(new[] {3875-1e-7,3875.0,Math.BitDecrement(3875+1e-7),3875+1e-7}).ToArray();
        var first=Input(3900,false);var second=Input(3900,false);first.StockLengthsMm=second.StockLengthsMm=rareStocks;first.StockEndSetbackMm=second.StockEndSetbackMm=double.Epsilon;
        Compare(first,second,new[] {1.0,24,150,1150,3900},200);
    }
    static void CancellationAndCompletedRanges()
    {
        var stocks=Enumerable.Range(1,240).Select(i=>25.0*i).ToArray();var positions=new[] {750.0,1750,3350,4350,5850};
        // A direct helper invocation uses a deterministic test clock to cancel
        // between two completed intervals. Production explicit-budget calls
        // never enter this helper; the actual fallback is tested separately.
        using(var cancellation=new CancellationTokenSource())
        {
            int checkpoints=0;BeamSearchTrace trace=null;
            trace=new BeamSearchTrace("range cancellation checkpoint",cancellation.Token,TimeSpan.FromHours(1),()=>{if(++checkpoints==8)cancellation.Cancel();return 0;});
            var input=Input(6000);input.Trace=trace;var diagnostic=new TowerSearchDiagnostics(270,trace:trace);bool analyzed=false;int failures=0;bool stopped=false;
            try{try{TowerStockPlanner.CountNearFailureStocks(input,stocks,positions,0,0,6000,150,diagnostic,ref analyzed,ref failures);}finally{diagnostic.GeometryRecorded("standalone stock force bound","same completed failures","advice",failures);}}
            catch(OperationCanceledException){stopped=true;}
            Check(stopped,"range counting observes cancellation before another bounded range proof can be published");
            Check(failures>0 && failures<stocks.Length && analyzed,"partial counts retain an actual completed first interval and exclude the aborted next one");
            Check(trace.Count(BeamSearchCounter.RejectionChecks)==failures,"completed interval failures survive cancellation without a counter recheck");
        }
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();var input=Input(6000);input.Trace=new BeamSearchTrace("cancelled before interval",cancelled.Token);bool analyzed=false;int failures=0,stopped=0;
            try{TowerStockPlanner.CountNearFailureStocks(input,stocks,positions,0,0,6000,150,input.Diagnostics,ref analyzed,ref failures);}catch(OperationCanceledException){stopped++;}
            Check(stopped==1 && !analyzed && failures==0,"pre-cancelled range work publishes no failure count or analyzed claim");
        }
        var a=Input(6000);var b=Input(6000);a.Trace=new BeamSearchTrace("old explicit stock count",searchTimeLimit:TimeSpan.FromHours(1));b.Trace=new BeamSearchTrace("old explicit optimized stock count",searchTimeLimit:TimeSpan.FromHours(1));
        a.Diagnostics=new TowerSearchDiagnostics(270,trace:a.Trace);b.Diagnostics=new TowerSearchDiagnostics(270,trace:b.Trace);Compare(a,b,positions,35);
    }
    internal static void Run(){CountProof();FullPlansAndDiagnostics();CancellationAndCompletedRanges();Console.WriteLine("PASS "+checks+" exact failed-stock interval counts: full guard/ULP/open-tolerance parity, candidate order, stock/material policy, selected plans, diagnostic totals, replay, cancellation and explicit-budget fallback");}
}
