#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace EngineeringDashboard.Dome;

/// <summary>Elastic constant-EI beam under a downward uniform line load, with simple vertical supports.</summary>
public static class BeamAnalysis
{
    public const double Rigidity = 449.8756; // kN m²; legacy H20 stiffness, see README design basis.

    public static BeamResult Analyze(IReadOnlyList<double> spans, double w, double leftOverhang, double rightOverhang)
    {
        if (spans.Count is < 1 or > 4 || spans.Any(s => !Numeric.IsFinite(s) || s < 0.01) ||
            !Numeric.IsFinite(w) || w < 0 || w > BeamDesign.MaxPressure ||
            !Numeric.IsFinite(leftOverhang) || leftOverhang < 0 ||
            !Numeric.IsFinite(rightOverhang) || rightOverhang < 0 ||
            spans.Sum() + leftOverhang + rightOverhang > BeamDesign.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(spans), "Invalid beam geometry or line load.");
        int n = spans.Count;
        var moments = new double[n + 1];
        moments[0] = -w * leftOverhang * leftOverhang / 2;
        moments[n] = -w * rightOverhang * rightOverhang / 2;
        if (n > 1)
        {
            // Three-moment equation; tridiagonal elimination, with prescribed overhang moments.
            int size = n - 1;
            var diagonal = new double[size];
            var upper = new double[size];
            var rhs = new double[size];
            for (int i = 0; i < size; i++)
            {
                double a = spans[i], b = spans[i + 1];
                diagonal[i] = 2 * (a + b);
                upper[i] = b;
                rhs[i] = -w * (a * a * a + b * b * b) / 4;
                if (i == 0) rhs[i] -= moments[0] * a;
                if (i == size - 1) rhs[i] -= moments[n] * b;
                if (i > 0)
                {
                    double factor = a / diagonal[i - 1];
                    diagonal[i] -= factor * upper[i - 1];
                    rhs[i] -= factor * rhs[i - 1];
                }
            }
            for (int i = size - 1; i >= 0; i--)
                moments[i + 1] = (rhs[i] - (i < size - 1 ? upper[i] * moments[i + 2] : 0)) / diagonal[i];
        }

        var shearLeft = new double[n];
        var slopeLeft = new double[n];
        for (int i = 0; i < n; i++)
        {
            double l = spans[i];
            shearLeft[i] = w * l / 2 + (moments[i + 1] - moments[i]) / l;
            slopeLeft[i] = -(shearLeft[i] * l * l * l / 6 - w * Math.Pow(l, 4) / 24 + moments[i] * l * l / 2) / l;
        }

        var segments = new List<BeamSegment>();
        if (leftOverhang > 0)
        {
            double c = leftOverhang;
            double slope = slopeLeft[0] + w * c * c * c / 6;
            double displacement = w * Math.Pow(c, 4) / 24 - slope * c;
            segments.Add(new(0, c, w, 0, 0, slope, displacement));
        }
        var supports = new List<double> { leftOverhang };
        var reactions = new List<double> { w * leftOverhang + shearLeft[0] };
        double x = leftOverhang;
        for (int i = 0; i < n; i++)
        {
            segments.Add(new(x, spans[i], w, moments[i], shearLeft[i], slopeLeft[i], 0));
            x += spans[i];
            supports.Add(x);
            reactions.Add(i == n - 1 ? w * rightOverhang - (shearLeft[i] - w * spans[i])
                : shearLeft[i + 1] - (shearLeft[i] - w * spans[i]));
        }
        if (rightOverhang > 0)
        {
            double slope = segments[segments.Count - 1].SlopeEI(spans[spans.Count - 1]);
            segments.Add(new(x, rightOverhang, w, moments[n], w * rightOverhang, slope, 0));
        }
        return new BeamResult(w, x + rightOverhang, segments, supports, reactions);
    }
}

public readonly record struct Demand(double Value, double Position)
{
    public double Magnitude => Math.Abs(Value);
}

/// <summary>Local polynomial coefficients. Displacement/slope coefficients are multiplied by EI.</summary>
public sealed record BeamSegment(double Start, double Length, double W, double M0, double V0, double T0, double D0)
{
    public double Shear(double x) => V0 - W * x;
    public double Moment(double x) => M0 + V0 * x - W * x * x / 2;
    public double SlopeEI(double x) => T0 + M0 * x + V0 * x * x / 2 - W * x * x * x / 6;
    public double Deflection(double x) => (D0 + T0 * x + M0 * x * x / 2 + V0 * x * x * x / 6 - W * Math.Pow(x, 4) / 24) / BeamAnalysis.Rigidity * 1000;
    public IEnumerable<double> MomentLocations() => new[] { 0.0, Length }.Concat(W > 0 && V0 / W > 0 && V0 / W < Length ? new[] { V0 / W } : Array.Empty<double>());
    public IEnumerable<double> DeflectionLocations() => new[] { 0.0, Length }.Concat(PolynomialRoots.InInterval(new[] { T0, M0, V0 / 2, -W / 6 }, Length));
}

public sealed class BeamResult
{
    public double W { get; }
    public double TotalLength { get; }
    public IReadOnlyList<BeamSegment> Segments { get; }
    public IReadOnlyList<double> SupportLocations { get; }
    public IReadOnlyList<double> Reactions { get; }
    public Demand MaximumShear { get; }
    public Demand MinimumShear { get; }
    public Demand MaximumMoment { get; }
    public Demand MinimumMoment { get; }
    public Demand MaximumDeflection { get; }
    public Demand MinimumDeflection { get; }
    public Demand GoverningShear => Larger(MaximumShear, MinimumShear);
    public Demand GoverningMoment => Larger(MaximumMoment, MinimumMoment);
    public Demand GoverningDeflection => Larger(MaximumDeflection, MinimumDeflection);
    public bool IsFinite => new[] { MaximumShear, MinimumShear, MaximumMoment, MinimumMoment, MaximumDeflection, MinimumDeflection }
        .All(d => Numeric.IsFinite(d.Value) && Numeric.IsFinite(d.Position)) && Reactions.All(Numeric.IsFinite);
    // Roundoff tolerance only, not a hold-down allowance.
    public bool HasUplift => Reactions.Any(r => r < -1e-9 * Math.Max(1, W * TotalLength));
    internal BeamResult(double w, double length, List<BeamSegment> segments, List<double> supports, List<double> reactions)
    {
        W = w; TotalLength = length;
        Segments = segments.AsReadOnly(); SupportLocations = supports.AsReadOnly(); Reactions = reactions.AsReadOnly();
        var shears = segments.SelectMany(s => new[] { new Demand(s.Shear(0), s.Start), new Demand(s.Shear(s.Length), s.Start + s.Length) }).ToArray();
        var moments = segments.SelectMany(s => s.MomentLocations().Select(x => new Demand(s.Moment(x), s.Start + x))).ToArray();
        var deflections = segments.SelectMany(s => s.DeflectionLocations().Select(x => new Demand(s.Deflection(x), s.Start + x))).ToArray();
        MaximumShear = shears.OrderByDescending(d => d.Value).First(); MinimumShear = shears.OrderBy(d => d.Value).First();
        MaximumMoment = moments.OrderByDescending(d => d.Value).First(); MinimumMoment = moments.OrderBy(d => d.Value).First();
        MaximumDeflection = deflections.OrderByDescending(d => d.Value).First(); MinimumDeflection = deflections.OrderBy(d => d.Value).First();
    }
    private static Demand Larger(Demand a, Demand b) => a.Magnitude >= b.Magnitude ? a : b;
}

/// <summary>Find roots on monotone intervals partitioned by derivative roots; degrees <= 3.</summary>
internal static class PolynomialRoots
{
    public static IEnumerable<double> InInterval(double[] coefficients, double length)
    {
        int degree = coefficients.Length - 1;
        while (degree > 0 && coefficients[degree] == 0) degree--;
        if (degree == 0) return Array.Empty<double>();
        if (degree == 1)
        {
            double root = -coefficients[0] / coefficients[1];
            return root >= 0 && root <= length ? new[] { root } : Array.Empty<double>();
        }
        double Evaluate(double x)
        {
            double result = coefficients[degree];
            for (int i = degree - 1; i >= 0; i--) result = result * x + coefficients[i];
            return result;
        }
        var derivative = Enumerable.Range(1, degree).Select(i => i * coefficients[i]).ToArray();
        var boundaries = new[] { 0.0 }.Concat(InInterval(derivative, length)).Append(length).Distinct().OrderBy(value => value).ToArray();
        double scale = Enumerable.Range(0, degree + 1).Sum(i => Math.Abs(coefficients[i]) * Math.Pow(length, i));
        double tolerance = scale * 1e-13;
        var roots = new List<double>();
        foreach (double x in boundaries) if (Math.Abs(Evaluate(x)) <= tolerance) roots.Add(x);
        for (int i = 1; i < boundaries.Length; i++)
        {
            double lo = boundaries[i - 1], hi = boundaries[i], fLo = Evaluate(lo), fHi = Evaluate(hi);
            if (fLo == 0 || fHi == 0 || Math.Sign(fLo) == Math.Sign(fHi)) continue;
            for (int step = 0; step < 70; step++)
            {
                double mid = (lo + hi) / 2, fMid = Evaluate(mid);
                if (fMid == 0) { lo = hi = mid; break; }
                if (Math.Sign(fMid) == Math.Sign(fLo)) { lo = mid; fLo = fMid; } else hi = mid;
            }
            roots.Add((lo + hi) / 2);
        }
        return roots.Distinct().OrderBy(value => value).ToArray();
    }
}

public sealed record DesignSelection(BeamResult? Result, double[] Spans, double SecondarySpacing, string Status)
{
    public bool Passed => Result is not null && BeamDesign.Passes(Result);
}

public static class BeamDesign
{
    // Software operating ranges, not certified product geometry/load limits.
    public const double MaxLength = 20;
    public const double MaxPressure = 10000;
    public const double ShearLimit = 11, MomentLimit = 5, DeflectionLimit = 5;
    public static bool Passes(BeamResult result) => result.IsFinite && !result.HasUplift &&
        result.GoverningShear.Magnitude <= ShearLimit && result.GoverningMoment.Magnitude <= MomentLimit &&
        result.GoverningDeflection.Magnitude <= DeflectionLimit;
    public static DesignSelection Select(double pressure, double length)
    {
        if (!Numeric.IsFinite(pressure) || pressure < 0 || pressure > MaxPressure ||
            !Numeric.IsFinite(length) || length <= 0 || length > MaxLength)
            return new(null, [], 0, "Invalid input: use a finite nonnegative pressure and positive beam length within the software range.");
        var candidates = new List<double[]>();
        for (int count = 1; count <= 4; count++)
        {
            candidates.AddRange(Combinations(count).Where(s => s.Sum() < length));
            var imperial = Enumerable.Repeat(1.524, count).ToArray();
            if (imperial.Sum() < length) candidates.Add(imperial);
        }
        if (candidates.Count == 0)
            return new(null, [], 0, "No admissible layout: beam length must exceed 0.600 m because this search requires overhangs at both ends.");
        BeamResult? best = null;
        double[] bestSpans = [];
        double bestSpacing = 0, bestCost = double.PositiveInfinity;
        foreach (double spacing in new[] { 0.488, 0.407, 0.350, 0.305, 0.275, 0.250, 0.225 })
        foreach (double[] spans in candidates)
        {
            double c = (length - spans.Sum()) / 2;
            var result = BeamAnalysis.Analyze(spans, pressure * spacing, c, c);
            if (!Passes(result)) continue;
            double cost = (spans.Length + 1) / spacing;
            if (cost < bestCost || (Math.Abs(cost - bestCost) < 1e-9 && spans.Sum() > bestSpans.Sum()))
            {
                best = result; bestSpans = spans; bestSpacing = spacing; bestCost = cost;
            }
        }
        return best is null
            ? new(null, [], 0, "No passing layout in the searched set: capacity, deflection or bearing-only support checks fail.")
            : new(best, bestSpans, bestSpacing, "Secondary-beam checks pass under the stated assumptions.");
    }
    private static IEnumerable<double[]> Combinations(int length)
    {
        if (length == 0) { yield return []; yield break; }
        foreach (var prefix in Combinations(length - 1))
        foreach (double span in new[] { 0.6, 1.0, 1.5 }) yield return [.. prefix, span];
    }
}
