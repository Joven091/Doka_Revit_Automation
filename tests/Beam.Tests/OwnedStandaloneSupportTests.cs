using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class OwnedStandaloneSupportTests
{
    const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int checks;
    static readonly FieldInfo storedSupports=typeof(TowerBeamAnalysis).GetField("supports",hidden);
    static readonly MethodInfo planParity=typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnosticParity=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Owned standalone supports: "+message);}
    static void Same(double a,double b,string message)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),message);
    static TowerInput Input(bool custom=false,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20)
    {
        var input=new TowerInput {LengthMm=6000,TotalLineLoad=12,LegCapacityKn=70,PrimaryMaterial=material,DeflectionDenominator=270,
            StockLengthsMm=new[] {1250.0,1800,2000,2450,3600,3900,4200,4900},AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,
            StockEndSetbackMm=150,Trace=new BeamSearchTrace("owned local stock support response")};
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static void Response(TowerBeamAnalysis a,TowerBeamAnalysis b,double length)
    {
        Same(a.Moment,b.Moment,"moment bits");Same(a.Shear,b.Shear,"shear bits");Same(a.DeflectionMm,b.DeflectionMm,"peak displacement bits");
        Check(a.Reactions.Length==b.Reactions.Length,"reaction count");for(int i=0;i<a.Reactions.Length;i++)Same(a.Reactions[i],b.Reactions[i],"reaction bits");
        Check(a.DeflectionRegions.Count==b.DeflectionRegions.Count,"complete region count");
        for(int i=0;i<a.DeflectionRegions.Count;i++)
        {
            var x=a.DeflectionRegions[i];var y=b.DeflectionRegions[i];Same(x.StartMm,y.StartMm,"region start");Same(x.EndMm,y.EndMm,"region end");Same(x.PeakMm,y.PeakMm,"region peak");Check(x.IsOverhang==y.IsOverhang,"region provenance");
        }
        foreach(int criterion in new[] {270,350,500})
        {
            Same(a.DeflectionUtilization(0,criterion),b.DeflectionUtilization(0,criterion),"criterion utilization bits");
            var x=a.GoverningDeflection(0,criterion);var y=b.GoverningDeflection(0,criterion);Same(x.StartMm,y.StartMm,"governing start");Same(x.EndMm,y.EndMm,"governing end");Same(x.PeakMm,y.PeakMm,"governing peak");
        }
        var stations=(double[])storedSupports.GetValue(a);
        var points=Enumerable.Range(0,41).Select(i=>length*i/40).Concat(stations.SelectMany(x=>new[] {Math.Max(0,x-1e-10),x,Math.Min(length,x+1e-10)}));
        foreach(double point in points)foreach(bool right in new[] {false,true})
        {var x=a.Response(point,right);var y=b.Response(point,right);for(int i=0;i<x.Length;i++)Same(x[i],y[i],"full response grid/face bits");}
    }
    static void ExactAnalysisAndPublicOwnership()
    {
        foreach(var supports in new[] {new[] {.15,3.75},new[] {0.0,1.125,2.675,3.9},new[] {.125,.875,1.675,2.925,3.775}})
        foreach(double load in new[] {double.Epsilon,.125,8.125,35.0,90.0})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            double ei=PrimaryBeamProfile.For(material).EI;var fresh=(double[])supports.Clone();
            var expected=TowerBeamAnalysis.Analyze(3.9,supports,load,ei);var actual=TowerBeamAnalysis.AnalyzeOwned(3.9,fresh,load,ei);
            Check(!ReferenceEquals(storedSupports.GetValue(expected),supports),"public analysis preserves caller support ownership");
            Check(ReferenceEquals(storedSupports.GetValue(actual),fresh),"internal analysis retains only the transferred fresh array");Response(expected,actual,3.9);
            var cache=new TowerStockPlanner.FindWorkCache();var caller=(double[])supports.Clone();var safe=cache.Analyze(Input(),3.9,caller,load,ei);
            Check(!ReferenceEquals(storedSupports.GetValue(safe),caller),"existing cache.Analyze keeps its original copy-safe behavior");
            caller[0]+=.01;Response(expected,safe,3.9);
        }
        foreach(var args in new[] {
            new object[] {0.0,new[] {.15,3.75},1.0,899.7512},new object[] {3.9,(double[])null,1.0,899.7512},
            new object[] {3.9,new[] {.15,.15},1.0,899.7512},new object[] {3.9,new[] {-.1,3.75},1.0,899.7512},
            new object[] {3.9,new[] {.15,3.75},0.0,899.7512},new object[] {3.9,new[] {.15,3.75},1.0,double.NaN}})
        {
            Exception a=null,b=null;
            try{TowerBeamAnalysis.Analyze((double)args[0],(double[])args[1],(double)args[2],(double)args[3]);}catch(Exception error){a=error;}
            try{TowerBeamAnalysis.AnalyzeOwned((double)args[0],(double[])args[1],(double)args[2],(double)args[3]);}catch(Exception error){b=error;}
            Check(a!=null&&b!=null&&a.GetType()==b.GetType()&&a.Message==b.Message,"owned path preserves complete validation and exception behavior");
        }
        var endSupports=new[] {.15,1.25,2.675,3.75};
        var endReference=TowerBeamAnalysis.Analyze(3.9,endSupports,8.125,899.7512,2.5,7.5);
        var endActual=TowerBeamAnalysis.Analyze(3.9,(double[])endSupports.Clone(),8.125,899.7512,2.5,7.5);
        endSupports[0]+=.01;Response(endReference,endActual,3.9);
        Check(Math.Abs(endReference.Reactions.Sum()-(8.125*3.9+2.5+7.5))<1e-9,"the unchanged public API retains both independent end loads and equilibrium");
    }
    static IDictionary Responses(TowerStockPlanner.FindWorkCache cache)
    {
        var owner=typeof(TowerStockPlanner.FindWorkCache).GetField("responses",hidden).GetValue(cache);
        return (IDictionary)owner.GetType().GetFields(hidden).First(f=>typeof(IDictionary).IsAssignableFrom(f.FieldType)).GetValue(owner);
    }
    static TowerBeamAnalysis EntryResponse(object node)
    {
        var pair=node.GetType().GetProperty("Value").GetValue(node);
        return (TowerBeamAnalysis)pair.GetType().GetProperty("Value").GetValue(pair);
    }
    static void ProductionOwnershipAndPlanParity()
    {
        var positions=new[] {150.0,1150,1600,2250,3000,3600,4050,4850,5850};
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(double q in new[] {.125,6.125,35.0})
        {
            var a=Input(custom,material);var b=Input(custom,material);var cache=new TowerStockPlanner.FindWorkCache(1);
            var expected=TowerStockPlanner.Find(a,positions,q,new TowerStockPlanner.FindWorkCache(1,optimizeStockLoop:false));
            var actual=TowerStockPlanner.Find(b,positions,q,cache);planParity.Invoke(null,new object[] {expected,actual});checks++;
            diagnosticParity.Invoke(null,new object[] {a.Diagnostics,b.Diagnostics});checks++;
            foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.PrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
                Check(a.Trace.Count(counter)==b.Trace.Count(counter),"responses and diagnostics retain exact call/counter provenance");
            foreach(DictionaryEntry entry in Responses(cache))
            {
                var keySupports=entry.Key.GetType().GetField("supports",hidden).GetValue(entry.Key);var response=EntryResponse(entry.Value);
                Check(ReferenceEquals(keySupports,storedSupports.GetValue(response)),"only the internal owner shares its fresh cache-key support array");
                Check(!ReferenceEquals(keySupports,positions),"production support ownership never aliases caller geometry");
            }
            var saved=(double[])positions.Clone();positions[1]+=1;
            planParity.Invoke(null,new object[] {expected,TowerStockPlanner.Find(b,saved,q,cache)});checks++;
            positions[1]-=1;
            if(actual!=null)
            {actual.Reactions[0]+=123;actual.Pieces[0].StartMm+=25;planParity.Invoke(null,new object[] {expected,TowerStockPlanner.Find(b,saved,q,cache)});checks++;}
        }
        foreach(bool diagnosticBudget in new[] {false,true})
        {
            var input=Input();var cache=new TowerStockPlanner.FindWorkCache();
            var timed=new BeamSearchTrace("unexpired copy-safe analysis budget",searchTimeLimit:TimeSpan.FromHours(1));
            if(diagnosticBudget)input.Diagnostics=new TowerSearchDiagnostics(270,trace:timed);else input.Trace=timed;
            TowerStockPlanner.Find(input,positions,6.125,cache);Check(Responses(cache).Count>0,"budget fixture performs actual member analysis");
            foreach(DictionaryEntry entry in Responses(cache))
                Check(!ReferenceEquals(entry.Key.GetType().GetField("supports",hidden).GetValue(entry.Key),storedSupports.GetValue(EntryResponse(entry.Value))),"either explicit budget retains the eager support-copy path");
        }
        using(var cancelled=new CancellationTokenSource())
        {
            var input=Input();var cache=new TowerStockPlanner.FindWorkCache();TowerStockPlanner.Find(input,positions,6.125,cache);
            cancelled.Cancel();input.Trace=new BeamSearchTrace("cancelled owned support cache",cancelled.Token);
            bool stopped=false;try{TowerStockPlanner.Find(input,positions,6.125,cache);}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"owned support storage cannot bypass cancellation before a retained plan");
        }
    }
    internal static void Run(){ExactAnalysisAndPublicOwnership();ProductionOwnershipAndPlanParity();Console.WriteLine("PASS "+checks+" owned standalone support checks: IEEE responses/regions, public and cache ownership, fresh private owner, validation, eviction, selected plans/diagnostics, mutation, cancellation and budget fallback");}
}
