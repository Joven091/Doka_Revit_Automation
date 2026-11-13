using System;
using System.Linq;
using EngineeringDashboard;

internal static class PrimarySparseLoadTests
{
    static int checks,certified;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Primary sparse loads: "+message);}
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static TowerInput Input(PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20)=>new TowerInput {PrimaryMaterial=material,DeflectionDenominator=270,LegCapacityKn=70};
    static void Parity(TowerInput input,double length,BeamDistributedLoad[] loads,double[] supports)
    {
        var sparse=CrossBeamSupportOptimizer.PrimarySparseLoads.TryCreate(length,loads);Check(sparse!=null,"finite disjoint ordered source supplies owned metadata");
        Check(sparse.TryFit(input,supports,out bool actual),"every valid ascending support query uses exact sparse arithmetic");
        bool expected=CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,length,supports,loads,null);
        Check(actual==expected,"sparse result equals the complete original ordered folds");certified++;
    }
    static void SignedAndGapped()
    {
        var random=new Random(854912);
        foreach(var material in new[]{PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        for(int sample=0;sample<18000;sample++)
        {
            double length=500+random.NextDouble()*11500;int count=2+random.Next(18);
            var cuts=Enumerable.Range(0,count+1).Select(i=>length*i/count).ToArray();cuts[count]=length;
            var loads=Enumerable.Range(0,count).Select(i=>Q(cuts[i],cuts[i+1],(random.NextDouble()*2-1)*120)).ToArray();
            for(int i=0;i<loads.Length;i++)
            {
                if(sample%5==0)loads[i].KnPerM=Math.Abs(loads[i].KnPerM);
                if((sample+i)%7==0)loads[i].StartMm+=(loads[i].EndMm-loads[i].StartMm)*.2;
                if((sample+i)%11==0)loads[i].KnPerM=(i%2==0 ? 0.0 : -0.0);
            }
            int stations=2+random.Next(10);double left=random.NextDouble()*length*.25,right=length*(.75+random.NextDouble()*.25);
            var supports=Enumerable.Range(0,stations).Select(i=>left+(right-left)*i/(stations-1)).ToArray();
            Parity(Input(material),length,loads,supports);
            if(sample%200==0)Parity(Input(material),length,loads,new[]{0.0,cuts[1],length});
        }
        foreach(double q in new[]{0.0,-0.0,double.Epsilon,-double.Epsilon,1e12,-1e12,double.MaxValue,-double.MaxValue})
            Parity(Input(),6000,new[]{Q(0,2000,q),Q(2500,6000,-q)},new[]{150.0,1650,3150,4650,5850});
        for(int sample=0;sample<2000;sample++)
        {
            double q=1e8+sample*1000,epsilon=(sample-1000)*1e-5;
            Parity(Input(),4000,new[]{Q(0,1000,q),Q(1000,2000,-q),Q(2000,3000,q+epsilon),Q(3000,4000,-q)},new[]{0.0,2000,4000});
        }
    }
    static void BoundariesAndNonfiniteArithmetic()
    {
        foreach(var material in new[]{PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(double sign in new[]{-1.0,1})
        {
            var input=Input(material);double v=input.Primary.ShearLimitKn,boundary=2*(v+v*1e-6);
            foreach(double q in new[]{PortableTestRuntime.NextDown(boundary),boundary,PortableTestRuntime.NextUp(boundary)})
                Parity(input,1000,new[]{Q(0,500,sign*q),Q(500,1000,sign*q)},new[]{0.0,1000});
        }
        foreach(double coordinate in new[]{500.0,PortableTestRuntime.NextUp(500),PortableTestRuntime.NextDown(500),500+double.Epsilon})
            Parity(Input(),1000,new[]{Q(0,500,-20),Q(500,1000,20)},new[]{0.0,coordinate,1000});
        Parity(Input(),2*double.Epsilon,new[]{Q(0,double.Epsilon,double.MaxValue),Q(double.Epsilon,2*double.Epsilon,-double.MaxValue)},new[]{0.0,2*double.Epsilon});
        Parity(Input(),1000,new[]{Q(0,500,double.MaxValue),Q(500,1000,-double.MaxValue)},new[]{0.0,1000});
        var cancelled=new[]{Q(0,100,double.MaxValue),Q(100,200,-double.MaxValue),Q(200,1000,0)};
        Check(CrossBeamSupportOptimizer.GenericPrimaryForcesFit(Input(),1000,new[]{400.0,600},cancelled,null),"the reference retains its original optimistic NaN tail comparison behavior");
        Parity(Input(),1000,cancelled,new[]{400.0,600});
        // An INTERSECTING zero q must still be multiplied by the original
        // overflowed midpoint. Dropping it would remove NaN and change the result.
        double length=double.MaxValue;var zeroThenLoad=new[]{Q(length*.5,length*.6,0),Q(length*.6,length,1e-308)};
        var support=new[]{length*.8,length};
        Check(CrossBeamSupportOptimizer.GenericPrimaryForcesFit(Input(),length,support,zeroThenLoad,null),"zero-times-infinite midpoint can mask a later infinite moment in the original fold");
        Parity(Input(),length,zeroThenLoad,support);
    }
    static void UnknownAndOwnership()
    {
        foreach(var loads in new[]{new[]{Q(500,1000,10),Q(0,500,10)},new[]{Q(0,700,10),Q(500,1000,10)},new[]{Q(0,0,10),Q(0,1000,10)},new[]{Q(-1,500,10),Q(500,1000,10)},new[]{Q(0,500,double.NaN),Q(500,1000,10)},new[]{Q(0,500,double.PositiveInfinity),Q(500,1000,10)},new[]{Q(0,500,double.NegativeInfinity),Q(500,1000,10)}})
            Check(CrossBeamSupportOptimizer.PrimarySparseLoads.TryCreate(1000,loads)==null,"unknown/nonfinite or noncanonical sources retain original complete checks");
        var source=new[]{Q(0,500,10),Q(500,1000,10)};var sparse=CrossBeamSupportOptimizer.PrimarySparseLoads.TryCreate(1000,source);
        foreach(var supports in new[]{new[]{500.0,100},new[]{0.0,0},new[]{0.0,1001},new[]{double.NaN,1000},new[]{0.0,double.PositiveInfinity},new[]{-double.Epsilon,1000}})
            Check(!sparse.TryFit(Input(),supports,out _),"unsupported query returns unknown before any sparse decision");
        Check(CrossBeamSupportOptimizer.PrimarySparseLoads.TryCreate(1000,new[]{source[0]})==null
            && CrossBeamSupportOptimizer.PrimarySparseLoads.TryCreate(double.PositiveInfinity,source)==null,"single atom and unsupported member length keep original arithmetic");
        source[0].KnPerM=1e10;source[1].StartMm=0;
        Check(sparse.TryFit(Input(),new[]{0.0,1000},out bool fits) && fits,"published segment values own their immutable source snapshot");
    }
    internal static void Run()
    {
        SignedAndGapped();BoundariesAndNonfiniteArithmetic();UnknownAndOwnership();
        Check(certified>36000,"parity corpus includes actual exact sparse evaluations");
        Console.WriteLine("PASS "+checks+" primary sparse checks: "+certified+" exact original verdicts; signed/zero sources, ULP boundaries, gaps, overflow/NaN folds and immutable ownership");
    }
}
