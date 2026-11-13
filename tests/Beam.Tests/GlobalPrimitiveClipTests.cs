using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;
using Bound=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;
internal static class GlobalPrimitiveClipTests
{
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Global primitive clipping: "+text);}
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static readonly BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Static;
    internal static void Run()
    {
        var raw=new[]{new[]{Q(0,500,10)},new[]{Q(0,500,10)}};
        var primitive=Bound.PrimitiveIntervals(raw,raw,new[]{0.0,650,1000});
        Check(primitive.SequenceEqual(new[]{0.0,500,500,650,650,1000}),"complete primitive partition preserves every zero-density design cell");
        var average=Bound.GuardedAverage(raw,raw,new[]{0.0,650,1000});Check(average.Length==1,"the numeric lower load can omit zero cells while its clipping contract cannot");
        var full=new Bound.UniversalProfileSignature(average,22,10,primitive);
        Check(!full.Equals(new Bound.UniversalProfileSignature(average,22,10,new[]{0.0,500,500,1000})),"equal lower loads with another zero-cell partition have independent cached evidence");
        Check(!full.Equals(new Bound.UniversalProfileSignature(average,22,10,new[]{0.0,500,500,Math.BitIncrement(650.0),Math.BitIncrement(650.0),1000})),"primitive clipping cuts retain exact numeric bits");
        Check(Bound.PrimitiveIntervals(raw,raw,new[]{double.NaN})==null,"nonfinite possible source cuts supply unknown");
        foreach(double delta in new[]{-2e-7,-1e-7,-5e-8,0,5e-8,1e-7,2e-7})
        {
            double start=650+delta,a=Math.Max(0,Math.Max(start,500)-start),b=Math.Min(500,Math.Min(start+500,650)-start);
            bool dropped=b>a && !(b>a+1e-7);
            Check(Bound.PrimitiveWindowSafe(primitive,start,500)==!dropped,"zero-cell clipped-fragment guard uses the exact original predicate at "+delta);
        }
        Check(!Bound.PrimitiveWindowSafe(new[]{0.0,650,650,650+5e-8,650+5e-8,1000},100,800),"a tiny primitive wholly inside a stock window also disables a signed proof");
        var input=(TowerInput)typeof(UniversalPrimaryGeometryTests).GetMethod("Common",hidden).Invoke(null,null);
        var network=(TransferNetworkInput)typeof(UniversalPrimaryGeometryTests).GetMethod("Network",hidden).Invoke(null,null);
        var cache=new TransferredSlabOptimizer.GeometryCache();var bound=Bound.TryCreate(input,network,6000,true,cache);Check(bound!=null,"native aggregate certificate remains available");
        var cuts=bound.UniversalClippingIntervals(0);Check(cuts.Length>2 && cuts.All(TowerBeamAnalysis.Finite),"beam, bay, crossing, raw and reduced endpoints form a finite complete partition");
        var local=input.CopyForSearch();local.EngineerPlan=EngineerPrimaryPlan.Create(local,network,0);
        local.TransferredSlab=new TransferredSlabInput {IsNetwork=true,SharedGeometryCache=cache,LeftBayMm=0,RightBayMm=3000,RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8};
        var p=new[]{150.0,5850};var bad=new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=1}},Positions=p,Joined=new[]{true},MemberSupports=new[]{p}};var source=new[]{bad};
        var filter=bound.GeometryFilter(0);double selected=double.NaN;
        foreach(double cut in cuts.Distinct().Where(c=>c>0 && c<6000))
            if(filter(local,source,cut-2e-7).Length==0){selected=cut;break;}
        Check(TowerBeamAnalysis.Finite(selected),"a real native mean bound rejects the sparse support pattern away from clipping ambiguity");
        Check(ReferenceEquals(filter(local,source,selected-5e-8),source),"a potentially dropped positive actual atom retains the entire member even when its density is absent from q0");
        Check(bound.StockForce(0,selected-2e-7,6000)>0 && bound.StockForce(0,selected-5e-8,6000)==0,"the aggregate force reservation also becomes unknown at an unsafe signed clipping window");
        Check(bound.StockForce(0,0,6000)>0,"ordinary exact-boundary stock reservations remain available");
        Console.WriteLine("PASS "+checks+" global primitive clipping checks: complete zero cells, exact signature contract, signed tiny-window unknowns, cached-view fallback and aggregate reserve safety");
    }
}
