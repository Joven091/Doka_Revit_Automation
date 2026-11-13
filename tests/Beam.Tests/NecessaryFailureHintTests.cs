using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using EngineeringDashboard;
using Geometry=EngineeringDashboard.TransferredSlabOptimizer.Geometry;
using Vector=EngineeringDashboard.TransferredSlabOptimizer.SupportVector;
using Sparse=EngineeringDashboard.CrossBeamSupportOptimizer.PrimarySparseLoads;

internal static class NecessaryFailureHintTests
{
    static int checks,proofs,changedLoadProofs;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly MethodInfo prepare=typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",hidden);
    static readonly MethodInfo compareDiagnostics=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",hidden);
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Necessary failure hint: "+text);}
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static TowerInput Input(PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20,CancellationToken token=default)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,PrimaryMaterial=material,LegCapacityKn=70,DeflectionDenominator=270,IgnoreUplift=true,Trace=new BeamSearchTrace("failure hint",token),Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true),TransferredSlab=new TransferredSlabInput()};
    static Geometry G(double[] positions,bool native=true)
    {
        var stocks=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=2450,FirstSupport=0,LastSupport=positions.Length-1}};
        if(native)return Geometry.Generated(stocks,positions,Enumerable.Repeat(true,positions.Length-1).ToArray(),null);
        return new Geometry {Stock=stocks,Positions=positions,Joined=Enumerable.Repeat(true,positions.Length-1).ToArray(),MemberSupports=new[]{positions.ToArray()}};
    }
    static Func<Geometry,double,bool> Prepare(TowerInput input,double force)
    {
        var check=(Func<Geometry,double,bool>)prepare.Invoke(null,new object[]{input,force});
        PreparedReceiverSupportBounds.Minimum(check,new[]{2450.0},0);return check;
    }
    // Exact ORIGINAL generic fold for just one named criterion; no sparse
    // cursor or new hint machinery is used by this independent oracle.
    static bool Criterion(TowerInput input,double length,double[] supports,BeamDistributedLoad[] loads,int hint,out double demand,out double limit)
    {
        int kind=hint&7,index=hint>>3;bool span=kind==1,left=kind==2 || kind==3;
        double a=span ? supports[index-1] : left ? 0 : supports[supports.Length-1];
        double b=span ? supports[index] : left ? supports[0] : length,root=left ? b : a,force=0,firstMoment=0;
        foreach(var q in loads)
        {
            force+=q.KnPerM*Math.Max(0,Math.Min(b,q.EndMm)-Math.Max(a,q.StartMm))/1000;
            if(!span){double lo=Math.Max(a,q.StartMm),hi=Math.Min(b,q.EndMm);firstMoment+=hi<=lo ? 0 : q.KnPerM*(hi-lo)*((lo+hi)/2-root)/1e6;}
        }
        double shear=input.Primary.ShearLimitKn,moment=input.Primary.MomentLimitKnM;
        if(span){demand=Math.Abs(force)/2;limit=shear+shear*1e-6;return demand<=limit;}
        if(kind==2 || kind==4){demand=Math.Abs(force);limit=shear+shear*1e-6;return !(demand>limit);}
        demand=Math.Abs(firstMoment);limit=moment+moment*1e-6;return !(demand>limit);
    }
    static int First(TowerInput input,double length,double[] supports,BeamDistributedLoad[] loads)
    {
        for(int i=1;i<supports.Length;i++)if(!Criterion(input,length,supports,loads,(i<<3)|1,out _,out _))return (i<<3)|1;
        foreach(int hint in new[]{2,3,4,5})if(!Criterion(input,length,supports,loads,hint,out _,out _))return hint;
        return 0;
    }
    static void One(TowerInput input,double length,double[] supports,BeamDistributedLoad[] loads)
    {
        var sparse=Sparse.TryCreate(length,loads);Check(sparse!=null,"finite disjoint source creates immutable sparse metadata");
        Check(sparse.TryFitSupportsWithFailure(input,new Vector(supports),out bool actual,out int first),"supported full call still returns a verdict");
        Check(actual==CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,length,supports,loads,null) && first==First(input,length,supports,loads),"first captured site and full result match the ORIGINAL ordered generic folds");
        foreach(int hint in Enumerable.Range(1,supports.Length-1).Select(i=>(i<<3)|1).Concat(new[]{2,3,4,5}))
        {
            bool expected=Criterion(input,length,supports,loads,hint,out _,out _);
            bool known=sparse.TryFitFailureHint(input,new Vector(supports),hint,out bool fits);
            if(!known)continue;
            Check(fits==expected,"every certified criterion equals the original IEEE fold and comparison");
            if(!fits){Check(!actual,"one freshly failing current interval implies the unchanged whole function rejects");proofs++;}
        }
        if(first!=0)
        {
            // A location from another load/line/material is merely a hint.
            foreach(double scale in new[]{-1.0,0,.1,2})
            {
                var other=loads.Select(q=>Q(q.StartMm,q.EndMm,q.KnPerM*scale)).ToArray();var current=Sparse.TryCreate(length,other);if(current==null)continue;
                if(current.TryFitFailureHint(input,new Vector(supports),first,out bool fits) && !fits)
                {Check(!CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,length,supports,other,null),"old failed locations never transfer old q/verdict to a new source");changedLoadProofs++;}
            }
            var changed=Input(input.PrimaryMaterial==PrimaryBeamMaterial.DoubleH20 ? PrimaryBeamMaterial.WS10 : PrimaryBeamMaterial.DoubleH20);
            if(sparse.TryFitFailureHint(changed,new Vector(supports),first,out bool retained) && !retained)
                Check(!CrossBeamSupportOptimizer.GenericPrimaryForcesFit(changed,length,supports,loads,null),"hint rejection always uses current material limits");
        }
    }
    static void PrimitiveParity()
    {
        var random=new Random(994241);
        foreach(var material in new[]{PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})for(int sample=0;sample<2500;sample++)
        {
            double length=500+random.NextDouble()*9000;int n=2+random.Next(6);
            var cuts=Enumerable.Range(0,n+1).Select(i=>length*i/n).ToArray();
            cuts[n]=length;
            var loads=Enumerable.Range(0,n).Select(i=>Q(cuts[i]+(sample%3==0 ? (cuts[i+1]-cuts[i])*.1 : 0),cuts[i+1],(random.NextDouble()-.5)*200)).ToArray();
            var supports=Enumerable.Range(0,2+random.Next(7)).Select(_=>random.NextDouble()*length).OrderBy(x=>x).ToArray();
            One(Input(material),length,supports,loads);
        }
        foreach(double q in new[]{0.0,-0.0,double.Epsilon,-double.Epsilon,1e12,-1e12,double.MaxValue,-double.MaxValue})
            One(Input(),6000,new[]{150.0,1650,3150,4650,5850},new[]{Q(0,2000,q),Q(2500,6000,-q)});
        var input=Input();double limit=input.Primary.ShearLimitKn+input.Primary.ShearLimitKn*1e-6,boundary=2*limit;
        foreach(double q in new[]{Math.BitDecrement(boundary),boundary,Math.BitIncrement(boundary)})
        {
            var loads=new[]{Q(0,500,q),Q(500,1000,q)};var sparse=Sparse.TryCreate(1000,loads);
            One(input,1000,new[]{0.0,1000},loads);
            Check(!sparse.TryFitFailureHint(input,new Vector(new[]{0.0,1000}),9,out _),"ULP threshold neighbourhood is unknown and keeps the old full path");
        }
        One(input,1000,new[]{400.0,600},new[]{Q(0,100,double.MaxValue),Q(100,200,-double.MaxValue),Q(200,1000,0)});
        One(input,1000,new[]{100.0,500,900},new[]{Q(0,500,-10),Q(500,500+5e-8,1e10),Q(500+5e-8,1000,10)});
        double huge=double.MaxValue;One(input,huge,new[]{huge*.8,huge},new[]{Q(huge*.5,huge*.6,0),Q(huge*.6,huge,1e-308)});
        var canonical=Sparse.TryCreate(1000,new[]{Q(0,500,10),Q(500,1000,10)});
        foreach(int hint in new[]{int.MinValue,-1,0,1,6,7,8,10,17,int.MaxValue})Check(!canonical.TryFitFailureHint(input,new Vector(new[]{0.0,1000}),hint,out _),"bad kind/span/side encoding is unknown");
        foreach(var supports in new[]{null,Array.Empty<double>(),new[]{100.0},new[]{200.0,100},new[]{0.0,0},new[]{0.0,double.NaN},new[]{-1.0,900},new[]{0.0,1001}})
            Check(!canonical.TryFitFailureHint(input,new Vector(supports),9,out _),"unsupported support metadata retains the whole old path");
        Check(proofs>1000 && changedLoadProofs>1000,"test corpus exercises true rejections and cross-source recomputation");
    }
    static void PreparedParity()
    {
        foreach(double force in new[]{-60.0,-20,0,20,40,60})
        {
            var actual=Input();var reference=Input();var fast=Prepare(actual,force);var old=Prepare(reference,force);
            foreach(var positions in new[]{new[]{200.0,2250},new[]{1300.0,2300},new[]{1800.0,2250},new[]{200.0,1225,2250},new[]{200.0,700,1225,1750,2250}})
            {
                var native=G(positions);var rawGeometry=G(positions,false);
                bool expected=PreparedReceiverSupportBounds.MayFit(old,rawGeometry,0),value=PreparedReceiverSupportBounds.MayFit(fast,native,0);
                Check(expected==value,"native hinted and raw unhinted actual prepared callbacks agree");
                compareDiagnostics.Invoke(null,new object[]{reference.Diagnostics,actual.Diagnostics});
                int hint=native.NecessaryFailureHint;
                for(int repeat=0;repeat<3;repeat++)Check(PreparedReceiverSupportBounds.MayFit(fast,native,0)==PreparedReceiverSupportBounds.MayFit(old,rawGeometry,0),"fresh hint/cache repeats preserve early truth");
                compareDiagnostics.Invoke(null,new object[]{reference.Diagnostics,actual.Diagnostics});
                Check(hint==native.NecessaryFailureHint && rawGeometry.NecessaryFailureHint==0,"first-writer native location is stable and raw paths never publish hints");
                if(expected)Check(fast(native,0)==old(rawGeometry,0),"retained candidates still execute the full original callback");
            }
            Check(actual.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==reference.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)
                && actual.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected)==reference.Trace.Count(BeamSearchCounter.ResolvedSpanGeometryRejected),"all original rejection counters are unchanged");
        }
        var a=Input();var first=Prepare(a,60);var shared=G(new[]{200.0,2250});
        Check(!PreparedReceiverSupportBounds.MayFit(first,shared,0) && shared.NecessaryFailureHint!=0,"an actual first failure records only its sparse site");
        var b=Input();var second=Prepare(b,20);var rawInput=Input();var raw=Prepare(rawInput,20);
        Check(!PreparedReceiverSupportBounds.MayFit(second,G(new[]{1800.0,2250}),0) && !PreparedReceiverSupportBounds.MayFit(raw,G(new[]{1800.0,2250},false),0),"new owner records its original first diagnostic before any hint use");
        Check(PreparedReceiverSupportBounds.MayFit(second,shared,0)==PreparedReceiverSupportBounds.MayFit(raw,G(shared.Positions,false),0),"a geometry hint from different resolved q rechecks current matched data");
        Check(b.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==0,"the old failure does not become a false current verdict");
        shared.MemberSupports=new[]{shared.Positions.ToArray()};Check(!shared.HasNativeSupportSlices,"explicit mutation clears native provenance");
        Check(PreparedReceiverSupportBounds.MayFit(second,shared,0)==PreparedReceiverSupportBounds.MayFit(raw,G(shared.Positions,false),0),"materialized/replaced metadata follows the original full path");
        var fail=G(new[]{1800.0,2250});Check(!PreparedReceiverSupportBounds.MayFit(second,fail,0),"actual strict tail failure is found");
        long before=b.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected);Check(!PreparedReceiverSupportBounds.MayFit(second,fail,0) && b.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==before+1,"ROI counter counts only a fresh current one-interval rejection");
        // A mismatching stock origin invalidates the cover binding and uses its
        // former unknown/dictionary path even with a nonzero geometry hint.
        fail.Stock[0].StartMm=25;before=b.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected);
        Check(PreparedReceiverSupportBounds.MayFit(second,fail,0) && b.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==before,"unmatched cover metadata cannot reuse a prior hint");
        var countInput=Input();var countCheck=Prepare(countInput,120);var tooFew=G(new[]{200.0,2250});tooFew.NecessaryFailureHint=shared.NecessaryFailureHint;
        Check(!PreparedReceiverSupportBounds.MayFit(countCheck,tooFew,0) && countInput.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==1 && countInput.Trace.Count(BeamSearchCounter.ResolvedFailureHintRejected)==0,"before this owner records its first span explanation, counts keep original priority even with an existing geometry hint");
        // The same local member can occupy another ordinal in a complete stock
        // graph: cached metadata must be repacked using that current ordinal.
        PreparedReceiverSupportBounds.Minimum(first,new[]{2450.0,2450},0);
        var swapped=Geometry.Generated(new[]{new TowerStockPiece {StartMm=2450,StockLengthMm=2450,FirstSupport=2,LastSupport=3},new TowerStockPiece {StartMm=0,StockLengthMm=2450,FirstSupport=0,LastSupport=1}},new[]{200.0,2250,2650,4700},new[]{true,true,true},null);
        Check(!PreparedReceiverSupportBounds.MayFit(first,swapped,0) && ((swapped.NecessaryFailureHint&255)>>1)==1,"a cached primitive is repacked with CURRENT run/line, not the former geometry site");
        var timed=Input();timed.Trace=new BeamSearchTrace("old timed hint path",default,TimeSpan.FromSeconds(60));var timedCheck=Prepare(timed,60);var timedGeometry=G(new[]{200.0,2250});
        Check(!PreparedReceiverSupportBounds.MayFit(timedCheck,timedGeometry,0) && timedGeometry.NecessaryFailureHint==0,"timed native checks keep the old path and publish no metadata");
        using(var cancel=new CancellationTokenSource())
        {
            var input=Input(token:cancel.Token);var check=Prepare(input,60);var geometry=G(new[]{200.0,2250});PreparedReceiverSupportBounds.MayFit(check,geometry,0);cancel.Cancel();bool caught=false;
            try{PreparedReceiverSupportBounds.MayFit(check,geometry,0);}catch(OperationCanceledException){caught=true;}
            Check(caught,"a pre-existing native hint never bypasses live cancellation");
        }
    }
    static void AllocationAndEncoding()
    {
        int old=Unsafe.SizeOf<KeyValuePair<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>>();int current=Unsafe.SizeOf<KeyValuePair<CrossBeamSupportOptimizer.NecessaryMemberKey,CrossBeamSupportOptimizer.NecessaryMemberDecision>>();
        Check(Unsafe.SizeOf<CrossBeamSupportOptimizer.NecessaryMemberDecision>()==8 && old==current,"the cached hint fills former KVP padding without increasing node payload");
        const int n=1024;var held=new Geometry[n];for(int i=0;i<64;i++)held[i]=new Geometry();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<n;i++)held[i]=new Geometry();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(bytes/n==80,"one int occupies existing native Geometry padding: original953 measured80 bytes");
        var sparse=Sparse.TryCreate(2450,new[]{Q(0,1200,60),Q(1200,2450,60)});var vector=new Vector(new[]{200.0,2250});
        for(int i=0;i<1024;i++)sparse.TryFitFailureHint(InputForAllocation,vector,9,out _);
        before=GC.GetAllocatedBytesForCurrentThread();int count=0;for(int i=0;i<16384;i++)if(sparse.TryFitFailureHint(InputForAllocation,vector,9,out bool fits) && !fits)count++;long probe=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(count==16384 && probe==0,"one-interval lookup allocates no object, closure, array or diagnostic graph");
        foreach(int run in new[]{0,1,127})foreach(int line in new[]{0,1})foreach(int primitive in new[]{9,313,2,3,4,5})
        {int hint=CrossBeamSupportOptimizer.EncodeNecessaryFailureHint(run,line,primitive);Check(hint>0 && ((hint&255)>>1)==run && (hint&1)==line && (hint>>8)==primitive,"atomic packed location round-trips run/line/primitive");}
        foreach(var invalid in new[]{new[]{-1,0,9},new[]{128,0,9},new[]{0,2,9},new[]{0,0,0},new[]{0,0,321},new[]{0,0,int.MaxValue}})
            Check(CrossBeamSupportOptimizer.EncodeNecessaryFailureHint(invalid[0],invalid[1],invalid[2])==0,"oversized/malformed sites keep original checks rather than narrowing source limits");
        Console.WriteLine("FAILURE_HINT_LAYOUT oldKvp="+old+" newKvp="+current+" decision="+Unsafe.SizeOf<CrossBeamSupportOptimizer.NecessaryMemberDecision>()+" geometry="+(bytes/n)+" probeBytes="+probe);
    }
    static readonly TowerInput InputForAllocation=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20};
    internal static void Run(){PrimitiveParity();PreparedParity();AllocationAndEncoding();Console.WriteLine("PASS "+checks+" necessary failure-hint checks: "+proofs+" fresh interval proofs, "+changedLoadProofs+" changed-source proofs; original full/counter/first-diagnostic parity, fallback/cancellation and measured layout/allocation");}
}
