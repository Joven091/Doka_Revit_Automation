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
        public BeamDeflectionOption(string label, double millimetres) { Label = label; Millimetres = millimetres; }
    }

    public class BeamCalculatorViewModel : INotifyPropertyChanged
    {
        public static IReadOnlyList<BeamDeflectionOption> AvailableDeflectionOptions { get; } = Array.AsReadOnly(new[] {
            new BeamDeflectionOption("Class A / Architectural Concrete (3mm limit)", 3),
            new BeamDeflectionOption("Standard / Concealed Concrete (5mm limit)", 5),
            new BeamDeflectionOption("Rough / Heavy Civil (10mm limit)", 10)
        });
        public IReadOnlyList<BeamDeflectionOption> DeflectionOptions => AvailableDeflectionOptions;
        private double depth = 500, width = 200, leftThickness = 200, rightThickness = 200;
        private double floorHeight = 3000;
        private BeamPropOption selectedProp = BeamPropOption.All.First(p => p.CapacityKey == "300 at bottom");
        public IReadOnlyList<BeamPropOption> PropOptions => BeamPropOption.All;
        public BeamPropOption SelectedProp { get => selectedProp; set { selectedProp = value; Changed(); } }
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
        public BeamDeflectionOption DeflectionLimit { get => deflection; set { deflection = value; Changed(); } }
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
        public double PrimaryLineSpacingMm => (Width + 1400) / 2;
        private double slabStripWidthMm = 300;
        public double SlabStripWidthMm { get => slabStripWidthMm; set { slabStripWidthMm = value; Changed(); } }
        public bool HasAnySlab => HasLeftSlab || HasRightSlab;

        public void SetTextInputErrors(bool value)
        {
            if (textErrors == value) return;
            textErrors = value; Calculate();
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
            IsCalculationValid = false;
            MaximumPropSpacing = MaximumPropLoad = PrimaryBeamCantilever = 0;
            LeftLineLoad = RightLineLoad = TotalLineLoad = 0;
            PropCapacityKn = PropLookupExtensionMm = PropUtilizationPercent = 0;
            LimitingFactor = string.Empty; WarningMessage = string.Empty;
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
                double deflect = 1000 * Math.Pow((DeflectionLimit.Millimetres / 1000 * 10760000 * 0.00004181) / (d * w), 0.25);
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
                    if (prop <= capacity) break;
                    governing = "Prop Capacity";
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
            foreach (string name in new[] { nameof(MaximumPropSpacing), nameof(MaximumPropLoad), nameof(PrimaryBeamCantilever), nameof(LimitingFactor), nameof(WarningMessage), nameof(LeftLineLoad), nameof(RightLineLoad), nameof(TotalLineLoad), nameof(PrimaryLineSpacingMm), nameof(PropExtensionMm), nameof(PropCapacityKn), nameof(PropLookupExtensionMm), nameof(PropUtilizationPercent), nameof(IsCalculationValid) }) OnPropertyChanged(name);
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
            return false;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
