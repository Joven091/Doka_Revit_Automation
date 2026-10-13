using System;
using System.Diagnostics;
using System.Linq;
using EngineeringDashboard;

// Reproduce the uploaded drawing inputs through the interactive configuration path.
internal static class DiagnosticBudgetBenchmark
{
    public static void Run()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,TransferSlabLoads=true,UseBeamNetwork=true,
            BeamLengthMm=6900,TowerLegCapacityKn=70,StockLengthsText=TowerStockPlanner.DefaultLengths,
            StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,Exclude600Standalone=false,
            AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,
            PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,
            DeflectionLimit=BeamCalculatorViewModel.AvailableDeflectionOptions.Single(d=>d.Denominator==270)};
        vm.AddNetworkBay();vm.AddNetworkBay();
        for(int i=0;i<4;i++) {vm.NetworkBeams[i].WidthMm=i%2==0 ? 800 : 650;vm.NetworkBeams[i].SetDepthKeepingTopMm(i%2==0 ? 1750 : 1150);}
        double[] widths={2278,2272,2252};
        for(int i=0;i<3;i++) {vm.NetworkBays[i].WidthMm=widths[i];vm.NetworkBays[i].ThicknessMm=300;}
        foreach(int bay in new[] {2,3})vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=bay,PositionMm=3450,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.Automatic});
        var timer=Stopwatch.StartNew();vm.OptimizeTowersAsync().GetAwaiter().GetResult();timer.Stop();
        if(vm.IsCalculationValid)
        {
            var result=vm.NetworkResult;
            foreach(var beam in result.Beams)
            {
                if(beam.MaxLegKn>70+1e-8)throw new Exception("Retained leg capacity checks failed");
                foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
                    if(!member.Analysis.DeflectionEvaluated || member.Utilization>1+1e-8)throw new Exception("Retained member checks failed");
            }
            if(result.CrossSupports.Length!=2 || result.CrossSupports.SelectMany(c=>c.Members).Any(m=>!m.Analysis.DeflectionEvaluated || m.Utilization>1+1e-8))throw new Exception("Retained crossing checks failed");
        }
        Console.WriteLine(FormattableString.Invariant($"REPORT-GEOMETRY seconds={timer.Elapsed.TotalSeconds:0.000} runtime={Environment.Version} processors={Environment.ProcessorCount} valid={vm.IsCalculationValid}"));
        Console.WriteLine(vm.BuildDiagnosticReport("Uploaded 2026-10-13 geometry; interactive 25-second exploration budget. WPF paint is excluded from this console benchmark."));
    }
}
