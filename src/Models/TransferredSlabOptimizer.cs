using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard
{
    public sealed class TransferredSlabInput
    {
        internal TransferredSlabInput Copy()=>(TransferredSlabInput)MemberwiseClone();
        public double BeamWidthMm, BeamLineLoadKnM, UpperSpacingMm;
        // Point reactions from slab-bearing ledgers, per metre along the crossing span.
        internal BeamPointLoad[] BeamEdgeLoadsKnM=new BeamPointLoad[0];
        internal double TotalBeamLineLoadKnM=>BeamLineLoadKnM+BeamEdgeLoadsKnM.Sum(p=>p.Kn);
        internal double LocalRowGapMm;
        internal double RowGapMm=>LocalRowGapMm>0 ? LocalRowGapMm : UpperSpacingMm;
        public double PlywoodSheetLengthMm;
        public double LeftSlabSpacingMm, RightSlabSpacingMm;
        internal double[] SharedRowsMm, LeftBaselineWidthsMm, RightBaselineWidthsMm;
        internal bool HasIndividualSpacings=>SharedRowsMm!=null || LeftSlabSpacingMm>0 || RightSlabSpacingMm>0;
        public double SlabSpacing(bool left)=>(left ? LeftSlabSpacingMm : RightSlabSpacingMm)>0 ? (left ? LeftSlabSpacingMm : RightSlabSpacingMm) : UpperSpacingMm;
        public double LeftBayMm, RightBayMm, LeftAreaLoadKnM2, RightAreaLoadKnM2;
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
            if(LeftPlan!=null)LeftPlan.Validate(LeftBayMm,input.StockLengthsMm,input.AllowCustomSlabLengths);
            if(RightPlan!=null)RightPlan.Validate(RightBayMm,input.StockLengthsMm,input.AllowCustomSlabLengths);
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
        // Physical setting-out can move/add beam-only bearers. Design rows and nominal loads stay fixed.
        public double[] SettingOutRowsMm;
        public int[] SettingOutDesignRows;
        public int PhysicalRowCount=>SettingOutRowsMm?.Length??RowsMm.Length;
        public double PhysicalRowPosition(int row)=>SettingOutRowsMm?[row]??RowsMm[row];
        public int PhysicalRowDesignIndex(int row)=>SettingOutDesignRows?[row]??row;
        public int AddedSecondaryCount=>PhysicalRowCount-RowsMm.Length;

        // Shared slab-row ordinal -> this beam's lower-secondary index. Other rows carry beam concrete only.
        public int[] SlabRowIndices;
        public int BeamOnlyRowCount=>HasIndividualSpacings ? Enumerable.Range(0,RowsMm.Length).Count(i=>(LeftUpperScales?[i]??0)==0 && (RightUpperScales?[i]??0)==0) : SlabRowIndices==null ? 0 : RowsMm.Length-SlabRowIndices.Length;
        public double[] LeftUpperScales, RightUpperScales;
        public double UpperDesignWidthMm(bool left,int row)=>UpperScale(left,row)*1000;
        public string UpperSpacingDescription(bool left,int row)
        {
            var active=Enumerable.Range(0,RowsMm.Length).Where(i=>UpperScale(left,i)>0).ToArray();
            int index=Array.IndexOf(active,row);
            string before=index>0 ? (RowsMm[row]-RowsMm[active[index-1]]).ToString("0.###",CultureInfo.CurrentCulture) : "—";
            string after=index>=0 && index<active.Length-1 ? (RowsMm[active[index+1]]-RowsMm[row]).ToString("0.###",CultureInfo.CurrentCulture) : "—";
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
        public string Schedule => (ColumnSupportNotice=="" ? "" : ColumnSupportNotice+"\n")+(SettingOutRowsMm==null ? "" : $"Local H20 setting-out: {AddedSecondaryCount} extra H20(s); design spacing remains {EnteredUpperSpacingMm:0.###} mm and primary UDL is unchanged.\n")+(BeamOnlyRowCount>0 ? $"{BeamOnlyRowCount} local beam-only secondary rows; no additional slab feet or slab tributary.\n" : "")+(UpperMembers.Length>0 ? UpperTransferPlan.SiteArrangement+(HasIndividualSpacings ? " Maximum slab row spacing: "+string.Join("; ",new[] {LeftSlabSpacingMm>0 ? $"left {LeftSlabSpacingMm:0.###} mm" : null,RightSlabSpacingMm>0 ? $"right {RightSlabSpacingMm:0.###} mm" : null}.Where(s=>s!=null))+".\n" : $" Maximum row spacing {EnteredUpperSpacingMm:0.###} mm.\n") : "")+(CrossFormwork ? "Cross-beam footprints exclude slab H20 rows and their upper feet. Remaining rows use their assigned tributaries; main beam concrete retains the full-spacing design.\n" : "")+(SettingOutRowsMm==null ? string.Join("\n",RowsMm.Select((x,i)=>$"T{i+1}: x={x:0.###} mm · actual local beam tributary {TributaryWidthsMm[i]:0.###} mm · effective beam design width {Math.Max(TributaryWidthsMm[i],EnteredUpperSpacingMm):0.###} mm · 1 × {LowerStockLengthMm:0} mm")) : string.Join("\n",SettingOutRowsMm.Select((x,i)=>$"T{i+1}: x={x:0.###} mm · 1 × {LowerStockLengthMm:0} mm")))
            + "\n"+string.Join("\n",Enumerable.Range(0,RowsMm.Length).SelectMany(row=>new[] {true,false}.Where(left=>(left ? LeftUpperStockMm : RightUpperStockMm)>0 && UpperScale(left,row)>0).Select(left=>$"T{row+1} {(left ? "left" : "right")}: "+UpperSpacingDescription(left,row))))
            + $"\nLower transverse stock{(LowerCustom ? " (custom, 25 mm increments)" : "")}: {LowerStockStartMm:0.###} to {LowerStockStartMm+LowerStockLengthMm:0.###} mm from beam centre"
            + (LeftUpperStockMm > 0 ? $"\nLeft slab H20: 1 × {LeftUpperStockMm:0} mm{(LeftUpperCustom ? " custom" : "")} per row · edge {LeftUpperEdgeMm:0} mm · equal foot setback {LeftFootMm:0.###} mm at both faces" : "")
            + (RightUpperStockMm > 0 ? $"\nRight slab H20: 1 × {RightUpperStockMm:0} mm{(RightUpperCustom ? " custom" : "")} per row · edge {RightUpperEdgeMm:0} mm · equal foot setback {RightFootMm:0.###} mm at both faces" : "")
            + $"\nSlab H20 covered area: {CoveredSlabAreaM2:0.00} m² · physical slab reference including external end supports: {SlabLoadKn:0.00} kN (including shutter {ShutterLoadKn:0.00} kN at 100 mm outward from each beam face)\nLoad cases: {string.Join("; ", CaseNames)}\n"
            + string.Join("\n", PrimaryCases.SelectMany(x => x).Concat(LowerCases.SelectMany(x => x)).Concat(UpperMembers).Select(m => $"{m.Name} · {m.LoadCase}: M {m.Analysis.MomentKnM:0.00}/{m.MomentLimitKnM:0.##} kNm · V {m.Analysis.ShearKn:0.00}/{m.ShearLimitKn:0.##} kN · d {m.Analysis.DeflectionMm:0.00} mm · minimum signed reaction {m.MinimumReactionKn:0.######} kN · utilization {m.Utilization*100:0.0}%"+(m.SubsetDeflectionBound>0 ? $" · all-subset bounds M {m.SubsetMomentBoundKnM:0.###} kNm, V {m.SubsetShearBoundKn:0.###} kN, d-utilization {m.SubsetDeflectionBound*100:0.0}%" : "")));
        public const string Assumptions = "Slab load transfers through single slab H20s and vertical upper feet, single lower secondary H20s and selected longitudinal primaries to tower legs. The upper assembly has no D3 geometry. Production checks the fully loaded beam, connected slabs and applicable crossings together. Partial construction stages are outside this result. The slab area over upper H20 stock loads that stock; each 200–350 mm slab-edge strip loads its own side shutter, acting 100 mm outward from the adjacent beam face. These edge loads are additional to the upper-foot loads. Slab above the concrete beam width is not counted again. Production main primaries use concrete tributary zones: Beam + Slab outside crossings and assigned Beam + Cross Beam across each full physical crossing width. Dedicated tower loads and slab ledgers are allocated once; historical replay retains the former bearing-derived model. Uncovered longitudinal end-strip loads are assigned to external column-side supports, whose capacity requires separate verification. Slab-secondary longitudinal strips outside shared coverage are assigned to assumed external supports; in-coverage tributaries, genuine crossing loads and transverse cantilevers remain checked. Local beam-only rows add no slab feet or slab tributary. Dedicated crossing formwork includes the full concrete span, including end strips, using a UDL scaled over centred primary stock coverage; secondary end-zone sheathing influences remain explicitly calculated. Each slab H20 is one length with no upper joints. Single-beam transfer mode retains its legacy upper-length fallback: if no catalogue length fits 200–350 mm edge strips, a custom upper length is rounded down to a multiple of 25 mm, keeping each edge closest to 200 mm. Network mode respects the custom-length setting for upper, lower and primary H20s. If full catalogue network transfer fails, separately supported slab bays may be excluded from transfer; the search minimizes their number before ranking calculated supports/timber. Their slab and shutter loads are excluded from transfer and their formwork design and quantities are excluded. Modern crossing beams are still designed using their selected shared or dedicated support arrangement; only historical slab-row crossing loads are excluded with an omitted slab bay. WS10 primary lengths always use uncut catalogue stock. H20 primary and lower secondary lengths use catalogue stock unless custom H20 lengths are enabled; additional lengths are multiples of 25 mm. The custom primary cover search adds bounded balanced runs and small joint shifts. The original stock-only solution is retained when it ranks better. Feet have at least 250 mm face-to-centre clearance and 150 mm inset from the upper stock end. The network uses 2–15 actual parallel beams, each with its own width/depth, and one slab bay between neighbours. Slab H20 stock is centred and its feet have equal setbacks from both concrete faces, giving equal overhangs; this arrangement repeats at every row in a bay. Uniform slab loading gives equal reactions at the two slab feet. Actual receiving-secondary, primary and tower-leg reactions can differ and are checked individually for the applicable loading case. Each lower beam is independently checked against the entered permissible leg load, and negative bearing reactions invalidate a candidate. Shared slab coverage and support rows align upper supports across all beams. Each beam can use longer primary coverage; its end rows carry the actual extra beam-concrete tributary load. In explicit Legacy slab rows mode, optional perpendicular beams span one clear slab bay and transfer through the existing slab H20s and shutter-edge strips during that bay loading stage. The existing slab row load is retained once; the additional crossing-beam concrete and surcharge are assigned only to H20 rows physically beneath that footprint, using midpoint tributaries between those rows. The added area load is the crossing beam total-depth load minus the existing slab-thickness load; concrete, formwork and working allowances are not counted twice. Crossing beam footprints must lie within longitudinal H20 coverage and cannot overlap within a bay. Legacy bearing and level-change details are outside the legacy load-transfer model. Modern cross-beam formwork has separately reported elevation, bearing and support checks. Two-support upper/lower bearers are statically determinate; different lower-foot movements add an affine displacement to the upper beam. Single-beam mode retains the mirrored opposite-beam assumption; network mode uses both actual beam responses. Slab and lower secondary H20 limits are M=5 kNm, V=11 kN and EI=449.8756 kNm². Double-H20 longitudinal primary limits are M=10 kNm, V=22 kN and EI=899.7512 kNm², assuming equal load sharing. The selected primary material basis defines WS10 checks and catalogue restrictions. Longitudinal primaries use distributed design loads derived from the per-metre beam and slab reactions, independent of exact secondary row positions. Beam-concrete UDL covers primary stock; production network slab UDL uses the selected slab allocation over normal zones, with crossing ledger loads assigned once. Historical replay uses the intersection of primary and shared slab support coverage. Physical loads outside either supported region are assigned once to unverified external column-side supports. Crossing increments retain their actual footprints. Slab and lower-secondary design reactions use at least the full entered spacing, including end rows. Covered-area totals remain physical reference quantities; design loads can exceed them. Additional crossing-beam load remains distributed over its physical footprint, rather than averaged over the whole beam. Primary moment, shear, deflection and tower-leg checks, diagrams and reported utilization all use this same UDL model. Secondary maximum spacing and bearing details still require site control. Relative H20 deflection uses each support span/overhang; total slab movement is reported without an assumed project tolerance. Production search ranks legs, tower grouping, brace preference, symmetry and then material economy, counting shared upper timber once. Historical replay retains its economy-led ranking. Search candidates are bounded; a global optimum is not proven. Dedicated crossing junctions use receiving H20s directly at matching soffits; a deeper parallel beam carries each crossing-end strip through its existing side shutter at the midpoint of its selected width (normally 200 mm / 100 mm offset; locally 175 mm / 87.5 mm offset only after the standard detail fails). These loads are included in receiving secondary, primary and individual tower-leg checks. Crossing primary stock ends retain their selected local 200 mm or exceptional 175 mm allowances from the parallel concrete faces; exceptions are identified in the schedule and plan. Plans show towers and primary stock lengths; a toggle shows single secondaries, which are also shown in section with stock length and spacing. Sheathing, shutter/connection strength, bearing, upper-support capacity/stability, lower tower stability and foundations require separate design. Doka H20 instructions restrict heavy point loads with wide spans; numeric member checks alone do not approve foot or shutter bearing details.";
    }
    /// <summary>Transverse force transfer with continuous distributed primary design loads.</summary>
    public static class TransferredSlabOptimizer
    {
        internal sealed class Geometry { public TowerStockPiece[] Stock; public double[] Positions; public bool[] Joined; public double[][] MemberSupports; }
        // Created for one pass only: stock, setback and exclusion settings are constant.
        internal sealed class GeometryCache
        {
            internal Geometry[] Fixed;
            private readonly ConcurrentDictionary<string,Lazy<Geometry[]>> items = new ConcurrentDictionary<string,Lazy<Geometry[]>>();
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
            public double Start, Stock; public Upper Left, Right; public LoadedBeamAnalysis[] Cases; public string[] Names;
            public TransferMember[] UnitMembers;public string[][] UnitPointLabels;
            public EngineerPrimaryPlan PositionedPlan;
            // Only the latest grid is retained, keeping memory bounded in single-beam searches.
            public string RowKey,TransverseKey;
            public bool TransversePassed;
            public int[] DesignCases;
            public TransferMember[][] LowerByCase;
            public bool[] LowerCaseComputed;
            // Transverse response depends on the load case and three exact tributaries,
            // not the longitudinal station. Bound this cache within one receiving-beam solve.
            public readonly Dictionary<Tuple<int,double,double,double>,LoadedBeamAnalysis> RowAnalyses = new Dictionary<Tuple<int,double,double,double>,LoadedBeamAnalysis>();
        }
        private sealed class RowGrid
        {
            public double[] Rows, Tributaries, LeftScales, RightScales, LeftPhysicalScales, RightPhysicalScales;
            public int[] SlabRowIndices;
            public string Key;
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
        private sealed class RowSlice { internal int[] Indices; internal double[] Positions; }
        private static RowGrid BuildRowGrid(TowerInput input,double covered,double rowCoverage,double first,int count,double phase,double edge)
        {
            var t=input.TransferredSlab;
            if(t.HasIndividualSpacings)return BuildIndividualRowGrid(input,covered,rowCoverage,edge);
            var shared=t.PlywoodSheetLengthMm>0
                ? PlywoodSpacing.SheetSupportRows((input.LengthMm-rowCoverage)/2,(input.LengthMm+rowCoverage)/2,t.UpperSpacingMm,t.PlywoodSheetLengthMm).Select(x=>x-edge).ToArray()
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
                var end=ConcreteEndStripTributaries(covered,shared,edge);
                if(initial[0]+end[0]>t.UpperSpacingMm+1e-7 && shared[0]>=120)rows.Add(40);
                if(initial[count-1]+end[count-1]>t.UpperSpacingMm+1e-7 && covered-shared.Last()>=120)rows.Add(covered-40);
            }
            var stations=rows.OrderBy(x=>x).ToArray();
            var indices=shared.Select(x=>Array.IndexOf(stations,x)).ToArray();
            var widths=Enumerable.Range(0,stations.Length).Select(i=>(i==stations.Length-1 ? covered : (stations[i]+stations[i+1])/2)-(i==0 ? 0 : (stations[i-1]+stations[i])/2)).ToArray();
            if(t.IncludeConcreteEndStrips || t.IncludeMainBeamEndLoads)
            {var end=ConcreteEndStripTributaries(covered,stations,edge);for(int i=0;i<widths.Length;i++)widths[i]+=end[i];}
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
            var t=input.TransferredSlab;double slabStart=(input.LengthMm-rowCoverage)/2,slabEnd=slabStart+rowCoverage;
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
            double[] absolute;
            try {absolute=PlywoodSpacing.FillRequiredRows(edge,edge+covered,t.UpperSpacingMm,shared.Concat(EngineerPrimaryPlan.Rows(t,shared)).Distinct().OrderBy(x=>x).ToArray());}
            catch(ArgumentException ex)
            {input.Diagnostics?.Geometry(input.DiagnosticScope+"receiving row setting-out",input.DiagnosticScope+ex.Message,"Review required slab/crossing rows and plywood joints.");return null;}
            var rows=absolute.Select(x=>x-edge).ToArray();
            var indices=shared.Select(x=>Array.IndexOf(absolute,x)).ToArray();
            var widthsBeam=PlywoodSpacing.RowTributaries(0,covered,rows);
            if(t.IncludeConcreteEndStrips || t.IncludeMainBeamEndLoads)
            {var end=ConcreteEndStripTributaries(covered,rows,edge);for(int i=0;i<rows.Length;i++)widthsBeam[i]+=end[i];}
            var grid=new RowGrid {Rows=rows,Tributaries=widthsBeam,SlabRowIndices=indices,
                LeftPhysicalScales=new double[rows.Length],RightPhysicalScales=new double[rows.Length],LeftScales=new double[rows.Length],RightScales=new double[rows.Length]};
            var ls=FullTributaryScales(leftBase,left,t.SlabSpacing(true),true);var rs=FullTributaryScales(rightBase,right,t.SlabSpacing(false),true);
            for(int i=0;i<shared.Length;i++)
            {int j=indices[i];grid.LeftPhysicalScales[j]=left[i]/1000;grid.RightPhysicalScales[j]=right[i]/1000;grid.LeftScales[j]=ls[i];grid.RightScales[j]=rs[i];}
            grid.Key=Values(rows)+"|"+Values(widthsBeam)+"|"+Values(grid.LeftScales)+"|"+Values(grid.RightScales)+"|"+edge.ToString("R",CultureInfo.InvariantCulture);
            return grid;
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
                :this(length,supports,uniform,new BeamPointLoad[0]) {}
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
        // Exact immutable numerical inputs. Include both distributed and point loads,
        // stiffness and criterion; no rounded geometry, forces or eligibility decisions.
        internal sealed class TransverseKey : IEquatable<TransverseKey>
        {
            private readonly double[] values;private readonly int hash;
            internal TransverseKey(double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,bool signed=false)
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
            internal readonly LoadedBeamAnalysis.MatrixCache Matrix=new LoadedBeamAnalysis.MatrixCache(2048,true);
            private readonly Dictionary<PrimaryKey,PrimaryCheck> responses=new Dictionary<PrimaryKey,PrimaryCheck>();
            private readonly Queue<PrimaryKey> order=new Queue<PrimaryKey>();
            internal PrimaryCheck Get(PrimaryKey key)=>responses.TryGetValue(key,out var check) ? check : null;
            internal void Store(PrimaryKey key,PrimaryCheck check)
            {
                if(responses.ContainsKey(key))return;
                if(responses.Count>=4096)responses.Remove(order.Dequeue());
                responses.Add(key,check);order.Enqueue(key);
            }
        }
        internal static LoadedBeamAnalysis AnalyzeTransverse(TowerInput input,BeamSearchCounter counter,double length,double[] supports,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,bool signed=false)
        {
            input.Trace?.CheckCancellation();
            var shared=input.ReuseSearchWork && input.ReuseTransverseNumerics ? input.NetworkNumerics??input.TransferredSlab?.SharedSearchWork : null;
            var key=shared==null ? null : new TransverseKey(length,supports,uniform,points,ei,denominator,input.IgnoreUplift || signed);
            var response=key==null ? null : shared.GetTransverse(key);
            if(response!=null) {input.Trace?.Increment(BeamSearchCounter.NetworkTransverseCacheHits);return response;}
            input.Trace?.Increment(counter);
            using(input.Trace?.Measure(BeamSearchStage.TransverseAnalysis))
                response=input.IgnoreUplift || signed ? LoadedBeamAnalysis.AnalyzeSigned(length,supports,uniform,points,ei,denominator) : LoadedBeamAnalysis.Analyze(length,supports,uniform,points,ei,denominator);
            if(key!=null)shared.StoreTransverse(key,response);
            return response;
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
            internal CrossOption[] Crosses;internal RowGrid Grid;
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
            int receiverCount=3;
            while(true)
            {
                var worker=input.CopyForSearch();worker.EngineerStationLimit=limit;
                worker.TransferredSlab=t.Copy();worker.TransferredSlab.SharedGeometryCache=cache;worker.TransferredSlab.EngineerReceiverCount=receiverCount;
                try{return OptimizeCoreByLegTier(worker,shareDiagnostics);}
                catch(TowerOptimizationException) when(t.EngineerNetwork!=null && receiverCount<8){receiverCount++;}
                catch(TowerOptimizationException) when(limit<maximum){limit=Math.Min(maximum,limit+(input.EngineerPlan==null ? 4 : 2));receiverCount=3;}
            }
        }
        private static TowerLayout OptimizeCoreReference(TowerInput input,bool shareDiagnostics)
        {
            var t = input.TransferredSlab; t.Validate(input); TowerLayout best = null; int tested = 0;
            var geometryCache = input.ReuseSearchWork ? t.SharedGeometryCache ?? new GeometryCache() : null;
            // A receiving-beam solve has fixed beam loads and upper-foot plans. These options
            // depend on transverse width and tributary size, not the primary stock cover.
            var crossCache = new Dictionary<Tuple<double,double>,CrossOption[]>();
            var rowCache = new Dictionary<Tuple<double,double>,RowGrid>();
            // Different tower combinations often repeat an identical local primary member.
            // Include every support, distributed-load boundary and intensity, without rounding. This cache
            // lives only in one receiving-beam solve and retains at most 4096 checks.
            var primaryCache = input.ReuseSearchWork ? new Dictionary<PrimaryKey,PrimaryCheck>() : null;
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
                    double covered = cover.Sum(), edge = (input.LengthMm-covered)/2;
                    double rowCoverage=t.FixedCoveredLengthMm??covered;
                    bool independent=t.IsNetwork && t.AllowLongerPrimaryCoverage && t.FixedCoveredLengthMm.HasValue;
                    if(t.FixedCoveredLengthMm.HasValue && (independent ? covered<rowCoverage-1e-7 : Math.Abs(covered-rowCoverage)>1e-7)) continue;
                    int count = (int)Math.Ceiling(rowCoverage/t.RowGapMm);
                    // Slab rows stay aligned in absolute coordinates across the network. Primary
                    // stock may extend farther toward the concrete ends on an individual beam.
                    double first = (rowCoverage-(count-1)*t.RowGapMm)/2+(input.LengthMm-rowCoverage)/2-edge;
                    foreach (bool rotated in new[] { false, true }) foreach (double width in rotated ? TowerOptimizer.BaysMm : new[] { 1524.0 })
                    {
                        if (t.FixedTowerWidthMm.HasValue && Math.Abs(width-t.FixedTowerWidthMm.Value)>1e-7) continue;
                        if (rotated && input.Exclude600Standalone && width == 600) continue;
                        input.Trace?.Work("Transfer slab and lower secondary options");
                        double tributary = t.UpperSpacingMm;
                        var crossKey = Tuple.Create(width,tributary);
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
                        string geometryKey = (input.EngineerSearch ? "E"+input.EngineerStationLimit+":"+(input.EngineerPlan?.InitialLegs??0)+":" : "")+(rotated ? "R:" : "N:") + Values(cover);
                        input.Trace?.CheckCancellation();
                        Func<Geometry[]> buildGeometry=()=>
                        {
                            Geometry[] generated;
                            using(input.Trace?.Measure(BeamSearchStage.Geometry))generated=Geometries(cover,rotated,input).ToArray();
                            input.Trace?.Increment(BeamSearchCounter.GeometryBuilds);
                            return generated;
                        };
                        bool hit=false;
                        var geometries=geometryCache==null ? buildGeometry() : geometryCache.Get(geometryKey,buildGeometry,out hit);
                        if(hit)input.Trace?.Increment(BeamSearchCounter.GeometryCacheHits);
                        if(geometries.Length==0)diag.Geometry(input.DiagnosticScope+"tower geometry",$"{input.DiagnosticScope}tower geometry: no searched D3 support pattern fits stocks {string.Join(", ",cover)} mm with {input.StockEndSetbackMm:0.###} mm end setbacks; exclude standalone 600 = {input.Exclude600Standalone}.","Review stock lengths, end setbacks and the standalone-600 setting.");
                        foreach (var geometry in geometries)
                        {
                            if (geometry.Positions.Length*2>input.NetworkLegCeiling) continue;
                            // Try centred rows and phase shifts. Independent primary coverage adds beam load
                            // at the end rows without increasing the shared slab tributaries.
                            foreach (double phase in t.PlywoodSheetLengthMm>0 ? new[] {0.0} : t.FixedRowPhaseMm.HasValue ? new[] {t.FixedRowPhaseMm.Value} : new[] { 0.0, -50, 50, -100, 100 })
                            {
                                var rowKey = Tuple.Create(covered,phase);
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
                                if(conflicting>0) {double joint=geometry.Stock.Skip(1).OrderBy(s=>Math.Abs(s.StartMm-conflicting)).First().StartMm;diag.Geometry(input.DiagnosticScope+"row/joint",$"{input.DiagnosticScope}support row x={conflicting+edge:0.###} mm is {Math.Abs(joint-conflicting):0.###} mm from primary joint x={joint+edge:0.###} mm; minimum 40 mm.","Review upper row spacing and the primary stock combination.",40-Math.Abs(joint-conflicting));continue;}
                                foreach (var cross in crosses)
                                {
                                    input.Trace?.Work("Transfer layout load-case checks");input.Trace?.Increment(BeamSearchCounter.TransferCandidates);
                                    tested++;
                                    if(input.PruneUneconomicCandidates && best!=null && Economy(geometry,cross,grid,input)>Economy(best)+.01)
                                    {input.Trace?.Increment(BeamSearchCounter.EconomyPruned);continue;}
                                    var candidate = OffsetReceiver(input,Evaluate(input,geometry,width,rotated,cross,grid,edge,primaryCache,matrixCache,shared,best));
                                    if (candidate != null && (best == null || Better(candidate,best)) && (input.TransferLayoutAcceptance==null || input.TransferLayoutAcceptance(candidate))) best = candidate;
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
        private static TowerLayout OptimizeCoreByLegTier(TowerInput input,bool shareDiagnostics)
        {
            var t = input.TransferredSlab; t.Validate(input); TowerLayout best = null; int tested = 0;
            var work=new List<LayoutWork>();
            var geometryCache = input.ReuseSearchWork ? t.SharedGeometryCache ?? new GeometryCache() : null;
            // A receiving-beam solve has fixed beam loads and upper-foot plans. These options
            // depend on transverse width and tributary size, not the primary stock cover.
            var crossCache = new Dictionary<Tuple<double,double>,CrossOption[]>();
            var rowCache = new Dictionary<Tuple<double,double>,RowGrid>();
            // Different tower combinations often repeat an identical local primary member.
            // Include every support, distributed-load boundary and intensity, without rounding. This cache
            // lives only in one receiving-beam solve and retains at most 4096 checks.
            var primaryCache = input.ReuseSearchWork ? new Dictionary<PrimaryKey,PrimaryCheck>() : null;
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
                    double covered = cover.Sum(), edge = (input.LengthMm-covered)/2;
                    double rowCoverage=t.FixedCoveredLengthMm??covered;
                    bool independent=t.IsNetwork && t.AllowLongerPrimaryCoverage && t.FixedCoveredLengthMm.HasValue;
                    if(t.FixedCoveredLengthMm.HasValue && (independent ? covered<rowCoverage-1e-7 : Math.Abs(covered-rowCoverage)>1e-7)) continue;
                    int count = (int)Math.Ceiling(rowCoverage/t.RowGapMm);
                    // Slab rows stay aligned in absolute coordinates across the network. Primary
                    // stock may extend farther toward the concrete ends on an individual beam.
                    double first = (rowCoverage-(count-1)*t.RowGapMm)/2+(input.LengthMm-rowCoverage)/2-edge;
                    foreach (bool rotated in new[] { false, true }) foreach (double width in rotated ? TowerOptimizer.BaysMm : new[] { 1524.0 })
                    {
                        if (t.FixedTowerWidthMm.HasValue && Math.Abs(width-t.FixedTowerWidthMm.Value)>1e-7) continue;
                        if (rotated && input.Exclude600Standalone && width == 600) continue;
                        input.Trace?.Work("Transfer slab and lower secondary options");
                        double tributary = t.UpperSpacingMm;
                        var crossKey = Tuple.Create(width,tributary);
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
                        if(input.EngineerSearch && t.PlywoodSheetLengthMm>0)
                        {
                            var rowKey=Tuple.Create(covered,0.0);
                            if(!rowCache.TryGetValue(rowKey,out var fixedGrid))
                            {
                                fixedGrid=BuildRowGrid(input,covered,rowCoverage,first,count,0,edge);
                                rowCache[rowKey]=fixedGrid;
                            }
                            if(fixedGrid==null)continue;
                            crosses=crosses.Where(c=>AcceptTransverseCases(input,c,width,fixedGrid,edge)).ToArray();
                            if(crosses.Length==0)continue;
                        }
                        input.Trace?.Work("Transfer tower/support geometry");
                        string geometryKey = (input.EngineerSearch ? "E"+input.EngineerStationLimit+":"+(input.EngineerPlan?.InitialLegs??0)+":" : "")+(rotated ? "R:" : "N:") + Values(cover);
                        input.Trace?.CheckCancellation();
                        Func<Geometry[]> buildGeometry=()=>
                        {
                            Geometry[] generated;
                            using(input.Trace?.Measure(BeamSearchStage.Geometry))generated=Geometries(cover,rotated,input).ToArray();
                            input.Trace?.Increment(BeamSearchCounter.GeometryBuilds);
                            return generated;
                        };
                        bool hit=false;
                        var geometries=geometryCache==null ? buildGeometry() : geometryCache.Get(geometryKey,buildGeometry,out hit);
                        if(hit)input.Trace?.Increment(BeamSearchCounter.GeometryCacheHits);
                        if(geometries.Length==0)diag.Geometry(input.DiagnosticScope+"tower geometry",$"{input.DiagnosticScope}tower geometry: no searched D3 support pattern fits stocks {string.Join(", ",cover)} mm with {input.StockEndSetbackMm:0.###} mm end setbacks; exclude standalone 600 = {input.Exclude600Standalone}.","Review stock lengths, end setbacks and the standalone-600 setting.");
                        foreach (var geometry in geometries)
                        {
                            if(geometry.Positions.Length*2>input.NetworkLegCeiling || input.EngineerSearch && (geometry.Positions.Length>input.EngineerStationLimit || geometry.Positions.Length<input.EngineerMinimumStations))continue;
                            // Try centred rows and phase shifts. Independent primary coverage adds beam load
                            // at the end rows without increasing the shared slab tributaries.
                            foreach (double phase in t.PlywoodSheetLengthMm>0 ? new[] {0.0} : t.FixedRowPhaseMm.HasValue ? new[] {t.FixedRowPhaseMm.Value} : new[] { 0.0, -50, 50, -100, 100 })
                            {
                                var rowKey = Tuple.Create(covered,phase);
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
                                if(conflicting>0) {double joint=geometry.Stock.Skip(1).OrderBy(s=>Math.Abs(s.StartMm-conflicting)).First().StartMm;diag.Geometry(input.DiagnosticScope+"row/joint",$"{input.DiagnosticScope}support row x={conflicting+edge:0.###} mm is {Math.Abs(joint-conflicting):0.###} mm from primary joint x={joint+edge:0.###} mm; minimum 40 mm.","Review upper row spacing and the primary stock combination.",40-Math.Abs(joint-conflicting));continue;}
                                work.Add(new LayoutWork {Geometry=geometry,Width=width,Rotated=rotated,Crosses=crosses,Grid=grid,Edge=edge});
                            }
                        }
                    }
                }
                // Stable order within a leg tier keeps all original timber/utilization ties.
                // Economic bounds, rather than a leg-only cutoff, retain valid timber/support trade-offs.
                int initial=input.EngineerSearch && work.Count>0 ? work.GroupBy(w=>Tuple.Create(w.Crosses,w.Edge)).Min(group=>group.First().Crosses.Min(c=>InitialLegs(input,c,group.First().Edge,group.First().Geometry.Stock))) : 2;
                foreach(var item in input.EngineerSearch ? work.OrderBy(w=>EngineerTowerSearch.TierOrder(w.Geometry.Positions.Length,initial)).ThenBy(w=>w.Geometry.Joined.Count(j=>j)) : work.OrderBy(w=>w.Geometry.Positions.Length))
                {
                    input.Trace?.CheckCancellation();
                    var geometry=item.Geometry;var grid=item.Grid;
                    if(geometry.Positions.Length*2>input.NetworkLegCeiling || input.EngineerSearch && best!=null && geometry.Positions.Length>best.PositionsMm.Length)continue;
                    if(input.EngineerSearch && best!=null && geometry.Positions.Length==best.PositionsMm.Length)
                    {
                        int bays=geometry.Joined.Count(j=>j);
                        if(bays>best.BayCount || bays==best.BayCount && EngineerTowerSearch.BracePenalty(geometry.Positions,geometry.Joined,item.Width,item.Rotated)>best.StandaloneBracePenalty+1e-7)continue;
                    }
                    double geometrySymmetry=input.EngineerSearch ? TowerOptimizer.SymmetryError(geometry.Positions,geometry.Stock.Select(s=>s.StockLengthMm).ToArray(),input.LengthMm/2-item.Edge)
                        +geometry.Stock.Sum(s=>Math.Abs((geometry.Positions[s.FirstSupport]-s.StartMm)-(s.EndMm-geometry.Positions[s.LastSupport]))) : 0;
                    foreach(var cross in item.Crosses)
                    {
                        if(input.EngineerSearch && input.PruneUneconomicCandidates && best!=null && geometry.Positions.Length==best.PositionsMm.Length
                            && EngineerTowerSearch.Compare(TowerOptimizer.Standalone600Count(geometry.Positions,geometry.Joined,item.Width,item.Rotated),geometry.Joined.Count(j=>j),EngineerTowerSearch.BracePenalty(geometry.Positions,geometry.Joined,item.Width,item.Rotated),best.Standalone600Count,best.BayCount,best.StandaloneBracePenalty)==0
                            && geometrySymmetry+Math.Abs(2*cross.Start+cross.Stock)>best.SymmetryErrorMm+1e-7)
                        {
                            // This exact geometric lower bound already ranks worse.
                            // Capacity checks cannot improve fixed stock/support symmetry;
                            // upper-member imbalance can only add to the lower bound.
                            continue;
                        }
                        input.Trace?.Increment(BeamSearchCounter.TransferCandidates);tested++;
                        if(!input.EngineerSearch && input.PruneUneconomicCandidates && best!=null)
                        {
                            double score=Economy(geometry,cross,grid,input);
                            if(score>Economy(best)+.01) {input.Trace?.Increment(BeamSearchCounter.EconomyPruned);continue;}
                        }
                        var candidate=OffsetReceiver(input,Evaluate(input,geometry,item.Width,item.Rotated,cross,grid,item.Edge,primaryCache,matrixCache,shared,best));
                        if(candidate!=null && input.EngineerSearch){candidate.SearchAssumedLegKn=EngineerTowerSearch.AssumedLegKn(input);candidate.SearchInitialLegsPerLine=InitialLegs(input,cross,item.Edge,geometry.Stock);}
                        if(candidate!=null && (best==null || Better(candidate,best)) && (input.TransferLayoutAcceptance==null || input.TransferLayoutAcceptance(candidate)))best=candidate;
                    }
                }
            }
            catch(BeamSearchDeadlineException) when(best!=null) {best.SearchTimeLimitReached=true;}
            if (best == null) throw diag.Exception("No valid transferred-slab layout found within the bounded search.");
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
            if(Math.Abs(a.SymmetryErrorMm-b.SymmetryErrorMm)>1e-7)return a.SymmetryErrorMm<b.SymmetryErrorMm;
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
        internal static double Economy(TowerLayout l)=>l.LegCount*TowerOptimizer.TowerLegTimberEquivalentMm+l.Transfer.UpperFootCount*TowerOptimizer.UpperFootTimberEquivalentMm+Timber(l);
        private static double Economy(Geometry g,CrossOption cross,RowGrid grid,TowerInput input)
        {
            int left=cross.Left==null ? 0 : grid.LeftScales.Count(x=>x>0),right=cross.Right==null ? 0 : grid.RightScales.Count(x=>x>0);
            return g.Positions.Length*2*TowerOptimizer.TowerLegTimberEquivalentMm+(left+right)*2*TowerOptimizer.UpperFootTimberEquivalentMm
                +g.Stock.Sum(s=>s.StockLengthMm)*2*input.Primary.CountPerLine+grid.Rows.Length*cross.Stock+left*(cross.Left?.Stock??0)+right*(cross.Right?.Stock??0);
        }
        internal static double[] CoveredLengths(TowerInput input) => Covers(input).Select(c=>c.Sum()).Distinct().ToArray();
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
                states=input.EngineerSearch
                    ? next.GroupBy(x=>x.Positions.Length).SelectMany(g=>g.OrderBy(x=>x.Joined.Count(j=>j)).ThenBy(x=>EngineerTowerSearch.BracePenalty(x.Positions,x.Joined,rotated ? 1500 : 1524,rotated)).ThenBy(x=>x.Stock.Sum(s=>x.Positions[s.FirstSupport]-s.StartMm)).Take(24)).ToList()
                    : next.GroupBy(x=>x.Positions.Length).SelectMany(g=>g.OrderBy(x=>x.Stock.Sum(s=>x.Positions[s.FirstSupport]-s.StartMm)).Take(12)).OrderBy(x=>x.Positions.Length).Take(84).ToList();
            }
            // Keep fully mirrored regular groups even when the inset-based beam-search cap drops them.
            var symmetric=SymmetricGeometries(stock,rotated,input).GroupBy(g=>g.Positions.Length).SelectMany(g=>g.Take(24));
            if(input.EngineerSearch)states=(rotated ? Enumerable.Empty<Geometry>() : EngineerTowerSearch.StockRunGeometries(stock,input)).Concat(EngineerTowerSearch.Geometries(stock,rotated,input)).Concat(states.Where(g=>g.Positions.Length<=input.EngineerStationLimit)).ToList();
            states=states.Concat(symmetric).GroupBy(g=>Values(g.Positions)+"|"+string.Join(";",g.Joined)).Select(g=>g.First()).ToList();
            if(input.TransferredSlab?.IsNetwork==true)states=states.SelectMany(g=>SiteGeometries(g,input.StockEndSetbackMm)).GroupBy(g=>Values(g.Positions)+"|"+string.Join(";",g.Joined)).Select(g=>g.First()).ToList();
            foreach(var geometry in states)geometry.MemberSupports=geometry.Stock.Select(s=>geometry.Positions.Skip(s.FirstSupport).Take(s.LastSupport-s.FirstSupport+1).Select(x=>x-s.StartMm).ToArray()).ToArray();
            return states;
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
        private static IEnumerable<Upper> UpperOptions(double bay,double load,double width,TowerInput input,double tributaryMm,UpperTransferPlan fixedPlan)
        {
            if (bay==0) return new[] { (Upper)null };
            var result=new List<Upper>();
            if(fixedPlan!=null)
            {
                fixedPlan.Validate(bay,input.StockLengthsMm,input.AllowCustomSlabLengths);
                if(!input.PermitsSlabLength(fixedPlan.StockLengthMm))throw new ArgumentException("The selected custom-length scope requires catalogue slab H20s.");
                var a=AnalyzeTransverse(input,BeamSearchCounter.UpperAnalyses,fixedPlan.StockLengthMm,new[] {fixedPlan.NearFootMm-fixedPlan.EdgeMm,bay-fixedPlan.FarFootMm-fixedPlan.EdgeMm},new[] {new BeamDistributedLoad {StartMm=0,EndMm=fixedPlan.StockLengthMm,KnPerM=load}},null,TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                if(Pass(input,a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Slab H20",1,()=> $"stock {fixedPlan.StockLengthMm:0} mm · feet {fixedPlan.NearFootMm:0.###}/{fixedPlan.FarFootMm:0.###} mm from faces · base tributary {tributaryMm:0.###} mm")) result.Add(new Upper {Stock=fixedPlan.StockLengthMm,Custom=fixedPlan.IsCustomLength,Edge=fixedPlan.EdgeMm,Foot=fixedPlan.NearFootMm,OtherFoot=fixedPlan.FarFootMm,Bay=bay,Load=load,Analysis=a});
                return result;
            }
            var lengths=UpperTransferPlan.SlabLengths(bay,input.StockLengthsMm,input.AllowCustomSlabLengths).Where(input.PermitsSlabLength).ToArray();
            foreach(double stock in lengths)
            {
                double edge=(bay-stock)/2; if (edge<200 || edge>350) continue;
                double min=Math.Max(250,edge+150), max=bay/2-150;
                var feet=new[] {min,max,Math.Max(min,Math.Min(max,(width-input.TransferredSlab.BeamWidthMm)/2))}
                    .Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Distinct();
                foreach(double foot in feet.Where(x=>x>=min && x<=max))
                {
                        var a=AnalyzeTransverse(input,BeamSearchCounter.UpperAnalyses,stock,new[] {foot-edge,bay-foot-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=load}},null,TowerOptimizer.PairEI/2,input.DeflectionDenominator);
                    if (Pass(input,a,tributaryMm/1000,1,input.Diagnostics,input.DiagnosticScope+"Slab H20",1,()=> $"stock {stock:0} mm · foot {foot:0.###} mm from face · base tributary {tributaryMm:0.###} mm")) result.Add(new Upper { Stock=stock,Custom=!input.StockLengthsMm.Contains(stock),Edge=edge,Foot=foot,OtherFoot=foot,Bay=bay,Load=load,Analysis=a });
                }
            }
            if(lengths.Length==0)input.Diagnostics?.Geometry(input.DiagnosticScope+"slab length "+bay,$"{input.DiagnosticScope}slab bay {bay:0.###} mm has no permitted H20 length with 200–350 mm edge strips.","Review slab stocks, edge clearance and the custom-length scope.");
            return result;
        }
        private static IEnumerable<CrossOption> CrossOptions(TowerInput input,double width,double tributaryMm)
        {
            var t=input.TransferredSlab; var result=new List<CrossOption>();
            foreach(var left in UpperOptions(t.LeftBayMm,t.LeftAreaLoadKnM2,width,input,t.HasIndividualSpacings ? t.SlabSpacing(true) : tributaryMm,t.LeftPlan)) foreach(var right in UpperOptions(t.RightBayMm,t.RightAreaLoadKnM2,width,input,t.HasIndividualSpacings ? t.SlabSpacing(false) : tributaryMm,t.RightPlan))
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
            if(cross.RowKey!=grid.Key)
            {
                cross.RowKey=grid.Key;
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
        private static BeamDistributedLoad[] PrimaryUniformLoads(TowerInput input,CrossOption cross,double stockStart,double stockLength,int line,int c,double edge)
            => input.EngineerPlan?.UseDesignZones==true
                ? (cross.PositionedPlan??input.EngineerPlan).Loads(stockStart,stockLength,line,selfWeight:input.Primary.SelfWeightKnM(stockLength))
                : LegacyPrimaryUniformLoads(input,cross,stockStart,stockLength,line,c,edge);
        private static BeamDistributedLoad[] LegacyPrimaryUniformLoads(TowerInput input,CrossOption cross,double stockStart,double stockLength,int line,int c,double edge)
        {
            double baseBeam=cross.Cases[0].Reactions[line],beam=baseBeam;
            // Dedicated crossing primaries use the same continuous UDL design as main beams.
            // Include the full concrete span on its centred stock coverage: this preserves
            // total force and its longitudinal resultant without secondary-row point loads.
            if(input.TransferredSlab.IncludeConcreteEndStrips && edge>1e-7)
                beam*=input.LengthMm/(input.LengthMm-2*edge);
            int left=Array.IndexOf(cross.Names,"Beam + left slab"),right=Array.IndexOf(cross.Names,"Beam + right slab");
            bool hasLeft=cross.Names[c].Contains("left") || cross.Names[c].Contains("both"),hasRight=cross.Names[c].Contains("right") || cross.Names[c].Contains("both");
            double l=hasLeft && left>=0 ? cross.Cases[left].Reactions[line]-baseBeam : 0,r=hasRight && right>=0 ? cross.Cases[right].Reactions[line]-baseBeam : 0;
            double slabCoverage=input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge),slabEdge=(input.LengthMm-slabCoverage)/2;
            // Main-primary intensities are independent of coverage. Uncovered longitudinal
            // strips belong to external column-side supports. Slab coverage can
            // differ from primary coverage; each excluded load is assigned once.
            var design=PrimaryUniformLoads(stockStart,stockLength,beam,l,r,input.TransferredSlab.LeftCrossingBands,input.TransferredSlab.RightCrossingBands,input.IgnoreUplift || input.DeferCrossingChecks,
                slabEdge,slabEdge+slabCoverage);
            return design==null || !input.Primary.IsWS10 ? design : design.Concat(new[] {new BeamDistributedLoad {StartMm=0,EndMm=stockLength,KnPerM=input.Primary.SelfWeightKnM(stockLength)}}).ToArray();
        }
        private static ColumnEndStripLoad[][] SelectColumnCases(TowerInput input,CrossOption cross,double edge)
            => input.EngineerPlan?.UseDesignZones==true
                ? cross.DesignCases.Select(c=>(cross.PositionedPlan??input.EngineerPlan).Columns(edge,input.LengthMm-edge,cross.Names[c])).ToArray()
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
            double slabCoverage=t.FixedCoveredLengthMm??(input.LengthMm-2*edge),slabEdge=(input.LengthMm-slabCoverage)/2;
            if(edge<=1e-7 && slabEdge<=1e-7)return new ColumnEndStripLoad[0][];
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
                    var full=PrimaryUniformLoads(0,input.LengthMm,beam,l,r,t.LeftCrossingBands,t.RightCrossingBands,true);
                    var carried=PrimaryUniformLoads(edge,input.LengthMm-2*edge,beam,l,r,t.LeftCrossingBands,t.RightCrossingBands,true,slabEdge,slabEdge+slabCoverage);
                    var cuts=full.SelectMany(q=>new[] {q.StartMm,q.EndMm}).Concat(carried.SelectMany(q=>new[] {edge+q.StartMm,edge+q.EndMm})).Concat(new[] {0.0,input.LengthMm}).Distinct().OrderBy(x=>x).ToArray();
                    for(int i=1;i<cuts.Length;i++)
                    {
                        double middle=(cuts[i-1]+cuts[i])/2;
                        double q=full.Where(b=>b.StartMm<middle && b.EndMm>middle).Sum(b=>b.KnPerM)-carried.Where(b=>edge+b.StartMm<middle && edge+b.EndMm>middle).Sum(b=>b.KnPerM);
                        if(Math.Abs(q)<=1e-10)continue;
                        int end=middle<input.LengthMm/2 ? 0 : 1;
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
            return input.LengthMm+(bands??new BeamDistributedLoad[0]).Sum(b=>b.KnPerM*Math.Max(0,Math.Min(input.LengthMm,b.EndMm)-Math.Max(0,b.StartMm)));
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
            if(input.ReuseSearchWork && cross.TransverseKey==grid.Key)return cross.TransversePassed;
            cross.TransverseKey=grid.Key;cross.TransversePassed=false;
            var ls=grid.LeftScales;var rs=grid.RightScales;
            if(cross.Left!=null && !Pass(input,cross.Left.Analysis,Math.Max(ls.Max(),input.TransferredSlab.SlabSpacing(true)/1000),1,input.Diagnostics,input.DiagnosticScope+"Left slab H20",1,()=> $"governing row T{Array.IndexOf(ls,ls.Max())+1} · stock {cross.Left.Stock:0} mm")
                || cross.Right!=null && !Pass(input,cross.Right.Analysis,Math.Max(rs.Max(),input.TransferredSlab.SlabSpacing(false)/1000),1,input.Diagnostics,input.DiagnosticScope+"Right slab H20",1,()=> $"governing row T{Array.IndexOf(rs,rs.Max())+1} · stock {cross.Right.Stock:0} mm"))return false;
            if(input.TransferLowerAcceptance==null)return cross.TransversePassed=true;
            var lower=cross.DesignCases.Select(c=>LowerCase(input,cross,width,grid,edge,c)).ToArray();
            if(lower.Any(c=>c==null))return false;
            var probe=new TowerLayout {EngineerSearch=true,PrimaryMaterial=input.PrimaryMaterial,PositionsMm=new double[0],Joined=new bool[0],WidthMm=width,LengthMm=input.LengthMm,
                StockPlan=new TowerStockPlan {Pieces=new TowerStockPiece[0]},
                Transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerReceiverCount,RowsMm=grid.Rows.Select(x=>x+edge).ToArray(),LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,CaseNames=cross.DesignCases.Select(c=>cross.Names[c]).ToArray(),LowerCases=lower}};
            return cross.TransversePassed=input.TransferLowerAcceptance(OffsetReceiver(input,probe));
        }
        private static bool NecessaryPrimarySpans(TowerInput input,Geometry geometry,CrossOption cross,double edge,int stage)
        {
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
                var supports=geometry.MemberSupports[Array.IndexOf(geometry.Stock,stock)];
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
                var limits=new[] {0.0}.Concat(supports).Concat(new[] {stock.StockLengthMm}).ToArray();
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
        private static double PrimaryUtilization(TowerInput input,LoadedBeamAnalysis analysis)=>Math.Max(analysis.DeflectionUtilization,input.Primary.Strength(analysis));
        private static TowerLayout Evaluate(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,RowGrid grid,double edge,Dictionary<PrimaryKey,PrimaryCheck> primaryCache,LoadedBeamAnalysis.MatrixCache matrixCache,SearchWorkCache shared,TowerLayout incumbent)
        {
            if(!input.DeferTransferLayout)return EvaluateReference(input,g,width,rotated,cross,grid,edge,primaryCache,matrixCache,shared);
            if(input.EngineerSearch && !AcceptTransverseCases(input,cross,width,grid,edge))return null;
            var rows=grid.Rows;var trib=grid.Tributaries;var ls=grid.LeftScales;var rs=grid.RightScales;
            if(ls.Length!=rows.Length || rs.Length!=rows.Length)throw new ArgumentException("Crossing beam row loads do not match the support rows.");
            if(!input.EngineerSearch && (cross.Left!=null && !Pass(input,cross.Left.Analysis,Math.Max(ls.Max(),input.TransferredSlab.SlabSpacing(true)/1000),1,input.Diagnostics,input.DiagnosticScope+"Left slab H20",1,()=> $"governing row T{Array.IndexOf(ls,ls.Max())+1} · stock {cross.Left.Stock:0} mm") || cross.Right!=null && !Pass(input,cross.Right.Analysis,Math.Max(rs.Max(),input.TransferredSlab.SlabSpacing(false)/1000),1,input.Diagnostics,input.DiagnosticScope+"Right slab H20",1,()=> $"governing row T{Array.IndexOf(rs,rs.Max())+1} · stock {cross.Right.Stock:0} mm")))return null;
            int n=g.Positions.Length; var left=Enumerable.Repeat(double.NegativeInfinity,n).ToArray(); var right=Enumerable.Repeat(double.NegativeInfinity,n).ToArray();
            var minLeft=Enumerable.Repeat(double.PositiveInfinity,n).ToArray(); var minRight=Enumerable.Repeat(double.PositiveInfinity,n).ToArray();
            var checkedCases=new List<PrimaryCheck[]>(); var lowerCases=new List<TransferMember[]>();
            foreach(int c in cross.DesignCases)
            {
                var checks=new PrimaryCheck[g.Stock.Length*2];
                if(input.EngineerSearch && !NecessaryPrimarySpans(input,g,cross,edge,c))return null;
                var lower=LowerCase(input,cross,width,grid,edge,c);if(lower==null)return null;
                for(int line=0;line<2;line++) foreach(var stock in g.Stock)
                {
                    var supports=g.MemberSupports[Array.IndexOf(g.Stock,stock)];
                    var uniform=PrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,c,edge);
                    if(uniform==null) {input.Diagnostics?.Geometry(input.DiagnosticScope+"primary distributed uplift","Primary UDL has an upward resultant within a loaded interval.","Review transverse bearings and asymmetric slab/crossing loads.");return null;}
                    input.Trace?.CheckCancellation();
                    var points=new BeamPointLoad[0];
                    var key=primaryCache==null ? null : new PrimaryKey(stock.StockLengthMm,supports,uniform,points,input.DeferCrossingChecks);
                    PrimaryCheck check;
                    if(primaryCache==null || !primaryCache.TryGetValue(key,out check))
                    {
                        // Cache the same complete UDL response used by diagrams and member checks.
                        var reused=key==null ? null : shared?.Get(key);
                        LoadedBeamAnalysis analysis;
                        if(reused!=null) {analysis=reused.Analysis;uniform=reused.Uniform;points=reused.Points;input.Trace?.Increment(BeamSearchCounter.NetworkPrimaryCacheHits);}
                        else
                        {
                            input.Trace?.Increment(BeamSearchCounter.PrimaryAnalyses);
                            long priorHits=matrixCache?.Hits??0;
                            using(input.Trace?.Measure(BeamSearchStage.PrimaryAnalysis))
                            analysis=LoadedBeamAnalysis.AnalyzeCombinedCached(stock.StockLengthMm,supports,uniform,points,input.Primary.EI,input.DeflectionDenominator,matrixCache,input.LegCapacityKn,input.StagePrimaryChecks && !input.Primary.IsWS10 && !input.DeferCrossingChecks,input.IgnoreUplift || input.DeferCrossingChecks,input.TransferredSlab.IncludeConcreteEndStrips);
                            if(!analysis.DeflectionEvaluated)input.Trace?.Increment(BeamSearchCounter.PrimaryDeflectionSkipped);
                            if(matrixCache!=null && matrixCache.Hits>priorHits)input.Trace?.Increment(BeamSearchCounter.PrimaryMatrixCacheHits);
                        }
                        bool memberPass=Pass(input,analysis,1,2,input.Diagnostics,input.DiagnosticScope+"Primary pair · UDL",3,()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · stock {stock.StockLengthMm:0} mm · x={stock.StartMm+edge:0.###}–{stock.EndMm+edge:0.###} mm");
                        int peakLeg=Array.IndexOf(analysis.Reactions,analysis.Reactions.Max());
                        if(!input.DeferCrossingChecks)input.Diagnostics?.LegsLazy(analysis.Reactions,input.LegCapacityKn,input.DiagnosticScope+"Tower legs · UDL",()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · peak leg x={g.Positions[stock.FirstSupport+peakLeg]+edge:0.###} mm");
                        check=new PrimaryCheck {Analysis=analysis,Points=points,Uniform=uniform,Passed=memberPass && (input.DeferCrossingChecks || analysis.Reactions.Max()<=input.LegCapacityKn+1e-8)};
                        if(primaryCache!=null && primaryCache.Count<4096)primaryCache[key]=check;
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
                    members[line*g.Stock.Length+i]=new TransferMember {PrimaryMaterial=input.PrimaryMaterial,PrimarySelfWeightKnM=input.Primary.SelfWeightKnM(stock.StockLengthMm),Name="P"+(i+1)+(line==0 ? " L" : " R"),LoadCase=cross.Names[cross.DesignCases[c]],StartMm=stock.StartMm+edge,LengthMm=stock.StockLengthMm,SupportsMm=g.MemberSupports[i],Points=check.Points,Uniform=check.Uniform,LegacyPrimaryUniform=input.EngineerPlan?.UseDesignZones==true ? LegacyPrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,cross.DesignCases[c],edge) : null,Analysis=check.Analysis};
                }
                primaryCases.Add(members);
            }
            if(input.EngineerSearch && input.TransferLayoutAcceptance!=null)
            {
                var provisional=new TowerLayout {EngineerSearch=true,PrimaryMaterial=input.PrimaryMaterial,IgnoreUplift=input.IgnoreUplift,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,LegCapacityKn=input.LegCapacityKn,
                    StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=g.Stock.Select(p=>new TowerStockPiece {StartMm=p.StartMm+edge,StockLengthMm=p.StockLengthMm,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport}).ToArray()},
                    Transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerReceiverCount,FullLoadingOnly=input.FullLoadingOnly,RowsMm=rows.Select(x=>x+edge).ToArray(),LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,CaseNames=cross.DesignCases.Select(i=>cross.Names[i]).ToArray(),LowerCases=lowerCases.ToArray(),PrimaryCases=primaryCases.ToArray(),UpperMembers=new TransferMember[0],ColumnSupportLoads=new ColumnEndStripLoad[0][]}};
                if(!input.TransferLayoutAcceptance(OffsetReceiver(input,provisional)))
                {input.Trace?.Increment(BeamSearchCounter.TransferLayoutsDeferred);return null;}
            }
            // Ranking uses the identical member utilization arithmetic. Preserve strict
            // original-order ties; only candidates outside the economic bound are deferred.
            var upper=new List<TransferMember>();
            foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null)) upper.Add(new TransferMember {H20Count=1,Name=u==cross.Left ? "Upper left" : "Upper right",LoadCase="Slab · governing row",StartMm=u.Edge,LengthMm=u.Stock,SupportsMm=new[] {u.Foot-u.Edge,u.Bay-u.OtherFoot-u.Edge},Points=new BeamPointLoad[0],Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=u.Stock,KnPerM=u.Load*Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000)}},Analysis=u.Analysis.Scale(Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000))});
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
            var transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerNetwork==null ? 0 : input.TransferredSlab.EngineerReceiverCount,UnitLowerCases=UnitLowerCases(input,cross,width),UnitPointLabels=cross.UnitPointLabels,SlabSupportStartMm=(input.LengthMm-(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge)))/2,SlabSupportEndMm=(input.LengthMm+(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge)))/2,ColumnSupportLoads=SelectColumnCases(input,cross,edge),LegacyColumnSupportLoads=input.EngineerPlan?.UseDesignZones==true ? SelectLegacyColumnCases(input,cross,edge) : null,CrossFormwork=input.TransferredSlab.CrossFormwork,LowerCustom=!input.StockLengthsMm.Contains(cross.Stock),LeftUpperScales=ls,RightUpperScales=rs,EnteredUpperSpacingMm=input.TransferredSlab.UpperSpacingMm,LeftSlabSpacingMm=input.TransferredSlab.LeftSlabSpacingMm,RightSlabSpacingMm=input.TransferredSlab.RightSlabSpacingMm,HasIndividualSpacings=input.TransferredSlab.HasIndividualSpacings,RowsMm=rows.Select(x=>x+edge).ToArray(),SlabRowIndices=grid.SlabRowIndices,TributaryWidthsMm=trib,LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,LeftFootMm=cross.Left?.Foot??0,RightFootMm=cross.Right?.Foot??0,
                LeftUpperStockMm=cross.Left?.Stock??0,RightUpperStockMm=cross.Right?.Stock??0,LeftUpperCustom=cross.Left?.Custom??false,RightUpperCustom=cross.Right?.Custom??false,LeftUpperEdgeMm=cross.Left?.Edge??0,RightUpperEdgeMm=cross.Right?.Edge??0,
                LeftFootReactionKn=(cross.Left?.Analysis.Reactions[0]??0)*ls.Max(),RightFootReactionKn=(cross.Right?.Analysis.Reactions[0]??0)*rs.Max(),
                BeamLoadKn=input.TransferredSlab.TotalBeamLineLoadKnM*input.LengthMm/1000,
                CoveredSlabAreaM2=(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge))*((cross.Left?.Stock??0)+(cross.Right?.Stock??0))/1e6,
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
            return new TowerLayout {EngineerSearch=input.EngineerSearch,PrimaryMaterial=input.PrimaryMaterial,IgnoreUplift=input.IgnoreUplift,Transfer=transfer,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,ConcreteEndOverhangMm=edge,LeftReactions=left,RightReactions=right,MinimumLeftReactions=minLeft,MinimumRightReactions=minRight,
                LeftLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[0])/(trib.Sum()/1000),RightLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[1])/(trib.Sum()/1000),LegCapacityKn=input.LegCapacityKn,MaxLegKn=peak,
                StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=pieces,Reactions=left.Zip(right,Math.Max).ToArray()},DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,MomentKnM=all.Max(m=>m.Analysis.MomentKnM),ShearKn=all.Max(m=>m.Analysis.ShearKn),DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm),Utilization=util,Governing=peak/input.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name};
        }
        private static TowerLayout EvaluateReference(TowerInput input,Geometry g,double width,bool rotated,CrossOption cross,RowGrid grid,double edge,Dictionary<PrimaryKey,PrimaryCheck> primaryCache,LoadedBeamAnalysis.MatrixCache matrixCache,SearchWorkCache shared)
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
                    var supports=g.MemberSupports[Array.IndexOf(g.Stock,stock)];
                    var uniform=PrimaryUniformLoads(input,cross,stock.StartMm+edge,stock.StockLengthMm,line,c,edge);
                    if(uniform==null) {input.Diagnostics?.Geometry(input.DiagnosticScope+"primary distributed uplift","Primary UDL has an upward resultant within a loaded interval.","Review transverse bearings and asymmetric slab/crossing loads.");return null;}
                    input.Trace?.CheckCancellation();
                    var points=new BeamPointLoad[0];
                    var key=primaryCache==null ? null : new PrimaryKey(stock.StockLengthMm,supports,uniform,points,input.DeferCrossingChecks);
                    PrimaryCheck check;
                    if(primaryCache==null || !primaryCache.TryGetValue(key,out check))
                    {
                        // Cache the same complete UDL response used by diagrams and member checks.
                        var reused=key==null ? null : shared?.Get(key);
                        LoadedBeamAnalysis analysis;
                        if(reused!=null) {analysis=reused.Analysis;uniform=reused.Uniform;points=reused.Points;input.Trace?.Increment(BeamSearchCounter.NetworkPrimaryCacheHits);}
                        else
                        {
                            input.Trace?.Increment(BeamSearchCounter.PrimaryAnalyses);
                            long priorHits=matrixCache?.Hits??0;
                            using(input.Trace?.Measure(BeamSearchStage.PrimaryAnalysis))
                            analysis=LoadedBeamAnalysis.AnalyzeCombinedCached(stock.StockLengthMm,supports,uniform,points,input.Primary.EI,input.DeflectionDenominator,matrixCache,input.LegCapacityKn,input.StagePrimaryChecks && !input.Primary.IsWS10 && !input.DeferCrossingChecks,input.IgnoreUplift || input.DeferCrossingChecks,input.TransferredSlab.IncludeConcreteEndStrips);
                            if(!analysis.DeflectionEvaluated)input.Trace?.Increment(BeamSearchCounter.PrimaryDeflectionSkipped);
                            if(matrixCache!=null && matrixCache.Hits>priorHits)input.Trace?.Increment(BeamSearchCounter.PrimaryMatrixCacheHits);
                        }
                        bool memberPass=Pass(input,analysis,1,2,input.Diagnostics,input.DiagnosticScope+"Primary pair · UDL",3,()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · stock {stock.StockLengthMm:0} mm · x={stock.StartMm+edge:0.###}–{stock.EndMm+edge:0.###} mm");
                        int peakLeg=Array.IndexOf(analysis.Reactions,analysis.Reactions.Max());
                        if(!input.DeferCrossingChecks)input.Diagnostics?.LegsLazy(analysis.Reactions,input.LegCapacityKn,input.DiagnosticScope+"Tower legs · UDL",()=> $"P{Array.IndexOf(g.Stock,stock)+1} {(line==0 ? "left" : "right")} · {cross.Names[c]} · peak leg x={g.Positions[stock.FirstSupport+peakLeg]+edge:0.###} mm");
                        check=new PrimaryCheck {Analysis=analysis,Points=points,Uniform=uniform,Passed=memberPass && (input.DeferCrossingChecks || analysis.Reactions.Max()<=input.LegCapacityKn+1e-8)};
                        if(primaryCache!=null && primaryCache.Count<4096)primaryCache[key]=check;
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
            foreach(var u in new[] {cross.Left,cross.Right}.Where(x=>x!=null)) upper.Add(new TransferMember {H20Count=1,Name=u==cross.Left ? "Upper left" : "Upper right",LoadCase="Slab · governing row",StartMm=u.Edge,LengthMm=u.Stock,SupportsMm=new[] {u.Foot-u.Edge,u.Bay-u.OtherFoot-u.Edge},Points=new BeamPointLoad[0],Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=u.Stock,KnPerM=u.Load*Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000)}},Analysis=u.Analysis.Scale(Math.Max((u==cross.Left ? ls : rs).Max(),input.TransferredSlab.SlabSpacing(u==cross.Left)/1000))});
            var transfer=new TransferredSlabResult {PrimaryPlan=input.EngineerPlan?.UseDesignZones==true ? cross.PositionedPlan??input.EngineerPlan : null,EngineerReceiverCount=input.TransferredSlab.EngineerNetwork==null ? 0 : input.TransferredSlab.EngineerReceiverCount,UnitLowerCases=UnitLowerCases(input,cross,width),UnitPointLabels=cross.UnitPointLabels,SlabSupportStartMm=(input.LengthMm-(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge)))/2,SlabSupportEndMm=(input.LengthMm+(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge)))/2,ColumnSupportLoads=SelectColumnCases(input,cross,edge),LegacyColumnSupportLoads=input.EngineerPlan?.UseDesignZones==true ? SelectLegacyColumnCases(input,cross,edge) : null,CrossFormwork=input.TransferredSlab.CrossFormwork,LowerCustom=!input.StockLengthsMm.Contains(cross.Stock),LeftUpperScales=ls,RightUpperScales=rs,EnteredUpperSpacingMm=input.TransferredSlab.UpperSpacingMm,LeftSlabSpacingMm=input.TransferredSlab.LeftSlabSpacingMm,RightSlabSpacingMm=input.TransferredSlab.RightSlabSpacingMm,HasIndividualSpacings=input.TransferredSlab.HasIndividualSpacings,RowsMm=rows.Select(x=>x+edge).ToArray(),SlabRowIndices=grid.SlabRowIndices,TributaryWidthsMm=trib,LowerStockStartMm=cross.Start,LowerStockLengthMm=cross.Stock,LeftFootMm=cross.Left?.Foot??0,RightFootMm=cross.Right?.Foot??0,
                LeftUpperStockMm=cross.Left?.Stock??0,RightUpperStockMm=cross.Right?.Stock??0,LeftUpperCustom=cross.Left?.Custom??false,RightUpperCustom=cross.Right?.Custom??false,LeftUpperEdgeMm=cross.Left?.Edge??0,RightUpperEdgeMm=cross.Right?.Edge??0,
                LeftFootReactionKn=(cross.Left?.Analysis.Reactions[0]??0)*ls.Max(),RightFootReactionKn=(cross.Right?.Analysis.Reactions[0]??0)*rs.Max(),
                BeamLoadKn=input.TransferredSlab.TotalBeamLineLoadKnM*input.LengthMm/1000,
                CoveredSlabAreaM2=(input.TransferredSlab.FixedCoveredLengthMm??(input.LengthMm-2*edge))*((cross.Left?.Stock??0)+(cross.Right?.Stock??0))/1e6,
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
            return new TowerLayout {EngineerSearch=input.EngineerSearch,PrimaryMaterial=input.PrimaryMaterial,IgnoreUplift=input.IgnoreUplift,Transfer=transfer,PositionsMm=g.Positions.Select(x=>x+edge).ToArray(),Joined=g.Joined,WidthMm=width,Rotated=rotated,LengthMm=input.LengthMm,ConcreteEndOverhangMm=edge,LeftReactions=left,RightReactions=right,MinimumLeftReactions=minLeft,MinimumRightReactions=minRight,
                LeftLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[0])/(trib.Sum()/1000),RightLineLoad=lowerCases.Last().Sum(m=>m.Analysis.Reactions[1])/(trib.Sum()/1000),LegCapacityKn=input.LegCapacityKn,MaxLegKn=peak,
                StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=pieces,Reactions=left.Zip(right,Math.Max).ToArray()},DeflectionDenominator=input.DeflectionDenominator,DeflectionLimitMm=input.DeflectionLimitMm,MomentKnM=all.Max(m=>m.Analysis.MomentKnM),ShearKn=all.Max(m=>m.Analysis.ShearKn),DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm),Utilization=util,Governing=peak/input.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name};
        }
    }
}
