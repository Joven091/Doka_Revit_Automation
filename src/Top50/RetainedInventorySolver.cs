using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Google.OrTools.Sat;

namespace EngineeringDashboard.Top50
{
    /// <summary>General exact stock-constrained packing over retained multisets; no golden-case stock constants.</summary>
    public sealed class RetainedInventorySolver : ITop50InventorySolver
    {
        private readonly ITop50PatternSource patterns;
        public RetainedInventorySolver(ITop50PatternSource patterns=null){this.patterns=patterns??new FewestComponentPatterns();}
        public Proposal Solve(string id,IReadOnlyList<WallRow> walls,IDictionary<int,int> stock,SearchLimits limits,CancellationToken cancellation,IProgress<string> progress=null)
        {
            using var runtimeScope=Top50Runtime.Enter();
            cancellation.ThrowIfCancellationRequested();
            stock=stock.OrderBy(p=>p.Key).ToDictionary(p=>p.Key,p=>p.Value);
            var included=walls.Where(w=>w.Included).ToList();
            if(included.Count==0 || stock.Count==0 || stock.Any(x=>x.Key<=0||x.Value<=0))throw new ArgumentException("Positive reusable stock and included walls are required.");
            if(limits.RequireIdenticalKits&&stock.Any(p=>p.Value%limits.LongestSimultaneous!=0))
            {progress?.Report("Stock does not form the required identical kits.");return null;}
            var lengths=included.GroupBy(w=>w.LengthMicrometres).OrderByDescending(g=>g.Key).Select(g=>g.OrderBy(w=>w.Id,StringComparer.Ordinal).ToArray()).ToArray();
            var retained=lengths.Select(group=>patterns.Enumerate(group[0].LengthMicrometres,stock.Keys,limits,cancellation).Where(p=>p.PanelsMm.GroupBy(n=>n).All(g=>g.Count()<=stock[g.Key])).ToArray()).ToArray();
            var kitCompatible=limits.RequireIdenticalKits?retained.Select(options=>options.Select(p=>p.PanelsMm.GroupBy(n=>n).All(g=>g.Count()<=stock[g.Key]/limits.LongestSimultaneous)).ToArray()).ToArray():null;
            cancellation.ThrowIfCancellationRequested();
            if(retained.Any(p=>p.Length==0)){progress?.Report("No stock-feasible retained pattern; this does not prove full-space infeasibility.");return null;}
            int groupCount=Math.Min(limits.MaximumGroups,included.Count);var model=new CpModel();
            var active=Enumerable.Range(0,groupCount).Select(g=>{cancellation.ThrowIfCancellationRequested();return model.NewBoolVar("active-"+g);}).ToArray();
            var variables=new Dictionary<Tuple<int,int,int>,IntVar>();
            for(int t=0;t<lengths.Length;t++)for(int p=0;p<retained[t].Length;p++)for(int g=0;g<groupCount;g++)
            {if((variables.Count&127)==0)cancellation.ThrowIfCancellationRequested();variables.Add(Tuple.Create(t,p,g),model.NewIntVar(0,lengths[t].Length,"count-"+t+"-"+p+"-"+g));}
            var orderedVariables=variables.OrderBy(v=>v.Key.Item1).ThenBy(v=>v.Key.Item2).ThenBy(v=>v.Key.Item3).ToArray();
            for(int t=0;t<lengths.Length;t++){cancellation.ThrowIfCancellationRequested();model.Add(LinearExpr.Sum(orderedVariables.Where(v=>v.Key.Item1==t).Select(v=>v.Value))==lengths[t].Length);}
            for(int g=0;g<groupCount;g++)
            {
                cancellation.ThrowIfCancellationRequested();
                var groupVariables=orderedVariables.Where(v=>v.Key.Item3==g).Select(v=>v.Value).ToArray();
                var number=LinearExpr.Sum(groupVariables);model.Add(number>=active[g]);model.Add(number<=included.Count*active[g]);
                foreach(var size in stock.Keys)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var entries=orderedVariables.Where(v=>v.Key.Item3==g).ToArray();
                    model.Add(LinearExpr.WeightedSum(entries.Select(e=>e.Value),entries.Select(e=>(long)retained[e.Key.Item1][e.Key.Item2].PanelsMm.Count(n=>n==size)))<=stock[size]);
                }
                if(g>0)model.Add(active[g]<=active[g-1]);
            }
            var longest=included.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(limits.LongestSimultaneous).ToArray();
            for(int t=0;t<lengths.Length;t++)
            {
                cancellation.ThrowIfCancellationRequested();
                int required=longest.Count(w=>w.LengthMicrometres==lengths[t][0].LengthMicrometres);
                if(required>0)
                {
                    // Only the specifically required longest walls must fit one
                    // kit. Other walls of this same length may use pooled stock.
                    model.Add(LinearExpr.Sum(orderedVariables.Where(v=>v.Key.Item1==t&&v.Key.Item3==0&&(!limits.RequireIdenticalKits||kitCompatible[t][v.Key.Item2])).Select(v=>v.Value))>=required);
                }
            }
            // Interchangeable later groups are symmetry-broken by panel load, never by physical site sequence.
            LinearExpr Load(int g)
            {
                var entries=orderedVariables.Where(v=>v.Key.Item3==g).ToArray();
                return LinearExpr.WeightedSum(entries.Select(v=>v.Value),entries.Select(v=>retained[v.Key.Item1][v.Key.Item2].PanelsMm.Sum(n=>(long)n)));
            }
            for(int g=2;g<groupCount;g++){cancellation.ThrowIfCancellationRequested();model.Add(Load(g)<=Load(g-1));}
            model.Minimize(LinearExpr.Sum(active));
            var solver=new CpSolver {StringParameters="max_deterministic_time:"+limits.SolverDeterministicWork.ToString("R",CultureInfo.InvariantCulture)+" num_search_workers:1 randomize_search:false random_seed:"+limits.Seed.ToString(CultureInfo.InvariantCulture)};
            progress?.Report("Solving inventory allocation in the retained-pattern model; omitted arrangements are not proven infeasible.");
            // CpSolver creates/releases its wrapper under this same instance lock.
            // It protects StopSearch from racing wrapper teardown; it does not
            // hold the lock for the native solve itself.
            var status=CancellableSearch.Run(()=>solver.Solve(model),solver.StopSearch,solver,cancellation);
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("Retained status: "+status+"; deterministic work allocation: "+limits.SolverDeterministicWork.ToString("R",CultureInfo.InvariantCulture)+". Optimal refers only to this retained inventory model.");
            if(status==CpSolverStatus.ModelInvalid)throw new InvalidOperationException("The retained allocation model is invalid.");
            if(status!=CpSolverStatus.Optimal && status!=CpSolverStatus.Feasible){progress?.Report("Retained model status: "+status+"; no full-space infeasibility claim.");return null;}
            var pending=lengths.Select(t=>new Queue<WallRow>(t)).ToArray();var groups=new List<CastingGroup>();
            for(int g=0;g<groupCount;g++)
            {
                var arrangements=new List<WallArrangement>();
                for(int t=0;t<lengths.Length;t++)
                {
                    // Required IDs are first in each length's ordinal queue.
                    // Assign compatible copies first in group zero; leave the
                    // ordinary non-kit reconstruction order untouched.
                    IEnumerable<int> order=Enumerable.Range(0,retained[t].Length);
                    if(g==0&&limits.RequireIdenticalKits)order=order.OrderByDescending(p=>kitCompatible[t][p]);
                    foreach(int p in order)for(long n=0;n<solver.Value(variables[Tuple.Create(t,p,g)]);n++)arrangements.Add(new WallArrangement(pending[t].Dequeue().Id,retained[t][p]));
                }
                if(arrangements.Count>0)groups.Add(new CastingGroup(arrangements.OrderBy(a=>a.WallId,StringComparer.Ordinal)));
            }
            groups=groups.Take(1).Concat(groups.Skip(1).OrderByDescending(g=>g.InstalledPanelMicrometres).ThenBy(Top50Order.Group,StringComparer.Ordinal)).ToList();
            var proposal=new Proposal(id,stock,groups){RetainedModelStatus=status.ToString(),RetainedObjective=solver.ObjectiveValue,RetainedBound=solver.BestObjectiveBound,IdenticalKitCount=limits.RequireIdenticalKits?limits.LongestSimultaneous:0};
            var errors=Top50Validation.Validate(walls,proposal,limits);if(errors.Count>0)throw new InvalidOperationException("Independent allocation validation failed: "+string.Join("; ",errors));
            return proposal;
        }
    }
}
