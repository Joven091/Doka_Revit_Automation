using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard.Backpropping
{
    public sealed class BackproppingProp
    {
        public string Name { get; }
        public int MinimumMm { get; }
        public int MaximumMm { get; }
        private readonly double standardCapacity;
        private readonly Dictionary<int, double> endRows;
        public BackproppingProp(string name, int minimum, int maximum, double capacity, params double[] fromMaximum)
        {
            Name = name; MinimumMm = minimum; MaximumMm = maximum; standardCapacity = capacity;
            endRows = new Dictionary<int, double>();
            for (int i = 0; i < fromMaximum.Length; i++) endRows[maximum - i * 100] = fromMaximum[i];
        }
        public double CapacityAt(int row) => endRows.TryGetValue(row, out double value) ? value : standardCapacity;
        public override string ToString() => Name;
    }

    public static class BackproppingPropCatalog
    {
        // Dedicated restrained-reshoring data from the user's supplied tables.
        // Do not substitute the existing free-standing formwork capacities.
        // DokaFit image is cropped at 1.9 m; shorter extensions are intentionally unavailable.
        public static IReadOnlyList<BackproppingProp> All { get; } = Create().AsReadOnly();
        private static List<BackproppingProp> Create()
        {
            var p = new List<BackproppingProp> {
                new BackproppingProp("DokaFit Prop 300",1900,3000,35),
                new BackproppingProp("DokaFit Prop 350",2200,3500,35),
                new BackproppingProp("DokaFit Prop 400",2600,4000,35,30,31.5,34)
            };
            foreach (string family in new[] { "Eurex 20", "Eurex 20 top" })
            {
                p.Add(new BackproppingProp(family+" 150",1000,1500,25));
                p.Add(new BackproppingProp(family+" 250",1500,2500,36.7,30,32,34));
                p.Add(new BackproppingProp(family+" 300",1800,3000,36.7,30,32,34));
                p.Add(new BackproppingProp(family+" 350",2000,3500,36.7,30,32,34));
                bool top = family.EndsWith("top", StringComparison.Ordinal);
                p.Add(top ? new BackproppingProp(family+" 400",2300,4000,36.7,30,32,34)
                          : new BackproppingProp(family+" 400",2300,4000,40,30,32,34,36,38));
                p.Add(top ? new BackproppingProp(family+" 550",3000,5500,36.7,30,32,34)
                          : new BackproppingProp(family+" 550",3000,5500,40,30,32,34,36,38));
            }
            // Table has one 700 column; avoid inventing a separate legacy variant.
            p.Add(new BackproppingProp("Eurex 20 top 700",3900,6600,36.7));
            foreach (string family in new[] { "Eurex 30", "Eurex 30 top" })
            {
                p.Add(new BackproppingProp(family+" 250",1500,2500,41.2));
                p.Add(new BackproppingProp(family+" 300",1800,3000,41.2));
                p.Add(new BackproppingProp(family+" 350",2000,3500,41.2));
                p.Add(new BackproppingProp(family+" 400",2300,4000,41.2));
                p.Add(new BackproppingProp(family+" 450",2500,4500,41.2));
                p.Add(new BackproppingProp(family+" 550",3100,5500,41.2));
            }
            return p;
        }
        public static double Capacity(string name, double extensionMm, out double lookupMm)
        {
            BackproppingCalculator.RequireNumber(extensionMm, "Prop extension", true);
            var prop = All.FirstOrDefault(p => p.Name == name);
            if (prop == null) throw new ArgumentException("Select a prop type.");
            if (extensionMm < prop.MinimumMm || extensionMm > prop.MaximumMm)
                throw new ArgumentException($"{prop.Name}: extension must be {prop.MinimumMm}–{prop.MaximumMm} mm in the supplied table.");
            lookupMm = Math.Ceiling(extensionMm / 100) * 100;
            return prop.CapacityAt((int)lookupMm);
        }
    }
}
