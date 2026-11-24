using System;
using System.Linq;
using EngineeringDashboard;

internal static class AccessTests
{
    internal static void Run(Action<bool,string> check, Action<double,double,string> near)
    {
        var original = new FlexCalculatorViewModel();
        double originalSpacing = original.SuggestedPrimaryBeamSpacing;
        near(.75, original.TotalAccessLoadKnM2, "Access defaults to total 0.75 kPa including deck");
        check(!original.PrimaryHasAccess && !original.SecondaryHasAccess && !original.UsesAccessAnalysis, "Legacy full-load calculation remains the default");
        foreach (string end in new[] { "Left", "Right", "Both" })
        {
            var vm = new FlexCalculatorViewModel { PrimaryBeamCount = "2", PropCount = "2", PropSpacing = 1000,
                PrimaryBeamCantilever = 450, SecondaryBeamCantilever = 150,
                PrimaryAccessEnd = end, PrimaryAccessLeftMm = 350, PrimaryAccessRightMm = 350, PrimaryHasAccess = true };
            check(vm.IsCalculationValid, "Primary " + end + " access finds a checked layout: " + vm.WarningMessage);
            check(vm.PrimaryAccessAnalysis != null && vm.SecondaryAccessAnalysis != null, "Both layer responses are available");
            near(end == "Both" ? 2 : 1, vm.PrimaryAccessAnalysis.Loads.Count(q => q.IsAccess), "Correct number of primary access UDL zones");
            check(vm.AccessReport().Split('\n').Length == 2 && vm.AccessReport().Split('\n').All(r => r.StartsWith("All loads present")),
                "One fully loaded result per receiving primary");
            double expectedAccess = ContinuousBeamResponse.Analyze(2, vm.SuggestedPrimaryBeamSpacing / 1000, .15, .75 * vm.SecondaryBeamSpacing / 1000, 449.8756).Reaction / (vm.SecondaryBeamSpacing / 1000);
            near(expectedAccess, vm.PrimaryAccessAnalysis.Loads.First(q => q.IsAccess).Load, "Access deck is included once; total pressure is not increased by 0.4");
            near(vm.SecondaryAccessAnalysis.Reactions[0] / (vm.SecondaryBeamSpacing / 1000), vm.PrimaryAccessAnalysis.Loads.Single(q => !q.IsAccess).Load, "This receiving primary uses its corresponding secondary reaction");
            var zones = vm.PrimaryDiagramViewModel.DiagramRows[0].Beam.Loads;
            near(vm.PrimaryAccessAnalysis.Loads.Length, zones.Length, "UDL diagram uses the selected slab/access zones");
            check(vm.PrimaryDiagramViewModel.DiagramRows[0].CaseText.Contains("2 actual supports"), "Diagram labels actual support count");
            check(!vm.PrimaryAccessAnalysis.HasUplift && !vm.SecondaryAccessAnalysis.HasUplift, "Fully loaded beams retain support contact");
            check(vm.PrimaryAccessAnalysis.Reaction <= vm.PropCapacityLimit + 1e-8 && vm.PrimaryAccessAnalysis.DeflectionUtilization <= 1 + 1e-8, "Governing primary passes prop and deflection limits");
            vm.PrimaryHasAccess = false;
            check(!vm.UsesAccessAnalysis && vm.AccessPreviewAnalysis == null && vm.AccessReport() == "", "Disabling access removes stale segmented diagrams");
        }
        var both = new FlexCalculatorViewModel { PrimaryBeamCount = "3", PropCount = "3", PropSpacing = 750,
            PrimaryBeamCantilever = 250, SecondaryBeamCantilever = 250, PrimaryAccessEnd = "Both", SecondaryAccessEnd = "Both",
            PrimaryAccessLeftMm = 150, PrimaryAccessRightMm = 150, SecondaryAccessLeftMm = 150, SecondaryAccessRightMm = 150,
            PrimaryHasAccess = true, SecondaryHasAccess = true };
        check(both.IsCalculationValid, "Access on both axes finds a valid layout: " + both.WarningMessage);
        near(3, both.PrimaryAccessAnalysis.Loads.Length, "Both primary ends plus slab zone");
        near(3, both.SecondaryAccessAnalysis.Loads.Length, "Both secondary ends plus slab zone");
        var label = both.PrimaryDiagramViewModel.DiagramRows[0].CaseText;
        int start = label.IndexOf("primary P", StringComparison.Ordinal) + "primary P".Length;
        int line = int.Parse(label.Substring(start).Split(' ')[0]) - 1;
        near(both.SecondaryAccessAnalysis.Reactions[line] / (both.SecondaryBeamSpacing / 1000), both.PrimaryAccessAnalysis.Loads.Single(q => !q.IsAccess).Load, "Secondary reactions remain paired to their receiving primary");
        check(both.PrimaryAccessAnalysis.Loads.All(q => q.Load > 0) && both.SecondaryAccessAnalysis.Loads.All(q => q.Load > 0), "Both access ends fully loaded on both axes");
        both.PrimaryBeamCount = "5+"; both.PropCount = "5+";
        both.ExactPrimaryBeamCount = 7; both.ExactPropCount = 8;
        check(both.ShowExactPrimaryCount && both.ShowExactPropCount && both.IsCalculationValid, "Partial loads use entered actual counts: " + both.WarningMessage);
        near(7, both.SecondaryAccessAnalysis.Supports.Length, "Secondary uses seven actual support lines");
        near(8, both.PrimaryAccessAnalysis.Supports.Length, "Primary uses eight actual props");
        both.ExactPropCount = 4; check(!both.IsCalculationValid && both.AccessReport() == "", "Invalid exact count clears results");
        both.ExactPropCount = 8; check(both.IsCalculationValid, "Exact count correction recovers");
        both.TotalAccessLoadKnM2 = double.NaN; check(!both.IsCalculationValid && both.PrimaryDiagramViewModel == null, "Invalid active access load clears diagrams");
        both.PrimaryHasAccess = both.SecondaryHasAccess = false;
        check(both.IsCalculationValid, "Inactive invalid access load is ignored");
        both.SecondaryHasAccess = true; check(!both.IsCalculationValid, "Invalid load blocks again when access is enabled");
        original.PrimaryRightCantilever = 150;
        check(original.UsesAccessAnalysis, "Unequal overhangs select exact response without requiring access");
        original.PrimaryBeamCantilever = 500;
        near(originalSpacing, original.SuggestedPrimaryBeamSpacing, "Legacy symmetric setter restores identical full-load spacing");
        original.PrimaryHasAccess = true; original.PrimaryAccessRightMm = 50000;
        check(!original.IsCalculationValid && original.WarningMessage.Contains("positive slab-loaded"), "Overlapping access zones cannot erase the slab");
        original.PrimaryAccessRightMm = 250;
        check(original.IsCalculationValid, "Corrected access geometry recovers");
        var highAccess = new FlexCalculatorViewModel { PrimaryBeamCount = "2", PropCount = "2", PropSpacing = 500,
            PrimaryBeamCantilever = 150, SecondaryBeamCantilever = 150, PrimaryAccessRightMm = 50, PrimaryHasAccess = true, TotalAccessLoadKnM2 = 30 };
        check(highAccess.PlywoodResult != null, "Higher access pressure is checked against the plywood chart");
        check(highAccess.IsCalculationValid, "Fully loaded layout passes despite uplift in the former before-pour case");
        near(30, highAccess.PlywoodResult.PressureKnM2, "Access pressure above slab pressure governs plywood");
        near(350, highAccess.SecondaryBeamSpacing, "Access deck pressure constrains secondary row spacing");
    }
}
