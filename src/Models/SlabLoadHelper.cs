using System;

namespace EngineeringDashboard
{
    public static class FormworkLoadCalculator
    {
        /// <summary>
        /// Calculates the total design load for concrete formwork based on slab thickness.
        /// Uses EN 12812 / BS 5975 standard load assumptions.
        /// </summary>
        /// <param name="thicknessMm">Slab thickness in millimeters (mm).</param>
        /// <returns>Total formwork design load in kN/m².</returns>
        public static double CalculateTotalLoad(int thicknessMm)
        {
            // Prevent negative or zero thickness inputs
            if (thicknessMm <= 0)
            {
                throw new ArgumentException("Slab thickness must be greater than zero.", nameof(thicknessMm));
            }

            // Convert thickness from mm to meters for the formula
            double h = thicknessMm / 1000.0;

            // 1. Concrete Dead Load (25 kN/m³ volumetric weight)
            double concreteLoad = h * 25.0;

            // 2. Placing/Heaping Live Load (10% of concrete weight)
            // Clamped between 0.75 and 1.5 kN/m²
            // Using Math.Max and Math.Min ensures full compatibility across all .NET versions
            double nominalPlacingLoad = 0.10 * concreteLoad;
            double placingLoad = Math.Max(0.75, Math.Min(1.5, nominalPlacingLoad));

            // 3. Construction Working Area Live Load (constant 0.75 kN/m²)
            double workingLoad = 0.75;

            // 4. Formwork Dead Load (constant 0.4 kN/m²)
            double formworkLoad = 0.4;

            // 5. Calculate Total Design Load
            double totalLoad = concreteLoad + placingLoad + workingLoad + formworkLoad;

            // Return rounded to 2 decimal places to match your original output style
            return Math.Round(totalLoad, 2);
        }
    }
}