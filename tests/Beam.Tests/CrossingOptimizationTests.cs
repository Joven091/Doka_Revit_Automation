using System;
using System.Diagnostics;
using System.Linq;
using EngineeringDashboard;

internal static class CrossingOptimizationTests
{
    private static long Counter(BeamCalculatorViewModel vm,string name)
        =>long.Parse(vm.BuildDiagnosticReport().Split('\n').Single(s=>s.StartsWith(name+": ")).Substring(name.Length+2).Trim());
    private static TransferMember[] Members(TransferNetworkResult r)=>r.Beams.Concat(r.CrossSupports.Where(s=>s.Towers!=null).Select(s=>s.Towers))
        .SelectMany(b=>b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers)).ToArray();
    private static double[] Demands(TransferNetworkResult r)=>Members(r).SelectMany(m=>new[] {
        m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization,m.Analysis.TotalLoadKn,m.StartMm,m.LengthMm
    }.Concat(m.Analysis.Reactions).Concat(m.SupportsMm)
        .Concat(m.Uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm,q.KnPerM}))
        .Concat(m.Points.SelectMany(p=>new[] {p.PositionMm,p.Kn}))).ToArray();
    private static void Validate(TransferNetworkResult r)
    {
        if(r.Beams.Length!=3 || r.Bays.Length!=2 || r.CrossSupports.Length!=2 || r.Bays.Any(b=>b==null))throw new Exception("Crossing benchmark requires the entire reported network");
        if(r.Beams.Concat(r.CrossSupports.Select(c=>c.Towers)).Any(l=>l==null || l.MaxLegKn>70+1e-8))throw new Exception("Crossing benchmark leg capacity failed");
        foreach(var member in Members(r))
        {
            if(!member.Analysis.DeflectionEvaluated || member.Utilization>1+1e-8)throw new Exception("Crossing benchmark member strength/deflection failed");
            if(Math.Abs(member.Analysis.Reactions.Sum()-member.Analysis.TotalLoadKn)>1e-8*Math.Max(1,Math.Abs(member.Analysis.TotalLoadKn)))throw new Exception("Crossing benchmark force equilibrium failed");
        }
        var existing=CrossBeamSupportOptimizer.NetworkSolids(r).Concat(CrossBeamSupportOptimizer.Concrete(r)).ToArray();
        foreach(var support in r.CrossSupports)
        foreach(var solid in support.Solids)
        {
            if(existing.Any(solid.Conflicts))throw new Exception("Crossing benchmark retained a physical clash");
            if(r.CrossSupports.Where(c=>!ReferenceEquals(c,support)).SelectMany(c=>c.Solids).Any(solid.Conflicts))throw new Exception("Crossing benchmark retained a crossing-to-crossing clash");
        }
    }
    private static BeamCalculatorViewModel Solve(bool optimize)
    {
        var vm=DiagnosticFullBenchmark.CreateModel();vm.PrecheckCrossingFootprints=optimize;var timer=Stopwatch.StartNew();
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();timer.Stop();
        string label=optimize ? "OPTIMIZED" : "REFERENCE";
        Console.WriteLine(FormattableString.Invariant($"CROSSING-{label} seconds={timer.Elapsed.TotalSeconds:0.000} runtime={Environment.Version} processors={Environment.ProcessorCount} valid={vm.IsCalculationValid}"));
        Console.WriteLine(vm.BuildDiagnosticReport(label+": exact uploaded 2026-10-14 inputs, no elapsed-time limit; console excludes Revit/WPF paint."));
        if(!vm.IsCalculationValid || vm.NetworkResult==null)throw new Exception(label+" did not produce a complete valid result: "+vm.WarningMessage);
        Validate(vm.NetworkResult);return vm;
    }
    internal static void Benchmark()
    {
        ObstacleIndexTests.Run();CrossingFootprintTests.Run();
        // Optimized goes first: the reference gets the warmer JIT, avoiding a
        // second-run warmup advantage for the proposed optimization.
        var optimized=Solve(true);var reference=Solve(false);
        if(optimized.NetworkResult.Schedule!=reference.NetworkResult.Schedule)throw new Exception("Crossing optimization changed selected geometry, stocks or printed member demands");
        var a=Demands(reference.NetworkResult);var b=Demands(optimized.NetworkResult);
        if(a.Length!=b.Length || !a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-12*Math.Max(1,Math.Abs(x))).All(x=>x))throw new Exception("Crossing optimization changed unrounded forces, deflections or reactions");
        // Exact reuse and live leg bounds deliberately change how many equivalent
        // candidates are visited. Compare the checked winning design and every
        // unrounded demand above; both traversals must still complete valid networks.
        if(reference.NetworkResult.Tested<=0 || optimized.NetworkResult.Tested<=0
            || Counter(reference,"Complete network candidates")<=0 || Counter(optimized,"Complete network candidates")<=0)throw new Exception("Crossing optimization did not complete checked network candidates");
        // The isolated footprint tests above assert skipped member work at fixed
        // poses. Full-run counters also depend on concurrent incumbent updates.
        if(Counter(optimized,"Crossing leg clashes rejected before member checks")<=0)throw new Exception("Early exact leg clash rejection was not exercised");
        Console.WriteLine("CROSSING-PARITY passed: selected geometry, stocks, checked complete networks, unrounded demands, physical clashes, member limits and leg capacities.");
    }
}
