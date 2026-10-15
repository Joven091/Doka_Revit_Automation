using System;
using System.Linq;
using EngineeringDashboard;

internal static class EngineerSearchTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Engineer search: "+message);}
    static void Verify(TransferNetworkResult result)
    {
        foreach(var beam in result.Beams)
        {
            Check(beam.EngineerSearch && beam.Transfer.FullLoadingOnly,"production policy propagated");
            Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r<=beam.LegCapacityKn+1e-8),"each actual leg passes configured capacity");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                Check(member.Utilization<=1+1e-8 && member.Analysis.DeflectionEvaluated,"unchanged bending/shear/deflection checks pass");
                Check(Math.Abs(member.Analysis.Reactions.Sum()-member.Analysis.TotalLoadKn)<1e-6,"member force equilibrium");
            }
        }
    }
    public static void Run()
    {
        var input=new TowerInput {LegCapacityKn=70};
        Check(EngineerTowerSearch.AssumedLegKn(input)==35 && EngineerTowerSearch.InitialLegs(input,28.688*7)==6,"double H20 starts at six legs per line");
        input.PrimaryMaterial=PrimaryBeamMaterial.WS10;
        Check(EngineerTowerSearch.AssumedLegKn(input)==70 && EngineerTowerSearch.InitialLegs(input,28.688*7)==3,"waler uses 70 kN seed");
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {input.PrimaryMaterial=material;input.LegCapacityKn=20;Check(EngineerTowerSearch.AssumedLegKn(input)==20 && EngineerTowerSearch.InitialLegs(input,200)==10,"configured lower leg capacity wins");}
        input=new TowerInput {LengthMm=7000,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,Exclude600Standalone=true,EngineerSearch=true};
        var candidates=EngineerTowerSearch.Geometries(new[] {4500.0,2450},false,input).ToArray();
        Check(candidates.Any(g=>g.Positions.Length==6 && g.Joined.Count(j=>j)==3 && g.Joined.Select((j,i)=>!j || Math.Abs(g.Positions[i+1]-g.Positions[i]-1500)<1e-7).All(v=>v)),"three 1500 mm separate towers survive stock-joint enumeration");
        Check(candidates.Any(g=>g.Positions.Length==6 && g.Joined.Count(j=>j)==4),"two combined three-frame towers are considered");
        var loose=new TowerLayout {EngineerSearch=true,PositionsMm=new[] {0.0,1500,2500,4000,5000,6500},Joined=new[] {true,false,true,false,true},WidthMm=1524};
        var combined=new TowerLayout {EngineerSearch=true,PositionsMm=new[] {0.0,1500,3000,3500,5000,6500},Joined=new[] {true,true,false,true,true},WidthMm=1524};
        Check(TowerOptimizer.CompareBracePreference(loose,combined)<0,"separate towers rank first at equal leg count");
        Check(EngineerTowerSearch.BracePenalty(new[] {0.0,1500},new[] {true},1524,false)<EngineerTowerSearch.BracePenalty(new[] {0.0,1000},new[] {true},1524,false),"1500 mm preferred over 1000 mm");
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var beam=TowerOptimizer.Optimize(new TowerInput {EngineerSearch=true,LengthMm=2450,TotalLineLoad=20,PrimaryMaterial=material,LegCapacityKn=15,DeflectionDenominator=270,StockLengthsMm=new[] {2450.0},MaximumConcreteEndOverhangMm=100});
            Check(beam.EngineerSearch && beam.MaxLegKn<=15+1e-8 && beam.Utilization<=1+1e-8,"actual lower leg capacity is checked for both materials");
        }
        Reported();
    }
    public static void Junction()
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=10800,MaximumConcreteEndOverhangMm=75,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        vm.NetworkBeams[0].WidthMm=650;vm.NetworkBeams[0].DepthMm=1150;vm.NetworkBeams[0].BottomElevationMm=-1150;
        vm.NetworkBeams[1].WidthMm=800;vm.NetworkBeams[1].DepthMm=1750;vm.NetworkBeams[1].BottomElevationMm=-1750;
        vm.NetworkBays[0].WidthMm=2575;vm.NetworkBays[0].ThicknessMm=300;vm.AddCrossingBeam();vm.CrossingBeams[0].PositionMm=5375;vm.CrossingBeams[0].WidthMm=650;vm.CrossingBeams[0].DepthMm=1150;vm.CrossingBeams[0].BottomElevationMm=-1150;
        var task=vm.OptimizeTowersAsync(false);var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.Wait(TimeSpan.FromSeconds(30)))
        {
            Console.WriteLine(vm.BuildDiagnosticReport("engineer junction progress"));
            if(timer.Elapsed.TotalSeconds>=90){vm.CancelOptimization();task.GetAwaiter().GetResult();throw new Exception("Engineer junction exceeded 90 seconds");}
        }
        task.GetAwaiter().GetResult();Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        Console.WriteLine("ENGINEER JUNCTION seconds="+timer.Elapsed.TotalSeconds);Console.WriteLine(vm.NetworkResult.LayoutDiagnostics);
    }
    static void Reported()
    {
        var common=new TowerInput {FullLoadingOnly=true,EngineerSearch=true,LengthMm=7000,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports};
        var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm,BeamSpacingsMm=Enumerable.Repeat(305.0,4).ToArray(),SlabSpacingsMm=Enumerable.Repeat(488.0,3).ToArray(),CrossingSpacingsMm=new double[0],UpperSpacingMm=305,
            Beams=Enumerable.Range(0,4).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300},new TransferBayRow {WidthMm=2500,ThicknessMm=200},new TransferBayRow {WidthMm=2000,ThicknessMm=200}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();
        common.NetworkSearchWorkers=1;var result=TransferNetworkOptimizer.Optimize(common,network);Verify(result);
        Console.WriteLine("ENGINEER REPORT seconds="+timer.Elapsed.TotalSeconds+" legs="+string.Join(",",result.Beams.Select(b=>b.LegCount)));
        Console.WriteLine(result.LayoutDiagnostics);
        foreach(var b in result.Beams)Console.WriteLine("ENGINEER positions="+string.Join(",",b.PositionsMm)+" joined="+string.Join(",",b.Joined)+" peak="+b.MaxLegKn);
        Check(result.Beams[0].LegCount<=12 && result.Beams[1].LegCount<=12,"reported B1 and B2 use at most six legs per line");
        Check(result.Beams.Take(2).All(b=>TowerOptimizer.StandaloneBraces(b.PositionsMm,b.Joined,b.WidthMm,b.Rotated).Count(brace=>Math.Abs(brace-1500)<1e-7)>=2),"B1/B2 retain a majority of 1500 mm separate towers");
        common.NetworkSearchWorkers=Math.Min(4,Environment.ProcessorCount);common.ReuseSearchWork=false;
        var repeat=TransferNetworkOptimizer.Optimize(common,network);Verify(repeat);
        Check(result.Schedule==repeat.Schedule && result.LayoutDiagnostics==repeat.LayoutDiagnostics,"serial/reused and parallel/uncached searches agree exactly");
        Console.WriteLine("Engineer search checks passed.");
    }
}
