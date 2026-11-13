using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EngineeringDashboard;
using Vector=EngineeringDashboard.TransferredSlabOptimizer.SupportVector;
using Geometry=EngineeringDashboard.TransferredSlabOptimizer.Geometry;
using Sparse=EngineeringDashboard.CrossBeamSupportOptimizer.PrimarySparseLoads;

internal static class NativeCellFailureHintTests
{
    static int checks,singleCellQueries,singleCellFailures,fallbacks;
    static readonly TowerInput input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20};
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Native single-cell hint: "+text);}
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static Vector Native(double[] values,double origin=0)=>Vector.NativeSlice(values.Select(x=>x+origin).ToArray(),0,values.Length,origin);
    static void Compare(TowerInput common,double length,BeamDistributedLoad[] loads,Vector supports,int hint,int cell)
    {
        var sparse=Sparse.TryCreate(length,loads);Check(sparse!=null,"canonical source has immutable disjoint finite metadata");
        bool oldKnown=sparse.TryFitFailureHint(common,supports,hint,out bool oldFits);
        bool known=sparse.TryFitNativeFailureDescriptor(common,supports,hint,cell,out bool fits,out bool single);
        if(single)
        {
            singleCellQueries++;Check(oldKnown && known && fits==oldFits,"certified single cell retains the validated original IEEE decision");
            if(!fits){singleCellFailures++;Check(!CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(common,length,supports,loads,null),"fresh single-cell failure also fails the complete unchanged generic function");}
        }
        else
        {
            fallbacks++;if(known)Check(oldKnown && fits==oldFits,"cell mismatch/raw fallback keeps the entire old validated helper decision");
        }
    }
    static void CaptureAndCurrentSources()
    {
        var fixtures=new[]{
            Tuple.Create(new[]{150.0,2100,3800},new[]{Q(0,2000,0),Q(2000,4000,100)},17,1),
            Tuple.Create(new[]{1500.0,3000},new[]{Q(0,2000,30),Q(2000,4000,0)},2,0),
            Tuple.Create(new[]{1500.0,3000},new[]{Q(0,2000,10),Q(2000,4000,0)},3,0),
            Tuple.Create(new[]{1000.0,2500},new[]{Q(0,2000,0),Q(2000,4000,30)},4,1),
            Tuple.Create(new[]{1000.0,2500},new[]{Q(0,2000,0),Q(2000,4000,10)},5,1)};
        foreach(var fixture in fixtures)
        {
            var vector=Native(fixture.Item1);var sparse=Sparse.TryCreate(4000,fixture.Item2);
            Check(sparse.TryFitSupportsWithDescriptor(input,vector,out bool fits,out int primitive,out int cell) && !fits && primitive==fixture.Item3 && cell==fixture.Item4,"original span/shear/moment return captures exactly its sole containing cell without changing its site");
            Check(sparse.TryFitSupportsWithFailure(input,vector,out bool old,out int oldPrimitive) && old==fits && oldPrimitive==primitive,"existing primitive API and arity retain the untagged failure code");
            Compare(input,4000,fixture.Item2,vector,primitive,cell);
            foreach(double scale in new[]{-1.0,0,.01,2})
            {
                var changed=fixture.Item2.Select(q=>Q(q.StartMm,q.EndMm,q.KnPerM*scale)).ToArray();
                Compare(input,4000,changed,vector,primitive,cell);
                Compare(new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.WS10},4000,changed,vector,primitive,cell);
            }
            var changedPartition=new[]{Q(0,100,0),Q(100,1000,1),Q(1000,4000,40)};
            Compare(input,4000,changedPartition,vector,primitive,cell); // old index remains in range, but does not contain the interval.
            Compare(input,4000,fixture.Item2,vector,primitive,-1);Compare(input,4000,fixture.Item2,vector,primitive,99);
            Compare(input,4000,fixture.Item2,new Vector(vector.Copy()),primitive,cell);
        }
        var straddle=new[]{Q(0,2000,100),Q(2000,4000,100)};var source=Sparse.TryCreate(4000,straddle);
        Check(source.TryFitSupportsWithDescriptor(input,Native(new[]{150.0,3850}),out bool passed,out int where,out int segment) && !passed && where==9 && segment==-1,"a failing span that crosses any source boundary records no cell certificate");
        var low=Sparse.TryCreate(4000,new[]{Q(0,2000,0),Q(2000,4000,1)});
        Check(low.TryFitNativeFailureDescriptor(input,Native(new[]{150.0,2100,3800}),17,1,out bool nowFits,out bool one) && one && nowFits,"unchanged cell index reads CURRENT q and can retain an old failure");
        var mutable=new[]{Q(0,2000,0),Q(2000,4000,100)};var owned=Sparse.TryCreate(4000,mutable);mutable[1].KnPerM=0;
        Check(owned.TryFitNativeFailureDescriptor(input,Native(new[]{150.0,2100,3800}),17,1,out bool staleObject,out bool ownedCell) && ownedCell && !staleObject,"cell proof reads the current owner's immutable segment snapshot, not a mutable source object");
        var replacement=Sparse.TryCreate(4000,mutable);Check(replacement.TryFitNativeFailureDescriptor(input,Native(new[]{150.0,2100,3800}),17,1,out bool currentObject,out bool replacementCell) && replacementCell && currentObject,"a new source owner with the same old index obtains its own current density");
    }
    static void RandomAndEdges()
    {
        var random=new Random(721549);
        foreach(var material in new[]{PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})for(int sample=0;sample<1200;sample++)
        {
            var common=new TowerInput {PrimaryMaterial=material};double length=1000+random.NextDouble()*8000;int n=2+random.Next(5);
            var cuts=Enumerable.Range(0,n+1).Select(i=>length*i/n).ToArray();cuts[n]=length;
            var loads=Enumerable.Range(0,n).Select(i=>Q(cuts[i]+(sample%3==0 ? .1*(cuts[i+1]-cuts[i]) : 0),cuts[i+1],(random.NextDouble()-.5)*200)).ToArray();
            var positions=Enumerable.Range(0,2+random.Next(7)).Select(_=>length*random.NextDouble()).OrderBy(x=>x).ToArray();var vector=Native(positions,sample%2==0 ? 0 : 125);
            var sparse=Sparse.TryCreate(length,loads);Check(sparse!=null,"random source is canonical");
            Check(sparse.TryFitSupportsWithDescriptor(common,vector,out bool fits,out int primitive,out int cell),"new full descriptor core handles the original supported query");
            Check(sparse.TryFitSupportsWithFailure(common,vector,out bool old,out int oldPrimitive) && old==fits && oldPrimitive==primitive,"all old full primitive outcomes are preserved");
            if(primitive!=0)foreach(int candidate in new[]{cell,0,n-1,-1,n})Compare(common,length,loads,vector,primitive,candidate);
        }
        foreach(double sign in new[]{1.0,-1})
        {
            double boundary=2*(input.Primary.ShearLimitKn+input.Primary.ShearLimitKn*1e-6);
            foreach(double q in new[]{Math.BitDecrement(boundary),boundary,Math.BitIncrement(boundary)})
            {
                var sparse=Sparse.TryCreate(2000,new[]{Q(0,1000,sign*q),Q(1000,2000,0)});
                Check(!sparse.TryFitNativeFailureDescriptor(input,Native(new[]{0.0,1000,1900}),9,0,out _,out _),"ULP threshold neighbourhood remains unknown on the native cell path");
            }
        }
        foreach(double q in new[]{0.0,-0.0,double.Epsilon,-double.Epsilon,double.MaxValue,-double.MaxValue})
            Compare(input,4000,new[]{Q(0,2000,0),Q(2000,4000,q)},Native(new[]{150.0,2100,3800}),17,1);
        Compare(input,2000,new[]{Q(-0.0,1000,100),Q(1000,2000,0)},Vector.NativeSlice(new[]{-0.0,1000,1900},0,3,0),9,0);
        foreach(double endpoint in new[]{2000.0,Math.BitDecrement(2000),Math.BitIncrement(2000),2000-5e-8,2000+5e-8})
            Compare(input,4000,new[]{Q(0,2000,50),Q(2000,4000,-50)},Native(new[]{150.0,endpoint,3800}),9,0);
        var tiny=new[]{Q(0,500,1),Q(500,500+5e-8,1e10),Q(500+5e-8,1000,1)};Compare(input,1000,tiny,Native(new[]{100.0,500,500+5e-8,900}),17,1);
        double huge=double.MaxValue;var zero=Sparse.TryCreate(huge,new[]{Q(0,huge*.5,0),Q(huge*.5,huge,-0.0)});
        Check(!zero.TryFitNativeFailureDescriptor(input,Native(new[]{0.0,huge*.8}),5,1,out _,out _),"zero times overflowed tail midpoint remains unknown, preserving original NaN folds");
        var malformed=Native(new[]{200.0,100,2100,3800});var strong=Sparse.TryCreate(4000,new[]{Q(0,2000,0),Q(2000,4000,100)});
        Check(!strong.TryFitFailureHint(input,malformed,25,out _),"validated compatibility API still refuses whole-vector disorder");
        Check(strong.TryFitNativeFailureDescriptor(input,malformed,25,1,out bool retained,out bool cellPath) && cellPath && !retained
            && !CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(input,4000,malformed,new[]{Q(0,2000,0),Q(2000,4000,100)},null),"local native proof remains a rejecting conjunct even when another interval is malformed");
        foreach(var positions in new[]{new[]{0.0,double.NaN},new[]{-1.0,1000},new[]{0.0,4001},new[]{3000.0,2000}})
            Check(!strong.TryFitNativeFailureDescriptor(input,Native(positions),9,0,out _,out _),"selected nonfinite/outside/reversed endpoints retain old full fallback");
        Check(singleCellQueries>100 && singleCellFailures>100 && fallbacks>100,"corpus measures both genuine certified cells and changed-cell fallbacks");
    }
    static void PackingAndLayout()
    {
        foreach(int primitive in new[]{9,313,2,3,4,5})foreach(int cell in new[]{-1,0,1,16382,16383,int.MinValue,int.MaxValue})
        foreach(int run in new[]{0,127})foreach(int line in new[]{0,1})
        {
            int descriptor=CrossBeamSupportOptimizer.PackNecessaryFailureDescriptor(primitive,cell),hint=CrossBeamSupportOptimizer.EncodeNecessaryFailureDescriptor(run,line,descriptor);
            Check(hint>0 && (hint&1)==line && ((hint&255)>>1)==run && ((hint>>8)&511)==primitive,"maximum packed run/line/primitive remains an exact positive31-bit integer");
            Check((hint>>17)-1==((uint)cell<16383 ? cell : -1),"unsupported cell counts fall back to a bare location without losing the original check");
            Check(CrossBeamSupportOptimizer.EncodeNecessaryFailureHint(run,line,primitive)==CrossBeamSupportOptimizer.EncodeNecessaryFailureDescriptor(run,line,primitive),"legacy bare encoder and descriptors remain interchangeable");
        }
        foreach(int bad in new[]{0,-1,int.MinValue,int.MaxValue,321})Check(CrossBeamSupportOptimizer.PackNecessaryFailureDescriptor(bad,0)==0,"malformed primitives supply no descriptor");
        foreach(int bad in new[]{0,-1,int.MinValue,int.MaxValue,321})Check(CrossBeamSupportOptimizer.EncodeNecessaryFailureDescriptor(0,0,bad)==0,"malformed maximum descriptors cannot decode into another valid location");
        int old=Unsafe.SizeOf<KeyValuePair<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>>(),current=Unsafe.SizeOf<KeyValuePair<CrossBeamSupportOptimizer.NecessaryMemberKey,CrossBeamSupportOptimizer.NecessaryMemberDecision>>();Check(old==current && Unsafe.SizeOf<CrossBeamSupportOptimizer.NecessaryMemberDecision>()==8,"cell metadata adds no node/cache payload");
        var sparse=Sparse.TryCreate(4000,new[]{Q(0,2000,0),Q(2000,4000,100)});var vector=Native(new[]{150.0,2100,3800});for(int i=0;i<1024;i++)sparse.TryFitNativeFailureDescriptor(input,vector,17,1,out _,out _);
        int count=0;long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<16384;i++)if(sparse.TryFitNativeFailureDescriptor(input,vector,17,1,out bool fits,out bool cell) && cell && !fits)count++;long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(count==16384 && bytes==0,"current single-cell proof allocates no object, source array or context graph");
        var held=new Geometry[1024];before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<held.Length;i++)held[i]=new Geometry();long size=(GC.GetAllocatedBytesForCurrentThread()-before)/held.Length;Check(size==80,"existing geometry remains80 bytes");
        Console.WriteLine("NATIVE_CELL_LAYOUT decision=8 kvp="+current+" geometry="+size+" probeBytes="+bytes);
    }
    internal static void Run(){CaptureAndCurrentSources();RandomAndEdges();PackingAndLayout();Console.WriteLine("PASS "+checks+" native single-cell hint checks: "+singleCellQueries+" certified test-corpus cells/"+singleCellFailures+" strict failures and "+fallbacks+" fallback queries; original APIs/current loads/partition/IEEE/packing/layout parity (no actual-search qualifying-ratio claim)");}
}
