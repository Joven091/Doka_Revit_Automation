using System;
using SupportVector=EngineeringDashboard.TransferredSlabOptimizer.SupportVector;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    internal sealed class NetworkSeedCompletion
    {
        private int found;
        internal bool Found=>Volatile.Read(ref found)!=0;
        internal void Complete()=>Interlocked.Exchange(ref found,1);
    }
    internal sealed class SpanStockCoverKey : IEquatable<SpanStockCoverKey>
    {
        private readonly double[] values;private readonly int hash;
        internal SpanStockCoverKey(TowerInput input)
        {
            values=new[] {input.LengthMm,input.MaximumConcreteEndOverhangMm,input.StockEndSetbackMm,(double)input.PrimaryMaterial,input.AllowCustomPrimaryLengths ? 1.0 : 0}
                .Concat(input.PrimaryStockLengthsMm.OrderBy(x=>x)).ToArray();
            unchecked{int h=17;foreach(double value in values)h=h*31+value.GetHashCode();hash=h;}
        }
        public override int GetHashCode()=>hash;
        public override bool Equals(object other)=>Equals(other as SpanStockCoverKey);
        public bool Equals(SpanStockCoverKey other)=>other!=null && hash==other.hash && values.SequenceEqual(other.values);
    }
    // Owned weakly by one network geometry cache, including its stock/spacing
    // retries. Covers depend on span and stock policy, not secondary row pitch,
    // receiving width, or the prefix's available leg budget.
    internal sealed class NetworkSpanStockCoverCache
    {
        private readonly Dictionary<SpanStockCoverKey,Lazy<double[][]>> entries=new Dictionary<SpanStockCoverKey,Lazy<double[][]>>();
        internal double[][] Get(TowerInput input)
        {
            input.Trace?.CheckCancellation();
            var frozen=input.CopyForSearch();frozen.StockLengthsMm=(double[])input.StockLengthsMm.Clone();frozen.TransferredSlab=null;
            var key=new SpanStockCoverKey(frozen);Lazy<double[][]> pending;
            lock(entries)
            {
                if(!entries.TryGetValue(key,out pending))
                {
                    if(entries.Count>=64)entries.Clear();
                    entries[key]=pending=new Lazy<double[][]>(()=>
                    {
                        var result=TransferredSlabOptimizer.StockCovers(frozen);
                        frozen.Trace?.CheckCancellation();return result;
                    },LazyThreadSafetyMode.ExecutionAndPublication);
                }
            }
            try {var result=pending.Value;input.Trace?.CheckCancellation();return result;}
            catch
            {
                lock(entries)if(entries.TryGetValue(key,out var current) && ReferenceEquals(current,pending))entries.Remove(key);
                throw;
            }
        }
    }
    // Owned by one network calculation, including its spacing retries. Completed
    // base layouts are immutable: crossing rechecks copy them before changing loads.
    // Scoped provenance from one completed catalogue call. Its source input is
    // the exact search context even when the result holds a terminal prefix copy.
    internal sealed class NetworkCatalogueBound
    {
        internal readonly TransferNetworkInput SourceInput;
        internal readonly TransferNetworkResult Result;
        internal NetworkCatalogueBound(TransferNetworkInput sourceInput,TransferNetworkResult result){SourceInput=sourceInput;Result=result;}
    }
    internal sealed class NetworkReceiverCache
    {
        internal sealed class Entry
        {
            internal TowerLayout Best;
            internal Dictionary<double,TowerLayout> Alternatives;
            internal TowerSearchDiagnostics Diagnostics;
            internal double[] RowRegistry;
            internal int LegCeiling;
            internal bool CompleteFailure,MinimumLegCountProven,WiderCeilingStable,TighterFailureMinimumProven;
            internal int MinimumRequiredLegs;
        }
        private sealed class Record {internal Entry Success,Failure,MinimumFailure;}
        private readonly TransferredSlabOptimizer.ResponseCache<ReceiverSolveKey,Record> entries=new TransferredSlabOptimizer.ResponseCache<ReceiverSolveKey,Record>(8192);
        // Adaptive searches can legitimately select another receiver count at a
        // tighter budget. Retain exact completed outcomes separately from the
        // widest success; bound extra response graphs globally, not per key.
        private readonly TransferredSlabOptimizer.ResponseCache<Tuple<ReceiverSolveKey,int>,Entry> exactSuccesses
            =new TransferredSlabOptimizer.ResponseCache<Tuple<ReceiverSolveKey,int>,Entry>(1024);
        internal bool Get(ReceiverSolveKey key,int ceiling,out Entry value)
        {
            lock(entries)
            {
                value=null;
                entries.TryGetValue(key,out var record);
                // A fully exhausted superset also excludes every smaller ceiling.
                // Preserve this evidence separately: a positive winner above the
                // requested ceiling must not erase a later failed tighter search.
                if(record?.Failure!=null && record.Failure.LegCeiling>=ceiling)
                {value=record.Failure;return true;}
                if(record?.MinimumFailure!=null && record.MinimumFailure.LegCeiling>=ceiling)
                {value=record.MinimumFailure;return true;}
                if(exactSuccesses.TryGetValue(Tuple.Create(key,ceiling),out value))return true;
                value=record?.Success;
                if(value==null || value.LegCeiling<ceiling && !value.MinimumLegCountProven && !value.WiderCeilingStable)return false;
                // A complete nonadaptive winner, or an adaptive winner from its
                // first receiver count, is stable when the ceiling grows: added
                // higher station counts cannot improve its legs-first rank or
                // an already retained width winner. Missing widths still receive
                // their ordinary fixed-width solves in Receivers. This wider-only
                // proof does NOT exclude a tighter adaptive search: capping the
                // stations can make it refine to another receiver count.
                // A checked winner from a superset remains the winner if it fits
                // the requested subset. Only nonadaptive engineer searches prove
                // that every smaller leg tier failed: spreader receiver counts
                // are otherwise refined only after a failed station search.
                if(value.Best.LegCount<=ceiling)return true;
                // Reaching the final adaptive receiver count exhausts counts
                // 3 through 7 at this same station limit as well. Its winner
                // therefore excludes every tighter budget below its legs, but
                // does not determine which count a wider search reaches first.
                if(!value.MinimumLegCountProven && !value.TighterFailureMinimumProven)return false;
                value=new Entry {LegCeiling=ceiling,CompleteFailure=true,MinimumRequiredLegs=value.Best.LegCount,
                    Diagnostics=value.Diagnostics,Alternatives=new Dictionary<double,TowerLayout>()};
                return true;
            }
        }
        internal void Store(ReceiverSolveKey key,Entry value)
        {
            lock(entries)
            {
                if(!entries.TryGetValue(key,out var record))record=new Record();
                if(value.Best==null)
                {
                    if(!value.CompleteFailure || record.Failure!=null && record.Failure.LegCeiling>=value.LegCeiling)return;
                    record.Failure=value;
                }
                else
                {
                    exactSuccesses.Store(Tuple.Create(key,value.LegCeiling),value);
                    if(value.TighterFailureMinimumProven && value.Best.LegCount>0)
                    {
                        int through=Math.Min(value.LegCeiling,value.Best.LegCount-1);
                        if(record.MinimumFailure==null || record.MinimumFailure.LegCeiling<through)
                            record.MinimumFailure=new Entry {CompleteFailure=true,LegCeiling=through,MinimumRequiredLegs=value.Best.LegCount,
                                Diagnostics=value.Diagnostics,Alternatives=new Dictionary<double,TowerLayout>()};
                    }
                    if(record.Success==null || record.Success.LegCeiling<value.LegCeiling)record.Success=value;
                }
                entries.Store(key,record);
            }
        }
    }
    internal sealed class ReceiverSolveKey : IEquatable<ReceiverSolveKey>
    {
        private readonly double[] values;private readonly string profile;private readonly int hash;
        internal ReceiverSolveKey(TowerInput input,TransferNetworkInput network,int beam,bool alternatives,bool layoutIdentity=false,bool shareLegCeilings=false)
        {
            var t=input.TransferredSlab;
            // The network has already selected these slab stocks and feet. With
            // slab-only custom scope, toggling custom search adds no receiver or
            // crossing stock options for this otherwise identical local problem.
            bool fixedSlabs=input.CustomH20Scope==CustomH20LengthScope.SlabSupports
                && (t.LeftBayMm==0 || t.LeftPlan!=null && !t.LeftPlan.IsCustomLength && input.StockLengthsMm.Contains(t.LeftPlan.StockLengthMm))
                && (t.RightBayMm==0 || t.RightPlan!=null && !t.RightPlan.IsCustomLength && input.StockLengthsMm.Contains(t.RightPlan.StockLengthMm));
            var v=new List<double> {beam,!layoutIdentity && alternatives?1:0,(int)input.PrimaryMaterial,input.LengthMm,input.LongitudinalStartMm,input.LegCapacityKn,input.DeflectionDenominator,input.DeflectionLimitMm,
                input.StockEndSetbackMm,input.MaximumConcreteEndOverhangMm,layoutIdentity?0:input.ReceiverFailedThroughStations,layoutIdentity?0:input.EngineerMinimumStations,layoutIdentity?0:input.EngineerStationLimit,layoutIdentity || shareLegCeilings?0:input.NetworkLegCeiling,
                input.ReceiverTransverseOffsetMm,(int)input.CustomH20Scope,network.HasIndividualSpacings ? 0 : network.UpperSpacingMm,network.LocalRowGapMm,
                t.BeamWidthMm,t.BeamLineLoadKnM,t.UpperSpacingMm,t.LocalRowGapMm,t.PlywoodSheetLengthMm,t.PlywoodDeflectionDenominator,
                t.LeftSlabSpacingMm,t.RightSlabSpacingMm,t.LeftBayMm,t.RightBayMm,t.LeftAreaLoadKnM2,t.RightAreaLoadKnM2,t.LeftSlabStartMm??-1,t.LeftSlabEndMm??-1,t.RightSlabStartMm??-1,t.RightSlabEndMm??-1,
                t.BeamCentreOffsetMm,t.FixedCoveredLengthMm??-1,t.FixedRowStartMm??-1,t.FixedRowPhaseMm??double.MinValue,layoutIdentity?0:t.FixedTowerWidthMm??-1};
            foreach(bool flag in new[] {input.EngineerSearch,input.FullLoadingOnly,input.DeferCrossingChecks,input.CoordinateCrossingLayouts,input.IgnoreUplift,
                !fixedSlabs && input.AllowCustomH20Lengths,input.AllowCustomPrimaryLengths,input.AllowCustomBeamLengths,!fixedSlabs && input.AllowCustomSlabLengths,input.AllowLocalShutterExceptions,
                input.Exclude600Standalone,input.PrepareReceiverChecks,input.OptimizePrimaryResponses,input.SeedNetworkSearch,input.PruneUneconomicCandidates,
                input.StagePrimaryChecks,input.DeferTransferLayout,input.SearchByLegTier,input.OptimizeCrossingSearch,
                t.LocalReceivingRows,t.IsNetwork,t.AllowLongerPrimaryCoverage,t.IncludeConcreteEndStrips,t.IncludeMainBeamEndLoads,t.CrossFormwork})v.Add(flag?1:0);
            Action<double[]> array=a=>{v.Add(a?.Length??-1);if(a!=null)v.AddRange(a);};
            array(input.StockLengthsMm);
            // A remote bay can add zero-load entries to the global drawing row
            // registry. Only actual local source rows affect this beam search.
            // Hits with a different registry rebuild the returned row mapping below.
            int[] local=t.LocalReceivingRows && t.SharedRowsMm!=null ? Enumerable.Range(0,t.SharedRowsMm.Length).Where(i=>
                ((t.LeftEffectiveWidthsMm??t.LeftBaselineWidthsMm)?[i]??0)>0 || ((t.RightEffectiveWidthsMm??t.RightBaselineWidthsMm)?[i]??0)>0 || t.ReceivingRowsMm.Contains(t.SharedRowsMm[i])).ToArray() : null;
            Action<double[]> rowArray=a=>array(a==null || local==null ? a : local.Select(i=>a[i]).ToArray());
            rowArray(t.SharedRowsMm);rowArray(t.LeftEffectiveWidthsMm);rowArray(t.RightEffectiveWidthsMm);
            rowArray(t.LeftBaselineWidthsMm);rowArray(t.RightBaselineWidthsMm);array(t.ReceivingRowsMm);
            foreach(var plan in new[] {t.LeftPlan,t.RightPlan})
            {v.Add(plan==null?0:1);if(plan!=null)v.AddRange(new[] {plan.StockLengthMm,plan.EdgeMm,plan.NearFootMm,plan.FarFootMm,plan.IsCustomLength?1.0:0});}
            foreach(var loads in new[] {t.LeftCrossingBands,t.RightCrossingBands})
            {v.Add(loads.Length);foreach(var q in loads)v.AddRange(new[] {q.StartMm,q.EndMm,q.KnPerM});}
            v.Add(t.BeamEdgeLoadsKnM.Length);foreach(var p in t.BeamEdgeLoadsKnM)v.AddRange(new[] {p.PositionMm,p.Kn});
            v.Add(t.StockCovers.Length);foreach(var cover in t.StockCovers)array(cover);
            // Physical network geometry is constant across retries, but retaining
            // it here also makes the identity independently testable. Nonlocal
            // beam pitches do not affect this beam's loads or receiving checks.
            v.Add(network.Beams.Length);foreach(var b in network.Beams)v.AddRange(new[] {b.WidthMm,b.DepthMm,b.BottomElevationMm,b.SpanStartMm,b.SpanLengthMm??-1});
            v.Add(network.Bays.Length);foreach(var b in network.Bays)v.AddRange(new[] {b.WidthMm,b.ThicknessMm,network.AreaLoad(b.ThicknessMm)});
            foreach(var b in network.Bays){v.Add(b.Outline?.Length??0);if(b.Outline!=null)foreach(var p in b.Outline)v.AddRange(new[] {p.X,p.Y});}
            v.Add(network.CrossingBeams.Length);for(int i=0;i<network.CrossingBeams.Length;i++)
            {var c=network.CrossingBeams[i];v.AddRange(new[] {(double)c.BayNumber,c.PositionMm,c.WidthMm,c.DepthMm,c.BottomElevationMm,(double)c.SupportMode,!input.LocalNetworkSearch || c.BayNumber==beam || c.BayNumber==beam+1 ? network.CrossSpacing(i) : 0,network.AreaLoad(c.DepthMm)});}
            values=v.ToArray();profile=t.PlywoodProfileId;
            unchecked{int h=17;foreach(double value in values)h=h*31+value.GetHashCode();hash=h*31+(profile?.GetHashCode()??0);}
        }
        public override int GetHashCode()=>hash;
        public override bool Equals(object other)=>Equals(other as ReceiverSolveKey);
        public bool Equals(ReceiverSolveKey other)=>other!=null && hash==other.hash && profile==other.profile && values.SequenceEqual(other.values);
    }
    // Exact immutable identity of a solved base receiver. Search ceilings and
    // the global drawing registry do not alter the response at identical actual
    // geometry/loads. Partial-prefix caches retain only pass/fail, never drawings.
    internal sealed class ReceiverLayoutKey : IEquatable<ReceiverLayoutKey>
    {
        private readonly ReceiverSolveKey input;private readonly double[] values;private readonly int hash;
        internal ReceiverLayoutKey(ReceiverSolveKey input,TowerLayout layout)
        {
            this.input=input;var t=layout.Transfer;
            var v=new List<double> {layout.WidthMm,layout.TransverseOffsetMm,layout.Rotated?1:0,layout.LengthMm,layout.ConcreteEndOverhangMm,
                t.LowerStockStartMm,t.LowerStockLengthMm,t.EngineerReceiverCount,t.HasCustomPlywoodJoints?1:0};
            v.Add(layout.PositionsMm.Length);v.AddRange(layout.PositionsMm);
            v.Add(layout.Joined.Length);v.AddRange(layout.Joined.Select(x=>x?1.0:0));
            v.Add(layout.StockPlan.Pieces.Length);foreach(var p in layout.StockPlan.Pieces)v.AddRange(new[] {p.StartMm,p.StockLengthMm,p.FirstSupport,p.LastSupport,p.IsCustomLength?1.0:0});
            foreach(var rows in new[] {t.RowsMm,t.TributaryWidthsMm,t.LeftUpperScales,t.RightUpperScales})
            {v.Add(rows?.Length??-1);if(rows!=null)v.AddRange(rows);}
            v.Add(t.PhysicalRowCount);for(int i=0;i<t.PhysicalRowCount;i++)v.Add(t.PhysicalRowPosition(i));
            values=v.ToArray();unchecked{int h=input.GetHashCode();foreach(double value in values)h=h*31+value.GetHashCode();hash=h;}
        }
        public override int GetHashCode()=>hash;
        public override bool Equals(object obj)=>Equals(obj as ReceiverLayoutKey);
        public bool Equals(ReceiverLayoutKey other)=>other!=null && hash==other.hash && input.Equals(other.input) && values.SequenceEqual(other.values);
    }
    internal sealed class NetworkPrefixCache
    {
        internal sealed class Entry {internal bool Passed;internal TowerSearchDiagnostics Diagnostics;}
        private readonly TransferredSlabOptimizer.ResponseCache<CheckedPrefixKey,Entry> entries=new TransferredSlabOptimizer.ResponseCache<CheckedPrefixKey,Entry>(4096);
        internal bool Get(CheckedPrefixKey key,out Entry value)=>Get(key,false,out value);
        internal bool Get(CheckedPrefixKey key,bool terminal,out Entry value)
        {lock(entries)return entries.TryGetValue(key,out value) && (!terminal || !value.Passed);}
        internal void Store(CheckedPrefixKey key,Entry value,bool terminal=false)
        {if(!terminal || !value.Passed)lock(entries)entries.Store(key,value);}
    }
    internal sealed class CheckedPrefixKey : IEquatable<CheckedPrefixKey>
    {
        private readonly object[] beams;private readonly double[] values;private readonly int hash;
        internal CheckedPrefixKey(TowerInput common,TransferNetworkResult prefix)
        {
            beams=prefix.Beams.Select(b=>common.LocalNetworkSearch ? (object)b.ReceiverIdentity??b : b).ToArray();var n=prefix.Input;
            bool fixedCatalogueSlabs=common.CustomH20Scope==CustomH20LengthScope.SlabSupports
                && prefix.Bays.All(p=>p==null || !p.IsCustomLength && common.StockLengthsMm.Contains(p.StockLengthMm));
            var v=new List<double> {prefix.SlabCoveredLengthMm,prefix.RowPhaseMm,common.AllowCustomH20Lengths && !fixedCatalogueSlabs?1:0,(int)common.CustomH20Scope,
                common.AllowLocalShutterExceptions?1:0,common.EngineerJunctionExtraTiers,common.EngineerMinimumStations,common.EngineerStationLimit,
                common.LegCapacityKn,common.DeflectionDenominator,common.IgnoreUplift?1:0};
            for(int i=0;i<n.Beams.Length;i++)v.Add(n.BeamSpacing(i));
            for(int i=0;i<n.Bays.Length;i++)
            {v.Add(n.BaySpacing(i));var p=prefix.Bays[i];v.Add(p==null?0:1);if(p!=null)v.AddRange(new[] {p.StockLengthMm,p.EdgeMm,p.NearFootMm,p.FarFootMm});}
            for(int i=0;i<n.CrossingBeams.Length;i++)v.Add(n.CrossSpacing(i));
            values=v.ToArray();unchecked{int h=17;foreach(var b in beams)h=h*31+b.GetHashCode();foreach(double value in values)h=h*31+value.GetHashCode();hash=h;}
        }
        public override int GetHashCode()=>hash;
        public override bool Equals(object other)=>Equals(other as CheckedPrefixKey);
        public bool Equals(CheckedPrefixKey other)=>other!=null && hash==other.hash && beams.SequenceEqual(other.beams) && values.SequenceEqual(other.values);
    }
    public sealed class UpperTransferPlan
    {
        public const string SiteArrangement = "Slab H20 feet use equal setbacks from both concrete faces, giving equal stock-end overhangs. Repeat the same arrangement at every slab H20 row in a bay. Uniform slab loading gives equal foot reactions; receiving-member and tower-leg reactions remain individually checked for every loading stage. The specified maximum row spacing, bearings and end allowances require site control.";
        public double StockLengthMm, EdgeMm, NearFootMm, FarFootMm;
        public bool IsCustomLength;
        // Custom fallback keeps edge strips closest to 200 mm, without changing primary/lower stocks.
        public static double[] SlabLengths(double bay,double[] stocks,double maximumEndStripMm,bool allowCustom=false)
        {
            if(!TowerBeamAnalysis.Finite(maximumEndStripMm) || maximumEndStripMm<200)
                throw new ArgumentException("The slab plywood end-strip limit must be at least 200 mm.");
            var fitting=stocks.Where(stock=>(bay-stock)/2>=200 && (bay-stock)/2<=maximumEndStripMm).ToArray();
            if(allowCustom)
            {
                int first=(int)Math.Ceiling(Math.Max(25,bay-2*maximumEndStripMm)/25),last=(int)Math.Floor(Math.Min(50000,bay-400)/25);
                return fitting.Concat(Enumerable.Range(first,Math.Max(0,last-first+1)).Select(x=>x*25.0)).Distinct().OrderBy(x=>x).ToArray();
            }
            if(fitting.Length>0)return fitting;
            double custom=Math.Floor((bay-400)/25)*25;
            return custom>0 && (bay-custom)/2<=maximumEndStripMm ? new[] {custom} : new double[0];
        }
        public UpperTransferPlan Reverse() => new UpperTransferPlan {StockLengthMm=StockLengthMm,EdgeMm=EdgeMm,NearFootMm=FarFootMm,FarFootMm=NearFootMm,IsCustomLength=IsCustomLength};
        public void Validate(double bay,double[] stocks,double maximumEndStripMm,bool allowCustom=false)
        {
            if(new[] {StockLengthMm,EdgeMm,NearFootMm,FarFootMm}.Any(x=>!TowerBeamAnalysis.Finite(x)) || !SlabLengths(bay,stocks,maximumEndStripMm,allowCustom).Contains(StockLengthMm) || IsCustomLength!=!stocks.Contains(StockLengthMm)
                || Math.Abs(bay-StockLengthMm-2*EdgeMm)>1e-7 || EdgeMm<200 || EdgeMm>maximumEndStripMm || NearFootMm<Math.Max(250,EdgeMm+150)
                || FarFootMm<Math.Max(250,EdgeMm+150) || bay-NearFootMm-FarFootMm<300) throw new ArgumentException("Invalid shared upper H20 stock or foot positions.");
            if(Math.Abs(NearFootMm-FarFootMm)>1e-7)throw new ArgumentException("Slab H20 supports must have equal setbacks from both concrete faces and equal stock-end overhangs.");
        }
    }
    public class TransferBeamRow : INotifyPropertyChanged
    {
        public string RevitSource {get;set;}
        public string Label {get;set;}
        private double spanStart;
        private double? spanLength;
        // Missing length in older drawings means the shared beam-length setting.
        public double SpanStartMm {get=>spanStart;set {spanStart=value;GeometryChanged(nameof(SpanStartMm));}}
        public double? SpanLengthMm {get=>spanLength;set {spanLength=value;GeometryChanged(nameof(SpanLengthMm));}}
        public bool ShouldSerializeSpanStartMm()=>SpanStartMm!=0;
        public bool ShouldSerializeSpanLengthMm()=>SpanLengthMm.HasValue;
        internal double SpanLength(double sharedLength)=>SpanLengthMm??sharedLength;
        internal double SpanEnd(double sharedLength)=>SpanStartMm+SpanLength(sharedLength);
        internal void ValidateSpan(double sharedLength)
        {
            if(!TowerBeamAnalysis.Finite(SpanStartMm) || !TowerBeamAnalysis.Finite(SpanLength(sharedLength)) || SpanStartMm<0 || SpanEnd(sharedLength)>50000 || SpanLength(sharedLength)<=0)
                throw new ArgumentException("Each parallel beam needs a valid start and a positive length within the 50,000 mm drawing limit.");
        }
        private double maximumSecondarySpacing;
        [System.Xml.Serialization.XmlIgnore]
        public double MaximumSecondarySpacingMm=>maximumSecondarySpacing;
        internal void SetPlywoodSpacing(double value)
        {if(maximumSecondarySpacing==value)return;maximumSecondarySpacing=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(MaximumSecondarySpacingMm)));}
        private double depth=1750,width=1100;
        private double? bottom;
        public double BottomElevationMm {get=>bottom??-DepthMm;set {bottom=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(BottomElevationMm)));}}
        public double DepthMm {get=>depth;set {depth=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(DepthMm)));PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(BottomElevationMm)));}}
        public double WidthMm {get=>width;set {width=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(WidthMm)));}}
        public void SetDepthKeepingTopMm(double value) {double top=BottomElevationMm+DepthMm;DepthMm=value;BottomElevationMm=top-value;}
        public event PropertyChangedEventHandler PropertyChanged;
        protected void GeometryChanged(string name)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
    public sealed class IndependentBeamRow : TransferBeamRow
    {
        private double startX,startY,length=3000;private bool vertical;
        public double StartXmm {get=>startX;set {startX=value;GeometryChanged(nameof(StartXmm));}}
        public double StartYmm {get=>startY;set {startY=value;GeometryChanged(nameof(StartYmm));}}
        public double LengthMm {get=>length;set {length=value;GeometryChanged(nameof(LengthMm));}}
        public bool IsVertical {get=>vertical;set {vertical=value;GeometryChanged(nameof(IsVertical));}}
        [System.Xml.Serialization.XmlIgnore] public double EndXmm=>StartXmm+(IsVertical ? 0 : LengthMm);
        [System.Xml.Serialization.XmlIgnore] public double EndYmm=>StartYmm+(IsVertical ? LengthMm : 0);
        internal void Validate()
        {
            if(new[] {StartXmm,StartYmm,EndXmm,EndYmm,WidthMm,DepthMm,LengthMm,BottomElevationMm}.Any(x=>!TowerBeamAnalysis.Finite(x)) || Math.Abs(StartXmm)>1000000 || Math.Abs(StartYmm)>1000000 || WidthMm<=0 || WidthMm>10000 || DepthMm<=0 || DepthMm>10000 || LengthMm<=0 || LengthMm>30000)throw new ArgumentException("Independent beams require a valid start point, positive width/depth and a length up to 30,000 mm.");
        }
        internal IndependentBeamRow Copy()=>new IndependentBeamRow {WidthMm=WidthMm,DepthMm=DepthMm,BottomElevationMm=BottomElevationMm,StartXmm=StartXmm,StartYmm=StartYmm,LengthMm=LengthMm,IsVertical=IsVertical};
    }
    public sealed class TransferBayRow : INotifyPropertyChanged
    {
        public string RevitSource {get;set;}
        public string Label {get;set;}
        // Actual imported outline, with Y measured from this bay's near concrete face.
        public ImportPoint[] Outline {get;set;}
        private double maximumSecondarySpacing;
        [System.Xml.Serialization.XmlIgnore]
        public double MaximumSecondarySpacingMm=>maximumSecondarySpacing;
        internal void SetPlywoodSpacing(double value)
        {if(maximumSecondarySpacing==value)return;maximumSecondarySpacing=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(MaximumSecondarySpacingMm)));}
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
        public string RevitSource {get;set;}
        private double maximumSecondarySpacing;
        [System.Xml.Serialization.XmlIgnore]
        public double MaximumSecondarySpacingMm=>maximumSecondarySpacing;
        internal void SetPlywoodSpacing(double value)
        {if(maximumSecondarySpacing==value)return;maximumSecondarySpacing=value;Notify(nameof(MaximumSecondarySpacingMm));}
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
        public void SetDepthKeepingTopMm(double value) {double top=BottomElevationMm+DepthMm;DepthMm=value;BottomElevationMm=top-value;}
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
    public sealed class TransferNetworkInput
    {
        public const int MaximumBeamCount=15;
        public TransferBeamRow[] Beams;
        internal double ReferenceLengthMm;
        public TransferBayRow[] Bays;
        public TransferCrossingBeamRow[] CrossingBeams = new TransferCrossingBeamRow[0];
        public double UpperSpacingMm, LiveKnM2, FormworkKnM2;
        public double PlywoodSheetLengthMm;
        internal double? RequestedUpperSpacingMm;
        // Historical setting-out fixtures can replay the former slab/crossing load path.
        internal bool UseCrossingSlabLedgers=true;
        internal double LocalRowGapMm;
        internal double RowGapMm=>LocalRowGapMm>0 ? LocalRowGapMm : UpperSpacingMm;
        public double[] BeamSpacingsMm, SlabSpacingsMm, CrossingSpacingsMm;
        // Zero marks a bay whose plywood has no permitted nominal spacing. Only
        // the explicitly reported separate-formwork fallback may exclude it.
        internal double[] LegacyBayPlywoodLimitsMm;
        public string PlywoodProfileId;
        public int PlywoodDeflectionDenominator;
        public bool HasIndividualSpacings=>BeamSpacingsMm!=null;
        internal bool HasIndividualSpans=>Beams.Any(b=>b.SpanStartMm!=0 || b.SpanLengthMm.HasValue);
        internal bool DifferentSpans(double length)=>Beams.Any(b=>b.SpanStartMm!=0 || Math.Abs(b.SpanLength(length)-length)>1e-7);
        internal double SharedStart=>Beams.Max(b=>b.SpanStartMm);
        internal double SharedEnd(double length)=>Beams.Min(b=>b.SpanEnd(length));
        internal double SharedRowStart(double length,double cover)=>DifferentSpans(length) ? (SharedStart+SharedEnd(length)-cover)/2 : (length-cover)/2;
        internal double Extent(double length)=>Beams.Max(b=>b.SpanEnd(length));
        internal double BayStart(int bay,double length)=>Bays[bay].Outline?.Min(p=>p.X)??Math.Max(Beams[bay].SpanStartMm,Beams[bay+1].SpanStartMm);
        internal double BayEnd(int bay,double length)=>Bays[bay].Outline?.Max(p=>p.X)??Math.Min(Beams[bay].SpanEnd(length),Beams[bay+1].SpanEnd(length));
        internal bool BayContains(int bay,double x,double y)=>SlabFootprint.Contains(Bays[bay].Outline,x,y);
        public double BeamSpacing(int beam)=>BeamSpacingsMm?[beam]??UpperSpacingMm;
        public double BaySpacing(int bay)=>SlabSpacingsMm?[bay]??UpperSpacingMm;
        public double CrossSpacing(int cross)=>CrossingSpacingsMm?[cross]??UpperSpacingMm;
        // A closer row-spacing retry does not reduce the slab plywood's transverse allowance.
        public double MaximumSlabEndStripMm(int bay)=>PlywoodSpacing.SlabEndStripLimitMm(AreaLoad(Bays[bay].ThicknessMm),PlywoodProfileId,PlywoodDeflectionDenominator);
        internal TransferNetworkInput WithSpacings(int steps=0,int beamCount=0)
        {
            int count=beamCount==0 ? Beams.Length : beamCount;
            Func<double,double> closer=x=>PlywoodSpacing.StandardSpacingsMm.SkipWhile(v=>v!=x).Skip(steps).DefaultIfEmpty(250).First();
            var crossIndices=Enumerable.Range(0,CrossingBeams.Length).Where(i=>CrossingBeams[i].BayNumber<count).ToArray();
            var beams=BeamSpacingsMm?.Take(count).Select(closer).ToArray();
            return new TransferNetworkInput {ReferenceLengthMm=ReferenceLengthMm,Beams=Beams.Take(count).ToArray(),Bays=Bays.Take(count-1).ToArray(),
                CrossingBeams=crossIndices.Select(i=>CrossingBeams[i]).ToArray(),BeamSpacingsMm=beams,
                SlabSpacingsMm=SlabSpacingsMm?.Take(count-1).Select(closer).ToArray(),
                LegacyBayPlywoodLimitsMm=LegacyBayPlywoodLimitsMm?.Take(count-1).ToArray(),
                CrossingSpacingsMm=CrossingSpacingsMm==null ? null : crossIndices.Select(i=>closer(CrossingSpacingsMm[i])).ToArray(),
                PlywoodProfileId=PlywoodProfileId,PlywoodDeflectionDenominator=PlywoodDeflectionDenominator,
                UpperSpacingMm=beams?.Min()??UpperSpacingMm,RequestedUpperSpacingMm=RequestedUpperSpacingMm??UpperSpacingMm,
                UseCrossingSlabLedgers=UseCrossingSlabLedgers,LocalRowGapMm=LocalRowGapMm,PlywoodSheetLengthMm=PlywoodSheetLengthMm,CustomLoads=CustomLoads,LiveKnM2=LiveKnM2,FormworkKnM2=FormworkKnM2};
        }
        private readonly ConcurrentDictionary<string,Lazy<double[][]>> slabRowsCache=new ConcurrentDictionary<string,Lazy<double[][]>>();
        internal double[][] SlabRows(double length,double cover)
        {
            string key=length.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+cover.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+string.Join(";",Enumerable.Range(0,Bays.Length).Select(i=>BaySpacing(i).ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            return slabRowsCache.GetOrAdd(key,k=>new Lazy<double[][]>(()=>BuildSlabRows(length,cover),LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        private readonly ConcurrentDictionary<string,CrossingSlabSupportLayout> crossSlabLayouts=new ConcurrentDictionary<string,CrossingSlabSupportLayout>();
        internal CrossingSlabSupportLayout CrossSlabLayout(double length,double cover,int bay)
            =>crossSlabLayouts.GetOrAdd(length.ToString("R")+"|"+cover.ToString("R")+"|"+bay,k=>
            {
                var adjacent=new double[0];double edge=SharedRowStart(length,cover);
                for(int i=0;i<bay;i++)
                {
                    var rows=UseCrossingSlabLedgers && CrossingBeams.Any(c=>c.BayNumber==i+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows)
                        ? CrossSlabLayout(length,cover,i).Rows : PlywoodSpacing.CompatibleSheetRows(edge,edge+cover,BaySpacing(i),adjacent);
                    adjacent=adjacent.Concat(rows).Distinct().OrderBy(x=>x).ToArray();
                }
                return CrossingSlabSupportLayout.Build(this,bay,edge,edge+cover,adjacent);
            });
        private double[][] BuildSlabRows(double length,double cover)
        {
            double edge=SharedRowStart(length,cover);
            var result=new List<double[]>();var adjacent=new double[0];
            for(int i=0;i<Bays.Length;i++)
            {
                var rows=UseCrossingSlabLedgers && CrossingBeams.Any(c=>c.BayNumber==i+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows) ? CrossSlabLayout(length,cover,i).Rows : PlywoodSpacing.CompatibleSheetRows(edge,edge+cover,BaySpacing(i),adjacent);
                if(rows.Length<2)rows=PlywoodSpacing.CompatibleSheetRows(edge,edge+cover,BaySpacing(i),adjacent);
                result.Add(rows);adjacent=adjacent.Concat(rows).Distinct().OrderBy(x=>x).ToArray();
            }
            return result.ToArray();
        }
        private void ValidateSpacings()
        {
            if(!HasIndividualSpacings)
            {if(SlabSpacingsMm!=null || CrossingSpacingsMm!=null)throw new ArgumentException("Incomplete element spacing settings.");return;}
            if(PlywoodSheetLengthMm!=PlywoodSpacing.SheetLengthMm || SlabSpacingsMm==null || CrossingSpacingsMm==null
                || BeamSpacingsMm.Length!=Beams.Length || SlabSpacingsMm.Length!=Bays.Length || CrossingSpacingsMm.Length!=CrossingBeams.Length)
                throw new ArgumentException("Each beam, slab bay and crossing needs its own sheet spacing.");
            var profile=PlywoodCatalog.FromId(PlywoodProfileId);
            Action<double,double> check=(spacing,depth)=>
            {
                if(!PlywoodSpacing.StandardSpacingsMm.Contains(spacing)
                    || spacing>PlywoodSpacing.CalculateForSheet(profile,AreaLoad(depth),PlywoodDeflectionDenominator).MaximumSpacingMm)
                    throw new ArgumentException("An element spacing exceeds its plywood load limit.");
            };
            for(int i=0;i<Beams.Length;i++)check(BeamSpacing(i),Beams[i].DepthMm);
            for(int i=0;i<Bays.Length;i++)check(BaySpacing(i),CrossingBeams.Where(c=>c.BayNumber==i+1 && c.SupportMode==CrossBeamSupportMode.LegacySlabRows).Select(c=>c.DepthMm).Concat(new[] {Bays[i].ThicknessMm}).Max());
            for(int i=0;i<CrossingBeams.Length;i++)check(CrossSpacing(i),CrossingBeams[i].DepthMm);
        }
        public bool CustomLoads;
        public double AreaLoad(double thickness) => 25*thickness/1000+(CustomLoads ? LiveKnM2+FormworkKnM2 : BeamCalculatorViewModel.HeapingLoad(25*thickness/1000)+.75+.4);
        public void Validate(TowerInput common)
        {
            ReferenceLengthMm=common.LengthMm;
            PrimaryBeamProfile.For(common.PrimaryMaterial);
            if(!TowerBeamAnalysis.Finite(LocalRowGapMm) || LocalRowGapMm<0 || LocalRowGapMm>0 && (LocalRowGapMm<100 || LocalRowGapMm>UpperSpacingMm))throw new ArgumentException("Invalid actual local secondary gap.");
            if(Beams==null || Bays==null || Beams.Length<2 || Beams.Length>MaximumBeamCount || Bays.Length!=Beams.Length-1) throw new ArgumentException("Use 2–15 beams with one slab bay between each adjacent pair.");
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
            foreach(var b in Beams)b.ValidateSpan(common.LengthMm);
            if(SharedEnd(common.LengthMm)-SharedStart<=0)throw new ArgumentException("The parallel beams need an overlapping clear span for shared slab support.");
            foreach(var b in Bays)if(b.Outline!=null && (!SlabFootprint.ValidEndCutbacks(b.Outline,MaximumSlabEndStripMm(Array.IndexOf(Bays,b))) || b.Outline.Any(p=>p.X<0 || p.X>50000 || p.Y< -1 || p.Y>b.WidthMm+1)))throw new ArgumentException("Invalid slab outline: use a rectangular bay with supported corner cutbacks.");
            foreach(var c in CrossingBeams)
                if(new[] {c.BayNumber-1,c.BayNumber}.Any(i=>c.PositionMm-c.WidthMm/2<Beams[i].SpanStartMm-1e-7 || c.PositionMm+c.WidthMm/2>Beams[i].SpanEnd(common.LengthMm)+1e-7))throw new ArgumentException("A cross beam must meet the actual span of both adjacent beams.");
            ValidateSpacings();
            new TransferredSlabInput {PlywoodSheetLengthMm=PlywoodSheetLengthMm,BeamWidthMm=Beams[0].WidthMm,BeamLineLoadKnM=AreaLoad(Beams[0].DepthMm)*Beams[0].WidthMm/1000,UpperSpacingMm=UpperSpacingMm,RightBayMm=Bays[0].WidthMm,RightAreaLoadKnM2=AreaLoad(Bays[0].ThicknessMm)}.Validate(common);
        }
    }
    public sealed class TransferNetworkResult
    {
        public TransferNetworkInput Input;
        public TowerLayout[] Beams;
        public UpperTransferPlan[] Bays;
        public CrossBeamSupportResult[] CrossSupports=new CrossBeamSupportResult[0];
        private SystemLoadingStage systemGoverningStage,displayStage;
        public SystemLoadingStage SystemGoverningStage=>systemGoverningStage??(systemGoverningStage=SystemLoadingStage.Governing(this));
        public SystemLoadingStage DisplayStage {get=>displayStage??SystemGoverningStage;set=>displayStage=value;}
        public double[] BeamCentresMm;
        public double[] SlabRowsMm=>Beams[0].Transfer.SharedSlabRowsMm??(Beams[0].Transfer.SlabRowIndices==null ? Beams[0].Transfer.RowsMm : Beams[0].Transfer.SlabRowIndices.Select(i=>Beams[0].Transfer.RowsMm[i]).ToArray());
        public int SlabBeamRow(int beam,int row)=>Beams[beam].Transfer.SlabSourceRowIndices?[row]??Beams[beam].Transfer.SlabRowIndices?[row]??row;
        public bool SlabRowActive(int bay,int row)
        {
            if(Bays[bay]==null)return false;
            double station=SlabRowsMm[row];var upper=Bays[bay];
            if(!Input.BayContains(bay,station-40,upper.EdgeMm) || !Input.BayContains(bay,station+40,Input.Bays[bay].WidthMm-upper.EdgeMm))return false;
            var t=Beams[bay].Transfer;int receiver=SlabBeamRow(bay,row);
            bool active=t.RightSlabSourceScales!=null ? t.RightSlabSourceScales[row]>0 : !Input.HasIndividualSpacings || receiver>=0 && t.RightUpperScales[receiver]>0;
            return active && !Input.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && c.BayNumber==bay+1 && Math.Abs(SlabRowsMm[row]-c.PositionMm)<c.WidthMm/2+40-1e-7);
        }
        public int SlabRowCount(int bay)=>Bays[bay]==null ? 0 : Enumerable.Range(0,SlabRowsMm.Length).Count(r=>SlabRowActive(bay,r));
        public double SlabMovementMm, SlabLoadKn, SlabCoveredLengthMm, RowPhaseMm;
        public string FullTransferFailureDetails {get;internal set;}="";
        public string[] SeparateSlabReasons {get;internal set;}=new string[0];
        public string SeparateSlabExplanation=>string.Join("\n\n",SeparateSlabReasons.Where(r=>!string.IsNullOrEmpty(r)));
        public string SeparateSlabExplanationForBay(int bay)=>bay>=0 && bay<SeparateSlabReasons.Length ? SeparateSlabReasons[bay]??"" : "";
        public int Tested;
        public bool SearchTimeLimitReached {get;internal set;}
        public int LegCount=>Beams.Sum(b=>b.LegCount)+CrossSupports.Sum(c=>c.Towers?.LegCount??0);
        private IEnumerable<TowerLayout> AllTowers=>Beams.Concat(CrossSupports.Select(c=>c.Towers).Where(t=>t!=null));
        public int Standalone600Count=>AllTowers.Sum(b=>b.Standalone600Count);
        public int BracedBayCount=>AllTowers.Sum(b=>b.BayCount);
        public double StandaloneBracePenalty=>AllTowers.Sum(b=>b.StandaloneBracePenalty);
        public double CantileverImbalanceMm=>AllTowers.Sum(b=>b.CantileverImbalanceMm);
        public double BraceLocationPenalty=>AllTowers.Sum(b=>b.BraceLocationPenalty);
        public int SeparateSlabCount=>Bays.Count(b=>b==null);
        public bool RequiresSeparateSlabFormwork=>SeparateSlabCount>0;
        public string SeparateSlabNotice=>!RequiresSeparateSlabFormwork ? "" : "Separate slab formwork required: "+string.Join(", ",Bays.Select((b,i)=>b==null ? "S"+(i+1) : null).Where(x=>x!=null))+". Its supports, timber and design are excluded from this result; these bays transfer no slab or shutter load to the calculated beams. Legacy crossing loads are excluded; dedicated cross-beam formwork is calculated separately.";
        public int UpperFootCount=>Enumerable.Range(0,Bays.Length).Sum(b=>SlabRowCount(b)*2);
        public double TimberMm=>Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.PhysicalRowCount*b.Transfer.LowerStockLengthMm)+Enumerable.Range(0,Bays.Length).Sum(b=>(Bays[b]?.StockLengthMm??0)*SlabRowCount(b))+CrossSupports.Sum(c=>c.TimberMm);
        public int WS10Count=>Beams.Sum(b=>b.StockPlan.WS10Count)+CrossSupports.Sum(c=>c.WS10Count);
        public double WS10LengthMm=>Beams.Where(b=>b.Primary.IsWS10).Sum(b=>b.StockPlan.PrimaryLengthMm)+CrossSupports.Sum(c=>c.WS10LengthMm);
        public double MaterialMm=>TimberMm+WS10LengthMm;
        public double EconomyScoreMm=>LegCount*TowerOptimizer.TowerLegTimberEquivalentMm+UpperFootCount*TowerOptimizer.UpperFootTimberEquivalentMm+MaterialMm;
        public double SymmetryErrorMm=>AllTowers.Sum(b=>b.SymmetryErrorMm)+CrossSupports.Where(c=>c.Towers==null && Beams.Any(b=>b.EngineerSearch)).Sum(c=>c.Members.Select(m=>m.SupportsMm.Length<2 ? 0 : Math.Abs(m.SupportsMm.First()-(m.LengthMm-m.SupportsMm.Last()))).DefaultIfEmpty().Max());
        public int H20Count=>Beams.Sum(b=>b.StockPlan.TotalH20Count+b.Transfer.PhysicalRowCount)+Enumerable.Range(0,Bays.Length).Sum(SlabRowCount)+CrossSupports.Sum(c=>c.H20Count);
        public string SecondarySpacingNotice=>Input.HasIndividualSpacings ? string.Join("; ",Input.BeamSpacingsMm.Select((x,i)=>$"B{i+1} nominal beam {x:0} mm").Concat(Input.SlabSpacingsMm.Select((x,i)=>$"S{i+1} nominal slab {x:0} mm")).Concat(Input.CrossingSpacingsMm.Select((x,i)=>$"X{i+1} secondary spacing limit {x:0} mm")))+". Actual local gaps can be shorter to align slab feet and sheet joints; effective design tributary widths retain each member’s nominal minimum and local load increments." : Input.RequestedUpperSpacingMm.HasValue ? System.FormattableString.Invariant($"Secondary spacing {Input.UpperSpacingMm:0.###} mm, within entered maximum {Input.RequestedUpperSpacingMm.Value:0.###} mm; ") + (Input.PlywoodSheetLengthMm>0 ? "closer listed sheet spacing for support/member checks." : "nominal design spacing; actual local gaps can be shorter for junction clearance.") : "";
        public double PeakLegKn=>Math.Max(Beams.Max(b=>b.MaxLegKn),CrossSupports.Select(c=>c.Towers?.MaxLegKn??0).DefaultIfEmpty().Max());
        public string Summary=>(SecondarySpacingNotice.Length==0 ? "" : SecondarySpacingNotice+"\n")+$"{Beams.Length} beams · {Bays.Length} slab bays · {LegCount} tower legs\n{H20Count} H20s{(WS10Count>0 ? $" · {WS10Count} WS10s" : "")} · {TimberMm/1000:0.00} m timber{(WS10Count>0 ? $" · {WS10LengthMm/1000:0.00} m WS10" : "")} · peak leg {PeakLegKn:0.00} kN\nSlab movement {SlabMovementMm:0.00} mm · "+(SearchTimeLimitReached ? "best checked solution found within search time limit" : "best found in bounded search")+(RequiresSeparateSlabFormwork ? "\n"+SeparateSlabNotice : "")+(Beams.Any(b=>b.Transfer.ColumnSupportLoads.Length>0) ? "\nUncovered longitudinal end strips: external column-side supports; capacity requires separate verification." : "");
        public string LayoutDiagnostics=> $"Loading configuration: {SystemGoverningStage.Name} · mean category score {SystemGoverningStage.Score*100:0.###}% (bending {SystemGoverningStage.Bending*100:0.###}%, shear {SystemGoverningStage.Shear*100:0.###}%, deflection {SystemGoverningStage.Deflection*100:0.###}%, legs {SystemGoverningStage.LegLoad*100:0.###}%)\n"+string.Join("\n",CrossSupports.Select(c=>c.Schedule))+"\n"+System.FormattableString.Invariant($"Shared slab support coverage={SlabCoveredLengthMm:0.###} mm; physical slab length={Beams[0].LengthMm:0.###} mm; row phase={RowPhaseMm:0.###} mm; rows={SlabRowsMm.Length}; upper feet={UpperFootCount}; relative economy={EconomyScoreMm/1000:0.###} m timber equivalent; symmetry error={SymmetryErrorMm:0.###} mm")
            +(SecondarySpacingNotice.Length==0 ? "" : "\n"+SecondarySpacingNotice)
            +System.FormattableString.Invariant($"\nEconomic preference (not supplier prices): tower leg = {TowerOptimizer.TowerLegTimberEquivalentMm/1000:0.###} m timber; upper foot = {TowerOptimizer.UpperFootTimberEquivalentMm/1000:0.###} m timber. Lower score is a material-economy preference; production first ranks legs, tower grouping, brace preference and symmetry.\nBalanced stock/support patterns are protected in the bounded search; linked bays share frames. Uncovered longitudinal main-beam end-strip loads are assigned to external column-side supports; their capacity requires separate verification.\nPrimary design model: UDL derived from beam/slab loads per metre; crossing loads stay over their physical footprints. No secondary-row point loads on primaries.")
            +"\n"+string.Join("\n",Beams.Select((b,i)=>b.Transfer.PrimaryPlan==null ? $"B{i+1} primary model: {b.Transfer.PrimaryLoadBasis}" : $"B{i+1} primary model: "+b.Transfer.PrimaryPlan.SelectedSummary))
            +"\n"+string.Join("\n",Beams.Select((b,i)=>System.FormattableString.Invariant($"B{i+1}: legs={b.LegCount}; shared frames={b.SharedFrameCount}; beam-only secondary rows={b.Transfer.BeamOnlyRowCount}; symmetry error={b.SymmetryErrorMm:0.###} mm; primary coverage={b.StockPlan.Pieces.Sum(p=>p.StockLengthMm):0.###} mm; end allowance={b.ConcreteEndOverhangMm:0.###} mm; lower H20={b.Transfer.LowerStockLengthMm:0.###} mm; secondary midpoint={b.Transfer.LowerStockStartMm+b.Transfer.LowerStockLengthMm/2:0.###} mm; standalone 600={b.Standalone600Count}; braced bays={b.BayCount}; brace preference={b.StandaloneBracePenalty:0.###} mm; beam load={b.Transfer.BeamLoadKn:0.###} kN; slab load={b.Transfer.SlabLoadKn:0.###} kN; primary runs=")+string.Join(", ",b.StockPlan.Pieces.Select(p=>p.StockLengthMm.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)))+(b.EngineerSearch ? $"; search seed={b.SearchInitialLegsPerLine} legs per line at {b.SearchAssumedLegKn:0.###} kN/leg (before final crossing allocation)" : "")+(string.IsNullOrEmpty(b.CentringNotice) ? "" : "; "+b.CentringNotice)))
            +(RequiresSeparateSlabFormwork ? "\n"+SeparateSlabExplanation : "")
            +"\n"+string.Join("\n",Bays.Select((b,i)=>b==null ? $"S{i+1}: separate slab formwork required (not calculated)" : System.FormattableString.Invariant($"S{i+1}: H20={b.StockLengthMm:0.###} mm; edge={b.EdgeMm:0.###} mm (plywood maximum {Input.MaximumSlabEndStripMm(i):0.###} mm); equal foot setback={b.NearFootMm:0.###} mm at both faces; repeat every row; maximum row spacing={Input.BaySpacing(i):0.###} mm")));
        public string Schedule=>(SecondarySpacingNotice.Length==0 ? "" : SecondarySpacingNotice+"\n\n")+(Bays.Any(b=>b!=null) ? UpperTransferPlan.SiteArrangement+"\n"+string.Join("\n",Bays.Select((b,i)=>b==null ? null : $"S{i+1}: equal foot setback {b.NearFootMm:0.###} mm at both concrete faces; repeat every slab row; maximum row spacing {Input.BaySpacing(i):0.###} mm").Where(x=>x!=null))+"\n\n" : "")+string.Join("\n\n",CrossSupports.Select(c=>c.Schedule))+ (CrossSupports.Length==0 ? "" : "\n"+CrossBeamSupportResult.Limitations+"\n\n")+(RequiresSeparateSlabFormwork ? SeparateSlabNotice+"\n\n"+SeparateSlabExplanation+"\n\n" : "")+(Input.CrossingBeams.Length==0 ? "" : string.Join("\n",Input.CrossingBeams.Select((c,i)=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows ? $"{CrossSupports.FirstOrDefault(s=>s.CrossingIndex==i)?.SupportDescription??($"X{i+1} · support not calculated")} · bottom {c.BottomElevationMm:0.###} mm · requested {c.SupportMode}" : (Bays[c.BayNumber-1]==null ? $"X{i+1}: S{c.BayNumber} · separate formwork design required (not calculated)" : $"X{i+1}: S{c.BayNumber} · centre {c.PositionMm:0.###} mm · {c.WidthMm:0.###} × {c.DepthMm:0.###} mm · extra bay load {(Input.AreaLoad(c.DepthMm)-Input.AreaLoad(Input.Bays[c.BayNumber-1].ThicknessMm))*c.WidthMm/1000*Input.Bays[c.BayNumber-1].WidthMm/1000:0.00} kN through slab H20s")))+"\n\n")+string.Join("\n\n",Beams.Select((b,i)=>$"B{i+1}: {Input.Beams[i].WidthMm:0} × {Input.Beams[i].DepthMm:0} mm · bottom {Input.Beams[i].BottomElevationMm:0.###} mm · {b.LegCount} legs\n{b.Schedule}\n{b.StockSchedule}"));
    }
    public sealed class NetworkBeamOption
    {
        public TransferNetworkResult Result {get;}
        public int BeamIndex {get;}
        public string Label {get;}
        public string RejectionReason {get;}
        public bool IsAvailable=>Result!=null;
        internal NetworkBeamOption(TransferNetworkResult result,int beam,string label,string rejection=null){Result=result;BeamIndex=beam;Label=label;RejectionReason=rejection;}

    }
    public static class TransferNetworkOptimizer
    {
        private static readonly ConditionalWeakTable<TransferredSlabOptimizer.GeometryCache,NetworkSpanStockCoverCache> spanStockCovers
            =new ConditionalWeakTable<TransferredSlabOptimizer.GeometryCache,NetworkSpanStockCoverCache>();
        internal static double[][] CachedSpanStockCovers(TowerInput input,TransferredSlabOptimizer.GeometryCache geometry)
        {
            input.Trace?.CheckCancellation();
            // Explicit restricted lists already define their own exact universe.
            if(!input.ReuseSearchWork || geometry==null || input.TransferredSlab?.StockCovers!=null)return TransferredSlabOptimizer.StockCovers(input);
            return spanStockCovers.GetValue(geometry,_=>new NetworkSpanStockCoverCache()).Get(input);
        }
        private sealed class State { public TowerLayout[] Beams; public UpperTransferPlan[] Bays; public int Legs,Separate,Narrow,BaysCount;public double Timber,BracePenalty,Symmetry; }
        internal sealed class ReceiverTopologyReserve
        {
            private readonly int[] pieces,current,legs,bays;
            internal ReceiverTopologyReserve(TowerInput common,TransferNetworkInput network,double[][] sharedCovers,TransferredSlabOptimizer.GeometryCache geometryCache,ReceiverAggregateLoadBound aggregate=null)
            {
                int count=network.Beams.Length;pieces=new int[count];current=new int[count];legs=new int[count+1];bays=new int[count+1];
                for(int beam=0;beam<count;beam++)
                {
                    var local=ReceiverStockCovers(common,network,beam,sharedCovers,geometryCache);
                    // Empty local covers are rejected by the ordinary solve;
                    // until then they supply no optimistic reservation.
                    pieces[beam]=local.Select(c=>c.Length).DefaultIfEmpty(0).Min();
                    current[beam]=Math.Max(4*pieces[beam],aggregate?.MinimumLegs(beam,local)??0);
                }
                for(int beam=count-1;beam>=0;beam--)
                {legs[beam]=legs[beam+1]+current[beam];bays[beam]=bays[beam+1]+pieces[beam];}
            }
            internal int CurrentLegs(int beam)=>current[beam];
            internal int FutureLegs(int beam)=>legs[beam+1];
            internal int FutureBays(int beam)=>bays[beam+1];
        }
        // Terminal physical loads supply a reserve independent of the selected
        // upper feet and later crossing reroutes. Sum both primary lines before
        // discarding certified nonnegative crossing forces; either line alone
        // can have negative reactions and does not supply this lower bound.
        internal sealed class ReceiverAggregateLoadBound
        {
            private readonly TowerInput common;
            private readonly TransferNetworkInput network;
            private readonly double coverage;
            private readonly double[] ledger;
            private RetainedNormalPlans[] retainedNormalPlans;
            private sealed class Profile
            {
                internal BeamDistributedLoad[][] Raw,Reduced;internal bool AveragePrepared,EarlyPrepared,PrimitivePrepared,NormalPrepared;internal BeamDistributedLoad[] Average,EarlyAverage;internal double[] Primitive;internal int Beam;internal NormalWitness Normal;
                internal readonly System.Runtime.CompilerServices.ConditionalWeakTable<TowerSearchDiagnostics,HashSet<double>> EmptyReports=new System.Runtime.CompilerServices.ConditionalWeakTable<TowerSearchDiagnostics,HashSet<double>>();
            }
            // A complete view depends only on these exact numeric inputs after
            // each caller independently establishes its physical certificate.
            // Keep every atomic cut and load in order: even equal adjacent
            // densities cannot be merged before the original tiny-load clipping.
            internal sealed class UniversalProfileSignature : IEquatable<UniversalProfileSignature>
            {
                private readonly long[] bits;private readonly int hash;private readonly int loadCount;
                internal long Weight=>64L+bits.Length*8L;
                internal int LoadCount=>loadCount;
                internal UniversalProfileSignature(BeamDistributedLoad[] average,double shear,double moment,double[] primitiveIntervals=null,BeamDistributedLoad[] normalWitness=null)
                {
                    loadCount=average.Length;int clipping=primitiveIntervals?.Length??0,witness=normalWitness?.Length??0;
                    bits=new long[4+average.Length*3+clipping+witness*3];bits[0]=BitConverter.DoubleToInt64Bits(shear);bits[1]=BitConverter.DoubleToInt64Bits(moment);bits[2]=clipping;bits[3]=witness;
                    int at=4;foreach(var q in average){bits[at++]=BitConverter.DoubleToInt64Bits(q.StartMm);bits[at++]=BitConverter.DoubleToInt64Bits(q.EndMm);bits[at++]=BitConverter.DoubleToInt64Bits(q.KnPerM);}
                    if(primitiveIntervals!=null)foreach(double value in primitiveIntervals)bits[at++]=BitConverter.DoubleToInt64Bits(value);
                    if(normalWitness!=null)foreach(var q in normalWitness){bits[at++]=BitConverter.DoubleToInt64Bits(q.StartMm);bits[at++]=BitConverter.DoubleToInt64Bits(q.EndMm);bits[at++]=BitConverter.DoubleToInt64Bits(q.KnPerM);}
                    unchecked{int value=17;foreach(long item in bits)value=value*31+item.GetHashCode();hash=value;}
                }
                public override int GetHashCode()=>hash;
                public override bool Equals(object other)=>Equals(other as UniversalProfileSignature);
                public bool Equals(UniversalProfileSignature other)
                {
                    if(ReferenceEquals(this,other))return true;
                    if(other==null || hash!=other.hash || bits.Length!=other.bits.Length)return false;
                    for(int i=0;i<bits.Length;i++)if(bits[i]!=other.bits[i])return false;return true;
                }
            }
            internal readonly struct UniversalViewKey : IEquatable<UniversalViewKey>
            {
                internal readonly UniversalProfileSignature Profile;internal readonly TransferredSlabOptimizer.Geometry[] Parent;private readonly long edge;
                internal UniversalViewKey(UniversalProfileSignature profile,TransferredSlabOptimizer.Geometry[] parent,double value)
                {Profile=profile;Parent=parent;edge=BitConverter.DoubleToInt64Bits(value);}
                public override int GetHashCode(){unchecked{return (Profile.GetHashCode()*31+RuntimeHelpers.GetHashCode(Parent))*31+edge.GetHashCode();}}
                public override bool Equals(object other)=>other is UniversalViewKey key && Equals(key);
                public bool Equals(UniversalViewKey other)=>ReferenceEquals(Parent,other.Parent) && edge==other.edge && Profile.Equals(other.Profile);
            }
            internal sealed class UniversalView
            {
                internal UniversalViewKey Key;internal TransferredSlabOptimizer.Geometry[] Kept;
                internal TransferredSlabOptimizer.Geometry FirstFailed;internal int FirstRun;internal BeamDistributedLoad[] FirstLoads;internal long Weight;
                internal NormalWitnessFailure? FirstNormalFailure;
                internal ulong[] Mask;internal ulong Counts;
            }
            // One search-pass GeometryCache owns the bounded history. Equivalent
            // numeric profiles from other beams, phases or coverage trials may
            // share a completed view without sharing their physical eligibility.
            internal sealed class UniversalViewCache
            {
                private readonly long maximumWeight;private readonly int maximumEntries;
                private readonly Dictionary<UniversalViewKey,LinkedListNode<UniversalView>> views=new Dictionary<UniversalViewKey,LinkedListNode<UniversalView>>();
                private readonly LinkedList<UniversalView> order=new LinkedList<UniversalView>();private long weight;
                internal UniversalViewCache(long maximumWeight=128L*1024*1024,int maximumEntries=int.MaxValue)
                {
                    if(maximumWeight<=0 || maximumEntries<=0)throw new ArgumentOutOfRangeException(nameof(maximumWeight));
                    this.maximumWeight=maximumWeight;this.maximumEntries=maximumEntries;
                }
                internal long RetainedBytes{get{lock(views)return weight;}}
                internal bool CanRetain(UniversalProfileSignature profile,int entries)
                    =>512L+entries*16L+((entries+63L)/64)*8L+profile.Weight+profile.LoadCount*64L<=maximumWeight;
                internal bool TryGet(UniversalViewKey key,BeamSearchTrace trace,out UniversalView view)
                {
                    lock(views)
                    {
                        trace?.CheckCancellation();
                        if(views.TryGetValue(key,out var found))
                        {
                            order.Remove(found);order.AddLast(found);view=found.Value;
                            if(!ReferenceEquals(view.Key.Profile,key.Profile))trace?.Increment(BeamSearchCounter.UniversalGeometrySharedProfileHits);
                            return true;
                        }
                        view=null;return false;
                    }
                }
                internal UniversalView Publish(UniversalView view,BeamSearchTrace trace)
                {
                    trace?.CheckCancellation();if(view.Weight>maximumWeight)return view;
                    lock(views)
                    {
                        trace?.CheckCancellation();
                        if(views.TryGetValue(view.Key,out var found)){order.Remove(found);order.AddLast(found);return found.Value;}
                        while(order.Count>=maximumEntries || weight+view.Weight>maximumWeight)
                        {var oldest=order.First;views.Remove(oldest.Value.Key);weight-=oldest.Value.Weight;order.RemoveFirst();}
                        views.Add(view.Key,order.AddLast(view));weight+=view.Weight;trace?.ObserveMaximum(BeamSearchCounter.UniversalGeometryPeakBytes,weight);return view;
                    }
                }
            }
            private readonly Profile[] profiles;
            private ReceiverAggregateLoadBound(TowerInput common,TransferNetworkInput network,double coverage,double[] ledger)
            {this.common=common;this.network=network;this.coverage=coverage;this.ledger=ledger;profiles=new Profile[network.Beams.Length];}
            private static bool Finite(double value)=>TowerBeamAnalysis.Finite(value);
            internal static double LowerForce(double force,double scale)=>Math.Max(0,force-1e-6-Math.Max(Math.Abs(force),scale)*1e-6);
            internal static double UpperLedger(double force)=>force>0 ? force+1e-6+Math.Abs(force)*1e-6 : 0;
            internal static double MaximumLedgerHalf(double edgeLineLoad,double span)=>UpperLedger(Math.Max(0,edgeLineLoad*span/2000));
            internal static ReceiverAggregateLoadBound TryCreate(TowerInput common,TransferNetworkInput network,double coverage,bool allSlabsCarried,TransferredSlabOptimizer.GeometryCache geometry)
            {
                if(!allSlabsCarried || !common.EngineerWorkflow || !common.CoordinateCrossingLayouts || !common.PruneNetworkBounds
                    || !common.OptimizeCrossingSearch || common.Trace?.HasTimeLimit==true || geometry?.Fixed!=null
                    || !network.HasIndividualSpacings || network.PlywoodSheetLengthMm!=PlywoodSpacing.SheetLengthMm
                    || !CrossBeamSupportOptimizer.Modern(network) || network.CrossingBeams.Any(c=>c.SupportMode==CrossBeamSupportMode.LegacySlabRows)
                    || !Finite(coverage) || coverage<=0)return null;
                try
                {
                    // SlabEdgeLoads uses the first receiver's concrete length,
                    // not the shared intersection or any primary stock length.
                    var source=new TransferNetworkResult {Input=network,Beams=new[]{new TowerLayout {LengthMm=network.Beams[0].SpanLength(common.LengthMm)}},
                        Bays=network.Bays.Select(_=>new UpperTransferPlan()).ToArray(),SlabCoveredLengthMm=coverage};
                    var ledger=new double[network.Bays.Length];
                    var centres=new double[network.Beams.Length];
                    for(int beam=1;beam<centres.Length;beam++)centres[beam]=centres[beam-1]+network.Beams[beam-1].WidthMm/2+network.Bays[beam-1].WidthMm+network.Beams[beam].WidthMm/2;
                    for(int index=0;index<network.CrossingBeams.Length;index++)
                    {
                        common.Trace?.CheckCancellation();var cross=network.CrossingBeams[index];int bay=cross.BayNumber-1;
                        var edges=CrossBeamSupportOptimizer.SlabEdgeLoads(source,index);
                        double edge=edges.Sum(p=>p.Kn),edgeScale=edges.Sum(p=>Math.Abs(p.Kn));
                        double concrete=network.AreaLoad(cross.DepthMm)*cross.WidthMm/1000,total=concrete+edge;
                        // Fixed ledger reactions can be signed. The combined
                        // concrete/ledger total must be positive before its
                        // receiver reaction may be omitted from this reserve.
                        if(!Finite(edge) || !Finite(edgeScale) || !Finite(concrete) || !Finite(total) || concrete<=0
                            || total<=1e-6+Math.Max(Math.Abs(concrete),edgeScale)*1e-6)return null;
                        if(CrossBeamSupportOptimizer.PrimaryBearingPossible(common,network,cross)
                            && !PrimaryAxesCertified(common,network,cross,centres))return null;
                        double maximum=MaximumLedgerHalf(edge,network.Bays[bay].WidthMm);
                        if(!Finite(maximum))return null;ledger[bay]+=maximum;
                        if(!Finite(ledger[bay]))return null;
                    }
                    return new ReceiverAggregateLoadBound(common,network,coverage,ledger);
                }
                catch(ArgumentException){return null;}
            }
            // A stock between axes a<b, with nonnegative near/far end strips,
            // has aggregate receiver reactions at least
            // q*(s-(s-a)^2/(2*(b-a))) and q*(s-b^2/(2*(b-a))).
            // Check the complete width/offset domain used by Receivers. Shared
            // bearings have the stronger universal 150..350 mm axis restriction;
            // dedicated junction end-zone totals are nonnegative by conservation.
            internal static bool PrimaryAxesCertified(TowerInput common,TransferNetworkInput network,TransferCrossingBeamRow cross,double[] centres=null)
            {
                int bay=cross.BayNumber-1;double span=network.Bays[bay].WidthMm;
                if(centres==null)
                {
                    centres=new double[network.Beams.Length];
                    for(int beam=1;beam<centres.Length;beam++)centres[beam]=centres[beam-1]+network.Beams[beam-1].WidthMm/2+network.Bays[beam-1].WidthMm+network.Beams[beam].WidthMm/2;
                }
                var settings=new[]{Tuple.Create(600.0,0.0),Tuple.Create(1000.0,0.0),Tuple.Create(1500.0,0.0),Tuple.Create(1524.0,0.0),Tuple.Create(2000.0,0.0),Tuple.Create(1000.0,-25.0),Tuple.Create(1000.0,25.0)};
                double face=centres[bay]+network.Beams[bay].WidthMm/2;
                foreach(var left in settings)foreach(var right in settings)
                {
                    double near=centres[bay]+left.Item2+left.Item1/2,far=centres[bay+1]+right.Item2-right.Item1/2;
                    double a=near-face,b=far-face,d=far-near;
                    if(!Finite(a) || !Finite(b) || !Finite(d) || !Finite(span))return false;
                    // These routes are rejected before stock enumeration by the
                    // original reversed-axis or earliest/latest endpoint checks.
                    if(far<=near+1e-7 || a<common.StockEndSetbackMm-1e-7 || b>span-common.StockEndSetbackMm+1e-7)continue;
                    double limit=2*span*d,nearSquare=(span-a)*(span-a),farSquare=b*b;
                    double margin=1e-6+Math.Max(Math.Abs(limit),Math.Max(nearSquare,farSquare))*1e-6;
                    if(!Finite(limit) || nearSquare>limit-margin || farSquare>limit-margin)return false;
                }
                return true;
            }
            internal static double PrimaryConcreteCoefficient(TowerInput common,TransferNetworkInput network,TransferCrossingBeamRow cross,int side)
            {
                if(side<0 || side>1)throw new ArgumentOutOfRangeException(nameof(side));
                int bay=cross.BayNumber-1;double span=network.Bays[bay].WidthMm,result=double.PositiveInfinity;
                if(!Finite(span) || span<=0 || !Finite(common.StockEndSetbackMm) || common.StockEndSetbackMm<0)return 0;
                var settings=NormalReceiverSettings();
                var centres=new double[network.Beams.Length];
                for(int beam=1;beam<centres.Length;beam++)centres[beam]=centres[beam-1]+network.Beams[beam-1].WidthMm/2+network.Bays[beam-1].WidthMm+network.Beams[beam].WidthMm/2;
                double face=centres[bay]+network.Beams[bay].WidthMm/2;
                foreach(var left in settings)foreach(var right in settings)
                {
                    // Preserve the actual absolute primary-axis association.
                    double near=centres[bay]+left.Item2+left.Item1/2,far=centres[bay+1]+right.Item2-right.Item1/2;
                    double a=near-face,b=far-face,d=far-near;
                    if(!Finite(a) || !Finite(b) || !Finite(d))return 0;
                    if(far<=near+1e-7)continue;
                    // Include unenclosable stock/setback poses conservatively:
                    // removing them at a separately associated ULP boundary
                    // could omit an actual permitted route. Extra poses only
                    // weaken this minimum; every real route is still covered.
                    double square=side==0 ? (span-a)*(span-a) : b*b,term=square/(2*d),value=span-term;
                    if(!Finite(value) || !Finite(term))return 0;
                    result=Math.Min(result,LowerForce(value,Math.Abs(span)+Math.Abs(term)));
                }
                // No admitted axis pair supplies no stronger positive proof.
                return Finite(result) ? result : 0;
            }
            internal static double DedicatedConcreteCoefficient(TowerInput common,double receiverBottom,double crossingBottom,bool carried)
            {
                // Full-span dedicated results have no receiving bearings. They
                // are impossible only below EVERY permitted shutter allowance.
                if(!Finite(common.MaximumConcreteEndOverhangMm) || common.MaximumConcreteEndOverhangMm>=175
                    || !Finite(receiverBottom) || !Finite(crossingBottom) || receiverBottom>crossingBottom+1e-7)return 0;
                double value=receiverBottom<crossingBottom-1e-7 ? 175 : carried ? 500 : 0;
                return LowerForce(value,Math.Abs(value)+Math.Abs(receiverBottom)+Math.Abs(crossingBottom));
            }
            internal double PhysicalConcreteCoefficient(int beam,TransferCrossingBeamRow crossing)
            {
                int bay=crossing.BayNumber-1,side=beam==bay ? 0 : beam==bay+1 ? 1 : -1;if(side<0)return 0;
                if(common.MaximumConcreteEndOverhangMm>=175 || crossing.SupportMode==CrossBeamSupportMode.LegacySlabRows)return 0;
                double value=double.PositiveInfinity;
                if(crossing.SupportMode!=CrossBeamSupportMode.DedicatedTowers)
                {
                    if(CrossBeamSupportOptimizer.PrimaryBearingPossible(common,network,crossing))
                        value=Math.Min(value,PrimaryConcreteCoefficient(common,network,crossing,side));
                    if(CrossBeamSupportOptimizer.ValidFillerGap(crossing.BottomElevationMm,network.Beams[bay].BottomElevationMm,out var near)
                        && CrossBeamSupportOptimizer.ValidFillerGap(crossing.BottomElevationMm,network.Beams[bay+1].BottomElevationMm,out var far))
                        value=Math.Min(value,SharedConcreteCoefficient(network.Bays[bay].WidthMm));
                }
                if(crossing.SupportMode!=CrossBeamSupportMode.ExistingSupportsOnly)
                    // TryCreate certifies every slab option carried. Actual input
                    // guards require this adjacent bay on the receiver too.
                    value=Math.Min(value,DedicatedConcreteCoefficient(common,network.Beams[beam].BottomElevationMm,crossing.BottomElevationMm,true));
                return Finite(value) ? value : 0;
            }
            private EngineerPrimaryPlan Plan(int beam,TowerInput local,bool deductLedger)
            {
                var plan=EngineerPrimaryPlan.Create(local,network,beam);
                if(deductLedger)foreach(int bay in new[]{beam-1,beam}.Where(b=>b>=0 && b<network.Bays.Length))
                {
                    double reference=network.ReferenceLengthMm>0 ? network.ReferenceLengthMm : plan.StartMm+plan.LengthMm;
                    double normal=network.BayEnd(bay,reference)-network.BayStart(bay,reference)-network.CrossingBeams.Where(c=>c.BayNumber==bay+1).Sum(c=>c.WidthMm);
                    double source=network.AreaLoad(network.Bays[bay].ThicknessMm)*network.Bays[bay].WidthMm/2000;
                    // An unattainable combination of maximal route ledgers may
                    // exceed the source. Zero normal slab force is then safe.
                    plan.SlabLedgerForces[bay]=Math.Max(0,Math.Min(ledger[bay],source*Math.Max(0,normal)/1000));
                }
                return plan;
            }
            private Profile ForBeam(int beam)
            {
                if(profiles[beam]!=null)return profiles[beam];
                var local=common.CopyForSearch();local.LengthMm=network.Beams[beam].SpanLength(common.LengthMm);local.LongitudinalStartMm=network.Beams[beam].SpanStartMm;
                // Build each fixed source profile once, before the stock-cover
                // loop. Integrating another interval needs no zone preparation.
                return profiles[beam]=new Profile {Beam=beam,Raw=Plan(beam,local,false).WithoutCrossingLoads(),Reduced=Plan(beam,local,true).WithoutCrossingLoads()};
            }
            private static double Force(BeamDistributedLoad[][] zones,double start,double length,double selfWeight)
            {
                double force=0;
                foreach(var line in zones)
                {
                    double value=selfWeight*length/1000;
                    foreach(var q in line)
                    {
                        // Preserve Loads' member-coordinate clipping and tiny
                        // interval exclusion, using its original absolute cuts.
                        double a=Math.Max(0,Math.Max(start,q.StartMm)-start),b=Math.Min(length,Math.Min(start+length,q.EndMm)-start);
                        if(b>a+1e-7)value+=q.KnPerM*(b-a)/1000;
                    }
                    force+=value;
                }
                return force;
            }
            internal double StockForce(int beam,double start,double length)
                =>StockForce(ForBeam(beam),start,length);
            private double StockForce(Profile profile,double start,double length)
            {
                var primitive=UniversalClippingIntervals(profile.Beam);
                if(primitive==null || !PrimitiveWindowSafe(primitive,start,length))return 0;
                double self=common.Primary.SelfWeightKnM(length),before=Force(profile.Raw,start,length,self),after=Force(profile.Reduced,start,length,self);
                // Scale the allowance to the raw sources, not a small residual
                // after cancellation. All algebra changes only a necessary bound.
                return LowerForce(after,Math.Abs(before)+Math.Abs(before-after));
            }
            // Atomic cells refine every possible source design cut, including
            // cuts erased by equal-density merging in Raw/Reduced. The separate
            // primitive-window guard protects signed source clipping at tiny cuts.
            internal BeamDistributedLoad[] UniversalAverage(int beam)
            {
                var profile=ForBeam(beam);if(profile.AveragePrepared)return profile.Average;
                var raw=profile.Raw.Select(line=>line.ToList()).ToArray();var lower=profile.Reduced.Select(line=>line.ToList()).ToArray();
                var source=new TransferNetworkResult {Input=network,Beams=new[]{new TowerLayout {LengthMm=network.Beams[0].SpanLength(common.LengthMm)}},
                    Bays=network.Bays.Select(_=>new UpperTransferPlan()).ToArray(),SlabCoveredLengthMm=coverage};
                try
                {
                    foreach(var crossing in network.CrossingBeams.Where(c=>c.BayNumber==beam || c.BayNumber==beam+1))
                    {
                        common.Trace?.CheckCancellation();double coefficient=PhysicalConcreteCoefficient(beam,crossing);if(coefficient<=0)continue;
                        int index=Array.IndexOf(network.CrossingBeams,crossing);var edges=CrossBeamSupportOptimizer.SlabEdgeLoads(source,index);
                        if(edges.Any(p=>!Finite(p.PositionMm) || !Finite(p.Kn) || p.Kn<0))continue;
                        double area=network.AreaLoad(crossing.DepthMm),span=network.Bays[crossing.BayNumber-1].WidthMm;
                        double ledgerEnvelope=network.AreaLoad(network.Bays[crossing.BayNumber-1].ThicknessMm)*50,edgeScale=edges.Sum(p=>Math.Abs(p.Kn));
                        if(!Finite(ledgerEnvelope) || ledgerEnvelope<0 || !Finite(edgeScale) || edgeScale>ledgerEnvelope)continue;
                        double before=area*span/1000+ledgerEnvelope*span/crossing.WidthMm,after=area*coefficient/1000;
                        if(!Finite(before) || !Finite(after) || before<after || after<=0)continue;
                        double start=crossing.PositionMm-crossing.WidthMm/2,end=crossing.PositionMm+crossing.WidthMm/2;
                        raw[0].Add(new BeamDistributedLoad {StartMm=start,EndMm=end,KnPerM=before});
                        lower[0].Add(new BeamDistributedLoad {StartMm=start,EndMm=end,KnPerM=after});
                    }
                }
                catch(ArgumentException){raw=profile.Raw.Select(line=>line.ToList()).ToArray();lower=profile.Reduced.Select(line=>line.ToList()).ToArray();}
                var average=GuardedAverage(raw.Select(l=>l.ToArray()).ToArray(),lower.Select(l=>l.ToArray()).ToArray(),UniversalCuts(beam));common.Trace?.CheckCancellation();
                profile.Average=average;profile.AveragePrepared=true;return average;
            }
            private double[] UniversalCuts(int beam)
            {
                var local=network.Beams[beam];double reference=network.ReferenceLengthMm>0 ? network.ReferenceLengthMm : local.SpanStartMm+local.SpanLength(common.LengthMm);
                var cuts=new List<double> {local.SpanStartMm,local.SpanEnd(common.LengthMm)};
                foreach(int bay in new[]{beam-1,beam}.Where(b=>b>=0 && b<network.Bays.Length))
                {cuts.Add(network.BayStart(bay,reference));cuts.Add(network.BayEnd(bay,reference));}
                foreach(var crossing in network.CrossingBeams.Where(c=>c.BayNumber==beam || c.BayNumber==beam+1))
                {cuts.Add(crossing.PositionMm-crossing.WidthMm/2);cuts.Add(crossing.PositionMm+crossing.WidthMm/2);}
                return cuts.ToArray();
            }
            // Resolved source lines only merge/remove these design cuts. A
            // clipped positive actual atom can disappear only if a primitive
            // subinterval also disappears. Keep zero-density primitive cells:
            // they may hide opposing signed actual line contributions.
            internal double[] UniversalClippingIntervals(int beam)
            {
                var profile=ForBeam(beam);if(profile.PrimitivePrepared)return profile.Primitive;
                var primitive=PrimitiveIntervals(profile.Raw,profile.Reduced,UniversalCuts(beam));common.Trace?.CheckCancellation();
                profile.Primitive=primitive;profile.PrimitivePrepared=true;return primitive;
            }
            internal static double[] PrimitiveIntervals(BeamDistributedLoad[][] raw,BeamDistributedLoad[][] reduced,double[] mandatory)
            {
                if(raw==null || reduced==null || raw.Any(l=>l==null) || reduced.Any(l=>l==null) || mandatory==null || mandatory.Any(x=>!Finite(x)))return null;
                var all=raw.Concat(reduced).SelectMany(l=>l).ToArray();
                if(all.Any(q=>q==null || !Finite(q.StartMm) || !Finite(q.EndMm) || q.EndMm<q.StartMm))return null;
                var cuts=all.SelectMany(q=>new[]{q.StartMm,q.EndMm}).Concat(mandatory).Distinct().OrderBy(x=>x).ToArray();var result=new List<double>();
                for(int i=1;i<cuts.Length;i++)if(cuts[i]>cuts[i-1]){result.Add(cuts[i-1]);result.Add(cuts[i]);}
                return result.ToArray();
            }
            internal static bool PrimitiveWindowSafe(double[] primitive,double start,double length)
            {
                for(int i=0;i<primitive.Length;i+=2)
                {
                    double a=Math.Max(0,Math.Max(start,primitive[i])-start),b=Math.Min(length,Math.Min(start+length,primitive[i+1])-start);
                    if(b>a && !(b>a+1e-7))return false;
                }
                return true;
            }
            internal readonly struct NormalWitnessFailure
            {
                internal readonly string Name,Unit;internal readonly double Demand,Limit,CellStart,CellEnd,Density;
                internal NormalWitnessFailure(string name,string unit,double demand,double limit,BeamDistributedLoad cell)
                {Name=name;Unit=unit;Demand=demand;Limit=limit;CellStart=cell.StartMm;CellEnd=cell.EndMm;Density=cell.KnPerM;}
                internal void Record(TowerInput input,int run)
                    =>input.Diagnostics?.Limit(input.DiagnosticScope+"Exterior slab-facing primary · P"+(run+1),Name,3,Demand,Limit,Unit,
                        $"One normal cell {CellStart:0.###}–{CellEnd:0.###} mm; guaranteed slab-facing line load {Density:0.######} kN/m",
                        "Reduce the primary support span or stock cantilever.",Limit*1e-6);
            }
            internal sealed class NormalWitness
            {
                internal readonly BeamDistributedLoad[] Cells;internal readonly double OwnLower,OwnUpper,SlabMinimum,CentroidLower,Scale;
                private readonly Tuple<double,double>[] settings;private readonly double bay,beamWidth;private readonly bool last;
                private readonly double? boundWidth;private readonly double boundOffset;
                private readonly double[] knownUpper;
                private readonly RetainedNormalPlans retainedUpper;
                internal NormalWitness(BeamDistributedLoad[] cells,double ownLower,double ownUpper,double slabMinimum,double centroidLower,double scale,Tuple<double,double>[] settings,double bay,double beamWidth,bool last,double? boundWidth=null,double boundOffset=0,double[] knownUpper=null,RetainedNormalPlans retainedUpper=null)
                {Cells=cells;OwnLower=ownLower;OwnUpper=ownUpper;SlabMinimum=slabMinimum;CentroidLower=centroidLower;Scale=scale;this.settings=settings;this.bay=bay;this.beamWidth=beamWidth;this.last=last;this.boundWidth=boundWidth;this.boundOffset=boundOffset;this.knownUpper=knownUpper;this.retainedUpper=retainedUpper;}
                private bool WidthAllowed(double width,double offset)=>settings.Any(s=>s.Item1==width && s.Item2==offset);
                internal NormalWitness ForKnownUpper(UpperTransferPlan plan)
                {
                    if(plan==null || !ValidNormalUpper(plan,bay))return null;
                    // A symmetric upper member places half its stock load at
                    // the foot and its edge shutter load 100 mm beyond the face.
                    // NormalLoads scales both together when deducting ledgers.
                    var snapshot=new[]{plan.StockLengthMm,plan.EdgeMm,plan.NearFootMm,plan.FarFootMm};
                    double stockMoment=snapshot[0]*snapshot[2],edgeMoment=2*snapshot[1]*100,h=(stockMoment+edgeMoment)/bay;
                    double scale=(Math.Abs(stockMoment)+Math.Abs(edgeMoment))/bay+Math.Abs(h);
                    if(!Finite(h) || !Finite(scale))return null;h=Math.Max(CentroidLower,LowerForce(h,scale));
                    double density=double.PositiveInfinity;
                    foreach(var setting in settings)
                    {
                        double delta=last ? -setting.Item2 : setting.Item2;
                        density=Math.Min(density,OwnLower*(.5-delta/setting.Item1)+SlabMinimum*(.5+(beamWidth/2+h-delta)/setting.Item1));
                    }
                    if(!Finite(density))return null;density=LowerForce(density,Scale);
                    var cells=Cells.Select(c=>new BeamDistributedLoad {StartMm=c.StartMm,EndMm=c.EndMm,KnPerM=Math.Max(c.KnPerM,density)}).ToArray();
                    return new NormalWitness(cells,OwnLower,OwnUpper,SlabMinimum,h,Scale,settings,bay,beamWidth,last,boundWidth,boundOffset,snapshot);
                }
                internal NormalWitness ForRetainedUpper(RetainedNormalPlans retained)
                {
                    if(retained==null || retained.Bay!=bay)return null;
                    double h=Math.Max(CentroidLower,retained.CentroidLower),density=double.PositiveInfinity;
                    foreach(var setting in settings)
                    {
                        double delta=last ? -setting.Item2 : setting.Item2;
                        density=Math.Min(density,OwnLower*(.5-delta/setting.Item1)+SlabMinimum*(.5+(beamWidth/2+h-delta)/setting.Item1));
                    }
                    if(!Finite(density))return null;density=LowerForce(density,Scale);
                    var cells=Cells.Select(c=>new BeamDistributedLoad {StartMm=c.StartMm,EndMm=c.EndMm,KnPerM=Math.Max(c.KnPerM,density)}).ToArray();
                    return new NormalWitness(cells,OwnLower,OwnUpper,SlabMinimum,h,Scale,settings,bay,beamWidth,last,boundWidth,boundOffset,retainedUpper:retained);
                }
                internal NormalWitness ForSetting(double width,double offset)
                {
                    if(!WidthAllowed(width,offset))return null;double delta=last ? -offset : offset;
                    double density=OwnLower*(.5-delta/width)+SlabMinimum*(.5+(beamWidth/2+CentroidLower-delta)/width);
                    if(!Finite(density))return null;density=LowerForce(density,Scale);if(density<=0)return null;
                    return new NormalWitness(Cells.Select(c=>new BeamDistributedLoad {StartMm=c.StartMm,EndMm=c.EndMm,KnPerM=density}).ToArray(),OwnLower,OwnUpper,SlabMinimum,CentroidLower,Scale,settings,bay,beamWidth,last,width,offset,knownUpper,retainedUpper);
                }
                internal bool Allows(TowerInput input)
                {
                    var t=input?.TransferredSlab;if(t==null || t.BeamWidthMm!=beamWidth || t.BeamCentreOffsetMm!=-input.ReceiverTransverseOffsetMm)return false;
                    if(input.StockLengthsMm==null || input.StockLengthsMm.Length==0 || input.StockLengthsMm.Any(s=>!Finite(s) || s<300 || s>50000))return false;
                    if(boundWidth.HasValue && (t.FixedTowerWidthMm!=boundWidth || input.ReceiverTransverseOffsetMm!=boundOffset))return false;
                    if(t.FixedTowerWidthMm.HasValue)
                    {if(!WidthAllowed(t.FixedTowerWidthMm.Value,input.ReceiverTransverseOffsetMm))return false;}
                    else if(input.ReceiverTransverseOffsetMm!=0 || TowerOptimizer.BaysMm.Any(w=>!WidthAllowed(w,0)) || !WidthAllowed(1524,0))return false;
                    var plan=last ? t.LeftPlan : t.RightPlan;
                    if(knownUpper!=null && (plan==null || plan.StockLengthMm!=knownUpper[0] || plan.EdgeMm!=knownUpper[1] || plan.NearFootMm!=knownUpper[2] || plan.FarFootMm!=knownUpper[3]))return false;
                    if(retainedUpper!=null && !retainedUpper.Contains(plan))return false;
                    return ValidNormalUpper(plan,bay);
                }
            }
            internal sealed class RetainedNormalPlans
            {
                private readonly struct PlanKey : IEquatable<PlanKey>
                {
                    private readonly double stock,edge,near,far;
                    internal PlanKey(UpperTransferPlan plan){stock=plan.StockLengthMm;edge=plan.EdgeMm;near=plan.NearFootMm;far=plan.FarFootMm;}
                    public bool Equals(PlanKey other)=>stock==other.stock && edge==other.edge && near==other.near && far==other.far;
                    public override bool Equals(object value)=>value is PlanKey other && Equals(other);
                    public override int GetHashCode(){unchecked{return ((stock.GetHashCode()*397^edge.GetHashCode())*397^near.GetHashCode())*397^far.GetHashCode();}}
                }
                private readonly HashSet<PlanKey> members;
                internal readonly double Bay,CentroidLower;
                private RetainedNormalPlans(double bay,double centroid,HashSet<PlanKey> members){Bay=bay;CentroidLower=centroid;this.members=members;}
                internal bool Contains(UpperTransferPlan plan)=>plan!=null && members.Contains(new PlanKey(plan));
                internal static RetainedNormalPlans TryCreate(double bay,UpperTransferPlan[] options)
                {
                    if(options==null || options.Length==0 || options.Any(p=>p==null || !ValidNormalUpper(p,bay)))return null;
                    var members=new HashSet<PlanKey>();double minimum=double.PositiveInfinity;
                    foreach(var plan in options)
                    {
                        double stockMoment=plan.StockLengthMm*plan.NearFootMm,edgeMoment=2*plan.EdgeMm*100,h=(stockMoment+edgeMoment)/bay;
                        double scale=(Math.Abs(stockMoment)+Math.Abs(edgeMoment))/bay+Math.Abs(h);
                        if(!Finite(h) || !Finite(scale))return null;
                        minimum=Math.Min(minimum,LowerForce(h,scale));members.Add(new PlanKey(plan));
                    }
                    return Finite(minimum) ? new RetainedNormalPlans(bay,minimum,members) : null;
                }
            }
            // Bind only the already-retained option universe. The immutable
            // numeric membership guard is rechecked on every native header;
            // reverse copies of symmetric left-hand plans need no reference key.
            internal void BindRetainedNormalOptions(UpperTransferPlan[][] options)
            {
                if(retainedNormalPlans!=null)return;
                var retained=new RetainedNormalPlans[network.Bays.Length];
                if(options!=null && options.Length==network.Bays.Length)
                    foreach(int bay in new[]{0,network.Bays.Length-1}.Distinct())
                    {common.Trace?.CheckCancellation();retained[bay]=RetainedNormalPlans.TryCreate(network.Bays[bay].WidthMm,options[bay]);}
                common.Trace?.CheckCancellation();retainedNormalPlans=retained;
            }
            internal static bool ValidNormalUpper(UpperTransferPlan plan,double bay)
                =>plan==null || SymmetricPlan(plan,bay) && new[]{plan.StockLengthMm,plan.EdgeMm,plan.NearFootMm,plan.FarFootMm}.All(Finite)
                    && plan.StockLengthMm>=600 && plan.EdgeMm>=200 && plan.NearFootMm>=Math.Max(250,plan.EdgeMm+150) && bay-plan.NearFootMm-plan.FarFootMm>=300;
            internal static double NormalCentroidLower(double bay)
            {
                if(!Finite(bay) || bay<1000)return 0;
                double term=100000/bay,value=350-term;return LowerForce(value,350+Math.Abs(term));
            }
            private static Tuple<double,double>[] NormalReceiverSettings()
                =>TowerOptimizer.BaysMm.Concat(new[]{1000.0,1500,1524,2000}).Distinct().Select(w=>Tuple.Create(w,0.0))
                    .Concat(new[]{Tuple.Create(1000.0,-25.0),Tuple.Create(1000.0,25.0)}).ToArray();
            // For an outermost receiver, all normal slab points lie beyond one
            // face. Symmetric upper stock with e>=200, f>=e+150 and a 300 mm
            // foot span has centroid h>=350-100000/bay beyond that face.
            internal NormalWitness ExteriorNormalWitness(int beam)
            {
                var profile=ForBeam(beam);if(profile.NormalPrepared)return profile.Normal;
                NormalWitness result=null;
                if(beam==0 || beam==network.Beams.Length-1)
                {
                    int bayIndex=beam==0 ? 0 : network.Bays.Length-1;var row=network.Beams[beam];var slab=network.Bays[bayIndex];double bay=slab.WidthMm;
                    var average=UniversalAverage(beam);var primitive=UniversalClippingIntervals(beam);var settings=NormalReceiverSettings();double h=NormalCentroidLower(bay);
                    if(average!=null && primitive!=null && h>0 && settings.All(s=>Finite(s.Item1) && s.Item1>0 && Finite(s.Item2) && Math.Abs(s.Item2)<s.Item1/2))
                    {
                        double own=network.AreaLoad(row.DepthMm)*row.WidthMm/1000,slabRaw=network.AreaLoad(slab.ThicknessMm)*bay/2000;
                        double ownMargin=1e-6+Math.Abs(own)*1e-6,ownUpper=own+ownMargin,ownLower=Math.Max(0,own-ownMargin);
                        double reference=network.ReferenceLengthMm>0 ? network.ReferenceLengthMm : row.SpanEnd(common.LengthMm);
                        double bayStart=network.BayStart(bayIndex,reference),bayEnd=network.BayEnd(bayIndex,reference),spanEnd=row.SpanEnd(common.LengthMm);
                        var crossings=network.CrossingBeams.Where(c=>c.BayNumber==bayIndex+1).ToArray();var cells=new List<BeamDistributedLoad>();double minimumSlab=double.PositiveInfinity,maximumScale=0;
                        if(Finite(own) && own>=0 && Finite(slabRaw) && slabRaw>=0 && Finite(ownUpper) && Finite(bayStart) && Finite(bayEnd))
                        for(int i=0;i<primitive.Length;i+=2)
                        {
                            common.Trace?.CheckCancellation();
                            double a=primitive[i],b=primitive[i+1];
                            if(a<row.SpanStartMm || b>spanEnd || a<bayStart || b>bayEnd || crossings.Any(c=>a<c.PositionMm+c.WidthMm/2 && b>c.PositionMm-c.WidthMm/2))continue;
                            double q=average.Where(v=>v.StartMm<=a && v.EndMm>=b).Select(v=>v.KnPerM).DefaultIfEmpty(0).Sum();
                            double slabMinimum=Math.Max(0,2*q-ownUpper),density=double.PositiveInfinity;
                            foreach(var setting in settings)
                            {
                                double width=setting.Item1,offset=beam==0 ? setting.Item2 : -setting.Item2;
                                density=Math.Min(density,ownLower*(.5-offset/width)+slabMinimum*(.5+(row.WidthMm/2+h-offset)/width));
                            }
                            double maximumLever=row.WidthMm/2+bay/2+100;
                            double amplification=1+settings.Max(s=>Math.Abs(.5-s.Item2/s.Item1)+Math.Abs(.5+(maximumLever+Math.Abs(s.Item2))/s.Item1));
                            double scale=(Math.Abs(own)+Math.Abs(slabRaw)+Math.Abs(slabRaw-slabMinimum))*amplification;
                            if(!Finite(density) || !Finite(scale))continue;density=LowerForce(density,scale);
                            if(density<=0)continue;cells.Add(new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=density});minimumSlab=Math.Min(minimumSlab,slabMinimum);maximumScale=Math.Max(maximumScale,scale);
                        }
                        if(cells.Count>0)result=new NormalWitness(cells.ToArray(),ownLower,ownUpper,minimumSlab,h,maximumScale,settings,bay,row.WidthMm,beam!=0);
                    }
                }
                common.Trace?.CheckCancellation();profile.Normal=result;profile.NormalPrepared=true;return result;
            }
            // A witness is local to one whole support span or free stock tail.
            // It never combines different cells or fills unknown crossing zones.
            internal static bool ExteriorNormalFits(TowerInput input,double start,double length,double[] supports,BeamDistributedLoad[] cells,out NormalWitnessFailure failure)
                =>ExteriorNormalFitsSupports(input,start,length,new SupportVector(supports),cells,out failure);
            internal static bool ExteriorNormalFitsSupports(TowerInput input,double start,double length,SupportVector supports,BeamDistributedLoad[] cells,out NormalWitnessFailure failure)
            {
                failure=default;double shear=input.Primary.ShearLimitKn,moment=input.Primary.MomentLimitKnM;
                for(int kind=0;kind<3;kind++)
                {
                    int begin=kind==0 ? 1 : 0,end=kind==0 ? supports.Length : 1;
                    for(int i=begin;i<end;i++)
                    {
                        double lo=kind==0 ? supports[i-1] : kind==1 ? 0 : supports[supports.Length-1],hi=kind==0 ? supports[i] : kind==1 ? supports[0] : length;
                        if(!(hi>lo+1e-7))continue;
                        foreach(var cell in cells)
                        {
                            double a=Math.Max(0,Math.Max(start,cell.StartMm)-start),b=Math.Min(length,Math.Min(start+length,cell.EndMm)-start);
                            if(lo<a || hi>b)continue;
                            double force=cell.KnPerM*Math.Max(0,Math.Min(hi,b)-Math.Max(lo,a))/1000;
                            double v=kind==0 ? Math.Abs(force)/2 : Math.Abs(force);
                            if(v>shear+shear*1e-6){failure=new NormalWitnessFailure(kind==0 ? "normal span shear lower bound" : "normal stock-tail shear", "kN",v,shear,cell);return false;}
                            if(kind!=0)
                            {
                                double root=kind==1 ? hi : lo,m=Math.Abs(cell.KnPerM*(hi-lo)*((lo+hi)/2-root)/1e6);
                                if(m>moment+moment*1e-6){failure=new NormalWitnessFailure("normal stock-tail moment","kNm",m,moment,cell);return false;}
                            }
                            break;
                        }
                    }
                }
                return true;
            }
            // Below refinement eight the native admission graph requires every
            // eligible crossing to pass Shared, or a direct-primary OR arm which
            // retains that crossing's half-span planning load. This is admission
            // metadata only; the terminal physical reserve keeps its weaker loads.
            internal static double SharedConcreteCoefficient(double span)
            {
                if(!Finite(span) || span<600)return 0;
                double a=Math.Min(350,span/2-150),d0=span-2*a,d1=span-150-a;
                if(d0<=0 || d1<=0)return 0;
                double p=(span-a)*(span-a)/(2*d0),q=(span-150)*(span-150)/(2*d1);
                double result=Math.Min(span/2,span-Math.Max(p,q)),scale=Math.Abs(span)+Math.Abs(p)+Math.Abs(q);
                return Finite(result) && Finite(scale) ? LowerForce(result,scale) : 0;
            }
            internal BeamDistributedLoad[] UniversalEarlyAverage(int beam)
            {
                var profile=ForBeam(beam);if(profile.EarlyPrepared)return profile.EarlyAverage;
                var raw=profile.Raw.Select(line=>line.ToList()).ToArray();var lower=profile.Raw.Select(line=>line.ToList()).ToArray();int added=0;
                var source=new TransferNetworkResult {Input=network,Beams=new[]{new TowerLayout {LengthMm=network.Beams[0].SpanLength(common.LengthMm)}},
                    Bays=network.Bays.Select(_=>new UpperTransferPlan()).ToArray(),SlabCoveredLengthMm=coverage};
                try
                {
                    foreach(var crossing in EngineerPrimaryPlan.Adjacent(network,beam))
                    {
                        common.Trace?.CheckCancellation();if(CrossBeamSupportOptimizer.PrimaryBearingPossible(common,network,crossing))continue;
                        int index=Array.IndexOf(network.CrossingBeams,crossing),bay=crossing.BayNumber-1;double span=network.Bays[bay].WidthMm;
                        double coefficient=SharedConcreteCoefficient(span),area=network.AreaLoad(crossing.DepthMm);
                        var edges=CrossBeamSupportOptimizer.SlabEdgeLoads(source,index);
                        if(coefficient<=0 || !Finite(area) || area<=0 || edges.Any(p=>!Finite(p.Kn) || p.Kn<0))continue;
                        // An input-only envelope keeps equivalent admission
                        // profiles independent of coverage-specific ledger rows.
                        // Check the actual source against it rather than assuming
                        // a coverage or virtual-reaction bound for unusual inputs.
                        double ledgerEnvelope=network.AreaLoad(network.Bays[bay].ThicknessMm)*50;
                        double edgeScale=edges.Sum(p=>Math.Abs(p.Kn));
                        if(!Finite(ledgerEnvelope) || ledgerEnvelope<0 || !Finite(edgeScale) || edgeScale>ledgerEnvelope)continue;
                        double before=area*span/1000+ledgerEnvelope*span/crossing.WidthMm,after=area*coefficient/1000;
                        if(!Finite(before) || !Finite(after) || before<after || after<=0)continue;
                        double start=crossing.PositionMm-crossing.WidthMm/2,end=crossing.PositionMm+crossing.WidthMm/2;
                        raw[0].Add(new BeamDistributedLoad {StartMm=start,EndMm=end,KnPerM=before});
                        lower[0].Add(new BeamDistributedLoad {StartMm=start,EndMm=end,KnPerM=after});added++;
                    }
                }
                catch(ArgumentException){added=0;}
                var average=added==0 ? UniversalAverage(beam) : GuardedAverage(raw.Select(l=>l.ToArray()).ToArray(),lower.Select(l=>l.ToArray()).ToArray(),UniversalCuts(beam));
                common.Trace?.CheckCancellation();profile.EarlyAverage=average??UniversalAverage(beam);profile.EarlyPrepared=true;return profile.EarlyAverage;
            }
            internal static BeamDistributedLoad[] GuardedAverage(BeamDistributedLoad[][] raw,BeamDistributedLoad[][] reduced,double[] mandatoryCuts)
            {
                if(raw==null || reduced==null || raw.Length!=2 || reduced.Length!=2 || raw.Any(l=>l==null) || reduced.Any(l=>l==null))return null;
                var all=raw.Concat(reduced).SelectMany(l=>l).ToArray();
                if(all.Any(q=>q==null || !Finite(q.StartMm) || !Finite(q.EndMm) || !Finite(q.KnPerM) || q.EndMm<q.StartMm || q.KnPerM<0)
                    || mandatoryCuts==null || mandatoryCuts.Any(x=>!Finite(x)))return null;
                var cuts=all.SelectMany(q=>new[]{q.StartMm,q.EndMm}).Concat(mandatoryCuts).Distinct().OrderBy(x=>x).ToArray();var result=new List<BeamDistributedLoad>();
                for(int i=1;i<cuts.Length;i++)
                {
                    double a=cuts[i-1],b=cuts[i];if(b<=a)continue;
                    double before=raw.SelectMany(l=>l).Where(q=>q.StartMm<=a && q.EndMm>=b).Sum(q=>q.KnPerM);
                    double after=reduced.SelectMany(l=>l).Where(q=>q.StartMm<=a && q.EndMm>=b).Sum(q=>q.KnPerM);
                    double scale=Math.Abs(before)+Math.Abs(before-after),allowance=1e-6+scale*1e-6;
                    if(!Finite(before) || !Finite(after) || !Finite(scale) || after>before+allowance)return null;
                    double lower=Math.Max(0,after-allowance)/2;if(!Finite(lower))return null;
                    if(lower>0)result.Add(new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=lower});
                }
                // Do not merge equal adjacent cells before member clipping.
                return result.ToArray();
            }
            internal static bool SymmetricPlan(UpperTransferPlan plan,double bay)
                =>plan==null || plan.NearFootMm==plan.FarFootMm && plan.EdgeMm==(bay-plan.StockLengthMm)/2 && plan.StockLengthMm+2*plan.EdgeMm==bay;
            internal Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilter(int beam)
                =>GeometryFilterForAverage(beam,UniversalAverage(beam));
            // The installation site must supply the native preparation/layout
            // callbacks. Identity guards protect subsequent search copies; they
            // do not authenticate arbitrary callbacks supplied by another caller.
            internal Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterWithNativeChecks(int beam,
                Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation,Func<TowerLayout,bool> acceptance)
                =>GeometryFilterForNativeReceiver(beam,preparation,acceptance,null,0);
            internal Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterForNativeReceiver(int beam,
                Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation,Func<TowerLayout,bool> acceptance,double? fixedWidth,double offset)
                =>GeometryFilterForKnownUpper(beam,preparation,acceptance,fixedWidth,offset,null);
            internal Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterForKnownUpper(int beam,
                Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation,Func<TowerLayout,bool> acceptance,double? fixedWidth,double offset,UpperTransferPlan facingPlan)
            {
                if(preparation==null || acceptance==null)return GeometryFilter(beam);
                var fallback=ExteriorNormalWitness(beam);
                return GeometryFilterForNormalWitness(beam,preparation,acceptance,fixedWidth,offset,fallback?.ForKnownUpper(facingPlan),fallback);
            }
            internal Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterForRetainedOptions(int beam,
                Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation,Func<TowerLayout,bool> acceptance,double? fixedWidth,double offset)
            {
                if(preparation==null || acceptance==null)return GeometryFilter(beam);
                var fallback=ExteriorNormalWitness(beam);int bay=beam==0 ? 0 : network.Bays.Length-1;
                var retained=retainedNormalPlans!=null && (beam==0 || beam==network.Beams.Length-1) ? retainedNormalPlans[bay] : null;
                return GeometryFilterForNormalWitness(beam,preparation,acceptance,fixedWidth,offset,fallback?.ForRetainedUpper(retained),fallback);
            }
            private Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterForNormalWitness(int beam,
                Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation,Func<TowerLayout,bool> acceptance,double? fixedWidth,double offset,NormalWitness normal,NormalWitness fallback)
            {
                if(preparation==null || acceptance==null)return GeometryFilter(beam);
                bool NativeChecks(TowerInput input)=>input!=null && input.TransferredSlab?.EngineerReceiverCount>=3 && input.TransferredSlab.EngineerReceiverCount<=8
                    && input.PrepareReceiverChecks && input.ReuseTransversePreparations && input.DeferTransferLayout && input.DeferCrossingChecks && input.FullLoadingOnly
                    && ReferenceEquals(input.TransferReceiverPreparation,preparation) && ReferenceEquals(input.TransferLayoutAcceptance,acceptance)
                    && ReferenceEquals(input.EngineerPlan?.Network,network) && input.EngineerPlan.Beam==beam;
                if(fixedWidth.HasValue){fallback=fallback?.ForSetting(fixedWidth.Value,offset);normal=normal?.ForSetting(fixedWidth.Value,offset);}
                if(normal==null){normal=fallback;fallback=null;}
                var weak=GeometryFilterWithWitness(beam,UniversalAverage(beam),"Guaranteed minimum combined load",normal,NativeChecks,fallback);
                var early=GeometryFilterWithWitness(beam,UniversalEarlyAverage(beam),"Early shared crossing trial lower load",normal,NativeChecks,fallback);if(early==null)return weak;
                return (input,source,edge)=>
                {
                    bool native=NativeChecks(input) && input.TransferredSlab.EngineerReceiverCount<8
                        && ReferenceEquals(input.TransferredSlab.EngineerNetwork,network) && input.TransferredSlab.EngineerBeam==beam;
                    return native ? early(input,source,edge) : weak==null ? source : weak(input,source,edge);
                };
            }
            private Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterForAverage(int beam,BeamDistributedLoad[] average,string explanation="Guaranteed minimum combined load")
                =>GeometryFilterWithWitness(beam,average,explanation,null,null);
            private Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> GeometryFilterWithWitness(int beam,BeamDistributedLoad[] average,string explanation,NormalWitness normal,Func<TowerInput,bool> native,NormalWitness fallback=null)
            {
                if(average==null || average.Length==0)return null;
                average=average.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM}).ToArray();
                var primitive=UniversalClippingIntervals(beam);if(primitive==null)return null;primitive=primitive.ToArray();
                var signature=new UniversalProfileSignature(average,common.Primary.ShearLimitKn,common.Primary.MomentLimitKnM,primitive);
                var normalCells=normal?.Cells.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM}).ToArray();
                var normalSignature=normalCells==null ? null : new UniversalProfileSignature(average,common.Primary.ShearLimitKn,common.Primary.MomentLimitKnM,primitive,normalCells);
                var fallbackCells=fallback?.Cells.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM}).ToArray();
                var fallbackSignature=fallbackCells==null ? null : new UniversalProfileSignature(average,common.Primary.ShearLimitKn,common.Primary.MomentLimitKnM,primitive,fallbackCells);
                var profile=ForBeam(beam);var row=network.Beams[beam];double start=row.SpanStartMm,length=row.SpanLength(common.LengthMm);
                double left=beam==0 ? 0 : network.Bays[beam-1].WidthMm,right=beam==network.Bays.Length ? 0 : network.Bays[beam].WidthMm;
                var material=common.PrimaryMaterial;
                double endAllowance=common.MaximumConcreteEndOverhangMm,endSetback=common.StockEndSetbackMm;
                double leftArea=left==0 ? 0 : network.AreaLoad(network.Bays[beam-1].ThicknessMm),rightArea=right==0 ? 0 : network.AreaLoad(network.Bays[beam].ThicknessMm);
                double beamLoad=network.AreaLoad(row.DepthMm)*row.WidthMm/1000;
                return (input,source,edge)=>
                {
                    if(input==null || !input.EngineerWorkflow || !input.PruneNetworkBounds || !input.OptimizeCrossingSearch || !input.ReuseSearchWork || input.Trace?.HasTimeLimit==true
                        || input.TransferredSlab?.SharedGeometryCache?.Fixed!=null || input.PrimaryMaterial!=material || input.LengthMm!=length || input.LongitudinalStartMm!=start
                        || input.MaximumConcreteEndOverhangMm!=endAllowance || input.StockEndSetbackMm!=endSetback
                        || input.TransferredSlab?.LeftBayMm!=left || input.TransferredSlab?.RightBayMm!=right
                        || input.TransferredSlab?.LeftAreaLoadKnM2!=leftArea || input.TransferredSlab?.RightAreaLoadKnM2!=rightArea || input.TransferredSlab?.BeamLineLoadKnM!=beamLoad
                        || !SymmetricPlan(input.TransferredSlab.LeftPlan,left) || !SymmetricPlan(input.TransferredSlab.RightPlan,right)
                        || input.EngineerPlan?.UseDesignZones!=true || input.EngineerPlan.Beam!=beam || beam>0 && !input.EngineerPlan.CarriesBay(beam-1)
                        || beam<network.Bays.Length && !input.EngineerPlan.CarriesBay(beam) || !ReferenceEquals(input.EngineerPlan.Network,network))return source;
                    input.Trace?.CheckCancellation();if(source==null)return source;
                    var views=input.TransferredSlab.SharedGeometryCache?.UniversalPrimaryViews;
                    bool certified=native?.Invoke(input)==true,useNormal=normalSignature!=null && certified && normal.Allows(input);
                    bool useFallback=!useNormal && fallbackSignature!=null && certified && fallback.Allows(input);
                    var activeSignature=useNormal ? normalSignature : useFallback ? fallbackSignature : signature;var activeCells=useNormal ? normalCells : useFallback ? fallbackCells : null;
                    // Skip unretainable builds up front. Never repeatedly scan a
                    // huge cold parent that cannot fit even its worst-case view.
                    if(views==null || !views.CanRetain(activeSignature,source.Length)){input.Trace?.Increment(BeamSearchCounter.UniversalGeometryViewFallbacks);return source;}
                    var key=new UniversalViewKey(activeSignature,source,edge);
                    if(views.TryGet(key,input.Trace,out var cached))input.Trace?.Increment(BeamSearchCounter.UniversalGeometryViewHits);
                    else
                    {
                        input.Trace?.Increment(BeamSearchCounter.UniversalGeometryViewBuilds);
                        input.Trace?.Increment(BeamSearchCounter.UniversalGeometrySourceEntries,source.Length);
                        cached=BuildUniversalView(input,source,edge,average,(origin,stock)=>PrimitiveWindowSafe(primitive,origin,stock),activeCells);
                        if(cached==null){input.Trace?.Increment(BeamSearchCounter.UniversalGeometryViewFallbacks);return source;}
                        input.Trace?.Increment(BeamSearchCounter.UniversalGeometrySurvivingEntries,cached.Kept.Length);
                        cached.Key=key;cached.Weight+=activeSignature.Weight;cached=views.Publish(cached,input.Trace);
                    }
                    input.Trace?.CheckCancellation();int excluded=source.Length-cached.Kept.Length;
                    if(excluded>0)
                    {
                        input.Trace?.Increment(BeamSearchCounter.UniversalPrimaryGeometryRejected,excluded);
                        if(cached.Kept.Length==0 && cached.FirstFailed!=null && input.Diagnostics!=null)
                        {
                            var reported=profile.EmptyReports.GetValue(input.Diagnostics,_=>new HashSet<double>());bool first;
                            lock(reported)first=reported.Add(edge);
                            if(first)
                            {
                                if(cached.FirstNormalFailure.HasValue)cached.FirstNormalFailure.Value.Record(input,cached.FirstRun);
                                else
                                {
                                    var member=cached.FirstFailed.Stock[cached.FirstRun];
                                    CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(input,member.StockLengthMm,cached.FirstFailed.Supports(cached.FirstRun),cached.FirstLoads,
                                        explanation+" · P"+(cached.FirstRun+1),true);
                                }
                            }
                        }
                    }
                    return cached.Kept;
                };
            }
            private readonly struct UniversalWindowKey : IEquatable<UniversalWindowKey>
            {
                private readonly double start,length;
                internal UniversalWindowKey(double start,double length){this.start=start;this.length=length;}
                public bool Equals(UniversalWindowKey other)=>start.Equals(other.start) && length.Equals(other.length);
                public override bool Equals(object other)=>other is UniversalWindowKey key && Equals(key);
                public override int GetHashCode(){unchecked{int hash=start.GetHashCode();return ((hash<<5)+hash)^length.GetHashCode();}}
            }
            private static UniversalView BuildUniversalView(TowerInput input,TransferredSlabOptimizer.Geometry[] source,double edge,BeamDistributedLoad[] average,Func<double,double,bool> windowSafe=null,BeamDistributedLoad[] normalWitness=null)
            {
                if(source==null || !Finite(edge))return null;
                int kept=0,index=0;var mask=new ulong[(int)((source.Length+63L)/64)];var windows=new Dictionary<UniversalWindowKey,BeamDistributedLoad[]>();
                var members=new TransferredSlabOptimizer.ResponseCache<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>(4096);
                var result=new UniversalView();
                foreach(var geometry in source)
                {
                    input.Trace?.CheckCancellation();bool fits=true;
                    if(geometry?.Stock==null || !geometry.HasSupportMetadata || geometry.Stock.Length!=geometry.SupportRuns)return null;
                    for(int run=0;run<geometry.Stock.Length;run++)
                    {
                        var member=geometry.Stock[run];var supports=geometry.Supports(run);if(member==null)return null;
                        double start=member.StartMm+edge,length=member.StockLengthMm;
                        if(!Finite(start) || !Finite(length) || length<=0 || supports.IsNull || supports.Length<2)return null;
                        for(int i=0;i<supports.Length;i++){double value=supports[i];if(!Finite(value) || value<0 || value>length)return null;}
                        for(int i=1;i<supports.Length;i++)if(supports[i]<=supports[i-1])return null;
                        var window=new UniversalWindowKey(start,length);
                        if(!windows.TryGetValue(window,out var loads))
                        {
                            if(windowSafe!=null && !windowSafe(start,length))windows[window]=loads=null;
                            else
                            {
                                var clipped=new List<BeamDistributedLoad>();
                                foreach(var q in average)
                                {
                                    double a=Math.Max(0,Math.Max(start,q.StartMm)-start),b=Math.Min(length,Math.Min(start+length,q.EndMm)-start);
                                    if(b>a+1e-7)clipped.Add(new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q.KnPerM});
                                }
                                windows[window]=loads=clipped.ToArray();
                            }
                        }
                        if(loads==null && normalWitness==null)continue;
                        TransferredSlabOptimizer.NecessarySupportHash(geometry,run,input.Trace,out int polynomial,out int power);
                        var key=new CrossBeamSupportOptimizer.NecessaryMemberKey(start,length,0,supports,polynomial,power);
                        if(!members.TryGetValue(key,out bool memberFits))
                        {
                            NormalWitnessFailure normalFailure=default;
                            memberFits=normalWitness==null || ExteriorNormalFitsSupports(input,start,length,supports,normalWitness,out normalFailure);
                            if(!memberFits)
                            {
                                input.Trace?.Increment(BeamSearchCounter.ExteriorNormalMembersRejected);
                                if(result.FirstFailed==null)result.FirstNormalFailure=normalFailure;
                            }
                            else memberFits=loads==null || CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(input,length,supports,loads,null);
                            input.Trace?.CheckCancellation();members.Store(key,memberFits);
                        }
                        if(memberFits)continue;
                        fits=false;if(result.FirstFailed==null){result.FirstFailed=geometry;result.FirstRun=run;result.FirstLoads=loads;}break;
                    }
                    if(fits)
                    {
                        mask[index/64]|=1UL<<(index%64);kept++;
                        int stations=geometry.Positions?.Length??0;
                        if(geometry.Positions==null)checked{for(int run=0;run<geometry.SupportRuns;run++)stations+=geometry.Supports(run).Length;}
                        result.Counts|=stations<64 ? 1UL<<stations : ulong.MaxValue;
                    }
                    index++;
                }
                input.Trace?.CheckCancellation();result.Mask=mask;
                if(kept==source.Length)result.Kept=source;
                else
                {
                    result.Kept=new TransferredSlabOptimizer.Geometry[kept];int at=0;
                    for(int i=0;i<source.Length;i++)if((mask[i/64]&(1UL<<(i%64)))!=0)result.Kept[at++]=source[i];
                }
                result.Weight=512L+source.Length*8L+(ReferenceEquals(result.Kept,source) ? 0 : result.Kept.Length*8L)+mask.Length*8L+(result.FirstLoads?.Length??0)*64L;return result;
            }
            internal int MinimumLegs(int beam,double[][] covers)
            {
                try
                {
                    var local=common.CopyForSearch();local.LengthMm=network.Beams[beam].SpanLength(common.LengthMm);local.LongitudinalStartMm=network.Beams[beam].SpanStartMm;
                    var profile=ForBeam(beam);
                    bool independent=common.IndependentNetworkPrimaryCoverage || network.DifferentSpans(common.LengthMm);
                    int minimum=int.MaxValue;var forces=new Dictionary<Tuple<double,double>,int>();
                    foreach(var cover in covers)
                    {
                        common.Trace?.CheckCancellation();double covered=cover.Sum();
                        if(independent ? covered<coverage-1e-7 : Math.Abs(covered-coverage)>1e-7)continue;
                        double start=local.CoverStart(covered);int stations=0;
                        foreach(double length in cover)
                        {
                            var key=Tuple.Create(start,length);
                            if(!forces.TryGetValue(key,out int count))
                            {
                                double force=StockForce(profile,start,length);
                                if(!Finite(force))return 0;
                                forces[key]=count=Math.Max(2,CrossBeamSupportOptimizer.PrimarySupportCountFloor(common,force/2));
                            }
                            stations+=count;start+=length;
                        }
                        minimum=Math.Min(minimum,2*stations);
                    }
                    return minimum==int.MaxValue ? 0 : minimum;
                }
                catch(ArgumentException){return 0;}
            }
        }
        private static bool Better(bool engineer,int legs,int narrow,int bays,double penalty,double timber,int oldLegs,int oldNarrow,int oldBays,double oldPenalty,double oldTimber,double symmetry=0,double oldSymmetry=0)
        {
            if(engineer)
            {
                if(legs!=oldLegs)return legs<oldLegs;
                int preference=EngineerTowerSearch.Compare(narrow,bays,penalty,oldNarrow,oldBays,oldPenalty);
                if(preference!=0)return preference<0;
                if(Math.Abs(symmetry-oldSymmetry)>1e-7)return symmetry<oldSymmetry;
            }
            double difference=(legs-oldLegs)*TowerOptimizer.TowerLegTimberEquivalentMm+timber-oldTimber;
            return difference<-.01 || Math.Abs(difference)<=.01 && TowerOptimizer.CompareBracePreference(narrow,bays,penalty,oldNarrow,oldBays,oldPenalty)<0;
        }
        private static bool Better(TransferNetworkResult a,TransferNetworkResult b)=>a.SeparateSlabCount<b.SeparateSlabCount || a.SeparateSlabCount==b.SeparateSlabCount
            && (a.Beams.Any(l=>l.EngineerSearch) ? EngineerBetter(a,b) : (a.EconomyScoreMm<b.EconomyScoreMm-.01 || Math.Abs(a.EconomyScoreMm-b.EconomyScoreMm)<=.01
                && (TowerOptimizer.CompareBracePreference(a.Standalone600Count,a.BracedBayCount,a.StandaloneBracePenalty,b.Standalone600Count,b.BracedBayCount,b.StandaloneBracePenalty)<0
                    || TowerOptimizer.CompareBracePreference(a.Standalone600Count,a.BracedBayCount,a.StandaloneBracePenalty,b.Standalone600Count,b.BracedBayCount,b.StandaloneBracePenalty)==0 && a.SymmetryErrorMm<b.SymmetryErrorMm-1e-7)));
        private static bool EngineerBetter(TransferNetworkResult a,TransferNetworkResult b)
        {
            if(a.LegCount!=b.LegCount)return a.LegCount<b.LegCount;
            int brace=EngineerTowerSearch.Compare(a.Standalone600Count,a.BracedBayCount,a.StandaloneBracePenalty,b.Standalone600Count,b.BracedBayCount,b.StandaloneBracePenalty);
            if(brace!=0)return brace<0;
            if(Math.Abs(a.CantileverImbalanceMm-b.CantileverImbalanceMm)>1e-7)return a.CantileverImbalanceMm<b.CantileverImbalanceMm;
            if(Math.Abs(a.SymmetryErrorMm-b.SymmetryErrorMm)>1e-7)return a.SymmetryErrorMm<b.SymmetryErrorMm;
            if(Math.Abs(a.BraceLocationPenalty-b.BraceLocationPenalty)>1e-7)return a.BraceLocationPenalty<b.BraceLocationPenalty;
            return a.EconomyScoreMm<b.EconomyScoreMm-.01 || Math.Abs(a.EconomyScoreMm-b.EconomyScoreMm)<=.01 && a.SymmetryErrorMm<b.SymmetryErrorMm-1e-7;
        }
        // Options are complete checked networks, never unchecked replacements of a
        // beam's displayed geometry. Other selected beam patterns remain fixed.
        public static NetworkBeamOption[] BeamOptions(TowerInput common,TransferNetworkResult current,int beam)
        {
            if(current==null || beam<0 || beam>=current.Beams.Length)throw new ArgumentException("Select a calculated main beam.");
            var worker=common.CopyForSearch();worker.CoordinateCrossingLayouts=false;worker.NetworkNumerics=null;worker.AllowLocalShutterExceptions=true;
            worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift);
            worker.DedicatedCrossCache=new Dictionary<string,DedicatedCrossSearch>();
            var results=new List<NetworkBeamOption> {new NetworkBeamOption(current,beam,"Option 1 · Original layout")};
            var seeds=new List<Tuple<TowerLayout,string>>();var rejected=new List<NetworkBeamOption>();
            foreach(int donor in Enumerable.Range(0,current.Beams.Length).Where(i=>i!=beam).OrderBy(i=>current.Beams[i].SymmetryErrorMm).ThenBy(i=>current.Beams[i].LegCount))
                seeds.Add(Tuple.Create(current.Beams[donor],$"Same tower/primary pattern as B{donor+1}"));
            var selected=current.Beams[beam];int legs=selected.PositionsMm.Length;
            seeds.Add(Tuple.Create(new TowerLayout {EngineerSearch=selected.EngineerSearch,LengthMm=selected.LengthMm,WidthMm=selected.WidthMm,Rotated=selected.Rotated,TransverseOffsetMm=selected.TransverseOffsetMm,
                LongitudinalStartMm=selected.LongitudinalStartMm,PositionsMm=selected.PositionsMm.Reverse().Select(x=>2*selected.LongitudinalStartMm+selected.LengthMm-x).ToArray(),Joined=selected.Joined.Reverse().ToArray(),
                StockPlan=new TowerStockPlan {PrimaryMaterial=selected.PrimaryMaterial,Pieces=selected.StockPlan.Pieces.Reverse().Select(p=>new TowerStockPiece {StartMm=2*selected.LongitudinalStartMm+selected.LengthMm-p.EndMm,StockLengthMm=p.StockLengthMm,FirstSupport=legs-1-p.LastSupport,LastSupport=legs-1-p.FirstSupport}).ToArray()}},"Mirrored tower/primary arrangement"));
            var checkedBeams=new TowerLayout[current.Beams.Length];
            for(int i=0;i<checkedBeams.Length;i++)
            {
                worker.Trace?.CheckCancellation();var source=current.Beams[i];
                checkedBeams[i]=Solve(worker,current.Input,i,i==0 ? null : current.Bays[i-1],i==current.Bays.Length ? null : current.Bays[i],current.SlabCoveredLengthMm,current.RowPhaseMm,
                    new[] {source.StockPlan.Pieces.Select(p=>p.StockLengthMm).ToArray()},TransferredSlabOptimizer.FixedGeometry(source),int.MaxValue,source.WidthMm,source.TransverseOffsetMm);
                if(checkedBeams[i]==null)return results.ToArray();
            }
            var covers=TransferredSlabOptimizer.StockCovers(worker);var cache=new TransferredSlabOptimizer.GeometryCache();
            foreach(double width in new[] {1524.0,1000,1500,2000}.Where(w=>w!=current.Beams[beam].WidthMm))
            {
                var alternative=Solve(worker,current.Input,beam,beam==0 ? null : current.Bays[beam-1],beam==current.Bays.Length ? null : current.Bays[beam],current.SlabCoveredLengthMm,current.RowPhaseMm,covers,cache,int.MaxValue,width);
                if(alternative!=null)seeds.Add(Tuple.Create(alternative,$"Alternative {width:0} mm transverse arrangement"));
            }
            var seen=new HashSet<string> {Pattern(current.Beams[beam])};
            foreach(var seed in seeds)
            {
                worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift);
                worker.Trace?.CheckCancellation();if(!seen.Add(Pattern(seed.Item1)))continue;
                var source=seed.Item1;
                var replacement=Solve(worker,current.Input,beam,beam==0 ? null : current.Bays[beam-1],beam==current.Bays.Length ? null : current.Bays[beam],current.SlabCoveredLengthMm,current.RowPhaseMm,
                    new[] {source.StockPlan.Pieces.Select(p=>p.StockLengthMm).ToArray()},TransferredSlabOptimizer.FixedGeometry(source),int.MaxValue,source.WidthMm,source.TransverseOffsetMm);
                if(replacement==null)continue;
                var beams=(TowerLayout[])checkedBeams.Clone();beams[beam]=replacement;
                var candidate=new TransferNetworkResult {Input=current.Input,Beams=beams,Bays=current.Bays,BeamCentresMm=current.BeamCentresMm,SlabCoveredLengthMm=current.SlabCoveredLengthMm,RowPhaseMm=current.RowPhaseMm,SlabLoadKn=beams.Sum(l=>l.Transfer.SlabLoadKn),FullTransferFailureDetails=current.FullTransferFailureDetails,SeparateSlabReasons=current.SeparateSlabReasons};
                if(!CrossBeamSupportOptimizer.Complete(worker,candidate))
                {
                    if(seed.Item2.StartsWith("Same tower/primary"))rejected.Add(new NetworkBeamOption(null,beam,seed.Item2+" · no complete crossing arrangement found",worker.Diagnostics.Exception("Alternative crossing search").Details));
                    continue;
                }
                CalculateMovement(candidate,worker.Trace);
                CrossBeamSupportOptimizer.BalanceSettingOut(worker,candidate);
                CalculateMovement(candidate,common.Trace);
                double delta=(candidate.TimberMm-current.TimberMm)/1000;
                results.Add(new NetworkBeamOption(candidate,beam,$"Option {results.Count+1} · {seed.Item2} · {candidate.Beams[beam].LegCount} beam legs · {candidate.LegCount} total legs · {delta:+0.##;-0.##;0} m H20"));
                if(results.Count==4)break;
            }
            return results.Concat(rejected).ToArray();
        }
        private static string Pattern(TowerLayout layout)=>string.Join(";",layout.StockPlan.Pieces.Select(p=>p.StockLengthMm.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+"|"+string.Join(";",layout.PositionsMm.Select(p=>p.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+"|"+string.Join(";",layout.Joined)+"|"+layout.WidthMm.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+layout.TransverseOffsetMm.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        public static TransferNetworkResult Optimize(TowerInput common,TransferNetworkInput input)
        {
            var previous=common.NetworkReceiverWork;
            common.NetworkReceiverWork=common.ReuseSearchWork && common.ReuseReceiverSearches ? previous??new NetworkReceiverCache() : null;
            try
            {
                var result=OptimizeNetwork(common,input);
                CrossBeamSupportOptimizer.BalanceSettingOut(common,result);
                if(!CrossBeamSupportOptimizer.ExternalNetworkFits(common,result))throw new ArgumentException("The coordinated network still intersects a neighboring part.");
                CalculateMovement(result,common.Trace);
                return result;
            }
            finally {common.NetworkReceiverWork=previous;}
        }
        private static TransferNetworkResult OptimizeNetwork(TowerInput common,TransferNetworkInput input)
        {
            input.Validate(common);
            if(!input.HasIndividualSpacings)
            {
                var source=input;input=source.WithSpacings();
                var profile=string.IsNullOrEmpty(source.PlywoodProfileId) ? PlywoodCatalog.Default : PlywoodCatalog.FromId(source.PlywoodProfileId);
                int denominator=source.PlywoodDeflectionDenominator==0 ? 350 : source.PlywoodDeflectionDenominator;
                input.LegacyBayPlywoodLimitsMm=source.Bays.Select((b,i)=>
                {
                    double pressure=source.AreaLoad(source.CrossingBeams.Where(c=>c.BayNumber==i+1 && c.SupportMode==CrossBeamSupportMode.LegacySlabRows).Select(c=>c.DepthMm).Concat(new[] {b.ThicknessMm}).Max());
                    try {return PlywoodSpacing.CalculateForSheet(profile,pressure,denominator).MaximumSpacingMm;}
                    catch(ArgumentException) {return 0.0;}
                }).ToArray();
                double plywoodMaximum=input.LegacyBayPlywoodLimitsMm.Where(x=>x>0).DefaultIfEmpty(source.UpperSpacingMm).Min();
                input.UpperSpacingMm=PlywoodSpacing.ListedSpacingAtOrBelow(Math.Min(source.UpperSpacingMm,plywoodMaximum));
            }
            try {return FullTransferWithAlternatives(common,input);}
            catch(TowerOptimizationException fullFailure) when (!common.AllowCustomH20Lengths && common.AllowSeparateSlabFallback)
            {
                var fullDiagnostics=common.Diagnostics;
                common.Trace?.Work("Separate slab formwork fallback");
                try
                {
                    var result=common.Trace==null ? SeparateTransfer(common,input) : common.Trace.RunPass(false,()=>SeparateTransfer(common,input));
                    result.FullTransferFailureDetails=fullFailure.Details;
                    result.SeparateSlabReasons=result.Bays.Select((b,i)=>b==null ? fullDiagnostics.SeparateBayExplanation(i) : "").ToArray();
                    return result;
                }
                catch(TowerOptimizationException partialFailure)
                {throw new TowerOptimizationException(partialFailure.Message,fullFailure.Details+"\n\nSeparate slab formwork fallback also failed:\n"+partialFailure.Details);}
            }
        }
        private static TransferNetworkResult PreferredShutterDetail(TowerInput common,TransferNetworkInput input)
        {
            try {return FullTransfer(common,input);}
            catch(TowerOptimizationException standardFailure) when (CrossBeamSupportOptimizer.Modern(input) && !common.AllowLocalShutterExceptions)
            {
                // Exhaust normal stock/custom, spacing and coordination searches first.
                // A locally relaxed early candidate must not hide a better standard plan.
                var worker=common.CopyForSearch();worker.AllowLocalShutterExceptions=true;
                common.Trace?.Work("Local 175 mm side-shutter fallback");
                try
                {
                    var result=FullTransfer(worker,input);
                    common.Diagnostics=worker.Diagnostics;return result;
                }
                catch(TowerOptimizationException reducedFailure)
                {throw new TowerOptimizationException(standardFailure.Message,standardFailure.Details+"\n\nLocal 175 mm side-shutter fallback also failed:\n"+reducedFailure.Details);}
            }
        }
        private static TransferNetworkResult FullTransferWithAlternatives(TowerInput common,TransferNetworkInput input)
        {
            try {return PreferredShutterDetail(common,input);}
            catch(TowerOptimizationException boundedFailure) when (!common.RetainAllSlabSupportOptions)
            {
                // A larger plywood allowance introduces shorter stock and moves
                // its feet. Before excluding a slab, retry every passing discrete
                // stock/setback option rather than relying on representative bands.
                var worker=common.CopyForSearch();worker.RetainAllSlabSupportOptions=true;
                common.Trace?.Work("Slab stock and support alternatives");
                try {var result=PreferredShutterDetail(worker,input);common.Diagnostics=worker.Diagnostics;return result;}
                catch(TowerOptimizationException expandedFailure)
                {throw new TowerOptimizationException(boundedFailure.Message,boundedFailure.Details+"\n\nFull slab stock/support alternatives also failed:\n"+expandedFailure.Details);}
            }
        }
        private static TransferNetworkResult FullTransfer(TowerInput common,TransferNetworkInput input)
        {
            if(input.HasIndividualSpacings)
            {
                // Spacing retries retain exact geometry/response work within this one stock pass.
                // Concrete loads, capacities, stock scope and exclusions stay constant here.
                var previousGeometry=common.NetworkGeometryWork;var previousNumerics=common.NetworkNumericalWork;
                var previousReceivers=common.NetworkReceiverWork;
                var previousPrefixes=common.NetworkPrefixWork;
                common.NetworkReceiverWork=common.ReuseSearchWork && common.ReuseReceiverSearches ? previousReceivers??new NetworkReceiverCache() : null;
                common.NetworkPrefixWork=common.ReuseSearchWork && common.ReuseReceiverSearches ? new NetworkPrefixCache() : null;
                common.NetworkGeometryWork=common.ReuseSearchWork ? new TransferredSlabOptimizer.GeometryCache() : null;
                using(var numericalWork=common.ReuseSearchWork ? new ThreadLocal<TransferredSlabOptimizer.SearchWorkCache>(()=>new TransferredSlabOptimizer.SearchWorkCache()) : null)
                {
                    common.NetworkNumericalWork=numericalWork;
                    try {return IndividualTransfer(common,input);}
                    finally {common.NetworkGeometryWork=previousGeometry;common.NetworkNumericalWork=previousNumerics;common.NetworkReceiverWork=previousReceivers;common.NetworkPrefixWork=previousPrefixes;}
                }
            }
            return LegacyTransfer(common,input);
        }
        private static TransferNetworkResult SearchSpacing(TowerInput common,TransferNetworkInput input)
        {
            common.Trace?.Increment(BeamSearchCounter.NetworkSpacingTrials);
            return SearchSpacingStockPasses(common,input,()=>OptimizeCore(common,input,false));
        }
        internal static TransferNetworkResult SearchSpacingStockPasses(TowerInput common,TransferNetworkInput input,Func<TransferNetworkResult> search)
        {
            if(!common.EngineerSearch || !common.LocalNetworkSearch || !common.PruneNetworkBounds || !common.AllowCustomH20Lengths
                || common.FirstCompleteNetworkOnly || common.Trace?.HasTimeLimit==true)
                return TowerStockPlanner.SearchWithCustom(common,search,Better);
            var original=common.NetworkCatalogueBound;TransferNetworkResult catalogue=null;
            try
            {
                return TowerStockPlanner.SearchWithCustom(common,()=>
                {
                    bool custom=common.AllowCustomH20Lengths;
                    if(custom && catalogue!=null && !catalogue.SearchTimeLimitReached)
                    {
                        // A completed catalogue design remains permitted in the
                        // custom-stock superset. Seed only its existing exact
                        // prefix bound; every equal or better branch still runs.
                        var previous=common.NetworkCatalogueBound;
                        if(previous==null || !ReferenceEquals(previous.SourceInput,input) || Better(catalogue,previous.Result))
                            common.NetworkCatalogueBound=new NetworkCatalogueBound(input,catalogue);
                    }
                    var result=search();if(!custom)catalogue=result;return result;
                },Better);
            }
            finally{common.NetworkCatalogueBound=original;}
        }
        private static TransferNetworkResult IndividualTransfer(TowerInput common,TransferNetworkInput input)
        {
            common.Trace?.NetworkPhase("Finding a complete starting arrangement");
            // Find a checked seed with the existing bounded descent, then recover
            // independent maxima. Never enumerate the Cartesian product of pitches.
            TowerOptimizationException failure=null;
            var first=common;
            // Two-crossing junctions need the same checked seed/recovery route
            // as larger networks, with either catalogue or custom stock. Each
            // spacing trial establishes feasibility; the final pass still
            // applies the complete design ranking.
            bool checkedSeedFirst=common.EngineerWorkflow && common.OptimizeCrossingSearch && input.CrossingBeams.Length>=2;
            if(common.EngineerSearch && CrossBeamSupportOptimizer.Modern(input))
            {
                // Start with the whole junction. A cheapest provisional receiver
                // can obstruct its crossing and adds no useful uncoordinated pass.
                first=common.CopyForSearch();first.CoordinateCrossingLayouts=true;
            }
            if(!checkedSeedFirst)
            {
                try {return SearchSpacing(first,input);}
                catch(TowerOptimizationException ex){failure=ex;}
                // The caller needs failed coordinated-search evidence for separate-formwork reasons.
                finally {common.Diagnostics=first.Diagnostics;}
            }
            var worker=common.CopyForSearch();worker.CoordinateCrossingLayouts=CrossBeamSupportOptimizer.Modern(input);
            worker.FirstCompleteNetworkOnly=checkedSeedFirst || common.FirstCompleteNetworkOnly;
            var attempted=new HashSet<string>();
            TransferNetworkResult best=null;
            Func<TransferNetworkInput,string> keyOf=n=>string.Join(";",n.BeamSpacingsMm.Concat(n.SlabSpacingsMm).Concat(n.CrossingSpacingsMm));
            if(!checkedSeedFirst && (!worker.CoordinateCrossingLayouts || first.CoordinateCrossingLayouts))attempted.Add(keyOf(input));
            // Try a single category step before a blanket seed. Receiving-beam
            // failure must not automatically tighten slabs or dedicated crossings.
            var closer=input.WithSpacings(1);
            var seeds=new List<TransferNetworkInput>();
            // Establish a fully checked complete network using the permitted
            // project stock before exhaustive effort on marginal upper-foot/receiver
            // combinations at every maximum pitch. Independent recovery still
            // attempts the requested maxima and intermediate permitted pitches.
            if(checkedSeedFirst && !common.AllowCustomH20Lengths)
            {
                // Catalogue-only junctions often need a smaller beam pitch but
                // can retain their slab and crossing maxima. Try that practical
                // seed before densifying every category in the network.
                var practical=input.WithSpacings();practical.BeamSpacingsMm=closer.BeamSpacingsMm;
                practical.UpperSpacingMm=practical.BeamSpacingsMm.Min();seeds.Add(practical);
            }
            if(checkedSeedFirst)seeds.Add(input.WithSpacings(PlywoodSpacing.StandardSpacingsMm.Count-1));
            if(worker.CoordinateCrossingLayouts)seeds.Add(input);
            for(int group=0;group<3;group++)
            {
                var candidate=input.WithSpacings();
                if(group==0)candidate.BeamSpacingsMm=closer.BeamSpacingsMm;
                if(group==1)candidate.SlabSpacingsMm=closer.SlabSpacingsMm;
                if(group==2)candidate.CrossingSpacingsMm=closer.CrossingSpacingsMm;
                candidate.UpperSpacingMm=candidate.BeamSpacingsMm.Min();seeds.Add(candidate);
            }
            seeds.AddRange(Enumerable.Range(1,PlywoodSpacing.StandardSpacingsMm.Count-1).Select(step=>input.WithSpacings(step)));
            foreach(var candidate in seeds)
            {
                if(!attempted.Add(keyOf(candidate)))continue;
                common.Trace?.CheckCancellation();
                try {best=SearchSpacing(worker,candidate);break;}
                catch(TowerOptimizationException ex){failure=ex;}
            }
            if(best==null){common.Diagnostics=worker.Diagnostics;throw failure;}
            if(common.FirstCompleteNetworkOnly){common.Diagnostics=worker.Diagnostics;return best;}
            var acceptedDiagnostics=worker.Diagnostics;
            var maxima=input.BeamSpacingsMm.Concat(input.SlabSpacingsMm).Concat(input.CrossingSpacingsMm).ToArray();
            Func<TransferNetworkInput,double[]> spacings=n=>n.BeamSpacingsMm.Concat(n.SlabSpacingsMm).Concat(n.CrossingSpacingsMm).ToArray();
            Func<double[],bool> restore=values=>
            {
                common.Trace?.CheckCancellation();
                string key=string.Join(";",values);
                if(!attempted.Add(key))return false;
                var candidate=best.Input.WithSpacings();
                candidate.BeamSpacingsMm=values.Take(input.Beams.Length).ToArray();
                candidate.SlabSpacingsMm=values.Skip(input.Beams.Length).Take(input.Bays.Length).ToArray();
                candidate.CrossingSpacingsMm=values.Skip(input.Beams.Length+input.Bays.Length).ToArray();
                candidate.UpperSpacingMm=candidate.BeamSpacingsMm.Min();
                common.Trace?.Work("Independent secondary spacing recovery");
                worker.NetworkWarmStart=common.WarmStartNetwork ? best : null;
                try
                {
                    var result=SearchSpacing(worker,candidate);
                    best=result;acceptedDiagnostics=worker.Diagnostics;return true;
                }
                catch(TowerOptimizationException) {return false;}
            };
            Action<int[]> recover=null;
            recover=indices=>
            {
                var current=spacings(best.Input);
                indices=indices.Where(i=>current[i]<maxima[i]).ToArray();
                if(indices.Length==0)return;
                var values=(double[])current.Clone();foreach(int i in indices)values[i]=maxima[i];
                if(restore(values))return;
                if(indices.Length>1)
                {
                    int half=indices.Length/2;recover(indices.Take(half).ToArray());recover(indices.Skip(half).ToArray());
                }
                else
                {
                    int i=indices[0];
                    // A rejected maximum need not mean all intermediate pitches fail.
                    foreach(double pitch in PlywoodSpacing.StandardSpacingsMm.Where(p=>p<maxima[i] && p>current[i]))
                    {
                        values=spacings(best.Input);values[i]=pitch;if(restore(values))break;
                    }
                }
            };
            try
            {
                if(checkedSeedFirst && common.WarmStartNetwork && common.LocalNetworkSearch && !spacings(best.Input).SequenceEqual(maxima))
                {
                    // One checked local probe can restore every category without
                    // launching another complete network enumeration. A failed
                    // probe leaves the ordinary recovery trials available.
                    var restored=TryWarmMaxima(worker,input,best);
                    if(restored!=null)
                    {best=restored;acceptedDiagnostics=worker.Diagnostics;attempted.Add(keyOf(best.Input));}
                }
                // Retain receiving-beam maxima first, then crossings and slab bays.
                // Group successes cost one solve; failed groups split deterministically.
                // At most 2N-1 group trials plus five intermediate trials per element.
                common.Trace?.NetworkPhase("Restoring main-beam secondary spacings");
                recover(Enumerable.Range(0,input.Beams.Length).ToArray());
                common.Trace?.NetworkPhase("Restoring crossing-beam secondary spacings");
                recover(Enumerable.Range(input.Beams.Length+input.Bays.Length,input.CrossingBeams.Length).ToArray());
                common.Trace?.NetworkPhase("Restoring slab secondary spacings");
                recover(Enumerable.Range(input.Beams.Length,input.Bays.Length).ToArray());
            }
            catch(BeamSearchDeadlineException)
            {
                best.SearchTimeLimitReached=true;
                foreach(var beam in best.Beams) {beam.SearchTimeLimitReached=true;beam.MinimumLegCountProven=false;}
            }
            common.Trace?.CheckUserCancellation();
            if(checkedSeedFirst && common.Trace?.BudgetReached!=true)
            {
                worker.FirstCompleteNetworkOnly=false;
                common.Trace?.NetworkPhase("Comparing the complete final design alternatives");
                // Recovery can replace best after setting the previous warm start.
                // Publish the exact recovered input's checked result so final ranking
                // starts with a valid junction-wide bound, rather than rediscovering it.
                worker.NetworkWarmStart=common.WarmStartNetwork ? best : null;
                common.Trace?.Work("Optimizing final recovered network spacing");
                try{best=SearchSpacing(worker,best.Input);acceptedDiagnostics=worker.Diagnostics;}
                catch(TowerOptimizationException){ /* Keep the already checked complete seed. */ }
                catch(BeamSearchDeadlineException)
                {
                    best.SearchTimeLimitReached=true;
                    foreach(var beam in best.Beams){beam.SearchTimeLimitReached=true;beam.MinimumLegCountProven=false;}
                }
            }
            common.Diagnostics=acceptedDiagnostics;return best;
        }
        private static TransferNetworkResult LegacyTransfer(TowerInput common,TransferNetworkInput input)
        {
            try {return SearchSpacing(common,input);}
            catch(TowerOptimizationException)
            {
                var worker=common.CopyForSearch();worker.CoordinateCrossingLayouts=CrossBeamSupportOptimizer.Modern(input);
                foreach(double spacing in PlywoodSpacing.StandardSpacingsMm.Where(x=>x<=input.UpperSpacingMm))
                {
                    common.Trace?.CheckCancellation();common.Trace?.Work("Sheet spacing / crossing junction search");
                    // Preserve the existing junction-clearance gap retries without
                    // redefining 225 mm as a nominal slab design spacing. All active
                    // slab rows are still checked for at least the listed nominal width.
                    var gaps=input.PlywoodSheetLengthMm==0 && CrossBeamSupportOptimizer.Modern(input)
                        ? new[] {spacing,Math.Min(spacing,250),Math.Min(spacing,225)}.Distinct() : new[] {spacing};
                    foreach(double gap in gaps)
                    {
                        var network=input.WithSpacings();network.UpperSpacingMm=spacing;network.LocalRowGapMm=gap;
                        try {var result=SearchSpacing(worker,network);common.Diagnostics=worker.Diagnostics;return result;}
                        catch(TowerOptimizationException) { }
                    }
                }
                throw;
            }
        }
        private static TransferNetworkResult SeparateTransfer(TowerInput common,TransferNetworkInput input)
        {
            TowerOptimizationException failure=null;
            for(int step=0;step<PlywoodSpacing.StandardSpacingsMm.Count;step++)
            {
                var candidate=input.WithSpacings(step);
                if(!candidate.HasIndividualSpacings)
                {
                    var allowed=PlywoodSpacing.StandardSpacingsMm.Where(x=>x<=input.UpperSpacingMm).ToArray();
                    if(step>=allowed.Length)break;
                    candidate.UpperSpacingMm=allowed[step];
                }
                try {return OptimizeCore(common,candidate,true);}
                catch(TowerOptimizationException ex)
                {
                    failure=ex;
                    // A changed reaction split can need a different receiver at the
                    // same leg tier. Compare the complete crossing before excluding
                    // another bay or reducing a permitted nominal spacing.
                    if(common.EngineerWorkflow && !common.CoordinateCrossingLayouts && candidate.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows))
                    {
                        var coordinated=common.CopyForSearch();coordinated.CoordinateCrossingLayouts=true;
                        try {return OptimizeCore(coordinated,candidate,true);}
                        catch(TowerOptimizationException coordinatedFailure){failure=coordinatedFailure;}
                    }
                }
            }
            throw failure;
        }
        // An optimistic economic bound excludes remaining timber, so pruning cannot hide a cheaper layout.
        private sealed class Incumbent
        {
            private double score=double.PositiveInfinity;
            internal double Score=>Volatile.Read(ref score);
            internal void Publish(double value)
            {
                double old;do {old=Score;if(value>=old)return;}while(Interlocked.CompareExchange(ref score,value,old)!=old);
            }
        }
        internal static int MinimumDedicatedLegs(TowerInput common,TransferNetworkInput input)
            =>4*input.CrossingBeams.Count(c=>c.SupportMode==CrossBeamSupportMode.DedicatedTowers
                    || c.SupportMode==CrossBeamSupportMode.Automatic && !CrossBeamSupportOptimizer.PrimaryBearingPossible(common,input,c) && (!CrossBeamSupportOptimizer.ValidFillerGap(c.BottomElevationMm,input.Beams[c.BayNumber-1].BottomElevationMm,out var near)
                        || !CrossBeamSupportOptimizer.ValidFillerGap(c.BottomElevationMm,input.Beams[c.BayNumber].BottomElevationMm,out var far)));

        // A prefix fixes its selected primary axes. If an inner axis cannot
        // retain the secondary-stock end setback inside the clear crossing bay,
        // no opposite receiver, row count or load can repair the direct route.
        // Count only automatic crossings whose stacked route is also impossible.
        internal static int MinimumDedicatedLegsForPrefix(TowerInput common,TransferNetworkInput input,TowerLayout[] known)
        {
            int minimum=MinimumDedicatedLegs(common,input);
            if(!common.EngineerWorkflow || !common.CoordinateCrossingLayouts || !common.OptimizeCrossingSearch || !common.ReuseSearchWork
                || common.Trace?.HasTimeLimit==true || known==null || known.Length==0 || known.Length>input.Beams.Length)return minimum;
            var centres=new double[input.Beams.Length];
            for(int beam=1;beam<centres.Length;beam++)centres[beam]=centres[beam-1]+input.Beams[beam-1].WidthMm/2+input.Bays[beam-1].WidthMm+input.Beams[beam].WidthMm/2;
            foreach(var cross in input.CrossingBeams)
            {
                common.Trace?.CheckCancellation();int bay=cross.BayNumber-1;
                if(cross.SupportMode!=CrossBeamSupportMode.Automatic || !CrossBeamSupportOptimizer.PrimaryBearingPossible(common,input,cross)
                    || CrossBeamSupportOptimizer.ValidFillerGap(cross.BottomElevationMm,input.Beams[bay].BottomElevationMm,out var near)
                    && CrossBeamSupportOptimizer.ValidFillerGap(cross.BottomElevationMm,input.Beams[bay+1].BottomElevationMm,out var far))continue;
                double face=centres[bay]+input.Beams[bay].WidthMm/2,span=input.Bays[bay].WidthMm;
                bool Impossible(int beam,bool left)
                {
                    var receiver=beam<known.Length ? known[beam] : null;if(receiver==null)return false;
                    double width=receiver.WidthMm,offset=receiver.TransverseOffsetMm,end=common.StockEndSetbackMm;
                    double axis=centres[beam]+offset+(left ? width/2 : -width/2);
                    double scale=1+Math.Abs(centres[beam])+Math.Abs(face)+Math.Abs(span)+Math.Abs(axis)+Math.Abs(width)+Math.Abs(offset)+Math.Abs(end)+50000;
                    // Cover the original earliest/latest 1e-7 allowance, source
                    // subtraction/cancellation and every validated stock length.
                    if(!TowerBeamAnalysis.Finite(scale) || scale>1073741824 || width<=0 || span<=0 || end<=0)return false;
                    double margin=1e-6+scale*1e-10,available=left ? axis-face : face+span-axis;
                    return TowerBeamAnalysis.Finite(available) && available<end-margin;
                }
                if(Impossible(bay,true) || Impossible(bay+1,false))minimum+=4;
            }
            return minimum;
        }

        private sealed class EngineerBound
        {
            private int legs=int.MaxValue;
            private readonly int minimumDedicatedLegs;
            private readonly TowerInput common;private readonly TransferNetworkInput input;
            private readonly object improvementGate=new object();private TransferNetworkResult best;private readonly BeamSearchTrace trace;
            internal EngineerBound(TowerInput common,TransferNetworkInput input,BeamSearchTrace trace)
            {
                this.trace=trace;this.common=common;this.input=input;
                // Reserve four legs only where neither stacked bearings nor the
                // new direct-primary route can avoid a dedicated crossing tower.
                minimumDedicatedLegs=MinimumDedicatedLegs(common,input);
            }
            internal int Ceiling(int used,int remaining,int minimum)=>Math.Max(0,Volatile.Read(ref legs)-used-remaining*minimum-minimumDedicatedLegs);
            internal int Ceiling(int used,int reservedLegs)=>Math.Max(0,Volatile.Read(ref legs)-used-reservedLegs-minimumDedicatedLegs);
            internal bool MayImprove(State prefix,int remaining,int minimumLegs,int minimumBays)
            {
                var incumbent=Volatile.Read(ref best);
                if(incumbent==null)return true;int dedicated=MinimumDedicatedLegsForPrefix(common,input,prefix.Beams);
                return EngineerPrefixMayImprove(incumbent,prefix.Legs+remaining*minimumLegs+dedicated,
                    prefix.Narrow,prefix.BaysCount+remaining*minimumBays+dedicated/4,prefix.BracePenalty,
                    prefix.Beams.Sum(b=>b.CantileverImbalanceMm),prefix.Symmetry,prefix.Beams.Sum(b=>b.BraceLocationPenalty),prefix.Separate);
            }
            internal bool MayImprove(State prefix,int reservedLegs,int reservedBays)
            {
                var incumbent=Volatile.Read(ref best);
                if(incumbent==null)return true;int dedicated=MinimumDedicatedLegsForPrefix(common,input,prefix.Beams);
                return EngineerPrefixMayImprove(incumbent,prefix.Legs+reservedLegs+dedicated,
                    prefix.Narrow,prefix.BaysCount+reservedBays+dedicated/4,prefix.BracePenalty,
                    prefix.Beams.Sum(b=>b.CantileverImbalanceMm),prefix.Symmetry,prefix.Beams.Sum(b=>b.BraceLocationPenalty),prefix.Separate);
            }
            internal void Publish(TransferNetworkResult result)
            {
                lock(improvementGate)
                {
                    if(best==null || Better(result,best))
                    {
                        best=result;
                        trace?.NetworkImproved(FormattableString.Invariant($"legs={result.LegCount}; groups={result.Beams.Sum(b=>b.BayCount)}; brace={result.Beams.Sum(b=>b.StandaloneBracePenalty):R}; cantilever={result.Beams.Sum(b=>b.CantileverImbalanceMm):R}; symmetry={result.Beams.Sum(b=>b.SymmetryErrorMm):R}; timber={result.TimberMm:R}"));
                    }
                }
                int value=result.LegCount,old;
                do{old=Volatile.Read(ref legs);if(value>=old)return;}while(Interlocked.CompareExchange(ref legs,value,old)!=old);
            }
        }
        // Optimistic prefix values use zero for every unknown nonnegative
        // ranking contribution. They may reject only a branch already worse at
        // the first unequal engineering criterion. Equal bounds retain timber ties.
        internal static bool EngineerPrefixMayImprove(TransferNetworkResult best,int legs,int narrow,int bays,double brace,double cantilever,double symmetry,double braceLocation,int separate=0)
        {
            if(separate!=best.SeparateSlabCount)return separate<best.SeparateSlabCount;
            if(legs!=best.LegCount)return legs<best.LegCount;
            int comparison=EngineerTowerSearch.Compare(narrow,bays,brace,best.Standalone600Count,best.BracedBayCount,best.StandaloneBracePenalty);
            if(comparison!=0)return comparison<0;
            if(Math.Abs(cantilever-best.CantileverImbalanceMm)>1e-7)return cantilever<best.CantileverImbalanceMm;
            if(Math.Abs(symmetry-best.SymmetryErrorMm)>1e-7)return symmetry<best.SymmetryErrorMm;
            return braceLocation<=best.BraceLocationPenalty+1e-7;
        }
        internal static int SearchWorkers(int requested,int processors)=>Math.Max(1,Math.Min(Math.Min(8,Math.Max(1,processors)),requested));
        private static TransferNetworkResult OptimizeCore(TowerInput common,TransferNetworkInput input,bool separate)
        {
            input.Validate(common); TransferNetworkResult best=null;int tested=0;
            common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift) {LazyLocations=common.LazySearchDiagnostics};var diag=common.Diagnostics;
            if(!CrossBeamSupportOptimizer.MandatoryStocksFit(common,input))throw diag.Exception("No permitted primary stock fits a mandatory crossing tower.");
            var geometryCache=common.ReuseSearchWork ? common.NetworkGeometryWork ?? new TransferredSlabOptimizer.GeometryCache() : null;
            var incumbent=!common.EngineerSearch && common.PruneNetworkBounds && !separate && input.CrossingBeams.All(c=>c.SupportMode==CrossBeamSupportMode.LegacySlabRows) ? new Incumbent() : null;
            var engineerBound=common.EngineerSearch && common.PruneNetworkBounds && !separate ? new EngineerBound(common,input,common.Trace) : null;
            // The final ranking pass can begin with its already checked exact
            // input, rather than waiting for each scenario to rediscover a seed.
            if(common.LocalNetworkSearch && engineerBound!=null && ReferenceEquals(common.NetworkWarmStart?.Input,input))
                engineerBound.Publish(common.NetworkWarmStart);
            var catalogueBound=common.NetworkCatalogueBound;
            if(common.LocalNetworkSearch && engineerBound!=null && catalogueBound!=null && !catalogueBound.Result.SearchTimeLimitReached
                && ReferenceEquals(catalogueBound.SourceInput,input))engineerBound.Publish(catalogueBound.Result);
            common.Trace?.Work("Network primary cover generation");
            double[][] stockCovers;
            using(common.Trace?.Measure(BeamSearchStage.StockCovers))stockCovers=CachedSpanStockCovers(input.DifferentSpans(common.LengthMm) ? SharedInput(common,input) : common,geometryCache);
            if(stockCovers.Length==0)
            {
                diag.Geometry("primary coverage",$"Primary coverage: no available stock combination totals {common.LengthMm-2*common.MaximumConcreteEndOverhangMm:0.###}–{common.LengthMm:0.###} mm.","Review available primary stocks and the maximum concrete end allowance.");
                throw diag.Exception("No primary stock coverage fits the beam; separate slab formwork cannot fix missing beam stock.");
            }
            if(common.WarmStartNetwork && common.FirstCompleteNetworkOnly && !separate && common.NetworkWarmStart!=null)
            {
                var warm=TryWarmNetwork(common,input,stockCovers,geometryCache);
                if(warm!=null)return warm;
            }
            {
                // Every braced group has at least two support stations with a
                // minimum 600 mm bay; independent groups need at least 25 mm.
                // span >= 312.5*n - 25 is an optimistic upper bound on stations,
                // valid for both production and historical staged searches.
                int maximumStations=(int)Math.Floor((common.LengthMm-2*common.StockEndSetbackMm+25)/312.5+1e-9);
                double maximumCapacity=2*Math.Max(0,maximumStations)*common.LegCapacityKn;
                double minimumCoverage=stockCovers.Min(c=>c.Sum());
                for(int beam=0;beam<input.Beams.Length;beam++)
                {
                    double force=input.AreaLoad(input.Beams[beam].DepthMm)*input.Beams[beam].WidthMm*minimumCoverage/1e6;
                    if(force<=maximumCapacity+1e-7)continue;
                    diag.Geometry("B"+(beam+1)+" total leg capacity",$"B{beam+1}: beam-concrete load alone is at least {force:0.###} kN, exceeding {maximumCapacity:0.###} kN total available leg capacity even at the maximum physical support count.","Review the configured permissible leg load or support system.");
                    throw diag.Exception("No main-beam layout can satisfy the configured leg capacity.");
                }
            }
            // Every independent primary run has at least two supports on each of two lines.
            // This is a topology bound over the actual searched covers, not an assumed kN/leg.
            int minimumLegs=input.DifferentSpans(common.LengthMm) ? 4 : stockCovers.Min(c=>c.Length)*4;
            var scenarios=stockCovers.Select(c=>c.Sum()).Distinct().SelectMany(cover=>(input.PlywoodSheetLengthMm>0 ? new[] {0.0} : new[] {0.0,-50,50}).Select(phase=>Tuple.Create(cover,phase))).ToArray();
            if(common.Trace?.HasTimeLimit==true)
                scenarios=scenarios.OrderBy(s=>Math.Abs(s.Item1-(common.LengthMm-common.MaximumConcreteEndOverhangMm))).ThenBy(s=>Math.Abs(s.Item2)).ToArray();
            var outcomes=new ScenarioResult[scenarios.Length];
            var completion=common.FirstCompleteNetworkOnly ? new NetworkSeedCompletion() : null;
            int workers=SearchWorkers(common.NetworkSearchWorkers,Environment.ProcessorCount);
            common.Trace?.Workers(workers);
            using(var numericalWork=new ThreadLocal<TransferredSlabOptimizer.SearchWorkCache>(()=>new TransferredSlabOptimizer.SearchWorkCache()))
            {
                Action<int> run=index=>
                {
                    var worker=common.CopyForSearch();
                    worker.NetworkSeedCompletion=completion;
                    if(completion?.Found==true){outcomes[index]=new ScenarioResult {Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator)};return;}
                    worker.DedicatedCrossCache=new Dictionary<string,DedicatedCrossSearch>();
                    worker.CrossingGeometryWork=new CrossGeometryCache();
                    worker.NetworkNumerics=common.ReuseSearchWork && (common.ReuseNetworkNumerics || common.ReuseTransverseNumerics) ? (common.NetworkNumericalWork ?? numericalWork).Value : null;
                    worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift) {LazyLocations=common.LazySearchDiagnostics};
                    outcomes[index]=SearchScenario(worker,input,stockCovers,geometryCache,incumbent,minimumLegs,scenarios[index].Item1,scenarios[index].Item2,separate,engineerBound);
                };
                if(workers==1)for(int i=0;i<scenarios.Length;i++)run(i);
                else
                {
                    try {Parallel.For(0,scenarios.Length,new ParallelOptions {MaxDegreeOfParallelism=workers,CancellationToken=common.Trace?.CancellationToken??default},run);}
                    catch(AggregateException ex)
                    {common.Trace?.CheckUserCancellation();ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();throw;}
                }
            }
            // Scheduling never resolves a tie: replay improvements in original scenario order.
            foreach(var outcome in outcomes)
            {
                common.Trace?.CheckUserCancellation();diag.Merge(outcome.Diagnostics);tested+=outcome.Tested;
                foreach(var candidate in outcome.Improvements)
                {
                    if(best!=null && !Better(candidate,best))continue;
                    best=candidate;using(common.Trace?.Measure(BeamSearchStage.Movement))CalculateMovement(best,common.Trace);
                }
            }
            if(best==null)
            {
                if(common.Trace?.BudgetReached==true)throw new BeamSearchDeadlineException();
                throw diag.Exception("No valid beam/slab network found within the bounded search.");
            }
            if(CrossBeamSupportOptimizer.Modern(input) && !common.CoordinateCrossingLayouts && !separate)
            {
                // Revisit a passing complete plan too: coordination is an improvement stage, not just a failure fallback.
                var worker=common.CopyForSearch();worker.CoordinateCrossingLayouts=true;
                worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift) {LazyLocations=common.LazySearchDiagnostics};
                worker.DedicatedCrossCache=new Dictionary<string,DedicatedCrossSearch>();
                // Refinement needs the same exact-response reuse as the initial scenario search.
                worker.NetworkNumerics=common.ReuseSearchWork && (common.ReuseNetworkNumerics || common.ReuseTransverseNumerics) ? common.NetworkNumericalWork?.Value ?? new TransferredSlabOptimizer.SearchWorkCache() : null;
                var refined=SearchScenario(worker,input,stockCovers,geometryCache,null,minimumLegs,best.SlabCoveredLengthMm,best.RowPhaseMm,separate);
                diag.Merge(refined.Diagnostics);tested+=refined.Tested;
                foreach(var candidate in refined.Improvements)if(Better(candidate,best)){best=candidate;using(common.Trace?.Measure(BeamSearchStage.Movement))CalculateMovement(best,common.Trace);}
            }
            best.SearchTimeLimitReached=common.Trace?.BudgetReached==true;
            foreach(var beam in best.Beams) {beam.SearchTimeLimitReached=best.SearchTimeLimitReached;if(best.SearchTimeLimitReached)beam.MinimumLegCountProven=false;}
            best.Tested=tested;return best;
        }
        private sealed class ScenarioResult
        {
            internal TowerSearchDiagnostics Diagnostics;internal int Tested;
            internal readonly List<TransferNetworkResult> Improvements=new List<TransferNetworkResult>();
        }
        private static TransferNetworkResult TryWarmMaxima(TowerInput common,TransferNetworkInput requested,TransferNetworkResult previous)
        {
            common.Trace?.CheckCancellation();common.Trace?.Increment(BeamSearchCounter.WarmMaximaProbes);
            var probe=common.CopyForSearch();probe.FirstCompleteNetworkOnly=true;probe.NetworkWarmStart=previous;
            // SearchWithCustom has restored the project permissions on common.
            // Use that complete permitted cover family for this additional seed.
            probe.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift) {LazyLocations=common.LazySearchDiagnostics};
            var candidate=requested.WithSpacings();candidate.UpperSpacingMm=candidate.BeamSpacingsMm.Min();
            try
            {
                candidate.Validate(probe);
                if(!CrossBeamSupportOptimizer.MandatoryStocksFit(probe,candidate))return null;
                common.Trace?.Work("Warm-only restoration of requested secondary spacings");
                double[][] covers;
                using(common.Trace?.Measure(BeamSearchStage.StockCovers))
                    covers=CachedSpanStockCovers(candidate.DifferentSpans(probe.LengthMm) ? SharedInput(probe,candidate) : probe,common.NetworkGeometryWork);
                if(covers.Length==0)return null;
                var result=TryWarmNetwork(probe,candidate,covers,common.NetworkGeometryWork);
                if(result==null)return null;
                common.Diagnostics=probe.Diagnostics;common.Trace?.Increment(BeamSearchCounter.WarmMaximaSuccesses);return result;
            }
            catch(ArgumentException){return null;}
        }
        private static TransferNetworkResult TryWarmNetwork(TowerInput common,TransferNetworkInput input,double[][] covers,TransferredSlabOptimizer.GeometryCache geometryCache)
        {
            common.Trace?.Increment(BeamSearchCounter.WarmNetworkAttempts);
            var previous=common.NetworkWarmStart;
            if(previous.Beams.Length!=input.Beams.Length || previous.Bays.Length!=input.Bays.Length || previous.Bays.Any(b=>b==null))return null;
            var worker=common.CopyForSearch();worker.NetworkNumerics=common.NetworkNumericalWork?.Value;
            worker.CrossingGeometryWork=new CrossGeometryCache();worker.DedicatedCrossCache=new Dictionary<string,DedicatedCrossSearch>();
            double cover=previous.SlabCoveredLengthMm,phase=previous.RowPhaseMm;
            var bays=new UpperTransferPlan[input.Bays.Length];
            for(int bay=0;bay<bays.Length;bay++)
            {
                var widths=EffectiveWidthsMm(worker,input,bay,cover,phase);if(widths==null)return null;
                var old=previous.Bays[bay];
                bays[bay]=Options(worker,input,bay,Math.Max(widths.Max(),input.BaySpacing(bay)),"Spacing recovery").FirstOrDefault(p=>
                    p.StockLengthMm==old.StockLengthMm && p.EdgeMm==old.EdgeMm && p.NearFootMm==old.NearFootMm && p.FarFootMm==old.FarFootMm);
                if(bays[bay]==null){common.Trace?.Increment(BeamSearchCounter.WarmSlabOptionMissing);return null;}
            }
            var beams=new TowerLayout[input.Beams.Length];
            for(int beam=0;beam<beams.Length;beam++)
            {
                common.Trace?.CheckCancellation();
                var old=previous.Beams[beam];
                beams[beam]=Solve(worker,input,beam,beam==0 ? null : bays[beam-1],beam==bays.Length ? null : bays[beam],cover,phase,covers,geometryCache,
                    int.MaxValue,old.WidthMm,old.TransverseOffsetMm,fixedLayout:old);
                if(beams[beam]==null && common.FirstCompleteNetworkOnly && common.LocalNetworkSearch)
                {
                    common.Trace?.Increment(BeamSearchCounter.WarmReceiverRepairAttempts);
                    // This is a feasibility seed. Retain already checked bays
                    // and neighboring receivers while repairing only this beam.
                    // Try new supports on its previous permitted stock sequence;
                    // failed repairs still return to the ordinary network search.
                    var stock=old.StockPlan.Pieces.Select(p=>p.StockLengthMm).ToArray();
                    beams[beam]=SolveReceiver(worker,input,beam,beam==0 ? null : bays[beam-1],beam==bays.Length ? null : bays[beam],cover,phase,covers,geometryCache,
                        int.MaxValue,old.WidthMm,old.TransverseOffsetMm,selectedStockCover:stock);
                    if(beams[beam]==null)
                        beams[beam]=SolveReceiver(worker,input,beam,beam==0 ? null : bays[beam-1],beam==bays.Length ? null : bays[beam],cover,phase,covers,geometryCache,
                            int.MaxValue,selectedStockCover:stock);
                    if(beams[beam]!=null){beams[beam].MinimumLegCountProven=false;common.Trace?.Increment(BeamSearchCounter.WarmReceiverRepairSuccesses);}
                }
                if(beams[beam]==null)return null;
            }
            var result=new TransferNetworkResult {Input=input,Beams=beams,Bays=bays,BeamCentresMm=previous.BeamCentresMm,SlabCoveredLengthMm=cover,RowPhaseMm=phase,SlabLoadKn=beams.Sum(b=>b.Transfer.SlabLoadKn)};
            if(!CrossBeamSupportOptimizer.Complete(worker,result))return null;
            CalculateMovement(result,common.Trace);result.Tested=1;
            common.Trace?.Increment(BeamSearchCounter.CompleteNetworkCandidates);common.Trace?.Increment(BeamSearchCounter.WarmNetworkCandidates);
            return result;
        }
        private static TransferNetworkResult Result(TransferNetworkInput input,State state,TowerLayout last,double cover,double phase)
        {
            var beams=state.Beams.Concat(new[] {last}).ToArray();
            var centres=new double[beams.Length];for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+input.Beams[i-1].WidthMm/2+input.Bays[i-1].WidthMm+input.Beams[i].WidthMm/2;
            return new TransferNetworkResult {Input=input,Beams=beams,Bays=state.Bays,BeamCentresMm=centres,SlabLoadKn=beams.Sum(b=>b.Transfer.SlabLoadKn),SlabCoveredLengthMm=cover,RowPhaseMm=phase};
        }
        private static ScenarioResult SearchScenario(TowerInput common,TransferNetworkInput input,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,Incumbent incumbent,int minimumLegs,double cover,double phase,bool separate,EngineerBound engineerBound=null)
        {
            var diag=common.Diagnostics;TransferNetworkResult best=null;int tested=0;
            var outcome=new ScenarioResult {Diagnostics=diag};
            try
            {
                common.Trace?.Increment(BeamSearchCounter.CoverageVariants);common.Trace?.Work("Network shared slab H20 options");
                common.Trace?.Context(System.FormattableString.Invariant($"Coverage={cover:R} mm; row phase={phase:R} mm"));
                var stations=RowStations(common,input,cover,phase);
                int rows=stations.Length;double first=stations[0]-(common.LengthMm-cover)/2;
                if(input.PlywoodSheetLengthMm==0 && (first<=0 || first+(rows-1)*input.RowGapMm>=cover || (rows>1 && (first+input.RowGapMm/2>input.UpperSpacingMm+1e-7 || cover-(first+(rows-1)*input.RowGapMm)+input.RowGapMm/2>input.UpperSpacingMm+1e-7))))return outcome;
                int upperFeet=input.HasIndividualSpacings ? input.SlabRows(common.LengthMm,cover).Sum(r=>r.Length)*2 : input.Bays.Length*2*rows;
                if(incumbent!=null && upperFeet*TowerOptimizer.UpperFootTimberEquivalentMm+minimumLegs*input.Beams.Length*TowerOptimizer.TowerLegTimberEquivalentMm>incumbent.Score+.01) {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);return outcome;}
                double edge=input.SharedRowStart(common.LengthMm,cover);
                var invalidBays=new HashSet<int>(input.CrossingBeams.Where(c=>c.PositionMm-c.WidthMm/2<edge-1e-7 || c.PositionMm+c.WidthMm/2>edge+cover+1e-7).Select(c=>c.BayNumber-1));
                for(int bay=0;bay<input.Bays.Length;bay++)if(input.LegacyBayPlywoodLimitsMm?[bay]==0)
                {
                    diag.Geometry($"S{bay+1} plywood",$"S{bay+1}: no permitted nominal slab spacing passes the plywood checks.","Select stronger plywood, revise the load or design separate slab formwork.");
                    invalidBays.Add(bay);
                }
                if(!separate && invalidBays.Count>0) {diag.Geometry("crossing coverage","Crossing footprint is outside primary coverage.","Review crossing position/width and primary stock end coverage.");return outcome;}
                var rowLoads=input.Bays.Select((b,i)=>invalidBays.Contains(i) ? null : EffectiveWidthsMm(common,input,i,cover,phase)).ToArray();
                var slabRows=rowLoads.Select(w=>w?.Count(x=>x>0)??0).ToArray();
                UpperTransferPlan[][] options;
                using(common.Trace?.Measure(BeamSearchStage.UpperOptions))
                    options=input.Bays.Select((b,i)=>rowLoads[i]==null ? new UpperTransferPlan[0] : Options(common,input,i,Math.Max(rowLoads[i].Max(),input.BaySpacing(i)),$"T{Array.IndexOf(rowLoads[i],rowLoads[i].Max())+1} · x={stations[Array.IndexOf(rowLoads[i],rowLoads[i].Max())]:0.###} mm").ToArray()).ToArray();
                if(separate)options=options.Select(o=>o.Concat(new UpperTransferPlan[] {null}).ToArray()).ToArray();
                if(options.Any(o=>o.Length==0))return outcome;
                if(common.CoordinateCrossingLayouts)return CoordinatedScenario(common,input,stockCovers,geometryCache,options,cover,phase,rows,engineerBound);
                Func<int,int,int> ceiling=(previous,remaining)=>incumbent==null || double.IsPositiveInfinity(incumbent.Score) ? int.MaxValue : (int)Math.Floor((incumbent.Score+.01-upperFeet*TowerOptimizer.UpperFootTimberEquivalentMm)/TowerOptimizer.TowerLegTimberEquivalentMm)-previous-remaining*minimumLegs;
                var states=new Dictionary<int,State>();
                for(int option=0;option<options[0].Length;option++)
                {
                    tested++;var beam=Solve(common,input,0,null,options[0][option],cover,phase,stockCovers,geometryCache,ceiling(0,input.Beams.Length-1));if(beam==null)continue;
                    states[option]=new State {Beams=new[] {beam},Bays=new[] {options[0][option]},Legs=beam.LegCount,Narrow=beam.Standalone600Count,BaysCount=beam.BayCount,BracePenalty=beam.StandaloneBracePenalty,Separate=options[0][option]==null ? 1 : 0,Timber=LowerTimber(beam)+(options[0][option]?.StockLengthMm??0)*slabRows[0],Symmetry=beam.SymmetryErrorMm};
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
                        }
                        tested++;var beam=Solve(common,input,i,previous.Bays.Last(),right,cover,phase,stockCovers,geometryCache,limit);if(beam==null)continue;
                        int legs=previous.Legs+beam.LegCount,narrow=previous.Narrow+beam.Standalone600Count,baysCount=previous.BaysCount+beam.BayCount;double penalty=previous.BracePenalty+beam.StandaloneBracePenalty;double timber=previous.Timber+LowerTimber(beam)+(right?.StockLengthMm??0)*slabRows[i];
                        if(old==null || count<old.Separate || count==old.Separate && Better(common.EngineerSearch,legs,narrow,baysCount,penalty,timber,old.Legs,old.Narrow,old.BaysCount,old.BracePenalty,old.Timber,previous.Symmetry+beam.SymmetryErrorMm,old.Symmetry))next[option]=new State {Beams=previous.Beams.Concat(new[] {beam}).ToArray(),Bays=previous.Bays.Concat(new[] {right}).ToArray(),Legs=legs,Narrow=narrow,BaysCount=baysCount,BracePenalty=penalty,Separate=count,Timber=timber,Symmetry=previous.Symmetry+beam.SymmetryErrorMm};
                    }
                    states=next;
                }
                foreach(var state in states.Values)
                {
                    int limit=ceiling(state.Legs,0);
                    if(best!=null && common.PruneNetworkBounds && !CrossBeamSupportOptimizer.Modern(input) && state.Separate==best.SeparateSlabCount)
                        limit=common.EngineerSearch ? Math.Min(limit,best.LegCount-state.Legs) : Math.Min(limit,(int)Math.Floor((best.EconomyScoreMm+.01-state.Timber-state.Bays.Select((b,i)=>b==null ? 0 : slabRows[i]).Sum()*2*TowerOptimizer.UpperFootTimberEquivalentMm)/TowerOptimizer.TowerLegTimberEquivalentMm)-state.Legs);
                    tested++;var last=Solve(common,input,input.Beams.Length-1,state.Bays.Last(),null,cover,phase,stockCovers,geometryCache,limit);if(last==null)continue;
                    var candidate=Result(input,state,last,cover,phase);
                    bool crossingPassed;
                    using(common.Trace?.Measure(BeamSearchStage.CrossingSupports))crossingPassed=CrossBeamSupportOptimizer.Complete(common,candidate);
                    if(!crossingPassed)continue;
                    incumbent?.Publish(candidate.EconomyScoreMm);
                    if(best==null || Better(candidate,best)) {best=candidate;outcome.Improvements.Add(best);}
                    common.Trace?.Increment(BeamSearchCounter.CompleteNetworkCandidates);
                }
            }
            catch(BeamSearchDeadlineException) {common.Trace?.CheckUserCancellation();}
            outcome.Tested=tested;return outcome;
        }
        // This attestation is minted only around SolveReceiverCore's native
        // obstacle AND callback. It is local to one width collector: the whole
        // receiver-search cache still excludes every external-obstacle scope.
        // Native worker inputs are unchanged during a sequential solve. Snapshot
        // revalidation guards publication/consumption; it does not authenticate
        // an arbitrary caller's mutate-and-restore history during evaluation.
        private sealed class NativeExternalWidthProof
        {
            private struct SolidSnapshot
            {
                internal FormworkSolid Solid;internal long X0,X1,Y0,Y1,Z0,Z1;
                internal bool IsLeg,IsTowerEnvelope;internal string TowerOwner;
                internal SolidSnapshot(FormworkSolid s)
                {
                    Solid=s;X0=Bits(s.X0);X1=Bits(s.X1);Y0=Bits(s.Y0);Y1=Bits(s.Y1);Z0=Bits(s.Z0);Z1=Bits(s.Z1);
                    IsLeg=s.IsLeg;IsTowerEnvelope=s.IsTowerEnvelope;TowerOwner=s.TowerOwner;
                }
                internal bool Matches(FormworkSolid s)=>ReferenceEquals(Solid,s) && X0==Bits(s.X0) && X1==Bits(s.X1)
                    && Y0==Bits(s.Y0) && Y1==Bits(s.Y1) && Z0==Bits(s.Z0) && Z1==Bits(s.Z1)
                    && IsLeg==s.IsLeg && IsTowerEnvelope==s.IsTowerEnvelope && TowerOwner==s.TowerOwner;
            }
            private readonly TowerInput common,input;
            private readonly FormworkSolid[] obstacles;
            private readonly SolidSnapshot[] solids;
            private readonly long[] transforms;
            private readonly FormworkObstacleIndex commonIndex;
            private readonly Dictionary<double,TowerLayout> collector;
            private readonly Func<TowerLayout,bool> nativeAcceptance,lowerAcceptance;
            private readonly Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation;
            private Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]> geometryFilter;
            private FormworkObstacleIndex builtIndex;
            private bool valid,sealedCallbacks;
            internal readonly Func<TowerLayout,bool> Acceptance;
            private static long Bits(double value)=>BitConverter.DoubleToInt64Bits(value);
            private static long[] Transforms(TowerInput common,TowerInput input)=>new[] {Bits(common.ObstacleOriginXmm),Bits(common.ObstacleCentreYmm),Bits(common.ObstacleBottomMm),
                Bits(input.ObstacleOriginXmm),Bits(input.ObstacleCentreYmm),Bits(input.ObstacleBottomMm)};
            internal NativeExternalWidthProof(TowerInput common,TowerInput input)
            {
                this.common=common;this.input=input;obstacles=input.ExternalObstacles;commonIndex=common.ExternalObstacleIndex;collector=input.ReceiverAlternatives;
                nativeAcceptance=input.TransferLayoutAcceptance;lowerAcceptance=input.TransferLowerAcceptance;preparation=input.TransferReceiverPreparation;
                transforms=Transforms(common,input);solids=new SolidSnapshot[obstacles.Length];
                valid=input.ExternalObstacleIndex==null && ReferenceEquals(common.ExternalObstacles,obstacles);
                for(int i=0;i<solids.Length;i++)
                {
                    var s=obstacles[i];if(s==null){valid=false;continue;}
                    solids[i]=new SolidSnapshot(s);
                    if(!TowerBeamAnalysis.Finite(s.X0) || !TowerBeamAnalysis.Finite(s.X1) || s.X0>s.X1
                        || !TowerBeamAnalysis.Finite(s.Y0) || !TowerBeamAnalysis.Finite(s.Y1) || s.Y0>s.Y1
                        || !TowerBeamAnalysis.Finite(s.Z0) || !TowerBeamAnalysis.Finite(s.Z1) || s.Z0>s.Z1)valid=false;
                }
                Acceptance=Accept;
            }
            internal void SealCallbacks(){geometryFilter=input.TransferGeometryFilter;sealedCallbacks=true;}
            private bool SameContext()=>ReferenceEquals(common.ExternalObstacles,obstacles) && ReferenceEquals(input.ExternalObstacles,obstacles)
                && ReferenceEquals(common.ExternalObstacleIndex,commonIndex)
                && transforms[0]==Bits(common.ObstacleOriginXmm) && transforms[1]==Bits(common.ObstacleCentreYmm) && transforms[2]==Bits(common.ObstacleBottomMm)
                && transforms[3]==Bits(input.ObstacleOriginXmm) && transforms[4]==Bits(input.ObstacleCentreYmm) && transforms[5]==Bits(input.ObstacleBottomMm);
            private bool SameCallbacks()=>sealedCallbacks && ReferenceEquals(input.TransferLayoutAcceptance,Acceptance)
                && ReferenceEquals(input.ReceiverAlternatives,collector) && ReferenceEquals(input.TransferLowerAcceptance,lowerAcceptance)
                && ReferenceEquals(input.TransferReceiverPreparation,preparation) && ReferenceEquals(input.TransferGeometryFilter,geometryFilter);
            private bool SameSnapshot()
            {
                if(!SameContext())return false;
                for(int i=0;i<solids.Length;i++)if(!solids[i].Matches(obstacles[i]))return false;
                return true;
            }
            private bool Accept(TowerLayout layout)
            {
                // Observe index provenance at the native call, rather than
                // trusting an arbitrary injected index with the same array.
                var before=input.ExternalObstacleIndex;
                if(valid && (!SameCallbacks() || !SameContext() || !ReferenceEquals(before,builtIndex) || builtIndex==null && !SameSnapshot()))valid=false;
                bool fits=CrossBeamSupportOptimizer.ExternalLayoutFits(input,layout);
                if(valid)
                {
                    var after=input.ExternalObstacleIndex;
                    if(before==null && after!=null && SameSnapshot())builtIndex=after;
                    else if(!ReferenceEquals(after,builtIndex))valid=false;
                }
                bool accepted=fits && (nativeAcceptance==null || nativeAcceptance(layout));
                if(valid && (!SameCallbacks() || !SameContext() || !ReferenceEquals(input.ExternalObstacleIndex,builtIndex)))valid=false;
                return accepted;
            }
            internal bool Matches(TowerInput common,TowerInput input)=>valid && sealedCallbacks && builtIndex!=null
                && ReferenceEquals(this.common,common) && ReferenceEquals(this.input,input)
                && ReferenceEquals(input.ExternalObstacleIndex,builtIndex) && SameCallbacks() && SameSnapshot();
            internal bool Matches()=>Matches(common,input);
        }
        // Per-call collector, never serialized or retained as a complete search
        // cache. A fresh normal count-eight completion certifies only the failed
        // smaller station subset of missing widths; later tiers remain open.
        internal sealed class ReceiverWidthAlternatives : Dictionary<double,TowerLayout>
        {
            private readonly HashSet<double> failedWidths=new HashSet<double>();
            private int rawCeiling,throughLegs,effectiveCeiling;
            private NativeExternalWidthProof nativeExternal,publishedExternal;
            // The opaque value must be the private native callback binding;
            // arbitrary external callbacks cannot mint that attestation.
            internal void BindNativeExternal(object binding)=>nativeExternal=binding as NativeExternalWidthProof;
            internal int ThroughLegs=>throughLegs;
            internal bool Complete(TowerInput common,TowerInput input,TowerLayout result,bool fullSearchCompleted,bool restricted)
            {
                failedWidths.Clear();throughLegs=0;rawCeiling=0;effectiveCeiling=0;publishedExternal=null;
                if(!fullSearchCompleted || restricted || result?.Transfer?.EngineerReceiverCount!=8
                    || !common.EngineerSearch || !common.LocalNetworkSearch || !common.ReuseSearchWork || !common.ReuseReceiverSearches || !common.OptimizeCrossingSearch
                    || common.FirstCompleteNetworkOnly || common.NetworkSeedCompletion?.Found==true || common.Trace?.HasTimeLimit==true || input.Trace?.HasTimeLimit==true
                    || common.Trace?.BudgetReached==true || input.Trace?.BudgetReached==true || result.SearchTimeLimitReached
                    || input.EngineerPlan==null
                    || input.TransferredSlab==null || input.TransferredSlab.FixedTowerWidthMm.HasValue || input.ReceiverTransverseOffsetMm!=0 || result.TransverseOffsetMm!=0
                    || !ReferenceEquals(input.ReceiverAlternatives,this))return false;
                // All refinements through eight failed for each missing width
                // at every station count through this winner. Higher station
                // counts remain unproven and keep their original search order.
                common.Trace?.CheckCancellation();input.Trace?.CheckCancellation();
                if((common.ExternalObstacles.Length!=0 || input.ExternalObstacles.Length!=0 || nativeExternal!=null)
                    && (nativeExternal==null || !nativeExternal.Matches(common,input)))return false;
                foreach(double width in new[] {1000.0,1524,1500,2000})
                    if(Math.Abs(result.WidthMm-width)>=1e-7 && !ContainsKey(width))failedWidths.Add(width);
                if(failedWidths.Count==0)return false;
                rawCeiling=input.NetworkLegCeiling;effectiveCeiling=EffectiveReceiverLegCeiling(input);throughLegs=result.LegCount;publishedExternal=nativeExternal;return true;
            }
            internal bool Rejects(double width,int ceiling,BeamSearchTrace trace)
            {
                trace?.CheckCancellation();
                // The object belongs to this exact receiving call. A larger raw
                // budget or another scope must not consume its failure proof.
                return FailedThroughStations(width,ceiling,trace)>0 && Math.Min(ceiling/2*2,effectiveCeiling)<=throughLegs;
            }
            internal int FailedThroughStations(double width,int ceiling,BeamSearchTrace trace)
            {
                trace?.CheckCancellation();
                return throughLegs>0 && ceiling<=rawCeiling && failedWidths.Contains(width) && !ContainsKey(width)
                    && (publishedExternal==null || publishedExternal.Matches()) ? throughLegs/2 : 0;
            }
        }
        private sealed class ReceiverSearchResult
        {
            internal int Ceiling;internal TowerLayout Best;internal TowerLayout[] Choices;
        }
        private static ScenarioResult CoordinatedScenario(TowerInput common,TransferNetworkInput input,double[][] covers,TransferredSlabOptimizer.GeometryCache geometryCache,UpperTransferPlan[][] options,double cover,double phase,int rows,EngineerBound engineerBound=null,Dictionary<Tuple<int,UpperTransferPlan,UpperTransferPlan>,ReceiverSearchResult> baseReceivers=null)
        {
            var outcome=new ScenarioResult {Diagnostics=common.Diagnostics};
            // Future reserves use actual local covers. The physical aggregate
            // is independent of all upper-foot alternatives and terminal reroutes.
            var aggregate=engineerBound==null ? null : ReceiverAggregateLoadBound.TryCreate(common,input,cover,
                options.Select((o,bay)=>o.Length>0 && o.All(p=>p!=null && ReceiverAggregateLoadBound.SymmetricPlan(p,input.Bays[bay].WidthMm))).All(x=>x),geometryCache);
            aggregate?.BindRetainedNormalOptions(options);
            var topology=engineerBound!=null && (input.DifferentSpans(common.LengthMm) || aggregate!=null)
                ? new ReceiverTopologyReserve(common,input,covers,geometryCache,aggregate) : null;
            var solveCache=new Dictionary<Tuple<int,UpperTransferPlan,UpperTransferPlan>,Tuple<int,TowerLayout[]>>();
            baseReceivers=baseReceivers??new Dictionary<Tuple<int,UpperTransferPlan,UpperTransferPlan>,ReceiverSearchResult>();
            var crossCaches=new Dictionary<int,Dictionary<string,DedicatedCrossSearch>>();
            // Dedicated central towers depend on crossing concrete and their own geometry.
            // Reuse them across prefixes; every receiver load and clash is still rechecked.
            var sharedCrossCache=common.ReuseSearchWork ? common.DedicatedCrossCache ?? new Dictionary<string,DedicatedCrossSearch>() : null;
            TowerLayout[] Receivers(int beam,UpperTransferPlan left,UpperTransferPlan right,int ceiling)
            {
                var key=Tuple.Create(beam,left,right);
                // A search performed with a tighter ceiling is not evidence for
                // an otherwise identical prefix with more available legs.
                if(solveCache.TryGetValue(key,out var saved) && saved.Item1>=ceiling)return saved.Item2;
                TowerLayout[] receivers;
                var choices=new List<TowerLayout>();
                TowerLayout best;
                if(common.OptimizeCrossingSearch && common.ReuseSearchWork && baseReceivers.TryGetValue(key,out var baseline) && baseline.Ceiling>=ceiling)
                {best=baseline.Best;choices.AddRange(baseline.Choices);}
                else
                {
                var alternatives=common.EngineerSearch && common.ReuseSearchWork ? new ReceiverWidthAlternatives() : null;
                best=SolveUniversal(aggregate,common,input,beam,left,right,cover,phase,covers,geometryCache,ceiling,alternatives:alternatives);if(best!=null)choices.Add(best);
                // The unrestricted solve already searches all these widths. A failed
                // solve proves each centred fixed-width subset fails too; offset searches
                // below remain necessary because they change the receiving geometry.
                if(best!=null || !common.ReuseSearchWork)foreach(double width in new[] {1000.0,1524,1500,2000})
                {
                    if(best!=null && Math.Abs(best.WidthMm-width)<1e-7)continue;
                    if(alternatives!=null && alternatives.Rejects(width,ceiling,common.Trace))
                    {common.Trace?.Increment(BeamSearchCounter.ReceiverWidthFailuresReused);continue;}
                    TowerLayout alternative;
                    if(alternatives==null || !alternatives.TryGetValue(width,out alternative))alternative=SolveUniversal(aggregate,common,input,beam,left,right,cover,phase,covers,geometryCache,ceiling,width,certifiedFailedStations:alternatives?.FailedThroughStations(width,ceiling,common.Trace)??0);
                    if(alternative!=null)choices.Add(alternative);
                }
                foreach(double offset in new[] {-25.0,25})
                {
                    var alternative=SolveUniversal(aggregate,common,input,beam,left,right,cover,phase,covers,geometryCache,ceiling,1000,offset);
                    if(alternative!=null)choices.Add(alternative);
                }
                if(common.OptimizeCrossingSearch && common.ReuseSearchWork)baseReceivers[key]=new ReceiverSearchResult {Ceiling=ceiling,Best=best,Choices=choices.ToArray()};
                }
                if(common.EngineerSearch && common.EngineerJunctionExtraTiers>0 && best!=null)
                {
                    // A sparse receiver may pass its local loads but obstruct the
                    // crossing tower. Keep the next leg tiers until the whole junction fits.
                    foreach(int extra in Enumerable.Range(1,common.EngineerJunctionExtraTiers))
                    {
                        var larger=common.CopyForSearch();larger.EngineerMinimumStations=best.PositionsMm.Length+extra;
                        var alternative=SolveUniversal(aggregate,larger,input,beam,left,right,cover,phase,covers,geometryCache,larger.EngineerMinimumStations*2,best.WidthMm);
                        if(alternative!=null)choices.Add(alternative);
                    }
                }
                receivers=choices.OrderBy(l=>common.EngineerSearch ? l.LegCount : TransferredSlabOptimizer.Economy(l)).ThenBy(l=>l.Standalone600Count).ThenBy(l=>l.BayCount).ThenBy(l=>l.StandaloneBracePenalty).ThenBy(l=>l.CantileverImbalanceMm).ThenBy(l=>l.SymmetryErrorMm).ThenBy(l=>l.BraceLocationPenalty).ToArray();solveCache[key]=Tuple.Create(ceiling,receivers);return receivers;
            }
            State Extend(State previous,UpperTransferPlan right,TowerLayout receiver,int beam)
            {
                common.Trace?.CheckCancellation();
                outcome.Tested++;var candidate=new State {Beams=previous.Beams.Concat(new[] {receiver}).ToArray(),Bays=beam==input.Bays.Length ? previous.Bays : previous.Bays.Concat(new[] {right}).ToArray(),Separate=previous.Separate+(beam<input.Bays.Length && right==null ? 1 : 0),Legs=previous.Legs+receiver.LegCount,Narrow=previous.Narrow+receiver.Standalone600Count,BaysCount=previous.BaysCount+receiver.BayCount,BracePenalty=previous.BracePenalty+receiver.StandaloneBracePenalty,Symmetry=previous.Symmetry+receiver.SymmetryErrorMm,Timber=previous.Timber+LowerTimber(receiver)+(right==null ? 0 : right.StockLengthMm*EffectiveWidthsMm(common,input,beam,cover,phase).Count(x=>x>0))};
                if(common.LocalNetworkSearch && engineerBound!=null && !(topology==null
                    ? engineerBound.MayImprove(candidate,input.Beams.Length-beam-1,covers.Min(c=>c.Length)*4,covers.Min(c=>c.Length))
                    : engineerBound.MayImprove(candidate,topology.FutureLegs(beam),topology.FutureBays(beam))))
                {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);return null;}
                if(beam>0)
                {
                    var network=input.HasIndividualSpacings ? input.WithSpacings(beamCount:beam+1) : new TransferNetworkInput {LocalRowGapMm=input.LocalRowGapMm,PlywoodProfileId=input.PlywoodProfileId,PlywoodDeflectionDenominator=input.PlywoodDeflectionDenominator,PlywoodSheetLengthMm=input.PlywoodSheetLengthMm,Beams=input.Beams.Take(beam+1).ToArray(),Bays=input.Bays.Take(beam).ToArray(),CrossingBeams=input.CrossingBeams.Where(c=>c.BayNumber<=beam).ToArray(),UpperSpacingMm=input.UpperSpacingMm,RequestedUpperSpacingMm=input.RequestedUpperSpacingMm,CustomLoads=input.CustomLoads,LiveKnM2=input.LiveKnM2,FormworkKnM2=input.FormworkKnM2};
                    var centres=new double[beam+1];for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+input.Beams[i-1].WidthMm/2+input.Bays[i-1].WidthMm+input.Beams[i].WidthMm/2;
                    var partial=new TransferNetworkResult {Input=network,Beams=candidate.Beams,Bays=candidate.Bays.Take(beam).ToArray(),BeamCentresMm=centres,SlabCoveredLengthMm=cover,RowPhaseMm=phase,SlabLoadKn=candidate.Beams.Sum(l=>l.Transfer.SlabLoadKn)};
                    // Cache keys include the crossing index, concrete geometry and absolute
                    // position, so inserted crossings cannot reuse another prefix's labels.
                    if(!crossCaches.TryGetValue(beam,out var cache))crossCaches[beam]=cache=new Dictionary<string,DedicatedCrossSearch>();
                    var worker=common.CopyForSearch();worker.DedicatedCrossCache=sharedCrossCache ?? cache;
                    bool crossingPassed;
                    bool terminal=beam==input.Beams.Length-1;
                    var prefixCache=common.ReuseReceiverSearches ? common.NetworkPrefixWork : null;
                    var prefixKey=prefixCache==null ? null : new CheckedPrefixKey(common,partial);
                    // A terminal failure needs no drawing result. Passing terminal
                    // combinations still rebuild their resolved members and rows.
                    if(prefixCache!=null && prefixCache.Get(prefixKey,terminal,out var checkedPrefix))
                    {
                        common.Trace?.Increment(BeamSearchCounter.CheckedPrefixCacheHits);crossingPassed=checkedPrefix.Passed;
                        common.Diagnostics.Merge(checkedPrefix.Diagnostics);
                    }
                    else
                    {
                        worker.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift) {LazyLocations=common.LazySearchDiagnostics};
                        using(common.Trace?.Measure(BeamSearchStage.CrossingSupports))crossingPassed=CrossBeamSupportOptimizer.Complete(worker,partial,beam<input.Beams.Length-1);
                        common.Diagnostics.Merge(worker.Diagnostics);
                        if(prefixCache!=null && (!terminal || !crossingPassed) && common.Trace?.BudgetReached!=true && common.NetworkSeedCompletion?.Found!=true)
                        {
                            common.Trace?.CheckCancellation();
                            prefixCache.Store(prefixKey,new NetworkPrefixCache.Entry {Passed=crossingPassed,Diagnostics=worker.Diagnostics},terminal);
                        }
                    }
                    if(!crossingPassed)return null;
                    if(beam==input.Beams.Length-1) {outcome.Improvements.Add(partial);engineerBound?.Publish(partial);common.Trace?.Increment(BeamSearchCounter.CompleteNetworkCandidates);common.NetworkSeedCompletion?.Complete();return null;}
                }
                return candidate;
            }
            bool Seed(State previous,int beam,ref int remaining)
            {
                // Close slab feet reduce receiving H20 cantilevers and timber
                // intrusion into a narrow junction. Try that complete path before
                // the least-timber slab stocks; the ordinary ranked search below
                // still compares every retained passing option.
                var seedOptions=beam==input.Bays.Length ? new UpperTransferPlan[] {null} : common.OptimizeCrossingSearch
                    ? options[beam].OrderBy(o=>o==null ? double.PositiveInfinity : o.NearFootMm+o.FarFootMm).ThenBy(o=>o?.StockLengthMm??double.PositiveInfinity).Take(2) : options[beam].Take(2);
                foreach(var right in seedOptions)
                {
                    if(common.NetworkSeedCompletion?.Found==true)return false;
                    common.Trace?.CheckCancellation();if(remaining--<=0)return false;
                    int ceiling=engineerBound==null ? int.MaxValue : topology==null
                        ? engineerBound.Ceiling(previous.Legs,input.Beams.Length-beam-1,covers.Min(c=>c.Length)*4)
                        : engineerBound.Ceiling(previous.Legs,topology.FutureLegs(beam));
                    bool tightJunction=input.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && (c.BayNumber==beam || c.BayNumber==beam+1)
                        && input.Bays[c.BayNumber-1].WidthMm<2*CrossBeamSupportOptimizer.SideShutterClearanceMm+1524+2*common.StockEndSetbackMm+CrossBeamSupportOptimizer.H20WidthMm);
                    var receivers=Receivers(beam,previous.Bays.LastOrDefault(),right,ceiling).Where(r=>r.LegCount<=ceiling);
                    if(common.OptimizeCrossingSearch && tightJunction)receivers=receivers.OrderBy(r=>Math.Abs(r.WidthMm-1000)).ThenBy(r=>Math.Abs(r.TransverseOffsetMm));
                    foreach(var receiver in receivers.Take(common.OptimizeCrossingSearch ? 4 : 2))
                    {
                        if(common.NetworkSeedCompletion?.Found==true)return false;
                        int before=outcome.Improvements.Count;var next=Extend(previous,right,receiver,beam);
                        if(outcome.Improvements.Count>before){common.Trace?.Increment(BeamSearchCounter.SeedNetworkCandidates);return true;}
                        if(next!=null && beam+1<input.Beams.Length && Seed(next,beam+1,ref remaining))return true;
                    }
                }
                return false;
            }
            bool ChangedReceiverFits()
            {
                var old=common.NetworkWarmStart?.Input;
                if(!common.LocalNetworkSearch || !common.FirstCompleteNetworkOnly || old==null || !input.HasIndividualSpacings || old.Beams.Length!=input.Beams.Length)return true;
                // Try deep changed receivers first, then work back from the far
                // end. This ordering is only a screen: every passing trial still
                // follows the ordinary ranked chain and final complete checks.
                foreach(int changed in Enumerable.Range(0,input.Beams.Length).Where(i=>input.BeamSpacing(i)!=old.BeamSpacing(i))
                    .OrderByDescending(i=>input.Beams[i].DepthMm).ThenByDescending(i=>i))
                {
                    var left=changed==0 ? new UpperTransferPlan[] {null} : options[changed-1];
                    var right=changed==input.Bays.Length ? new UpperTransferPlan[] {null} : options[changed];
                    bool fits=false;
                    foreach(var l in left)
                    {
                        foreach(var r in right)
                        {
                            common.Trace?.CheckCancellation();
                            if(Receivers(changed,l,r,int.MaxValue).Length>0){fits=true;break;}
                        }
                        if(fits)break;
                    }
                    if(fits)continue;
                    common.Trace?.Increment(BeamSearchCounter.LocalReceiverFailures);
                    common.Diagnostics.Geometry($"B{changed+1} spacing feasibility",$"B{changed+1}: no retained adjacent slab-foot arrangement admits a receiving beam at {input.BeamSpacing(changed):0.###} mm secondary spacing.","Try another permitted secondary spacing or revise this beam's supports.",blocker:true);
                    return false;
                }
                return true;
            }
            var states=new[] {new State {Beams=new TowerLayout[0],Bays=new UpperTransferPlan[0]}}.ToList();
            try
            {
                if(!ChangedReceiverFits())return outcome;
                if(common.EngineerWorkflow && common.SeedNetworkSearch)
                {
                    // Explore a small complete path before expanding every slab
                    // option at B1/B2. The ordinary bounded search still follows.
                    int remaining=common.OptimizeCrossingSearch ? input.Beams.Length*8+32 : input.Beams.Length*2+8;Seed(states[0],0,ref remaining);
                    if(common.NetworkSeedCompletion?.Found==true)return outcome;
                }
                for(int beam=0;beam<input.Beams.Length && states.Count>0 && common.NetworkSeedCompletion?.Found!=true;beam++)
                {
                    common.Trace?.CheckCancellation();
                    var next=new Dictionary<UpperTransferPlan,List<State>>();var closed=new List<State>();
                    foreach(var previous in states)
                    foreach(var right in beam==input.Bays.Length ? new UpperTransferPlan[] {null} : options[beam])
                    {
                        if(common.NetworkSeedCompletion?.Found==true)break;
                        var left=previous.Bays.LastOrDefault();
                        int ceiling=engineerBound==null ? int.MaxValue : topology==null
                            ? engineerBound.Ceiling(previous.Legs,input.Beams.Length-beam-1,covers.Min(c=>c.Length)*4)
                            : engineerBound.Ceiling(previous.Legs,topology.FutureLegs(beam));
                        if(ceiling<(topology?.CurrentLegs(beam)??covers.Min(c=>c.Length)*4)){common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);continue;}
                        var receivers=Receivers(beam,left,right,ceiling);
                        foreach(var receiver in receivers)
                        {
                            if(common.NetworkSeedCompletion?.Found==true)break;
                            if(receiver.LegCount>ceiling){common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);continue;}
                            var candidate=Extend(previous,right,receiver,beam);if(candidate==null)continue;
                            if(right==null) {closed.Add(candidate);continue;}
                            if(!next.TryGetValue(right,out var list))next[right]=list=new List<State>();list.Add(candidate);
                        }
                    }
                    // Keep geometrically different current receivers until their next junction is
                    // known. Economy is applied only among prefixes that already pass their crossings.
                    states=next.Values.SelectMany(list=>list.GroupBy(s=>Tuple.Create(s.Beams.Last().WidthMm,s.Beams.Last().TransverseOffsetMm)).SelectMany(g=>g.OrderBy(s=>s.Separate).ThenBy(s=>common.EngineerSearch ? s.Legs : s.Legs*TowerOptimizer.TowerLegTimberEquivalentMm+s.Timber).ThenBy(s=>s.Narrow).ThenBy(s=>s.BaysCount).ThenBy(s=>s.BracePenalty).ThenBy(s=>s.Beams.Sum(b=>b.CantileverImbalanceMm)).ThenBy(s=>s.Symmetry).ThenBy(s=>s.Beams.Sum(b=>b.BraceLocationPenalty)).ThenBy(s=>s.Timber).Take(common.EngineerSearch ? 3 : 2))).Concat(closed).ToList();
                }
            }
            catch(BeamSearchDeadlineException) {common.Trace?.CheckUserCancellation();}
            if(common.EngineerSearch && common.EngineerJunctionExtraTiers==0 && outcome.Improvements.Count==0 && common.Trace?.BudgetReached!=true && common.NetworkSeedCompletion?.Found!=true)
            {
                var expanded=common.CopyForSearch();expanded.EngineerJunctionExtraTiers=2;
                var alternative=CoordinatedScenario(expanded,input,covers,geometryCache,options,cover,phase,rows,engineerBound,common.OptimizeCrossingSearch ? baseReceivers : null);
                alternative.Tested+=outcome.Tested;return alternative;
            }
            return outcome;
        }

        // Incremental crossing loads go only to rows beneath the beam footprint, with closing tributaries.
        // The transverse beam spans the entire clear bay; subtract existing slab concrete and allowances once.
        private static double[] RowStations(TowerInput common,TransferNetworkInput input,double cover,double phase)
        {
            double edge=input.SharedRowStart(common.LengthMm,cover);
            if(input.HasIndividualSpacings)
            {
                var slab=input.SlabRows(common.LengthMm,cover).SelectMany(r=>r);
                // Shared shutter ledgers need receiving rows inside the crossing as well as
                // slab rows outside it. Insert those anchors before filling the beam grid.
                var crossing=input.UseCrossingSlabLedgers ? input.CrossingBeams.SelectMany((c,i)=>
                    c.SupportMode==CrossBeamSupportMode.LegacySlabRows || common.EngineerWorkflow && EngineerPrimaryPlan.Eligible(input,c) ? new double[0] : c.WidthMm>80
                        ? PlywoodSpacing.SheetSupportRows(c.PositionMm-c.WidthMm/2,c.PositionMm+c.WidthMm/2,input.CrossSpacing(i))
                        : new[] {c.PositionMm}) : Enumerable.Empty<double>();
                return slab.Concat(crossing).Distinct().OrderBy(x=>x).ToArray();
            }
            if(input.PlywoodSheetLengthMm>0) return PlywoodSpacing.SheetSupportRows(edge,edge+cover,input.UpperSpacingMm,input.PlywoodSheetLengthMm);
            int count=(int)Math.Ceiling(cover/input.RowGapMm);
            double first=(cover-(count-1)*input.RowGapMm)/2+phase;
            return Enumerable.Range(0,count).Select(i=>edge+first+i*input.RowGapMm).ToArray();
        }
        internal static double[] EffectiveWidthsMm(TowerInput common,TransferNetworkInput input,int bay,double cover,double phase)
        {
            var allStations=RowStations(common,input,cover,phase);
            var stations=input.HasIndividualSpacings ? input.SlabRows(common.LengthMm,cover)[bay] : allStations;
            int count=stations.Length;double edge=input.SharedRowStart(common.LengthMm,cover);
            double slabLoad=input.AreaLoad(input.Bays[bay].ThicknessMm);
            var widths=Enumerable.Range(0,count).Select(i=>(i==count-1 ? edge+cover : (stations[i]+stations[i+1])/2)-(i==0 ? edge : (stations[i-1]+stations[i])/2)).ToArray();
            widths=CrossBeamSupportOptimizer.SlabWidths(input,bay,edge,cover,stations,widths);
            if(widths==null) {common.Diagnostics?.Geometry("slab cross exclusion",$"S{bay+1}: remaining slab strips have no H20 within the entered {input.BaySpacing(bay):0.###} mm row spacing after cross-beam exclusion.","Review secondary spacing and crossing positions/widths.");return null;}
            // Longitudinal slab strips outside this shared coverage are assigned
            // to assumed external column-side supports, not added to slab rows.
            // Crossing increments below still retain their complete footprints.
            var activeRows=Enumerable.Range(0,count).Where(i=>widths[i]>1e-7).ToArray();
            if(activeRows.Length>0 && (stations[activeRows[0]]<edge-1e-7 || stations[activeRows.Last()]>edge+cover+1e-7 || stations[activeRows[0]]-edge>input.BaySpacing(bay)+1e-7 || edge+cover-stations[activeRows.Last()]>input.BaySpacing(bay)+1e-7))
            {common.Diagnostics?.Geometry("slab supported end span",$"S{bay+1}: a slab end span inside shared support coverage exceeds {input.BaySpacing(bay):0.###} mm.","Review slab rows within the supported coverage.");return null;}
            foreach(var crossing in input.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode==CrossBeamSupportMode.LegacySlabRows))
            {
                double lo=crossing.PositionMm-crossing.WidthMm/2,hi=crossing.PositionMm+crossing.WidthMm/2;
                var beneath=Enumerable.Range(0,count).Where(i=>stations[i]>=lo && stations[i]<=hi).ToArray();
                if(beneath.Length==0) {common.Diagnostics?.Geometry("crossing row "+Array.IndexOf(input.CrossingBeams,crossing),$"X{Array.IndexOf(input.CrossingBeams,crossing)+1} in S{bay+1}: no H20 row beneath footprint {lo:0.###}–{hi:0.###} mm; nearest row x={stations.OrderBy(x=>Math.Abs(x-crossing.PositionMm)).First():0.###} mm at entered spacing {input.BaySpacing(bay):0.###} mm.","Review upper row spacing and crossing position/width.");return null;} // A crossing beam needs an H20 row physically below its footprint.
                for(int k=0;k<beneath.Length;k++)
                {
                    double a=k==0 ? lo : (stations[beneath[k-1]]+stations[beneath[k]])/2,b=k==beneath.Length-1 ? hi : (stations[beneath[k]]+stations[beneath[k+1]])/2;
                    widths[beneath[k]]+=(b-a)*(input.AreaLoad(crossing.DepthMm)-slabLoad)/slabLoad;
                }
            }
            return input.HasIndividualSpacings ? allStations.Select(x=>Array.IndexOf(stations,x)).Select(i=>i<0 ? 0 : widths[i]).ToArray() : widths;
        }
        private static TowerInput SharedInput(TowerInput common,TransferNetworkInput network)
        {var input=common.CopyForSearch();input.LengthMm=network.SharedEnd(common.LengthMm)-network.SharedStart;input.LongitudinalStartMm=network.SharedStart;return input;}
        private static double LowerTimber(TowerLayout beam)=>beam.StockPlan.PrimaryLengthMm+beam.Transfer.RowsMm.Length*beam.Transfer.LowerStockLengthMm;
        private static IEnumerable<UpperTransferPlan> Options(TowerInput common,TransferNetworkInput input,int bay,double tributary,string rowLocation)
        {
            var row=input.Bays[bay];var result=new List<Tuple<UpperTransferPlan,double>>();
            double maximumEndStrip=input.MaximumSlabEndStripMm(bay);
            var lengths=UpperTransferPlan.SlabLengths(row.WidthMm,common.StockLengthsMm,maximumEndStrip,common.AllowCustomSlabLengths)
                .Where(length=>common.AllowCustomSlabLengths || !common.RespectNetworkCatalogueLengths && common.CustomH20Scope==CustomH20LengthScope.Both || common.StockLengthsMm.Contains(length)).ToArray();
            if(lengths.Length==0)common.Diagnostics.Geometry("S"+(bay+1)+" length",$"S{bay+1}: no permitted slab H20 length fits clear bay {row.WidthMm:0.###} mm with 200–{maximumEndStrip:0.###} mm edge strips (slab plywood spacing limit).","Review slab stocks, plywood allowance and the custom-length scope.",blocker:true);
            foreach(double stock in lengths)
            {
                double edge=(row.WidthMm-stock)/2;if(edge<200 || edge>maximumEndStrip)continue;
                double min=Math.Max(250,edge+150),max=row.WidthMm/2-150;
                Func<double,IEnumerable<double>> feet=width=>new[] {min,max}.Concat(new[] {600.0,1000,1500,1524,2000}.Select(w=>(w-width)/2)).Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Where(x=>x>=min && x<=max).Distinct();
                // One setback is repeated on both faces and at every row in this bay.
                // Include geometry candidates from both receiving beams, without moving one foot independently.
                foreach(double foot in feet(input.Beams[bay].WidthMm).Concat(feet(input.Beams[bay+1].WidthMm)).Distinct())
                {
                    var analysis=TransferredSlabOptimizer.AnalyzeTransverse(common,BeamSearchCounter.UpperAnalyses,stock,new[] {foot-edge,row.WidthMm-foot-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=input.AreaLoad(row.ThicknessMm)*tributary/1000}},null,TowerOptimizer.PairEI/2,common.DeflectionDenominator);
                    common.Diagnostics.AnalysisLazy(analysis,1,1,"S"+(bay+1)+" slab H20",1,()=> $"{rowLocation} · stock {stock:0} mm · equal setback {foot:0.###} mm from both faces");
                    if(analysis.MomentKnM>TowerOptimizer.PairMoment/2+1e-8 || analysis.ShearKn>TowerOptimizer.PairShear/2+1e-8 || analysis.DeflectionUtilization>1+1e-8 || !common.IgnoreUplift && analysis.Reactions.Any(r=>r< -1e-7))continue;
                    result.Add(Tuple.Create(new UpperTransferPlan {StockLengthMm=stock,EdgeMm=edge,NearFootMm=foot,FarFootMm=foot,IsCustomLength=!common.StockLengthsMm.Contains(stock)},analysis.DeflectionUtilization));
                }
            }
            if(result.Count==0)common.Diagnostics.Geometry("S"+(bay+1)+" symmetric supports",$"S{bay+1}: no permitted slab H20 passes with equal support setbacks on both sides.","Review stock, spacing and receiving geometry, or provide separately designed slab supports.");
            if(common.RetainAllSlabSupportOptions)
                return result.OrderBy(r=>r.Item1.StockLengthMm).ThenBy(r=>r.Item1.NearFootMm)
                    .GroupBy(r=>Tuple.Create(r.Item1.StockLengthMm,r.Item1.NearFootMm)).Select(g=>g.First().Item1).ToArray();
            // The chain DP compares only passing symmetric arrangements from this bounded set.
            // A shorter slab stock can move its foot farther along the receiving
            // secondary and increase that member's demand. Retain the closest-foot
            // alternative as well as the least-timber choice in each setback band.
            return result.OrderBy(r=>r.Item1.StockLengthMm).ThenBy(r=>r.Item1.NearFootMm+r.Item1.FarFootMm).ThenBy(r=>r.Item2)
                .GroupBy(r=>Math.Round(r.Item1.NearFootMm/250))
                .SelectMany(g=>new[] {g.First(),g.OrderBy(r=>r.Item1.NearFootMm).ThenBy(r=>r.Item1.StockLengthMm).ThenBy(r=>r.Item2).First()}.Distinct())
                .Select(r=>r.Item1).Take(20).ToArray();
        }
        // Row registry remains global for slab quantities/drawing; each receiving
        // beam requests only its own slab feet and adjacent crossing receivers.
        internal static double[] CrossingRows(TowerInput common,TransferNetworkInput input,double cover,int beam)
            =>input.UseCrossingSlabLedgers ? input.CrossingBeams.SelectMany((c,i)=>
                (c.BayNumber!=beam && c.BayNumber!=beam+1) || c.SupportMode==CrossBeamSupportMode.LegacySlabRows || common.EngineerWorkflow && EngineerPrimaryPlan.Eligible(input,c)
                    ? new double[0] : c.WidthMm>80 ? PlywoodSpacing.SheetSupportRows(c.PositionMm-c.WidthMm/2,c.PositionMm+c.WidthMm/2,input.CrossSpacing(i)) : new[] {c.PositionMm}).Distinct().OrderBy(x=>x).ToArray() : new double[0];
        private static double[] BayBaseline(TowerInput common,TransferNetworkInput input,int bay,UpperTransferPlan plan,double cover,double phase)
        {
            var all=RowStations(common,input,cover,phase);
            if(plan==null)return new double[all.Length];
            var own=input.SlabRows(common.LengthMm,cover)[bay];double edge=input.SharedRowStart(common.LengthMm,cover);
            var widths=PlywoodSpacing.RowTributaries(edge,edge+cover,own);
            return all.Select(x=>Array.IndexOf(own,x)).Select(i=>i<0 ? 0 : widths[i]).ToArray();
        }
        private static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling,double? towerWidth=null,double transverseOffset=0,Dictionary<double,TowerLayout> alternatives=null,TowerLayout fixedLayout=null)
            =>SolveReceiver(common,network,beam,left,right,cover,phase,stockCovers,geometryCache,ceiling,towerWidth,transverseOffset,alternatives,fixedLayout);
        private static TowerLayout SolveUniversal(ReceiverAggregateLoadBound aggregate,TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling,double? towerWidth=null,double transverseOffset=0,Dictionary<double,TowerLayout> alternatives=null,TowerLayout fixedLayout=null,int certifiedFailedStations=0)
            =>SolveReceiverCore(common,network,beam,left,right,cover,phase,stockCovers,geometryCache,ceiling,towerWidth,transverseOffset,alternatives,fixedLayout,
                certifiedFailedStations:certifiedFailedStations,universalAggregate:aggregate);
        private static TowerLayout SolveCertified(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling,double? towerWidth=null,double transverseOffset=0,Dictionary<double,TowerLayout> alternatives=null,TowerLayout fixedLayout=null,int certifiedFailedStations=0)
            =>SolveReceiverCore(common,network,beam,left,right,cover,phase,stockCovers,geometryCache,ceiling,towerWidth,transverseOffset,alternatives,fixedLayout,certifiedFailedStations:certifiedFailedStations);
        internal static Func<TransferredSlabOptimizer.Geometry,double,bool> WithReceiverFrameChecks(TowerInput input,TransferNetworkInput network,int beam,TowerLayout layout,
            Func<TransferredSlabOptimizer.Geometry,double,bool> prepared,TransferredSlabOptimizer.ResponseCache<Tuple<TransferredSlabOptimizer.Geometry,double,double>,bool> frameChecks=null)
        {
            // This cache belongs to one receiver solve with a fixed transverse
            // offset. Both the early necessary query and the ordinary callback
            // use the same exact geometry/edge/width frame result, in that order.
            frameChecks=frameChecks??new TransferredSlabOptimizer.ResponseCache<Tuple<TransferredSlabOptimizer.Geometry,double,double>,bool>(4096);
            double width=layout.WidthMm,offset=layout.TransverseOffsetMm;
            bool FrameFits(TransferredSlabOptimizer.Geometry geometry,double edge)
            {
                input.Trace?.CheckCancellation();var key=Tuple.Create(geometry,edge,width);
                if(!frameChecks.TryGetValue(key,out bool fits))
                    frameChecks.Store(key,fits=CrossBeamSupportOptimizer.ReceiverFramesFit(input,network,beam,geometry,edge,width,offset));
                return fits;
            }
            bool FrameMayFit(TransferredSlabOptimizer.Geometry geometry,double edge)
            {
                bool fits;try{fits=FrameFits(geometry,edge);}catch(ArgumentException){return true;}
                if(!fits)input.Trace?.Increment(BeamSearchCounter.ReceiverFrameMetadataRejected);return fits;
            }
            Func<TransferredSlabOptimizer.Geometry,double,bool> frames=FrameFits;
            var bounded=PreparedReceiverSupportBounds.Attach(frames,(stocks,edge)=>0,FrameMayFit);
            return CrossBeamSupportOptimizer.BothReceivers(bounded,prepared);
        }
        internal static int EffectiveReceiverLegCeiling(TowerInput input)
            =>input.EngineerSearch ? 2*Math.Min(EngineerTowerSearch.MaximumStations(input),input.NetworkLegCeiling/2) : input.NetworkLegCeiling;
        internal static double[][] ReceiverStockCovers(TowerInput common,TransferNetworkInput network,int beam,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache)
        {
            if(!network.DifferentSpans(common.LengthMm))return stockCovers;
            var local=common.CopyForSearch();local.LengthMm=network.Beams[beam].SpanLength(common.LengthMm);local.LongitudinalStartMm=network.Beams[beam].SpanStartMm;
            var allowed=CachedSpanStockCovers(local,geometryCache);
            // Protected geometries retain their exact restricted stock universe.
            return geometryCache?.Fixed==null ? allowed : stockCovers.Where(c=>allowed.Any(a=>a.SequenceEqual(c))).ToArray();
        }
        private static TowerLayout SolveReceiver(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling,double? towerWidth=null,double transverseOffset=0,Dictionary<double,TowerLayout> alternatives=null,TowerLayout fixedLayout=null,double[] selectedStockCover=null)
            =>SolveReceiverCore(common,network,beam,left,right,cover,phase,stockCovers,geometryCache,ceiling,towerWidth,transverseOffset,alternatives,fixedLayout,selectedStockCover);
        private static TowerLayout SolveReceiverCore(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling,double? towerWidth=null,double transverseOffset=0,Dictionary<double,TowerLayout> alternatives=null,TowerLayout fixedLayout=null,double[] selectedStockCover=null,int certifiedFailedStations=0,ReceiverAggregateLoadBound universalAggregate=null)
        {
            common.Trace?.CheckCancellation();
            if(network.DifferentSpans(common.LengthMm))
            {
                stockCovers=ReceiverStockCovers(common,network,beam,stockCovers,geometryCache);
                if(stockCovers.Length==0)return null;
            }
            // Local warm repairs may reuse an old stock topology only when that
            // exact sequence still belongs to this beam's current permitted
            // cover universe. Apply this after local-span cover generation.
            if(selectedStockCover!=null)
            {
                stockCovers=stockCovers.Where(c=>c.SequenceEqual(selectedStockCover)).ToArray();
                if(stockCovers.Length==0)return null;
            }
            if(ceiling<stockCovers.Min(c=>c.Length)*4) {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);return null;}
            common.Trace?.Context(System.FormattableString.Invariant($"B{beam+1}; coverage={cover:R} mm; row phase={phase:R} mm; beam spacing={network.BeamSpacing(beam):R} mm; crossing coordination={common.CoordinateCrossingLayouts}"));
            var b=network.Beams[beam];var t=new TransferredSlabInput {PlywoodProfileId=network.PlywoodProfileId,PlywoodDeflectionDenominator=network.PlywoodDeflectionDenominator,PlywoodSheetLengthMm=network.PlywoodSheetLengthMm,IsNetwork=true,IncludeMainBeamEndLoads=true,FixedTowerWidthMm=towerWidth,BeamCentreOffsetMm=-transverseOffset,CrossFormwork=CrossBeamSupportOptimizer.Modern(network),AllowLongerPrimaryCoverage=common.IndependentNetworkPrimaryCoverage || network.DifferentSpans(common.LengthMm),StockCovers=stockCovers,SharedGeometryCache=geometryCache,SharedSearchWork=common.NetworkNumerics,BeamWidthMm=b.WidthMm,BeamLineLoadKnM=network.AreaLoad(b.DepthMm)*b.WidthMm/1000,UpperSpacingMm=network.BeamSpacing(beam),LocalRowGapMm=network.LocalRowGapMm,LeftSlabSpacingMm=network.HasIndividualSpacings && left!=null ? network.BaySpacing(beam-1) : 0,RightSlabSpacingMm=network.HasIndividualSpacings && right!=null ? network.BaySpacing(beam) : 0,SharedRowsMm=network.HasIndividualSpacings ? RowStations(common,network,cover,phase) : null,FixedCoveredLengthMm=cover,FixedRowStartMm=network.SharedRowStart(common.LengthMm,cover),FixedRowPhaseMm=phase,
                LeftSlabStartMm=left==null ? (double?)null : network.BayStart(beam-1,common.LengthMm),LeftSlabEndMm=left==null ? (double?)null : network.BayEnd(beam-1,common.LengthMm),RightSlabStartMm=right==null ? (double?)null : network.BayStart(beam,common.LengthMm),RightSlabEndMm=right==null ? (double?)null : network.BayEnd(beam,common.LengthMm),LeftBayMm=left==null ? 0 : network.Bays[beam-1].WidthMm,RightBayMm=right==null ? 0 : network.Bays[beam].WidthMm,LeftAreaLoadKnM2=left==null ? 0 : network.AreaLoad(network.Bays[beam-1].ThicknessMm),RightAreaLoadKnM2=right==null ? 0 : network.AreaLoad(network.Bays[beam].ThicknessMm),LeftPlan=left?.Reverse(),RightPlan=right,
                LeftCrossingBands=left==null ? new BeamDistributedLoad[0] : CrossingBands(network,beam-1,common.LengthMm,cover),RightCrossingBands=right==null ? new BeamDistributedLoad[0] : CrossingBands(network,beam,common.LengthMm,cover),
                LeftEffectiveWidthsMm=left==null ? null : EffectiveWidthsMm(common,network,beam-1,cover,phase),RightEffectiveWidthsMm=right==null ? null : EffectiveWidthsMm(common,network,beam,cover,phase),
                LeftBaselineWidthsMm=network.HasIndividualSpacings ? BayBaseline(common,network,beam-1,left,cover,phase) : null,RightBaselineWidthsMm=network.HasIndividualSpacings ? BayBaseline(common,network,beam,right,cover,phase) : null};
            if(common.EngineerWorkflow && network.HasIndividualSpacings)
            {
                t.ReceivingRowsMm=CrossingRows(common,network,cover,beam);
                t.ReceivingCrossings=network.CrossingBeams.Where(c=>(c.BayNumber==beam || c.BayNumber==beam+1) && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).ToArray();
                t.LocalReceivingRows=true;
            }
            var input=new TowerInput {LocalNetworkSearch=common.LocalNetworkSearch,ReuseCrossingLocalWork=common.ReuseCrossingLocalWork,PrepareSpanGeometry=common.PrepareSpanGeometry,ReuseTransversePreparations=common.ReuseTransversePreparations,PrepareReceiverChecks=common.PrepareReceiverChecks,OptimizePrimaryResponses=common.OptimizePrimaryResponses,SeedNetworkSearch=common.SeedNetworkSearch,ReceiverAlternatives=alternatives,EngineerSearch=common.EngineerSearch,EngineerMinimumStations=common.EngineerMinimumStations,FullLoadingOnly=common.FullLoadingOnly,DeferCrossingChecks=common.FullLoadingOnly && network.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && (c.BayNumber==beam || c.BayNumber==beam+1)),PrimaryMaterial=common.PrimaryMaterial,AllowLocalShutterExceptions=common.AllowLocalShutterExceptions,ReceiverTransverseOffsetMm=transverseOffset,IgnoreUplift=common.IgnoreUplift,Trace=common.Trace,ReuseSearchWork=common.ReuseSearchWork,PruneUneconomicCandidates=common.PruneUneconomicCandidates,StagePrimaryChecks=common.StagePrimaryChecks,ReuseNetworkNumerics=common.ReuseNetworkNumerics,ReuseTransverseNumerics=common.ReuseTransverseNumerics,LazySearchDiagnostics=common.LazySearchDiagnostics,DeferTransferLayout=common.DeferTransferLayout,SearchByLegTier=common.SearchByLegTier,NetworkLegCeiling=ceiling,AllowCustomH20Lengths=common.AllowCustomH20Lengths,CustomH20Scope=common.CustomH20Scope,LengthMm=b.SpanLength(common.LengthMm),LongitudinalStartMm=b.SpanStartMm,LegCapacityKn=common.LegCapacityKn,DeflectionDenominator=common.DeflectionDenominator,DeflectionLimitMm=common.DeflectionLimitMm,StockLengthsMm=common.StockLengthsMm,StockEndSetbackMm=common.StockEndSetbackMm,MaximumConcreteEndOverhangMm=common.MaximumConcreteEndOverhangMm,Exclude600Standalone=common.Exclude600Standalone,TransferredSlab=t,Diagnostics=common.Diagnostics,DiagnosticScope=$"B{beam+1} · "};
            if(common.EngineerWorkflow)
            {
                var plan=EngineerPrimaryPlan.Create(input,network,beam,left!=null,right!=null);
                if(plan.UseDesignZones || plan.HasCrossings)input.EngineerPlan=plan;
                if(plan.HasCrossings){t.EngineerNetwork=network;t.EngineerBeam=beam;}
            }
            // A certificate belongs only to the freshly searched centred width
            // subset in this Receivers call. Do not alter minimum/maximum tiers.
            input.ReceiverFailedThroughStations=towerWidth.HasValue && transverseOffset==0 && fixedLayout==null && selectedStockCover==null
                && geometryCache?.Fixed==null && input.EngineerPlan!=null ? Math.Max(0,certifiedFailedStations) : 0;
            input.ExternalObstacles=common.ExternalObstacles;
            input.ObstacleBottomMm=b.BottomElevationMm;
            for(int i=0;i<beam;i++)input.ObstacleCentreYmm+=network.Beams[i].WidthMm/2+network.Bays[i].WidthMm+network.Beams[i+1].WidthMm/2;
            input.CoordinateCrossingLayouts=common.CoordinateCrossingLayouts;
            // A fixed geometry cache narrows the candidate universe independently
            // of fixedLayout. Its identity is absent from ReceiverSolveKey, so it
            // cannot consume or publish a complete unrestricted search result.
            var receiverCache=fixedLayout==null && geometryCache?.Fixed==null && common.ExternalObstacles.Length==0 && common.ReuseReceiverSearches && common.ReuseSearchWork ? common.NetworkReceiverWork : null;
            var receiverKey=receiverCache==null ? null : new ReceiverSolveKey(input,network,beam,alternatives!=null,shareLegCeilings:common.EngineerSearch && common.LocalNetworkSearch);
            // OptimizeEngineer uses this exact maximum station count regardless
            // of its adaptive receiver-count refinement. Raw budgets above that
            // physical cap, and adjacent odd/even budgets, have identical work.
            int cacheCeiling=EffectiveReceiverLegCeiling(input);
            input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator,trace:input.Trace,ignoreUplift:input.IgnoreUplift) {LazyLocations=input.LazySearchDiagnostics};
            if(common.CoordinateCrossingLayouts)
            {
                input.OptimizeCrossingSearch=common.OptimizeCrossingSearch;
                if(input.PrepareReceiverChecks && input.ReuseSearchWork && input.DeferTransferLayout && input.FullLoadingOnly && input.EngineerPlan?.UseDesignZones==true)
                {
                    var frameChecks=new TransferredSlabOptimizer.ResponseCache<Tuple<TransferredSlabOptimizer.Geometry,double,double>,bool>(4096);
                    bool checkFrames=CrossBeamSupportOptimizer.ReceiverFrameCrossings(input,network,beam).Any();
                    input.TransferReceiverPreparation=layout=>
                    {
                        var prepared=CrossBeamSupportOptimizer.PrepareReceiver(input,network,beam,layout);
                        if(!input.OptimizeCrossingSearch || prepared==null || ReferenceEquals(prepared,CrossBeamSupportOptimizer.FailedReceiver) || !checkFrames)return prepared;
                        return WithReceiverFrameChecks(input,network,beam,layout,prepared,frameChecks);
                    };
                }
                input.TransferLayoutAcceptance=layout=>(t.EngineerNetwork==null || CrossBeamSupportOptimizer.CheckEngineerReceiver(input,network,beam,layout)) && CrossBeamSupportOptimizer.CheckJunctionReceiver(input,network,beam,layout);
                if(common.EngineerSearch)input.TransferLowerAcceptance=layout=>(t.EngineerNetwork==null || CrossBeamSupportOptimizer.CheckEngineerReceiver(input,network,beam,layout,true)) && CrossBeamSupportOptimizer.CheckJunctionLowerReceiver(input,network,beam,layout);
            }
            NativeExternalWidthProof externalWidthProof=null;
            if(input.ExternalObstacles.Length>0)
            {
                if(alternatives is ReceiverWidthAlternatives widthCollector && fixedLayout==null && selectedStockCover==null && geometryCache?.Fixed==null
                    && input.EngineerPlan!=null && !towerWidth.HasValue && transverseOffset==0
                    && common.EngineerSearch && common.LocalNetworkSearch && common.ReuseSearchWork && common.ReuseReceiverSearches && common.OptimizeCrossingSearch
                    && !common.FirstCompleteNetworkOnly && common.NetworkSeedCompletion?.Found!=true && common.Trace?.HasTimeLimit!=true && input.Trace?.HasTimeLimit!=true)
                {
                    externalWidthProof=new NativeExternalWidthProof(common,input);
                    input.TransferLayoutAcceptance=externalWidthProof.Acceptance;widthCollector.BindNativeExternal(externalWidthProof);
                }
                else
                {
                    var existingAcceptance=input.TransferLayoutAcceptance;
                    input.TransferLayoutAcceptance=layout=>CrossBeamSupportOptimizer.ExternalLayoutFits(input,layout) && (existingAcceptance==null || existingAcceptance(layout));
                }
            }
            // Capture only the native callbacks just installed above, including
            // the additional obstacle AND check. Copies retain these identities;
            // another caller's callback always uses the physical weak profile.
            if(universalAggregate!=null && fixedLayout==null && selectedStockCover==null && geometryCache?.Fixed==null)
                input.TransferGeometryFilter=universalAggregate.GeometryFilterForRetainedOptions(beam,input.TransferReceiverPreparation,input.TransferLayoutAcceptance,t.FixedTowerWidthMm,input.ReceiverTransverseOffsetMm);
            externalWidthProof?.SealCallbacks();
            TowerLayout result=null;
            bool completeFailure=false,fullSearchStarted=false,fullSearchCompleted=false;
            ReceiverSolveKey identityInput=null;
            void Identify(TowerLayout layout)
            {
                if(layout==null || !common.LocalNetworkSearch)return;
                identityInput=identityInput??new ReceiverSolveKey(input,network,beam,false,layoutIdentity:true);
                layout.ReceiverIdentity=new ReceiverLayoutKey(identityInput,layout);
            }
            try
            {
                if(receiverCache!=null && receiverCache.Get(receiverKey,cacheCeiling,out var cachedReceiver))
                {
                    if(cachedReceiver.Best==null || (cachedReceiver.RowRegistry??new double[0]).SequenceEqual(t.SharedRowsMm??new double[0]))
                    {
                        common.Trace?.Increment(BeamSearchCounter.ReceiverSearchCacheHits);
                        input.Diagnostics.Merge(cachedReceiver.Diagnostics);
                        if(cachedReceiver.MinimumRequiredLegs>ceiling)
                            input.Diagnostics.Geometry("B"+(beam+1)+" minimum legs",$"B{beam+1}: the complete receiving-beam search requires at least {cachedReceiver.MinimumRequiredLegs} legs; this network branch permits {ceiling}.","Use another network branch or revise the receiving supports.");
                        if(alternatives!=null)foreach(var choice in cachedReceiver.Alternatives.Where(choice=>choice.Value.LegCount<=ceiling))alternatives[choice.Key]=choice.Value;
                        return cachedReceiver.Best;
                    }
                    // Identical local mathematical problem, new global row IDs.
                    // Rebuild all member loads, source indices and diagrams at the
                    // cached best geometry instead of repeating its geometry search.
                    var rebuilt=new Dictionary<double,TowerLayout>();
                    result=TransferredSlabOptimizer.TryFixedLayout(input,cachedReceiver.Best);
                    bool complete=result!=null;
                    if(complete && alternatives!=null)foreach(var choice in cachedReceiver.Alternatives.Where(choice=>choice.Value.LegCount<=ceiling))
                    {
                        var layout=ReferenceEquals(choice.Value,cachedReceiver.Best) ? result : TransferredSlabOptimizer.TryFixedLayout(input,choice.Value);
                        if(layout==null){complete=false;break;}rebuilt[choice.Key]=layout;
                    }
                    if(complete)
                    {
                        common.Trace?.Increment(BeamSearchCounter.ReceiverSearchCacheHits);
                        Identify(result);foreach(var choice in rebuilt)Identify(choice.Value);
                        if(alternatives!=null)foreach(var choice in rebuilt)alternatives[choice.Key]=choice.Value;
                        return result;
                    }
                }
                common.Trace?.Increment(BeamSearchCounter.NetworkSolves);
                using(common.Trace?.Measure(BeamSearchStage.ReceivingBeams))
                {
                    result=null;
                    if(fixedLayout==null)
                    {fullSearchStarted=true;result=TransferredSlabOptimizer.Optimize(input,true);fullSearchCompleted=true;}
                    else result=TransferredSlabOptimizer.TryFixedLayout(input,fixedLayout);
                }
            }
            catch(TowerOptimizationException){completeFailure=fullSearchStarted;}
            catch(ArgumentException ex) {input.Diagnostics.Geometry("B"+(beam+1)+" geometry",$"B{beam+1}: {ex.Message}","Review this beam's geometry and transfer inputs.");}
            finally{common.Diagnostics?.Merge(input.Diagnostics);}
            Identify(result);if(alternatives!=null)foreach(var choice in alternatives)Identify(choice.Value);
            if(alternatives is ReceiverWidthAlternatives widthEvidence)
                widthEvidence.Complete(common,input,result,fullSearchCompleted,fixedLayout!=null || selectedStockCover!=null || geometryCache?.Fixed!=null);
            if(receiverCache!=null && (result!=null || completeFailure) && common.Trace?.BudgetReached!=true && result?.SearchTimeLimitReached!=true && common.NetworkSeedCompletion?.Found!=true)
            {
                common.Trace?.CheckCancellation();
                receiverCache.Store(receiverKey,new NetworkReceiverCache.Entry {Best=result,Alternatives=alternatives==null ? new Dictionary<double,TowerLayout>() : new Dictionary<double,TowerLayout>(alternatives),Diagnostics=input.Diagnostics,RowRegistry=t.SharedRowsMm,LegCeiling=cacheCeiling,
                    CompleteFailure=completeFailure,MinimumLegCountProven=selectedStockCover==null && fullSearchCompleted && common.EngineerSearch && (t.EngineerNetwork==null || !EngineerPrimaryPlan.Adjacent(t.EngineerNetwork,t.EngineerBeam).Any()),
                    WiderCeilingStable=selectedStockCover==null && fullSearchCompleted && common.EngineerSearch && result?.Transfer?.EngineerReceiverCount==3,
                    TighterFailureMinimumProven=selectedStockCover==null && fullSearchCompleted && common.EngineerSearch && !common.FirstCompleteNetworkOnly && result?.Transfer?.EngineerReceiverCount==8});
            }
            return result;
        }
        private static BeamDistributedLoad[] CrossingBands(TransferNetworkInput network,int bay,double length,double cover)
        {
            double slab=network.AreaLoad(network.Bays[bay].ThicknessMm);
            return network.CrossingBeams.Where(c=>c.BayNumber==bay+1).Select(c=>new BeamDistributedLoad {
                StartMm=c.PositionMm-c.WidthMm/2,EndMm=c.PositionMm+c.WidthMm/2,
                KnPerM=c.SupportMode==CrossBeamSupportMode.LegacySlabRows ? (network.AreaLoad(c.DepthMm)-slab)/slab : -1}).Concat(network.UseCrossingSlabLedgers && network.HasIndividualSpacings && network.CrossingBeams.Any(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows) ? network.CrossSlabLayout(length,cover,bay).Redistribution : new List<BeamDistributedLoad>()).ToArray();
        }
        public static double FootDisplacement(TowerLayout beam,double beamWidth,int loadCase,int row,bool left)
        {
            var t=beam.Transfer;double station=t.RowsMm[row];int run=Array.FindIndex(beam.StockPlan.Pieces,p=>station>=p.StartMm && station<p.EndMm);var stock=beam.StockPlan.Pieces[run];
            double dl=t.PrimaryCases[loadCase][run].Analysis.Response(station-stock.StartMm)[2],dr=t.PrimaryCases[loadCase][run+beam.StockPlan.Pieces.Length].Analysis.Response(station-stock.StartMm)[2];
            double x=(left ? -1 : 1)*(beamWidth/2+(left ? t.LeftFootMm : t.RightFootMm));
            return t.LowerCases[loadCase][row].Analysis.Response(x-t.LowerStockStartMm)[2]+dl+(dr-dl)*(x-beam.TransverseOffsetMm+beam.WidthMm/2)/beam.WidthMm;
        }
        private static int Case(TowerLayout b,bool left,bool right)=>b.Transfer.FullLoadingOnly ? 0 : Array.IndexOf(b.Transfer.CaseNames,left && right ? "Beam + both slabs" : left ? "Beam + left slab" : right ? "Beam + right slab" : "Beam");
        private static void CalculateMovement(TransferNetworkResult result,BeamSearchTrace trace)
        {
            for(int bay=0;bay<result.Bays.Length;bay++)for(int mask=0;mask<4;mask++)
            {
                if(result.Beams[bay].Transfer.FullLoadingOnly)
                {
                    int full=(bay>0 && result.Bays[bay-1]!=null ? 1 : 0)|(bay+1<result.Bays.Length && result.Bays[bay+1]!=null ? 2 : 0);
                    if(mask!=full)continue;
                }
                if(result.Bays[bay]==null || (mask&1)!=0 && (bay==0 || result.Bays[bay-1]==null) || (mask&2)!=0 && (bay==result.Bays.Length-1 || result.Bays[bay+1]==null))continue;
                var left=result.Beams[bay];var right=result.Beams[bay+1];var upper=left.Transfer.UpperMembers.Single(m=>m.Name=="Upper right");
                int lc=Case(left,(mask&1)!=0,true),rc=Case(right,true,(mask&2)!=0);
                for(int row=0;row<result.SlabRowsMm.Length;row++)
                {
                    trace?.CheckUserCancellation();
                    if(!result.SlabRowActive(bay,row))continue;
                    int lr=result.SlabBeamRow(bay,row),rr=result.SlabBeamRow(bay+1,row);
                    double dl=FootDisplacement(left,result.Input.Beams[bay].WidthMm,lc,lr,false),dr=FootDisplacement(right,result.Input.Beams[bay+1].WidthMm,rc,rr,true);
                    if(CrossBeamSupportOptimizer.Modern(result.Input))
                    {
                        string leftStage=left.Transfer.CaseNames[lc],rightStage=right.Transfer.CaseNames[rc];
                        dl=Enumerable.Range(0,left.Transfer.CaseNames.Length).Where(c=>left.Transfer.CaseNames[c]==leftStage || left.Transfer.CaseNames[c].StartsWith(leftStage+" + X",StringComparison.Ordinal)).Min(c=>FootDisplacement(left,result.Input.Beams[bay].WidthMm,c,lr,false));
                        dr=Enumerable.Range(0,right.Transfer.CaseNames.Length).Where(c=>right.Transfer.CaseNames[c]==rightStage || right.Transfer.CaseNames[c].StartsWith(rightStage+" + X",StringComparison.Ordinal)).Min(c=>FootDisplacement(right,result.Input.Beams[bay+1].WidthMm,c,rr,true));
                    }
                    var analysis=upper.Analysis.Scale((left.Transfer.RightSlabSourceScales?[row]??left.Transfer.UpperScale(false,lr))/Math.Max(left.Transfer.RightUpperScales.Max(),left.Transfer.SlabSpacing(false)/1000));
                    double movement=analysis.DeflectionWithAffineOffsetMm(dl,dr,upper.SupportsMm[0],upper.SupportsMm[1]);
                    result.SlabMovementMm=Math.Max(result.SlabMovementMm,movement);
                    left.Transfer.SlabMovementMm=Math.Max(left.Transfer.SlabMovementMm,movement);right.Transfer.SlabMovementMm=Math.Max(right.Transfer.SlabMovementMm,movement);
                }
            }
        }
    }
    /// <summary>A consistent network loading stage, ranked by the mean of four category maxima.</summary>
    public sealed class SystemLoadingStage
    {
        public bool[] Slabs;
        public int[] Crossings,BeamCases;
        public double Bending,Shear,Deflection,LegLoad;
        public double Score=>(Bending+Shear+Deflection+LegLoad)/4;
        public string Name=>"Beams"+string.Concat(Slabs.Select((on,i)=>on ? " + S"+(i+1) : ""))+string.Concat(Crossings.Select(i=>" + X"+(i+1)));
        private static bool Left(string name)=>name.Contains("left slab") || name.Contains("both slabs");
        private static bool Right(string name)=>name.Contains("right slab") || name.Contains("both slabs");
        private static int[] Cross(string name)=>name.Split('+').Select(x=>x.Trim()).Where(x=>x.Length>1 && x[0]=='X' && int.TryParse(x.Substring(1),out _)).Select(x=>int.Parse(x.Substring(1))-1).OrderBy(x=>x).ToArray();
        private static double[] Metrics(TowerLayout layout,int stage)
        {
            var t=layout.Transfer;var name=t.CaseNames[stage];
            var members=t.PrimaryCases[stage].Concat(t.LowerCases[stage]).Concat((t.UpperMembers??new TransferMember[0]).Where(m=>m.Name.Contains("left") ? Left(name) : Right(name))).ToArray();
            return new[] {members.Select(m=>Math.Max(m.Analysis.MomentKnM,m.SubsetMomentBoundKnM)/m.MomentLimitKnM).DefaultIfEmpty().Max(),
                members.Select(m=>Math.Max(m.Analysis.ShearKn,m.SubsetShearBoundKn)/m.ShearLimitKn).DefaultIfEmpty().Max(),
                members.Select(m=>Math.Max(m.Analysis.DeflectionUtilization,m.SubsetDeflectionBound)).DefaultIfEmpty().Max(),
                t.PrimaryCases[stage].SelectMany(m=>m.Analysis.Reactions).Select(q=>Math.Max(0,q)/layout.LegCapacityKn).DefaultIfEmpty().Max()};
        }
        private static double[] CrossMetrics(CrossBeamSupportResult support)
        {
            var members=support.Members;
            return new[] {members.Select(m=>m.Analysis.MomentKnM/m.MomentLimitKnM).DefaultIfEmpty().Max(),
                members.Select(m=>m.Analysis.ShearKn/m.ShearLimitKn).DefaultIfEmpty().Max(),
                members.Select(m=>m.Analysis.DeflectionUtilization).DefaultIfEmpty().Max(),
                support.Towers==null ? 0 : support.Towers.MaxLegKn/support.Towers.LegCapacityKn};
        }
        private static void Include(SystemLoadingStage state,double[] m)
        {state.Bending=Math.Max(state.Bending,m[0]);state.Shear=Math.Max(state.Shear,m[1]);state.Deflection=Math.Max(state.Deflection,m[2]);state.LegLoad=Math.Max(state.LegLoad,m[3]);}
        private static SystemLoadingStage Copy(SystemLoadingStage s)=>new SystemLoadingStage {Slabs=(bool[])s.Slabs.Clone(),Crossings=s.Crossings,BeamCases=(int[])s.BeamCases.Clone(),Bending=s.Bending,Shear=s.Shear,Deflection=s.Deflection,LegLoad=s.LegLoad};
        private static bool Dominates(SystemLoadingStage a,SystemLoadingStage b)=>a.Bending>=b.Bending && a.Shear>=b.Shear && a.Deflection>=b.Deflection && a.LegLoad>=b.LegLoad;
        private static void AddFrontier(List<SystemLoadingStage> list,SystemLoadingStage state,int beam)
        {
            bool right=beam<state.Slabs.Length && state.Slabs[beam];
            Func<SystemLoadingStage,bool> compatible=s=>(beam<s.Slabs.Length && s.Slabs[beam])==right;
            if(list.Any(s=>compatible(s) && Dominates(s,state)))return;
            list.RemoveAll(s=>compatible(s) && Dominates(state,s));list.Add(state);
        }
        private static int Resolve(TransferNetworkResult network,int beam,bool left,bool right,int[] crossings)
        {
            var relevant=network.CrossSupports.Where(s=>s.Bearings.Any(b=>b.Beam==beam)).Select(s=>s.CrossingIndex).ToArray();
            var expected=crossings.Where(relevant.Contains).OrderBy(x=>x).ToArray();
            return Array.FindIndex(network.Beams[beam].Transfer.CaseNames,n=>Left(n)==left && Right(n)==right && Cross(n).SequenceEqual(expected));
        }
        public static SystemLoadingStage Governing(TransferNetworkResult network)
        {
            var metrics=network.Beams.Select(b=>Enumerable.Range(0,b.Transfer.CaseNames.Length).Select(c=>Metrics(b,c)).ToArray()).ToArray();
            if(network.Beams.All(b=>b.Transfer.FullLoadingOnly))
            {
                var full=new SystemLoadingStage {Slabs=network.Bays.Select(b=>b!=null).ToArray(),Crossings=network.CrossSupports.Select(s=>s.CrossingIndex).OrderBy(i=>i).ToArray(),BeamCases=new int[network.Beams.Length]};
                for(int b=0;b<network.Beams.Length;b++)Include(full,metrics[b][0]);
                foreach(var support in network.CrossSupports)Include(full,CrossMetrics(support));return full;
            }
            var supportMetrics=network.CrossSupports.ToDictionary(s=>s.CrossingIndex,CrossMetrics);
            var ids=network.CrossSupports.Select(s=>s.CrossingIndex).OrderBy(x=>x).ToArray();
            int combinations=ids.Length<=8 ? 1<<ids.Length : 2;
            SystemLoadingStage best=null;
            for(int mask=0;mask<combinations;mask++)
            {
                var active=ids.Where((id,i)=>ids.Length>8 ? mask==1 : (mask&(1<<i))!=0).ToArray();
                var seed=new SystemLoadingStage {Slabs=new bool[network.Bays.Length],BeamCases=new int[network.Beams.Length],Crossings=active};
                foreach(int cross in active)Include(seed,supportMetrics[cross]);
                var frontier=new List<SystemLoadingStage> {seed};
                for(int beam=0;beam<network.Beams.Length && frontier.Count>0;beam++)
                {
                    var next=new List<SystemLoadingStage>();
                    foreach(var state in frontier)for(int right=0;right<2;right++)
                    {
                        if(right==1 && (beam>=network.Bays.Length || network.Bays[beam]==null))continue;
                        int stage=Resolve(network,beam,beam>0 && state.Slabs[beam-1],right==1,active);if(stage<0)continue;
                        var candidate=Copy(state);candidate.BeamCases[beam]=stage;if(beam<candidate.Slabs.Length)candidate.Slabs[beam]=right==1;
                        Include(candidate,metrics[beam][stage]);AddFrontier(next,candidate,beam);
                    }
                    frontier=next;
                }
                foreach(var state in frontier)if(best==null || state.Score>best.Score+1e-12)best=state;
            }
            if(best==null)throw new InvalidOperationException("No consistent checked system loading stage is available.");
            return best;
        }
        public static SystemLoadingStage Select(TransferNetworkResult network,SystemLoadingStage current,int beam,int stage)
        {
            var state=Copy(current);var name=network.Beams[beam].Transfer.CaseNames[stage];
            if(beam>0)state.Slabs[beam-1]=Left(name);if(beam<state.Slabs.Length)state.Slabs[beam]=Right(name);
            var relevant=network.CrossSupports.Where(s=>s.Bearings.Any(b=>b.Beam==beam)).Select(s=>s.CrossingIndex).ToArray();
            state.Crossings=current.Crossings.Except(relevant).Concat(Cross(name)).Distinct().OrderBy(x=>x).ToArray();
            state.Bending=state.Shear=state.Deflection=state.LegLoad=0;
            for(int b=0;b<network.Beams.Length;b++)
            {
                int c=Resolve(network,b,b>0 && state.Slabs[b-1],b<state.Slabs.Length && state.Slabs[b],state.Crossings);
                if(c<0)return current;state.BeamCases[b]=c;Include(state,Metrics(network.Beams[b],c));
            }
            foreach(var support in network.CrossSupports.Where(s=>state.Crossings.Contains(s.CrossingIndex)))Include(state,CrossMetrics(support));
            return state;
        }
        public static int Governing(TowerLayout layout)
            =>Enumerable.Range(0,layout.Transfer.CaseNames.Length).OrderByDescending(c=>Metrics(layout,c).Average()).First();
    }
}
