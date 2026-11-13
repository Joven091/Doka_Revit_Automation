using System;
using System.Linq;
using EngineeringDashboard;

internal static class PrimaryBandReuseTests
{
    static double[] Demands(LoadedBeamAnalysis a)=>new[] {a.MomentKnM,a.ShearKn,a.DeflectionMm,a.DeflectionUtilization,a.TotalLoadKn}.Concat(a.Reactions).ToArray();
    internal static void Run()
    {
        int checks=0;
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(bool ignore in new[] {false,true})
        foreach(var supports in new[] {new[] {500.0,2800},new[] {400.0,1400,2900}})
        {
            var input=new TowerInput {PrimaryMaterial=material,DeflectionDenominator=270,LegCapacityKn=70,IgnoreUplift=ignore,StagePrimaryChecks=false};
            var work=new TransferredSlabOptimizer.SearchWorkCache();
            foreach(var band in new[] {new[] {0.0,3300},new[] {100.0,3200},new[] {500.0,2700},new[] {1000.0,1750}})
            foreach(double q in new[] {.01,.5,1,12.5,40.0})
            {
                var loads=new[] {new BeamDistributedLoad {StartMm=band[0],EndMm=band[1],KnPerM=q}};
                input.OptimizePrimaryResponses=false;
                var reference=TransferredSlabOptimizer.AnalyzePrimary(input,3300,supports,loads,Array.Empty<BeamPointLoad>(),null,null);
                input.OptimizePrimaryResponses=true;
                var actual=TransferredSlabOptimizer.AnalyzePrimary(input,3300,supports,loads,Array.Empty<BeamPointLoad>(),work.Matrix,work);
                var a=Demands(reference);var b=Demands(actual);
                if(!a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-9*Math.Max(1,Math.Abs(x))).All(x=>x))throw new Exception("Partial UDL response reuse changed member demands: "+material+" / "+ignore+" / "+string.Join(",",supports)+" / "+string.Join(",",band)+" / "+q+" / "+string.Join(",",a)+" vs "+string.Join(",",b));
                foreach(double x in new[] {0.0,400,1000,1750,2800,3300})
                    if(!reference.Response(x).Zip(actual.Response(x),(v,w)=>Math.Abs(v-w)<=1e-9*Math.Max(1,Math.Abs(v))).All(v=>v))throw new Exception("Partial UDL response diagram changed");
                // The existing near-capacity fallback must retain the original
                // arithmetic, rather than accepting a rounded scaled response.
                input.LegCapacityKn=reference.Reactions.Max();
                var boundary=TransferredSlabOptimizer.AnalyzePrimary(input,3300,supports,loads,Array.Empty<BeamPointLoad>(),work.Matrix,work);
                if(!Demands(reference).SequenceEqual(Demands(boundary)))throw new Exception("Leg-capacity boundary did not use original arithmetic");
                input.LegCapacityKn=70;checks++;
            }
        }
        Console.WriteLine("PASS partial UDL reuse: "+checks+" complete responses, diagram samples and exact capacity boundaries across both materials");
    }
}
