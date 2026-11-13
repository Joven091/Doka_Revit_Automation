using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using EngineeringDashboard;

// Hotspot evidence only. This path omits neighboring parts and coordination;
// its timing can never satisfy full-scenario acceptance.
internal static class ReportedPartDiagnostics
{
    static string Option(string[] args,string name,string fallback) {int i=Array.IndexOf(args,name);return i<0 ? fallback : args[i+1];}
    internal static void Run(string[] args)
    {
        int scenario=int.Parse(Option(args,"--scenario","1"),CultureInfo.InvariantCulture);
        var parts=Option(args,"--parts","1").Split(',').Select(int.Parse).ToArray();
        string output=Option(args,"--output","artifacts/reported-parts"),mode=Option(args,"--mode","worker");
        if(mode!="worker" && mode!="in-process")throw new Exception("Mode must be worker or in-process");
        string worker=Path.GetFullPath(Option(args,"--worker","tools/BeamSolver.Worker/bin/Release/net10.0/Doka.BeamSolver.dll"));
        string dotnet=Option(args,"--dotnet",PortableTestRuntime.ProcessPath);
        bool fixturesOnly=args.Contains("--reported-part-fixtures");
        if(!fixturesOnly && mode=="worker" && (!File.Exists(worker)||!File.Exists(dotnet)))throw new Exception("Build matching worker and provide --dotnet host");
        Directory.CreateDirectory(output);bool failed=false;
        foreach(int index in parts)
        {
            var part=ReportedScenarioTests.DiagnosticPart(scenario,index);var child=part.Item2;
            string prefix=Path.Combine(output,"scenario"+scenario+".part"+index);
            File.WriteAllText(prefix+".inputs.xml",child.NetworkConfigurationXml);
            if(fixturesOnly)
            {
                var input=child.CreateTowerInput();
                if(input.LegCapacityKn!=70 || !input.AllowCustomH20Lengths || input.CustomH20Scope!=CustomH20LengthScope.Both)throw new Exception("Part lost exact project search settings");
                Console.WriteLine("PART_FIXTURE scenario="+scenario+" part="+index+" label="+part.Item1.Label+" network="+(input.Network!=null)+" legCapacity="+input.LegCapacityKn+" custom="+input.CustomH20Scope);
                continue;
            }
            using var session=new BeamSolverSession(mode=="worker" ? worker : prefix+".explicit-in-process",mode=="worker" ? dotnet : null);
            child.SharedSolverSession=session;
            var clock=Stopwatch.StartNew();var task=child.OptimizeTowersAsync();double next=10;
            Console.WriteLine("PART_START scenario="+scenario+" part="+index+" label="+part.Item1.Label+" diagnosticOnly=True no deadline");
            while(!task.IsCompleted)
            {
                Thread.Sleep(100);
                if(clock.Elapsed.TotalSeconds>=next)
                {
                    Console.WriteLine("PART_PROGRESS scenario="+scenario+" part="+index+" seconds="+clock.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" "+child.SearchProgressText);
                    File.WriteAllText(prefix+".running.txt",child.BuildDiagnosticReport("Independent single-part hotspot diagnostic"));next+=10;
                }
            }
            task.GetAwaiter().GetResult();double search=clock.Elapsed.TotalSeconds;string failure=null;
            try {ReportedScenarioTests.ValidatePart(part,mode);}catch(Exception error){failure=error.ToString();failed=true;}
            clock.Stop();File.WriteAllText(prefix+".diagnostics.txt",child.BuildDiagnosticReport("Independent single-part hotspot diagnostic; not full-scenario acceptance"));
            double evidenceSeconds=0;string evidenceFailure=null;var evidenceClock=Stopwatch.StartNew();
            try {evidenceSeconds=ReportedScenarioEvidence.Write(prefix,scenario,mode,child,new[] {part},"SinglePartDiagnostic",clock.Elapsed.TotalSeconds,failure);}
            catch(Exception error){evidenceFailure=error.ToString();failed=true;}
            finally {evidenceSeconds=evidenceClock.Elapsed.TotalSeconds;}
            var result=new {Scenario=scenario,Part=index,part.Item1.Label,SourceSha256=ReportedScenarioTests.FixtureHash(scenario),EngineIdentity=BeamWire.EngineIdentity,Scope="SinglePartDiagnostic",FullScenarioAcceptance=false,Mode=mode,Runtime=Environment.Version.ToString(),SearchSeconds=search,ValidationSeconds=clock.Elapsed.TotalSeconds-search,TotalSeconds=clock.Elapsed.TotalSeconds,EvidenceWriteAndReadValidationSeconds=evidenceSeconds,EvidenceFailure=evidenceFailure,Valid=failure==null,ValidationFailure=failure,Snapshot=ReportedScenarioTests.PartSnapshot(part)};
            File.WriteAllText(prefix+".json",JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));
            Console.WriteLine("PART_RESULT scenario="+scenario+" part="+index+" seconds="+clock.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" valid="+(failure==null)+" fullScenarioAcceptance=False");
            if(failure!=null)Console.WriteLine(failure);
        }
        if(failed)throw new Exception("A reported-part diagnostic failed validation; see JSON.");
    }
}
