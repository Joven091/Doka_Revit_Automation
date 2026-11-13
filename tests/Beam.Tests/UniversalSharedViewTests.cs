using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;
using Bound=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;

internal static class UniversalSharedViewTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception("Universal shared views: "+reason);}
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static Bound.UniversalProfileSignature Signature(BeamDistributedLoad[] q=null,double shear=22,double moment=10)
        =>new Bound.UniversalProfileSignature(q??new[]{Q(0,1000,10),Q(1000,2000,10)},shear,moment);
    static Bound.UniversalView View(Bound.UniversalViewKey key,long weight=1024)
        =>new Bound.UniversalView {Key=key,Weight=weight,Kept=key.Parent};
    internal static void Run()
    {
        var original=new[]{Q(0,1000,10),Q(1000,2000,10)};var signature=Signature(original);var equivalent=Signature();
        Check(signature.Equals(equivalent) && signature.GetHashCode()==equivalent.GetHashCode(),"copied exact atomic profiles have equal precomputed signatures");
        original[0].KnPerM=100;Check(signature.Equals(equivalent),"a signature owns its numeric snapshot independently of its source load objects");
        Check(!signature.Equals(Signature(new[]{Q(0,2000,10)})),"equal density cannot erase an atomic clipping boundary");
        Check(!signature.Equals(Signature(new[]{Q(1000,2000,10),Q(0,1000,10)})),"load arithmetic order is part of the signature");
        Check(!signature.Equals(Signature(new[]{Q(0,1000,Math.BitIncrement(10.0)),Q(1000,2000,10)})),"one-bit density changes have separate evidence");
        Check(!signature.Equals(Signature(shear:Math.BitIncrement(22.0))) && !signature.Equals(Signature(moment:Math.BitIncrement(10.0))),"both generic section capacities are exact signature inputs");
        var source=new[]{new TransferredSlabOptimizer.Geometry()};var key=new Bound.UniversalViewKey(signature,source,150);
        var same=new Bound.UniversalViewKey(equivalent,source,150);
        Check(key.Equals(same) && key.GetHashCode()==same.GetHashCode(),"view keys combine exact numeric profile with parent identity and edge");
        Check(!key.Equals(new Bound.UniversalViewKey(signature,source.ToArray(),150)),"another parent array has an independent retained universe");
        Check(!key.Equals(new Bound.UniversalViewKey(signature,source,Math.BitIncrement(150.0))),"one-bit absolute origin changes have separate evidence");
        var cache=new Bound.UniversalViewCache(4096,2);var trace=new BeamSearchTrace("shared universal cache checks");var first=View(key);
        Check(ReferenceEquals(cache.Publish(first,trace),first) && cache.TryGet(same,trace,out var found) && ReferenceEquals(first,found),"separate numeric snapshots share only completed identical views");
        Check(trace.Count(BeamSearchCounter.UniversalGeometrySharedProfileHits)==1,"equivalent-snapshot hits are measured separately from ordinary same-context hits");
        Check(ReferenceEquals(cache.Publish(View(same),trace),first) && cache.RetainedBytes==1024,"concurrent-style double publication retains one complete view and one byte charge");
        var secondKey=new Bound.UniversalViewKey(signature,source,250);cache.Publish(View(secondKey),trace);cache.TryGet(key,trace,out _);
        var thirdKey=new Bound.UniversalViewKey(signature,source,350);cache.Publish(View(thirdKey),trace);
        Check(cache.TryGet(key,trace,out _) && !cache.TryGet(secondKey,trace,out _) && cache.TryGet(thirdKey,trace,out _),"whole-pass LRU updates on use and evicts the least recently used entry");
        Check(cache.RetainedBytes==2048 && !cache.CanRetain(signature,int.MaxValue),"entry payload and upfront admission remain bounded");
        var bytes=new Bound.UniversalViewCache(1536,8);bytes.Publish(View(key),trace);bytes.Publish(View(secondKey),trace);
        Check(bytes.RetainedBytes==1024 && !bytes.TryGet(key,trace,out _) && bytes.TryGet(secondKey,trace,out _),"byte pressure evicts complete entries even below the entry limit");
        bytes.Publish(View(thirdKey,2048),trace);Check(bytes.RetainedBytes==1024 && !bytes.TryGet(thirdKey,trace,out _),"oversized complete values do not enter or disturb the shared history");
        var parallel=new Bound.UniversalViewCache(4096,4);Parallel.For(0,16,_=>parallel.Publish(View(key),trace));
        Check(parallel.RetainedBytes==1024 && parallel.TryGet(key,trace,out _),"parallel complete publications cannot duplicate a key or inflate retained bytes");
        using(var token=new CancellationTokenSource())
        {
            token.Cancel();var cancelled=new BeamSearchTrace("cancelled shared view",token.Token);bool get=false,store=false;
            try{cache.TryGet(key,cancelled,out _);}catch(OperationCanceledException){get=true;}
            try{cache.Publish(View(secondKey),cancelled);}catch(OperationCanceledException){store=true;}
            Check(get && store && !cache.TryGet(secondKey,trace,out _),"cancellation propagates through both hit and publication without adding evidence");
        }
        Console.WriteLine("PASS "+checks+" universal shared-view checks: exact immutable signatures, source identity, equivalent snapshots, bounded LRU, parallel publication and cancellation");
    }
}
