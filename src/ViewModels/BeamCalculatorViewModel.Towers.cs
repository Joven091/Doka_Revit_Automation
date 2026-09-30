using System;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorViewModel
    {
        private bool useTowers;
        private double beamLengthMm = 6000, towerLegCapacityKn = 30;
        private int towerRevision;
        public bool UseTowers { get => useTowers; set { useTowers = value; Changed(); OnPropertyChanged(nameof(UseProps)); } }
        public bool UseProps => !UseTowers;
        public double BeamLengthMm { get => beamLengthMm; set { beamLengthMm = value; Changed(); } }
        public double TowerLegCapacityKn { get => towerLegCapacityKn; set { towerLegCapacityKn = value; Changed(); } }
        public TowerLayout TowerResult { get; private set; }
        public bool IsOptimizing { get; private set; }
        public bool CanOptimizeTowers => UseTowers && !IsOptimizing && !textErrors;
        public bool CanImport => IsCalculationValid && !UseTowers;
        public bool ShowPropResults => IsCalculationValid && !UseTowers;
        public bool ShowTowerResults => IsCalculationValid && UseTowers;
        public string TowerSummary => TowerResult?.Summary ?? "";
        public string TowerSchedule => TowerResult?.Schedule ?? "";
        public string TowerSearchNote => TowerResult == null ? "" : TowerResult.SearchNote + " Tested layouts: " + TowerResult.Tested + ".";
        public const string TowerAssumptions = "Two continuous double-H20 primary lines; each pair shares load equally. Pair limits: moment 10 kNm, shear 22 kN, EI 899.7512 kNm². Full uniform load, rigid non-settling supports, free ends. Checks include unequal spans, end overhangs and every leg reaction. Cross bays 600/1000/1500/2000 mm; frames 1524 mm. Rotated towers are isolated and use a common transverse width. Gaps and left end offsets use 25 mm increments; the right overhang takes the remainder. Search ranks legs, groups, then governing utilization; it is bounded, not an exhaustive global optimum. Enter a permissible leg load applicable to the actual tower configuration. Secondary H20s, sheeting, timber stock lengths/joints, bearing, tower height/bracing/stability and foundations are outside this calculation. Tower layouts are report-only; existing prop-spacing Revit parameters cannot represent them.";
        private TowerInput CreateTowerInput()
        {
            if (textErrors) throw new ArgumentException("Correct the highlighted input before optimizing.");
            Check(Depth, "Beam depth", true); Check(Width, "Beam width", true);
            Check(BeamLengthMm, "Beam length", true); Check(TowerLegCapacityKn, "Permissible tower leg load", true);
            if (DeflectionLimit == null || !ContainsOption(DeflectionLimit)) throw new ArgumentException("Select a deflection limit.");
            if (HasAnySlab) Check(SlabStripWidthMm, "Adjacent slab strip width", true);
            if (HasLeftSlab) ValidateSlab(LeftSlabThickness, "Left slab thickness");
            if (HasRightSlab) ValidateSlab(RightSlabThickness, "Right slab thickness");
            if (UseCustomLoads) { Check(CustomLiveLoad, "Placing + working live load"); Check(CustomFormworkLoad, "Formwork load"); }
            double left = HasLeftSlab ? AreaLoad(LeftSlabThickness) * SlabStripWidthMm / 1000 : 0;
            double right = HasRightSlab ? AreaLoad(RightSlabThickness) * SlabStripWidthMm / 1000 : 0;
            var input = new TowerInput { LengthMm = BeamLengthMm, LegCapacityKn = TowerLegCapacityKn, DeflectionLimitMm = DeflectionLimit.Millimetres,
                TotalLineLoad = AreaLoad(Depth) * Width / 1000 + left + right,
                TransverseLoadMoment = (right - left) * (Width + (HasAnySlab ? SlabStripWidthMm : 0)) / 2000 };
            Check(input.TotalLineLoad, "Calculated line load", true); Check(Math.Abs(input.TransverseLoadMoment), "Transverse load moment");
            return input;
        }
        internal void ValidateTowerInputs() { CreateTowerInput(); if (BeamLengthMm > 50000) throw new ArgumentException("Beam length must not exceed 50,000 mm."); }
        public async Task OptimizeTowersAsync()
        {
            if (!UseTowers || IsOptimizing) return;
            int revision = towerRevision; IsOptimizing = true; TowerResult = null; IsCalculationValid = false;
            WarningMessage = "Searching tower layouts…"; NotifyTower();
            try
            {
                var input = CreateTowerInput();
                var result = await Task.Run(() => TowerOptimizer.Optimize(input));
                if (revision != towerRevision || !UseTowers) return;
                TowerResult = result; LeftLineLoad = result.LeftLineLoad; RightLineLoad = result.RightLineLoad;
                TotalLineLoad = input.TotalLineLoad; LimitingFactor = result.Governing;
                IsCalculationValid = true; WarningMessage = "";
            }
            catch (ArgumentException ex) { if (revision == towerRevision) WarningMessage = ex.Message; }
            finally { IsOptimizing = false; NotifyTower(); }
        }
        private void NotifyTower()
        {
            foreach (string name in new[] { nameof(IsOptimizing), nameof(CanOptimizeTowers), nameof(CanImport), nameof(ShowPropResults), nameof(ShowTowerResults), nameof(TowerSummary), nameof(TowerSchedule), nameof(TowerSearchNote), nameof(TowerResult), nameof(WarningMessage), nameof(PrimaryLineSpacingMm), nameof(LeftLineLoad), nameof(RightLineLoad), nameof(TotalLineLoad), nameof(LimitingFactor), nameof(IsCalculationValid) }) OnPropertyChanged(name);
        }
    }
}
