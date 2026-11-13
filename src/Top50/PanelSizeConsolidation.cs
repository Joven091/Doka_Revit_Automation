using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    // Improve a checked equipment allocation without rerunning the timed/work-limited
    // inventory solves. Group membership/order and the total panel quantity stay fixed.
    internal static class PanelSizeConsolidation
    {
        internal static IReadOnlyList<Proposal> Improve(Proposal original,IReadOnlyList<WallRow> walls,IReadOnlyList<int> catalog,
            SearchLimits limits,PlanningPreset preset,RankingScales scales,Func<Proposal,bool> eligible,CancellationToken cancellation,
            IProgress<string> progress=null)
        {
            var permitted=new HashSet<int>(catalog);var result=new List<Proposal>();var current=original;int tested=0;
            // Every accepted step removes one panel size. Test every ordered pair
            // in stable size order, then continue from the best improving step.
            for(int step=1;current.Stock.Count>1;step++)
            {
                cancellation.ThrowIfCancellationRequested();var trials=new List<Proposal>();
                foreach(int from in current.Stock.Keys.OrderBy(x=>x))foreach(int to in current.Stock.Keys.OrderBy(x=>x))
                {
                    cancellation.ThrowIfCancellationRequested();if(from==to || !permitted.Contains(to))continue;
                    tested++;
                    var candidate=Replace(current,original.Id+"-C"+step.ToString(CultureInfo.InvariantCulture)+"-"+from+"-"+to,from,to,limits,cancellation);
                    if(candidate==null || !eligible(candidate) || Top50Validation.Validate(walls,candidate,limits).Count!=0)continue;
                    trials.Add(candidate);
                }
                if(trials.Count==0)break;
                // Keep every checked alternative for the final cross-offer ranking;
                // the best local improvement determines the next consolidation step.
                result.AddRange(trials);
                var ranked=ProposalRanking.Rank(new[]{current}.Concat(trials).Select(p=>RankingMetrics.From(p,TargetDelta(p))),preset,1,scales);
                if(ranked[0].Id==current.Id)break;
                current=trials.Single(p=>p.Id==ranked[0].Id);
            }
            progress?.Report(original.Id+": panel-size consolidation checked "+tested+" substitutions; "+result.Count+" valid alternatives retained with the original offer.");
            return result.AsReadOnly();
            decimal TargetDelta(Proposal proposal)
            {
                return scales.TargetMetres.HasValue ? proposal.StockMicrometres/1000000m-scales.TargetMetres.Value : 0;
            }
        }
        private static Proposal Replace(Proposal source,string id,int from,int to,SearchLimits limits,CancellationToken cancellation)
        {
            var stock=source.Stock.ToDictionary(p=>p.Key,p=>p.Value);
            stock[to]=checked(stock[to]+stock[from]);stock.Remove(from);
            var groups=new List<CastingGroup>();
            foreach(var group in source.Groups)
            {
                cancellation.ThrowIfCancellationRequested();var arrangements=new List<WallArrangement>();
                foreach(var wall in group.Walls)
                {
                    cancellation.ThrowIfCancellationRequested();int count=wall.Pattern.PanelsMm.Count(n=>n==from);
                    if(count==0){arrangements.Add(wall);continue;}
                    var panels=wall.Pattern.PanelsMm.Select(n=>n==from?to:n).ToArray();
                    var gaps=wall.Pattern.InternalGapsMicrometres.ToArray();
                    long change=checked((long)(from-to)*count*1000);
                    if(!AdjustGaps(gaps,change,limits))return null;
                    arrangements.Add(new WallArrangement(wall.WallId,new PanelPattern(panels,gaps)));
                }
                groups.Add(new CastingGroup(arrangements));
            }
            return new Proposal(id,stock,groups) {IdenticalKitCount=source.IdenticalKitCount,RetainedModelStatus="Validated panel-size consolidation; allocation not re-solved",
                RetainedObjective=groups.Count,RetainedBound=0};
        }
        private static bool AdjustGaps(long[] gaps,long change,SearchLimits limits)
        {
            if(change<0)
            {
                long remove=-change;
                for(int i=0;i<gaps.Length && remove>0;i++){long take=Math.Min(gaps[i],remove);gaps[i]-=take;remove-=take;}
                return remove==0;
            }
            // Preserve existing filler positions first. A new internal filler is
            // eligible only when the project's filler-count/width rules allow it.
            int fillers=gaps.Count(g=>g>0);long maximum=(long)limits.MaxFillerMm*1000;
            foreach(int i in Enumerable.Range(0,gaps.Length).OrderByDescending(i=>gaps[i]>0).ThenBy(i=>i))
            {
                if(change==0)break;
                if(gaps[i]==0 && fillers>=limits.MaxInternalFillers)continue;
                long add=Math.Min(Math.Max(0,maximum-gaps[i]),change);
                if(add>0 && gaps[i]==0)fillers++;
                gaps[i]+=add;change-=add;
            }
            return change==0;
        }
    }
}
