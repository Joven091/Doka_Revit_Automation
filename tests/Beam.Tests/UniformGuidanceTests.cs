using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class UniformGuidanceTests
{
    static int checks;
    static void Check(bool value,string text) {checks++;if(!value)throw new Exception("Uniform-load guidance: "+text);}
    static TowerInput Common(bool guide,bool custom,int workers)=>new TowerInput {LengthMm=2450,LegCapacityKn=70,
        StockLengthsMm=new[] {1250.0,1800,2450},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,
        DeflectionDenominator=270,AllowCustomH20Lengths=custom,NetworkSearchWorkers=workers,
        GuidePrimaryByUniformLoad=guide,Trace=new BeamSearchTrace("uniform guide regression")};
    static TransferNetworkInput Network(bool crossing)=>new TransferNetworkInput {UpperSpacingMm=200,
        CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,
        Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=350,DepthMm=250},new TransferBeamRow {WidthMm=300,DepthMm=200}},
        Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100},new TransferBayRow {WidthMm=1850,ThicknessMm=125}},
        CrossingBeams=crossing ? new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1200,WidthMm=500,DepthMm=175}} : new TransferCrossingBeamRow[0]};
    static void Same(TransferNetworkResult a,TransferNetworkResult b)
    {
        Check(a.Summary==b.Summary && a.Schedule==b.Schedule && a.LayoutDiagnostics==b.LayoutDiagnostics,"layouts, quantities, movements and fallback scope match exactly");
        for(int i=0;i<a.Beams.Length;i++)
        {
            var x=a.Beams[i];var y=b.Beams[i];
            Check(x.PositionsMm.SequenceEqual(y.PositionsMm) && x.Joined.SequenceEqual(y.Joined),"stock supports and braces match exactly");
            Check(x.Transfer.CaseNames.SequenceEqual(y.Transfer.CaseNames),"diagram cases retain their original order");
            Check(x.Transfer.RowsMm.SequenceEqual(y.Transfer.RowsMm) && x.Transfer.TributaryWidthsMm.SequenceEqual(y.Transfer.TributaryWidthsMm),"actual rows and end tributaries remain identical");
            var first=x.Transfer.PrimaryCases.Concat(x.Transfer.LowerCases).SelectMany(c=>c).Concat(x.Transfer.UpperMembers).ToArray();
            var second=y.Transfer.PrimaryCases.Concat(y.Transfer.LowerCases).SelectMany(c=>c).Concat(y.Transfer.UpperMembers).ToArray();
            Check(first.Length==second.Length,"every selected member and loading stage remains checked");
            for(int j=0;j<first.Length;j++)
            {
                var p=first[j];var q=second[j];var u=p.Analysis;var v=q.Analysis;
                Check(p.Name==q.Name && p.LoadCase==q.LoadCase && p.SupportsMm.SequenceEqual(q.SupportsMm),"member identity and bearing geometry remain identical");
                Check(p.Points.Select(z=>z.PositionMm).SequenceEqual(q.Points.Select(z=>z.PositionMm)) && p.Points.Select(z=>z.Kn).SequenceEqual(q.Points.Select(z=>z.Kn)),"no actual point forces are replaced by UDL");
                Check(u.TotalLoadKn==v.TotalLoadKn && u.MomentKnM==v.MomentKnM && u.ShearKn==v.ShearKn && u.DeflectionMm==v.DeflectionMm && u.DeflectionUtilization==v.DeflectionUtilization && u.Reactions.SequenceEqual(v.Reactions),"all unrounded demands and reactions remain exactly identical");
                Check(v.DeflectionEvaluated && q.Utilization<=1+1e-8 && v.Reactions.All(z=>z>=-1e-7),"full actual moment, shear, deflection and bearing checks pass");
            }
            Check(y.Transfer.PrimaryCases.SelectMany(c=>c).All(m=>m.Uniform.Length==0 && m.Points.Length>0),"primary diagrams retain actual concentrated reactions");
        }
    }
    public static void Run()
    {
        foreach(bool custom in new[] {false,true})foreach(bool crossing in new[] {false,true})
        {
            var network=Network(crossing);var plain=Common(false,custom,1);
            var a=TransferNetworkOptimizer.Optimize(plain,network);
            foreach(int workers in new[] {1,4})
            {
                var guided=Common(true,custom,workers);Same(a,TransferNetworkOptimizer.Optimize(guided,network));
                Check(long.Parse(guided.Trace.Snapshot().Split('\n').Single(z=>z.StartsWith("Candidates checked in uniform-load guidance order: ")).Split(':')[1],CultureInfo.InvariantCulture)>0,"guide is exercised in receiving-beam solves");
                network.Beams[1].DepthMm+=25;
                Same(TransferNetworkOptimizer.Optimize(plain,network),TransferNetworkOptimizer.Optimize(guided,network));
                network.Beams[1].DepthMm-=25;
            }
        }
        var fallback=NetworkFallbackTests.Common();fallback.NetworkSearchWorkers=1;fallback.GuidePrimaryByUniformLoad=false;
        var original=TransferNetworkOptimizer.Optimize(fallback,NetworkFallbackTests.Network());
        fallback.GuidePrimaryByUniformLoad=true;Same(original,TransferNetworkOptimizer.Optimize(fallback,NetworkFallbackTests.Network()));
        foreach(bool guide in new[] {false,true})
        {
            var input=Common(guide,true,1);input.LegCapacityKn=.01;input.AllowSeparateSlabFallback=false;
            bool failed=false;try {TransferNetworkOptimizer.Optimize(input,Network(true));}catch(TowerOptimizationException){failed=true;}
            Check(failed,"guidance cannot turn an actual overload into a passing solution");
        }
        using(var source=new CancellationTokenSource())
        {
            source.Cancel();var input=Common(true,true,4);input.Trace=new BeamSearchTrace("uniform guide cancellation",source.Token);
            bool cancelled=false;try {TransferNetworkOptimizer.Optimize(input,Network(true));}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"guidance retains cancellation");
        }
        Console.WriteLine($"Passed {checks} uniform-load guidance checks.");
    }
}
