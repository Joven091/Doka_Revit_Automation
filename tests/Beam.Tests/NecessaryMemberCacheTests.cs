using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;
internal static class NecessaryMemberCacheTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Necessary member cache: "+message);}
    static CrossBeamSupportOptimizer.NecessaryMemberKey Key(double start=0,double length=2450,int line=0,double[] supports=null)
        =>new CrossBeamSupportOptimizer.NecessaryMemberKey(start,length,line,supports??new[]{200.0,2250});
    internal static void Run()
    {
        var cache=new TransferredSlabOptimizer.ResponseCache<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>(2);
        cache.Store(Key(),false);
        Check(cache.TryGetValue(Key(supports:new[]{200.0,2250}),out bool failed) && !failed,"equal local members from distinct complete geometries reuse the same necessary rejection");
        foreach(var changed in new[] {Key(start:Math.BitIncrement(0.0)),Key(length:Math.BitIncrement(2450)),Key(line:1),Key(supports:new[]{Math.BitIncrement(200),2250.0}),Key(supports:new[]{2250.0,200}),Key(supports:new[]{200.0,1225,2250})})
            Check(!cache.TryGetValue(changed,out _),"absolute load start, length, line, adjacent support values, order and count stay exact");
        cache.Store(Key(line:1),true);cache.TryGetValue(Key(),out _);cache.Store(Key(start:1),false);
        Check(cache.Count==2 && !cache.TryGetValue(Key(line:1),out _) && cache.TryGetValue(Key(),out failed) && !failed,"member responses retain bounded LRU eviction and both positive/negative outcomes");
        var ownerA=new TransferredSlabOptimizer.ResponseCache<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>(2);
        var ownerB=new TransferredSlabOptimizer.ResponseCache<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>(2);
        ownerA.Store(Key(),true);ownerB.Store(Key(),false);
        Check(ownerA.TryGetValue(Key(),out bool a) && a && ownerB.TryGetValue(Key(),out bool b) && !b,"different prepared load/material owners never share necessary decisions");
        var weak=typeof(ResolvedSpanFrameBoundTests).GetMethod("Geometry",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(double force in new[]{-60.0,-20,0,20,40,60})
        {
            var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=70,DeflectionDenominator=270,IgnoreUplift=true,Trace=new BeamSearchTrace("necessary local cache"),Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true),TransferredSlab=new TransferredSlabInput()};
            var prepare=typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",BindingFlags.Static|BindingFlags.NonPublic);
            var reference=input.CopyForSearch();reference.ReuseSearchWork=false;
            var check=(Func<TransferredSlabOptimizer.Geometry,double,bool>)prepare.Invoke(null,new object[]{input,force});
            var unchanged=(Func<TransferredSlabOptimizer.Geometry,double,bool>)prepare.Invoke(null,new object[]{reference,force});
            PreparedReceiverSupportBounds.Minimum(check,new[]{2450.0},0);PreparedReceiverSupportBounds.Minimum(unchanged,new[]{2450.0},0);
            bool? prior=null;
            for(int i=0;i<5;i++)
            {
                var g=(TransferredSlabOptimizer.Geometry)weak.Invoke(null,new object[]{new[]{200.0,1225,2250},2450.0});
                bool necessary=PreparedReceiverSupportBounds.MayFit(check,g,0);
                Check(necessary==PreparedReceiverSupportBounds.MayFit(unchanged,g,0) && check(g,0)==unchanged(g,0),"cache on/off preserves both early necessity and complete ordinary callback results");
                Check(!prior.HasValue || prior.Value==necessary,"equal local support shapes preserve resolved signed necessity across distinct full geometry objects");
                Check(necessary || !check(g,0),"a cached early failure remains rejected by its unchanged ordinary callback");prior=necessary;
            }
        }
        var cover=typeof(ResolvedSpanFrameBoundTests).GetMethod("Cover",BindingFlags.NonPublic|BindingFlags.Static);
        var solve=typeof(ResolvedSpanFrameBoundTests).GetMethod("Solve",BindingFlags.NonPublic|BindingFlags.Static);
        var identity=typeof(ResolvedSpanFrameBoundTests).GetMethod("Identity",BindingFlags.NonPublic|BindingFlags.Static);
        var cached=(TowerInput)cover.Invoke(null,new object[]{true,false});var uncached=(TowerInput)cover.Invoke(null,new object[]{true,false});uncached.ReuseSearchWork=false;
        Check((string)identity.Invoke(null,new[]{solve.Invoke(null,new object[]{cached})})==(string)identity.Invoke(null,new[]{solve.Invoke(null,new object[]{uncached})}),"complete single-stock receiving solve retains the cache-disabled result identity");
        Console.WriteLine("PASS "+checks+" necessary member cache checks: exact load-window/line/ordered supports, bounded LRU, owner isolation and signed full-callback parity");
    }
}
