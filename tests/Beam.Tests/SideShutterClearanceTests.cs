using System;
using System.Linq;
using EngineeringDashboard;

internal static class SideShutterClearanceTests
{
    public static void Run()
    {
        var result=new TransferNetworkResult {BeamCentresMm=new[] {0.0,3300},Input=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=600},new TransferBeamRow {WidthMm=600}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1}}}};
        var timber=new FormworkSolid {Y0=500,Y1=2800,Member=new TransferMember {H20Count=2}};
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber})!=null)throw new Exception("Exactly 200 mm primary-end clearance must pass.");
        foreach(bool near in new[] {true,false})
        {
            timber.Y0=near ? 499.99 : 500;timber.Y1=near ? 2800 : 2800.01;
            var failure=CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber});
            if(failure==null || !failure.Contains("side shutters"))throw new Exception("Full physical primary stock, including unloaded tails, must retain the shutter gap.");
        }
        timber.Member.H20Count=1;
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber})!=null)throw new Exception("Primary-end clearance must not remove receiving secondary support.");
        timber.Member.H20Count=2;timber.Y0=475;timber.Y1=2800;
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber},175,200)!=null)throw new Exception("Exactly 175 mm local clearance must pass at the selected end.");
        timber.Y0=474.99;
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber},175,200)==null)throw new Exception("Even the exception must check the full stock end at its exact boundary.");
        timber.Y0=475;timber.Y1=2800.01;
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber},175,200)==null)throw new Exception("A local near exception must not relax the opposite end.");
        var input=new TransferNetworkInput {UpperSpacingMm=250,Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=650,DepthMm=1150}},Bays=new[] {new TransferBayRow {WidthMm=3550,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3375,WidthMm=650,DepthMm=1150}}};
        var common=new TowerInput {LengthMm=6750,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,IgnoreUplift=true};
        var solved=TransferNetworkOptimizer.Optimize(common,input);var crossing=solved.CrossSupports.Single();
        if(crossing.Towers==null || CrossBeamSupportOptimizer.PrimaryShutterClash(solved,0,crossing.Solids)!=null)throw new Exception("A coordinated matching-soffit junction must return checked dedicated towers with the shutter gap.");
        if(crossing.HasReducedShutterClearance)throw new Exception("A standard passing junction must retain both 200 mm allowances.");
        foreach(var layout in solved.Beams.Concat(new[] {crossing.Towers}))
        {
            if(layout.MaxLegKn>70+1e-8)throw new Exception("Shutter clearance must retain individual leg checks.");
            foreach(var member in layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers))
            {
                if(!member.Analysis.DeflectionEvaluated || member.Utilization>1+1e-8)throw new Exception("Shutter clearance must retain every H20 strength and deflection check.");
                var independent=LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,TowerOptimizer.PairEI*member.H20Count/2,270);
                if(Math.Abs(independent.MomentKnM-member.Analysis.MomentKnM)>1e-7 || Math.Abs(independent.ShearKn-member.Analysis.ShearKn)>1e-7 || Math.Abs(independent.DeflectionUtilization-member.Analysis.DeflectionUtilization)>1e-7)throw new Exception("Accepted H20 demands must agree with independent signed analysis.");
            }
        }
        var primary=crossing.Solids.Where(s=>s.Member?.H20Count==2).ToArray();
        double nearGap=primary.Min(s=>s.Y0)-325,farGap=solved.BeamCentresMm[1]-325-primary.Max(s=>s.Y1);
        if(nearGap<200-1e-7 || farGap<200-1e-7)throw new Exception("Physical primary endpoints must independently retain both 200 mm gaps.");
        double expected=input.AreaLoad(1150)*.65*3.55;
        double supported=crossing.Bearings.Where(b=>b.IsConcreteEndZone || b.IsDedicated && b.Label.StartsWith("Dedicated P")).Sum(b=>b.ReactionKn);
        if(Math.Abs(supported-expected)>1e-7)throw new Exception("The shutter gap must not lose any crossing concrete load.");
        Console.WriteLine($"Side shutter clearance: {solved.LegCount} checked legs; peak {solved.PeakLegKn:0.###}/70 kN; primary gaps {nearGap:0.###}/{farGap:0.###} mm; exact 200 mm boundary and both stock ends checked.");
    }
}
