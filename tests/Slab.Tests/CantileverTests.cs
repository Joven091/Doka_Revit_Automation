using System;
using System.Linq;
using EngineeringDashboard;
using MyRevitAddin.Models;
using OxyPlot.Series;

internal static class CantileverTests
{
    internal static void Run(Action<bool, string> check, Action<double, double, string> equal)
    {
        const double ei = 449.8756;
        // Two supports: independent closed form, positive downward.
        // delta = q/EI * (a^4/8 + a^3 L/4 - a L^3/24).
        equal(1000.0 / (3 * ei), ContinuousBeamResponse.Analyze(2, 1, 1, 1, ei).SignedTipDeflection * 1000, "3 m H20 tip at 1 kN/m");
        equal(5000.0 / (3 * ei), ContinuousBeamResponse.Analyze(2, 1, 1, 5, ei).SignedTipDeflection * 1000, "3 m H20 tip at 5 kN/m");
        foreach (double length in new[] { .5, 1, 2 })
        foreach (double a in new[] { 0, .1, .5, 1 })
        {
            double expected = 3 / ei * (Math.Pow(a, 4) / 8 + Math.Pow(a, 3) * length / 4 - a * Math.Pow(length, 3) / 24);
            equal(expected, ContinuousBeamResponse.Analyze(2, length, a, 3, ei).SignedTipDeflection, "two-support signed closed form");
        }
        check(ContinuousBeamResponse.Analyze(2, 2, .1, 1, ei).SignedTipDeflection < 0, "span rotation can lift free tip without support uplift");
        double offset = ContinuousBeamResponse.Analyze(2, 1, .4, 1, ei).SignedTipDeflection;
        check(offset > 0 && offset < Math.Pow(.4, 4) / (8 * ei), "rotation can partly offset overhang bending");
        var invalid = new FlexCalculatorViewModel { PropCount = "2", PropSpacing = 1000, PrimaryBeamCantilever = 1000 };
        check(!invalid.IsCalculationValid && invalid.WarningMessage.Contains("Primary H20 end deflection"), "formerly unchecked primary tip rejects layout");
        check(invalid.SuggestedPrimaryBeamSpacing == 0 && invalid.PrimaryDiagramViewModel == null && !invalid.ViewPrimaryBeamDiagramCommand.CanExecute(null), "tip failure clears output and diagram");
        invalid.PrimaryBeamCantilever = 500;
        check(invalid.IsCalculationValid, "shorter primary overhang recovers");
        foreach (double a in new[] { 0, .1, .5, 1, 2 })
        {
            var envelope = SlabSupportEnvelope.Calculate(1, a, 1, ei);
            for (int n = 5; n <= 100; n++)
                check(ContinuousBeamResponse.Analyze(n, 1, a, 1, ei).CantileverDeflection <= envelope.CantileverDeflection + 1e-10, "five-plus tip envelope");
            foreach (int n in new[] { 2, 3, 4, 5, 6 })
            {
                var response = ContinuousBeamResponse.Analyze(n, 1, a, 3, ei);
                var plots = DiagramDataGenerator.GeneratePlots(n, 1, a, a, 3, ei, "Tip check");
                var points = ((LineSeries)plots.deflectionPlot.Series[0]).Points;
                equal(-response.SignedTipDeflection * 1000, points.First().Y, "independent Macaulay left tip");
                equal(-response.SignedTipDeflection * 1000, points.Last().Y, "independent Macaulay right tip");
            }
        }
        // Both layers must satisfy their adopted a/denominator limit in every returned layout.
        foreach (string n in new[] { "2", "3", "4", "5+" })
        foreach (int a in new[] { 0, 250, 500, 750 })
        foreach (string limit in new[] { "L/270", "L/350", "L/500" })
        {
            var v = new FlexCalculatorViewModel { PrimaryBeamCount = n, PropCount = n,
                PropSpacing = 750, PrimaryBeamCantilever = a, SecondaryBeamCantilever = a, DeflectionLimit = limit };
            if (!v.IsCalculationValid) continue;
            int count = n == "5+" ? 5 : int.Parse(n);
            double denominator = double.Parse(limit.Substring(2)), q = SlabLoadHelper.CalculateTotalLoad(v.SlabThickness);
            var sec = SlabSupportEnvelope.ForSelection(count, v.SuggestedPrimaryBeamSpacing / 1000, a / 1000.0, 1, ei);
            var prim = SlabSupportEnvelope.ForSelection(count, .75, a / 1000.0, 1, ei);
            check(sec.CantileverDeflection * q * v.SecondaryBeamSpacing/1000 <= a / 1000.0 / denominator + 1e-10, "returned secondary tip passes");
            check(prim.CantileverDeflection * q * sec.Reaction <= a / 1000.0 / denominator + 1e-10, "returned primary tip passes");
        }
    }
}