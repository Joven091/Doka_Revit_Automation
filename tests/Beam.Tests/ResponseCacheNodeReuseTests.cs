using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EngineeringDashboard;

internal static class ResponseCacheNodeReuseTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Response cache nodes: "+message);}
    // Independent behavioural oracle: a small ordered list, not linked nodes.
    sealed class OrderedReference
    {
        readonly int capacity;readonly List<KeyValuePair<int,int>> values=new List<KeyValuePair<int,int>>();
        internal OrderedReference(int capacity){this.capacity=capacity;}
        internal int Count=>values.Count;
        internal bool Get(int key,out int value)
        {
            int at=values.FindIndex(v=>v.Key==key);if(at<0){value=0;return false;}
            var found=values[at];values.RemoveAt(at);values.Add(found);value=found.Value;return true;
        }
        internal void Put(int key,int value)
        {
            int at=values.FindIndex(v=>v.Key==key);if(at>=0)values.RemoveAt(at);else if(values.Count>=capacity)values.RemoveAt(0);
            values.Add(new KeyValuePair<int,int>(key,value));
        }
    }
    sealed class Key : IEquatable<Key>
    {
        internal int Id;internal readonly string Label;
        internal Key(int id,string label){Id=id;Label=label;}
        public bool Equals(Key other)=>other!=null && Id==other.Id;
        public override bool Equals(object other)=>Equals(other as Key);
        public override int GetHashCode()=>Id;
    }
    sealed class Payload {internal int Value;internal Payload(int value){Value=value;}}
    static void OrderedBehaviour()
    {
        foreach(int capacity in new[]{1,2,3,17,128})
        {
            var actual=new TransferredSlabOptimizer.ResponseCache<int,int>(capacity);var expected=new OrderedReference(capacity);var random=new Random(1358+capacity);
            for(int i=0;i<20000;i++)
            {
                int key=random.Next(capacity*4+3);
                if(random.Next(3)!=0){int value=random.Next();actual.Store(key,value);expected.Put(key,value);}
                else
                {
                    bool a=actual.TryGetValue(key,out int av),b=expected.Get(key,out int bv);
                    Check(a==b && av==bv,"mixed promotions/replacements/evictions return the independent ordered oracle's exact outputs");
                }
                Check(actual.Count==expected.Count && actual.Count<=capacity,"retention remains bounded throughout repeated reuse");
                if(i%101==0)for(int repeated=0;repeated<5;repeated++)
                {bool a=actual.TryGetValue(key,out int av),b=expected.Get(key,out int bv);Check(a==b && av==bv,"already-newest hits preserve later eviction behaviour");}
            }
        }
        var lru=new TransferredSlabOptimizer.ResponseCache<int,int>(2);lru.Store(1,10);lru.Store(2,20);lru.TryGetValue(1,out _);lru.Store(3,30);
        Check(!lru.TryGetValue(2,out _) && lru.TryGetValue(1,out int first) && first==10,"a promoted old entry survives the next eviction");
        lru.Store(1,11);lru.Store(4,40);Check(!lru.TryGetValue(3,out _) && lru.TryGetValue(1,out first) && first==11,"existing-key replacement remains newest");
    }
    static void AliasesAndOutputs()
    {
        var cache=new TransferredSlabOptimizer.ResponseCache<Key,Payload>(2);var original=new Key(1,"original");var replacement=new Key(1,"replacement");
        var first=new Payload(10);var second=new Payload(20);cache.Store(original,first);cache.Store(new Key(2,"other"),new Payload(2));
        Check(cache.TryGetValue(original,out var retained) && ReferenceEquals(retained,first),"readers receive the original payload reference");
        cache.Store(replacement,second);original.Id=99;
        Check(cache.TryGetValue(new Key(1,"query"),out var current) && ReferenceEquals(current,second),"equal-key Store replaces the DICTIONARY key alias, rather than retaining the removed mutable original");
        retained.Value=999;Check(first.Value==999 && current.Value==20,"recycled nodes do not mutate previously returned payload objects");
        cache.Store(new Key(3,"new"),new Payload(3));Check(!cache.TryGetValue(new Key(2,"evicted"),out _),"replacement/promoted node retains the same LRU position");
        current=second;Check(!cache.TryGetValue(new Key(-1,"missing"),out current) && current==null,"failed lookups overwrite an old out value with default");
        foreach(int invalid in new[]{0,-1})
        {
            var unsupported=new TransferredSlabOptimizer.ResponseCache<int,int>(invalid);bool threw=false;
            try{unsupported.Store(1,2);}catch(NullReferenceException){threw=true;}
            Check(threw && unsupported.Count==0 && !unsupported.TryGetValue(1,out int value) && value==0,"unsupported nonpositive capacities retain original first-Store exception and empty state");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static WeakReference[] EvictOwnedObjects(TransferredSlabOptimizer.ResponseCache<Key,Payload> cache)
    {
        var key=new Key(1,"evicted");var value=new Payload(1);var result=new[]{new WeakReference(key),new WeakReference(value)};
        cache.Store(key,value);cache.Store(new Key(2,"two"),new Payload(2));cache.Store(new Key(3,"three"),new Payload(3));return result;
    }
    static void RetentionAndAllocation()
    {
        var references=new TransferredSlabOptimizer.ResponseCache<Key,Payload>(2);var evicted=EvictOwnedObjects(references);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        Check(evicted.All(w=>!w.IsAlive),"recycled nodes release old keys and values after eviction");GC.KeepAlive(references);
        var cache=new TransferredSlabOptimizer.ResponseCache<int,int>(32);
        for(int i=0;i<256;i++){cache.Store(i,i);cache.TryGetValue(i,out _);}
        long before=GC.GetAllocatedBytesForCurrentThread();long checksum=0;
        for(int i=0;i<20000;i++)
        {
            int key=1000+i;cache.Store(key,i);cache.Store(key,i+1);
            if(cache.TryGetValue(key,out int value))checksum+=value;
            cache.TryGetValue(key,out _);
        }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(bytes==0,"a warmed bounded cache allocates no linked node for full-capacity misses, replacements or repeated newest hits");
        Check(checksum==20000L*20001/2 && cache.Count==32,"allocation check consumes all returned values and preserves final bounded outputs");
    }
    internal static void Run()
    {
        OrderedBehaviour();AliasesAndOutputs();RetentionAndAllocation();
        Console.WriteLine("PASS "+checks+" response-cache node checks: ordered independent oracle, dictionary alias replacement, exact payload ownership, original unsupported-capacity failure and allocation-free bounded churn");
    }
}
