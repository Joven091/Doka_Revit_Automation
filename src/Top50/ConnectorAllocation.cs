using System;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    public sealed class ConnectorAllocation
    {
        public bool Determined {get;private set;}
        public decimal StandardStock {get;private set;}
        public decimal AdjustableStock {get;private set;}
        public decimal[] DirectUnits {get;private set;}=Array.Empty<decimal>();
        public decimal[] FillerUnits {get;private set;}=Array.Empty<decimal>();
        public string Description=>Determined?"Reviewed connector units: "+StandardStock+" standard / "+AdjustableStock+" adjustable reusable peak stock.":"Connector units: Not determined — reviewed connection levels and face multiplier required. Procurement-ready connector numbers are disabled.";
        public static ConnectorAllocation Calculate(Proposal proposal,NativeTop50Project settings)
        {
            var result=new ConnectorAllocation();
            if(proposal==null||!settings.ConnectionReviewed||!settings.ConnectionLevels.HasValue||!settings.ConnectionFaces.HasValue||settings.ConnectionLevels<=0||settings.ConnectionFaces<=0)return result;
            decimal factor=(decimal)settings.ConnectionLevels.Value*settings.ConnectionFaces.Value;
            result.DirectUnits=proposal.Groups.Select(g=>g.Walls.Sum(a=>a.Pattern.DirectLocations)*factor).ToArray();result.FillerUnits=proposal.Groups.Select(g=>g.Walls.Sum(a=>a.Pattern.FillerLocations)*factor).ToArray();
            result.AdjustableStock=result.FillerUnits.DefaultIfEmpty(0).Max();
            result.StandardStock=settings.AdjustableOnDirect?Math.Max(0,result.DirectUnits.Select((d,i)=>d+result.FillerUnits[i]).DefaultIfEmpty(0).Max()-result.AdjustableStock):result.DirectUnits.DefaultIfEmpty(0).Max();
            result.Determined=true;return result;
        }
    }
}
