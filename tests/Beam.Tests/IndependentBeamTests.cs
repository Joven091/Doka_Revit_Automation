using System;
using System.Linq;
using EngineeringDashboard;

internal static class IndependentBeamTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Independent beams: "+message);}
    internal static void Run()
    {
        var defaults=new BeamCalculatorViewModel();
        Check(defaults.PlywoodDeflectionLimit=="L/350" && defaults.DeflectionLimit.Denominator==270,"independent plywood and H20 defaults");
        Check(!defaults.Exclude600Standalone && !defaults.AllowCustomH20Lengths && defaults.IgnoreUplift && defaults.MaximumConcreteEndOverhangMm==150,"requested support and end-allowance defaults");
        defaults.IgnoreUplift=false;Check(defaults.IgnoreUplift,"saved/programmatic false cannot disable the fixed uplift exclusion");
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var vm=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,TransferSlabLoads=true,UseBeamNetwork=true,BeamLengthMm=2500,TowerLegCapacityKn=70,PrimaryMaterial=material};
            foreach(var beam in vm.NetworkBeams){beam.WidthMm=300;beam.DepthMm=600;}
            vm.NetworkBays[0].WidthMm=2900;vm.NetworkBays[0].ThicknessMm=100;
            var independent=new IndependentBeamRow {StartXmm=-1200,StartYmm=-250,IsVertical=true,LengthMm=1800,WidthMm=200,DepthMm=200,BottomElevationMm=-200};
            vm.IndependentBeams.Add(independent);
            vm.IndependentBeams.Add(new IndependentBeamRow {StartXmm=-5200,StartYmm=6000,LengthMm=3900,WidthMm=300,DepthMm=600});
            Check(independent.EndXmm==-1200 && independent.EndYmm==1550,"independent endpoints do not clamp to main beam extents");
            string saved=vm.NetworkConfigurationXml;var restored=new BeamCalculatorViewModel {UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true};restored.NetworkConfigurationXml=saved;
            Check(restored.IndependentBeams.Count==2 && restored.IndependentBeams[0].LengthMm==1800 && restored.IndependentBeams[0].IsVertical && restored.IndependentBeams[1].StartXmm==-5200 && restored.IndependentBeams[1].LengthMm==3900,"geometry and orientation round-trip");
            vm.OptimizeTowersAsync().GetAwaiter().GetResult();Check(vm.IsCalculationValid,"complete document calculation: "+vm.WarningMessage+" "+vm.TowerFailureDetails);
            Check(independent.MaximumSecondarySpacingMm>vm.NetworkBeams[0].MaximumSecondarySpacingMm && vm.IndependentBeamResults[0].Transfer.EnteredUpperSpacingMm==independent.MaximumSecondarySpacingMm,"independent shallow beam uses its own spacing rather than the deepest main beam spacing");
            Check(vm.IndependentBeamResults.Length==2 && vm.IndependentBeamResults[0].LengthMm==1800 && vm.IndependentBeamResults[1].LengthMm==3900 && vm.NetworkResult.Beams.All(b=>b.LengthMm==2500),"every solver uses its beam's own length");
            double expectedSlab=vm.NetworkResult.Input.AreaLoad(100)*2.9*2.5;
            Check(Math.Abs(vm.NetworkResult.SlabLoadKn-expectedSlab)<1e-7,"independent beams do not alter connected slab allocation");
            for(int i=0;i<2;i++)
            {
                var row=vm.IndependentBeams[i];var result=vm.IndependentBeamResults[i];var members=result.Transfer.PrimaryCases.SelectMany(c=>c).Concat(result.Transfer.LowerCases.SelectMany(c=>c)).ToArray();
                Check(result.Primary.Material==material && result.IgnoreUplift && result.MaxLegKn<=70+1e-8 && members.All(m=>m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8),"independent primary, lower H20, deflection and leg checks pass");
                double expectedBeam=vm.NetworkResult.Input.AreaLoad(row.DepthMm)*row.WidthMm/1000*row.LengthMm/1000;
                double steel=result.Transfer.PrimaryCases.Last().Sum(m=>m.IsWS10 ? m.Primary.SelfWeightKnM(m.LengthMm)*m.LengthMm/1000 : 0);
                double column=result.Transfer.ColumnSupportLoadKn(result.Transfer.CaseNames.Length-1);
                Console.WriteLine($"Independent {material} I{i+1}: primary={result.TotalSupportedLoadKn:G12}, column={column:G12}, beam={expectedBeam:G12}, steel={steel:G12} kN");
                Check(result.Transfer.SlabLoadKn==0 && Math.Abs(result.TotalSupportedLoadKn+column-expectedBeam-steel)<1e-7 && members.All(m=>Math.Abs(m.Analysis.Reactions.Sum()-m.Analysis.TotalLoadKn)<1e-7),"own beam load and steel self-weight are conserved without fabricated slab loads");
            }
            Check(vm.BuildDiagnosticReport().Contains("I1: independent") && vm.BuildDiagnosticReport().Contains("INDEPENDENT BEAM RESULTS"),"diagnostics record independent geometry and load scope");
            independent.LengthMm=2450;Check(!vm.IsCalculationValid && vm.IndependentBeamResults.Length==0 && vm.NetworkResult==null,"endpoint/length edits clear all stale results");
            vm.IndependentBeams.RemoveAt(0);independent.DepthMm=900;Check(vm.IndependentBeams.Count==1,"removing a beam releases its input subscription");
            vm.StartNewSession();Check(vm.IndependentBeams.Count==0 && vm.IgnoreUplift && vm.DeflectionLimit.Denominator==270 && vm.MaximumConcreteEndOverhangMm==150,"new session resets independent geometry and retains requested defaults");
        }
        Console.WriteLine($"Passed {checks} independent beam model checks.");
    }
}
