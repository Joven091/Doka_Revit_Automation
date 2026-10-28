using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class ProjectedCrossingTests
{
    static readonly MethodInfo solve=typeof(CrossBeamSupportOptimizer).GetMethod("ProjectedJunction",BindingFlags.Static|BindingFlags.NonPublic);
    static double[] Demands(CrossBeamSupportResult r)=>r.Members.SelectMany(m=>new[] {m.LengthMm,m.StartMm,m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization}
        .Concat(m.SupportsMm).Concat(m.Analysis.Reactions).Concat(m.Uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm,q.KnPerM})).Concat(m.Points.SelectMany(p=>new[] {p.PositionMm,p.Kn}))).ToArray();
    static CrossBeamSupportResult Solve(TowerInput input,TransferNetworkResult r,double width,double offset,double shift,FormworkSolid[] obstacles,double spacing,CrossBeamSupportOptimizer.ProjectedJunctionWork work)
    {
        var args=new object[] {input,r,0,spacing,width,offset,shift,200.0,200.0,obstacles,null,width==1524 ? 2000.0 : 1524.0,CrossShutterAllowance.Standard,null,
            input.ReuseSearchWork ? work : null};
        try{return (CrossBeamSupportResult)solve.Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}
    }
    internal static void Run()
    {
        int passed=0,failed=0,checks=0;
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(bool custom in new[] {false,true})
        foreach(double width in new[] {1000.0,1500,1524,2000})
        foreach(int example in Enumerable.Range(0,4))
        {
            var r=PrimaryBearingTests.Fixture();r.Input.Bays[0].WidthMm=example==0 ? 3950 : 4203.3922668457035;r.BeamCentresMm[1]=r.Input.Beams[0].WidthMm/2+r.Input.Bays[0].WidthMm+r.Input.Beams[1].WidthMm/2;
            var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,PrimaryMaterial=material,LengthMm=6900,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,
                DeflectionDenominator=270,LegCapacityKn=example==1 ? 5 : 70,IgnoreUplift=example%2==0,AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.BeamSupports};
            double offset=example==2 ? -75 : 0,shift=example==3 ? 400 : 0;
            var obstacles=example==3 ? new[] {new FormworkSolid {Name="Fixed primary",X0=2500,X1=4500,Y0=1800,Y1=2200,Z0=-2000,Z1=-1100}} : Array.Empty<FormworkSolid>();
            if(example==2)
            {
                r.Input.BeamSpacingsMm=new[] {305.0,305};r.Input.SlabSpacingsMm=new[] {488.0};r.Input.CrossingSpacingsMm=new[] {350.0};
                r.Bays[0]=new UpperTransferPlan {StockLengthMm=3300,EdgeMm=(r.Input.Bays[0].WidthMm-3300)/2,NearFootMm=1100,FarFootMm=1100};r.SlabCoveredLengthMm=6900;
            }
            var work=new CrossBeamSupportOptimizer.ProjectedJunctionWork(obstacles);
            foreach(double spacing in new[] {350.0,305,244})
            {
            input.ReuseSearchWork=false;var reference=Solve(input,r,width,offset,shift,obstacles,spacing,work);
            input.ReuseSearchWork=true;var actual=Solve(input,r,width,offset,shift,obstacles,spacing,work);
            if((reference==null)!=(actual==null))throw new Exception("Projected crossing feasibility changed: "+material+" "+custom+" "+width+" "+example);
            if(actual==null){failed++;continue;}
            passed++;var a=Demands(reference);var b=Demands(actual);
            if(a.Length!=b.Length || !a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-12*Math.Max(1,Math.Abs(x))).All(x=>x))throw new Exception("Projected crossing unrounded member demands changed");
            if(!reference.SecondaryStationsMm.SequenceEqual(actual.SecondaryStationsMm) || !reference.Towers.PositionsMm.SequenceEqual(actual.Towers.PositionsMm))throw new Exception("Projected crossing selected geometry changed");
            foreach(var m in actual.Members)
            {
                if(m.Utilization>1+1e-8 || !m.Analysis.DeflectionEvaluated || Math.Abs(m.Analysis.Reactions.Sum()-m.Analysis.TotalLoadKn)>1e-8*Math.Max(1,Math.Abs(m.Analysis.TotalLoadKn)))throw new Exception("Projected member capacity/equilibrium failed");
                checks++;
            }
            if(actual.Towers.MaxLegKn>input.LegCapacityKn+1e-8)throw new Exception("Projected leg capacity failed");
            }
        }
        if(passed==0 || failed==0)throw new Exception("Projected parity cases did not exercise both passing and rejected designs: "+passed+" / "+failed);
        Console.WriteLine("PASS projected crossing optimized/reference parity: "+passed+" passing, "+failed+" rejected, "+checks+" checked members; both materials and catalogue/custom scopes");
    }
}
