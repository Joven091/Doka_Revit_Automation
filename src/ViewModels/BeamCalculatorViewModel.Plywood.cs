using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorViewModel
    {
        private PlywoodProfile selectedPlywood = PlywoodCatalog.Default;
        private string plywoodDeflectionLimit = "L/350";
        public IReadOnlyList<string> PlywoodDeflectionLimitOptions => PlywoodSpacing.DeflectionLimitOptions;
        public string PlywoodDeflectionLimit
        {
            get => plywoodDeflectionLimit;
            set { plywoodDeflectionLimit=value; Changed(); }
        }
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
        public PlywoodSpacingResult LeftSlabPlywoodResult {get;private set;}
        public PlywoodSpacingResult RightSlabPlywoodResult {get;private set;}
        public double LeftSlabSecondarySpacingMm=>LeftSlabPlywoodResult?.MaximumSpacingMm??0;
        public double RightSlabSecondarySpacingMm=>RightSlabPlywoodResult?.MaximumSpacingMm??0;
        public string ElementPlywoodSpacingSummary=>(IsNetworkMode
            ? string.Join("\n",NetworkBeams.Select(b=>$"{b.Label}: beam {b.DepthMm:0} mm · spacing limit {b.MaximumSecondarySpacingMm:0} mm")
                .Concat(NetworkBays.Select(b=>$"{b.Label}: slab {b.ThicknessMm:0} mm · spacing limit {b.MaximumSecondarySpacingMm:0} mm"))
                .Concat(CrossingBeams.Select((c,i)=>$"X{i+1}: crossing {c.DepthMm:0} mm · spacing limit {c.MaximumSecondarySpacingMm:0} mm")))
            : $"Beam: {MaximumSecondarySpacingMm:0} mm"+(HasLeftSlab ? $"\nLeft slab: {LeftSlabSecondarySpacingMm:0} mm" : "")+(HasRightSlab ? $"\nRight slab: {RightSlabSecondarySpacingMm:0} mm" : ""))
            +string.Concat(IndependentBeams.Select(b=>$"\n{b.Label}: independent beam {b.DepthMm:0} mm · spacing limit {b.MaximumSecondarySpacingMm:0} mm"));
        public double MaximumSecondarySpacingMm => PlywoodResult?.MaximumSpacingMm ?? 0;
        public double PlywoodPressureKnM2 => PlywoodResult?.PressureKnM2 ?? 0;
        public string SecondarySpacingGoverning => PlywoodResult?.Governing ?? "";
        public string PlywoodCriterion => PlywoodResult == null ? "" : "L/" + PlywoodResult.Denominator;
        public string PlywoodSpacingWarning { get; private set; } = "";
        public string SecondarySpacingSummary => PlywoodResult == null ? PlywoodSpacingWarning
            : $"{PlywoodLabel} · maximum secondary spacing {MaximumSecondarySpacingMm:0} mm · {PlywoodCriterion}\n"
            + $"Pressure {PlywoodPressureKnM2:0.00} kN/m² · governs: {SecondarySpacingGoverning} · approximate chart reading"
            + $"\n2440 × 1220 mm sheet layout · chart maximum {PlywoodResult.ChartMaximumSpacingMm:0} mm · shorter spans at joints"
            + (PlywoodResult.ChartPressureKnM2 > PlywoodPressureKnM2 ? $"\nUses the chart minimum of {PlywoodResult.ChartPressureKnM2:0.###} kN/m²." : "");

        private void CalculatePlywoodSpacing()
        {
            PlywoodResult = null; LeftSlabPlywoodResult=RightSlabPlywoodResult=null; PlywoodSpacingWarning = "";
            try
            {
                if (textErrors) throw new ArgumentException("Correct the highlighted input to calculate secondary spacing.");
                if (SelectedPlywood==null || !PlywoodOptions.Contains(SelectedPlywood)) throw new ArgumentException("Select a plywood product and thickness.");
                if (DeflectionLimit == null || !ContainsOption(DeflectionLimit)) throw new ArgumentException("Select an H20 deflection limit.");
                int plywoodDenominator=PlywoodSpacing.ParseDeflectionLimit(PlywoodDeflectionLimit);
                if (UseCustomLoads) { Check(CustomLiveLoad, "Placing + working live load"); Check(CustomFormworkLoad, "Formwork load"); }
                double pressure;
                if (IsNetworkMode)
                {
                    // Derive each beam, slab and crossing limit from its own concrete depth.
                    var input = CreateNetworkInput();
                    // The real spacing is derived below; validate geometry using the chart minimum
                    // so clearing the previous result cannot create a spacing/pressure dependency.
                    input.BeamSpacingsMm=input.SlabSpacingsMm=input.CrossingSpacingsMm=null;
                    input.UpperSpacingMm = Math.Max(100, SelectedPlywood.MinimumSpanMm);
                    input.PlywoodSheetLengthMm = 0; // Temporary chart-domain geometry validation, before selecting a sheet spacing.
                    input.Validate(new TowerInput {LengthMm=BeamLengthMm, LegCapacityKn=TowerLegCapacityKn,
                        StockEndSetbackMm=StockEndSetbackMm, MaximumConcreteEndOverhangMm=MaximumConcreteEndOverhangMm,
                        DeflectionDenominator=DeflectionLimit.Denominator, StockLengthsMm=TowerStockPlanner.ParseLengths(StockLengthsText)});
                    var beamResults=input.Beams.Select(b=>PlywoodSpacing.CalculateForSheet(SelectedPlywood,AreaLoad(b.DepthMm),plywoodDenominator)).ToArray();
                    var slabResults=input.Bays.Select((b,i)=>PlywoodSpacing.CalculateForSheet(SelectedPlywood,
                        AreaLoad(input.CrossingBeams.Where(c=>c.BayNumber==i+1 && c.SupportMode==CrossBeamSupportMode.LegacySlabRows).Select(c=>c.DepthMm).Concat(new[] {b.ThicknessMm}).Max()),plywoodDenominator)).ToArray();
                    var crossResults=input.CrossingBeams.Select(c=>PlywoodSpacing.CalculateForSheet(SelectedPlywood,AreaLoad(c.DepthMm),plywoodDenominator)).ToArray();
                    for(int i=0;i<NetworkBeams.Count;i++)NetworkBeams[i].SetPlywoodSpacing(beamResults[i].MaximumSpacingMm);
                    for(int i=0;i<NetworkBays.Count;i++)NetworkBays[i].SetPlywoodSpacing(slabResults[i].MaximumSpacingMm);
                    for(int i=0;i<CrossingBeams.Count;i++)CrossingBeams[i].SetPlywoodSpacing(crossResults[i].MaximumSpacingMm);
                    pressure=beamResults.Max(r=>r.PressureKnM2);
                }
                else
                {
                    Check(Depth, "Beam depth", true);
                    if (HasLeftSlab) ValidateSlab(LeftSlabThickness, "Left slab thickness");
                    if (HasRightSlab) ValidateSlab(RightSlabThickness, "Right slab thickness");
                    pressure = AreaLoad(Depth);
                    if(HasLeftSlab)LeftSlabPlywoodResult=PlywoodSpacing.CalculateForSheet(SelectedPlywood,AreaLoad(LeftSlabThickness),plywoodDenominator);
                    if(HasRightSlab)RightSlabPlywoodResult=PlywoodSpacing.CalculateForSheet(SelectedPlywood,AreaLoad(RightSlabThickness),plywoodDenominator);
                }
                foreach(var b in IndependentBeams){b.Validate();b.SetPlywoodSpacing(PlywoodSpacing.CalculateForSheet(SelectedPlywood,AreaLoad(b.DepthMm),plywoodDenominator).MaximumSpacingMm);}
                PlywoodResult = PlywoodSpacing.CalculateForSheet(SelectedPlywood, pressure, plywoodDenominator);
            }
            catch (ArgumentException ex)
            {
                PlywoodResult=null;LeftSlabPlywoodResult=RightSlabPlywoodResult=null;PlywoodSpacingWarning=ex.Message;
                foreach(var b in NetworkBeams)b.SetPlywoodSpacing(0);foreach(var b in NetworkBays)b.SetPlywoodSpacing(0);foreach(var c in CrossingBeams)c.SetPlywoodSpacing(0);foreach(var b in IndependentBeams)b.SetPlywoodSpacing(0);
            }
            foreach (string name in new[] { nameof(PlywoodResult), nameof(MaximumSecondarySpacingMm), nameof(UpperSupportSpacingMm), nameof(PlywoodPressureKnM2),
                nameof(LeftSlabSecondarySpacingMm),nameof(RightSlabSecondarySpacingMm),nameof(ElementPlywoodSpacingSummary),nameof(SecondarySpacingGoverning), nameof(PlywoodCriterion), nameof(PlywoodSpacingWarning), nameof(SecondarySpacingSummary) }) OnPropertyChanged(name);
        }

        private void ValidatePlywoodSpacing()
        {
            if (PlywoodResult == null) throw new ArgumentException(PlywoodSpacingWarning);
        }
    }
}
