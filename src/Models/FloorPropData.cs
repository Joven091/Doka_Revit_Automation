using System.Collections.Generic;

namespace EngineeringDashboard
{
    public static class FloorPropData
    {
        // Dictionary mapping Slab Thickness to a list of (PropSpacing, BeamSpacing) pairs
        public static readonly Dictionary<double, List<(double Prop, double Beam)>> TableData = new Dictionary<double, List<(double, double)>>
        {
            { 500, new List<(double, double)> { (690, 2000), (790, 1750), (920, 1500), (1100, 1250), (1380, 1000) } },
            { 450, new List<(double, double)> { (670, 2250), (760, 2000), (870, 1750), (1010, 1500), (1210, 1250), (1520, 1000) } },
            { 400, new List<(double, double)> { (750, 2250), (840, 2000), (970, 1750), (1130, 1500), (1350, 1250), (1690, 1000) } },
            { 350, new List<(double, double)> { (760, 2500), (850, 2250), (950, 2000), (1090, 1750), (1270, 1500), (1520, 1250), (1910, 1000) } },
            { 300, new List<(double, double)> { (870, 2500), (970, 2250), (1090, 2000), (1250, 1750), (1460, 1500), (1750, 1250), (2080, 1000) } },
            { 280, new List<(double, double)> { (920, 2500), (1030, 2250), (1150, 2000), (1320, 1750), (1540, 1500), (1850, 1250), (2130, 1000) } },
            { 260, new List<(double, double)> { (890, 2750), (980, 2500), (1090, 2250), (1220, 2000), (1400, 1750), (1630, 1500), (1960, 1250), (2180, 1000) } },
            { 240, new List<(double, double)> { (950, 2750), (1040, 2500), (1160, 2250), (1300, 2000), (1490, 1750), (1730, 1500), (2040, 1250), (2240, 1000) } },
            { 220, new List<(double, double)> { (1010, 2750), (1110, 2500), (1240, 2250), (1390, 2000), (1590, 1750), (1850, 1500), (2110, 1250), (2300, 1000) } },
            { 200, new List<(double, double)> { (1080, 2750), (1190, 2500), (1330, 2250), (1490, 2000), (1700, 1750), (1990, 1500), (2180, 1250), (2370, 1000) } }
        };

        // H20 Component mapping
        public static readonly Dictionary<string, double> H20Data = new Dictionary<string, double>
        {
            { "2.90m", 1250 },
            { "3.30m", 1450 },
            { "3.60m", 1050 },
            { "3.90m", 1150 },
            { "4.50m", 1350 }
        };
    }
}