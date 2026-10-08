using System;
using System.Linq;
using EngineeringDashboard;

internal static class CrossEndStripTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Cross end strips: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<=1e-7*Math.Max(1,Math.Abs(expected)),message);
    static TowerInput Common()=>new TowerInput {LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,IgnoreUplift=true};
    static TransferMember[] Members(TowerLayout layout)=>layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers).ToArray();
    static void MemberChecks(TransferMember member,int denominator)
    {
        var independent=LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,TowerOptimizer.PairEI*member.H20Count/2,denominator);
        Near(member.Analysis.MomentKnM,independent.MomentKnM,"reported moment includes all applied loads");
        Near(member.Analysis.ShearKn,independent.ShearKn,"reported shear includes all applied loads");
        Near(member.Analysis.DeflectionMm,independent.DeflectionMm,"reported deflection includes all applied loads");
        Check(member.Analysis.DeflectionEvaluated && member.Analysis.MomentKnM<=member.MomentLimitKnM+1e-8 && member.Analysis.ShearKn<=member.ShearLimitKn+1e-8 && member.Analysis.DeflectionUtilization<=1+1e-8,"every accepted H20 passes moment, shear and deflection");
        Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"signed member force equilibrium");
        double applied=(member.Uniform??new BeamDistributedLoad[0]).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.StartMm+q.EndMm)/2000)+(member.Points??new BeamPointLoad[0]).Sum(p=>p.Kn*p.PositionMm/1000);
        Near(member.Analysis.Reactions.Zip(member.SupportsMm,(r,x)=>r*x/1000).Sum(),applied,"signed member moment equilibrium");
    }
    static void EndStripChecks(TowerLayout layout,double q,double span)
    {
        double edge=layout.ConcreteEndOverhangMm;
        Check(edge>=0 && edge<=75+1e-7 && layout.MaxLegKn<=layout.LegCapacityKn+1e-8,"entered end allowance and individual leg limit retained");
        Near(layout.Transfer.BeamLoadKn,q*span/1000,"physical concrete load includes both end strips");
        Near(layout.Transfer.TributaryWidthsMm.Sum(),span,"secondary tributaries retain full concrete span");
        var primary=layout.Transfer.PrimaryCases[0];
        Near(primary.Sum(m=>m.Analysis.TotalLoadKn),q*span/1000,"full cross load reaches primary pairs");
        Near(primary.Sum(m=>m.Analysis.Reactions.Sum()),q*span/1000,"full cross load reaches individual legs");
        Near(primary.Sum(m=>m.Uniform.Sum(u=>u.KnPerM*(u.EndMm-u.StartMm)/1000)),q*span/1000,"primary UDL retains full concrete load including end strips");
        Check(primary.All(m=>m.Points.Length==0),"all crossing primary design loads are UDLs without row point loads");
        Near(primary.Sum(m=>m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*(x+m.StartMm)/1000).Sum()),q*span/1000*span/2000,"end-strip transfer preserves full concrete moment about bay face");
        foreach(var member in Members(layout))MemberChecks(member,layout.DeflectionDenominator);
        if(edge<=1e-7)return;
        var rows=layout.Transfer.RowsMm;int last=rows.Length-1;
        double force=q*edge/2000;
        double leftSecond=force*(edge/2-rows[0])/(rows[1]-rows[0]);
        double rightLast=force*(span-edge/2-rows[last-1])/(rows[last]-rows[last-1]);
        var expected=new double[rows.Length];expected[0]+=force-leftSecond;expected[1]+=leftSecond;expected[last-1]+=force-rightLast;expected[last]+=rightLast;
        var influence=TransferredSlabOptimizer.ConcreteEndStripTributaries(span-2*edge,rows.Select(x=>x-edge).ToArray(),edge);
        Check(influence.Any(v=>v<0),"signed sheathing end-strip influence retained in secondary tributaries");
        for(int row=0;row<rows.Length;row++)Near(influence[row]*q/2000,expected[row],"secondary end-strip influence agrees with independent two-row statics");

    }
    static TransferNetworkInput Small(CrossBeamSupportMode mode=CrossBeamSupportMode.DedicatedTowers)=>new TransferNetworkInput {UpperSpacingMm=100,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=300,DepthMm=600}},Bays=new[] {new TransferBayRow {WidthMm=2677,ThicknessMm=100}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=400,DepthMm=150,BottomElevationMm=-1400,SupportMode=mode}}};
    public static void Run()
    {
        var rows=new[] {100.0,350,600,850};
        var widths=TransferredSlabOptimizer.ConcreteEndStripTributaries(950,rows,75);
        Near(widths.Sum(),150,"strip statics preserve force");Near(widths.Zip(rows,(w,x)=>w*x).Sum(),75*950,"strip statics preserve moment");
        Check(widths[0]>75 && widths[1]<0 && widths[2]<0 && widths[3]>75,"cantilever sheathing influence reactions are signed");
        double q=31.65*.65;
        foreach(double span in new[] {2677.0,2278,2272,2252})
        {
            var common=Common();common.LengthMm=span;
            common.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeConcreteEndStrips=true,BeamWidthMm=650,BeamLineLoadKnM=q,UpperSpacingMm=250};
            var result=TransferredSlabOptimizer.Optimize(common);EndStripChecks(result,q,span);
            Check(result.ConcreteEndOverhangMm>0,"non-25 mm span is supported using permitted end strips");
            Check(result.StockPlan.Pieces.All(p=>!p.IsCustomLength || p.StockLengthMm%25==0),"custom lengths remain multiples of 25 mm");
            // Replaying without caches/deferred construction must retain the same end-strip loads.
            common.ReuseSearchWork=false;common.SearchByLegTier=false;common.DeferTransferLayout=false;
            var reference=TransferredSlabOptimizer.Optimize(common);EndStripChecks(reference,q,span);
            Near(reference.MaxLegKn,result.MaxLegKn,"cached and reference end-strip leg loads agree");
            Near(reference.StockPlan.TotalStockLengthMm,result.StockPlan.TotalStockLengthMm,"cached and reference stock plans agree");
        }
        var strict=Common();strict.IgnoreUplift=false;strict.LengthMm=2677;strict.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeConcreteEndStrips=true,BeamWidthMm=650,BeamLineLoadKnM=q,UpperSpacingMm=250};
        var bearingOnly=TransferredSlabOptimizer.Optimize(strict);EndStripChecks(bearingOnly,q,2677);
        Check(Members(bearingOnly).All(m=>m.Analysis.Reactions.Min()>=-1e-7),"signed applied strip influences do not disable bearing-only acceptance");
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=1}};
        var supports=new[] {0.0,1000};
        var a=new TransferredSlabOptimizer.PrimaryKey(1000,supports,uniform,new[] {new BeamPointLoad {PositionMm=100,Kn=.1}});
        var b=new TransferredSlabOptimizer.PrimaryKey(1000,supports,uniform,new[] {new BeamPointLoad {PositionMm=100,Kn=.2}});
        Check(!a.Equals(b),"mixed-load response cache distinguishes end-strip forces");
        var commonNetwork=Common();commonNetwork.LengthMm=2450;commonNetwork.DeflectionDenominator=350;
        var dedicated=TransferNetworkOptimizer.Optimize(commonNetwork,Small());var cross=dedicated.CrossSupports.Single();
        Check(cross.Towers!=null && cross.Towers.ConcreteEndOverhangMm>0 && cross.Schedule.Contains("end strips"),"dedicated support retains end-strip detail in its schedule");
        EndStripChecks(cross.Towers,Small().AreaLoad(150)*.4,2677);
        foreach(var solid in cross.Solids)foreach(var existing in CrossBeamSupportOptimizer.NetworkSolids(dedicated))Check(!solid.Overlaps(existing),"end allowance does not bypass physical clashes");
        commonNetwork.MaximumConcreteEndOverhangMm=0;
        bool failed=false;
        try {TransferNetworkOptimizer.Optimize(commonNetwork,Small(CrossBeamSupportMode.Automatic));}
        catch(TowerOptimizationException e) {failed=true;Check(e.Message.Contains("X1") && e.Details.Contains("X1 dedicated") && e.Details.Contains("2677–2677") && !e.Details.Contains("Dedicated cross-beam stock/tower search failed."),"cross failure and cached attempts retain their own exact-coverage diagnosis");}
        Check(failed,"zero end allowance still rejects impossible exact stock coverage");
        var vm=new BeamCalculatorViewModel {UseBeamNetwork=true};vm.AddCrossingBeam();vm.NetworkBeams[0].BottomElevationMm=-1750;vm.CrossingBeams[0].BottomElevationMm=-1150;vm.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        var report=vm.BuildDiagnosticReport();Check(report.Contains("bottom=-1750 mm") && report.Contains("bottom=-1150 mm; supports=DedicatedTowers"),"diagnostics capture soffit elevations and requested cross support mode");
        var currentCommon=Common();currentCommon.LengthMm=10450;
        var currentInput=new TransferNetworkInput {UpperSpacingMm=275,Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=3550,ThicknessMm=300},new TransferBayRow {WidthMm=2575,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=2500,WidthMm=300,DepthMm=600,SupportMode=CrossBeamSupportMode.Automatic}}};
        var current=TransferNetworkOptimizer.Optimize(currentCommon,currentInput);var currentCross=current.CrossSupports.Single();
        Check(current.SeparateSlabCount==0 && currentCross.Towers!=null,"current three-beam report still returns complete slab transfer and its own crossing tower");
        EndStripChecks(currentCross.Towers,currentInput.AreaLoad(600)*.3,3550);
        foreach(var beam in current.Beams)foreach(var member in Members(beam))MemberChecks(member,270);
        Near(currentCross.LoadKn,18.79725,"current X1 UDL retains full concrete and construction load");
        Console.WriteLine($"CURRENT DRAWING REPORT: {current.LegCount} legs; peak {current.PeakLegKn:0.000} kN; X1 primary load model UDL.");
        Console.WriteLine($"Cross end strips: {checks} checks passed.");
    }
    public static void Reported()
    {
        var network=new TransferNetworkInput {UpperSpacingMm=275,Beams=Enumerable.Range(0,8).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),Bays=new[] {2677.0,1875,3275,2275,2278,2272,2252}.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray(),CrossingBeams=Enumerable.Range(1,7).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=3450,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.Automatic}).ToArray()};
        var timer=System.Diagnostics.Stopwatch.StartNew();bool failed=false;
        try {TransferNetworkOptimizer.Optimize(Common(),network);}
        catch(TowerOptimizationException e)
        {
            failed=true;
            Check(e.Message.Contains("Physical overlap:") && e.Message.Contains("X1"),"reported case identifies its actual cross timber clash");
            Check(!e.Message.Contains("leg load:") && !e.Details.Contains("2677–2677"),"reported case is no longer blocked by exact-span coverage or an unrelated main-leg example");
            Console.WriteLine($"REPORTED AUTOMATIC {timer.Elapsed.TotalSeconds:0.00}s; remaining geometry rejection: {e.Message}");
        }
        Check(failed,"reported coincident timber geometry remains rejected rather than bypassing clashes");
        Console.WriteLine($"Reported Automatic diagnosis: {checks} checks passed.");
    }
}
