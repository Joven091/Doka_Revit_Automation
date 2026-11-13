using System;
using System.Linq;
using EngineeringDashboard;

internal static class PrimaryMidpointBoundTests
{
    static int checks,denseChecks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Primary midpoint bound: "+message);}
    static TowerInput Input(bool enabled=true)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,DeflectionDenominator=270,LegCapacityKn=70,
        IgnoreUplift=true,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.BeamSupports,ReuseCrossingLocalWork=enabled,Trace=new BeamSearchTrace("primary midpoint")};
    static void CounterfactualStocks()
    {
        foreach(double span in new[]{1800.0,3000,5400,8000})foreach(double near in new[]{150.0,350,600})foreach(double far in new[]{150.0,350,600})
        foreach(int rows in new[]{2,4,8})foreach(double total in new[]{-80.0,-20,20,80})
        {
            double a=near,b=span-far;if(b<=a)continue;
            Check(CrossBeamSupportOptimizer.TryPrimaryMidpointDemand(span,a,b,total,Math.Abs(total),rows,span+10000,out double demand,out _),"finite canonical geometry supplies optional numeric metadata");
            if(demand<=5*(1+1e-6)+1e-8)continue;
            var distributions=new[]{Enumerable.Repeat(total/rows,rows).ToArray(),new[]{-2*total,3*total}.Concat(Enumerable.Repeat(0.0,rows-2)).ToArray()};
            foreach(double stock in new[]{span,span-100,span-300})foreach(double start in new[]{-5e-8,0,50,(span-stock)/2,span-stock})
            {
                if(stock<=0 || start< -1e-7 || start+stock>span+1e-7)continue;
                var supports=new[]{a-start,b-start};if(supports[0]<0 || supports[1]>stock || supports[1]-supports[0]<=1e-4)continue;
                foreach(var distribution in distributions)
                {
                    double q=distribution.OrderByDescending(Math.Abs).First();var load=new[]{new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=Math.Abs(q)}};
                    var dense=LoadedBeamAnalysis.AnalyzeDenseReference(stock,supports,load,null,TowerOptimizer.PairEI/2,270);
                    Check(dense.MomentKnM>5+1e-8,"a stock-independent rejection retains an independently analysed failing row for every stock/end offset and signed density distribution");denseChecks++;
                }
            }
        }
        Check(denseChecks>100,"counterfactual oracle exercises many actual permitted stock/edge and signed-row cases");
    }
    static void SourceAndNativeParity()
    {
        foreach(var material in new[]{PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var off=Input(false);off.PrimaryMaterial=material;var on=Input();on.PrimaryMaterial=material;
            var a=CrossBeamSupportOptimizer.PrimaryBearing(off,PrimaryBearingTests.Fixture(),0,out var oldReason);
            var b=CrossBeamSupportOptimizer.PrimaryBearing(on,PrimaryBearingTests.Fixture(),0,out var newReason);
            Check(a!=null && b!=null,"native passing direct-primary rows are retained: "+oldReason+" / "+newReason);
            double[] Values(CrossBeamSupportResult value)=>value.Members.SelectMany(m=>new[]{m.StartMm,m.LengthMm,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.TotalLoadKn}.Concat(m.SupportsMm).Concat(m.Analysis.Reactions)).ToArray();
            Check(Values(a).SequenceEqual(Values(b)) && a.SecondaryStationsMm.SequenceEqual(b.SecondaryStationsMm),"native ON/OFF preserves exact selected stock, row positions and member forces for both main-primary materials");
        }
        var r=PrimaryBearingTests.Fixture();r.Input.Bays[0].WidthMm=5399.9994598388666;r.Input.Beams[0].WidthMm=800.0000061035156;r.Input.Beams[1].WidthMm=649.99985961914069;
        r.Input.CrossingBeams[0].WidthMm=649.99985961914069;r.Input.CrossingBeams[0].PositionMm=5602.7789428710948;
        r.Input.Beams[0].BottomElevationMm=-1750;r.Input.Beams[1].BottomElevationMm=-1150;r.BeamCentresMm[1]=r.Input.Beams[0].WidthMm/2+r.Input.Bays[0].WidthMm+r.Input.Beams[1].WidthMm/2;
        foreach(var layout in r.Beams)layout.WidthMm=1524;
        var input=Input();input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);
        Check(CrossBeamSupportOptimizer.PrimaryBearingPossible(input,r.Input,r.Input.CrossingBeams[0]),"actual long scenario2 axis family is an eligible primary-bearing route");
        var result=CrossBeamSupportOptimizer.PrimaryBearing(input,r,0,out string reason);
        Check(result==null && input.Trace.Count(BeamSearchCounter.PrimaryBearingMidpointRejections)==1,"actual scenario2 long bay is rejected before primary-bearing stock and row work");
        Check(input.Trace.Count(BeamSearchCounter.LowerAnalyses)==0 && input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0,"stock-independent precheck adds no FE and precedes unit-response analysis");
        Check(reason.Contains("midpoint") && input.Diagnostics.Exception("stock-independent primary detail").Details.Contains("midpoint moment lower bound"),"new early rejection retains a concrete moment explanation");
        var reference=Input(false);Check(CrossBeamSupportOptimizer.PrimaryBearing(reference,r,0,out _)==null,"the same long-bay detail also fails the original complete search");
        r.Input.BeamSpacingsMm=new[]{305.0,305};r.Input.SlabSpacingsMm=new[]{488.0};r.Input.CrossingSpacingsMm=new[]{305.0};r.Input.UseCrossingSlabLedgers=true;r.Bays[0]=new UpperTransferPlan();r.SlabCoveredLengthMm=6900;
        foreach(var layout in r.Beams)layout.Transfer.SharedSlabRowsMm=Array.Empty<double>();
        var ledger=r.Input.CrossSlabLayout(6900,6900,0);ledger.Edges.Clear();ledger.Edges.Add(new SlabEdgeReaction {Crossing=0,PositionMm=r.Input.CrossingBeams[0].PositionMm-r.Input.CrossingBeams[0].WidthMm/2,WidthMm=-50});
        Check(CrossBeamSupportOptimizer.PrimaryBearingMidpointFails(input,r,0,out _) && CrossBeamSupportOptimizer.PrimaryBearing(reference,r,0,out _)==null,"actual signed ledger source is included conservatively and the original full route still fails");
        double concrete=r.Input.AreaLoad(r.Input.CrossingBeams[0].DepthMm)*r.Input.CrossingBeams[0].WidthMm/1000;
        ledger.Edges.Clear();ledger.Edges.Add(new SlabEdgeReaction {Crossing=0,PositionMm=r.Input.CrossingBeams[0].PositionMm-r.Input.CrossingBeams[0].WidthMm/2,WidthMm=(-concrete+1e-10)*1000/r.Input.AreaLoad(r.Input.Bays[0].ThicknessMm)});
        Check(!CrossBeamSupportOptimizer.PrimaryBearingMidpointFails(input,r,0,out _),"actual source cancellation near zero retains the original route search");
        var invalid=PrimaryBearingTests.Fixture();invalid.Input.BeamSpacingsMm=new[]{305.0,305};invalid.Input.SlabSpacingsMm=new[]{488.0};invalid.Input.CrossingSpacingsMm=new[]{305.0};invalid.Input.UseCrossingSlabLedgers=true;invalid.Bays[0]=new UpperTransferPlan();invalid.SlabCoveredLengthMm=50;
        Check(!CrossBeamSupportOptimizer.PrimaryBearingMidpointFails(input,invalid,0,out _),"speculative source ArgumentException remains metadata-unknown");
        Console.WriteLine("Actual scenario2 long-bay precheck: "+input.Trace.Count(BeamSearchCounter.PrimaryBearingMidpointRejections)+" rejection; "+reference.Trace.Count(BeamSearchCounter.LowerAnalyses)+" original unit/member analyses");
    }
    static void MarginsAndUnknown()
    {
        foreach(double shift in new[]{-1e-7,-5e-8,0,5e-8,1e-7})
        {
            Check(CrossBeamSupportOptimizer.TryPrimaryMidpointDemand(5400,600+shift,4800-shift,40,40,8,10000,out double demand,out double coefficient),"near-tolerance axes remain finite");
            foreach(double start in new[]{-5e-8,0,5e-8})
            {
                double end=5400+5e-8,d=4800-shift-(600+shift),left=600+shift-start,right=end-(4800-shift),exact=d*d/8-left*left/4-right*right/4;
                Check(coefficient<=exact && demand<=40.0/8*exact/1e6,"both tails protect the original near-negative start/oversize-end tolerance");
            }
        }
        Check(CrossBeamSupportOptimizer.TryPrimaryMidpointDemand(5400,600,4800,.001,1e12,8,10000,out double cancelled,out _) && cancelled==0,"signed source cancellation guard scales to raw ledger terms");
        Check(!CrossBeamSupportOptimizer.TryPrimaryMidpointDemand(double.NaN,600,4800,40,40,8,10000,out _,out _)
            && !CrossBeamSupportOptimizer.TryPrimaryMidpointDemand(5400,600,4800,40,40,1,10000,out _,out _),"unsupported/nonfinite optional metadata retains the original path");
        var timed=Input();timed.Trace=new BeamSearchTrace("original timed primary path",searchTimeLimit:TimeSpan.FromMinutes(1));
        Check(!CrossBeamSupportOptimizer.PrimaryBearingMidpointFails(timed,PrimaryBearingTests.Fixture(),0,out _),"timed search bypasses the new proof");
    }
    internal static void NativeParity()
    {
        var common=Input();common.LengthMm=4000;common.StockLengthsMm=new[]{2000.0,4000};common.CustomH20Scope=CustomH20LengthScope.Both;common.CoordinateCrossingLayouts=true;
        var network=new TransferNetworkInput {ReferenceLengthMm=4000,PlywoodSheetLengthMm=2440,PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,UpperSpacingMm=305,
            BeamSpacingsMm=new[]{305.0,305},SlabSpacingsMm=new[]{488.0},CrossingSpacingsMm=new[]{305.0},UseCrossingSlabLedgers=true,
            Beams=new[]{new TransferBeamRow {WidthMm=1000,DepthMm=1800,BottomElevationMm=0},new TransferBeamRow {WidthMm=1000,DepthMm=1800,BottomElevationMm=0}},
            Bays=new[]{new TransferBayRow {WidthMm=2500,ThicknessMm=50}},CrossingBeams=new[]{new TransferCrossingBeamRow {BayNumber=1,PositionMm=2000,WidthMm=400,DepthMm=200,BottomElevationMm=0,SupportMode=CrossBeamSupportMode.Automatic}}};
        var off=common.CopyForSearch();off.ReuseCrossingLocalWork=false;var expected=TransferNetworkOptimizer.Optimize(off,network);var actual=TransferNetworkOptimizer.Optimize(common,network);
        Check(ControlledPerformanceTests.Fingerprint(expected)==ControlledPerformanceTests.Fingerprint(actual),"native complete ON/OFF preserves exact selected network geometry, crossing arrangement and all member forces");
        ControlledPerformanceTests.Validate(common,actual);
        Check(actual.CrossSupports.Length==network.CrossingBeams.Length,"complete parity retains every crossing and its permitted fallback route");
        Console.WriteLine("PASS midpoint native ON/OFF exact network parity: "+actual.LegCount+" legs; all crossings resolved (passing direct-primary details verified separately)");
    }
    internal static void Run(){CounterfactualStocks();SourceAndNativeParity();MarginsAndUnknown();Console.WriteLine("PASS "+checks+" primary midpoint checks, including "+denseChecks+" independent dense counterfactual rows");}
}
