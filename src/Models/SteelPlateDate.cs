using System.Collections.Generic;

namespace EngineeringDashboard
{
    public static class SteelPlateData
    {
        // Standard available steel plate thicknesses in mm
        public static readonly List<double> StandardThicknesses = new List<double>
        {
            4.0, 5.0, 6.0, 8.0, 10.0, 12.0, 15.0, 16.0, 20.0, 25.0, 30.0, 40.0, 50.0
        };
    }
}