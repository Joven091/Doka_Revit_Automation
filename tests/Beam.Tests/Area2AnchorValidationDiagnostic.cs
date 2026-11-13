using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using EngineeringDashboard;

internal static class Area2AnchorValidationDiagnostic
{
    internal static void Run()
    {
        const string output="/workspace/validation/beam-30s-20261103/area2-anchor-fix";
        Directory.CreateDirectory(output);var part=ReportedScenarioTests.DiagnosticPart(1,2);var child=part.Item2;
        File.WriteAllText(Path.Combine(output,"original-automatic.inputs.xml"),child.NetworkConfigurationXml);
        var started=DateTime.UtcNow;Console.WriteLine("AREA2_ANCHOR_FIX_START UTC="+started.ToString("O")+" ORIGINAL_AUTOMATIC=True");
        using var session=new BeamSolverSession(Path.Combine(output,"explicit-in-process"),null);child.SharedSolverSession=session;
        var timer=Stopwatch.StartNew();var task=child.OptimizeTowersAsync();double next=10;
        while(!task.IsCompleted)
        {
            Thread.Sleep(100);
            if(timer.Elapsed.TotalSeconds>=next)
            {
                Console.WriteLine("AREA2_ANCHOR_FIX_PROGRESS seconds="+timer.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" "+child.SearchProgressText);
                File.WriteAllText(Path.Combine(output,"original-automatic.running.txt"),child.BuildDiagnosticReport("Actual original Automatic Area2 after numerical row identity fix"));next+=10;
            }
        }
        task.GetAwaiter().GetResult();Exception failure=null;
        int solidCount=0,solidChecks=0;
        try
        {
            ReportedScenarioTests.ValidatePart(part,"in-process");
            var network=child.NetworkResult??throw new Exception("Original Automatic Area2 has no complete network.");
            var solids=CrossBeamSupportOptimizer.NetworkSolids(network).Concat(network.CrossSupports.SelectMany(c=>c.Solids)).ToArray();
            var concrete=CrossBeamSupportOptimizer.Concrete(network).ToArray();solidCount=solids.Length;
            for(int i=0;i<solids.Length;i++)
            {
                foreach(var block in concrete)
                {
                    solidChecks++;if(solids[i].Conflicts(block))throw new Exception("Final concrete clash: "+solids[i].Name+" / "+block.Name);
                }
                for(int j=0;j<i;j++)
                {
                    solidChecks++;if(solids[i].Conflicts(solids[j]))throw new Exception("Final formwork clash: "+solids[i].Name+" / "+solids[j].Name);
                }
            }
        }
        catch(Exception error){failure=error;}
        timer.Stop();File.WriteAllText(Path.Combine(output,"original-automatic.diagnostics.txt"),child.BuildDiagnosticReport("Actual original Automatic Area2; independent member, load, footprint and all-solids validation"));
        var record=new {StartedUtc=started,CompletedUtc=DateTime.UtcNow,EngineIdentity=BeamWire.EngineIdentity,SourceFixtureSha256=ReportedScenarioTests.FixtureHash(1),
            Scope="Actual original Automatic scenario1 part2; isolated diagnostic, not full combined performance acceptance",Valid=failure==null,Seconds=timer.Elapsed.TotalSeconds,
            Failure=failure?.ToString(),SolidCount=solidCount,SolidChecks=solidChecks,Snapshot=ReportedScenarioTests.PartSnapshot(part)};
        File.WriteAllText(Path.Combine(output,"original-automatic.json"),JsonSerializer.Serialize(record,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("AREA2_ANCHOR_FIX_RESULT valid="+(failure==null)+" seconds="+timer.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture));
        if(failure!=null)throw failure;
    }
}
