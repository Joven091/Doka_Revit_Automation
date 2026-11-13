using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;
internal static class ScalarCoverBindingTests
{
    static int checks;static readonly BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Static;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Scalar cover binding: "+text);}
    static TowerInput Input(double capacity=70,CancellationToken token=default)=>(TowerInput)typeof(ResolvedSupportCountBoundTests).GetMethod("Input",hidden).Invoke(null,new object[]{capacity,true,token});
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Prepare(TowerInput input,double force)=>(Func<TransferredSlabOptimizer.Geometry,double,bool>)typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",hidden).Invoke(null,new object[]{input,force});
    static TransferredSlabOptimizer.Geometry G(double[] supports,double start=0,double length=2450)=>new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=start,StockLengthMm=length,FirstSupport=0,LastSupport=supports.Length-1}},Positions=supports,Joined=Enumerable.Repeat(true,supports.Length-1).ToArray(),MemberSupports=new[]{supports}};
    internal static void Run()
    {
        foreach(double force in new[]{-60.0,-20,0,20,40,60})foreach(double edge in new[]{0.0,25})
        {
            var a=Input();var b=Input();var bound=Prepare(a,force);var fallback=Prepare(b,force);
            PreparedReceiverSupportBounds.Minimum(bound,new[]{2450.0},edge);PreparedReceiverSupportBounds.Minimum(fallback,new[]{2450.0},edge);
            // A different completed cover forces the original dynamic path for
            // this exact old cover, while retaining its previously prepared data.
            PreparedReceiverSupportBounds.Minimum(fallback,new[]{1250.0,1200},edge);
            foreach(var supports in new[]{new[]{200.0,2250},new[]{200.0,1225,2250},new[]{1300.0,2300},new[]{1800.0,2250}})
            {
                var geometry=G(supports);
                bool expected=PreparedReceiverSupportBounds.MayFit(fallback,geometry,edge),actual=PreparedReceiverSupportBounds.MayFit(bound,geometry,edge);
                Check(actual==expected,"completed count/load binding equals the original dynamic lookup for signed loads and exact edge");
                Check(bound(geometry,edge)==fallback(geometry,edge),"ordinary full callback and member FE results remain unchanged");
            }
            Check(a.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==b.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)
                && a.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected)==b.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected),"bound and fallback paths replay the same necessary component counters");
        }
        var input=Input();var check=Prepare(input,60);var original=G(new[]{200.0,2250});
        Check(PreparedReceiverSupportBounds.MayFit(check,original,0),"an unprepared cover remains inconclusive");
        PreparedReceiverSupportBounds.Minimum(check,new[]{2450.0},0);
        Check(!PreparedReceiverSupportBounds.MayFit(check,original,0),"the prepared exact cover retains its strict resolved failure");
        Check(PreparedReceiverSupportBounds.MayFit(check,original,25),"a different absolute edge cannot use an old binding");
        Check(PreparedReceiverSupportBounds.MayFit(check,G(new[]{200.0,2250},25),0),"a different relative stock start cannot use an old binding");
        Check(PreparedReceiverSupportBounds.MayFit(check,G(new[]{200.0,2250},length:2451),0),"a different stock length cannot use an old binding");
        PreparedReceiverSupportBounds.Minimum(check,new[]{1250.0,1200},0);
        Check(!PreparedReceiverSupportBounds.MayFit(check,original,0),"changing topology falls back to current dynamic known data rather than stale or unknown snapshots");
        var counts=Input();var countCheck=Prepare(counts,60);PreparedReceiverSupportBounds.Minimum(countCheck,new[]{2450.0,2450},0);
        var two=new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=2450},new TowerStockPiece {StartMm=2450,StockLengthMm=2450}},MemberSupports=new[]{new[]{200.0,2250},new[]{200.0}}};
        Check(!PreparedReceiverSupportBounds.MayFit(countCheck,two,0) && counts.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==1
            && counts.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected)==0,"the complete stock-count pass still precedes a first-stock span failure");
        var unsupported=Input();unsupported.PrimaryMaterial=PrimaryBeamMaterial.WS10;var unknown=Prepare(unsupported,60);
        Check(PreparedReceiverSupportBounds.Minimum(unknown,new[]{2450.0},0)==0 && PreparedReceiverSupportBounds.MayFit(unknown,original,0),"an unsupported stock never publishes a false binding");
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=Prepare(Input(token:cancellation.Token),60);PreparedReceiverSupportBounds.Minimum(interrupted,new[]{2450.0},0);cancellation.Cancel();bool cancelled=false;
            try{PreparedReceiverSupportBounds.MayFit(interrupted,original,0);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"completed cover bindings cannot bypass cancellation");
        }
        Console.WriteLine("PASS "+checks+" scalar cover binding checks: exact window/topology, signed dynamic/full-callback parity, counter order, unsupported and cancellation fallbacks");
    }
}
