using System;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    public sealed class ConnectorAllocation
    {
        public bool Determined {get;private set;}
        public bool AssumedSingleLevel=>true;
        public int Levels=>1;
        public int Faces=>Top50PlannerPolicy.WallFaces;
        public decimal StandardStock {get;private set;}
        public decimal AdjustableStock {get;private set;}
        public decimal[] DirectUnits {get;private set;}=Array.Empty<decimal>();
        public decimal[] FillerUnits {get;private set;}=Array.Empty<decimal>();
        public string Description=>Determined?StandardStock+" "+ConnectorEquipment.DirectName+" ("+ConnectorEquipment.DirectArticle+"), "+AdjustableStock+" "+ConnectorEquipment.FillerName+" ("+ConnectorEquipment.FillerArticle+"). Connectors per level — both faces; one connector per joint per face. Stock is reused between casting groups; spare adjustable waling extensions may serve direct joints.":"No proposal selected.";
        public static ConnectorAllocation Calculate(Proposal proposal,NativeTop50Project settings=null)
        {
            var result=new ConnectorAllocation();if(proposal==null)return result;
            result.DirectUnits=proposal.Groups.Select(g=>g.Walls.Sum(a=>a.Pattern.DirectLocations)*(decimal)Top50PlannerPolicy.WallFaces).ToArray();
            result.FillerUnits=proposal.Groups.Select(g=>g.Walls.Sum(a=>a.Pattern.FillerLocations)*(decimal)Top50PlannerPolicy.WallFaces).ToArray();
            result.AdjustableStock=result.FillerUnits.DefaultIfEmpty(0).Max();
            result.StandardStock=Math.Max(0,result.DirectUnits.Select((d,i)=>d+result.FillerUnits[i]).DefaultIfEmpty(0).Max()-result.AdjustableStock);
            result.Determined=true;return result;
        }
    }
}
