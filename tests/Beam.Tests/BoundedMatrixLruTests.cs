using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class BoundedMatrixLruTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Bounded matrix LRU: "+message);}
    static void Same(double a,double b,string message)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),message);
    static LoadedBeamAnalysis.MatrixKey Key(double delta=0,double ei=899.7512)=>new LoadedBeamAnalysis.MatrixKey(3900,new[] {150.0,1150+delta,2750,3750},Array.Empty<BeamPointLoad>(),ei);
    static LoadedBeamAnalysis.PreparedBeam Prepared(int nodes=8)=>new LoadedBeamAnalysis.PreparedBeam
    {
        Nodes=new List<double>(Enumerable.Range(0,nodes).Select(i=>(double)i)),Supports=new[] {150.0,1150,2750,3750},
        Stiffness=new double[nodes*2,7],Factor=new double[nodes*2,4],FixedDof=new[] {0,2,4,6},PointDof=Array.Empty<int>()
    };
    static void PolicyAndMemory()
    {
        var a=Key();var b=Key(.125);var c=Key(.25);var p=Prepared();var other=Prepared();
        var fifo=new LoadedBeamAnalysis.MatrixCache(2,true);var lru=new LoadedBeamAnalysis.MatrixCache(2,true,leastRecentlyUsed:true);
        foreach(var cache in new[] {fifo,lru}){cache.Store(a,p);cache.Store(b,p);Check(ReferenceEquals(cache.Get(Key()),p),"exact value-key hit");cache.Store(c,p);}
        Check(fifo.Get(a)==null&&ReferenceEquals(fifo.Get(b),p),"original FIFO reference is unchanged by hits");
        Check(lru.Get(b)==null&&ReferenceEquals(lru.Get(a),p),"LRU hit refresh protects A and evicts B");
        lru.Store(Key(),other);Check(ReferenceEquals(lru.Get(a),p),"duplicate stores do not overwrite immutable preparation");
        Check(lru.Get(Key(PortableTestRuntime.NextUp(1150)-1150))==null,"adjacent IEEE support coordinate is not rounded");
        Check(lru.Get(Key(ei:PortableTestRuntime.NextUp(899.7512)))==null,"adjacent IEEE stiffness is not rounded");
        var untouched=new LoadedBeamAnalysis.MatrixCache(1);untouched.Store(a,p);untouched.Store(b,p);
        Check(ReferenceEquals(untouched.Get(a),p)&&untouched.Get(b)==null,"default cache retains the original no-admission-after-capacity policy");
        long size=LoadedBeamAnalysis.MatrixCache.EstimateEntryBytes(a,p);
        Check(size>=8L*(p.Stiffness.LongLength+p.Factor.LongLength)+8L*(p.Nodes.Capacity+p.Supports.Length),"byte estimate includes full retained numerical payload");
        var weighted=new LoadedBeamAnalysis.MatrixCache(100,true,leastRecentlyUsed:true,maximumBytes:size*2);
        weighted.Store(a,p);weighted.Store(b,p);weighted.Get(a);weighted.Store(c,p);
        Check(weighted.RetainedEntryCount==2&&weighted.EstimatedRetainedBytes==size*2,"byte cap evicts before the entry-count cap");
        Check(weighted.Get(b)==null&&ReferenceEquals(weighted.Get(a),p),"byte eviction follows refreshed LRU order");
        long before=weighted.EstimatedRetainedBytes;weighted.Store(Key(.5),Prepared(250));
        Check(weighted.RetainedEntryCount==2&&weighted.EstimatedRetainedBytes==before&&ReferenceEquals(weighted.Get(a),p),"oversized shape is not retained and does not flush useful entries");
        var tiny=new LoadedBeamAnalysis.MatrixCache(5,true,leastRecentlyUsed:true,maximumBytes:1);tiny.Store(a,p);
        Check(tiny.RetainedEntryCount==0&&tiny.EstimatedRetainedBytes==0&&tiny.Get(a)==null,"one oversized first entry cannot exceed the memory cap");
        for(int i=0;i<250;i++)
        {
            var key=Key(i/16.0);weighted.Get(key);weighted.Store(key,Prepared(5+i%5));
            Check(weighted.EstimatedRetainedBytes<=size*2&&weighted.RetainedEntryCount<=100,"variable-size stream remains within both bounds");
        }
        int invalid=0;try{new LoadedBeamAnalysis.MatrixCache(0,true,leastRecentlyUsed:true);}catch(ArgumentOutOfRangeException){invalid++;}
        try{new LoadedBeamAnalysis.MatrixCache(1,true,leastRecentlyUsed:true,maximumBytes:0);}catch(ArgumentOutOfRangeException){invalid++;}
        Check(invalid==2,"invalid new LRU bounds are rejected");
    }
    static void Analysis(LoadedBeamAnalysis expected,LoadedBeamAnalysis actual)
    {
        foreach(var property in typeof(LoadedBeamAnalysis).GetProperties(BindingFlags.Instance|BindingFlags.Public))
        {
            if(property.PropertyType==typeof(double))Same((double)property.GetValue(expected),(double)property.GetValue(actual),"scalar "+property.Name);
            if(property.PropertyType==typeof(bool))Check((bool)property.GetValue(expected)==(bool)property.GetValue(actual),"completion flag");
        }
        Check(expected.Reactions.Length==actual.Reactions.Length,"reaction count");
        for(int i=0;i<expected.Reactions.Length;i++)Same(expected.Reactions[i],actual.Reactions[i],"reaction bits");
        var field=typeof(LoadedBeamAnalysis).GetField("elements",BindingFlags.Instance|BindingFlags.NonPublic);var a=(IList)field.GetValue(expected);var b=(IList)field.GetValue(actual);
        Check(a.Count==b.Count,"coefficient count");
        for(int i=0;i<a.Count;i++)foreach(var coefficient in a[i].GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
            Same((double)coefficient.GetValue(a[i]),(double)coefficient.GetValue(b[i]),"full coefficient bits");
        foreach(double x in Enumerable.Range(0,21).Select(i=>3900.0*i/20))foreach(bool right in new[] {false,true})
        {
            var lhs=expected.Response(x,right);var rhs=actual.Response(x,right);for(int i=0;i<lhs.Length;i++)Same(lhs[i],rhs[i],"force/displacement response bits");
        }
        Same(expected.DeflectionWithAffineOffsetMm(.625,-.375,150,3750),actual.DeflectionWithAffineOffsetMm(.625,-.375,150,3750),"support-movement response");
        Same(expected.WS10StrengthUtilization(),actual.WS10StrengthUtilization(),"WS10 coefficient interaction");
    }
    static void NumericalAndCancellation()
    {
        var lru=new LoadedBeamAnalysis.MatrixCache(3,true,leastRecentlyUsed:true,maximumBytes:7000);var fifo=new LoadedBeamAnalysis.MatrixCache(3,true);
        for(int i=0;i<72;i++)
        {
            int criterion=new[] {270,350,500}[i%3];double ei=i%2==0 ? TowerOptimizer.PairEI : 2200.125;
            var supports=new[] {150.0,1150+(i%4)*.125,2750,3750};
            var uniform=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=.125+i%3},new BeamDistributedLoad {StartMm=725.125,EndMm=3075.625,KnPerM=i%2==0 ? -.375 : .625}};
            var points=new[] {new BeamPointLoad {PositionMm=1725.125,Kn=.125+i%4}};
            var expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(3900,supports,uniform,points,ei,criterion,allowSigned:true);
            Analysis(expected,LoadedBeamAnalysis.AnalyzeCombinedCached(3900,supports,uniform,points,ei,criterion,lru,allowSignedLoads:true));
            Analysis(expected,LoadedBeamAnalysis.AnalyzeCombinedCached(3900,supports,uniform,points,ei,criterion,fifo,allowSignedLoads:true));
            if(i%5==0)
            {
                var changed=uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*.625}).ToArray();
                var reference=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(3900,supports,changed,points,ei,criterion,allowSigned:true);long hits=lru.Hits;
                var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(3900,supports,changed,points,ei,criterion,lru,allowSignedLoads:true);Analysis(reference,actual);
                Check(lru.Hits==hits+1,"real changed-load solve reuses its immutable LRU preparation");
                var lhs=new TowerSearchDiagnostics(criterion,ignoreUplift:true);var rhs=new TowerSearchDiagnostics(criterion,ignoreUplift:true);
                lhs.Analysis(reference,1,2,"P1 L",3,"Crossing resolved primary");rhs.Analysis(actual,1,2,"P1 L",3,"Crossing resolved primary");
                lhs.Legs(reference.Reactions,70,"B1 stage legs","Crossing resolved primary");rhs.Legs(actual.Reactions,70,"B1 stage legs","Crossing resolved primary");
                typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {lhs,rhs});checks++;
            }
            Check(lru.RetainedEntryCount<=3&&lru.EstimatedRetainedBytes<=7000,"numerical misses/hits and evictions retain bounded payload");
        }
        var work=new TransferredSlabOptimizer.SearchWorkCache();var trace=new BeamSearchTrace("LRU cancellation parity");
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,ReuseSearchWork=true,ReuseNetworkNumerics=true,ReuseTransverseNumerics=true,OptimizePrimaryResponses=true,NetworkNumerics=work,Trace=trace};
        var stations=new[] {150.0,1150,2750,3750};var loads=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=.625},new BeamDistributedLoad {StartMm=725,EndMm=3075,KnPerM=.125}};
        TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.PrimaryAnalyses,3900,stations,loads,Array.Empty<BeamPointLoad>(),input.Primary.EI,270,signed:true);
        Check(work.Matrix.RetainedEntryCount==1&&work.Matrix.EstimatedRetainedBytes>0,"shared numerical caller uses the bounded LRU preparation");
        using(var cancellation=new CancellationTokenSource())
        {
            long hits=work.Matrix.Hits;cancellation.Cancel();input.Trace=new BeamSearchTrace("Cancelled LRU warm response",cancellation.Token);
            bool stopped=false;try{TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.PrimaryAnalyses,3900,stations,loads,Array.Empty<BeamPointLoad>(),input.Primary.EI,270,signed:true);}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&work.Matrix.Hits==hits,"existing numerical cancellation guard precedes warm cache reuse");
        }
    }
    internal static void Run()
    {
        PolicyAndMemory();NumericalAndCancellation();
        Console.WriteLine("PASS "+checks+" bounded matrix LRU checks: original defaults/FIFO, exact keys, hit refresh, byte/count eviction, oversize shapes, full numerical/diagram parity and cancellation");
    }
}
