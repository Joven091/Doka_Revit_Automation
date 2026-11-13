using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;
using Geometry=EngineeringDashboard.TransferredSlabOptimizer.Geometry;
using Pair=EngineeringDashboard.TransferredSlabOptimizer.ReceivingPairKey;

internal static class ReceivingHotKeyTests
{
    static int checks,nativeEntries;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Receiving hot key: "+text);}
    static double From(ulong bits)=>BitConverter.Int64BitsToDouble(unchecked((long)bits));
    static void PairContract()
    {
        var values=new[]{0.0,From(0x8000000000000000),1.0,-1,From(0x3ff0000100000001),double.Epsilon,-double.Epsilon,double.NaN,From(0x7ff0000000000001),From(0xfff8000000000002),double.PositiveInfinity,double.NegativeInfinity,double.MaxValue,-double.MaxValue,150,1524};
        var reference=new Dictionary<Tuple<double,double>,int>();var actual=new Dictionary<Pair,int>();int next=0;
        foreach(double a in values)foreach(double b in values)
        {
            var old=Tuple.Create(a,b);var key=new Pair(a,b);
            Check(old.GetHashCode()==key.GetHashCode(),"the actual key exactly matches Tuple2 hashing, including NaN payloads and signed zero");
            foreach(double c in values)
            {
                Check(old.Equals(Tuple.Create(c,b))==key.Equals(new Pair(c,b)),"first coordinate uses the original Double.Equals relation");
                Check(old.Equals(Tuple.Create(a,c))==key.Equals(new Pair(a,c)),"second coordinate uses the original Double.Equals relation");
            }
            reference[old]=next;actual[key]=next++;
        }
        Check(reference.Count==actual.Count,"IEEE equivalence classes produce the same occupied entries");
        foreach(double a in values)foreach(double b in values)
        {
            Check(reference.TryGetValue(Tuple.Create(a,b),out int x)==actual.TryGetValue(new Pair(a,b),out int y) && x==y,"all overwritten/colliding entries retain identical values and lookup results");
            Check(new Pair(a,b).Equals((object)new Pair(a,b)) && !new Pair(a,b).Equals(null) && !new Pair(a,b).Equals(Tuple.Create(a,b)),"object equality is limited to this value-key type");
        }
        Check(new Pair(1,150).GetHashCode()==new Pair(From(0x3ff0000100000001),150).GetHashCode() && !new Pair(1,150).Equals(new Pair(From(0x3ff0000100000001),150)),"an intentional finite hash collision remains distinct");
        const int visits=16384;int sum=0;
        for(int i=0;i<4096;i++){actual.TryGetValue(new Pair(1,150),out int a);reference.TryGetValue(Tuple.Create(1.0,150.0),out int b);sum+=a+b;}
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++){actual.TryGetValue(new Pair(1,150),out int a);sum+=a;}long typed=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++){reference.TryGetValue(Tuple.Create(1.0,150.0),out int a);sum+=a;}long tuples=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(sum>0 && typed==0 && tuples>=visits*32,"warmed production-key lookups remove both tuple and element-boxing allocation");
        Console.WriteLine("RECEIVING_KEY_ALLOCATIONS visits="+visits+" oldTupleBytes="+tuples+" typedBytes="+typed);
    }
    static void CountKeyContract<T>() where T:struct,IEquatable<T>
    {
        var ctor=typeof(T).GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Geometry[]),typeof(int),typeof(int)},null);
        var p=Expression.Parameter(typeof(Geometry[]));var a=Expression.Parameter(typeof(int));var b=Expression.Parameter(typeof(int));
        var key=Expression.Lambda<Func<Geometry[],int,int,T>>(Expression.New(ctor,p,a,b),p,a,b).Compile();
        var parent=new[]{new Geometry {Positions=new double[3]}};var clone=(Geometry[])parent.Clone();
        var arrays=new[]{null,Array.Empty<Geometry>(),new Geometry[0],parent,parent,clone};
        var limits=new[]{int.MinValue,-1,0,1,8,int.MaxValue};
        var old=new Dictionary<Tuple<Geometry[],int,int>,int>();var actual=new Dictionary<T,int>();int ordinal=0;
        foreach(var source in arrays)foreach(int minimum in limits)foreach(int maximum in limits)
        {
            var tuple=Tuple.Create(source,minimum,maximum);var value=key(source,minimum,maximum);
            Check(tuple.GetHashCode()==value.GetHashCode(),"actual count key retains exact nested Tuple3 hash including null arrays and integer extremes");
            foreach(var other in arrays)Check(tuple.Equals(Tuple.Create(other,minimum,maximum))==value.Equals(key(other,minimum,maximum)),"equal-content arrays are distinct unless their parent references match");
            old[tuple]=ordinal;actual[value]=ordinal++;
        }
        foreach(var source in arrays)foreach(int minimum in limits)foreach(int maximum in limits)
            Check(old.TryGetValue(Tuple.Create(source,minimum,maximum),out int x)==actual.TryGetValue(key(source,minimum,maximum),out int y) && x==y,"count dictionary lookups preserve all tuple values");
        Check(old.Count==actual.Count,"count dictionary has the same identity classes");
    }
    // Frozen 4d6 implementation: same original LRU, query sequence and publication
    // order; only the old Tuple key is retained for this independent reference.
    sealed class ReferenceViews
    {
        readonly TransferredSlabOptimizer.ResponseCache<Tuple<Geometry[],int,int>,Geometry[]> cache=new TransferredSlabOptimizer.ResponseCache<Tuple<Geometry[],int,int>,Geometry[]>(128);
        internal Geometry[] Get(Geometry[] source,int minimum,int maximum,BeamSearchTrace trace,out bool hit)
        {
            trace?.CheckCancellation();var key=Tuple.Create(source,minimum,maximum);
            lock(cache){trace?.CheckCancellation();if(cache.TryGetValue(key,out var saved)){hit=true;return saved;}}
            var selected=source.Where(g=>g.Positions.Length>=minimum && g.Positions.Length<=maximum).ToArray();
            if(selected.Length==source.Length)selected=source;trace?.CheckCancellation();
            lock(cache){trace?.CheckCancellation();if(cache.TryGetValue(key,out var saved)){hit=true;return saved;}cache.Store(key,selected);hit=false;return selected;}
        }
    }
    static void ViewParity(Geometry[] source)
    {
        var reference=new ReferenceViews();var actual=new TransferredSlabOptimizer.GeometryCache();
        var clone=(Geometry[])source.Clone();var parents=new[]{source,source,clone,source.Concat(source.Take(2)).ToArray()};
        var random=new Random(48165);
        for(int i=0;i<420;i++)
        {
            var parent=parents[i%parents.Length];int minimum=i<140 ? i-50 : random.Next(-3,15);int maximum=i<140 ? i+1 : random.Next(-3,18);
            var expected=reference.Get(parent,minimum,maximum,null,out bool a);var value=actual.GetCountRange(parent,minimum,maximum,null,out bool b);
            Check(a==b && value.SequenceEqual(expected),"actual bounded view retains exact ordered object occurrences and original hit/LRU trajectory");
            Check(ReferenceEquals(value,parent)==ReferenceEquals(expected,parent),"full-range query keeps original parent-array publication fast path");
        }
        var x=reference.Get(source,2,40,null,out _);var y=actual.GetCountRange(source,2,40,null,out _);
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();var trace=new BeamSearchTrace("hot key cancelled lookup",cancel.Token);bool oldCancelled=false,newCancelled=false;
            try{reference.Get(source,2,40,trace,out _);}catch(OperationCanceledException){oldCancelled=true;}
            try{actual.GetCountRange(source,2,40,trace,out _);}catch(OperationCanceledException){newCancelled=true;}
            Check(oldCancelled && newCancelled,"cancellation still occurs before a completed cache hit");
        }
        Check(reference.Get(source,2,40,null,out bool oldHit).SequenceEqual(actual.GetCountRange(source,2,40,null,out bool newHit)) && oldHit && newHit,"cancelled access leaves completed results untouched");
        int ticks=0;var oldTrace=new BeamSearchTrace("old interrupted publication",default,TimeSpan.FromSeconds(1),()=>++ticks<4 ? 0 : 2*Stopwatch.Frequency);
        bool oldExpired=false,newExpired=false;
        try{reference.Get(source,-99,99,oldTrace,out _);}catch(BeamSearchDeadlineException){oldExpired=true;}
        ticks=0;var newTrace=new BeamSearchTrace("new interrupted publication",default,TimeSpan.FromSeconds(1),()=>++ticks<4 ? 0 : 2*Stopwatch.Frequency);
        try{actual.GetCountRange(source,-99,99,newTrace,out _);}catch(BeamSearchDeadlineException){newExpired=true;}
        Check(oldExpired && newExpired,"the same deadline checkpoint interrupts completed-view publication");
        reference.Get(source,-99,99,null,out oldHit);actual.GetCountRange(source,-99,99,null,out newHit);Check(!oldHit && !newHit,"interruption publishes neither old nor new key");
        const int visits=4096;for(int i=0;i<1024;i++){reference.Get(source,2,40,null,out _);actual.GetCountRange(source,2,40,null,out _);}
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++)actual.GetCountRange(source,2,40,null,out _);long typed=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++)reference.Get(source,2,40,null,out _);long tuples=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(tuples-typed>=visits*24,"actual warmed count-view hits remove tuple and boxed-integer equality allocations while retaining the old filtering closure");
        Console.WriteLine("COUNT_VIEW_KEY_ALLOCATIONS visits="+visits+" oldTupleBytes="+tuples+" typedBytes="+typed);
    }
    static void Malformed()
    {
        foreach(int mode in new[]{0,1,2})
        {
            var reference=new ReferenceViews();var actual=new TransferredSlabOptimizer.GeometryCache();
            var parent=mode==0 ? null : new[]{mode==1 ? null : new Geometry {Positions=null}};Exception old=null,value=null;
            try{reference.Get(parent,2,8,null,out _);}catch(Exception e){old=e;}
            try{actual.GetCountRange(parent,2,8,null,out _);}catch(Exception e){value=e;}
            Check(old?.GetType()==value?.GetType() && old!=null,"malformed parent/geometry/positions retain the original exception type");
            if(parent!=null){parent[0]=new Geometry {Positions=new double[4]};reference.Get(parent,2,8,null,out bool a);actual.GetCountRange(parent,2,8,null,out bool b);Check(!a && !b,"failed malformed builders publish neither cache entry");}
        }
    }
    static void NativeSources()
    {
        for(int scenario=1;scenario<=3;scenario++)
        {
            var common=ReportedScenarioTests.DiagnosticPart(scenario,1).Item2.CreateTowerInput();
            for(int beam=0;beam<common.Network.Beams.Length;beam++)
            {
                var local=common.CopyForSearch();local.LengthMm=common.Network.Beams[beam].SpanLength(common.LengthMm);local.LongitudinalStartMm=common.Network.Beams[beam].SpanStartMm;
                var cover=TransferredSlabOptimizer.StockCovers(local).First();
                var source=EngineerTowerSearch.Geometries(cover,false,local,0,Math.Max(8,2*cover.Length)).Take(32).ToArray();
                Check(source.Length>0,"reported native cover provides a bounded original geometry sample");nativeEntries+=source.Length;ViewParity(source);
            }
        }
    }
    internal static void Run()
    {
        PairContract();typeof(ReceivingHotKeyTests).GetMethod(nameof(CountKeyContract),BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(typeof(TransferredSlabOptimizer).GetNestedType("CountRangeKey",BindingFlags.NonPublic)).Invoke(null,null);
        var preparation=typeof(TransferredSlabOptimizer).GetNestedType("ReceivingPreparation",BindingFlags.NonPublic);
        foreach(string name in new[]{"Crosses","Rows"})Check(preparation.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).FieldType.GetGenericArguments()[0]==typeof(Pair),"actual prepared "+name+" owner uses the tested value key");
        ViewParity(new[]{6,2,8,4,7,3,6,5}.Select(n=>new Geometry {Positions=new double[n]}).ToArray());Malformed();NativeSources();
        Console.WriteLine("PASS "+checks+" receiving hot-key checks: exact old Tuple IEEE/hash/collision behavior; actual old/new bounded count views, cancellation, malformed/publication parity; "+nativeEntries+" native entries; warm lookup allocations");
    }
}
