using System;
using System.Linq;
using EngineeringDashboard;
internal static class NetworkTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Network: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message);
    public static void Run()
    {
        var common=new TowerInput {AllowSeparateSlabFallback=false,LengthMm=6750,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=350};
        var input=new TransferNetworkInput {UpperSpacingMm=200,Beams=new[] {new TransferBeamRow {WidthMm=1100,DepthMm=1750},new TransferBeamRow {WidthMm=1100,DepthMm=1200},new TransferBeamRow {WidthMm=900,DepthMm=1500}},Bays=new[] {new TransferBayRow {WidthMm=3900,ThicknessMm=300},new TransferBayRow {WidthMm=3500,ThicknessMm=250}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,input);
        Check(result.Beams.All(b=>b.Transfer.LowerCases.SelectMany(c=>c).All(m=>m.H20Count==1 && m.Utilization<=1+1e-8)),"single lower secondaries checked in every beam and stage");
        Check(result.Beams.Length==3 && result.Bays.Length==2,"extended slab/beam chain");
        double loadedLength=result.SlabCoveredLengthMm;
        Near(result.SlabLoadKn,input.Bays.Sum(b=>input.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*loadedLength/1000,"full slab area conserved including both shutter edges");
        for(int b=0;b<result.Beams.Length;b++)
        {
            var beam=result.Beams[b];Check(beam.Transfer.RowsMm.SequenceEqual(result.Beams[0].Transfer.RowsMm),"shared upper rows align across all lower beams");
            double beamLoad=input.AreaLoad(input.Beams[b].DepthMm)*input.Beams[b].WidthMm/1000*(common.LengthMm-2*beam.ConcreteEndOverhangMm)/1000;
            Near(beam.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn),beamLoad,"each beam depth produces its own beam concrete load");
            Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7 && r<=common.LegCapacityKn+1e-8),"each beam's legs checked");
            Near(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn),beamLoad+beam.Transfer.SlabLoadKn,"shutter and upper feet reach longitudinal pairs");
            foreach(var m in beam.Transfer.LowerCases.Last())
            {
                var shutters=m.Points.Where(p=>Math.Abs(Math.Abs(p.PositionMm+m.StartMm)-input.Beams[b].WidthMm/2-100)<1e-6).ToArray();
                Check(shutters.Length==(b==0 || b==result.Beams.Length-1 ? 1 : 2),"one point reaction per shutter, 100 mm outward");
            }
        }
        for(int bay=0;bay<result.Bays.Length;bay++)
        {
            var left=result.Beams[bay].Transfer.UpperMembers.Single(m=>m.Name=="Upper right");var right=result.Beams[bay+1].Transfer.UpperMembers.Single(m=>m.Name=="Upper left");
            Near(left.LengthMm,right.LengthMm,"upper stock shared across adjacent beams");
            Near(left.Analysis.Reactions[0],right.Analysis.Reactions[1],"same physical upper-left reaction at both analyses");
            Near(left.Analysis.Reactions[1],right.Analysis.Reactions[0],"same physical upper-right reaction at both analyses");
        }
        double expectedTimber=result.Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*b.Transfer.LowerStockLengthMm)+result.Bays.Sum(b=>b.StockLengthMm)*result.Beams[0].Transfer.RowsMm.Length;
        Near(result.TimberMm,expectedTimber,"shared upper timber counted once");
        Check(result.Beams.All(b=>b.Transfer.UpperMembers.All(m=>m.H20Count==1 && m.MomentLimitKnM==5 && m.ShearLimitKn==11 && m.Utilization<=1+1e-8)),"slab H20s use single-member capacities in every network bay");
        Near(result.H20Count,result.Beams.Sum(b=>b.StockPlan.TotalH20Count+b.Transfer.RowsMm.Length)+result.Bays.Length*result.Beams[0].Transfer.RowsMm.Length,"single lower and shared slab H20 quantities counted once per row");
        Check(result.SlabMovementMm>0 && TowerBeamAnalysis.Finite(result.SlabMovementMm),"different lower-beam movements propagated into slab");
        var free=LoadedBeamAnalysis.Analyze(2000,new[] {0.0,2000},null,new BeamPointLoad[0],900,350);
        Near(free.DeflectionWithAffineOffsetMm(-2,-5,0,2000),5,"different prescribed foot movements");
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasLeftSlab=true,TransferSlabLoads=true,UseBeamNetwork=true};
        Check(vm.NetworkBeams.Count==2,"network initializes two actual beams");vm.AddNetworkBay();vm.NetworkBeams[1].DepthMm=1200;Check(vm.NetworkBeams.Count==3 && vm.NetworkBays.Count==2,"add slab and beam");
        var xml=vm.NetworkConfigurationXml;var other=new BeamCalculatorViewModel();other.NetworkConfigurationXml=xml;Near(other.NetworkBeams[1].DepthMm,1200,"per-beam depths saved");
        vm.RemoveNetworkBay();Check(vm.NetworkBeams.Count==2 && !vm.CanRemoveNetworkBay,"remove preserves connected sequence");
        var bracePositions=new[] {450.0,1450,2450,3450,4450};
        var braceJoins=TowerOptimizer.RemoveRedundantJoins(new[] {true,true,true,true},bracePositions,false);
        Check(braceJoins.SequenceEqual(new[] {true,false,true,true}),"five frames split into one standalone and one joined tower with three cross bays");
        Check(TowerOptimizer.RemoveRedundantJoins(new[] {true,true,true,true},new[] {150.0,750,1350,1950,2550},true).All(j=>j),"five 600 frames stay joined when standalone 600 bays are excluded");
        var crossingInput=new TransferNetworkInput {Beams=input.Beams.Take(2).ToArray(),Bays=input.Bays.Take(1).ToArray(),UpperSpacingMm=200,
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3175,WidthMm=400,DepthMm=350}}};
        Near(crossingInput.AreaLoad(300),9.4,"default slab area load includes one set of allowances");
        Near(crossingInput.AreaLoad(1750),46.65,"default deep beam load clamps 10 percent surcharge to 1.75");
        var custom=new TransferNetworkInput {CustomLoads=true,LiveKnM2=2,FormworkKnM2=.5};
        Near(custom.AreaLoad(300),10,"custom allowances replace the defaults");
        var widths=TransferNetworkOptimizer.EffectiveWidthsMm(common,crossingInput,0,6700,0);
        Near(widths.Sum(),6700+400*(crossingInput.AreaLoad(350)-9.4)/9.4,"crossing footprint replaces the slab once, with force conservation");
        Check(widths.Count(w=>w>200+1e-8)<=2,"crossing weight is local to rows beneath its footprint");
        var crossed=TransferNetworkOptimizer.Optimize(common,crossingInput);
        double crossingLength=crossed.SlabCoveredLengthMm;
        double expectedCrossing=9.4*crossingInput.Bays[0].WidthMm/1000*crossingLength/1000+(crossingInput.AreaLoad(350)-9.4)*.4*crossingInput.Bays[0].WidthMm/1000;
        Near(crossed.SlabLoadKn,expectedCrossing,"crossing and slab load reaches both main beams including shutter edges");
        Check(crossed.Beams.All(b=>b.Transfer.UpperMembers.All(m=>m.Utilization<=1+1e-8) && b.Transfer.LowerCases.Concat(b.Transfer.PrimaryCases).SelectMany(c=>c).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.All(r=>r>=-1e-7)) && b.MaxLegKn<=70+1e-8),"crossing layout checks upper singles, lower singles, primaries and individual legs");
        for(int r=0;r<crossed.Beams[0].Transfer.RowsMm.Length;r++)
        {
            Near(crossed.Beams[0].Transfer.UpperScale(false,r),crossed.Beams[1].Transfer.UpperScale(true,r),"both main beams use the same physical crossing row loads");
            foreach(var beam in crossed.Beams)Near(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.Reactions.Sum()),beam.Transfer.LowerCases.Last().Sum(m=>m.Analysis.TotalLoadKn),"crossing row point reactions conserved through primaries to legs");
        }
        foreach(var beam in crossed.Beams)Near(beam.Transfer.CoveredSlabAreaM2,crossed.SlabCoveredLengthMm*(beam.Transfer.LeftUpperStockMm+beam.Transfer.RightUpperStockMm)/1e6,"reported slab stock area uses physical coverage, without weighting the crossing load");
        Check(crossed.Schedule.Contains("X1: S1") && crossed.Schedule.Contains("extra bay load"),"crossing geometry and added load retained in schedule");
        vm.UseBeamNetwork=true;vm.AddCrossingBeam();vm.CrossingBeams[0].DepthMm=800;
        other.NetworkConfigurationXml=vm.NetworkConfigurationXml;Near(other.CrossingBeams[0].DepthMm,800,"crossing beam XML round trip");
        vm.CrossingBeams[0].BayNumber=2;vm.AddNetworkBay();vm.RemoveNetworkBay();Check(vm.CrossingBeams.Count==0,"removing a bay removes its crossing beams");
        crossingInput.CrossingBeams[0].DepthMm=300;
        Near(TransferNetworkOptimizer.EffectiveWidthsMm(common,crossingInput,0,6700,0).Sum(),6700,"equal slab and crossing depths add no duplicate area load");
        crossingInput.CrossingBeams[0].DepthMm=10000;bool overloaded=false;try {TransferNetworkOptimizer.Optimize(common,crossingInput);}catch(ArgumentException){overloaded=true;}Check(overloaded,"severe local crossing overload cannot produce a passing layout");
        crossingInput.CrossingBeams[0].DepthMm=350;crossingInput.CrossingBeams[0].WidthMm=10;crossingInput.CrossingBeams[0].PositionMm=3175;
        Check(TransferNetworkOptimizer.EffectiveWidthsMm(common,crossingInput,0,6700,0)==null,"crossing footprint between H20 rows cannot send weight to neighbouring rows");
        crossingInput.CrossingBeams[0].WidthMm=400;
        crossingInput.CrossingBeams[0].BayNumber=99;bool rejected=false;try {crossingInput.Validate(common);}catch(ArgumentException){rejected=true;}Check(rejected,"nonexistent crossing bay rejected");
        crossingInput.CrossingBeams[0].BayNumber=1;crossingInput.CrossingBeams[0].DepthMm=200;rejected=false;try {crossingInput.Validate(common);}catch(ArgumentException){rejected=true;}Check(rejected,"crossing depth below slab thickness rejected");
        crossingInput.CrossingBeams[0].DepthMm=350;crossingInput.CrossingBeams=new[] {crossingInput.CrossingBeams[0],new TransferCrossingBeamRow {PositionMm=3100,WidthMm=300,DepthMm=400}};rejected=false;try {crossingInput.Validate(common);}catch(ArgumentException){rejected=true;}Check(rejected,"overlapping crossing beams rejected to avoid duplicate volume");
        var failureVm=new BeamCalculatorViewModel {UseTowers=true,Depth=1750,Width=800,BeamLengthMm=10600,TowerLegCapacityKn=70,HasRightSlab=true,RightSlabThickness=300,RightSlabBayMm=2575,TransferSlabLoads=true,Exclude600Standalone=true,UseBeamNetwork=true};
        failureVm.NetworkBeams[1].WidthMm=650;failureVm.NetworkBeams[1].DepthMm=1150;
        failureVm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(!failureVm.IsCalculationValid && failureVm.NetworkResult==null && failureVm.HasTowerFailure,"failed user setup retains diagnostics without a solution");
        Check(failureVm.TowerFailureDetails.Contains("Lower secondary · moment") && failureVm.TowerFailureDetails.Contains("Lower secondary · shear") && failureVm.TowerFailureDetails.Contains("Lower secondary · deflection") && failureVm.TowerFailureDetails.Contains("stock 2175 mm"),"custom length removes stock blocker and exposes lower secondary failures at 750 mm rows");
        Check(!failureVm.TowerFailureDetails.Contains("Numerical checks recorded: 0") && !failureVm.TowerFailureDetails.Contains("no available stock fits"),"custom slab H20 reaches numerical checks");
        failureVm.UpperSupportSpacingMm=200;Check(!failureVm.HasTowerFailure && failureVm.TowerFailureDetails=="","editing spacing clears stale failure report");
        failureVm.OptimizeTowersAsync().GetAwaiter().GetResult();
        var customResult=failureVm.NetworkResult;
        Check(failureVm.IsCalculationValid && customResult!=null && !failureVm.HasTowerFailure,"same user setup passes at 200 mm rows with custom slab length");
        Near(customResult.Bays[0].StockLengthMm,2175,"2575 bay custom length");Near(customResult.Bays[0].EdgeMm,200,"custom length leaves minimum edge strips");
        Check(customResult.Bays[0].IsCustomLength && customResult.Beams[0].Transfer.RightUpperCustom && customResult.Beams[1].Transfer.LeftUpperCustom && customResult.Schedule.Contains("2175 mm custom"),"custom identity survives reversal and is visible in report");
        Near(customResult.SlabLoadKn,9.4*2.575*customResult.SlabCoveredLengthMm/1000,"custom-covered slab plus shutter strips conserve full bay load");
        Check(failureVm.BuildDiagnosticReport().Contains("SELECTED NETWORK LAYOUT") && customResult.LayoutDiagnostics.Contains("B1: legs=") && customResult.LayoutDiagnostics.Contains("primary runs=") && customResult.LayoutDiagnostics.Contains("S1: H20="),"diagnosis includes selected grid, per-beam legs and actual stocks and shared feet");
        foreach(var beam in customResult.Beams)
        {
            Check(beam.MaxLegKn<=70 && beam.StockPlan.Pieces.All(piece=>common.StockLengthsMm.Contains(piece.StockLengthMm)),"custom fallback does not change leg capacity or primary catalogue");
            foreach(var member in beam.Transfer.PrimaryCases.SelectMany(c=>c).Concat(beam.Transfer.LowerCases.SelectMany(c=>c)).Concat(beam.Transfer.UpperMembers))
            {
                Check(member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"custom layout passes all staged member checks and no uplift");
                Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"custom member vertical equilibrium");
            }
            for(int i=0;i<beam.Joined.Length;i++)if(beam.Joined[i] && Math.Abs(beam.PositionsMm[i+1]-beam.PositionsMm[i]-600)<1e-7)Check(i>0 && beam.Joined[i-1] || i+1<beam.Joined.Length && beam.Joined[i+1],"custom layout preserves standalone 600 exclusion");
        }
        Console.WriteLine("Custom user setup: "+customResult.Summary.Replace("\n"," · "));
        foreach(double bay in new[] {2575.0,2580,2587.5,2600})
        {
            double length=UpperTransferPlan.SlabLengths(bay,common.StockLengthsMm).Single();double edge=(bay-length)/2;
            Near(length%25,0,"custom lengths are multiples of 25");Check(edge>=200 && edge<212.5,"rounded custom end clearance stays close to 200 mm");
        }
        Check(UpperTransferPlan.SlabLengths(3900,common.StockLengthsMm).All(length=>common.StockLengthsMm.Contains(length)),"fitting catalogue lengths retain precedence");
        var plan=customResult.Bays[0];plan.Reverse().Validate(2575,common.StockLengthsMm);
        bool invalidCustom=false;try {new UpperTransferPlan {StockLengthMm=2176,EdgeMm=199.5,NearFootMm=600,FarFootMm=350,IsCustomLength=true}.Validate(2575,common.StockLengthsMm);}catch(ArgumentException){invalidCustom=true;}
        Check(invalidCustom,"invalid custom length and edge cannot bypass shared-plan validation");
        var overloadInput=new TransferNetworkInput {Beams=input.Beams.Take(2).ToArray(),Bays=input.Bays.Take(1).ToArray(),UpperSpacingMm=200,CrossingBeams=new[] {new TransferCrossingBeamRow {WidthMm=400,DepthMm=10000,PositionMm=3175}}};
        TowerOptimizationException failure=null;try {TransferNetworkOptimizer.Optimize(common,overloadInput);}catch(TowerOptimizationException ex){failure=ex;}
        Check(failure!=null && failure.Details.Contains("S1 slab H20") && failure.Details.Contains("moment") && failure.Details.Contains("shear") && failure.Details.Contains("deflection") && failure.Details.Contains("T") && failure.Details.Contains("L/350"),"crossing overload reports member, actual row, demand, capacity and deflection criterion");
        Check(failure.Details.Contains("examples may come from different layouts"),"diagnostic samples do not pretend to form one alternative layout");
        overloadInput.CrossingBeams=new TransferCrossingBeamRow[0];TransferNetworkOptimizer.Optimize(common,overloadInput);
        overloadInput.Bays[0]=new TransferBayRow {WidthMm=3900,ThicknessMm=300};common.StockLengthsMm=new[] {1250.0};failure=null;try {TransferNetworkOptimizer.Optimize(common,overloadInput);}catch(TowerOptimizationException ex){failure=ex;}
        Check(failure!=null && !failure.Details.Contains("S1 slab H20 · moment") && failure.Details.Contains("Numerical checks recorded: 0"),"reusing a snapshot starts a new diagnostic run");
        var legInput=new TowerInput {LengthMm=6750,LegCapacityKn=1,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=350,TransferredSlab=new TransferredSlabInput {BeamWidthMm=1100,BeamLineLoadKnM=50,UpperSpacingMm=200,LeftBayMm=3900,LeftAreaLoadKnM2=9.4}};
        failure=null;try {TransferredSlabOptimizer.Optimize(legInput);}catch(TowerOptimizationException ex){failure=ex;}
        Check(failure!=null && failure.Details.Contains("leg load") && failure.Details.Contains("peak leg x=") && failure.Details.Contains("/ 1 kN"),"leg overload reports exact line, load case, support station and permissible load");
        var responseCheck=LoadedBeamAnalysis.Analyze(1000,new[] {0.0,1000},new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=120}},null,TowerOptimizer.PairEI/2,350);
        var numericReport=new TowerSearchDiagnostics(350);numericReport.Analysis(responseCheck,1,1,"Test single H20",2,"T4 · x=750 mm · Beam + right slab");
        var numericText=numericReport.Exception("Test failure").Details;
        Check(numericText.Contains("15 / 5 kNm") && numericText.Contains("60 / 11 kN"),"failure demands and capacities agree with independent simply-supported UDL equilibrium");
        double analyticDeflection=5*120/(384*(TowerOptimizer.PairEI/2))*1000;
        Check(numericText.Contains($"{analyticDeflection:0.###} / {1000.0/350:0.###} mm") && numericText.Contains("T4 · x=750 mm · Beam + right slab"),"reported deflection uses governing-region demand/allowance and preserves location and load stage");
        numericReport.Analysis(LoadedBeamAnalysis.Analyze(1000,new[] {700.0,900},new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=80}},null,TowerOptimizer.PairEI/2,350),1,1,"Uplift test",2,"one-sided load");
        Check(numericReport.Exception("Test failure").Details.Contains("reaction -80 kN; minimum 0 kN"),"negative bearing reaction reports signed magnitude and zero bound");
        Console.WriteLine($"Network: {checks} checks passed · {result.LegCount} tower legs · {result.TimberMm/1000:0.0} m timber · {timer.Elapsed.TotalSeconds:0.00} s");
    }
}
