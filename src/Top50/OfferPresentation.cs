using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    /// <summary>Names and explanations for existing ranks; never changes proposal order or allocations.</summary>
    public sealed class Top50OfferPresentation
    {
        private readonly IReadOnlyList<Proposal> proposals;
        private readonly NativeTop50Project settings;
        private readonly Dictionary<string,RankingMetrics> metrics;
        private readonly RankingScales scales;
        private readonly bool stale;
        public Top50OfferPresentation(ImportSnapshot input,NativeTop50Project settings,IReadOnlyList<Proposal> proposals,bool stale=false)
        {
            this.proposals=proposals;this.settings=settings;this.stale=stale;
            if(stale){metrics=new Dictionary<string,RankingMetrics>();return;}
            var included=input.Walls.Where(w=>w.Included).ToArray();decimal length=included.Sum(w=>w.LengthMm)/1000;
            decimal? target=settings.OfferingMode==1?(decimal?)(length*settings.Percentage/100):null;
            scales=new RankingScales(length,settings.CatalogMm.Distinct().Count(),settings.MaxGroups,included.Length*settings.MaxFillers,target);
            metrics=proposals.ToDictionary(p=>p.Id,p=>
            {
                var value=RankingMetrics.From(p,target.HasValue?p.StockMicrometres/1000000m-target.Value:0);
                return ProposalRanking.Rank(new[]{value},(PlanningPreset)settings.Preset,1,scales)[0];
            },StringComparer.Ordinal);
        }
        public int RankNumber(Proposal p)=>p.Rank>0?p.Rank:proposals.TakeWhile(other=>other.Id!=p.Id).Count()+1;
        public string Rank(Proposal p)=>Ordinal(RankNumber(p));
        public string Reason(Proposal proposal)
        {
            if(stale)return "The inputs have changed. Optimize again to update this result.";
            if(proposals.Count==1)return "Only one valid option was found.";
            var first=proposals.OrderBy(RankNumber).First();bool leader=proposal.Id==first.Id;
            var other=leader?proposals.OrderBy(RankNumber).Skip(1).First():first;
            var current=metrics[proposal.Id];var reference=metrics[other.Id];string rank=Rank(other)+" option";
            if(settings.Preset==(int)PlanningPreset.EconomicalOffer)
            {
                if(current.StockMetres!=reference.StockMetres)return LengthComparison(current.StockMetres,reference.StockMetres,rank);
                if(current.Types!=reference.Types)return CountComparison("panel length",current.Types,reference.Types,rank);
                if(current.Groups!=reference.Groups)return CountComparison("casting group",current.Groups,reference.Groups,rank);
            }
            if(settings.Preset==(int)PlanningPreset.FastestConstruction&&current.Groups!=reference.Groups)return CountComparison("casting group",current.Groups,reference.Groups,rank);
            if(current.BalancedScore==reference.BalancedScore)return "This option has the same overall score as the "+rank+".";
            int driver=Enumerable.Range(1,7).OrderByDescending(i=>(leader?reference.Costs[i]-current.Costs[i]:current.Costs[i]-reference.Costs[i])/scales.Values[i]).First();
            switch(driver)
            {
                case 1:return LengthComparison(current.StockMetres,reference.StockMetres,rank);
                case 2:return CountComparison("panel length",current.Types,reference.Types,rank);
                case 3:return CountComparison("casting group",current.Groups,reference.Groups,rank);
                case 4:return "This option leaves an average of "+Number(current.StockMetres*current.MeanIdleFraction)+" LM unused per casting group, compared with "+Number(reference.StockMetres*reference.MeanIdleFraction)+" LM in the "+rank+" (per face).";
                case 5:return "This option uses "+(current.FillerLocations*Top50PlannerPolicy.WallFaces)+" filler joints across both faces, compared with "+(reference.FillerLocations*Top50PlannerPolicy.WallFaces)+" in the "+rank+".";
                case 6:
                    decimal unused=current.StockMetres*current.WorstIdleFraction,otherUnused=reference.StockMetres*reference.WorstIdleFraction;
                    return (unused<otherUnused?"Less":"More")+" equipment is left unused in the least busy casting group: "+Number(unused)+" LM, compared with "+Number(otherUnused)+" LM in the "+rank+" (per face).";
                default:return "The offered length differs from the target by "+Number(current.AbsoluteTargetDeviation)+" LM per face, compared with "+Number(reference.AbsoluteTargetDeviation)+" LM in the "+rank+".";
            }
        }
        private static string Number(decimal value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
        private static string LengthComparison(decimal value,decimal other,string rank)=>"This option needs "+Number(value)+" LM per face, compared with "+Number(other)+" LM in the "+rank+".";
        private static string CountComparison(string item,int value,int other,string rank)=>"This option "+(item=="casting group"?"needs ":"uses ")+value+" "+item+(value==1?"":"s")+", compared with "+other+" in the "+rank+".";
        public static string CastingGroup(int index)=>"C-"+(index+1).ToString("00",CultureInfo.InvariantCulture);
        public static string Ordinal(int number)
        {
            int lastTwo=number%100;string suffix=lastTwo>=11&&lastTwo<=13?"th":number%10==1?"st":number%10==2?"nd":number%10==3?"rd":"th";
            return number.ToString(CultureInfo.InvariantCulture)+suffix;
        }
    }
}
