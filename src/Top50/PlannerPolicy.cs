using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    /// <summary>Fixed user-approved defaults; persisted legacy tuning never overrides them.</summary>
    public static class Top50PlannerPolicy
    {
        public const int WallFaces=2;
        public const int DisplayLimit=3;
        public const double ThoroughSearchEffort=100;
        public const int SearchPolicyVersion=3;
        public const int ProjectVersion=4;
        public static void Apply(NativeTop50Project settings)
        {
            settings.Seed=50;settings.SearchEffort=ThoroughSearchEffort;settings.CandidateDisplayLimit=DisplayLimit;
            settings.ConnectionLevels=1;settings.ConnectionFaces=WallFaces;settings.ConnectionReviewed=false;settings.AdjustableOnDirect=true;
        }
        // The allocation model describes a matching arrangement on each face.
        // Physical equipment quantities cover both faces simultaneously.
        public static int TotalPanels(Proposal proposal)=>checked(proposal.PanelCount*WallFaces);
        public static decimal OfferedLmPerFace(Proposal proposal)=>proposal.StockMicrometres/1000000m;
        public static IReadOnlyList<PanelInventoryRow> Inventory(Proposal proposal)=>proposal.Stock.OrderBy(p=>p.Key).Select(p=>new PanelInventoryRow(p.Key,checked(p.Value*WallFaces),proposal.IdenticalKitCount)).ToArray();
    }
    public sealed class PanelInventoryRow
    {
        public int LengthMm{get;}public int Quantity{get;}public int QuantityPerSet{get;}
        public string Length=>(LengthMm/1000m).ToString("0.00",CultureInfo.InvariantCulture);
        public PanelInventoryRow(int lengthMm,int quantity,int sets){LengthMm=lengthMm;Quantity=quantity;QuantityPerSet=sets>1?quantity/sets:0;}
    }
}
