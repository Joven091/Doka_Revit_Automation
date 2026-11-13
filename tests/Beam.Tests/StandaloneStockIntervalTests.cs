using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class StandaloneStockIntervalTests
{
    static readonly MethodInfo next=typeof(TowerStockPlanner).GetMethod("NextStockInterval",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo bound=typeof(TowerStockPlanner).GetMethod("UniformMemberForcesFitAt",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo planParity=typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnosticParity=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Standalone stock intervals: "+message);}
    static TowerInput Input(double length,bool custom=true,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20,int criterion=270)=>new TowerInput
    {
        LengthMm=length,TotalLineLoad=12,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1333,1800,2450,2679,3900,4901},
        AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,PrimaryMaterial=material,DeflectionDenominator=criterion,
        StockEndSetbackMm=150,Trace=new BeamSearchTrace("exact standalone stock interval parity")
    };
    static bool Eligible(double stock,double[] positions,int start,int first,double length,double setback,out int last)
    {
        double end=start+stock;last=first;
        if(end>length+1e-7 || positions[first]-start<setback-1e-7)return false;
        while(last+1<positions.Length && positions[last+1]<end-1e-7)last++;
        if(last<=first || end-positions[last]<setback-1e-7)return false;
        if(last+1<positions.Length && positions[last+1]<end+setback-1e-7)return false;
        return !(end<length-1e-7 && last+1==positions.Length || Math.Abs(end-length)<1e-7 && last+1!=positions.Length);
    }
    static void Intervals(double[] stocks,double[] positions,int start,int first,double length,double setback)
    {
        var expected=new List<string>();
        for(int i=0;i<stocks.Length;i++)if(Eligible(stocks[i],positions,start,first,length,setback,out int last))expected.Add(i+":"+last);
        var actual=new List<string>();int current=first,lastEnd=0;
        while(true)
        {
            var args=new object[] {stocks,positions,start,length,setback,current,0,0,null};
            bool found=(bool)next.Invoke(null,args);current=(int)args[5];if(!found)break;
            int begin=(int)args[6],end=(int)args[7];Check(begin>=lastEnd && end>begin,"eligible stock intervals retain ascending disjoint source indices");lastEnd=end;
            for(int i=begin;i<end;i++)if(Eligible(stocks[i],positions,start,first,length,setback,out int last))
            {Check(last==current,"interval last support matches the original strict stock-end comparison");actual.Add(i+":"+current);}
        }
        Check(expected.SequenceEqual(actual),"every original eligible stock is visited in its unchanged order");
    }
    static void EnumerationParity()
    {
        var random=new Random(529184);
        for(int sample=0;sample<180;sample++)
        {
            double length=1500+25*random.Next(180);var input=Input(length);var stocks=TowerStockPlanner.CandidateLengths(input,25,length);
            int count=2+random.Next(7);var positions=Enumerable.Range(0,count).Select(i=>100+(length-200)*i/(count-1)).ToArray();
            int first=random.Next(count-1),start=(int)(25*random.Next(Math.Max(1,(int)(positions[first]/25))));
            double setback=new[] {0.000000001,150.0,165.125,300.0}[sample%4];
            Intervals(stocks,positions,start,first,length,setback);
        }
        foreach(double delta in new[] {-1e-7,-1e-9,0,1e-9,1e-7})
        {
            var input=Input(3900+delta);var stocks=TowerStockPlanner.CandidateLengths(input,25,input.LengthMm);
            foreach(var positions in new[] {new[] {150.0+delta,1450,2450,3750},new[] {150.0,1800+delta,3750}})
                Intervals(stocks,positions,0,0,input.LengthMm,150);
        }
    }
    static void BoundParity()
    {
        var random=new Random(941867);
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})for(int sample=0;sample<120;sample++)
        {
            double length=2000+random.Next(3000);int start=25*random.Next(80),count=2+sample%6;
            var positions=Enumerable.Range(0,count).Select(i=>start+150+(length-300)*i/(count-1)).ToArray();
            var local=positions.Select(x=>(x-start)/1000).ToArray();var input=Input(length,material:material);
            int first=0,last=count-1;
            if(sample%2!=0){positions=new[] {start-125.0}.Concat(positions).Concat(new[] {start+length+125}).ToArray();first=1;last=count;}
            var loads=new[] {.125,8.125,35.0,input.Primary.ShearLimitKn/local[0],2*input.Primary.MomentLimitKnM/(local[0]*local[0]),
                count*Math.Min(input.LegCapacityKn,2*input.Primary.ShearLimitKn)/(length/1000)};
            foreach(double q in loads.SelectMany(q=>new[] {PortableTestRuntime.NextDown(q),q,PortableTestRuntime.NextUp(q),q*(1+1e-6),q*(1-1e-6)}))
            {
                bool expected=TowerStockPlanner.UniformMemberForcesFit(input,length/1000,local,q);
                bool actual=(bool)bound.Invoke(null,new object[] {input,length/1000,positions,start,first,last,q});
                Check(expected==actual,"array-free bounds retain the exact local-coordinate arithmetic and margin");
            }
        }
    }
    static void FindParity()
    {
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(int criterion in new[] {270,500})
        foreach(double length in new[] {2450.0,3900,5000})foreach(double q in new[] {2.125,12.375,35.0})
        {
            var expectedInput=Input(length,custom,material,criterion);var actualInput=Input(length,custom,material,criterion);
            var positions=length==2450 ? new[] {150.0,1225,2300} : length==3900 ? new[] {150.0,1150,2750,3750} : new[] {150.0,1150,2850,3850,4850};
            expectedInput.Diagnostics=new TowerSearchDiagnostics(criterion,trace:expectedInput.Trace,ignoreUplift:true);
            actualInput.Diagnostics=new TowerSearchDiagnostics(criterion,trace:actualInput.Trace,ignoreUplift:true);
            var reference=new TowerStockPlanner.FindWorkCache(optimizeStockLoop:false);var optimized=new TowerStockPlanner.FindWorkCache();
            var expected=TowerStockPlanner.Find(expectedInput,positions,q,reference);var actual=TowerStockPlanner.Find(actualInput,positions,q,optimized);
            planParity.Invoke(null,new object[] {expected,actual});diagnosticParity.Invoke(null,new object[] {expectedInput.Diagnostics,actualInput.Diagnostics});
            foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.PrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
                Check(expectedInput.Trace.Count(counter)==actualInput.Trace.Count(counter),"candidate order preserves response-cache and diagnostic counters");
            var plain=Input(length,custom,material,criterion);planParity.Invoke(null,new object[] {TowerStockPlanner.Find(plain,positions,q),actual});
            var nextDiagnostic=new TowerSearchDiagnostics(criterion,trace:actualInput.Trace,ignoreUplift:true);actualInput.Diagnostics=nextDiagnostic;
            planParity.Invoke(null,new object[] {actual,TowerStockPlanner.Find(actualInput,positions,q,optimized)});
            diagnosticParity.Invoke(null,new object[] {expectedInput.Diagnostics,nextDiagnostic});Check(optimized.PlanHits>0,"complete plan context and diagnostic replay remain reusable");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var input=Input(3900);var cache=new TowerStockPlanner.FindWorkCache();var positions=new[] {150.0,1150,2750,3750};
            TowerStockPlanner.Find(input,positions,6,cache);cancellation.Cancel();input.Trace=new BeamSearchTrace("cancelled optimized stock loop",cancellation.Token);
            bool cancelled=false;try{TowerStockPlanner.Find(input,positions,6,cache);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancellation precedes retained full-plan evidence");
        }
    }
    internal static void Run()
    {
        EnumerationParity();BoundParity();FindParity();
        Console.WriteLine("PASS "+checks+" standalone stock interval checks: exact candidate order, local bounds, selected plans, diagnostics/counters, context replay and cancellation");
    }
}
