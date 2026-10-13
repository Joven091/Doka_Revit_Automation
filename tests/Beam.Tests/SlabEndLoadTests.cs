using System;
using System.Linq;
using EngineeringDashboard;

internal static class SlabEndLoadTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Slab end loads: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-7*Math.Max(1,Math.Abs(expected)),message+$" ({actual:G12} vs {expected:G12})");
    static TransferNetworkInput Network(CrossBeamSupportMode? mode=null,double position=3450)=>new TransferNetworkInput {
        UpperSpacingMm=250,
        Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},
        Bays=new[] {new TransferBayRow {WidthMm=2677,ThicknessMm=300},new TransferBayRow {WidthMm=1875,ThicknessMm=300}},
        CrossingBeams=mode.HasValue ? new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=position,WidthMm=650,DepthMm=1150,SupportMode=mode.Value}} : new TransferCrossingBeamRow[0]};
    static void Member(TransferMember m)
    {
        Check(m.Utilization<=1+1e-8 && m.Analysis.DeflectionEvaluated,"accepted members include end-load checks");
        Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"member force equilibrium");
        double moment=m.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.StartMm+q.EndMm)/2000)+m.Points.Sum(p=>p.Kn*p.PositionMm/1000);
        Near(m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*x/1000).Sum(),moment,"member moment equilibrium");
    }
    public static void Run()
    {
        // Reproduce the report's 125 mm concrete strips beyond shared timber.
        foreach(var mode in new CrossBeamSupportMode?[] {null,CrossBeamSupportMode.Automatic,CrossBeamSupportMode.LegacySlabRows})
        foreach(double phase in new[] {-50.0,0,50})
        {
            var n=Network(mode);var common=new TowerInput {LengthMm=6900};
            var actual=TransferNetworkOptimizer.EffectiveWidthsMm(common,n,0,6650,phase);
            var covered=TransferNetworkOptimizer.EffectiveWidthsMm(new TowerInput {LengthMm=6650},Network(mode,3325),0,6650,phase);
            Check(actual!=null && covered!=null,"full slab strips fit each tested row phase");
            int count=actual.Length;double first=(6650-(count-1)*250)/2.0+phase;
            var rows=Enumerable.Range(0,count).Select(i=>125+first+i*250).ToArray();
            var added=actual.Zip(covered,(a,b)=>a-b).ToArray();
            Near(added.Sum(),250,"both physical slab strips reach slab rows");
            Near(added.Zip(rows,(w,x)=>w*x).Sum(),250*3450,"end-strip resultant stays at the concrete centre");
            Check(added.Any(w=>w<0),"signed two-row sheathing influences retained");
            if(mode==CrossBeamSupportMode.Automatic)
                for(int i=0;i<count;i++)if(Math.Abs(rows[i]-3450)<365-1e-7)Near(actual[i],0,"crossing-excluded slab rows receive no end load");
        }
        Check(TransferNetworkOptimizer.EffectiveWidthsMm(new TowerInput {LengthMm=6900},Network(),0,6650,150)==null,
            "short support coverage cannot hide an excessive concrete-end distance");
        var sheetNetwork=Network();sheetNetwork.UpperSpacingMm=275;sheetNetwork.PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm;
        var sheetRows=PlywoodSpacing.SheetSupportRows(125,6775,275);
        var sheetWidths=TransferNetworkOptimizer.EffectiveWidthsMm(new TowerInput {LengthMm=6900},sheetNetwork,0,6650,0);
        Check(sheetWidths!=null,"sheet-joint rows support the full concrete ends");
        var sheetBaseline=Enumerable.Range(0,sheetRows.Length).Select(i=>(i==sheetRows.Length-1 ? 6775 : (sheetRows[i]+sheetRows[i+1])/2)-(i==0 ? 125 : (sheetRows[i-1]+sheetRows[i])/2)).ToArray();
        var sheetAdded=sheetWidths.Zip(sheetBaseline,(a,b)=>a-b).ToArray();
        Near(sheetWidths.Sum(),6900,"sheet layout does not shorten physical slab loading");
        Near(sheetAdded.Sum(),250,"sheet layout retains both concrete end strips");
        Near(sheetAdded.Zip(sheetRows,(w,x)=>w*x).Sum(),250*3450,"nonuniform sheet-joint rows retain the end-strip resultant");
        // The last 75 mm of the photographed primary must retain the slab baseline.
        var loads=TransferredSlabOptimizer.PrimaryUniformLoads(2950,3900,9.916,14.110,0,new BeamDistributedLoad[0],new BeamDistributedLoad[0],slabBaselineFactor:6900.0/6800);
        Check(loads.Length==1 && loads[0].StartMm==0 && loads[0].EndMm==3900,"slab load extends through the final 75 mm");
        Near(loads[0].KnPerM,9.916+14.110*6900/6800,"full-length slab reaction on primary tail");
        var exclusion=TransferredSlabOptimizer.PrimaryUniformLoads(50,6800,0,14.110,0,
            new[] {new BeamDistributedLoad {StartMm=2175,EndMm=2825,KnPerM=-1}},new BeamDistributedLoad[0],slabBaselineFactor:6900.0/6800);
        Near(exclusion.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),14.110*(6900-650)/1000,"crossing exclusion removes physical slab load exactly once");
        Near(exclusion.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(50+(q.StartMm+q.EndMm)/2)/1000),14.110*(6900.0*6900/2-650*2500)/1e6,"offset crossing exclusion preserves the full physical resultant");
        // Deliberately different shared and primary coverage, on both evaluation paths.
        foreach(bool sheet in new[] {false,true})
        foreach(bool deferred in new[] {false,true})
        {
            double spacing=sheet ? 275 : 200;
            var common=new TowerInput {LengthMm=2100,LegCapacityKn=70,DeflectionDenominator=270,MaximumConcreteEndOverhangMm=150,
                StockLengthsMm=new[] {1250.0,1800,2000,2450},DeferTransferLayout=deferred,ReuseSearchWork=deferred};
            var n=new TransferNetworkInput {UpperSpacingMm=spacing,PlywoodSheetLengthMm=sheet ? PlywoodSpacing.SheetLengthMm : 0,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=300,DepthMm=200}},Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100}}};
            var widths=TransferNetworkOptimizer.EffectiveWidthsMm(common,n,0,1900,0);
            common.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeMainBeamEndLoads=true,AllowLongerPrimaryCoverage=true,FixedCoveredLengthMm=1900,FixedRowPhaseMm=0,
                PlywoodSheetLengthMm=n.PlywoodSheetLengthMm,BeamWidthMm=300,BeamLineLoadKnM=n.AreaLoad(200)*.3,UpperSpacingMm=spacing,RightBayMm=1800,RightAreaLoadKnM2=n.AreaLoad(100),RightEffectiveWidthsMm=widths};
            var beam=TransferredSlabOptimizer.Optimize(common);var t=beam.Transfer;
            var actualRows=t.SlabRowIndices.Select(i=>t.RowsMm[i]).ToArray();
            double firstEnd=100*(actualRows[1]-50)/(actualRows[1]-actualRows[0]);
            int last=actualRows.Length-1;
            double lastEnd=100*(2050-actualRows[last-1])/(actualRows[last]-actualRows[last-1]);
            Near(t.UpperScale(false,t.SlabRowIndices[0]),(spacing+firstEnd)/1000,"first slab row checks the full end-strip influence");
            Near(t.UpperScale(false,t.SlabRowIndices.Last()),(spacing+lastEnd)/1000,"last slab row checks the full end-strip influence");
            Near(beam.StockPlan.Pieces.Sum(p=>p.StockLengthMm),2000,"primary extends beyond the shorter shared grid on both lines");
            Near(t.SlabLoadKn,n.AreaLoad(100)*1.8*2.1/2,"receiver takes half the complete slab and shutter load");
            Near(t.CoveredSlabAreaM2,2.1*t.RightUpperStockMm/1000,"reported load area includes longitudinal concrete ends");
            var stage=t.PrimaryCases[Array.IndexOf(t.CaseNames,"Beam + right slab")];
            Near(stage.Sum(m=>m.Analysis.TotalLoadKn),t.BeamLoadKn+t.SlabLoadKn,"full beam and slab force reaches primary checks");
            Near(stage.Sum(m=>m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*(x+m.StartMm)/1000).Sum()),(t.BeamLoadKn+t.SlabLoadKn)*1.05,"full primary resultant at physical midpoint");
            foreach(var m in t.PrimaryCases.Concat(t.LowerCases).SelectMany(c=>c).Concat(t.UpperMembers))Member(m);
            Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r<=70+1e-8),"each corrected tower reaction checked");
            foreach(int row in Enumerable.Range(0,t.RowsMm.Length).Except(t.SlabRowIndices))Near(t.UpperScale(false,row),0,"local beam-only rows acquire no slab feet");
        }
        Console.WriteLine($"Slab end loads: {checks} checks passed.");
    }
    public static void Reported()
    {
        var common=new TowerInput {LengthMm=6900,LegCapacityKn=70,DeflectionDenominator=270,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,
            AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,IgnoreUplift=true};
        var n=Network(CrossBeamSupportMode.Automatic);n.UpperSpacingMm=275;
        n.CrossingBeams=n.CrossingBeams.Concat(new[] {new TransferCrossingBeamRow {BayNumber=2,PositionMm=3450,WidthMm=650,DepthMm=1150}}).ToArray();
        var result=TransferNetworkOptimizer.Optimize(common,n);
        Check(result.SeparateSlabCount==0,"reported slabs and crossings all included");
        Near(result.SlabLoadKn,n.Bays.Sum(b=>n.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*(6900-650)/1000,"actual 6900 mm slabs conserved outside crossing footprints");
        foreach(var b in result.Beams)
        {
            var t=b.Transfer;string combined=ReferenceEquals(b,result.Beams[0]) ? "Beam + right slab" : ReferenceEquals(b,result.Beams.Last()) ? "Beam + left slab" : "Beam + both slabs";int stage=Array.IndexOf(t.CaseNames,combined);
            Near(t.PrimaryCases[stage].Sum(m=>m.Analysis.TotalLoadKn),t.BeamLoadKn+t.SlabLoadKn,"complete baseline slab force reaches each primary pair");
            foreach(var m in t.PrimaryCases.Concat(t.LowerCases).SelectMany(c=>c).Concat(t.UpperMembers))Member(m);
            Check(b.LeftReactions.Concat(b.RightReactions).All(r=>r<=70+1e-8),"reported corrected legs pass");
        }
        Console.WriteLine($"Slab end loads reported: {checks} checks; {result.LegCount} legs; slab {result.SlabLoadKn:0.000} kN; peak {result.PeakLegKn:0.000} kN.");
        Console.WriteLine(result.LayoutDiagnostics);
    }
}
