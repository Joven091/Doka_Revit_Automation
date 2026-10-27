using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    /// <summary>Presentation names only; source IDs and exact engineering lengths remain unchanged.</summary>
    public sealed class Top50WallLabels
    {
        private readonly Dictionary<string,WallRow> walls;
        private readonly Dictionary<string,int> numbers;
        public Top50WallLabels(IEnumerable<WallRow> source)
        {
            var ordered=source.OrderBy(w=>w.Id,StringComparer.Ordinal).ToArray();
            walls=ordered.ToDictionary(w=>w.Id,StringComparer.Ordinal);
            numbers=ordered.Select((wall,index)=>new{wall.Id,Number=index+1}).ToDictionary(w=>w.Id,w=>w.Number,StringComparer.Ordinal);
        }
        public string this[string sourceId]=>"W-"+numbers[sourceId].ToString("00",CultureInfo.InvariantCulture)+" (Length = "+(walls[sourceId].LengthMicrometres/1000000m).ToString("F2",CultureInfo.InvariantCulture)+" m)";
    }
}
