using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    /// <summary>A documented general candidate family, not a claim to recover the unknown original catalog strategies.</summary>
    public sealed class CatalogCutoffCandidates : ITop50InventoryCandidateSource
    {
        private readonly ITop50PatternSource source;
        public CatalogCutoffCandidates():this(null){}
        public CatalogCutoffCandidates(ITop50PatternSource source){this.source=source??new FewestComponentPatterns();}
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
        public int SkippedForPanelQuantity { get; internal set; }
    }
    public sealed class Top50Search
    {
        private readonly ITop50InventoryCandidateSource candidates;
        private readonly ITop50InventorySolver solver;
        public Top50Search(ITop50InventoryCandidateSource candidates=null,ITop50InventorySolver solver=null){this.candidates=candidates;this.solver=solver;}
        public SearchRun Run(IReadOnlyList<WallRow> walls,IReadOnlyList<int> catalog,SearchLimits limits,PlanningPreset preset,int displayLimit,CancellationToken cancellation,IProgress<string> progress=null,decimal? percentage=null,decimal bandPoints=10,bool allowExpansion=false,bool identicalKits=false)
        {
            using var runtimeScope=Top50Runtime.Enter();
            // Pattern inputs do not depend on the longest-wall seed or stock
            // quantity. One calculation owns this cache; other projects/runs do not.
            var patterns=new CalculationPatternCache();
            var candidateSource=candidates??new CatalogCutoffCandidates(patterns);
            var inventorySolver=solver??new RetainedInventorySolver(patterns);
            if(catalog==null||catalog.Count==0||catalog.Any(n=>n<=0))throw new ArgumentException("Select at least one positive nominal panel length.");
            if(!Enum.IsDefined(typeof(PlanningPreset),preset))throw new ArgumentException("Select a supported planning preset.");
            if(percentage.HasValue&&(percentage<=0||percentage>100))throw new ArgumentException("Target stock percentage must be greater than 0 and at most 100.");
            if(percentage.HasValue&&bandPoints<0)throw new ArgumentException("Search range above / below target must be zero or greater.");
            identicalKits=!percentage.HasValue&&(identicalKits||limits.RequireIdenticalKits);
            var log=new List<string>{"Native candidate strategy: descending catalog cutoffs and retained longest-wall multisets, plus solo-fit coverage. This is an explicit reconstructed strategy, not recovered original source.","Quantity-first policy: show only the fewest-panel validated tier. Presets compare equal-panel-count inventories.","Repeatable search: fixed pattern-node and single-worker deterministic solver-work limits; no elapsed-time cutoff. Cancellation remains available.","Feasible / best found. Work-limited or omitted lower-count arrangements are not proven infeasible; no global design-space proof."};
            var included=walls.Where(w=>w.Included).ToArray();if(included.Length==0)throw new ArgumentException("Include reviewed walls before optimizing.");
            decimal totalMicrometres=included.Sum(w=>(decimal)w.LengthMicrometres);
            var allInventories=new List<IDictionary<int,int>>();
            if(percentage.HasValue)
            {
                int center=(int)Math.Ceiling(totalMicrometres*percentage.Value/100/included.Max(w=>w.LengthMicrometres));
                for(int count=1;count<=Math.Min(included.Length,center+2);count++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var seedLimits=new SearchLimits(limits.MaxInternalFillers,limits.MaxFillerMm,limits.MaximumGroups,count,limits.SearchEffort,limits.Seed,limits.PatternsPerLength);
                    allInventories.AddRange(candidateSource.Generate(walls,catalog,seedLimits,cancellation));
                }
            }
            else allInventories.AddRange(candidateSource.Generate(walls,catalog,limits,cancellation));
            if(identicalKits)
            {
                var rounded=allInventories.Select(stock=>(IDictionary<int,int>)stock.ToDictionary(p=>p.Key,p=>(int)Math.Ceiling(p.Value/(decimal)limits.LongestSimultaneous)*limits.LongestSimultaneous)).GroupBy(Top50Order.Stock).Select(g=>g.First()).ToArray();
                allInventories=rounded.ToList();int added=0;
                var requiredLengths=included.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(limits.LongestSimultaneous).Select(w=>w.LengthMicrometres).Distinct().ToArray();
                foreach(var stock in rounded)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var completed=CompleteIdenticalSeed(stock,requiredLengths,patterns,limits,cancellation);
                        if(completed==null)log.Add("Identical-kit seed completion omitted: no retained pattern for a required wall length; original rounded candidate preserved.");
                        else if(Top50Order.Stock(completed)!=Top50Order.Stock(stock)){allInventories.Add(completed);added++;}
                    }
                    catch(TimeoutException error){log.Add("Identical-kit seed completion omitted; original rounded candidate preserved. "+error.Message);}
                }
                log.Add("Identical-kit seed completion appended "+added+" candidates before deduplication; original rounded candidates preserved. Completion uses retained patterns and does not prove a global optimum.");
            }
            var unique=allInventories.GroupBy(Top50Order.Stock).Select(g=>(IDictionary<int,int>)g.First().OrderBy(p=>p.Key).ToDictionary(p=>p.Key,p=>p.Value)).ToArray();
            Func<IDictionary<int,int>,bool> inBand=stock=>!percentage.HasValue||Math.Abs(stock.Sum(p=>(decimal)p.Key*p.Value*1000)/totalMicrometres*100-percentage.Value)<=bandPoints;
            Func<IEnumerable<IDictionary<int,int>>,IDictionary<int,int>[]> ordered=items=>items.OrderBy(s=>s.Sum(p=>p.Value)).ThenBy(s=>s.Sum(p=>(long)p.Key*p.Value)).ThenBy(Top50Order.Stock,StringComparer.Ordinal).ToArray();
            var inside=ordered(unique.Where(inBand));var outside=allowExpansion?ordered(unique.Where(s=>!inBand(s))):Array.Empty<IDictionary<int,int>>();
            int candidateCount=inside.Length+outside.Length;
            double workPerInventory=limits.SearchEffort/Math.Max(1,candidateCount);
            log.Add("Eligible inventories: "+candidateCount+"; pattern visit limit per enumeration: "+limits.PatternVisitLimit+"; deterministic solver work per inventory: "+workPerInventory.ToString("R",CultureInfo.InvariantCulture)+".");
            var found=new List<Proposal>();int attempts=0,skippedQuantity=0;
            // The requested percentage band is a project requirement. Expanded
            // offers are considered only when no validated in-band offer exists.
            foreach(var phase in new[]{inside,outside})
            {
                foreach(var tier in phase.GroupBy(s=>s.Sum(p=>p.Value)))
                {
                    foreach(var inventory in tier)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var perInventory=new SearchLimits(limits.MaxInternalFillers,limits.MaxFillerMm,limits.MaximumGroups,percentage.HasValue?1:limits.LongestSimultaneous,limits.SearchEffort,limits.Seed,limits.PatternsPerLength,workPerInventory,identicalKits);
                        string id="S"+(++attempts);progress?.Report("Checking "+tier.Key+"-panel inventory "+id+" of "+candidateCount);
                        try
                        {
                            var result=inventorySolver.Solve(id,walls,inventory,perInventory,cancellation,new SearchProgress(message=>{log.Add(id+": "+message);progress?.Report(id+": "+message);}));
                            if(result==null){log.Add(id+": no complete retained-model allocation within fixed work; no full-space infeasibility claim.");continue;}
                            var issues=Top50Validation.Validate(walls,result,perInventory);
                            if(issues.Count>0)throw new InvalidOperationException("Returned allocation failed independent validation: "+string.Join("; ",issues));
                            found.Add(result);log.Add(id+": validated "+result.PanelCount+" panels, "+(result.StockMicrometres/1000000m).ToString(CultureInfo.InvariantCulture)+"m, "+result.Groups.Count+" groups; retained status "+result.RetainedModelStatus+".");
                        }
                        catch(TimeoutException e){log.Add(id+": "+e.Message);}
                    }
                    if(found.Count>0){skippedQuantity=phase.Count(s=>s.Sum(p=>p.Value)>tier.Key);break;}
                }
                if(found.Count>0)break;
            }
            var scales=new RankingScales(totalMicrometres/1000000m,catalog.Distinct().Count(),limits.MaximumGroups,included.Length*limits.MaxInternalFillers,percentage.HasValue?totalMicrometres/1000000m*percentage.Value/100:(decimal?)null);
            bool hasInBand=!percentage.HasValue || found.Any(p=>Math.Abs(p.StockMicrometres/totalMicrometres*100-percentage.Value)<=bandPoints);
            Func<Proposal,bool> eligible=p=>!percentage.HasValue || !hasInBand && allowExpansion || Math.Abs(p.StockMicrometres/totalMicrometres*100-percentage.Value)<=bandPoints;
            var consolidationLimits=new SearchLimits(limits.MaxInternalFillers,limits.MaxFillerMm,limits.MaximumGroups,percentage.HasValue?1:limits.LongestSimultaneous,limits.SearchEffort,limits.Seed,limits.PatternsPerLength,workPerInventory,identicalKits);
            foreach(var original in found.ToArray())
                found.AddRange(PanelSizeConsolidation.Improve(original,walls,catalog,consolidationLimits,preset,scales,eligible,cancellation,new SearchProgress(message=>{log.Add(message);progress?.Report(message);})));
            // An expanded offer can consolidate back into the requested band.
            // In-band offers retain their precedence over all expanded alternatives.
            if(percentage.HasValue && found.Any(p=>Math.Abs(p.StockMicrometres/totalMicrometres*100-percentage.Value)<=bandPoints))
                found=found.Where(p=>Math.Abs(p.StockMicrometres/totalMicrometres*100-percentage.Value)<=bandPoints).ToList();
            // Equivalent inventory/arrangement pairs reached from different seeds
            // must not occupy several places in the three-result shortlist.
            found=found.GroupBy(p=>Top50Order.Stock(p.Stock)+"/"+string.Join("|",p.Groups.Select(Top50Order.Group)),StringComparer.Ordinal).Select(g=>g.First()).ToList();
            var ranked=ProposalRanking.Rank(found.Select(p=>RankingMetrics.From(p,percentage.HasValue?p.StockMicrometres/1000000m-totalMicrometres/1000000m*percentage.Value/100:0)),preset,displayLimit,scales);
            var shown=ranked.Select(r=>found.Single(p=>p.Id==r.Id)).ToArray();
            for(int i=0;i<shown.Length;i++){shown[i].Rank=i+1;shown[i].Pareto=ranked[i].Pareto;}
            log.Add("All "+found.Count+" found candidates ranked before display limit "+displayLimit+"; "+Math.Max(0,found.Count-shown.Length)+" feasible alternatives omitted from display.");
            log.Add("Skipped "+skippedQuantity+" higher-panel-count candidates under the quantity-first policy. Elapsed time does not select the inventory pool.");
            log.Add("Retained pattern preparations: "+patterns.Preparations+"; exact reuse hits: "+patterns.Hits+".");
            return new SearchRun{Proposals=Array.AsReadOnly(shown),Log=log.AsReadOnly(),Attempted=attempts,Unsearched=candidateCount-attempts,SkippedForPanelQuantity=skippedQuantity,BudgetExpired=false};
        }
        private static IDictionary<int,int> CompleteIdenticalSeed(IDictionary<int,int> stock,IReadOnlyList<long> requiredLengths,ITop50PatternSource patterns,SearchLimits limits,CancellationToken cancellation)
        {
            int kits=limits.LongestSimultaneous;var kit=stock.ToDictionary(p=>p.Key,p=>p.Value/kits);
            foreach(long length in requiredLengths)
            {
                cancellation.ThrowIfCancellationRequested();
                var best=patterns.Enumerate(length,stock.Keys,limits,cancellation)
                    .Select(p=>p.PanelsMm.GroupBy(n=>n).ToDictionary(g=>g.Key,g=>g.Count()))
                    .OrderBy(counts=>counts.Sum(p=>Math.Max(0,p.Value-kit[p.Key])))
                    .ThenBy(counts=>counts.Sum(p=>(long)Math.Max(0,p.Value-kit[p.Key])*p.Key))
                    .ThenBy(Top50Order.Stock,StringComparer.Ordinal).FirstOrDefault();
                if(best==null)return null;
                foreach(var part in best)kit[part.Key]=Math.Max(kit[part.Key],part.Value);
            }
            return kit.ToDictionary(p=>p.Key,p=>checked(p.Value*kits));
        }
        private sealed class SearchProgress:IProgress<string>
        {private readonly Action<string> report;public SearchProgress(Action<string> report){this.report=report;}public void Report(string value)=>report(value);}
    }
}
