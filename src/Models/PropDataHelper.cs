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
            [5.0] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 25.8, ["550 at top"] = 29.4 },
            [4.9] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 27.2, ["550 at top"] = 31.0 },
            [4.8] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 28.7, ["550 at top"] = 32.5 },
            [4.7] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 30.1, ["550 at top"] = 34.2 },
            [4.6] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 31.6, ["550 at top"] = 35.9 },
            [4.5] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 33.2, ["550 at top"] = 36.7 },
            [4.4] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 34.9, ["550 at top"] = 36.7 },
            [4.3] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [4.2] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [4.1] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [4.0] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 21.5, ["400 at top"] = 24.8, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.9] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 23.0, ["400 at top"] = 26.8, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.8] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 24.4, ["400 at top"] = 28.7, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.7] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 26.0, ["400 at top"] = 30.8, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.6] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = 27.7, ["400 at top"] = 33.2, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.5] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 20.8, ["350 at top"] = 24.5, ["400 at bottom"] = 29.4, ["400 at top"] = 35.5, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.4] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 22.3, ["350 at top"] = 26.7, ["400 at bottom"] = 31.0, ["400 at top"] = 36.7, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.3] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 23.8, ["350 at top"] = 28.9, ["400 at bottom"] = 32.4, ["400 at top"] = 36.7, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.2] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 25.4, ["350 at top"] = 31.3, ["400 at bottom"] = 33.5, ["400 at top"] = 36.7, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.1] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = 27.1, ["350 at top"] = 34.0, ["400 at bottom"] = 34.5, ["400 at top"] = 36.7, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [3.0] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 20.7, ["300 at top"] = 24.8, ["350 at bottom"] = 28.8, ["350 at top"] = 36.7, ["400 at bottom"] = 35.5, ["400 at top"] = 36.7, ["550 at bottom"] = 36.7, ["550 at top"] = 36.7 },
            [2.9] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 22.4, ["300 at top"] = 27.4, ["350 at bottom"] = 29.6, ["350 at top"] = 36.7, ["400 at bottom"] = 36.5, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.8] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 24.0, ["300 at top"] = 29.9, ["350 at bottom"] = 30.5, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.7] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 25.3, ["300 at top"] = 32.6, ["350 at bottom"] = 31.5, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.6] = new Dictionary<string, double?> { ["250 at bottom"] = null, ["250 at top"] = null, ["300 at bottom"] = 26.2, ["300 at top"] = 35.3, ["350 at bottom"] = 32.7, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.5] = new Dictionary<string, double?> { ["250 at bottom"] = 20.2, ["250 at top"] = 24.8, ["300 at bottom"] = 27.0, ["300 at top"] = 36.7, ["350 at bottom"] = 33.9, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.4] = new Dictionary<string, double?> { ["250 at bottom"] = 21.3, ["250 at top"] = 27.2, ["300 at bottom"] = 27.8, ["300 at top"] = 36.7, ["350 at bottom"] = 35.0, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.3] = new Dictionary<string, double?> { ["250 at bottom"] = 22.5, ["250 at top"] = 29.5, ["300 at bottom"] = 28.7, ["300 at top"] = 36.7, ["350 at bottom"] = 36.1, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.2] = new Dictionary<string, double?> { ["250 at bottom"] = 23.3, ["250 at top"] = 31.9, ["300 at bottom"] = 29.8, ["300 at top"] = 36.7, ["350 at bottom"] = 36.7, ["350 at top"] = 36.7, ["400 at bottom"] = 36.7, ["400 at top"] = 36.7, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.1] = new Dictionary<string, double?> { ["250 at bottom"] = 24.0, ["250 at top"] = 34.3, ["300 at bottom"] = 31.1, ["300 at top"] = 36.7, ["350 at bottom"] = 36.7, ["350 at top"] = 36.7, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null },
            [2.0] = new Dictionary<string, double?> { ["250 at bottom"] = 24.6, ["250 at top"] = 36.7, ["300 at bottom"] = 32.4, ["300 at top"] = 36.7, ["350 at bottom"] = 36.7, ["350 at top"] = 36.7, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null },
            [1.9] = new Dictionary<string, double?> { ["250 at bottom"] = 25.8, ["250 at top"] = 36.7, ["300 at bottom"] = 34.0, ["300 at top"] = 36.7, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null },
            [1.8] = new Dictionary<string, double?> { ["250 at bottom"] = 26.9, ["250 at top"] = 36.7, ["300 at bottom"] = 35.6, ["300 at top"] = 36.7, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null },
            [1.7] = new Dictionary<string, double?> { ["250 at bottom"] = 28.4, ["250 at top"] = 36.7, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null },
            [1.6] = new Dictionary<string, double?> { ["250 at bottom"] = 30.1, ["250 at top"] = 36.7, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null },
            [1.5] = new Dictionary<string, double?> { ["250 at bottom"] = 31.8, ["250 at top"] = 36.7, ["300 at bottom"] = null, ["300 at top"] = null, ["350 at bottom"] = null, ["350 at top"] = null, ["400 at bottom"] = null, ["400 at top"] = null, ["550 at bottom"] = null, ["550 at top"] = null }
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