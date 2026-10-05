using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class TowerStockPiece
    {
        public double StockLengthMm { get; internal set; }
        public bool IsCustomLength { get; internal set; }
        public double StartMm { get; internal set; }
        public double EndMm => StartMm + StockLengthMm;
        public int FirstSupport { get; internal set; }
        public int LastSupport { get; internal set; }
        public double MomentKnM { get; internal set; }
        public double ShearKn { get; internal set; }
        public double DeflectionMm { get; internal set; }
        public double DeflectionUtilization { get; internal set; }
        public double GoverningDeflectionMm { get; internal set; }
        public double GoverningDeflectionLimitMm { get; internal set; }
        public string GoverningDeflectionRegion { get; internal set; }
    }
    public sealed class TowerStockPlan
    {
        public TowerStockPiece[] Pieces { get; internal set; }
        public double[] Reactions { get; internal set; }
        public double MomentKnM => Pieces.Max(x => x.MomentKnM);
        public double ShearKn => Pieces.Max(x => x.ShearKn);
        public double DeflectionMm => Pieces.Max(x => x.DeflectionMm);
        public double DeflectionUtilization => Pieces.Max(x => x.DeflectionUtilization);
        public int TotalH20Count => Pieces.Length * 4;
        public double TotalStockLengthMm => Pieces.Sum(x => x.StockLengthMm) * 4;
        internal double Utilization;
        public string Schedule
        {
            get
            {
                var lines = new List<string> { "Same stock arrangement on both primary lines: 2 H20s per piece per line (4 total). No cutting, overlaps or projection beyond concrete ends. Member breaks have no shared support or bending continuity." };
                for (int i = 0; i < Pieces.Length; i++)
                {
                    var p = Pieces[i];
                    lines.Add(string.Format(CultureInfo.CurrentCulture, "P{0}: 4 × {1:0.###} mm · x={2:0.###}–{3:0.###} mm · supports {4}–{5}\n  M {6:0.00} kNm · V {7:0.00} kN · d {8:0.00} mm",
                        i + 1, p.StockLengthMm, p.StartMm, p.EndMm, p.FirstSupport + 1, p.LastSupport + 1, p.MomentKnM, p.ShearKn, p.DeflectionMm) + (p.IsCustomLength ? " · custom length (25 mm increments)" : ""));
                    lines.Add($"  Deflection: {p.GoverningDeflectionMm:0.00}/{p.GoverningDeflectionLimitMm:0.00} mm · {p.GoverningDeflectionRegion}");
                }
                lines.Add("Stock quantities: " + string.Join("; ", Pieces.GroupBy(p => p.StockLengthMm).OrderBy(g => g.Key).Select(g => string.Format(CultureInfo.CurrentCulture, "{0} × {1:0.###} mm", g.Count() * 4, g.Key))));
                return string.Join("\n", lines);
            }
        }
    }
    /// <summary>Exact uncut stock cover, no overlaps. Independent members have free ends and at least two supports each.
    /// No support at a member break is allowed; support-centre end setback is a planning input, not a bearing check.</summary>
    public static class TowerStockPlanner
    {
        public const string DefaultLengths = "1250; 1800; 2450; 2650; 2900; 3300; 3600; 3900; 4500; 4900";
        // Keep the entered catalogue separate: only additional lengths must be multiples of 25 mm.
        internal static double[] CandidateLengths(TowerInput input, double minimum, double maximum)
        {
            var stocks = input.StockLengthsMm.Where(x => x >= minimum && x <= maximum);
            if (!input.AllowCustomH20Lengths) return stocks.OrderBy(x => x).ToArray();
            int first = (int)Math.Ceiling(Math.Max(25, minimum) / 25), last = (int)Math.Floor(Math.Min(50000, maximum) / 25);
            return stocks.Concat(Enumerable.Range(first, Math.Max(0, last-first+1)).Select(x => x*25.0)).Distinct().OrderBy(x => x).ToArray();
        }
        // Evaluate the previous stock search as well. Candidate caps must never replace a better stock solution.
        internal static T SearchWithCustom<T>(TowerInput input, Func<T> search, Func<T,T,bool> better) where T : class
        {
            if (!input.AllowCustomH20Lengths) return search();
            T stock = null;
            input.AllowCustomH20Lengths = false;
            try { stock = search(); } catch (ArgumentException) { }
            finally { input.AllowCustomH20Lengths = true; }
            try { var custom = search(); return stock == null || better(custom,stock) ? custom : stock; }
            catch (ArgumentException) { if (stock != null) return stock; throw; }
        }
        internal static int[] CoverageCounts(int total, double[] stocks)
        {
            var counts = Enumerable.Repeat(int.MaxValue,total+1).ToArray(); counts[0]=0;
            int step=0;
            foreach(double stock in stocks) {int b=(int)stock;while(b!=0){int r=step%b;step=b;b=r;}}
            if(step==0)return counts;
            for(int end=step;end<=total;end+=step) foreach(double stock in stocks)
                if(stock<=end && counts[end-(int)stock]!=int.MaxValue) counts[end]=Math.Min(counts[end],counts[end-(int)stock]+1);
            return counts;
        }
        public static double[] ParseLengths(string text)
        {
            var tokens = (text ?? "").Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Length > 30) throw new ArgumentException("Enter 1–30 H20 stock lengths in whole millimetres, separated by semicolons.");
            var result = new List<double>();
            foreach (string token in tokens)
            {
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int mm) || mm <= 0 || mm > 50000)
                    throw new ArgumentException("H20 stock lengths must be whole millimetres from 1 to 50,000 (for example 1250; 2450; 4900).");
                result.Add(mm);
            }
            return result.Distinct().OrderBy(x => x).ToArray();
        }
        public static int MinimumPieces(double length, double[] stocks)
        {
            if (!TowerBeamAnalysis.Finite(length) || length <= 0 || length > 50000 || Math.Abs(length - Math.Round(length)) > 1e-7) return int.MaxValue;
            if(stocks.Contains(length))return 1;
            return CoverageCounts((int)Math.Round(length),stocks)[(int)Math.Round(length)];
        }
        public static TowerStockPlan Find(TowerInput input, double[] positions, double load)
        {
            bool analyzed=false; int n = positions.Length; double length = input.LengthMm, setback = input.StockEndSetbackMm;
            var stocks = CandidateLengths(input,25,length);
            var states = new Dictionary<int, TowerStockPlan>();
            states[0] = new TowerStockPlan { Pieces = new TowerStockPiece[0], Reactions = new double[n] };
            var pending = new SortedSet<int> { 0 };
            while (pending.Count > 0)
            {
                int start = pending.Min; pending.Remove(start); var previous = states[start];
                int first = previous.Pieces.Length == 0 ? 0 : previous.Pieces.Last().LastSupport + 1;
                if (first >= n) continue;
                foreach (double stock in stocks)
                {
                    double end = start + stock;
                    if (end > length + 1e-7 || positions[first] - start < setback - 1e-7) continue;
                    int last = first; while (last + 1 < n && positions[last + 1] < end - 1e-7) last++;
                    if (last <= first || end - positions[last] < setback - 1e-7) continue;
                    if (last + 1 < n && positions[last + 1] < end + setback - 1e-7) continue;
                    if (end < length - 1e-7 && last + 1 == n || Math.Abs(end - length) < 1e-7 && last + 1 != n) continue;
                    analyzed=true;var response = TowerBeamAnalysis.Analyze(stock / 1000,
                        positions.Skip(first).Take(last - first + 1).Select(x => (x - start) / 1000).ToArray(), load, TowerOptimizer.PairEI);
                    var u = new[] { response.Reactions.Max() / input.LegCapacityKn, response.Moment / TowerOptimizer.PairMoment,
                        response.Shear / TowerOptimizer.PairShear, response.DeflectionUtilization(input.DeflectionLimitMm, input.DeflectionDenominator) };
                    input.Diagnostics?.Primary(response,input,$"stock {stock:0} mm · x={start+input.DiagnosticOffsetMm:0.###}–{end+input.DiagnosticOffsetMm:0.###} mm · support x={string.Join(", ",positions.Skip(first).Take(last-first+1).Select(x=>(x+input.DiagnosticOffsetMm).ToString("0.###")))} mm · governing loaded line");
                    if (response.Reactions.Min() < -1e-9 * response.Reactions.Max() || u.Any(x => !TowerBeamAnalysis.Finite(x) || x > 1 + 1e-10)) continue;
                    var region = response.GoverningDeflection(input.DeflectionLimitMm, input.DeflectionDenominator);
                    var piece = new TowerStockPiece { StartMm = start, StockLengthMm = stock, IsCustomLength = !input.StockLengthsMm.Contains(stock), FirstSupport = first, LastSupport = last,
                        MomentKnM = response.Moment, ShearKn = response.Shear, DeflectionMm = response.DeflectionMm, DeflectionUtilization = u[3], GoverningDeflectionMm = region.PeakMm,
                        GoverningDeflectionLimitMm = region.AllowableMm(input.DeflectionLimitMm, input.DeflectionDenominator),
                        GoverningDeflectionRegion = $"{(region.IsOverhang ? "overhang" : "span")} {region.EndMm-region.StartMm:0.###} mm" };
                    var reactions = (double[])previous.Reactions.Clone();
                    for (int k = first; k <= last; k++) reactions[k] = response.Reactions[k - first];
                    var next = new TowerStockPlan { Pieces = previous.Pieces.Concat(new[] { piece }).ToArray(), Reactions = reactions, Utilization = Math.Max(previous.Utilization, u.Max()) };
                    int key = (int)Math.Round(end);
                    if (!states.TryGetValue(key, out var old) || next.TotalH20Count < old.TotalH20Count || next.TotalH20Count == old.TotalH20Count && next.Utilization < old.Utilization)
                    { states[key] = next; if (end < length - 1e-7) pending.Add(key); }
                }
            }
            if(!analyzed)input.Diagnostics?.Geometry("primary stock supports",$"Primary stock/support geometry: no member can be checked in this support pattern with at least two supports and {setback:0.###} mm stock-end setbacks.","Review the stock lengths, support positions and end setbacks.");
            return states.TryGetValue((int)Math.Round(length), out var result) ? result : null;
        }
    }
}
