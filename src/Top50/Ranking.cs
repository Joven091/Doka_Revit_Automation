using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    public enum PlanningPreset { EconomicalOffer, BalancedOptimization, FastestConstruction }
    public sealed class RankingMetrics
    {
        public string Id { get; }
        public int PanelCount { get; }
        public string StableKey { get; }
        public decimal StockMetres { get; }
        public int Types { get; }
        public int Groups { get; }
        public decimal MeanIdleFraction { get; }
        public int FillerLocations { get; }
        public decimal WorstIdleFraction { get; }
        public decimal AbsoluteTargetDeviation { get; }
        public bool Pareto { get; internal set; }
        public decimal BalancedScore { get; internal set; }
        internal decimal[] Costs => new[]{(decimal)PanelCount,StockMetres,Types,Groups,MeanIdleFraction,FillerLocations,WorstIdleFraction,AbsoluteTargetDeviation};
        public RankingMetrics(string id,int panelCount,decimal stockMetres,int types,int groups,decimal meanUse,int fillers,decimal minimumUse,decimal absoluteTargetDeviation=0,string stableKey=null)
        {if(panelCount<1)throw new ArgumentOutOfRangeException(nameof(panelCount));Id=id;PanelCount=panelCount;StableKey=stableKey??id;StockMetres=stockMetres;Types=types;Groups=groups;MeanIdleFraction=1-meanUse;FillerLocations=fillers;WorstIdleFraction=1-minimumUse;AbsoluteTargetDeviation=Math.Abs(absoluteTargetDeviation);}
        public static RankingMetrics From(Proposal p,decimal absoluteTargetDeviation=0)=>new RankingMetrics(p.Id,p.PanelCount,p.StockMicrometres/1000000m,p.Stock.Count,p.Groups.Count,p.MeanUtilization,p.FillerLocations,p.MinimumUtilization,absoluteTargetDeviation,Top50Order.Stock(p.Stock));
    }
    public sealed class RankingScales
    {
        internal decimal[] Values {get;}
        public RankingScales(decimal totalWallMetres,int catalogTypes,int maximumGroups,int maximumFillerLocations,decimal? targetMetres=null)
        {
            if(totalWallMetres<=0 || catalogTypes<1 || maximumGroups<1 || maximumFillerLocations<0)throw new ArgumentOutOfRangeException(nameof(totalWallMetres));
            Values=new[]{1m,totalWallMetres,(decimal)catalogTypes,maximumGroups,1m,Math.Max(1,maximumFillerLocations),1m,Math.Max(1,targetMetres??totalWallMetres)};
        }
    }
    public static class ProposalRanking
    {
        public static IReadOnlyList<RankingMetrics> Rank(IEnumerable<RankingMetrics> candidates,PlanningPreset preset,int displayLimit,RankingScales scales)
        {
            if(displayLimit<1)throw new ArgumentOutOfRangeException(nameof(displayLimit));
            if(scales==null)throw new ArgumentNullException(nameof(scales));
            var found=candidates.ToArray();if(found.Length==0)return found;
            foreach(var c in found)c.Pareto=false;
            // Quantity is a requirement for admission to the shortlist, before
            // Pareto/scoring. A metre saving cannot reintroduce 60 versus 27 panels.
            int fewest=found.Min(c=>c.PanelCount);
            var all=found.Where(c=>c.PanelCount==fewest).ToArray();
            foreach(var c in all)
            {
                c.Pareto=!all.Any(other=>!ReferenceEquals(c,other)&&Enumerable.Range(0,8).All(i=>other.Costs[i]<=c.Costs[i])&&Enumerable.Range(0,8).Any(i=>other.Costs[i]<c.Costs[i]));
                // Fixed project scales replace candidate-pool min/max scaling.
                c.BalancedScore=Enumerable.Range(1,7).Sum(i=>c.Costs[i]/scales.Values[i])/7;
            }
            var front=all.OrderByDescending(c=>c.Pareto);
            IOrderedEnumerable<RankingMetrics> ranked;
            if(preset==PlanningPreset.EconomicalOffer)ranked=front.ThenBy(c=>c.StockMetres).ThenBy(c=>c.Types).ThenBy(c=>c.Groups);
            else if(preset==PlanningPreset.FastestConstruction)ranked=front.ThenBy(c=>c.Groups).ThenBy(c=>c.BalancedScore);
            else ranked=front.ThenBy(c=>c.BalancedScore);
            return Array.AsReadOnly(ranked.ThenBy(c=>c.StableKey,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).Take(displayLimit).ToArray());
        }
    }
}
