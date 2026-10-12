using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EngineeringDashboard
{
    public sealed class BeamDeflectionOption
    {
        public string Label { get; }
        public double Millimetres { get; }
        public int Denominator { get; }
        public double AllowableMm(double lengthMm) => Denominator == 0 ? Millimetres : lengthMm / Denominator;
        public BeamDeflectionOption(int denominator) { Denominator = denominator; Label = "L/" + denominator; }

        public BeamDeflectionOption(string label, double millimetres) { Label = label; Millimetres = millimetres; }
    }

    public partial class BeamCalculatorViewModel : INotifyPropertyChanged
    {
        public static IReadOnlyList<BeamDeflectionOption> AvailableDeflectionOptions { get; } = Array.AsReadOnly(new[] {
            new BeamDeflectionOption(500), new BeamDeflectionOption(350), new BeamDeflectionOption(270)
        });
        private static readonly BeamDeflectionOption[] legacyOptions = { new BeamDeflectionOption("Legacy fixed 3 mm", 3), new BeamDeflectionOption("Legacy fixed 5 mm", 5), new BeamDeflectionOption("Legacy fixed 10 mm", 10) };
        public static BeamDeflectionOption RestoreDeflectionOption(string criterion, string fixedMm)
        {
            var option = criterion == null ? legacyOptions.SingleOrDefault(x => x.Millimetres.ToString(System.Globalization.CultureInfo.InvariantCulture) == fixedMm)
                : AvailableDeflectionOptions.SingleOrDefault(x => x.Label == criterion) ?? legacyOptions.SingleOrDefault(x => x.Label == criterion);
            if (option == null || option.Denominator == 0 && fixedMm != option.Millimetres.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new ArgumentException("The saved deflection criterion is invalid.");
            return option;
        }
        public IReadOnlyList<BeamDeflectionOption> DeflectionOptions => deflection?.Denominator == 0 ? AvailableDeflectionOptions.Concat(new[] { deflection }).ToArray() : AvailableDeflectionOptions;
        public string DeflectionCriteriaNote => "L = each H20 support span; overhangs use their own length. Includes support rotation.";
        public double PrimaryDeflectionMm { get; private set; }
        public double PrimaryDeflectionUtilization { get; private set; }
        public string PrimaryDeflectionSummary => $"H20 deflection: {PrimaryDeflectionMm:0.00} mm · {PrimaryDeflectionUtilization*100:0.0}% of criterion";
        private double depth = 500, width = 200, leftThickness = 200, rightThickness = 200;
        private double floorHeight = 3000;
        private BeamPropOption selectedProp = BeamPropOption.All.First(p => p.CapacityKey == "300 at bottom");
        public IReadOnlyList<BeamPropOption> PropOptions => BeamPropOption.All;
        public BeamPropOption SelectedProp
        {
            get => selectedProp;
            set { selectedProp = value; Changed(); NotifyPropSelection(); }
        }
        public IReadOnlyList<string> PropFamilyOptions => BeamPropOption.All.Select(p => p.Family).Distinct().ToArray();
        public IReadOnlyList<string> PropSizeOptions => BeamPropOption.All.Where(p => p.Family == PropFamily).Select(p => p.Size).Distinct().ToArray();
        public IReadOnlyList<string> PropOrientationOptions => BeamPropOption.All.Where(p => p.Family == PropFamily && p.Size == PropSize).Select(p => p.Orientation).Distinct().ToArray();
        public string PropFamily
        {
            get => SelectedProp?.Family;
            set { if (value != null && value != PropFamily) SelectProp(BeamPropOption.All.Where(p => p.Family == value), PropSize, OuterTubePosition); }
        }
        public string PropSize
        {
            get => SelectedProp?.Size;
            set { if (value != null && value != PropSize) SelectProp(BeamPropOption.All.Where(p => p.Family == PropFamily && p.Size == value), value, OuterTubePosition); }
        }
        public string OuterTubePosition
        {
            get => SelectedProp?.Orientation;
            set { if (value != null && value != OuterTubePosition) SelectProp(BeamPropOption.All.Where(p => p.Family == PropFamily && p.Size == PropSize && p.Orientation == value), PropSize, value); }
        }
        private void SelectProp(IEnumerable<BeamPropOption> candidates, string size, string orientation)
        {
            var choices = candidates.ToArray();
            var choice = choices.FirstOrDefault(p => p.Size == size && p.Orientation == orientation)
                ?? choices.FirstOrDefault(p => p.Size == size) ?? choices.FirstOrDefault();
            if (choice != null) SelectedProp = choice;
        }
        private void NotifyPropSelection()
        {
            foreach (string name in new[] { nameof(PropFamilyOptions), nameof(PropSizeOptions), nameof(PropOrientationOptions), nameof(PropFamily), nameof(PropSize), nameof(OuterTubePosition) }) OnPropertyChanged(name);
        }
        public TowerBeamAnalysis AnalyzePropPrimary(int line, int supportCount)
        {
            if (UseTowers || !IsCalculationValid) throw new ArgumentException("Calculate a valid Dokaflex layout first.");
            if (line < 0 || line > 1 || (NumberOfProps == "4+" ? supportCount < 4 || supportCount > 12 : supportCount.ToString() != NumberOfProps))
                throw new ArgumentException("Select a primary line and a supported prop count.");
            double span = MaximumPropSpacing / 1000, end = PrimaryBeamCantilever / 1000;
            return TowerBeamAnalysis.Analyze((supportCount - 1) * span + 2 * end,
                Enumerable.Range(0, supportCount).Select(i => end + i * span).ToArray(),
                line == 0 ? LeftLineLoad : RightLineLoad, 449.8756);
        }
        public double FloorHeight { get => floorHeight; set { floorHeight = value; Changed(); } }
        public const double FormworkDepthMm = 418;
        public double PropExtensionMm => FloorHeight - Depth - FormworkDepthMm;
        public double PropCapacityKn { get; private set; }
        public double PropLookupExtensionMm { get; private set; }
        public double PropUtilizationPercent { get; private set; }
        private bool hasLeft, hasRight, customLoads, textErrors;
        private double customLive = 2.25, customFormwork = 0.4;
        private string numberOfProps = "2";
        private BeamDeflectionOption deflection = AvailableDeflectionOptions[1];

        public BeamCalculatorViewModel() { Calculate(); }
        public double Depth { get => depth; set { depth = value; Changed(); } }
        public double Width { get => width; set { width = value; Changed(); } }
        public bool HasLeftSlab { get => hasLeft; set { hasLeft = value; Changed(); } }
        public bool HasRightSlab { get => hasRight; set { hasRight = value; Changed(); } }
        public double LeftSlabThickness { get => leftThickness; set { leftThickness = value; Changed(); } }
        public double RightSlabThickness { get => rightThickness; set { rightThickness = value; Changed(); } }
        public bool UseCustomLoads { get => customLoads; set { customLoads = value; Changed(); } }
        public double CustomLiveLoad { get => customLive; set { customLive = value; Changed(); } }
        public double CustomFormworkLoad { get => customFormwork; set { customFormwork = value; Changed(); } }
        public string NumberOfProps { get => numberOfProps; set { numberOfProps = value; Changed(); } }
        public BeamDeflectionOption DeflectionLimit { get => deflection; set { deflection = value; Changed(); OnPropertyChanged(nameof(DeflectionOptions)); } }
        public bool IsCalculationValid { get; private set; }
        public string WarningMessage { get; private set; }
        public double MaximumPropSpacing { get; private set; }
        public double MaximumPropLoad { get; private set; }
        public double PrimaryBeamCantilever { get; private set; }
        public string LimitingFactor { get; private set; }
        public double LeftLineLoad { get; private set; }
        public double RightLineLoad { get; private set; }
        public double TotalLineLoad { get; private set; }
        // Existing transverse arrangement: primary lines at the quarter points of
        // the secondary member, whose overall length is beam width + 1400 mm.
        public double PrimaryLineSpacingMm => UseTowers ? TowerResult?.WidthMm ?? 1524 : (Width + 1400) / 2;
        private double slabStripWidthMm = 300;
        public double SlabStripWidthMm { get => slabStripWidthMm; set { slabStripWidthMm = value; Changed(); } }
        public bool HasAnySlab => HasLeftSlab || HasRightSlab;

        public void SetTextInputErrors(bool value)
        {
            calculatorTextErrors=value;RefreshTextInputErrors();
        }
        private bool calculatorTextErrors;
        private readonly HashSet<object> additionalTextErrors=new HashSet<object>();
        internal void SetAdditionalTextInputErrors(object source,bool value)
        {
            if(value)additionalTextErrors.Add(source);else additionalTextErrors.Remove(source);
            RefreshTextInputErrors();
        }
        private void RefreshTextInputErrors()
        {
            bool value=calculatorTextErrors || additionalTextErrors.Count>0;
            if(textErrors==value)return;textErrors=value;Calculate();
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void Check(double value, string label, bool positive = false)
        {
            if (!Finite(value) || value < 0 || positive && value == 0)
                throw new ArgumentException(label + (positive ? " must be a finite number greater than zero." : " must be a finite number zero or greater."));
        }
        public static double HeapingLoad(double selfWeight)
        {
            Check(selfWeight, "Concrete self-weight");
            return Math.Max(0.75, Math.Min(1.75, 0.10 * selfWeight));
        }
        private double AreaLoad(double thickness)
        {
            double concrete = 25 * thickness / 1000;
            return concrete + (UseCustomLoads ? CustomLiveLoad + CustomFormworkLoad : HeapingLoad(concrete) + 0.75 + 0.40);
        }
        private void Calculate()
        {
            towerRevision++; networkBeamOptions.Clear(); TowerFailureDetails=""; TowerResult = null; NetworkResult=null; SelectedNetworkBeam=0;
            IsCalculationValid = false;
            MaximumPropSpacing = MaximumPropLoad = PrimaryBeamCantilever = PrimaryDeflectionMm = PrimaryDeflectionUtilization = 0;
            LeftLineLoad = RightLineLoad = TotalLineLoad = 0;
            PropCapacityKn = PropLookupExtensionMm = PropUtilizationPercent = 0;
            LimitingFactor = string.Empty; WarningMessage = string.Empty;
            if (UseTowers)
            {
                try { CreateTowerInput(); WarningMessage = "Select Optimize towers to calculate the current inputs."; }
                catch (ArgumentException ex) { WarningMessage = ex.Message; }
                NotifyTower(); return;
            }
            try
            {
                if (textErrors) throw new ArgumentException("Correct the highlighted input before calculating or importing.");
                Check(Depth, "Beam depth", true); Check(Width, "Beam width", true);
                Check(FloorHeight, "Floor height", true); Check(PropExtensionMm, "Prop extension (floor height minus beam depth and 418 mm formwork)", true);
                if (SelectedProp == null || !BeamPropOption.All.Contains(SelectedProp)) throw new ArgumentException("Select a prop type and orientation.");
                double capacity = SelectedProp.Capacity(PropExtensionMm, out double lookupMeters);
                if (HasAnySlab) Check(SlabStripWidthMm, "Adjacent slab strip width", true);
                if (HasLeftSlab) ValidateSlab(LeftSlabThickness, "Left slab thickness");
                if (HasRightSlab) ValidateSlab(RightSlabThickness, "Right slab thickness");
                if (UseCustomLoads) { Check(CustomLiveLoad, "Placing + working live load"); Check(CustomFormworkLoad, "Formwork load"); }
                if (NumberOfProps != "2" && NumberOfProps != "3" && NumberOfProps != "4+") throw new ArgumentException("Select a supported number of props.");
                if (DeflectionLimit == null || !ContainsOption(DeflectionLimit)) throw new ArgumentException("Select a deflection limit.");

                double beam = AreaLoad(Depth) * Width / 1000;
                double left = HasLeftSlab ? AreaLoad(LeftSlabThickness) * SlabStripWidthMm / 1000 : 0;
                double right = HasRightSlab ? AreaLoad(RightSlabThickness) * SlabStripWidthMm / 1000 : 0;
                double total = beam + left + right;
                Check(total, "Calculated line load", true);
                double stripCentroid = (Width + (HasAnySlab ? SlabStripWidthMm : 0)) / 2000;
                // Sum forces and moments about the beam centre. Use the heavier line.
                double imbalance = (right - left) * stripCentroid / (PrimaryLineSpacingMm / 1000);
                double leftReaction = total / 2 - imbalance, rightReaction = total / 2 + imbalance;
                Check(leftReaction, "Left support-line reaction", true); Check(rightReaction, "Right support-line reaction", true);
                double w = Math.Max(leftReaction, rightReaction);
                double a, m, v, r, d;
                if (NumberOfProps == "2") { a = 0.354; m = 1.0 / 16; v = 0.5; r = 0; d = 1.0 / 192; }
                else if (NumberOfProps == "3") { a = 0.25; m = 0.125; v = 0.625; r = 1.25; d = 1.0 / 185; }
                else { a = 0.25; m = 0.10; v = 0.60; r = 1.143; d = 1.0 / 145; }
                double moment = 1000 * Math.Sqrt(5 / (m * w));
                double shear = 1000 * 11 / (v * w);
                double deflect = DeflectionLimit.Denominator == 0
                    ? 1000 * Math.Pow((DeflectionLimit.Millimetres / 1000 * 449.8756) / (d * w), 0.25)
                    : 1000 * Math.Pow(449.8756 / (DeflectionLimit.Denominator * d * w), 1.0 / 3);
                double limit = Math.Min(moment, Math.Min(shear, deflect));
                string governing = limit == moment ? "Moment" : limit == shear ? "Shear" : "Deflection";
                double ceiling = NumberOfProps == "2" ? 2000 : 3000;
                if (ceiling <= limit) { limit = ceiling; governing = "Max Spacing Limit"; }
                Check(limit, "Calculated span", true);
                double span = Math.Floor(limit / 25) * 25;
                if (span < 25) throw new ArgumentException("No spacing of at least 25 mm satisfies these inputs.");
                double cantilever = 0, prop = 0;
                // Test the actual rounded layout, so prop capacity cannot be exceeded by rounding.
                for (; span >= 25; span -= 25)
                {
                    cantilever = Math.Floor(span * a / 25) * 25;
                    prop = NumberOfProps == "2" ? 0.5 * w * (span + 2 * cantilever) / 1000 : r * w * span / 1000;
                    if (prop > capacity) { governing = "Prop Capacity"; continue; }
                    double deflectionUse = 0, peakDeflection = 0;
                    int count = NumberOfProps == "2" ? 2 : NumberOfProps == "3" ? 3 : 4;
                    for (int n = count; n <= (count == 4 ? 12 : count); n++)
                    {
                        var response = TowerBeamAnalysis.Analyze(((n - 1) * span + 2 * cantilever) / 1000,
                            Enumerable.Range(0, n).Select(i => (cantilever + i * span) / 1000).ToArray(), w, 449.8756);
                        peakDeflection = Math.Max(peakDeflection, response.DeflectionMm);
                        deflectionUse = Math.Max(deflectionUse, response.DeflectionUtilization(DeflectionLimit.Millimetres, DeflectionLimit.Denominator));
                    }
                    if (deflectionUse <= 1 + 1e-10) { PrimaryDeflectionMm = peakDeflection; PrimaryDeflectionUtilization = deflectionUse; break; }
                    governing = "Deflection";
                }
                if (span < 25) throw new ArgumentException("No spacing of at least 25 mm satisfies the selected prop capacity.");
                Check(prop, "Calculated prop reaction", true);
                MaximumPropSpacing = span; PrimaryBeamCantilever = cantilever; MaximumPropLoad = prop;
                LeftLineLoad = leftReaction; RightLineLoad = rightReaction; TotalLineLoad = total;
                PropCapacityKn = capacity; PropLookupExtensionMm = lookupMeters * 1000;
                PropUtilizationPercent = prop / capacity * 100;
                LimitingFactor = governing; IsCalculationValid = true;
            }
            catch (ArgumentException ex) { WarningMessage = ex.Message; }
            NotifyTower();
            foreach (string name in new[] { nameof(PrimaryDeflectionSummary), nameof(PrimaryDeflectionMm), nameof(PrimaryDeflectionUtilization), nameof(MaximumPropSpacing), nameof(MaximumPropLoad), nameof(PrimaryBeamCantilever), nameof(LimitingFactor), nameof(WarningMessage), nameof(LeftLineLoad), nameof(RightLineLoad), nameof(TotalLineLoad), nameof(PrimaryLineSpacingMm), nameof(PropExtensionMm), nameof(PropCapacityKn), nameof(PropLookupExtensionMm), nameof(PropUtilizationPercent), nameof(IsCalculationValid) }) OnPropertyChanged(name);
        }
        private void Changed([CallerMemberName] string property = null) { Calculate(); OnPropertyChanged(property); if(property == nameof(HasLeftSlab) || property == nameof(HasRightSlab)) OnPropertyChanged(nameof(HasAnySlab)); }
        private void ValidateSlab(double thickness, string label)
        {
            Check(thickness, label, true);
            if (thickness > Depth) throw new ArgumentException(label + " cannot exceed the total beam depth.");
        }
        private static bool ContainsOption(BeamDeflectionOption option)
        {
            foreach (var item in AvailableDeflectionOptions) if (ReferenceEquals(item, option)) return true;
            return legacyOptions.Contains(option);
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
