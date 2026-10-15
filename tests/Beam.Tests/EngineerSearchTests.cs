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
        Check(EngineerTowerSearch.MaximumStations(new TowerInput {LengthMm=2450,StockEndSetbackMm=150,Exclude600Standalone=true})==4,"short crossing searches stop at the manufactured support-span bound");
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
    public static void Junction(bool report=false)
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=report ? 10450 : 10800,MaximumConcreteEndOverhangMm=75,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,Exclude600Standalone=true,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        if(report)vm.AddNetworkBay();
        for(int i=0;i<vm.NetworkBeams.Count;i++)
        {
            bool deep=report ? i==2 : i==1;
            vm.NetworkBeams[i].WidthMm=deep ? 800 : 650;vm.NetworkBeams[i].DepthMm=deep ? 1750 : 1150;vm.NetworkBeams[i].BottomElevationMm=deep ? -1750 : -1150;
        }
        vm.NetworkBays[0].WidthMm=report ? 3550 : 2575;foreach(var bay in vm.NetworkBays)bay.ThicknessMm=300;
        if(report)vm.NetworkBays[1].WidthMm=2575;
        vm.AddCrossingBeam();vm.CrossingBeams[0].PositionMm=report ? 2500 : 5375;vm.CrossingBeams[0].WidthMm=report ? 600 : 650;vm.CrossingBeams[0].DepthMm=report ? 600 : 1150;vm.CrossingBeams[0].BottomElevationMm=report ? -600 : -1150;
        var task=vm.OptimizeTowersAsync(false);var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.Wait(TimeSpan.FromSeconds(30)))
        {
            Console.WriteLine(vm.BuildDiagnosticReport("engineer junction progress"));
            if(timer.Elapsed.TotalSeconds>=(report ? 180 : 90)){vm.CancelOptimization();task.GetAwaiter().GetResult();throw new Exception("Engineer junction exceeded its regression time budget");}
        }
        task.GetAwaiter().GetResult();if(!vm.IsCalculationValid)Console.WriteLine(vm.BuildDiagnosticReport("engineer junction failure"));Check(vm.IsCalculationValid,vm.WarningMessage+" "+vm.TowerFailureDetails);
        Console.WriteLine("ENGINEER JUNCTION "+(report ? "REPORT" : "ORIGINAL")+" seconds="+timer.Elapsed.TotalSeconds);Console.WriteLine(vm.NetworkResult.LayoutDiagnostics);
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
        // The same receiver lower rows recur under many longitudinal primary layouts.
        // Compare reuse with a fresh calculation and invalidate on changed crossing load.
        var junction=network.WithSpacings();junction.CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3300,WidthMm=650,DepthMm=200,BottomElevationMm=-200}};
        common.ReuseSearchWork=true;common.CrossLowerCases=null;
        bool original=CrossBeamSupportOptimizer.CheckJunctionReceiver(common,junction,0,result.Beams[0]);
        Check(common.CrossLowerCases!=null && common.CrossLowerCases.Count>0,"crossing lower case is cached");
        Func<double[]> demands=()=>common.CrossLowerCases.Values.SelectMany(v=>v.Members.Where(m=>m!=null)).SelectMany(m=>new[] {m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm}.Concat(m.Analysis.Reactions)).ToArray();
        var checkedDemands=demands();
        Check(CrossBeamSupportOptimizer.CheckJunctionReceiver(common,junction,0,result.Beams[0])==original && demands().SequenceEqual(checkedDemands),"identical junction reuses identical demands");
        common.CrossLowerCases.Clear();
        Check(CrossBeamSupportOptimizer.CheckJunctionReceiver(common,junction,0,result.Beams[0])==original && demands().SequenceEqual(checkedDemands),"fresh and cached crossing lower cases agree exactly");
        junction.CrossingBeams[0].DepthMm=300;junction.CrossingBeams[0].BottomElevationMm=-300;
        CrossBeamSupportOptimizer.CheckJunctionReceiver(common,junction,0,result.Beams[0]);
        Check(common.CrossLowerCases.Count>=2,"changing crossing load does not reuse a stale lower case");
        Console.WriteLine("Engineer search checks passed.");
    }
}
