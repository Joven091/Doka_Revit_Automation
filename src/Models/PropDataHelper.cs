using System.Collections.Generic;

namespace EngineeringDashboard
{
    public static class PropDataHelper
    {
        /// <summary>
        /// Prop capacities lookup table.
        /// Outer Key: Extension length (m)
        /// Inner Key: Prop type and orientation ("Size at position")
        /// Value: Capacity (kN), or null if not supported.
        /// </summary>
        public static readonly Dictionary<double, Dictionary<string, double?>> PropCapacities = new Dictionary<double, Dictionary<string, double?>>
        {
            [5.5] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 20.6, ["550 at top"] = 22.7 },
            [5.4] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 21.6, ["550 at top"] = 23.9 },
            [5.3] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 22.5, ["550 at top"] = 25.2 },
            [5.2] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 23.6, ["550 at top"] = 26.5 },
            [5.1] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 24.7, ["550 at top"] = 27.9 },
            [5.0] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 26.0, ["550 at top"] = 29.5 },
            [4.9] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 27.2, ["550 at top"] = 31.2 },
            [4.8] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 28.6, ["550 at top"] = 33.1 },
            [4.7] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 30.0, ["550 at top"] = 35.1 },
            [4.6] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 31.5, ["550 at top"] = 37.3 },
            [4.5] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 33.1, ["550 at top"] = 39.7 },
            [4.4] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 34.8, ["550 at top"] = 40.0 },
            [4.3] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 36.6, ["550 at top"] = 40.0 },
            [4.2] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 38.6, ["550 at top"] = 40.0 },
            [4.1] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 25.1, ["400 at top"] = 26.6, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [4.0] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 26.6, ["400 at top"] = 28.4, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.9] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 28.3, ["400 at top"] = 30.4, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.8] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 30.1, ["400 at top"] = 32.6, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.7] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 24.2, ["350 at top"] = 25.2, ["400 at bottom"] = 32.1, ["400 at top"] = 35.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.6] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 25.7, ["350 at top"] = 27.0, ["400 at bottom"] = 34.3, ["400 at top"] = 37.7, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.5] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 27.5, ["350 at top"] = 29.0, ["400 at bottom"] = 36.7, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.4] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 29.4, ["350 at top"] = 31.3, ["400 at bottom"] = 39.4, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.3] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 31.6, ["350 at top"] = 33.9, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.2] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 34.0, ["350 at top"] = 36.8, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.1] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 24.3, ["300 at top"] = 24.8, ["350 at bottom"] = 36.8, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [3.0] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 26.2, ["300 at top"] = 26.8, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.9] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 28.3, ["300 at top"] = 29.2, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.8] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 30.6, ["300 at top"] = 31.9, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.7] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 33.3, ["300 at top"] = 35.1, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.6] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 36.3, ["300 at top"] = 38.8, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.5] = new Dictionary<string, double?> { ["250 at bottom"] = 24.3, ["250 at top"] = 24.9, ["300 at bottom"] = 39.8, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.4] = new Dictionary<string, double?> { ["250 at bottom"] = 26.6, ["250 at top"] = 27.5, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.3] = new Dictionary<string, double?> { ["250 at bottom"] = 29.3, ["250 at top"] = 30.6, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.2] = new Dictionary<string, double?> { ["250 at bottom"] = 32.4, ["250 at top"] = 34.3, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.1] = new Dictionary<string, double?> { ["250 at bottom"] = 36.1, ["250 at top"] = 38.7, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [2.0] = new Dictionary<string, double?> { ["250 at bottom"] = 40.0, ["250 at top"] = 40.0, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [1.9] = new Dictionary<string, double?> { ["250 at bottom"] = 40.0, ["250 at top"] = 40.0, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [1.8] = new Dictionary<string, double?> { ["250 at bottom"] = 40.0, ["250 at top"] = 40.0, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [1.7] = new Dictionary<string, double?> { ["250 at bottom"] = 40.0, ["250 at top"] = 40.0, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [1.6] = new Dictionary<string, double?> { ["250 at bottom"] = 40.0, ["250 at top"] = 40.0, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 },
            [1.5] = new Dictionary<string, double?> { ["250 at bottom"] = 40.0, ["250 at top"] = 40.0, ["300 at bottom"] = 40.0, ["300 at top"] = 40.0, ["350 at bottom"] = 40.0, ["350 at top"] = 40.0, ["400 at bottom"] = 40.0, ["400 at top"] = 40.0, ["550 at bottom"] = 40.0, ["550 at top"] = 40.0 }
        };

        /// <summary>
        /// Safely retrieves the capacity for a given extension and configuration.
        /// Returns null if the configuration is invalid or exceeds safe limits.
        /// </summary>
        public static double? GetCapacity(double extension, string configurationKey)
        {
            if (PropCapacities.TryGetValue(extension, out var configurations))
            {
                if (configurations.TryGetValue(configurationKey, out var capacity))
                {
                    return capacity;
                }
            }
            return null;
        }
    }
}
