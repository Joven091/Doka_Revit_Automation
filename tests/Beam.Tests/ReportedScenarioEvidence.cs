using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EngineeringDashboard;

// Final applied result graphs, using only the existing bounded BeamWire model
// set. Inputs are reconstructed from completed children; coordination obstacles
// have already been cleared, so this is evidence for validation, not a search replay.
internal static class ReportedScenarioEvidence
{
    internal sealed class Part
    {
        public int Index {get;set;} public string Key {get;set;} public string Label {get;set;}
        public bool IsCalculationValid {get;set;} public string Warning {get;set;} public string FailureDetails {get;set;}
        public string Inputs {get;set;} public string InputXml {get;set;} public string InputXmlSha256 {get;set;}
        public string Request {get;set;} public string RequestSha256 {get;set;} public string Reply {get;set;} public string ReplySha256 {get;set;}
    }
    internal sealed class Manifest
    {
        public int Format {get;set;}=1; public int Scenario {get;set;}
        public string SourceSha256 {get;set;} public string EngineIdentity {get;set;} public string Scope {get;set;} public string Mode {get;set;}
        public string PayloadOrigin {get;set;}="Final applied child graphs; reconstructed post-search inputs";
        public bool IsCalculationValid {get;set;} public string Warning {get;set;} public string FailureDetails {get;set;}
        public double OriginalTotalSeconds {get;set;} public string OriginalValidationFailure {get;set;}
        public string EngineSources {get;set;} public string HarnessSha256 {get;set;} public Part[] Parts {get;set;}
    }
    internal sealed class Verification
    {
        public int Scenario {get;set;} public string EngineIdentity {get;set;} public string OriginalScope {get;set;}
        public string Scope {get;set;}="SavedResultRevalidation";
        public bool FullScenarioAcceptance {get;set;}=false;
        public bool Valid {get;set;} public string ValidationFailure {get;set;} public object[] Parts {get;set;}
        public double OriginalTotalSeconds {get;set;} public double ReadAndValidationSeconds {get;set;}
    }
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions {WriteIndented=true};
    static readonly BindingFlags Instance=BindingFlags.NonPublic|BindingFlags.Instance;
    static string Inputs(BeamCalculatorViewModel model)=>(string)typeof(BeamCalculatorViewModel).GetMethod("CaptureDiagnosticInputs",Instance).Invoke(model,null);
    static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
    internal static string Hash(string path){using var stream=File.OpenRead(path);using var algorithm=SHA256.Create();return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");}
    static void Set(BeamCalculatorViewModel model,string property,object value)=>typeof(BeamCalculatorViewModel).GetProperty(property).SetValue(model,value);
    internal static void Apply(BeamCalculatorViewModel model,BeamSolverReply reply,bool valid,string warning="",string failure="")
    {
        Set(model,"TowerResult",reply.Main);Set(model,"NetworkResult",reply.Network);Set(model,"IndependentBeamResults",reply.Independent);
        Set(model,"IsCalculationValid",valid);Set(model,"WarningMessage",warning);Set(model,"TowerFailureDetails",failure);
    }
    internal static BeamCalculatorViewModel ArchivedChild(BeamCalculatorViewModel root,Tuple<RevitDrawingDefinition,BeamCalculatorViewModel> part)
    {
        if(!ReferenceEquals(root,part.Item2) || root.RevitDrawings.Length<2)return part.Item2;
        var committed=(Dictionary<string,BeamCalculatorViewModel>)typeof(BeamCalculatorViewModel).GetField("revitDrawingModels",Instance).GetValue(root);
        Check(committed.TryGetValue(part.Item1.Key,out var child),"Active evidence part has no committed child");
        Check(ReferenceEquals(root.NetworkResult,child.NetworkResult) && ReferenceEquals(root.TowerResult,child.TowerResult)
            && ReferenceEquals(root.IndependentBeamResults,child.IndependentBeamResults) && root.IsCalculationValid==child.IsCalculationValid,
            "Active wrapper differs from its committed child result");
        // CopyProjectInputs deliberately leaves some wrapper display defaults
        // out of detached children. Archive the actual child's input snapshot.
        return child;
    }
    static void Wire(string path,object value)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {BeamWire.Write(temp,value);PortableTestRuntime.MoveOverwrite(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static void CheckReconstructedRequest(string path,BeamCalculatorViewModel child,string expectedHash)
    {
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".fixture.tmp";
        try
        {
            BeamWire.Write(temporary,new BeamSolverRequest {Main=child.CreateTowerInput()});
            Check(Hash(temporary)==expectedHash,"Saved request differs from reconstructed fixture inputs");
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    static string Artifact(string manifestPath,string relative,string checksum=null)
    {
        string folder=Path.GetFullPath(Path.GetDirectoryName(manifestPath))+Path.DirectorySeparatorChar;
        string path=Path.GetFullPath(Path.Combine(folder,relative));
        Check(path.StartsWith(folder,StringComparison.Ordinal),"Evidence path leaves its artifact directory");
        if(checksum!=null)Check(Hash(path)==checksum,"Evidence checksum mismatch: "+relative);
        return path;
    }
    internal static double Write(string prefix,int scenario,string mode,BeamCalculatorViewModel root,Tuple<RevitDrawingDefinition,BeamCalculatorViewModel>[] parts,string scope,double totalSeconds,string validationFailure)
    {
        var timer=Stopwatch.StartNew();prefix=Path.GetFullPath(prefix);
        string manifestPath=prefix+".evidence.json",relative=Path.GetFileName(prefix)+".evidence",folder=Path.Combine(Path.GetDirectoryName(prefix),relative);
        Directory.CreateDirectory(folder);
        var definitions=ReportedScenarioTests.FixtureModel(scenario).RevitDrawings;
        var entries=new List<Part>();
        foreach(var item in parts)
        {
            var child=ArchivedChild(root,item);int index=Array.FindIndex(definitions,d=>d.Key==item.Item1.Key)+1;Check(index>0,"Unknown evidence calculation part");
            Check(child.IndependentBeams.Count==0,"Reported fixture unexpectedly gained independent beams");
            string stem=relative+"/part"+index;
            var entry=new Part {Index=index,Key=item.Item1.Key,Label=item.Item1.Label,IsCalculationValid=child.IsCalculationValid,Warning=child.WarningMessage,FailureDetails=child.TowerFailureDetails,
                Inputs=Inputs(child),InputXml=stem+".inputs.xml",Request=stem+".request.bin",Reply=stem+".reply.bin"};
            File.WriteAllText(Artifact(manifestPath,entry.InputXml),child.NetworkConfigurationXml);
            Wire(Artifact(manifestPath,entry.Request),new BeamSolverRequest {Main=child.CreateTowerInput()});
            var trace=(BeamSearchTrace)typeof(BeamCalculatorViewModel).GetField("beamSearchTrace",Instance).GetValue(child);
            Wire(Artifact(manifestPath,entry.Reply),new BeamSolverReply {Main=child.NetworkResult?.Beams[0]??child.TowerResult,Network=child.NetworkResult,Independent=child.IndependentBeamResults,
                Diagnostics=child.BuildDiagnosticReport(),Counters=trace?.ExportWorkerCounters()??new long[Enum.GetValues(typeof(BeamSearchCounter)).Length],
                Error=child.IsCalculationValid ? null : child.WarningMessage,ErrorDetails=child.IsCalculationValid ? null : child.TowerFailureDetails,ErrorKind=child.IsCalculationValid ? null : "recorded-failure"});
            entry.InputXmlSha256=Hash(Artifact(manifestPath,entry.InputXml));entry.RequestSha256=Hash(Artifact(manifestPath,entry.Request));entry.ReplySha256=Hash(Artifact(manifestPath,entry.Reply));entries.Add(entry);
        }
        string sources=relative+"/engine.sources.txt";
        using(var stream=typeof(BeamWire).Assembly.GetManifestResourceStream("EngineeringDashboard.BeamSolver.SourceHashes"))
        using(var file=File.Create(Artifact(manifestPath,sources)))stream.CopyTo(file);
        var manifest=new Manifest {Scenario=scenario,SourceSha256=ReportedScenarioTests.FixtureHash(scenario),EngineIdentity=BeamWire.EngineIdentity,Scope=scope,Mode=mode,
            IsCalculationValid=root.IsCalculationValid,Warning=root.WarningMessage,FailureDetails=root.TowerFailureDetails,OriginalTotalSeconds=totalSeconds,OriginalValidationFailure=validationFailure,
            EngineSources=sources,HarnessSha256=Hash(typeof(ReportedScenarioTests).Assembly.Location),Parts=entries.ToArray()};
        File.WriteAllText(manifestPath,JsonSerializer.Serialize(manifest,Json));
        var verified=Verify(manifestPath);
        Check(validationFailure!=null || verified.Valid,"Saved evidence failed read-back validation: "+verified.ValidationFailure);
        timer.Stop();return timer.Elapsed.TotalSeconds;
    }
    internal static Verification Verify(string manifestPath)
    {
        var timer=Stopwatch.StartNew();manifestPath=Path.GetFullPath(manifestPath);
        var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath),Json);
        Check(manifest.Format==1 && manifest.EngineIdentity==BeamWire.EngineIdentity,"Evidence format/source identity mismatch; use matching production sources");
        Check(manifest.SourceSha256==ReportedScenarioTests.FixtureHash(manifest.Scenario),"Evidence fixture report checksum mismatch");
        Check(manifest.Mode=="worker" || manifest.Mode=="in-process","Unknown recorded solver mode");
        Check(manifest.Scope=="FullCombinedScenario" || manifest.Scope=="SinglePartDiagnostic","Unknown original evidence scope");
        Artifact(manifestPath,manifest.EngineSources,manifest.EngineIdentity);
        var root=ReportedScenarioTests.FixtureModel(manifest.Scenario);var parts=ReportedScenarioTests.ActualParts(root);
        Check(manifest.Parts!=null && manifest.Parts.Length>0 && manifest.Parts.Select(p=>p.Index).Distinct().Count()==manifest.Parts.Length,"Duplicate or missing evidence parts");
        if(manifest.Scope=="FullCombinedScenario")Check(manifest.Parts.Select(p=>p.Index).SequenceEqual(Enumerable.Range(1,parts.Length)),"Incomplete full-scenario evidence");
        else Check(manifest.Parts.Length==1,"Single-part evidence contains multiple parts");
        var failures=new List<string>();var snapshots=new List<object>();BeamSolverReply active=null;
        foreach(var entry in manifest.Parts)
        {
            Check(entry.Index>=1 && entry.Index<=parts.Length,"Evidence part index out of range");
            Check(new[] {entry.InputXmlSha256,entry.RequestSha256,entry.ReplySha256}.All(value=>value!=null && value.Length==64),"Evidence file checksums are incomplete");
            var part=parts[entry.Index-1];var child=part.Item2;
            typeof(BeamCalculatorViewModel).GetMethod("CopyProjectInputs",Instance).Invoke(root,new object[] {child});
            Check(entry.Key==part.Item1.Key && entry.Label==part.Item1.Label,"Evidence part identity differs from report fixture");
            string xml=File.ReadAllText(Artifact(manifestPath,entry.InputXml,entry.InputXmlSha256));
            Check(xml==child.NetworkConfigurationXml && entry.Inputs==Inputs(child),"Saved child inputs differ from reconstructed report fixture");
            var request=BeamWire.Read<BeamSolverRequest>(Artifact(manifestPath,entry.Request,entry.RequestSha256));
            CheckReconstructedRequest(Artifact(manifestPath,entry.Request),child,entry.RequestSha256);
            var reply=BeamWire.Read<BeamSolverReply>(Artifact(manifestPath,entry.Reply,entry.ReplySha256));
            if(entry.IsCalculationValid){Check(reply.Error==null,"Completed evidence contains a worker error");BeamSolverSession.Validate(reply,request);}
            else Check(reply.Error!=null,"Failed child evidence lost its recorded error");
            Apply(child,reply,entry.IsCalculationValid,entry.Warning,entry.FailureDetails);
            if(part.Item1.Key==root.ActiveRevitDrawingKey)active=reply;
            try {ReportedScenarioTests.ValidatePart(part,manifest.Mode,reply.Diagnostics);}catch(Exception error){failures.Add(entry.Label+": "+error);}
            snapshots.Add(ReportedScenarioTests.PartSnapshot(part));
        }
        if(manifest.Scope=="FullCombinedScenario")
        {
            Check(active!=null,"Evidence lost the active calculation part");
            Apply(root,active,manifest.IsCalculationValid,manifest.Warning,manifest.FailureDetails);
            if(!manifest.IsCalculationValid)failures.Add("Original combined result rejected: "+manifest.Warning+"\n"+manifest.FailureDetails);
            string clash=root.CombinedRevitClash();if(clash!=null)failures.Add("Saved combined clash: "+clash);
        }
        timer.Stop();return new Verification {Scenario=manifest.Scenario,EngineIdentity=manifest.EngineIdentity,OriginalScope=manifest.Scope,Valid=failures.Count==0,ValidationFailure=failures.Count==0 ? null : string.Join("\n\n",failures),
            OriginalTotalSeconds=manifest.OriginalTotalSeconds,ReadAndValidationSeconds=timer.Elapsed.TotalSeconds,Parts=snapshots.ToArray()};
    }
    internal static void Run(string[] args)
    {
        int at=Array.IndexOf(args,"--input");Check(at>=0 && at+1<args.Length,"Provide --input path/to/scenarioN.evidence.json");
        string path=Path.GetFullPath(args[at+1]);var result=Verify(path);
        File.WriteAllText(path+".revalidated.json",JsonSerializer.Serialize(result,Json));
        Console.WriteLine("EVIDENCE_REVALIDATED scenario="+result.Scenario+" valid="+result.Valid+" originalScope="+result.OriginalScope+" fullScenarioAcceptance=False");
        Check(result.Valid,result.ValidationFailure??"Saved-result verification failed");
    }
}
