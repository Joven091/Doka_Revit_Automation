using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorViewModel
    {
        private bool hasSelectedSupportSystem;
        public bool HasSelectedSupportSystem
        {
            get => hasSelectedSupportSystem;
            set { hasSelectedSupportSystem = value; OnPropertyChanged(); }
        }
        private bool useTowers, exclude600Standalone;
        public bool Exclude600Standalone { get => exclude600Standalone; set { exclude600Standalone = value; Changed(); } }
        private PrimaryBeamMaterial primaryMaterial;
        public PrimaryBeamMaterialOption[] PrimaryMaterialOptions=>PrimaryBeamMaterialOption.All;
        public PrimaryBeamMaterial PrimaryMaterial
        {get=>primaryMaterial;set {PrimaryBeamProfile.For(value);primaryMaterial=value;Changed();}}
        public string PrimaryMaterialAssumptions=>PrimaryBeamProfile.For(PrimaryMaterial).Assumptions;
        public string WS10CatalogueText=>"WS10 catalogue only (mm): "+string.Join("; ",PrimaryBeamProfile.WS10LengthsMm);
        private bool ignoreUplift=true;
        public bool IgnoreUplift { get => ignoreUplift; set { /* Retained for saved-file compatibility. Uplift exclusion is fixed on. */ } }
        private bool allowCustomH20Lengths;
        public bool AllowCustomH20Lengths { get => allowCustomH20Lengths; set { allowCustomH20Lengths = value; Changed(); } }
        private CustomH20LengthScope customH20Scope;
        public CustomH20ScopeOption[] CustomH20ScopeOptions => CustomH20ScopeOption.All;
        public CustomH20LengthScope CustomH20Scope
        {
            get => customH20Scope;
            set { if(!Enum.IsDefined(typeof(CustomH20LengthScope),value))throw new ArgumentException("Select a custom H20 length scope."); customH20Scope=value; Changed(); }
        }
        private double beamLengthMm = 6100, towerLegCapacityKn = 30;
        private string stockLengthsText = TowerStockPlanner.DefaultLengths;
        private double stockEndSetbackMm = 150, maximumConcreteEndOverhangMm = 150;
        public double MaximumConcreteEndOverhangMm { get => maximumConcreteEndOverhangMm; set { maximumConcreteEndOverhangMm = value; Changed(); } }
        private int towerRevision;
        public bool UseTowers { get => useTowers; set { useTowers = value; Changed(); OnPropertyChanged(nameof(UseProps)); } }
        public bool UseProps => !UseTowers;
        public double BeamLengthMm { get => beamLengthMm; set { beamLengthMm = value; Changed(); } }
        public double TowerLegCapacityKn { get => towerLegCapacityKn; set { towerLegCapacityKn = value; Changed(); } }
        public string StockLengthsText { get => stockLengthsText; set { stockLengthsText = value; Changed(); } }
        public double StockEndSetbackMm { get => stockEndSetbackMm; set { stockEndSetbackMm = value; Changed(); } }
        internal bool ReplayConstructionStages {get;set;}
        public TowerLayout TowerResult { get; private set; }
        private CancellationTokenSource towerCancellation;
        internal bool OptimizeCrossingJunctions {get;set;}=true;
        internal bool PrecheckCrossingFootprints {get;set;}=true;
        // Optional deadline for explicitly requested diagnostic runs; normal calculation is unrestricted.
        internal TimeSpan TowerSearchTimeLimit {get;set;}=TimeSpan.FromSeconds(25);
        public bool IsOptimizing { get; private set; }
        public string SearchProgressText=>towerCancellation?.IsCancellationRequested==true ? "Stopping the search…" : beamSearchTrace?.ProgressText??"Preparing the calculation";
        public double SearchElapsedSeconds=>beamSearchTrace?.ElapsedSeconds??0;
        public bool CanCancelOptimization => IsOptimizing && towerCancellation!=null && !towerCancellation.IsCancellationRequested;
        public void CancelOptimization()
        {
            if(!CanCancelOptimization)return;
            towerCancellation.Cancel();WarningMessage="Cancelling tower analysis…";NotifyTower();
        }
        public string TowerFailureDetails {get;private set;}="";
        public bool HasTowerFailure=>!string.IsNullOrEmpty(TowerFailureDetails);
        public bool CanSearchLonger=>UseTowers && !IsOptimizing && !textErrors && (beamSearchTrace?.BudgetReached==true || NetworkResult?.SearchTimeLimitReached==true || TowerResult?.SearchTimeLimitReached==true);
        public bool CanOptimizeTowers => UseTowers && !IsOptimizing && !textErrors;
        public bool CanImport => IsCalculationValid && !UseTowers;
        public bool ShowPropResults => IsCalculationValid && !UseTowers;
        public bool ShowTowerResults => IsCalculationValid && UseTowers;
        public string TowerSummary => TowerResult?.Summary ?? "";
        public string TowerSchedule => TowerResult?.Schedule ?? "";
        public string TowerStockSchedule => TowerResult?.StockSchedule ?? "";
        public string TowerSearchNote => TowerResult == null ? "" : TowerResult.SearchNote + " Tested layouts: " + TowerResult.Tested + ".";
        public const string TowerAssumptions = "Two primary lines use the selected material and its stated strength/stiffness basis. Each stock member is analyzed separately with at least two supports, full uniform load, rigid non-settling supports and free ends. Catalogue primary lengths (or optional custom lengths for H20 only) have no internal gaps, overlaps or projection beyond the concrete ends. Additional custom lengths are multiples of 25 mm; enabling them retains a better stock-only solution. Symmetric concrete end overhang is allowed up to the entered limit (default 75 mm). Only the concrete load directly over the actual primary stock length is included. Concrete beyond the primary ends contributes no load to these checks. Adjacent member ends are independent; no shared support, mechanical butt splice or bending continuity is assumed at a member break. The support-centre setback from every stock end is a planning allowance (default 150 mm), not a verified bearing/head detail. Exact secondary placement and load transfer near member ends require detailing. Deflection criteria L/500, L/350 (default) or L/270 apply separately to each support span and end overhang, including support rotation; L is not the concrete or primary stock length. Legacy fixed limits remain as saved. Checks include every member's shear, moment, deflection and leg reactions. Cross bays 600/1000/1500/2000 mm; frames 1524 mm. Rotated towers are isolated with a common transverse width. Search ranks legs, braced bays, primary quantity, then governing utilization; it is bounded, not an exhaustive global optimum. Enter a permissible leg load applicable to the actual tower configuration. The selected plywood chart sets the secondary spacing limit. Secondary H20s, sheet joints/orientation/end details, local bearing, tower height/bracing/stability, tower self-weight and foundations are outside this calculation. Tower layouts are report-only; existing prop-spacing Revit parameters cannot represent them.";
        private TowerInput CreateTowerInput(bool checkPlywood = true)
        {
            if (textErrors) throw new ArgumentException("Correct the highlighted input before optimizing.");
            if(!IsNetworkMode) {Check(Depth, "Beam depth", true); Check(Width, "Beam width", true);}
            Check(BeamLengthMm, "Beam length", true); Check(TowerLegCapacityKn, "Permissible tower leg load", true);
            Check(StockEndSetbackMm, "Stock end support-centre setback", true);
            Check(MaximumConcreteEndOverhangMm, "Maximum concrete end overhang");
            if (MaximumConcreteEndOverhangMm > 500) throw new ArgumentException("Concrete end allowance must not exceed 500 mm per end.");
            if (DeflectionLimit == null || !ContainsOption(DeflectionLimit)) throw new ArgumentException("Select a deflection limit.");
            if (HasAnySlab && !IsTransferMode) Check(SlabStripWidthMm, "Adjacent slab strip width", true);
            if (HasLeftSlab && !IsNetworkMode) ValidateSlab(LeftSlabThickness, "Left slab thickness");
            if (HasRightSlab && !IsNetworkMode) ValidateSlab(RightSlabThickness, "Right slab thickness");
            if (UseCustomLoads) { Check(CustomLiveLoad, "Placing + working live load"); Check(CustomFormworkLoad, "Formwork load"); }
            if (checkPlywood) ValidatePlywoodSpacing();
            double left = HasLeftSlab && !IsTransferMode ? AreaLoad(LeftSlabThickness) * SlabStripWidthMm / 1000 : 0;
            double right = HasRightSlab && !IsTransferMode ? AreaLoad(RightSlabThickness) * SlabStripWidthMm / 1000 : 0;
            var input = new TowerInput { FullLoadingOnly=!ReplayConstructionStages, EngineerSearch=!ReplayConstructionStages, PrimaryMaterial=PrimaryMaterial, LengthMm = BeamLengthMm, LegCapacityKn = TowerLegCapacityKn, DeflectionLimitMm = DeflectionLimit.Millimetres, DeflectionDenominator = DeflectionLimit.Denominator,
                IgnoreUplift = IgnoreUplift, AllowCustomH20Lengths = AllowCustomH20Lengths, CustomH20Scope = CustomH20Scope, Exclude600Standalone = Exclude600Standalone, MaximumConcreteEndOverhangMm = MaximumConcreteEndOverhangMm, StockLengthsMm = TowerStockPlanner.ParseLengths(StockLengthsText), StockEndSetbackMm = StockEndSetbackMm,
                TotalLineLoad = IsNetworkMode ? 0 : AreaLoad(Depth) * Width / 1000 + left + right,
                TransverseLoadMoment = IsTransferMode ? 0 : (right - left) * (Width + (HasAnySlab ? SlabStripWidthMm : 0)) / 2000 };
            if(IsNetworkMode) { input.Network=CreateNetworkInput(); input.Network.Validate(input); input.TotalLineLoad=input.Network.AreaLoad(input.Network.Beams[0].DepthMm)*input.Network.Beams[0].WidthMm/1000; }
            else if (IsTransferMode)
            {
                if (HasLeftSlab) Check(LeftSlabBayMm, "Left clear slab bay", true);
                if (HasRightSlab) Check(RightSlabBayMm, "Right clear slab bay", true);
                input.TransferredSlab = new TransferredSlabInput { PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm, BeamWidthMm=Width, BeamLineLoadKnM=AreaLoad(Depth)*Width/1000, UpperSpacingMm=UpperSupportSpacingMm,LeftSlabSpacingMm=HasLeftSlab ? LeftSlabSecondarySpacingMm : 0,RightSlabSpacingMm=HasRightSlab ? RightSlabSecondarySpacingMm : 0,
                    LeftBayMm=HasLeftSlab ? LeftSlabBayMm : 0, RightBayMm=HasRightSlab ? RightSlabBayMm : 0, LeftAreaLoadKnM2=HasLeftSlab ? AreaLoad(LeftSlabThickness) : 0, RightAreaLoadKnM2=HasRightSlab ? AreaLoad(RightSlabThickness) : 0 };
                input.TransferredSlab.Validate(input);
            }
            Check(input.TotalLineLoad, "Calculated line load", true); Check(Math.Abs(input.TransverseLoadMoment), "Transverse load moment");
            return input;
        }
        internal void ValidateTowerInputs() { var common=CreateTowerInput(false);if(IndependentBeams.Count>30)throw new ArgumentException("Use at most 30 independent beams.");foreach(var b in IndependentBeams)IndependentInput(common,b); if (BeamLengthMm > 50000) throw new ArgumentException("Beam length must not exceed 50,000 mm."); }
        public async Task OptimizeTowersAsync(bool useTimeLimit=false)
        {
            if (!UseTowers || IsOptimizing) return;
            var cancellation=towerCancellation=new CancellationTokenSource();
            var trace=beamSearchTrace=new BeamSearchTrace(CaptureDiagnosticInputs(),cancellation.Token,useTimeLimit ? TowerSearchTimeLimit : (TimeSpan?)null);
            int revision = towerRevision; networkBeamOptions.Clear(); IsOptimizing = true; TowerResult = null; NetworkResult=null;IndependentBeamResults=new TowerLayout[0]; TowerFailureDetails=""; IsCalculationValid = false;
            WarningMessage = "Searching tower layouts…"; NotifyTower();
            try
            {
                TowerInput input;
                using(trace.Measure(BeamSearchStage.InputValidation))input = CreateTowerInput();
                input.Trace=trace;input.OptimizeCrossingJunctions=OptimizeCrossingJunctions;input.PrecheckCrossingFootprints=PrecheckCrossingFootprints;
                var independentInputs=IndependentBeams.Select(b=>IndependentInput(input,b)).ToArray();
                trace.DerivedInputs(System.FormattableString.Invariant($"Mode: {(input.Network!=null ? "Network" : input.TransferredSlab!=null ? "Transferred slab" : "Beam-only")}; initial beam line load={input.TotalLineLoad:R} kN/m; transverse load moment={input.TransverseLoadMoment:R} kN")
                    +$"\nSearch configuration: requested workers={input.NetworkSearchWorkers}; reuse={input.ReuseSearchWork}; primary reuse={input.ReuseNetworkNumerics}; transverse reuse={input.ReuseTransverseNumerics}; leg-tier traversal={input.SearchByLegTier}; network bounds={input.PruneNetworkBounds}; crossing optimization={input.OptimizeCrossingJunctions}; crossing footprint precheck={input.PrecheckCrossingFootprints}; economy bounds={input.PruneUneconomicCandidates}; staged primary checks={input.StagePrimaryChecks}; defer layouts={input.DeferTransferLayout}; catalogue-only enforcement={input.RespectNetworkCatalogueLengths}"
                    +(input.TransferredSlab==null ? "" : System.FormattableString.Invariant($"\nTransfer beam load={input.TransferredSlab.BeamLineLoadKnM:R} kN/m; slab area loads L/R={input.TransferredSlab.LeftAreaLoadKnM2:R}/{input.TransferredSlab.RightAreaLoadKnM2:R} kN/m²"))
                    +(input.Network==null ? "" : "\n"+string.Join("\n",input.Network.Beams.Select((b,i)=>System.FormattableString.Invariant($"B{i+1}: area load={input.Network.AreaLoad(b.DepthMm):R} kN/m²; beam line load={input.Network.AreaLoad(b.DepthMm)*b.WidthMm/1000:R} kN/m")).Concat(input.Network.Bays.Select((s,i)=>System.FormattableString.Invariant($"S{i+1}: area load={input.Network.AreaLoad(s.ThicknessMm):R} kN/m²"))))));
                TransferNetworkResult networkResult=null;
                TowerLayout[] independentResults=null;
                var result = await Task.Run(() => {
                    using(trace.Measure(BeamSearchStage.SolverWall))
                    {
                        var main=input.Network==null ? TowerOptimizer.Optimize(input) : (networkResult=TransferNetworkOptimizer.Optimize(input,input.Network)).Beams[0];
                        independentResults=independentInputs.Select(i=>TransferredSlabOptimizer.Optimize(i)).ToArray();return main;
                    }
                });
                cancellation.Token.ThrowIfCancellationRequested();
                if (revision != towerRevision || !UseTowers) {trace.Complete("Discarded: inputs changed during search",summary:networkResult?.Summary??result.Summary);return;}
                using(trace.Measure(BeamSearchStage.ResultApplication))
                {
                    if(trace.BudgetReached)
                    {
                        if(networkResult!=null)networkResult.SearchTimeLimitReached=true;
                        foreach(var checkedLayout in (networkResult?.Beams??new[] {result}).Concat(independentResults)) {checkedLayout.SearchTimeLimitReached=true;checkedLayout.MinimumLegCountProven=false;}
                    }
                    IndependentBeamResults=independentResults;NetworkResult=networkResult;SelectedNetworkBeam=0;TowerResult = result; LeftLineLoad = result.LeftLineLoad; RightLineLoad = result.RightLineLoad;
                    TotalLineLoad = result.LeftLineLoad + result.RightLineLoad; LimitingFactor = result.Governing;
                    IsCalculationValid = true; WarningMessage = "";
                }
                trace.Complete(trace.BudgetReached ? "Succeeded: search time limit reached" : "Succeeded",summary:networkResult?.Summary??result.Summary);
            }
            catch (OperationCanceledException) when(cancellation.IsCancellationRequested)
            {
                trace.Complete("Cancelled", "User cancelled the analysis. Search stopped; no partial result was applied.");
                WarningMessage="Tower analysis cancelled. Select Diagnose to copy the search details.";
            }
            catch (BeamSearchDeadlineException ex)
            {
                trace.Complete("Time limit reached: no complete solution",ex.Message);
                if(revision==towerRevision) {WarningMessage=ex.Message;TowerFailureDetails=ex.Message+"\nOpen Diagnose for stage timings and checked candidate counts.";}
            }
            catch (TowerOptimizationException ex) {trace.Complete("No solution",ex.ToString()+"\n"+ex.Details);if(revision==towerRevision) {WarningMessage=ex.Message;TowerFailureDetails=ex.Details;}}
            catch (ArgumentException ex) {trace.Complete("Rejected input/search",ex.ToString());if (revision == towerRevision) WarningMessage = ex.Message; }
            catch (Exception ex) {trace.Complete("Unexpected error",ex.ToString());if(revision==towerRevision)WarningMessage="Unexpected search error. Select Diagnose to copy the details.";}
            finally
            {
                IsOptimizing = false;towerCancellation=null;cancellation.Dispose();
                using(trace.Measure(BeamSearchStage.ResultApplication))NotifyTower();
                trace.Finish();lastCompletedFullSearch=trace;
            }
        }
        internal IDisposable MeasureDrawingPreparation()=>beamSearchTrace?.Measure(BeamSearchStage.DrawingPreparation);
        internal void FinishDrawingPreparation()=>beamSearchTrace?.Finish();
        private void NotifyTower()
        {
            NotifyTransfer();
            foreach (string name in new[] { nameof(IndependentBeamResults),nameof(PrimaryMaterial),nameof(PrimaryMaterialAssumptions),nameof(TowerFailureDetails),nameof(HasTowerFailure),nameof(IsOptimizing), nameof(CanCancelOptimization), nameof(CanSearchLonger), nameof(CanOptimizeTowers), nameof(CanImport), nameof(ShowPropResults), nameof(ShowTowerResults), nameof(TowerSummary), nameof(TowerSchedule), nameof(TowerStockSchedule), nameof(TowerSearchNote), nameof(TowerResult), nameof(WarningMessage), nameof(PrimaryLineSpacingMm), nameof(LeftLineLoad), nameof(RightLineLoad), nameof(TotalLineLoad), nameof(LimitingFactor), nameof(IsCalculationValid) }) OnPropertyChanged(name);
        }
    }
}
