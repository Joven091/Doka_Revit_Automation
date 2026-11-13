using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class ProjectedValueScalarTests
{
    const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    delegate TransferMember[] Factory(TowerInput common,double stock,double start,double[] positions,double edge,double span,Tuple<double,double> reactions);
    delegate TransferMember CheckMember(TransferMember original,TowerInput common,TowerInput input,int index);
    delegate TransferMember[] CheckPair(TowerInput common,TowerInput input,int index,double stock,double start,double[] positions,double[] lookup,double edge,double span,Tuple<double,double> reactions);
    static readonly Factory factory=typeof(CrossBeamSupportOptimizer).GetMethod("ProjectedPrimaries",hidden).CreateDelegate<Factory>();
    static readonly CheckMember check=typeof(CrossBeamSupportOptimizer).GetMethod("CheckDedicatedPrimary",hidden).CreateDelegate<CheckMember>();
    static readonly CheckPair fast=typeof(CrossBeamSupportOptimizer).GetMethod("CheckProjectedPrimaryPair",hidden).CreateDelegate<CheckPair>();
    static readonly MethodInfo diagnostics=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",hidden);
    static int checks,rejected,retained,rightFailed,exceptions;
    static void Assert(bool value,string text){checks++;if(!value)throw new Exception("Projected value scalar: "+text);}
    static TowerInput Input(int criterion=270,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20,bool lazy=true)=>new TowerInput {PrimaryMaterial=material,
        DeflectionDenominator=criterion,LegCapacityKn=70,NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache(),IgnoreUplift=true,
        Trace=new BeamSearchTrace("projected value scalar"),Diagnostics=new TowerSearchDiagnostics(criterion,ignoreUplift:true){LazyLocations=lazy}};
    static TransferMember[] Eager(TowerInput common,TowerInput input,double stock,double start,double[] positions,double edge,double span,Tuple<double,double> reactions)
    {
        var members=factory(common,stock,start,positions,edge,span,reactions);
        for(int line=0;line<members.Length;line++){members[line]=check(members[line],common,input,0);if(members[line]==null)return null;}return members;
    }
    static void Seed(TowerInput input,double stock,double[] supports,double a,double b,LoadedBeamAnalysis unit)
        =>input.NetworkNumerics.UniformShapes.Store(new TransferredSlabOptimizer.UniformShapeKey(stock,supports,input.Primary.EI,input.DeflectionDenominator,a,b),unit);
    static void Same(double a,double b,string text)=>Assert(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),text);
    static void Compare(double stock,int criterion,Tuple<double,double> reactions,LoadedBeamAnalysis unit,bool warm=true,bool lazy=true,int mode=0)
    {
        double start=-100,edge=50,span=stock-200;var supports=new[]{450.0,stock-450};var positions=supports.Select(x=>x+start).ToArray();
        var expected=Input(criterion,mode==1 ? PrimaryBeamMaterial.WS10 : PrimaryBeamMaterial.DoubleH20,lazy);var actual=Input(criterion,expected.PrimaryMaterial,lazy);
        if(warm && mode!=1){Seed(expected,stock,supports,edge-start,span-edge-start,unit);Seed(actual,stock,supports,edge-start,span-edge-start,unit);}
        var oldCommon=expected.CopyForSearch();var newCommon=actual.CopyForSearch();double[] lookup=supports;
        if(mode==2){expected.ReuseSearchWork=false;actual.ReuseSearchWork=false;}
        if(mode==3){oldCommon.Trace=new BeamSearchTrace("common timed fallback",searchTimeLimit:TimeSpan.FromMinutes(1));newCommon.Trace=new BeamSearchTrace("common timed fallback",searchTimeLimit:TimeSpan.FromMinutes(1));}
        if(mode==4){expected.Trace=new BeamSearchTrace("input timed fallback",searchTimeLimit:TimeSpan.FromMinutes(1));actual.Trace=new BeamSearchTrace("input timed fallback",searchTimeLimit:TimeSpan.FromMinutes(1));}
        if(mode==5)lookup=null;
        if(mode==6){oldCommon.PrimaryMaterial=PrimaryBeamMaterial.WS10;newCommon.PrimaryMaterial=PrimaryBeamMaterial.WS10;}
        if(mode==7){oldCommon.LegCapacityKn=5;newCommon.LegCapacityKn=5;}
        TransferMember[] a=null,b=null;Exception oldError=null,newError=null;
        try{a=Eager(oldCommon,expected,stock,start,positions,edge,span,reactions);}catch(Exception e){oldError=e;}
        try{b=fast(newCommon,actual,0,stock,start,positions,lookup,edge,span,reactions);}catch(Exception e){newError=e;}
        Assert((oldError==null)==(newError==null) && (oldError==null || oldError.GetType()==newError.GetType() && oldError.Message==newError.Message),"unsupported/nonfinite source retains original exception semantics");
        if(oldError!=null){exceptions++;return;}
        Assert((a==null)==(b==null),"eager original and values-first pair retain identical decisions");
        diagnostics.Invoke(null,new object[]{expected.Diagnostics,actual.Diagnostics});Assert(true,"complete diagnostics/failure multiplicities match the original eager pair");
        foreach(var counter in new[]{BeamSearchCounter.PrimaryAnalyses,BeamSearchCounter.UniformPrimaryCacheHits,BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})
            Assert(expected.Trace.Count(counter)==actual.Trace.Count(counter),"exact counter and left-before-right provenance: "+counter);
        Assert(expected.NetworkNumerics.UniformShapes.Count==actual.NetworkNumerics.UniformShapes.Count,"left response preparation effects precede any right failure");
        if(a==null){rejected++;if(reactions.Item1>0 && reactions.Item2>4*reactions.Item1)rightFailed++;return;}
        retained++;Assert(b.Length==2 && !ReferenceEquals(b[0].SupportsMm,b[1].SupportsMm) && b.All(m=>!ReferenceEquals(m.SupportsMm,lookup)),"retained members own fresh per-line supports independent of private lookup metadata");
        for(int line=0;line<2;line++)
        {
            var x=a[line];var y=b[line];Assert(x.Name==y.Name && x.LoadCase==y.LoadCase && x.PrimaryMaterial==y.PrimaryMaterial && x.H20Count==y.H20Count,"retained member identity/material/role matches");
            foreach(var pair in new[]{Tuple.Create(x.StartMm,y.StartMm),Tuple.Create(x.LengthMm,y.LengthMm),Tuple.Create(x.PrimarySelfWeightKnM,y.PrimarySelfWeightKnM),Tuple.Create(x.Analysis.MomentKnM,y.Analysis.MomentKnM),Tuple.Create(x.Analysis.ShearKn,y.Analysis.ShearKn),Tuple.Create(x.Analysis.DeflectionMm,y.Analysis.DeflectionMm),Tuple.Create(x.Analysis.DeflectionUtilization,y.Analysis.DeflectionUtilization)})Same(pair.Item1,pair.Item2,"retained scalar IEEE bits");
            Assert(x.SupportsMm.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(y.SupportsMm.Select(BitConverter.DoubleToInt64Bits)),"retained support coordinates are exact");
            for(int q=0;q<x.Uniform.Length;q++){Same(x.Uniform[q].StartMm,y.Uniform[q].StartMm,"UDL start bits");Same(x.Uniform[q].EndMm,y.Uniform[q].EndMm,"UDL end bits");Same(x.Uniform[q].KnPerM,y.Uniform[q].KnPerM,"UDL factor multiplication/division bits");}
            for(int i=0;i<x.Analysis.Reactions.Length;i++)Same(x.Analysis.Reactions[i],y.Analysis.Reactions[i],"retained reaction IEEE bits");
            foreach(double station in new[]{0.0,450,stock/2,stock-450,stock})for(int field=0;field<3;field++)Same(x.Analysis.Response(station)[field],y.Analysis.Response(station)[field],"retained response diagram IEEE bits");
        }
    }
    static void Decisions()
    {
        foreach(double stock in new[]{2450.0,3900})foreach(int criterion in new[]{270,500})
        {
            var supports=new[]{450.0,stock-450};double span=stock-200,edge=50,start=-100;
            var unit=LoadedBeamAnalysis.AnalyzeCombinedCached(stock,supports,new[]{new BeamDistributedLoad {StartMm=edge-start,EndMm=span-edge-start,KnPerM=1}},Array.Empty<BeamPointLoad>(),TowerOptimizer.PairEI,criterion,null);
            double use=Math.Max(unit.DeflectionUtilization,Math.Max(unit.MomentKnM/10,unit.ShearKn/22)),threshold=1/use;
            double R(double q)=>q*(span-2*edge)/span;
            var cases=new[]{Tuple.Create(R(.1/use),R(.1/use)),Tuple.Create(R(4/use),R(.1/use)),Tuple.Create(R(.1/use),R(4/use)),Tuple.Create(R(4/use),R(4/use)),Tuple.Create(0.0,R(.1/use)),Tuple.Create(-0.0,R(.1/use)),
                Tuple.Create(R(Math.BitDecrement(threshold)),R(.1/use)),Tuple.Create(R(Math.BitIncrement(threshold)),R(.1/use)),Tuple.Create(R(70/unit.Reactions.Max()),R(.1/use)),Tuple.Create(-1.0,R(.1/use)),Tuple.Create(double.NaN,1.0),Tuple.Create(1.0,double.PositiveInfinity)};
            foreach(var reaction in cases)foreach(bool lazy in new[]{false,true})Compare(stock,criterion,reaction,unit,true,lazy);
            foreach(int mode in Enumerable.Range(0,8))Compare(stock,criterion,Tuple.Create(R(.1/use),R(4/use)),unit,true,true,mode);
            Compare(stock,criterion,Tuple.Create(R(.1/use),R(4/use)),unit,false);
        }
        Assert(retained>0 && rejected>0 && rightFailed>0 && exceptions>0,"passing/rejected/asymmetric-right/unsupported full paths are all exercised");
    }
    static void Allocation()
    {
        const double stock=3900,start=-100,edge=50,span=3700;var supports=new[]{450.0,3450};var positions=new[]{350.0,3350};
        var unit=LoadedBeamAnalysis.AnalyzeCombinedCached(stock,supports,new[]{new BeamDistributedLoad {StartMm=150,EndMm=3750,KnPerM=1}},Array.Empty<BeamPointLoad>(),TowerOptimizer.PairEI,270,null);
        var expected=Input();var actual=Input();Seed(expected,stock,supports,150,3750,unit);Seed(actual,stock,supports,150,3750,unit);expected.Diagnostics=actual.Diagnostics=null;expected.Trace=actual.Trace=null;
        var reaction=Tuple.Create(200.0,200.0);Eager(expected,expected,stock,start,positions,edge,span,reaction);fast(actual,actual,0,stock,start,positions,supports,edge,span,reaction);
        const int visits=512;int oldFails=0,newFails=0;long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++)if(Eager(expected,expected,stock,start,positions,edge,span,reaction)==null)oldFails++;long oldBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++)if(fast(actual,actual,0,stock,start,positions,supports,edge,span,reaction)==null)newFails++;long newBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine("PROJECTED_PAIR_ALLOCATIONS visits="+visits+" oldEagerBytes="+oldBytes+" valueFirstBytes="+newBytes);
        Assert(oldFails==visits && newFails==visits && oldBytes-newBytes>=visits*128 && newBytes<4096,"warm exact scalar failures avoid rejected member/UDL/support graphs without a new cache");
    }
    internal static void Run(){Decisions();Allocation();Console.WriteLine("PASS "+checks+" projected value scalar checks: "+retained+" retained, "+rejected+" rejected, "+rightFailed+" asymmetric-right failures, "+exceptions+" unsupported exceptions; exact members/diagnostics/counters and allocations");}
}
