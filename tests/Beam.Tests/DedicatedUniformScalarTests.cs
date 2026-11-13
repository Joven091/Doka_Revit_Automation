using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class DedicatedUniformScalarTests
{
    const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly MethodInfo checkDedicated=typeof(CrossBeamSupportOptimizer).GetMethod("CheckDedicatedPrimary",BindingFlags.Static|BindingFlags.NonPublic);
    static int checks,rejected,retained;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Dedicated uniform scalar: "+message);}
    static void Same(double a,double b,string message)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),message);
    static TowerInput Input(TransferredSlabOptimizer.SearchWorkCache shared,int criterion=270,double capacity=70,bool lazy=true)=>new TowerInput
    {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,DeflectionDenominator=criterion,LegCapacityKn=capacity,NetworkNumerics=shared,Trace=new BeamSearchTrace("dedicated uniform scalar parity"),
        Diagnostics=new TowerSearchDiagnostics(criterion,ignoreUplift:false){LazyLocations=lazy}};
    static LoadedBeamAnalysis Unit(double[] supports,int criterion)=>LoadedBeamAnalysis.AnalyzeCombinedCached(3900,supports,new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=1}},Array.Empty<BeamPointLoad>(),TowerOptimizer.PairEI,criterion,null);
    static void Seed(TransferredSlabOptimizer.SearchWorkCache shared,double[] supports,int criterion,LoadedBeamAnalysis unit)
        =>shared.UniformShapes.Store(new TransferredSlabOptimizer.UniformShapeKey(3900,supports,TowerOptimizer.PairEI,criterion),unit);
    static void Diagnostics(TowerSearchDiagnostics expected,TowerSearchDiagnostics actual)
    {
        var type=typeof(TowerSearchDiagnostics);
        Check((int)type.GetField("checks",hidden).GetValue(expected)==(int)type.GetField("checks",hidden).GetValue(actual),"exact diagnostic check count");
        var a=(IDictionary)type.GetField("failures",hidden).GetValue(expected);var b=(IDictionary)type.GetField("failures",hidden).GetValue(actual);
        Check(a.Count==b.Count,"exact diagnostic failure count");
        foreach(DictionaryEntry entry in a)
        {
            Check(b.Contains(entry.Key),"exact diagnostic scope");var other=b[entry.Key];
            foreach(var field in entry.Value.GetType().GetFields(hidden))
            {
                var x=field.GetValue(entry.Value);var y=field.GetValue(other);
                if(x is double number)Same(number,(double)y,"diagnostic IEEE bits: "+field.Name);
                else Check(Equals(x,y),"diagnostic provenance: "+field.Name);
            }
        }
        Check(expected.Exception("parity").Details==actual.Exception("parity").Details,"complete failure report text");
    }
    static TransferMember Actual(TransferMember original,TowerInput common,TowerInput input)
    {
        try{return (TransferMember)checkDedicated.Invoke(null,new object[] {original,common,input,0});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void Case(double[] supports,int criterion,double factor,double inputCapacity,double outerCapacity,bool lazy,LoadedBeamAnalysis unit)
    {
        var expectedWork=new TransferredSlabOptimizer.SearchWorkCache();var actualWork=new TransferredSlabOptimizer.SearchWorkCache();
        Seed(expectedWork,supports,criterion,unit);Seed(actualWork,supports,criterion,unit);
        var expectedInput=Input(expectedWork,criterion,inputCapacity,lazy);var actualInput=Input(actualWork,criterion,inputCapacity,lazy);
        var loads=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=factor}};var points=Array.Empty<BeamPointLoad>();
        var original=new TransferMember {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,Name="P1 L",H20Count=2,LengthMm=3900,SupportsMm=supports,Uniform=loads,Points=points};
        var check=expectedInput.CopyForSearch();check.StagePrimaryChecks=false;
        var expectedAnalysis=TransferredSlabOptimizer.AnalyzePrimary(check,3900,supports,loads,points,expectedWork.Matrix,expectedWork);
        var expected=new TransferMember {PrimaryMaterial=original.PrimaryMaterial,Name=original.Name,H20Count=2,LengthMm=3900,SupportsMm=supports,Uniform=loads,Points=points,Analysis=expectedAnalysis};
        bool passes=CrossBeamSupportOptimizer.Pass(expected,expectedInput,"X1 dedicated primary UDL + end strips") && expectedAnalysis.Reactions.Max()<=outerCapacity+1e-8;
        var common=actualInput.CopyForSearch();common.LegCapacityKn=outerCapacity;
        var actual=Actual(original,common,actualInput);
        Check((actual!=null)==passes,"original complete analysis and exact scalar filter retain identical decisions");
        if(actual==null)rejected++;else
        {
            retained++;
            var a=expected.Analysis;var b=actual.Analysis;
            foreach(var pair in new[] {Tuple.Create(a.MomentKnM,b.MomentKnM),Tuple.Create(a.ShearKn,b.ShearKn),Tuple.Create(a.DeflectionMm,b.DeflectionMm),Tuple.Create(a.DeflectionUtilization,b.DeflectionUtilization)})Same(pair.Item1,pair.Item2,"retained scalar IEEE bits");
            for(int i=0;i<a.Reactions.Length;i++)Same(a.Reactions[i],b.Reactions[i],"retained reaction IEEE bits");
            foreach(double x in new[] {0.0,450,1125,1950,2775,3450,3900})for(int i=0;i<3;i++)Same(a.Response(x)[i],b.Response(x)[i],"retained diagram IEEE bits");
        }
        Diagnostics(expectedInput.Diagnostics,actualInput.Diagnostics);
        foreach(var counter in new[] {BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.UniformPrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
            Check(expectedInput.Trace.Count(counter)==actualInput.Trace.Count(counter),"counter provenance: "+counter);
    }
    internal static void Run()
    {
        foreach(var supports in new[] {new[] {450.0,3450},new[] {450.0,1950,3450},new[] {150.0,1150,2150,3750},new[] {150.0,1150}})
        foreach(int criterion in new[] {270,500})
        {
            var unit=Unit(supports,criterion);double use=Math.Max(unit.DeflectionUtilization,Math.Max(unit.MomentKnM/TowerOptimizer.PairMoment,unit.ShearKn/TowerOptimizer.PairShear));
            double peak=unit.Reactions.Max();
            var factors=new List<double> {0,.125,1,8,35};
            for(int i=-20;i<=20;i++)
            {factors.Add((1+i*1e-8)/use);factors.Add((70+i*1e-7)/peak);}
            foreach(double q in new[] {1/use,70/peak}){factors.Add(Math.BitDecrement(q));factors.Add(Math.BitIncrement(q));}
            foreach(double factor in factors)foreach(bool lazy in new[] {false,true})Case(supports,criterion,factor,70,70,lazy,unit);
            double passing=.25/use,max=peak*passing;
            for(int i=-20;i<=20;i++)Case(supports,criterion,passing,70,max+i*1e-9,true,unit);
            var shared=new TransferredSlabOptimizer.SearchWorkCache();Seed(shared,supports,criterion,unit);var input=Input(shared,criterion);
            var loads=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=4/use}};
            input.Trace=null;input.Diagnostics=null;
            Check(TransferredSlabOptimizer.TryRejectUniformPrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),shared,70,out var fields),"a cached decisive failure is screened");
            var eager=unit.Scale(loads[0].KnPerM);Same(eager.MomentKnM,fields.Moment,"failed moment IEEE bits");Same(eager.ShearKn,fields.Shear,"failed shear IEEE bits");Same(eager.DeflectionUtilization,fields.DeflectionUse,"failed deflection IEEE bits");
            Same(eager.Reactions.Max(),fields.MaximumReaction,"failed peak-reaction IEEE bits");Same(eager.Reactions.Min(),fields.MinimumReaction,"failed minimum-reaction IEEE bits");
            Check(Array.IndexOf(eager.Reactions,eager.Reactions.Max())==fields.PeakReaction,"failed first peak support index");
            foreach(bool lazy in new[] {false,true})
            {
                var oldLegs=new TowerSearchDiagnostics(criterion,ignoreUplift:false){LazyLocations=lazy};
                var scalarLegs=new TowerSearchDiagnostics(criterion,ignoreUplift:false){LazyLocations=lazy};
                oldLegs.Legs(eager.Reactions,Math.Abs(fields.MaximumReaction)/2,"Leg parity","Same exact receiver");
                scalarLegs.LegsScalars(fields.MaximumReaction,fields.MinimumReaction,fields.PeakReaction,Math.Abs(fields.MaximumReaction)/2,"Leg parity","Same exact receiver");Diagnostics(oldLegs,scalarLegs);
            }
            long before=GC.GetAllocatedBytesForCurrentThread();int failures=0;
            for(int i=0;i<256;i++)if(TransferredSlabOptimizer.TryRejectUniformPrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),shared,70,out _))failures++;
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(failures==256 && allocated<2048,"cached decisive scalar rejection allocates no response/input/member objects");
            var empty=new TransferredSlabOptimizer.SearchWorkCache();Check(!TransferredSlabOptimizer.TryRejectUniformPrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),empty,70,out _),"a cold unit falls through to ordinary numerical analysis");
            input.PrimaryMaterial=PrimaryBeamMaterial.WS10;Check(!TransferredSlabOptimizer.TryRejectUniformPrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),shared,70,out _),"WS10 keeps its complete ordinary path");input.PrimaryMaterial=PrimaryBeamMaterial.DoubleH20;
            Check(!TransferredSlabOptimizer.TryRejectUniformPrimary(input,3900,supports,loads,new[] {new BeamPointLoad {PositionMm=1500,Kn=1}},shared,70,out _),"point loads keep their ordinary path");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();input.Trace=new BeamSearchTrace("cancelled cached scalar check",cancellation.Token);bool cancelled=false;
                try{TransferredSlabOptimizer.TryRejectUniformPrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),shared,70,out _);}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled,"cancellation precedes a cached scalar decision");
            }
        }
        Check(rejected>0 && retained>0,"both decisive failures and complete retained responses are exercised");
        Console.WriteLine("PASS "+checks+" dedicated scalar checks: "+rejected+" rejected, "+retained+" retained; exact diagnostics, counters, adjacent boundaries, allocations and cancellation");
    }
}
