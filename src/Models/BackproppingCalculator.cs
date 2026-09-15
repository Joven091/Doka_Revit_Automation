using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard.Backpropping
{
    public enum TemperatureCurve { A, B, C }

    public sealed class SlabInput
    {
        public string Name { get; set; }
        public double ThicknessMm { get; set; }
        public double FloorHeightBelowMm { get; set; }
        public double AgeDays { get; set; }
        public double Sidl { get; set; }
        public double FinalLiveLoad { get; set; }
        // Configuration belongs to the storey BELOW this slab; index 0 is never sized.
        public string PropId { get; set; }
        public double PropExtensionMm => FloorHeightBelowMm - ThicknessMm;
        public double? MaximumSpacingMm { get; set; }
        public SlabInput Copy() => (SlabInput)MemberwiseClone();
    }

    public sealed class SlabResult
    {
        public int Index { get; set; }
        public double SelfWeight { get; set; }
        public double StrengthPercent { get; set; }
        public double Incoming { get; set; }
        public double ConstructionLive { get; set; }
        public double Demand { get; set; }
        public double Capacity { get; set; }
        public double Transfer { get; set; }
        public double DownwardLoad { get; set; }
    }

    public sealed class LoadResult
    {
        public List<SlabResult> Slabs { get; } = new List<SlabResult>();
        public bool Complete { get; set; }
        public double Heaping { get; set; }
        public double PourLoad { get; set; }
    }

    public sealed class SpacingResult
    {
        public double CapacityKn { get; set; }
        public double LookupExtensionMm { get; set; }
        public double CapacitySpacingMm { get; set; }
        public double SpacingMm { get; set; }
        public double PropLoadKn { get; set; }
        public double UtilizationPercent { get; set; }
        public string GoverningLimit { get; set; }
        public bool HasProjectLimit { get; set; }
    }

    /// <summary>
    /// Adopted uniform-area, capacity-based calculation. It is not a stiffness-based
    /// prediction of prop reactions or an independent structural verification of slabs.
    /// All area loads are kN/m²; dimensions are mm except concrete age (days).
    /// </summary>
    public static class BackproppingCalculator
    {
        public const double ConcreteUnitWeight = 25;
        public const double RepropWeight = 0.40;
        private static readonly double[,] A = { {0,0}, {2,50}, {4,70}, {12,80}, {20,90}, {28,100} };
        private static readonly double[,] B = { {0,0}, {4,50}, {8,70}, {28,100} };
        private static readonly double[,] C = { {0,0}, {6,50}, {12,70}, {28,100} };

        public static void RequireNumber(double value, string name, bool positive = false)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || (positive && value == 0))
                throw new ArgumentException(name + (positive ? " must be greater than zero." : " must be zero or greater."));
        }

        public static double Strength(double days, TemperatureCurve curve)
        {
            RequireNumber(days, "Concrete age");
            if (!Enum.IsDefined(typeof(TemperatureCurve), curve)) throw new ArgumentException("Select temperature A, B or C.");
            if (days >= 28) return 100;
            var points = curve == TemperatureCurve.A ? A : curve == TemperatureCurve.B ? B : C;
            for (int i = 1; i < points.GetLength(0); i++)
                if (days <= points[i, 0])
                    return points[i-1,1] + (points[i,1]-points[i-1,1]) * (days-points[i-1,0]) / (points[i,0]-points[i-1,0]);
            throw new InvalidOperationException("Strength curve has no matching segment.");
        }

        public static double HeapingLoad(double slabSelfWeight)
        {
            RequireNumber(slabSelfWeight, "Fresh concrete self-weight");
            return Math.Min(1.75, Math.Max(0.75, slabSelfWeight * 0.10));
        }

        public static LoadResult CalculateLoads(IReadOnlyList<SlabInput> slabs, TemperatureCurve curve)
        {
            if (slabs == null) throw new ArgumentNullException(nameof(slabs));
            var result = new LoadResult();
            for (int i = 0; i < slabs.Count; i++)
            {
                var s = slabs[i];
                if (s == null || string.IsNullOrWhiteSpace(s.Name)) throw new ArgumentException("Every slab needs a name.");
                RequireNumber(s.ThicknessMm, s.Name + " thickness", true);
                RequireNumber(s.FloorHeightBelowMm, s.Name + " floor-to-floor height", true);
                RequireNumber(s.PropExtensionMm, s.Name + " floor height minus slab thickness", true);
                RequireNumber(s.AgeDays, s.Name + " age");
                RequireNumber(s.Sidl, s.Name + " SIDL");
                RequireNumber(s.FinalLiveLoad, s.Name + " final live load");
                if (i == 0 && s.AgeDays != 0) throw new ArgumentException("The slab being cast has age 0 days.");
                if (i > 0 && s.AgeDays < slabs[i-1].AgeDays) throw new ArgumentException(s.Name + " cannot be younger than the slab above.");
                Strength(s.AgeDays, curve); // Validate the selected curve even for the active pour.
            }
            if (slabs.Count == 0) return result;
            double self = slabs[0].ThicknessMm / 1000 * ConcreteUnitWeight;
            result.Heaping = HeapingLoad(self);
            result.PourLoad = self + 0.50 + 0.75 + result.Heaping;
            result.Slabs.Add(new SlabResult { Index = 0, SelfWeight = self, Demand = result.PourLoad,
                Transfer = result.PourLoad, DownwardLoad = result.PourLoad });
            double incoming = result.PourLoad;
            for (int i = 1; i < slabs.Count; i++)
            {
                var s = slabs[i];
                var row = new SlabResult { Index = i, SelfWeight = s.ThicknessMm / 1000 * ConcreteUnitWeight,
                    StrengthPercent = Strength(s.AgeDays, curve), Incoming = incoming, ConstructionLive = i == 1 ? 0.75 : 0 };
                row.Demand = row.SelfWeight + row.Incoming + row.ConstructionLive;
                row.Capacity = row.StrengthPercent / 100 * (row.SelfWeight + s.Sidl + s.FinalLiveLoad);
                RequireNumber(row.Demand, "Calculated demand");
                RequireNumber(row.Capacity, "Calculated capacity");
                row.Transfer = Math.Max(0, row.Demand - row.Capacity);
                // Use full precision for the stopping decision, not rounded display values.
                if (row.Transfer <= 1e-9) row.Transfer = 0;
                row.DownwardLoad = row.Transfer == 0 ? 0 : row.Transfer + RepropWeight;
                result.Slabs.Add(row);
                if (row.Transfer == 0) { result.Complete = true; break; }
                incoming = row.DownwardLoad;
            }
            return result;
        }

        public static SpacingResult CalculateSpacing(double downwardLoad, string propId, double extensionMm, double? maximumMm)
        {
            RequireNumber(downwardLoad, "Downward load", true);
            if (maximumMm.HasValue) { RequireNumber(maximumMm.Value, "Maximum spacing", true);
                if (maximumMm.Value < 25) throw new ArgumentException("Maximum spacing must be at least 25 mm."); }
            double lookup;
            double capacity = BackproppingPropCatalog.Capacity(propId, extensionMm, out lookup);
            double raw = 1000 * Math.Sqrt(capacity / downwardLoad);
            RequireNumber(raw, "Calculated spacing", true);
            double limited = maximumMm.HasValue ? Math.Min(raw, maximumMm.Value) : raw;
            double spacing = Math.Floor(limited / 25) * 25;
            if (spacing < 25) throw new ArgumentException("No spacing of at least 25 mm satisfies this load.");
            return new SpacingResult { CapacityKn = capacity, LookupExtensionMm = lookup, CapacitySpacingMm = raw,
                SpacingMm = spacing, PropLoadKn = downwardLoad * Math.Pow(spacing / 1000, 2),
                UtilizationPercent = 100 * downwardLoad * Math.Pow(spacing / 1000, 2) / capacity,
                HasProjectLimit = maximumMm.HasValue,
                GoverningLimit = maximumMm.HasValue && maximumMm.Value <= raw ? "Maximum spacing" : "Prop capacity" };
        }
    }
}
