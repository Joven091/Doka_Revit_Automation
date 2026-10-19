using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class CentredSearchSpeedTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Centred search: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<2e-6*Math.Max(1,Math.Abs(expected)),message+$" ({actual:R}/{expected:R})");
    internal static void Run()
    {
        var random=new Random(71018);
        for(int sample=0;sample<240;sample++)
        {
            double length=1250+random.Next(0,160)*25,a=sample%5==0 ? 0 : 150+random.Next(0,16)*25,b=length-(sample%7==0 ? 0 : 150+random.Next(0,16)*25);
            var uniform=sample%4==0 ? new BeamDistributedLoad[0] : new[] {
                new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=random.NextDouble()*30},
                new BeamDistributedLoad {StartMm=length*.2,EndMm=length*.7,KnPerM=random.NextDouble()*15}};
            var points=new[] {new BeamPointLoad {PositionMm=sample%3==0 ? a : length*.35,Kn=random.NextDouble()*12},new BeamPointLoad {PositionMm=length,Kn=2}};
            var fast=LoadedBeamAnalysis.Analyze(length,new[] {a,b},uniform,points,899.7512,270);
            var reference=LoadedBeamAnalysis.AnalyzeDenseReference(length,new[] {a,b},uniform,points,899.7512,270);
            Near(fast.Reactions[0],reference.Reactions[0],"left reaction");Near(fast.Reactions[1],reference.Reactions[1],"right reaction");
            Near(fast.MomentKnM,reference.MomentKnM,"moment peak");Near(fast.ShearKn,reference.ShearKn,"shear peak");
            Near(fast.DeflectionMm,reference.DeflectionMm,"deflection peak");Near(fast.DeflectionUtilization,reference.DeflectionUtilization,"local deflection limit");
            for(int i=0;i<=12;i++)for(int component=0;component<3;component++)Near(fast.Response(length*i/12)[component],reference.Response(length*i/12)[component],"diagram response");
        }
        double root;
        Check(LoadedBeamAnalysis.TryCubicRoot(1,-6,11,-6,.5,1.5,out root),"three-root cubic chooses the isolated first root");Near(root,1,"cubic root one");
        Check(LoadedBeamAnalysis.TryCubicRoot(1,-6,11,-6,1.5,2.5,out root),"three-root cubic chooses the isolated middle root");Near(root,2,"cubic root two");
        Check(LoadedBeamAnalysis.TryCubicRoot(1,0,0,-8,0,3,out root),"single real cubic root");Near(root,2,"cube root");
        var positions=Enumerable.Range(0,4).SelectMany(i=>new[] {i*2650+563.0,i*2650+2087}).ToArray();
        var pieces=Enumerable.Range(0,4).Select(i=>new TowerStockPiece {StartMm=i*2650,StockLengthMm=2650,FirstSupport=i*2,LastSupport=i*2+1}).ToArray();
        Func<double[],TowerLayout> layout=p=>new TowerLayout {EngineerSearch=true,PositionsMm=p,Joined=new[] {true,false,true,false,true,false,true},WidthMm=1000,Rotated=true,LengthMm=10600,StockPlan=new TowerStockPlan {Pieces=pieces}};
        var centred=layout(positions);var shifted=layout(positions.Select((p,i)=>p+(i<4 ? 100 : -100)).ToArray());
        Near(centred.StandaloneBracePenalty,shifted.StandaloneBracePenalty,"identical brace sizes receive identical brace scores");
        Check(TransferredSlabOptimizer.Better(centred,shifted),"centred four-stock layout outranks an inward shift");
        var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=6900,LegCapacityKn=70,IgnoreUplift=true,DeflectionDenominator=270};
        var fixture=CentredCrossingTests.Fixture(common);fixture.CrossSupports=fixture.CrossSupports.Take(1).ToArray();
        foreach(var c in fixture.Input.CrossingBeams)c.PositionMm+=2225;
        foreach(var c in fixture.CrossSupports)
        {
            foreach(var bearing in c.Bearings.Where(b=>b.IsDedicated))bearing.Xmm+=2225;
            foreach(var solid in c.Solids){solid.X0+=2225;solid.X1+=2225;}
        }
        var original=fixture.Beams[1].Transfer;
        CrossBeamSupportOptimizer.CentreCrossingFrames(common,fixture,Console.WriteLine);
        Near(fixture.CrossSupports[0].Bearings.Where(b=>b.IsDedicated && b.Label.StartsWith("Dedicated P")).Average(b=>b.Xmm),5675,"centred leg can clear the 4880 plywood-joint row");
        Check(fixture.Beams[1].Transfer.HasCustomPlywoodJoints,"relocated joint requires local plywood cuts");
        Check(ReferenceEquals(fixture.Beams[1].Transfer.PrimaryCases,original.PrimaryCases) && ReferenceEquals(fixture.Beams[1].Transfer.LowerCases,original.LowerCases),"joint relocation retains the existing design calculations");
        var primary=fixture.Beams[0].Transfer.PrimaryCases[0][0];
        CrossBeamSupportOptimizer.BalanceSettingOut(common,fixture);
        Check(fixture.Beams[0].CantileverImbalanceMm<1e-7,"main-beam stock is also centred");
        var centredPrimary=fixture.Beams[0].Transfer.PrimaryCases[0][0];
        Check(ReferenceEquals(primary.Uniform,centredPrimary.Uniform) && !ReferenceEquals(primary.Analysis,centredPrimary.Analysis),"main centring retains UDL and recomputes support responses");
        Near(primary.Analysis.TotalLoadKn,centredPrimary.Analysis.TotalLoadKn,"main centring retains primary force");
        var trace=new BeamSearchTrace("batched counters");
        Parallel.For(0,8,i=>{for(int j=0;j<1001;j++)trace.Increment(BeamSearchCounter.NumericalChecks);});
        Near(trace.Count(BeamSearchCounter.NumericalChecks),8008,"live counter includes pending worker events");trace.Complete("Succeeded");
        Near(trace.Count(BeamSearchCounter.NumericalChecks),8008,"completed counter remains exact");
        foreach(bool engineer in new[] {false,true})
        {
            var capacityTrace=new BeamSearchTrace("impossible leg capacity");
            var impossible=new TowerInput {LengthMm=10600,LegCapacityKn=1,EngineerSearch=engineer,FullLoadingOnly=true,StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths),DeflectionDenominator=270,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=175,AllowSeparateSlabFallback=false,Trace=capacityTrace};
            var network=new TransferNetworkInput {UpperSpacingMm=305,Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750},new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}},Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300}}};
            bool rejected=false;try{TransferNetworkOptimizer.Optimize(impossible,network);}catch(TowerOptimizationException failure){rejected=failure.Details.Contains("total available leg capacity");}
            Check(rejected,"impossible capacity is rejected in production and historical searches");
            Check(capacityTrace.Count(BeamSearchCounter.PrimaryAnalyses)==0,"impossible total capacity needs no primary numerical analyses");
        }
        Console.WriteLine($"Passed {checks} centred search / two-support checks.");
    }
    internal static void Reported()
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,
            BeamLengthMm=10600,Depth=1750,Width=800,FloorHeight=3000,TowerLegCapacityKn=70,AllowCustomH20Lengths=false,MaximumConcreteEndOverhangMm=175,StockEndSetbackMm=150,
            Exclude600Standalone=false,PlywoodDeflectionLimit="L/350",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        vm.AddNetworkBay();
        for(int i=0;i<3;i++){vm.NetworkBeams[i].WidthMm=i==0 ? 650 : 800;vm.NetworkBeams[i].DepthMm=i==0 ? 1150 : 1750;vm.NetworkBeams[i].BottomElevationMm=i==0 ? -1150 : -1750;}
        vm.NetworkBays[0].WidthMm=3000;vm.NetworkBays[0].ThicknessMm=200;vm.NetworkBays[1].WidthMm=2125;vm.NetworkBays[1].ThicknessMm=300;
        foreach(int bay in new[] {2,1})vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=bay,PositionMm=5675,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150});
        var timer=Stopwatch.StartNew();var task=vm.OptimizeTowersAsync(false);
        while(!task.Wait(TimeSpan.FromSeconds(30)))Console.WriteLine($"REPORT progress {timer.Elapsed.TotalSeconds:0.0}s · {vm.SearchProgressText}");
        task.GetAwaiter().GetResult();Console.WriteLine(vm.BuildDiagnosticReport("2026-10-18 10600 mm performance and centring regression"));
        Check(vm.IsCalculationValid,"10600 reported input finds a complete solution");
        var result=vm.NetworkResult;
        foreach(var beam in result.Beams)
        {
            Check(beam.MaxLegKn<=70+1e-7,"reported leg capacity");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(x=>x).Concat(beam.Transfer.UpperMembers))Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-7,"reported primary / secondary capacity");
        }
        foreach(var crossing in result.CrossSupports.Where(c=>c.Towers!=null))
        {
            Near(crossing.Bearings.Where(b=>b.IsDedicated && b.Label.StartsWith("Dedicated P")).Average(b=>b.Xmm),crossing.Input.PositionMm,"reported crossing frame centre");
            Check(crossing.Members.All(m=>m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-7),"reported crossing capacities");
        }
        var b1=result.Beams[0];
        Check(b1.CantileverImbalanceMm<1e-6,"B1 has centred primary stock supports");
        foreach(var cross in result.CrossSupports)foreach(var solid in cross.Solids)
            Check(CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).All(other=>!solid.Conflicts(other)),"reported crossing physical clearance");
        Console.WriteLine($"REPORT completed {timer.Elapsed.TotalSeconds:0.000}s; {result.LegCount} legs; {checks} acceptance checks.");
    }
}
