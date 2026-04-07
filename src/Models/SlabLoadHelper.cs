using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public static class SlabLoadHelper
    {
        /// <summary>
        /// Lookup table for Hollow Core Slab Loads.
        /// Key: Slab Thickness in millimeters (mm)
        /// Value: Slab Load in kN/m²
        /// </summary>
        public static readonly Dictionary<int, double> SlabLoads = new Dictionary<int, double>
        {
            [100] = 4.25, [120] = 4.74, [140] = 5.23, [160] = 5.72,
            [180] = 6.21, [200] = 6.71, [220] = 7.20, [240] = 7.69,
            [260] = 8.18, [280] = 8.67, [300] = 9.16, [350] = 10.49,
            [400] = 11.84, [450] = 13.19, [500] = 14.54
        };

        /// <summary>
        /// Retrieves the slab load. If exact thickness is missing,
        /// it performs linear interpolation between the nearest values.
        /// </summary>
        public static double? GetSlabLoad(int thicknessMm)
        {
            // 1. Check for exact match
            if (SlabLoads.TryGetValue(thicknessMm, out double exactLoad))
            {
                return exactLoad;
            }

            // 2. Check for out of bounds
            int minThick = SlabLoads.Keys.Min();
            int maxThick = SlabLoads.Keys.Max();

            if (thicknessMm < minThick || thicknessMm > maxThick)
            {
                return null;
            }

            // 3. Perform Linear Interpolation
            int lowerBound = minThick;
            int upperBound = maxThick;

            foreach (var key in SlabLoads.Keys.OrderBy(k => k))
            {
                if (key < thicknessMm) lowerBound = key;
                if (key > thicknessMm) { upperBound = key; break; }
            }

            double lowerLoad = SlabLoads[lowerBound];
            double upperLoad = SlabLoads[upperBound];

            double interpolatedLoad = lowerLoad + (upperLoad - lowerLoad) * (thicknessMm - lowerBound) / (upperBound - lowerBound);

            return Math.Round(interpolatedLoad, 2);
        }
    }
}
