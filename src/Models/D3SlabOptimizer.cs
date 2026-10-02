using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class D3SlabInput
    {
        public double LengthMm { get; set; } = 6000;
        public double WidthMm { get; set; } = 5000;
        public double ThicknessMm { get; set; } = 200;
        public double LegCapacityKn { get; set; } = 70;
        public double MinimumEdgeMm { get; set; } = 200;
        public double MaximumEdgeMm { get; set; } = 350;
        public double LiveLoadKnM2 { get; set; } = 2.25;
        public double FormworkKnM2 { get; set; } = .4;
        public bool UseCustomLoads { get; set; }
        public int DeflectionDenominator { get; set; } = 350;
        public double[] StockLengthsMm { get; set; } = TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths);
        public bool Exclude600Standalone { get; set; }
        public double AreaLoad => 25 * ThicknessMm / 1000 + (UseCustomLoads ? LiveLoadKnM2 : BeamCalculatorViewModel.HeapingLoad(25 * ThicknessMm / 1000) + .75) + (UseCustomLoads ? FormworkKnM2 : .4);
        public void Validate()
        {
            if (new[] { LengthMm, WidthMm, ThicknessMm, LegCapacityKn }.Any(x => !Finite(x) || x <= 0) || LengthMm > 30000 || WidthMm > 30000) throw new ArgumentException("Slab dimensions and leg capacity must be positive; length and width are limited to 30,000 mm.");
            if (!Finite(MinimumEdgeMm) || !Finite(MaximumEdgeMm) || MinimumEdgeMm < 200 || MaximumEdgeMm > 350 || MaximumEdgeMm < MinimumEdgeMm) throw new ArgumentException("Beam-shutter setbacks must lie between 200 and 350 mm.");
            if (Math.Min(LengthMm, WidthMm) <= 2 * MaximumEdgeMm) throw new ArgumentException("Slab dimensions must leave room inside the edge setbacks.");
            if (!new[] { 500, 350, 270 }.Contains(DeflectionDenominator)) throw new ArgumentException("Select L/500, L/350 or L/270.");
            if (UseCustomLoads && (!Finite(LiveLoadKnM2) || !Finite(FormworkKnM2) || LiveLoadKnM2 < 0 || FormworkKnM2 < 0)) throw new ArgumentException("Custom loads must be finite and nonnegative.");
            if (!Finite(AreaLoad)) throw new ArgumentException("The resulting slab load must be finite.");
            if (StockLengthsMm == null || StockLengthsMm.Length == 0 || StockLengthsMm.Length > 30 || StockLengthsMm.Any(x => !Finite(x) || x < 300 || x > 50000 || x != Math.Round(x))) throw new ArgumentException("Provide positive whole-mm stock lengths of at least 300 mm.");
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
    public sealed class D3SlabStock
    {
        public double StartMm, LengthMm; public int FirstSupport, LastSupport;
        public double EndMm => StartMm + LengthMm;
    }
    public sealed class D3SlabMember
    {
        public bool Primary; public int Line; public double CoordinateMm;
        public D3SlabStock Stock; public LoadedBeamAnalysis Analysis;
    }
    public sealed class D3SlabLayout
    {
        public bool SwapAxes, RotatedTowers;
        public double CoveredLengthMm, CoveredWidthMm, EdgeXmm, EdgeYmm, TowerWidthMm, SecondarySpacingMm, AreaLoadKnM2;
        public double[] StationsMm, PrimaryLinesMm, SecondaryRowsMm;
        public bool[] Joined;
        public D3SlabStock[] PrimaryStock, SecondaryStock;
        public D3SlabMember[] Members;
        public double[,] LegLoads;
        public double LegCapacityKn;
        public int DeflectionDenominator, Tested;
        public int LegCount => StationsMm.Length * PrimaryLinesMm.Length;
        public int PrimaryH20Count => PrimaryStock.Length * PrimaryLinesMm.Length * 2;
        public int SecondaryH20Count => SecondaryStock.Length * SecondaryRowsMm.Length;
        public double H20LengthMm => PrimaryStock.Sum(x => x.LengthMm) * PrimaryLinesMm.Length * 2 + SecondaryStock.Sum(x => x.LengthMm) * SecondaryRowsMm.Length;
        public double CoveredAreaM2 => CoveredLengthMm * CoveredWidthMm / 1e6;
        public double PeakLegKn => LegLoads.Cast<double>().Max();
        public double Utilization => Math.Max(PeakLegKn / LegCapacityKn, Members.Max(x => Math.Max(x.Analysis.DeflectionUtilization, Math.Max(x.Analysis.MomentKnM / (x.Primary ? 10 : 5), x.Analysis.ShearKn / (x.Primary ? 22 : 11)))));
        public double OverlapMm => SecondaryStock.Sum(x => x.LengthMm) - CoveredWidthMm;
        public string Summary => $"{LegCount} legs · {PrimaryH20Count} primary H20s · {SecondaryH20Count} secondary H20s\nSecondary spacing {SecondarySpacingMm:0} mm · timber {H20LengthMm / 1000:0.00} m\nCovered area {CoveredAreaM2:0.00} m² · load {AreaLoadKnM2:0.00} kN/m²\nPeak leg {PeakLegKn:0.00}/{LegCapacityKn:0.00} kN · governing utilization {Utilization * 100:0.0}%";
        public string SearchNote => "Best valid layout found in a bounded grid/stock search; global minimum is not proven.";
        public string Schedule => string.Join("\n", Members.Select((m, i) => $"{(m.Primary ? "P" : "S")}{i + 1}: {(m.Primary ? "2 ×" : "1 ×")} {m.Stock.LengthMm:0} mm · line {m.Line + 1} · stock {m.Stock.StartMm:0}–{m.Stock.EndMm:0} mm · M {m.Analysis.MomentKnM:0.00} kNm · V {m.Analysis.ShearKn:0.00} kN · d {m.Analysis.DeflectionMm:0.00} mm · deflection {m.Analysis.DeflectionUtilization * 100:0.0}%"));
        public string LegSchedule => string.Join("\n", Enumerable.Range(0, PrimaryLinesMm.Length).SelectMany(y => Enumerable.Range(0, StationsMm.Length).Select(x => $"Leg {y * StationsMm.Length + x + 1}: x={StationsMm[x] + EdgeXmm:0}, y={PrimaryLinesMm[y] + EdgeYmm:0} mm · {LegLoads[y, x]:0.00}/{LegCapacityKn:0.00} kN")));
    }
    public static class D3SlabOptimizer
    {
        public static readonly double[] SecondarySpacingsMm = { 488, 407, 350, 305, 275 };
        private sealed class Longitudinal { public D3SlabStock[] Stock; public double[] Supports; public bool[] Joined; }
        private sealed class Secondary { public D3SlabStock[] Stock; public LoadedBeamAnalysis[] Analysis; public double[] LineReactions, Lines; }
        public static D3SlabLayout Optimize(D3SlabInput input, Func<bool> cancelled = null)
        {
            input.Validate(); D3SlabLayout best = null; int tested = 0;
            // Both slab directions, tower orientations and all five secondary spacings are tested.
            for (int axis = 0; axis < 2; axis++)
            {
                double x = axis == 0 ? input.LengthMm : input.WidthMm, y = axis == 0 ? input.WidthMm : input.LengthMm;
                var covers = PrimaryCovers(x, input).ToArray();
                foreach (var stock in covers) for (int rotation = 0; rotation < 2; rotation++) foreach (double width in rotation == 0 ? new[] { 1524.0 } : new[] { 600.0, 1000, 1500, 2000 })
                        {
                            if (rotation == 1 && input.Exclude600Standalone && width == 600) continue;
                            foreach (var longitudinal in Longitudinals(stock, rotation == 1, input.Exclude600Standalone))
                                for (int bands = 1; bands <= 8; bands++)
                                {
                                    if (best != null && 2 * bands * longitudinal.Supports.Length > best.LegCount) break;
                                    foreach (double edge in new[] { input.MinimumEdgeMm, input.MaximumEdgeMm, (input.MinimumEdgeMm + input.MaximumEdgeMm) / 2 }.Concat(input.StockLengthsMm.Select(v => (y - v) / 2).Where(v => v >= input.MinimumEdgeMm && v <= input.MaximumEdgeMm)).Distinct())
                                    {
                                        double coveredY = y - 2 * edge; if (width + 300 > coveredY) continue;
                                        foreach (double inset in bands == 1 ? new[] { (coveredY - width) / 2 } : new[] { 150.0, 300, 450, 600, 750 })
                                        {
                                            double step = bands == 1 ? 0 : (coveredY - 2 * inset - width) / (bands - 1); if (bands > 1 && step < width + 25) continue;
                                            var lines = Enumerable.Range(0, bands).SelectMany(b => new[] { inset + b * step, inset + b * step + width }).ToArray();
                                            var secondaries = SecondaryPlans(coveredY, lines, input).ToArray();
                                            if (secondaries.Length == 0) continue;
                                            foreach (double spacing in SecondarySpacingsMm)
                                            {
                                                if (cancelled?.Invoke() == true) throw new OperationCanceledException();
                                                // Centred rows, with no closing gap larger than the selected permissible spacing.
                                                int rows = (int)Math.Ceiling(longitudinal.Stock.Sum(s => s.LengthMm) / spacing);
                                                double coverX = longitudinal.Stock.Sum(s => s.LengthMm), offset = (coverX - (rows - 1) * spacing) / 2;
                                                var rowPositions = Enumerable.Range(0, rows).Select(i => offset + i * spacing).ToArray();
                                                foreach (var secondaryStock in secondaries)
                                                {
                                                    tested++;
                                                    var secondary = CheckSecondary(secondaryStock, coveredY, lines, input, spacing / 1000 * input.AreaLoad);
                                                    if (secondary == null) continue;
                                                    var candidate = Evaluate(input, axis == 1, rotation == 1, width, longitudinal, secondary, rowPositions, spacing, coveredY, edge);
                                                    if (candidate != null && (best == null || Better(candidate, best))) best = candidate;
                                                }
                                            }
                                        }
                                    }
                                }
                        }
            }
            if (best == null) throw new ArgumentException("No valid slab layout found within the bounded search. Check stock lengths, edge setbacks, loads or leg capacity.");
            best.Tested = tested; return best;
        }
        private static bool Better(D3SlabLayout a, D3SlabLayout b)
        {
            if (a.LegCount != b.LegCount) return a.LegCount < b.LegCount;
            if (Math.Abs(a.H20LengthMm - b.H20LengthMm) > .01) return a.H20LengthMm < b.H20LengthMm;
            if (a.PrimaryH20Count + a.SecondaryH20Count != b.PrimaryH20Count + b.SecondaryH20Count) return a.PrimaryH20Count + a.SecondaryH20Count < b.PrimaryH20Count + b.SecondaryH20Count;
            if (Math.Abs(a.EdgeXmm + a.EdgeYmm - b.EdgeXmm - b.EdgeYmm) > .01) return a.EdgeXmm + a.EdgeYmm < b.EdgeXmm + b.EdgeYmm;
            return a.Utilization < b.Utilization;
        }
        private static IEnumerable<double[]> PrimaryCovers(double size, D3SlabInput input)
        {
            int max = (int)Math.Floor(size - 2 * input.MinimumEdgeMm), min = (int)Math.Ceiling(size - 2 * input.MaximumEdgeMm);
            var states = new Dictionary<int, List<double[]>> { { 0, new List<double[]> { new double[0] } } };
            for (int total = 1; total <= max; total++) foreach (double stock in input.StockLengthsMm.OrderByDescending(s => s))
                    if (stock <= total && states.TryGetValue(total - (int)stock, out var previous))
                    {
                        if (!states.TryGetValue(total, out var candidates)) states[total] = candidates = new List<double[]>();
                        foreach (var p in previous) if (p.Length < 8) candidates.Add(p.Concat(new[] { stock }).ToArray());
                        states[total] = candidates.OrderBy(p => p.Length).ThenByDescending(p => p[0]).Take(2).ToList();
                    }
            return states.Where(p => p.Key >= min && p.Key <= max).OrderBy(p => p.Value.Min(s => s.Length)).ThenByDescending(p => p.Key)
                .Take(8).SelectMany(p => p.Value).Take(12).Select(p => p).ToArray();
        }
        private static IEnumerable<Longitudinal> Longitudinals(double[] stock, bool rotated, bool exclude600)
        {
            // Each primary run has independent stock ends and at least two supporting leg planes.
            var states = new List<Longitudinal> { new Longitudinal { Stock = new D3SlabStock[0], Supports = new double[0], Joined = new bool[0] } };
            foreach (double length in stock)
            {
                var patterns = new List<double[]>();
                foreach (double bay in rotated ? new[] { 1524.0 } : new[] { 2000.0, 1500, 1000, 600 })
                    for (int count = 2; count <= (rotated ? 2 : 4); count++) if ((count - 1) * bay <= length - 300 && !(exclude600 && count == 2 && bay == 600)) patterns.Add(Enumerable.Repeat(bay, count - 1).ToArray());
                if (!rotated) foreach (double a in TowerOptimizer.BaysMm) foreach (double b in TowerOptimizer.BaysMm) if (a != b && a + b <= length - 300) patterns.Add(new[] { a, b });
                var next = new List<Longitudinal>();
                foreach (var state in states) foreach (var bays in patterns)
                    {
                        double start = state.Stock.Sum(s => s.LengthMm), inset = (length - bays.Sum()) / 2; int first = state.Supports.Length;
                        var p = new List<double> { start + inset }; foreach (double bay in bays) p.Add(p.Last() + bay);
                        bool keepJoined = p.Count == 3 || exclude600 && (bays[0] == 600 || bays.Last() == 600);
                        var joins = state.Joined.Concat(first == 0 ? new bool[0] : new[] { false }).Concat(Enumerable.Range(1, p.Count - 1).Select(i => keepJoined || i % 2 == 1)).ToArray();
                        next.Add(new Longitudinal { Stock = state.Stock.Concat(new[] { new D3SlabStock { StartMm = start, LengthMm = length, FirstSupport = first, LastSupport = first + p.Count - 1 } }).ToArray(), Supports = state.Supports.Concat(p).ToArray(), Joined = joins });
                    }
                // Retain different support counts and mixed manufactured bays in a bounded deterministic beam search.
                states = next.GroupBy(s => s.Supports.Length).SelectMany(g => g.OrderBy(s => s.Stock.Sum(p => s.Supports[p.FirstSupport] - p.StartMm)).Take(8)).OrderBy(s => s.Supports.Length).Take(40).ToList();
            }
            return states;
        }
        private static IEnumerable<D3SlabStock[]> SecondaryPlans(double covered, double[] lines, D3SlabInput input)
        {
            var result = new List<D3SlabStock[]>();
            Action<List<D3SlabStock>> visit = null;
            visit = pieces =>
            {
                if (result.Count >= 16 || pieces.Count >= 8) return;
                int joint = pieces.Count == 0 ? 0 : pieces.Last().LastSupport;
                foreach (double length in input.StockLengthsMm.OrderByDescending(s => s))
                    foreach (double start in pieces.Count == 0 ? new[] { 0.0 } : new[] { lines[joint] - 150, covered - length }.Distinct())
                    {
                        double end = start + length; if (start < 0 || end > covered + 1e-7) continue;
                        if (pieces.Count > 0 && (start > lines[joint] - 150 + 1e-7 || start <= pieces.Last().StartMm + 1e-7 || end <= pieces.Last().EndMm + 1e-7 || pieces.Count > 1 && start < pieces[pieces.Count - 2].EndMm - 1e-7)) continue;
                        int first = Array.FindIndex(lines, s => s >= start + 150 - 1e-7), last = Array.FindLastIndex(lines, s => s <= end - 150 + 1e-7);
                        if (first < 0 || last <= first || first != (pieces.Count == 0 ? 0 : joint)) continue;
                        // Every crossed primary is a physical support; do not omit a line near a stock end.
                        if (first != Array.FindIndex(lines, s => s >= start - 1e-7) || last != Array.FindLastIndex(lines, s => s <= end + 1e-7)) continue;
                        var next = pieces.Concat(new[] { new D3SlabStock { StartMm = start, LengthMm = length, FirstSupport = first, LastSupport = last } }).ToList();
                        if (Math.Abs(end - covered) < 1e-7 && last == lines.Length - 1) { result.Add(next.ToArray()); continue; }
                        if (last < lines.Length - 1) visit(next);
                    }
            };
            visit(new List<D3SlabStock>());
            return result.OrderBy(p => p.Sum(s => s.LengthMm)).ThenBy(p => p.Length).Take(8);
        }
        private static Secondary CheckSecondary(D3SlabStock[] stock, double covered, double[] lines, D3SlabInput input, double load)
        {
            var analyses = new List<LoadedBeamAnalysis>(); var reactions = new double[lines.Length];
            foreach (var piece in stock)
            {
                var boundaries = stock.SelectMany(p => new[] { p.StartMm, p.EndMm }).Where(x => x >= piece.StartMm && x <= piece.EndMm).Concat(new[] { piece.StartMm, piece.EndMm }).Distinct().OrderBy(x => x).ToArray();
                var loads = Enumerable.Range(1, boundaries.Length - 1).Select(i => new BeamDistributedLoad
                {
                    StartMm = boundaries[i - 1] - piece.StartMm,
                    EndMm = boundaries[i] - piece.StartMm,
                    KnPerM = load / stock.Count(p => p.StartMm < (boundaries[i - 1] + boundaries[i]) / 2 && p.EndMm > (boundaries[i - 1] + boundaries[i]) / 2)
                }).ToArray();
                var analysis = LoadedBeamAnalysis.Analyze(piece.LengthMm, lines.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => x - piece.StartMm).ToArray(), loads, null, 449.8756, input.DeflectionDenominator);
                if (!Pass(analysis, 5, 11)) return null;
                analyses.Add(analysis); for (int i = piece.FirstSupport; i <= piece.LastSupport; i++) reactions[i] += analysis.Reactions[i - piece.FirstSupport];
            }
            if (Math.Abs(reactions.Sum() - covered / 1000 * load) > 1e-6 * Math.Max(1, covered / 1000 * load)) throw new ArgumentException("Secondary overlap loads were not conserved.");
            return new Secondary { Stock = stock, Analysis = analyses.ToArray(), LineReactions = reactions, Lines = lines };
        }
        private static bool Pass(LoadedBeamAnalysis a, double moment, double shear) => a.MomentKnM <= moment + 1e-8 && a.ShearKn <= shear + 1e-8 && a.DeflectionUtilization <= 1 + 1e-8 && a.Reactions.Min() >= -1e-7;
        private static D3SlabLayout Evaluate(D3SlabInput input, bool swap, bool rotated, double width, Longitudinal plan, Secondary secondary, double[] rows, double spacing, double coveredY, double edgeY)
        {
            var members = new List<D3SlabMember>(); var legs = new double[secondary.LineReactions.Length, plan.Supports.Length];
            double coverX = plan.Stock.Sum(s => s.LengthMm);
            if (plan.Stock.Skip(1).Any(p => rows.Any(x => Math.Abs(x - p.StartMm) < 40))) return null;
            var tributaries = rows.Select((v, i) => (i == rows.Length - 1 ? coverX : (v + rows[i + 1]) / 2) - (i == 0 ? 0 : (rows[i - 1] + v) / 2)).ToArray();
            for (int line = 0; line < secondary.LineReactions.Length; line++) foreach (var piece in plan.Stock)
                {
                    // Every secondary reaction enters the primary at its actual crossing as a point load.
                    var loads = rows.Select((x, i) => new { x, i }).Where(p => p.x >= piece.StartMm - 1e-7 && p.x < piece.EndMm - 1e-7).Select(p => new BeamPointLoad { PositionMm = p.x - piece.StartMm, Kn = secondary.LineReactions[line] * tributaries[p.i] / spacing }).ToArray();
                    var a = LoadedBeamAnalysis.Analyze(piece.LengthMm, plan.Supports.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => x - piece.StartMm).ToArray(), null, loads, 899.7512, input.DeflectionDenominator);
                    if (!Pass(a, 10, 22)) return null;
                    for (int i = piece.FirstSupport; i <= piece.LastSupport; i++) { legs[line, i] = a.Reactions[i - piece.FirstSupport]; if (legs[line, i] > input.LegCapacityKn + 1e-8) return null; }
                    members.Add(new D3SlabMember { Primary = true, Line = line, CoordinateMm = 0, Stock = piece, Analysis = a });
                }
            for (int row = 0; row < rows.Length; row++) for (int p = 0; p < secondary.Stock.Length; p++)
                {
                    // Edge rows have smaller tributary strips; recalculate to retain their actual individual reactions.
                    var check = secondary.Analysis[p].Scale(tributaries[row] / spacing);
                    // The representative full-spacing check bounds the closing strips, which never exceed spacing.
                    members.Add(new D3SlabMember { Primary = false, Line = row, CoordinateMm = rows[row], Stock = secondary.Stock[p], Analysis = check });
                }
            double total = legs.Cast<double>().Sum(), expected = coverX * coveredY / 1e6 * input.AreaLoad;
            if (Math.Abs(total - expected) > 1e-5 * Math.Max(1, expected)) return null;
            return new D3SlabLayout
            {
                SwapAxes = swap,
                RotatedTowers = rotated,
                TowerWidthMm = width,
                StationsMm = plan.Supports,
                Joined = plan.Joined,
                PrimaryStock = plan.Stock,
                SecondaryStock = secondary.Stock,
                PrimaryLinesMm = secondary.Lines,
                SecondaryRowsMm = rows,
                SecondarySpacingMm = spacing,
                CoveredLengthMm = coverX,
                CoveredWidthMm = coveredY,
                EdgeXmm = ((swap ? input.WidthMm : input.LengthMm) - coverX) / 2,
                EdgeYmm = edgeY,
                AreaLoadKnM2 = input.AreaLoad,
                LegCapacityKn = input.LegCapacityKn,
                LegLoads = legs,
                Members = members.ToArray(),
                DeflectionDenominator = input.DeflectionDenominator
            };
        }
    }
}
