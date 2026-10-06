using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EngineeringDashboard;

// Compiled separately against each source tree; no feature switches alter the baseline.
internal static class ColdBenchmarkHost
{
    static TowerInput Common(bool warm)=>new TowerInput {LengthMm=warm ? 4900 : 10600,LegCapacityKn=70,
        StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths),StockEndSetbackMm=150,
        MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true};
    static TransferNetworkInput Network(string scenario)
    {
        if(scenario=="warm")return new TransferNetworkInput {UpperSpacingMm=275,
            Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1450},new TransferBeamRow {WidthMm=650,DepthMm=1150}},
            Bays=new[] {new TransferBayRow {WidthMm=2500,ThicknessMm=300}}};
        return new TransferNetworkInput {UpperSpacingMm=275,
            Beams=Enumerable.Range(0,scenario=="eight" ? 8 : 3).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),
            Bays=(scenario=="eight" ? new[] {2677.0,1874,3276,2274,2278,2272,2252} : new[] {2252.0,2299}).Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray()};
    }
    static string Signature(TransferNetworkResult result)
    {
        var text=new StringBuilder(result.Summary+"\n"+result.Schedule+"\n"+result.LayoutDiagnostics);
        Action<double> add=x=>text.Append('|').Append(BitConverter.DoubleToInt64Bits(x).ToString("X16"));
        foreach(var beam in result.Beams)
        {
            foreach(double value in beam.PositionsMm.Concat(beam.LeftReactions).Concat(beam.RightReactions).Concat(beam.Transfer.RowsMm).Concat(beam.Transfer.TributaryWidthsMm))add(value);
            if(beam.MaxLegKn>70+1e-8 || beam.LeftReactions.Concat(beam.RightReactions).Any(r=>r< -1e-7))throw new Exception("Leg checks failed");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                var a=member.Analysis;
                if(!a.DeflectionEvaluated || member.Utilization>1+1e-8 || a.Reactions.Any(r=>r< -1e-7))throw new Exception("Member checks failed");
                add(member.LengthMm);add(member.StartMm);foreach(double support in member.SupportsMm)add(support);
                add(a.MomentKnM);add(a.ShearKn);add(a.DeflectionMm);add(a.DeflectionUtilization);add(a.TotalLoadKn);
                foreach(double reaction in a.Reactions)add(reaction);
            }
        }
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
    }
    static string Count(BeamSearchTrace trace,string name)=>trace.Snapshot().Split('\n').Single(x=>x.StartsWith(name+": ")).Substring(name.Length+2).Trim();
    static int Main(string[] args)
    {
        try
        {
            Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
            string scenario=args[0],version=args[1];
            TransferNetworkOptimizer.Optimize(Common(true),Network("warm"));
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var common=Common(false);
            using(var source=new CancellationTokenSource(TimeSpan.FromMinutes(6)))
            {
                common.Trace=new BeamSearchTrace("actual source-tree benchmark",source.Token);
                var timer=Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,Network(scenario));timer.Stop();
                Console.WriteLine(FormattableString.Invariant($"ACTUAL scenario={scenario} version={version} seconds={timer.Elapsed.TotalSeconds:0.000} processors={Environment.ProcessorCount} legs={result.LegCount} feet={result.UpperFootCount} timber={result.TimberMm:0.###} B1={result.Beams[0].LegCount} signature={Signature(result)} primary={Count(common.Trace,"Primary member analyses")} lower={Count(common.Trace,"Lower secondary analyses")}"));
            }
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
