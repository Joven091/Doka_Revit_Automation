using System;
using System.Linq;
using EngineeringDashboard;

internal static class UpliftTests
{
    internal static void Run(Action<bool, string> check, Action<double, double, string> equal)
    {
        // Independent three-support solution: Rmiddle/q = 1.25L - 1.5a²/L.
        var negative = SlabSupportEnvelope.ForSelection(3, 1, 1, 1, 1);
        equal(-0.25, negative.MinimumReaction, "signed middle reaction retained");
        check(negative.HasUplift && negative.MinimumReactionSupport == 2, "middle contact lost");
        double boundary = Math.Sqrt(5.0 / 6);
        check(!SlabSupportEnvelope.ForSelection(3, 1, boundary, 1, 1).HasUplift, "zero contact arithmetic tolerance");
        check(!SlabSupportEnvelope.ForSelection(3, 1, boundary - 1e-7, 1, 1).HasUplift, "positive boundary contact");
        check(SlabSupportEnvelope.ForSelection(3, 1, boundary + 1e-7, 1, 1).HasUplift, "negative boundary rejected");
        foreach (double load in new[] { 1e-6, 1.0, 1e6 })
            check(SlabSupportEnvelope.ForSelection(3, 1, 1, load, 1).HasUplift, "uplift is load-scale independent");
        check(!SlabSupportEnvelope.ForSelection(2, 1, 2, 1, 1).HasUplift, "two supports retain symmetric contact");
        foreach (double a in new[] { 0, .5, .9, 1, 2, 5 })
        {
            var envelope = SlabSupportEnvelope.Calculate(1, a, 1, 1);
            for (int n = 5; n <= 100; n++)
                check(ContinuousBeamResponse.Analyze(n, 1, a, 1, 1).MinimumReaction >= envelope.MinimumReaction - 1e-9, "5+ minimum bounds higher counts");
        }
        var v = new FlexCalculatorViewModel { PrimaryBeamCantilever = 1000 };
        check(!v.IsCalculationValid && v.WarningMessage.Contains("Primary H20") && v.WarningMessage.Contains("prop 2"), "primary uplift identifies support");
        equal(0, v.SuggestedPrimaryBeamSpacing, "primary uplift clears spacing");
        equal(2300, v.PropLookupExtensionMm, "uplift retains valid extension lookup");
        check(v.PrimaryDiagramViewModel == null && !v.ViewPrimaryBeamDiagramCommand.CanExecute(null), "primary uplift clears stale diagram");
        v.SecondaryBeamSpacing = 100;
        check(!v.IsCalculationValid && v.WarningMessage.Contains("Uplift"), "load reduction does not fix primary geometry");
        v.PrimaryBeamCantilever = 500;
        check(v.IsCalculationValid, "geometry repair restores calculation");
        v.PropCount = "5+"; v.PropSpacing = 500; v.PrimaryBeamCantilever = 600;
        check(!v.IsCalculationValid && v.WarningMessage.Contains("case of 5+"), "5+ uplift identifies modeled count");

        // Deliberately extreme overhang: every candidate in the 100–4000 mm search loses contact.
        // Small secondary tributary width isolates contact from the cantilever strength precheck.
        v = new FlexCalculatorViewModel { SlabThickness = 100, FloorHeight = 2518,
            PropCount = "2", PropSpacing = 500, PrimaryBeamCantilever = 0,
            SecondaryBeamSpacing = 10, SecondaryBeamCantilever = 3700, PrimaryBeamCount = "3" };
        check(!v.IsCalculationValid && v.WarningMessage.Contains("Secondary H20") && v.WarningMessage.Contains("support 2"), "secondary no-contact solution identifies support");
        check(v.WarningMessage.Contains("4000 mm") && v.WarningMessage.Contains("strength and span-deflection checks pass"), "secondary conflict explains candidate rejection");
        equal(0, v.SuggestedPrimaryBeamSpacing, "secondary uplift clears spacing");
        check(v.SecondaryDiagramViewModel == null && !v.ViewSecondaryBeamDiagramCommand.CanExecute(null), "secondary uplift blocks diagrams");
        v.SecondaryBeamCantilever = 500;
        check(v.IsCalculationValid, "secondary overhang repair recovers");

        // Search varied secondary geometries and assert every returned spacing keeps contact.
        foreach (int a in new[] { 250, 500, 750, 1000 })
        foreach (int h in new[] { 100, 300, 600 })
        foreach (int secondarySpacing in new[] { 100, 250, 488 })
        foreach (string count in new[] { "2", "3", "4", "5+" })
        {
            v = new FlexCalculatorViewModel { SlabThickness = h, FloorHeight = h + 2418,
                SecondaryBeamCantilever = a, SecondaryBeamSpacing = secondarySpacing, PrimaryBeamCount = count };
            if (!v.IsCalculationValid) continue;
            int n = count == "5+" ? 5 : int.Parse(count);
            foreach (int actualCount in count == "5+" ? new[] { 5, 6, 10, 30 } : new[] { n })
                check(ContinuousBeamResponse.Analyze(actualCount, v.SuggestedPrimaryBeamSpacing / 1000, a / 1000.0, 1, 1).Reactions.Min() >= -1e-9, "returned spacing retains every secondary support");
        }
    }
}