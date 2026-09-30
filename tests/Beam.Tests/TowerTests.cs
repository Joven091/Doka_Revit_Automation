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
        var simple = TowerBeamAnalysis.Analyze(2, new[] { 0.0, 2.0 }, 10, 900);
        Near(simple.Moment, 5, 1e-10, "simple-span moment"); Near(simple.Shear, 10, 1e-10, "simple-span shear");
        Near(simple.DeflectionMm, 5 * 10 * 16.0 / (384 * 900) * 1000, 1e-10, "simple-span deflection");
        var continuous = TowerBeamAnalysis.Analyze(4, new[] { 0.0, 2.0, 4.0 }, 10, 900);
        Near(continuous.Reactions[1], 25, 1e-10, "shared frame receives unequal reaction");
        var random = new Random(42);
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
            Near(result.Moment, reference.Moment, 2e-4, "independent sampled moment");
            Near(result.Shear, reference.Shear, 1e-7, "independent shear");
        }
        var input = new TowerInput { LengthMm = 3000, LegCapacityKn = 30, TotalLineLoad = 10, DeflectionLimitMm = 5 };
        var joined = TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, true }, 1524, false);
        Check(joined != null && joined.LegCount == 6 && joined.GroupCount == 1, "mixed joined bays share frame/legs");
        Check(TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, false }, 1524, false) == null, "no standalone frame");
        Check(TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, true }, 1500, false) == null, "frame width fixed");
        input.LegCapacityKn = 1; Check(TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750 }, new[] { true, true }, 1524, false) == null, "leg overload rejection");
        input.LegCapacityKn = 30;
        var rotated = TowerOptimizer.Evaluate(input, new[] { 700.0, 2224 }, new[] { true }, 1000, true);
        Check(rotated != null && rotated.Rotated, "rotated isolated geometry");
        input.LengthMm = 4000;
        Check(TowerOptimizer.Evaluate(input, new[] { 0.0, 1524, 3048 }, new[] { true, true }, 1000, true) == null, "rotated joining does not create extra primary rows");
        input.TotalLineLoad = 1000;
        Check(TowerOptimizer.Evaluate(input, new[] { 500.0, 2500 }, new[] { true }, 1524, false) == null, "primary failure rejection");
        input.LegCapacityKn = 1e6; input.DeflectionLimitMm = 1e6;
        Check(TowerOptimizer.Evaluate(input, new[] { 500.0, 2500 }, new[] { true }, 1524, false) == null, "moment/shear reject despite unlimited legs/deflection");
        input = new TowerInput { LengthMm = 5500, LegCapacityKn = 30, TotalLineLoad = 10, DeflectionLimitMm = 5 };
        var mixed = TowerOptimizer.Evaluate(input, new[] { 250.0, 1250, 2750, 3750, 5250 }, new[] { true, true, false, true }, 1524, false);
        Check(mixed != null && mixed.LegCount == 10 && mixed.GroupCount == 2, "joined and isolated groups coexist");
        input.LengthMm = 6000;
        Check(TowerOptimizer.Evaluate(input, new[] { 0.0, 600, 1200 }, new[] { true, true }, 1524, false) == null, "uplift/overhang layout rejected");
        input.LegCapacityKn = 1e-100; bool tooMany = false;
        try { TowerOptimizer.Optimize(input); } catch (ArgumentException) { tooMany = true; }
        Check(tooMany, "extreme capacity lower bound rejected without integer overflow");
        foreach (double length in new[] { 2500.0, 6000, 9007, 15000 }) foreach (double q in new[] { 5.0, 25.0 })
        {
            input = new TowerInput { LengthMm = length, LegCapacityKn = 30, TotalLineLoad = q, DeflectionLimitMm = 5 };
            var layout = TowerOptimizer.Optimize(input);
            Check(layout.PositionsMm.First() >= 0 && layout.PositionsMm.Last() <= length, "within concrete beam");
            Check(layout.MaxLegKn <= 30.000001 && layout.MomentKnM <= 10.000001 && layout.ShearKn <= 22.000001 && layout.DeflectionMm <= 5.000001, "all accepted checks");
            Near(layout.LeftReactions.Sum() + layout.RightReactions.Sum(), q * length / 1000, 1e-8, "accepted layout equilibrium");
            Console.WriteLine($"Tower {length}mm q={q}: {layout.LegCount} legs, {layout.GroupCount} groups, {layout.Tested} tested");
        }
        input = new TowerInput { LengthMm = 6000, LegCapacityKn = 30, TotalLineLoad = 12, TransverseLoadMoment = 1.5, DeflectionLimitMm = 5 };
        var leftHeavy = TowerOptimizer.Optimize(input); input.TransverseLoadMoment *= -1; var rightHeavy = TowerOptimizer.Optimize(input);
        Check(leftHeavy.PositionsMm.SequenceEqual(rightHeavy.PositionsMm), "mirrored loading preserves layout");
        Near(leftHeavy.LeftReactions.Sum(), rightHeavy.RightReactions.Sum(), 1e-9, "mirrored reactions swap");
        var vm = new BeamCalculatorViewModel { UseTowers = true, FloorHeight = -1, BeamLengthMm = 6000 };
        vm.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(vm.IsCalculationValid && !vm.CanImport, "tower does not depend on free-prop configuration; import blocked");
        vm.BeamLengthMm = 6500; Check(!vm.IsCalculationValid && vm.TowerResult == null, "edits clear stale result");
        vm.TowerLegCapacityKn = double.NaN; vm.OptimizeTowersAsync().GetAwaiter().GetResult(); Check(!vm.IsCalculationValid, "NaN rejects");
        vm.TowerLegCapacityKn = 30; var running = vm.OptimizeTowersAsync(); vm.BeamLengthMm = 7000; running.GetAwaiter().GetResult();
        Check(!vm.IsCalculationValid && vm.TowerResult == null, "in-flight stale result discarded");
        Console.WriteLine($"Passed {checks} tower checks (160 independent stiffness comparisons).");
    }
    sealed class ReferenceResult { public double[] Reactions; public double Moment, Shear, DeflectionMm; }
    // Independent Euler-Bernoulli finite-element stiffness solution with consistent distributed loads.
    static ReferenceResult Reference(double length, double[] supports, double q, double ei)
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
            for(int j=0;j<=400;j++){double s=l*j/400;double y=u+t*s+c2*s*s+c3*s*s*s+c4*Math.Pow(s,4);result.DeflectionMm=Math.Max(result.DeflectionMm,Math.Abs(y)*1000);result.Moment=Math.Max(result.Moment,Math.Abs(ei*(2*c2+6*c3*s+12*c4*s*s)));result.Shear=Math.Max(result.Shear,Math.Abs(ei*(6*c3+24*c4*s)));}
        }
        return result;
    }
}
