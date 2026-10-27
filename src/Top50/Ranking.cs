using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard.Top50
{
    public enum PlanningPreset { EconomicalOffer, BalancedOptimization, FastestConstruction }
    public sealed class RankingMetrics
    {
        public string Id { get; }
        public decimal StockMetres { get; }
        public int Types { get; }
        public int Groups { get; }
        public decimal MeanIdleFraction { get; }
        public int FillerLocations { get; }
        public decimal WorstIdleFraction { get; }
        public decimal AbsoluteTargetDeviation { get; }
        public bool Pareto { get; internal set; }
        public decimal BalancedScore { get; internal set; }
        internal decimal[] Costs => new[]{StockMetres,Types,Groups,MeanIdleFraction,FillerLocations,WorstIdleFraction,AbsoluteTargetDeviation};
        public RankingMetrics(string id,decimal stockMetres,int types,int groups,decimal meanUse,int fillers,decimal minimumUse,decimal absoluteTargetDeviation=0)
        {Id=id;StockMetres=stockMetres;Types=types;Groups=groups;MeanIdleFraction=1-meanUse;FillerLocations=fillers;WorstIdleFraction=1-minimumUse;AbsoluteTargetDeviation=Math.Abs(absoluteTargetDeviation);}
        public static RankingMetrics From(Proposal p,decimal absoluteTargetDeviation=0)=>new RankingMetrics(p.Id,p.StockMicrometres/1000000m,p.Stock.Count,p.Groups.Count,p.MeanUtilization,p.FillerLocations,p.MinimumUtilization,absoluteTargetDeviation);
    }
    public static class ProposalRanking
    {
        public static IReadOnlyList<RankingMetrics> Rank(IEnumerable<RankingMetrics> candidates,PlanningPreset preset,int displayLimit)
        {
            if(displayLimit<1)throw new ArgumentOutOfRangeException(nameof(displayLimit));
            var all=candidates.ToArray();if(all.Length==0)return all;
            var minima=Enumerable.Range(0,7).Select(i=>all.Min(c=>c.Costs[i])).ToArray();var maxima=Enumerable.Range(0,7).Select(i=>all.Max(c=>c.Costs[i])).ToArray();
            foreach(var c in all)
            {
                c.Pareto=!all.Any(other=>!ReferenceEquals(c,other)&&Enumerable.Range(0,7).All(i=>other.Costs[i]<=c.Costs[i])&&Enumerable.Range(0,7).Any(i=>other.Costs[i]<c.Costs[i]));
                c.BalancedScore=Enumerable.Range(0,7).Sum(i=>maxima[i]==minima[i]?0:(c.Costs[i]-minima[i])/(maxima[i]-minima[i]))/7;
            }
            var front=all.OrderByDescending(c=>c.Pareto);
            IOrderedEnumerable<RankingMetrics> ranked;
            if(preset==PlanningPreset.EconomicalOffer)ranked=front.ThenBy(c=>c.StockMetres).ThenBy(c=>c.Types).ThenBy(c=>c.Groups);
            else if(preset==PlanningPreset.FastestConstruction)ranked=front.ThenBy(c=>c.Groups).ThenBy(c=>c.BalancedScore);
            else ranked=front.ThenBy(c=>c.BalancedScore);
            return Array.AsReadOnly(ranked.ThenBy(c=>c.Id,StringComparer.Ordinal).Take(displayLimit).ToArray());
        }
    }
}
