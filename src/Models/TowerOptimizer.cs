using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class TowerOptimizationException : ArgumentException
    {
        public string Details {get;}
        internal TowerOptimizationException(string message,string details):base(message) {Details=details;}
    }
    /// <summary>Bounded rejection samples; examples may belong to different candidates, never a passing layout.</summary>
    internal sealed class TowerSearchDiagnostics
    {
        private sealed class Failure {public int Stage,Count;public double Score;public string Text,Advice,Summary;public bool Blocker;}
        private readonly Dictionary<string,Failure> failures=new Dictionary<string,Failure>();
        private readonly int denominator;
        private readonly double fixedLimit;
        private int checks;
        private readonly BeamSearchTrace trace;
        private readonly bool ignoreUplift;
        public TowerSearchDiagnostics(int denominator,double fixedLimit=0,BeamSearchTrace trace=null,bool ignoreUplift=false) {this.denominator=denominator;this.fixedLimit=fixedLimit;this.trace=trace;this.ignoreUplift=ignoreUplift;}
        private void Add(string key,int stage,double score,string text,string advice,string summary=null,bool blocker=false)
        {
            trace?.Increment(BeamSearchCounter.RejectionChecks);
            if(!failures.TryGetValue(key,out var f)) failures[key]=f=new Failure {Stage=stage,Score=double.PositiveInfinity};
            f.Count++;if(score<f.Score) {f.Score=score;f.Text=text;f.Advice=advice;f.Summary=summary??text;f.Blocker=blocker;}
        }
        public void Geometry(string key,string text,string advice,double score=0,string summary=null,bool blocker=false)=>Add(key,0,score,text,advice,summary,blocker);
        public void Limit(string scope,string check,int stage,double demand,double limit,string units,string location,string advice,double tolerance=1e-8)
        {
            trace?.Increment(BeamSearchCounter.NumericalChecks);checks++;if(demand<=limit+tolerance)return;
            Add(scope+"/"+check,stage,demand/limit,$"{scope} · {check}: {demand:0.###} / {limit:0.###} {units} ({demand/limit*100:0.0}%). {location}",advice,$"{scope} · {check}: {demand:0.###} / {limit:0.###} {units} ({demand/limit*100:0.0}%).");
        }
        public void Bearing(string scope,int stage,double reaction,string location,double tolerance=1e-7)
        {
            trace?.Increment(BeamSearchCounter.NumericalChecks);checks++;if(ignoreUplift || reaction>=-tolerance)return;
            Add(scope+"/uplift",stage,Math.Abs(reaction),$"{scope} · uplift: reaction {reaction:G6} kN; minimum 0 kN. {location}","Review foot positions, primary-line/tower width and one-sided loading balance.");
        }
        internal bool LazyLocations=true;
        public void AnalysisLazy(LoadedBeamAnalysis a,double scale,int count,string scope,int stage,Func<string> location)
        {
            double allowable=a.GoverningLengthMm/denominator;
            bool failing=!LazyLocations || a.MomentKnM*scale>TowerOptimizer.PairMoment*count/2+1e-8
                || a.ShearKn*scale>TowerOptimizer.PairShear*count/2+1e-8
                || a.DeflectionEvaluated && a.DeflectionUtilization*allowable*scale>allowable+allowable*1e-8
                || !ignoreUplift && a.Reactions.Min()*scale< -1e-7;
            Analysis(a,scale,count,scope,stage,failing ? location() : "");
        }
        public void LegsLazy(double[] reactions,double capacity,string scope,Func<string> location,double tolerance=1e-8,double bearingTolerance=1e-7)
            =>Legs(reactions,capacity,scope,!LazyLocations || reactions.Max()>capacity+tolerance || !ignoreUplift && reactions.Min()< -bearingTolerance ? location() : "",tolerance,bearingTolerance);
        public void Analysis(LoadedBeamAnalysis a,double scale,int count,string scope,int stage,string location)
        {
            string advice=stage==1 ? "Review upper spacing, stock length and foot positions." : stage==2 ? "Review upper row spacing, lower secondary stock and foot/primary bearing positions." : "Review primary stock lengths and tower support spacing.";
            Limit(scope,"moment",stage,a.MomentKnM*scale,TowerOptimizer.PairMoment*count/2,"kNm",location,advice);
            Limit(scope,"shear",stage,a.ShearKn*scale,TowerOptimizer.PairShear*count/2,"kN",location,advice);
            double allowable=a.GoverningLengthMm/denominator;
            if(a.DeflectionEvaluated)
            {
                double demand=a.DeflectionUtilization*allowable*scale;
                string details=!LazyLocations || demand>allowable+allowable*1e-8 ? location+$" · governing span/overhang {a.GoverningLengthMm:0.###} mm (L/{denominator})" : "";
                Limit(scope,"deflection",stage,demand,allowable,"mm",details,advice,allowable*1e-8);
            }
            Bearing(scope,stage,a.Reactions.Min()*scale,location);
        }
        public void Primary(TowerBeamAnalysis a,TowerInput input,string location)
        {
            string scope="Primary pair";var region=a.GoverningDeflection(input.DeflectionLimitMm,input.DeflectionDenominator);
            Limit(scope,"moment",3,a.Moment,TowerOptimizer.PairMoment,"kNm",location,"Review primary support spacing and stock layout.",1e-10*TowerOptimizer.PairMoment);
            Limit(scope,"shear",3,a.Shear,TowerOptimizer.PairShear,"kN",location,"Review primary support spacing and stock layout.",1e-10*TowerOptimizer.PairShear);
            Limit(scope,"deflection",3,region.PeakMm,region.AllowableMm(fixedLimit,denominator),"mm",location+$" · {(region.IsOverhang ? "overhang" : "span")} {region.EndMm-region.StartMm:0.###} mm", "Review the support span/overhang and selected criterion.",1e-10*region.AllowableMm(fixedLimit,denominator));
            Legs(a.Reactions,input.LegCapacityKn,scope,location,input.LegCapacityKn*1e-10,a.Reactions.Max()*1e-9);
        }
        public void Legs(double[] reactions,double capacity,string scope,string location,double tolerance=1e-8,double bearingTolerance=1e-7)
        {
            int leg=Array.IndexOf(reactions,reactions.Max());
            Limit(scope,"leg load",4,reactions[leg],capacity,"kN",(!LazyLocations || reactions[leg]>capacity+tolerance ? location+$" · support {leg+1}" : ""),"Review support layout and applied loads. Use the permissible leg load for the actual tower configuration.",tolerance);
            Bearing(scope+" legs",4,reactions.Min(),location,bearingTolerance);
        }
        public TowerOptimizationException Exception(string title)
        {
            var numeric=failures.Values.Where(f=>f.Stage>0).OrderByDescending(f=>f.Stage).ThenBy(f=>f.Score).ToArray();
            var geometry=failures.Values.Where(f=>f.Stage==0).ToArray();
            var selected=geometry.Where(f=>f.Blocker).Concat(numeric).Concat(geometry.Where(f=>!f.Blocker)).ToArray();
            string summary=selected.Length==0 ? title : "No solution found in the searched candidates. Example rejection: "+selected[0].Summary+" Open failure details.";
            string details=title+"\nBounded search: this does not prove that no feasible arrangement exists.\nClosest recorded failing example for each check; examples may come from different layouts.\n"
                +string.Join("\n\n",selected.Select(f=>f.Text+$"\nRecorded failures: {f.Count}. "+f.Advice))
                +$"\n\nNumerical checks recorded: {checks}. Rejected layouts are not approved or displayed as solutions.";
            return new TowerOptimizationException(summary,details);
        }
        // Only use scoped evidence from the rejected full-transfer search. A receiving
        // beam can also carry an adjacent bay; its examples are not proof about this bay alone.
        internal string SeparateBayExplanation(int bay)
        {
            Func<string,string,bool> scoped=(key,prefix)=>key.StartsWith(prefix,StringComparison.Ordinal)
                && (key.Length==prefix.Length || !char.IsDigit(key[prefix.Length]));
            Func<Failure,string> describe=f=>f.Text+"\n"+f.Advice;
            var slab=failures.Where(e=>scoped(e.Key,"S"+(bay+1))).Select(e=>e.Value)
                .OrderByDescending(f=>f.Blocker).ThenByDescending(f=>f.Stage).ThenBy(f=>f.Score).ToArray();
            var beams=failures.Where(e=>scoped(e.Key,"B"+(bay+1)) || scoped(e.Key,"B"+(bay+2))).Select(e=>e.Value)
                .OrderByDescending(f=>f.Blocker).ThenByDescending(f=>f.Stage).ThenBy(f=>f.Score).ToArray();
            return $"S{bay+1}: no passing load-transfer arrangement found with the selected H20 stocks, spacing and bearing geometry."
                +(slab.Length==0 ? "" : "\nSlab H20 / geometry examples:\n"+string.Join("\n\n",slab.Select(describe)))
                +(beams.Length==0 ? "" : $"\nReceiving-beam examples (B{bay+1}, B{bay+2}; may also involve adjacent bays):\n"+string.Join("\n\n",beams.Select(describe)))
                +(slab.Length+beams.Length==0 ? "\nThe full-transfer network search failed; no bay-specific rejection was recorded. Review the full-transfer search details." : "")
                +"\nThese are rejected search examples, potentially from different layouts. The bounded search does not prove impossibility."
                +$"\nThis result carries no slab or shutter load from S{bay+1} to B{bay+1} or B{bay+2}. Its independent supports, H20s and design are not calculated or included in totals.";
        }
        // Workers own their diagnostics. Merge in original scenario order after they finish,
        // preserving counts, nearest failures and the first example when scores tie.
        internal void Merge(TowerSearchDiagnostics other)
        {
            checks+=other.checks;
            foreach(var entry in other.failures)
            {
                if(!failures.TryGetValue(entry.Key,out var current))
                {
                    var f=entry.Value;
                    failures[entry.Key]=new Failure {Stage=f.Stage,Count=f.Count,Score=f.Score,Text=f.Text,Advice=f.Advice,Summary=f.Summary,Blocker=f.Blocker};
                    continue;
                }
                current.Count+=entry.Value.Count;
                if(entry.Value.Score<current.Score)
                {
                    var f=entry.Value;current.Stage=f.Stage;current.Score=f.Score;current.Text=f.Text;current.Advice=f.Advice;current.Summary=f.Summary;current.Blocker=f.Blocker;
                }
            }
        }
    }
    public enum CustomH20LengthScope { Both, BeamSupports, SlabSupports }
    public sealed class CustomH20ScopeOption
    {
        public string Label { get; }
        public CustomH20LengthScope Value { get; }
        private CustomH20ScopeOption(string label, CustomH20LengthScope value) { Label=label; Value=value; }
        public static readonly CustomH20ScopeOption[] All = {
            new CustomH20ScopeOption("Both",CustomH20LengthScope.Both),
            new CustomH20ScopeOption("Beam supports only",CustomH20LengthScope.BeamSupports),
            new CustomH20ScopeOption("Slab supports only",CustomH20LengthScope.SlabSupports)
        };
    }
    public sealed class TowerInput
    {
        internal TowerSearchDiagnostics Diagnostics;
        internal Dictionary<Tuple<int,double,double,double,double,double,double>,DedicatedCrossSearch> DedicatedCrossCache;
        internal BeamSearchTrace Trace;
        // Tests can replay the original search without reuse; production searches reuse work.
        internal bool ReuseSearchWork = true;
        // Regression tests can replay the former common-primary-coverage restriction.
        internal bool IndependentNetworkPrimaryCoverage = true;
        internal bool CoordinateCrossingLayouts;
        internal int NetworkSearchWorkers = Math.Min(4,Environment.ProcessorCount);
        internal bool PruneUneconomicCandidates = true;
        internal bool StagePrimaryChecks = true;
        internal bool ReuseNetworkNumerics = true;
        internal bool DeferTransferLayout = true;
        internal bool SearchByLegTier = true;
        internal bool AllowSeparateSlabFallback = true;
        internal bool RespectNetworkCatalogueLengths = true;
        internal bool PruneNetworkBounds = true;
        internal bool ReuseTransverseNumerics = true;
        internal bool LazySearchDiagnostics = true;
        // A caller may restrict a solve only when larger leg counts cannot win.
        internal int NetworkLegCeiling = int.MaxValue;
        internal TransferredSlabOptimizer.SearchWorkCache NetworkNumerics;
        internal TowerInput CopyForSearch() => (TowerInput)MemberwiseClone();
        internal string DiagnosticScope="";
        internal double DiagnosticOffsetMm;
        public TransferredSlabInput TransferredSlab { get; set; }
        public TransferNetworkInput Network {get;set;}
        public double LengthMm { get; set; }
        public double LegCapacityKn { get; set; }
        public double TotalLineLoad { get; set; }
        // Moment of transverse line loads about the concrete beam centre, kN (kN/m * m).
        public double TransverseLoadMoment { get; set; }
        public double DeflectionLimitMm { get; set; }
        public int DeflectionDenominator { get; set; }
        public double[] StockLengthsMm { get; set; } = TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths);
        public bool AllowCustomH20Lengths { get; set; }
        private CustomH20LengthScope customH20Scope;
        public CustomH20LengthScope CustomH20Scope
        {
            get => customH20Scope;
            set { if(!Enum.IsDefined(typeof(CustomH20LengthScope),value))throw new ArgumentException("Select a custom H20 length scope."); customH20Scope=value; }
        }
        public bool AllowCustomBeamLengths => AllowCustomH20Lengths && CustomH20Scope!=CustomH20LengthScope.SlabSupports;
        public bool AllowCustomSlabLengths => AllowCustomH20Lengths && CustomH20Scope!=CustomH20LengthScope.BeamSupports;
        // Preserve the historical standalone slab fallback only for the default scope.
        internal bool PermitsSlabLength(double length) => AllowCustomSlabLengths || StockLengthsMm.Contains(length)
            || CustomH20Scope==CustomH20LengthScope.Both && TransferredSlab!=null && !TransferredSlab.IsNetwork;

        // Explicit project assumption; no stabilizing deadweight or connection model is added.
        public bool IgnoreUplift { get; set; }
        public double StockEndSetbackMm { get; set; } = 150;
        public double MaximumConcreteEndOverhangMm { get; set; }
        public bool Exclude600Standalone { get; set; }
    }
    public sealed class TowerLayout
    {
        public TransferredSlabResult Transfer { get; internal set; }
        public bool IgnoreUplift { get; internal set; }
        public double MinimumReactionKn => Transfer == null
            ? LeftReactions.Concat(RightReactions).Min()
            : Transfer.PrimaryCases.Concat(Transfer.LowerCases).SelectMany(c=>c).Concat(Transfer.UpperMembers).Select(m=>m.MinimumReactionKn).Concat(MinimumLeftReactions??LeftReactions).Concat(MinimumRightReactions??RightReactions).Min();
        public const string ConnectedFormworkAssumption = "Uplift excluded from acceptance: connected plywood and stabilizing deadweight are assumed by the user. Signed reactions are retained. Deadweight adequacy and connections are not calculated; negative reactions indicate required restraint. Strength, deflection and maximum individual leg loads remain checked.";
        public string UpliftSummary => (IgnoreUplift ? ConnectedFormworkAssumption : "Bearing-only supports: uplift is rejected.") + $" Minimum signed reaction {MinimumReactionKn:0.######} kN.";
        public double[] PositionsMm { get; internal set; }
        public bool[] Joined { get; internal set; }
        public double WidthMm { get; internal set; }
        public bool Rotated { get; internal set; }
        public double LengthMm { get; internal set; }
        public double ConcreteEndOverhangMm { get; internal set; }
        public double LeftLineLoad { get; internal set; }
        public double RightLineLoad { get; internal set; }
        public double[] LeftReactions { get; internal set; }
        public double[] RightReactions { get; internal set; }
        public double[] MinimumLeftReactions { get; internal set; }
        public double[] MinimumRightReactions { get; internal set; }
        public double MinimumLegReaction(bool left,int index) => (left ? MinimumLeftReactions : MinimumRightReactions)?[index] ?? (left ? LeftReactions : RightReactions)[index];
        public TowerStockPlan StockPlan { get; internal set; }
        public double LegUtilizationPercent => MaxLegKn / LegCapacityKn * 100;
        public string CompactSummary => $"{LegCount} legs · {GroupCount} tower groups · {StockPlan.TotalH20Count + (Transfer?.AdditionalH20Count ?? 0)} H20s";
        public string LegLoadSummary => $"Peak leg: {MaxLegKn:0.00} / permissible {LegCapacityKn:0.00} kN ({LegUtilizationPercent:0.0}%)";
        public string CapacityBasis => Transfer != null ? "Every leg is checked for beam, one-sided and combined slab loading. Search minimizes receiving-beam legs, then total H20 length. Leg values are load-case envelopes." : $"Every leg is checked against {LegCapacityKn:0.00} kN. Search minimizes legs, then braced bays, H20 quantity and governing utilization. Change the permissible load and optimize again to recalculate.";
        public string StockSchedule => StockPlan.Schedule + (Transfer == null ? "" : "\n" + Transfer.Schedule);
        public int LegCount => PositionsMm.Length * 2;
        public int GroupCount => 1 + Joined.Count(x => !x);
        public int BayCount => Joined.Count(x => x);
        public double MaxLegKn { get; internal set; }
        public double LegCapacityKn { get; internal set; }
        public double DeflectionLimitMm { get; internal set; }
        public int DeflectionDenominator { get; internal set; }
        public double MomentKnM { get; internal set; }
        public double ShearKn { get; internal set; }
        public double DeflectionMm { get; internal set; }
        public double Utilization { get; internal set; }
        public string Governing { get; internal set; }
        public int Tested { get; internal set; }
        public bool MinimumLegCountProven { get; internal set; }
        public string SearchNote => MinimumLegCountProven
            ? "Minimum leg count meets the load/stock lower bound. Group count/layout are best found in a bounded search."
            : "Best valid layout found in a bounded search; global minimum leg/group count is not proven.";
        private string MemberDemandSummary
        {
            get
            {
                if(Transfer==null)return $"Moment {MomentKnM:0.00}/10 kNm · shear {ShearKn:0.00}/22 kN";
                var members=Transfer.PrimaryCases.SelectMany(c=>c).Concat(Transfer.LowerCases.SelectMany(c=>c)).Concat(Transfer.UpperMembers).ToArray();
                return $"Moment utilization {members.Max(m=>m.Analysis.MomentKnM/m.MomentLimitKnM)*100:0.0}% · shear utilization {members.Max(m=>m.Analysis.ShearKn/m.ShearLimitKn)*100:0.0}%";
            }
        }
        public string Summary => string.Format(CultureInfo.CurrentCulture, "Legs: {0} · tower groups: {1} · bays: {2}\n{3}; primary lines {4:0} mm apart\nMaximum leg {5:0.00}/{10:0.00} kN · governing utilization {6:0.0}%\n{12}\nPeak deflection {9:0.00} mm · criterion {11} (each span/overhang)",
            LegCount, GroupCount, BayCount, Rotated ? "Rotated isolated towers" : "1524 mm frames across beam", WidthMm, MaxLegKn, Utilization * 100, MomentKnM, ShearKn, DeflectionMm, LegCapacityKn, DeflectionDenominator == 0 ? $"{DeflectionLimitMm:0.00} mm" : $"L/{DeflectionDenominator}", MemberDemandSummary)
            + string.Format(CultureInfo.CurrentCulture, "\nPrimary H20s: {0} pieces · no overlaps\nConcrete beyond H20: {1:0.###} mm per end", StockPlan.TotalH20Count, ConcreteEndOverhangMm) + (IgnoreUplift ? "\n"+UpliftSummary : "");
        public string Schedule
        {
            get
            {
                var lines = new List<string>(); int group = 1;
                for (int i = 0; i < PositionsMm.Length; i++)
                {
                    if (i > 0 && !Joined[i - 1]) group++;
                    lines.Add(string.Format(CultureInfo.CurrentCulture, "G{0} · x={1:0.###} mm · legs L/R={2:0.00}/{3:0.00} kN · utilization L/R={4:0.0}/{5:0.0}%", group, PositionsMm[i], LeftReactions[i], RightReactions[i], LeftReactions[i] / LegCapacityKn * 100, RightReactions[i] / LegCapacityKn * 100));
                    if (IgnoreUplift) lines.Add($"  Minimum signed leg reactions L/R={MinimumLegReaction(true,i):0.######}/{MinimumLegReaction(false,i):0.######} kN across stages");
                    if (i < Joined.Length) lines.Add(string.Format(CultureInfo.CurrentCulture, "  {0}: {1:0.###} mm", Joined[i] ? (Rotated ? "Frame span" : "Cross bay") : "Clear gap between leg planes", PositionsMm[i + 1] - PositionsMm[i]));
                }
                lines.Add(string.Format(CultureInfo.CurrentCulture, "End overhangs: {0:0.###} / {1:0.###} mm", PositionsMm[0], LengthMm - PositionsMm.Last()));
                return string.Join("\n", lines);
            }
        }
    }
    /// <summary>Deterministic bounded layout search, ranked legs, braced bays, then governing utilization.
    /// Two double-H20 lines with independently supported stock members; no secondary, bearing or stability design.</summary>
    public static class TowerOptimizer
    {
        public static readonly double[] BaysMm = { 600, 1000, 1500, 2000 };
        public const double PairEI = 2 * 449.8756, PairMoment = 10, PairShear = 22;
        public static TowerLayout Optimize(TowerInput input)
        {
            if(input?.Network!=null)return TransferNetworkOptimizer.Optimize(input,input.Network).Beams[0];
            if (input?.TransferredSlab != null) return TransferredSlabOptimizer.Optimize(input);
            Validate(input);
            return TowerStockPlanner.SearchWithCustom(input,()=>OptimizeCore(input),(a,b)=>a.LegCount<b.LegCount || a.LegCount==b.LegCount && (a.BayCount<b.BayCount || a.BayCount==b.BayCount && (a.StockPlan.TotalH20Count<b.StockPlan.TotalH20Count || a.StockPlan.TotalH20Count==b.StockPlan.TotalH20Count && a.Utilization<b.Utilization)));
        }
        private static TowerLayout OptimizeCore(TowerInput input)
        {
            Validate(input);
            input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator,input.DeflectionLimitMm,input.Trace,input.IgnoreUplift);
            if (input.MaximumConcreteEndOverhangMm == 0) return OptimizeCovered(input);
            input.Trace?.Work("Beam-only primary coverage DP");
            // Reachable stock totals; allow only symmetric concrete end overhang, never internal stock gaps.
            int total = (int)Math.Floor(input.LengthMm);
            var counts = TowerStockPlanner.CoverageCounts(total,TowerStockPlanner.CandidateLengths(input,25,total),input.Trace?.CancellationToken??default);
            var covers = Enumerable.Range(1, total).Where(x => x >= input.LengthMm - 2 * input.MaximumConcreteEndOverhangMm && counts[x] != int.MaxValue)
                .OrderBy(x => counts[x]).ThenByDescending(x => x).ToArray();
            input.Trace?.Increment(BeamSearchCounter.CoveragePlans,covers.Length);
            TowerLayout best = null; int tested = 0;
            foreach (int covered in covers)
            {
                input.Trace?.Increment(BeamSearchCounter.CoverageVariants);input.Trace?.Context("Covered length "+covered+" mm");
                if (best != null && counts[covered] * 4 > best.LegCount) continue;
                double overhang = (input.LengthMm - covered) / 2;
                var candidateInput = new TowerInput { LengthMm = covered, LegCapacityKn = input.LegCapacityKn, TotalLineLoad = input.TotalLineLoad,
                    TransverseLoadMoment = input.TransverseLoadMoment, DeflectionLimitMm = input.DeflectionLimitMm, DeflectionDenominator = input.DeflectionDenominator,
                    Trace = input.Trace, IgnoreUplift = input.IgnoreUplift, AllowCustomH20Lengths = input.AllowCustomH20Lengths, CustomH20Scope = input.CustomH20Scope, StockLengthsMm = input.StockLengthsMm, StockEndSetbackMm = input.StockEndSetbackMm, Exclude600Standalone = input.Exclude600Standalone,Diagnostics=input.Diagnostics,DiagnosticOffsetMm=overhang };
                TowerLayout candidate;
                try { candidate = OptimizeCovered(candidateInput); }
                catch (TowerOptimizationException) {continue;}
                catch (ArgumentException ex) {input.Diagnostics.Geometry("search limits",$"Coverage {covered} mm: {ex.Message}","Review the reported stock/load bounds; changing leg capacity requires a configuration-specific permissible value.");continue;}
                tested += candidate.Tested;
                candidate.PositionsMm = candidate.PositionsMm.Select(x => x + overhang).ToArray();
                foreach (var piece in candidate.StockPlan.Pieces) piece.StartMm += overhang;
                candidate.LengthMm = input.LengthMm; candidate.ConcreteEndOverhangMm = overhang;
                if (best == null || candidate.LegCount < best.LegCount || candidate.LegCount == best.LegCount && (candidate.BayCount < best.BayCount
                    || candidate.BayCount == best.BayCount && (candidate.StockPlan.TotalH20Count < best.StockPlan.TotalH20Count
                    || candidate.StockPlan.TotalH20Count == best.StockPlan.TotalH20Count && candidate.Utilization < best.Utilization))) best = candidate;
            }
            if(covers.Length==0)input.Diagnostics.Geometry("primary coverage",$"Primary stock coverage: no stock combination totals {input.LengthMm-2*input.MaximumConcreteEndOverhangMm:0.###}–{input.LengthMm:0.###} mm.","Review available stock lengths and the maximum concrete end allowance.");
            if (best == null) throw input.Diagnostics.Exception("No valid layout found within the end-overhang allowance and bounded search.");
            int capacityBound = 2 * Math.Max(2, (int)Math.Ceiling((input.TotalLineLoad / 2 + Math.Abs(input.TransverseLoadMoment) / 2) * covers.Min() / 1000 / input.LegCapacityKn - 1e-10));
            best.MinimumLegCountProven = best.LegCount == Math.Max(capacityBound, covers.Min(x => counts[x]) * 4);
            best.Tested = tested; return best;
        }
        private static TowerLayout OptimizeCovered(TowerInput input)
        {
            Validate(input); var random = new Random(1524); int tested = 0, attempts = 0;
            input.Trace?.Work("Beam-only coverage/member candidates");
            var lengths = TowerStockPlanner.CandidateLengths(input,25,input.LengthMm);
            int minimumPieces = TowerStockPlanner.MinimumPieces(input.LengthMm, lengths,input.Trace?.CancellationToken??default);
            if (minimumPieces == int.MaxValue) throw new ArgumentException("The available uncut H20 stock lengths cannot cover this beam length exactly without overlaps or projection beyond its ends. Change the length or available stock; no partial coverage is accepted.");
            if (minimumPieces > 20) throw new ArgumentException("The stock cover requires more than the search's 80-leg limit (at least four legs per independent stock run).");
            double length = input.LengthMm, w = input.TotalLineLoad / 2 + Math.Abs(input.TransverseLoadMoment) / 2.0;
            if (!TowerBeamAnalysis.Finite(w * length / 1000) || w * length / 1000 / input.LegCapacityKn > 40)
                throw new ArgumentException("The load-capacity lower bound exceeds this search's 80-leg limit.");
            int lower = Math.Max(2, (int)Math.Ceiling(w * length / 1000 / input.LegCapacityKn - 1e-10));
            lower = Math.Max(lower, minimumPieces * 2);
            // A per-span moment range of 2*M and shear range of 2*V gives necessary span bounds.
            double longest = 1000 * Math.Min(Math.Sqrt(16 * PairMoment / w), 2 * PairShear / w);
            double tail = 1000 * Math.Min(Math.Sqrt(2 * PairMoment / w), PairShear / w);
            lower = Math.Max(lower, 1 + (int)Math.Ceiling(Math.Max(0, length - 2 * tail) / longest - 1e-10));
            input.Trace?.Work("Beam-only tower layout enumeration");
            TowerLayout best = null;
            // Seed isolated towers directly from stock runs. Random gap placement can miss simple two-tower solutions.
            if (minimumPieces <= 2)
            {
                foreach (double first in lengths)
                {
                    input.Trace?.CheckCancellation();
                    double second = length - first;
                    if (second < 0 || second > 0 && !lengths.Contains(second)) continue;
                    var runs = second == 0 ? new[] { first } : new[] { first, second };
                    foreach (double bay in BaysMm) foreach (double bay2 in BaysMm)
                    {
                        double start = 0; var positions = new List<double>();
                        for (int i = 0; i < runs.Length; i++) { double span = i == 0 ? bay : bay2; positions.Add(start + (runs[i] - span) / 2); positions.Add(start + (runs[i] + span) / 2); start += runs[i]; }
                        if (positions.Any(x => x < 0 || x > length) || positions.Skip(1).Where((x,i) => x <= positions[i]).Any()) continue;
                        var candidate = Evaluate(input, positions.ToArray(), runs.Length == 1 ? new[] { true } : new[] { true, false, true }, 1524, false); tested++;
                        if (candidate != null && (best == null || candidate.LegCount < best.LegCount || candidate.LegCount == best.LegCount && candidate.Utilization < best.Utilization)) best = candidate;
                    }
                }
            }
            for (int count = lower; count <= 40 && attempts < 200000; count++)
            {
                if (best != null && count > best.PositionsMm.Length) break;
                // Rotated groups are isolated: joining across the beam would require more than two primary lines.
                for (int rotation = 0; rotation < 2; rotation++)
                {
                    if (rotation == 1 && count % 2 != 0) continue;
                    int minGroups = rotation == 1 ? count / 2 : 1;
                    for (int groups = minGroups; groups <= count / 2; groups++)
                    {
                        int joins = count - groups;
                        if ((rotation == 1 ? joins * 1524 : joins * 600) > length) continue;
                        // Small joined runs enumerate every bay sequence; larger runs use reproducible mixed samples.
                        int sequences = rotation == 1 ? 4 : (int)Math.Min(1024, Math.Pow(4, joins));
                        int trials = Math.Min(4096, Math.Max(256, sequences * 4));
                        for (int trial = 0; trial < trials && attempts < 200000; trial++)
                        {
                            input.Trace?.CheckCancellation();
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
                            tested++;
                            var candidate = Evaluate(input, positions, joined, width, rotation == 1);
                            if (candidate != null && (best == null || candidate.LegCount < best.LegCount || candidate.LegCount == best.LegCount && (candidate.BayCount < best.BayCount || candidate.BayCount == best.BayCount
                                && (candidate.StockPlan.TotalH20Count < best.StockPlan.TotalH20Count || candidate.StockPlan.TotalH20Count == best.StockPlan.TotalH20Count && candidate.Utilization < best.Utilization)))) best = candidate;
                        }
                    }
                }
                if (best != null) break;
            }
            if (best == null) throw (input.Diagnostics??new TowerSearchDiagnostics(input.DeflectionDenominator,input.DeflectionLimitMm,input.Trace,input.IgnoreUplift)).Exception("No valid tower layout found within the bounded search (up to 80 legs).");
            best.Tested = tested;
            int capacityBound = Math.Max(2, (int)Math.Ceiling((input.TotalLineLoad / 2 + Math.Abs(input.TransverseLoadMoment) / 2) * length / 1000 / input.LegCapacityKn - 1e-10));
            best.MinimumLegCountProven = best.PositionsMm.Length == Math.Max(capacityBound, minimumPieces * 2); return best;
        }
        public static TowerLayout Evaluate(TowerInput input, double[] positions, bool[] joined, double width, bool rotated)
        {
            Validate(input);
            input.Trace?.Increment(BeamSearchCounter.TowerCandidates);
            if (positions == null || joined == null || positions.Length < 2 || joined.Length != positions.Length - 1 || !TowerBeamAnalysis.Finite(width) || width <= 0) throw new ArgumentException("Invalid tower layout.");
            if (positions.Any(x => !TowerBeamAnalysis.Finite(x) || x < 0 || x > input.LengthMm) || positions.Skip(1).Where((x, i) => x <= positions[i]).Any()) throw new ArgumentException("Support positions must increase within the beam length.");
            if (rotated ? !BaysMm.Contains(width) : width != 1524) return null;
            int run = 1;
            for (int i = 0; i < joined.Length; i++)
            {
                double distance = positions[i + 1] - positions[i];
                if (joined[i]) { if (rotated ? Math.Abs(distance - 1524) > 1e-6 : !BaysMm.Any(x => Math.Abs(x - distance) < 1e-6)) return null; run++; }
                else { if (run < 2 || rotated && run != 2) return null; run = 1; }
            }
            if (run < 2 || rotated && run != 2) return null;
            var finalJoined = RemoveRedundantJoins(joined, positions, input.Exclude600Standalone);
            if (finalJoined == null || input.Exclude600Standalone && rotated && width == 600) return null;
            double left = input.TotalLineLoad / 2 - input.TransverseLoadMoment / (width / 1000), right = input.TotalLineLoad - left;
            if (Math.Min(left, right) <= 0) return null;
            var response = TowerStockPlanner.Find(input, positions, Math.Max(left, right));
            if (response == null) return null;
            double maxLeg = response.Reactions.Max();
            if (!input.IgnoreUplift && response.Reactions.Min() < -1e-9 * maxLeg) return null;
            var utilization = new[] { maxLeg / input.LegCapacityKn, response.MomentKnM / PairMoment, response.ShearKn / PairShear, response.DeflectionUtilization };
            if (utilization.Any(x => !TowerBeamAnalysis.Finite(x)) || utilization.Max() > 1 + 1e-10) return null;
            return new TowerLayout { IgnoreUplift = input.IgnoreUplift, PositionsMm = (double[])positions.Clone(), Joined = finalJoined, WidthMm = width, Rotated = rotated, LengthMm = input.LengthMm, StockPlan = response,
                LeftLineLoad = left, RightLineLoad = right, LeftReactions = response.Reactions.Select(x => x * left / Math.Max(left, right)).ToArray(), RightReactions = response.Reactions.Select(x => x * right / Math.Max(left, right)).ToArray(),
                MaxLegKn = maxLeg, LegCapacityKn = input.LegCapacityKn, DeflectionLimitMm = input.DeflectionLimitMm, DeflectionDenominator = input.DeflectionDenominator, MomentKnM = response.MomentKnM, ShearKn = response.ShearKn, DeflectionMm = response.DeflectionMm,
                Utilization = utilization.Max(), Governing = new[] { "Leg capacity", "Primary moment", "Primary shear", "Primary deflection" }[Array.IndexOf(utilization, utilization.Max())] };
        }
        // Maximize independent groups while retaining at least two frames per group.
        // When excluded, a 600 mm pair must remain joined to more frames or be rejected.
        internal static bool[] RemoveRedundantJoins(bool[] joined, double[] positions, bool exclude600)
        {
            var result = (bool[])joined.Clone(); int first = 0;
            while (first < positions.Length)
            {
                int last = first;
                while (last < joined.Length && joined[last]) last++;
                int n = last - first + 1;
                var groups = Enumerable.Repeat(-1, n + 1).ToArray(); var sizes = new int[n + 1]; groups[n] = 0;
                for (int i = n - 2; i >= 0; i--)
                    for (int size = 2; i + size <= n; size++)
                    {
                        if (groups[i + size] < 0 || exclude600 && size == 2 && Math.Abs(positions[first+i+1] - positions[first+i] - 600) < 1e-6) continue;
                        if (groups[i + size] + 1 > groups[i]) { groups[i] = groups[i + size] + 1; sizes[i] = size; }
                    }
                if (groups[0] < 0) return null;
                for (int i = 0; i + sizes[i] < n; i += sizes[i]) result[first + i + sizes[i] - 1] = false;
                first = last + 1;
            }
            return result;
        }
        private static void Validate(TowerInput input)
        {
            if (input == null || new[] { input.LengthMm, input.LegCapacityKn, input.TotalLineLoad }.Any(x => !TowerBeamAnalysis.Finite(x) || x <= 0)
                || !TowerBeamAnalysis.Finite(input.TransverseLoadMoment)) throw new ArgumentException("Tower length, leg capacity, load and deflection limit must be finite and positive.");
            if (input.DeflectionDenominator == 0 ? !TowerBeamAnalysis.Finite(input.DeflectionLimitMm) || input.DeflectionLimitMm <= 0
                : !new[] { 500, 350, 270 }.Contains(input.DeflectionDenominator)) throw new ArgumentException("Select L/500, L/350, L/270 or a positive legacy fixed limit.");
            if (!TowerBeamAnalysis.Finite(input.MaximumConcreteEndOverhangMm) || input.MaximumConcreteEndOverhangMm < 0 || input.MaximumConcreteEndOverhangMm > 500) throw new ArgumentException("Concrete end allowance must be between 0 and 500 mm per end.");
            if (input.LengthMm > 50000) throw new ArgumentException("Tower optimization supports beam lengths up to 50,000 mm.");
            if (input.StockLengthsMm == null || input.StockLengthsMm.Length == 0 || input.StockLengthsMm.Length > 30
                || input.StockLengthsMm.Any(x => !TowerBeamAnalysis.Finite(x) || x <= 0 || x > 50000 || x != Math.Round(x)))
                throw new ArgumentException("Provide 1–30 positive whole-millimetre H20 stock lengths up to 50,000 mm.");
            if (!TowerBeamAnalysis.Finite(input.StockEndSetbackMm) || input.StockEndSetbackMm <= 0)
                throw new ArgumentException("H20 support-centre setback from each member end must be finite and greater than zero.");
        }
    }
}
