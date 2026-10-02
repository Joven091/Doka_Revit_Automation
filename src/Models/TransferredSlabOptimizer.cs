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
        public double Utilization => Math.Max(Analysis.DeflectionUtilization, Math.Max(Analysis.MomentKnM / 10, Analysis.ShearKn / 22));
    }
    public sealed class TransferredSlabResult
    {
        public double[] RowsMm, TributaryWidthsMm;
        public double EnteredUpperSpacingMm;
        public double LowerStockStartMm, LowerStockLengthMm, LeftFootMm, RightFootMm;
        public double LeftUpperStockMm, RightUpperStockMm, LeftUpperEdgeMm, RightUpperEdgeMm;
        public double LeftFootReactionKn, RightFootReactionKn, SlabMovementMm, CoveredSlabAreaM2, SlabLoadKn, ShutterLoadKn;
        public TransferMember[][] PrimaryCases;
        public TransferMember[][] LowerCases;
        public TransferMember[] UpperMembers;
        public string[] CaseNames;
        public int UpperFootCount => RowsMm.Length * UpperMembers.Length * 2;
        public int AdditionalH20Count => RowsMm.Length * (2 + UpperMembers.Length * 2);
        public double AdditionalTimberMm => RowsMm.Length * 2 * (LowerStockLengthMm + LeftUpperStockMm + RightUpperStockMm);
        public string Summary => $"Upper spacing {EnteredUpperSpacingMm:0} mm · {RowsMm.Length} bearer rows\nPeak upper foot L/R {LeftFootReactionKn:0.00}/{RightFootReactionKn:0.00} kN\nEstimated slab soffit movement {SlabMovementMm:0.00} mm";
        public string Schedule => string.Join("\n", RowsMm.Select((x,i) => $"T{i+1}: x={x:0.###} mm · tributary {TributaryWidthsMm[i]:0.###} mm · 2 × {LowerStockLengthMm:0} mm"))
            + $"\nLower transverse stock: {LowerStockStartMm:0.###} to {LowerStockStartMm+LowerStockLengthMm:0.###} mm from beam centre"
            + (LeftUpperStockMm > 0 ? $"\nLeft upper: 2 × {LeftUpperStockMm:0} mm per row · edge {LeftUpperEdgeMm:0} mm · foot clearance {LeftFootMm:0} mm" : "")
            + (RightUpperStockMm > 0 ? $"\nRight upper: 2 × {RightUpperStockMm:0} mm per row · edge {RightUpperEdgeMm:0} mm · foot clearance {RightFootMm:0} mm" : "")
            + $"\nSlab H20 covered area: {CoveredSlabAreaM2:0.00} m² · receiving beam slab load: {SlabLoadKn:0.00} kN (including shutter {ShutterLoadKn:0.00} kN at 100 mm outward from each beam face)\nLoad cases: {string.Join("; ", CaseNames)}\n"
            + string.Join("\n", PrimaryCases.SelectMany(x => x).Concat(LowerCases.SelectMany(x => x)).Concat(UpperMembers).Select(m => $"{m.Name} · {m.LoadCase}: M {m.Analysis.MomentKnM:0.00}/10 kNm · V {m.Analysis.ShearKn:0.00}/22 kN · d {m.Analysis.DeflectionMm:0.00} mm · utilization {m.Utilization*100:0.0}%"));
        public const string Assumptions = "Slab load transfers through paired upper H20s and vertical upper feet, paired lower secondary H20s and paired longitudinal primaries to tower legs. The upper assembly has no D3 geometry. Beam concrete is present before slab loading; beam-only, each one-sided slab stage and combined stages are checked. The slab area over upper H20 stock loads that stock; each 200–350 mm slab-edge strip loads its own side shutter, acting 100 mm outward from the adjacent beam face. These edge loads are additional to the upper-foot loads. Slab above the concrete beam width is not counted again. The longitudinal primary end allowance still excludes concrete beyond the primary ends. Upper stock is one uncut length per bay with no upper joints. Feet have at least 250 mm face-to-centre clearance and 150 mm inset from the upper stock end. The network uses 2–8 actual parallel beams, each with its own width/depth, and one slab bay between neighbours. Upper stock and two foot positions are shared by those neighbours; the two foot reactions need not be equal. Each lower beam is independently checked against the entered permissible leg load, and negative bearing reactions invalidate a candidate. Shared longitudinal coverage and support rows align upper supports across all beams. Two-support upper/lower bearers are statically determinate; different lower-foot movements add an affine displacement to the upper beam. Single-beam mode retains the mirrored opposite-beam assumption; network mode uses both actual beam responses. Pair limits are M=10 kNm, V=22 kN and EI=899.7512 kNm², assuming equal load sharing. Relative H20 deflection uses each support span/overhang; total slab movement is reported without an assumed project tolerance. Network search ranks all tower legs plus upper feet, then total H20 length, counting shared upper timber once. Search candidates are bounded; a global optimum is not proven. Plans show towers and primary stock lengths; secondary pairs are shown in the section with stock length and spacing. Sheathing, slab secondary H20s, shutter/connection strength, bearing, upper-support capacity/stability, lower tower stability and foundations require separate design. Doka H20 instructions restrict heavy point loads with wide spans; numeric member checks alone do not approve foot or shutter bearing details.";
    }
    /// <summary>Explicit downward force transfer; no equivalent UDL is used to check the primaries.</summary>
    public static class TransferredSlabOptimizer
    {
        private sealed class Geometry { public TowerStockPiece[] Stock; public double[] Positions; public bool[] Joined; }
        private sealed class Upper { public double Stock, Edge, Foot, OtherFoot, Bay, Load; public LoadedBeamAnalysis Analysis; }
        private sealed class CrossOption { public double Start, Stock; public Upper Left, Right; public LoadedBeamAnalysis[] Cases; public string[] Names; }
        public static TowerLayout Optimize(TowerInput input)
        {
            var t = input.TransferredSlab; t.Validate(input); TowerLayout best = null; int tested = 0;
            foreach (var cover in Covers(input))
            {
                double covered = cover.Sum(), edge = (input.LengthMm-covered)/2;
                if(t.FixedCoveredLengthMm.HasValue && Math.Abs(covered-t.FixedCoveredLengthMm.Value)>1e-7) continue;
                int count = (int)Math.Ceiling(covered/t.UpperSpacingMm);
                double first = (covered-(count-1)*t.UpperSpacingMm)/2;
                foreach (bool rotated in new[] { false, true }) foreach (double width in rotated ? TowerOptimizer.BaysMm : new[] { 1524.0 })
                {
                    if (rotated && input.Exclude600Standalone && width == 600) continue;
                    var crosses = CrossOptions(input,width,count>2 ? t.UpperSpacingMm : covered/count).ToArray(); if (crosses.Length == 0) continue;
                    foreach (var geometry in Geometries(cover,rotated,input))
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
                            if (rows.Any(r => geometry.Stock.Skip(1).Any(s => Math.Abs(s.StartMm-r) < 40))) continue;
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
            if (best == null) throw new ArgumentException("No valid transferred-slab layout found. Check upper spacing, slab-bay stock coverage, loads, leg capacity or available lengths. A different upper arrangement may be needed.");
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
            return states.Where(x=>x.Key>=min && x.Key<=max).OrderBy(x=>x.Value.Min(y=>y.Length)).ThenBy(x=>x.Key).SelectMany(x=>x.Value).Take(18).ToArray();
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
                    next.Add(new Geometry { Stock=state.Stock.Concat(new[] {new TowerStockPiece {StartMm=start,StockLengthMm=length,FirstSupport=first,LastSupport=first+p.Count-1}}).ToArray(),Positions=state.Positions.Concat(p).ToArray(),Joined=joins });
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
                fixedPlan.Validate(bay,input.StockLengthsMm);
                var a=LoadedBeamAnalysis.Analyze(fixedPlan.StockLengthMm,new[] {fixedPlan.NearFootMm-fixedPlan.EdgeMm,bay-fixedPlan.FarFootMm-fixedPlan.EdgeMm},new[] {new BeamDistributedLoad {StartMm=0,EndMm=fixedPlan.StockLengthMm,KnPerM=load}},null,TowerOptimizer.PairEI,input.DeflectionDenominator);
                if(Pass(a,tributaryMm/1000)) result.Add(new Upper {Stock=fixedPlan.StockLengthMm,Edge=fixedPlan.EdgeMm,Foot=fixedPlan.NearFootMm,OtherFoot=fixedPlan.FarFootMm,Bay=bay,Load=load,Analysis=a});
                return result;
            }
            foreach(double stock in input.StockLengthsMm)
            {
                double edge=(bay-stock)/2; if (edge<200 || edge>350) continue;
                double min=Math.Max(250,edge+150), max=bay/2-150;
                var feet=new[] {min,max,Math.Max(min,Math.Min(max,(width-input.TransferredSlab.BeamWidthMm)/2))}
                    .Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Distinct();
                foreach(double foot in feet.Where(x=>x>=min && x<=max))
                {
                    var a=LoadedBeamAnalysis.Analyze(stock,new[] {foot-edge,bay-foot-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=load}},null,TowerOptimizer.PairEI,input.DeflectionDenominator);
                    if (Pass(a,tributaryMm/1000)) result.Add(new Upper { Stock=stock,Edge=edge,Foot=foot,OtherFoot=foot,Bay=bay,Load=load,Analysis=a });
                }
            }
            return result;
        }
        private static IEnumerable<CrossOption> CrossOptions(TowerInput input,double width,double tributaryMm)
        {
            var t=input.TransferredSlab; var result=new List<CrossOption>();
            foreach(var left in UpperOptions(t.LeftBayMm,t.LeftAreaLoadKnM2,width,input,tributaryMm,t.LeftPlan)) foreach(var right in UpperOptions(t.RightBayMm,t.RightAreaLoadKnM2,width,input,tributaryMm,t.RightPlan))
            {
                double lx=left==null ? -t.BeamWidthMm/2 : -t.BeamWidthMm/2-left.Foot, rx=right==null ? t.BeamWidthMm/2 : t.BeamWidthMm/2+right.Foot;
                double lo=Math.Min(-width/2,lx)-150,hi=Math.Max(width/2,rx)+150;
                foreach(double stock in input.StockLengthsMm.Where(x=>x>=hi-lo))
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
                            var a=LoadedBeamAnalysis.Analyze(stock,new[] {-width/2-start,width/2-start},new[] {new BeamDistributedLoad {StartMm=-t.BeamWidthMm/2-start,EndMm=t.BeamWidthMm/2-start,KnPerM=t.BeamLineLoadKnM/(t.BeamWidthMm/1000)}},points.ToArray(),TowerOptimizer.PairEI,input.DeflectionDenominator);
                            if (!Pass(a,tributaryMm/1000)) { valid=false;break; }
                            cases.Add(a); names.Add(mask==0 ? "Beam" : mask==1 ? "Beam + left slab" : mask==2 ? "Beam + right slab" : "Beam + both slabs");
                        }
                        if(valid) result.Add(new CrossOption { Start=start,Stock=stock,Left=left,Right=right,Cases=cases.ToArray(),Names=names.ToArray() });
                    }
            }
            // Retain varied reaction splits as well as the least-timber choices; this is a bounded search.
            return result.OrderBy(x=>x.Stock+(x.Left?.Stock??0)+(x.Right?.Stock??0)).ThenBy(x=>x.Cases.Max(a=>a.DeflectionUtilization)).GroupBy(x=>Math.Round(x.Cases.Last().Reactions[0],1)).Select(g=>g.First()).Take(24).ToArray();
        }
        private static bool Pass(LoadedBeamAnalysis a,double scale=1) => a.Reactions.All(x=>x>=-1e-7) && a.MomentKnM*scale<=10+1e-8 && a.ShearKn*scale<=22+1e-8 && a.DeflectionUtilization*scale<=1+1e-8;
        private static TowerLayout Evaluate(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,double[] rows,double[] trib,double edge)
        {
            if(new[] {cross.Left,cross.Right}.Any(u=>u!=null && !Pass(u.Analysis,trib.Max()/1000))) return null;
            int n=g.Positions.Length; var left=new double[n]; var right=new double[n];
            var primaryCases=new List<TransferMember[]>(); var lowerCases=new List<TransferMember[]>();
            for(int c=0;c<cross.Cases.Length;c++)
            {
                var members=new List<TransferMember>(); var lower=new List<TransferMember>();
                for(int row=0;row<rows.Length;row++)
                {
                    var a=cross.Cases[c].Scale(trib[row]/1000);
                    if(!Pass(a)) return null;
                    var points=new List<BeamPointLoad>(); bool hasLeft=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"), hasRight=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
                    if(hasLeft) points.Add(new BeamPointLoad {PositionMm=-input.TransferredSlab.BeamWidthMm/2-cross.Left.Foot-cross.Start,Kn=cross.Left.Analysis.Reactions[0]*trib[row]/1000});
                    if(hasRight) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamWidthMm/2+cross.Right.Foot-cross.Start,Kn=cross.Right.Analysis.Reactions[0]*trib[row]/1000});
                    if(hasLeft) points.Add(new BeamPointLoad {PositionMm=-input.TransferredSlab.BeamWidthMm/2-100-cross.Start,Kn=cross.Left.Load*cross.Left.Edge/1000*trib[row]/1000});
                    if(hasRight) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamWidthMm/2+100-cross.Start,Kn=cross.Right.Load*cross.Right.Edge/1000*trib[row]/1000});
                    lower.Add(new TransferMember {Name="T"+(row+1),LoadCase=cross.Names[c],StartMm=cross.Start,LengthMm=cross.Stock,SupportsMm=new[] {-width/2-cross.Start,width/2-cross.Start},Points=points.ToArray(),Uniform=new[] {new BeamDistributedLoad {StartMm=-input.TransferredSlab.BeamWidthMm/2-cross.Start,EndMm=input.TransferredSlab.BeamWidthMm/2-cross.Start,KnPerM=input.TransferredSlab.BeamLineLoadKnM/input.TransferredSlab.BeamWidthMm*trib[row]}},Analysis=a});
                }
                for(int line=0;line<2;line++) foreach(var stock in g.Stock)
                {
                    var supports=g.Positions.Skip(stock.FirstSupport).Take(stock.LastSupport-stock.FirstSupport+1).Select(x=>x-stock.StartMm).ToArray();
                    var points=Enumerable.Range(0,rows.Length).Where(i=>rows[i]>=stock.StartMm && rows[i]<stock.EndMm).Select(i=>new BeamPointLoad {PositionMm=rows[i]-stock.StartMm,Kn=lower[i].Analysis.Reactions[line]}).ToArray();
                    var a=LoadedBeamAnalysis.Analyze(stock.StockLengthMm,supports,null,points,TowerOptimizer.PairEI,input.DeflectionDenominator);
                    if(!Pass(a) || a.Reactions.Max()>input.LegCapacityKn+1e-8) return null;
                    for(int i=0;i<a.Reactions.Length;i++) { var envelope=line==0 ? left : right; envelope[stock.FirstSupport+i]=Math.Max(envelope[stock.FirstSupport+i],a.Reactions[i]); }
                    members.Add(new TransferMember {Name="P"+(Array.IndexOf(g.Stock,stock)+1)+(line==0 ? " L" : " R"),LoadCase=cross.Names[c],StartMm=stock.StartMm+edge,LengthMm=stock.StockLengthMm,SupportsMm=supports,Points=points,Uniform=new BeamDistributedLoad[0],Analysis=a});
                }
                primaryCases.Add(members.ToArray()); lowerCases.Add(lower.ToArray());
            }
            var pieces=g.Stock.Select((s,i)=>new TowerStockPiece {StartMm=s.StartMm+edge,StockLengthMm=s.StockLengthMm,FirstSupport=s.FirstSupport,LastSupport=s.LastSupport,
                MomentKnM=primaryCases.Max(c=>Math.Max(c[i].Analysis.MomentKnM,c[i+g.Stock.Length].Analysis.MomentKnM)),ShearKn=primaryCases.Max(c=>Math.Max(c[i].Analysis.ShearKn,c[i+g.Stock.Length].Analysis.ShearKn)),
                DeflectionMm=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionMm,c[i+g.Stock.Length].Analysis.DeflectionMm)),DeflectionUtilization=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionUtilization,c[i+g.Stock.Length].Analysis.DeflectionUtilization)),GoverningDeflectionRegion="Envelope of transferred point-load cases"}).ToArray();
            for(int i=0;i<pieces.Length;i++)
            {
                var governing=primaryCases.SelectMany(c=>new[] {c[i],c[i+g.Stock.Length]}).OrderByDescending(m=>m.Analysis.DeflectionUtilization).First().Analysis;
                pieces[i].GoverningDeflectionLimitMm=governing.GoverningLengthMm/input.DeflectionDenominator;
                pieces[i].GoverningDeflectionMm=governing.DeflectionUtilization*pieces[i].GoverningDeflectionLimitMm;
            }
            var upper=new List<TransferMember>();
            foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null)) upper.Add(new TransferMember {Name=u==cross.Left ? "Upper left" : "Upper right",LoadCase="Slab · maximum tributary row",StartMm=u.Edge,LengthMm=u.Stock,SupportsMm=new[] {u.Foot-u.Edge,u.Bay-u.OtherFoot-u.Edge},Points=new BeamPointLoad[0],Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=u.Stock,KnPerM=u.Load*trib.Max()/1000}},Analysis=u.Analysis.Scale(trib.Max()/1000)});
            var transfer=new TransferredSlabResult {EnteredUpperSpacingMm=input.TransferredSlab.UpperSpacingMm,RowsMm=rows.Select(x=>x+edge).ToArray(),TributaryWidthsMm=trib,LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,LeftFootMm=cross.Left?.Foot??0,RightFootMm=cross.Right?.Foot??0,
                LeftUpperStockMm=cross.Left?.Stock??0,RightUpperStockMm=cross.Right?.Stock??0,LeftUpperEdgeMm=cross.Left?.Edge??0,RightUpperEdgeMm=cross.Right?.Edge??0,
                LeftFootReactionKn=(cross.Left?.Analysis.Reactions[0]??0)*trib.Max()/1000,RightFootReactionKn=(cross.Right?.Analysis.Reactions[0]??0)*trib.Max()/1000,
                CoveredSlabAreaM2=rows.Length==0 ? 0 : trib.Sum()*((cross.Left?.Stock??0)+(cross.Right?.Stock??0))/1e6,
                ShutterLoadKn=trib.Sum()/1e6*((cross.Left==null ? 0 : cross.Left.Load*cross.Left.Edge)+(cross.Right==null ? 0 : cross.Right.Load*cross.Right.Edge)),
                SlabLoadKn=trib.Sum()/1000*((cross.Left?.Analysis.Reactions[0]??0)+(cross.Right?.Analysis.Reactions[0]??0))+trib.Sum()/1e6*((cross.Left==null ? 0 : cross.Left.Load*cross.Left.Edge)+(cross.Right==null ? 0 : cross.Right.Load*cross.Right.Edge)),PrimaryCases=primaryCases.ToArray(),LowerCases=lowerCases.ToArray(),UpperMembers=upper.ToArray(),CaseNames=cross.Names};
            // Two-support lower and upper bearers are statically determinate: support movement adds an affine displacement without altering reactions.
            if(!input.TransferredSlab.IsNetwork) for(int c=0;c<cross.Cases.Length;c++) for(int row=0;row<rows.Length;row++) foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null))
            {
                bool isLeft=u==cross.Left; if(!(cross.Names[c].Contains(isLeft ? "left" : "right") || cross.Names[c].Contains("both"))) continue;
                int run=Array.FindIndex(g.Stock,s=>rows[row]>=s.StartMm && rows[row]<s.EndMm);
                double dl=primaryCases[c][run].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2],dr=primaryCases[c][run+g.Stock.Length].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2];
                double x=(isLeft ? -1 : 1)*(input.TransferredSlab.BeamWidthMm/2+u.Foot);
                double foot=lowerCases[c][row].Analysis.Response(x-cross.Start)[2]+dl+(dr-dl)*(x+width/2)/width;
                var a=u.Analysis.Scale(trib[row]/1000);
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
