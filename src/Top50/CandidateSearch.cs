using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    /// <summary>A documented general candidate family, not a claim to recover the unknown original catalog strategies.</summary>
    public sealed class CatalogCutoffCandidates : ITop50InventoryCandidateSource
    {
        private readonly ITop50PatternSource source=new FewestComponentPatterns();
        public IEnumerable<IDictionary<int,int>> Generate(IReadOnlyList<WallRow> walls,IReadOnlyList<int> allowed,SearchLimits limits,CancellationToken cancellation)
        {
            var included=walls.Where(w=>w.Included).ToArray();
            var longest=included.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(limits.LongestSimultaneous).ToArray();
            var lengths=included.Select(w=>w.LengthMicrometres).Distinct().OrderBy(x=>x).ToArray();var seen=new HashSet<string>();
            foreach(var cutoff in allowed.Distinct().OrderByDescending(x=>x))
            {
                cancellation.ThrowIfCancellationRequested();
                var catalog=allowed.Where(x=>x<=cutoff).ToArray();
                var options=lengths.ToDictionary(n=>n,n=>source.Enumerate(n,catalog,limits,cancellation));
                if(options.Values.Any(v=>v.Count==0))continue;
                for(int variant=0;variant<limits.PatternsPerLength;variant++)
                {
                    var stock=new Dictionary<int,int>();
                    foreach(var wall in longest)foreach(var size in options[wall.LengthMicrometres][variant%options[wall.LengthMicrometres].Count].PanelsMm){stock.TryGetValue(size,out var n);stock[size]=n+1;}
                    foreach(var length in lengths)
                    {
                        var best=options[length].OrderBy(p=>p.PanelsMm.GroupBy(n=>n).Sum(g=>Math.Max(0,g.Count()-(stock.TryGetValue(g.Key,out var have)?have:0))*(long)g.Key))
                            .ThenBy(p=>p.PanelsMm.Distinct().Count(n=>!stock.ContainsKey(n))).ThenBy(p=>p.PanelsMm.Count).First();
                        foreach(var part in best.PanelsMm.GroupBy(n=>n)){stock.TryGetValue(part.Key,out var n);stock[part.Key]=Math.Max(n,part.Count());}
                    }
                    var signature=string.Join(";",stock.OrderBy(p=>p.Key).Select(p=>p.Key.ToString(CultureInfo.InvariantCulture)+":"+p.Value));
                    if(seen.Add(signature))yield return stock;
                }
            }
        }
    }
    public sealed class SearchRun
    {
        public IReadOnlyList<Proposal> Proposals { get; internal set; }=Array.Empty<Proposal>();
        public IReadOnlyList<string> Log { get; internal set; }=Array.Empty<string>();
        public int Attempted { get; internal set; }
        public int Unsearched { get; internal set; }
        public bool BudgetExpired { get; internal set; }
    }
    public sealed class Top50Search
    {
        private readonly ITop50InventoryCandidateSource candidates;
        private readonly ITop50InventorySolver solver;
        public Top50Search(ITop50InventoryCandidateSource candidates=null,ITop50InventorySolver solver=null){this.candidates=candidates??new CatalogCutoffCandidates();this.solver=solver??new RetainedInventorySolver();}
        public SearchRun Run(IReadOnlyList<WallRow> walls,IReadOnlyList<int> catalog,SearchLimits limits,PlanningPreset preset,int displayLimit,CancellationToken cancellation,IProgress<string> progress=null,decimal? percentage=null,decimal bandPoints=10,bool allowExpansion=false,bool identicalKits=false)
        {
            using var runtimeScope=Top50Runtime.Enter();
            if(percentage.HasValue&&(percentage<=0||percentage>100||bandPoints<0))throw new ArgumentOutOfRangeException(nameof(percentage));
            if(identicalKits&&percentage.HasValue)throw new ArgumentException("Identical fixed kits require Fixed longest-wall sets.");
            var log=new List<string>{"Native candidate strategy: descending catalog cutoffs and retained longest-wall multisets, plus solo-fit coverage. This is an explicit reconstructed strategy, not recovered original source.","Feasible / best found. Bounds apply only to each retained-pattern inventory allocation; no global design-space proof."};
            var clock=Stopwatch.StartNew();
            var included=walls.Where(w=>w.Included).ToArray();if(included.Length==0)throw new ArgumentException("Include reviewed walls before optimizing.");
            decimal totalMicrometres=included.Sum(w=>(decimal)w.LengthMicrometres);
            var allInventories=new List<IDictionary<int,int>>();
            if(percentage.HasValue)
            {
                int center=(int)Math.Ceiling(totalMicrometres*percentage.Value/100/included.Max(w=>w.LengthMicrometres));
                for(int count=1;count<=Math.Min(included.Length,center+2);count++)
                {
                    cancellation.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>=limits.Seconds)break;
                    var seedLimits=new SearchLimits(limits.MaxComponents,limits.MaxInternalFillers,limits.MaxFillerMm,limits.MaximumGroups,count,limits.Seconds,limits.Seed,limits.PatternsPerLength);
                    allInventories.AddRange(candidates.Generate(walls,catalog,seedLimits,cancellation));
                }
            }
            else allInventories.AddRange(candidates.Generate(walls,catalog,limits,cancellation));
            if(identicalKits)
                allInventories=allInventories.Select(stock=>(IDictionary<int,int>)stock.ToDictionary(p=>p.Key,p=>(int)Math.Ceiling(p.Value/(decimal)limits.LongestSimultaneous)*limits.LongestSimultaneous)).ToList();
            var unique=allInventories.GroupBy(stock=>string.Join(";",stock.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value))).Select(g=>g.First()).ToArray();
            Func<IDictionary<int,int>,bool> inBand=stock=>!percentage.HasValue||Math.Abs(stock.Sum(p=>(decimal)p.Key*p.Value*1000)/totalMicrometres*100-percentage.Value)<=bandPoints;
            var inventories=unique.Where(inBand).Concat(allowExpansion?unique.Where(s=>!inBand(s)):Enumerable.Empty<IDictionary<int,int>>()).ToArray();
            var found=new List<Proposal>();int attempts=0;bool foundInside=false;
            foreach(var inventory in inventories)
            {
                cancellation.ThrowIfCancellationRequested();double remaining=limits.Seconds-clock.Elapsed.TotalSeconds;if(remaining<=0)break;
                if(!inBand(inventory)&&foundInside)break;
                double slice=Math.Min(2,Math.Max(.1,remaining/Math.Max(1,inventories.Length-attempts)));
                var perInventory=new SearchLimits(limits.MaxComponents,limits.MaxInternalFillers,limits.MaxFillerMm,limits.MaximumGroups,percentage.HasValue?1:limits.LongestSimultaneous,slice,limits.Seed,limits.PatternsPerLength);
                string id="S"+(++attempts);progress?.Report("Exploring "+id+" of "+inventories.Length+" retained inventory candidates");
                try
                {
                    var result=solver.Solve(id,walls,inventory,perInventory,cancellation);
                    if(result!=null)
                    {
                        if(identicalKits)
                        {
                            bool fit=included.OrderByDescending(w=>w.LengthMicrometres).Take(limits.LongestSimultaneous).All(w=>new FewestComponentPatterns().Enumerate(w.LengthMicrometres,inventory.Keys,perInventory,cancellation).Any(p=>p.PanelsMm.GroupBy(n=>n).All(g=>g.Count()<=inventory[g.Key]/limits.LongestSimultaneous)));
                            if(!fit){log.Add(id+": rejected: repeated stock does not provide an independently fitting kit for every required longest wall.");continue;}
                            result.IdenticalKitCount=limits.LongestSimultaneous;
                            if(Top50Validation.Validate(walls,result,perInventory).Count>0){log.Add(id+": rejected: returned allocation did not fit the declared individual identical kits.");continue;}
                        }
                        found.Add(result);if(inBand(inventory))foundInside=true;log.Add(id+": validated "+result.StockMicrometres/1000000m+"m, "+result.Groups.Count+" groups; retained status "+result.RetainedModelStatus+".");
                    }
                    else log.Add(id+": no complete proposal returned within its retained-model budget; not a full-space infeasibility claim.");
                }
                catch(TimeoutException e){log.Add(id+": "+e.Message);}
            }
            var ranked=ProposalRanking.Rank(found.Select(p=>RankingMetrics.From(p,percentage.HasValue?p.StockMicrometres/1000000m-totalMicrometres/1000000m*percentage.Value/100:0)),preset,displayLimit);
            var shown=ranked.Select(r=>found.Single(p=>p.Id==r.Id)).ToArray();
            for(int i=0;i<shown.Length;i++){shown[i].Rank=i+1;shown[i].Pareto=ranked[i].Pareto;}
            log.Add("All "+found.Count+" found candidates ranked before display limit "+displayLimit+"; "+Math.Max(0,found.Count-shown.Length)+" feasible alternatives omitted from display.");
            return new SearchRun{Proposals=Array.AsReadOnly(shown),Log=log.AsReadOnly(),Attempted=attempts,Unsearched=inventories.Length-attempts,BudgetExpired=clock.Elapsed.TotalSeconds>=limits.Seconds};
        }
    }
}
