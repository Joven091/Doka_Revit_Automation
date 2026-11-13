using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class SpanShapeHistoryTests
{
    static int checks;
    static void Check(bool value,string message){Interlocked.Increment(ref checks);if(!value)throw new Exception("Span shape history: "+message);}
    static TransferredSlabOptimizer.Geometry Geometry()=>new TransferredSlabOptimizer.Geometry {
        Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=2500,FirstSupport=0,LastSupport=2},new TowerStockPiece {StartMm=2500,StockLengthMm=3000,FirstSupport=3,LastSupport=5}},
        Positions=new[] {150.0,1150,2350,2650,4000,5350},Joined=new[] {true,true,false,true,true},
        MemberSupports=new[] {new[] {150.0,1150,2350},new[] {150.0,1500,2850}}};
    static BeamDistributedLoad[] Bands()=>new[] {new BeamDistributedLoad {StartMm=500,EndMm=900,KnPerM=-1},new BeamDistributedLoad {StartMm=2250,EndMm=2800,KnPerM=-1}};
    static TransferredSlabOptimizer.SpanGeometryContext Context(double edge=0)=>new TransferredSlabOptimizer.SpanGeometryContext(edge,Bands());
    static bool SameBits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
    static void ShapeEqual(TransferredSlabOptimizer.SpanGeometryShape actual,TransferredSlabOptimizer.SpanGeometryShape expected)
    {
        Check(actual.Context.Same(expected.Context) && actual.Tails.Zip(expected.Tails,SameBits).All(x=>x) && actual.ClearSpans.Zip(expected.ClearSpans,SameBits).All(x=>x)
            && SameBits(actual.MaximumTail,expected.MaximumTail) && SameBits(actual.MaximumClearSpan,expected.MaximumClearSpan),"owned reuse preserves exact complete tail/span metadata");
    }
    static void IdentityAndImmutability()
    {
        var geometry=Geometry();var owner=new TransferredSlabOptimizer.GeometryCache();var trace=new BeamSearchTrace("normal span metrics");
        var bands=Bands();var a=new TransferredSlabOptimizer.SpanGeometryContext(0,bands);var b=Context(25.125);
        var first=TransferredSlabOptimizer.PrepareSpanShape(geometry,a,trace,owner);
        TransferredSlabOptimizer.PrepareSpanShape(geometry,b,trace,owner);
        var same=TransferredSlabOptimizer.PrepareSpanShape(geometry,new TransferredSlabOptimizer.SpanGeometryContext(0,Bands()),trace,owner);
        Check(ReferenceEquals(first,same) && owner.SpanShapes.Count==2 && owner.SpanShapes.Builds==2 && owner.SpanShapes.Hits==1,"A/B/A interleaving reuses its bounded history across value-equal context instances");
        Check(trace.Count(BeamSearchCounter.SpanGeometryBuilds)==2 && trace.Count(BeamSearchCounter.SpanGeometryHits)==1,"normal unrestricted searches report shape builds and reuse without enabling timing");
        Check(ReferenceEquals(same,TransferredSlabOptimizer.PrepareSpanShape(geometry,a,trace,owner)) && owner.SpanShapes.Hits==1,"the volatile last-slot fast path avoids the shared history lock");
        bands[0].StartMm=100;bands[1].EndMm=3000;
        Check(a.Boundaries[0]==500 && a.Boundaries[3]==2800 && a.Same(Context()),"context owns an immutable prehashed snapshot of source footprint values");
        bool readOnly=false;try{a.Boundaries[0]=1;}catch(NotSupportedException){readOnly=true;}
        Check(readOnly && a.Same(Context()),"inspection cannot mutate the hashed context through its boundary collection");
        var edge=Context(Math.BitIncrement(25.125));var edgeShape=TransferredSlabOptimizer.PrepareSpanShape(geometry,edge,trace,owner);
        Check(!ReferenceEquals(edgeShape,TransferredSlabOptimizer.PrepareSpanShape(geometry,b,trace,owner)),"adjacent representable edges retain distinct exact contexts");
        var changed=Bands();changed[0].StartMm=Math.BitIncrement(changed[0].StartMm);
        var shifted=new TransferredSlabOptimizer.SpanGeometryContext(0,changed);
        Check(!a.Same(shifted) && !ReferenceEquals(first,TransferredSlabOptimizer.PrepareSpanShape(geometry,shifted,trace,owner)),"adjacent representable footprint boundaries are not rounded or merged");
        var reversed=new TransferredSlabOptimizer.SpanGeometryContext(0,Bands().Reverse().ToArray());
        Check(!a.Same(reversed),"ordered footprint sequences remain distinct");
        var differentLoad=Bands();foreach(var band in differentLoad)band.KnPerM=-999;
        Check(a.Same(new TransferredSlabOptimizer.SpanGeometryContext(0,differentLoad)),"pure shape keys deliberately contain no current load or acceptance decision");
        var otherGeometry=Geometry();var other=TransferredSlabOptimizer.PrepareSpanShape(otherGeometry,a,trace,owner);
        Check(!ReferenceEquals(first,other),"different immutable geometry objects retain separate exact history entries");ShapeEqual(other,first);
    }
    static readonly BindingFlags hidden=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Type crossType=typeof(TransferredSlabOptimizer).GetNestedType("CrossOption",BindingFlags.NonPublic);
    static readonly MethodInfo screen=typeof(TransferredSlabOptimizer).GetMethod("PreparedSpanScreen",BindingFlags.Static|BindingFlags.NonPublic);
    static object Cross(double q)
    {
        object cross=Activator.CreateInstance(crossType,true);
        crossType.GetField("SpanPrepared",hidden).SetValue(cross,true);crossType.GetField("MinimumLineLoads",hidden).SetValue(cross,new[] {q/2,q});
        crossType.GetField("UnknownSpans",hidden).SetValue(cross,Bands());return cross;
    }
    static bool Screen(TowerInput input,TransferredSlabOptimizer.Geometry geometry,object cross,double edge)
    {
        try{return (bool)screen.Invoke(null,new object[] {input,geometry,cross,edge,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void LivePhysics()
    {
        var geometry=Geometry();var owner=new TransferredSlabOptimizer.GeometryCache();
        foreach(bool transferOwner in new[] {false,true})
        {
            var input=new TowerInput {NetworkGeometryWork=transferOwner ? null : owner,TransferredSlab=transferOwner ? new TransferredSlabInput {SharedGeometryCache=owner} : null};
            foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
            foreach(double q in new[] {0.0,1,10,22,31,50,100})foreach(double edge in new[] {0.0,25.125,0})
            {
                input.PrimaryMaterial=material;var cross=Cross(q);input.PrepareSpanGeometry=false;bool expected=Screen(input,geometry,cross,edge);
                input.PrepareSpanGeometry=true;Check(Screen(input,geometry,cross,edge)==expected,"history-backed production screen matches original per-span traversal with current load/material/self-weight");
            }
        }
        Check(owner.SpanShapes.Count==2 && owner.SpanShapes.Hits>0,"production resolves both transfer-owner and network-owner histories");
        var h20=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,NetworkGeometryWork=owner};
        var steel=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.WS10,NetworkGeometryWork=owner};
        var context=Context();Check(!TransferredSlabOptimizer.PreparedSpanShapeFits(h20,geometry,context,100) && TransferredSlabOptimizer.PreparedSpanShapeFits(steel,geometry,context,100),"shared shape reuse does not replay another material's capacity decision");
        long builds=owner.SpanShapes.Builds,hits=owner.SpanShapes.Hits;
        h20.ReuseSearchWork=false;TransferredSlabOptimizer.PreparedSpanShapeFits(h20,geometry,Context(50),10);
        Check(owner.SpanShapes.Builds==builds && owner.SpanShapes.Hits==hits,"disabled work reuse does not consult or publish an owner history");
    }
    static void CancellationAndFailure()
    {
        var owner=new TransferredSlabOptimizer.GeometryCache();var geometry=Geometry();var a=Context();
        var first=TransferredSlabOptimizer.PrepareSpanShape(geometry,a,owner:owner);
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();var trace=new BeamSearchTrace("cancel warm shape",cancellation.Token);bool cancelled=false;
            try{TransferredSlabOptimizer.PrepareSpanShape(geometry,a,trace,owner);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled && owner.SpanShapes.Count==1 && ReferenceEquals(first,geometry.SpanShape),"cancellation cannot be bypassed by an already warm last slot");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            int calls=0;var trace=new BeamSearchTrace("cancel before publication",cancellation.Token,TimeSpan.FromHours(1),()=>{if(++calls==6)cancellation.Cancel();return 0;});
            bool cancelled=false;
            try{TransferredSlabOptimizer.PrepareSpanShape(geometry,Context(25),trace,owner);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled && owner.SpanShapes.Count==1 && owner.SpanShapes.Builds==1 && ReferenceEquals(first,geometry.SpanShape),"cancellation after private construction publishes neither a history entry nor a last slot");
        }
        var retried=TransferredSlabOptimizer.PrepareSpanShape(geometry,Context(25),owner:owner);
        Check(retried!=null && owner.SpanShapes.Count==2,"a cancelled shape construction remains retryable");
        var broken=Geometry();broken.MemberSupports[1]=null;var empty=new TransferredSlabOptimizer.GeometryCache();bool failed=false;
        try{TransferredSlabOptimizer.PrepareSpanShape(broken,Context(),owner:empty);}catch(NullReferenceException){failed=true;}
        Check(failed && empty.SpanShapes.Count==0 && broken.SpanShape==null,"a failed partial builder cannot enter either geometry cache");
        broken.MemberSupports[1]=Geometry().MemberSupports[1];
        Check(TransferredSlabOptimizer.PrepareSpanShape(broken,Context(),owner:empty)!=null && empty.SpanShapes.Count==1,"repair before first publication does not replay failed construction evidence");
    }
    static void EvictionAndConcurrency()
    {
        var geometry=Geometry();var owner=new TransferredSlabOptimizer.GeometryCache();var a=Context();var first=TransferredSlabOptimizer.PrepareSpanShape(geometry,a,owner:owner);
        for(int edge=1;edge<=100000;edge++)TransferredSlabOptimizer.PrepareSpanShape(geometry,Context(edge),owner:owner);
        Check(owner.SpanShapes.Count<=16384 && owner.SpanShapes.Count>0,"owner history bounds total retained geometry/context entries at16384");
        var rebuilt=TransferredSlabOptimizer.PrepareSpanShape(geometry,a,owner:owner);
        Check(owner.SpanShapes.Count<=16384,"retention and eviction never exceed the total owner bound");ShapeEqual(rebuilt,first);
        var single=new TransferredSlabOptimizer.SpanShapeHistory(1);var next=Context(1);
        var firstKey=new TransferredSlabOptimizer.SpanShapeKey(geometry,a);var nextKey=new TransferredSlabOptimizer.SpanShapeKey(geometry,next);
        single.Publish(firstKey,first,null);single.Publish(nextKey,next.Build(geometry,null),null);
        Check(!single.TryGet(firstKey,null,out _) && single.Count==1,"a one-entry history deterministically evicts the previous exact context");
        var replacement=a.Build(geometry,null);single.Publish(firstKey,replacement,null);
        Check(!ReferenceEquals(first,replacement) && single.TryGet(firstKey,null,out var retained) && ReferenceEquals(retained,replacement),"an evicted shape is rebuilt and published as a complete ordinary result");ShapeEqual(replacement,first);
        var shared=new TransferredSlabOptimizer.GeometryCache();var concurrentGeometry=Geometry();var contexts=Enumerable.Range(0,4).Select(i=>Context(i*25)).ToArray();var canonical=new TransferredSlabOptimizer.SpanGeometryShape[4];
        Parallel.For(0,2000,new ParallelOptions {MaxDegreeOfParallelism=4},index=>
        {
            int at=index%4;var shape=TransferredSlabOptimizer.PrepareSpanShape(concurrentGeometry,contexts[at],owner:shared);
            var prior=Interlocked.CompareExchange(ref canonical[at],shape,null);
            Check(shape.Context.Same(contexts[at]) && (prior==null || ReferenceEquals(prior,shape)),"four workers observe only complete canonical shapes for each exact context");
        });
        Check(shared.SpanShapes.Count==4 && shared.SpanShapes.Builds>=4,"concurrent misses publish one retained entry per exact context");
        foreach(int at in Enumerable.Range(0,4))ShapeEqual(canonical[at],TransferredSlabOptimizer.PrepareSpanShape(Geometry(),contexts[at]));
        foreach(int capacity in new[] {1,2,3,15,16,17,31,100})
        {
            var history=new TransferredSlabOptimizer.SpanShapeHistory(capacity);
            for(int i=0;i<1000;i++)
            {
                var context=Context(i);var key=new TransferredSlabOptimizer.SpanShapeKey(geometry,context);
                var expected=context.Build(geometry,null);var shape=history.Publish(key,expected,null);
                Check(ReferenceEquals(shape,expected) && history.Count<=capacity,"small and uneven capacities retain only complete exact entries within their total bound");
                Check(history.TryGet(key,null,out var found) && ReferenceEquals(found,expected),"the newest exact entry remains immediately retrievable at every supported capacity");
            }
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference OwnerReference(TransferredSlabOptimizer.Geometry geometry)
    {
        var owner=new TransferredSlabOptimizer.GeometryCache();TransferredSlabOptimizer.PrepareSpanShape(geometry,Context(),owner:owner);
        TransferredSlabOptimizer.PrepareSpanShape(geometry,Context(25),owner:owner);return new WeakReference(owner);
    }
    static void Lifetime()
    {
        var geometry=Geometry();var weak=OwnerReference(geometry);
        for(int i=0;i<4 && weak.IsAlive;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
        Check(!weak.IsAlive && geometry.SpanShape!=null,"retaining one geometry's fast slot does not keep an old network owner/history alive");GC.KeepAlive(geometry);
    }
    internal static void Run()
    {
        IdentityAndImmutability();LivePhysics();CancellationAndFailure();EvictionAndConcurrency();Lifetime();
        Console.WriteLine("PASS "+checks+" span-shape history checks: immutable exact keys, production/live-capacity parity,16384 owner bound, four-worker publication, cancellation/fault retry and owner lifetime");
    }
}
