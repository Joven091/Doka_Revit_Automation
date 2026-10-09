using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    /// <summary>Uniform load, constant EI, arbitrary simple support positions and free ends.
    /// Three-moment solution; deflection extrema are roots of the integrated slope, not samples.</summary>
    public sealed class TowerBeamAnalysis
    {
        public double[] Reactions { get; private set; }
        public double Moment { get; private set; }
        public double Shear { get; private set; }
        public double DeflectionMm { get; private set; }
        public sealed class DeflectionRegion
        {
            public double StartMm { get; internal set; }
            public double EndMm { get; internal set; }
            public double PeakMm { get; internal set; }
            public bool IsOverhang { get; internal set; }
            public double AllowableMm(double fixedMm, int denominator) => denominator == 0 ? fixedMm : (EndMm - StartMm) / denominator;
        }
        private readonly List<DeflectionRegion> regions = new List<DeflectionRegion>();
        public IReadOnlyList<DeflectionRegion> DeflectionRegions => regions.AsReadOnly();
        public double DeflectionUtilization(double fixedMm, int denominator) => regions.Max(x => x.PeakMm / x.AllowableMm(fixedMm, denominator));
        public DeflectionRegion GoverningDeflection(double fixedMm, int denominator) => regions.OrderByDescending(x => x.PeakMm / x.AllowableMm(fixedMm, denominator)).First();
        private double length, load, ei, firstSlope, lastSlope, leftEndLoad, rightEndLoad;
        private double[] supports, moments;
        // Signed response: sagging moment positive; upward displacement positive.
        // At a support, rightSide selects the corresponding side of the shear jump.
        public double[] Response(double position, bool rightSide = true)
        {
            if (!Finite(position) || position < -1e-10 || position > length + 1e-10) throw new ArgumentOutOfRangeException(nameof(position));
            position = Math.Max(0, Math.Min(length, position));
            double v = -load * position - leftEndLoad, m = -load * position * position / 2 - leftEndLoad * position;
            if (position == 0 && !rightSide) v += leftEndLoad;
            if (position == length && rightSide) v -= rightEndLoad;
            for (int j = 0; j < supports.Length; j++)
                if (supports[j] < position || (rightSide && supports[j] == position))
                { v += Reactions[j]; m += Reactions[j] * (position - supports[j]); }
            double y;
            if (position < supports[0])
            {
                double x = position - supports[0], a = supports[0];
                y = firstSlope * x - load * (Math.Pow(x, 4) / 24 + a * Math.Pow(x, 3) / 6 + a * a * x * x / 4) - leftEndLoad * (x * x * x / 6 + a * x * x / 2);
            }
            else if (position > supports[supports.Length - 1])
            {
                double x = position - supports[supports.Length - 1], b = length - supports[supports.Length - 1];
                y = lastSlope * x - load * (b * b * x * x / 4 - b * Math.Pow(x, 3) / 6 + Math.Pow(x, 4) / 24) - rightEndLoad * (b * x * x / 2 - x * x * x / 6);
            }
            else
            {
                int i = 0; while (i < supports.Length - 2 && position > supports[i + 1]) i++;
                double l = supports[i + 1] - supports[i], x = position - supports[i], ml = moments[i];
                double vs = load * l / 2 + (moments[i + 1] - ml) / l;
                double c = -(ml * l / 2 + vs * l * l / 6 - load * l * l * l / 24);
                y = c * x + ml * x * x / 2 + vs * Math.Pow(x, 3) / 6 - load * Math.Pow(x, 4) / 24;
            }
            return new[] { v, m, y / ei * 1000 };
        }
        internal double WS10StrengthUtilization()=>PrimaryBeamProfile.WS10Strength(length*1000,supports.Select(x=>x*1000),(x,right)=>Response(x/1000,right));
        public static TowerBeamAnalysis Analyze(double length, double[] supports, double load, double ei, double leftEndLoad = 0, double rightEndLoad = 0)
        {
            if (!Finite(length) || length <= 0 || !Finite(load) || load <= 0 || !Finite(ei) || ei <= 0
                || !Finite(leftEndLoad) || !Finite(rightEndLoad) || leftEndLoad < 0 || rightEndLoad < 0
                || supports == null || supports.Length < 2 || supports.Any(x => !Finite(x) || x < 0 || x > length))
                throw new ArgumentException("Invalid tower beam analysis input.");
            int n = supports.Length; var h = new double[n - 1];
            for (int i = 0; i < n - 1; i++) { h[i] = supports[i + 1] - supports[i]; if (h[i] <= 1e-8) throw new ArgumentException("Support positions must increase."); }
            double a = supports[0], b = length - supports[n - 1];
            var m = new double[n]; m[0] = -load * a * a / 2 - leftEndLoad * a; m[n - 1] = -load * b * b / 2 - rightEndLoad * b;
            var d = new double[n]; var rhs = new double[n];
            for (int i = 1; i < n - 1; i++)
            {
                d[i] = 2 * (h[i - 1] + h[i]); rhs[i] = -load * (Math.Pow(h[i - 1], 3) + Math.Pow(h[i], 3)) / 4;
                if (i == 1) rhs[i] -= h[i - 1] * m[0];
                if (i == n - 2) rhs[i] -= h[i] * m[n - 1];
                if (i > 1) { double f = h[i - 1] / d[i - 1]; d[i] -= f * h[i - 1]; rhs[i] -= f * rhs[i - 1]; }
            }
            for (int i = n - 2; i >= 1; i--) m[i] = (rhs[i] - (i < n - 2 ? h[i] * m[i + 1] : 0)) / d[i];
            var result = new TowerBeamAnalysis { Reactions = new double[n], Moment = m.Max(x => Math.Abs(x)), Shear = Math.Max(load * a + leftEndLoad, load * b + rightEndLoad) };
            double maxY = 0, firstSlope = 0, lastSlope = 0;
            for (int i = 0; i < n - 1; i++)
            {
                double l = h[i], ml = m[i], v = load * l / 2 + (m[i + 1] - ml) / l;
                result.Reactions[i] += v; result.Reactions[i + 1] += load * l - v;
                result.Shear = Math.Max(result.Shear, Math.Max(Math.Abs(v), Math.Abs(v - load * l)));
                double peak = v / load;
                if (peak > 0 && peak < l) result.Moment = Math.Max(result.Moment, Math.Abs(ml + v * peak - load * peak * peak / 2));
                double c = -(ml * l / 2 + v * l * l / 6 - load * l * l * l / 24);
                Func<double, double> slope = x => c + ml * x + v * x * x / 2 - load * x * x * x / 6;
                Func<double, double> y = x => c * x + ml * x * x / 2 + v * Math.Pow(x, 3) / 6 - load * Math.Pow(x, 4) / 24;
                var cuts = new List<double> { 0, l }; double disc = v * v + 2 * load * ml;
                if (disc >= 0) foreach (double x in new[] { (v - Math.Sqrt(disc)) / load, (v + Math.Sqrt(disc)) / load }) if (x > 0 && x < l) cuts.Add(x);
                double regionPeak = Extrema(slope, y, cuts);
                maxY = Math.Max(maxY, regionPeak);
                result.regions.Add(new DeflectionRegion { StartMm = supports[i] * 1000, EndMm = supports[i + 1] * 1000, PeakMm = regionPeak / ei * 1000 });
                if (i == 0) firstSlope = c; if (i == n - 2) lastSlope = slope(l);
            }
            result.Reactions[0] += load * a + leftEndLoad; result.Reactions[n - 1] += load * b + rightEndLoad;
            double leftPeak = Extrema(x => firstSlope - load * (x * x * x / 6 + a * x * x / 2 + a * a * x / 2) - leftEndLoad * (x * x / 2 + a * x),
                x => firstSlope * x - load * (Math.Pow(x, 4) / 24 + a * Math.Pow(x, 3) / 6 + a * a * x * x / 4) - leftEndLoad * (x * x * x / 6 + a * x * x / 2), new List<double> { -a, 0 });
            double rightPeak = Extrema(x => lastSlope - load * (b * b * x / 2 - b * x * x / 2 + x * x * x / 6) - rightEndLoad * (b * x - x * x / 2),
                x => lastSlope * x - load * (b * b * x * x / 4 - b * Math.Pow(x, 3) / 6 + Math.Pow(x, 4) / 24) - rightEndLoad * (b * x * x / 2 - x * x * x / 6), new List<double> { 0, b });
            // Overhang displacement includes the rotation of the adjacent supported span.
            if (a > 1e-10) result.regions.Add(new DeflectionRegion { StartMm = 0, EndMm = a * 1000, PeakMm = leftPeak / ei * 1000, IsOverhang = true });
            if (b > 1e-10) result.regions.Add(new DeflectionRegion { StartMm = supports[n - 1] * 1000, EndMm = length * 1000, PeakMm = rightPeak / ei * 1000, IsOverhang = true });
            result.DeflectionMm = Math.Max(maxY, Math.Max(leftPeak, rightPeak)) / ei * 1000;
            result.length = length; result.load = load; result.ei = ei;
            result.supports = (double[])supports.Clone(); result.moments = m;
            result.firstSlope = firstSlope; result.lastSlope = lastSlope;
            result.leftEndLoad = leftEndLoad; result.rightEndLoad = rightEndLoad;
            return result;
        }
        private static double Extrema(Func<double, double> slope, Func<double, double> y, List<double> cuts)
        {
            cuts.Sort(); double max = cuts.Max(x => Math.Abs(y(x)));
            for (int i = 1; i < cuts.Count; i++)
            {
                double lo = cuts[i - 1], hi = cuts[i], sign = slope(lo);
                if (sign * slope(hi) >= 0) continue;
                for (int j = 0; j < 42; j++) { double mid = (lo + hi) / 2; if (slope(mid) * sign > 0) lo = mid; else hi = mid; }
                max = Math.Max(max, Math.Abs(y((lo + hi) / 2)));
            }
            return max;
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
