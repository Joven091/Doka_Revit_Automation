using System;
using System.Linq;
using EngineeringDashboard;

internal static class TransferTests
{
    static int checks;
    static void Check(bool value,string text) { checks++; if (!value) throw new Exception("Transfer: "+text); }
    static void Near(double a,double b,string text) => Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(b)),text);
    public static void Run()
    {
        var input=new TowerInput {LengthMm=6750,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=350,
            TransferredSlab=new TransferredSlabInput {BeamWidthMm=1100,BeamLineLoadKnM=50,UpperSpacingMm=275,LeftBayMm=3900,LeftAreaLoadKnM2=9.4}};
        var layout=TowerOptimizer.Optimize(input); var t=layout.Transfer;
        Check(t!=null && t.RowsMm.Length>0,"transfer result");
        Check(t.LeftFootMm>=250 && t.RightFootMm==0,"clearance measured from concrete face to foot centre");
        Check(t.LeftUpperEdgeMm>=200 && t.LeftUpperEdgeMm<=350,"slab edge coverage");
        Check(layout.LegCount==layout.PositionsMm.Length*2 && layout.MaxLegKn<=70,"individual permissible leg checks");
        Near(t.TributaryWidthsMm.Sum(),input.LengthMm-2*layout.ConcreteEndOverhangMm,"closing tributary strips");
        Near(t.SlabLoadKn,9.4*t.LeftUpperStockMm/1000*t.TributaryWidthsMm.Sum()/1000/2,"only covered slab load reaches receiving beam");
        for(int c=0;c<t.CaseNames.Length;c++)
        {
            var primary=t.PrimaryCases[c]; var lower=t.LowerCases[c];
            Near(primary.Sum(m=>m.Analysis.TotalLoadKn),lower.Sum(m=>m.Analysis.TotalLoadKn),"downward force conservation");
            foreach(var m in primary.Concat(lower).Concat(t.UpperMembers))
            {
                Check(m.SupportsMm.Length>=2,"at least two supports per stock");
                Check(m.Analysis.Reactions.All(x=>x>=-1e-7),"no uplift at any interface");
                Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"member equilibrium");
                double loadMoment=m.Points.Sum(p=>p.Kn*p.PositionMm/1000)+m.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.EndMm+q.StartMm)/2000);
                Near(m.Analysis.Reactions.Select((r,i)=>r*m.SupportsMm[i]/1000).Sum(),loadMoment,"independent moment equilibrium");
                Check(m.Utilization<=1+1e-8,"paired H20 shear/moment/deflection");
            }
        }
        Check(TowerBeamAnalysis.Finite(t.SlabMovementMm) && t.SlabMovementMm>0,"soffit movement includes lower formwork displacement");
        Check(t.PrimaryCases.Last().Any(m=>m.Points.Length>0) && t.PrimaryCases.Last().All(m=>m.Uniform.Length==0),"primaries use discrete transverse reactions");
        foreach(bool joined in new[] {false,true}) Check(layout.Joined.Contains(joined),"independent gaps and manufactured tower bays");
        for(int i=0;i<layout.Joined.Length;i++) if(layout.Joined[i])
            Check(layout.Rotated ? Math.Abs(layout.PositionsMm[i+1]-layout.PositionsMm[i]-1524)<1e-7 : TowerOptimizer.BaysMm.Any(b=>Math.Abs(b-layout.PositionsMm[i+1]+layout.PositionsMm[i])<1e-7),"manufactured geometry");
        Near(t.UpperMembers[0].Analysis.DeflectionWithOffsetMm(0),t.UpperMembers[0].Analysis.DeflectionMm,"exact displacement envelope at zero settlement");
        var simple=LoadedBeamAnalysis.Analyze(2000,new[] {0.0,2000},new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=10}},null,900,350);
        Near(simple.DeflectionWithOffsetMm(-2),2+5.0*10*16/384/900*1000,"closed-form support-settlement displacement");
        var point=LoadedBeamAnalysis.Analyze(2000,new[] {0.0,2000},null,new[] {new BeamPointLoad {PositionMm=1000,Kn=10}},900,350);
        Near(point.Response(1000,false)[0]-point.Response(1000,true)[0],10,"point-load shear jump");
        Console.WriteLine($"Transfer sample: {layout.LegCount} legs, width {layout.WidthMm}, {t.LeftUpperStockMm} mm upper, {t.LowerStockLengthMm} mm transverse, peak {layout.MaxLegKn:0.00} kN, movement {t.SlabMovementMm:0.00} mm");
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasLeftSlab=true,Depth=1750,Width=1100,BeamLengthMm=6750,TowerLegCapacityKn=70,TransferSlabLoads=true,UpperSupportSpacingMm=275};
        vm.ValidateTowerInputs(); Check(vm.IsTransferMode && !vm.ShowSlabStrip,"mode selection");
        vm.HasLeftSlab=false; Check(!vm.IsTransferMode,"no selected slab restores beam-only mode");
        input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=1100,BeamLineLoadKnM=50,UpperSpacingMm=275,RightBayMm=3900,RightAreaLoadKnM2=9.4};
        var mirror=TowerOptimizer.Optimize(input);
        Check(mirror.Transfer.LeftFootMm==0 && mirror.Transfer.RightFootMm>=250,"mirrored right bay");
        // Search tie-breaks may choose different phases, so check the force transfer independently rather than requiring identical geometry.
        Near(mirror.Transfer.SlabLoadKn,9.4*mirror.Transfer.RightUpperStockMm/1000*mirror.Transfer.TributaryWidthsMm.Sum()/2000,"right covered-area reaction");
        input.TransferredSlab.LeftBayMm=3900;input.TransferredSlab.LeftAreaLoadKnM2=9.4;input.Exclude600Standalone=true;
        var both=TowerOptimizer.Optimize(input);Check(both.Transfer.CaseNames.Length==4,"beam, each one-sided stage and combined load");
        Check(both.Transfer.LowerCases.All(c=>c.All(m=>m.Analysis.Reactions.All(r=>r>=-1e-7))),"two-sided cases reject bearing uplift");
        for(int i=0;i<both.Joined.Length;i++) if(both.Joined[i] && Math.Abs(both.PositionsMm[i+1]-both.PositionsMm[i]-600)<1e-7)
            Check(i>0 && both.Joined[i-1] || i+1<both.Joined.Length && both.Joined[i+1],"600 bay is joined when standalone excluded");
        input.TransferredSlab.UpperSpacingMm=double.NaN;
        try {TowerOptimizer.Optimize(input);throw new Exception("accepted invalid transfer spacing");} catch(ArgumentException) {checks++;}
        Console.WriteLine($"Transfer: {checks} checks passed");
    }
}
