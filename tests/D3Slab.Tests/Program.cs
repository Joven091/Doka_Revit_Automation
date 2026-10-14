using System;
using System.Linq;
using EngineeringDashboard;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-6 * Math.Max(1, Math.Abs(expected)), message + ": " + actual + " / " + expected);
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    static void AnalysisChecks()
    {
        var udl = new[] { new BeamDistributedLoad { StartMm = 0, EndMm = 2000, KnPerM = 10 } };
        var a = LoadedBeamAnalysis.Analyze(2000, new[] { 0.0, 2000 }, udl, null, 900, 350);
        Near(a.MomentKnM, 5, "Simply supported UDL moment");
        Near(a.ShearKn, 10, "Simply supported UDL shear");
        Near(a.DeflectionMm, 5.0 * 10 * 16 / 384 / 900 * 1000, "Simply supported UDL deflection");
        Near(a.Reactions.Sum(), 20, "UDL vertical equilibrium");
        a = LoadedBeamAnalysis.Analyze(2000, new[] { 0.0, 2000 }, null, new[] { new BeamPointLoad { PositionMm = 1000, Kn = 10 } }, 900, 350);
        Near(a.MomentKnM, 5, "Central point moment");
        Near(a.ShearKn, 5, "Central point shear");
        Near(a.DeflectionMm, 10.0 * 8 / 48 / 900 * 1000, "Central point deflection");
        Near(a.Reactions.Sum(), 10, "Point vertical equilibrium");

        // Compare against the independent three-moment solver, including support rotation.
        foreach (var supports in new[] { new[] { 175.0, 1675, 3275, 4200 }, new[] { 0.0, 1100, 2800, 4500 }, new[] { 650.0, 2650, 3850 } })
        foreach (int denominator in new[] { 500, 350, 270 })
        {
            var reference = TowerBeamAnalysis.Analyze(4.5, supports.Select(v => v / 1000).ToArray(), 7.8, 449.8756);
            a = LoadedBeamAnalysis.Analyze(4500, supports, new[] { new BeamDistributedLoad { StartMm = 0, EndMm = 4500, KnPerM = 7.8 } }, null, 449.8756, denominator);
            for (int i = 0; i < supports.Length; i++) Near(a.Reactions[i], reference.Reactions[i], "Unequal-span reaction");
            Near(a.MomentKnM, reference.Moment, "Unequal-span moment");
            Near(a.ShearKn, reference.Shear, "Unequal-span shear");
            Near(a.DeflectionMm, reference.DeflectionMm, "Unequal-span deflection with rotation");
            Near(a.DeflectionUtilization, reference.DeflectionUtilization(5, denominator), "Support-region deflection allowance");
            foreach (double x in new[] { 0.0, 500, 2100, 4300, 4500 })
                for (int k = 0; k < 3; k++) Near(a.Response(x)[k], reference.Response(x / 1000, x < 4500)[k], "Signed response");
        }
        var supportsMm = new[] { 200.0, 2000, 4050 };
        var distributed = new[] { new BeamDistributedLoad { StartMm = 0, EndMm = 1500, KnPerM = 3 }, new BeamDistributedLoad { StartMm = 1500, EndMm = 4500, KnPerM = 8 } };
        var points = new[] { new BeamPointLoad { PositionMm = 880, Kn = 5 }, new BeamPointLoad { PositionMm = 2000, Kn = 6 }, new BeamPointLoad { PositionMm = 4500, Kn = 2 } };
        var combined = LoadedBeamAnalysis.Analyze(4500, supportsMm, distributed, points, 900, 350);
        var uniformOnly = LoadedBeamAnalysis.Analyze(4500, supportsMm, distributed, null, 900, 350);
        var pointOnly = LoadedBeamAnalysis.Analyze(4500, supportsMm, null, points, 900, 350);
        for (int i = 0; i < supportsMm.Length; i++) Near(combined.Reactions[i], uniformOnly.Reactions[i] + pointOnly.Reactions[i], "Mixed-load reaction superposition");
        double loadMoment = distributed.Sum(p => p.KnPerM * (p.EndMm - p.StartMm) / 1000 * (p.EndMm + p.StartMm) / 2000) + points.Sum(p => p.Kn * p.PositionMm / 1000);
        Near(combined.Reactions.Select((r, i) => r * supportsMm[i] / 1000).Sum(), loadMoment, "Mixed-load moment equilibrium");
        foreach (double x in new[] { 100.0, 1000, 1900, 2900, 4400 })
            for (int k = 0; k < 3; k++) Near(combined.Response(x)[k], uniformOnly.Response(x)[k] + pointOnly.Response(x)[k], "Mixed-load response superposition");
        var half = combined.Scale(.5);
        Near(half.DeflectionUtilization, combined.DeflectionUtilization / 2, "Edge tributary scaling");
        Near(half.Reactions.Sum(), combined.TotalLoadKn / 2, "Scaled equilibrium");
        Reject(() => LoadedBeamAnalysis.Analyze(2000, new[] { 500.0 }, udl, null, 900, 350), "Single support rejected");
        Reject(() => LoadedBeamAnalysis.Analyze(2000, new[] { 500.0, 500 }, udl, null, 900, 350), "Duplicate supports rejected");
        Reject(() => combined.Response(double.NaN), "Invalid response position rejected");
    }
    static D3SlabLayout LayoutChecks(D3SlabInput input, string name)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var layout = D3SlabOptimizer.Optimize(input);
        Console.WriteLine(name + ": " + layout.Summary.Replace("\n", " | ") + " | " + layout.Tested + " candidates in " + watch.Elapsed.TotalSeconds.ToString("0.00") + " s");
        Check(layout.Utilization <= 1.000001, name + " all capacities and deflection pass");
        Check(layout.LegLoads.Cast<double>().All(v => v >= -1e-7 && v <= input.LegCapacityKn + 1e-7), name + " individual legs pass");
        Check(layout.EdgeXmm >= input.MinimumEdgeMm && layout.EdgeXmm <= input.MaximumEdgeMm && layout.EdgeYmm >= input.MinimumEdgeMm && layout.EdgeYmm <= input.MaximumEdgeMm, name + " edge allowance");
        Check(D3SlabOptimizer.SecondarySpacingsMm.Contains(layout.SecondarySpacingMm), name + " permitted secondary spacing");
        Near(layout.LegLoads.Cast<double>().Sum(), layout.CoveredAreaM2 * layout.AreaLoadKnM2, name + " covered load counted once");
        Near(layout.CoveredLengthMm + 2 * layout.EdgeXmm, layout.SwapAxes ? input.WidthMm : input.LengthMm, name + " primary direction fits slab");
        Near(layout.CoveredWidthMm + 2 * layout.EdgeYmm, layout.SwapAxes ? input.LengthMm : input.WidthMm, name + " secondary direction fits slab");
        Check(layout.Members.All(m => input.StockLengthsMm.Contains(m.Stock.LengthMm) && m.Analysis.Reactions.Length >= 2), name + " stock available, each piece has two supports");
        Near(layout.Members.Sum(m => m.Stock.LengthMm * (m.Primary ? 2 : 1)), layout.H20LengthMm, name + " material schedule");
        Near(layout.Members.Where(m => !m.Primary).Sum(m => m.Analysis.TotalLoadKn), layout.CoveredAreaM2 * layout.AreaLoadKnM2, name + " secondary overlap conserves area");
        Near(layout.Members.Where(m => m.Primary).Sum(m => m.Analysis.TotalLoadKn), layout.CoveredAreaM2 * layout.AreaLoadKnM2, name + " exact primary point loads");
        foreach (var member in layout.Members)
        {
            var stock = member.Stock;
            var supports = member.Primary ? layout.StationsMm : layout.PrimaryLinesMm;
            Near(supports.Count(v => v >= stock.StartMm - 1e-7 && v <= stock.EndMm + 1e-7), member.Analysis.Reactions.Length, name + " every physically crossed support modeled");
            Check(stock.StartMm >= 0 && stock.EndMm <= (member.Primary ? layout.CoveredLengthMm : layout.CoveredWidthMm) + 1e-7, name + " no stock outside coverage");
        }
        for (int i = 1; i < layout.SecondaryStock.Length; i++)
        {
            var previous = layout.SecondaryStock[i - 1]; var next = layout.SecondaryStock[i];
            double support = layout.PrimaryLinesMm[next.FirstSupport];
            Check(next.FirstSupport == previous.LastSupport && previous.EndMm - support >= 150 - 1e-7 && support - next.StartMm >= 150 - 1e-7, name + " supported 150 mm overlap ends");
        }
        for (int i = 1; i < layout.PrimaryStock.Length; i++) Near(layout.PrimaryStock[i - 1].EndMm, layout.PrimaryStock[i].StartMm, name + " primaries never overlap");
        if (input.Exclude600Standalone)
        {
            if (layout.RotatedTowers) Check(layout.TowerWidthMm != 600, name + " rotated standalone 600 excluded");
            for (int i = 0; i < layout.Joined.Length; i++)
                if (layout.Joined[i] && Math.Abs(layout.StationsMm[i + 1] - layout.StationsMm[i] - 600) < .01)
                    Check(i > 0 && layout.Joined[i - 1] || i + 1 < layout.Joined.Length && layout.Joined[i + 1], name + " 600 bay belongs to joined tower");
        }
        return layout;
    }
    static int Main()
    {
        try
        {
            Check(D3SlabOptimizer.SecondarySpacingsMm.SequenceEqual(PlywoodSpacing.StandardSpacingsMm),"All six permitted nominal slab spacings, including 250 mm, are searched");
            AnalysisChecks();
            var baseline = LayoutChecks(new D3SlabInput(), "Default");
            Check(baseline.OverlapMm > 0, "Default exercises secondary overlap");
            var limited = LayoutChecks(new D3SlabInput { LegCapacityKn = 12, Exclude600Standalone = true }, "Lower leg capacity");
            Check(limited.LegCount > baseline.LegCount, "Lower capacity requires more legs");
            LayoutChecks(new D3SlabInput { LengthMm = 4000, WidthMm = 3200, ThicknessMm = 180, DeflectionDenominator = 500 }, "Smaller L/500 slab");
            var heavy = LayoutChecks(new D3SlabInput { LengthMm = 4000, WidthMm = 3200, ThicknessMm = 600 }, "Thicker slab");
            Check(heavy.SecondarySpacingMm < 488, "High secondary load selects closer spacing");
            LayoutChecks(new D3SlabInput { LengthMm = 10600, WidthMm = 6750, MinimumEdgeMm = 200, MaximumEdgeMm = 300, DeflectionDenominator = 270 }, "Larger L/270 slab");
            var custom = new D3SlabInput { UseCustomLoads = true, LiveLoadKnM2 = 1.5, FormworkKnM2 = .6 };
            Near(custom.AreaLoad, 7.1, "Custom load addition");
            Reject(() => new D3SlabInput { MinimumEdgeMm = 199 }.Validate(), "200 mm minimum edge");
            Reject(() => new D3SlabInput { MaximumEdgeMm = 351 }.Validate(), "350 mm maximum edge");
            Reject(() => new D3SlabInput { LegCapacityKn = 0 }.Validate(), "Positive leg capacity");
            Reject(() => D3SlabOptimizer.Optimize(new D3SlabInput { StockLengthsMm = new[] { 1250.0 } }), "Incompatible stock/edge combination returns no layout");
            bool cancelled = false;
            try { D3SlabOptimizer.Optimize(new D3SlabInput(), () => true); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Search cancellation");
            Console.WriteLine("Passed " + checks + " D3 slab checks."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

