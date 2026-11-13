using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class GeometryCountViewTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Geometry count view: "+message);}
    static TowerInput Input()
    {
        var input=ReportedCombinedTests.Input(1);
        input.EngineerSearch=true;input.EngineerStationLimit=8;
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,input.Network,0);input.EngineerPlan.InitialLegs=7;
        return input;
    }
    static TransferredSlabOptimizer.Geometry[] Expected(TransferredSlabOptimizer.Geometry[] source,int minimum,int maximum)
        =>source.Where(g=>g.Positions.Length>=minimum && g.Positions.Length<=maximum).ToArray();
    static void Views(TowerInput input,TransferredSlabOptimizer.GeometryCache cache,TransferredSlabOptimizer.Geometry[] source)
    {
        foreach(int minimum in new[] {1,4,7,6,9,2,7})
        foreach(int ceiling in new[] {int.MinValue,-3,-1,0,7,8,11,12,15,16,99})
        {
            int maximum=Math.Min(input.EngineerStationLimit,ceiling/2);
            var expected=Expected(source,minimum,maximum);
            var actual=TransferredSlabOptimizer.CountRange(input,cache,source,minimum,maximum);
            Check(actual.SequenceEqual(expected),"whole-count filtering retains every original geometry object in source order, including odd ceilings");
            Check(ReferenceEquals(actual,TransferredSlabOptimizer.CountRange(input,cache,source,minimum,maximum)),"a completed exact range reuses its array");
        }
    }
    static void ActualContexts()
    {
        // Use the saved reported settings and every local beam's real span;
        // these calls generate geometry only, never run a network search.
        for(int scenario=1;scenario<=3;scenario++)
        {
            var common=ReportedScenarioTests.DiagnosticPart(scenario,1).Item2.CreateTowerInput();
            for(int beam=0;beam<common.Network.Beams.Length;beam++)
            {
                var input=common.CopyForSearch();input.LengthMm=common.Network.Beams[beam].SpanLength(common.LengthMm);
                input.LongitudinalStartMm=common.Network.Beams[beam].SpanStartMm;
                input.EngineerPlan=EngineerPrimaryPlan.Create(input,common.Network,beam);input.EngineerPlan.InitialLegs=8;
                input.EngineerStationLimit=8;input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
                var covers=TransferredSlabOptimizer.StockCovers(input);
                Check(covers.Length>0,"reported local span has its original permitted cover family");
                foreach(var cover in covers.Take(1))foreach(bool rotated in new[] {false,true})
                {
                    var cache=new TransferredSlabOptimizer.GeometryCache();
                    var source=TransferredSlabOptimizer.SearchGeometries(input,cache,cover,rotated);
                    Views(input,cache,source);
                    Check(ReferenceEquals(source,TransferredSlabOptimizer.SearchGeometries(input,cache,cover,rotated)),"bounded views leave the unrestricted completed universe intact");
                }
            }
        }
    }
    static void FullSearchParity()
    {
        const BindingFlags methods=BindingFlags.Static|BindingFlags.NonPublic;
        var generated=typeof(ExhaustedStationReuseTests).GetMethod("Generated",methods);
        var core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",methods);
        var fingerprint=typeof(ExhaustedStationReuseTests).GetMethod("Fingerprint",methods);
        foreach(double width in new[] {1524.0,1000.0})
        {
            var stocks=width==1524 ? new[] {3900.0} : new[] {3900.0,3900};
            Func<TowerInput> make=()=>
            {
                var value=(TowerInput)generated.Invoke(null,new object[] {stocks,width});
                value.EngineerStationLimit=6;value.EngineerMinimumStations=4;value.NetworkLegCeiling=13;return value;
            };
            var actual=make();var reference=make();
            // A fixed complete universe bypasses only the new filtered view,
            // while preserving the actual geometry and full engineering checks.
            reference.TransferredSlab.SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache
                {Fixed=TransferredSlabOptimizer.SearchGeometries(reference,null,stocks,width!=1524).Where(g=>g.Positions.Length<=6).ToArray()};
            var actualCallbacks=new List<string>();var referenceCallbacks=new List<string>();
            actual.TransferLayoutAcceptance=layout=>{actualCallbacks.Add((string)fingerprint.Invoke(null,new object[] {layout}));return layout.PositionsMm.Length>=5;};
            reference.TransferLayoutAcceptance=layout=>{referenceCallbacks.Add((string)fingerprint.Invoke(null,new object[] {layout}));return layout.PositionsMm.Length>=5;};
            var selected=(TowerLayout)core.Invoke(null,new object[] {actual,true,null});
            var expected=(TowerLayout)core.Invoke(null,new object[] {reference,true,null});
            Check((string)fingerprint.Invoke(null,new object[] {selected})==(string)fingerprint.Invoke(null,new object[] {expected}),"complete filtered search preserves selected stock, geometry, member forces and all analysis results");
            Check(actualCallbacks.Count>0 && actualCallbacks.SequenceEqual(referenceCallbacks),"retained complete-layout callbacks keep their original order");
        }
    }
    internal static void Run()
    {
        var input=Input();var cache=new TransferredSlabOptimizer.GeometryCache();
        var source=new[] {6,2,8,4,7,3,6,5}.Select(n=>new TransferredSlabOptimizer.Geometry {Positions=new double[n]}).ToArray();
        Views(input,cache,source);
        var all=cache.GetCountRange(source,1,99,null,out _);
        Check(ReferenceEquals(all,source),"a complete range retains the original parent array");
        var first=cache.GetCountRange(source,4,7,null,out _);
        var extended=source.Concat(new[] {new TransferredSlabOptimizer.Geometry {Positions=new double[5]}}).ToArray();
        var next=cache.GetCountRange(extended,4,7,null,out bool extensionHit);
        Check(!extensionHit && next.Length==first.Length+1 && ReferenceEquals(next.Last(),extended.Last()),"a completed extension's new parent identity invalidates earlier views");
        var cloned=(TransferredSlabOptimizer.Geometry[])source.Clone();
        cache.GetCountRange(cloned,4,7,null,out bool cloneHit);
        Check(!cloneHit,"equal elements in another parent array do not alias cache identity");
        int builds=0;var transactional=new TransferredSlabOptimizer.GeometryCache();
        var parent=transactional.GetEngineer("parent",4,_=>source.Where(g=>g.Positions.Length<=4).ToArray(),out _,out _);
        var old=transactional.GetCountRange(parent,3,8,null,out _);
        var full=transactional.GetEngineer("parent",8,_=>{builds++;return source;},out _,out _);
        Check(transactional.GetCountRange(full,3,8,null,out _).SequenceEqual(Expected(full,3,8)) && builds==1 && old.SequenceEqual(Expected(parent,3,8)),"real incremental parent publication preserves old and new complete views");
        foreach(int gate in Enumerable.Range(0,5))
        {
            var guarded=Input();var guardedCache=new TransferredSlabOptimizer.GeometryCache();
            if(gate==0)guarded.EngineerSearch=false;
            if(gate==1)guarded.EngineerPlan=null;
            if(gate==2)guardedCache=null;
            if(gate==3)guardedCache.Fixed=source;
            if(gate==4)guarded.Trace=new BeamSearchTrace("timed original geometry traversal",default,TimeSpan.FromSeconds(1));
            Check(ReferenceEquals(source,TransferredSlabOptimizer.CountRange(guarded,guardedCache,source,9,0)),"disabled, planless, cacheless, fixed and timed searches preserve their original arrays");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            cache.GetCountRange(source,4,7,null,out _);cancellation.Cancel();
            try{cache.GetCountRange(source,4,7,new BeamSearchTrace("cancelled count view",cancellation.Token),out _);throw new Exception("Expected cancellation");}
            catch(OperationCanceledException){}
            Check(cache.GetCountRange(source,4,7,null,out bool savedHit).SequenceEqual(first) && savedHit,"cancellation before a hit preserves the completed view");
        }
        int ticks=0;var interrupted=new TransferredSlabOptimizer.GeometryCache();
        var trace=new BeamSearchTrace("interrupted view publication",default,TimeSpan.FromSeconds(1),()=>++ticks<4 ? 0 : 2*Stopwatch.Frequency);
        try{interrupted.GetCountRange(source,4,7,trace,out _);throw new Exception("Expected deadline");}catch(BeamSearchDeadlineException){}
        interrupted.GetCountRange(source,4,7,null,out bool interruptedHit);
        Check(!interruptedHit,"an interrupted view is never published");
        var bounded=new TransferredSlabOptimizer.GeometryCache();bounded.GetCountRange(source,4,7,null,out _);
        for(int i=0;i<128;i++)bounded.GetCountRange(source,i+100,i+101,null,out _);
        bounded.GetCountRange(source,4,7,null,out bool evictedHit);Check(!evictedHit,"the range history is bounded");
        var parallel=new TransferredSlabOptimizer.GeometryCache();
        Parallel.For(0,32,i=>{var actual=parallel.GetCountRange(source,4,7,null,out _);if(!actual.SequenceEqual(first))throw new Exception("Concurrent count view mismatch");});
        Check(parallel.GetCountRange(source,4,7,null,out bool parallelHit).SequenceEqual(first) && parallelHit,"concurrent publication retains a completed ordered view");
        foreach(bool rotated in new[] {false,true})foreach(bool exclude in new[] {false,true})
        foreach(double setback in new[] {PortableTestRuntime.NextDown(150.0),150.0,PortableTestRuntime.NextUp(150.0)})
        foreach(var cover in new[] {new[] {2450.0},new[] {1800.0,2450},new[] {2450.0,2450,2450}})
        {
            input=Input();input.Exclude600Standalone=exclude;input.StockEndSetbackMm=setback;
            cache=new TransferredSlabOptimizer.GeometryCache();
            source=TransferredSlabOptimizer.SearchGeometries(input,cache,cover,rotated);Views(input,cache,source);
        }
        ActualContexts();FullSearchParity();
        Console.WriteLine("PASS "+checks+" geometry count-view checks: exact survivor order/identity, original settings and saved contexts, bounded/concurrent caches, original-path gates and interruption safety");
    }
}
