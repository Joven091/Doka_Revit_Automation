using System;
using System.Linq;
using EngineeringDashboard;

internal static class LocalShutterTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Local shutter: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),message);
    internal static TransferNetworkResult Fixture()
    {
        var common=new TowerInput {LengthMm=4500,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,IgnoreUplift=true};
        var input=new TransferNetworkInput {UpperSpacingMm=250,Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=2175,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=2250,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.DedicatedTowers}}};
        return TransferNetworkOptimizer.Optimize(common,input);
    }
    public static void Run()
    {
        var result=Fixture();var crossing=result.CrossSupports.Single();
        // With 600 mm bays excluded, a 1000 mm bay needs at least 1300 mm stock.
        // 1800 mm is the next catalogue length, exceeding the standard 1775 mm slot.
        Check(2175-400<1800 && 2175-375==1800,"one local 25 mm reduction is sufficient in the physical fixture");
        Near(crossing.NearShutterClearanceMm,175,"only the necessary near allowance is reduced");Near(crossing.FarShutterClearanceMm,200,"the opposite end keeps the standard allowance");
        Check(crossing.HasReducedShutterClearance && crossing.Schedule.Contains("local side-shutter exception"),"exception is reported with the checked arrangement");
        Check(crossing.Towers.Standalone600Count==0,"exception does not bypass the narrow-tower exclusion");
        Check(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,crossing.Solids)!=null,"accepted physical stock cannot fit the standard allowance");
        Check(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,crossing.Solids,175,200)==null,"selected local allowances fit the full primary stock");
        foreach(var b in crossing.Bearings.Where(b=>b.IsShutterEndZone))
        {
            double width=b.EndZoneEndMm-b.EndZoneStartMm;
            Near(width,b.Beam==0 ? 175 : 200,"shutter solids and load strips use the local width");
            double face=result.BeamCentresMm[b.Beam]+(b.Beam==0 ? 400 : -400);
            Near(Math.Abs(b.Ymm-face),width/2,"shutter reaction acts at the selected strip midpoint");
            Near(b.ReactionKn,result.Input.AreaLoad(1150)*b.EndZoneTributaryWidthMm*width/1e6,"full local end-zone force is retained");
        }
        foreach(var layout in result.Beams.Concat(new[] {crossing.Towers}))
        {
            Check(layout.MaxLegKn<=70+1e-8,"all receiving and dedicated legs pass");
            foreach(var m in layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers))
            {
                Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8,"all construction-stage member checks pass");
                var independent=LoadedBeamAnalysis.AnalyzeSigned(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,m.EI,270);
                Near(m.Analysis.MomentKnM,independent.MomentKnM,"moment agrees with independent applied-load analysis");Near(m.Analysis.DeflectionMm,independent.DeflectionMm,"deflection agrees with independent analysis");
                Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"each member conserves force");
            }
        }
        var supported=crossing.Bearings.Where(b=>b.IsConcreteEndZone || b.IsDedicated && b.Label.StartsWith("Dedicated P")).ToArray();
        double load=result.Input.AreaLoad(1150)*.65*2.175;
        Near(supported.Sum(b=>b.ReactionKn),load,"full crossing load reaches the receiving and dedicated supports once");
        Near(supported.Sum(b=>b.ReactionKn*b.Xmm/1000),load*2.25,"whole-junction moment along the main beams is conserved");Near(supported.Sum(b=>b.ReactionKn*b.Ymm/1000),load*(.4+2.175/2),"whole-junction moment along the crossing is conserved");
        var existing=CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).ToArray();
        foreach(var solid in crossing.Solids)foreach(var other in existing)Check(!solid.Conflicts(other),"exception retains physical timber, concrete and tower clearance");
        // Ordinary slab shutter reactions still use 100 mm, despite the crossing exception.
        var baseline=result.Beams[0].Transfer.LowerCases.First(c=>c[0].LoadCase.Contains("right slab") && !c[0].LoadCase.Contains(" + X"));
        Check(baseline.Any(c=>c.Points.Any(p=>Math.Abs(p.PositionMm+c.StartMm-500)<1e-7)),"ordinary slab shutter offset remains 100 mm");
        Console.WriteLine($"Local shutters: {checks} checks passed; {result.LegCount} legs; {crossing.NearShutterClearanceMm}/{crossing.FarShutterClearanceMm} mm allowances.");
    }
}
