using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class ExactAdaptiveReceiverOutcomeTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Exact adaptive receiver outcomes: "+message);}
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static ReceiverSolveKey Key(int identifier=0)
    {
        var common=new TowerInput {LengthMm=2450,EngineerSearch=true,StockLengthsMm=new[] {2450.0},LegCapacityKn=70+identifier,
            TransferredSlab=new TransferredSlabInput {BeamWidthMm=400,BeamLineLoadKnM=5,UpperSpacingMm=305,StockCovers=new[] {new[] {2450.0}}}};
        return new ReceiverSolveKey(common,new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=400,DepthMm=800}},Bays=Array.Empty<TransferBayRow>(),CrossingBeams=Array.Empty<TransferCrossingBeamRow>()},0,false,shareLegCeilings:true);
    }
    static NetworkReceiverCache.Entry Entry(int ceiling,TowerLayout best,bool wider=false,bool minimum=false)=>new NetworkReceiverCache.Entry {
        LegCeiling=ceiling,Best=best,WiderCeilingStable=wider,TighterFailureMinimumProven=minimum,
        Diagnostics=new TowerSearchDiagnostics(270),Alternatives=new Dictionary<double,TowerLayout>()};
    static TowerLayout Winner(int legs)=>new TowerLayout {PositionsMm=new double[legs/2]};
    static void OriginalCounterexample()
    {
        var method=typeof(AdaptiveReceiverProofTests).GetMethod("Adaptive",hidden);
        TowerLayout Actual(int ceiling)
        {
            try{return (TowerLayout)method.Invoke(null,new object[] {ceiling});}
            catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        var broad=Actual(12);var tight=Actual(8);
        Check(broad.LegCount==12 && broad.Transfer.EngineerReceiverCount==3 && tight.LegCount==8 && tight.Transfer.EngineerReceiverCount==4,"actual original solver retains its broad/tight adaptive difference");
        var key=Key();var cache=new NetworkReceiverCache();cache.Store(key,Entry(12,broad,true));
        Check(!cache.Get(key,8,out _),"the broad result alone cannot erase a tighter count-four search");
        cache.Store(key,Entry(8,tight));
        foreach(int iteration in Enumerable.Range(0,4))
        {
            Check(cache.Get(key,8,out var cached) && ReferenceEquals(cached.Best,tight),"the exact completed tighter outcome survives the wider positive record");
            Check(cache.Get(key,12,out cached) && ReferenceEquals(cached.Best,broad),"revisiting the broad budget retains its original count-three outcome");
        }
        Check(!cache.Get(key,10,out _),"an unseen intermediate budget still runs its original adaptive search");
        Check(cache.Get(key,14,out var widened) && ReferenceEquals(widened.Best,broad),"only the count-three wider-stability proof permits a broader reuse");
        Check(!cache.Get(Key(1),8,out _),"different exact physics cannot consume another budget's outcome");
    }
    static void FailureFloorAndEviction()
    {
        var cache=new NetworkReceiverCache();var original=Key();var broad=Winner(12);var tight=Winner(8);
        cache.Store(original,Entry(12,broad,true));cache.Store(original,Entry(8,tight,minimum:true));
        Check(cache.Get(original,6,out var floor) && floor.Best==null && floor.MinimumRequiredLegs==8,"a discarded tighter last-count winner preserves its below-minimum failure proof");
        Check(cache.Get(original,8,out var selected) && ReferenceEquals(selected.Best,tight),"the tight exact positive remains independently available");
        Check(cache.Get(original,14,out selected) && ReferenceEquals(selected.Best,broad),"the smaller last-count result never replaces the broad count-three result");
        for(int index=1;index<=1200;index++)
        {
            var key=Key(index);cache.Store(key,Entry(12,Winner(12),true));cache.Store(key,Entry(8,Winner(8)));
        }
        Check(!cache.Get(original,8,out _),"evicted exact positives fall back to an ordinary search rather than use a different adaptive outcome");
        Check(cache.Get(original,6,out floor) && floor.Best==null && floor.MinimumRequiredLegs==8,"small negative minimum evidence survives positive-graph eviction");
        Check(cache.Get(original,14,out selected) && ReferenceEquals(selected.Best,broad),"the widest stable success remains available after exact-graph eviction");
        var inner=typeof(NetworkReceiverCache).GetField("exactSuccesses",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cache);
        int count=(int)inner.GetType().GetProperty("Count",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(inner);
        Check(count<=1024,"additional exact positive response graphs are bounded globally at1024");
        var latest=Key(1200);Check(cache.Get(latest,8,out selected) && selected.Best.LegCount==8,"recent exact outcomes survive bounded eviction");
    }
    static void FailureCoexistence()
    {
        var key=Key();var cache=new NetworkReceiverCache();var broad=Winner(12);var tight=Winner(8);
        cache.Store(key,Entry(12,broad));cache.Store(key,Entry(8,tight));
        cache.Store(key,new NetworkReceiverCache.Entry {LegCeiling=6,CompleteFailure=true,Diagnostics=new TowerSearchDiagnostics(270)});
        Check(cache.Get(key,6,out var failure) && failure.Best==null,"separately completed smaller failures coexist with both successes");
        Check(cache.Get(key,8,out var success) && ReferenceEquals(success.Best,tight),"a smaller exhausted failure does not replace an exact positive");
        Check(cache.Get(key,12,out success) && ReferenceEquals(success.Best,broad),"the broader exact outcome is preserved independently");
        Check(!cache.Get(key,14,out _),"neither exact record fabricates a wider proof without count-three provenance");
        var cancelled=Entry(10,null);cancelled.CompleteFailure=false;cache.Store(key,cancelled);
        Check(!cache.Get(key,10,out _),"an incomplete null cannot be admitted as an exact completed outcome");
    }
    internal static void Run()
    {
        OriginalCounterexample();FailureFloorAndEviction();FailureCoexistence();
        Console.WriteLine("PASS "+checks+" exact adaptive outcome checks: actual12-vs-8 solver parity, unseen budgets,1024 global graph bound, independent minimum-floor retention and fault guards");
    }
}
