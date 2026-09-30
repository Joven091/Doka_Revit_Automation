using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class TowerInput
    {
        public double LengthMm { get; set; }
        public double LegCapacityKn { get; set; }
        public double TotalLineLoad { get; set; }
        // Moment of transverse line loads about the concrete beam centre, kN (kN/m * m).
        public double TransverseLoadMoment { get; set; }
        public double DeflectionLimitMm { get; set; }
    }
    public sealed class TowerLayout
    {
        public double[] PositionsMm { get; internal set; }
        public bool[] Joined { get; internal set; }
        public double WidthMm { get; internal set; }
        public bool Rotated { get; internal set; }
        public double LengthMm { get; internal set; }
        public double LeftLineLoad { get; internal set; }
        public double RightLineLoad { get; internal set; }
        public double[] LeftReactions { get; internal set; }
        public double[] RightReactions { get; internal set; }
        public int LegCount => PositionsMm.Length * 2;
        public int GroupCount => 1 + Joined.Count(x => !x);
        public int BayCount => Joined.Count(x => x);
        public double MaxLegKn { get; internal set; }
        public double LegCapacityKn { get; internal set; }
        public double DeflectionLimitMm { get; internal set; }
        public double MomentKnM { get; internal set; }
        public double ShearKn { get; internal set; }
        public double DeflectionMm { get; internal set; }
        public double Utilization { get; internal set; }
        public string Governing { get; internal set; }
        public int Tested { get; internal set; }
        public bool MinimumLegCountProven { get; internal set; }
        public string SearchNote => MinimumLegCountProven
            ? "Minimum leg count meets the load-capacity lower bound. Group count/layout are best found in a bounded search."
            : "Best valid layout found in a bounded search; global minimum leg/group count is not proven.";
        public string Summary => string.Format(CultureInfo.CurrentCulture, "Legs: {0} · tower groups: {1} · bays: {2}\n{3}; primary lines {4:0} mm apart\nMaximum leg {5:0.00}/{10:0.00} kN · governing utilization {6:0.0}%\nMoment {7:0.00}/10 kNm · shear {8:0.00}/22 kN\nDeflection {9:0.00}/{11:0.00} mm (includes end overhangs)",
            LegCount, GroupCount, BayCount, Rotated ? "Rotated isolated towers" : "1524 mm frames across beam", WidthMm, MaxLegKn, Utilization * 100, MomentKnM, ShearKn, DeflectionMm, LegCapacityKn, DeflectionLimitMm);
        public string Schedule
        {
            get
            {
                var lines = new List<string>(); int group = 1;
                for (int i = 0; i < PositionsMm.Length; i++)
                {
                    if (i > 0 && !Joined[i - 1]) group++;
                    lines.Add(string.Format(CultureInfo.CurrentCulture, "G{0} · x={1:0.###} mm · legs L/R={2:0.00}/{3:0.00} kN", group, PositionsMm[i], LeftReactions[i], RightReactions[i]));
                    if (i < Joined.Length) lines.Add(string.Format(CultureInfo.CurrentCulture, "  {0}: {1:0.###} mm", Joined[i] ? (Rotated ? "Frame span" : "Cross bay") : "Clear gap between leg planes", PositionsMm[i + 1] - PositionsMm[i]));
                }
                lines.Add(string.Format(CultureInfo.CurrentCulture, "End overhangs: {0:0.###} / {1:0.###} mm", PositionsMm[0], LengthMm - PositionsMm.Last()));
                return string.Join("\n", lines);
            }
        }
    }
    /// <summary>Deterministic bounded layout search, ranked legs, groups, then governing utilization.
    /// Two continuous double-H20 lines; no secondary, stock/joint, bearing or stability design.</summary>
    public static class TowerOptimizer
    {
        public static readonly double[] BaysMm = { 600, 1000, 1500, 2000 };
        public const double PairEI = 2 * 449.8756, PairMoment = 10, PairShear = 22;
        public static TowerLayout Optimize(TowerInput input)
        {
            Validate(input); var random = new Random(1524); int tested = 0, attempts = 0;
            double length = input.LengthMm, w = input.TotalLineLoad / 2 + Math.Abs(input.TransverseLoadMoment) / 2.0;
            if (!TowerBeamAnalysis.Finite(w * length / 1000) || w * length / 1000 / input.LegCapacityKn > 40)
                throw new ArgumentException("The load-capacity lower bound exceeds this search's 80-leg limit.");
            int lower = Math.Max(2, (int)Math.Ceiling(w * length / 1000 / input.LegCapacityKn - 1e-10));
            // A per-span moment range of 2*M and shear range of 2*V gives necessary span bounds.
            double longest = 1000 * Math.Min(Math.Sqrt(16 * PairMoment / w), 2 * PairShear / w);
            double tail = 1000 * Math.Min(Math.Sqrt(2 * PairMoment / w), PairShear / w);
            lower = Math.Max(lower, 1 + (int)Math.Ceiling(Math.Max(0, length - 2 * tail) / longest - 1e-10));
            TowerLayout best = null;
            for (int count = lower; count <= 40 && attempts < 200000; count++)
            {
                // Rotated groups are isolated: joining across the beam would require more than two primary lines.
                for (int rotation = 0; rotation < 2; rotation++)
                {
                    if (rotation == 1 && count % 2 != 0) continue;
                    int minGroups = rotation == 1 ? count / 2 : 1;
                    for (int groups = minGroups; groups <= count / 2; groups++)
                    {
                        if (best != null && groups > best.GroupCount) break;
                        int joins = count - groups;
                        if ((rotation == 1 ? joins * 1524 : joins * 600) > length) continue;
                        // Small joined runs enumerate every bay sequence; larger runs use reproducible mixed samples.
                        int sequences = rotation == 1 ? 4 : (int)Math.Min(1024, Math.Pow(4, joins));
                        int trials = Math.Min(4096, Math.Max(256, sequences * 4));
                        for (int trial = 0; trial < trials && attempts < 200000; trial++)
                        {
                            attempts++;
                            var sizes = Enumerable.Repeat(2, groups).ToArray();
                            for (int j = 0; j < count - groups * 2; j++) sizes[trial < sequences ? j % groups : random.Next(groups)]++;
                            var joined = new bool[count - 1]; int p = 0;
                            foreach (int size in sizes) { for (int j = 1; j < size; j++) joined[p++] = true; if (p < count - 1) p++; }
                            double width = rotation == 0 ? 1524 : BaysMm[trial % 4];
                            var spans = new double[count - 1]; int code = trial % sequences; double used = 0;
                            for (int j = 0; j < spans.Length; j++) if (joined[j])
                            {
                                int digit = trial < sequences * 2 && joins <= 6 ? code % 4 : random.Next(4); code /= 4;
                                spans[j] = rotation == 1 ? 1524 : BaysMm[digit]; used += spans[j];
                            }
                            if (used > length) continue;
                            double remaining = length - used;
                            // Allocate remaining length to gaps and ends. Include symmetric and asymmetric candidates.
                            var weights = new double[groups + 1];
                            for (int j = 0; j < weights.Length; j++)
                            {
                                double nominal = j == 0 || j == groups ? .30 : 1;
                                weights[j] = nominal * (trial < sequences ? 1 : .15 + random.NextDouble() * 1.7);
                            }
                            double sum = weights.Sum(); var spaces = weights.Select(x => remaining * x / sum).ToArray();
                            // Preserve exact manufactured bay dimensions. Only gaps and the left offset snap to 25 mm.
                            for (int j = 0; j < spaces.Length - 1; j++) spaces[j] = Math.Floor(spaces[j] / 25) * 25;
                            int gap = 1; var positions = new double[count]; positions[0] = spaces[0];
                            for (int j = 0; j < spans.Length; j++) { if (!joined[j]) spans[j] = spaces[gap++]; positions[j + 1] = positions[j] + spans[j]; }
                            if (spans.Any(x => x < 25) || positions.Last() > length) continue;
                            if (rotation == 0) for (int j = 0; j < joined.Length; j++)
                                if (!joined[j] && BaysMm.Contains(spans[j])) joined[j] = true;
                            tested++;
                            var candidate = Evaluate(input, positions, joined, width, rotation == 1);
                            if (candidate != null && (best == null || candidate.GroupCount < best.GroupCount || candidate.GroupCount == best.GroupCount && candidate.Utilization < best.Utilization)) best = candidate;
                        }
                    }
                }
                if (best != null) break;
            }
            if (best == null) throw new ArgumentException("No valid tower layout found within the bounded search (up to 80 legs). This does not prove that no feasible layout exists.");
            best.Tested = tested;
            int capacityBound = Math.Max(2, (int)Math.Ceiling((input.TotalLineLoad / 2 + Math.Abs(input.TransverseLoadMoment) / 2) * length / 1000 / input.LegCapacityKn - 1e-10));
            best.MinimumLegCountProven = best.PositionsMm.Length == capacityBound; return best;
        }
        public static TowerLayout Evaluate(TowerInput input, double[] positions, bool[] joined, double width, bool rotated)
        {
            Validate(input);
            if (positions == null || joined == null || positions.Length < 2 || joined.Length != positions.Length - 1 || !TowerBeamAnalysis.Finite(width) || width <= 0) throw new ArgumentException("Invalid tower layout.");
            if (rotated ? !BaysMm.Contains(width) : width != 1524) return null;
            int run = 1;
            for (int i = 0; i < joined.Length; i++)
            {
                double distance = positions[i + 1] - positions[i];
                if (joined[i]) { if (rotated ? Math.Abs(distance - 1524) > 1e-6 : !BaysMm.Any(x => Math.Abs(x - distance) < 1e-6)) return null; run++; }
                else { if (run < 2 || rotated && run != 2) return null; run = 1; }
            }
            if (run < 2 || rotated && run != 2) return null;
            double left = input.TotalLineLoad / 2 - input.TransverseLoadMoment / (width / 1000), right = input.TotalLineLoad - left;
            if (Math.Min(left, right) <= 0) return null;
            var response = TowerBeamAnalysis.Analyze(input.LengthMm / 1000, positions.Select(x => x / 1000).ToArray(), Math.Max(left, right), PairEI);
            double maxLeg = response.Reactions.Max();
            if (response.Reactions.Min() < -1e-9 * maxLeg) return null;
            var utilization = new[] { maxLeg / input.LegCapacityKn, response.Moment / PairMoment, response.Shear / PairShear, response.DeflectionMm / input.DeflectionLimitMm };
            if (utilization.Any(x => !TowerBeamAnalysis.Finite(x)) || utilization.Max() > 1 + 1e-10) return null;
            return new TowerLayout { PositionsMm = (double[])positions.Clone(), Joined = (bool[])joined.Clone(), WidthMm = width, Rotated = rotated, LengthMm = input.LengthMm,
                LeftLineLoad = left, RightLineLoad = right, LeftReactions = response.Reactions.Select(x => x * left / Math.Max(left, right)).ToArray(), RightReactions = response.Reactions.Select(x => x * right / Math.Max(left, right)).ToArray(),
                MaxLegKn = maxLeg, LegCapacityKn = input.LegCapacityKn, DeflectionLimitMm = input.DeflectionLimitMm, MomentKnM = response.Moment, ShearKn = response.Shear, DeflectionMm = response.DeflectionMm,
                Utilization = utilization.Max(), Governing = new[] { "Leg capacity", "Primary moment", "Primary shear", "Primary deflection" }[Array.IndexOf(utilization, utilization.Max())] };
        }
        private static void Validate(TowerInput input)
        {
            if (input == null || new[] { input.LengthMm, input.LegCapacityKn, input.TotalLineLoad, input.DeflectionLimitMm }.Any(x => !TowerBeamAnalysis.Finite(x) || x <= 0)
                || !TowerBeamAnalysis.Finite(input.TransverseLoadMoment)) throw new ArgumentException("Tower length, leg capacity, load and deflection limit must be finite and positive.");
            if (input.LengthMm > 50000) throw new ArgumentException("Tower optimization supports beam lengths up to 50,000 mm.");
        }
    }
}
