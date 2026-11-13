using System;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;
internal static class PreparedPredicateBindingTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Prepared predicate binding: "+message);}
    sealed class Receiver {internal bool Check(TransferredSlabOptimizer.Geometry g,double edge)=>true;}
    internal static void Run()
    {
        var g=new TransferredSlabOptimizer.Geometry();int calls=0;
        var bound=PreparedReceiverSupportBounds.Attach(new Receiver().Check,(s,e)=>3,(x,e)=>{calls++;return e==12;});
        var predicate=PreparedReceiverSupportBounds.Predicate(bound);
        Check(predicate(g,12) && !predicate(g,13) && calls==2,"bound predicate retains exact geometry/edge behavior");
        Check(PreparedReceiverSupportBounds.Predicate(null)(g,0) && !PreparedReceiverSupportBounds.Predicate(CrossBeamSupportOptimizer.FailedReceiver)(g,0),"unknown and failed callbacks retain their opposite conservative semantics");
        Func<TransferredSlabOptimizer.Geometry,double,bool> later=new Receiver().Check;
        var snapshot=PreparedReceiverSupportBounds.Predicate(later);
        PreparedReceiverSupportBounds.Attach(later,(s,e)=>9,(x,e)=>false);
        Check(snapshot(g,0) && !PreparedReceiverSupportBounds.MayFit(later,g,0),"metadata added after an unknown snapshot stays conservative; direct current lookup remains exact");
        int second=0;
        var no=PreparedReceiverSupportBounds.Attach(new Receiver().Check,(s,e)=>2,(x,e)=>false);
        var yes=PreparedReceiverSupportBounds.Attach(new Receiver().Check,(s,e)=>4,(x,e)=>{second++;return true;});
        var both=CrossBeamSupportOptimizer.BothReceivers(no,yes);
        Check(!PreparedReceiverSupportBounds.Predicate(both)(g,0) && second==0,"AND retains first-branch short circuit and order");
        var either=(Func<TransferredSlabOptimizer.Geometry,double,bool>)typeof(CrossBeamSupportOptimizer).GetMethod("EitherReceiver",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{yes,no});
        Check(PreparedReceiverSupportBounds.Predicate(either)(g,0) && second==1,"OR retains first-branch short circuit and order");
        var preserved=PreparedReceiverSupportBounds.Preserve(new Receiver().Check,bound);
        Check(PreparedReceiverSupportBounds.Predicate(preserved)(g,12) && !PreparedReceiverSupportBounds.Predicate(preserved)(g,0) && PreparedReceiverSupportBounds.Minimum(preserved,new[]{1.0},0)==3,"Preserve binds geometry predicate while retaining the scalar floor");
        using(var cancelled=new CancellationTokenSource())
        {
            var token=cancelled.Token;var pending=PreparedReceiverSupportBounds.Attach(new Receiver().Check,(s,e)=>0,(x,e)=>{token.ThrowIfCancellationRequested();return true;});
            var captured=PreparedReceiverSupportBounds.Predicate(pending);cancelled.Cancel();bool threw=false;
            try{captured(g,0);}catch(OperationCanceledException){threw=true;}
            Check(threw,"binding cannot bypass the original predicate's cancellation");
        }
        Console.WriteLine("PASS "+checks+" prepared predicate binding checks: exact edge, conservative unknowns, failed/AND/OR/Preserve order, late attachment and cancellation");
    }
}
