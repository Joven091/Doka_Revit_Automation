using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class TransferredSlabInput
    {
        public double BeamWidthMm, BeamLineLoadKnM, UpperSpacingMm;
        public double LeftBayMm, RightBayMm, LeftAreaLoadKnM2, RightAreaLoadKnM2;
        public UpperTransferPlan LeftPlan, RightPlan;
        internal double[] LeftEffectiveWidthsMm, RightEffectiveWidthsMm;
        // The network solver fixes the loaded longitudinal coverage and row phase across all beams.
        public double? FixedCoveredLengthMm, FixedRowPhaseMm;
        internal double[][] StockCovers;
        public bool IsNetwork;
        public const double FootClearanceMm = 250;
        public void Validate(TowerInput input)
        {
            if (new[] { BeamWidthMm, BeamLineLoadKnM, UpperSpacingMm, input.LengthMm, input.LegCapacityKn, input.StockEndSetbackMm }.Any(x => !TowerBeamAnalysis.Finite(x) || x <= 0)
                || input.LengthMm > 30000 || UpperSpacingMm < 100 || UpperSpacingMm > 5000 || BeamWidthMm > 10000)
                throw new ArgumentException("Transfer mode: positive geometry and loads are required; beam length ≤30,000 mm and upper spacing 100–5,000 mm.");
            if (!new[] { 500, 350, 270 }.Contains(input.DeflectionDenominator)) throw new ArgumentException("Transfer mode requires L/500, L/350 or L/270.");
            if (!TowerBeamAnalysis.Finite(input.MaximumConcreteEndOverhangMm) || input.MaximumConcreteEndOverhangMm < 0 || input.MaximumConcreteEndOverhangMm > 500)
                throw new ArgumentException("Concrete end allowance must be between 0 and 500 mm.");
            if (new[] { LeftBayMm, RightBayMm, LeftAreaLoadKnM2, RightAreaLoadKnM2 }.Any(x => !TowerBeamAnalysis.Finite(x) || x < 0)
                || LeftBayMm > 0 && LeftAreaLoadKnM2 <= 0 || RightBayMm > 0 && RightAreaLoadKnM2 <= 0
                || LeftBayMm > 30000 || RightBayMm > 30000 || LeftBayMm == 0 && LeftAreaLoadKnM2 != 0 || RightBayMm == 0 && RightAreaLoadKnM2 != 0 || LeftBayMm + RightBayMm == 0)
                throw new ArgumentException("Select at least one slab bay, with positive clear width and slab load.");
            if (input.StockLengthsMm == null || input.StockLengthsMm.Length == 0 || input.StockLengthsMm.Length > 30
                || input.StockLengthsMm.Any(x => !TowerBeamAnalysis.Finite(x) || x < 300 || x > 50000 || x != Math.Round(x))) throw new ArgumentException("Invalid available H20 lengths.");
        }
    }
    public sealed class TransferMember
    {
        public string Name, LoadCase;
        public double StartMm, LengthMm;
        public double[] SupportsMm;
        public BeamPointLoad[] Points;
        public BeamDistributedLoad[] Uniform;
        public LoadedBeamAnalysis Analysis;
        public int H20Count = 2;
        public double MomentLimitKnM => TowerOptimizer.PairMoment * H20Count / 2;
        public double ShearLimitKn => TowerOptimizer.PairShear * H20Count / 2;
        public double Utilization => Math.Max(Analysis.DeflectionUtilization, Math.Max(Analysis.MomentKnM / MomentLimitKnM, Analysis.ShearKn / ShearLimitKn));
    }
    public sealed class TransferredSlabResult
    {
        public double[] RowsMm, TributaryWidthsMm;
        public double[] LeftUpperScales, RightUpperScales;
        public double UpperScale(bool left,int row)=>(left ? LeftUpperScales : RightUpperScales)?[row]??TributaryWidthsMm[row]/1000;
        public double EnteredUpperSpacingMm;
        public double LowerStockStartMm, LowerStockLengthMm, LeftFootMm, RightFootMm;
        public double LeftUpperStockMm, RightUpperStockMm, LeftUpperEdgeMm, RightUpperEdgeMm;
        public bool LeftUpperCustom, RightUpperCustom;
        public bool LowerCustom;
        public double LeftFootReactionKn, RightFootReactionKn, SlabMovementMm, CoveredSlabAreaM2, SlabLoadKn, ShutterLoadKn;
        public TransferMember[][] PrimaryCases;
        public TransferMember[][] LowerCases;
        public TransferMember[] UpperMembers;
        public string[] CaseNames;
        public int UpperFootCount => RowsMm.Length * UpperMembers.Length * 2;
        public int AdditionalH20Count => RowsMm.Length * (1 + UpperMembers.Length);
        public double AdditionalTimberMm => RowsMm.Length * (LowerStockLengthMm + LeftUpperStockMm + RightUpperStockMm);
        public string Summary => $"Upper spacing {EnteredUpperSpacingMm:0} mm · {RowsMm.Length} bearer rows\nPeak upper foot L/R {LeftFootReactionKn:0.00}/{RightFootReactionKn:0.00} kN\nEstimated slab soffit movement {SlabMovementMm:0.00} mm";
        public string Schedule => string.Join("\n", RowsMm.Select((x,i) => $"T{i+1}: x={x:0.###} mm · tributary {TributaryWidthsMm[i]:0.###} mm · 1 × {LowerStockLengthMm:0} mm"))
            + $"\nLower transverse stock{(LowerCustom ? " (custom, 25 mm increments)" : "")}: {LowerStockStartMm:0.###} to {LowerStockStartMm+LowerStockLengthMm:0.###} mm from beam centre"
            + (LeftUpperStockMm > 0 ? $"\nLeft slab H20: 1 × {LeftUpperStockMm:0} mm{(LeftUpperCustom ? " custom" : "")} per row · edge {LeftUpperEdgeMm:0} mm · foot clearance {LeftFootMm:0} mm" : "")
            + (RightUpperStockMm > 0 ? $"\nRight slab H20: 1 × {RightUpperStockMm:0} mm{(RightUpperCustom ? " custom" : "")} per row · edge {RightUpperEdgeMm:0} mm · foot clearance {RightFootMm:0} mm" : "")
            + $"\nSlab H20 covered area: {CoveredSlabAreaM2:0.00} m² · receiving beam slab load: {SlabLoadKn:0.00} kN (including shutter {ShutterLoadKn:0.00} kN at 100 mm outward from each beam face)\nLoad cases: {string.Join("; ", CaseNames)}\n"
            + string.Join("\n", PrimaryCases.SelectMany(x => x).Concat(LowerCases.SelectMany(x => x)).Concat(UpperMembers).Select(m => $"{m.Name} · {m.LoadCase}: M {m.Analysis.MomentKnM:0.00}/{m.MomentLimitKnM:0.##} kNm · V {m.Analysis.ShearKn:0.00}/{m.ShearLimitKn:0.##} kN · d {m.Analysis.DeflectionMm:0.00} mm · utilization {m.Utilization*100:0.0}%"));
        public const string Assumptions = "Slab load transfers through single slab H20s and vertical upper feet, single lower secondary H20s and paired longitudinal primaries to tower legs. The upper assembly has no D3 geometry. Beam concrete is present before slab loading; beam-only, each one-sided slab stage and combined stages are checked. The slab area over upper H20 stock loads that stock; each 200–350 mm slab-edge strip loads its own side shutter, acting 100 mm outward from the adjacent beam face. These edge loads are additional to the upper-foot loads. Slab above the concrete beam width is not counted again. The longitudinal primary end allowance still excludes concrete beyond the primary ends. Each slab H20 is one length with no upper joints. If no catalogue length fits 200–350 mm edge strips, a custom length is rounded down to a multiple of 25 mm, keeping each edge closest to 200 mm. Primary and lower secondary lengths use catalogue stock unless custom H20 lengths are enabled; additional lengths are multiples of 25 mm. The custom primary cover search adds bounded balanced runs and small joint shifts. The original stock-only solution is retained when it ranks better. Feet have at least 250 mm face-to-centre clearance and 150 mm inset from the upper stock end. The network uses 2–8 actual parallel beams, each with its own width/depth, and one slab bay between neighbours. Upper stock and two foot positions are shared by those neighbours; the two foot reactions need not be equal. Each lower beam is independently checked against the entered permissible leg load, and negative bearing reactions invalidate a candidate. Shared longitudinal coverage and support rows align upper supports across all beams. Optional perpendicular beams span one clear slab bay and transfer through the existing slab H20s and shutter-edge strips during that bay loading stage. The existing slab row load is retained once; the additional crossing-beam concrete and surcharge are assigned only to H20 rows physically beneath that footprint, using midpoint tributaries between those rows. The added area load is the crossing beam total-depth load minus the existing slab-thickness load; concrete, formwork and working allowances are not counted twice. Crossing beam footprints must lie within longitudinal H20 coverage and cannot overlap within a bay. Formwork bearing and level-change details are outside this load-transfer model. Two-support upper/lower bearers are statically determinate; different lower-foot movements add an affine displacement to the upper beam. Single-beam mode retains the mirrored opposite-beam assumption; network mode uses both actual beam responses. Slab and lower secondary H20 limits are M=5 kNm, V=11 kN and EI=449.8756 kNm². Longitudinal primary pair limits are M=10 kNm, V=22 kN and EI=899.7512 kNm², assuming equal load sharing. Relative H20 deflection uses each support span/overhang; total slab movement is reported without an assumed project tolerance. Network search ranks all tower legs plus upper feet, then total H20 length, counting shared upper timber once. Search candidates are bounded; a global optimum is not proven. Plans show towers and primary stock lengths; single secondaries are shown in the section with stock length and spacing. Sheathing, shutter/connection strength, bearing, upper-support capacity/stability, lower tower stability and foundations require separate design. Doka H20 instructions restrict heavy point loads with wide spans; numeric member checks alone do not approve foot or shutter bearing details.";
    }
    /// <summary>Explicit downward force transfer; no equivalent UDL is used to check the primaries.</summary>
    public static class TransferredSlabOptimizer
    {
        private sealed class Geometry { public TowerStockPiece[] Stock; public double[] Positions; public bool[] Joined; }
        private sealed class Upper { public double Stock, Edge, Foot, OtherFoot, Bay, Load; public bool Custom; public LoadedBeamAnalysis Analysis; }
        private sealed class CrossOption { public double Start, Stock; public Upper Left, Right; public LoadedBeamAnalysis[] Cases; public string[] Names; public readonly Dictionary<int,LoadedBeamAnalysis> RowAnalyses=new Dictionary<int,LoadedBeamAnalysis>(); }
        public static TowerLayout Optimize(TowerInput input,bool shareDiagnostics=false)
        {
            input.TransferredSlab.Validate(input);
            if(shareDiagnostics)return OptimizeCore(input,true);
            return TowerStockPlanner.SearchWithCustom(input,()=>OptimizeCore(input,false),(a,b)=>a.LegCount<b.LegCount || a.LegCount==b.LegCount && (Timber(a)<Timber(b)-.01 || Math.Abs(Timber(a)-Timber(b))<.01 && a.Utilization<b.Utilization));
        }
        private static TowerLayout OptimizeCore(TowerInput input,bool shareDiagnostics)
        {
            var t = input.TransferredSlab; t.Validate(input); TowerLayout best = null; int tested = 0;
            if(!shareDiagnostics || input.Diagnostics==null)input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator);var diag=input.Diagnostics;
            var covers=Covers(input).ToArray();
            if(covers.Length==0)diag.Geometry(input.DiagnosticScope+"coverage",$"{input.DiagnosticScope}primary coverage: no available stock combination totals {input.LengthMm-2*input.MaximumConcreteEndOverhangMm:0.###}–{input.LengthMm:0.###} mm.","Review stock lengths and the concrete end allowance.");
            foreach (var cover in covers)
            {
                if(best!=null && cover.Length*4>best.LegCount)continue;
                double covered = cover.Sum(), edge = (input.LengthMm-covered)/2;
                if(t.FixedCoveredLengthMm.HasValue && Math.Abs(covered-t.FixedCoveredLengthMm.Value)>1e-7) continue;
                int count = (int)Math.Ceiling(covered/t.UpperSpacingMm);
                double first = (covered-(count-1)*t.UpperSpacingMm)/2;
                foreach (bool rotated in new[] { false, true }) foreach (double width in rotated ? TowerOptimizer.BaysMm : new[] { 1524.0 })
                {
                    if (rotated && input.Exclude600Standalone && width == 600) continue;
                    var crosses = CrossOptions(input,width,count>2 ? t.UpperSpacingMm : covered/count).ToArray(); if (crosses.Length == 0) continue;
                    var geometries=Geometries(cover,rotated,input).ToArray();
                    if(geometries.Length==0)diag.Geometry(input.DiagnosticScope+"tower geometry",$"{input.DiagnosticScope}tower geometry: no searched D3 support pattern fits stocks {string.Join(", ",cover)} mm with {input.StockEndSetbackMm:0.###} mm end setbacks; exclude standalone 600 = {input.Exclude600Standalone}.","Review stock lengths, end setbacks and the standalone-600 setting.");
                    foreach (var geometry in geometries)
                    {
                        if (best != null && geometry.Positions.Length*2 > best.LegCount) continue;
                        // Try a centred grid and small phase shifts; every end tributary stays ≤ the entered spacing.
                        foreach (double phase in t.FixedRowPhaseMm.HasValue ? new[] {t.FixedRowPhaseMm.Value} : new[] { 0.0, -50, 50, -100, 100 })
                        {
                            var rows = Enumerable.Range(0,count).Select(i => first+phase+i*t.UpperSpacingMm).ToArray();
                            if (rows[0] <= 0 || rows.Last() >= covered) continue;
                            var tributaries = Enumerable.Range(0,count).Select(i => (i == count-1 ? covered : (rows[i]+rows[i+1])/2)-(i == 0 ? 0 : (rows[i-1]+rows[i])/2)).ToArray();
                            if (tributaries.Max() > t.UpperSpacingMm+1e-7) continue;
                            // A transverse row must bear on both longitudinal stocks, away from independent member breaks.
                            var conflicting=rows.FirstOrDefault(r=>geometry.Stock.Skip(1).Any(s=>Math.Abs(s.StartMm-r)<40));
                            if(conflicting>0) {double joint=geometry.Stock.Skip(1).OrderBy(s=>Math.Abs(s.StartMm-conflicting)).First().StartMm;diag.Geometry(input.DiagnosticScope+"row/joint",$"{input.DiagnosticScope}support row x={conflicting+edge:0.###} mm is {Math.Abs(joint-conflicting):0.###} mm from primary joint x={joint+edge:0.###} mm; minimum 40 mm.","Review upper row spacing and the primary stock combination.",40-Math.Abs(joint-conflicting));continue;}
                            foreach (var cross in crosses)
                            {
                                tested++; var candidate = Evaluate(input,geometry,width,rotated,cross,rows,tributaries,edge);
                                if (candidate != null && (best == null || candidate.LegCount < best.LegCount || candidate.LegCount == best.LegCount
                                    && (Timber(candidate) < Timber(best)-.01 || Math.Abs(Timber(candidate)-Timber(best)) < .01 && candidate.Utilization < best.Utilization))) best = candidate;
                            }
                        }
                    }
                }
            }
            if (best == null) throw diag.Exception("No valid transferred-slab layout found within the bounded search.");
            best.Tested = tested; return best;
        }
        private static double Timber(TowerLayout l) => l.StockPlan.TotalStockLengthMm+l.Transfer.AdditionalTimberMm;
        internal static double[] CoveredLengths(TowerInput input) => Covers(input).Select(c=>c.Sum()).Distinct().ToArray();
        internal static double[][] StockCovers(TowerInput input) => Covers(input).ToArray();
        private static IEnumerable<double[]> Covers(TowerInput input)
        {
            if(input.TransferredSlab?.StockCovers!=null)return input.TransferredSlab.StockCovers;
            int max=(int)Math.Floor(input.LengthMm), min=(int)Math.Ceiling(input.LengthMm-2*input.MaximumConcreteEndOverhangMm);
            var states = new Dictionary<int,List<double[]>> { { 0,new List<double[]> { new double[0] } } };
            for (int total=1;total<=max;total++) foreach (double stock in input.StockLengthsMm.OrderByDescending(x=>x))
                if (stock<=total && states.TryGetValue(total-(int)stock,out var previous))
                {
                    if (!states.TryGetValue(total,out var list)) states[total]=list=new List<double[]>();
                    list.AddRange(previous.Where(x=>x.Length<10).Select(x=>x.Concat(new[] {stock}).ToArray()));
                    states[total]=list.OrderBy(x=>x.Length).ThenByDescending(x=>x[0]).Take(3).ToList();
                }
            var catalogue = states.Where(x=>x.Key>=min && x.Key<=max).OrderBy(x=>x.Value.Min(y=>y.Length)).ThenBy(x=>x.Key).SelectMany(x=>x.Value).Take(18).ToArray();
            if(!input.AllowCustomH20Lengths)return catalogue;
            // Bounded balanced runs, including small joint shifts to avoid transverse rows at member breaks.
            // Do not send a dense 25 mm catalogue through the stock-only DP or its 18-cover cap.
            int low=(int)Math.Ceiling(Math.Max(25,min)/25.0), high=max/25;
            var custom=new List<double[]>();
            foreach(int units in new[] {low,high,(low+high)/2}.Where(x=>x>=low && x<=high).Distinct())
                for(int count=1;count<=10;count++)
                {
                    var runs=Enumerable.Range(0,count).Select(i=>(units/count+(i<units%count ? 1 : 0))*25.0).ToArray();
                    if(runs.Any(x=>x<=2*input.StockEndSetbackMm))continue;
                    custom.Add(runs);
                    if(count>1)foreach(int shift in new[] {-1,1})
                    {
                        var varied=(double[])runs.Clone();varied[0]+=shift*25;varied[count-1]-=shift*25;
                        if(varied.All(x=>x>2*input.StockEndSetbackMm))custom.Add(varied);
                    }
                }
            return catalogue.Concat(custom).GroupBy(x=>string.Join(";",x)).Select(g=>g.First()).OrderBy(x=>x.Length).ThenBy(x=>x.Sum()).ToArray();
        }
        private static IEnumerable<Geometry> Geometries(double[] stock,bool rotated,TowerInput input)
        {
            var states=new List<Geometry> {new Geometry {Stock=new TowerStockPiece[0],Positions=new double[0],Joined=new bool[0]}};
            foreach (double length in stock)
            {
                var patterns=new List<double[]>();
                foreach (double bay in rotated ? new[] {1524.0} : TowerOptimizer.BaysMm)
                    for (int n=2;n<=(rotated ? 2 : 6);n++) if ((n-1)*bay<=length-2*input.StockEndSetbackMm && !(input.Exclude600Standalone && n==2 && bay==600)) patterns.Add(Enumerable.Repeat(bay,n-1).ToArray());
                if (!rotated) foreach(double a in TowerOptimizer.BaysMm) foreach(double b in TowerOptimizer.BaysMm)
                    if (a!=b && a+b<=length-2*input.StockEndSetbackMm) patterns.Add(new[] {a,b});
                // Independent towers can have an unbraced gap, including multiple rotated towers under one stock run.
                foreach(double a in rotated ? new[] {1524.0} : TowerOptimizer.BaysMm) foreach(double b in rotated ? new[] {1524.0} : TowerOptimizer.BaysMm)
                    foreach(double inset in new[] {input.StockEndSetbackMm,300.0,450.0}.Where(x=>x>=input.StockEndSetbackMm))
                    {
                        double gap=length-2*inset-a-b;
                        if(gap>=25 && !(input.Exclude600Standalone && (a==600 || b==600))) patterns.Add(new[] {a,gap,b});
                    }
                var next=new List<Geometry>();
                foreach(var state in states) foreach(var bays in patterns)
                {
                    double start=state.Stock.Sum(x=>x.StockLengthMm), inset=(length-bays.Sum())/2; int first=state.Positions.Length;
                    var p=new List<double> {start+inset}; foreach(double bay in bays) p.Add(p.Last()+bay);
                    bool keep=p.Count%2!=0 || !rotated && input.Exclude600Standalone && (bays[0]==600 || bays.Last()==600);
                    var joins=state.Joined.Concat(first==0 ? new bool[0] : new[] {false}).Concat(Enumerable.Range(1,p.Count-1).Select(i=>keep || i%2==1)).ToArray();
                    var positions=state.Positions.Concat(p).ToArray();
                    joins=TowerOptimizer.RemoveRedundantJoins(joins,positions,input.Exclude600Standalone);
                    if(joins==null)continue;
                    next.Add(new Geometry { Stock=state.Stock.Concat(new[] {new TowerStockPiece {StartMm=start,StockLengthMm=length,FirstSupport=first,LastSupport=first+p.Count-1}}).ToArray(),Positions=positions,Joined=joins });
                }
                states=next.GroupBy(x=>x.Positions.Length).SelectMany(g=>g.OrderBy(x=>x.Stock.Sum(s=>x.Positions[s.FirstSupport]-s.StartMm)).Take(12)).OrderBy(x=>x.Positions.Length).Take(84).ToList();
            }
            return states;
        }
        private static IEnumerable<Upper> UpperOptions(double bay,double load,double width,TowerInput input,double tributaryMm,UpperTransferPlan fixedPlan)
        {
            if (bay==0) return new[] { (Upper)null };
            var result=new List<Upper>();
            if(fixedPlan!=null)
            {
                fixedPlan.Validate(bay,input.StockLengthsMm,input.AllowCustomH20Lengths);
                var a=LoadedBeamAnalysis.Analyze(fixedPlan.StockLengthMm,new[] {fixedPlan.NearFootMm-fixedPlan.EdgeMm,bay-fixedPlan.FarFootMm-fixedPlan.EdgeMm},new[] {new BeamDistributedLoad {StartMm=0,EndMm=fixedPlan.StockLengthMm,KnPerM=load}},null,TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                if(Pass(a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Slab H20",1,$"stock {fixedPlan.StockLengthMm:0} mm · feet {fixedPlan.NearFootMm:0.###}/{fixedPlan.FarFootMm:0.###} mm from faces · base tributary {tributaryMm:0.###} mm")) result.Add(new Upper {Stock=fixedPlan.StockLengthMm,Custom=fixedPlan.IsCustomLength,Edge=fixedPlan.EdgeMm,Foot=fixedPlan.NearFootMm,OtherFoot=fixedPlan.FarFootMm,Bay=bay,Load=load,Analysis=a});
                return result;
            }
            foreach(double stock in UpperTransferPlan.SlabLengths(bay,input.StockLengthsMm,input.AllowCustomH20Lengths))
            {
                double edge=(bay-stock)/2; if (edge<200 || edge>350) continue;
                double min=Math.Max(250,edge+150), max=bay/2-150;
                var feet=new[] {min,max,Math.Max(min,Math.Min(max,(width-input.TransferredSlab.BeamWidthMm)/2))}
                    .Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Distinct();
                foreach(double foot in feet.Where(x=>x>=min && x<=max))
                {
                    var a=LoadedBeamAnalysis.Analyze(stock,new[] {foot-edge,bay-foot-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=load}},null,TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                    if (Pass(a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Slab H20",1,$"stock {stock:0} mm · foot {foot:0.###} mm from face · base tributary {tributaryMm:0.###} mm")) result.Add(new Upper { Stock=stock,Custom=!input.StockLengthsMm.Contains(stock),Edge=edge,Foot=foot,OtherFoot=foot,Bay=bay,Load=load,Analysis=a });
                }
            }
            if(UpperTransferPlan.SlabLengths(bay,input.StockLengthsMm,input.AllowCustomH20Lengths).Length==0)input.Diagnostics?.Geometry(input.DiagnosticScope+"slab length "+bay,$"{input.DiagnosticScope}slab bay {bay:0.###} mm cannot fit a positive 25 mm custom length with 200–350 mm edge strips.","Review the clear bay width and required edge clearance.");
            return result;
        }
        private static IEnumerable<CrossOption> CrossOptions(TowerInput input,double width,double tributaryMm)
        {
            var t=input.TransferredSlab; var result=new List<CrossOption>();
            foreach(var left in UpperOptions(t.LeftBayMm,t.LeftAreaLoadKnM2,width,input,tributaryMm,t.LeftPlan)) foreach(var right in UpperOptions(t.RightBayMm,t.RightAreaLoadKnM2,width,input,tributaryMm,t.RightPlan))
            {
                double lx=left==null ? -t.BeamWidthMm/2 : -t.BeamWidthMm/2-left.Foot, rx=right==null ? t.BeamWidthMm/2 : t.BeamWidthMm/2+right.Foot;
                double lo=Math.Min(-width/2,lx)-150,hi=Math.Max(width/2,rx)+150;
                var lowerLengths=input.StockLengthsMm.Where(x=>x>=hi-lo);
                // Unloaded tails need only the shortest custom length that encloses every bearing/load.
                double custom=Math.Ceiling((hi-lo)/25)*25;
                if(input.AllowCustomH20Lengths && custom<=50000)lowerLengths=lowerLengths.Concat(new[] {custom}).Distinct();
                var lowerStocks=lowerLengths.ToArray();
                if(lowerStocks.Length==0)input.Diagnostics?.Geometry(input.DiagnosticScope+"lower stock",$"{input.DiagnosticScope}lower secondary needs at least {hi-lo:0.###} mm stock to reach bearings/feet with 150 mm end allowance; longest available {input.StockLengthsMm.Max():0} mm.","Review lower secondary stock lengths and upper-foot positions.",hi-lo-input.StockLengthsMm.Max());
                foreach(double stock in lowerStocks)
                    foreach(double start in new[] {lo,hi-stock,(lo+hi-stock)/2}.Distinct())
                    {
                        var cases=new List<LoadedBeamAnalysis>(); var names=new List<string>(); bool valid=true;
                        for(int mask=0;mask<4;mask++)
                        {
                            if ((mask&1)!=0 && left==null || (mask&2)!=0 && right==null) continue;
                            var points=new List<BeamPointLoad>(); if ((mask&1)!=0) points.Add(new BeamPointLoad {PositionMm=lx-start,Kn=left.Analysis.Reactions[0]});
                            if ((mask&2)!=0) points.Add(new BeamPointLoad {PositionMm=rx-start,Kn=right.Analysis.Reactions[0]});
                            if ((mask&1)!=0) points.Add(new BeamPointLoad {PositionMm=-t.BeamWidthMm/2-100-start,Kn=left.Load*left.Edge/1000});
                            if ((mask&2)!=0) points.Add(new BeamPointLoad {PositionMm=t.BeamWidthMm/2+100-start,Kn=right.Load*right.Edge/1000});
                            var a=LoadedBeamAnalysis.Analyze(stock,new[] {-width/2-start,width/2-start},new[] {new BeamDistributedLoad {StartMm=-t.BeamWidthMm/2-start,EndMm=t.BeamWidthMm/2-start,KnPerM=t.BeamLineLoadKnM/(t.BeamWidthMm/1000)}},points.ToArray(),TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                            if (!Pass(a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Lower secondary",2,$"stock {stock:0} mm · primary span {width:0} mm · {(mask==0 ? "Beam" : mask==1 ? "Beam + left slab" : mask==2 ? "Beam + right slab" : "Beam + both slabs")} · base tributary {tributaryMm:0.###} mm")) { valid=false;break; }
                            cases.Add(a); names.Add(mask==0 ? "Beam" : mask==1 ? "Beam + left slab" : mask==2 ? "Beam + right slab" : "Beam + both slabs");
                        }
                        if(valid) result.Add(new CrossOption { Start=start,Stock=stock,Left=left,Right=right,Cases=cases.ToArray(),Names=names.ToArray() });
                    }
            }
            // Retain varied reaction splits as well as the least-timber choices; this is a bounded search.
            return result.OrderBy(x=>x.Stock+(x.Left?.Stock??0)+(x.Right?.Stock??0)).ThenBy(x=>x.Cases.Max(a=>a.DeflectionUtilization)).GroupBy(x=>Math.Round(x.Cases.Last().Reactions[0],1)).Select(g=>g.First()).Take(24).ToArray();
        }
        private static bool Pass(LoadedBeamAnalysis a,double scale=1,int h20Count=2,TowerSearchDiagnostics diag=null,string scope="H20",int stage=1,string location="")
        {
            diag?.Analysis(a,scale,h20Count,scope,stage,location);
            return a.Reactions.All(x=>x>=-1e-7) && a.MomentKnM*scale<=TowerOptimizer.PairMoment*h20Count/2+1e-8 && a.ShearKn*scale<=TowerOptimizer.PairShear*h20Count/2+1e-8 && a.DeflectionUtilization*scale<=1+1e-8;
        }
        private static TowerLayout Evaluate(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,double[] rows,double[] trib,double edge)
        {
            var ls=(input.TransferredSlab.LeftEffectiveWidthsMm??trib).Select(x=>x/1000).ToArray();
            var rs=(input.TransferredSlab.RightEffectiveWidthsMm??trib).Select(x=>x/1000).ToArray();
            if(ls.Length!=rows.Length || rs.Length!=rows.Length)throw new ArgumentException("Crossing beam row loads do not match the support rows.");
            if(cross.Left!=null && !Pass(cross.Left.Analysis,ls.Max(),1,input.Diagnostics,input.DiagnosticScope+"Left slab H20",1,$"governing row T{Array.IndexOf(ls,ls.Max())+1} · stock {cross.Left.Stock:0} mm") || cross.Right!=null && !Pass(cross.Right.Analysis,rs.Max(),1,input.Diagnostics,input.DiagnosticScope+"Right slab H20",1,$"governing row T{Array.IndexOf(rs,rs.Max())+1} · stock {cross.Right.Stock:0} mm"))return null;
            int n=g.Positions.Length; var left=new double[n]; var right=new double[n];
            var primaryCases=new List<TransferMember[]>(); var lowerCases=new List<TransferMember[]>();
            for(int c=0;c<cross.Cases.Length;c++)
            {
                var members=new List<TransferMember>(); var lower=new List<TransferMember>();
                for(int row=0;row<rows.Length;row++)
                {
                    var a=cross.Cases[c].Scale(trib[row]/1000);
                    var points=new List<BeamPointLoad>(); bool hasLeft=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"), hasRight=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
                    if(hasLeft) points.Add(new BeamPointLoad {PositionMm=-input.TransferredSlab.BeamWidthMm/2-cross.Left.Foot-cross.Start,Kn=cross.Left.Analysis.Reactions[0]*ls[row]});
                    if(hasRight) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamWidthMm/2+cross.Right.Foot-cross.Start,Kn=cross.Right.Analysis.Reactions[0]*rs[row]});
                    if(hasLeft) points.Add(new BeamPointLoad {PositionMm=-input.TransferredSlab.BeamWidthMm/2-100-cross.Start,Kn=cross.Left.Load*cross.Left.Edge/1000*ls[row]});
                    if(hasRight) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamWidthMm/2+100-cross.Start,Kn=cross.Right.Load*cross.Right.Edge/1000*rs[row]});
                    if(hasLeft && Math.Abs(ls[row]-trib[row]/1000)>1e-10 || hasRight && Math.Abs(rs[row]-trib[row]/1000)>1e-10)
                    {
                        int key=c*rows.Length+row;
                        if(!cross.RowAnalyses.TryGetValue(key,out a)) cross.RowAnalyses[key]=a=LoadedBeamAnalysis.Analyze(cross.Stock,new[] {-width/2-cross.Start,width/2-cross.Start},new[] {new BeamDistributedLoad {StartMm=-input.TransferredSlab.BeamWidthMm/2-cross.Start,EndMm=input.TransferredSlab.BeamWidthMm/2-cross.Start,KnPerM=input.TransferredSlab.BeamLineLoadKnM/input.TransferredSlab.BeamWidthMm*trib[row]}},points.ToArray(),TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                    }
                    if(!Pass(a,1,1,input.Diagnostics,input.DiagnosticScope+"Lower secondary",2,$"T{row+1} · x={rows[row]+edge:0.###} mm · {cross.Names[c]} · stock {cross.Stock:0} mm · primary span {width:0} mm"))return null;
                    lower.Add(new TransferMember {H20Count=1,Name="T"+(row+1),LoadCase=cross.Names[c],StartMm=cross.Start,LengthMm=cross.Stock,SupportsMm=new[] {-width/2-cross.Start,width/2-cross.Start},Points=points.ToArray(),Uniform=new[] {new BeamDistributedLoad {StartMm=-input.TransferredSlab.BeamWidthMm/2-cross.Start,EndMm=input.TransferredSlab.BeamWidthMm/2-cross.Start,KnPerM=input.TransferredSlab.BeamLineLoadKnM/input.TransferredSlab.BeamWidthMm*trib[row]}},Analysis=a});
                }
                for(int line=0;line<2;line++) foreach(var stock in g.Stock)
                {
                    var supports=g.Positions.Skip(stock.FirstSupport).Take(stock.LastSupport-stock.FirstSupport+1).Select(x=>x-stock.StartMm).ToArray();
                    var points=Enumerable.Range(0,rows.Length).Where(i=>rows[i]>=stock.StartMm && rows[i]<stock.EndMm).Select(i=>new BeamPointLoad {PositionMm=rows[i]-stock.StartMm,Kn=lower[i].Analysis.Reactions[line]}).ToArray();
                    var a=LoadedBeamAnalysis.Analyze(stock.StockLengthMm,supports,null,points,TowerOptimizer.PairEI,input.DeflectionDenominator);
                    bool memberPass=Pass(a,1,2,input.Diagnostics,input.DiagnosticScope+"Primary pair",3,$"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · stock {stock.StockLengthMm:0} mm · x={stock.StartMm+edge:0.###}–{stock.EndMm+edge:0.###} mm");
                    int peakLeg=Array.IndexOf(a.Reactions,a.Reactions.Max());
                    input.Diagnostics?.Legs(a.Reactions,input.LegCapacityKn,input.DiagnosticScope+"Tower legs",$"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · peak leg x={g.Positions[stock.FirstSupport+peakLeg]+edge:0.###} mm");
                    if(!memberPass || a.Reactions.Max()>input.LegCapacityKn+1e-8) return null;
                    for(int i=0;i<a.Reactions.Length;i++) { var envelope=line==0 ? left : right; envelope[stock.FirstSupport+i]=Math.Max(envelope[stock.FirstSupport+i],a.Reactions[i]); }
                    members.Add(new TransferMember {Name="P"+(Array.IndexOf(g.Stock,stock)+1)+(line==0 ? " L" : " R"),LoadCase=cross.Names[c],StartMm=stock.StartMm+edge,LengthMm=stock.StockLengthMm,SupportsMm=supports,Points=points,Uniform=new BeamDistributedLoad[0],Analysis=a});
                }
                primaryCases.Add(members.ToArray()); lowerCases.Add(lower.ToArray());
            }
            var pieces=g.Stock.Select((s,i)=>new TowerStockPiece {IsCustomLength=!input.StockLengthsMm.Contains(s.StockLengthMm),StartMm=s.StartMm+edge,StockLengthMm=s.StockLengthMm,FirstSupport=s.FirstSupport,LastSupport=s.LastSupport,
                MomentKnM=primaryCases.Max(c=>Math.Max(c[i].Analysis.MomentKnM,c[i+g.Stock.Length].Analysis.MomentKnM)),ShearKn=primaryCases.Max(c=>Math.Max(c[i].Analysis.ShearKn,c[i+g.Stock.Length].Analysis.ShearKn)),
                DeflectionMm=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionMm,c[i+g.Stock.Length].Analysis.DeflectionMm)),DeflectionUtilization=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionUtilization,c[i+g.Stock.Length].Analysis.DeflectionUtilization)),GoverningDeflectionRegion="Envelope of transferred point-load cases"}).ToArray();
            for(int i=0;i<pieces.Length;i++)
            {
                var governing=primaryCases.SelectMany(c=>new[] {c[i],c[i+g.Stock.Length]}).OrderByDescending(m=>m.Analysis.DeflectionUtilization).First().Analysis;
                pieces[i].GoverningDeflectionLimitMm=governing.GoverningLengthMm/input.DeflectionDenominator;
                pieces[i].GoverningDeflectionMm=governing.DeflectionUtilization*pieces[i].GoverningDeflectionLimitMm;
            }
            var upper=new List<TransferMember>();
            foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null)) upper.Add(new TransferMember {H20Count=1,Name=u==cross.Left ? "Upper left" : "Upper right",LoadCase="Slab · governing row",StartMm=u.Edge,LengthMm=u.Stock,SupportsMm=new[] {u.Foot-u.Edge,u.Bay-u.OtherFoot-u.Edge},Points=new BeamPointLoad[0],Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=u.Stock,KnPerM=u.Load*(u==cross.Left ? ls : rs).Max()}},Analysis=u.Analysis.Scale((u==cross.Left ? ls : rs).Max())});
            var transfer=new TransferredSlabResult {LowerCustom=!input.StockLengthsMm.Contains(cross.Stock),LeftUpperScales=ls,RightUpperScales=rs,EnteredUpperSpacingMm=input.TransferredSlab.UpperSpacingMm,RowsMm=rows.Select(x=>x+edge).ToArray(),TributaryWidthsMm=trib,LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,LeftFootMm=cross.Left?.Foot??0,RightFootMm=cross.Right?.Foot??0,
                LeftUpperStockMm=cross.Left?.Stock??0,RightUpperStockMm=cross.Right?.Stock??0,LeftUpperCustom=cross.Left?.Custom??false,RightUpperCustom=cross.Right?.Custom??false,LeftUpperEdgeMm=cross.Left?.Edge??0,RightUpperEdgeMm=cross.Right?.Edge??0,
                LeftFootReactionKn=(cross.Left?.Analysis.Reactions[0]??0)*ls.Max(),RightFootReactionKn=(cross.Right?.Analysis.Reactions[0]??0)*rs.Max(),
                CoveredSlabAreaM2=rows.Length==0 ? 0 : trib.Sum()*((cross.Left?.Stock??0)+(cross.Right?.Stock??0))/1e6,
                ShutterLoadKn=(cross.Left==null ? 0 : cross.Left.Load*cross.Left.Edge*ls.Sum()/1000)+(cross.Right==null ? 0 : cross.Right.Load*cross.Right.Edge*rs.Sum()/1000),
                SlabLoadKn=(cross.Left?.Analysis.Reactions[0]??0)*ls.Sum()+(cross.Right?.Analysis.Reactions[0]??0)*rs.Sum()+(cross.Left==null ? 0 : cross.Left.Load*cross.Left.Edge*ls.Sum()/1000)+(cross.Right==null ? 0 : cross.Right.Load*cross.Right.Edge*rs.Sum()/1000),PrimaryCases=primaryCases.ToArray(),LowerCases=lowerCases.ToArray(),UpperMembers=upper.ToArray(),CaseNames=cross.Names};
            // Two-support lower and upper bearers are statically determinate: support movement adds an affine displacement without altering reactions.
            if(!input.TransferredSlab.IsNetwork) for(int c=0;c<cross.Cases.Length;c++) for(int row=0;row<rows.Length;row++) foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null))
            {
                bool isLeft=u==cross.Left; if(!(cross.Names[c].Contains(isLeft ? "left" : "right") || cross.Names[c].Contains("both"))) continue;
                int run=Array.FindIndex(g.Stock,s=>rows[row]>=s.StartMm && rows[row]<s.EndMm);
                double dl=primaryCases[c][run].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2],dr=primaryCases[c][run+g.Stock.Length].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2];
                double x=(isLeft ? -1 : 1)*(input.TransferredSlab.BeamWidthMm/2+u.Foot);
                double foot=lowerCases[c][row].Analysis.Response(x-cross.Start)[2]+dl+(dr-dl)*(x+width/2)/width;
                var a=u.Analysis.Scale((isLeft ? ls : rs)[row]);
                transfer.SlabMovementMm=Math.Max(transfer.SlabMovementMm,a.DeflectionWithOffsetMm(foot));
            }
            var all=primaryCases.SelectMany(c=>c).Concat(lowerCases.SelectMany(c=>c)).Concat(upper).ToArray();
            double peak=Math.Max(left.Max(),right.Max()),util=Math.Max(peak/input.LegCapacityKn,all.Max(m=>m.Utilization));
            return new TowerLayout {Transfer=transfer,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,ConcreteEndOverhangMm=edge,LeftReactions=left,RightReactions=right,
                LeftLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[0])/(trib.Sum()/1000),RightLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[1])/(trib.Sum()/1000),LegCapacityKn=input.LegCapacityKn,MaxLegKn=peak,
                StockPlan=new TowerStockPlan {Pieces=pieces,Reactions=left.Zip(right,Math.Max).ToArray()},DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,MomentKnM=all.Max(m=>m.Analysis.MomentKnM),ShearKn=all.Max(m=>m.Analysis.ShearKn),DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm),Utilization=util,Governing=peak/input.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name};
        }
    }
}
