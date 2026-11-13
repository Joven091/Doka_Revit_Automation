using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EngineeringDashboard;

internal static class ReportedScenarioEvidenceTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Reported evidence: "+message);}
    static void Reject(Action action,string message)
    {
        bool rejected=false;try {action();}catch(InvalidDataException){rejected=true;}
        Check(rejected,message);
    }
    static void Manifest(string path,ReportedScenarioEvidence.Manifest data)=>File.WriteAllText(path,JsonSerializer.Serialize(data));
    internal static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"beam-reported-evidence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var part=ReportedScenarioTests.DiagnosticPart(1,6);var input=part.Item2.CreateTowerInput();
            var covered=input.CopyForSearch();covered.LengthMm=3800;covered.LongitudinalStartMm=0;
            var layout=TowerOptimizer.Evaluate(covered,new[] {900.0,2900},new[] {true},1524,false);
            Check(layout!=null,"fixed standalone stock should pass without a search");
            double edge=(input.LengthMm-covered.LengthMm)/2;
            layout.LengthMm=input.LengthMm;layout.ConcreteEndOverhangMm=edge;layout.LongitudinalStartMm=input.LongitudinalStartMm;
            layout.PositionsMm=layout.PositionsMm.Select(x=>x+edge+input.LongitudinalStartMm).ToArray();
            foreach(var piece in layout.StockPlan.Pieces)piece.StartMm+=edge+input.LongitudinalStartMm;
            ReportedScenarioEvidence.Apply(part.Item2,new BeamSolverReply {Main=layout},true);
            var wrapper=ReportedScenarioTests.FixtureModel(2);var committed=ReportedScenarioTests.ActualParts(wrapper)[0];
            typeof(BeamCalculatorViewModel).GetMethod("CopyProjectInputs",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(wrapper,new object[] {committed.Item2});
            var alias=new BeamSolverReply {Main=layout};
            ReportedScenarioEvidence.Apply(wrapper,alias,true);ReportedScenarioEvidence.Apply(committed.Item2,alias,true);
            Check(wrapper.SlabStripWidthMm==500 && committed.Item2.SlabStripWidthMm==300,"fixture reproduces intentionally different wrapper and detached child defaults");
            var active=ReportedScenarioTests.ActualParts(wrapper)[0];Check(ReferenceEquals(active.Item2,wrapper),"applied active part is represented by the wrapper during validation");
            Check(ReferenceEquals(ReportedScenarioEvidence.ArchivedChild(wrapper,active),committed.Item2),"archive uses committed child inputs for the identical applied result graph");
            ReportedScenarioEvidence.Apply(wrapper,new BeamSolverReply(),true);
            Reject(()=>ReportedScenarioEvidence.ArchivedChild(wrapper,active),"a changed wrapper result must not silently archive stale child evidence");
            string prefix=Path.Combine(directory,"scenario1.part6");
            ReportedScenarioEvidence.Write(prefix,1,"in-process",part.Item2,new[] {part},"SinglePartDiagnostic",.125,null);
            string path=prefix+".evidence.json";var verified=ReportedScenarioEvidence.Verify(path);
            Check(verified.Valid && verified.Parts.Length==1 && verified.OriginalScope=="SinglePartDiagnostic" && !verified.FullScenarioAcceptance && verified.OriginalTotalSeconds==.125,"read-back validation and scope preserve actual result evidence");
            var metadata=JsonSerializer.Deserialize<ReportedScenarioEvidence.Manifest>(File.ReadAllText(path));
            string replyPath=Path.Combine(directory,metadata.Parts[0].Reply);var bytes=File.ReadAllBytes(replyPath);
            bytes[bytes.Length-1]^=1;File.WriteAllBytes(replyPath,bytes);
            Reject(()=>ReportedScenarioEvidence.Verify(path),"a corrupt reply must fail its checksum");
            bytes[bytes.Length-1]^=1;File.WriteAllBytes(replyPath,bytes);
            string identity=metadata.EngineIdentity;metadata.EngineIdentity="different-engine";Manifest(path,metadata);
            Reject(()=>ReportedScenarioEvidence.Verify(path),"a different engine must never silently deserialize");metadata.EngineIdentity=identity;
            string source=metadata.SourceSha256;metadata.SourceSha256="different-report";Manifest(path,metadata);
            Reject(()=>ReportedScenarioEvidence.Verify(path),"a different report fixture must fail");metadata.SourceSha256=source;Manifest(path,metadata);
            string checksum=metadata.Parts[0].ReplySha256;metadata.Parts[0].ReplySha256=null;Manifest(path,metadata);
            Reject(()=>ReportedScenarioEvidence.Verify(path),"missing checksums must not disable reply integrity");metadata.Parts[0].ReplySha256=checksum;Manifest(path,metadata);
            var reply=BeamWire.Read<BeamSolverReply>(replyPath);reply.Main.StockPlan.Pieces[0].MomentKnM*=.5;
            File.Delete(replyPath);BeamWire.Write(replyPath,reply);metadata.Parts[0].ReplySha256=ReportedScenarioEvidence.Hash(replyPath);Manifest(path,metadata);
            Check(!ReportedScenarioEvidence.Verify(path).Valid,"checksum-correct numerical corruption must fail fresh engineering reanalysis");
            // Preserve a failed network input/result without pretending it is a solution.
            var failed=ReportedScenarioTests.DiagnosticPart(1,2);
            ReportedScenarioEvidence.Apply(failed.Item2,new BeamSolverReply(),false,"recorded no solution","bounded search failed");
            string failurePrefix=Path.Combine(directory,"scenario1.part2");
            ReportedScenarioEvidence.Write(failurePrefix,1,"in-process",failed.Item2,new[] {failed},"SinglePartDiagnostic",4,"recorded no solution");
            Check(!ReportedScenarioEvidence.Verify(failurePrefix+".evidence.json").Valid,"failed network children retain failure status after serialization");
            var full=ReportedScenarioTests.FixtureModel(2);var fullParts=ReportedScenarioTests.ActualParts(full);
            foreach(var item in fullParts)
            {
                typeof(BeamCalculatorViewModel).GetMethod("CopyProjectInputs",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(full,new object[] {item.Item2});
                ReportedScenarioEvidence.Apply(item.Item2,new BeamSolverReply(),false,"recorded no solution","bounded search failed");
            }
            string fullPrefix=Path.Combine(directory,"scenario2");
            ReportedScenarioEvidence.Write(fullPrefix,2,"in-process",full,fullParts,"FullCombinedScenario",5,"recorded full failure");
            var all=ReportedScenarioEvidence.Verify(fullPrefix+".evidence.json");
            Check(!all.Valid && all.Parts.Length==3 && all.OriginalScope=="FullCombinedScenario","full evidence retains every failed/unsolved calculation part");
            var complete=JsonSerializer.Deserialize<ReportedScenarioEvidence.Manifest>(File.ReadAllText(fullPrefix+".evidence.json"));
            complete.Parts=complete.Parts.Take(2).ToArray();Manifest(fullPrefix+".evidence.json",complete);
            Reject(()=>ReportedScenarioEvidence.Verify(fullPrefix+".evidence.json"),"a full-scenario manifest may not omit an unsolved child");
            Console.WriteLine("PASS reported evidence: exact fixture/request/source identity, completed and failed result graphs, checksum guards and independent engineering reanalysis without another search");
        }
        finally {Directory.Delete(directory,true);}
    }
}
