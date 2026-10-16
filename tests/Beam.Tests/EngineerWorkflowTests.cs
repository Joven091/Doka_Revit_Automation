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
        ReportedPrimaryZones();
    }

    private static void ReportedPrimaryZones()
    {
        var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=6100,LegCapacityKn=30,StockEndSetbackMm=150,
            MaximumConcreteEndOverhangMm=150,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,IgnoreUplift=true,DeflectionDenominator=270};
        var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
            UpperSpacingMm=305,BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new[] {350.0},
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=200}},
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3350,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}}};
        var plan=EngineerPrimaryPlan.Create(common,network,1);
        var loads=plan.Loads(75,3300,0);
        Check(loads.Length==2,"reported 3300 mm member has exactly two UDL zones");
        Near(loads[0].StartMm,0,"normal zone starts at stock end");Near(loads[0].EndMm,2950,"normal zone ends at the physical X1 face");
        Near(loads[0].KnPerM,25.99125,"beam plus 200 mm slab UDL");
        Near(loads[1].StartMm,2950,"crossing starts at the same boundary");Near(loads[1].EndMm,3300,"crossing continues to this stock end");
        Near(loads[1].KnPerM,52.288125,"beam plus crossing UDL");
        var a=LoadedBeamAnalysis.AnalyzeSigned(3300,new[] {450.0,1450,2075,3075},loads,new BeamPointLoad[0],TowerOptimizer.PairEI,270);
        // Independent Python Euler-Bernoulli FEM reference, including both free tails.
        double[] reactions={26.009082253282962,18.878950606650072,22.811522161825874,27.27547622824104};
        for(int i=0;i<reactions.Length;i++)Near(a.Reactions[i],reactions[i],"reported two-zone reaction "+(i+1));
        Near(a.TotalLoadKn,94.97503125,"reported two-zone member force");Near(a.MomentKnM,2.6316140625,"reported two-zone bending");
        Near(a.ShearKn,15.51064810324112,"reported two-zone shear");Near(a.DeflectionMm,.16734319666955558,"reported two-zone deflection");
        var result=TransferNetworkOptimizer.Optimize(common,network);
        Check(result.CrossSupports.Single().UsesSpreader && result.Bays.All(b=>b!=null),"exact reported model retains complete beam/slab/shared-X1 support");
        Check(result.Schedule.Contains("X1 · shared spreader support") && !result.Schedule.Contains("X1: dedicated cross-beam formwork"),"report identifies the calculated shared support instead of the requested automatic mode");
        Check(result.Beams.All(b=>b.Transfer.PrimaryLoadBasis.Contains("full crossing width")),"reported primary basis identifies concrete tributary UDLs");
        Check(!CrossBeamSupportResult.Limitations.Contains("An eccentric resultant uses a shorter centred flat block"),"shared support explanation no longer describes the obsolete primary model");
        for(int beam=0;beam<2;beam++)
        {
            var layout=result.Beams[beam];Check(layout.Transfer.PrimaryPlan!=null,"final primary retains the agreed zone model");
            int runs=layout.StockPlan.Pieces.Length;
            for(int line=0;line<2;line++)for(int run=0;run<runs;run++)
            {
                var m=layout.Transfer.PrimaryCases.Last()[run+line*runs];
                bool inner=beam==0 ? line==1 : line==0;
                var cuts=new[] {m.StartMm,m.StartMm+m.LengthMm,3025.0,3675}.Where(x=>x>=m.StartMm && x<=m.StartMm+m.LengthMm).Distinct().OrderBy(x=>x).ToArray();
                Check(m.Points.Length==0,"primary uses UDLs only");
                for(int j=1;j<cuts.Length;j++)
                {
                    double x=(cuts[j-1]+cuts[j])/2,q=18.66+(inner ? (x>=3025 && x<=3675 ? 33.628125 : 7.33125) : 0);
                    Near(m.Uniform.Where(u=>x-m.StartMm>=u.StartMm && x-m.StartMm<=u.EndMm).Sum(u=>u.KnPerM),q,"actual selected primary intensity matches the concrete tributary zone");
                }
                Check(m.Uniform.All(u=>Math.Abs(u.StartMm)<1e-7 || Math.Abs(u.StartMm+m.StartMm-3025)<1e-7 || Math.Abs(u.StartMm+m.StartMm-3675)<1e-7),"no sheet-joint/ledger/flange steps on the primary");
                var manual=LoadedBeamAnalysis.AnalyzeSigned(m.LengthMm,m.SupportsMm,m.Uniform,new BeamPointLoad[0],TowerOptimizer.PairEI,270);
                Near(m.Analysis.MomentKnM,manual.MomentKnM,"displayed load model drives bending");Near(m.Analysis.ShearKn,manual.ShearKn,"displayed load model drives shear");
                Near(m.Analysis.DeflectionMm,manual.DeflectionMm,"displayed load model drives deflection");
                Near(m.Utilization,Math.Max(manual.DeflectionUtilization,Math.Max(manual.MomentKnM/10,manual.ShearKn/22)),"displayed load model drives primary utilization");
                for(int j=0;j<manual.Reactions.Length;j++)
                {
                    Near(m.Analysis.Reactions[j],manual.Reactions[j],"displayed load model drives support reaction");
                    Near((line==0 ? layout.LeftReactions : layout.RightReactions)[layout.StockPlan.Pieces[run].FirstSupport+j],manual.Reactions[j],"tower leg demand uses the same primary reaction");
                }
                Check(m.Utilization<=1+1e-8 && m.Analysis.Reactions.Max()<=30+1e-8,"final member and leg capacities pass the same load model");
            }
        }
        double carried=result.Beams.Sum(b=>b.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn));
        double external=result.Beams.Sum(b=>b.Transfer.ColumnSupportLoadKn(b.Transfer.CaseNames.Length-1));
        Near(carried+external,2*37.32*6.1+6.9*2.125*5.45+31.65*2.125*.65,"complete concrete force retained without double-counted ledgers");
        double firstMoment=result.Beams.Sum(b=>b.Transfer.PrimaryCases.Last().Sum(m=>m.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(m.StartMm+(q.StartMm+q.EndMm)/2)/1000)))
            +result.Beams.Sum(b=>b.Transfer.ColumnSupportLoads.SelectMany(c=>c).Sum(c=>c.LeftLineMomentKnM+c.RightLineMomentKnM));
        Near(firstMoment,(2*37.32+6.9*2.125)*6.1*6.1/2+(31.65-6.9)*2.125*.65*3.35,"primary and end-support loads preserve the concrete longitudinal first moment");
        Console.WriteLine("Reported primary zones passed: 25.99125 / 52.288125 kN/m; complete 6100 mm network.");
        Console.WriteLine(result.LayoutDiagnostics);
    }
}
