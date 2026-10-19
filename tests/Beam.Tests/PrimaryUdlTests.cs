using System;
using System.Linq;
using EngineeringDashboard;

internal static class PrimaryUdlTests
{
    static int checks;
    static void Check(bool condition,string message) {checks++;if(!condition)throw new Exception("Primary UDL: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<=1e-8*Math.Max(1,Math.Abs(b)),message);
    static void CheckLayout(TowerLayout beam)
    {
        foreach(var stage in beam.Transfer.PrimaryCases)
        foreach(var member in stage)
        {
            Check(member.Points.Length==0 && member.Uniform.Length>0,"no secondary point loads in primary design cases");
            var independent=LoadedBeamAnalysis.Analyze(member.LengthMm,member.SupportsMm,member.Uniform,null,TowerOptimizer.PairEI,beam.DeflectionDenominator);
            Near(independent.MomentKnM,member.Analysis.MomentKnM,"independent UDL moment");
            Near(independent.ShearKn,member.Analysis.ShearKn,"independent UDL shear");
            Near(independent.DeflectionMm,member.Analysis.DeflectionMm,"independent UDL deflection");
            Near(member.Analysis.TotalLoadKn,member.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),"distributed load conservation");
            Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"UDL force equilibrium");
            Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8,"all displayed UDL checks pass");
            Check(member.Analysis.Reactions.All(r=>r>=-1e-7 && r<=beam.LegCapacityKn+1e-8),"UDL tower reactions pass");
        }
        Near(beam.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn)+beam.Transfer.ColumnSupportLoadKn(0),beam.Transfer.BeamLoadKn,"beam-only primary UDL preserves covered beam load");
        Check(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)+beam.Transfer.ColumnSupportLoadKn(beam.Transfer.CaseNames.Length-1)>=beam.Transfer.BeamLoadKn+beam.Transfer.SlabLoadKn-1e-7,"full-tributary primary design covers physical beam and slab reference load");
        for(int row=0;row<beam.Transfer.RowsMm.Length;row++)
            Check(beam.Transfer.UpperScale(false,row)>=beam.Transfer.SlabSpacing(false)/1000,"upper slab foot retains full end-row tributary");
        foreach(var member in beam.Transfer.PrimaryCases.SelectMany(c=>c))
            Check(member.Uniform.All(q=>q.StartMm>=0 && q.EndMm<=member.LengthMm),"distributed loads clipped to independent stock");
    }
    static void CheckNetworkLoads(TransferNetworkResult result)
    {
        var network=result.Input;
        for(int b=0;b<result.Beams.Length;b++)
        {
            var beam=result.Beams[b];var t=beam.Transfer;
            var lower=t.LowerCases[0][0];
            double x1=lower.StartMm+lower.SupportsMm[0],x2=lower.StartMm+lower.SupportsMm[1];
            double beamQ=network.AreaLoad(network.Beams[b].DepthMm)*network.Beams[b].WidthMm/1000;
            // Independent static equilibrium for the transverse loads per metre.
            Func<bool,double[]> slabReaction=left=>
            {
                int bay=left ? b-1 : b;
                if(bay<0 || bay>=network.Bays.Length || result.Bays[bay]==null)return new[] {0.0,0.0};
                var upper=t.UpperMembers.Single(m=>m.Name==(left ? "Upper left" : "Upper right"));
                double scale=(left ? t.LeftUpperScales : t.RightUpperScales).Max();
                double foot=upper.Analysis.Reactions[0]/scale;
                double shutter=network.AreaLoad(network.Bays[bay].ThicknessMm)*(left ? t.LeftUpperEdgeMm : t.RightUpperEdgeMm)/1000;
                double sign=left ? -1 : 1,half=network.Beams[b].WidthMm/2;
                double moment=foot*sign*(half+(left ? t.LeftFootMm : t.RightFootMm))+shutter*sign*(half+100);
                double r2=(moment-(foot+shutter)*x1)/(x2-x1);
                return new[] {foot+shutter-r2,r2};
            };
            var l=slabReaction(true);var r=slabReaction(false);
            double beamR2=-beamQ*x1/(x2-x1);
            int runs=beam.StockPlan.Pieces.Length;
            for(int c=0;c<t.CaseNames.Length;c++)
            {
                bool left=t.CaseNames[c].Contains("left") || t.CaseNames[c].Contains("both");
                bool right=t.CaseNames[c].Contains("right") || t.CaseNames[c].Contains("both");
                for(int i=0;i<t.PrimaryCases[c].Length;i++)
                {
                    int line=i/runs;var member=t.PrimaryCases[c][i];
                    foreach(var band in member.Uniform)
                    {
                        double station=member.StartMm+(band.StartMm+band.EndMm)/2;
                        double fullBeamFactor=1;
                        double expected=((line==0 ? beamQ-beamR2 : beamR2)+(left ? l[line] : 0)+(right ? r[line] : 0))*fullBeamFactor;
                        foreach(var crossing in network.CrossingBeams)
                        {
                            bool active=crossing.BayNumber==b && left || crossing.BayNumber==b+1 && right;
                            if(!active || station<crossing.PositionMm-crossing.WidthMm/2 || station>crossing.PositionMm+crossing.WidthMm/2)continue;
                            double slab=network.AreaLoad(network.Bays[crossing.BayNumber-1].ThicknessMm);
                            expected+=(crossing.BayNumber==b ? l[line] : r[line])*(network.AreaLoad(crossing.DepthMm)-slab)/slab;
                        }
                        Near(band.KnPerM,expected,"optimizer UDL equals independent transverse reaction per metre in each stage and footprint");
                    }
                }
            }
            foreach(var member in t.LowerCases[0])
            {
                int row=Array.IndexOf(t.LowerCases[0],member);
                Near(member.Analysis.TotalLoadKn,beamQ*Math.Max(t.TributaryWidthsMm[row],network.UpperSpacingMm)/1000,"lower beam-concrete end row uses full spacing");
            }
        }
    }
    public static void Run()
    {
        var empty=new BeamDistributedLoad[0];
        var endScales=TransferredSlabOptimizer.FullTributaryScales(new[] {125.0,275,125},new[] {225.0,375,125},275);
        Near(endScales[0],.375,"full baseline end spacing retains the entire crossing increment");
        Near(endScales[1],.375,"interior crossing increment is retained once");
        Near(endScales[2],.275,"unloaded end row retains full spacing");
        var stock=TransferredSlabOptimizer.PrimaryUniformLoads(0,2475,20,7,4.572,empty,empty);
        Check(stock.Length==1 && stock[0].StartMm==0 && stock[0].EndMm==2475,"uniform beam/slab loads cover complete primary stock");
        Near(stock[0].KnPerM,31.572,"intensity comes from per-metre reactions");
        // Reproduce the photographed slab-to-secondary-to-primary load path independently.
        var slab=LoadedBeamAnalysis.Analyze(2000,new[] {150.0,1850},new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=9.4*.275}},null,TowerOptimizer.PairEI/2,270);
        Near(slab.Reactions[0],2.585,"full-tributary slab reaction transferred to end secondary");
        var secondary=LoadedBeamAnalysis.Analyze(1950,new[] {150.0,1650},new[] {new BeamDistributedLoad {StartMm=500,EndMm=1300,KnPerM=46.65*.275}},new[] {new BeamPointLoad {PositionMm=1788,Kn=slab.Reactions[0]},new BeamPointLoad {PositionMm=1400,Kn=9.4*.338*.275}},TowerOptimizer.PairEI/2,270);
        Near(secondary.Reactions[1],8.682428333333334,"full-tributary lower right reaction");
        Near(secondary.Reactions[1]/.275,31.572466666666667,"primary UDL is secondary reaction divided by spacing in metres");
        var supports=new[] {475.5,1999.5};
        var failed=LoadedBeamAnalysis.AnalyzeUniformCached(2475,supports,stock,TowerOptimizer.PairEI,270,new LoadedBeamAnalysis.MatrixCache(),70,true);
        Near(failed.ShearKn,24.057864,"reported 1524 mm span fails the UDL shear check");
        Check(failed.ShearKn>22 && !failed.DeflectionEvaluated,"failed shear can reject before deflection evaluation");
        var points=Enumerable.Range(0,9).Select(i=>new BeamPointLoad {PositionMm=137.5+275*i,Kn=31.572*2.475/9}).ToArray();
        var former=LoadedBeamAnalysis.Analyze(2475,supports,null,points,TowerOptimizer.PairEI,270);
        Check(former.ShearKn<22,"even secondary point loads can pass the arrangement rejected by UDL");
        Near(former.TotalLoadKn,failed.TotalLoadKn,"point and UDL examples have identical total loads");
        var cache=new LoadedBeamAnalysis.MatrixCache();
        var combinedSupports=new[] {475.5,1237.5,1999.5};
        var first=LoadedBeamAnalysis.AnalyzeUniformCached(2475,combinedSupports,stock,TowerOptimizer.PairEI,270,cache);
        var half=stock.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM/2}).ToArray();
        var second=LoadedBeamAnalysis.AnalyzeUniformCached(2475,combinedSupports,half,TowerOptimizer.PairEI,270,cache);
        Check(cache.Hits==1,"UDL support matrix is reused independently of intensity");
        Near(second.ShearKn,first.ShearKn/2,"cached intensity change preserves shear");
        Near(second.DeflectionMm,first.DeflectionMm/2,"cached intensity change preserves deflection");
        var shifted=new[] {new BeamDistributedLoad {StartMm=800,EndMm=1400,KnPerM=10}};
        var shiftedCached=LoadedBeamAnalysis.AnalyzeUniformCached(2475,supports,shifted,TowerOptimizer.PairEI,270,cache);
        var shiftedDense=LoadedBeamAnalysis.AnalyzeDenseReference(2475,supports,shifted,null,TowerOptimizer.PairEI,270);
        Check(cache.Hits==1,"changed UDL boundaries use a different support matrix");
        Near(shiftedCached.MomentKnM,shiftedDense.MomentKnM,"localized UDL cached/dense moment");
        Near(shiftedCached.ShearKn,shiftedDense.ShearKn,"localized UDL cached/dense shear");
        Near(shiftedCached.DeflectionMm,shiftedDense.DeflectionMm,"localized UDL cached/dense deflection");
        var localized=TransferredSlabOptimizer.PrimaryUniformLoads(0,3000,5,3,0,new[] {new BeamDistributedLoad {StartMm=1000,EndMm=1500,KnPerM=2}},empty);
        Check(localized.Length==3 && localized[1].StartMm==1000 && localized[1].EndMm==1500,"crossing increment remains over actual footprint");
        Near(localized[0].KnPerM,8,"baseline outside crossing");Near(localized[1].KnPerM,14,"crossing intensity retained locally");
        Near(localized.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),27,"crossing distributed load conserved");
        var clipped=TransferredSlabOptimizer.PrimaryUniformLoads(1250,1500,5,3,0,new[] {new BeamDistributedLoad {StartMm=1000,EndMm=1500,KnPerM=2}},empty);
        Check(clipped[0].StartMm==0 && clipped[0].EndMm==250,"crossing band clips at primary stock break");
        var partial=TransferredSlabOptimizer.PrimaryUniformLoads(0,3000,5,3,0,empty,empty);
        Near(partial.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),24,"full-tributary UDL does not reduce end-strip loading");
        var reversed=TransferredSlabOptimizer.PrimaryUniformLoads(0,3000,5,-1,2,empty,empty);
        Near(reversed[0].KnPerM,6,"eccentric slab reaction components sum with correct signs");
        foreach(bool deferred in new[] {false,true})
        foreach(bool endCrossing in new[] {false,true})
        {
            var common=new TowerInput {LengthMm=2450,LegCapacityKn=70,DeflectionDenominator=270,StockLengthsMm=new[] {1250.0,1800,2450},MaximumConcreteEndOverhangMm=75,DeferTransferLayout=deferred,AllowCustomH20Lengths=true};
            var network=new TransferNetworkInput {UpperSpacingMm=250,CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,
                Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=350,DepthMm=250}},
                Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100}},
                CrossingBeams=new[] {new TransferCrossingBeamRow {SupportMode=CrossBeamSupportMode.LegacySlabRows,BayNumber=1,PositionMm=endCrossing ? 200 : 1200,WidthMm=endCrossing ? 200 : 500,DepthMm=150}}};
            var result=TransferNetworkOptimizer.Optimize(common,network);
            foreach(var beam in result.Beams)CheckLayout(beam);
            CheckNetworkLoads(result);
            Check(result.LayoutDiagnostics.Contains("Primary design model: UDL"),"diagnostic names governing model");
        }
        Console.WriteLine($"Primary UDL: {checks} checks passed.");
    }
    public static void ReportedCase()
    {
        var common=new TowerInput {LengthMm=9900,LegCapacityKn=70,DeflectionDenominator=270,StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths),StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,AllowCustomH20Lengths=true};
        var network=new TransferNetworkInput {UpperSpacingMm=275,
            Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},
            Bays=new[] {new TransferBayRow {WidthMm=2676,ThicknessMm=300},new TransferBayRow {WidthMm=1874,ThicknessMm=300}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var result=TransferNetworkOptimizer.Optimize(common,network);
        foreach(var beam in result.Beams)CheckLayout(beam);
        Console.WriteLine($"REPORTED UDL seconds={timer.Elapsed.TotalSeconds:0.000} legs={result.LegCount} byBeam={string.Join(",",result.Beams.Select(b=>b.LegCount))} peakLeg={result.PeakLegKn:0.000}");
        Console.WriteLine(result.LayoutDiagnostics);
    }
}
