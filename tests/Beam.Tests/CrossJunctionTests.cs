using System;
using System.Linq;
using EngineeringDashboard;

internal static class CrossJunctionTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Cross junction: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),message+$" ({a:G12} vs {b:G12})");
    static TowerInput Common()=>new TowerInput {LengthMm=10800,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,IgnoreUplift=true};
    static TransferNetworkInput Input()=>new TransferNetworkInput {UpperSpacingMm=275,Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=2575,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=5375,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.Automatic}}};
    static void Member(TransferMember m)
    {
        Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8,"every complete-stage H20 passes strength and deflection");
        var a=LoadedBeamAnalysis.AnalyzeSigned(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,TowerOptimizer.PairEI*m.H20Count/2,270);
        Near(a.MomentKnM,m.Analysis.MomentKnM,"moment uses complete applied loads");Near(a.ShearKn,m.Analysis.ShearKn,"shear uses complete applied loads");Near(a.DeflectionUtilization,m.Analysis.DeflectionUtilization,"deflection uses complete applied loads");
        Near(a.Reactions.Sum(),a.TotalLoadKn,"signed member force equilibrium");
        double moment=(m.Uniform??new BeamDistributedLoad[0]).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.StartMm+q.EndMm)/2000)+(m.Points??new BeamPointLoad[0]).Sum(p=>p.Kn*p.PositionMm/1000);
        Near(a.Reactions.Zip(m.SupportsMm,(r,x)=>r*x/1000).Sum(),moment,"member moment equilibrium");
    }
    internal static TransferNetworkResult Reported()
    {
        var input=Input();var clock=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(Common(),input);var cross=result.CrossSupports.Single();
        Check(result.SeparateSlabCount==0 && cross.Towers!=null && cross.Bearings.Any(b=>b.IsConcreteEndZone),"reported case returns a full slab transfer and central cross tower with checked end zones");
        Check(result.Beams.All(l=>l.MaxLegKn<=70+1e-8) && cross.Towers.MaxLegKn<=70+1e-8,"every individual leg remains below its entered capacity");
        foreach(var l in result.Beams.Concat(new[] {cross.Towers}))foreach(var m in l.Transfer.PrimaryCases.Concat(l.Transfer.LowerCases).SelectMany(c=>c).Concat(l.Transfer.UpperMembers))Member(m);
        var solids=cross.Solids;var existing=CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).ToArray();
        foreach(var s in solids) {foreach(var other in existing)Check(!s.Conflicts(other),"complete cross timber and legs clear existing timber, legs and concrete");foreach(var other in solids.Where(v=>!ReferenceEquals(v,s)))Check(!s.Conflicts(other),"cross members clear one another");}
        double expected=input.AreaLoad(1150)*.65*2.575;Near(cross.LoadKn,expected,"full crossing concrete and allowances conserved");
        var reactions=cross.Bearings.Where(b=>b.IsConcreteEndZone || b.IsDedicated && b.Label.StartsWith("Dedicated P")).ToArray();
        Near(reactions.Sum(b=>b.ReactionKn),expected,"full cross load reaches receiving supports and dedicated legs exactly once");
        Near(reactions.Sum(b=>b.ReactionKn*b.Xmm/1000),expected*5.375,"eccentric tower placement preserves global moment along the main beams");
        Near(reactions.Sum(b=>b.ReactionKn*b.Ymm/1000),expected*(.325+2.575/2),"junction split preserves global moment along the crossing span");
        foreach(var beam in Enumerable.Range(0,2))
        {
            var t=result.Beams[beam].Transfer;var stage=Array.FindLastIndex(t.CaseNames,s=>s.Contains(" + X1"));
            if(stage<0)continue;string baseline=t.CaseNames[stage].Split(new[] {" + X1"},StringSplitOptions.None)[0];int baseStage=Array.IndexOf(t.CaseNames,baseline);
            Near(t.PrimaryCases[stage].Sum(m=>m.Analysis.TotalLoadKn)-t.PrimaryCases[baseStage].Sum(m=>m.Analysis.TotalLoadKn),cross.Bearings.Where(b=>b.IsConcreteEndZone && b.Beam==beam).Sum(b=>b.ReactionKn),"end-zone load reaches the receiving primary pairs without clipping or duplication");
        }
        Check(cross.Schedule.Contains("end zone") && cross.Schedule.Contains("eccentric concrete loads"),"schedule explains the actual junction and offset load path");
        Console.WriteLine($"REPORTED JUNCTION PASS: {result.LegCount} legs, peak {result.PeakLegKn:0.000} kN; {clock.Elapsed.TotalSeconds:0.00}s; {checks} checks.");
        return result;
    }
    public static void Run()
    {
        var widths=CrossBeamSupportOptimizer.EndZoneWidths(0,1000,new[] {600.0,900});Near(widths.Sum(),1000,"end-zone sheathing influence preserves force");Near(widths.Zip(new[] {600.0,900},(w,x)=>w*x).Sum(),500000,"end-zone sheathing influence preserves first moment");Check(widths[1]<0,"signed end-zone influence is retained");
        var edit=new TransferBeamRow {DepthMm=500,BottomElevationMm=-500};edit.SetDepthKeepingTopMm(1150);Near(edit.BottomElevationMm,-1150,"depth edit preserves concrete top after saved soffit has become explicit");edit.BottomElevationMm=-1050;edit.SetDepthKeepingTopMm(1750);Near(edit.BottomElevationMm,-1650,"depth edit preserves an intentionally raised top level");
        var crossEdit=new TransferCrossingBeamRow {DepthMm=500,BottomElevationMm=-500};crossEdit.SetDepthKeepingTopMm(1150);Near(crossEdit.BottomElevationMm,-1150,"cross depth edit also preserves the concrete top");
        Reported();
        var wrong=Input();foreach(var b in wrong.Beams)b.BottomElevationMm=-500;
        bool failed=false;try {TransferNetworkOptimizer.Optimize(Common(),wrong);}catch(TowerOptimizationException e) {failed=true;Check(e.Details.Contains("cross concrete") && e.Details.Contains("B1 bottom -500, top 650"),"incorrect elevations retain a concrete-clash diagnosis and actual levels");}Check(failed,"solver never silently repairs incorrect geometry");
        var shift=Common();shift.LengthMm=2000;shift.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeConcreteEndStrips=true,FixedTowerWidthMm=2000,BeamCentreOffsetMm=-25,BeamWidthMm=650,BeamLineLoadKnM=20.5725,UpperSpacingMm=250};
        var shifted=TransferredSlabOptimizer.Optimize(shift);foreach(var m in shifted.Transfer.PrimaryCases.Concat(shifted.Transfer.LowerCases).SelectMany(c=>c))Member(m);
        Check(shifted.LeftReactions.Sum()>shifted.RightReactions.Sum(),"offset concrete produces an unequal reaction split rather than assumed equal sharing");
        Console.WriteLine($"Cross junction: {checks} checks passed.");
    }
}
