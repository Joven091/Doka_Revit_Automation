using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Google.OrTools.Sat;

namespace EngineeringDashboard.Top50
{
    public sealed class PourDefinition
    {
        public string Id {get;set;}
        public string Name {get;set;}
        public string[] WallIds {get;set;}=Array.Empty<string>();
        public PourDefinition Copy()=>new PourDefinition{Id=Id,Name=Name,WallIds=(WallIds??Array.Empty<string>()).ToArray()};
    }
    public static class PourPlan
    {
        public static PourDefinition[] Copy(IEnumerable<PourDefinition> pours)=>(pours??Array.Empty<PourDefinition>()).Select(p=>p?.Copy()??throw new ArgumentException("A pour definition is missing.")).ToArray();
        public static void Validate(IEnumerable<PourDefinition> pours,IEnumerable<WallRow> walls)
        {
            var known=new HashSet<string>((walls??Array.Empty<WallRow>()).Select(w=>w.Id),StringComparer.Ordinal);
            var ids=new HashSet<string>(StringComparer.Ordinal);var assigned=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in pours??Array.Empty<PourDefinition>())
            {
                Guid id;if(p==null||!Guid.TryParseExact(p.Id,"N",out id)||!ids.Add(p.Id))throw new ArgumentException("Pours need unique stable identities.");
                if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>80)throw new ArgumentException("Use a pour name between 1 and 80 characters.");
                if(p.WallIds==null)throw new ArgumentException("A pour wall list is missing.");
                foreach(string wall in p.WallIds)
                    if(!known.Contains(wall)||!assigned.Add(wall))throw new ArgumentException("A pour contains an unknown wall or a wall assigned to more than one pour: "+wall);
            }
        }
    }
    public sealed class PourCheck
    {
        public string PourId {get;internal set;}
        public CastingGroup Group {get;internal set;}
        public IReadOnlyDictionary<int,int> AdditionalPanels {get;internal set;}=new Dictionary<int,int>(); // per face
        public int AdditionalPanelCount=>checked(AdditionalPanels.Values.Sum()*Top50PlannerPolicy.WallFaces);
        public int AdditionalStandard {get;internal set;}
        public int AdditionalAdjustable {get;internal set;}
        public int StandardUsed {get;internal set;}
        public int AdjustableUsed {get;internal set;}
        public bool WithinStock=>Group!=null&&AdditionalPanelCount==0&&AdditionalStandard==0&&AdditionalAdjustable==0;
        public string Detail {get;internal set;}="";
        public string Status=>Group==null?Detail:WithinStock?"Within stock":AdditionalPanelCount>0?"Shown layout needs "+AdditionalPanelCount+" more panels":"Shown layout needs "+(AdditionalStandard+AdditionalAdjustable)+" more connectors";
    }
    public static class PourSequencePlanner
    {
        public static IReadOnlyList<PourCheck> Check(IReadOnlyList<WallRow> walls,IReadOnlyList<PourDefinition> pours,Proposal offer,SearchLimits limits,IReadOnlyList<int> catalog,CancellationToken cancellation,IProgress<string> progress=null)
        {
            using var runtime=Top50Runtime.Enter();
            cancellation.ThrowIfCancellationRequested();PourPlan.Validate(pours,walls);
            if(offer==null||offer.Stock.Count==0)throw new ArgumentException("Select an equipment offer before checking pours.");
            var rows=walls.ToDictionary(w=>w.Id,StringComparer.Ordinal);var results=new List<PourCheck>();var cache=new CalculationPatternCache();
            var connectors=ConnectorAllocation.Calculate(offer);
            var existing=offer.Groups.SelectMany(g=>g.Walls).Where(a=>rows.ContainsKey(a.WallId)).GroupBy(a=>rows[a.WallId].LengthMicrometres).ToDictionary(g=>g.Key,g=>g.Select(a=>a.Pattern).ToArray());
            foreach(var pour in pours)
            {
                cancellation.ThrowIfCancellationRequested();progress?.Report("Checking "+pour.Name+" against the selected offer…");
                var result=new PourCheck{PourId=pour.Id};results.Add(result);
                if(pour.WallIds.Length==0){result.Detail="No walls assigned";continue;}
                var included=pour.WallIds.Select(id=>rows[id]).ToArray();
                if(included.Any(w=>!w.Included||w.Status!=WallReviewStatus.Valid)){result.Detail="Review assigned wall measurements";continue;}
                var local=new SearchLimits(limits.MaxInternalFillers,limits.MaxFillerMm,1,included.Length,limits.SearchEffort,limits.Seed,solverDeterministicWork:limits.SolverDeterministicWork/Math.Max(1,pours.Count));
                var lengths=included.GroupBy(w=>w.LengthMicrometres).OrderByDescending(g=>g.Key).Select(g=>g.OrderBy(w=>w.Id,StringComparer.Ordinal).ToArray()).ToArray();
                PanelPattern[][] options;
                try
                {
                    options=lengths.Select(g=>
                    {
                        var patterns=cache.Enumerate(g[0].LengthMicrometres,catalog,local,cancellation)
                            .Concat(cache.Enumerate(g[0].LengthMicrometres,offer.Stock.Keys,local,cancellation));
                        if(existing.TryGetValue(g[0].LengthMicrometres,out var prior))patterns=patterns.Concat(prior);
                        return patterns.Where(p=>p.LengthMicrometres==g[0].LengthMicrometres&&p.PanelsMm.Count>0&&p.InternalGapsMicrometres.Count==p.PanelsMm.Count-1
                            &&p.FillerLocations<=local.MaxInternalFillers&&p.InternalGapsMicrometres.All(gap=>gap>=0&&gap<=(long)local.MaxFillerMm*1000)&&p.PanelsMm.All(n=>catalog.Contains(n)))
                            .GroupBy(p=>string.Join(",",p.PanelsMm)+"/"+string.Join(",",p.InternalGapsMicrometres),StringComparer.Ordinal).Select(gp=>gp.First()).ToArray();
                    }).ToArray();
                }
                catch(TimeoutException){result.Detail="Pattern search limit reached; no conclusion";continue;}
                if(options.Any(o=>o.Length==0)){result.Detail="No layout found with permitted panels and fillers";continue;}
                var group=Solve(lengths,options,offer,local,false,cancellation)??Solve(lengths,options,offer,local,true,cancellation);
                if(group==null){result.Detail="No layout found within checked patterns";continue;}
                // Restore the engineer's wall ordering. No wall may move to another pour.
                var byId=group.Walls.ToDictionary(a=>a.WallId,StringComparer.Ordinal);group=new CastingGroup(pour.WallIds.Select(id=>byId[id]));
                var used=group.Walls.SelectMany(w=>w.Pattern.PanelsMm).GroupBy(n=>n).ToDictionary(g=>g.Key,g=>g.Count());
                var validation=new Proposal(pour.Id,used,new[]{group});
                var errors=Top50Validation.Validate(included,validation,local);if(errors.Count>0)throw new InvalidOperationException("Pour layout validation failed: "+string.Join("; ",errors));
                result.Group=group;
                result.AdditionalPanels=new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(used.Where(p=>p.Value>(offer.Stock.TryGetValue(p.Key,out int have)?have:0)).ToDictionary(p=>p.Key,p=>p.Value-(offer.Stock.TryGetValue(p.Key,out int have)?have:0)));
                result.AdjustableUsed=checked(group.Walls.Sum(w=>w.Pattern.FillerLocations)*Top50PlannerPolicy.WallFaces);
                result.StandardUsed=checked(group.Walls.Sum(w=>w.Pattern.DirectLocations)*Top50PlannerPolicy.WallFaces);
                result.AdditionalAdjustable=(int)Math.Max(0,result.AdjustableUsed-connectors.AdjustableStock);
                result.AdditionalStandard=(int)Math.Max(0,result.StandardUsed+result.AdjustableUsed-connectors.StandardStock-connectors.AdjustableStock-result.AdditionalAdjustable);
                result.Detail=result.WithinStock?"All assigned walls fit the selected reusable stock simultaneously. Connectors cover one level on both faces.":
                    "Shortage for the shown, independently validated layout. A fitting alternative was not found in the checked patterns; additional stock is not proven unavoidable. Panel quantities and connector shortages include both faces. Connector counts cover one level.";
            }
            return results.AsReadOnly();
        }
        static CastingGroup Solve(WallRow[][] lengths,PanelPattern[][] options,Proposal offer,SearchLimits limits,bool addStock,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();var model=new CpModel();var variables=new List<IntVar>();var entries=new List<Tuple<int,int>>();
            for(int t=0;t<lengths.Length;t++)
            {
                var row=new List<IntVar>();
                for(int p=0;p<options[t].Length;p++){var v=model.NewIntVar(0,lengths[t].Length,"n-"+t+"-"+p);row.Add(v);variables.Add(v);entries.Add(Tuple.Create(t,p));}
                model.Add(LinearExpr.Sum(row)==lengths[t].Length);
            }
            var extras=new List<IntVar>();long maximum=lengths.Select((g,t)=>(long)g.Length*options[t].Max(p=>p.PanelsMm.Count)).Sum();
            foreach(int size in options.SelectMany(o=>o).SelectMany(p=>p.PanelsMm).Distinct().OrderBy(n=>n))
            {
                cancellation.ThrowIfCancellationRequested();
                var demand=LinearExpr.WeightedSum(variables,entries.Select(e=>(long)options[e.Item1][e.Item2].PanelsMm.Count(n=>n==size)));
                int have=offer.Stock.TryGetValue(size,out int n)?n:0;
                if(addStock){var extra=model.NewIntVar(0,maximum,"extra-"+size);extras.Add(extra);model.Add(demand<=have+extra);}else model.Add(demand<=have);
            }
            var total=LinearExpr.WeightedSum(variables,entries.Select(e=>(long)options[e.Item1][e.Item2].PanelsMm.Count));
            model.Minimize(addStock?LinearExpr.Sum(extras)*(maximum+1)+total:total);
            var solver=new CpSolver{StringParameters="max_deterministic_time:"+limits.SolverDeterministicWork.ToString("R",CultureInfo.InvariantCulture)+" num_search_workers:1 randomize_search:false random_seed:"+limits.Seed};
            var status=CancellableSearch.Run(()=>solver.Solve(model),solver.StopSearch,solver,cancellation);cancellation.ThrowIfCancellationRequested();
            if(status==CpSolverStatus.ModelInvalid)throw new InvalidOperationException("Invalid pour allocation model.");
            if(status!=CpSolverStatus.Optimal&&status!=CpSolverStatus.Feasible)return null;
            var pending=lengths.Select(g=>new Queue<WallRow>(g)).ToArray();var arrangements=new List<WallArrangement>();
            for(int i=0;i<variables.Count;i++)for(long j=0;j<solver.Value(variables[i]);j++)
            {var e=entries[i];arrangements.Add(new WallArrangement(pending[e.Item1].Dequeue().Id,options[e.Item1][e.Item2]));}
            return new CastingGroup(arrangements);
        }
    }
}
