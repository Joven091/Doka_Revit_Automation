using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorViewModel
    {
        private PlywoodProfile selectedPlywood = PlywoodCatalog.Default;
        public IReadOnlyList<PlywoodProfile> PlywoodOptions => PlywoodCatalog.All;
        public PlywoodProfile SelectedPlywood
        {
            get => selectedPlywood;
            set { selectedPlywood=value; Changed(); OnPropertyChanged(nameof(PlywoodProfileId)); OnPropertyChanged(nameof(PlywoodLabel));
                OnPropertyChanged(nameof(PlywoodSelectionNote)); OnPropertyChanged(nameof(PlywoodCalculationBasis)); OnPropertyChanged(nameof(FormworkDepthMm)); }
        }
        public string PlywoodProfileId { get => SelectedPlywood?.Id; set => SelectedPlywood=PlywoodCatalog.FromId(value); }
        public string PlywoodLabel => SelectedPlywood?.Label ?? "Select plywood";
        public string PlywoodSelectionNote => SelectedPlywood?.Note ?? "Select a plywood product and thickness.";
        public string PlywoodCalculationBasis => (SelectedPlywood?.Basis ?? "Select a plywood product and thickness.") + " " + PlywoodSpacing.SheetBasis;
        public PlywoodSpacingResult PlywoodResult { get; private set; }
        public double MaximumSecondarySpacingMm => PlywoodResult?.MaximumSpacingMm ?? 0;
        public double PlywoodPressureKnM2 => PlywoodResult?.PressureKnM2 ?? 0;
        public string SecondarySpacingGoverning => PlywoodResult?.Governing ?? "";
        public string PlywoodCriterion => PlywoodResult == null ? "" : "L/" + PlywoodResult.Denominator;
        public string PlywoodSpacingWarning { get; private set; } = "";
        public string SecondarySpacingSummary => PlywoodResult == null ? PlywoodSpacingWarning
            : $"{PlywoodLabel} · maximum secondary spacing {MaximumSecondarySpacingMm:0} mm · {PlywoodCriterion}\n"
            + $"Pressure {PlywoodPressureKnM2:0.00} kN/m² · governs: {SecondarySpacingGoverning} · approximate chart reading"
            + $"\n2440 × 1220 mm sheet layout · chart maximum {PlywoodResult.ChartMaximumSpacingMm:0} mm · shorter spans at joints"
            + (PlywoodResult.ChartPressureKnM2 > PlywoodPressureKnM2 ? $"\nUses the chart minimum of {PlywoodResult.ChartPressureKnM2:0.###} kN/m²." : "")
            + (DeflectionLimit.Denominator == 0 ? "\nLegacy H20 limit retained; plywood uses L/500." : "");

        private void CalculatePlywoodSpacing()
        {
            PlywoodResult = null; PlywoodSpacingWarning = "";
            try
            {
                if (textErrors) throw new ArgumentException("Correct the highlighted input to calculate secondary spacing.");
                if (SelectedPlywood==null || !PlywoodOptions.Contains(SelectedPlywood)) throw new ArgumentException("Select a plywood product and thickness.");
                if (DeflectionLimit == null || !ContainsOption(DeflectionLimit)) throw new ArgumentException("Select a deflection limit.");
                if (UseCustomLoads) { Check(CustomLiveLoad, "Placing + working live load"); Check(CustomFormworkLoad, "Formwork load"); }
                double pressure;
                if (IsNetworkMode)
                {
                    // A common row spacing must cover the deepest beam, slab and crossing beam.
                    var input = CreateNetworkInput();
                    // The real spacing is derived below; validate geometry using the chart minimum
                    // so clearing the previous result cannot create a spacing/pressure dependency.
                    input.UpperSpacingMm = Math.Max(100, SelectedPlywood.MinimumSpanMm);
                    input.PlywoodSheetLengthMm = 0; // Temporary chart-domain geometry validation, before selecting a sheet spacing.
                    input.Validate(new TowerInput {LengthMm=BeamLengthMm, LegCapacityKn=TowerLegCapacityKn,
                        StockEndSetbackMm=StockEndSetbackMm, MaximumConcreteEndOverhangMm=MaximumConcreteEndOverhangMm,
                        DeflectionDenominator=DeflectionLimit.Denominator, StockLengthsMm=TowerStockPlanner.ParseLengths(StockLengthsText)});
                    pressure = input.Beams.Select(b=>AreaLoad(b.DepthMm))
                        .Concat(input.Bays.Select(b=>AreaLoad(b.ThicknessMm)))
                        .Concat(input.CrossingBeams.Select(b=>AreaLoad(b.DepthMm))).Max();
                }
                else
                {
                    Check(Depth, "Beam depth", true);
                    if (HasLeftSlab) ValidateSlab(LeftSlabThickness, "Left slab thickness");
                    if (HasRightSlab) ValidateSlab(RightSlabThickness, "Right slab thickness");
                    pressure = AreaLoad(Depth);
                }
                PlywoodResult = PlywoodSpacing.CalculateForSheet(SelectedPlywood, pressure, DeflectionLimit.Denominator == 0 ? 500 : DeflectionLimit.Denominator);
            }
            catch (ArgumentException ex) { PlywoodSpacingWarning = ex.Message; }
            foreach (string name in new[] { nameof(PlywoodResult), nameof(MaximumSecondarySpacingMm), nameof(UpperSupportSpacingMm), nameof(PlywoodPressureKnM2),
                nameof(SecondarySpacingGoverning), nameof(PlywoodCriterion), nameof(PlywoodSpacingWarning), nameof(SecondarySpacingSummary) }) OnPropertyChanged(name);
        }

        private void ValidatePlywoodSpacing()
        {
            if (PlywoodResult == null) throw new ArgumentException(PlywoodSpacingWarning);
        }
    }
}
