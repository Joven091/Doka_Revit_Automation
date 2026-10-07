using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    public sealed class UpperTransferPlan
    {
        public const string SiteArrangement = "Slab H20 feet use equal setbacks from both concrete faces, giving equal stock-end overhangs. Repeat the same arrangement at every slab H20 row in a bay. Uniform slab loading gives equal foot reactions; receiving-member and tower-leg reactions remain individually checked for every loading stage. The specified maximum row spacing, bearings and end allowances require site control.";
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
            if(Math.Abs(NearFootMm-FarFootMm)>1e-7)throw new ArgumentException("Slab H20 supports must have equal setbacks from both concrete faces and equal stock-end overhangs.");
        }
    }
    public sealed class TransferBeamRow : INotifyPropertyChanged
    {
        public string Label {get;set;}
        private double depth=1750,width=1100;
        private double? bottom;
        public double BottomElevationMm {get=>bottom??-DepthMm;set {bottom=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(BottomElevationMm)));}}
        public double DepthMm {get=>depth;set {depth=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(DepthMm)));PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(BottomElevationMm)));}}
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
    public enum CrossBeamSupportMode { Automatic, ExistingSupportsOnly, DedicatedTowers, LegacySlabRows }
    public sealed class CrossBeamModeOption
    {
        public CrossBeamSupportMode Value {get;set;}
        public string Label {get;set;}
    }
    public sealed class TransferCrossingBeamRow : INotifyPropertyChanged
    {
        private int bayNumber=1;
        private double position=3000,width=300,depth=600;
        private double? bottom;
        private CrossBeamSupportMode mode=CrossBeamSupportMode.Automatic;
        public double BottomElevationMm {get=>bottom??-DepthMm;set {bottom=value;Notify(nameof(BottomElevationMm));}}
        public CrossBeamSupportMode SupportMode {get=>mode;set {mode=value;Notify(nameof(SupportMode));}}
        [System.Xml.Serialization.XmlIgnore]
        public CrossBeamModeOption[] SupportModes=>new[] {new CrossBeamModeOption {Value=CrossBeamSupportMode.Automatic,Label="Automatic"},new CrossBeamModeOption {Value=CrossBeamSupportMode.ExistingSupportsOnly,Label="Existing supports only"},new CrossBeamModeOption {Value=CrossBeamSupportMode.DedicatedTowers,Label="Dedicated towers"},new CrossBeamModeOption {Value=CrossBeamSupportMode.LegacySlabRows,Label="Legacy slab rows"}};
        public int BayNumber {get=>bayNumber;set {bayNumber=value;Notify(nameof(BayNumber));}}
        public double PositionMm {get=>position;set {position=value;Notify(nameof(PositionMm));}}
        public double WidthMm {get=>width;set {width=value;Notify(nameof(WidthMm));}}
        public double DepthMm {get=>depth;set {depth=value;Notify(nameof(DepthMm));Notify(nameof(BottomElevationMm));}}
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
            if(Beams.Any(b=>b==null || !TowerBeamAnalysis.Finite(b.WidthMm) || !TowerBeamAnalysis.Finite(b.DepthMm) || !TowerBeamAnalysis.Finite(b.BottomElevationMm) || b.WidthMm<=0 || b.WidthMm>10000 || b.DepthMm<=0 || b.DepthMm>10000)) throw new ArgumentException("Enter a positive width and depth for every beam (maximum 10,000 mm).");
            if(Bays.Any(b=>b==null || !TowerBeamAnalysis.Finite(b.WidthMm) || !TowerBeamAnalysis.Finite(b.ThicknessMm) || b.WidthMm<=0 || b.WidthMm>30000 || b.ThicknessMm<=0)) throw new ArgumentException("Enter a positive width and thickness for every slab bay.");
            for(int i=0;i<Bays.Length;i++) if(Bays[i].ThicknessMm>Math.Min(Beams[i].DepthMm,Beams[i+1].DepthMm)) throw new ArgumentException($"Slab S{i+1} is thicker than an adjacent beam.");
            if(CustomLoads && new[] {LiveKnM2,FormworkKnM2}.Any(x=>!TowerBeamAnalysis.Finite(x) || x<0)) throw new ArgumentException("Custom loads must be finite and nonnegative.");
            if(CrossingBeams==null || CrossingBeams.Length>30 || CrossingBeams.Any(c=>c==null || c.BayNumber<1 || c.BayNumber>Bays.Length
                || new[] {c.PositionMm,c.WidthMm,c.DepthMm,c.BottomElevationMm}.Any(x=>!TowerBeamAnalysis.Finite(x)) || !Enum.IsDefined(typeof(CrossBeamSupportMode),c.SupportMode) || c.WidthMm<=0 || c.DepthMm<=0 || c.DepthMm>10000
                || c.SupportMode==CrossBeamSupportMode.LegacySlabRows && c.DepthMm<Bays[c.BayNumber-1].ThicknessMm
                || c.PositionMm-c.WidthMm/2<0 || c.PositionMm+c.WidthMm/2>common.LengthMm)) throw new ArgumentException("Cross beams: enter a valid slab bay, centre position within the beam length and positive width/depth (maximum depth 10,000 mm). Legacy slab rows require total depth at least the slab thickness.");
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
        public CrossBeamSupportResult[] CrossSupports=new CrossBeamSupportResult[0];
        public double[] BeamCentresMm;
        public bool SlabRowActive(int bay,int row)=>!Input.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && c.BayNumber==bay+1 && Math.Abs(Beams[0].Transfer.RowsMm[row]-c.PositionMm)<c.WidthMm/2+40-1e-7);
        public int SlabRowCount(int bay)=>Bays[bay]==null ? 0 : Enumerable.Range(0,Beams[0].Transfer.RowsMm.Length).Count(r=>SlabRowActive(bay,r));
        public double SlabMovementMm, SlabLoadKn, SlabCoveredLengthMm, RowPhaseMm;
        public int Tested;
        public int LegCount=>Beams.Sum(b=>b.LegCount)+CrossSupports.Sum(c=>c.Towers?.LegCount??0);
        public int SeparateSlabCount=>Bays.Count(b=>b==null);
        public bool RequiresSeparateSlabFormwork=>SeparateSlabCount>0;
        public string SeparateSlabNotice=>!RequiresSeparateSlabFormwork ? "" : "Separate slab formwork required: "+string.Join(", ",Bays.Select((b,i)=>b==null ? "S"+(i+1) : null).Where(x=>x!=null))+". Its supports, timber and design are excluded from this result; these bays transfer no slab or shutter load to the calculated beams. Legacy crossing loads are excluded; dedicated cross-beam formwork is calculated separately.";
        public int UpperFootCount=>Enumerable.Range(0,Bays.Length).Sum(b=>SlabRowCount(b)*2);
        public double TimberMm=>Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*b.Transfer.LowerStockLengthMm)+Enumerable.Range(0,Bays.Length).Sum(b=>(Bays[b]?.StockLengthMm??0)*SlabRowCount(b))+CrossSupports.Sum(c=>c.TimberMm);
        public int H20Count=>Beams.Sum(b=>b.StockPlan.TotalH20Count+b.Transfer.RowsMm.Length)+Enumerable.Range(0,Bays.Length).Sum(SlabRowCount)+CrossSupports.Sum(c=>c.H20Count);
        public double PeakLegKn=>Math.Max(Beams.Max(b=>b.MaxLegKn),CrossSupports.Select(c=>c.Towers?.MaxLegKn??0).DefaultIfEmpty().Max());
        public string Summary=>$"{Beams.Length} beams · {Bays.Length} slab bays · {LegCount} tower legs\n{H20Count} H20s · {TimberMm/1000:0.00} m timber · peak leg {PeakLegKn:0.00} kN\nSlab movement {SlabMovementMm:0.00} mm · best found in bounded search"+(RequiresSeparateSlabFormwork ? "\n"+SeparateSlabNotice : "");
        public string LayoutDiagnostics=>string.Join("\n",CrossSupports.Select(c=>c.Schedule))+"\n"+System.FormattableString.Invariant($"Shared slab coverage={SlabCoveredLengthMm:0.###} mm; row phase={RowPhaseMm:0.###} mm; rows={Beams[0].Transfer.RowsMm.Length}; upper feet={UpperFootCount}; support objective={LegCount+UpperFootCount}")
            +"\nPrimary design model: UDL derived from beam/slab loads per metre; crossing loads stay over their physical footprints. No secondary-row point loads on primaries."
            +"\n"+string.Join("\n",Beams.Select((b,i)=>System.FormattableString.Invariant($"B{i+1}: legs={b.LegCount}; primary coverage={b.StockPlan.Pieces.Sum(p=>p.StockLengthMm):0.###} mm; end allowance={b.ConcreteEndOverhangMm:0.###} mm; lower H20={b.Transfer.LowerStockLengthMm:0.###} mm; beam load={b.Transfer.BeamLoadKn:0.###} kN; slab load={b.Transfer.SlabLoadKn:0.###} kN; primary runs=")+string.Join(", ",b.StockPlan.Pieces.Select(p=>p.StockLengthMm.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)))))
            +"\n"+string.Join("\n",Bays.Select((b,i)=>b==null ? $"S{i+1}: separate slab formwork required (not calculated)" : System.FormattableString.Invariant($"S{i+1}: H20={b.StockLengthMm:0.###} mm; edge={b.EdgeMm:0.###} mm; equal foot setback={b.NearFootMm:0.###} mm at both faces; repeat every row; maximum row spacing={Input.UpperSpacingMm:0.###} mm")));
        public string Schedule=>(Bays.Any(b=>b!=null) ? UpperTransferPlan.SiteArrangement+"\n"+string.Join("\n",Bays.Select((b,i)=>b==null ? null : $"S{i+1}: equal foot setback {b.NearFootMm:0.###} mm at both concrete faces; repeat every slab row; maximum row spacing {Input.UpperSpacingMm:0.###} mm").Where(x=>x!=null))+"\n\n" : "")+string.Join("\n\n",CrossSupports.Select(c=>c.Schedule))+ (CrossSupports.Length==0 ? "" : "\n"+CrossBeamSupportResult.Limitations+"\n\n")+(RequiresSeparateSlabFormwork ? SeparateSlabNotice+"\n\n" : "")+(Input.CrossingBeams.Length==0 ? "" : string.Join("\n",Input.CrossingBeams.Select((c,i)=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows ? $"X{i+1}: dedicated cross-beam formwork · bottom {c.BottomElevationMm:0.###} mm · requested {c.SupportMode}" : (Bays[c.BayNumber-1]==null ? $"X{i+1}: S{c.BayNumber} · separate formwork design required (not calculated)" : $"X{i+1}: S{c.BayNumber} · centre {c.PositionMm:0.###} mm · {c.WidthMm:0.###} × {c.DepthMm:0.###} mm · extra bay load {(Input.AreaLoad(c.DepthMm)-Input.AreaLoad(Input.Bays[c.BayNumber-1].ThicknessMm))*c.WidthMm/1000*Input.Bays[c.BayNumber-1].WidthMm/1000:0.00} kN through slab H20s")))+"\n\n")+string.Join("\n\n",Beams.Select((b,i)=>$"B{i+1}: {Input.Beams[i].WidthMm:0} × {Input.Beams[i].DepthMm:0} mm · bottom {Input.Beams[i].BottomElevationMm:0.###} mm · {b.LegCount} legs\n{b.Schedule}\n{b.StockSchedule}"));
    }
    public static class TransferNetworkOptimizer
    {
        private sealed class State { public TowerLayout[] Beams; public UpperTransferPlan[] Bays; public int Legs,Separate;public double Timber; }
        private static bool Better(int legs,double timber,int oldLegs,double oldTimber)=>legs<oldLegs || legs==oldLegs && timber<oldTimber-.01;
        private static bool Better(TransferNetworkResult a,TransferNetworkResult b)=>a.SeparateSlabCount<b.SeparateSlabCount || a.SeparateSlabCount==b.SeparateSlabCount && Better(a.LegCount+a.UpperFootCount,a.TimberMm,b.LegCount+b.UpperFootCount,b.TimberMm);
        public static TransferNetworkResult Optimize(TowerInput common,TransferNetworkInput input)
        {
            input.Validate(common);
            try {return TowerStockPlanner.SearchWithCustom(common,()=>OptimizeCore(common,input,false),Better);}
            catch(TowerOptimizationException fullFailure) when (!common.AllowCustomH20Lengths && common.AllowSeparateSlabFallback)
            {
                common.Trace?.Work("Separate slab formwork fallback");
                try {return common.Trace==null ? OptimizeCore(common,input,true) : common.Trace.RunPass(false,()=>OptimizeCore(common,input,true));}
                catch(TowerOptimizationException partialFailure)
                {throw new TowerOptimizationException(partialFailure.Message,fullFailure.Details+"\n\nSeparate slab formwork fallback also failed:\n"+partialFailure.Details);}
            }
        }
        // This bound is a complete passing network's support count. Strictly worse counts
        // may be discarded; equal counts retain all timber ties and original replay order.
        private sealed class Incumbent
        {
            private int supports=int.MaxValue;
            internal int Supports=>Volatile.Read(ref supports);
            internal void Publish(int value)
            {
                int old;do {old=Supports;if(value>=old)return;}while(Interlocked.CompareExchange(ref supports,value,old)!=old);
            }
        }
        private static TransferNetworkResult OptimizeCore(TowerInput common,TransferNetworkInput input,bool separate)
        {
            input.Validate(common); TransferNetworkResult best=null;int tested=0;
            common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace) {LazyLocations=common.LazySearchDiagnostics};var diag=common.Diagnostics;
            var geometryCache=common.ReuseSearchWork ? new TransferredSlabOptimizer.GeometryCache() : null;
            var incumbent=common.PruneNetworkBounds && !separate && input.CrossingBeams.All(c=>c.SupportMode==CrossBeamSupportMode.LegacySlabRows) ? new Incumbent() : null;
            common.Trace?.Work("Network primary cover generation");
            var stockCovers=TransferredSlabOptimizer.StockCovers(common);
            if(stockCovers.Length==0)
            {
                diag.Geometry("primary coverage",$"Primary coverage: no available stock combination totals {common.LengthMm-2*common.MaximumConcreteEndOverhangMm:0.###}–{common.LengthMm:0.###} mm.","Review available primary stocks and the maximum concrete end allowance.");
                throw diag.Exception("No primary stock coverage fits the beam; separate slab formwork cannot fix missing beam stock.");
            }
            // Every independent primary run has at least two supports on each of two lines.
            // This is a topology bound over the actual searched covers, not an assumed kN/leg.
            int minimumLegs=stockCovers.Min(c=>c.Length)*4;
            var scenarios=stockCovers.Select(c=>c.Sum()).Distinct().SelectMany(cover=>new[] {0.0,-50,50}.Select(phase=>Tuple.Create(cover,phase))).ToArray();
            var outcomes=new ScenarioResult[scenarios.Length];
            int workers=Math.Max(1,Math.Min(4,common.NetworkSearchWorkers));
            using(var numericalWork=new ThreadLocal<TransferredSlabOptimizer.SearchWorkCache>(()=>new TransferredSlabOptimizer.SearchWorkCache()))
            {
                Action<int> run=index=>
                {
                    var worker=common.CopyForSearch();
                    worker.DedicatedCrossCache=new Dictionary<Tuple<int,double>,CrossBeamSupportResult>();
                    worker.NetworkNumerics=common.ReuseSearchWork && (common.ReuseNetworkNumerics || common.ReuseTransverseNumerics) ? numericalWork.Value : null;
                    worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace) {LazyLocations=common.LazySearchDiagnostics};
                    outcomes[index]=SearchScenario(worker,input,stockCovers,geometryCache,incumbent,minimumLegs,scenarios[index].Item1,scenarios[index].Item2,separate);
                };
                if(workers==1)for(int i=0;i<scenarios.Length;i++)run(i);
                else
                {
                    try {Parallel.For(0,scenarios.Length,new ParallelOptions {MaxDegreeOfParallelism=workers,CancellationToken=common.Trace?.CancellationToken??default},run);}
                    catch(AggregateException ex)
                    {common.Trace?.CheckCancellation();ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();throw;}
                }
            }
            // Scheduling never resolves a tie: replay improvements in original scenario order.
            foreach(var outcome in outcomes)
            {
                common.Trace?.CheckCancellation();diag.Merge(outcome.Diagnostics);tested+=outcome.Tested;
                foreach(var candidate in outcome.Improvements)
                {
                    if(best!=null && !Better(candidate,best))continue;
                    best=candidate;CalculateMovement(best,common.Trace);
                }
            }
            if(best==null)throw diag.Exception("No valid beam/slab network found within the bounded search.");
            best.Tested=tested;return best;
        }
        private sealed class ScenarioResult
        {
            internal TowerSearchDiagnostics Diagnostics;internal int Tested;
            internal readonly List<TransferNetworkResult> Improvements=new List<TransferNetworkResult>();
        }
        private static TransferNetworkResult Result(TransferNetworkInput input,State state,TowerLayout last,double cover,double phase)
        {
            var beams=state.Beams.Concat(new[] {last}).ToArray();
            var centres=new double[beams.Length];for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+input.Beams[i-1].WidthMm/2+input.Bays[i-1].WidthMm+input.Beams[i].WidthMm/2;
            return new TransferNetworkResult {Input=input,Beams=beams,Bays=state.Bays,BeamCentresMm=centres,SlabLoadKn=beams.Sum(b=>b.Transfer.SlabLoadKn),SlabCoveredLengthMm=cover,RowPhaseMm=phase};
        }
        private static ScenarioResult SearchScenario(TowerInput common,TransferNetworkInput input,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,Incumbent incumbent,int minimumLegs,double cover,double phase,bool separate)
        {
            var diag=common.Diagnostics;TransferNetworkResult best=null;int tested=0;
            var outcome=new ScenarioResult {Diagnostics=diag};
            common.Trace?.Increment(BeamSearchCounter.CoverageVariants);common.Trace?.Work("Network shared slab H20 options");
            common.Trace?.Context(System.FormattableString.Invariant($"Coverage={cover:R} mm; row phase={phase:R} mm"));
            int rows=(int)Math.Ceiling(cover/input.UpperSpacingMm);double first=(cover-(rows-1)*input.UpperSpacingMm)/2+phase;
            if(first<=0 || first+(rows-1)*input.UpperSpacingMm>=cover || (rows>1 && (first+input.UpperSpacingMm/2>input.UpperSpacingMm+1e-7 || cover-(first+(rows-1)*input.UpperSpacingMm)+input.UpperSpacingMm/2>input.UpperSpacingMm+1e-7)))return outcome;
            int upperFeet=input.Bays.Length*2*rows;
            if(incumbent!=null && upperFeet+minimumLegs*input.Beams.Length>incumbent.Supports) {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);return outcome;}
            double edge=(common.LengthMm-cover)/2;
            var invalidBays=new HashSet<int>(input.CrossingBeams.Where(c=>c.PositionMm-c.WidthMm/2<edge-1e-7 || c.PositionMm+c.WidthMm/2>edge+cover+1e-7).Select(c=>c.BayNumber-1));
            if(!separate && invalidBays.Count>0) {diag.Geometry("crossing coverage","Crossing footprint is outside primary coverage.","Review crossing position/width and primary stock end coverage.");return outcome;}
            var rowLoads=input.Bays.Select((b,i)=>invalidBays.Contains(i) ? null : EffectiveWidthsMm(common,input,i,cover,phase)).ToArray();
            var options=input.Bays.Select((b,i)=>rowLoads[i]==null ? new UpperTransferPlan[0] : Options(common,input,i,rowLoads[i].Max(),$"T{Array.IndexOf(rowLoads[i],rowLoads[i].Max())+1} · x={edge+first+Array.IndexOf(rowLoads[i],rowLoads[i].Max())*input.UpperSpacingMm:0.###} mm").ToArray()).ToArray();
            if(separate)options=options.Select(o=>o.Concat(new UpperTransferPlan[] {null}).ToArray()).ToArray();
            if(options.Any(o=>o.Length==0))return outcome;
            Func<int,int,int> ceiling=(previous,remaining)=>incumbent==null ? int.MaxValue : incumbent.Supports-upperFeet-previous-remaining*minimumLegs;
            var states=new Dictionary<int,State>();
            for(int option=0;option<options[0].Length;option++)
            {
                tested++;var beam=Solve(common,input,0,null,options[0][option],cover,phase,stockCovers,geometryCache,ceiling(0,input.Beams.Length-1));if(beam==null)continue;
                states[option]=new State {Beams=new[] {beam},Bays=new[] {options[0][option]},Legs=beam.LegCount,Separate=options[0][option]==null ? 1 : 0,Timber=LowerTimber(beam)+(options[0][option]?.StockLengthMm??0)*rows};
            }
            for(int i=1;i<input.Bays.Length && states.Count>0;i++)
            {
                var next=new Dictionary<int,State>();
                foreach(var previous in states.Values)for(int option=0;option<options[i].Length;option++)
                {
                    var right=options[i][option];int count=previous.Separate+(right==null ? 1 : 0);
                    int limit=ceiling(previous.Legs,input.Beams.Length-i-1);
                    if(next.TryGetValue(option,out var old) && common.PruneNetworkBounds)
                    {
                        if(count>old.Separate) {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);continue;}
                        if(count==old.Separate)limit=Math.Min(limit,old.Legs-previous.Legs);
                    }
                    tested++;var beam=Solve(common,input,i,previous.Bays.Last(),right,cover,phase,stockCovers,geometryCache,limit);if(beam==null)continue;
                    int legs=previous.Legs+beam.LegCount;double timber=previous.Timber+LowerTimber(beam)+(right?.StockLengthMm??0)*rows;
                    if(old==null || count<old.Separate || count==old.Separate && Better(legs,timber,old.Legs,old.Timber))next[option]=new State {Beams=previous.Beams.Concat(new[] {beam}).ToArray(),Bays=previous.Bays.Concat(new[] {right}).ToArray(),Legs=legs,Separate=count,Timber=timber};
                }
                states=next;
            }
            foreach(var state in states.Values)
            {
                int limit=ceiling(state.Legs,0);
                if(best!=null && common.PruneNetworkBounds && !CrossBeamSupportOptimizer.Modern(input) && state.Separate==best.SeparateSlabCount)
                    limit=Math.Min(limit,best.LegCount+best.UpperFootCount-state.Bays.Count(b=>b!=null)*2*rows-state.Legs);
                tested++;var last=Solve(common,input,input.Beams.Length-1,state.Bays.Last(),null,cover,phase,stockCovers,geometryCache,limit);if(last==null)continue;
                var candidate=Result(input,state,last,cover,phase);
                if(!CrossBeamSupportOptimizer.Complete(common,candidate))continue;
                incumbent?.Publish(candidate.LegCount+candidate.UpperFootCount);
                if(best!=null && !Better(candidate,best))continue;
                best=candidate;outcome.Improvements.Add(best);
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
            widths=CrossBeamSupportOptimizer.SlabWidths(input,bay,edge,cover,stations,widths);
            if(widths==null) {common.Diagnostics?.Geometry("slab cross exclusion",$"S{bay+1}: remaining slab strips have no H20 within the entered {input.UpperSpacingMm:0.###} mm row spacing after cross-beam exclusion.","Review secondary spacing and crossing positions/widths.");return null;}
            foreach(var crossing in input.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode==CrossBeamSupportMode.LegacySlabRows))
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
            var lengths=UpperTransferPlan.SlabLengths(row.WidthMm,common.StockLengthsMm,common.AllowCustomH20Lengths)
                .Where(length=>common.AllowCustomH20Lengths || !common.RespectNetworkCatalogueLengths || common.StockLengthsMm.Contains(length)).ToArray();
            if(lengths.Length==0)common.Diagnostics.Geometry("S"+(bay+1)+" length",$"S{bay+1}: no permitted slab H20 length fits clear bay {row.WidthMm:0.###} mm with 200–350 mm edge strips.","Review the clear bay width and required edge clearance.",blocker:true);
            foreach(double stock in lengths)
            {
                double edge=(row.WidthMm-stock)/2;if(edge<200 || edge>350)continue;
                double min=Math.Max(250,edge+150),max=row.WidthMm/2-150;
                Func<double,IEnumerable<double>> feet=width=>new[] {min,max}.Concat(new[] {600.0,1000,1500,1524,2000}.Select(w=>(w-width)/2)).Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Where(x=>x>=min && x<=max).Distinct();
                // One setback is repeated on both faces and at every row in this bay.
                // Include geometry candidates from both receiving beams, without moving one foot independently.
                foreach(double foot in feet(input.Beams[bay].WidthMm).Concat(feet(input.Beams[bay+1].WidthMm)).Distinct())
                {
                    var analysis=TransferredSlabOptimizer.AnalyzeTransverse(common,BeamSearchCounter.UpperAnalyses,stock,new[] {foot-edge,row.WidthMm-foot-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=input.AreaLoad(row.ThicknessMm)*tributary/1000}},null,TowerOptimizer.PairEI/2,common.DeflectionDenominator);
                    common.Diagnostics.AnalysisLazy(analysis,1,1,"S"+(bay+1)+" slab H20",1,()=> $"{rowLocation} · stock {stock:0} mm · equal setback {foot:0.###} mm from both faces");
                    if(analysis.MomentKnM>TowerOptimizer.PairMoment/2+1e-8 || analysis.ShearKn>TowerOptimizer.PairShear/2+1e-8 || analysis.DeflectionUtilization>1+1e-8 || analysis.Reactions.Any(r=>r< -1e-7))continue;
                    result.Add(Tuple.Create(new UpperTransferPlan {StockLengthMm=stock,EdgeMm=edge,NearFootMm=foot,FarFootMm=foot,IsCustomLength=!common.StockLengthsMm.Contains(stock)},analysis.DeflectionUtilization));
                }
            }
            if(result.Count==0)common.Diagnostics.Geometry("S"+(bay+1)+" symmetric supports",$"S{bay+1}: no permitted slab H20 passes with equal support setbacks on both sides.","Review stock, spacing and receiving geometry, or provide separately designed slab supports.");
            // The chain DP compares only passing symmetric arrangements from this bounded set.
            return result.OrderBy(r=>r.Item1.StockLengthMm).ThenBy(r=>r.Item1.NearFootMm+r.Item1.FarFootMm).ThenBy(r=>r.Item2)
                .GroupBy(r=>Math.Round(r.Item1.NearFootMm/250)).Select(g=>g.First().Item1).Take(10).ToArray();
        }
        private static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling)
        {
            common.Trace?.CheckCancellation();
            if(ceiling<stockCovers.Min(c=>c.Length)*4) {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);return null;}
            common.Trace?.Increment(BeamSearchCounter.NetworkSolves);common.Trace?.Context(System.FormattableString.Invariant($"B{beam+1}; coverage={cover:R} mm; row phase={phase:R} mm"));
            var b=network.Beams[beam];var t=new TransferredSlabInput {IsNetwork=true,CrossFormwork=CrossBeamSupportOptimizer.Modern(network),AllowLongerPrimaryCoverage=common.IndependentNetworkPrimaryCoverage,StockCovers=stockCovers,SharedGeometryCache=geometryCache,SharedSearchWork=common.NetworkNumerics,BeamWidthMm=b.WidthMm,BeamLineLoadKnM=network.AreaLoad(b.DepthMm)*b.WidthMm/1000,UpperSpacingMm=network.UpperSpacingMm,FixedCoveredLengthMm=cover,FixedRowPhaseMm=phase,
                LeftBayMm=left==null ? 0 : network.Bays[beam-1].WidthMm,RightBayMm=right==null ? 0 : network.Bays[beam].WidthMm,LeftAreaLoadKnM2=left==null ? 0 : network.AreaLoad(network.Bays[beam-1].ThicknessMm),RightAreaLoadKnM2=right==null ? 0 : network.AreaLoad(network.Bays[beam].ThicknessMm),LeftPlan=left?.Reverse(),RightPlan=right,
                LeftCrossingBands=left==null ? new BeamDistributedLoad[0] : CrossingBands(network,beam-1),RightCrossingBands=right==null ? new BeamDistributedLoad[0] : CrossingBands(network,beam),
                LeftEffectiveWidthsMm=left==null ? null : EffectiveWidthsMm(common,network,beam-1,cover,phase),RightEffectiveWidthsMm=right==null ? null : EffectiveWidthsMm(common,network,beam,cover,phase)};
            var input=new TowerInput {Trace=common.Trace,ReuseSearchWork=common.ReuseSearchWork,PruneUneconomicCandidates=common.PruneUneconomicCandidates,StagePrimaryChecks=common.StagePrimaryChecks,ReuseNetworkNumerics=common.ReuseNetworkNumerics,ReuseTransverseNumerics=common.ReuseTransverseNumerics,LazySearchDiagnostics=common.LazySearchDiagnostics,DeferTransferLayout=common.DeferTransferLayout,SearchByLegTier=common.SearchByLegTier,NetworkLegCeiling=ceiling,AllowCustomH20Lengths=common.AllowCustomH20Lengths,LengthMm=common.LengthMm,LegCapacityKn=common.LegCapacityKn,DeflectionDenominator=common.DeflectionDenominator,DeflectionLimitMm=common.DeflectionLimitMm,StockLengthsMm=common.StockLengthsMm,StockEndSetbackMm=common.StockEndSetbackMm,MaximumConcreteEndOverhangMm=common.MaximumConcreteEndOverhangMm,Exclude600Standalone=common.Exclude600Standalone,TransferredSlab=t,Diagnostics=common.Diagnostics,DiagnosticScope=$"B{beam+1} · "};
            try {return TransferredSlabOptimizer.Optimize(input,true);}
            catch(TowerOptimizationException){return null;}
            catch(ArgumentException ex) {common.Diagnostics.Geometry("B"+(beam+1)+" geometry",$"B{beam+1}: {ex.Message}","Review this beam's geometry and transfer inputs.");return null;}
        }
        private static BeamDistributedLoad[] CrossingBands(TransferNetworkInput network,int bay)
        {
            double slab=network.AreaLoad(network.Bays[bay].ThicknessMm);
            return network.CrossingBeams.Where(c=>c.BayNumber==bay+1).Select(c=>new BeamDistributedLoad {
                StartMm=c.PositionMm-c.WidthMm/2,EndMm=c.PositionMm+c.WidthMm/2,
                KnPerM=c.SupportMode==CrossBeamSupportMode.LegacySlabRows ? (network.AreaLoad(c.DepthMm)-slab)/slab : -1}).ToArray();
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
                if(result.Bays[bay]==null || (mask&1)!=0 && (bay==0 || result.Bays[bay-1]==null) || (mask&2)!=0 && (bay==result.Bays.Length-1 || result.Bays[bay+1]==null))continue;
                var left=result.Beams[bay];var right=result.Beams[bay+1];var upper=left.Transfer.UpperMembers.Single(m=>m.Name=="Upper right");
                int lc=Case(left,(mask&1)!=0,true),rc=Case(right,true,(mask&2)!=0);
                for(int row=0;row<left.Transfer.RowsMm.Length;row++)
                {
                    trace?.CheckCancellation();
                    if(!result.SlabRowActive(bay,row))continue;
                    double dl=FootDisplacement(left,result.Input.Beams[bay].WidthMm,lc,row,false),dr=FootDisplacement(right,result.Input.Beams[bay+1].WidthMm,rc,row,true);
                    if(CrossBeamSupportOptimizer.Modern(result.Input))
                    {
                        string leftStage=left.Transfer.CaseNames[lc],rightStage=right.Transfer.CaseNames[rc];
                        dl=Enumerable.Range(0,left.Transfer.CaseNames.Length).Where(c=>left.Transfer.CaseNames[c]==leftStage || left.Transfer.CaseNames[c].StartsWith(leftStage+" + X",StringComparison.Ordinal)).Min(c=>FootDisplacement(left,result.Input.Beams[bay].WidthMm,c,row,false));
                        dr=Enumerable.Range(0,right.Transfer.CaseNames.Length).Where(c=>right.Transfer.CaseNames[c]==rightStage || right.Transfer.CaseNames[c].StartsWith(rightStage+" + X",StringComparison.Ordinal)).Min(c=>FootDisplacement(right,result.Input.Beams[bay+1].WidthMm,c,row,true));
                    }
                    var analysis=upper.Analysis.Scale(left.Transfer.UpperScale(false,row)/Math.Max(left.Transfer.RightUpperScales.Max(),left.Transfer.EnteredUpperSpacingMm/1000));
                    double movement=analysis.DeflectionWithAffineOffsetMm(dl,dr,upper.SupportsMm[0],upper.SupportsMm[1]);
                    result.SlabMovementMm=Math.Max(result.SlabMovementMm,movement);
                    left.Transfer.SlabMovementMm=Math.Max(left.Transfer.SlabMovementMm,movement);right.Transfer.SlabMovementMm=Math.Max(right.Transfer.SlabMovementMm,movement);
                }
            }
        }
    }
}
