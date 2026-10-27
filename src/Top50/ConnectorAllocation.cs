using System;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    public sealed class ConnectorAllocation
    {
        public bool Determined {get;private set;}
        public bool AssumedSingleLevel {get;private set;}
        public int Levels {get;private set;}
        public int Faces {get;private set;}
        public decimal StandardStock {get;private set;}
        public decimal AdjustableStock {get;private set;}
        public decimal[] DirectUnits {get;private set;}=Array.Empty<decimal>();
        public decimal[] FillerUnits {get;private set;}=Array.Empty<decimal>();
        public string Description=>Determined?StandardStock+" "+ConnectorEquipment.DirectName+" ("+ConnectorEquipment.DirectArticle+"), "+AdjustableStock+" "+ConnectorEquipment.FillerName+" ("+ConnectorEquipment.FillerArticle+"), reused between casting groups. "+(AssumedSingleLevel?"Basis: one connector per joint, per wall face.":"Basis: "+Levels+" connectors per joint × "+Faces+" wall faces."):"No proposal selected.";
        public static ConnectorAllocation Calculate(Proposal proposal,NativeTop50Project settings)
        {
            var result=new ConnectorAllocation();
            if(proposal==null)return result;
            bool reviewed=settings.ConnectionReviewed&&settings.ConnectionLevels.HasValue&&settings.ConnectionFaces.HasValue&&settings.ConnectionLevels>0&&settings.ConnectionFaces>0;
            result.AssumedSingleLevel=!reviewed;result.Levels=reviewed?settings.ConnectionLevels.Value:1;result.Faces=reviewed?settings.ConnectionFaces.Value:1;
            decimal factor=(decimal)result.Levels*result.Faces;
            result.DirectUnits=proposal.Groups.Select(g=>g.Walls.Sum(a=>a.Pattern.DirectLocations)*factor).ToArray();result.FillerUnits=proposal.Groups.Select(g=>g.Walls.Sum(a=>a.Pattern.FillerLocations)*factor).ToArray();
            result.AdjustableStock=result.FillerUnits.DefaultIfEmpty(0).Max();
            result.StandardStock=settings.AdjustableOnDirect?Math.Max(0,result.DirectUnits.Select((d,i)=>d+result.FillerUnits[i]).DefaultIfEmpty(0).Max()-result.AdjustableStock):result.DirectUnits.DefaultIfEmpty(0).Max();
            result.Determined=true;return result;
        }
    }
}
