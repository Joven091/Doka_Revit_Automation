using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;
using V=EngineeringDashboard.TransferredSlabOptimizer.SupportVector;
using K=EngineeringDashboard.CrossBeamSupportOptimizer.NecessaryMemberKey;
using B=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;
internal static class SupportVectorTests
{
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Support vector: "+text);}
    static void Bits(double a,double b,string text)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),text);
    static string Outcome(Func<bool> call){try{return "value:"+call();}catch(Exception e){return e.GetType().FullName+":"+e.Message;}}
    static void StorageAndBits()
    {
        var values=new[]{0.0,BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000)),double.Epsilon,-double.Epsilon,double.NaN,double.PositiveInfinity,double.NegativeInfinity,150.1,10000.125};
        var raw=new V(values);Check(!raw.IsNative && !raw.IsNull && raw.Length==values.Length,"raw vectors retain their supplied storage and count");
        var copy=raw.Copy();Check(!ReferenceEquals(values,copy),"explicit copying transfers a fresh array owner");
        for(int i=0;i<values.Length;i++){Bits(raw[i],values[i],"raw value bits include signed zero and nonfinite data");Bits(copy[i],values[i],"copy preserves raw IEEE payloads");}
        values[0]=123;Check(raw[0]==123 && copy[0]==0,"raw fallback keeps the former borrowed-array behavior");
        var random=new Random(20261105);
        for(int count=0;count<=40;count++)for(int sample=0;sample<6;sample++)
        {
            double origin=sample%2==0 ? 10000 : random.NextDouble()*50000;
            var positions=Enumerable.Range(0,count+4).Select(i=>origin+150.1+i*1000.0/3).ToArray();
            var slice=V.NativeSlice(positions,2,count,origin);var expected=positions.Skip(2).Take(count).Select(x=>x-origin).ToArray();
            Check(slice.IsNative && slice.Length==count,"explicit native slice retains its ordered count");
            for(int i=0;i<count;i++)Bits(slice[i],expected[i],"native slice preserves original stored-global minus stock-origin bits");
            Check(!ReferenceEquals(slice.Copy(),positions),"native copying never aliases the global geometry array");
            Check(Outcome(()=>{var ignored=slice[-1];return true;}).StartsWith(typeof(IndexOutOfRangeException).FullName) && Outcome(()=>{var ignored=slice[count];return true;}).StartsWith(typeof(IndexOutOfRangeException).FullName),"native bounds match a materialized local vector");
        }
        double inset=(3600-(1000+(3600-2*150.1-1000-1000)+1000))/2;
        var translated=V.NativeSlice(new[]{10000+inset},0,1,10000);
        Check(translated[0]!=inset,"native slicing does not reconstruct an unequal zero-origin template");
        var nullRaw=new V(null);var defaultRaw=default(V);Check(nullRaw.IsNull && defaultRaw.IsNull && nullRaw.SameStorage(defaultRaw) && Outcome(()=>nullRaw.Length==0).StartsWith(typeof(NullReferenceException).FullName),"default/null raw vectors retain array exception behavior");
        foreach(var malformed in new[]{(double[])null,Array.Empty<double>()})
        {
            var vector=new V(malformed);
            Check(Outcome(()=>malformed.First()==0)==Outcome(()=>vector.First()==0) && Outcome(()=>malformed.Last()==0)==Outcome(()=>vector.Last()==0),"legacy First/Last preserve their own null and empty exception contracts");
        }
        Check(Outcome(()=>V.NativeSlice(null,0,0,0).Length==0).StartsWith(typeof(ArgumentNullException).FullName),"explicit native factory rejects missing owned coordinates");
    }
    static void KeysAndRawGeometry()
    {
        var positions=new[]{200.0,2250,2650,4700};var a=V.NativeSlice(positions,0,2,0);var same=V.NativeSlice(positions,0,2,0);
        var equal=V.NativeSlice(positions,2,2,2450);var different=V.NativeSlice(positions,2,2,2400);
        Check(a.SameStorage(same) && !a.SameStorage(equal) && !a.SameStorage(different) && !a.SameStorage(V.NativeSlice(positions,0,3,0)),"native identity includes first/count/origin rather than only backing storage");
        var expected=new K(25,2450,1,a.Copy());var actual=new K(25,2450,1,a);
        Check(expected.Equals(actual) && actual.Equals(expected) && expected.GetHashCode()==actual.GetHashCode(),"native and raw keys retain exact ordered hash/equality semantics");
        Check(actual.Equals(new K(25,2450,1,equal)),"different native slices with equal local values still share a response key");
        Check(!new K(25,2450,1,a,0,1).Equals(new K(25,2450,1,different,0,1)),"forced hash collisions cannot equate different mappings of the same backing positions");
        foreach(var values in new[]{new[]{double.NaN,1},new[]{-0.0,1},new[]{double.NegativeInfinity,double.PositiveInfinity}})
        {
            var raw=new V(values);var clone=new V((double[])values.Clone());
            Check(new K(0,2450,0,values).Equals(new K(0,2450,0,raw)),"raw wrapper retains original same-array equality for exceptional IEEE values");
            Check(new K(0,2450,0,values).Equals(new K(0,2450,0,(double[])values.Clone()))==new K(0,2450,0,raw).Equals(new K(0,2450,0,clone)),"distinct raw arrays retain old NaN and signed-zero equality");
        }
        var provided=new[]{200.0,2250};var manual=new TransferredSlabOptimizer.Geometry {Positions=new[]{999.0},Stock=new[]{new TowerStockPiece {StartMm=800,FirstSupport=7,LastSupport=8}},MemberSupports=new[]{provided}};
        Check(manual.HasSupportMetadata && manual.SupportRuns==1 && !manual.Supports(0).IsNative,"manual geometry never gains inferred native provenance");
        for(int i=0;i<provided.Length;i++)Bits(manual.Supports(0)[i],provided[i],"raw manual vectors override unrelated global coordinates and stock indices");
        manual.MemberSupports=new double[][]{null};Check(manual.Supports(0).IsNull,"provided null entries remain null rather than being inferred");
        manual.MemberSupports=null;Check(!manual.HasSupportMetadata && Outcome(()=>manual.Supports(0).Length==0).StartsWith(typeof(NullReferenceException).FullName),"missing raw metadata preserves its original failure behavior");
        var fields=typeof(V).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        Check(fields.All(f=>f.FieldType.IsPrimitive || f.FieldType==typeof(double[])),"support keys retain only scalar mapping and coordinate-array storage, never Geometry graphs");
    }
    static void NecessaryMathAndFactories()
    {
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20};
        var loads=new[]{new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=25},new BeamDistributedLoad {StartMm=1000,EndMm=2200,KnPerM=-10},new BeamDistributedLoad {StartMm=2200,EndMm=3000,KnPerM=90}};
        var sparse=CrossBeamSupportOptimizer.PrimarySparseLoads.TryCreate(3000,loads);
        foreach(var local in new[]{new[]{150.0,1150,2150,2850},new[]{0.0,3000},new[]{0.0,0.0},new[]{-1.0,3100},new[]{double.NaN,2800},Array.Empty<double>()})
        {
            var positions=local.Select(x=>x+10000).ToArray();var slice=V.NativeSlice(positions,0,local.Length,10000);var old=slice.Copy();
            Check(Outcome(()=>CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,3000,old,loads,null))==Outcome(()=>CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(input,3000,slice,loads,null)),"native generic necessary physics preserves values and malformed exceptions");
            bool rawFits=false,vectorFits=false;bool rawKnown=sparse.TryFit(input,old,out rawFits),vectorKnown=sparse.TryFitSupports(input,slice,out vectorFits);
            Check(rawKnown==vectorKnown && rawFits==vectorFits,"sparse necessary integration retains signed loads, validation and fallback behavior");
            Check(Outcome(()=>B.ExteriorNormalFits(input,0,3000,old,loads,out _))==Outcome(()=>B.ExteriorNormalFitsSupports(input,0,3000,slice,loads,out _)),"normal-cell necessity preserves interval arithmetic and exceptional vector behavior");
        }
        Check(!sparse.TryFitSupports(input,new V(null),out _) && !sparse.TryFit(input,null,out _),"sparse null input remains an inconclusive fallback");
        var first=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,Diagnostics=new TowerSearchDiagnostics(270)};
        var second=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,Diagnostics=new TowerSearchDiagnostics(270)};
        var failing=new[]{new BeamDistributedLoad {EndMm=3000,KnPerM=100}};var original=new[]{150.0,2850};
        bool oldVerdict=CrossBeamSupportOptimizer.GenericPrimaryForcesFit(first,3000,original,failing,"same first failure",true);
        bool vectorVerdict=CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(second,3000,new V(original),failing,"same first failure",true);
        Check(oldVerdict==vectorVerdict,"typed generic screening preserves its first failed witness");
        typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{first.Diagnostics,second.Diagnostics});checks++;
        var withoutPositions=new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StockLengthMm=2450},new TowerStockPiece {StartMm=2450,StockLengthMm=2450}},MemberSupports=new[]{new[]{200.0,2250},new[]{150.0,1150,2250}}};
        var view=(B.UniversalView)typeof(B).GetMethod("BuildUniversalView",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{input,new[]{withoutPositions},0.0,Array.Empty<BeamDistributedLoad>(),null,null});
        Check(view.Kept.Length==1 && view.Counts==(1UL<<5),"Positions-null manual geometries retain the ordered checked support-count fallback");
        var generated=EngineerTowerSearch.Geometries(new[]{3000.0,3000},false,new TowerInput {LengthMm=6000,StockLengthsMm=new[]{3000.0},StockEndSetbackMm=150,Exclude600Standalone=true,EngineerStationLimit=8},5,8).Take(24).ToArray();
        Check(generated.Length>0,"native source sample emits geometry");
        foreach(var geometry in generated)for(int run=0;run<geometry.Stock.Length;run++)
        {
            Check(geometry.MemberSupports!=null && !geometry.Supports(run).IsNative,"compatibility inspection materializes the original per-geometry array view");
            var stock=geometry.Stock[run];var vector=V.NativeSlice(geometry.Positions,stock.FirstSupport,stock.LastSupport-stock.FirstSupport+1,stock.StartMm);
            for(int i=0;i<vector.Length;i++)Bits(vector[i],geometry.MemberSupports[run][i],"future native slice agrees bitwise with actual generated member metadata");
        }
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
        var generic=typeof(CrossBeamSupportOptimizer).GetMethods(hidden).Single(m=>m.Name=="GenericPrimaryForcesFit");
        Check(generic.GetParameters().Length==6 && generic.GetParameters()[2].ParameterType==typeof(double[]),"old generic double-array reflection signature is unique and unchanged");
        var normal=typeof(B).GetMethods(hidden).Single(m=>m.Name=="ExteriorNormalFits");Check(normal.GetParameters().Length==6 && normal.GetParameters()[3].ParameterType==typeof(double[]),"old normal-cell reflection signature is unchanged");
        Check(typeof(TransferNetworkOptimizer).GetMethods(hidden).Single(m=>m.Name=="Solve").GetParameters().Length==14 && typeof(TransferNetworkOptimizer).GetMethods(hidden).Single(m=>m.Name=="SolveReceiver").GetParameters().Length==15,"receiver reflection entry arities remain unchanged");
    }
    internal static void Run(){StorageAndBits();KeysAndRawGeometry();NecessaryMathAndFactories();Console.WriteLine("PASS "+checks+" support-vector phase-one checks: IEEE slices, raw fallback/ownership, collision-safe keys, necessary physics, compatibility factory inspection and reflection entries");}
}
