using System;
using System.Collections.Generic;
namespace EngineeringDashboard
{
    /// <summary>Maximum displacement between supports under full uniform loading.
    /// Equal spans, constant EI, two equal end cantilevers. Cantilever tip checks are separate.</summary>
    public static class ContinuousSpanDeflection
    {
        public static double Maximum(int count, double length, double cantilever, double load, double ei)
        {
            return FromMoments(ContinuousBeamResponse.SupportMoments(count, length, cantilever, cantilever, load), length, load, ei);
        }
        internal static double FromMoments(double[] moments, double length, double load, double ei)
        {
            if (length <= 0 || ei <= 0) throw new ArgumentException("Invalid span model.");
            int count = moments.Length;
            double maximum = 0;
            for (int i = 0; i < count - 1; i++)
            {
                double m = moments[i], v = load * length / 2 + (moments[i + 1] - m) / length;
                double c = -(m * length * length / 2 + v * Math.Pow(length, 3) / 6 - load * Math.Pow(length, 4) / 24) / length;
                Func<double, double> slope = x => c + m * x + v * x * x / 2 - load * x * x * x / 6;
                Func<double, double> displacement = x => (c * x + m * x * x / 2 + v * x * x * x / 6 - load * Math.Pow(x, 4) / 24) / ei;
                // Roots of curvature partition slope into monotonic intervals.
                var points = new List<double> { 0, length };
                double discriminant = v * v + 2 * load * m;
                if (load > 0 && discriminant >= 0)
                    foreach (double x in new[] { (v - Math.Sqrt(discriminant)) / load, (v + Math.Sqrt(discriminant)) / load })
                        if (x > 0 && x < length) points.Add(x);
                points.Sort();
                foreach (double x in points) maximum = Math.Max(maximum, Math.Abs(displacement(x)));
                for (int j = 1; j < points.Count; j++)
                {
                    double lo = points[j - 1], hi = points[j], flo = slope(lo), fhi = slope(hi);
                    if (flo * fhi >= 0) continue;
                    for (int step = 0; step < 60; step++)
                    {
                        double mid = (lo + hi) / 2, fmid = slope(mid);
                        if (flo * fmid <= 0) hi = mid; else { lo = mid; flo = fmid; }
                    }
                    maximum = Math.Max(maximum, Math.Abs(displacement((lo + hi) / 2)));
                }
            }
            return maximum;
        }
    }
}
