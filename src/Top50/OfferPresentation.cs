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
            scales=new RankingScales(length,settings.CatalogMm.Distinct().Count(),settings.MaxGroups,included.Length*Math.Min(settings.MaxFillers,settings.MaxComponents-1),target);
            metrics=proposals.ToDictionary(p=>p.Id,p=>
            {
                var value=RankingMetrics.From(p,target.HasValue?p.StockMicrometres/1000000m-target.Value:0);
                return ProposalRanking.Rank(new[]{value},(PlanningPreset)settings.Preset,1,scales)[0];
            },StringComparer.Ordinal);
        }
        public int RankNumber(Proposal p)=>p.Rank>0?p.Rank:proposals.TakeWhile(other=>other.Id!=p.Id).Count()+1;
        public string Rank(Proposal p)=>Ordinal(RankNumber(p));
        public string Reason(Proposal p)
        {
            if(stale)return "Inputs changed; optimize to refresh this rank";
            var first=proposals.OrderBy(RankNumber).First();
            if(p.Id==first.Id)
            {
                if(proposals.Count==1)return "Only lowest-panel-count option found";
                var next=proposals.OrderBy(RankNumber).Skip(1).First();var leader=metrics[p.Id];var runnerUp=metrics[next.Id];string reference=Rank(next);
                if(settings.Preset==(int)PlanningPreset.EconomicalOffer)
                {
                    if(leader.StockMetres<runnerUp.StockMetres)return Compare("Less stock",leader.StockMetres,runnerUp.StockMetres," m",reference);
                    if(leader.Types<runnerUp.Types)return Compare("Fewer panel sizes",leader.Types,runnerUp.Types,"",reference);
                    if(leader.Groups<runnerUp.Groups)return Compare("Fewer casting groups",leader.Groups,runnerUp.Groups,"",reference);
                    return "Same stock, panel sizes and castings; fixed tie order";
                }
                if(settings.Preset==(int)PlanningPreset.FastestConstruction&&leader.Groups<runnerUp.Groups)return Compare("Fewer casting groups",leader.Groups,runnerUp.Groups,"",reference);
                return "Best balance · "+CompareMeasures(leader,runnerUp,reference,true);
            }
            if(!p.Pareto&&first.Pareto)
            {
                var dominated=metrics[p.Id];var better=proposals.OrderBy(RankNumber).FirstOrDefault(other=>other.Id!=p.Id&&Enumerable.Range(0,8).All(i=>metrics[other.Id].Costs[i]<=dominated.Costs[i])&&Enumerable.Range(0,8).Any(i=>metrics[other.Id].Costs[i]<dominated.Costs[i]));
                if(better!=null)return "Outperformed by "+Rank(better)+" · "+CompareMeasures(dominated,metrics[better.Id],Rank(better),false);
                return "Another offer matches or improves every ranking measure";
            }
            var current=metrics[p.Id];var best=metrics[first.Id];
            if(settings.Preset==(int)PlanningPreset.EconomicalOffer)
            {
                if(current.StockMetres>best.StockMetres)return Compare("More stock",current.StockMetres,best.StockMetres," m");
                if(current.Types>best.Types)return Compare("More panel sizes",current.Types,best.Types);
                if(current.Groups>best.Groups)return Compare("More casting groups",current.Groups,best.Groups);
                return "Same stock, panel sizes and castings; fixed tie order";
            }
            if(settings.Preset==(int)PlanningPreset.FastestConstruction&&current.Groups>best.Groups)return Compare("More casting groups",current.Groups,best.Groups);
            return CompareMeasures(current,best,"1st",false);
        }
        private string CompareMeasures(RankingMetrics current,RankingMetrics other,string reference,bool better)
        {
            if(current.BalancedScore==other.BalancedScore)return "Equal overall balance; fixed tie order";
            int driver=Enumerable.Range(1,7).OrderByDescending(i=>(better?other.Costs[i]-current.Costs[i]:current.Costs[i]-other.Costs[i])/scales.Values[i]).First();
            switch(driver)
            {
                case 1:return Compare(better?"Less stock":"More stock",current.StockMetres,other.StockMetres," m",reference);
                case 2:return Compare(better?"Fewer panel sizes":"More panel sizes",current.Types,other.Types,"",reference);
                case 3:return Compare(better?"Fewer casting groups":"More casting groups",current.Groups,other.Groups,"",reference);
                case 4:return Compare(better?"Higher average panel use":"Lower average panel use",(1-current.MeanIdleFraction)*100,(1-other.MeanIdleFraction)*100,"%",reference);
                case 5:return Compare(better?"Fewer filler joints":"More filler joints",current.FillerLocations,other.FillerLocations,"",reference);
                case 6:return Compare(better?"Higher minimum panel use":"Lower minimum panel use",(1-current.WorstIdleFraction)*100,(1-other.WorstIdleFraction)*100,"%",reference);
                default:return Compare(better?"Closer to target stock length":"Further from target stock length",current.AbsoluteTargetDeviation,other.AbsoluteTargetDeviation," m",reference);
            }
        }
        private static string Compare(string label,decimal value,decimal best,string unit="",string reference="1st")=>label+": "+value.ToString("0.##",CultureInfo.InvariantCulture)+unit+" ("+reference+": "+best.ToString("0.##",CultureInfo.InvariantCulture)+unit+")";
        public static string CastingGroup(int index)=>"C-"+(index+1).ToString("00",CultureInfo.InvariantCulture);
        public static string Ordinal(int number)
        {
            int lastTwo=number%100;string suffix=lastTwo>=11&&lastTwo<=13?"th":number%10==1?"st":number%10==2?"nd":number%10==3?"rd":"th";
            return number.ToString(CultureInfo.InvariantCulture)+suffix;
        }
    }
}
