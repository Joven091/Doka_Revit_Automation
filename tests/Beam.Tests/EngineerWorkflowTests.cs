using System;
using System.Linq;
using EngineeringDashboard;

internal static class EngineerWorkflowTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception("Engineer workflow: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message+" actual="+actual+" expected="+expected);
    public static void Run()
    {
        var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=7000,LegCapacityKn=70,StockEndSetbackMm=150,
            MaximumConcreteEndOverhangMm=175,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,DeflectionDenominator=270};
        var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
            UpperSpacingMm=305,BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new[] {350.0},
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300}},
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3350,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}}};
        var plan=EngineerPrimaryPlan.Create(common,network,0);
        Near(plan.Forces[1],28.6475*6.35+(18.66+31.65*2.125/2)*.65,"complete two-zone primary force established before geometry");
        Near(plan.Forces[0],18.66*7,"outer primary planning force");Check(plan.InitialLegs==7,"seven starting legs include X1");
        var reduced=common.CopyForSearch();reduced.LegCapacityKn=20;Check(EngineerPrimaryPlan.Create(reduced,network,0).InitialLegs==11,"lower configured leg capacity governs seed");
        reduced.PrimaryMaterial=PrimaryBeamMaterial.WS10;reduced.LegCapacityKn=70;Check(EngineerPrimaryPlan.Create(reduced,network,0).InitialLegs==4,"WS10 uses its 70 kN assumption");
        var primary=LoadedBeamAnalysis.AnalyzeSigned(3300,new[] {400.0,1400,2900},new[] {new BeamDistributedLoad {StartMm=0,EndMm=2975,KnPerM=28.6475},new BeamDistributedLoad {StartMm=2975,EndMm=3300,KnPerM=52.29}},new BeamPointLoad[0],TowerOptimizer.PairEI,270);
        Near(primary.Reactions[0],23.501281015625,"engineer 3300 mm left reaction");Near(primary.Reactions[1],38.3952196614583,"engineer 3300 mm middle reaction");Near(primary.Reactions[2],40.3240618229167,"engineer 3300 mm right reaction");
        Near(primary.MomentKnM,4.573268984375,"engineer 3300 mm bending");Near(primary.ShearKn,21.7900006770833,"engineer 3300 mm shear");
        Check(primary.DeflectionUtilization<=1 && primary.Reactions.Max()<70,"reference member deflection and legs pass");
        var next=LoadedBeamAnalysis.AnalyzeSigned(3600,new[] {300.0,1300,2300,3300},new[] {new BeamDistributedLoad {StartMm=0,EndMm=325,KnPerM=52.29},new BeamDistributedLoad {StartMm=325,EndMm=3600,KnPerM=28.6475}},new BeamPointLoad[0],TowerOptimizer.PairEI,270);
        Near(next.MomentKnM,2.67735760183919,"engineer 3600 mm bending");Near(next.ShearKn,15.7119701018392,"engineer 3600 mm shear");Check(next.DeflectionUtilization<=1 && next.Reactions.Max()<70,"reference 3600 mm member passes");
        var geometryInput=common.CopyForSearch();geometryInput.EngineerPlan=plan;geometryInput.EngineerStationLimit=7;
        Check(EngineerTowerSearch.StockRunGeometries(new[] {3300.0,3600},geometryInput).Any(g=>g.Positions.SequenceEqual(new[] {400.0,1400,2900,3600,4600,5600,6600}) && g.Joined.SequenceEqual(new[] {true,true,false,true,true,true})),"engineer's complete mixed-brace 3300 + 3600 arrangement is generated");
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,network);
        Console.WriteLine("COMPLETE ENGINEER WORKFLOW seconds="+timer.Elapsed.TotalSeconds);Console.WriteLine(result.LayoutDiagnostics);
        Check(result.CrossSupports.Single().UsesSpreader,"reported model uses the defined spreader load path");
        Check(result.Beams.All(b=>b.SearchInitialLegsPerLine==7),"full-load seed retained in results");
        var crossing=result.CrossSupports.Single();
        Check(crossing.SecondaryStationsMm.Zip(crossing.SecondaryStationsMm.Skip(1),(a,b)=>b-a).All(g=>g<=350+1e-7 && g>=80-1e-7),"independently refined X1 H20 rows retain plywood/flange limits");
        foreach(var side in crossing.Bearings.GroupBy(b=>b.Beam))
        {
            Check(side.Select(b=>b.Row).Distinct().Count()>=3,"multiple receiver rows participate");
            Check(side.All(b=>b.Xmm>=3025+40-1e-7 && b.Xmm<=3675-40+1e-7 && b.FootprintStartMm>=3025-1e-7 && b.FootprintEndMm<=3675+1e-7),"all spreader transfer stays within 650 mm");
        }
        Near(crossing.Bearings.Sum(b=>b.ReactionKn),crossing.LoadKn,"spreader force conserved");
        double ledgerMoment=CrossBeamSupportOptimizer.SlabEdgeLoads(result,0).Sum(p=>p.Kn*p.PositionMm)*2.125;
        Near(crossing.Bearings.Sum(b=>b.ReactionKn*(b.Xmm-3350)),ledgerMoment,"spreader preserves crossing and ledger longitudinal first moment");
        foreach(var beam in result.Beams)
        {
            Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r<=70+1e-8),"all final leg reactions pass");
            foreach(var m in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8,"all final bending/shear/deflection checks pass");
                Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"member force equilibrium");
            }
        }
        double carried=result.Beams.Sum(b=>b.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn));
        double external=result.Beams.Sum(b=>b.Transfer.ColumnSupportLoadKn(b.Transfer.CaseNames.Length-1));
        Near(carried+external,2*37.32*7+9.4*2.125*6.35+31.65*2.125*.65,"complete beam/slab/crossing force counted once");
        var obstacles=CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).ToArray();
        foreach(var solid in crossing.Solids)foreach(var obstacle in obstacles)Check(!solid.Conflicts(obstacle),"complete selected arrangement clears actual timber and concrete");
        Console.WriteLine("Complete engineer workflow passed.");
    }
}
