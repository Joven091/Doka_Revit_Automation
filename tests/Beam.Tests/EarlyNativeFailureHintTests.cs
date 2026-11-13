using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;
using Geometry=EngineeringDashboard.TransferredSlabOptimizer.Geometry;
using Sparse=EngineeringDashboard.CrossBeamSupportOptimizer.PrimarySparseLoads;

internal static class EarlyNativeFailureHintTests
{
    static int checks;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly MethodInfo prepare=typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",hidden);
    static readonly MethodInfo diagnostics=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",hidden);
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Early native failure hint: "+text);}
    static TowerInput Input(double capacity=70,CancellationToken token=default)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=capacity,DeflectionDenominator=270,IgnoreUplift=true,Trace=new BeamSearchTrace("early native hint",token),Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true),TransferredSlab=new TransferredSlabInput()};
    static Func<Geometry,double,bool> Prepare(TowerInput input,double force)
    {
        var result=(Func<Geometry,double,bool>)prepare.Invoke(null,new object[]{input,force});PreparedReceiverSupportBounds.Minimum(result,new[]{2450.0},0);return result;
    }
    static Geometry G(double[] positions,bool native=true,double length=2450)
    {
        var stocks=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=length,FirstSupport=0,LastSupport=positions.Length-1}};var joins=Enumerable.Repeat(true,positions.Length-1).ToArray();
        return native ? Geometry.Generated(stocks,positions,joins,null) : new Geometry {Stock=stocks,Positions=positions,Joined=joins,MemberSupports=new[]{positions.ToArray()}};
    }
    static Sparse Integral(Func<Geometry,double,bool> callback,int line)
    {
        var owner=PreparedReceiverSupportBounds.Predicate(callback).Target;var table=(IDictionary)owner.GetType().GetField("loads",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(owner);
        foreach(DictionaryEntry item in table)
        {
            if((int)item.Key.GetType().GetField("Line",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(item.Key)!=line)continue;
            return (Sparse)item.Value.GetType().GetField("Integral",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(item.Value);
        }
        throw new Exception("No exact current load owner");
    }
    static int Hint(int primitive)=>CrossBeamSupportOptimizer.EncodeNecessaryFailureHint(0,0,primitive);
    static void OrderingAndCurrentProof()
    {
        var input=Input();var rawInput=Input();var fast=Prepare(input,120);var old=Prepare(rawInput,120);
        Check(PreparedReceiverSupportBounds.Minimum(fast,new[]{2450.0},0)>2,"real force floor requires more than two supports");
        var first=G(new[]{1800.0,2250});first.NecessaryFailureHint=Hint(2);
        Check(!PreparedReceiverSupportBounds.MayFit(fast,first,0) && input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==1 && input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==0,"before the first explanation, a populated foreign hint still follows original count priority");
        Check(!PreparedReceiverSupportBounds.MayFit(old,G(new[]{1800.0,2250},false),0),"raw baseline rejects the same first candidate");
        var prime=G(new[]{1800.0,2000,2300});var rawPrime=G(new[]{1800.0,2000,2300},false);
        Check(!PreparedReceiverSupportBounds.MayFit(fast,prime,0) && !PreparedReceiverSupportBounds.MayFit(old,rawPrime,0),"a count-admissible real geometry records the original first span/tail explanation");
        diagnostics.Invoke(null,new object[]{rawInput.Diagnostics,input.Diagnostics});
        var both=G(new[]{1800.0,2250});both.NecessaryFailureHint=prime.NecessaryFailureHint;var sparse=Integral(fast,0);
        int primitive=(both.NecessaryFailureHint>>8)&511,segment=(both.NecessaryFailureHint>>17)-1;
        Check(sparse!=null && sparse.TryFitNativeFailureDescriptor(input,both.Supports(0),primitive,segment,out bool fits,out _) && !fits,"the early branch has an independently recomputed CURRENT numeric failure, not merely an old hint");
        long count=input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected),span=input.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected),hint=input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected);
        Check(!PreparedReceiverSupportBounds.MayFit(fast,both,0) && !PreparedReceiverSupportBounds.MayFit(old,G(new[]{1800.0,2250},false),0),"simultaneous count+span failure preserves exact rejection truth");
        Check(input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==count && input.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected)==span+1 && input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==hint+1,"already-explained fresh span proof intentionally takes category3 before redundant category2 traversal");
        diagnostics.Invoke(null,new object[]{rawInput.Diagnostics,input.Diagnostics});
        Check(!fast(both,0) && !old(G(new[]{1800.0,2250},false),0),"both complete original callbacks retain the same rejection");
        diagnostics.Invoke(null,new object[]{rawInput.Diagnostics,input.Diagnostics});
        foreach(int where in new[]{Hint(2),Hint(313),7<<8,0})
        {
            var retained=G(new[]{200.0,2250});retained.NecessaryFailureHint=where;count=input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected);hint=input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected);
            Check(!PreparedReceiverSupportBounds.MayFit(fast,retained,0) && input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==count+1 && input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==hint,"passing, unknown, malformed or absent hints keep the original count rejection");
        }
        var mismatch=G(new[]{1800.0,2250});mismatch.Stock[0].StartMm=25;mismatch.NecessaryFailureHint=prime.NecessaryFailureHint;
        hint=input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected);Check(PreparedReceiverSupportBounds.MayFit(fast,mismatch,0) && input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==hint,"a missing/mismatching current binding is unknown, never an early old-load proof");
        var materialized=G(new[]{1800.0,2250});materialized.NecessaryFailureHint=prime.NecessaryFailureHint;materialized.MaterializeSupports(null);count=input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected);
        Check(!PreparedReceiverSupportBounds.MayFit(fast,materialized,0) && input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==count+1,"materialized/native-off data retains original count-first behavior");
        foreach(var positions in new[]{new[]{double.NaN,2250},new[]{2600.0,2700},new[]{2250.0,1800}})
        {
            var unsupported=G(positions);unsupported.NecessaryFailureHint=prime.NecessaryFailureHint;count=input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected);hint=input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected);
            Check(!PreparedReceiverSupportBounds.MayFit(fast,unsupported,0) && input.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==count+1 && input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==hint,"raw factory fallback or invalid selected interval keeps original count rejection");
        }
    }
    static void ChangedSourceAndFallbacks()
    {
        var input=Input();var fast=Prepare(input,20);var rawInput=Input();var old=Prepare(rawInput,20);
        Check(!PreparedReceiverSupportBounds.MayFit(fast,G(new[]{1800.0,2250}),0) && !PreparedReceiverSupportBounds.MayFit(old,G(new[]{1800.0,2250},false),0),"both current owners establish their original explanation");
        var passing=G(new[]{200.0,2250});passing.NecessaryFailureHint=Hint(2);
        Check(PreparedReceiverSupportBounds.MayFit(fast,passing,0)==PreparedReceiverSupportBounds.MayFit(old,G(new[]{200.0,2250},false),0) && input.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==0,"an old force120 tail location under current force20 confers no old verdict");
        Check(fast(passing,0)==old(G(new[]{200.0,2250},false),0),"retained candidates still run complete current engineering callbacks");
        var missing=Input(1);var noIntegral=Prepare(missing,0);PreparedReceiverSupportBounds.Minimum(noIntegral,new[]{10000.0},0);
        Check(!PreparedReceiverSupportBounds.MayFit(noIntegral,G(new[]{8000.0,9000,9800},true,10000),0),"a real generic-only tail failure can establish the original explanation");
        PreparedReceiverSupportBounds.Minimum(noIntegral,new[]{2450.0},0);var noSparse=G(new[]{1800.0,2250});noSparse.NecessaryFailureHint=Hint(2);
        Check(Integral(noIntegral,0)==null && !PreparedReceiverSupportBounds.MayFit(noIntegral,noSparse,0) && missing.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==0,"missing single-atom sparse metadata keeps original count path");
        var timed=Input();timed.Trace=new BeamSearchTrace("original timed count order",default,TimeSpan.FromSeconds(60));var timedCheck=Prepare(timed,120);
        PreparedReceiverSupportBounds.MayFit(timedCheck,G(new[]{1800.0,2000,2300}),0);var timedFew=G(new[]{1800.0,2250});timedFew.NecessaryFailureHint=Hint(2);
        Check(!PreparedReceiverSupportBounds.MayFit(timedCheck,timedFew,0) && timed.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==0 && timed.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==1,"timed scope keeps original count priority even after a real numeric explanation");
        using(var cancel=new CancellationTokenSource())
        {
            var cancellable=Input(token:cancel.Token);var callback=Prepare(cancellable,120);var prime=G(new[]{1800.0,2000,2300});PreparedReceiverSupportBounds.MayFit(callback,prime,0);var few=G(new[]{1800.0,2250});few.NecessaryFailureHint=prime.NecessaryFailureHint;cancel.Cancel();bool caught=false;
            try{PreparedReceiverSupportBounds.MayFit(callback,few,0);}catch(OperationCanceledException){caught=true;}
            Check(caught,"live cancellation still precedes an otherwise immediate fresh hint rejection");
        }
    }
    internal static void Run(){OrderingAndCurrentProof();ChangedSourceAndFallbacks();Console.WriteLine("PASS "+checks+" early native-hint ordering checks: current numeric false before redundant counts, deliberate2to3 category only after first explanation, original first diagnostics/raw/fullcallbacks/fallback/cancellation");}
}
