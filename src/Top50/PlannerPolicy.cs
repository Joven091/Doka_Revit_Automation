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
        public const int SearchPolicyVersion=4;
        public const int ProjectVersion=5;
        public static void Apply(NativeTop50Project settings)
        {
            settings.Seed=50;settings.SearchEffort=ThoroughSearchEffort;settings.CandidateDisplayLimit=DisplayLimit;
            settings.ConnectionLevels=1;settings.ConnectionFaces=WallFaces;settings.ConnectionReviewed=false;settings.AdjustableOnDirect=true;
        }
        public static bool EffectiveIdenticalKits(NativeTop50Project settings)=>settings.OfferingMode==0&&settings.RequireIdenticalKits;
        public static int WholeNumber(decimal value,string field,int minimum,int maximum=int.MaxValue)
        {
            if(value!=decimal.Truncate(value)||value<minimum||value>maximum)
                throw new ArgumentException(field+" must be a whole number between "+minimum+" and "+maximum+".");
            return (int)value;
        }
        public static void ValidateSettings(NativeTop50Project settings)
        {
            if(settings==null)throw new ArgumentNullException(nameof(settings));
            if(settings.OfferingMode!=0&&settings.OfferingMode!=1)throw new ArgumentException("Select Fixed longest-wall sets or Percentage reference.");
            if(!Enum.IsDefined(typeof(PlanningPreset),settings.Preset))throw new ArgumentException("Select a supported planning preset.");
            if(settings.CatalogMm==null||settings.CatalogMm.Length==0||settings.CatalogMm.Any(n=>n<=0))throw new ArgumentException("Select at least one positive nominal panel length.");
            if(settings.OfferingMode==0)WholeNumber(settings.LongestCount,"Longest walls required simultaneously",1);
            WholeNumber(settings.MaxGroups,"Maximum allocation groups",1);
            WholeNumber(settings.MaxFillers,"Maximum internal fillers per wall",0);
            WholeNumber(settings.MaxFillerMm,"Maximum filler width (mm)",0,350);
            if(settings.OfferingMode==1)
            {
                if(settings.Percentage<=0||settings.Percentage>100)throw new ArgumentException("Target stock percentage must be greater than 0 and at most 100.");
                if(settings.BandPoints<0)throw new ArgumentException("Search range above / below target must be zero or greater.");
            }
        }
        public static IReadOnlyList<string> ValidateProposal(IReadOnlyList<WallRow> walls,Proposal proposal,NativeTop50Project settings)
        {
            var errors=new List<string>();
            if(proposal.Stock.Keys.Any(size=>!settings.CatalogMm.Contains(size)))errors.Add("Cached stock uses a panel length outside the permitted catalog.");
            if(settings.OfferingMode==1&&proposal.IdenticalKitCount!=0)errors.Add("Percentage offers cannot claim identical fixed kits.");
            errors.AddRange(Top50Validation.ValidateIdenticalKits(walls,proposal,settings.OfferingMode==1?1:settings.LongestCount,EffectiveIdenticalKits(settings)?(int?)settings.LongestCount:null));
            if(settings.OfferingMode==1&&!settings.AllowExpansion&&!InPercentageBand(walls,proposal,settings))errors.Add("Cached offer is outside the required percentage range.");
            return errors.AsReadOnly();
        }
        public static IReadOnlyList<Proposal> PreferOfferingBand(IReadOnlyList<WallRow> walls,IEnumerable<Proposal> proposals,NativeTop50Project settings)
        {
            var all=proposals.ToArray();if(settings.OfferingMode!=1)return all;
            var inside=all.Where(p=>InPercentageBand(walls,p,settings)).ToArray();
            return inside.Length>0||!settings.AllowExpansion?inside:all;
        }
        private static bool InPercentageBand(IReadOnlyList<WallRow> walls,Proposal proposal,NativeTop50Project settings)
        {
            decimal total=walls.Where(w=>w.Included).Sum(w=>(decimal)w.LengthMicrometres);
            return total>0&&Math.Abs(proposal.StockMicrometres/total*100-settings.Percentage)<=settings.BandPoints;
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
