using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    public sealed class UpperTransferPlan
    {
        public double StockLengthMm, EdgeMm, NearFootMm, FarFootMm;
        public bool IsCustomLength;
        // Custom fallback keeps edge strips closest to 200 mm, without changing primary/lower stocks.
        public static double[] SlabLengths(double bay,double[] stocks,bool allowCustom=false)
        {
            var fitting=stocks.Where(stock=>(bay-stock)/2>=200 && (bay-stock)/2<=350).ToArray();
            if(allowCustom)
            {
                int first=(int)Math.Ceiling(Math.Max(25,bay-700)/25),last=(int)Math.Floor(Math.Min(50000,bay-400)/25);
                return fitting.Concat(Enumerable.Range(first,Math.Max(0,last-first+1)).Select(x=>x*25.0)).Distinct().OrderBy(x=>x).ToArray();
            }
            if(fitting.Length>0)return fitting;
            double custom=Math.Floor((bay-400)/25)*25;
            return custom>0 && (bay-custom)/2<=350 ? new[] {custom} : new double[0];
        }
        public UpperTransferPlan Reverse() => new UpperTransferPlan {StockLengthMm=StockLengthMm,EdgeMm=EdgeMm,NearFootMm=FarFootMm,FarFootMm=NearFootMm,IsCustomLength=IsCustomLength};
        public void Validate(double bay,double[] stocks,bool allowCustom=false)
        {
            if(new[] {StockLengthMm,EdgeMm,NearFootMm,FarFootMm}.Any(x=>!TowerBeamAnalysis.Finite(x)) || !SlabLengths(bay,stocks,allowCustom).Contains(StockLengthMm) || IsCustomLength!=!stocks.Contains(StockLengthMm)
                || Math.Abs(bay-StockLengthMm-2*EdgeMm)>1e-7 || EdgeMm<200 || EdgeMm>350 || NearFootMm<Math.Max(250,EdgeMm+150)
                || FarFootMm<Math.Max(250,EdgeMm+150) || bay-NearFootMm-FarFootMm<300) throw new ArgumentException("Invalid shared upper H20 stock or foot positions.");
        }
    }
    public sealed class TransferBeamRow : INotifyPropertyChanged
    {
        public string Label {get;set;}
        private double depth=1750,width=1100;
        public double DepthMm {get=>depth;set {depth=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(DepthMm)));}}
        public double WidthMm {get=>width;set {width=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(WidthMm)));}}
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public sealed class TransferBayRow : INotifyPropertyChanged
    {
        public string Label {get;set;}
        private double width=3900,thickness=300;
        public double WidthMm {get=>width;set {width=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(WidthMm)));}}
        public double ThicknessMm {get=>thickness;set {thickness=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(ThicknessMm)));}}
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public sealed class TransferCrossingBeamRow : INotifyPropertyChanged
    {
        private int bayNumber=1;
        private double position=3000,width=300,depth=600;
        public int BayNumber {get=>bayNumber;set {bayNumber=value;Notify(nameof(BayNumber));}}
        public double PositionMm {get=>position;set {position=value;Notify(nameof(PositionMm));}}
        public double WidthMm {get=>width;set {width=value;Notify(nameof(WidthMm));}}
        public double DepthMm {get=>depth;set {depth=value;Notify(nameof(DepthMm));}}
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
    public sealed class TransferNetworkInput
    {
        public TransferBeamRow[] Beams;
        public TransferBayRow[] Bays;
        public TransferCrossingBeamRow[] CrossingBeams = new TransferCrossingBeamRow[0];
        public double UpperSpacingMm, LiveKnM2, FormworkKnM2;
        public bool CustomLoads;
        public double AreaLoad(double thickness) => 25*thickness/1000+(CustomLoads ? LiveKnM2+FormworkKnM2 : BeamCalculatorViewModel.HeapingLoad(25*thickness/1000)+.75+.4);
        public void Validate(TowerInput common)
        {
            if(Beams==null || Bays==null || Beams.Length<2 || Beams.Length>8 || Bays.Length!=Beams.Length-1) throw new ArgumentException("Use 2–8 beams with one slab bay between each adjacent pair.");
            if(Beams.Any(b=>b==null || !TowerBeamAnalysis.Finite(b.WidthMm) || !TowerBeamAnalysis.Finite(b.DepthMm) || b.WidthMm<=0 || b.WidthMm>10000 || b.DepthMm<=0 || b.DepthMm>10000)) throw new ArgumentException("Enter a positive width and depth for every beam (maximum 10,000 mm).");
            if(Bays.Any(b=>b==null || !TowerBeamAnalysis.Finite(b.WidthMm) || !TowerBeamAnalysis.Finite(b.ThicknessMm) || b.WidthMm<=0 || b.WidthMm>30000 || b.ThicknessMm<=0)) throw new ArgumentException("Enter a positive width and thickness for every slab bay.");
            for(int i=0;i<Bays.Length;i++) if(Bays[i].ThicknessMm>Math.Min(Beams[i].DepthMm,Beams[i+1].DepthMm)) throw new ArgumentException($"Slab S{i+1} is thicker than an adjacent beam.");
            if(CustomLoads && new[] {LiveKnM2,FormworkKnM2}.Any(x=>!TowerBeamAnalysis.Finite(x) || x<0)) throw new ArgumentException("Custom loads must be finite and nonnegative.");
            if(CrossingBeams==null || CrossingBeams.Length>30 || CrossingBeams.Any(c=>c==null || c.BayNumber<1 || c.BayNumber>Bays.Length
                || new[] {c.PositionMm,c.WidthMm,c.DepthMm}.Any(x=>!TowerBeamAnalysis.Finite(x)) || c.WidthMm<=0 || c.DepthMm<Bays[c.BayNumber-1].ThicknessMm || c.DepthMm>10000
                || c.PositionMm-c.WidthMm/2<0 || c.PositionMm+c.WidthMm/2>common.LengthMm)) throw new ArgumentException("Crossing beams: enter a valid slab bay, centre position within the beam length, positive width and total depth at least the slab thickness (maximum 10,000 mm).");
            foreach(var group in CrossingBeams.GroupBy(c=>c.BayNumber))
            {
                var ordered=group.OrderBy(c=>c.PositionMm).ToArray();
                for(int i=1;i<ordered.Length;i++) if(ordered[i].PositionMm-ordered[i].WidthMm/2<ordered[i-1].PositionMm+ordered[i-1].WidthMm/2-1e-7) throw new ArgumentException("Crossing beams in the same bay must not overlap.");
            }
            new TransferredSlabInput {BeamWidthMm=Beams[0].WidthMm,BeamLineLoadKnM=AreaLoad(Beams[0].DepthMm)*Beams[0].WidthMm/1000,UpperSpacingMm=UpperSpacingMm,RightBayMm=Bays[0].WidthMm,RightAreaLoadKnM2=AreaLoad(Bays[0].ThicknessMm)}.Validate(common);
        }
    }
    public sealed class TransferNetworkResult
    {
        public TransferNetworkInput Input;
        public TowerLayout[] Beams;
        public UpperTransferPlan[] Bays;
        public double[] BeamCentresMm;
        public double SlabMovementMm, SlabLoadKn, SlabCoveredLengthMm, RowPhaseMm;
        public int Tested;
        public int LegCount=>Beams.Sum(b=>b.LegCount);
        public int UpperFootCount=>Bays.Length*2*Beams[0].Transfer.RowsMm.Length;
        public double TimberMm=>Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*b.Transfer.LowerStockLengthMm)+Bays.Sum(b=>b.StockLengthMm)*Beams[0].Transfer.RowsMm.Length;
        public int H20Count=>Beams.Sum(b=>b.StockPlan.TotalH20Count+b.Transfer.RowsMm.Length)+Bays.Length*Beams[0].Transfer.RowsMm.Length;
        public double PeakLegKn=>Beams.Max(b=>b.MaxLegKn);
        public string Summary=>$"{Beams.Length} beams · {Bays.Length} slab bays · {LegCount} tower legs\n{H20Count} H20s · {TimberMm/1000:0.00} m timber · peak leg {PeakLegKn:0.00} kN\nSlab movement {SlabMovementMm:0.00} mm · best found in bounded search";
        public string LayoutDiagnostics=>System.FormattableString.Invariant($"Shared slab coverage={SlabCoveredLengthMm:0.###} mm; row phase={RowPhaseMm:0.###} mm; rows={Beams[0].Transfer.RowsMm.Length}; upper feet={UpperFootCount}; support objective={LegCount+UpperFootCount}")
            +"\n"+string.Join("\n",Beams.Select((b,i)=>System.FormattableString.Invariant($"B{i+1}: legs={b.LegCount}; primary coverage={b.StockPlan.Pieces.Sum(p=>p.StockLengthMm):0.###} mm; end allowance={b.ConcreteEndOverhangMm:0.###} mm; lower H20={b.Transfer.LowerStockLengthMm:0.###} mm; beam load={b.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn):0.###} kN; slab load={b.Transfer.SlabLoadKn:0.###} kN; primary runs=")+string.Join(", ",b.StockPlan.Pieces.Select(p=>p.StockLengthMm.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)))))
            +"\n"+string.Join("\n",Bays.Select((b,i)=>System.FormattableString.Invariant($"S{i+1}: H20={b.StockLengthMm:0.###} mm; edge={b.EdgeMm:0.###} mm; left foot={b.NearFootMm:0.###} mm; right foot={b.FarFootMm:0.###} mm")));
        public string Schedule=>(Input.CrossingBeams.Length==0 ? "" : string.Join("\n",Input.CrossingBeams.Select((c,i)=>$"X{i+1}: S{c.BayNumber} · centre {c.PositionMm:0.###} mm · {c.WidthMm:0.###} × {c.DepthMm:0.###} mm · extra bay load {(Input.AreaLoad(c.DepthMm)-Input.AreaLoad(Input.Bays[c.BayNumber-1].ThicknessMm))*c.WidthMm/1000*Input.Bays[c.BayNumber-1].WidthMm/1000:0.00} kN through slab H20s"))+"\n\n")+string.Join("\n\n",Beams.Select((b,i)=>$"B{i+1}: {Input.Beams[i].WidthMm:0} × {Input.Beams[i].DepthMm:0} mm · {b.LegCount} legs\n{b.Schedule}\n{b.StockSchedule}"));
    }
    public static class TransferNetworkOptimizer
    {
        private sealed class State { public TowerLayout[] Beams; public UpperTransferPlan[] Bays; public int Legs;public double Timber; }
        private static bool Better(int legs,double timber,int oldLegs,double oldTimber)=>legs<oldLegs || legs==oldLegs && timber<oldTimber-.01;
        public static TransferNetworkResult Optimize(TowerInput common,TransferNetworkInput input)
        {
            input.Validate(common);
            return TowerStockPlanner.SearchWithCustom(common,()=>OptimizeCore(common,input),(a,b)=>Better(a.LegCount+a.UpperFootCount,a.TimberMm,b.LegCount+b.UpperFootCount,b.TimberMm));
        }
        private static TransferNetworkResult OptimizeCore(TowerInput common,TransferNetworkInput input)
        {
            input.Validate(common); TransferNetworkResult best=null;int tested=0;
            common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace);var diag=common.Diagnostics;
            // Geometry depends only on the common tower settings and primary stock run.
            // Keep it within this pass so edits and catalogue/custom modes cannot reuse stale work.
            var geometryCache = common.ReuseSearchWork ? new TransferredSlabOptimizer.GeometryCache() : null;
            common.Trace?.Work("Network primary cover generation");
            var stockCovers=TransferredSlabOptimizer.StockCovers(common);
            if(stockCovers.Length==0)diag.Geometry("primary coverage",$"Primary coverage: no available stock combination totals {common.LengthMm-2*common.MaximumConcreteEndOverhangMm:0.###}–{common.LengthMm:0.###} mm.","Review available primary stocks and the maximum concrete end allowance.");
            var scenarios=stockCovers.Select(c=>c.Sum()).Distinct().SelectMany(cover=>new[] {0.0,-50,50}.Select(phase=>Tuple.Create(cover,phase))).ToArray();
            var outcomes=new ScenarioResult[scenarios.Length];
            int workers=Math.Max(1,Math.Min(4,common.NetworkSearchWorkers));
            Action<int> run=index=>
            {
                var worker=common.CopyForSearch();
                worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace);
                outcomes[index]=SearchScenario(worker,input,stockCovers,geometryCache,scenarios[index].Item1,scenarios[index].Item2);
            };
            if(workers==1)for(int i=0;i<scenarios.Length;i++)run(i);
            else
            {
                try {Parallel.For(0,scenarios.Length,new ParallelOptions {MaxDegreeOfParallelism=workers,CancellationToken=common.Trace?.CancellationToken??default},run);}
                catch(AggregateException ex)
                {
                    common.Trace?.CheckCancellation();
                    ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();throw;
                }
            }
            // Completion order never decides the winner. Replay strict improvements in the
            // original coverage/phase order, including the original displacement updates.
            foreach(var outcome in outcomes)
            {
                common.Trace?.CheckCancellation();diag.Merge(outcome.Diagnostics);tested+=outcome.Tested;
                foreach(var candidate in outcome.Improvements)
                {
                    if(best!=null && !Better(candidate.LegCount+candidate.UpperFootCount,candidate.TimberMm,best.LegCount+best.UpperFootCount,best.TimberMm))continue;
                    best=candidate;CalculateMovement(best,common.Trace);
                }
            }
            if(best==null)throw diag.Exception("No valid beam/slab network found within the bounded search.");
            best.Tested=tested;return best;
        }
        private sealed class ScenarioResult
        {
            internal TowerSearchDiagnostics Diagnostics;
            internal int Tested;
            internal readonly List<TransferNetworkResult> Improvements=new List<TransferNetworkResult>();
        }
        private static ScenarioResult SearchScenario(TowerInput common,TransferNetworkInput input,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,double cover,double phase)
        {
            var diag=common.Diagnostics;TransferNetworkResult best=null;int tested=0;
            var outcome=new ScenarioResult {Diagnostics=diag};
            common.Trace?.Increment(BeamSearchCounter.CoverageVariants);common.Trace?.Work("Network shared slab H20 options");
            common.Trace?.Context(System.FormattableString.Invariant($"Coverage={cover:R} mm; row phase={phase:R} mm"));
            int rows=(int)Math.Ceiling(cover/input.UpperSpacingMm);double first=(cover-(rows-1)*input.UpperSpacingMm)/2+phase;
            if(first<=0 || first+(rows-1)*input.UpperSpacingMm>=cover || (rows>1 && (first+input.UpperSpacingMm/2>input.UpperSpacingMm+1e-7 || cover-(first+(rows-1)*input.UpperSpacingMm)+input.UpperSpacingMm/2>input.UpperSpacingMm+1e-7))) return outcome;
            double edge=(common.LengthMm-cover)/2;
            var outside=input.CrossingBeams.FirstOrDefault(c=>c.PositionMm-c.WidthMm/2<edge-1e-7 || c.PositionMm+c.WidthMm/2>edge+cover+1e-7);
            if(outside!=null) {diag.Geometry("crossing coverage",$"X{Array.IndexOf(input.CrossingBeams,outside)+1} in S{outside.BayNumber}: footprint {outside.PositionMm-outside.WidthMm/2:0.###}–{outside.PositionMm+outside.WidthMm/2:0.###} mm is outside primary coverage {edge:0.###}–{edge+cover:0.###} mm.","Review crossing position/width and primary stock end coverage.");return outcome;}
            var rowLoads=input.Bays.Select((b,i)=>EffectiveWidthsMm(common,input,i,cover,phase)).ToArray();
            if(rowLoads.Any(r=>r==null))return outcome;
            var options=input.Bays.Select((b,i)=>Options(common,input,i,rowLoads[i].Max(),$"T{Array.IndexOf(rowLoads[i],rowLoads[i].Max())+1} · x={edge+first+Array.IndexOf(rowLoads[i],rowLoads[i].Max())*input.UpperSpacingMm:0.###} mm").ToArray()).ToArray();if(options.Any(o=>o.Length==0))return outcome;
            var states=new Dictionary<int,State>();
            for(int option=0;option<options[0].Length;option++)
            {
                tested++;var beam=Solve(common,input,0,null,options[0][option],cover,phase,stockCovers,geometryCache);if(beam==null)continue;
                states[option]=new State {Beams=new[] {beam},Bays=new[] {options[0][option]},Legs=beam.LegCount,Timber=LowerTimber(beam)+options[0][option].StockLengthMm*rows};
            }
            for(int i=1;i<input.Bays.Length && states.Count>0;i++)
            {
                var next=new Dictionary<int,State>();
                foreach(var previous in states.Values) for(int option=0;option<options[i].Length;option++)
                {
                    tested++;var beam=Solve(common,input,i,previous.Bays.Last(),options[i][option],cover,phase,stockCovers,geometryCache);if(beam==null)continue;
                    int legs=previous.Legs+beam.LegCount;double timber=previous.Timber+LowerTimber(beam)+options[i][option].StockLengthMm*rows;
                    if(!next.TryGetValue(option,out var old) || Better(legs,timber,old.Legs,old.Timber))next[option]=new State {Beams=previous.Beams.Concat(new[] {beam}).ToArray(),Bays=previous.Bays.Concat(new[] {options[i][option]}).ToArray(),Legs=legs,Timber=timber};
                }
                states=next;
            }
            foreach(var state in states.Values)
            {
                tested++;var last=Solve(common,input,input.Beams.Length-1,state.Bays.Last(),null,cover,phase,stockCovers,geometryCache);if(last==null)continue;
                var beams=state.Beams.Concat(new[] {last}).ToArray();int legs=state.Legs+last.LegCount;double timber=state.Timber+LowerTimber(last);
                if(best!=null && !Better(legs+input.Bays.Length*2*rows,timber,best.LegCount+best.UpperFootCount,best.TimberMm))continue;
                var centres=new double[beams.Length];for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+input.Beams[i-1].WidthMm/2+input.Bays[i-1].WidthMm+input.Beams[i].WidthMm/2;
                best=new TransferNetworkResult {Input=input,Beams=beams,Bays=state.Bays,BeamCentresMm=centres,SlabLoadKn=beams.Sum(b=>b.Transfer.SlabLoadKn),SlabCoveredLengthMm=cover,RowPhaseMm=phase};
                outcome.Improvements.Add(best);
            }
            outcome.Tested=tested;return outcome;
        }
        // Incremental crossing loads go only to rows beneath the beam footprint, with closing tributaries.
        // The transverse beam spans the entire clear bay; subtract existing slab concrete and allowances once.
        internal static double[] EffectiveWidthsMm(TowerInput common,TransferNetworkInput input,int bay,double cover,double phase)
        {
            int count=(int)Math.Ceiling(cover/input.UpperSpacingMm);
            double first=(cover-(count-1)*input.UpperSpacingMm)/2+phase,edge=(common.LengthMm-cover)/2;
            var stations=Enumerable.Range(0,count).Select(i=>edge+first+i*input.UpperSpacingMm).ToArray();
            double slabLoad=input.AreaLoad(input.Bays[bay].ThicknessMm);
            var widths=Enumerable.Range(0,count).Select(i=>(i==count-1 ? edge+cover : (stations[i]+stations[i+1])/2)-(i==0 ? edge : (stations[i-1]+stations[i])/2)).ToArray();
            foreach(var crossing in input.CrossingBeams.Where(c=>c.BayNumber==bay+1))
            {
                double lo=crossing.PositionMm-crossing.WidthMm/2,hi=crossing.PositionMm+crossing.WidthMm/2;
                var beneath=Enumerable.Range(0,count).Where(i=>stations[i]>=lo && stations[i]<=hi).ToArray();
                if(beneath.Length==0) {common.Diagnostics?.Geometry("crossing row "+Array.IndexOf(input.CrossingBeams,crossing),$"X{Array.IndexOf(input.CrossingBeams,crossing)+1} in S{bay+1}: no H20 row beneath footprint {lo:0.###}–{hi:0.###} mm; nearest row x={stations.OrderBy(x=>Math.Abs(x-crossing.PositionMm)).First():0.###} mm at entered spacing {input.UpperSpacingMm:0.###} mm.","Review upper row spacing and crossing position/width.");return null;} // A crossing beam needs an H20 row physically below its footprint.
                for(int k=0;k<beneath.Length;k++)
                {
                    double a=k==0 ? lo : (stations[beneath[k-1]]+stations[beneath[k]])/2,b=k==beneath.Length-1 ? hi : (stations[beneath[k]]+stations[beneath[k+1]])/2;
                    widths[beneath[k]]+=(b-a)*(input.AreaLoad(crossing.DepthMm)-slabLoad)/slabLoad;
                }
            }
            return widths;
        }
        private static double LowerTimber(TowerLayout beam)=>beam.StockPlan.TotalStockLengthMm+beam.Transfer.RowsMm.Length*beam.Transfer.LowerStockLengthMm;
        private static IEnumerable<UpperTransferPlan> Options(TowerInput common,TransferNetworkInput input,int bay,double tributary,string rowLocation)
        {
            var row=input.Bays[bay];var result=new List<Tuple<UpperTransferPlan,double>>();
            var lengths=UpperTransferPlan.SlabLengths(row.WidthMm,common.StockLengthsMm,common.AllowCustomH20Lengths);
            if(lengths.Length==0)common.Diagnostics.Geometry("S"+(bay+1)+" length",$"S{bay+1}: no positive 25 mm slab H20 length fits clear bay {row.WidthMm:0.###} mm with 200–350 mm edge strips.","Review the clear bay width and required edge clearance.",blocker:true);
            foreach(double stock in lengths)
            {
                double edge=(row.WidthMm-stock)/2;if(edge<200 || edge>350)continue;
                double min=Math.Max(250,edge+150),max=row.WidthMm/2-150;
                Func<double,IEnumerable<double>> feet=width=>new[] {min,max}.Concat(new[] {600.0,1000,1500,1524,2000}.Select(w=>(w-width)/2)).Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Where(x=>x>=min && x<=max).Distinct();
                foreach(double a in feet(input.Beams[bay].WidthMm))foreach(double b in feet(input.Beams[bay+1].WidthMm))
                {
                    common.Trace?.Increment(BeamSearchCounter.UpperAnalyses);
                    var analysis=LoadedBeamAnalysis.Analyze(stock,new[] {a-edge,row.WidthMm-b-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=input.AreaLoad(row.ThicknessMm)*tributary/1000}},null,TowerOptimizer.PairEI/2,common.DeflectionDenominator);
                    common.Diagnostics.Analysis(analysis,1,1,"S"+(bay+1)+" slab H20",1,$"{rowLocation} · stock {stock:0} mm · feet {a:0.###}/{b:0.###} mm from faces");
                    if(analysis.MomentKnM>TowerOptimizer.PairMoment/2+1e-8 || analysis.ShearKn>TowerOptimizer.PairShear/2+1e-8 || analysis.DeflectionUtilization>1+1e-8 || analysis.Reactions.Any(r=>r< -1e-7))continue;
                    result.Add(Tuple.Create(new UpperTransferPlan {StockLengthMm=stock,EdgeMm=edge,NearFootMm=a,FarFootMm=b,IsCustomLength=!common.StockLengthsMm.Contains(stock)},analysis.DeflectionUtilization));
                }
            }
            // Keep varied near/far positions, both symmetric and asymmetric. The chain DP is exact over this bounded candidate set.
            return result.OrderBy(r=>r.Item1.StockLengthMm).ThenBy(r=>r.Item1.NearFootMm+r.Item1.FarFootMm).ThenBy(r=>r.Item2)
                .GroupBy(r=>Math.Round(r.Item1.NearFootMm/250)+":"+Math.Round(r.Item1.FarFootMm/250)).Select(g=>g.First().Item1).Take(10).ToArray();
        }
        private static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache)
        {
            common.Trace?.Increment(BeamSearchCounter.NetworkSolves);common.Trace?.Context(System.FormattableString.Invariant($"B{beam+1}; coverage={cover:R} mm; row phase={phase:R} mm"));
            var b=network.Beams[beam];var t=new TransferredSlabInput {IsNetwork=true,AllowLongerPrimaryCoverage=common.IndependentNetworkPrimaryCoverage,StockCovers=stockCovers,SharedGeometryCache=geometryCache,BeamWidthMm=b.WidthMm,BeamLineLoadKnM=network.AreaLoad(b.DepthMm)*b.WidthMm/1000,UpperSpacingMm=network.UpperSpacingMm,FixedCoveredLengthMm=cover,FixedRowPhaseMm=phase,
                LeftBayMm=left==null ? 0 : network.Bays[beam-1].WidthMm,RightBayMm=right==null ? 0 : network.Bays[beam].WidthMm,LeftAreaLoadKnM2=left==null ? 0 : network.AreaLoad(network.Bays[beam-1].ThicknessMm),RightAreaLoadKnM2=right==null ? 0 : network.AreaLoad(network.Bays[beam].ThicknessMm),LeftPlan=left?.Reverse(),RightPlan=right,
                LeftEffectiveWidthsMm=left==null ? null : EffectiveWidthsMm(common,network,beam-1,cover,phase),RightEffectiveWidthsMm=right==null ? null : EffectiveWidthsMm(common,network,beam,cover,phase)};
            var input=new TowerInput {Trace=common.Trace,ReuseSearchWork=common.ReuseSearchWork,PruneUneconomicCandidates=common.PruneUneconomicCandidates,AllowCustomH20Lengths=common.AllowCustomH20Lengths,LengthMm=common.LengthMm,LegCapacityKn=common.LegCapacityKn,DeflectionDenominator=common.DeflectionDenominator,DeflectionLimitMm=common.DeflectionLimitMm,StockLengthsMm=common.StockLengthsMm,StockEndSetbackMm=common.StockEndSetbackMm,MaximumConcreteEndOverhangMm=common.MaximumConcreteEndOverhangMm,Exclude600Standalone=common.Exclude600Standalone,TransferredSlab=t,Diagnostics=common.Diagnostics,DiagnosticScope=$"B{beam+1} · "};
            try{return TransferredSlabOptimizer.Optimize(input,true);}catch(TowerOptimizationException){return null;}
            catch(ArgumentException ex) {common.Diagnostics.Geometry("B"+(beam+1)+" geometry",$"B{beam+1}: {ex.Message}","Review this beam's geometry and transfer inputs.");return null;}
        }
        public static double FootDisplacement(TowerLayout beam,double beamWidth,int loadCase,int row,bool left)
        {
            var t=beam.Transfer;double station=t.RowsMm[row];int run=Array.FindIndex(beam.StockPlan.Pieces,p=>station>=p.StartMm && station<p.EndMm);var stock=beam.StockPlan.Pieces[run];
            double dl=t.PrimaryCases[loadCase][run].Analysis.Response(station-stock.StartMm)[2],dr=t.PrimaryCases[loadCase][run+beam.StockPlan.Pieces.Length].Analysis.Response(station-stock.StartMm)[2];
            double x=(left ? -1 : 1)*(beamWidth/2+(left ? t.LeftFootMm : t.RightFootMm));
            return t.LowerCases[loadCase][row].Analysis.Response(x-t.LowerStockStartMm)[2]+dl+(dr-dl)*(x+beam.WidthMm/2)/beam.WidthMm;
        }
        private static int Case(TowerLayout b,bool left,bool right)=>Array.IndexOf(b.Transfer.CaseNames,left && right ? "Beam + both slabs" : left ? "Beam + left slab" : right ? "Beam + right slab" : "Beam");
        private static void CalculateMovement(TransferNetworkResult result,BeamSearchTrace trace)
        {
            for(int bay=0;bay<result.Bays.Length;bay++)for(int mask=0;mask<4;mask++)
            {
                if((mask&1)!=0 && bay==0 || (mask&2)!=0 && bay==result.Bays.Length-1)continue;
                var left=result.Beams[bay];var right=result.Beams[bay+1];var upper=left.Transfer.UpperMembers.Single(m=>m.Name=="Upper right");
                int lc=Case(left,(mask&1)!=0,true),rc=Case(right,true,(mask&2)!=0);
                for(int row=0;row<left.Transfer.RowsMm.Length;row++)
                {
                    trace?.CheckCancellation();
                    double dl=FootDisplacement(left,result.Input.Beams[bay].WidthMm,lc,row,false),dr=FootDisplacement(right,result.Input.Beams[bay+1].WidthMm,rc,row,true);
                    var analysis=upper.Analysis.Scale(left.Transfer.UpperScale(false,row)/left.Transfer.RightUpperScales.Max());
                    double movement=analysis.DeflectionWithAffineOffsetMm(dl,dr,upper.SupportsMm[0],upper.SupportsMm[1]);
                    result.SlabMovementMm=Math.Max(result.SlabMovementMm,movement);
                    left.Transfer.SlabMovementMm=Math.Max(left.Transfer.SlabMovementMm,movement);right.Transfer.SlabMovementMm=Math.Max(right.Transfer.SlabMovementMm,movement);
                }
            }
        }
    }
}
