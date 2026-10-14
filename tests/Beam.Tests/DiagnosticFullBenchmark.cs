using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

// Exact committed inputs in the uploaded 2026-10-14 diagnostic, through the normal UI model.
internal static class DiagnosticFullBenchmark
{
    internal static void Run()
    {
        var vm=new BeamCalculatorViewModel {
            UseTowers=true,HasSelectedSupportSystem=true,TransferSlabLoads=true,UseBeamNetwork=true,
            Depth=1450,Width=800,BeamLengthMm=6900,FloorHeight=3000,TowerLegCapacityKn=70,
            HasRightSlab=true,RightSlabThickness=300,
            StockLengthsText=TowerStockPlanner.DefaultLengths,StockEndSetbackMm=150,
            MaximumConcreteEndOverhangMm=150,Exclude600Standalone=false,
            AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,
            PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,PlywoodDeflectionLimit="L/350",
            PlywoodProfileId="doka-18mm-9732-103",
            DeflectionLimit=BeamCalculatorViewModel.AvailableDeflectionOptions.Single(d=>d.Denominator==270)
        };
        vm.AddNetworkBay();
        for(int i=0;i<3;i++) {
            vm.NetworkBeams[i].WidthMm=i==1 ? 800 : 650;
            vm.NetworkBeams[i].SetDepthKeepingTopMm(i==1 ? 1750 : 1150);
        }
        double[] widths={2272,2252};
        for(int i=0;i<2;i++) {vm.NetworkBays[i].WidthMm=widths[i];vm.NetworkBays[i].ThicknessMm=300;}
        foreach(int bay in new[] {1,2})vm.CrossingBeams.Add(new TransferCrossingBeamRow {
            BayNumber=bay,PositionMm=3450,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.Automatic
        });
        Console.WriteLine($"BASELINE start runtime={Environment.Version} processors={Environment.ProcessorCount}; 2026-10-14 inputs; no elapsed-time limit");
        var timer=Stopwatch.StartNew();var task=vm.OptimizeTowersAsync();
        double nextProgress=30;
        while(!task.IsCompleted) {
            Thread.Sleep(200);
            if(timer.Elapsed.TotalSeconds>=nextProgress) {
                Console.WriteLine(FormattableString.Invariant($"BASELINE progress seconds={timer.Elapsed.TotalSeconds:0.000} work={vm.SearchProgressText}"));
                nextProgress+=30;
            }
        }
        task.GetAwaiter().GetResult();timer.Stop();
        Console.WriteLine(FormattableString.Invariant($"BASELINE seconds={timer.Elapsed.TotalSeconds:0.000} valid={vm.IsCalculationValid}"));
        Console.WriteLine(vm.BuildDiagnosticReport("Exact uploaded 2026-10-14 inputs. Normal full search; console timing excludes WPF painting and Revit host overhead."));
        if(!vm.IsCalculationValid || vm.NetworkResult==null)throw new Exception("Baseline did not produce a complete valid network: "+vm.WarningMessage);
        var result=vm.NetworkResult;
        foreach(var beam in result.Beams) {
            if(beam.MaxLegKn>70+1e-8)throw new Exception("Leg capacity check failed");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
                if(!member.Analysis.DeflectionEvaluated || member.Utilization>1+1e-8)throw new Exception("Member strength/deflection check failed");
        }
        if(result.CrossSupports.Length!=2 || result.CrossSupports.SelectMany(c=>c.Members).Any(m=>!m.Analysis.DeflectionEvaluated || m.Utilization>1+1e-8))
            throw new Exception("Crossing support check failed");
        Console.WriteLine("BASELINE complete network, leg, member and crossing checks passed.");
    }
}
