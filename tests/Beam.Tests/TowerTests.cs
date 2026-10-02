using System;
using System.Linq;
using EngineeringDashboard;

internal static class TowerTests
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Tower: " + message); }
    static void Near(double actual, double expected, double tolerance, string message) => Check(Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + $" ({actual} vs {expected})");
    public static void Run()
    {
        foreach (int denominator in new[] { 500, 350, 270 })
        {
            var benchmark = TowerBeamAnalysis.Analyze(2, new[] { 0.0, 2.0 }, 10, 900);
            Near(benchmark.DeflectionUtilization(0, denominator), (5*10*16.0/(384*900)*1000)/(2000.0/denominator), 1e-10, "closed-form ratio limit uses support span");
            var ratioInput = new TowerInput { LengthMm=6750, MaximumConcreteEndOverhangMm=75, StockLengthsMm=new[]{3300.0}, TotalLineLoad=2.98, LegCapacityKn=70, DeflectionDenominator=denominator };
            var ratioMixed=TowerOptimizer.Evaluate(new TowerInput { LengthMm=3600, StockLengthsMm=new[]{3600.0}, TotalLineLoad=2.98,LegCapacityKn=70,DeflectionDenominator=denominator },new[]{450.0,1450,2950},new[]{true,true},1524,false);
            Check(ratioMixed!=null && ratioMixed.StockPlan.DeflectionUtilization<=1,"mixed cross bays pass local ratio checks");
            var ratioLayout = TowerOptimizer.Optimize(ratioInput);Check(ratioLayout.DeflectionDenominator==denominator,"criterion propagated through optimization");
            foreach(var piece in ratioLayout.StockPlan.Pieces) {
                var memberSupports=ratioLayout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport-piece.FirstSupport+1).Select(x=>(x-piece.StartMm)/1000).ToArray();
                var reference=Reference(piece.StockLengthMm/1000,memberSupports,Math.Max(ratioLayout.LeftLineLoad,ratioLayout.RightLineLoad),TowerOptimizer.PairEI);
                Check(reference.DeflectionRatios[denominator]<=1.00001,"optimized member independently satisfies every span and overhang limit");
                Near(piece.DeflectionUtilization,reference.DeflectionRatios[denominator],2e-4,"optimizer local deflection utilization matches stiffness solution");
            }
        }
        var shortEnd=TowerBeamAnalysis.Analyze(2.4,new[]{.2,1.7,2.2},8,900);
        Check(shortEnd.DeflectionRegions.Count==4,"unequal support spans and two overhangs retained separately");
        foreach(var region in shortEnd.DeflectionRegions) Near(region.AllowableMm(0,350),(region.EndMm-region.StartMm)/350,1e-12,"local length defines allowance");
        var rotatedEnd=TowerBeamAnalysis.Analyze(3.3,new[]{.65,2.65},5,TowerOptimizer.PairEI);
        Check(rotatedEnd.DeflectionUtilization(0,500)>rotatedEnd.DeflectionMm/(3300.0/500),"stock length cannot mask an overhang or span failure");
        var excessiveEnd=TowerOptimizer.Evaluate(new TowerInput { LengthMm=4900,StockLengthsMm=new[]{4900.0},TotalLineLoad=4,LegCapacityKn=70,DeflectionDenominator=500 },new[]{1700.0,3224},new[]{true},1000,true);
        Check(excessiveEnd==null,"overhang ratio rejects layout even at low leg loads");
        var simple = TowerBeamAnalysis.Analyze(2, new[] { 0.0, 2.0 }, 10, 900);
        Near(simple.Moment, 5, 1e-10, "simple-span moment"); Near(simple.Shear, 10, 1e-10, "simple-span shear");
        Near(simple.DeflectionMm, 5 * 10 * 16.0 / (384 * 900) * 1000, 1e-10, "simple-span deflection");
        Near(simple.Response(1)[1], 5, 1e-10, "diagram sagging moment");
        Near(simple.Response(1)[2], -simple.DeflectionMm, 1e-10, "diagram downward displacement");
        var continuous = TowerBeamAnalysis.Analyze(4, new[] { 0.0, 2.0, 4.0 }, 10, 900);
        Near(continuous.Reactions[1], 25, 1e-10, "shared frame receives unequal reaction");
        var random = new Random(42);
        for (int trial = 0; trial < 40; trial++)
        {
            var supports = new[] { .2 + random.NextDouble() * .5, 1.5, 2.7 };
            double p = random.NextDouble() * 10, r = random.NextDouble() * 10;
            var actual = TowerBeamAnalysis.Analyze(3.3, supports, 8, 900, p, r);
            var expected = Reference(3.3, supports, 8, 900, p, r);
            Near(actual.Reactions.Sum(), 8 * 3.3 + p + r, 1e-9, "end loads included in equilibrium");
            for (int i = 0; i < supports.Length; i++) Near(actual.Reactions[i], expected.Reactions[i], 1e-7, "end-load stiffness reactions");
            Near(actual.Moment, expected.Moment, 2e-4, "end-load stiffness moment");
            Near(actual.Shear, expected.Shear, 1e-7, "end-load stiffness shear");
            Near(actual.DeflectionMm, expected.DeflectionMm, 2e-4, "end-load stiffness deflection");
            foreach(int denominator in new[]{500,350,270}) Near(actual.DeflectionUtilization(0,denominator),expected.DeflectionRatios[denominator],2e-4,"end-load local deflection ratio");
            Near(actual.Response(0)[0], -p, 1e-8, "left end-load diagram");
            Near(actual.Response(3.3, false)[0], r, 1e-8, "right end-load diagram");
            var samples = Enumerable.Range(0, 2001).Select(i => actual.Response(3.3 * i / 2000)).ToArray();
            Near(samples.Max(x => Math.Abs(x[2])), expected.DeflectionMm, 2e-4, "end-load plotted deflection matches stiffness");
        }
        for (int test = 0; test < 160; test++)
        {
            int count = 2 + random.Next(7); var supports = new double[count]; supports[0] = random.NextDouble() * 1.2;
            for (int i = 1; i < count; i++) supports[i] = supports[i - 1] + .6 + random.NextDouble() * 2.4;
            double length = supports.Last() + random.NextDouble() * 1.2, load = 1 + random.NextDouble() * 40;
            var result = TowerBeamAnalysis.Analyze(length, supports, load, 899.7512);
            Near(result.Reactions.Sum(), load * length, 1e-9, "vertical equilibrium");
            Near(result.Reactions.Select((r, i) => r * supports[i]).Sum(), load * length * length / 2, 1e-9, "moment equilibrium");
            var reference = Reference(length, supports, load, 899.7512);
            for (int i = 0; i < count; i++) Near(result.Reactions[i], reference.Reactions[i], 1e-7, "independent stiffness reaction");
            Near(result.DeflectionMm, reference.DeflectionMm, 2e-4, "independent sampled deflection");
            foreach(int denominator in new[]{500,350,270}) Near(result.DeflectionUtilization(0,denominator),reference.DeflectionRatios[denominator],2e-4,"independent local deflection ratio");
            Near(result.Moment, reference.Moment, 2e-4, "independent sampled moment");
            Near(result.Shear, reference.Shear, 1e-7, "independent shear");
            foreach (double x in supports)
            {
                int i = Array.IndexOf(supports, x);
                Near(result.Response(x)[0] - result.Response(x, false)[0], reference.Reactions[i], 1e-7, "diagram shear jump equals leg reaction");
                Near(result.Response(x)[2], 0, 1e-8, "diagram zero support displacement");
            }
            Near(result.Response(0, false)[0], 0, 1e-8, "left free-end shear");
            Near(result.Response(length)[0], 0, 1e-8, "right free-end shear");
            Near(result.Response(length)[1], 0, 1e-8, "right free-end moment");
            var curve = Enumerable.Range(0, 2001).Select(i => length * i / 2000).Concat(supports).Select(x => result.Response(x)).ToArray();
            Near(curve.Max(p => Math.Abs(p[1])), reference.Moment, .006, "diagram moment envelope vs stiffness solution");
            Near(curve.Max(p => Math.Abs(p[2])), reference.DeflectionMm, .0003, "diagram deflection envelope vs stiffness solution");
        }
        var input = new TowerInput { LengthMm = 3000, StockLengthsMm = new[] { 3000.0 }, LegCapacityKn = 30, TotalLineLoad = 10, DeflectionLimitMm = 5 };
        var joined = TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, true }, 1524, false);
        Check(joined != null && joined.LegCount == 6 && joined.GroupCount == 1, "mixed joined bays share frame/legs");
        Check(TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, false }, 1524, false) == null, "no standalone frame");
        Check(TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, true }, 1500, false) == null, "frame width fixed");
        input.LegCapacityKn = 1; Check(TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, true }, 1524, false) == null, "leg overload rejection");
        input.LegCapacityKn = 30;
        var rotated = TowerOptimizer.Evaluate(input, new[] { 700.0, 2224 }, new[] { true }, 1000, true);
        Check(rotated != null && rotated.Rotated, "rotated isolated geometry");
        input.LengthMm = 4000; input.StockLengthsMm = new[] { 4000.0 };
        Check(TowerOptimizer.Evaluate(input, new[] { 0.0, 1524, 3048 }, new[] { true, true }, 1000, true) == null, "rotated joining does not create extra primary rows");
        input.TotalLineLoad = 1000;
        Check(TowerOptimizer.Evaluate(input, new[] { 500.0, 2500 }, new[] { true }, 1524, false) == null, "primary failure rejection");
        input.LegCapacityKn = 1e6; input.DeflectionLimitMm = 1e6;
        Check(TowerOptimizer.Evaluate(input, new[] { 500.0, 2500 }, new[] { true }, 1524, false) == null, "moment/shear reject despite unlimited legs/deflection");
        input = new TowerInput { LengthMm = 5500, StockLengthsMm = new[] { 5500.0 }, LegCapacityKn = 30, TotalLineLoad = 10, DeflectionLimitMm = 5 };
        var mixed = TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750, 3750, 5250 }, new[] { true, true, false, true }, 1524, false);
        Check(mixed != null && mixed.LegCount == 10 && mixed.GroupCount == 2, "joined and isolated groups coexist");
        input.LengthMm = 6000; input.StockLengthsMm = new[] { 6000.0 };
        Check(TowerOptimizer.Evaluate(input, new[] { 0.0, 600, 1200 }, new[] { true, true }, 1524, false) == null, "uplift/overhang layout rejected");
        input.LegCapacityKn = 1e-100; bool tooMany = false;
        try { TowerOptimizer.Optimize(input); } catch (ArgumentException) { tooMany = true; }
        Check(tooMany, "extreme capacity lower bound rejected without integer overflow");
        foreach (double length in new[] { 2450.0, 6100, 9000, 15000 }) foreach (double q in new[] { 5.0, 25.0 })
        {
            input = new TowerInput { LengthMm = length, LegCapacityKn = 30, TotalLineLoad = q, DeflectionLimitMm = 5 };
            var layout = TowerOptimizer.Optimize(input);
            Check(layout.PositionsMm.First() >= 0 && layout.PositionsMm.Last() <= length, "within concrete beam");
            Check(layout.MaxLegKn <= 30.000001 && layout.MomentKnM <= 10.000001 && layout.ShearKn <= 22.000001 && layout.DeflectionMm <= 5.000001, "all accepted checks");
            Near(layout.LeftReactions.Sum() + layout.RightReactions.Sum(), q * length / 1000, 1e-8, "accepted layout equilibrium");
            VerifyStock(input, layout);
            Console.WriteLine($"Tower {length}mm q={q}: {layout.LegCount} legs, {layout.GroupCount} groups, {layout.Tested} tested");
        }
        input = new TowerInput { LengthMm = 6100, LegCapacityKn = 30, TotalLineLoad = 12, TransverseLoadMoment = 1.5, DeflectionLimitMm = 5 };
        var leftHeavy = TowerOptimizer.Optimize(input); input.TransverseLoadMoment *= -1; var rightHeavy = TowerOptimizer.Optimize(input);
        Check(leftHeavy.PositionsMm.SequenceEqual(rightHeavy.PositionsMm), "mirrored loading preserves layout");
        Near(leftHeavy.LeftReactions.Sum(), rightHeavy.RightReactions.Sum(), 1e-9, "mirrored reactions swap");
        input = new TowerInput { LengthMm = 9000, LegCapacityKn = 30, TotalLineLoad = 25, DeflectionLimitMm = 5 };
        var highCapacity = TowerOptimizer.Optimize(input); input.LegCapacityKn = 15;
        var lowCapacity = TowerOptimizer.Optimize(input);
        Check(lowCapacity.LegCount > highCapacity.LegCount && lowCapacity.MaxLegKn <= 15.000001, "lower permissible load requires more legs and passes individual reactions");
        Near(lowCapacity.LegUtilizationPercent, lowCapacity.MaxLegKn / 15 * 100, 1e-10, "displayed leg utilization uses permissible capacity");
        var vm = new BeamCalculatorViewModel { UseTowers = true, FloorHeight = -1, BeamLengthMm = 6100 };
        vm.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(vm.IsCalculationValid && !vm.CanImport, "tower does not depend on free-prop configuration; import blocked");
        vm.BeamLengthMm = 6500; Check(!vm.IsCalculationValid && vm.TowerResult == null, "edits clear stale result");
        vm.TowerLegCapacityKn = double.NaN; vm.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(!vm.IsCalculationValid, "NaN rejects");
        vm.TowerLegCapacityKn = 30; var running = vm.OptimizeTowersAsync(); vm.BeamLengthMm = 7000; running.GetAwaiter().GetResult();
        Check(!vm.IsCalculationValid && vm.TowerResult == null, "in-flight stale result discarded");
        StockChecks();
        var exclusion = new TowerInput { LengthMm = 1800, StockLengthsMm = new[] { 1800.0 }, TotalLineLoad = 5, LegCapacityKn = 70, DeflectionLimitMm = 10 };
        Check(TowerOptimizer.Evaluate(exclusion, new[] { 600.0, 1200 }, new[] { true }, 1524, false) != null, "600 standalone allowed by default");
        exclusion.Exclude600Standalone = true;
        Check(TowerOptimizer.Evaluate(exclusion, new[] { 600.0, 1200 }, new[] { true }, 1524, false) == null, "600 standalone excluded");
        Check(TowerOptimizer.Evaluate(exclusion, new[] { 300.0, 900, 1500 }, new[] { true, true }, 1524, false)?.LegCount == 6, "joined 600 bays remain allowed");
        exclusion.LengthMm = 3000; exclusion.StockLengthsMm = new[] { 3000.0 };
        Check(TowerOptimizer.Evaluate(exclusion, new[] { 700.0, 2224 }, new[] { true }, 600, true) == null, "rotated standalone 600 excluded");
        Check(TowerOptimizer.Evaluate(exclusion, new[] { 700.0, 2224 }, new[] { true }, 1000, true) != null, "other rotated tower widths retained");
        exclusion.LengthMm = 2400; exclusion.StockLengthsMm = new[] { 2400.0 };
        var retained = TowerOptimizer.Evaluate(exclusion, new[] { 300.0, 900, 1500, 2100 }, new[] { true, true, true }, 1524, false);
        Check(retained != null && retained.GroupCount == 1, "join removal cannot create forbidden 600 standalone towers");
        exclusion.LengthMm = 6750; exclusion.StockLengthsMm = TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths); exclusion.MaximumConcreteEndOverhangMm = 75;
        var filtered = TowerOptimizer.Optimize(exclusion);
        int groupStart = 0;
        for (int i = 0; i < filtered.PositionsMm.Length; i++) if (i == filtered.Joined.Length || !filtered.Joined[i])
        {
            Check(i - groupStart != 1 || (filtered.Rotated ? filtered.WidthMm : filtered.PositionsMm[i] - filtered.PositionsMm[groupStart]) != 600, "optimized end-allowance candidates respect checkbox");
            groupStart = i + 1;
        }
        var regression = new TowerInput { LengthMm = 6750, LegCapacityKn = 70, TotalLineLoad = 2.98, DeflectionLimitMm = 5, MaximumConcreteEndOverhangMm = 75 };
        var optimized = TowerOptimizer.Optimize(regression);
        Check(optimized.LegCount == 8, "6750 mm with 75 mm allowance uses eight legs");
        Near(optimized.LeftReactions.Sum() + optimized.RightReactions.Sum(), optimized.StockPlan.Pieces.Sum(p => p.StockLengthMm) / 1000 * 2.98, 1e-9, "only actual H20 length contributes load");
        Check(optimized.ConcreteEndOverhangMm <= 75 && optimized.StockPlan.Pieces.First().StartMm == optimized.ConcreteEndOverhangMm, "symmetric stock placement within allowance");
        Console.WriteLine("6750 regression: " + optimized.Summary + "\n" + optimized.StockSchedule);
        var proposed = TowerOptimizer.Evaluate(new TowerInput { LengthMm = 6600, LegCapacityKn = 70, TotalLineLoad = 2.98, DeflectionLimitMm = 5, StockLengthsMm = new[] { 3300.0 } },
            new[] { 650.0, 2650, 3950, 5950 }, new[] { true, false, true }, 1524, false);
        Check(proposed != null && proposed.LegCount == 8, "two 2000 towers with 3300 stock pass default beam loading");
        var separate = TowerOptimizer.Optimize(new TowerInput { LengthMm = 10600, LegCapacityKn = 70, TotalLineLoad = 2.98, DeflectionLimitMm = 5, MaximumConcreteEndOverhangMm = 75 });
        Check(separate.LegCount == 12 && separate.GroupCount == 3 && separate.BayCount == 3, "10600 uses three independent towers");
        Check(separate.Joined.SequenceEqual(new[] { true, false, true, false, true }), "no crosses across gaps between independent towers");
        foreach (var piece in separate.StockPlan.Pieces) Check(piece.LastSupport - piece.FirstSupport + 1 >= 2, "each individual H20 has at least two longitudinal supports");
        var spaced = TowerOptimizer.Evaluate(new TowerInput { LengthMm = 6600, LegCapacityKn = 70, TotalLineLoad = 2.98, DeflectionLimitMm = 5, StockLengthsMm = new[] { 3300.0 } },
            new[] { 600.0, 2600, 4100, 6100 }, new[] { true, true, true }, 1524, false);
        Check(spaced != null && !spaced.Joined[1] && spaced.GroupCount == 2, "1500 distance stays an unbraced gap when each tower has two frames");
        regression.MaximumConcreteEndOverhangMm = 0;
        Check(TowerStockPlanner.MinimumPieces(6750, regression.StockLengthsMm) == 4, "exact-coverage cause of 16-leg minimum");
        var validation = new BeamCalculatorViewModel { UseTowers = true, MaximumConcreteEndOverhangMm = -1 };
        validation.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(!validation.IsCalculationValid, "negative concrete allowance rejected");
        Console.WriteLine($"Passed {checks} tower checks (160 independent stiffness comparisons).");
    }
    static void VerifyStock(TowerInput input, TowerLayout layout)
    {
        double end = 0; int nextSupport = 0;
        foreach (var piece in layout.StockPlan.Pieces)
        {
            Near(piece.StartMm, end, 1e-9, "stock cover has no overlap or gap"); end = piece.EndMm;
            Check(input.StockLengthsMm.Contains(piece.StockLengthMm), "selected stock is available");
            Check(piece.FirstSupport == nextSupport && piece.LastSupport > piece.FirstSupport, "each member has its own two or more supports");
            nextSupport = piece.LastSupport + 1;
            Check(layout.PositionsMm[piece.FirstSupport] - piece.StartMm >= input.StockEndSetbackMm - 1e-7 && piece.EndMm - layout.PositionsMm[piece.LastSupport] >= input.StockEndSetbackMm - 1e-7, "stock end support inset");
            var positions = layout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => (x - piece.StartMm) / 1000).ToArray();
            var reference = Reference(piece.StockLengthMm / 1000, positions, Math.Max(layout.LeftLineLoad, layout.RightLineLoad), TowerOptimizer.PairEI);
            Near(piece.MomentKnM, reference.Moment, 2e-4, "per-stock independent moment");
            Near(piece.ShearKn, reference.Shear, 1e-7, "per-stock independent shear");
            Near(piece.DeflectionMm, reference.DeflectionMm, 2e-4, "per-stock independent deflection");
            for (int i = 0; i < reference.Reactions.Length; i++) Near(Math.Max(layout.LeftReactions[piece.FirstSupport + i], layout.RightReactions[piece.FirstSupport + i]), reference.Reactions[i], 1e-7, "per-stock reaction");
        }
        Near(end, input.LengthMm, 1e-9, "stock cover reaches concrete end");
        Check(nextSupport == layout.PositionsMm.Length, "all legs assigned once");
        Check(layout.StockPlan.TotalH20Count == layout.StockPlan.Pieces.Length * 4, "two H20s on each of two primary lines");
    }
    static void StockChecks()
    {
        var stocks = TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths);
        Check(stocks.SequenceEqual(new[] { 1250.0, 1800, 2450, 2650, 2900, 3300, 3600, 3900, 4500, 4900 }), "user stock catalogue");
        Check(TowerStockPlanner.MinimumPieces(6000, stocks) == int.MaxValue, "6000 has no exact cover with corrected stock");
        Check(TowerStockPlanner.MinimumPieces(5950, stocks) == 2, "5950 can use 2650 + 3300");
        Check(TowerStockPlanner.MinimumPieces(6001, stocks) == int.MaxValue, "uncut stock cannot cover residual length");
        var input = new TowerInput { LengthMm = 3600, StockLengthsMm = new[] { 1800.0 }, TotalLineLoad = 10, LegCapacityKn = 30, DeflectionLimitMm = 5 };
        var plan = TowerStockPlanner.Find(input, new[] { 300.0, 1500, 2100, 3300 }, 5);
        Check(plan != null && plan.Pieces.Length == 2 && plan.TotalH20Count == 8, "two independent stock pieces");
        Near(plan.Reactions.Sum(), 18, 1e-9, "no duplicated load at piece break");
        Check(TowerStockPlanner.Find(input, new[] { 300.0, 1800, 3300 }, 5) == null, "no shared-head butt joint at stock boundary");
        Check(TowerStockPlanner.Find(input, new[] { 300.0, 3300 }, 5) == null, "each stock piece needs at least two supports");
        input.StockLengthsMm = new[] { 4900.0 }; Check(TowerStockPlanner.Find(input, new[] { 300.0, 1500, 2100, 3300 }, 5) == null, "overlength stock rejected rather than cut");
        var vm = new BeamCalculatorViewModel { UseTowers = true, MaximumConcreteEndOverhangMm = 0, BeamLengthMm = 6001 };
        vm.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(!vm.IsCalculationValid && vm.WarningMessage.Contains("exactly"), "uncoverable length explained");
        vm.StockLengthsText = "1800; NaN"; vm.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(!vm.IsCalculationValid, "invalid stock list rejected");
    }
    sealed class ReferenceResult { public double[] Reactions; public double Moment, Shear, DeflectionMm; public System.Collections.Generic.Dictionary<int,double> DeflectionRatios = new System.Collections.Generic.Dictionary<int,double> { {500,0}, {350,0}, {270,0} }; }
    // Independent Euler-Bernoulli finite-element stiffness solution with consistent distributed loads.
    static ReferenceResult Reference(double length, double[] supports, double q, double ei, double leftEndLoad = 0, double rightEndLoad = 0)
    {
        var x = new[] { 0.0 }.Concat(supports).Concat(new[] { length }).Distinct().OrderBy(v => v).ToArray(); int n = x.Length * 2;
        var k = new double[n, n]; var f = new double[n];
        for (int e = 0; e < x.Length - 1; e++)
        {
            double l = x[e + 1] - x[e], s = ei / Math.Pow(l, 3);
            var ke = new double[,] { { 12, 6*l, -12, 6*l }, { 6*l, 4*l*l, -6*l, 2*l*l }, { -12, -6*l, 12, -6*l }, { 6*l, 2*l*l, -6*l, 4*l*l } };
            var fe = new[] { q*l/2, q*l*l/12, q*l/2, -q*l*l/12 };
            for (int i = 0; i < 4; i++) { f[2*e+i] += fe[i]; for (int j = 0; j < 4; j++) k[2*e+i, 2*e+j] += s * ke[i,j]; }
        }
        f[0] += leftEndLoad; f[n - 2] += rightEndLoad;
        var fixedDof = supports.Select(v => 2 * Array.IndexOf(x, v)).ToArray(); var free = Enumerable.Range(0, n).Except(fixedDof).ToArray();
        int size = free.Length; var a = new double[size, size + 1];
        for (int i = 0; i < size; i++) { a[i,size] = f[free[i]]; for (int j = 0; j < size; j++) a[i,j] = k[free[i],free[j]]; }
        for (int p = 0; p < size; p++)
        {
            int pivot = p; for (int i = p+1; i < size; i++) if (Math.Abs(a[i,p]) > Math.Abs(a[pivot,p])) pivot=i;
            for (int j=p;j<=size;j++){double t=a[p,j];a[p,j]=a[pivot,j];a[pivot,j]=t;}
            for(int i=p+1;i<size;i++){double r=a[i,p]/a[p,p];for(int j=p;j<=size;j++)a[i,j]-=r*a[p,j];}
        }
        var d = new double[n];
        for (int i=size-1;i>=0;i--){double v=a[i,size];for(int j=i+1;j<size;j++)v-=a[i,j]*d[free[j]];d[free[i]]=v/a[i,i];}
        var result = new ReferenceResult { Reactions = fixedDof.Select(i => -(Enumerable.Range(0,n).Sum(j=>k[i,j]*d[j])-f[i])).ToArray() };
        for(int e=0;e<x.Length-1;e++)
        {
            double l=x[e+1]-x[e], u=d[2*e], v=d[2*e+2], t=d[2*e+1], z=d[2*e+3];
            double c2=(-3*u+3*v)/l/l-(2*t+z)/l+q*l*l/(24*ei), c3=(2*u-2*v)/l/l/l+(t+z)/l/l-q*l/(12*ei), c4=q/(24*ei);
            double regionPeak=0;
            for(int j=0;j<=400;j++){double s=l*j/400;double y=u+t*s+c2*s*s+c3*s*s*s+c4*Math.Pow(s,4);regionPeak=Math.Max(regionPeak,Math.Abs(y)*1000);result.DeflectionMm=Math.Max(result.DeflectionMm,Math.Abs(y)*1000);result.Moment=Math.Max(result.Moment,Math.Abs(ei*(2*c2+6*c3*s+12*c4*s*s)));result.Shear=Math.Max(result.Shear,Math.Abs(ei*(6*c3+24*c4*s)));}
            foreach(int denominator in new[]{500,350,270})result.DeflectionRatios[denominator]=Math.Max(result.DeflectionRatios[denominator],regionPeak/(l*1000/denominator));
        }
        return result;
    }
}

