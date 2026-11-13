using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

// Exercise actual resolved receiver preparation, rather than a second copy of
// the sparse arithmetic. Reuse-off retains the original generic miss path.
internal static class PrimarySparseLoadIntegrationTests
{
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly MethodInfo createInput=typeof(ResolvedSupportCountBoundTests).GetMethod("Input",hidden);
    static readonly MethodInfo prepare=typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",hidden);
    static readonly MethodInfo diagnostics=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",hidden);
    static int checks,certified;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Primary sparse integration: "+text);}
    static TowerInput Input(bool reuse,CancellationToken token=default)
    {
        var input=(TowerInput)createInput.Invoke(null,new object[]{70.0,true,token});input.ReuseSearchWork=reuse;return input;
    }
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Prepare(TowerInput input,double force)
    {
        try{return (Func<TransferredSlabOptimizer.Geometry,double,bool>)prepare.Invoke(null,new object[]{input,force});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static TransferredSlabOptimizer.Geometry G(double[] positions)=>new TransferredSlabOptimizer.Geometry {
        Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=2450,FirstSupport=0,LastSupport=positions.Length-1}},
        Positions=positions,Joined=Enumerable.Repeat(true,positions.Length-1).ToArray(),MemberSupports=new[]{positions}};
    static IDictionary Loads(Func<TransferredSlabOptimizer.Geometry,double,bool> check)
    {
        var owner=PreparedReceiverSupportBounds.Predicate(check).Target;
        return (IDictionary)owner.GetType().GetField("loads",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(owner);
    }
    static CrossBeamSupportOptimizer.PrimarySparseLoads Integral(object value)
        =>(CrossBeamSupportOptimizer.PrimarySparseLoads)value.GetType().GetField("Integral",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
    static void PreparedParity()
    {
        foreach(double force in new[]{-60.0,-20,0,20,40,60})foreach(double edge in new[]{0.0,25})
        {
            var actual=Input(true);var expected=Input(false);var fast=Prepare(actual,force);var old=Prepare(expected,force);
            int actualMinimum=PreparedReceiverSupportBounds.Minimum(fast,new[]{2450.0},edge);
            Check(actualMinimum==PreparedReceiverSupportBounds.Minimum(old,new[]{2450.0},edge),"exact per-line force floors are unchanged");
            var prepared=Loads(fast);Check(prepared.Count==2,"each exact line/window has one private prepared load owner");
            foreach(DictionaryEntry entry in prepared)
            {
                var value=(Tuple<BeamDistributedLoad[],double[],double[],double>)entry.Value;
                Check(value.Item2.SequenceEqual(value.Item1.SelectMany(q=>new[]{q.StartMm,q.EndMm}))
                    && value.Item3.SequenceEqual(value.Item1.SelectMany(q=>new[]{q.KnPerM,q.KnPerM}))
                    && BitConverter.DoubleToInt64Bits(value.Item4)==BitConverter.DoubleToInt64Bits(value.Item1.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000)),"existing load key arrays and ordered total-force bits are retained");
            }
            var owners=prepared.Values.Cast<object>().ToArray();var integrals=owners.Select(Integral).ToArray();
            foreach(var positions in new[]{new[]{200.0,2250},new[]{200.0,1225,2250},new[]{1300.0,2300},new[]{1800.0,2250},new[]{200.0,700,1225,1750,2250}})
            {
                foreach(var integral in integrals)if(integral!=null && integral.TryFit(actual,positions,out _))certified++;
                var geometry=G(positions);bool before=PreparedReceiverSupportBounds.MayFit(old,geometry,edge);
                Check(PreparedReceiverSupportBounds.MayFit(fast,geometry,edge)==before,"prefix-or-original miss matches generic resolved necessity for signed loads and stock tails");
                Check(fast(geometry,edge)==old(geometry,edge),"the complete ordinary receiver callback still makes the same full numerical decision");
                Check(PreparedReceiverSupportBounds.MayFit(fast,G(positions.ToArray()),edge)==PreparedReceiverSupportBounds.MayFit(old,G(positions.ToArray()),edge),"a different geometry reference retains exact member-L1 decision reuse");
                diagnostics.Invoke(null,new object[]{expected.Diagnostics,actual.Diagnostics});
            }
            PreparedReceiverSupportBounds.Minimum(fast,new[]{2450.0},edge);
            Check(prepared.Values.Cast<object>().SequenceEqual(owners)
                && prepared.Values.Cast<object>().Select(Integral).SequenceEqual(integrals),"repeated covers reuse existing immutable per-window preparations");
            Check(Loads(old).Values.Cast<object>().All(v=>Integral(v)==null),"reuse-off keeps original generic arithmetic without integral preparation");
            Check(actual.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==expected.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)
                && actual.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected)==expected.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected),"repeated early decisions retain diagnostic rejection counts and order");
        }
        Check(certified>20,"actual resolved windows exercise certified integral decisions, not only fallback");
    }
    static void OwnershipAndFallbacks()
    {
        var owner=typeof(CrossBeamSupportOptimizer).GetNestedType("ResolvedPrimaryLoad",BindingFlags.NonPublic);
        var constructor=owner.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();
        object Make(BeamDistributedLoad[] loads,bool enabled)=>constructor.Invoke(new object[]{1000.0,loads,enabled});
        var one=new[]{new BeamDistributedLoad {EndMm=1000,KnPerM=10}};
        Check(Integral(Make(one,true))==null,"single-atom members keep the original generic shortcut-free path");
        var overlap=new[]{new BeamDistributedLoad {EndMm=700,KnPerM=10},new BeamDistributedLoad {StartMm=500,EndMm=1000,KnPerM=10}};
        Check(Integral(Make(overlap,true))==null,"unsupported overlapping load atoms keep the entire original predicate");
        var input=Input(true);double shear=input.Primary.ShearLimitKn,q=2*(shear+shear*1e-6);var boundary=new[]{new BeamDistributedLoad {EndMm=500,KnPerM=q},new BeamDistributedLoad {StartMm=500,EndMm=1000,KnPerM=q}};
        var value=Make(boundary,true);var integral=Integral(value);
        Check(integral!=null && integral.TryFit(input,new[]{0.0,1000},out bool boundaryFits)
            && boundaryFits==CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,1000,new[]{0.0,1000},boundary,null),"original-limit ULP neighbourhood keeps the exact original sparse fold");
        Check(Integral(Make(boundary,false))==null,"disabled reuse does not prepare metadata");
        using(var cancellation=new CancellationTokenSource())
        {
            var pending=Prepare(Input(true,cancellation.Token),20);PreparedReceiverSupportBounds.Minimum(pending,new[]{2450.0},0);
            PreparedReceiverSupportBounds.MayFit(pending,G(new[]{200.0,2250}),0);cancellation.Cancel();bool cancelled=false;
            try{PreparedReceiverSupportBounds.MayFit(pending,G(new[]{200.0,2250}),0);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"a prepared prefix or reused member decision never bypasses cancellation");
        }
    }
    internal static void Run()
    {
        PreparedParity();OwnershipAndFallbacks();
        Console.WriteLine("PASS "+checks+" primary sparse integration checks: "+certified+" actual resolved certificates, original full callback/diagnostic parity, immutable per-window preparation and fallback/cancellation");
    }
}
