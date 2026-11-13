using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EngineeringDashboard
{
    public sealed class TransferredSlabInput
    {
        internal TransferredSlabInput Copy()=>(TransferredSlabInput)MemberwiseClone();
        public double BeamWidthMm, BeamLineLoadKnM, UpperSpacingMm;
        // Point reactions from slab-bearing ledgers, per metre along the crossing span.
        internal BeamPointLoad[] BeamEdgeLoadsKnM=Array.Empty<BeamPointLoad>();
        internal double TotalBeamLineLoadKnM=>BeamLineLoadKnM+BeamEdgeLoadsKnM.Sum(p=>p.Kn);
        internal double LocalRowGapMm;
        internal double RowGapMm=>LocalRowGapMm>0 ? LocalRowGapMm : UpperSpacingMm;
        public double PlywoodSheetLengthMm;
        public string PlywoodProfileId;
        public int PlywoodDeflectionDenominator;
        public double LeftSlabSpacingMm, RightSlabSpacingMm;
        internal double[] SharedRowsMm, LeftBaselineWidthsMm, RightBaselineWidthsMm;
        internal bool LocalReceivingRows;
        internal double[] ReceivingRowsMm=new double[0];
        internal TransferCrossingBeamRow[] ReceivingCrossings=new TransferCrossingBeamRow[0];
        internal bool HasIndividualSpacings=>SharedRowsMm!=null || LeftSlabSpacingMm>0 || RightSlabSpacingMm>0;
        public double SlabSpacing(bool left)=>(left ? LeftSlabSpacingMm : RightSlabSpacingMm)>0 ? (left ? LeftSlabSpacingMm : RightSlabSpacingMm) : UpperSpacingMm;
        public double LeftBayMm, RightBayMm, LeftAreaLoadKnM2, RightAreaLoadKnM2;
        public double MaximumSlabEndStripMm(bool left)=>PlywoodSpacing.SlabEndStripLimitMm(left ? LeftAreaLoadKnM2 : RightAreaLoadKnM2,PlywoodProfileId,PlywoodDeflectionDenominator);
        public UpperTransferPlan LeftPlan, RightPlan;
        internal double[] LeftEffectiveWidthsMm, RightEffectiveWidthsMm;
        internal bool CrossFormwork;
        internal TransferNetworkInput EngineerNetwork;
        internal int EngineerBeam,EngineerReceiverCount=3;
        // Extra crossing concrete remains distributed only over its physical footprint.
        // These dimensionless factors multiply the corresponding per-metre slab reaction.
        internal BeamDistributedLoad[] LeftCrossingBands=new BeamDistributedLoad[0], RightCrossingBands=new BeamDistributedLoad[0];
        // The network solver fixes shared timber coverage and row phase, not concrete extent.
        public double? FixedCoveredLengthMm, FixedRowPhaseMm;
        internal double? FixedRowStartMm;
        internal double? LeftSlabStartMm,LeftSlabEndMm,RightSlabStartMm,RightSlabEndMm;
        internal double RowStart(TowerInput input,double coverage)=>FixedRowStartMm??input.CoverStart(coverage);
        internal double[][] StockCovers;
        internal TransferredSlabOptimizer.GeometryCache SharedGeometryCache;
        internal TransferredSlabOptimizer.SearchWorkCache SharedSearchWork;
        internal bool AllowLongerPrimaryCoverage;
        // Dedicated cross formwork carries the concrete outside stock onto the outer rows.
        internal bool IncludeConcreteEndStrips;
        internal bool IncludeMainBeamEndLoads;
        // Crossing placement retries each permitted tower width after a physical clash.
        internal double? FixedTowerWidthMm;
        internal double BeamCentreOffsetMm;
        public bool IsNetwork;
        public const double FootClearanceMm = 250;
        public const double SideShutterReactionOffsetMm = 100;
        public void Validate(TowerInput input)
        {
            if (new[] { BeamWidthMm, BeamLineLoadKnM, UpperSpacingMm, input.LengthMm, input.LegCapacityKn, input.StockEndSetbackMm }.Any(x => !TowerBeamAnalysis.Finite(x) || x <= 0)
                || !TowerBeamAnalysis.Finite(BeamCentreOffsetMm) || Math.Abs(BeamCentreOffsetMm)>10000
                || input.LengthMm > 30000 || UpperSpacingMm < 100 || UpperSpacingMm > 5000 || BeamWidthMm > 10000)
                throw new ArgumentException("Transfer mode: positive geometry and loads are required; beam length ≤30,000 mm and upper spacing 100–5,000 mm.");
            if(!TowerBeamAnalysis.Finite(LocalRowGapMm) || LocalRowGapMm<0 || LocalRowGapMm>0 && (LocalRowGapMm<100 || LocalRowGapMm>UpperSpacingMm))throw new ArgumentException("Invalid actual local secondary gap.");
            if (!TowerBeamAnalysis.Finite(PlywoodSheetLengthMm) || PlywoodSheetLengthMm<0 || PlywoodSheetLengthMm>0 && PlywoodSheetLengthMm!=PlywoodSpacing.SheetLengthMm) throw new ArgumentException("Use the supported 2440 mm sheet length.");
            if (PlywoodSheetLengthMm>0 && !PlywoodSpacing.StandardSpacingsMm.Contains(UpperSpacingMm)) throw new ArgumentException("Select a permitted sheet spacing: 488, 407, 350, 305, 275 or 250 mm.");
            if(new[] {LeftSlabSpacingMm,RightSlabSpacingMm}.Any(x=>!TowerBeamAnalysis.Finite(x) || x<0 || x>0 && !PlywoodSpacing.StandardSpacingsMm.Contains(x)))throw new ArgumentException("Invalid slab sheet spacing.");
            if(HasIndividualSpacings && PlywoodSheetLengthMm!=PlywoodSpacing.SheetLengthMm)throw new ArgumentException("Individual spacings require 2440 mm sheets.");
            if(SharedRowsMm!=null && (SharedRowsMm.Length<2 || SharedRowsMm.Any(x=>!TowerBeamAnalysis.Finite(x))
                || LeftBaselineWidthsMm?.Length!=SharedRowsMm.Length || RightBaselineWidthsMm?.Length!=SharedRowsMm.Length))throw new ArgumentException("Invalid shared slab row geometry.");
            if (!new[] { 500, 350, 270 }.Contains(input.DeflectionDenominator)) throw new ArgumentException("Transfer mode requires L/500, L/350 or L/270.");
            if (!TowerBeamAnalysis.Finite(input.MaximumConcreteEndOverhangMm) || input.MaximumConcreteEndOverhangMm < 0 || input.MaximumConcreteEndOverhangMm > 500)
                throw new ArgumentException("Concrete end allowance must be between 0 and 500 mm.");
            if (new[] { LeftBayMm, RightBayMm, LeftAreaLoadKnM2, RightAreaLoadKnM2 }.Any(x => !TowerBeamAnalysis.Finite(x) || x < 0)
                || LeftBayMm > 0 && LeftAreaLoadKnM2 <= 0 || RightBayMm > 0 && RightAreaLoadKnM2 <= 0
                || LeftBayMm > 30000 || RightBayMm > 30000 || LeftBayMm == 0 && LeftAreaLoadKnM2 != 0 || RightBayMm == 0 && RightAreaLoadKnM2 != 0 || !IsNetwork && LeftBayMm + RightBayMm == 0)
                throw new ArgumentException("Select at least one slab bay, with positive clear width and slab load.");
            if (input.StockLengthsMm == null || input.StockLengthsMm.Length == 0 || input.StockLengthsMm.Length > 30
                || input.StockLengthsMm.Any(x => !TowerBeamAnalysis.Finite(x) || x < 300 || x > 50000 || x != Math.Round(x))) throw new ArgumentException("Invalid available H20 lengths.");
            if(LeftPlan!=null)LeftPlan.Validate(LeftBayMm,input.StockLengthsMm,MaximumSlabEndStripMm(true),input.AllowCustomSlabLengths);
            if(RightPlan!=null)RightPlan.Validate(RightBayMm,input.StockLengthsMm,MaximumSlabEndStripMm(false),input.AllowCustomSlabLengths);
        }
    }
    public sealed class TransferMember
    {
        public double[] SubsetMinimumReactionsKn;
        public double MinimumReactionKn => SubsetMinimumReactionsKn==null ? Analysis.Reactions.Min() : Math.Min(Analysis.Reactions.Min(),SubsetMinimumReactionsKn.Min());
        public string Name, LoadCase;
        public double StartMm, LengthMm;
        public double[] SupportsMm;
        public BeamPointLoad[] Points;
        public BeamDistributedLoad[] Uniform;
        internal BeamDistributedLoad[] LegacyPrimaryUniform;
        public LoadedBeamAnalysis Analysis;
        // Historical 2/1 value identifies primary/secondary role. Actual quantities
        // use H20Quantity and WS10Quantity; a WS10 primary is one assembly per line.
        // Legacy role marker: 2 is a primary line; 1 is a single secondary/slab H20.
        // Use H20Quantity / WS10Quantity for actual material quantities.
        public int H20Count = 2;
        public PrimaryBeamMaterial PrimaryMaterial;
        internal double PrimarySelfWeightKnM;
        public bool IsPrimary=>H20Count==2;
        public PrimaryBeamProfile Primary=>PrimaryBeamProfile.For(PrimaryMaterial);
        public bool IsWS10=>IsPrimary && Primary.IsWS10;
        public int H20Quantity=>IsWS10 ? 0 : H20Count;
        public int WS10Quantity=>IsWS10 ? 1 : 0;
        public string MaterialDescription=>IsWS10 ? Primary.Description : H20Count==2 ? "double H20" : "single H20";
        public string StockLabel=>$"{(IsWS10 ? "WS10" : "H20")} · {LengthMm:0.###}";
        public double EI=>IsPrimary ? Primary.EI : TowerOptimizer.PairEI/2;
        public double StrengthUtilization=>IsWS10 ? Math.Max(Primary.Strength(Analysis),SubsetMomentBoundKnM/10+SubsetShearBoundKn/82) : Math.Max(Math.Max(Analysis.MomentKnM,SubsetMomentBoundKnM)/MomentLimitKnM,Math.Max(Analysis.ShearKn,SubsetShearBoundKn)/ShearLimitKn);
        public double SubsetMomentBoundKnM,SubsetShearBoundKn,SubsetDeflectionBound;
        public double MomentLimitKnM => IsPrimary ? Primary.MomentLimitKnM : TowerOptimizer.PairMoment * H20Count / 2;
        public double ShearLimitKn => IsPrimary ? Primary.ShearLimitKn : TowerOptimizer.PairShear * H20Count / 2;
        public double Utilization => Math.Max(Math.Max(Analysis.DeflectionUtilization,SubsetDeflectionBound),StrengthUtilization);
    }
    public sealed class ColumnEndStripLoad
    {
        public string LoadCase, End;
        public double StartMm, EndMm, LeftLineKn, RightLineKn;
        public double LeftLineMomentKnM,RightLineMomentKnM;
        public double TotalKn=>LeftLineKn+RightLineKn;
        public double TotalMomentKnM=>LeftLineMomentKnM+RightLineMomentKnM;
        public string Description=>$"{LoadCase} · {End} column-side supports · strip {StartMm:0.###}–{EndMm:0.###} mm · L/R {LeftLineKn:0.###}/{RightLineKn:0.###} kN · total {TotalKn:0.###} kN";
    }
    public sealed class TransferredSlabResult
    {
        internal EngineerPrimaryPlan PrimaryPlan;
        internal string PrimarySupportNotice="";
        internal string PrimaryLoadBasis=>(PrimaryPlan!=null
            ? "Positioned secondary reactions: Beam + Slab UDL outside crossings; Beam + Cross Beam UDL over each full crossing width."
            : "Bearing-derived UDLs from the actual selected support transfers.")
            +(string.IsNullOrEmpty(PrimarySupportNotice) ? "" : " "+PrimarySupportNotice)
            +(PrimarySupportNotice.Contains("dedicated tower") ? " Dedicated crossing towers carry their own assigned loads; remaining transfers are distributed within the crossing width." : "");
        internal ColumnEndStripLoad[][] LegacyColumnSupportLoads;
        internal int EngineerReceiverCount;
        internal TransferredSlabResult Copy() => (TransferredSlabResult)MemberwiseClone();
        public double[] RowsMm, TributaryWidthsMm;
        public bool FullLoadingOnly;
        // Physical setting-out can move/add lower bearers independently of slab feet. Design rows and nominal loads stay fixed.
        public bool HasCustomPlywoodJoints;
        public double[] SettingOutRowsMm;
        public int[] SettingOutDesignRows;
        public int PhysicalRowCount=>SettingOutRowsMm?.Length??RowsMm.Length;
        public double PhysicalRowPosition(int row)=>SettingOutRowsMm?[row]??RowsMm[row];
        public int PhysicalRowDesignIndex(int row)=>SettingOutDesignRows?[row]??row;
        public int AddedSecondaryCount=>PhysicalRowCount-RowsMm.Length;

        // Shared slab-row ordinal -> this beam's lower-secondary index. Other rows carry beam concrete only.
        public int[] SlabRowIndices;
        // Original slab rows stay fixed above the site bearer. These explicit maps
        // identify their local lower receivers without imposing remote bay rows.
        public double[] SharedSlabRowsMm,LeftSlabSourceScales,RightSlabSourceScales;
        public int[] SlabSourceRowIndices;
        public int BeamOnlyRowCount=>HasIndividualSpacings ? Enumerable.Range(0,RowsMm.Length).Count(i=>(LeftUpperScales?[i]??0)==0 && (RightUpperScales?[i]??0)==0) : SlabRowIndices==null ? 0 : RowsMm.Length-SlabRowIndices.Length;
        public double[] LeftUpperScales, RightUpperScales;
        public double UpperDesignWidthMm(bool left,int row)=>UpperScale(left,row)*1000;
        public string UpperSpacingDescription(bool left,int row)
        {
            var source=left ? LeftSlabSourceScales : RightSlabSourceScales;
            var stations=SharedSlabRowsMm??RowsMm;
            var active=Enumerable.Range(0,stations.Length).Where(i=>source==null ? UpperScale(left,i)>0 : source[i]>0).ToArray();
            int selected=source==null ? row : active.Where(i=>SlabSourceRowIndices[i]==row).DefaultIfEmpty(-1).First();
            int index=Array.IndexOf(active,selected);
            string before=index>0 ? (stations[selected]-stations[active[index-1]]).ToString("0.###",CultureInfo.CurrentCulture) : "—";
            string after=index>=0 && index<active.Length-1 ? (stations[active[index+1]]-stations[selected]).ToString("0.###",CultureInfo.CurrentCulture) : "—";
            return $"Nominal slab design spacing {SlabSpacing(left):0.###} mm · actual local gaps before/after {before}/{after} mm · effective design tributary width {UpperDesignWidthMm(left,row):0.###} mm";
        }
        public double GoverningUpperScale(bool left)=>Math.Max((left ? LeftUpperScales : RightUpperScales).Max(),SlabSpacing(left)/1000);
        public TransferMember UpperMemberAtRow(bool left,int row)
        {
            var original=UpperMembers.Single(x=>x.Name==(left ? "Upper left" : "Upper right"));
            double scale=UpperScale(left,row)/GoverningUpperScale(left);
            return new TransferMember {Name=original.Name+" · T"+(row+1),LoadCase=original.LoadCase,
                StartMm=original.StartMm,LengthMm=original.LengthMm,SupportsMm=original.SupportsMm,H20Count=original.H20Count,
                Points=original.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*scale}).ToArray(),
                Uniform=original.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*scale}).ToArray(),
                Analysis=original.Analysis.Scale(scale)};
        }
        // Unit-width transverse sources for the read-only equivalent-UDL view.
        // These never replace the actual row/stage member design analyses.
        public TransferMember[] UnitLowerCases=new TransferMember[0];
        public string[][] UnitPointLabels=new string[0][];
        public double SlabSupportStartMm,SlabSupportEndMm;
        // Per construction stage, near/far external column-side support assignments.
        public ColumnEndStripLoad[][] ColumnSupportLoads=new ColumnEndStripLoad[0][];
        public double ColumnSupportLoadKn(int stage)=>ColumnSupportLoads.Length==0 ? 0 : ColumnSupportLoads[stage].Sum(x=>x.TotalKn);
        public string ColumnSupportNotice=>ColumnSupportLoads.Length==0 ? "" :
            "External column-side end supports are assumed for uncovered main-primary and slab-support strips; their capacity requires separate verification.\n"
            +string.Join("\n",ColumnSupportLoads.SelectMany(x=>x).Select(x=>x.Description));
        public double UpperScale(bool left,int row)
        {double scale=(left ? LeftUpperScales : RightUpperScales)?[row]??TributaryWidthsMm[row]/1000;return scale==0 ? 0 : Math.Max(scale,SlabSpacing(left)/1000);}
        public double EnteredUpperSpacingMm, LeftSlabSpacingMm, RightSlabSpacingMm;
        public bool HasIndividualSpacings;
        public double SlabSpacing(bool left)=>(left ? LeftSlabSpacingMm : RightSlabSpacingMm)>0 ? (left ? LeftSlabSpacingMm : RightSlabSpacingMm) : EnteredUpperSpacingMm;
        public bool CrossFormwork;
        public double LowerStockStartMm, LowerStockLengthMm, LeftFootMm, RightFootMm;
        public double LeftUpperStockMm, RightUpperStockMm, LeftUpperEdgeMm, RightUpperEdgeMm;
        public bool LeftUpperCustom, RightUpperCustom;
        public bool LowerCustom;
        public double LeftFootReactionKn, RightFootReactionKn, SlabMovementMm, CoveredSlabAreaM2, SlabLoadKn, ShutterLoadKn, BeamLoadKn;
        public TransferMember[][] PrimaryCases;
        public TransferMember[][] LowerCases;
        public TransferMember[] UpperMembers;
        public string[] CaseNames;
        public int UpperFootCount => 2*((LeftUpperStockMm>0 ? LeftUpperScales?.Count(s=>s>0)??RowsMm.Length : 0)+(RightUpperStockMm>0 ? RightUpperScales?.Count(s=>s>0)??RowsMm.Length : 0));
        public int AdditionalH20Count => PhysicalRowCount+UpperFootCount/2;
        public double AdditionalTimberMm => PhysicalRowCount*LowerStockLengthMm+(LeftUpperScales?.Count(s=>s>0)??RowsMm.Length)*LeftUpperStockMm+(RightUpperScales?.Count(s=>s>0)??RowsMm.Length)*RightUpperStockMm;
        public string Summary => (ColumnSupportLoads.Length==0 ? "" : "External column-side end supports require separate capacity verification.\n")+(HasIndividualSpacings ? $"Nominal beam spacing {EnteredUpperSpacingMm:0} mm · slab L/R {(LeftUpperStockMm>0 ? SlabSpacing(true).ToString("0") : "—")}/{(RightUpperStockMm>0 ? SlabSpacing(false).ToString("0") : "—")} mm" : $"Nominal secondary spacing {EnteredUpperSpacingMm:0} mm")+$" · {PhysicalRowCount} bearer rows\nPeak upper foot L/R {LeftFootReactionKn:0.00}/{RightFootReactionKn:0.00} kN\nEstimated slab soffit movement {SlabMovementMm:0.00} mm";
        public string Schedule => (ColumnSupportNotice=="" ? "" : ColumnSupportNotice+"\n")+(SettingOutRowsMm==null ? "" : $"Local H20 setting-out: {AddedSecondaryCount} extra H20(s); design spacing remains {EnteredUpperSpacingMm:0.###} mm and primary UDL is unchanged.\n")+(HasCustomPlywoodJoints ? "Local plywood cuts/joints accommodate the relocated receiving H20s.\n" : "")+(BeamOnlyRowCount>0 ? $"{BeamOnlyRowCount} local beam-only secondary rows; no additional slab feet or slab tributary.\n" : "")+(UpperMembers.Length>0 ? UpperTransferPlan.SiteArrangement+(HasIndividualSpacings ? " Maximum slab row spacing: "+string.Join("; ",new[] {LeftSlabSpacingMm>0 ? $"left {LeftSlabSpacingMm:0.###} mm" : null,RightSlabSpacingMm>0 ? $"right {RightSlabSpacingMm:0.###} mm" : null}.Where(s=>s!=null))+".\n" : $" Maximum row spacing {EnteredUpperSpacingMm:0.###} mm.\n") : "")+(CrossFormwork ? "Cross-beam footprints exclude slab H20 rows and their upper feet. Remaining rows use their assigned tributaries; main beam concrete retains the full-spacing design.\n" : "")+(SettingOutRowsMm==null ? string.Join("\n",RowsMm.Select((x,i)=>$"T{i+1}: x={x:0.###} mm · actual local beam tributary {TributaryWidthsMm[i]:0.###} mm · effective beam design width {Math.Max(TributaryWidthsMm[i],EnteredUpperSpacingMm):0.###} mm · 1 × {LowerStockLengthMm:0} mm")) : string.Join("\n",SettingOutRowsMm.Select((x,i)=>$"T{i+1}: x={x:0.###} mm · 1 × {LowerStockLengthMm:0} mm")))
            + "\n"+string.Join("\n",Enumerable.Range(0,RowsMm.Length).SelectMany(row=>new[] {true,false}.Where(left=>(left ? LeftUpperStockMm : RightUpperStockMm)>0 && UpperScale(left,row)>0).Select(left=>$"T{row+1} {(left ? "left" : "right")}: "+UpperSpacingDescription(left,row))))
            + $"\nLower transverse stock{(LowerCustom ? " (custom, 25 mm increments)" : "")}: {LowerStockStartMm:0.###} to {LowerStockStartMm+LowerStockLengthMm:0.###} mm from beam centre"
            + (LeftUpperStockMm > 0 ? $"\nLeft slab H20: 1 × {LeftUpperStockMm:0} mm{(LeftUpperCustom ? " custom" : "")} per row · edge {LeftUpperEdgeMm:0} mm · equal foot setback {LeftFootMm:0.###} mm at both faces" : "")
            + (RightUpperStockMm > 0 ? $"\nRight slab H20: 1 × {RightUpperStockMm:0} mm{(RightUpperCustom ? " custom" : "")} per row · edge {RightUpperEdgeMm:0} mm · equal foot setback {RightFootMm:0.###} mm at both faces" : "")
            + $"\nSlab H20 covered area: {CoveredSlabAreaM2:0.00} m² · physical slab reference including external end supports: {SlabLoadKn:0.00} kN (including shutter {ShutterLoadKn:0.00} kN at 100 mm outward from each beam face)\nLoad cases: {string.Join("; ", CaseNames)}\n"
            + string.Join("\n", PrimaryCases.SelectMany(x => x).Concat(LowerCases.SelectMany(x => x)).Concat(UpperMembers).Select(m => $"{m.Name} · {m.LoadCase}: M {m.Analysis.MomentKnM:0.00}/{m.MomentLimitKnM:0.##} kNm · V {m.Analysis.ShearKn:0.00}/{m.ShearLimitKn:0.##} kN · d {m.Analysis.DeflectionMm:0.00} mm · minimum signed reaction {m.MinimumReactionKn:0.######} kN · utilization {m.Utilization*100:0.0}%"+(m.SubsetDeflectionBound>0 ? $" · all-subset bounds M {m.SubsetMomentBoundKnM:0.###} kNm, V {m.SubsetShearBoundKn:0.###} kN, d-utilization {m.SubsetDeflectionBound*100:0.0}%" : "")));
        public const string Assumptions = "Slab load transfers through single slab H20s and vertical upper feet, single lower secondary H20s and selected longitudinal primaries to tower legs. The upper assembly has no D3 geometry. Production checks the fully loaded beam, connected slabs and applicable crossings together. Partial construction stages are outside this result. The slab area over upper H20 stock loads that stock; each slab-edge strip (minimum 200 mm, maximum the slab plywood spacing allowance) loads its own side shutter, acting 100 mm outward from the adjacent beam face. These edge loads are additional to the upper-foot loads. Slab above the concrete beam width is not counted again. Production main primaries use concrete tributary zones: Beam + Slab outside crossings and assigned Beam + Cross Beam across each full physical crossing width. Dedicated tower loads and slab ledgers are allocated once; historical replay retains the former bearing-derived model. Uncovered longitudinal end-strip loads are assigned to external column-side supports, whose capacity requires separate verification. Slab-secondary longitudinal strips outside shared coverage are assigned to assumed external supports; in-coverage tributaries, genuine crossing loads and transverse cantilevers remain checked. Local beam-only rows add no slab feet or slab tributary. Dedicated crossing formwork includes the full concrete span, including end strips, using a UDL scaled over centred primary stock coverage; secondary end-zone sheathing influences remain explicitly calculated. Each slab H20 is one length with no upper joints. Single-beam transfer mode retains its legacy upper-length fallback: if no catalogue length fits the 200 mm minimum and slab plywood spacing maximum, a custom upper length is rounded down to a multiple of 25 mm, keeping each edge closest to 200 mm. Network mode respects the custom-length setting for upper, lower and primary H20s. If full catalogue network transfer fails, separately supported slab bays may be excluded from transfer; the search minimizes their number before ranking calculated supports/timber. Their slab and shutter loads are excluded from transfer and their formwork design and quantities are excluded. Modern crossing beams are still designed using their selected shared or dedicated support arrangement; only historical slab-row crossing loads are excluded with an omitted slab bay. WS10 primary lengths always use uncut catalogue stock. H20 primary and lower secondary lengths use catalogue stock unless custom H20 lengths are enabled; additional lengths are multiples of 25 mm. The custom primary cover search adds bounded balanced runs and small joint shifts. The original stock-only solution is retained when it ranks better. Feet have at least 250 mm face-to-centre clearance and 150 mm inset from the upper stock end. The network uses 2–15 actual parallel beams, each with its own width/depth, and one slab bay between neighbours. Slab H20 stock is centred and its feet have equal setbacks from both concrete faces, giving equal overhangs; this arrangement repeats at every row in a bay. Uniform slab loading gives equal reactions at the two slab feet. Actual receiving-secondary, primary and tower-leg reactions can differ and are checked individually for the applicable loading case. Each lower beam is independently checked against the entered permissible leg load, and negative bearing reactions invalidate a candidate. Shared slab coverage and support rows align upper supports across all beams. Each beam can use longer primary coverage; its end rows carry the actual extra beam-concrete tributary load. In explicit Legacy slab rows mode, optional perpendicular beams span one clear slab bay and transfer through the existing slab H20s and shutter-edge strips during that bay loading stage. The existing slab row load is retained once; the additional crossing-beam concrete and surcharge are assigned only to H20 rows physically beneath that footprint, using midpoint tributaries between those rows. The added area load is the crossing beam total-depth load minus the existing slab-thickness load; concrete, formwork and working allowances are not counted twice. Crossing beam footprints must lie within longitudinal H20 coverage and cannot overlap within a bay. Legacy bearing and level-change details are outside the legacy load-transfer model. Modern cross-beam formwork has separately reported elevation, bearing and support checks. Two-support upper/lower bearers are statically determinate; different lower-foot movements add an affine displacement to the upper beam. Single-beam mode retains the mirrored opposite-beam assumption; network mode uses both actual beam responses. Slab and lower secondary H20 limits are M=5 kNm, V=11 kN and EI=449.8756 kNm². Double-H20 longitudinal primary limits are M=10 kNm, V=22 kN and EI=899.7512 kNm², assuming equal load sharing. The selected primary material basis defines WS10 checks and catalogue restrictions. Longitudinal primaries use distributed design loads derived from the per-metre beam and slab reactions, independent of exact secondary row positions. Beam-concrete UDL covers primary stock; production network slab UDL uses the selected slab allocation over normal zones, with crossing ledger loads assigned once. Historical replay uses the intersection of primary and shared slab support coverage. Physical loads outside either supported region are assigned once to unverified external column-side supports. Crossing increments retain their actual footprints. Slab and lower-secondary design reactions use at least the full entered spacing, including end rows. Covered-area totals remain physical reference quantities; design loads can exceed them. Additional crossing-beam load remains distributed over its physical footprint, rather than averaged over the whole beam. Primary moment, shear, deflection and tower-leg checks, diagrams and reported utilization all use this same UDL model. Secondary maximum spacing and bearing details still require site control. Relative H20 deflection uses each support span/overhang; total slab movement is reported without an assumed project tolerance. Production search ranks legs, tower grouping, brace preference, symmetry and then material economy, counting shared upper timber once. Historical replay retains its economy-led ranking. Search candidates are bounded; a global optimum is not proven. Dedicated crossing junctions use receiving H20s directly at matching soffits; a deeper parallel beam carries each crossing-end strip through its existing side shutter at the midpoint of its selected width (normally 200 mm / 100 mm offset; locally 175 mm / 87.5 mm offset only after the standard detail fails). These loads are included in receiving secondary, primary and individual tower-leg checks. Crossing primary stock ends retain their selected local 200 mm or exceptional 175 mm allowances from the parallel concrete faces; exceptions are identified in the schedule and plan. Plans show towers and primary stock lengths; a toggle shows single secondaries, which are also shown in section with stock length and spacing. Sheathing, shutter/connection strength, bearing, upper-support capacity/stability, lower tower stability and foundations require separate design. Doka H20 instructions restrict heavy point loads with wide spans; numeric member checks alone do not approve foot or shutter bearing details.";
    }
    /// <summary>Transverse force transfer with continuous distributed primary design loads.</summary>
    public static class TransferredSlabOptimizer
    {
        // A raw caller-supplied vector is never inferred from geometry. Native
        // slices are reserved for immutable generated positions and retain the
        // original global-coordinate subtraction, including its rounding.
        internal readonly struct SupportVector
        {
            private readonly double[] values;
            private readonly double origin;
            private readonly int firstPlusOne,count;
            internal SupportVector(double[] values){this.values=values;origin=0;firstPlusOne=0;count=0;}
            private SupportVector(double[] values,int first,int count,double origin){this.values=values;this.origin=origin;firstPlusOne=first+1;this.count=count;}
            internal static SupportVector NativeSlice(double[] positions,int first,int count,double origin)
            {
                if(positions==null)throw new ArgumentNullException(nameof(positions));
                if(first<0 || count<0 || first>positions.Length-count)throw new ArgumentOutOfRangeException(nameof(first));
                return new SupportVector(positions,first,count,origin);
            }
            internal bool IsNull=>values==null;
            internal bool IsNative=>firstPlusOne!=0;
            internal int Length=>firstPlusOne==0 ? values.Length : count;
            internal double this[int index]
            {
                get
                {
                    if(firstPlusOne==0)return values[index];
                    if((uint)index>=(uint)count)throw new IndexOutOfRangeException();
                    return values[firstPlusOne-1+index]-origin;
                }
            }
            internal bool SameStorage(SupportVector other)
                =>ReferenceEquals(values,other.values) && firstPlusOne==other.firstPlusOne
                    && (firstPlusOne==0 || count==other.count && BitConverter.DoubleToInt64Bits(origin)==BitConverter.DoubleToInt64Bits(other.origin));
            // Legacy span screening used LINQ First/Last, whose malformed-input
            // exceptions differ from array indexing. Keep those error paths.
            internal double First()=>IsNull ? Enumerable.First((double[])null) : Length==0 ? Enumerable.First(Array.Empty<double>()) : this[0];
            internal double Last()=>IsNull ? Enumerable.Last((double[])null) : Length==0 ? Enumerable.Last(Array.Empty<double>()) : this[Length-1];
            internal double[] Copy()
            {
                var result=new double[Length];for(int i=0;i<result.Length;i++)result[i]=this[i];return result;
            }
        }
        internal sealed class Geometry
        {
            public TowerStockPiece[] Stock; public double[] Positions; public bool[] Joined;
            private double[][] memberSupports;
            private bool nativeSupports;
            // Compatibility inspectors/full response consumers receive the same
            // fresh per-geometry arrays as before. Early checks use Supports().
            public double[][] MemberSupports
            {
                get=>MaterializeSupports(null);
                set{Volatile.Write(ref nativeSupports,false);Volatile.Write(ref memberSupports,value);}
            }
            internal bool HasNativeSupportSlices=>Volatile.Read(ref nativeSupports);
            internal bool HasMaterializedSupports=>Volatile.Read(ref memberSupports)!=null;
            internal SupportVector Supports(int run)
            {
                var saved=Volatile.Read(ref memberSupports);if(saved!=null)return new SupportVector(saved[run]);
                if(Volatile.Read(ref nativeSupports))
                {
                    var stock=Stock[run];return SupportVector.NativeSlice(Positions,stock.FirstSupport,stock.LastSupport-stock.FirstSupport+1,stock.StartMm);
                }
                return new SupportVector(saved[run]);
            }
            internal bool HasSupportMetadata=>Volatile.Read(ref nativeSupports) || Volatile.Read(ref memberSupports)!=null;
            internal int SupportRuns
            {
                get{var saved=Volatile.Read(ref memberSupports);return saved!=null ? saved.Length : Volatile.Read(ref nativeSupports) ? Stock.Length : saved.Length;}
            }
            internal double[][] MaterializeSupports(BeamSearchTrace trace)
            {
                trace?.CheckCancellation();var saved=Volatile.Read(ref memberSupports);
                if(saved!=null || !Volatile.Read(ref nativeSupports))return saved;
                var created=new double[Stock.Length][];
                for(int run=0;run<created.Length;run++)
                {trace?.CheckCancellation();var stock=Stock[run];created[run]=SupportVector.NativeSlice(Positions,stock.FirstSupport,stock.LastSupport-stock.FirstSupport+1,stock.StartMm).Copy();}
                trace?.CheckCancellation();return Interlocked.CompareExchange(ref memberSupports,created,null)??created;
            }
            // Called only by the native generator factories after their arrays
            // and pieces are complete. Those sources are immutable afterward;
            // site translations construct a new Geometry before registration.
            internal void PrepareGeneratedSupports(BeamSearchTrace trace)
            {
                trace?.CheckCancellation();if(HasSupportMetadata)return;
                bool known=Stock!=null && Stock.Length>0 && Positions!=null && Joined!=null && Joined.Length==Positions.Length-1;
                if(known)foreach(var stock in Stock)
                {
                    trace?.CheckCancellation();
                    if(stock==null || stock.FirstSupport<0 || stock.LastSupport<=stock.FirstSupport || stock.LastSupport>=Positions.Length
                        || !TowerBeamAnalysis.Finite(stock.StartMm) || !TowerBeamAnalysis.Finite(stock.StockLengthMm) || stock.StockLengthMm<=0) {known=false;break;}
                    for(int i=stock.FirstSupport;i<=stock.LastSupport;i++)
                        if(!TowerBeamAnalysis.Finite(Positions[i]) || !TowerBeamAnalysis.Finite(Positions[i]-stock.StartMm)){known=false;break;}
                    if(!known)break;
                }
                trace?.CheckCancellation();
                if(known){Volatile.Write(ref nativeSupports,true);return;}
                // Unsupported sources keep the exact former eager expression,
                // including its LINQ slicing/error behavior.
                MemberSupports=Stock.Select(s=>Positions.Skip(s.FirstSupport).Take(s.LastSupport-s.FirstSupport+1).Select(x=>x-s.StartMm).ToArray()).ToArray();
            }
            internal static Geometry Generated(TowerStockPiece[] stock,double[] positions,bool[] joined,BeamSearchTrace trace)
            {
                var result=new Geometry {Stock=stock,Positions=positions,Joined=joined};result.PrepareGeneratedSupports(trace);return result;
            }
            internal SpanGeometryShape SpanShape;
            internal GeometryRank[] Rankings;
            internal int[] NecessarySupportHashes;
            // A location to recheck, never a load, capacity or cached verdict.
            internal int NecessaryFailureHint;
        }
        // Pure integer hashes for the retained immutable member-support arrays.
        // No loads, capacities or acceptance decisions are stored on geometry.
        internal static void NecessarySupportHash(Geometry geometry,int run,BeamSearchTrace trace,out int polynomial,out int power)
        {
            var saved=Volatile.Read(ref geometry.NecessarySupportHashes);
            if(saved==null)
            {
                trace?.CheckCancellation();var created=new int[geometry.SupportRuns*2];
                unchecked
                {
                    for(int i=0;i<geometry.SupportRuns;i++)
                    {
                        var supports=geometry.Supports(i);int h=0,factor=1;for(int j=0;j<supports.Length;j++){h=h*31+supports[j].GetHashCode();factor*=31;}
                        created[2*i]=h;created[2*i+1]=factor;
                    }
                }
                trace?.CheckCancellation();Volatile.Write(ref geometry.NecessarySupportHashes,created);saved=created;
            }
            polynomial=saved[2*run];power=saved[2*run+1];
        }
        internal sealed class GeometryRank
        {
            internal FinalGeometryRank Final;
            internal readonly double Width,Centre,Brace,Cantilever,Symmetry;
            internal readonly bool Rotated,Engineer;
            internal readonly int Joined,Narrow;
            internal GeometryRank(Geometry geometry,double width,bool rotated,double centre,bool engineer)
            {
                Width=width;Rotated=rotated;Centre=centre;Engineer=engineer;
                Joined=geometry.Joined.Count(j=>j);
                Brace=EngineerTowerSearch.BracePenalty(geometry.Positions,geometry.Joined,width,rotated);
                Narrow=TowerOptimizer.Standalone600Count(geometry.Positions,geometry.Joined,width,rotated);
                Cantilever=geometry.Stock.Sum(p=>Math.Abs((geometry.Positions[p.FirstSupport]-p.StartMm)-(p.EndMm-geometry.Positions[p.LastSupport])));
                Symmetry=engineer ? TowerOptimizer.SymmetryError(geometry.Positions,geometry.Stock.Select(p=>p.StockLengthMm).ToArray(),centre) : 0;
            }
            internal bool Matches(double width,bool rotated,double centre,bool engineer)=>Width==width && Rotated==rotated && Centre==centre && Engineer==engineer;
        }
        internal sealed class FinalGeometryRank
        {
            internal readonly double Edge,Centre,Brace,Cantilever,PrimarySymmetry,BraceLocation,StockLength;
            internal readonly int Stations,Joined,Narrow;
            internal FinalGeometryRank(Geometry geometry,GeometryRank rank,double edge,double centre)
            {
                Edge=edge;Centre=centre;Stations=geometry.Positions.Length;Joined=geometry.Joined.Count(x=>x);
                var positions=geometry.Positions.Select(x=>x+edge).ToArray();
                Narrow=TowerOptimizer.Standalone600Count(positions,geometry.Joined,rank.Width,rank.Rotated);
                Brace=EngineerTowerSearch.BracePenalty(positions,geometry.Joined,rank.Width,rank.Rotated);
                Cantilever=geometry.Stock.Sum(p=>
                {
                    double start=p.StartMm+edge;
                    return Math.Abs((positions[p.FirstSupport]-start)-(start+p.StockLengthMm-positions[p.LastSupport]));
                });
                PrimarySymmetry=TowerOptimizer.SymmetryError(positions,geometry.Stock.Select(p=>p.StockLengthMm).ToArray(),centre);
                BraceLocation=EngineerTowerSearch.BraceLocationPenalty(positions,geometry.Joined,rank.Width,rank.Rotated);
                StockLength=geometry.Stock.Sum(p=>p.StockLengthMm);
            }
        }
        internal static FinalGeometryRank FinalRankGeometry(Geometry geometry,GeometryRank rank,double edge,double centre,bool reuse)
        {
            var saved=reuse ? Volatile.Read(ref rank.Final) : null;
            if(saved!=null && saved.Edge==edge && saved.Centre==centre)return saved;
            var created=new FinalGeometryRank(geometry,rank,edge,centre);
            if(reuse)Volatile.Write(ref rank.Final,created);return created;
        }
        internal sealed class KnownLayoutRank
        {
            internal readonly TowerLayout Layout;
            internal readonly int Stations,Joined,Narrow;
            internal readonly double Brace,Cantilever,Symmetry,BraceLocation,Cost;
            internal KnownLayoutRank(TowerLayout layout)
            {
                Layout=layout;Stations=layout.PositionsMm.Length;Joined=layout.BayCount;Narrow=layout.Standalone600Count;
                Brace=layout.StandaloneBracePenalty;Cantilever=layout.CantileverImbalanceMm;Symmetry=layout.SymmetryErrorMm;
                BraceLocation=layout.BraceLocationPenalty;Cost=Economy(layout);
            }
        }
        private static KnownLayoutRank KnownWinner(Dictionary<double,KnownLayoutRank> saved,double width,TowerLayout layout)
        {
            if(!saved.TryGetValue(width,out var rank) || !ReferenceEquals(rank.Layout,layout))saved[width]=rank=new KnownLayoutRank(layout);
            return rank;
        }
        // All operations, including absolute coordinates and parenthesization,
        // match the final layout getters. A zero result retains cost/utilization
        // ties for the ordinary complete member and receiver checks.
        internal static int CompareKnownRanks(FinalGeometryRank candidate,double secondary,KnownLayoutRank best)
        {
            int order=candidate.Stations.CompareTo(best.Stations);if(order!=0)return order;
            order=EngineerTowerSearch.Compare(candidate.Narrow,candidate.Joined,candidate.Brace,best.Narrow,best.Joined,best.Brace);if(order!=0)return order;
            if(Math.Abs(candidate.Cantilever-best.Cantilever)>1e-7)return candidate.Cantilever.CompareTo(best.Cantilever);
            double symmetry=candidate.PrimarySymmetry+(candidate.Cantilever+secondary);
            if(Math.Abs(symmetry-best.Symmetry)>1e-7)return symmetry.CompareTo(best.Symmetry);
            if(Math.Abs(candidate.BraceLocation-best.BraceLocation)>1e-7)return candidate.BraceLocation.CompareTo(best.BraceLocation);
            return 0;
        }
        internal static GeometryRank RankGeometry(Geometry geometry,double width,bool rotated,double centre,bool engineer,bool reuse)
        {
            var saved=reuse ? Volatile.Read(ref geometry.Rankings) : null;
            if(saved!=null)foreach(var entry in saved)if(entry.Matches(width,rotated,centre,engineer))return entry;
            var created=new GeometryRank(geometry,width,rotated,centre,engineer);
            if(!reuse)return created;
            while(true)
            {
                saved=Volatile.Read(ref geometry.Rankings);
                if(saved!=null)foreach(var entry in saved)if(entry.Matches(width,rotated,centre,engineer))return entry;
                int retained=Math.Min(15,saved?.Length??0);
                var next=new GeometryRank[retained+1];
                if(retained>0)Array.Copy(saved,saved.Length-retained,next,0,retained);
                next[retained]=created;
                if(ReferenceEquals(Interlocked.CompareExchange(ref geometry.Rankings,next,saved),saved))return created;
            }
        }
        // Exact immutable footprint values. Geometry dimensions are retained
        // separately; loads, material and capacities remain live on every use.
        internal sealed class SpanGeometryContext
        {
            internal readonly double Edge;
            private readonly double[] boundaries;
            internal readonly int Hash;
            internal IList<double> Boundaries=>Array.AsReadOnly(boundaries);
            internal SpanGeometryContext(double edge,BeamDistributedLoad[] bands)
            {
                Edge=edge;boundaries=bands.SelectMany(b=>new[] {b.StartMm,b.EndMm}).ToArray();
                unchecked{int hash=edge.GetHashCode()*31+boundaries.Length;foreach(double value in boundaries)hash=hash*31+value.GetHashCode();Hash=hash;}
            }
            internal bool Same(SpanGeometryContext other)
            {
                if(ReferenceEquals(this,other))return true;
                if(other==null || Hash!=other.Hash || Edge!=other.Edge || boundaries.Length!=other.boundaries.Length)return false;
                for(int i=0;i<boundaries.Length;i++)if(boundaries[i]!=other.boundaries[i])return false;
                return true;
            }
            internal SpanGeometryShape Build(Geometry geometry,BeamSearchTrace trace)
            {
                var tails=new double[geometry.Stock.Length];var clearSpans=new double[geometry.Stock.Length];double maximumTail=0,maximumClearSpan=0;
                for(int run=0;run<geometry.Stock.Length;run++)
                {
                    trace?.CheckCancellation();
                    var stock=geometry.Stock[run];var supports=geometry.Supports(run);double origin=stock.StartMm+Edge;
                    for(int tail=0;tail<2;tail++)
                    {
                        double a=tail==0 ? 0 : supports[supports.Length-1],b=tail==0 ? supports[0] : stock.StockLengthMm;
                        bool unknown=false;for(int i=0;i<boundaries.Length;i+=2)if(boundaries[i+1]>origin+a && boundaries[i]<origin+b){unknown=true;break;}
                        if(!unknown)tails[run]=Math.Max(tails[run],(b-a)/1000);
                    }
                    for(int span=0;span<=supports.Length;span++)
                    {
                        double a=origin+(span==0 ? 0 : supports[span-1]),b=origin+(span==supports.Length ? stock.StockLengthMm : supports[span]);
                        for(int i=0;i<boundaries.Length;i+=2)
                        {
                            if(boundaries[i+1]<=a)continue;if(boundaries[i]>=b)break;
                            clearSpans[run]=Math.Max(clearSpans[run],(Math.Min(b,boundaries[i])-a)/1000);
                            a=Math.Max(a,boundaries[i+1]);if(a>=b)break;
                        }
                        clearSpans[run]=Math.Max(clearSpans[run],(b-a)/1000);
                    }
                    maximumTail=Math.Max(maximumTail,tails[run]);maximumClearSpan=Math.Max(maximumClearSpan,clearSpans[run]);
                }
                trace?.CheckCancellation();return new SpanGeometryShape(this,tails,clearSpans,maximumTail,maximumClearSpan);
            }
        }
        internal sealed class SpanGeometryShape
        {
            internal readonly SpanGeometryContext Context;
            // Owned complete arrays; retained search consumers only read them.
            internal readonly double[] Tails,ClearSpans;
            internal readonly double MaximumTail,MaximumClearSpan;
            internal SpanGeometryShape(SpanGeometryContext context,double[] tails,double[] clearSpans,double maximumTail,double maximumClearSpan)
            {Context=context;Tails=tails;ClearSpans=clearSpans;MaximumTail=maximumTail;MaximumClearSpan=maximumClearSpan;}
        }
        internal readonly struct SpanShapeKey : IEquatable<SpanShapeKey>
        {
            private readonly Geometry geometry;private readonly SpanGeometryContext context;private readonly int hash;
            internal SpanShapeKey(Geometry geometry,SpanGeometryContext context)
            {this.geometry=geometry;this.context=context;unchecked{hash=RuntimeHelpers.GetHashCode(geometry)*31+context.Hash;}}
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>other is SpanShapeKey && Equals((SpanShapeKey)other);
            public bool Equals(SpanShapeKey other)=>ReferenceEquals(geometry,other.geometry) && (context==null ? other.context==null : context.Same(other.context));
        }
        internal sealed class SpanShapeHistory
        {
            private sealed class Shard
            {
                internal readonly ResponseCache<SpanShapeKey,SpanGeometryShape> Entries;
                internal long Hits,Builds;
                internal Shard(int capacity){Entries=new ResponseCache<SpanShapeKey,SpanGeometryShape>(capacity);}
            }
            // Workers share only immutable shapes. Independent bounded LRUs keep
            // unrelated exact-key hits from serializing on one owner-wide lock.
            // Their capacities sum to the same total retention limit.
            private readonly Shard[] shards;
            internal SpanShapeHistory(int capacity=16384)
            {
                if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));
                int count=Math.Min(16,capacity);shards=new Shard[count];
                for(int i=0;i<count;i++)shards[i]=new Shard(capacity/count+(i<capacity%count ? 1 : 0));
            }
            private Shard For(SpanShapeKey key)
            {
                // Spread integer-mm and adjacent-double contexts across shards;
                // the original exact key still decides every cache hit.
                unchecked
                {
                    uint hash=(uint)key.GetHashCode();hash^=hash>>16;hash*=0x7feb352d;
                    hash^=hash>>15;hash*=0x846ca68b;hash^=hash>>16;
                    return shards[hash%(uint)shards.Length];
                }
            }
            internal int Count {get{int count=0;foreach(var shard in shards)lock(shard.Entries)count+=shard.Entries.Count;return count;}}
            internal long Hits {get{long hits=0;foreach(var shard in shards)lock(shard.Entries)hits+=shard.Hits;return hits;}}
            internal long Builds {get{long builds=0;foreach(var shard in shards)lock(shard.Entries)builds+=shard.Builds;return builds;}}
            internal bool TryGet(SpanShapeKey key,BeamSearchTrace trace,out SpanGeometryShape shape)
            {
                var shard=For(key);
                lock(shard.Entries){trace?.CheckCancellation();if(!shard.Entries.TryGetValue(key,out shape))return false;shard.Hits++;return true;}
            }
            internal SpanGeometryShape Publish(SpanShapeKey key,SpanGeometryShape shape,BeamSearchTrace trace)
            {
                var shard=For(key);
                lock(shard.Entries)
                {
                    trace?.CheckCancellation();shard.Builds++;
                    if(shard.Entries.TryGetValue(key,out var saved)){shard.Hits++;return saved;}
                    shard.Entries.Store(key,shape);return shape;
                }
            }
        }
        internal static SpanGeometryShape PrepareSpanShape(Geometry geometry,SpanGeometryContext context,BeamSearchTrace trace=null,GeometryCache owner=null)
        {
            trace?.CheckCancellation();var found=Volatile.Read(ref geometry.SpanShape);
            if(found!=null && context.Same(found.Context))
            {trace?.Increment(BeamSearchCounter.SpanGeometryHits);return found;}
            var history=owner?.SpanShapes;var key=history==null ? default(SpanShapeKey) : new SpanShapeKey(geometry,context);
            if(history!=null && history.TryGet(key,trace,out found))
            {trace?.Increment(BeamSearchCounter.SpanGeometryHits);Volatile.Write(ref geometry.SpanShape,found);return found;}
            // No shared lock encloses the supports-by-footprints traversal. A
            // fault/cancellation leaves both the history and last slot untouched.
            var shape=context.Build(geometry,trace);trace?.CheckCancellation();
            if(history!=null)shape=history.Publish(key,shape,trace);
            trace?.Increment(BeamSearchCounter.SpanGeometryBuilds);trace?.CheckCancellation();
            Volatile.Write(ref geometry.SpanShape,shape);return shape;
        }
        internal static bool PreparedSpanShapeFits(TowerInput input,Geometry geometry,SpanGeometryContext context,double largest)
        {
            var shape=PrepareSpanShape(geometry,context,input.Trace,input.ReuseSearchWork ? input.TransferredSlab?.SharedGeometryCache??input.NetworkGeometryWork : null);
            if(!input.Primary.IsWS10)
            {
                // H20 has no stock-dependent self-weight. Every run therefore
                // uses the same nonnegative line load and the same limits;
                // the largest dimensions retain each original scalar check.
                double q=largest,tail=shape.MaximumTail,span=shape.MaximumClearSpan;
                return !(q*tail>input.Primary.ShearLimitKn+1e-7 || q*tail*tail/2>input.Primary.MomentLimitKnM+1e-7
                    || q*span>2*input.Primary.ShearLimitKn+1e-7 || q*span*span>16*input.Primary.MomentLimitKnM+1e-7);
            }
            for(int run=0;run<geometry.Stock.Length;run++)
            {
                double q=largest+input.Primary.SelfWeightKnM(geometry.Stock[run].StockLengthMm),tail=shape.Tails[run],span=shape.ClearSpans[run];
                if(q*tail>input.Primary.ShearLimitKn+1e-7 || q*tail*tail/2>input.Primary.MomentLimitKnM+1e-7
                    || q*span>2*input.Primary.ShearLimitKn+1e-7 || q*span*span>16*input.Primary.MomentLimitKnM+1e-7)return false;
            }
            return true;
        }
        // Exact geometry identity; avoids formatting every support coordinate
        // into a temporary string while retaining first-occurrence ordering.
        internal sealed class GeometryKey : IEquatable<GeometryKey>
        {
            private readonly double[] positions;private readonly bool[] joined;private readonly int hash;
            internal GeometryKey(double[] positions,bool[] joined)
            {
                this.positions=positions;this.joined=joined;
                unchecked{int value=17;foreach(double p in positions)value=value*31+p.GetHashCode();foreach(bool j in joined)value=value*31+(j ? 1 : 0);hash=value;}
            }
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>Equals(other as GeometryKey);
            public bool Equals(GeometryKey other)
            {
                if(other==null || hash!=other.hash || positions.Length!=other.positions.Length || joined.Length!=other.joined.Length)return false;
                for(int i=0;i<positions.Length;i++)if(positions[i]!=other.positions[i])return false;
                for(int i=0;i<joined.Length;i++)if(joined[i]!=other.joined[i])return false;
                return true;
            }
        }
        // Preserve Tuple<double,double> equality and hashing without boxing either
        // coordinate while the hot receiving-header dictionaries are queried.
        internal readonly struct ReceivingPairKey : IEquatable<ReceivingPairKey>
        {
            private readonly double first,second;
            internal ReceivingPairKey(double first,double second){this.first=first;this.second=second;}
            public bool Equals(ReceivingPairKey other)=>first.Equals(other.first) && second.Equals(other.second);
            public override bool Equals(object other)=>other is ReceivingPairKey key && Equals(key);
            public override int GetHashCode(){unchecked{int hash=first.GetHashCode();return ((hash<<5)+hash)^second.GetHashCode();}}
        }
        private readonly struct CountRangeKey : IEquatable<CountRangeKey>
        {
            private readonly Geometry[] parent;
            private readonly int minimum,maximum;
            internal CountRangeKey(Geometry[] parent,int minimum,int maximum){this.parent=parent;this.minimum=minimum;this.maximum=maximum;}
            public bool Equals(CountRangeKey other)=>ReferenceEquals(parent,other.parent) && minimum==other.minimum && maximum==other.maximum;
            public override bool Equals(object other)=>other is CountRangeKey key && Equals(key);
            public override int GetHashCode(){unchecked{int hash=parent==null ? 0 : parent.GetHashCode();hash=((hash<<5)+hash)^minimum;return ((hash<<5)+hash)^maximum;}}
        }
        // Created for one pass only: stock, setback and exclusion settings are constant.
        internal sealed class GeometryCache
        {
            internal Geometry[] Fixed;
            internal readonly SpanShapeHistory SpanShapes=new SpanShapeHistory();
            internal readonly TransferNetworkOptimizer.ReceiverAggregateLoadBound.UniversalViewCache UniversalPrimaryViews=new TransferNetworkOptimizer.ReceiverAggregateLoadBound.UniversalViewCache();
            private readonly ConcurrentDictionary<string,Lazy<Geometry[]>> items = new ConcurrentDictionary<string,Lazy<Geometry[]>>();
            private sealed class EngineerEntry
            {
                internal int Limit;
                internal Geometry[] All=Array.Empty<Geometry>();
                internal readonly Dictionary<int,Geometry[]> Ranges=new Dictionary<int,Geometry[]>();
            }
            private readonly ConcurrentDictionary<string,EngineerEntry> engineer=new ConcurrentDictionary<string,EngineerEntry>();
            private readonly ResponseCache<string,Geometry[]> regularPrefixes=new ResponseCache<string,Geometry[]>(128);
            private readonly ResponseCache<CountRangeKey,Geometry[]> countRanges=new ResponseCache<CountRangeKey,Geometry[]>(128);
            internal Geometry[] GetCountRange(Geometry[] source,int minimum,int maximum,BeamSearchTrace trace,out bool hit)
            {
                trace?.CheckCancellation();
                // Arrays use reference identity: completed extensions publish a
                // new parent array and cannot reuse a view of an older universe.
                var key=new CountRangeKey(source,minimum,maximum);
                lock(countRanges)
                {
                    trace?.CheckCancellation();
                    if(countRanges.TryGetValue(key,out var saved)){hit=true;return saved;}
                }
                var selected=source.Where(g=>g.Positions.Length>=minimum && g.Positions.Length<=maximum).ToArray();
                if(selected.Length==source.Length)selected=source;
                trace?.CheckCancellation();
                lock(countRanges)
                {
                    trace?.CheckCancellation();
                    if(countRanges.TryGetValue(key,out var saved)){hit=true;return saved;}
                    countRanges.Store(key,selected);hit=false;return selected;
                }
            }
            internal Geometry[] GetRegularPrefix(string key,Func<Geometry[]> build,out bool hit)
            {
                lock(regularPrefixes)if(regularPrefixes.TryGetValue(key,out var saved)){hit=true;return saved;}
                var generated=build();
                // Independent prefixes can build concurrently. Publish only a
                // completed result; an interrupted builder leaves no evidence.
                lock(regularPrefixes)
                {
                    if(regularPrefixes.TryGetValue(key,out var saved)){hit=true;return saved;}
                    regularPrefixes.Store(key,generated);hit=false;return generated;
                }
            }
            internal Geometry[] GetEngineer(string key,int limit,Func<int,Geometry[]> build,out bool hit,out int reused)
            {
                if(Fixed!=null){hit=true;reused=Fixed.Length;return Fixed;}
                var entry=engineer.GetOrAdd(key,_=>new EngineerEntry());
                lock(entry)
                {
                    if(entry.Ranges.TryGetValue(limit,out var saved)){hit=true;reused=saved.Length;return saved;}
                    if(limit<=entry.Limit)
                    {
                        hit=true;var subset=entry.All.Where(g=>g.Positions.Length<=limit).ToArray();
                        entry.Ranges[limit]=subset;reused=subset.Length;return subset;
                    }
                    int previous=entry.Limit;
                    var added=build(previous).Where(g=>g.Positions.Length>previous && g.Positions.Length<=limit).ToArray();
                    reused=entry.All.Length;hit=false;
                    var all=entry.All.Concat(added).ToArray();
                    // Publish only a completed extension; cancellation leaves earlier ranges intact.
                    entry.All=all;entry.Limit=limit;entry.Ranges[limit]=all;return all;
                }
            }
            internal Geometry[] Get(string key,Func<Geometry[]> build,out bool hit)
            {
                if(Fixed!=null){hit=true;return Fixed;}
                if(items.TryGetValue(key,out var found)) {hit=true;return found.Value;}
                var created=new Lazy<Geometry[]>(build,LazyThreadSafetyMode.ExecutionAndPublication);
                found=items.GetOrAdd(key,created);hit=!ReferenceEquals(found,created);return found.Value;
            }
        }
        private sealed class Upper { public double Stock, Edge, Foot, OtherFoot, Bay, Load; public bool Custom; public LoadedBeamAnalysis Analysis; }
        private sealed class CrossOption
        {
            internal bool FinalSymmetryPrepared;
            internal double FinalSymmetryWidth,FinalSecondarySymmetry;
            internal readonly ResponseCache<PrimaryLoadKey,PrimaryLoadData> PrimaryLoads=new ResponseCache<PrimaryLoadKey,PrimaryLoadData>(512);
            public double Start, Stock; public Upper Left, Right; public LoadedBeamAnalysis[] Cases; public string[] Names;
            public TransferMember[] UnitMembers;public string[][] UnitPointLabels;
            public EngineerPrimaryPlan PositionedPlan;
            // The latest grid stays on the fast path. Ranked geometry traversal
            // alternates covered lengths, so retain a bounded set of complete
            // transverse checks and their exact resolved receiving callbacks.
            public string RowKey,TransverseKey;
            public bool TransversePassed;
            internal Func<Geometry,double,bool> PreparedReceiver;
            internal ResponseCache<string,TransversePreparation> TransversePreparations;
            internal bool SpanPrepared;
            internal double[] MinimumLineLoads;
            internal BeamDistributedLoad[] UnknownSpans;
            internal SpanGeometryContext SpanContext;
            internal ResponseCache<double,SpanGeometryContext> SpanContexts;
            public int[] DesignCases;
            public TransferMember[][] LowerByCase;
            public bool[] LowerCaseComputed;
            // Transverse response depends on the load case and three exact tributaries,
            // not the longitudinal station. Bound this cache within one receiving-beam solve.
            public readonly Dictionary<Tuple<int,double,double,double>,LoadedBeamAnalysis> RowAnalyses = new Dictionary<Tuple<int,double,double,double>,LoadedBeamAnalysis>();
        }
        private sealed class TransversePreparation
        {
            internal bool Passed;
            internal Func<Geometry,double,bool> Receiver;
        }
        private sealed class RowGrid
        {
            internal RowQuantities Quantities;
            public double[] Rows, Tributaries, LeftScales, RightScales, LeftPhysicalScales, RightPhysicalScales;
            internal double[] SettingOutRows;
            internal int[] SettingOutDesignRows;
            internal readonly Dictionary<string,RowGrid> JointLayouts=new Dictionary<string,RowGrid>();
            internal StockGridContract JointContract;
            internal StockGridMemo LastStockGrid;
            internal StockGridMemo[] StockGrids;
            internal int NextStockGrid;
            public int[] SlabRowIndices,SlabSourceRowIndices;
            internal double[] SharedSlabRowsMm,LeftSourceScales,RightSourceScales;
            internal bool Adjusted;
            public string Key;
            // Moving physical rows around a stock joint retains the exact checked
            // design rows, tributaries and source loads under the bearer assumption.
            // Physical clearances still use Key/SettingOutRows for each geometry.
            internal string SourceAnalysisKey;
            internal string AnalysisKey=>SourceAnalysisKey??Key;
            internal string CheckKey(TowerInput input)=>input.ReuseTransversePreparations ? AnalysisKey : Key;
            internal readonly Dictionary<Tuple<double,double>,RowSlice> Slices=new Dictionary<Tuple<double,double>,RowSlice>();
            internal RowSlice Slice(TowerStockPiece stock)
            {
                var key=Tuple.Create(stock.StartMm,stock.StockLengthMm);
                if(!Slices.TryGetValue(key,out var slice))
                {
                    var indices=Enumerable.Range(0,Rows.Length).Where(i=>Rows[i]>=stock.StartMm && Rows[i]<stock.EndMm).ToArray();
                    slice=new RowSlice {Indices=indices,Positions=indices.Select(i=>Rows[i]-stock.StartMm).ToArray()};Slices[key]=slice;
                }
                return slice;
            }
        }
        private sealed class RowQuantities
        {
            internal readonly int Physical,Left,Right;
            internal RowQuantities(RowGrid grid)
            {Physical=grid.SettingOutRows?.Length??grid.Rows.Length;Left=grid.LeftScales.Count(x=>x>0);Right=grid.RightScales.Count(x=>x>0);}
        }
        internal sealed class IndividualGridKey : IEquatable<IndividualGridKey>
        {
            private readonly long[] values;private readonly int hash;
            internal IndividualGridKey(TowerInput input,double covered,double rowCoverage,double edge)
            {
                var t=input.TransferredSlab;
                var eligible=t.EngineerNetwork==null ? Array.Empty<TransferCrossingBeamRow>() : EngineerPrimaryPlan.Adjacent(t.EngineerNetwork,t.EngineerBeam).ToArray();
                values=new long[22+(t.SharedRowsMm?.Length??0)+(t.LeftBaselineWidthsMm?.Length??0)+(t.RightBaselineWidthsMm?.Length??0)
                    +(t.LeftEffectiveWidthsMm?.Length??0)+(t.RightEffectiveWidthsMm?.Length??0)+(t.ReceivingRowsMm?.Length??0)
                    +2*(t.ReceivingCrossings?.Length??0)+2*eligible.Length];int at=0;
                Action<double> add=x=>values[at++]=BitConverter.DoubleToInt64Bits(x);
                Action<double[]> array=items=>{values[at++]=items?.Length??-1;if(items!=null)foreach(double x in items)add(x);};
                add(covered);add(rowCoverage);add(edge);add(t.RowStart(input,rowCoverage));add(input.LongitudinalStartMm);
                add(t.UpperSpacingMm);add(t.SlabSpacing(true));add(t.SlabSpacing(false));
                values[at++]=t.LeftBayMm>0 ? 1 : 0;values[at++]=t.RightBayMm>0 ? 1 : 0;
                values[at++]=t.LocalReceivingRows ? 1 : 0;values[at++]=t.IncludeConcreteEndStrips ? 1 : 0;values[at++]=t.IncludeMainBeamEndLoads ? 1 : 0;
                // Full source order, including inactive rows, determines the
                // public slab-source ordinal maps. Never trim it to local rows.
                array(t.SharedRowsMm);array(t.LeftBaselineWidthsMm);array(t.RightBaselineWidthsMm);
                array(t.LeftEffectiveWidthsMm);array(t.RightEffectiveWidthsMm);array(t.ReceivingRowsMm);
                values[at++]=t.ReceivingCrossings?.Length??-1;
                if(t.ReceivingCrossings!=null)foreach(var c in t.ReceivingCrossings){add(c.PositionMm);add(c.WidthMm);}
                values[at++]=t.EngineerReceiverCount;values[at++]=eligible.Length;
                foreach(var c in eligible){add(c.PositionMm);add(c.WidthMm);}
                unchecked {int h=17;foreach(long x in values)h=h*31+x.GetHashCode();hash=h;}
            }
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>Equals(other as IndividualGridKey);
            public bool Equals(IndividualGridKey other)=>other!=null && hash==other.hash && values.SequenceEqual(other.values);
        }
        private sealed class IndividualGridSnapshot
        {
            private readonly RowGrid data;
            internal IndividualGridSnapshot(RowGrid source){data=Copy(source);}
            private static T[] Copy<T>(T[] source)=>source==null ? null : (T[])source.Clone();
            private static RowGrid Copy(RowGrid source)=>new RowGrid
            {
                Rows=Copy(source.Rows),Tributaries=Copy(source.Tributaries),LeftScales=Copy(source.LeftScales),RightScales=Copy(source.RightScales),
                LeftPhysicalScales=Copy(source.LeftPhysicalScales),RightPhysicalScales=Copy(source.RightPhysicalScales),
                SettingOutRows=Copy(source.SettingOutRows),SettingOutDesignRows=Copy(source.SettingOutDesignRows),
                SlabRowIndices=Copy(source.SlabRowIndices),SlabSourceRowIndices=Copy(source.SlabSourceRowIndices),SharedSlabRowsMm=Copy(source.SharedSlabRowsMm),
                LeftSourceScales=Copy(source.LeftSourceScales),RightSourceScales=Copy(source.RightSourceScales),
                Adjusted=source.Adjusted,Key=source.Key,SourceAnalysisKey=source.SourceAnalysisKey
            };
            // No mutable joint, slice or quantity memo leaves its receiving
            // context. Public result arrays cannot mutate the saved snapshot.
            internal RowGrid Create()=>Copy(data);
        }
        private sealed class StockGridContract
        {
            private readonly bool engineer,prepared;
            private readonly double edge,maximum;
            private readonly double[] crossings;
            internal StockGridContract(TowerInput input,double edge)
            {
                engineer=input.EngineerWorkflow;prepared=input.PrepareReceiverChecks;this.edge=edge;
                maximum=input.TransferredSlab.UpperSpacingMm;
                crossings=input.TransferredSlab.ReceivingCrossings.SelectMany(c=>new[] {c.PositionMm,c.WidthMm}).ToArray();
            }
            internal bool Matches(TowerInput input,double candidateEdge)
            {
                var current=input.TransferredSlab.ReceivingCrossings;
                if(engineer!=input.EngineerWorkflow || prepared!=input.PrepareReceiverChecks || edge!=candidateEdge
                    || maximum!=input.TransferredSlab.UpperSpacingMm || crossings.Length!=current.Length*2)return false;
                for(int i=0;i<current.Length;i++)
                    if(crossings[2*i]!=current[i].PositionMm || crossings[2*i+1]!=current[i].WidthMm)return false;
                return true;
            }
        }
        private sealed class StockGridMemo
        {
            private readonly double[] stocks;
            internal readonly double Conflict;
            internal readonly RowGrid Grid;
            internal StockGridMemo(Geometry geometry,double conflict,RowGrid grid)
            {
                stocks=new double[geometry.Stock.Length*2];
                for(int i=0;i<geometry.Stock.Length;i++){stocks[2*i]=geometry.Stock[i].StartMm;stocks[2*i+1]=geometry.Stock[i].StockLengthMm;}
                Conflict=conflict;Grid=grid;
            }
            internal bool Matches(Geometry geometry)
            {
                if(stocks.Length!=geometry.Stock.Length*2)return false;
                for(int i=0;i<geometry.Stock.Length;i++)
                    if(stocks[2*i]!=geometry.Stock[i].StartMm || stocks[2*i+1]!=geometry.Stock[i].StockLengthMm)return false;
                return true;
            }
        }
        private sealed class RowSlice { internal int[] Indices; internal double[] Positions; }
        private static RowGrid BuildRowGrid(TowerInput input,double covered,double rowCoverage,double first,int count,double phase,double edge)
        {
            var t=input.TransferredSlab;
            if(t.HasIndividualSpacings)return BuildIndividualRowGrid(input,covered,rowCoverage,edge);
            var shared=t.PlywoodSheetLengthMm>0
                ? PlywoodSpacing.SheetSupportRows(t.RowStart(input,rowCoverage),t.RowStart(input,rowCoverage)+rowCoverage,t.UpperSpacingMm,t.PlywoodSheetLengthMm).Select(x=>x-edge).ToArray()
                : Enumerable.Range(0,count).Select(i=>first+phase+i*t.RowGapMm).ToArray();
            count=shared.Length;
            var slabWidths=Enumerable.Range(0,count).Select(i=>(i==count-1 ? covered : (shared[i]+shared[i+1])/2)-(i==0 ? 0 : (shared[i-1]+shared[i])/2)).ToArray();
            slabWidths[0]-=(covered-rowCoverage)/2;slabWidths[count-1]-=(covered-rowCoverage)/2;
            var rows=shared.ToList();
            if(t.IncludeMainBeamEndLoads && shared.Length>=2)
            {
                // Lower rows can be added without changing any slab stock, foot or tributary.
                // 40 mm keeps the full secondary flange on the primary stock; 80 mm avoids row overlap.
                var initial=Enumerable.Range(0,count).Select(i=>(i==count-1 ? covered : (shared[i]+shared[i+1])/2)-(i==0 ? 0 : (shared[i-1]+shared[i])/2)).ToArray();
                var end=ConcreteEndStripTributaries(covered,shared,edge-input.LongitudinalStartMm);
                if(initial[0]+end[0]>t.UpperSpacingMm+1e-7 && shared[0]>=120)rows.Add(40);
                if(initial[count-1]+end[count-1]>t.UpperSpacingMm+1e-7 && covered-shared.Last()>=120)rows.Add(covered-40);
            }
            var stations=rows.OrderBy(x=>x).ToArray();
            var indices=shared.Select(x=>Array.IndexOf(stations,x)).ToArray();
            var widths=Enumerable.Range(0,stations.Length).Select(i=>(i==stations.Length-1 ? covered : (stations[i]+stations[i+1])/2)-(i==0 ? 0 : (stations[i-1]+stations[i])/2)).ToArray();
            if(t.IncludeConcreteEndStrips || t.IncludeMainBeamEndLoads)
            {var end=ConcreteEndStripTributaries(covered,stations,edge-input.LongitudinalStartMm);for(int i=0;i<widths.Length;i++)widths[i]+=end[i];}
            var left=t.LeftEffectiveWidthsMm??slabWidths;var right=t.RightEffectiveWidthsMm??slabWidths;
            var grid=new RowGrid {Rows=stations,Tributaries=widths,SlabRowIndices=indices,
                LeftPhysicalScales=new double[stations.Length],RightPhysicalScales=new double[stations.Length],LeftScales=new double[stations.Length],RightScales=new double[stations.Length]};
            var ls=FullTributaryScales(slabWidths,left,t.UpperSpacingMm,t.CrossFormwork);var rs=FullTributaryScales(slabWidths,right,t.UpperSpacingMm,t.CrossFormwork);
            for(int i=0;i<indices.Length;i++){int j=indices[i];grid.LeftPhysicalScales[j]=left[i]/1000;grid.RightPhysicalScales[j]=right[i]/1000;grid.LeftScales[j]=ls[i];grid.RightScales[j]=rs[i];}
            grid.Key=Values(stations)+"|"+Values(widths)+"|"+Values(grid.LeftScales)+"|"+Values(grid.RightScales)+"|"+edge.ToString("R",CultureInfo.InvariantCulture);
            return grid;
        }
        private static RowGrid BuildIndividualRowGrid(TowerInput input,double covered,double rowCoverage,double edge)
        {
            var owner=input.ReuseSearchWork && input.Trace?.HasTimeLimit!=true && input.TransferredSlab.HasIndividualSpacings
                ? input.NetworkNumerics??input.TransferredSlab.SharedSearchWork : null;
            if(owner==null)return CreateIndividualRowGrid(input,covered,rowCoverage,edge);
            input.Trace?.CheckCancellation();
            var key=new IndividualGridKey(input,covered,rowCoverage,edge);IndividualGridSnapshot saved;
            lock(owner.IndividualRows)
            {
                input.Trace?.CheckCancellation();
                saved=owner.IndividualRows.TryGetValue(key,out var value) ? (IndividualGridSnapshot)value : null;
            }
            if(saved!=null)
            {input.Trace?.Increment(BeamSearchCounter.IndividualRowGridCacheHits);return saved.Create();}
            var generated=CreateIndividualRowGrid(input,covered,rowCoverage,edge);
            if(generated==null)return null; // Replay failures in their own diagnostic scope.
            input.Trace?.CheckCancellation();
            var snapshot=new IndividualGridSnapshot(generated);
            lock(owner.IndividualRows)
            {
                input.Trace?.CheckCancellation();
                if(!owner.IndividualRows.TryGetValue(key,out _))owner.IndividualRows.Store(key,snapshot);
            }
            return generated;
        }
        private static RowGrid CreateIndividualRowGrid(TowerInput input,double covered,double rowCoverage,double edge)
        {
            var t=input.TransferredSlab;double slabStart=t.RowStart(input,rowCoverage),slabEnd=slabStart+rowCoverage;
            double[] shared,left,right,leftBase,rightBase;
            if(t.SharedRowsMm!=null)
            {
                shared=t.SharedRowsMm;
                leftBase=t.LeftBaselineWidthsMm;rightBase=t.RightBaselineWidthsMm;
                left=t.LeftEffectiveWidthsMm??leftBase;right=t.RightEffectiveWidthsMm??rightBase;
            }
            else
            {
                var l=t.LeftBayMm>0 ? PlywoodSpacing.SheetSupportRows(slabStart,slabEnd,t.SlabSpacing(true)) : new double[0];
                var r=t.RightBayMm>0 ? PlywoodSpacing.CompatibleSheetRows(slabStart,slabEnd,t.SlabSpacing(false),l) : new double[0];
                shared=l.Concat(r).Distinct().OrderBy(x=>x).ToArray();
                Func<double[],double[]> map=own=>
                {
                    if(own.Length==0)return new double[shared.Length];
                    var widths=PlywoodSpacing.RowTributaries(slabStart,slabEnd,own);
                    return shared.Select(x=>Array.IndexOf(own,x)).Select(i=>i<0 ? 0 : widths[i]).ToArray();
                };
                left=leftBase=map(l);right=rightBase=map(r);
            }
            var sourceIndices=Enumerable.Range(0,shared.Length).Where(i=>!t.LocalReceivingRows || left[i]>0 || right[i]>0 || t.ReceivingRowsMm.Contains(shared[i])).ToArray();
            var extra=EngineerPrimaryPlan.Rows(t,shared.Where((x,i)=>!t.LocalReceivingRows || left[i]>0 || right[i]>0).ToArray());
            var required=sourceIndices.Select(i=>shared[i]).Concat(extra).Distinct().OrderBy(x=>x).ToArray();
            double[] absolute;var mapped=(double[])shared.Clone();bool adjusted=false;
            try
            {
                try {absolute=PlywoodSpacing.FillRequiredRows(edge,edge+covered,t.UpperSpacingMm,required);}
                catch(ArgumentException) when(t.LocalReceivingRows)
                {
                    var requests=required.Select(x=>new ReceivingRowRequest {PositionMm=x,
                        Sides=Enumerable.Range(0,shared.Length).Where(i=>shared[i]==x).Select(i=>(left[i]>0 ? 1 : 0)|(right[i]>0 ? 2 : 0)).DefaultIfEmpty().First(),
                        Crossing=t.ReceivingRowsMm.Contains(x) || extra.Contains(x)}).ToArray();
                    foreach(var request in requests.Where(r=>r.Crossing))foreach(var c in t.ReceivingCrossings.Where(c=>Math.Abs(c.PositionMm-request.PositionMm)<=c.WidthMm/2-40+1e-7))
                    {request.MinimumMm=Math.Max(request.MinimumMm,c.PositionMm-c.WidthMm/2+40);request.MaximumMm=Math.Min(request.MaximumMm,c.PositionMm+c.WidthMm/2-40);}
                    var coordinated=ReceivingRows.Coordinate(edge,edge+covered,t.UpperSpacingMm,requests);
                    absolute=coordinated.RowsMm;
                    foreach(int i in sourceIndices)mapped[i]=coordinated.ReceiversMm[Array.IndexOf(required,shared[i])];
                    adjusted=true;
                }
            }
            catch(ArgumentException ex)
            {input.Diagnostics?.Geometry(input.DiagnosticScope+"receiving row setting-out",input.DiagnosticScope+ex.Message,"Review required slab/crossing rows and plywood joints.");return null;}
            var rows=absolute.Select(x=>x-edge).ToArray();
            var sourceSet=new HashSet<int>(sourceIndices);
            var indices=shared.Select((x,i)=>sourceSet.Contains(i) ? Array.IndexOf(absolute,mapped[i]) : -1).ToArray();
            var widthsBeam=PlywoodSpacing.RowTributaries(0,covered,rows);
            if(t.IncludeConcreteEndStrips || t.IncludeMainBeamEndLoads)
            {var end=ConcreteEndStripTributaries(covered,rows,edge-input.LongitudinalStartMm);for(int i=0;i<rows.Length;i++)widthsBeam[i]+=end[i];}
            var grid=new RowGrid {Rows=rows,Tributaries=widthsBeam,SlabRowIndices=t.LocalReceivingRows ? indices.Where(i=>i>=0).Distinct().OrderBy(i=>i).ToArray() : indices,
                SharedSlabRowsMm=t.LocalReceivingRows ? shared : null,SlabSourceRowIndices=t.LocalReceivingRows ? indices : null,Adjusted=adjusted,
                LeftPhysicalScales=new double[rows.Length],RightPhysicalScales=new double[rows.Length],LeftScales=new double[rows.Length],RightScales=new double[rows.Length]};
            var ls=FullTributaryScales(leftBase,left,t.SlabSpacing(true),true);var rs=FullTributaryScales(rightBase,right,t.SlabSpacing(false),true);
            grid.LeftSourceScales=ls;grid.RightSourceScales=rs;
            for(int i=0;i<shared.Length;i++)
            {int j=indices[i];if(j<0)continue;grid.LeftPhysicalScales[j]+=left[i]/1000;grid.RightPhysicalScales[j]+=right[i]/1000;grid.LeftScales[j]+=ls[i];grid.RightScales[j]+=rs[i];}
            grid.Key=Values(rows)+"|"+Values(widthsBeam)+"|"+Values(grid.LeftScales)+"|"+Values(grid.RightScales)+"|"+edge.ToString("R",CultureInfo.InvariantCulture);
            return grid;
        }
        // Per-worker bounded LRU: a late, frequently used response must not be
        // excluded just because the first 4096 trial responses filled the cache.
        internal sealed class ResponseCache<TKey,TValue>
        {
            private readonly int capacity;
            private readonly Dictionary<TKey,LinkedListNode<KeyValuePair<TKey,TValue>>> entries=new Dictionary<TKey,LinkedListNode<KeyValuePair<TKey,TValue>>>();
            private readonly LinkedList<KeyValuePair<TKey,TValue>> order=new LinkedList<KeyValuePair<TKey,TValue>>();
            internal ResponseCache(int capacity){this.capacity=capacity;}
            internal int Count=>entries.Count;
            internal bool TryGetValue(TKey key,out TValue value)
            {
                if(!entries.TryGetValue(key,out var node)){value=default(TValue);return false;}
                if(!ReferenceEquals(node,order.Last)){order.Remove(node);order.AddLast(node);}
                value=node.Value.Value;return true;
            }
            internal void Store(TKey key,TValue value)
            {
                LinkedListNode<KeyValuePair<TKey,TValue>> node;
                if(entries.TryGetValue(key,out node)){order.Remove(node);entries.Remove(key);}
                else if(entries.Count>=capacity){node=order.First;entries.Remove(node.Value.Key);order.RemoveFirst();}
                // The private node has no external identity. Replace its key as
                // well as its value, preserving remove/add dictionary semantics
                // for an equal key object with different ownership.
                if(node==null)node=order.AddLast(new KeyValuePair<TKey,TValue>(key,value));
                else {node.Value=new KeyValuePair<TKey,TValue>(key,value);order.AddLast(node);}
                entries.Add(key,node);
            }
        }
        private struct PrimaryLoadKey : IEquatable<PrimaryLoadKey>
        {
            private readonly double start,length,edge;private readonly int line,stage;
            internal PrimaryLoadKey(double start,double length,int line,int stage,double edge){this.start=start;this.length=length;this.line=line;this.stage=stage;this.edge=edge;}
            public bool Equals(PrimaryLoadKey other)=>start==other.start && length==other.length && edge==other.edge && line==other.line && stage==other.stage;
            public override bool Equals(object other)=>other is PrimaryLoadKey && Equals((PrimaryLoadKey)other);
            public override int GetHashCode(){unchecked{return (((start.GetHashCode()*31+length.GetHashCode())*31+line)*31+stage)*31+edge.GetHashCode();}}
        }
        private sealed class PrimaryLoadData
        {
            internal BeamDistributedLoad[] Uniform;
            internal double[] Positions,Forces;
        }
        internal readonly struct UniformShapeKey : IEquatable<UniformShapeKey>
        {
            private readonly double length,ei,start,end;private readonly int denominator,hash;private readonly double[] supports;
            internal UniformShapeKey(double length,double[] supports,double ei,int denominator,double start=0,double? end=null)
            {
                this.length=length;this.supports=supports;this.ei=ei;this.denominator=denominator;this.start=start;this.end=end??length;
                unchecked{int h=(((length.GetHashCode()*31+ei.GetHashCode())*31+denominator)*31+this.start.GetHashCode())*31+this.end.GetHashCode();foreach(double x in supports)h=h*31+x.GetHashCode();hash=h;}
            }
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>other is UniformShapeKey && Equals((UniformShapeKey)other);
            public bool Equals(UniformShapeKey other)
            {
                if(hash!=other.hash || length!=other.length || ei!=other.ei || start!=other.start || end!=other.end || denominator!=other.denominator)return false;
                if(ReferenceEquals(supports,other.supports))return true;
                if(supports==null || other.supports==null || supports.Length!=other.supports.Length)return false;
                for(int i=0;i<supports.Length;i++)if(!supports[i].Equals(other.supports[i]))return false;
                return true;
            }
        }
        internal sealed class PrimaryCheck { public LoadedBeamAnalysis Analysis; public bool Passed; public BeamPointLoad[] Points; public BeamDistributedLoad[] Uniform; }
        internal sealed class PrimaryKey : IEquatable<PrimaryKey>
        {
            private readonly double length;
            private readonly int uniformCount;
            private readonly bool deferred;
            private readonly double[] supports;
            private readonly double[] positions,forces;
            private readonly int hash;
            internal PrimaryKey(double length,double[] supports,double[] positions,double[] forces,int uniformCount=-1,bool deferred=false)
            {
                this.length=length;this.supports=supports;this.positions=positions;this.forces=forces;this.uniformCount=uniformCount;this.deferred=deferred;
                unchecked
                {
                    int h=(length.GetHashCode()*31+uniformCount)*31+(deferred ? 1 : 0);
                    for(int i=0;i<supports.Length;i++)h=h*31+supports[i].GetHashCode();
                    for(int i=0;i<positions.Length;i++) {h=h*31+positions[i].GetHashCode();h=h*31+forces[i].GetHashCode();}
                    hash=h;
                }
            }
            internal PrimaryKey(double length,double[] supports,BeamDistributedLoad[] uniform)
                :this(length,supports,uniform,Array.Empty<BeamPointLoad>()) {}
            internal PrimaryKey(double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,bool deferred=false)
                :this(length,supports,uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm}).Concat(points.Select(p=>p.PositionMm)).ToArray(),uniform.SelectMany(q=>new[] {q.KnPerM,q.KnPerM}).Concat(points.Select(p=>p.Kn)).ToArray(),uniform.Length,deferred) {}
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>Equals(other as PrimaryKey);
            public bool Equals(PrimaryKey other)
            {
                if(other==null || hash!=other.hash || length!=other.length || uniformCount!=other.uniformCount || deferred!=other.deferred || supports.Length!=other.supports.Length || positions.Length!=other.positions.Length)return false;
                for(int i=0;i<supports.Length;i++)if(supports[i]!=other.supports[i])return false;
                for(int i=0;i<positions.Length;i++)if(positions[i]!=other.positions[i] || forces[i]!=other.forces[i])return false;
                return true;
            }
        }
        internal struct PreparedPrimaryKey : IEquatable<PreparedPrimaryKey>
        {
            private readonly PrimaryKey member;private readonly PrimaryBeamMaterial material;private readonly int criterion;private readonly double legLimit;private readonly bool ignoreUplift;
            internal PreparedPrimaryKey(PrimaryKey member,TowerInput input){this.member=member;material=input.PrimaryMaterial;criterion=input.DeflectionDenominator;legLimit=input.LegCapacityKn;ignoreUplift=input.IgnoreUplift;}
            public bool Equals(PreparedPrimaryKey other)=>material==other.material && criterion==other.criterion && legLimit==other.legLimit && ignoreUplift==other.ignoreUplift && member.Equals(other.member);
            public override bool Equals(object other)=>other is PreparedPrimaryKey && Equals((PreparedPrimaryKey)other);
            public override int GetHashCode(){unchecked{return (((member.GetHashCode()*31+(int)material)*31+criterion)*31+legLimit.GetHashCode())*31+(ignoreUplift ? 1 : 0);}}
        }
        internal sealed class PreparedPrimaryCheck {internal bool Passed;internal TowerSearchDiagnostics Diagnostics;internal LoadedBeamAnalysis Analysis;internal UniformPrimaryFailure? UniformFailure;}
        // Exact immutable numerical inputs. Include both distributed and point loads,
        // stiffness and criterion; no rounded geometry, forces or eligibility decisions.
        internal sealed class TransverseKey : IEquatable<TransverseKey>
        {
            private readonly double[] values;private readonly int hash;
            internal TransverseKey(double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,bool signed=false,bool rejectResolvedPrimary=false)
            {
                int uniforms=uniform?.Length??0,pointCount=points?.Length??0;
                values=new double[7+supports.Length+uniforms*3+pointCount*2];int at=0;
                values[at++]=length;values[at++]=ei;values[at++]=denominator;values[at++]=signed ? 1 : 0;values[at++]=supports.Length;
                foreach(double support in supports)values[at++]=support;
                values[at++]=uniforms;
                if(uniform!=null)foreach(var load in uniform){values[at++]=load.StartMm;values[at++]=load.EndMm;values[at++]=load.KnPerM;}
                values[at++]=pointCount;
                if(points!=null)foreach(var load in points){values[at++]=load.PositionMm;values[at++]=load.Kn;}
                unchecked {int h=17;foreach(double value in values)h=h*31+value.GetHashCode();hash=h;}
            }
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>Equals(other as TransverseKey);
            public bool Equals(TransverseKey other)=>other!=null && hash==other.hash && values.SequenceEqual(other.values);
        }
        // One numerical worker owns this bounded cache for one catalogue/custom pass.
        // Complete responses use exact force keys; diagnostic locations are never cached.
        internal sealed class SearchWorkCache
        {
            // Numeric immutable snapshots only: no input, network, callback or
            // global registry owner is retained by this per-worker cache.
            internal readonly ResponseCache<IndividualGridKey,object> IndividualRows=new ResponseCache<IndividualGridKey,object>(128);
            private readonly Dictionary<TransverseKey,LinkedListNode<KeyValuePair<TransverseKey,LoadedBeamAnalysis>>> transverse=new Dictionary<TransverseKey,LinkedListNode<KeyValuePair<TransverseKey,LoadedBeamAnalysis>>>();
            private readonly LinkedList<KeyValuePair<TransverseKey,LoadedBeamAnalysis>> transverseOrder=new LinkedList<KeyValuePair<TransverseKey,LoadedBeamAnalysis>>();
            internal LoadedBeamAnalysis GetTransverse(TransverseKey key)
            {
                if(!transverse.TryGetValue(key,out var node))return null;
                transverseOrder.Remove(node);transverseOrder.AddLast(node);return node.Value.Value;
            }
            internal void StoreTransverse(TransverseKey key,LoadedBeamAnalysis value)
            {
                if(transverse.ContainsKey(key))return;
                if(transverse.Count>=16384) {transverse.Remove(transverseOrder.First.Value.Key);transverseOrder.RemoveFirst();}
                transverse.Add(key,transverseOrder.AddLast(new KeyValuePair<TransverseKey,LoadedBeamAnalysis>(key,value)));
            }
            internal readonly ResponseCache<PreparedPrimaryKey,PreparedPrimaryCheck> PreparedPrimaryChecks=new ResponseCache<PreparedPrimaryKey,PreparedPrimaryCheck>(16384);
            internal readonly LoadedBeamAnalysis.MatrixCache Matrix=new LoadedBeamAnalysis.MatrixCache(32768,true,leastRecentlyUsed:true,maximumBytes:80L*1024*1024);
            private readonly ResponseCache<PrimaryKey,PrimaryCheck> responses=new ResponseCache<PrimaryKey,PrimaryCheck>(4096);
            internal readonly ResponseCache<UniformShapeKey,LoadedBeamAnalysis> UniformShapes=new ResponseCache<UniformShapeKey,LoadedBeamAnalysis>(8192);
            internal PrimaryCheck Get(PrimaryKey key)=>responses.TryGetValue(key,out var check) ? check : null;
            internal void Store(PrimaryKey key,PrimaryCheck check)=>responses.Store(key,check);
        }
        internal static LoadedBeamAnalysis AnalyzeTransverse(TowerInput input,BeamSearchCounter counter,double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,bool signed=false,bool rejectResolvedPrimary=false)
        {
            using(var hot=input.Trace?.Hot(counter==BeamSearchCounter.PrimaryAnalyses ? BeamHotStage.PrimaryResponse : BeamHotStage.TransverseResponse))
            {
            input.Trace?.CheckCancellation();
            bool reject=rejectResolvedPrimary && counter==BeamSearchCounter.PrimaryAnalyses && input.StagePrimaryChecks && !input.Primary.IsWS10;
            var shared=input.ReuseSearchWork && input.ReuseTransverseNumerics ? input.NetworkNumerics??input.TransferredSlab?.SharedSearchWork : null;
            var key=shared==null ? null : new TransverseKey(length,supports,uniform,points,ei,denominator,input.IgnoreUplift || signed);
            var response=key==null ? null : shared.GetTransverse(key);
            if(response!=null) {input.Trace?.Increment(BeamSearchCounter.NetworkTransverseCacheHits);return response;}
            input.Trace?.Increment(counter);
            using(input.Trace?.Measure(BeamSearchStage.TransverseAnalysis))
            {
                // Only fully resolved H20 checks may stop after another check
                // proves rejection. Ordinary callers and every accepted member
                // retain a complete response; partial failures never enter the
                // shared numerical response cache below.
                bool reuseMatrix=counter==BeamSearchCounter.PrimaryAnalyses && input.OptimizePrimaryResponses && input.ReuseNetworkNumerics && shared!=null;
                if(reuseMatrix || reject)
                {
                    var matrix=reuseMatrix ? shared.Matrix : null;long before=matrix?.Hits??0;
                    response=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,uniform,points,ei,denominator,matrix,input.LegCapacityKn,reject,input.IgnoreUplift,allowSignedLoads:input.IgnoreUplift || signed);
                    if(matrix!=null && matrix.Hits>before)input.Trace?.Increment(BeamSearchCounter.PrimaryMatrixCacheHits);
                }
                else response=input.IgnoreUplift || signed ? LoadedBeamAnalysis.AnalyzeSigned(length,supports,uniform,points,ei,denominator) : LoadedBeamAnalysis.Analyze(length,supports,uniform,points,ei,denominator);
            }
            if(!response.DeflectionEvaluated)input.Trace?.Increment(BeamSearchCounter.PrimaryDeflectionSkipped);
            if(key!=null && response.DeflectionEvaluated)shared.StoreTransverse(key,response);
            return response;
            }
        }
        private static string Values(IEnumerable<double> values) => string.Join(";",values.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
        public static TowerLayout Optimize(TowerInput input,bool shareDiagnostics=false)
        {
            input.TransferredSlab.Validate(input);
            if(shareDiagnostics)
            {
                if(input.TransferredSlab.LeftBayMm>0 && !PlywoodSpacing.StandardSpacingsMm.Contains(input.TransferredSlab.SlabSpacing(true))
                    || input.TransferredSlab.RightBayMm>0 && !PlywoodSpacing.StandardSpacingsMm.Contains(input.TransferredSlab.SlabSpacing(false)))
                    throw new ArgumentException("Slab design spacing must be a permitted nominal value; local gaps are separate.");
                return OptimizeCore(input,true);
            }
            if(input.TransferredSlab.HasIndividualSpacings && input.TransferredSlab.SharedRowsMm==null)
            {
                TowerOptimizationException failure=null;var attempted=new HashSet<string>();
                Func<double,int,double> closer=(x,step)=>x==0 ? 0 : PlywoodSpacing.StandardSpacingsMm.SkipWhile(v=>v!=x).Skip(step).DefaultIfEmpty(250).First();
                for(int step=0;step<PlywoodSpacing.StandardSpacingsMm.Count;step++)
                {
                    var worker=input.CopyForSearch();var t=input.TransferredSlab.Copy();worker.TransferredSlab=t;
                    t.UpperSpacingMm=closer(t.UpperSpacingMm,step);t.LeftSlabSpacingMm=closer(t.LeftSlabSpacingMm,step);t.RightSlabSpacingMm=closer(t.RightSlabSpacingMm,step);
                    if(!attempted.Add(t.UpperSpacingMm+"|"+t.LeftSlabSpacingMm+"|"+t.RightSlabSpacingMm))continue;
                    try {return TowerStockPlanner.SearchWithCustom(worker,()=>OptimizeCore(worker,false),Better);}
                    catch(TowerOptimizationException ex){failure=ex;}
                }
                throw failure;
            }
            if(input.TransferredSlab.LeftBayMm+input.TransferredSlab.RightBayMm>0)
            {
                var source=input.TransferredSlab;
                double pressure=Math.Max(source.LeftAreaLoadKnM2,source.RightAreaLoadKnM2);
                double maximum=Math.Min(source.UpperSpacingMm,PlywoodSpacing.CalculateForSheet(PlywoodCatalog.Default,pressure,350).MaximumSpacingMm);
                PlywoodSpacing.ListedSpacingAtOrBelow(maximum);
                TowerOptimizationException failure=null;
                foreach(double spacing in PlywoodSpacing.StandardSpacingsMm.Where(x=>x<=maximum))
                {
                    var worker=input.CopyForSearch();worker.TransferredSlab=source.Copy();worker.TransferredSlab.UpperSpacingMm=spacing;
                    try {return TowerStockPlanner.SearchWithCustom(worker,()=>OptimizeCore(worker,false),Better);}
                    catch(TowerOptimizationException ex){failure=ex;}
                }
                throw failure;
            }
            return TowerStockPlanner.SearchWithCustom(input,()=>OptimizeCore(input,false),Better);
        }
        private sealed class LayoutWork
        {
            internal Geometry Geometry;internal double Width,Edge;internal bool Rotated;
            internal GeometryRank Rank;
            internal CrossOption[] Crosses;internal RowGrid Grid;
            internal int FirstSpansPassing=-1;
            internal int JoinedBays,Narrow;internal double Brace,Cantilever,Symmetry;
        }
        private static TowerLayout OptimizeCore(TowerInput input,bool shareDiagnostics)
            => input.EngineerSearch ? OptimizeEngineer(input,shareDiagnostics) : input.SearchByLegTier ? OptimizeCoreByLegTier(input,shareDiagnostics) : OptimizeCoreReference(input,shareDiagnostics);
        private static TowerLayout OptimizeEngineer(TowerInput input,bool shareDiagnostics)
        {
            var t=input.TransferredSlab;t.Validate(input);
            var widths=t.FixedTowerWidthMm.HasValue ? new[] {t.FixedTowerWidthMm.Value} : new[] {1524.0,1000,1500,2000};
            var seeds=widths.SelectMany(width=>CrossOptions(input,width,t.UpperSpacingMm)).Select(c=>EngineerTowerSearch.InitialLegs(input,c.DesignCases.Max(stage=>c.Cases[stage].Reactions.Max())*input.LengthMm/1000)).ToArray();
            if(seeds.Length==0)return OptimizeCoreByLegTier(input,shareDiagnostics);
            int maximum=Math.Min(EngineerTowerSearch.MaximumStations(input),input.NetworkLegCeiling/2);
            int limit=Math.Min(maximum,Math.Max(input.EngineerMinimumStations,Math.Max(4,input.EngineerPlan?.InitialLegs??(seeds.Min()+2))));
            var cache=t.SharedGeometryCache??new GeometryCache();
            // Receiver-count refinement only changes eligible shared-spreader rows.
            // Dedicated/negative-gap crossings have identical geometry and checks
            // at counts 3 through 8; do not repeat a failed search six times.
            bool refineReceivers=t.EngineerNetwork!=null && EngineerPrimaryPlan.Adjacent(t.EngineerNetwork,t.EngineerBeam).Any();
            // Increasing the station ceiling changes candidate geometry only.
            // Keep exact transverse options, rows and numerical checks for each
            // fixed receiver count instead of preparing them again at every tier.
            var preparations=input.ReuseSearchWork && shareDiagnostics ? new Dictionary<int,ReceivingPreparation>() : null;
            int receiverCount=3;
            while(true)
            {
                var worker=input.CopyForSearch();worker.EngineerStationLimit=limit;
                worker.TransferredSlab=t.Copy();worker.TransferredSlab.SharedGeometryCache=cache;worker.TransferredSlab.EngineerReceiverCount=receiverCount;
                ReceivingPreparation prepared=null;
                if(preparations!=null && !preparations.TryGetValue(receiverCount,out prepared))preparations[receiverCount]=prepared=new ReceivingPreparation(worker);
                try{return OptimizeCoreByLegTier(worker,shareDiagnostics,prepared);}
                catch(TowerOptimizationException) when(refineReceivers && receiverCount<8){receiverCount++;}
                catch(TowerOptimizationException) when(limit<maximum){limit=Math.Min(maximum,limit+(input.EngineerPlan==null ? 4 : 2));receiverCount=3;}
            }
        }
        private static TowerLayout OptimizeCoreReference(TowerInput input,bool shareDiagnostics)
        {
            var t = input.TransferredSlab; t.Validate(input); TowerLayout best = null; int tested = 0;
            var geometryCache = input.ReuseSearchWork ? t.SharedGeometryCache ?? new GeometryCache() : null;
            // A receiving-beam solve has fixed beam loads and upper-foot plans. These options
            // depend on transverse width and tributary size, not the primary stock cover.
            var crossCache = new Dictionary<ReceivingPairKey,CrossOption[]>();
            var rowCache = new Dictionary<ReceivingPairKey,RowGrid>();
            // Different tower combinations often repeat an identical local primary member.
            // Include every support, distributed-load boundary and intensity, without rounding. This cache
            // lives only in one receiving-beam solve and retains at most 4096 checks.
            var primaryCache = input.ReuseSearchWork ? new ResponseCache<PrimaryKey,PrimaryCheck>(4096) : null;
            var shared = input.ReuseSearchWork && input.ReuseNetworkNumerics ? t.SharedSearchWork : null;
            var matrixCache = input.ReuseSearchWork ? shared?.Matrix ?? new LoadedBeamAnalysis.MatrixCache() : null;
            if(!shareDiagnostics || input.Diagnostics==null)input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator,trace:input.Trace,ignoreUplift:input.IgnoreUplift) {LazyLocations=input.LazySearchDiagnostics};var diag=input.Diagnostics;
            input.Trace?.Work("Transfer primary cover generation");
            double[][] covers;
            using(input.Trace?.Measure(BeamSearchStage.StockCovers))covers=Covers(input).ToArray();
            if(covers.Length==0)diag.Geometry(input.DiagnosticScope+"coverage",$"{input.DiagnosticScope}primary coverage: no available stock combination totals {input.LengthMm-2*input.MaximumConcreteEndOverhangMm:0.###}–{input.LengthMm:0.###} mm.","Review stock lengths and the concrete end allowance.");
            try
            {
                foreach (var cover in covers)
                {
                    input.Trace?.Increment(BeamSearchCounter.CoverageVariants);
                    if(cover.Length*4>input.NetworkLegCeiling)continue;
                    double covered = cover.Sum(), edge = input.CoverStart(covered);
                    double rowCoverage=t.FixedCoveredLengthMm??covered;
                    bool independent=t.IsNetwork && t.AllowLongerPrimaryCoverage && t.FixedCoveredLengthMm.HasValue;
                    if(t.FixedCoveredLengthMm.HasValue && (independent ? covered<rowCoverage-1e-7 : Math.Abs(covered-rowCoverage)>1e-7)) continue;
                    int count = (int)Math.Ceiling(rowCoverage/t.RowGapMm);
                    // Slab rows stay aligned in absolute coordinates across the network. Primary
                    // stock may extend farther toward the concrete ends on an individual beam.
                    double first = (rowCoverage-(count-1)*t.RowGapMm)/2+t.RowStart(input,rowCoverage)-edge;
                    foreach (bool rotated in new[] { false, true }) foreach (double width in rotated ? TowerOptimizer.BaysMm : new[] { 1524.0 })
                    {
                        if (t.FixedTowerWidthMm.HasValue && Math.Abs(width-t.FixedTowerWidthMm.Value)>1e-7) continue;
                        if (rotated && input.Exclude600Standalone && width == 600) continue;
                        input.Trace?.Work("Transfer slab and lower secondary options");
                        double tributary = t.UpperSpacingMm;
                        var crossKey = new ReceivingPairKey(width,tributary);
                        input.Trace?.CheckCancellation();
                        if (!input.ReuseSearchWork || !crossCache.TryGetValue(crossKey,out var crosses))
                        {
                            using(input.Trace?.Measure(BeamSearchStage.UpperOptions))
                                crosses = CrossOptions(input,width,tributary).ToArray();
                            if (input.ReuseSearchWork) crossCache[crossKey] = crosses;
                        }
                        else input.Trace?.Increment(BeamSearchCounter.CrossCacheHits);
                        if (crosses.Length == 0) continue;
                        input.Trace?.Work("Transfer tower/support geometry");
                        var geometries=SearchGeometries(input,geometryCache,cover,rotated);
                        if(geometries.Length==0)diag.Geometry(input.DiagnosticScope+"tower geometry",$"{input.DiagnosticScope}tower geometry: no searched D3 support pattern fits stocks {string.Join(", ",cover)} mm with {input.StockEndSetbackMm:0.###} mm end setbacks; exclude standalone 600 = {input.Exclude600Standalone}.","Review stock lengths, end setbacks and the standalone-600 setting.");
                        foreach (var geometry in geometries)
                        {
                            if (geometry.Positions.Length*2>input.NetworkLegCeiling) continue;
                            // Try centred rows and phase shifts. Independent primary coverage adds beam load
                            // at the end rows without increasing the shared slab tributaries.
                            foreach (double phase in t.PlywoodSheetLengthMm>0 ? new[] {0.0} : t.FixedRowPhaseMm.HasValue ? new[] {t.FixedRowPhaseMm.Value} : new[] { 0.0, -50, 50, -100, 100 })
                            {
                                var rowKey = new ReceivingPairKey(covered,phase);
                                if (!rowCache.TryGetValue(rowKey,out var grid))
                                {
                                    grid=BuildRowGrid(input,covered,rowCoverage,first,count,phase,edge);
                                    rowCache[rowKey]=grid;
                                }
                                if(grid==null)continue;
                                var rows = grid.Rows;
                                if (rows[0] <= 0 || rows.Last() >= covered) continue;
                                var tributaries = grid.Tributaries;
                                // Shared slab tributaries were checked by the network solver. A longer
                                // primary cover puts additional beam concrete onto the end rows; the
                                // resulting larger beam tributaries are checked at their actual loads.
                                if (!independent && !t.IncludeConcreteEndStrips && !t.IncludeMainBeamEndLoads && tributaries.Max() > t.UpperSpacingMm+1e-7) continue;
                                // A transverse row must bear on both longitudinal stocks, away from independent member breaks.
                                var conflicting=rows.FirstOrDefault(r=>geometry.Stock.Skip(1).Any(s=>Math.Abs(s.StartMm-r)<40));
                                if(conflicting>0 && (grid=GridForStocks(input,geometry,grid,edge))==null) {double joint=geometry.Stock.Skip(1).OrderBy(s=>Math.Abs(s.StartMm-conflicting)).First().StartMm;diag.Geometry(input.DiagnosticScope+"row/joint",$"{input.DiagnosticScope}support row x={conflicting+edge:0.###} mm is {Math.Abs(joint-conflicting):0.###} mm from primary joint x={joint+edge:0.###} mm; minimum 40 mm.","Review upper row spacing and the primary stock combination.",40-Math.Abs(joint-conflicting));continue;}
                                foreach (var cross in crosses)
                                {
                                    input.Trace?.Work("Transfer layout load-case checks");input.Trace?.Increment(BeamSearchCounter.TransferCandidates);
                                    tested++;
                                    if(input.PruneUneconomicCandidates && best!=null && Economy(geometry,cross,grid,input)>Economy(best)+.01)
                                    {input.Trace?.Increment(BeamSearchCounter.EconomyPruned);continue;}
                                    var candidate = OffsetReceiver(input,Evaluate(input,geometry,width,rotated,cross,grid,edge,primaryCache,matrixCache,shared,best));
                                    if (candidate != null && (best == null || Better(candidate,best)) && (input.TransferLayoutAcceptance==null || AcceptLayout(input,candidate))) best = candidate;
                                }
                            }
                        }
                    }
                }
            }
            catch(BeamSearchDeadlineException) when(best!=null) {best.SearchTimeLimitReached=true;}
            if (best == null) throw diag.Exception("No valid transferred-slab layout found within the bounded search.");
            best.SearchTimeLimitReached=input.Trace?.BudgetReached==true;
            best.Tested = tested; return best;
        }
        private static double ConflictingRow(Geometry geometry,double[] rows)
        {
            int first=rows.Length;
            for(int i=1;i<geometry.Stock.Length;i++)
            {
                double joint=geometry.Stock[i].StartMm;int at=Array.BinarySearch(rows,joint);if(at<0)at=~at;
                int before=at-1;while(before>0 && Math.Abs(rows[before-1]-joint)<40)before--;
                if(before>=0 && Math.Abs(rows[before]-joint)<40)first=Math.Min(first,before);
                if(at<rows.Length && Math.Abs(rows[at]-joint)<40)first=Math.Min(first,at);
            }
            return first==rows.Length ? 0 : rows[first];
        }
        private static void EnsureStockGridContract(TowerInput input,RowGrid source,double edge)
        {
            if(source.JointContract!=null && source.JointContract.Matches(input,edge))return;
            var contract=new StockGridContract(input,edge);input.Trace?.CheckCancellation();
            source.JointLayouts.Clear();source.StockGrids=null;source.LastStockGrid=null;source.NextStockGrid=0;source.JointContract=contract;
        }
        // Primary support positions do not affect row/joint conflicts or their
        // repair. Reuse the complete answer for the exact stock starts/lengths
        // within this one immutable base row grid and repair-input contract.
        private static RowGrid ResolveStockGrid(TowerInput input,Geometry geometry,RowGrid source,double edge,out double conflicting)
        {
            input.Trace?.CheckCancellation();EnsureStockGridContract(input,source,edge);
            bool reuse=input.ReuseSearchWork && input.PrepareReceiverChecks;
            if(reuse)
            {
                var saved=source.LastStockGrid;
                if(saved!=null && saved.Matches(geometry)){conflicting=saved.Conflict;return saved.Grid;}
                if(source.StockGrids!=null)foreach(var entry in source.StockGrids)
                    if(entry!=null && entry.Matches(geometry)){source.LastStockGrid=entry;conflicting=entry.Conflict;return entry.Grid;}
            }
            conflicting=input.PrepareReceiverChecks ? ConflictingRow(geometry,source.Rows) : source.Rows.FirstOrDefault(r=>geometry.Stock.Skip(1).Any(s=>Math.Abs(s.StartMm-r)<40));
            var result=conflicting>0 ? GridForStocks(input,geometry,source,edge) : source;
            input.Trace?.CheckCancellation();
            if(reuse)
            {
                var saved=new StockGridMemo(geometry,conflicting,result);
                if(source.StockGrids==null)source.StockGrids=new StockGridMemo[8];
                source.StockGrids[source.NextStockGrid]=saved;source.NextStockGrid=(source.NextStockGrid+1)%source.StockGrids.Length;source.LastStockGrid=saved;
            }
            return result;
        }
        // The site bearer permits lower H20 placement to vary independently of the
        // checked design rows. Repair primary-joint conflicts before rejecting a
        // tower arrangement; keep all source loads and nominal UDLs unchanged.
        private static RowGrid GridForStocks(TowerInput input,Geometry geometry,RowGrid source,double edge)
        {
            if(source==null)return null;
            EnsureStockGridContract(input,source,edge);
            if(ConflictingRow(geometry,source.SettingOutRows??source.Rows)==0)return source;
            if(!input.EngineerWorkflow)return null;
            string key=Values(geometry.Stock.Select(p=>p.StartMm).ToArray());
            if(source.JointLayouts.TryGetValue(key,out var cached))return cached;
            double covered=geometry.Stock.Sum(p=>p.StockLengthMm);
            var crossings=input.TransferredSlab.ReceivingCrossings;
            var requests=source.Rows.Select((x,i)=>
            {
                var request=new ReceivingRowRequest {PositionMm=x,Sides=1,MinimumMm=40,MaximumMm=covered-40};
                foreach(var c in crossings)
                {
                    double a=c.PositionMm-c.WidthMm/2-edge,b=c.PositionMm+c.WidthMm/2-edge;
                    if(x>=a && x<=b)
                    {request.Crossing=true;request.MinimumMm=Math.Max(request.MinimumMm,a+40);request.MaximumMm=Math.Min(request.MaximumMm,b-40);}
                    else if(x<a)request.MaximumMm=Math.Min(request.MaximumMm,a-40);
                    else request.MinimumMm=Math.Max(request.MinimumMm,b+40);
                }
                // Retain the checked end overhangs. Interior sheet joints may be
                // cut locally; upper slab H20s and their source maps stay fixed.
                if(i==0 || i==source.Rows.Length-1)request.MinimumMm=request.MaximumMm=x;
                return request;
            }).ToArray();
            try
            {
                var joints=geometry.Stock.Skip(1).Select(p=>p.StartMm).ToArray();
                var placed=ReceivingRows.Coordinate(0,covered,input.TransferredSlab.UpperSpacingMm,requests,joints);
                var mapping=placed.RowsMm.Select(x=>
                {
                    int original=Array.FindIndex(placed.ReceiversMm,r=>Math.Abs(r-x)<1e-7);
                    if(original>=0)return original;
                    // Added bearers use a checked row from the same normal or
                    // crossing zone, and do not duplicate any upper slab foot.
                    return Enumerable.Range(0,requests.Length).Where(i=>x>=requests[i].MinimumMm-1e-7 && x<=requests[i].MaximumMm+1e-7)
                        .OrderBy(i=>Math.Abs(placed.ReceiversMm[i]-x)).DefaultIfEmpty(-1).First();
                }).ToArray();
                if(mapping.Any(i=>i<0) || ConflictingRow(geometry,placed.RowsMm)>0)
                {input.Trace?.CheckCancellation();source.JointLayouts[key]=null;return null;}
                var adjusted=new RowGrid {Rows=source.Rows,Tributaries=source.Tributaries,LeftScales=source.LeftScales,RightScales=source.RightScales,
                    LeftPhysicalScales=source.LeftPhysicalScales,RightPhysicalScales=source.RightPhysicalScales,SlabRowIndices=source.SlabRowIndices,
                    SlabSourceRowIndices=source.SlabSourceRowIndices,SharedSlabRowsMm=source.SharedSlabRowsMm,LeftSourceScales=source.LeftSourceScales,RightSourceScales=source.RightSourceScales,
                    Adjusted=true,SettingOutRows=placed.RowsMm,SettingOutDesignRows=mapping,SourceAnalysisKey=source.AnalysisKey,Key=source.Key+"|joints:"+key};
                input.Trace?.CheckCancellation();source.JointLayouts[key]=adjusted;return adjusted;
            }
            catch(ArgumentException){input.Trace?.CheckCancellation();source.JointLayouts[key]=null;return null;}
        }
        // Private to one receiving solve and one spreader receiver count;
        // loads, criteria, permissions and junction callbacks stay unchanged.
        private sealed class ReceivingPreparation
        {
            private readonly Func<TowerLayout,bool> layoutAcceptance,lowerAcceptance;
            private readonly Func<TowerLayout,Func<Geometry,double,bool>> receiverPreparation;
            private int exhaustedStations;
            internal ReceivingPreparation(TowerInput input)
            {layoutAcceptance=input.TransferLayoutAcceptance;lowerAcceptance=input.TransferLowerAcceptance;receiverPreparation=input.TransferReceiverPreparation;}
            // This preparation never leaves one OptimizeEngineer invocation and
            // one receiver count: its loads, stock scope and captured callback
            // inputs stay fixed while only the station ceiling grows. Keep the
            // callback identity guard explicit if preparation reuse is expanded.
            private bool SameCallbacks(TowerInput input)=>ReferenceEquals(layoutAcceptance,input.TransferLayoutAcceptance)
                && ReferenceEquals(lowerAcceptance,input.TransferLowerAcceptance) && ReferenceEquals(receiverPreparation,input.TransferReceiverPreparation);
            internal int ExhaustedStations(TowerInput input)=>SameCallbacks(input) ? exhaustedStations : 0;
            internal void Exhausted(TowerInput input)
            {
                if(SameCallbacks(input) && input.Trace?.BudgetReached!=true && input.NetworkSeedCompletion?.Found!=true)
                    exhaustedStations=Math.Max(exhaustedStations,input.EngineerStationLimit);
            }
            internal readonly Dictionary<ReceivingPairKey,CrossOption[]> Crosses=new Dictionary<ReceivingPairKey,CrossOption[]>();
            internal readonly Dictionary<ReceivingPairKey,RowGrid> Rows=new Dictionary<ReceivingPairKey,RowGrid>();
            internal readonly ResponseCache<PrimaryKey,PrimaryCheck> Primaries=new ResponseCache<PrimaryKey,PrimaryCheck>(4096);
            internal readonly LoadedBeamAnalysis.MatrixCache Matrix=new LoadedBeamAnalysis.MatrixCache();
        }
        private static TowerLayout OptimizeCoreByLegTier(TowerInput input,bool shareDiagnostics,ReceivingPreparation prepared=null)
        {
            var t = input.TransferredSlab; t.Validate(input); TowerLayout best = null; int tested = 0;
            var work=new List<LayoutWork>();
            var knownWinners=new Dictionary<double,KnownLayoutRank>();
            bool recordedSupportFloor=false;
            // A positioned primary plan fixes InitialLegs independently of the
            // geometries below. Its exhausted tiers can therefore be discarded
            // before grid repair and ranking metadata are allocated. Planless
            // callers still derive their initial tier from the original work.
            int exhaustedGeometry=input.EngineerSearch && input.EngineerPlan!=null ? Math.Max(prepared?.ExhaustedStations(input)??0,input.ReceiverFailedThroughStations) : 0;
            // This necessary check uses only the fixed positioned plan, geometry
            // and cover edge. It does not depend on repaired setting-out rows or
            // invoke receiving callbacks. Keep planless initial-tier inference
            // on its original complete worklist.
            bool screenWorkSpans=input.EngineerSearch && input.EngineerPlan!=null && input.DeferTransferLayout
                && input.PrepareReceiverChecks && input.PrepareSpanGeometry && input.ReuseSearchWork && input.DeferCrossingChecks
                && input.EngineerPlan.Network.CrossingBeams.Any(c=>(c.BayNumber==input.EngineerPlan.Beam || c.BayNumber==input.EngineerPlan.Beam+1)
                    && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows);
            var geometryCache = input.ReuseSearchWork ? t.SharedGeometryCache ?? new GeometryCache() : null;
            // A receiving-beam solve has fixed beam loads and upper-foot plans. These options
            // depend on transverse width and tributary size, not the primary stock cover.
            var crossCache = prepared?.Crosses ?? new Dictionary<ReceivingPairKey,CrossOption[]>();
            var rowCache = prepared?.Rows ?? new Dictionary<ReceivingPairKey,RowGrid>();
            // Different tower combinations often repeat an identical local primary member.
            // Include every support, distributed-load boundary and intensity, without rounding. This cache
            // lives only in one receiving-beam solve and retains at most 4096 checks.
            var primaryCache = input.ReuseSearchWork ? prepared?.Primaries ?? new ResponseCache<PrimaryKey,PrimaryCheck>(4096) : null;
            var shared = input.ReuseSearchWork && input.ReuseNetworkNumerics ? t.SharedSearchWork : null;
            var matrixCache = input.ReuseSearchWork ? shared?.Matrix ?? prepared?.Matrix ?? new LoadedBeamAnalysis.MatrixCache() : null;
            if(!shareDiagnostics || input.Diagnostics==null)input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator,trace:input.Trace,ignoreUplift:input.IgnoreUplift) {LazyLocations=input.LazySearchDiagnostics};var diag=input.Diagnostics;
            input.Trace?.Work("Transfer primary cover generation");
            double[][] covers;
            using(input.Trace?.Measure(BeamSearchStage.StockCovers))covers=Covers(input).ToArray();
            if(covers.Length==0)diag.Geometry(input.DiagnosticScope+"coverage",$"{input.DiagnosticScope}primary coverage: no available stock combination totals {input.LengthMm-2*input.MaximumConcreteEndOverhangMm:0.###}–{input.LengthMm:0.###} mm.","Review stock lengths and the concrete end allowance.");
            try
            {
                foreach (var cover in covers)
                {
                    input.Trace?.Increment(BeamSearchCounter.CoverageVariants);
                    if(cover.Length*4>input.NetworkLegCeiling)continue;
                    double covered = cover.Sum(), edge = input.CoverStart(covered);
                    double rowCoverage=t.FixedCoveredLengthMm??covered;
                    bool independent=t.IsNetwork && t.AllowLongerPrimaryCoverage && t.FixedCoveredLengthMm.HasValue;
                    if(t.FixedCoveredLengthMm.HasValue && (independent ? covered<rowCoverage-1e-7 : Math.Abs(covered-rowCoverage)>1e-7)) continue;
                    int count = (int)Math.Ceiling(rowCoverage/t.RowGapMm);
                    // Slab rows stay aligned in absolute coordinates across the network. Primary
                    // stock may extend farther toward the concrete ends on an individual beam.
                    double first = (rowCoverage-(count-1)*t.RowGapMm)/2+t.RowStart(input,rowCoverage)-edge;
                    foreach (bool rotated in new[] { false, true }) foreach (double width in rotated ? TowerOptimizer.BaysMm : new[] { 1524.0 })
                    {
                        if (t.FixedTowerWidthMm.HasValue && Math.Abs(width-t.FixedTowerWidthMm.Value)>1e-7) continue;
                        if (rotated && input.Exclude600Standalone && width == 600) continue;
                        input.Trace?.Work("Transfer slab and lower secondary options");
                        double tributary = t.UpperSpacingMm;
                        var crossKey = new ReceivingPairKey(width,tributary);
                        input.Trace?.CheckCancellation();
                        if (!input.ReuseSearchWork || !crossCache.TryGetValue(crossKey,out var crosses))
                        {
                            using(input.Trace?.Measure(BeamSearchStage.UpperOptions))
                                crosses = CrossOptions(input,width,tributary).ToArray();
                            if (input.ReuseSearchWork) crossCache[crossKey] = crosses;
                        }
                        else input.Trace?.Increment(BeamSearchCounter.CrossCacheHits);
                        if (crosses.Length == 0) continue;
                        // Check the receiving H20s before generating primary tower geometry.
                        // Sheet-controlled rows have one fixed phase, independent of the towers.
                        // Refining a failing crossing must not enumerate thousands of primaries first.
                        int minimumStations=0;
                        bool checkPreparedGeometry=false;
                        if(input.EngineerSearch && t.PlywoodSheetLengthMm>0)
                        {
                            var rowKey=new ReceivingPairKey(covered,0.0);
                            if(!rowCache.TryGetValue(rowKey,out var fixedGrid))
                            {
                                fixedGrid=BuildRowGrid(input,covered,rowCoverage,first,count,0,edge);
                                rowCache[rowKey]=fixedGrid;
                            }
                            if(fixedGrid==null)continue;
                            crosses=crosses.Where(c=>AcceptTransverseCases(input,c,width,fixedGrid,edge)).ToArray();
                            if(crosses.Length==0)continue;
                            // Joint setting-out retains this exact source analysis
                            // key and prepared check. Unknown external callbacks
                            // carry no bound; an OR detail uses its weaker branch.
                            if(input.PrepareReceiverChecks && input.ReuseSearchWork && input.ReuseTransversePreparations
                                && input.OptimizeCrossingSearch && input.DeferTransferLayout && input.DeferCrossingChecks
                                && input.FullLoadingOnly && input.EngineerPlan?.UseDesignZones==true
                                && input.Trace?.HasTimeLimit!=true && geometryCache?.Fixed==null)
                            {
                                // Resolved span forces and receiving-frame
                                // footprints constrain single-stock covers too.
                                checkPreparedGeometry=true;
                                minimumStations=crosses.Min(c=>PreparedReceiverSupportBounds.Minimum(c.PreparedReceiver,cover,edge));
                                int available=Math.Min(input.EngineerStationLimit,input.NetworkLegCeiling/2);
                                if(minimumStations>available)
                                {
                                    if(!recordedSupportFloor)
                                    {
                                        diag.Geometry(input.DiagnosticScope+"resolved support floor",
                                            $"{input.DiagnosticScope}this primary stock cover's resolved loads require at least {minimumStations} support stations per primary line; the current ceiling allows {available}.",
                                            "Review the station ceiling or select another primary stock arrangement.",minimumStations-available);
                                        recordedSupportFloor=true;
                                    }
                                    continue;
                                }
                            }
                        }
                        input.Trace?.Work("Transfer tower/support geometry");
                        var geometries=SearchGeometries(input,geometryCache,cover,rotated);
                        if(geometries.Length==0 && !diag.GeometryRepeated(input.DiagnosticScope+"tower geometry"))diag.Geometry(input.DiagnosticScope+"tower geometry",$"{input.DiagnosticScope}tower geometry: no searched D3 support pattern fits stocks {string.Join(", ",cover)} mm with {input.StockEndSetbackMm:0.###} mm end setbacks; exclude standalone 600 = {input.Exclude600Standalone}.","Review stock lengths, end setbacks and the standalone-600 setting.");
                        // Universal lower-load exclusion follows all original
                        // retention/dedup/site-normalization rules; it cannot
                        // refill a capped family or reorder surviving objects.
                        if(checkPreparedGeometry && input.TransferGeometryFilter!=null)
                            geometries=input.TransferGeometryFilter(input,geometries,edge);
                        int firstStations=Math.Max(Math.Max(minimumStations,input.EngineerMinimumStations),exhaustedGeometry+1);
                        geometries=CountRange(input,geometryCache,geometries,firstStations,Math.Min(input.EngineerStationLimit,input.NetworkLegCeiling/2));
                        var preparedPredicates=checkPreparedGeometry ? crosses.Select(c=>PreparedReceiverSupportBounds.Predicate(c.PreparedReceiver)).ToArray() : null;
                        foreach (var geometry in geometries)
                        {
                            if(geometry.Positions.Length<minimumStations)continue;
                            if(geometry.Positions.Length*2>input.NetworkLegCeiling || input.EngineerSearch && (geometry.Positions.Length>input.EngineerStationLimit || geometry.Positions.Length<input.EngineerMinimumStations))continue;
                            if(geometry.Positions.Length<=exhaustedGeometry)continue;
                            if(checkPreparedGeometry)
                            {
                                bool mayFit=false;
                                foreach(var predicate in preparedPredicates)
                                    if(predicate(geometry,edge)){mayFit=true;break;}
                                if(!mayFit){input.Trace?.Increment(BeamSearchCounter.PreparedGeometryBoundsRejected);continue;}
                            }
                            int firstSpansPassing=-1;
                            if(screenWorkSpans)
                            {
                                input.Trace?.CheckCancellation();
                                firstSpansPassing=FirstPrimarySpansPassing(input,geometry,crosses,edge);
                                if(firstSpansPassing<0)continue;
                            }
                            // Try centred rows and phase shifts. Independent primary coverage adds beam load
                            // at the end rows without increasing the shared slab tributaries.
                            foreach (double phase in t.PlywoodSheetLengthMm>0 ? new[] {0.0} : t.FixedRowPhaseMm.HasValue ? new[] {t.FixedRowPhaseMm.Value} : new[] { 0.0, -50, 50, -100, 100 })
                            {
                                var rowKey = new ReceivingPairKey(covered,phase);
                                if (!rowCache.TryGetValue(rowKey,out var grid))
                                {
                                    grid=BuildRowGrid(input,covered,rowCoverage,first,count,phase,edge);
                                    rowCache[rowKey]=grid;
                                }
                                if(grid==null)continue;
                                var rows = grid.Rows;
                                if (rows[0] <= 0 || rows.Last() >= covered) continue;
                                var tributaries = grid.Tributaries;
                                // Shared slab tributaries were checked by the network solver. A longer
                                // primary cover puts additional beam concrete onto the end rows; the
                                // resulting larger beam tributaries are checked at their actual loads.
                                if (!independent && !t.IncludeConcreteEndStrips && !t.IncludeMainBeamEndLoads && tributaries.Max() > t.UpperSpacingMm+1e-7) continue;
                                // A transverse row must bear on both longitudinal stocks, away from independent member breaks.
                                grid=ResolveStockGrid(input,geometry,grid,edge,out double conflicting);
                                if(conflicting>0 && grid==null) {double joint=geometry.Stock.Skip(1).OrderBy(s=>Math.Abs(s.StartMm-conflicting)).First().StartMm;if(!diag.GeometryRepeated(input.DiagnosticScope+"row/joint",40-Math.Abs(joint-conflicting)))diag.Geometry(input.DiagnosticScope+"row/joint",$"{input.DiagnosticScope}support row x={conflicting+edge:0.###} mm is {Math.Abs(joint-conflicting):0.###} mm from primary joint x={joint+edge:0.###} mm; minimum 40 mm.","Review upper row spacing and the primary stock combination.",40-Math.Abs(joint-conflicting));continue;}
                                var rank=RankGeometry(geometry,width,rotated,input.LongitudinalStartMm+input.LengthMm/2-edge,input.EngineerSearch,input.ReuseSearchWork);
                                work.Add(new LayoutWork {Geometry=geometry,Width=width,Rotated=rotated,Crosses=crosses,Grid=grid,Edge=edge,
                                    Rank=rank,FirstSpansPassing=firstSpansPassing,JoinedBays=rank.Joined,Brace=rank.Brace,Narrow=rank.Narrow,Cantilever=rank.Cantilever,Symmetry=rank.Symmetry});
                            }
                        }
                    }
                }
                // Stable order within a leg tier keeps all original timber/utilization ties.
                // Economic bounds, rather than a leg-only cutoff, retain valid timber/support trade-offs.
                int initial=input.EngineerSearch && work.Count>0 ? input.EngineerPlan?.InitialLegs??work.GroupBy(w=>Tuple.Create(w.Crosses,w.Edge)).Min(group=>group.First().Crosses.Min(c=>InitialLegs(input,c,group.First().Edge,group.First().Geometry.Stock))) : 2;
                // A completed failed ceiling already checked every earlier
                // geometry, including its receiving callbacks. Growing only the
                // ceiling cannot make that exact rejected candidate pass. Keep
                // the original initial tier above, then sort only new work.
                int exhausted=input.EngineerSearch ? Math.Max(prepared?.ExhaustedStations(input)??0,input.EngineerPlan==null ? 0 : input.ReceiverFailedThroughStations) : 0;
                var pending=exhausted==0 ? (IEnumerable<LayoutWork>)work : work.Where(w=>w.Geometry.Positions.Length>exhausted);
                foreach(var item in input.EngineerSearch ? pending.OrderBy(w=>EngineerTowerSearch.TierOrder(w.Geometry.Positions.Length,initial)).ThenBy(w=>w.JoinedBays).ThenBy(w=>w.Brace).ThenBy(w=>w.Cantilever) : pending.OrderBy(w=>w.Geometry.Positions.Length))
                {
                    input.Trace?.CheckCancellation();
                    var geometry=item.Geometry;var grid=item.Grid;
                    TowerLayout widthBest=best;
                    if(input.ReceiverAlternatives!=null)input.ReceiverAlternatives.TryGetValue(item.Width,out widthBest);
                    if(geometry.Positions.Length*2>input.NetworkLegCeiling || input.EngineerSearch && widthBest!=null && geometry.Positions.Length>widthBest.PositionsMm.Length)continue;
                    KnownLayoutRank knownBest=input.EngineerSearch && widthBest!=null ? KnownWinner(knownWinners,item.Width,widthBest) : null;
                    if(input.EngineerSearch && widthBest!=null && geometry.Positions.Length==widthBest.PositionsMm.Length)
                    {
                        int bays=item.JoinedBays;
                        if(bays>knownBest.Joined || bays==knownBest.Joined && item.Brace>knownBest.Brace+1e-7)continue;
                    }
                    double geometryCantilever=item.Cantilever;
                    double geometrySymmetry=input.EngineerSearch ? item.Symmetry+item.Cantilever : 0;
                    for(int crossIndex=Math.Max(0,item.FirstSpansPassing);crossIndex<item.Crosses.Length;crossIndex++)
                    {
                        var cross=item.Crosses[crossIndex];
                        if(input.EngineerSearch && widthBest!=null && (knownBest==null || !ReferenceEquals(knownBest.Layout,widthBest)))knownBest=KnownWinner(knownWinners,item.Width,widthBest);
                        if(input.EngineerSearch && input.PruneUneconomicCandidates && widthBest!=null && geometry.Positions.Length==widthBest.PositionsMm.Length
                            && EngineerTowerSearch.Compare(item.Narrow,item.JoinedBays,item.Brace,knownBest.Narrow,knownBest.Joined,knownBest.Brace)==0
                            && (geometryCantilever>knownBest.Cantilever+1e-7 || Math.Abs(geometryCantilever-knownBest.Cantilever)<1e-7 && geometrySymmetry+Math.Abs(2*cross.Start+cross.Stock)>knownBest.Symmetry+1e-7))
                        {
                            // This exact geometric lower bound already ranks worse.
                            // Capacity checks cannot improve fixed stock/support symmetry;
                            // upper-member imbalance can only add to the lower bound.
                            continue;
                        }
                        if(input.EngineerSearch && input.PruneUneconomicCandidates && input.PrepareReceiverChecks && widthBest!=null && geometry.Positions.Length==widthBest.PositionsMm.Length)
                        {
                            var winner=knownBest;
                            var basis=item.Rank??RankGeometry(geometry,item.Width,item.Rotated,input.LongitudinalStartMm+input.LengthMm/2-item.Edge,true,input.ReuseSearchWork);
                            var final=FinalRankGeometry(geometry,basis,item.Edge,input.LongitudinalStartMm+input.LengthMm/2,input.ReuseSearchWork);
                            int order=CompareKnownRanks(final,FinalSecondarySymmetry(cross,item.Width),winner);
                            bool rejected=order>0;
                            if(order==0){double cost=FinalEconomy(final,cross,grid,input);rejected=cost>winner.Cost && Math.Abs(cost-winner.Cost)>.01;}
                            if(rejected)
                            {input.Trace?.Increment(BeamSearchCounter.FinalRankPruned);continue;}
                        }
                        input.Trace?.Increment(BeamSearchCounter.TransferCandidates);tested++;
                        if(!input.EngineerSearch && input.PruneUneconomicCandidates && widthBest!=null)
                        {
                            double score=Economy(geometry,cross,grid,input);
                            if(score>Economy(best)+.01) {input.Trace?.Increment(BeamSearchCounter.EconomyPruned);continue;}
                        }
                        var candidate=OffsetReceiver(input,Evaluate(input,geometry,item.Width,item.Rotated,cross,grid,item.Edge,primaryCache,matrixCache,shared,widthBest,crossIndex==item.FirstSpansPassing));
                        if(candidate!=null && input.EngineerSearch){candidate.SearchAssumedLegKn=EngineerTowerSearch.AssumedLegKn(input);candidate.SearchInitialLegsPerLine=InitialLegs(input,cross,item.Edge,geometry.Stock);}
                        if(candidate!=null && (widthBest==null || Better(candidate,widthBest)) && (input.TransferLayoutAcceptance==null || AcceptLayout(input,candidate)))
                        {
                            widthBest=candidate;
                            if(input.ReceiverAlternatives!=null)input.ReceiverAlternatives[item.Width]=candidate;
                            if(best==null || Better(candidate,best))best=candidate;
                        }
                    }
                }
            }
            catch(BeamSearchDeadlineException) when(best!=null) {best.SearchTimeLimitReached=true;}
            if (best == null)
            {
                // Cancellation/deadlines do not prove exhaustion, even if they
                // arrive immediately after the last candidate was checked.
                input.Trace?.CheckCancellation();
                if(input.EngineerSearch)prepared?.Exhausted(input);
                throw diag.Exception("No valid transferred-slab layout found within the bounded search.");
            }
            best.SearchTimeLimitReached=input.Trace?.BudgetReached==true;
            best.Tested = tested; return best;
        }
        internal static bool Better(TowerLayout a,TowerLayout b)
            => a.EngineerSearch ? EngineerBetter(a,b) : Economy(a)<Economy(b)-.01 || Math.Abs(Economy(a)-Economy(b))<=.01 && (TowerOptimizer.CompareBracePreference(a,b)<0
                || TowerOptimizer.CompareBracePreference(a,b)==0 && (a.SymmetryErrorMm<b.SymmetryErrorMm-1e-7 || Math.Abs(a.SymmetryErrorMm-b.SymmetryErrorMm)<1e-7 && a.Utilization<b.Utilization));
        private static bool EngineerBetter(TowerLayout a,TowerLayout b)
        {
            if(a.LegCount!=b.LegCount)return a.LegCount<b.LegCount;
            int preference=TowerOptimizer.CompareBracePreference(a,b);if(preference!=0)return preference<0;
            if(Math.Abs(a.CantileverImbalanceMm-b.CantileverImbalanceMm)>1e-7)return a.CantileverImbalanceMm<b.CantileverImbalanceMm;
            if(Math.Abs(a.SymmetryErrorMm-b.SymmetryErrorMm)>1e-7)return a.SymmetryErrorMm<b.SymmetryErrorMm;
            if(Math.Abs(a.BraceLocationPenalty-b.BraceLocationPenalty)>1e-7)return a.BraceLocationPenalty<b.BraceLocationPenalty;
            return Economy(a)<Economy(b)-.01 || Math.Abs(Economy(a)-Economy(b))<=.01 && (a.SymmetryErrorMm<b.SymmetryErrorMm-1e-7 || Math.Abs(a.SymmetryErrorMm-b.SymmetryErrorMm)<1e-7 && a.Utilization<b.Utilization);
        }
        private static int InitialLegs(TowerInput input,CrossOption cross,double edge,TowerStockPiece[] stocks)
        {
            double force=0;
            foreach(int c in cross.DesignCases)for(int line=0;line<2;line++)
            {
                double carried=0;
                foreach(var stock in stocks)
                {
                    var loads=PrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,c,edge);
                    if(loads!=null)carried+=loads.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000);
                }
                force=Math.Max(force,carried*input.LengthMm/stocks.Sum(s=>s.StockLengthMm));
            }
            return input.EngineerPlan?.InitialLegs??EngineerTowerSearch.InitialLegs(input,force);
        }
        private static double Timber(TowerLayout l) => l.StockPlan.PrimaryLengthMm+l.Transfer.AdditionalTimberMm;
        private static double FinalSecondarySymmetry(CrossOption cross,double width)
        {
            if(cross.FinalSymmetryPrepared && cross.FinalSymmetryWidth==width)return cross.FinalSecondarySymmetry;
            double result=Math.Abs((-width/2-cross.Start)-(cross.Stock-(width/2-cross.Start)));
            foreach(var upper in new[] {cross.Left,cross.Right})if(upper!=null)
                result=Math.Max(result,Math.Abs((upper.Foot-upper.Edge)-(upper.Stock-(upper.Bay-upper.OtherFoot-upper.Edge))));
            cross.FinalSecondarySymmetry=result;cross.FinalSymmetryWidth=width;cross.FinalSymmetryPrepared=true;return result;
        }
        private static double FinalEconomy(FinalGeometryRank geometry,CrossOption cross,RowGrid grid,TowerInput input)
        {
            var quantities=Volatile.Read(ref grid.Quantities);
            if(quantities==null){quantities=new RowQuantities(grid);Volatile.Write(ref grid.Quantities,quantities);}
            int feet=2*((cross.Left==null ? 0 : quantities.Left)+(cross.Right==null ? 0 : quantities.Right));
            double primary=geometry.StockLength*2*input.Primary.CountPerLine;
            double additional=quantities.Physical*cross.Stock+quantities.Left*(cross.Left?.Stock??0)+quantities.Right*(cross.Right?.Stock??0);
            return geometry.Stations*2*TowerOptimizer.TowerLegTimberEquivalentMm+feet*TowerOptimizer.UpperFootTimberEquivalentMm+(primary+additional);
        }
        internal static double Economy(TowerLayout l)=>l.LegCount*TowerOptimizer.TowerLegTimberEquivalentMm+l.Transfer.UpperFootCount*TowerOptimizer.UpperFootTimberEquivalentMm+Timber(l);
        private static double Economy(Geometry g,CrossOption cross,RowGrid grid,TowerInput input)
        {
            int left=cross.Left==null ? 0 : grid.LeftScales.Count(x=>x>0),right=cross.Right==null ? 0 : grid.RightScales.Count(x=>x>0);
            return g.Positions.Length*2*TowerOptimizer.TowerLegTimberEquivalentMm+(left+right)*2*TowerOptimizer.UpperFootTimberEquivalentMm
                +g.Stock.Sum(s=>s.StockLengthMm)*2*input.Primary.CountPerLine+(grid.SettingOutRows?.Length??grid.Rows.Length)*cross.Stock+left*(cross.Left?.Stock??0)+right*(cross.Right?.Stock??0);
        }
        internal static double[] CoveredLengths(TowerInput input) => Covers(input).Select(c=>c.Sum()).Distinct().ToArray();
        // Rebuild the entire row/load model at the new pitch, evaluating only
        // the previous support/stock geometry. Failure returns to the normal search.
        internal static TowerLayout TryFixedLayout(TowerInput input,TowerLayout previous)
        {
            var t=input.TransferredSlab;t.Validate(input);
            if(previous==null || previous.PrimaryMaterial!=input.PrimaryMaterial || previous.LengthMm!=input.LengthMm || previous.LongitudinalStartMm!=input.LongitudinalStartMm
                || previous.LegCount>input.NetworkLegCeiling || previous.PositionsMm.Length<input.EngineerMinimumStations)return null;
            int previousCount=t.EngineerReceiverCount;
            try
            {
            t.EngineerReceiverCount=previous.Transfer.EngineerReceiverCount;
            var lengths=previous.StockPlan.Pieces.Select(p=>p.StockLengthMm).ToArray();
            if(!Covers(input).Any(c=>c.SequenceEqual(lengths)))return null;
            double covered=lengths.Sum(),edge=input.CoverStart(covered),rowCoverage=t.FixedCoveredLengthMm??covered;
            if(t.FixedCoveredLengthMm.HasValue && (t.AllowLongerPrimaryCoverage ? covered<rowCoverage-1e-7 : Math.Abs(covered-rowCoverage)>1e-7))return null;
            var positions=previous.PositionsMm.Select(x=>x-edge).ToArray();
            var stocks=previous.StockPlan.Pieces.Select(p=>new TowerStockPiece {StartMm=p.StartMm-edge,StockLengthMm=p.StockLengthMm,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport}).ToArray();
            var geometry=new Geometry {Positions=positions,Joined=previous.Joined,Stock=stocks,
                MemberSupports=stocks.Select(p=>positions.Skip(p.FirstSupport).Take(p.LastSupport-p.FirstSupport+1).Select(x=>x-p.StartMm).ToArray()).ToArray()};
            int count=(int)Math.Ceiling(rowCoverage/t.RowGapMm);
            double first=(rowCoverage-(count-1)*t.RowGapMm)/2+t.RowStart(input,rowCoverage)-edge;
            var grid=BuildRowGrid(input,covered,rowCoverage,first,count,t.FixedRowPhaseMm??0,edge);
            grid=GridForStocks(input,geometry,grid,edge);
            if(grid==null)return null;
            var shared=input.ReuseSearchWork ? t.SharedSearchWork : null;
            foreach(var cross in CrossOptions(input,previous.WidthMm,t.UpperSpacingMm))
            {
                if(cross.Stock!=previous.Transfer.LowerStockLengthMm || cross.Start!=previous.Transfer.LowerStockStartMm-input.ReceiverTransverseOffsetMm)continue;
                var result=OffsetReceiver(input,Evaluate(input,geometry,previous.WidthMm,previous.Rotated,cross,grid,edge,null,shared?.Matrix,shared,null));
                if(result==null || input.TransferLayoutAcceptance!=null && !AcceptLayout(input,result))continue;
                result.MinimumLegCountProven=false;result.SearchAssumedLegKn=previous.SearchAssumedLegKn;result.SearchInitialLegsPerLine=previous.SearchInitialLegsPerLine;
                return result;
            }
            return null;
            }
            finally{t.EngineerReceiverCount=previousCount;}
        }
        private static TowerLayout OffsetReceiver(TowerInput input,TowerLayout layout)
        {
            double offset=input.ReceiverTransverseOffsetMm;
            if(layout==null || offset==0)return layout;
            layout.TransverseOffsetMm=offset;layout.Transfer=layout.Transfer.Copy();
            layout.Transfer.LowerStockStartMm+=offset;
            // LowerCase members are cached across geometries; preserve that cache and
            // express only the returned members' stock origins relative to the concrete.
            layout.Transfer.LowerCases=layout.Transfer.LowerCases.Select(stage=>stage.Select(m=>new TransferMember {Name=m.Name,LoadCase=m.LoadCase,H20Count=m.H20Count,StartMm=m.StartMm+offset,LengthMm=m.LengthMm,SupportsMm=m.SupportsMm,Uniform=m.Uniform,Points=m.Points,Analysis=m.Analysis}).ToArray()).ToArray();
            return layout;
        }
        internal static double[][] StockCovers(TowerInput input) => Covers(input).ToArray();
        private static IEnumerable<double[]> Covers(TowerInput input)
        {
            if(input.TransferredSlab?.StockCovers!=null)return input.TransferredSlab.StockCovers;
            int max=(int)Math.Floor(input.LengthMm), min=(int)Math.Ceiling(input.LengthMm-2*input.MaximumConcreteEndOverhangMm);
            var states = new Dictionary<int,List<double[]>> { { 0,new List<double[]> { new double[0] } } };
            var stocks=input.PrimaryStockLengthsMm.OrderByDescending(x=>x).ToArray();
            int step=0;
            foreach(double stock in stocks) {int b=(int)stock;while(b!=0) {int remainder=step%b;step=b;b=remainder;}}
            for (int total=step;total<=max;total+=step)
            {
                input.Trace?.CheckCancellation();
                foreach (double stock in stocks)
                if (stock<=total && states.TryGetValue(total-(int)stock,out var previous))
                {
                    if (!states.TryGetValue(total,out var list)) states[total]=list=new List<double[]>();
                    list.AddRange(previous.Where(x=>x.Length<10).Select(x=>x.Concat(new[] {stock}).ToArray()));
                    states[total]=list.OrderBy(x=>x.Length).ThenByDescending(x=>x[0]).Take(3).ToList();
                }
            }
            var original = states.Where(x=>x.Key>=min && x.Key<=max).OrderBy(x=>x.Value.Min(y=>y.Length)).ThenBy(x=>x.Key).SelectMany(x=>x.Value).Take(18).ToArray();
            // Protect centre-first and mirrored stock families independently of the short-cover DP cap.
            // A third stock can save a frame through a linked centre group.
            var balanced=new List<double[]>();
            foreach(double outer in input.PrimaryStockLengthsMm.OrderBy(x=>x))
            for(int pairs=1;pairs<=4;pairs++)
            foreach(double centre in new[] {0.0}.Concat(input.PrimaryStockLengthsMm.OrderBy(x=>x)))
            {
                double total=2*pairs*outer+centre;if(total<min || total>max)continue;
                balanced.Add(Enumerable.Repeat(outer,pairs).Concat(centre==0 ? new double[0] : new[] {centre}).Concat(Enumerable.Repeat(outer,pairs)).ToArray());
            }
            var reordered=original.Where(c=>c.Length>2).SelectMany(c=>new[] {c.Reverse().ToArray(),c.OrderBy(x=>x).Take(c.Length/2).Concat(c.OrderBy(x=>x).Skip(c.Length/2).Reverse()).ToArray()});
            var catalogue=original.Concat(balanced.OrderBy(c=>c.Length).ThenBy(c=>c.Sum()).Take(36)).Concat(reordered)
                .GroupBy(c=>Values(c)).Select(g=>g.First()).ToArray();
            if(!input.AllowCustomPrimaryLengths) {input.Trace?.Increment(BeamSearchCounter.CoveragePlans,catalogue.Length);return catalogue;}
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
            var covers=catalogue.Concat(custom).GroupBy(x=>string.Join(";",x)).Select(g=>g.First()).OrderBy(x=>x.Length).ThenBy(x=>x.Sum()).ToArray();
            input.Trace?.Increment(BeamSearchCounter.CoveragePlans,covers.Length);return covers;
        }
        internal static Geometry[] SearchGeometries(TowerInput input,GeometryCache cache,double[] cover,bool rotated)
        {
            input.Trace?.CheckCancellation();
            Func<int,Geometry[]> build=after=>
            {
                Geometry[] generated;
                using(input.Trace?.Measure(BeamSearchStage.Geometry))generated=Geometries(cover,rotated,input,after,cache).ToArray();
                input.Trace?.Increment(BeamSearchCounter.GeometryBuilds);return generated;
            };
            if(cache==null)return build(0);
            bool hit;Geometry[] result;
            if(input.EngineerSearch)
            {
                // Generator settings stay exact; loads and response criteria do not
                // affect these stock/support patterns. Work ordering later sorts by
                // station tier, preserving the original order within each tier.
                bool stockRuns=!rotated && cover.Length<=3 && input.EngineerPlan?.HasCrossings==true;
                string key=System.FormattableString.Invariant($"{(stockRuns ? input.EngineerPlan.InitialLegs : 0)}|{stockRuns}|{input.StockEndSetbackMm:R}|{input.Exclude600Standalone}|{input.TransferredSlab?.IsNetwork??false}|{input.PrimaryMaterial}|")
                    +Values(input.PrimaryStockLengthsMm)+"|"+(rotated ? "R:" : "N:")+Values(cover);
                result=cache.GetEngineer(key,input.EngineerStationLimit,build,out hit,out int reused);
                if(reused>0)input.Trace?.Increment(BeamSearchCounter.ExtendedGeometryPatternsReused,reused);
            }
            else result=cache.Get((rotated ? "R:" : "N:")+Values(cover),()=>build(0),out hit);
            if(hit)input.Trace?.Increment(BeamSearchCounter.GeometryCacheHits);return result;
        }
        internal static Geometry[] CountRange(TowerInput input,GeometryCache cache,Geometry[] source,int minimum,int maximum)
        {
            // Planless searches infer their initial tier from the full worklist.
            // Fixed and timed searches retain their original traversal as well.
            if(!input.EngineerSearch || input.EngineerPlan==null || cache==null || cache.Fixed!=null || input.Trace?.HasTimeLimit==true)return source;
            return cache.GetCountRange(source,minimum,maximum,input.Trace,out _);
        }
        private static IEnumerable<Geometry> Geometries(double[] stock,bool rotated,TowerInput input,int afterStations=0,GeometryCache cache=null)
        {
            var states=new List<Geometry> {new Geometry {Stock=new TowerStockPiece[0],Positions=new double[0],Joined=new bool[0]}};
            for(int run=0;run<stock.Length;run++)
            {
                input.Trace?.CheckCancellation();double length=stock[run];
                if(cache!=null && run<stock.Length-1)
                {
                    // Regular intermediate states depend only on this exact
                    // stock prefix and its generation settings, not on any
                    // later member, load, primary plan or completed station tier.
                    string key=System.FormattableString.Invariant($"{input.EngineerSearch}|{input.EngineerStationLimit}|{input.StockEndSetbackMm:R}|{input.Exclude600Standalone}|{rotated}|")+Values(stock.Take(run+1).ToArray());
                    states=cache.GetRegularPrefix(key,()=>
                    {
                        var generated=RegularGeometryStep(states,length,rotated,input).ToArray();
                        input.Trace?.CheckCancellation();return generated;
                    },out _).ToList();
                }
                else states=RegularGeometryStep(states,length,rotated,input);
            }
            // Keep fully mirrored regular groups even when the inset-based beam-search cap drops them.
            var symmetric=SymmetricGeometries(stock,rotated,input).GroupBy(g=>g.Positions.Length).SelectMany(g=>g.Take(24));
            if(input.EngineerSearch)states=(rotated ? Enumerable.Empty<Geometry>() : EngineerTowerSearch.StockRunGeometries(stock,input,afterStations)).Concat(EngineerTowerSearch.Geometries(stock,rotated,input,afterStations)).Concat(states.Where(g=>g.Positions.Length<=input.EngineerStationLimit && g.Positions.Length>afterStations)).ToList();
            states=states.Concat(symmetric).Where(g=>g.Positions.Length>afterStations).GroupBy(g=>new GeometryKey(g.Positions,g.Joined)).Select(g=>g.First()).ToList();
            if(input.TransferredSlab?.IsNetwork==true)states=states.SelectMany(g=>SiteGeometries(g,input.StockEndSetbackMm)).GroupBy(g=>new GeometryKey(g.Positions,g.Joined)).Select(g=>g.First()).ToList();
            // Engineer generators already register exact stored-coordinate
            // slices. Register regular/site objects without materializing them.
            foreach(var geometry in states)if(!geometry.HasSupportMetadata)geometry.PrepareGeneratedSupports(input.Trace);
            return states;
        }
        private static List<Geometry> RegularGeometryStep(List<Geometry> states,double length,bool rotated,TowerInput input)
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
                input.Trace?.CheckCancellation();
                double start=state.Stock.Sum(x=>x.StockLengthMm), inset=(length-bays.Sum())/2; int first=state.Positions.Length;
                var p=new List<double> {start+inset}; foreach(double bay in bays) p.Add(p.Last()+bay);
                bool keep=p.Count%2!=0 || !rotated && bays.All(b=>TowerOptimizer.BaysMm.Contains(b)) && bays.Any(b=>b==600);
                var joins=state.Joined.Concat(first==0 ? new bool[0] : new[] {false}).Concat(Enumerable.Range(1,p.Count-1).Select(i=>keep || i%2==1)).ToArray();
                var positions=state.Positions.Concat(p).ToArray();
                if(input.EngineerSearch && positions.Length>input.EngineerStationLimit)continue;
                joins=TowerOptimizer.RemoveRedundantJoins(joins,positions,input.Exclude600Standalone);
                if(joins==null)continue;
                next.Add(new Geometry { Stock=state.Stock.Concat(new[] {new TowerStockPiece {StartMm=start,StockLengthMm=length,FirstSupport=first,LastSupport=first+p.Count-1}}).ToArray(),Positions=positions,Joined=joins });
            }
            return input.EngineerSearch
                ? next.GroupBy(x=>x.Positions.Length).SelectMany(g=>g.OrderBy(x=>x.Joined.Count(j=>j)).ThenBy(x=>EngineerTowerSearch.BracePenalty(x.Positions,x.Joined,rotated ? 1500 : 1524,rotated)).ThenBy(x=>x.Stock.Sum(s=>x.Positions[s.FirstSupport]-s.StartMm)).Take(24)).ToList()
                : next.GroupBy(x=>x.Positions.Length).SelectMany(g=>g.OrderBy(x=>x.Stock.Sum(s=>x.Positions[s.FirstSupport]-s.StartMm)).Take(12)).OrderBy(x=>x.Positions.Length).Take(84).ToList();
        }
        // Set-out gaps between independent towers use 25 mm increments. Translate
        // each complete group; never stretch catalogue frames or linked brace bays.
        internal static IEnumerable<Geometry> SiteGeometries(Geometry source,double setback)
        {
            var groups=new List<Tuple<int,int,double,double>>();
            for(int first=0;first<source.Positions.Length;)
            {
                int last=first;while(last<source.Joined.Length && source.Joined[last])last++;
                double minimum=double.NegativeInfinity,maximum=double.PositiveInfinity;
                for(int leg=first;leg<=last;leg++)
                {
                    var stock=source.Stock.Single(p=>leg>=p.FirstSupport && leg<=p.LastSupport);
                    minimum=Math.Max(minimum,stock.StartMm+setback-source.Positions[leg]);
                    maximum=Math.Min(maximum,stock.EndMm-setback-source.Positions[leg]);
                }
                groups.Add(Tuple.Create(first,last,minimum,maximum));first=last+1;
            }
            if(Enumerable.Range(0,source.Joined.Length).Where(i=>!source.Joined[i]).All(i=>Math.Abs((source.Positions[i+1]-source.Positions[i])/25-Math.Round((source.Positions[i+1]-source.Positions[i])/25))<1e-8))
            {yield return source;yield break;}
            if(source.Stock.All(p=>Math.Abs((source.Positions[p.FirstSupport]-p.StartMm)-(p.EndMm-source.Positions[p.LastSupport]))<1e-7))yield return source;
            foreach(int anchor in new[] {groups.Count/2,0,groups.Count-1}.Distinct())
            {
                var positions=(double[])source.Positions.Clone();bool valid=true;
                foreach(int i in Enumerable.Range(anchor+1,groups.Count-anchor-1).Concat(Enumerable.Range(0,anchor).Reverse()))
                {
                    var group=groups[i];bool right=i>anchor;
                    int leg=right ? group.Item1 : group.Item2;
                    double reference=right ? positions[groups[i-1].Item2] : positions[groups[i+1].Item1];
                    double low=Math.Ceiling((source.Positions[leg]+group.Item3-reference)/25-1e-9);
                    double high=Math.Floor((source.Positions[leg]+group.Item4-reference)/25+1e-9);
                    if(right)low=Math.Max(low,1);else high=Math.Min(high,-1);
                    if(low>high){valid=false;break;}
                    double shift=reference+25*Math.Max(low,Math.Min(high,Math.Round((source.Positions[leg]-reference)/25)))-source.Positions[leg];
                    for(int j=group.Item1;j<=group.Item2;j++)positions[j]+=shift;
                }
                if(valid)yield return new Geometry {Stock=source.Stock,Positions=positions,Joined=source.Joined};
            }
        }
        internal static GeometryCache FixedGeometry(TowerLayout layout)
        {
            double origin=layout.StockPlan.Pieces[0].StartMm;
            var geometry=new Geometry {Stock=layout.StockPlan.Pieces.Select(p=>new TowerStockPiece {StartMm=p.StartMm-origin,StockLengthMm=p.StockLengthMm,IsCustomLength=p.IsCustomLength,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport}).ToArray(),Positions=layout.PositionsMm.Select(x=>x-origin).ToArray(),Joined=layout.Joined};
            geometry.MemberSupports=geometry.Stock.Select(p=>geometry.Positions.Skip(p.FirstSupport).Take(p.LastSupport-p.FirstSupport+1).Select(x=>x-p.StartMm).ToArray()).ToArray();
            var cache=new GeometryCache {Fixed=new[] {geometry}};
            return cache;
        }
        private static IEnumerable<Geometry> SymmetricGeometries(double[] stock,bool rotated,TowerInput input)
        {
            if(!stock.SequenceEqual(stock.Reverse()) || stock.Length>5)yield break;
            var choices=stock.Take((stock.Length+1)/2).Select(length=>(rotated ? new[] {1524.0} : TowerOptimizer.BaysMm)
                .SelectMany(bay=>Enumerable.Range(2,rotated ? 1 : 5).Where(n=>(n-1)*bay<=length-2*input.StockEndSetbackMm && !(input.Exclude600Standalone && n==2 && bay==600))
                .Select(n=>Enumerable.Repeat(bay,n-1).ToArray())).ToArray()).ToArray();
            var patterns=new double[stock.Length][];
            foreach(var geometry in SymmetricGroups(stock,choices,patterns,0,input))yield return geometry;
        }
        private static IEnumerable<Geometry> SymmetricGroups(double[] stock,double[][][] choices,double[][] patterns,int index,TowerInput input)
        {
            input.Trace?.CheckCancellation();
            if(index<choices.Length)
            {
                foreach(var pattern in choices[index])
                {patterns[index]=pattern;patterns[stock.Length-1-index]=pattern;foreach(var g in SymmetricGroups(stock,choices,patterns,index+1,input))yield return g;}
                yield break;
            }
            var positions=new List<double>();var joins=new List<bool>();var pieces=new List<TowerStockPiece>();double start=0;
            for(int i=0;i<stock.Length;i++)
            {
                int first=positions.Count;double p=start+(stock[i]-patterns[i].Sum())/2;
                if(first>0)joins.Add(false);positions.Add(p);
                foreach(double bay in patterns[i]){p+=bay;positions.Add(p);joins.Add(true);}
                pieces.Add(new TowerStockPiece {StartMm=start,StockLengthMm=stock[i],FirstSupport=first,LastSupport=positions.Count-1});start+=stock[i];
            }
            var joined=TowerOptimizer.RemoveRedundantJoins(joins.ToArray(),positions.ToArray(),input.Exclude600Standalone);
            if(joined!=null)yield return new Geometry {Stock=pieces.ToArray(),Positions=positions.ToArray(),Joined=joined};
        }
        private static IEnumerable<Upper> UpperOptions(double bay,double load,double width,TowerInput input,double tributaryMm,UpperTransferPlan fixedPlan,double maximumEndStripMm)
        {
            if (bay==0) return new[] { (Upper)null };
            var result=new List<Upper>();
            if(fixedPlan!=null)
            {
                fixedPlan.Validate(bay,input.StockLengthsMm,maximumEndStripMm,input.AllowCustomSlabLengths);
                if(!input.PermitsSlabLength(fixedPlan.StockLengthMm))throw new ArgumentException("The selected custom-length scope requires catalogue slab H20s.");
                var a=AnalyzeTransverse(input,BeamSearchCounter.UpperAnalyses,fixedPlan.StockLengthMm,new[] {fixedPlan.NearFootMm-fixedPlan.EdgeMm,bay-fixedPlan.FarFootMm-fixedPlan.EdgeMm},new[] {new BeamDistributedLoad {StartMm=0,EndMm=fixedPlan.StockLengthMm,KnPerM=load}},null,TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                if(Pass(input,a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Slab H20",1,()=> $"stock {fixedPlan.StockLengthMm:0} mm · feet {fixedPlan.NearFootMm:0.###}/{fixedPlan.FarFootMm:0.###} mm from faces · base tributary {tributaryMm:0.###} mm")) result.Add(new Upper {Stock=fixedPlan.StockLengthMm,Custom=fixedPlan.IsCustomLength,Edge=fixedPlan.EdgeMm,Foot=fixedPlan.NearFootMm,OtherFoot=fixedPlan.FarFootMm,Bay=bay,Load=load,Analysis=a});
                return result;
            }
            var lengths=UpperTransferPlan.SlabLengths(bay,input.StockLengthsMm,maximumEndStripMm,input.AllowCustomSlabLengths).Where(input.PermitsSlabLength).ToArray();
            foreach(double stock in lengths)
            {
                double edge=(bay-stock)/2; if (edge<200 || edge>maximumEndStripMm) continue;
                double min=Math.Max(250,edge+150), max=bay/2-150;
                var feet=new[] {min,max,Math.Max(min,Math.Min(max,(width-input.TransferredSlab.BeamWidthMm)/2))}
                    .Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Distinct();
                foreach(double foot in feet.Where(x=>x>=min && x<=max))
                {
                        var a=AnalyzeTransverse(input,BeamSearchCounter.UpperAnalyses,stock,new[] {foot-edge,bay-foot-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=load}},null,TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                    if (Pass(input,a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Slab H20",1,()=> $"stock {stock:0} mm · foot {foot:0.###} mm from face · base tributary {tributaryMm:0.###} mm")) result.Add(new Upper { Stock=stock,Custom=!input.StockLengthsMm.Contains(stock),Edge=edge,Foot=foot,OtherFoot=foot,Bay=bay,Load=load,Analysis=a });
                }
            }
            if(lengths.Length==0)input.Diagnostics?.Geometry(input.DiagnosticScope+"slab length "+bay,$"{input.DiagnosticScope}slab bay {bay:0.###} mm has no permitted H20 length with 200–{maximumEndStripMm:0.###} mm edge strips (slab plywood spacing limit).","Review slab stocks, plywood allowance and the custom-length scope.");
            return result;
        }
        private static IEnumerable<CrossOption> CrossOptions(TowerInput input,double width,double tributaryMm)
        {
            var t=input.TransferredSlab; var result=new List<CrossOption>();
            foreach(var left in UpperOptions(t.LeftBayMm,t.LeftAreaLoadKnM2,width,input,t.HasIndividualSpacings ? t.SlabSpacing(true) : tributaryMm,t.LeftPlan,t.LeftBayMm>0 ? t.MaximumSlabEndStripMm(true) : 0)) foreach(var right in UpperOptions(t.RightBayMm,t.RightAreaLoadKnM2,width,input,t.HasIndividualSpacings ? t.SlabSpacing(false) : tributaryMm,t.RightPlan,t.RightBayMm>0 ? t.MaximumSlabEndStripMm(false) : 0))
            {
                double lx=t.BeamCentreOffsetMm-t.BeamWidthMm/2-(left?.Foot??0), rx=t.BeamCentreOffsetMm+t.BeamWidthMm/2+(right?.Foot??0);
                double lo=Math.Min(-width/2,lx)-150,hi=Math.Max(width/2,rx)+150;
                double required=2*Math.Max(t.BeamCentreOffsetMm-lo,hi-t.BeamCentreOffsetMm);
                var lowerLengths=input.StockLengthsMm.Where(x=>x>=required-1e-7);
                // Centre on concrete, including an offset tower; equal tails enclose every bearing/load.
                double custom=Math.Ceiling(required/25)*25;
                if(input.AllowCustomBeamLengths && custom<=50000)lowerLengths=lowerLengths.Concat(new[] {custom}).Distinct();
                var lowerStocks=lowerLengths.ToArray();
                if(lowerStocks.Length==0)input.Diagnostics?.Geometry(input.DiagnosticScope+"lower stock",$"{input.DiagnosticScope}lower secondary needs at least {required:0.###} mm centred stock to reach bearings/feet with 150 mm end allowance; longest available {input.StockLengthsMm.Max():0} mm.","Review lower secondary stock lengths and upper-foot positions.",required-input.StockLengthsMm.Max());
                foreach(double stock in lowerStocks)
                    foreach(double start in (input.EngineerSearch ? new[] {-stock/2,t.BeamCentreOffsetMm-stock/2} : new[] {t.BeamCentreOffsetMm-stock/2}).Distinct().Where(x=>x<=lo+1e-7 && x+stock>=hi-1e-7))
                    {
                        var cases=new List<LoadedBeamAnalysis>(); var names=new List<string>(); bool valid=true;
                        for(int mask=0;mask<4;mask++)
                        {
                            if ((mask&1)!=0 && left==null || (mask&2)!=0 && right==null) continue;
                            var points=t.BeamEdgeLoadsKnM.Select(p=>new BeamPointLoad {PositionMm=t.BeamCentreOffsetMm+p.PositionMm-start,Kn=p.Kn}).ToList(); if ((mask&1)!=0) points.Add(new BeamPointLoad {PositionMm=lx-start,Kn=left.Analysis.Reactions[0]});
                            if ((mask&2)!=0) points.Add(new BeamPointLoad {PositionMm=rx-start,Kn=right.Analysis.Reactions[0]});
                            if ((mask&1)!=0) points.Add(new BeamPointLoad {PositionMm=t.BeamCentreOffsetMm-t.BeamWidthMm/2-100-start,Kn=left.Load*left.Edge/1000});
                            if ((mask&2)!=0) points.Add(new BeamPointLoad {PositionMm=t.BeamCentreOffsetMm+t.BeamWidthMm/2+100-start,Kn=right.Load*right.Edge/1000});
                            var a=AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,stock,new[] {-width/2-start,width/2-start},new[] {new BeamDistributedLoad {StartMm=t.BeamCentreOffsetMm-t.BeamWidthMm/2-start,EndMm=t.BeamCentreOffsetMm+t.BeamWidthMm/2-start,KnPerM=t.BeamLineLoadKnM/(t.BeamWidthMm/1000)}},points.ToArray(),TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                            if ((!input.FullLoadingOnly || mask==((left==null ? 0 : 1)|(right==null ? 0 : 2))) && !Pass(input,a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Lower secondary",2,()=> $"stock {stock:0} mm · primary span {width:0} mm · {(mask==0 ? "Beam" : mask==1 ? "Beam + left slab" : mask==2 ? "Beam + right slab" : "Beam + both slabs")} · base tributary {tributaryMm:0.###} mm")) { valid=false;break; }
                            cases.Add(a); names.Add(mask==0 ? "Beam" : mask==1 ? "Beam + left slab" : mask==2 ? "Beam + right slab" : "Beam + both slabs");
                        }
                        if(valid)
                        {
                            var option=new CrossOption {Start=start,Stock=stock,Left=left,Right=right,Cases=cases.ToArray(),Names=names.ToArray(),DesignCases=input.FullLoadingOnly ? new[] {cases.Count-1} : Enumerable.Range(0,cases.Count).ToArray()};
                            if(input.EngineerPlan?.UseDesignZones==true)
                            {
                                var units=UnitLowerCases(input,option,width);
                                option.PositionedPlan=input.EngineerPlan.WithGeometry(input,units.Last(),option.UnitPointLabels.Last());
                            }
                            result.Add(option);
                        }
                    }
            }
            // Retain varied reaction splits as well as the least-timber choices; this is a bounded search.
            return result.OrderBy(x=>x.Stock+(x.Left?.Stock??0)+(x.Right?.Stock??0)).ThenBy(x=>x.DesignCases.Max(i=>x.Cases[i].DeflectionUtilization)).GroupBy(x=>Math.Round(x.Cases.Last().Reactions[0],1)).Select(g=>g.First()).Take(24).ToArray();
        }
        private static bool Pass(TowerInput input,LoadedBeamAnalysis a,double scale=1,int h20Count=2,TowerSearchDiagnostics diag=null,string scope="H20",int stage=1,Func<string> location=null)
        {
            if(input.DeferCrossingChecks && (stage==2 || stage==3))return true;
            if(stage==3 && input.Primary.IsWS10)
            {
                double strength=input.Primary.Strength(a)*scale;
                diag?.Limit(scope,"WS10 M/V interaction (end zones included)",stage,strength,1,"utilization",location?.Invoke()??"","Review primary stock and tower spacing.");
                diag?.Limit(scope,"deflection",stage,a.DeflectionUtilization*scale,1,"utilization",location?.Invoke()??"","Review the support span and deflection criterion.");
                diag?.Bearing(scope,stage,a.Reactions.Min()*scale,location?.Invoke()??"");
                return (input.IgnoreUplift || a.Reactions.All(x=>x>=-1e-7)) && strength<=1+1e-8 && a.DeflectionUtilization*scale<=1+1e-8;
            }
            diag?.AnalysisLazy(a,scale,h20Count,scope,stage,location??(()=>""));
            return (input.IgnoreUplift || a.Reactions.All(x=>x>=-1e-7)) && a.MomentKnM*scale<=TowerOptimizer.PairMoment*h20Count/2+1e-8 && a.ShearKn*scale<=TowerOptimizer.PairShear*h20Count/2+1e-8 && a.DeflectionUtilization*scale<=1+1e-8;
        }
        // Preserve the entire crossing increment after restoring the full baseline end tributary.
        internal static double[] FullTributaryScales(double[] baselineMm,double[] effectiveMm,double spacingMm,bool excludeZero=false)
            =>baselineMm.Select((width,i)=>excludeZero && effectiveMm[i]==0 ? 0 : (Math.Max(width,spacingMm)+effectiveMm[i]-width)/1000).ToArray();
        private static TransferMember[] LowerCase(TowerInput input,CrossOption cross,double width,RowGrid grid,double edge,int c)
        {
            input.Trace?.CheckCancellation();
            if(cross.RowKey!=grid.CheckKey(input))
            {
                cross.RowKey=grid.CheckKey(input);
                cross.LowerByCase=new TransferMember[cross.Cases.Length][];
                cross.LowerCaseComputed=new bool[cross.Cases.Length];
            }
            if(input.ReuseSearchWork && cross.LowerCaseComputed[c])
            {
                input.Trace?.Increment(BeamSearchCounter.LowerCaseCacheHits);return cross.LowerByCase[c];
            }
            var rows=grid.Rows;var trib=grid.Tributaries;var ls=grid.LeftScales;var rs=grid.RightScales;
            var lower=new List<TransferMember>();
            bool hasLeft=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"), hasRight=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
            for(int row=0;row<rows.Length;row++)
            {
                input.Trace?.CheckCancellation();
                // Design every row for at least the entered spacing; end strips receive no load reduction.
                double beamWidth=EngineerPrimaryPlan.Local(input.TransferredSlab,rows[row]+edge) ? Math.Max(0,trib[row]) : Math.Max(trib[row],input.TransferredSlab.UpperSpacingMm);
                double leftScale=ls[row]==0 ? 0 : Math.Max(ls[row],input.TransferredSlab.SlabSpacing(true)/1000),rightScale=rs[row]==0 ? 0 : Math.Max(rs[row],input.TransferredSlab.SlabSpacing(false)/1000);
                var points=input.TransferredSlab.BeamEdgeLoadsKnM.Select(p=>new BeamPointLoad {PositionMm=input.TransferredSlab.BeamCentreOffsetMm+p.PositionMm-cross.Start,Kn=p.Kn*beamWidth/1000}).ToList();
                if(hasLeft) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamCentreOffsetMm-input.TransferredSlab.BeamWidthMm/2-cross.Left.Foot-cross.Start,Kn=cross.Left.Analysis.Reactions[0]*leftScale});
                if(hasRight) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamCentreOffsetMm+input.TransferredSlab.BeamWidthMm/2+cross.Right.Foot-cross.Start,Kn=cross.Right.Analysis.Reactions[0]*rightScale});
                if(hasLeft) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamCentreOffsetMm-input.TransferredSlab.BeamWidthMm/2-TransferredSlabInput.SideShutterReactionOffsetMm-cross.Start,Kn=cross.Left.Load*cross.Left.Edge/1000*leftScale});
                if(hasRight) points.Add(new BeamPointLoad {PositionMm=input.TransferredSlab.BeamCentreOffsetMm+input.TransferredSlab.BeamWidthMm/2+TransferredSlabInput.SideShutterReactionOffsetMm-cross.Start,Kn=cross.Right.Load*cross.Right.Edge/1000*rightScale});
                var responseKey=Tuple.Create(c,beamWidth,leftScale,rightScale);
                LoadedBeamAnalysis a;
                if(input.ReuseSearchWork && cross.RowAnalyses.TryGetValue(responseKey,out a))input.Trace?.Increment(BeamSearchCounter.LowerResponseCacheHits);
                else
                {
                    if(hasLeft && Math.Abs(leftScale-beamWidth/1000)>1e-10 || hasRight && Math.Abs(rightScale-beamWidth/1000)>1e-10)
                    {
                        a=AnalyzeTransverse(input,BeamSearchCounter.LowerAnalyses,cross.Stock,new[] {-width/2-cross.Start,width/2-cross.Start},new[] {new BeamDistributedLoad {StartMm=input.TransferredSlab.BeamCentreOffsetMm-input.TransferredSlab.BeamWidthMm/2-cross.Start,EndMm=input.TransferredSlab.BeamCentreOffsetMm+input.TransferredSlab.BeamWidthMm/2-cross.Start,KnPerM=input.TransferredSlab.BeamLineLoadKnM/input.TransferredSlab.BeamWidthMm*beamWidth}},points.ToArray(),TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                    }
                    else a=cross.Cases[c].Scale(beamWidth/1000);
                    if(input.ReuseSearchWork && cross.RowAnalyses.Count<256)cross.RowAnalyses[responseKey]=a;
                }
                if(!Pass(input,a,1,1,input.Diagnostics,input.DiagnosticScope+"Lower secondary",2,()=> $"T{row+1} · x={rows[row]+edge:0.###} mm · {cross.Names[c]} · stock {cross.Stock:0} mm · primary span {width:0} mm"))
                {
                    cross.LowerCaseComputed[c]=true;return null;
                }
                lower.Add(new TransferMember {H20Count=1,Name="T"+(row+1),LoadCase=cross.Names[c],StartMm=cross.Start,LengthMm=cross.Stock,SupportsMm=new[] {-width/2-cross.Start,width/2-cross.Start},Points=points.ToArray(),Uniform=new[] {new BeamDistributedLoad {StartMm=input.TransferredSlab.BeamCentreOffsetMm-input.TransferredSlab.BeamWidthMm/2-cross.Start,EndMm=input.TransferredSlab.BeamCentreOffsetMm+input.TransferredSlab.BeamWidthMm/2-cross.Start,KnPerM=input.TransferredSlab.BeamLineLoadKnM/input.TransferredSlab.BeamWidthMm*beamWidth}},Analysis=a});
            }
            cross.LowerCaseComputed[c]=true;return cross.LowerByCase[c]=lower.ToArray();
        }
        private static TransferMember[] UnitLowerCases(TowerInput input,CrossOption cross,double width)
        {
            if(cross.UnitMembers!=null)return cross.UnitMembers;
            var t=input.TransferredSlab;
            cross.UnitMembers=new TransferMember[cross.Names.Length];cross.UnitPointLabels=new string[cross.Names.Length][];
            for(int c=0;c<cross.Names.Length;c++)
            {
                bool left=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"),right=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
                var points=t.BeamEdgeLoadsKnM.Select(p=>new BeamPointLoad {PositionMm=t.BeamCentreOffsetMm+p.PositionMm-cross.Start,Kn=p.Kn}).ToList();var labels=t.BeamEdgeLoadsKnM.Select(p=>"Slab edge ledger").ToList();
                Action<bool,bool> add=(isLeft,shutter)=>
                {
                    var upper=isLeft ? cross.Left : cross.Right;double sign=isLeft ? -1 : 1;
                    points.Add(new BeamPointLoad {PositionMm=t.BeamCentreOffsetMm+sign*(t.BeamWidthMm/2+(shutter ? TransferredSlabInput.SideShutterReactionOffsetMm : upper.Foot))-cross.Start,
                        Kn=shutter ? upper.Load*upper.Edge/1000 : upper.Analysis.Reactions[0]});
                    labels.Add((isLeft ? "Left " : "Right ")+(shutter ? "shutter" : "slab foot"));
                };
                if(left)add(true,false);if(right)add(false,false);if(left)add(true,true);if(right)add(false,true);
                cross.UnitMembers[c]=new TransferMember {Name="Equivalent source",LoadCase=cross.Names[c],H20Count=1,StartMm=cross.Start,LengthMm=cross.Stock,
                    SupportsMm=new[] {-width/2-cross.Start,width/2-cross.Start},Points=points.ToArray(),
                    Uniform=new[] {new BeamDistributedLoad {StartMm=t.BeamCentreOffsetMm-t.BeamWidthMm/2-cross.Start,EndMm=t.BeamCentreOffsetMm+t.BeamWidthMm/2-cross.Start,KnPerM=t.BeamLineLoadKnM/t.BeamWidthMm*1000}},Analysis=cross.Cases[c]};
                cross.UnitPointLabels[c]=labels.ToArray();
            }
            return cross.UnitMembers;
        }
        // Primary design loads use full-tributary reactions per metre and crossing footprints,
        // never on the assumed secondary row stations or which row falls across a support.
        internal static BeamDistributedLoad[] PrimaryUniformLoads(double stockStart,double stockLength,double beamKnM,double leftKnM,double rightKnM,BeamDistributedLoad[] leftCrossings,BeamDistributedLoad[] rightCrossings,bool allowSigned=false,double slabStart=double.NegativeInfinity,double slabEnd=double.PositiveInfinity)
        {
            var bands=new List<BeamDistributedLoad>();
            Action<double,double,double> add=(start,end,q)=>
            {
                double a=Math.Max(stockStart,start),b=Math.Min(stockStart+stockLength,end);
                if(b>a+1e-7 && Math.Abs(q)>1e-10)bands.Add(new BeamDistributedLoad {StartMm=a-stockStart,EndMm=b-stockStart,KnPerM=q});
            };
            add(stockStart,stockStart+stockLength,beamKnM);
            // Slab baseline is carried only inside its supported longitudinal coverage.
            add(Math.Max(stockStart,slabStart),Math.Min(stockStart+stockLength,slabEnd),leftKnM+rightKnM);
            // Negative bands exclude baseline slab beneath independent crossings:
            // exclude only baseline that exists here. Positive crossing concrete
            // remains over its full physical footprint, including end regions.
            foreach(var band in leftCrossings??new BeamDistributedLoad[0])add(band.KnPerM<0 ? Math.Max(band.StartMm,slabStart) : band.StartMm,band.KnPerM<0 ? Math.Min(band.EndMm,slabEnd) : band.EndMm,leftKnM*band.KnPerM);
            foreach(var band in rightCrossings??new BeamDistributedLoad[0])add(band.KnPerM<0 ? Math.Max(band.StartMm,slabStart) : band.StartMm,band.KnPerM<0 ? Math.Min(band.EndMm,slabEnd) : band.EndMm,rightKnM*band.KnPerM);
            var cuts=bands.SelectMany(q=>new[] {q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();
            var result=new List<BeamDistributedLoad>();
            for(int i=1;i<cuts.Length;i++)
            {
                double middle=(cuts[i-1]+cuts[i])/2,q=bands.Where(b=>b.StartMm<middle && b.EndMm>middle).Sum(b=>b.KnPerM);
                if(!allowSigned && q< -1e-8)return null;
                if(Math.Abs(q)<=1e-10)continue;
                var prior=result.LastOrDefault();
                if(prior!=null && prior.EndMm==cuts[i-1] && prior.KnPerM==q)prior.EndMm=cuts[i];
                else result.Add(new BeamDistributedLoad {StartMm=cuts[i-1],EndMm=cuts[i],KnPerM=q});
            }
            return result.ToArray();
        }
        private static PrimaryLoadData PrimaryLoads(TowerInput input,CrossOption cross,double stockStart,double stockLength,int line,int c,double edge)
        {
            using(var hot=input.Trace?.Hot(BeamHotStage.LoadPreparation))
            {
            // Load clipping depends on the stock and transverse source, never on
            // the tower stations being tried beneath that same stock.
            var key=new PrimaryLoadKey(stockStart,stockLength,line,c,edge);
            if(input.ReuseSearchWork && input.OptimizePrimaryResponses && cross.PrimaryLoads.TryGetValue(key,out var found))
            {input.Trace?.Increment(BeamSearchCounter.PrimaryLoadCacheHits);return found;}
            var uniform=input.EngineerPlan?.UseDesignZones==true
                ? (cross.PositionedPlan??input.EngineerPlan).Loads(stockStart,stockLength,line,selfWeight:input.Primary.SelfWeightKnM(stockLength))
                : LegacyPrimaryUniformLoads(input,cross,stockStart,stockLength,line,c,edge);
            var data=new PrimaryLoadData {Uniform=uniform};
            if(uniform!=null)
            {
                data.Positions=new double[uniform.Length*2];data.Forces=new double[uniform.Length*2];
                for(int i=0;i<uniform.Length;i++){data.Positions[2*i]=uniform[i].StartMm;data.Positions[2*i+1]=uniform[i].EndMm;data.Forces[2*i]=data.Forces[2*i+1]=uniform[i].KnPerM;}
            }
            if(input.ReuseSearchWork && input.OptimizePrimaryResponses)cross.PrimaryLoads.Store(key,data);
            return data;
            }
        }
        private static BeamDistributedLoad[] PrimaryUniformLoads(TowerInput input,CrossOption cross,double stockStart,double stockLength,int line,int c,double edge)
            =>PrimaryLoads(input,cross,stockStart,stockLength,line,c,edge).Uniform;
        internal readonly struct UniformPrimaryFailure
        {
            internal readonly double Moment,Shear,DeflectionUse,GoverningLength,MinimumReaction,MaximumReaction;
            internal readonly int PeakReaction;
            internal UniformPrimaryFailure(double moment,double shear,double deflectionUse,double length,double minimum,double maximum,int peak)
            {Moment=moment;Shear=shear;DeflectionUse=deflectionUse;GoverningLength=length;MinimumReaction=minimum;MaximumReaction=maximum;PeakReaction=peak;}
        }
        internal static bool TryRejectUniformPrimary(TowerInput input,double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,SearchWorkCache shared,double legCapacity,out UniformPrimaryFailure failure)
        {
            if(!TryCheckUniformPrimary(input,length,supports,uniform,points,shared,legCapacity,out failure,out bool passed) || passed)
            {failure=default(UniformPrimaryFailure);return false;}
            input.Trace?.Increment(BeamSearchCounter.UniformPrimaryCacheHits);return true;
        }
        internal static bool TryCheckUniformPrimary(TowerInput input,double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,SearchWorkCache shared,double legCapacity,out UniformPrimaryFailure failure,out bool passed)
        {
            failure=default(UniformPrimaryFailure);passed=false;
            if(input.Primary.IsWS10 || !input.OptimizePrimaryResponses || !input.ReuseSearchWork || !input.ReuseNetworkNumerics || shared==null
                || points==null || points.Length!=0 || uniform==null || uniform.Length!=1 || uniform[0].StartMm<0 || uniform[0].EndMm>length
                || !TowerBeamAnalysis.Finite(uniform[0].KnPerM) || uniform[0].KnPerM<0 || !TowerBeamAnalysis.Finite(legCapacity) || legCapacity<=0)return false;
            return CheckUniformPrimaryValues(input,length,supports,uniform[0].StartMm,uniform[0].EndMm,uniform[0].KnPerM,shared,legCapacity,out failure,out passed);
        }
        // The native projected pair is point-free with one exact UDL. Query its
        // existing immutable unit before allocating a rejected member graph.
        internal static bool TryRejectUniformPrimaryValues(TowerInput input,double length,double[] supports,double loadStart,double loadEnd,double factor,SearchWorkCache shared,double legCapacity,out UniformPrimaryFailure failure)
        {
            failure=default(UniformPrimaryFailure);
            if(input.Primary.IsWS10 || !input.OptimizePrimaryResponses || !input.ReuseSearchWork || !input.ReuseNetworkNumerics || shared==null
                || loadStart<0 || loadEnd>length || !TowerBeamAnalysis.Finite(factor) || factor<0 || !TowerBeamAnalysis.Finite(legCapacity) || legCapacity<=0)return false;
            if(!CheckUniformPrimaryValues(input,length,supports,loadStart,loadEnd,factor,shared,legCapacity,out failure,out bool passed) || passed)
            {failure=default(UniformPrimaryFailure);return false;}
            input.Trace?.Increment(BeamSearchCounter.UniformPrimaryCacheHits);return true;
        }
        private static bool CheckUniformPrimaryValues(TowerInput input,double length,double[] supports,double loadStart,double loadEnd,double factor,SearchWorkCache shared,double legCapacity,out UniformPrimaryFailure failure,out bool passed)
        {
            failure=default(UniformPrimaryFailure);passed=false;
            input.Trace?.CheckCancellation();
            var key=new UniformShapeKey(length,supports,input.Primary.EI,input.DeflectionDenominator,loadStart,loadEnd);
            if(!shared.UniformShapes.TryGetValue(key,out var unit) || unit==null || !unit.DeflectionEvaluated || unit.Reactions==null || unit.Reactions.Length==0)return false;
            double moment=unit.MomentKnM*factor,shear=unit.ShearKn*factor,deflection=unit.DeflectionUtilization*factor;
            double maximum=unit.Reactions[0]*factor,minimum=maximum;int peak=0;
            if(!TowerBeamAnalysis.Finite(moment) || !TowerBeamAnalysis.Finite(shear) || !TowerBeamAnalysis.Finite(deflection) || !TowerBeamAnalysis.Finite(maximum))return false;
            for(int i=1;i<unit.Reactions.Length;i++)
            {
                double reaction=unit.Reactions[i]*factor;if(!TowerBeamAnalysis.Finite(reaction))return false;
                if(reaction>maximum){maximum=reaction;peak=i;}if(reaction<minimum)minimum=reaction;
            }
            double utilization=Math.Max(deflection,Math.Max(moment/input.Primary.MomentLimitKnM,shear/input.Primary.ShearLimitKn));
            // These exact boundaries already require a fresh direct numerical
            // response in AnalyzePrimary; never short-circuit that reanalysis.
            if(Math.Abs(utilization-1)<1e-7 || Math.Abs(maximum-input.LegCapacityKn)<1e-6
                || !input.IgnoreUplift && Math.Abs(minimum)<1e-7)return false;
            passed=utilization<=1+1e-8 && (input.IgnoreUplift || minimum>=-1e-7) && maximum<=legCapacity+1e-8;
            failure=new UniformPrimaryFailure(moment,shear,deflection,unit.GoverningLengthMm,minimum,maximum,peak);
            return true;
        }
        // A whole projected pool may fail at its least governing actual UDL.
        // Keep every factor in the existing scaled-response path: crossing ANY
        // direct-reanalysis boundary, a cold shape or nonfinite scale is unknown.
        internal static bool TryRejectUniformPrimaryRange(TowerInput input,double length,double[] supports,double loadStart,double loadEnd,double minimumFactor,double maximumFactor,SearchWorkCache shared,double legCapacity,out UniformPrimaryFailure failure)
        {
            failure=default(UniformPrimaryFailure);
            bool finite(double value)=>TowerBeamAnalysis.Finite(value);
            if(input.PrimaryMaterial!=PrimaryBeamMaterial.DoubleH20 || !input.OptimizePrimaryResponses || !input.ReuseSearchWork || !input.ReuseNetworkNumerics
                || input.Trace?.HasTimeLimit==true || shared==null || !finite(length) || length<=0 || supports==null || supports.Length!=2
                || !finite(supports[0]) || !finite(supports[1]) || supports[0]<0 || supports[1]>length || supports[1]<=supports[0]
                || !finite(loadStart) || !finite(loadEnd) || loadStart<0 || loadEnd>length || loadEnd<=loadStart
                || !finite(minimumFactor) || !finite(maximumFactor) || minimumFactor<0 || maximumFactor<minimumFactor
                || !finite(legCapacity) || legCapacity<=0 || !finite(input.LegCapacityKn) || input.LegCapacityKn<=0)return false;
            input.Trace?.CheckCancellation();
            var key=new UniformShapeKey(length,supports,input.Primary.EI,input.DeflectionDenominator,loadStart,loadEnd);
            if(!shared.UniformShapes.TryGetValue(key,out var unit) || unit==null || !unit.DeflectionEvaluated || unit.Reactions==null || unit.Reactions.Length!=2
                || !finite(unit.MomentKnM) || unit.MomentKnM<0 || !finite(unit.ShearKn) || unit.ShearKn<0
                || !finite(unit.DeflectionUtilization) || unit.DeflectionUtilization<0)return false;
            if(!Scale(minimumFactor,out var low,out double lowUse) || !Scale(maximumFactor,out var high,out double highUse))return false;
            // Rounded subtraction is monotone on either side of the centre.
            // Test the nearest endpoint with the ORIGINAL subtraction/Abs;
            // equality and every interval crossing the band stay conservative.
            if(!OutsideBand(lowUse,highUse,1,1e-7)
                || !OutsideBand(Math.Min(low.MaximumReaction,high.MaximumReaction),Math.Max(low.MaximumReaction,high.MaximumReaction),input.LegCapacityKn,1e-6)
                || !input.IgnoreUplift && !OutsideBand(Math.Min(low.MinimumReaction,high.MinimumReaction),Math.Max(low.MinimumReaction,high.MinimumReaction),0,1e-7))return false;
            if(lowUse<=1+1e-8 && (input.IgnoreUplift || low.MinimumReaction>=-1e-7) && low.MaximumReaction<=legCapacity+1e-8)return false;
            input.Trace?.CheckCancellation();failure=low;return true;
            bool Scale(double factor,out UniformPrimaryFailure scaled,out double utilization)
            {
                scaled=default(UniformPrimaryFailure);utilization=0;
                double moment=unit.MomentKnM*factor,shear=unit.ShearKn*factor,deflection=unit.DeflectionUtilization*factor;
                double maximum=unit.Reactions[0]*factor,minimum=maximum;int peak=0;
                if(!finite(moment) || !finite(shear) || !finite(deflection) || !finite(maximum))return false;
                for(int i=1;i<unit.Reactions.Length;i++)
                {
                    double reaction=unit.Reactions[i]*factor;if(!finite(reaction))return false;
                    if(reaction>maximum){maximum=reaction;peak=i;}if(reaction<minimum)minimum=reaction;
                }
                utilization=Math.Max(deflection,Math.Max(moment/input.Primary.MomentLimitKnM,shear/input.Primary.ShearLimitKn));
                if(!finite(utilization))return false;
                scaled=new UniformPrimaryFailure(moment,shear,deflection,unit.GoverningLengthMm,minimum,maximum,peak);return true;
            }
        }
        private static bool OutsideBand(double low,double high,double centre,double tolerance)
            =>high<centre ? Math.Abs(high-centre)>tolerance : low>centre && Math.Abs(low-centre)>tolerance;
        internal static LoadedBeamAnalysis AnalyzePrimary(TowerInput input,double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,LoadedBeamAnalysis.MatrixCache matrices,SearchWorkCache shared)
        {
            using(var hot=input.Trace?.Hot(BeamHotStage.PrimaryResponse))
            {
            input.Trace?.CheckCancellation();
            bool reject=input.StagePrimaryChecks && !input.Primary.IsWS10 && !input.DeferCrossingChecks;
            LoadedBeamAnalysis analysis=null;long before=matrices?.Hits??0;
            if(input.OptimizePrimaryResponses && shared!=null && points.Length==0 && uniform.Length==1 && uniform[0].StartMm>=0 && uniform[0].EndMm<=length && uniform[0].KnPerM>=0)
            {
                var shape=new UniformShapeKey(length,supports,input.Primary.EI,input.DeflectionDenominator,uniform[0].StartMm,uniform[0].EndMm);
                if(!shared.UniformShapes.TryGetValue(shape,out var unit))
                {
                    input.Trace?.Increment(BeamSearchCounter.PrimaryAnalyses);
                    unit=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,new[] {new BeamDistributedLoad {StartMm=uniform[0].StartMm,EndMm=uniform[0].EndMm,KnPerM=1}},points,input.Primary.EI,input.DeflectionDenominator,matrices);
                    shared.UniformShapes.Store(shape,unit);
                }
                else input.Trace?.Increment(BeamSearchCounter.UniformPrimaryCacheHits);
                analysis=input.Primary.IsWS10 ? unit.Scale(uniform[0].KnPerM) : unit.ScaleForCheck(uniform[0].KnPerM);
                // Retain the original arithmetic at acceptance boundaries.
                if(Math.Abs(PrimaryUtilization(input,analysis)-1)<1e-7 || Math.Abs(analysis.Reactions.Max()-input.LegCapacityKn)<1e-6
                    || !input.IgnoreUplift && Math.Abs(analysis.Reactions.Min())<1e-7)analysis=null;
            }
            if(analysis==null)
            {
                input.Trace?.Increment(BeamSearchCounter.PrimaryAnalyses);
                using(input.Trace?.Measure(BeamSearchStage.PrimaryAnalysis))
                    analysis=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,uniform,points,input.Primary.EI,input.DeflectionDenominator,matrices,input.LegCapacityKn,reject,input.IgnoreUplift || input.DeferCrossingChecks,input.TransferredSlab?.IncludeConcreteEndStrips==true);
            }
            if(!analysis.DeflectionEvaluated)input.Trace?.Increment(BeamSearchCounter.PrimaryDeflectionSkipped);
            if(matrices!=null && matrices.Hits>before)input.Trace?.Increment(BeamSearchCounter.PrimaryMatrixCacheHits);
            return analysis;
            }
        }
        private static BeamDistributedLoad[] LegacyPrimaryUniformLoads(TowerInput input,CrossOption cross,double stockStart,double stockLength,int line,int c,double edge)
        {
            double baseBeam=cross.Cases[0].Reactions[line],beam=baseBeam;
            // Dedicated crossing primaries use the same continuous UDL design as main beams.
            // Include the full concrete span on its centred stock coverage: this preserves
            // total force and its longitudinal resultant without secondary-row point loads.
            if(input.TransferredSlab.IncludeConcreteEndStrips && edge-input.LongitudinalStartMm>1e-7)
                beam*=input.LengthMm/(input.LengthMm-2*(edge-input.LongitudinalStartMm));
            int left=Array.IndexOf(cross.Names,"Beam + left slab"),right=Array.IndexOf(cross.Names,"Beam + right slab");
            bool hasLeft=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"),hasRight=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
            double l=hasLeft && left>=0 ? cross.Cases[left].Reactions[line]-baseBeam : 0,r=hasRight && right>=0 ? cross.Cases[right].Reactions[line]-baseBeam : 0;
            double slabCoverage=input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm)),slabEdge=input.TransferredSlab.RowStart(input,slabCoverage);
            // Main-primary intensities are independent of coverage. Uncovered longitudinal
            // strips belong to external column-side supports. Slab coverage can
            // differ from primary coverage; each excluded load is assigned once.
            var design=PrimaryUniformLoads(stockStart,stockLength,beam,l,r,input.TransferredSlab.LeftCrossingBands,input.TransferredSlab.RightCrossingBands,input.IgnoreUplift || input.DeferCrossingChecks,
                slabEdge,slabEdge+slabCoverage);
            return design==null || !input.Primary.IsWS10 ? design : design.Concat(new[] {new BeamDistributedLoad {StartMm=0,EndMm=stockLength,KnPerM=input.Primary.SelfWeightKnM(stockLength)}}).ToArray();
        }
        private static ColumnEndStripLoad[][] SelectColumnCases(TowerInput input,CrossOption cross,double edge)
            => input.EngineerPlan?.UseDesignZones==true
                ? cross.DesignCases.Select(c=>(cross.PositionedPlan??input.EngineerPlan).Columns(edge,input.LongitudinalEndMm-(edge-input.LongitudinalStartMm),cross.Names[c])).ToArray()
                : SelectLegacyColumnCases(input,cross,edge);
        private static ColumnEndStripLoad[][] SelectLegacyColumnCases(TowerInput input,CrossOption cross,double edge)
        {
            var all=ColumnSupportLoads(input,cross,edge);
            return all.Length==0 ? all : cross.DesignCases.Select(i=>all[i]).ToArray();
        }
        private static ColumnEndStripLoad[][] ColumnSupportLoads(TowerInput input,CrossOption cross,double edge)
        {
            if(input.TransferredSlab.IncludeConcreteEndStrips)return new ColumnEndStripLoad[0][];
            var t=input.TransferredSlab;
            double slabCoverage=t.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm)),slabEdge=input.TransferredSlab.RowStart(input,slabCoverage);
            if(edge<=input.LongitudinalStartMm+1e-7 && slabEdge<=input.LongitudinalStartMm+1e-7)return new ColumnEndStripLoad[0][];
            var result=new ColumnEndStripLoad[cross.Names.Length][];
            for(int c=0;c<result.Length;c++)
            {
                var forces=new double[2,2];
                var moments=new double[2,2];
                var starts=new[] {double.PositiveInfinity,double.PositiveInfinity};
                var ends=new[] {double.NegativeInfinity,double.NegativeInfinity};
                for(int line=0;line<2;line++)
                {
                    double beam=cross.Cases[0].Reactions[line];
                    int left=Array.IndexOf(cross.Names,"Beam + left slab"),right=Array.IndexOf(cross.Names,"Beam + right slab");
                    bool hasLeft=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"),hasRight=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
                    double l=hasLeft && left>=0 ? cross.Cases[left].Reactions[line]-beam : 0,r=hasRight && right>=0 ? cross.Cases[right].Reactions[line]-beam : 0;
                    // Complement of the actual primary/slab-supported regions,
                    // rather than a second assignment added to the old strips.
                    var full=PrimaryUniformLoads(input.LongitudinalStartMm,input.LengthMm,beam,l,r,t.LeftCrossingBands,t.RightCrossingBands,true);
                    var carried=PrimaryUniformLoads(edge,input.LengthMm-2*(edge-input.LongitudinalStartMm),beam,l,r,t.LeftCrossingBands,t.RightCrossingBands,true,slabEdge,slabEdge+slabCoverage);
                    var cuts=full.SelectMany(q=>new[] {input.LongitudinalStartMm+q.StartMm,input.LongitudinalStartMm+q.EndMm}).Concat(carried.SelectMany(q=>new[] {edge+q.StartMm,edge+q.EndMm})).Concat(new[] {input.LongitudinalStartMm,input.LongitudinalEndMm}).Distinct().OrderBy(x=>x).ToArray();
                    for(int i=1;i<cuts.Length;i++)
                    {
                        double middle=(cuts[i-1]+cuts[i])/2;
                        double q=full.Where(b=>input.LongitudinalStartMm+b.StartMm<middle && input.LongitudinalStartMm+b.EndMm>middle).Sum(b=>b.KnPerM)-carried.Where(b=>edge+b.StartMm<middle && edge+b.EndMm>middle).Sum(b=>b.KnPerM);
                        if(Math.Abs(q)<=1e-10)continue;
                        int end=middle<input.LongitudinalStartMm+input.LengthMm/2 ? 0 : 1;
                        double force=q*(cuts[i]-cuts[i-1])/1000;
                        forces[end,line]+=force;moments[end,line]+=force*middle/1000;
                        starts[end]=Math.Min(starts[end],cuts[i-1]);ends[end]=Math.Max(ends[end],cuts[i]);
                    }
                }
                result[c]=Enumerable.Range(0,2).Where(end=>starts[end]<ends[end]).Select(end=>new ColumnEndStripLoad {LoadCase=cross.Names[c],End=end==0 ? "Near" : "Far",
                    StartMm=starts[end],EndMm=ends[end],LeftLineKn=forces[end,0],RightLineKn=forces[end,1],
                    LeftLineMomentKnM=moments[end,0],RightLineMomentKnM=moments[end,1]}).ToArray();
            }
            return result;
        }
        private static double PhysicalSlabReferenceLengthMm(TowerInput input,bool left)
        {
            var bands=left ? input.TransferredSlab.LeftCrossingBands : input.TransferredSlab.RightCrossingBands;
            double start=(left ? input.TransferredSlab.LeftSlabStartMm : input.TransferredSlab.RightSlabStartMm)??input.LongitudinalStartMm;
            double end=(left ? input.TransferredSlab.LeftSlabEndMm : input.TransferredSlab.RightSlabEndMm)??input.LongitudinalEndMm;
            return end-start+(bands??new BeamDistributedLoad[0]).Sum(b=>b.KnPerM*Math.Max(0,Math.Min(end,b.EndMm)-Math.Max(start,b.StartMm)));
        }
        private static double PhysicalSlabLoadKn(TowerInput input,CrossOption cross)
            =>(cross.Left==null ? 0 : (cross.Left.Analysis.Reactions[0]+cross.Left.Load*cross.Left.Edge/1000)*PhysicalSlabReferenceLengthMm(input,true)/1000)
                +(cross.Right==null ? 0 : (cross.Right.Analysis.Reactions[0]+cross.Right.Load*cross.Right.Edge/1000)*PhysicalSlabReferenceLengthMm(input,false)/1000);
        private static double PhysicalShutterLoadKn(TowerInput input,CrossOption cross)
            =>(cross.Left==null ? 0 : cross.Left.Load*cross.Left.Edge*PhysicalSlabReferenceLengthMm(input,true)/1e6)
                +(cross.Right==null ? 0 : cross.Right.Load*cross.Right.Edge*PhysicalSlabReferenceLengthMm(input,false)/1e6);
        // Simply supported sheathing strips transfer to the outer two secondary rows.
        // Signed influence reactions retain both force and moment of the full end strips.
        internal static double[] ConcreteEndStripTributaries(double covered,double[] rows,double edge)
        {
            var widths=new double[rows.Length];
            if(edge<=1e-7)return widths;
            if(rows.Length<2)throw new ArgumentException("End-strip transfer needs two secondary rows.");
            double leftSecond=edge*(-edge/2-rows[0])/(rows[1]-rows[0]);
            widths[0]+=edge-leftSecond;widths[1]+=leftSecond;
            int last=rows.Length-1;
            double rightLast=edge*(covered+edge/2-rows[last-1])/(rows[last]-rows[last-1]);
            widths[last-1]+=edge-rightLast;widths[last]+=rightLast;
            return widths;
        }
        private static bool AcceptTransverseCases(TowerInput input,CrossOption cross,double width,RowGrid grid,double edge)
        {
            string key=grid.CheckKey(input);
            if(input.ReuseSearchWork && cross.TransverseKey==key)return cross.TransversePassed;
            bool reuse=input.ReuseSearchWork && input.ReuseTransversePreparations;
            if(reuse && cross.TransversePreparations!=null && cross.TransversePreparations.TryGetValue(key,out var saved))
            {
                cross.TransverseKey=key;cross.TransversePassed=saved.Passed;cross.PreparedReceiver=saved.Receiver;
                return saved.Passed;
            }
            string previousKey=cross.TransverseKey;bool previousPassed=cross.TransversePassed;var previousReceiver=cross.PreparedReceiver;
            cross.TransverseKey=key;cross.TransversePassed=false;cross.PreparedReceiver=null;
            try
            {
                bool passed=CheckTransverseCases(input,cross,width,grid,edge);
                if(reuse)
                {
                    // An interrupted lower check must never become cached evidence
                    // that an otherwise identical grid fails or has been prepared.
                    input.Trace?.CheckCancellation();
                    if(cross.TransversePreparations==null)cross.TransversePreparations=new ResponseCache<string,TransversePreparation>(8);
                    cross.TransversePreparations.Store(key,new TransversePreparation {Passed=passed,Receiver=cross.PreparedReceiver});
                }
                return passed;
            }
            catch {cross.TransverseKey=previousKey;cross.TransversePassed=previousPassed;cross.PreparedReceiver=previousReceiver;throw;}
        }
        private static bool CheckTransverseCases(TowerInput input,CrossOption cross,double width,RowGrid grid,double edge)
        {
            var ls=grid.LeftScales;var rs=grid.RightScales;
            if(cross.Left!=null && !Pass(input,cross.Left.Analysis,Math.Max(ls.Max(),input.TransferredSlab.SlabSpacing(true)/1000),1,input.Diagnostics,input.DiagnosticScope+"Left slab H20",1,()=> $"governing row T{Array.IndexOf(ls,ls.Max())+1} · stock {cross.Left.Stock:0} mm")
                || cross.Right!=null && !Pass(input,cross.Right.Analysis,Math.Max(rs.Max(),input.TransferredSlab.SlabSpacing(false)/1000),1,input.Diagnostics,input.DiagnosticScope+"Right slab H20",1,()=> $"governing row T{Array.IndexOf(rs,rs.Max())+1} · stock {cross.Right.Stock:0} mm"))return false;
            if(input.TransferLowerAcceptance==null)return cross.TransversePassed=true;
            var lower=cross.DesignCases.Select(c=>LowerCase(input,cross,width,grid,edge,c)).ToArray();
            if(lower.Any(c=>c==null))return false;
            var probe=new TowerLayout {EngineerSearch=true,PrimaryMaterial=input.PrimaryMaterial,PositionsMm=new double[0],Joined=new bool[0],WidthMm=width,LengthMm=input.LengthMm,LongitudinalStartMm=input.LongitudinalStartMm,
                StockPlan=new TowerStockPlan {Pieces=new TowerStockPiece[0]},
                Transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerReceiverCount,RowsMm=grid.Rows.Select(x=>x+edge).ToArray(),LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,CaseNames=cross.DesignCases.Select(c=>cross.Names[c]).ToArray(),LowerCases=lower}};
            probe=OffsetReceiver(input,probe);
            if(!input.TransferLowerAcceptance(probe))return false;
            cross.PreparedReceiver=input.TransferReceiverPreparation?.Invoke(probe);
            // Failure resolved entirely from this fixed transverse option and
            // row grid cannot be repaired by any primary tower geometry. Keep
            // ordinary geometry-dependent callbacks on the full candidate path.
            if(ReferenceEquals(cross.PreparedReceiver,CrossBeamSupportOptimizer.FailedReceiver))return false;
            return cross.TransversePassed=true;
        }
        private static bool PreparedSpanScreen(TowerInput input,Geometry geometry,CrossOption cross,double edge,EngineerPrimaryPlan plan)
        {
            if(!cross.SpanPrepared)
            {
                cross.UnknownSpans=input.TransferredSlab.LeftCrossingBands.Concat(input.TransferredSlab.RightCrossingBands).Where(b=>b.KnPerM<0)
                    .Concat(plan.Network.CrossingBeams.Where(c=>c.BayNumber==plan.Beam || c.BayNumber==plan.Beam+1).Select(c=>new BeamDistributedLoad {StartMm=c.PositionMm-c.WidthMm/2,EndMm=c.PositionMm+c.WidthMm/2})).OrderBy(q=>q.StartMm).ToArray();
                var beam=plan.Network.Beams[plan.Beam];
                cross.MinimumLineLoads=plan.HasPositionedLoads ? EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,plan.OwnLoads)
                    : new[] {plan.Network.AreaLoad(beam.DepthMm)*beam.WidthMm/2000,plan.Network.AreaLoad(beam.DepthMm)*beam.WidthMm/2000};
                if(plan.HasPositionedLoads)foreach(var slab in plan.SlabLoads.Values)
                {var reaction=EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,slab);for(int line=0;line<2;line++)cross.MinimumLineLoads[line]+=Math.Min(0,reaction[line]);}
                cross.SpanPrepared=true;
            }
            double largest=Math.Max(0,Math.Max(cross.MinimumLineLoads[0],cross.MinimumLineLoads[1]));
            if(input.PrepareSpanGeometry && input.ReuseSearchWork)
            {
                return PreparedSpanShapeFits(input,geometry,PreparedSpanContext(input,cross,edge),largest);
            }
            for(int run=0;run<geometry.Stock.Length;run++)
            {
                var stock=geometry.Stock[run];var supports=geometry.Supports(run);double origin=stock.StartMm+edge;
                double q=largest+input.Primary.SelfWeightKnM(stock.StockLengthMm);
                for(int tail=0;tail<2;tail++)
                {
                    double a=tail==0 ? 0 : supports[supports.Length-1],b=tail==0 ? supports[0] : stock.StockLengthMm;
                    bool unknown=false;foreach(var band in cross.UnknownSpans)if(band.EndMm>origin+a && band.StartMm<origin+b){unknown=true;break;}
                    if(unknown)continue;
                    double length=(b-a)/1000;
                    if(q*length>input.Primary.ShearLimitKn+1e-7 || q*length*length/2>input.Primary.MomentLimitKnM+1e-7)return false;
                }
                for(int span=0;span<=supports.Length;span++)
                {
                    double a=origin+(span==0 ? 0 : supports[span-1]),b=origin+(span==supports.Length ? stock.StockLengthMm : supports[span]);
                    foreach(var band in cross.UnknownSpans)
                    {
                        if(band.EndMm<=a)continue;if(band.StartMm>=b)break;
                        double length=(Math.Min(b,band.StartMm)-a)/1000;
                        if(length>0 && (q*length>2*input.Primary.ShearLimitKn+1e-7 || q*length*length>16*input.Primary.MomentLimitKnM+1e-7))return false;
                        a=Math.Max(a,band.EndMm);if(a>=b)break;
                    }
                    double last=(b-a)/1000;
                    if(last>0 && (q*last>2*input.Primary.ShearLimitKn+1e-7 || q*last*last>16*input.Primary.MomentLimitKnM+1e-7))return false;
                }
            }
            return true;
        }
        private static SpanGeometryContext PreparedSpanContext(TowerInput input,CrossOption cross,double edge)
        {
            if(cross.SpanContext!=null && cross.SpanContext.Edge==edge)return cross.SpanContext;
            if(cross.SpanContexts!=null && cross.SpanContexts.TryGetValue(edge,out var saved))return cross.SpanContext=saved;
            // UnknownSpans is fixed for this transverse option. Ranked work
            // interleaves covered lengths; changing only the exact edge should
            // not repeatedly allocate the same immutable footprint boundaries.
            var context=new SpanGeometryContext(edge,cross.UnknownSpans);
            input.Trace?.CheckCancellation();
            if(cross.SpanContexts==null)cross.SpanContexts=new ResponseCache<double,SpanGeometryContext>(8);
            cross.SpanContexts.Store(edge,context);return cross.SpanContext=context;
        }
        private static bool NecessaryPrimarySpans(TowerInput input,Geometry geometry,CrossOption cross,double edge,int stage)
        {
            using(var hot=input.Trace?.Hot(BeamHotStage.SpanScreen))
            {
            var preparedPlan=cross.PositionedPlan??input.EngineerPlan;
            if(input.PrepareReceiverChecks && input.DeferCrossingChecks && preparedPlan!=null && (cross.SpanPrepared || preparedPlan.Network.CrossingBeams.Any(c=>(c.BayNumber==preparedPlan.Beam || c.BayNumber==preparedPlan.Beam+1) && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows)))return PreparedSpanScreen(input,geometry,cross,edge,preparedPlan);
            // Between supports, a constant UDL changes shear by qL and needs a
            // moment range qL²/8. With |V|<=Vlim and |M|<=Mlim these are necessary
            // bounds, independent of end moments. Unknown crossing increments
            // are confined to their footprints, which are excluded from this filter.
            var unknown=input.TransferredSlab.LeftCrossingBands.Concat(input.TransferredSlab.RightCrossingBands).Where(b=>b.KnPerM<0).ToArray();
            var plan=cross.PositionedPlan??input.EngineerPlan;
            bool unresolved=input.DeferCrossingChecks && plan!=null && plan.Network.CrossingBeams.Any(c=>(c.BayNumber==plan.Beam || c.BayNumber==plan.Beam+1) && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows);
            if(unresolved)
                unknown=unknown.Concat(plan.Network.CrossingBeams.Where(c=>c.BayNumber==plan.Beam || c.BayNumber==plan.Beam+1).Select(c=>new BeamDistributedLoad {StartMm=c.PositionMm-c.WidthMm/2,EndMm=c.PositionMm+c.WidthMm/2})).ToArray();
            double[] minimum=null;
            if(unresolved)
            {
                // This transverse lower bound is identical for every stock piece.
                // Do not build the provisional zones only to discard them below.
                var beam=plan.Network.Beams[plan.Beam];
                minimum=plan.HasPositionedLoads ? EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,plan.OwnLoads)
                    : new[] {plan.Network.AreaLoad(beam.DepthMm)*beam.WidthMm/2000,plan.Network.AreaLoad(beam.DepthMm)*beam.WidthMm/2000};
                if(plan.HasPositionedLoads)foreach(var slab in plan.SlabLoads.Values)
                {
                    var reaction=EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,slab);
                    for(int line=0;line<2;line++)minimum[line]+=Math.Min(0,reaction[line]);
                }
            }
            foreach(var stock in geometry.Stock)for(int line=0;line<2;line++)
            {
                var loads=unresolved ? new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock.StockLengthMm,KnPerM=Math.Max(0,minimum[line])+input.Primary.SelfWeightKnM(stock.StockLengthMm)}}
                    : PrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,stage,edge);
                if(loads==null)continue;
                var supports=geometry.Supports(Array.IndexOf(geometry.Stock,stock));
                foreach(bool far in new[] {false,true})
                {
                    double a=far ? supports.Last() : 0,b=far ? stock.StockLengthMm : supports.First(),support=far ? a : b;
                    if(unknown.Any(q=>q.EndMm>stock.StartMm+edge+a && q.StartMm<stock.StartMm+edge+b))continue;
                    double force=0,moment=0;
                    foreach(var q in loads)
                    {
                        double lo=Math.Max(a,q.StartMm),hi=Math.Min(b,q.EndMm);if(hi<=lo)continue;
                        double load=q.KnPerM*(hi-lo)/1000;force+=load;moment+=load*Math.Abs((lo+hi)/2-support)/1000;
                    }
                    // A free stock end has zero shear and moment. The exact known
                    // tail resultant therefore gives necessary support-face demands.
                    if(Math.Abs(force)>input.Primary.ShearLimitKn+1e-7 || Math.Abs(moment)>input.Primary.MomentLimitKnM+1e-7)return false;
                }
                var limits=new double[supports.Length+2];for(int i=0;i<supports.Length;i++)limits[i+1]=supports[i];limits[limits.Length-1]=stock.StockLengthMm;
                for(int span=1;span<limits.Length;span++)
                {
                    double a=limits[span-1],b=limits[span];
                    var cuts=loads.SelectMany(q=>new[] {q.StartMm,q.EndMm}).Concat(unknown.SelectMany(q=>new[] {q.StartMm-stock.StartMm-edge,q.EndMm-stock.StartMm-edge})).Concat(new[] {a,b}).Where(x=>x>=a && x<=b).Distinct().OrderBy(x=>x).ToArray();
                    for(int i=1;i<cuts.Length;i++)
                    {
                        double mid=(cuts[i-1]+cuts[i])/2,absolute=mid+stock.StartMm+edge;
                        if(unknown.Any(q=>absolute>=q.StartMm && absolute<=q.EndMm))continue;
                        double q=Math.Abs(loads.Where(u=>mid>=u.StartMm && mid<=u.EndMm).Sum(u=>u.KnPerM)),length=(cuts[i]-cuts[i-1])/1000;
                        if(q*length>2*input.Primary.ShearLimitKn+1e-7 || q*length*length>16*input.Primary.MomentLimitKnM+1e-7)return false;
                    }
                }
            }
            return true;
            }
        }
        // Preserve transverse-option order. Earlier entries are proven failures;
        // retain the first passing entry's complete stage proof on LayoutWork so
        // it need not repeat the same necessary checks after sorting.
        private static int FirstPrimarySpansPassing(TowerInput input,Geometry geometry,CrossOption[] crosses,double edge)
        {
            for(int i=0;i<crosses.Length;i++)
            {
                bool fits=true;
                foreach(int stage in crosses[i].DesignCases)
                    if(!NecessaryPrimarySpans(input,geometry,crosses[i],edge,stage)){fits=false;break;}
                if(fits)return i;
            }
            return -1;
        }
        // Ordering only: never use this estimate to accept or reject a member.
        internal static double PrimaryPriority(TransferMember member)
        {
            double gap=member.SupportsMm[0],last=0;
            foreach(double support in member.SupportsMm){gap=Math.Max(gap,support-last);last=support;}
            gap=Math.Max(gap,member.LengthMm-last);
            double q=member.Uniform.Length==0 ? 0 : member.Uniform.Max(load=>Math.Abs(load.KnPerM));
            return q*gap*gap;
        }
        private static bool AcceptLayout(TowerInput input,TowerLayout layout)
        {
            using(var hot=input.Trace?.Hot(BeamHotStage.LayoutAcceptance))return input.TransferLayoutAcceptance(layout);
        }
        private static double PrimaryUtilization(TowerInput input,LoadedBeamAnalysis analysis)=>Math.Max(analysis.DeflectionUtilization,input.Primary.Strength(analysis));
        private static TowerLayout Evaluate(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,RowGrid grid,double edge,ResponseCache<PrimaryKey,PrimaryCheck> primaryCache,LoadedBeamAnalysis.MatrixCache matrixCache,SearchWorkCache shared,TowerLayout incumbent,bool primarySpansChecked=false)
        {
            try {return EvaluateCandidate(input,g,width,rotated,cross,grid,edge,primaryCache,matrixCache,shared,incumbent,primarySpansChecked);}
            catch(ArgumentException error) when(error.Message=="Beam load equilibrium failed." || error.Message=="Unstable beam supports.")
            {
                // A numerically rejected trial must not discard an already passing
                // stock plan or prevent the remaining generated layouts being checked.
                input.Diagnostics?.Geometry(input.DiagnosticScope+"candidate numerics",input.DiagnosticScope+error.Message+" Generated primary stocks "+string.Join(", ",g.Stock.Select(p=>p.StockLengthMm))+" mm rejected.","This trial is excluded; other support arrangements remain eligible.");
                return null;
            }
        }
        private static TowerLayout EvaluateCandidate(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,RowGrid grid,double edge,ResponseCache<PrimaryKey,PrimaryCheck> primaryCache,LoadedBeamAnalysis.MatrixCache matrixCache,SearchWorkCache shared,TowerLayout incumbent,bool primarySpansChecked)
        {
            if(!input.DeferTransferLayout)return EvaluateReference(input,g,width,rotated,cross,grid,edge,primaryCache,matrixCache,shared);
            if(input.EngineerSearch && !AcceptTransverseCases(input,cross,width,grid,edge))return null;
            bool preparedSpans=cross.PreparedReceiver!=null && input.DeferCrossingChecks;
            if(preparedSpans && !primarySpansChecked)foreach(int stage in cross.DesignCases)if(!NecessaryPrimarySpans(input,g,cross,edge,stage))return null;
            if(cross.PreparedReceiver!=null && !cross.PreparedReceiver(g,edge)){input.Trace?.Increment(BeamSearchCounter.TransferLayoutsDeferred);return null;}
            var rows=grid.Rows;var trib=grid.Tributaries;var ls=grid.LeftScales;var rs=grid.RightScales;
            if(ls.Length!=rows.Length || rs.Length!=rows.Length)throw new ArgumentException("Crossing beam row loads do not match the support rows.");
            if(!input.EngineerSearch && (cross.Left!=null && !Pass(input,cross.Left.Analysis,Math.Max(ls.Max(),input.TransferredSlab.SlabSpacing(true)/1000),1,input.Diagnostics,input.DiagnosticScope+"Left slab H20",1,()=> $"governing row T{Array.IndexOf(ls,ls.Max())+1} · stock {cross.Left.Stock:0} mm") || cross.Right!=null && !Pass(input,cross.Right.Analysis,Math.Max(rs.Max(),input.TransferredSlab.SlabSpacing(false)/1000),1,input.Diagnostics,input.DiagnosticScope+"Right slab H20",1,()=> $"governing row T{Array.IndexOf(rs,rs.Max())+1} · stock {cross.Right.Stock:0} mm")))return null;
            int n=g.Positions.Length; var left=Enumerable.Repeat(double.NegativeInfinity,n).ToArray(); var right=Enumerable.Repeat(double.NegativeInfinity,n).ToArray();
            var minLeft=Enumerable.Repeat(double.PositiveInfinity,n).ToArray(); var minRight=Enumerable.Repeat(double.PositiveInfinity,n).ToArray();
            var checkedCases=new List<PrimaryCheck[]>(); var lowerCases=new List<TransferMember[]>();
            foreach(int c in cross.DesignCases)
            {
                var checks=new PrimaryCheck[g.Stock.Length*2];
                if(input.EngineerSearch && !preparedSpans && !primarySpansChecked && !NecessaryPrimarySpans(input,g,cross,edge,c))return null;
                var lower=LowerCase(input,cross,width,grid,edge,c);if(lower==null)return null;
                for(int line=0;line<2;line++) foreach(var stock in g.Stock)
                {
                    var supports=g.MaterializeSupports(input.Trace)[Array.IndexOf(g.Stock,stock)];
                    var loadData=PrimaryLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,c,edge);
                    var uniform=loadData.Uniform;
                    if(uniform==null) {input.Diagnostics?.Geometry(input.DiagnosticScope+"primary distributed uplift","Primary UDL has an upward resultant within a loaded interval.","Review transverse bearings and asymmetric slab/crossing loads.");return null;}
                    input.Trace?.CheckCancellation();
                    var points=Array.Empty<BeamPointLoad>();
                    var key=primaryCache==null ? null : new PrimaryKey(stock.StockLengthMm,supports,loadData.Positions,loadData.Forces,uniform.Length,input.DeferCrossingChecks);
                    PrimaryCheck check;
                    if(primaryCache==null || !primaryCache.TryGetValue(key,out check))
                    {
                        // Cache the same complete UDL response used by diagrams and member checks.
                        var reused=key==null ? null : shared?.Get(key);
                        LoadedBeamAnalysis analysis;
                        if(reused!=null) {analysis=reused.Analysis;uniform=reused.Uniform;points=reused.Points;input.Trace?.Increment(BeamSearchCounter.NetworkPrimaryCacheHits);}
                        else
                        {
                            analysis=AnalyzePrimary(input,stock.StockLengthMm,supports,uniform,points,matrixCache,shared);
                        }
                        bool memberPass=Pass(input,analysis,1,2,input.Diagnostics,input.DiagnosticScope+"Primary pair · UDL",3,()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · stock {stock.StockLengthMm:0} mm · x={stock.StartMm+edge:0.###}–{stock.EndMm+edge:0.###} mm");
                        int peakLeg=Array.IndexOf(analysis.Reactions,analysis.Reactions.Max());
                        if(!input.DeferCrossingChecks)input.Diagnostics?.LegsLazy(analysis.Reactions,input.LegCapacityKn,input.DiagnosticScope+"Tower legs · UDL",()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · peak leg x={g.Positions[stock.FirstSupport+peakLeg]+edge:0.###} mm");
                        check=new PrimaryCheck {Analysis=analysis,Points=points,Uniform=uniform,Passed=memberPass && (input.DeferCrossingChecks || analysis.Reactions.Max()<=input.LegCapacityKn+1e-8)};
                        primaryCache?.Store(key,check);
                        if(key!=null && reused==null)shared?.Store(key,check);
                    }
                    else input.Trace?.Increment(BeamSearchCounter.PrimaryCacheHits);
                    if(!check.Passed)return null;
                    var a=check.Analysis;
                    for(int i=0;i<a.Reactions.Length;i++) { var envelope=line==0 ? left : right; envelope[stock.FirstSupport+i]=Math.Max(envelope[stock.FirstSupport+i],a.Reactions[i]); var minimum=line==0 ? minLeft : minRight; minimum[stock.FirstSupport+i]=Math.Min(minimum[stock.FirstSupport+i],a.Reactions[i]); }
                    checks[line*g.Stock.Length+Array.IndexOf(g.Stock,stock)]=check;
                }
                checkedCases.Add(checks); lowerCases.Add(lower);
            }
            var primaryCases=new List<TransferMember[]>();
            for(int c=0;c<checkedCases.Count;c++)
            {
                var members=new TransferMember[g.Stock.Length*2];
                for(int line=0;line<2;line++)for(int i=0;i<g.Stock.Length;i++)
                {
                    var stock=g.Stock[i];var check=checkedCases[c][line*g.Stock.Length+i];
                    members[line*g.Stock.Length+i]=new TransferMember {PrimaryMaterial=input.PrimaryMaterial,PrimarySelfWeightKnM=input.Primary.SelfWeightKnM(stock.StockLengthMm),Name="P"+(i+1)+(line==0 ? " L" : " R"),LoadCase=cross.Names[cross.DesignCases[c]],StartMm=stock.StartMm+edge,LengthMm=stock.StockLengthMm,SupportsMm=g.MaterializeSupports(input.Trace)[i],Points=check.Points,Uniform=check.Uniform,LegacyPrimaryUniform=input.EngineerPlan?.UseDesignZones==true ? LegacyPrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,cross.DesignCases[c],edge) : null,Analysis=check.Analysis};
                }
                primaryCases.Add(members);
            }
            if(input.EngineerSearch && input.TransferLayoutAcceptance!=null && cross.PreparedReceiver==null)
            {
                var provisional=new TowerLayout {EngineerSearch=true,PrimaryMaterial=input.PrimaryMaterial,IgnoreUplift=input.IgnoreUplift,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,LongitudinalStartMm=input.LongitudinalStartMm,DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,LegCapacityKn=input.LegCapacityKn,
                    StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=g.Stock.Select(p=>new TowerStockPiece {StartMm=p.StartMm+edge,StockLengthMm=p.StockLengthMm,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport}).ToArray()},
                    Transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerReceiverCount,FullLoadingOnly=input.FullLoadingOnly,RowsMm=rows.Select(x=>x+edge).ToArray(),LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,CaseNames=cross.DesignCases.Select(i=>cross.Names[i]).ToArray(),LowerCases=lowerCases.ToArray(),PrimaryCases=primaryCases.ToArray(),UpperMembers=new TransferMember[0],ColumnSupportLoads=new ColumnEndStripLoad[0][]}};
                if(!AcceptLayout(input,OffsetReceiver(input,provisional)))
                {input.Trace?.Increment(BeamSearchCounter.TransferLayoutsDeferred);return null;}
            }
            // Ranking uses the identical member utilization arithmetic. Preserve strict
            // original-order ties; only candidates outside the economic bound are deferred.
            var upper=new List<TransferMember>();
            foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null)) upper.Add(new TransferMember {H20Count=1,Name=u==cross.Left ? "Upper left" : "Upper right",LoadCase="Slab · governing row",StartMm=u.Edge,LengthMm=u.Stock,SupportsMm=new[] {u.Foot-u.Edge,u.Bay-u.OtherFoot-u.Edge},Points=Array.Empty<BeamPointLoad>(),Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=u.Stock,KnPerM=u.Load*Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000)}},Analysis=u.Analysis.Scale(Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000))});
            if(!input.EngineerSearch && input.TransferredSlab.IsNetwork && incumbent!=null && Economy(g,cross,grid,input)>Economy(incumbent)+.01)
            {input.Trace?.Increment(BeamSearchCounter.TransferLayoutsDeferred);return null;}
            input.Trace?.Increment(BeamSearchCounter.TransferLayoutsBuilt);
            var pieces=g.Stock.Select((s,i)=>new TowerStockPiece {IsCustomLength=!input.PrimaryStockLengthsMm.Contains(s.StockLengthMm),StartMm=s.StartMm+edge,StockLengthMm=s.StockLengthMm,FirstSupport=s.FirstSupport,LastSupport=s.LastSupport,
                StrengthUtilization=primaryCases.SelectMany(c=>new[] {c[i],c[i+g.Stock.Length]}).Max(m=>m.StrengthUtilization),MomentKnM=primaryCases.Max(c=>Math.Max(c[i].Analysis.MomentKnM,c[i+g.Stock.Length].Analysis.MomentKnM)),ShearKn=primaryCases.Max(c=>Math.Max(c[i].Analysis.ShearKn,c[i+g.Stock.Length].Analysis.ShearKn)),
                DeflectionMm=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionMm,c[i+g.Stock.Length].Analysis.DeflectionMm)),DeflectionUtilization=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionUtilization,c[i+g.Stock.Length].Analysis.DeflectionUtilization)),GoverningDeflectionRegion=input.TransferredSlab.IncludeConcreteEndStrips ? "Envelope of primary UDL + end-strip stages" : "Envelope of primary UDL stages"}).ToArray();
            for(int i=0;i<pieces.Length;i++)
            {
                var governing=primaryCases.SelectMany(c=>new[] {c[i],c[i+g.Stock.Length]}).OrderByDescending(m=>m.Analysis.DeflectionUtilization).First().Analysis;
                pieces[i].GoverningDeflectionLimitMm=governing.GoverningLengthMm/input.DeflectionDenominator;
                pieces[i].GoverningDeflectionMm=governing.DeflectionUtilization*pieces[i].GoverningDeflectionLimitMm;
            }
            var transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerNetwork==null ? 0 : input.TransferredSlab.EngineerReceiverCount,UnitLowerCases=UnitLowerCases(input,cross,width),UnitPointLabels=cross.UnitPointLabels,SlabSupportStartMm=input.TransferredSlab.RowStart(input,input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm))),SlabSupportEndMm=input.TransferredSlab.RowStart(input,input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm)))+(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm))),ColumnSupportLoads=SelectColumnCases(input,cross,edge),LegacyColumnSupportLoads=input.EngineerPlan?.UseDesignZones==true ? SelectLegacyColumnCases(input,cross,edge) : null,CrossFormwork=input.TransferredSlab.CrossFormwork,LowerCustom=!input.StockLengthsMm.Contains(cross.Stock),LeftUpperScales=ls,RightUpperScales=rs,EnteredUpperSpacingMm=input.TransferredSlab.UpperSpacingMm,LeftSlabSpacingMm=input.TransferredSlab.LeftSlabSpacingMm,RightSlabSpacingMm=input.TransferredSlab.RightSlabSpacingMm,HasIndividualSpacings=input.TransferredSlab.HasIndividualSpacings,RowsMm=rows.Select(x=>x+edge).ToArray(),SlabRowIndices=grid.SlabRowIndices,SharedSlabRowsMm=grid.SharedSlabRowsMm,SlabSourceRowIndices=grid.SlabSourceRowIndices,LeftSlabSourceScales=grid.SharedSlabRowsMm==null ? null : grid.LeftSourceScales,RightSlabSourceScales=grid.SharedSlabRowsMm==null ? null : grid.RightSourceScales,HasCustomPlywoodJoints=grid.Adjusted,SettingOutRowsMm=grid.SettingOutRows?.Select(x=>x+edge).ToArray(),SettingOutDesignRows=grid.SettingOutDesignRows,TributaryWidthsMm=trib,LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,LeftFootMm=cross.Left?.Foot??0,RightFootMm=cross.Right?.Foot??0,
                LeftUpperStockMm=cross.Left?.Stock??0,RightUpperStockMm=cross.Right?.Stock??0,LeftUpperCustom=cross.Left?.Custom??false,RightUpperCustom=cross.Right?.Custom??false,LeftUpperEdgeMm=cross.Left?.Edge??0,RightUpperEdgeMm=cross.Right?.Edge??0,
                LeftFootReactionKn=(cross.Left?.Analysis.Reactions[0]??0)*ls.Max(),RightFootReactionKn=(cross.Right?.Analysis.Reactions[0]??0)*rs.Max(),
                BeamLoadKn=input.TransferredSlab.TotalBeamLineLoadKnM*input.LengthMm/1000,
                CoveredSlabAreaM2=(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm)))*((cross.Left?.Stock??0)+(cross.Right?.Stock??0))/1e6,
                ShutterLoadKn=PhysicalShutterLoadKn(input,cross),
                SlabLoadKn=PhysicalSlabLoadKn(input,cross),PrimaryCases=primaryCases.ToArray(),LowerCases=lowerCases.ToArray(),UpperMembers=upper.ToArray(),CaseNames=cross.DesignCases.Select(i=>cross.Names[i]).ToArray(),FullLoadingOnly=input.FullLoadingOnly};
            // Two-support lower and upper bearers are statically determinate: support movement adds an affine displacement without altering reactions.
            if(!input.TransferredSlab.IsNetwork) for(int c=0;c<cross.DesignCases.Length;c++) for(int row=0;row<rows.Length;row++) foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null))
            {
                input.Trace?.CheckCancellation();
                bool isLeft=u==cross.Left; if(!(transfer.CaseNames[c].Contains(isLeft ? "left" : "right") || transfer.CaseNames[c].Contains("both"))) continue;
                int run=Array.FindIndex(g.Stock,s=>rows[row]>=s.StartMm && rows[row]<s.EndMm);
                double dl=primaryCases[c][run].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2],dr=primaryCases[c][run+g.Stock.Length].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2];
                double x=input.TransferredSlab.BeamCentreOffsetMm+(isLeft ? -1 : 1)*(input.TransferredSlab.BeamWidthMm/2+u.Foot);
                double foot=lowerCases[c][row].Analysis.Response(x-cross.Start)[2]+dl+(dr-dl)*(x+width/2)/width;
                var a=u.Analysis.Scale(transfer.UpperScale(isLeft,row));
                transfer.SlabMovementMm=Math.Max(transfer.SlabMovementMm,a.DeflectionWithOffsetMm(foot));
            }
            var all=primaryCases.SelectMany(c=>c).Concat(lowerCases.SelectMany(c=>c)).Concat(upper).ToArray();
            double peak=Math.Max(left.Max(),right.Max()),util=Math.Max(peak/input.LegCapacityKn,all.Max(m=>m.Utilization));
            return new TowerLayout {EngineerSearch=input.EngineerSearch,PrimaryMaterial=input.PrimaryMaterial,IgnoreUplift=input.IgnoreUplift,Transfer=transfer,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,LongitudinalStartMm=input.LongitudinalStartMm,ConcreteEndOverhangMm=edge-input.LongitudinalStartMm,LeftReactions=left,RightReactions=right,MinimumLeftReactions=minLeft,MinimumRightReactions=minRight,
                LeftLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[0])/(trib.Sum()/1000),RightLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[1])/(trib.Sum()/1000),LegCapacityKn=input.LegCapacityKn,MaxLegKn=peak,
                StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=pieces,Reactions=left.Zip(right,Math.Max).ToArray()},DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,MomentKnM=all.Max(m=>m.Analysis.MomentKnM),ShearKn=all.Max(m=>m.Analysis.ShearKn),DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm),Utilization=util,Governing=peak/input.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name};
        }
        private static TowerLayout EvaluateReference(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,RowGrid grid,double edge,ResponseCache<PrimaryKey,PrimaryCheck> primaryCache,LoadedBeamAnalysis.MatrixCache matrixCache,SearchWorkCache shared)
        {
            if(input.EngineerSearch && !AcceptTransverseCases(input,cross,width,grid,edge))return null;
            var rows=grid.Rows;var trib=grid.Tributaries;var ls=grid.LeftScales;var rs=grid.RightScales;
            if(ls.Length!=rows.Length || rs.Length!=rows.Length)throw new ArgumentException("Crossing beam row loads do not match the support rows.");
            if(!input.EngineerSearch && (cross.Left!=null && !Pass(input,cross.Left.Analysis,Math.Max(ls.Max(),input.TransferredSlab.SlabSpacing(true)/1000),1,input.Diagnostics,input.DiagnosticScope+"Left slab H20",1,()=> $"governing row T{Array.IndexOf(ls,ls.Max())+1} · stock {cross.Left.Stock:0} mm") || cross.Right!=null && !Pass(input,cross.Right.Analysis,Math.Max(rs.Max(),input.TransferredSlab.SlabSpacing(false)/1000),1,input.Diagnostics,input.DiagnosticScope+"Right slab H20",1,()=> $"governing row T{Array.IndexOf(rs,rs.Max())+1} · stock {cross.Right.Stock:0} mm")))return null;
            int n=g.Positions.Length; var left=Enumerable.Repeat(double.NegativeInfinity,n).ToArray(); var right=Enumerable.Repeat(double.NegativeInfinity,n).ToArray();
            var minLeft=Enumerable.Repeat(double.PositiveInfinity,n).ToArray(); var minRight=Enumerable.Repeat(double.PositiveInfinity,n).ToArray();
            var primaryCases=new List<TransferMember[]>(); var lowerCases=new List<TransferMember[]>();
            foreach(int c in cross.DesignCases)
            {
                var members=new List<TransferMember>();
                if(input.EngineerSearch && !NecessaryPrimarySpans(input,g,cross,edge,c))return null;
                var lower=LowerCase(input,cross,width,grid,edge,c);if(lower==null)return null;
                for(int line=0;line<2;line++) foreach(var stock in g.Stock)
                {
                    var supports=g.MaterializeSupports(input.Trace)[Array.IndexOf(g.Stock,stock)];
                    var loadData=PrimaryLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,c,edge);
                    var uniform=loadData.Uniform;
                    if(uniform==null) {input.Diagnostics?.Geometry(input.DiagnosticScope+"primary distributed uplift","Primary UDL has an upward resultant within a loaded interval.","Review transverse bearings and asymmetric slab/crossing loads.");return null;}
                    input.Trace?.CheckCancellation();
                    var points=Array.Empty<BeamPointLoad>();
                    var key=primaryCache==null ? null : new PrimaryKey(stock.StockLengthMm,supports,loadData.Positions,loadData.Forces,uniform.Length,input.DeferCrossingChecks);
                    PrimaryCheck check;
                    if(primaryCache==null || !primaryCache.TryGetValue(key,out check))
                    {
                        // Cache the same complete UDL response used by diagrams and member checks.
                        var reused=key==null ? null : shared?.Get(key);
                        LoadedBeamAnalysis analysis;
                        if(reused!=null) {analysis=reused.Analysis;uniform=reused.Uniform;points=reused.Points;input.Trace?.Increment(BeamSearchCounter.NetworkPrimaryCacheHits);}
                        else
                        {
                            analysis=AnalyzePrimary(input,stock.StockLengthMm,supports,uniform,points,matrixCache,shared);
                        }
                        bool memberPass=Pass(input,analysis,1,2,input.Diagnostics,input.DiagnosticScope+"Primary pair · UDL",3,()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · stock {stock.StockLengthMm:0} mm · x={stock.StartMm+edge:0.###}–{stock.EndMm+edge:0.###} mm");
                        int peakLeg=Array.IndexOf(analysis.Reactions,analysis.Reactions.Max());
                        if(!input.DeferCrossingChecks)input.Diagnostics?.LegsLazy(analysis.Reactions,input.LegCapacityKn,input.DiagnosticScope+"Tower legs · UDL",()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · peak leg x={g.Positions[stock.FirstSupport+peakLeg]+edge:0.###} mm");
                        check=new PrimaryCheck {Analysis=analysis,Points=points,Uniform=uniform,Passed=memberPass && (input.DeferCrossingChecks || analysis.Reactions.Max()<=input.LegCapacityKn+1e-8)};
                        primaryCache?.Store(key,check);
                        if(key!=null && reused==null)shared?.Store(key,check);
                    }
                    else input.Trace?.Increment(BeamSearchCounter.PrimaryCacheHits);
                    if(!check.Passed)return null;
                    var a=check.Analysis;
                    for(int i=0;i<a.Reactions.Length;i++) { var envelope=line==0 ? left : right; envelope[stock.FirstSupport+i]=Math.Max(envelope[stock.FirstSupport+i],a.Reactions[i]); var minimum=line==0 ? minLeft : minRight; minimum[stock.FirstSupport+i]=Math.Min(minimum[stock.FirstSupport+i],a.Reactions[i]); }
                    members.Add(new TransferMember {PrimaryMaterial=input.PrimaryMaterial,PrimarySelfWeightKnM=input.Primary.SelfWeightKnM(stock.StockLengthMm),Name="P"+(Array.IndexOf(g.Stock,stock)+1)+(line==0 ? " L" : " R"),LoadCase=cross.Names[c],StartMm=stock.StartMm+edge,LengthMm=stock.StockLengthMm,SupportsMm=supports,Points=check.Points,Uniform=check.Uniform,LegacyPrimaryUniform=input.EngineerPlan?.UseDesignZones==true ? LegacyPrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,c,edge) : null,Analysis=a});
                }
                primaryCases.Add(members.ToArray()); lowerCases.Add(lower.ToArray());
            }
            input.Trace?.Increment(BeamSearchCounter.TransferLayoutsBuilt);
            var pieces=g.Stock.Select((s,i)=>new TowerStockPiece {IsCustomLength=!input.PrimaryStockLengthsMm.Contains(s.StockLengthMm),StartMm=s.StartMm+edge,StockLengthMm=s.StockLengthMm,FirstSupport=s.FirstSupport,LastSupport=s.LastSupport,
                StrengthUtilization=primaryCases.SelectMany(c=>new[] {c[i],c[i+g.Stock.Length]}).Max(m=>m.StrengthUtilization),MomentKnM=primaryCases.Max(c=>Math.Max(c[i].Analysis.MomentKnM,c[i+g.Stock.Length].Analysis.MomentKnM)),ShearKn=primaryCases.Max(c=>Math.Max(c[i].Analysis.ShearKn,c[i+g.Stock.Length].Analysis.ShearKn)),
                DeflectionMm=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionMm,c[i+g.Stock.Length].Analysis.DeflectionMm)),DeflectionUtilization=primaryCases.Max(c=>Math.Max(c[i].Analysis.DeflectionUtilization,c[i+g.Stock.Length].Analysis.DeflectionUtilization)),GoverningDeflectionRegion=input.TransferredSlab.IncludeConcreteEndStrips ? "Envelope of primary UDL + end-strip stages" : "Envelope of primary UDL stages"}).ToArray();
            for(int i=0;i<pieces.Length;i++)
            {
                var governing=primaryCases.SelectMany(c=>new[] {c[i],c[i+g.Stock.Length]}).OrderByDescending(m=>m.Analysis.DeflectionUtilization).First().Analysis;
                pieces[i].GoverningDeflectionLimitMm=governing.GoverningLengthMm/input.DeflectionDenominator;
                pieces[i].GoverningDeflectionMm=governing.DeflectionUtilization*pieces[i].GoverningDeflectionLimitMm;
            }
            var upper=new List<TransferMember>();
            foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null)) upper.Add(new TransferMember {H20Count=1,Name=u==cross.Left ? "Upper left" : "Upper right",LoadCase="Slab · governing row",StartMm=u.Edge,LengthMm=u.Stock,SupportsMm=new[] {u.Foot-u.Edge,u.Bay-u.OtherFoot-u.Edge},Points=Array.Empty<BeamPointLoad>(),Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=u.Stock,KnPerM=u.Load*Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000)}},Analysis=u.Analysis.Scale(Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000))});
            var transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerNetwork==null ? 0 : input.TransferredSlab.EngineerReceiverCount,UnitLowerCases=UnitLowerCases(input,cross,width),UnitPointLabels=cross.UnitPointLabels,SlabSupportStartMm=input.TransferredSlab.RowStart(input,input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm))),SlabSupportEndMm=input.TransferredSlab.RowStart(input,input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm)))+(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm))),ColumnSupportLoads=SelectColumnCases(input,cross,edge),LegacyColumnSupportLoads=input.EngineerPlan?.UseDesignZones==true ? SelectLegacyColumnCases(input,cross,edge) : null,CrossFormwork=input.TransferredSlab.CrossFormwork,LowerCustom=!input.StockLengthsMm.Contains(cross.Stock),LeftUpperScales=ls,RightUpperScales=rs,EnteredUpperSpacingMm=input.TransferredSlab.UpperSpacingMm,LeftSlabSpacingMm=input.TransferredSlab.LeftSlabSpacingMm,RightSlabSpacingMm=input.TransferredSlab.RightSlabSpacingMm,HasIndividualSpacings=input.TransferredSlab.HasIndividualSpacings,RowsMm=rows.Select(x=>x+edge).ToArray(),SlabRowIndices=grid.SlabRowIndices,SharedSlabRowsMm=grid.SharedSlabRowsMm,SlabSourceRowIndices=grid.SlabSourceRowIndices,LeftSlabSourceScales=grid.SharedSlabRowsMm==null ? null : grid.LeftSourceScales,RightSlabSourceScales=grid.SharedSlabRowsMm==null ? null : grid.RightSourceScales,HasCustomPlywoodJoints=grid.Adjusted,SettingOutRowsMm=grid.SettingOutRows?.Select(x=>x+edge).ToArray(),SettingOutDesignRows=grid.SettingOutDesignRows,TributaryWidthsMm=trib,LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,LeftFootMm=cross.Left?.Foot??0,RightFootMm=cross.Right?.Foot??0,
                LeftUpperStockMm=cross.Left?.Stock??0,RightUpperStockMm=cross.Right?.Stock??0,LeftUpperCustom=cross.Left?.Custom??false,RightUpperCustom=cross.Right?.Custom??false,LeftUpperEdgeMm=cross.Left?.Edge??0,RightUpperEdgeMm=cross.Right?.Edge??0,
                LeftFootReactionKn=(cross.Left?.Analysis.Reactions[0]??0)*ls.Max(),RightFootReactionKn=(cross.Right?.Analysis.Reactions[0]??0)*rs.Max(),
                BeamLoadKn=input.TransferredSlab.TotalBeamLineLoadKnM*input.LengthMm/1000,
                CoveredSlabAreaM2=(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*(edge-input.LongitudinalStartMm)))*((cross.Left?.Stock??0)+(cross.Right?.Stock??0))/1e6,
                ShutterLoadKn=PhysicalShutterLoadKn(input,cross),
                SlabLoadKn=PhysicalSlabLoadKn(input,cross),PrimaryCases=primaryCases.ToArray(),LowerCases=lowerCases.ToArray(),UpperMembers=upper.ToArray(),CaseNames=cross.DesignCases.Select(i=>cross.Names[i]).ToArray(),FullLoadingOnly=input.FullLoadingOnly};
            // Two-support lower and upper bearers are statically determinate: support movement adds an affine displacement without altering reactions.
            if(!input.TransferredSlab.IsNetwork) for(int c=0;c<cross.DesignCases.Length;c++) for(int row=0;row<rows.Length;row++) foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null))
            {
                input.Trace?.CheckCancellation();
                bool isLeft=u==cross.Left; if(!(transfer.CaseNames[c].Contains(isLeft ? "left" : "right") || transfer.CaseNames[c].Contains("both"))) continue;
                int run=Array.FindIndex(g.Stock,s=>rows[row]>=s.StartMm && rows[row]<s.EndMm);
                double dl=primaryCases[c][run].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2],dr=primaryCases[c][run+g.Stock.Length].Analysis.Response(rows[row]-g.Stock[run].StartMm)[2];
                double x=input.TransferredSlab.BeamCentreOffsetMm+(isLeft ? -1 : 1)*(input.TransferredSlab.BeamWidthMm/2+u.Foot);
                double foot=lowerCases[c][row].Analysis.Response(x-cross.Start)[2]+dl+(dr-dl)*(x+width/2)/width;
                var a=u.Analysis.Scale(transfer.UpperScale(isLeft,row));
                transfer.SlabMovementMm=Math.Max(transfer.SlabMovementMm,a.DeflectionWithOffsetMm(foot));
            }
            var all=primaryCases.SelectMany(c=>c).Concat(lowerCases.SelectMany(c=>c)).Concat(upper).ToArray();
            double peak=Math.Max(left.Max(),right.Max()),util=Math.Max(peak/input.LegCapacityKn,all.Max(m=>m.Utilization));
            return new TowerLayout {EngineerSearch=input.EngineerSearch,PrimaryMaterial=input.PrimaryMaterial,IgnoreUplift=input.IgnoreUplift,Transfer=transfer,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,LongitudinalStartMm=input.LongitudinalStartMm,ConcreteEndOverhangMm=edge-input.LongitudinalStartMm,LeftReactions=left,RightReactions=right,MinimumLeftReactions=minLeft,MinimumRightReactions=minRight,
                LeftLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[0])/(trib.Sum()/1000),RightLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[1])/(trib.Sum()/1000),LegCapacityKn=input.LegCapacityKn,MaxLegKn=peak,
                StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=pieces,Reactions=left.Zip(right,Math.Max).ToArray()},DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,MomentKnM=all.Max(m=>m.Analysis.MomentKnM),ShearKn=all.Max(m=>m.Analysis.ShearKn),DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm),Utilization=util,Governing=peak/input.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name};
        }
    }
}
