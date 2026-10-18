using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        public string Label {get;set;}
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
        public string Label {get;set;}
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
            return new TransferNetworkInput {Beams=Beams.Take(count).ToArray(),Bays=Bays.Take(count-1).ToArray(),
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
                var adjacent=new double[0];double edge=(length-cover)/2;
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
            double edge=(length-cover)/2;
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
        public double[] SlabRowsMm=>Beams[0].Transfer.SlabRowIndices==null ? Beams[0].Transfer.RowsMm : Beams[0].Transfer.SlabRowIndices.Select(i=>Beams[0].Transfer.RowsMm[i]).ToArray();
        public int SlabBeamRow(int beam,int row)=>Beams[beam].Transfer.SlabRowIndices?[row]??row;
        public bool SlabRowActive(int bay,int row)=>(!Input.HasIndividualSpacings || Beams[bay].Transfer.RightUpperScales[SlabBeamRow(bay,row)]>0) && !Input.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && c.BayNumber==bay+1 && Math.Abs(Beams[0].Transfer.RowsMm[SlabBeamRow(0,row)]-c.PositionMm)<c.WidthMm/2+40-1e-7);
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
        private sealed class State { public TowerLayout[] Beams; public UpperTransferPlan[] Bays; public int Legs,Separate,Narrow,BaysCount;public double Timber,BracePenalty,Symmetry; }
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
                PositionsMm=selected.PositionsMm.Reverse().Select(x=>selected.LengthMm-x).ToArray(),Joined=selected.Joined.Reverse().ToArray(),
                StockPlan=new TowerStockPlan {PrimaryMaterial=selected.PrimaryMaterial,Pieces=selected.StockPlan.Pieces.Reverse().Select(p=>new TowerStockPiece {StartMm=selected.LengthMm-p.EndMm,StockLengthMm=p.StockLengthMm,FirstSupport=legs-1-p.LastSupport,LastSupport=legs-1-p.FirstSupport}).ToArray()}},"Mirrored tower/primary arrangement"));
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
            var result=OptimizeNetwork(common,input);
            CrossBeamSupportOptimizer.BalanceSettingOut(common,result);
            CalculateMovement(result,common.Trace);
            return result;
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
                common.NetworkGeometryWork=common.ReuseSearchWork ? new TransferredSlabOptimizer.GeometryCache() : null;
                using(var numericalWork=common.ReuseSearchWork ? new ThreadLocal<TransferredSlabOptimizer.SearchWorkCache>(()=>new TransferredSlabOptimizer.SearchWorkCache()) : null)
                {
                    common.NetworkNumericalWork=numericalWork;
                    try {return IndividualTransfer(common,input);}
                    finally {common.NetworkGeometryWork=previousGeometry;common.NetworkNumericalWork=previousNumerics;}
                }
            }
            return LegacyTransfer(common,input);
        }
        private static TransferNetworkResult SearchSpacing(TowerInput common,TransferNetworkInput input)
        {
            common.Trace?.Increment(BeamSearchCounter.NetworkSpacingTrials);
            return TowerStockPlanner.SearchWithCustom(common,()=>OptimizeCore(common,input,false),Better);
        }
        private static TransferNetworkResult IndividualTransfer(TowerInput common,TransferNetworkInput input)
        {
            // Find a checked seed with the existing bounded descent, then recover
            // independent maxima. Never enumerate the Cartesian product of pitches.
            TowerOptimizationException failure;
            var first=common;
            if(common.EngineerSearch && CrossBeamSupportOptimizer.Modern(input))
            {
                // Start with the whole junction. A cheapest provisional receiver
                // can obstruct its crossing and adds no useful uncoordinated pass.
                first=common.CopyForSearch();first.CoordinateCrossingLayouts=true;
            }
            try {return SearchSpacing(first,input);}
            catch(TowerOptimizationException ex){failure=ex;}
            // The caller needs failed coordinated-search evidence for separate-formwork reasons.
            finally {common.Diagnostics=first.Diagnostics;}
            var worker=common.CopyForSearch();worker.CoordinateCrossingLayouts=CrossBeamSupportOptimizer.Modern(input);
            var attempted=new HashSet<string>();
            TransferNetworkResult best=null;
            Func<TransferNetworkInput,string> keyOf=n=>string.Join(";",n.BeamSpacingsMm.Concat(n.SlabSpacingsMm).Concat(n.CrossingSpacingsMm));
            if(!worker.CoordinateCrossingLayouts || first.CoordinateCrossingLayouts)attempted.Add(keyOf(input));
            // Try a single category step before a blanket seed. Receiving-beam
            // failure must not automatically tighten slabs or dedicated crossings.
            var closer=input.WithSpacings(1);
            var seeds=new List<TransferNetworkInput>();
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
                // Retain receiving-beam maxima first, then crossings and slab bays.
                // Group successes cost one solve; failed groups split deterministically.
                // At most 2N-1 group trials plus five intermediate trials per element.
                recover(Enumerable.Range(0,input.Beams.Length).ToArray());
                recover(Enumerable.Range(input.Beams.Length+input.Bays.Length,input.CrossingBeams.Length).ToArray());
                recover(Enumerable.Range(input.Beams.Length,input.Bays.Length).ToArray());
            }
            catch(BeamSearchDeadlineException)
            {
                best.SearchTimeLimitReached=true;
                foreach(var beam in best.Beams) {beam.SearchTimeLimitReached=true;beam.MinimumLegCountProven=false;}
            }
            common.Trace?.CheckUserCancellation();common.Diagnostics=acceptedDiagnostics;return best;
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
        private sealed class EngineerBound
        {
            private int legs=int.MaxValue;
            private readonly int minimumDedicatedLegs;
            internal EngineerBound(TransferNetworkInput input)
            {
                // Dedicated formwork needs at least one four-leg tower. A negative
                // filler gap makes the shared mode impossible at every receiver pose.
                minimumDedicatedLegs=4*input.CrossingBeams.Count(c=>c.SupportMode==CrossBeamSupportMode.DedicatedTowers
                    || c.SupportMode==CrossBeamSupportMode.Automatic && (!CrossBeamSupportOptimizer.ValidFillerGap(c.BottomElevationMm,input.Beams[c.BayNumber-1].BottomElevationMm,out var near)
                        || !CrossBeamSupportOptimizer.ValidFillerGap(c.BottomElevationMm,input.Beams[c.BayNumber].BottomElevationMm,out var far)));
            }
            internal int Ceiling(int used,int remaining,int minimum)=>Math.Max(0,Volatile.Read(ref legs)-used-remaining*minimum-minimumDedicatedLegs);
            internal void Publish(TransferNetworkResult result)
            {
                int value=result.LegCount,old;
                do{old=Volatile.Read(ref legs);if(value>=old)return;}while(Interlocked.CompareExchange(ref legs,value,old)!=old);
            }
        }
        internal static int SearchWorkers(int requested,int processors)=>Math.Max(1,Math.Min(Math.Min(8,Math.Max(1,processors)),requested));
        private static TransferNetworkResult OptimizeCore(TowerInput common,TransferNetworkInput input,bool separate)
        {
            input.Validate(common); TransferNetworkResult best=null;int tested=0;
            common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift) {LazyLocations=common.LazySearchDiagnostics};var diag=common.Diagnostics;
            var geometryCache=common.ReuseSearchWork ? common.NetworkGeometryWork ?? new TransferredSlabOptimizer.GeometryCache() : null;
            var incumbent=!common.EngineerSearch && common.PruneNetworkBounds && !separate && input.CrossingBeams.All(c=>c.SupportMode==CrossBeamSupportMode.LegacySlabRows) ? new Incumbent() : null;
            var engineerBound=common.EngineerSearch && common.PruneNetworkBounds && !separate ? new EngineerBound(input) : null;
            common.Trace?.Work("Network primary cover generation");
            double[][] stockCovers;
            using(common.Trace?.Measure(BeamSearchStage.StockCovers))stockCovers=TransferredSlabOptimizer.StockCovers(common);
            if(stockCovers.Length==0)
            {
                diag.Geometry("primary coverage",$"Primary coverage: no available stock combination totals {common.LengthMm-2*common.MaximumConcreteEndOverhangMm:0.###}–{common.LengthMm:0.###} mm.","Review available primary stocks and the maximum concrete end allowance.");
                throw diag.Exception("No primary stock coverage fits the beam; separate slab formwork cannot fix missing beam stock.");
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
            int minimumLegs=stockCovers.Min(c=>c.Length)*4;
            var scenarios=stockCovers.Select(c=>c.Sum()).Distinct().SelectMany(cover=>(input.PlywoodSheetLengthMm>0 ? new[] {0.0} : new[] {0.0,-50,50}).Select(phase=>Tuple.Create(cover,phase))).ToArray();
            if(common.Trace?.HasTimeLimit==true)
                scenarios=scenarios.OrderBy(s=>Math.Abs(s.Item1-(common.LengthMm-common.MaximumConcreteEndOverhangMm))).ThenBy(s=>Math.Abs(s.Item2)).ToArray();
            var outcomes=new ScenarioResult[scenarios.Length];
            int workers=SearchWorkers(common.NetworkSearchWorkers,Environment.ProcessorCount);
            common.Trace?.Workers(workers);
            using(var numericalWork=new ThreadLocal<TransferredSlabOptimizer.SearchWorkCache>(()=>new TransferredSlabOptimizer.SearchWorkCache()))
            {
                Action<int> run=index=>
                {
                    var worker=common.CopyForSearch();
                    worker.DedicatedCrossCache=new Dictionary<string,DedicatedCrossSearch>();
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
                double edge=(common.LengthMm-cover)/2;
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
        private static ScenarioResult CoordinatedScenario(TowerInput common,TransferNetworkInput input,double[][] covers,TransferredSlabOptimizer.GeometryCache geometryCache,UpperTransferPlan[][] options,double cover,double phase,int rows,EngineerBound engineerBound=null)
        {
            var outcome=new ScenarioResult {Diagnostics=common.Diagnostics};
            var solveCache=new Dictionary<Tuple<int,UpperTransferPlan,UpperTransferPlan>,Tuple<int,TowerLayout[]>>();
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
                var alternatives=common.EngineerSearch && common.ReuseSearchWork ? new Dictionary<double,TowerLayout>() : null;
                var best=Solve(common,input,beam,left,right,cover,phase,covers,geometryCache,ceiling,alternatives:alternatives);if(best!=null)choices.Add(best);
                // The unrestricted solve already searches all these widths. A failed
                // solve proves each centred fixed-width subset fails too; offset searches
                // below remain necessary because they change the receiving geometry.
                if(best!=null || !common.ReuseSearchWork)foreach(double width in new[] {1000.0,1524,1500,2000})
                {
                    if(best!=null && Math.Abs(best.WidthMm-width)<1e-7)continue;
                    TowerLayout alternative;
                    if(alternatives==null || !alternatives.TryGetValue(width,out alternative))alternative=Solve(common,input,beam,left,right,cover,phase,covers,geometryCache,ceiling,width);
                    if(alternative!=null)choices.Add(alternative);
                }
                if(common.EngineerSearch && common.EngineerJunctionExtraTiers>0 && best!=null)
                {
                    // A sparse receiver may pass its local loads but obstruct the
                    // crossing tower. Keep the next leg tiers until the whole junction fits.
                    foreach(int extra in Enumerable.Range(1,common.EngineerJunctionExtraTiers))
                    {
                        var larger=common.CopyForSearch();larger.EngineerMinimumStations=best.PositionsMm.Length+extra;
                        var alternative=Solve(larger,input,beam,left,right,cover,phase,covers,geometryCache,larger.EngineerMinimumStations*2,best.WidthMm);
                        if(alternative!=null)choices.Add(alternative);
                    }
                }
                foreach(double offset in new[] {-25.0,25})
                {
                    var alternative=Solve(common,input,beam,left,right,cover,phase,covers,geometryCache,ceiling,1000,offset);
                    if(alternative!=null)choices.Add(alternative);
                }
                receivers=choices.OrderBy(l=>common.EngineerSearch ? l.LegCount : TransferredSlabOptimizer.Economy(l)).ThenBy(l=>l.Standalone600Count).ThenBy(l=>l.BayCount).ThenBy(l=>l.StandaloneBracePenalty).ThenBy(l=>l.CantileverImbalanceMm).ThenBy(l=>l.SymmetryErrorMm).ThenBy(l=>l.BraceLocationPenalty).ToArray();solveCache[key]=Tuple.Create(ceiling,receivers);return receivers;
            }
            State Extend(State previous,UpperTransferPlan right,TowerLayout receiver,int beam)
            {
                outcome.Tested++;var candidate=new State {Beams=previous.Beams.Concat(new[] {receiver}).ToArray(),Bays=beam==input.Bays.Length ? previous.Bays : previous.Bays.Concat(new[] {right}).ToArray(),Separate=previous.Separate+(beam<input.Bays.Length && right==null ? 1 : 0),Legs=previous.Legs+receiver.LegCount,Narrow=previous.Narrow+receiver.Standalone600Count,BaysCount=previous.BaysCount+receiver.BayCount,BracePenalty=previous.BracePenalty+receiver.StandaloneBracePenalty,Symmetry=previous.Symmetry+receiver.SymmetryErrorMm,Timber=previous.Timber+LowerTimber(receiver)+(right==null ? 0 : right.StockLengthMm*EffectiveWidthsMm(common,input,beam,cover,phase).Count(x=>x>0))};
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
                    using(common.Trace?.Measure(BeamSearchStage.CrossingSupports))crossingPassed=CrossBeamSupportOptimizer.Complete(worker,partial,beam<input.Beams.Length-1);
                    if(!crossingPassed)return null;
                    if(beam==input.Beams.Length-1) {outcome.Improvements.Add(partial);engineerBound?.Publish(partial);common.Trace?.Increment(BeamSearchCounter.CompleteNetworkCandidates);return null;}
                }
                return candidate;
            }
            bool Seed(State previous,int beam,ref int remaining)
            {
                foreach(var right in beam==input.Bays.Length ? new UpperTransferPlan[] {null} : options[beam].Take(2))
                {
                    common.Trace?.CheckCancellation();if(remaining--<=0)return false;
                    int ceiling=engineerBound?.Ceiling(previous.Legs,input.Beams.Length-beam-1,covers.Min(c=>c.Length)*4)??int.MaxValue;
                    foreach(var receiver in Receivers(beam,previous.Bays.LastOrDefault(),right,ceiling).Where(r=>r.LegCount<=ceiling).Take(2))
                    {
                        int before=outcome.Improvements.Count;var next=Extend(previous,right,receiver,beam);
                        if(outcome.Improvements.Count>before){common.Trace?.Increment(BeamSearchCounter.SeedNetworkCandidates);return true;}
                        if(next!=null && beam+1<input.Beams.Length && Seed(next,beam+1,ref remaining))return true;
                    }
                }
                return false;
            }
            var states=new[] {new State {Beams=new TowerLayout[0],Bays=new UpperTransferPlan[0]}}.ToList();
            try
            {
                if(common.EngineerWorkflow && common.SeedNetworkSearch)
                {
                    // Explore a small complete path before expanding every slab
                    // option at B1/B2. The ordinary bounded search still follows.
                    int remaining=input.Beams.Length*2+8;Seed(states[0],0,ref remaining);
                }
                for(int beam=0;beam<input.Beams.Length && states.Count>0;beam++)
                {
                    common.Trace?.CheckCancellation();
                    var next=new Dictionary<UpperTransferPlan,List<State>>();var closed=new List<State>();
                    foreach(var previous in states)
                    foreach(var right in beam==input.Bays.Length ? new UpperTransferPlan[] {null} : options[beam])
                    {
                        var left=previous.Bays.LastOrDefault();
                        int ceiling=engineerBound?.Ceiling(previous.Legs,input.Beams.Length-beam-1,covers.Min(c=>c.Length)*4)??int.MaxValue;
                        if(ceiling<covers.Min(c=>c.Length)*4){common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);continue;}
                        var receivers=Receivers(beam,left,right,ceiling);
                        foreach(var receiver in receivers)
                        {
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
            if(common.EngineerSearch && common.EngineerJunctionExtraTiers==0 && outcome.Improvements.Count==0 && common.Trace?.BudgetReached!=true)
            {
                var expanded=common.CopyForSearch();expanded.EngineerJunctionExtraTiers=2;
                var alternative=CoordinatedScenario(expanded,input,covers,geometryCache,options,cover,phase,rows,engineerBound);
                alternative.Tested+=outcome.Tested;return alternative;
            }
            return outcome;
        }

        // Incremental crossing loads go only to rows beneath the beam footprint, with closing tributaries.
        // The transverse beam spans the entire clear bay; subtract existing slab concrete and allowances once.
        private static double[] RowStations(TowerInput common,TransferNetworkInput input,double cover,double phase)
        {
            double edge=(common.LengthMm-cover)/2;
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
            int count=stations.Length;double edge=(common.LengthMm-cover)/2;
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
        private static double[] BayBaseline(TowerInput common,TransferNetworkInput input,int bay,UpperTransferPlan plan,double cover,double phase)
        {
            var all=RowStations(common,input,cover,phase);
            if(plan==null)return new double[all.Length];
            var own=input.SlabRows(common.LengthMm,cover)[bay];double edge=(common.LengthMm-cover)/2;
            var widths=PlywoodSpacing.RowTributaries(edge,edge+cover,own);
            return all.Select(x=>Array.IndexOf(own,x)).Select(i=>i<0 ? 0 : widths[i]).ToArray();
        }
        private static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers,TransferredSlabOptimizer.GeometryCache geometryCache,int ceiling,double? towerWidth=null,double transverseOffset=0,Dictionary<double,TowerLayout> alternatives=null)
        {
            common.Trace?.CheckCancellation();
            if(ceiling<stockCovers.Min(c=>c.Length)*4) {common.Trace?.Increment(BeamSearchCounter.NetworkBranchesPruned);return null;}
            common.Trace?.Increment(BeamSearchCounter.NetworkSolves);common.Trace?.Context(System.FormattableString.Invariant($"B{beam+1}; coverage={cover:R} mm; row phase={phase:R} mm; beam spacing={network.BeamSpacing(beam):R} mm; crossing coordination={common.CoordinateCrossingLayouts}"));
            var b=network.Beams[beam];var t=new TransferredSlabInput {PlywoodProfileId=network.PlywoodProfileId,PlywoodDeflectionDenominator=network.PlywoodDeflectionDenominator,PlywoodSheetLengthMm=network.PlywoodSheetLengthMm,IsNetwork=true,IncludeMainBeamEndLoads=true,FixedTowerWidthMm=towerWidth,BeamCentreOffsetMm=-transverseOffset,CrossFormwork=CrossBeamSupportOptimizer.Modern(network),AllowLongerPrimaryCoverage=common.IndependentNetworkPrimaryCoverage,StockCovers=stockCovers,SharedGeometryCache=geometryCache,SharedSearchWork=common.NetworkNumerics,BeamWidthMm=b.WidthMm,BeamLineLoadKnM=network.AreaLoad(b.DepthMm)*b.WidthMm/1000,UpperSpacingMm=network.BeamSpacing(beam),LocalRowGapMm=network.LocalRowGapMm,LeftSlabSpacingMm=network.HasIndividualSpacings && left!=null ? network.BaySpacing(beam-1) : 0,RightSlabSpacingMm=network.HasIndividualSpacings && right!=null ? network.BaySpacing(beam) : 0,SharedRowsMm=network.HasIndividualSpacings ? RowStations(common,network,cover,phase) : null,FixedCoveredLengthMm=cover,FixedRowPhaseMm=phase,
                LeftBayMm=left==null ? 0 : network.Bays[beam-1].WidthMm,RightBayMm=right==null ? 0 : network.Bays[beam].WidthMm,LeftAreaLoadKnM2=left==null ? 0 : network.AreaLoad(network.Bays[beam-1].ThicknessMm),RightAreaLoadKnM2=right==null ? 0 : network.AreaLoad(network.Bays[beam].ThicknessMm),LeftPlan=left?.Reverse(),RightPlan=right,
                LeftCrossingBands=left==null ? new BeamDistributedLoad[0] : CrossingBands(network,beam-1,common.LengthMm,cover),RightCrossingBands=right==null ? new BeamDistributedLoad[0] : CrossingBands(network,beam,common.LengthMm,cover),
                LeftEffectiveWidthsMm=left==null ? null : EffectiveWidthsMm(common,network,beam-1,cover,phase),RightEffectiveWidthsMm=right==null ? null : EffectiveWidthsMm(common,network,beam,cover,phase),
                LeftBaselineWidthsMm=network.HasIndividualSpacings ? BayBaseline(common,network,beam-1,left,cover,phase) : null,RightBaselineWidthsMm=network.HasIndividualSpacings ? BayBaseline(common,network,beam,right,cover,phase) : null};
            var input=new TowerInput {OptimizePrimaryResponses=common.OptimizePrimaryResponses,SeedNetworkSearch=common.SeedNetworkSearch,ReceiverAlternatives=alternatives,EngineerSearch=common.EngineerSearch,EngineerMinimumStations=common.EngineerMinimumStations,FullLoadingOnly=common.FullLoadingOnly,DeferCrossingChecks=common.FullLoadingOnly && network.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && (c.BayNumber==beam || c.BayNumber==beam+1)),PrimaryMaterial=common.PrimaryMaterial,AllowLocalShutterExceptions=common.AllowLocalShutterExceptions,ReceiverTransverseOffsetMm=transverseOffset,IgnoreUplift=common.IgnoreUplift,Trace=common.Trace,ReuseSearchWork=common.ReuseSearchWork,PruneUneconomicCandidates=common.PruneUneconomicCandidates,StagePrimaryChecks=common.StagePrimaryChecks,ReuseNetworkNumerics=common.ReuseNetworkNumerics,ReuseTransverseNumerics=common.ReuseTransverseNumerics,LazySearchDiagnostics=common.LazySearchDiagnostics,DeferTransferLayout=common.DeferTransferLayout,SearchByLegTier=common.SearchByLegTier,NetworkLegCeiling=ceiling,AllowCustomH20Lengths=common.AllowCustomH20Lengths,CustomH20Scope=common.CustomH20Scope,LengthMm=common.LengthMm,LegCapacityKn=common.LegCapacityKn,DeflectionDenominator=common.DeflectionDenominator,DeflectionLimitMm=common.DeflectionLimitMm,StockLengthsMm=common.StockLengthsMm,StockEndSetbackMm=common.StockEndSetbackMm,MaximumConcreteEndOverhangMm=common.MaximumConcreteEndOverhangMm,Exclude600Standalone=common.Exclude600Standalone,TransferredSlab=t,Diagnostics=common.Diagnostics,DiagnosticScope=$"B{beam+1} · "};
            if(common.EngineerWorkflow)
            {
                var plan=EngineerPrimaryPlan.Create(common,network,beam,left!=null,right!=null);
                if(plan.UseDesignZones || plan.HasCrossings)input.EngineerPlan=plan;
                if(plan.HasCrossings){t.EngineerNetwork=network;t.EngineerBeam=beam;}
            }
            if(common.CoordinateCrossingLayouts)
            {
                input.TransferLayoutAcceptance=layout=>(t.EngineerNetwork==null || CrossBeamSupportOptimizer.CheckEngineerReceiver(input,network,beam,layout)) && CrossBeamSupportOptimizer.CheckJunctionReceiver(input,network,beam,layout);
                if(common.EngineerSearch)input.TransferLowerAcceptance=layout=>(t.EngineerNetwork==null || CrossBeamSupportOptimizer.CheckEngineerReceiver(input,network,beam,layout,true)) && CrossBeamSupportOptimizer.CheckJunctionLowerReceiver(input,network,beam,layout);
            }
            try {using(common.Trace?.Measure(BeamSearchStage.ReceivingBeams))return TransferredSlabOptimizer.Optimize(input,true);}
            catch(TowerOptimizationException){return null;}
            catch(ArgumentException ex) {common.Diagnostics.Geometry("B"+(beam+1)+" geometry",$"B{beam+1}: {ex.Message}","Review this beam's geometry and transfer inputs.");return null;}
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
                    var analysis=upper.Analysis.Scale(left.Transfer.UpperScale(false,lr)/Math.Max(left.Transfer.RightUpperScales.Max(),left.Transfer.SlabSpacing(false)/1000));
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
