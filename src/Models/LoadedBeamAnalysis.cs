using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class BeamPointLoad { public double PositionMm, Kn; }
    public sealed class BeamDistributedLoad { public double StartMm, EndMm, KnPerM; }
    /// <summary>Euler-Bernoulli analysis of free-ended stock, with arbitrary point and piecewise uniform loads.</summary>
    public sealed class LoadedBeamAnalysis
    {
        public double[] Reactions { get; private set; }
        public double MomentKnM { get; private set; }
        public double ShearKn { get; private set; }
        public double DeflectionMm { get; private set; }
        public double DeflectionUtilization { get; private set; }
        public double TotalLoadKn { get; private set; }
        public double GoverningLengthMm { get; private set; }
        private sealed class Element { public double X, L, Q, U, T, C2, C3, C4; }
        private readonly List<Element> elements = new List<Element>();
        public double[] Response(double mm, bool rightSide = true)
        {
            if (!Finite(mm) || mm < 0 || mm > (elements.Last().X + elements.Last().L) * 1000 + 1e-7) throw new ArgumentOutOfRangeException(nameof(mm));
            var e = (rightSide ? elements.LastOrDefault(v => mm >= v.X * 1000 - 1e-7) : elements.FirstOrDefault(v => mm <= (v.X+v.L)*1000+1e-7)) ?? elements[0];
            double x = Math.Max(0, Math.Min(e.L, mm / 1000 - e.X));
            return new[] { -(6*e.C3+24*e.C4*x)*rigidity, -(2*e.C2+6*e.C3*x+12*e.C4*x*x)*rigidity,
                -(e.U+e.T*x+e.C2*x*x+e.C3*x*x*x+e.C4*x*x*x*x)*1000 };
        }
        public LoadedBeamAnalysis Scale(double factor)
        {
            if (!Finite(factor) || factor <= 0) throw new ArgumentException("Load scale must be positive.");
            var result = new LoadedBeamAnalysis { rigidity = rigidity, Reactions = Reactions.Select(v => v * factor).ToArray(), MomentKnM = MomentKnM * factor, ShearKn = ShearKn * factor, DeflectionMm = DeflectionMm * factor, DeflectionUtilization = DeflectionUtilization * factor, TotalLoadKn = TotalLoadKn * factor, GoverningLengthMm = GoverningLengthMm };
            foreach (var e in elements) result.elements.Add(new Element { X = e.X, L = e.L, Q = e.Q * factor, U = e.U * factor, T = e.T * factor, C2 = e.C2 * factor, C3 = e.C3 * factor, C4 = e.C4 * factor });
            return result;
        }
        /// <summary>Exact peak absolute displacement after an equal prescribed displacement of every support.</summary>
        public double DeflectionWithOffsetMm(double offset)
        {
            if (!Finite(offset)) throw new ArgumentException("Support displacement must be finite.");
            double result=0;
            foreach(var e in elements)
            {
                Func<double,double> slope=x=>e.T+2*e.C2*x+3*e.C3*x*x+4*e.C4*x*x*x;
                var cuts=new List<double> {0,e.L}; double disc=36*e.C3*e.C3-96*e.C4*e.C2;
                if(Math.Abs(e.C4)>1e-20 && disc>=0) foreach(double root in new[] {(-6*e.C3-Math.Sqrt(disc))/(24*e.C4),(-6*e.C3+Math.Sqrt(disc))/(24*e.C4)}) {if(root>0 && root<e.L)cuts.Add(root);}
                else if(Math.Abs(e.C3)>1e-20) {double root=-e.C2/(3*e.C3);if(root>0 && root<e.L)cuts.Add(root);}
                cuts.Sort();var peaks=new List<double>(cuts);
                for(int i=1;i<cuts.Count;i++)
                {
                    double lo=cuts[i-1],hi=cuts[i],sign=slope(lo);if(sign*slope(hi)>=0)continue;
                    for(int j=0;j<42;j++) {double mid=(lo+hi)/2;if(sign*slope(mid)>0)lo=mid;else hi=mid;}peaks.Add((lo+hi)/2);
                }
                result=Math.Max(result,peaks.Max(x=>Math.Abs(offset-(e.U+e.T*x+e.C2*x*x+e.C3*x*x*x+e.C4*x*x*x*x)*1000)));
            }
            return result;
        }
        private double rigidity;
        public static LoadedBeamAnalysis Analyze(double lengthMm, double[] supportMm, BeamDistributedLoad[] uniform, BeamPointLoad[] points, double ei, int denominator)
        {
            if (!Finite(lengthMm) || lengthMm <= 0 || !Finite(ei) || ei <= 0 || supportMm == null || supportMm.Length < 2
                || supportMm.Any(v => !Finite(v) || v < 0 || v > lengthMm) || supportMm.Distinct().Count() != supportMm.Length
                || !new[] { 500, 350, 270 }.Contains(denominator)) throw new ArgumentException("Invalid stock/support geometry or deflection criterion.");
            uniform = uniform ?? new BeamDistributedLoad[0]; points = points ?? new BeamPointLoad[0];
            if (uniform.Any(v => v == null || !Finite(v.StartMm) || !Finite(v.EndMm) || v.StartMm < 0 || v.EndMm > lengthMm || v.EndMm <= v.StartMm || !Finite(v.KnPerM) || v.KnPerM < 0)
                || points.Any(v => v == null || !Finite(v.PositionMm) || v.PositionMm < 0 || v.PositionMm > lengthMm || !Finite(v.Kn) || v.Kn < 0)) throw new ArgumentException("Invalid beam loads.");
            var supports = supportMm.OrderBy(v => v).ToArray();
            var nodes = new[] { 0.0, lengthMm }.Concat(supports).Concat(uniform.SelectMany(v => new[] { v.StartMm, v.EndMm })).Concat(points.Select(v => v.PositionMm)).OrderBy(v => v).ToList();
            for (int i = nodes.Count - 1; i > 0; i--) if (nodes[i] - nodes[i - 1] < 1e-7) nodes.RemoveAt(i);
            int n = 2 * nodes.Count; var k = new double[n, n]; var f = new double[n];
            for (int e = 0; e < nodes.Count - 1; e++)
            {
                double l = (nodes[e + 1] - nodes[e]) / 1000, q = uniform.Where(v => v.StartMm < (nodes[e] + nodes[e + 1]) / 2 && v.EndMm > (nodes[e] + nodes[e + 1]) / 2).Sum(v => v.KnPerM), c = ei / (l * l * l);
                var ke = new double[,] { { 12, 6 * l, -12, 6 * l }, { 6 * l, 4 * l * l, -6 * l, 2 * l * l }, { -12, -6 * l, 12, -6 * l }, { 6 * l, 2 * l * l, -6 * l, 4 * l * l } };
                var fe = new[] { q * l / 2, q * l * l / 12, q * l / 2, -q * l * l / 12 };
                for (int i = 0; i < 4; i++) { f[2 * e + i] += fe[i]; for (int j = 0; j < 4; j++) k[2 * e + i, 2 * e + j] += c * ke[i, j]; }
            }
            foreach (var p in points) f[2 * nodes.FindIndex(v => Math.Abs(v - p.PositionMm) < 1e-7)] += p.Kn;
            var original = (double[,])k.Clone(); var originalF = (double[])f.Clone();
            var fixedDof = supports.Select(s => 2 * nodes.FindIndex(v => Math.Abs(v - s) < 1e-7)).ToArray();
            foreach (int index in fixedDof) { for (int j = Math.Max(0, index - 3); j <= Math.Min(n - 1, index + 3); j++) { k[index, j] = 0; k[j, index] = 0; } k[index, index] = 1; f[index] = 0; }
            // Cholesky factorization retains the assembled half-bandwidth of three.
            var factor = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = Math.Max(0, i - 3); j <= i; j++)
                {
                    double value = k[i, j]; for (int h = Math.Max(0, i - 3); h < j; h++) value -= factor[i, h] * factor[j, h];
                    if (i == j) { if (value <= 0 || !Finite(value)) throw new ArgumentException("Unstable beam supports."); factor[i, j] = Math.Sqrt(value); } else factor[i, j] = value / factor[j, j];
                }
            var d = new double[n]; for (int i = 0; i < n; i++) { double value = f[i]; for (int j = Math.Max(0, i - 3); j < i; j++) value -= factor[i, j] * d[j]; d[i] = value / factor[i, i]; }
            for (int i = n - 1; i >= 0; i--) { double value = d[i]; for (int j = i + 1; j <= Math.Min(n - 1, i + 3); j++) value -= factor[j, i] * d[j]; d[i] = value / factor[i, i]; }
            var result = new LoadedBeamAnalysis { rigidity = ei, Reactions = fixedDof.Select(i => originalF[i] - Enumerable.Range(Math.Max(0, i - 3), Math.Min(n - 1, i + 3) - Math.Max(0, i - 3) + 1).Sum(j => original[i, j] * d[j])).ToArray(), TotalLoadKn = uniform.Sum(v => (v.EndMm - v.StartMm) / 1000 * v.KnPerM) + points.Sum(v => v.Kn) };
            for (int index = 0; index < nodes.Count - 1; index++)
            {
                double l = (nodes[index + 1] - nodes[index]) / 1000, u = d[2 * index], v = d[2 * index + 2], t = d[2 * index + 1], z = d[2 * index + 3];
                double q = uniform.Where(w => w.StartMm < (nodes[index] + nodes[index + 1]) / 2 && w.EndMm > (nodes[index] + nodes[index + 1]) / 2).Sum(w => w.KnPerM);
                var e = new Element { X = nodes[index] / 1000, L = l, Q = q, U = u, T = t, C2 = (-3 * u + 3 * v) / (l * l) - (2 * t + z) / l + q * l * l / (24 * ei), C3 = (2 * u - 2 * v) / (l * l * l) + (t + z) / (l * l) - q * l / (12 * ei), C4 = q / (24 * ei) };
                result.elements.Add(e);
                Func<double, double> slope = x => e.T + 2 * e.C2 * x + 3 * e.C3 * x * x + 4 * e.C4 * x * x * x;
                var cuts = new List<double> { 0, l };
                double disc = 36 * e.C3 * e.C3 - 96 * e.C4 * e.C2;
                if (Math.Abs(e.C4) > 1e-20 && disc >= 0) { foreach (double root in new[] { (-6 * e.C3 - Math.Sqrt(disc)) / (24 * e.C4), (-6 * e.C3 + Math.Sqrt(disc)) / (24 * e.C4) }) if (root > 0 && root < l) cuts.Add(root); }
                else if (Math.Abs(e.C3) > 1e-20) { double root = -e.C2 / (3 * e.C3); if (root > 0 && root < l) cuts.Add(root); }
                cuts.Sort(); var peaks = new List<double> { 0, l };
                for (int i = 1; i < cuts.Count; i++) { double lo = cuts[i - 1], hi = cuts[i], sign = slope(lo); if (sign * slope(hi) >= 0) continue; for (int j = 0; j < 42; j++) { double middle = (lo + hi) / 2; if (sign * slope(middle) > 0) lo = middle; else hi = middle; } peaks.Add((lo + hi) / 2); }
                double peak = peaks.Max(x => Math.Abs(e.U + e.T * x + e.C2 * x * x + e.C3 * x * x * x + e.C4 * x * x * x * x)) * 1000;
                double middleMm = (nodes[index] + nodes[index + 1]) / 2; int span = Array.FindLastIndex(supports, s => s < middleMm);
                double region = span < 0 ? supports[0] : span == supports.Length - 1 ? lengthMm - supports.Last() : supports[span + 1] - supports[span];
                double use = peak / (region / denominator); if (use > result.DeflectionUtilization) { result.DeflectionUtilization = use; result.GoverningLengthMm = region; }
                result.DeflectionMm = Math.Max(result.DeflectionMm, peak);
                var momentPoints = new List<double> { 0, l }; if (Math.Abs(e.C4) > 1e-20) { double x = -e.C3 / (4 * e.C4); if (x > 0 && x < l) momentPoints.Add(x); }
                result.MomentKnM = Math.Max(result.MomentKnM, momentPoints.Max(x => Math.Abs(ei * (2 * e.C2 + 6 * e.C3 * x + 12 * e.C4 * x * x))));
                result.ShearKn = Math.Max(result.ShearKn, Math.Max(Math.Abs(ei * 6 * e.C3), Math.Abs(ei * (6 * e.C3 + 24 * e.C4 * l))));
            }
            if (Math.Abs(result.Reactions.Sum() - result.TotalLoadKn) > 1e-6 * Math.Max(1, result.TotalLoadKn)) throw new ArgumentException("Beam load equilibrium failed.");
            return result;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
