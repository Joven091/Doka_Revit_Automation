using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    /// <summary>Downward line load, with positions in metres and intensity in kN/m.</summary>
    public sealed class SlabBeamLoad
    {
        public double Start { get; set; }
        public double End { get; set; }
        public double Load { get; set; }
        public bool IsAccess { get; set; }
    }

    /// <summary>
    /// Constant-EI continuous beam with equal support spans, independent free overhangs,
    /// and piecewise UDLs. Three-moment compatibility gives the support moments;
    /// Macaulay integration retains signed rotations, deflections and reactions.
    /// No five/six-support envelope is used for partial or asymmetric loading.
    /// </summary>
    public sealed class SlabBeamAnalysis
    {
        public double[] Supports { get; private set; }
        public double[] Reactions { get; private set; }
        public SlabBeamLoad[] Loads { get; private set; }
        public double Length { get; private set; }
        public double Span { get; private set; }
        public double LeftCantilever { get; private set; }
        public double RightCantilever { get; private set; }
        public double Rigidity { get; private set; }
        public double TotalLoad { get; private set; }
        public double Reaction => Reactions.Max();
        public double MinimumReaction => Reactions.Min();
        public int MinimumReactionSupport => Array.IndexOf(Reactions, MinimumReaction) + 1;
        public bool HasUplift => MinimumReaction < -1e-10 * Math.Max(Math.Abs(Reaction), Math.Abs(MinimumReaction));
        public double Shear { get; private set; }
        public double Moment { get; private set; }
        public double SpanDeflection { get; private set; }
        public double LeftTipDeflection => Math.Abs(Response(0)[2]);
        public double RightTipDeflection => Math.Abs(Response(Length)[2]);
        public double DeflectionUtilization { get; private set; }
        public double MaximumShear { get; private set; }
        public double MinimumShear { get; private set; }
        public double MaximumMoment { get; private set; }
        public double MinimumMoment { get; private set; }
        public double MaximumDeflection { get; private set; }
        public double MinimumDeflection { get; private set; }
        private double c, d;

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static double Positive(double x) => Math.Max(0, x);
        private static double Power(double x, int n) => Math.Pow(Positive(x), n);

        public static SlabBeamAnalysis Analyze(int count, double span, double left, double right,
            IEnumerable<SlabBeamLoad> loads, double ei, double denominator)
        {
            if (count < 2 || !Finite(span) || span <= 0 || !Finite(left) || left < 0 ||
                !Finite(right) || right < 0 || !Finite(ei) || ei <= 0 || !Finite(denominator) || denominator <= 0)
                throw new ArgumentException("Enter valid support counts, spans and cantilevers.");
            double length = left + (count - 1.0) * span + right;
            if (!Finite(length)) throw new ArgumentException("The beam length must be finite.");
            var result = new SlabBeamAnalysis
            {
                Length = length, Span = span, LeftCantilever = left, RightCantilever = right, Rigidity = ei,
                Supports = Enumerable.Range(0, count).Select(i => left + i * span).ToArray(),
                Loads = (loads ?? Enumerable.Empty<SlabBeamLoad>()).Select(q => q == null ? null :
                    new SlabBeamLoad { Start = q.Start, End = q.End, Load = q.Load, IsAccess = q.IsAccess }).ToArray()
            };
            foreach (var q in result.Loads)
                if (q == null || !Finite(q.Start) || !Finite(q.End) || !Finite(q.Load) || q.Load < 0 ||
                    q.Start < 0 || q.End <= q.Start || q.End > length + 1e-10)
                    throw new ArgumentException("A slab/access line load lies outside the beam or has an invalid value.");
            var moments = new double[count];
            moments[0] = -result.LoadMoment(0, left, left);
            moments[count - 1] = result.LoadMoment(result.Supports[count - 1], length, result.Supports[count - 1]);
            // LoadMoment returns integral q*(origin-x): the right overhang has the opposite sign.
            var leftSlopes = new double[count - 1]; var rightSlopes = new double[count - 1];
            for (int i = 0; i < count - 1; i++)
            {
                double start = result.Supports[i];
                double v = result.LoadMoment(start, start + span, start + span) / span;
                double fourth = result.LoadIntegral(start, start + span, 4);
                double third = result.LoadIntegral(start, start + span, 3);
                leftSlopes[i] = v * span * span / 6 - fourth / (24 * span);
                rightSlopes[i] = leftSlopes[i] - v * span * span / 2 + third / 6;
            }
            int inner = count - 2;
            if (inner > 0)
            {
                var diagonal = Enumerable.Repeat(4 * span, inner).ToArray();
                var rhs = Enumerable.Range(0, inner).Select(i => 6 * (rightSlopes[i] - leftSlopes[i + 1])).ToArray();
                rhs[0] -= span * moments[0]; rhs[inner - 1] -= span * moments[count - 1];
                for (int i = 1; i < inner; i++)
                {
                    double factor = span / diagonal[i - 1];
                    diagonal[i] -= factor * span; rhs[i] -= factor * rhs[i - 1];
                }
                moments[count - 2] = rhs[inner - 1] / diagonal[inner - 1];
                for (int i = inner - 2; i >= 0; i--)
                    moments[i + 1] = (rhs[i] - span * moments[i + 2]) / diagonal[i];
            }
            var starts = new double[count - 1]; var ends = new double[count - 1];
            for (int i = 0; i < count - 1; i++)
            {
                starts[i] = (moments[i + 1] - moments[i] + result.LoadMoment(result.Supports[i], result.Supports[i + 1], result.Supports[i + 1])) / span;
                ends[i] = starts[i] - result.LoadBetween(result.Supports[i], result.Supports[i + 1]);
            }
            result.Reactions = new double[count];
            result.Reactions[0] = starts[0] + result.LoadBetween(0, left);
            for (int i = 1; i < count - 1; i++) result.Reactions[i] = starts[i] - ends[i - 1];
            result.Reactions[count - 1] = result.LoadBetween(result.Supports[count - 1], length) - ends[count - 2];
            double first = result.Supports[0], second = result.Supports[1];
            result.c = -(result.BaseDeflection(second) - result.BaseDeflection(first)) / span;
            result.d = -result.BaseDeflection(first) - result.c * first;
            result.TotalLoad = result.LoadBetween(0, length);
            double loadScale = Math.Max(1, result.TotalLoad);
            if (result.Reactions.Any(x => !Finite(x)) || !Finite(result.c) || !Finite(result.d) ||
                Math.Abs(result.Reactions.Sum() - result.TotalLoad) > 1e-8 * loadScale ||
                Math.Abs(result.Reactions.Select((r, i) => r * result.Supports[i]).Sum() -
                    result.Loads.Sum(q => q.Load * (q.End - q.Start) * (q.Start + q.End) / 2)) > 1e-8 * loadScale * Math.Max(1, length))
                throw new ArgumentException("The slab/access beam response did not satisfy equilibrium.");
            result.FindExtrema(denominator);
            return result;
        }

        public static SlabBeamLoad[] Zones(double length, double leftAccess, double rightAccess,
            double slabLoad, double leftLoad, double rightLoad)
        {
            if (!Finite(length) || !Finite(leftAccess) || !Finite(rightAccess) || leftAccess < 0 || rightAccess < 0 ||
                leftAccess + rightAccess >= length)
                throw new ArgumentException("The access extensions must leave a positive slab-loaded length.");
            var result = new List<SlabBeamLoad>();
            if (leftAccess > 0) result.Add(new SlabBeamLoad { Start = 0, End = leftAccess, Load = leftLoad, IsAccess = true });
            result.Add(new SlabBeamLoad { Start = leftAccess, End = length - rightAccess, Load = slabLoad });
            if (rightAccess > 0) result.Add(new SlabBeamLoad { Start = length - rightAccess, End = length, Load = rightLoad, IsAccess = true });
            return result.ToArray();
        }

        private double LoadBetween(double start, double end) => Loads.Sum(q => q.Load * Math.Max(0, Math.Min(end, q.End) - Math.Max(start, q.Start)));
        private double LoadMoment(double start, double end, double origin)
        {
            return Loads.Sum(q =>
            {
                double a = Math.Max(start, q.Start), b = Math.Min(end, q.End);
                return b <= a ? 0 : q.Load * (b - a) * (origin - (a + b) / 2);
            });
        }
        private double LoadIntegral(double start, double end, int power)
        {
            return Loads.Sum(q =>
            {
                double a = Math.Max(start, q.Start), b = Math.Min(end, q.End);
                return b <= a ? 0 : q.Load * (Math.Pow(end - a, power) - Math.Pow(end - b, power));
            });
        }
        private double BaseDeflection(double x)
        {
            return Loads.Sum(q => q.Load * (Power(x - q.Start, 4) - Power(x - q.End, 4)) / 24)
                - Reactions.Select((r, i) => r * Power(x - Supports[i], 3) / 6).Sum();
        }
        /// <returns>Shear kN, sagging-positive moment kNm, downward-positive displacement m, downward slope.</returns>
        public double[] Response(double x, bool rightSide = true)
        {
            if (!Finite(x) || x < -1e-10 || x > Length + 1e-10) throw new ArgumentOutOfRangeException(nameof(x));
            x = Math.Max(0, Math.Min(Length, x));
            double v = -Loads.Sum(q => q.Load * (Positive(x - q.Start) - Positive(x - q.End)));
            double m = -Loads.Sum(q => q.Load * (Power(x - q.Start, 2) - Power(x - q.End, 2)) / 2);
            double slope = c + Loads.Sum(q => q.Load * (Power(x - q.Start, 3) - Power(x - q.End, 3)) / 6);
            for (int i = 0; i < Supports.Length; i++)
            {
                double delta = x - Supports[i];
                if (delta > 1e-10 || rightSide && delta >= -1e-10) v += Reactions[i];
                m += Reactions[i] * Positive(delta);
                slope -= Reactions[i] * Power(delta, 2) / 2;
            }
            return new[] { v, m, (BaseDeflection(x) + c * x + d) / Rigidity, slope / Rigidity };
        }
        private void FindExtrema(double denominator)
        {
            var nodes = Supports.Concat(Loads.SelectMany(q => new[] { q.Start, q.End })).Concat(new[] { 0.0, Length }).Distinct().OrderBy(x => x).ToArray();
            foreach (double x in nodes) { Record(x, true); Record(x, false); }
            for (int i = 1; i < nodes.Length; i++)
            {
                double start = nodes[i - 1], end = nodes[i], width = end - start;
                if (width <= 1e-12) continue;
                var r = Response(start); double mid = (start + end) / 2;
                double q = Loads.Where(v => v.Start <= mid && v.End >= mid).Sum(v => v.Load);
                if (q > 0 && r[0] / q > 0 && r[0] / q < width) Record(start + r[0] / q, true);
                foreach (double root in Roots(new[] { r[3], -r[1] / Rigidity, -r[0] / (2 * Rigidity), q / (6 * Rigidity) }, 0, width))
                    Record(start + root, true);
            }
            Shear = Math.Max(Math.Abs(MinimumShear), Math.Abs(MaximumShear));
            Moment = Math.Max(Math.Abs(MinimumMoment), Math.Abs(MaximumMoment));
            void Record(double x, bool side)
            {
                var r = Response(x, side);
                MaximumShear = Math.Max(MaximumShear, r[0]); MinimumShear = Math.Min(MinimumShear, r[0]);
                MaximumMoment = Math.Max(MaximumMoment, r[1]); MinimumMoment = Math.Min(MinimumMoment, r[1]);
                MaximumDeflection = Math.Max(MaximumDeflection, r[2]); MinimumDeflection = Math.Min(MinimumDeflection, r[2]);
                double limitLength;
                if (x < Supports[0]) limitLength = LeftCantilever;
                else if (x > Supports[Supports.Length - 1]) limitLength = RightCantilever;
                else { limitLength = Span; SpanDeflection = Math.Max(SpanDeflection, Math.Abs(r[2])); }
                if (limitLength > 0) DeflectionUtilization = Math.Max(DeflectionUtilization, Math.Abs(r[2]) * denominator / limitLength);
            }
        }
        private static IEnumerable<double> Roots(double[] coefficients, double min, double max)
        {
            int degree = coefficients.Length - 1;
            while (degree > 0 && coefficients[degree] == 0) degree--;
            if (degree == 0) yield break;
            if (degree == 1)
            {
                double x = -coefficients[0] / coefficients[1];
                if (x > min && x < max) yield return x;
                yield break;
            }
            var derivative = Enumerable.Range(1, degree).Select(i => coefficients[i] * i).ToArray();
            var cuts = new[] { min }.Concat(Roots(derivative, min, max)).Concat(new[] { max }).OrderBy(x => x).ToArray();
            double At(double x) { double v = coefficients[degree]; for (int i = degree - 1; i >= 0; i--) v = v * x + coefficients[i]; return v; }
            double scale = coefficients.Take(degree + 1).Select((v, i) => Math.Abs(v) * Math.Pow(Math.Max(Math.Abs(min), Math.Abs(max)), i)).Sum();
            double tolerance = 1e-12 * Math.Max(1e-30, scale);
            foreach (double x in cuts.Skip(1).Take(cuts.Length - 2)) if (Math.Abs(At(x)) <= tolerance) yield return x;
            for (int i = 1; i < cuts.Length; i++)
            {
                double a = cuts[i - 1], b = cuts[i], fa = At(a), fb = At(b);
                if (fa == 0 || fb == 0 || Math.Sign(fa) == Math.Sign(fb)) continue;
                for (int iteration = 0; iteration < 60; iteration++)
                {
                    double mid = (a + b) / 2, fm = At(mid);
                    if (Math.Sign(fm) == Math.Sign(fa)) { a = mid; fa = fm; } else b = mid;
                }
                yield return (a + b) / 2;
            }
        }
    }
}
