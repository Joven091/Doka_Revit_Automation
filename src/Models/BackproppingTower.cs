using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard.Backpropping
{
    public sealed class BackproppingTower
    {
        public const string Name = "D3 shoring tower";
        public const double FrameLength = 1524, InterFrame = 1500;
        public int Small, Medium, Large;
        public double Opening, Capacity, HeightLimit;
        public bool Braced => Opening > 300;
        public string Stack => string.Join(" + ", new[] { Tuple.Create(Large,1.80), Tuple.Create(Medium,1.20), Tuple.Create(Small,0.90) }
            .Where(p => p.Item1 > 0).Select(p => $"{p.Item1} × {p.Item2:0.00} m"));
        public static BackproppingTower Fit(double height)
        {
            BackproppingCalculator.RequireNumber(height,"Tower height",true);
            if (height > 20000) throw new ArgumentException("D3 tower height exceeds the 20 m table limit.");
            double available = height; // The two jack openings include the plates.
            var candidates = new List<BackproppingTower>();
            for (int large=0; large*1800 <= available-250; large++)
            for (int medium=0; large*1800+medium*1200 <= available-250; medium++)
            for (int small=0; (large*1800+medium*1200+small*900) <= available-250; small++)
            {
                if (small+medium+large == 0) continue;
                double opening=(available-large*1800-medium*1200-small*900)/2;
                if (opening>500) continue;
                // Braced jacks above 300 mm use the agreed second braced block.
                bool first = opening<=150, tallFrame = large>0;
                double shortHeight = tallFrame ? (first?2100:2400) : (first?1500:1800);
                bool shortTower = height<=shortHeight;
                double capacity = tallFrame ? (first ? (shortTower?39.3:37.5) : (shortTower?35.2:35.0))
                    : (first ? (shortTower?44.3:44.7) : (shortTower?40.7:41.4));
                candidates.Add(new BackproppingTower { Small=small, Medium=medium, Large=large, Opening=opening, Capacity=capacity, HeightLimit=shortTower?shortHeight:20000 });
            }
            return candidates.OrderBy(t=>t.Small+t.Medium+t.Large).ThenByDescending(t=>t.Capacity).ThenBy(t=>t.Opening).FirstOrDefault()
                ?? throw new ArgumentException("No D3 frame stack fits this storey with equal jack openings of 125–500 mm.");
        }
    }
}
