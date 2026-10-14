using System;
using System.Linq;
using EngineeringDashboard;

internal static class ColumnSlabEndSupportTests
{
    static int checks;
    static void Check(bool ok,string message) {checks++;if(!ok)throw new Exception("Column slab ends: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),message+$" ({a:R} vs {b:R})");
    static double Force(BeamDistributedLoad[] loads)=>loads.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000);
    static void Member(TransferMember m)
    {
        Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8,"complete member checks retained");
        Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"signed member force equilibrium");
        double moment=m.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.StartMm+q.EndMm)/2000)+m.Points.Sum(p=>p.Kn*p.PositionMm/1000);
        Near(m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*x/1000).Sum(),moment,"signed member moment equilibrium");
    }
    internal static void Run()
    {
        foreach(bool sheet in new[] {false,true})foreach(double phase in new[] {-10.0,0,10})foreach(double pitch in new[] {250.0,275,305,350,407,488})
        {
            var n=new TransferNetworkInput {UpperSpacingMm=pitch,PlywoodSheetLengthMm=sheet ? 2440 : 0,
                Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=400},new TransferBeamRow {WidthMm=300,DepthMm=400}},
                Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100}}};
            var widths=TransferNetworkOptimizer.EffectiveWidthsMm(new TowerInput {LengthMm=6900},n,0,6650,phase);
            Near(widths.Sum(),6650,"slab rows receive supported coverage, not longitudinal external strips");
            // Sheet joints stay anchored to physical x=0; translating the whole
            // concrete length would change their in-coverage tributaries.
            var supported=sheet ? PlywoodSpacing.RowTributaries(125,6775,PlywoodSpacing.SheetSupportRows(125,6775,pitch,2440))
                : TransferNetworkOptimizer.EffectiveWidthsMm(new TowerInput {LengthMm=6650},n,0,6650,phase);
            Check(widths.Zip(supported,(a,b)=>a-b).All(w=>Math.Abs(w)<1e-8),"external strips add nothing at any nominal pitch; real in-coverage end tributaries remain");
        }
        // Negative baseline exclusions cannot remove slab that was already sent
        // to columns. Genuine crossing concrete is retained over its footprint.
        var excluded=TransferredSlabOptimizer.PrimaryUniformLoads(50,2000,2,3,0,new[] {new BeamDistributedLoad {StartMm=0,EndMm=200,KnPerM=-1}},null,true,100,2000);
        Near(Force(excluded),2*2.0+3*1.8,"crossing exclusion intersects supported slab exactly once");
        var crossing=TransferredSlabOptimizer.PrimaryUniformLoads(50,2000,2,3,0,new[] {new BeamDistributedLoad {StartMm=30,EndMm=130,KnPerM=.5}},null,true,100,2000);
        Near(Force(crossing),2*2.0+3*1.9+3*.5*.08,"positive crossing loads survive outside slab coverage");
        foreach(bool deferred in new[] {false,true})foreach(double primaryStock in new[] {2000.0,2100})
        {
            var input=new TowerInput {LengthMm=2100,LegCapacityKn=70,DeflectionDenominator=270,MaximumConcreteEndOverhangMm=150,
                StockLengthsMm=new[] {1250.0,1800,primaryStock,2450},DeferTransferLayout=deferred,ReuseSearchWork=deferred};
            input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeMainBeamEndLoads=true,AllowLongerPrimaryCoverage=true,
                FixedCoveredLengthMm=1900,FixedRowPhaseMm=0,BeamWidthMm=300,BeamLineLoadKnM=1.5,UpperSpacingMm=275,PlywoodSheetLengthMm=2440,
                RightBayMm=1800,RightAreaLoadKnM2=2.5};
            var layout=TransferredSlabOptimizer.Optimize(input);var t=layout.Transfer;
            Near(layout.StockPlan.Pieces.Sum(p=>p.StockLengthMm),primaryStock,"primary and slab coverage differ intentionally");
            foreach(int row in t.SlabRowIndices)Near(t.UpperScale(false,row),.275,"slab design pitch has no external end-strip increment");
            int stage=Array.IndexOf(t.CaseNames,"Beam + right slab");
            double beamTotal=1.5*2.1,slabTotal=2.5*1.8/2*2.1;
            double beamExternal=1.5*(2100-primaryStock)/1000,slabExternal=2.5*1.8/2*.2;
            Near(t.BeamLoadKn,beamTotal,"beam reference retains full physical force");
            Near(t.SlabLoadKn,slabTotal,"slab reference retains loads assigned to columns");
            Near(t.CoveredSlabAreaM2,1.9*t.RightUpperStockMm/1000,"H20 covered area excludes column-supported strips");
            Near(t.ColumnSupportLoadKn(stage),beamExternal+slabExternal,"external main/slab strips assigned once, without overlap duplication");
            Near(t.PrimaryCases[stage].Sum(m=>m.Analysis.TotalLoadKn)+t.ColumnSupportLoadKn(stage),beamTotal+slabTotal,"all physical force retained");
            double memberMoment=t.PrimaryCases[stage].Sum(m=>m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*(x+m.StartMm)/1000).Sum());
            Near(memberMoment+t.ColumnSupportLoads[stage].Sum(x=>x.TotalMomentKnM),(beamTotal+slabTotal)*1.05,"all physical longitudinal moment retained");
            Near(t.ColumnSupportLoadKn(0),beamExternal,"beam-only stage excludes slab assignment");
            Check(t.ColumnSupportNotice.Contains("assumed") && t.ColumnSupportNotice.Contains("capacity requires separate verification"),"external support status explicitly unverified");
            foreach(var member in t.PrimaryCases.Concat(t.LowerCases).SelectMany(c=>c).Concat(t.UpperMembers))Member(member);
            Check(layout.MaxLegKn<=70+1e-8 && layout.IgnoreUplift,"leg limits and current uplift policy retained");
            foreach(int row in t.SlabRowIndices)for(int c=0;c<t.CaseNames.Length;c++)
            {
                var equivalent=EquivalentTransferredLoading.Create(layout,row,c);
                Check(equivalent.IsAvailable,"real solver unit sources reproduce their verified primary UDL");
                for(int line=0;line<2;line++)Near(equivalent.Member.Analysis.Reactions[line]/(t.EnteredUpperSpacingMm/1000),equivalent.PrimaryTransferKnM[line],"real model reference invariant");
            }
        }
        Console.WriteLine($"Passed {checks} column slab end-support checks.");
    }
}
