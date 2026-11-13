using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

// Exercises the diagnostic boundary without running or interrupting a search.
// Saved child defaults deliberately differ from the actual running search.
internal static class LiveCombinedDiagnosticTests
{
    static readonly BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    static void Set(object target,string field,object value)
    {
        var found=target.GetType().GetField(field,Instance);
        Check(found!=null,"Missing live diagnostic field: "+field);found.SetValue(target,value);
    }
    static void Optimizing(BeamCalculatorViewModel model,bool value)=>typeof(BeamCalculatorViewModel).GetProperty("IsOptimizing").SetValue(model,value);
    static string Part(string report,RevitDrawingDefinition drawing)
    {
        string marker=drawing.Label+" | "+drawing.Key+"\n";
        int start=report.IndexOf(marker,StringComparison.Ordinal);Check(start>=0,"Missing diagnostic part "+drawing.Label);
        start+=marker.Length;int end=report.IndexOf("=== END BEAM FORMWORK DIAGNOSTIC REPORT ===",start,StringComparison.Ordinal);
        Check(end>start,"Missing diagnostic child end");return report.Substring(start,end-start);
    }
    internal static void Run()
    {
        var root=ReportedScenarioTests.FixtureModel(1);
        var saved=(Dictionary<string,BeamCalculatorViewModel>)typeof(BeamCalculatorViewModel).GetField("revitDrawingModels",Instance).GetValue(root);
        var factory=typeof(BeamCalculatorViewModel).GetMethod("DrawingModel",Static);
        var copy=typeof(BeamCalculatorViewModel).GetMethod("CopyProjectInputs",Instance);
        var calculating=root.RevitDrawings.ToDictionary(d=>d.Key,d=>
        {
            var child=(BeamCalculatorViewModel)factory.Invoke(null,new object[] {d});
            copy.Invoke(root,new object[] {child});return child;
        });
        var definition=root.RevitDrawings[1];var running=calculating[definition.Key];
        Check(saved[definition.Key].TowerLegCapacityKn==30 && !saved[definition.Key].AllowCustomH20Lengths,"Test must retain unsolved saved defaults");
        Check(running.TowerLegCapacityKn==70 && running.AllowCustomH20Lengths,"Actual project settings were not propagated");
        var worker=new BeamSearchTrace("Independent live worker fixture");
        worker.Workers(4);worker.RunPass(false,()=> {worker.Work("Independent receiving-beam work");worker.Increment(BeamSearchCounter.PrimaryAnalyses,17);return 0;});
        var host=new BeamSearchTrace("Independent host fixture");host.WorkerStatus(".NET 10 worker running",runtime:true);
        host.WorkerStatus("Checking tower and H20 arrangements");
        string directory=Path.Combine(Path.GetTempPath(),"beam-live-diagnostic-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var read=typeof(BeamSolverSession).GetMethod("ReadLiveDiagnostics",Static);
        Check(read!=null,"Missing optional worker diagnostic reader");
        try
        {
            string path=Path.Combine(directory,"diagnostics.txt");
            // Missing optional files must leave cancellation/runtime/progress intact.
            read.Invoke(null,new object[] {path,host});
            Check(host.ProgressText=="Checking tower and H20 arrangements","Missing diagnostics changed progress");
            File.WriteAllText(path,worker.Snapshot());read.Invoke(null,new object[] {path,host});
            Check(host.ProgressText=="Checking tower and H20 arrangements","Live diagnostics overwrote progress");
            Check(!host.CancellationToken.IsCancellationRequested,"Optional diagnostics cancelled the search");
            Set(running,"beamSearchTrace",host);Optimizing(running,true);Optimizing(root,true);
            Set(root,"calculatingRevitDrawingModels",calculating);Set(root,"runningRevitDrawing",running);Set(root,"runningRevitDrawingKey",definition.Key);
            Set(root,"combinedSearchProgress",definition.Label+" (2/11)");
            string report=root.BuildDiagnosticReport("Independent in-flight regression");string child=Part(report,definition);
            Check(report.Contains("Completed valid parts: 0 / 11"),"Combined progress omitted the active calculation dictionary");
            Check(child.Contains("Search running: True") && child.Contains("TowerLegCapacityKn = 70") && child.Contains("AllowCustomH20Lengths = True"),"Report used the stale saved child");
            Check(!child.Contains("TowerLegCapacityKn = 30"),"Saved default capacity leaked into the running child");
            Check(child.Contains("Solver runtime: .NET 10 worker running"),"Running worker identity missing");
            Check(child.Contains("--- .NET 10 worker diagnostics ---") && child.Contains("Current/last pass: Catalogue lengths") && child.Contains("Primary member analyses: 17") && child.Contains("Network workers: 4"),"Live worker pass/counters missing");
            Check(!child.Contains("No tower search has been started"),"In-flight report incorrectly claims no search");
            Check(saved[definition.Key].TowerLegCapacityKn==30 && !saved[definition.Key].AllowCustomH20Lengths,"Read-only report mutated saved inputs");
            // A coordination retry has its own model, outside the calculation dictionary.
            var retry=(BeamCalculatorViewModel)factory.Invoke(null,new object[] {definition});copy.Invoke(root,new object[] {retry});
            var retryTrace=new BeamSearchTrace("Independent retry fixture");retryTrace.Work("Independent coordination retry");Set(retry,"beamSearchTrace",retryTrace);Optimizing(retry,true);
            Set(root,"runningRevitDrawing",retry);Set(root,"combinedSearchProgress","Coordinating "+definition.Label);
            string retryReport=Part(root.BuildDiagnosticReport(),definition);
            Check(retryReport.Contains("Current/last work: Independent coordination retry"),"Coordination diagnostic used the old part model");
            Check(!retryReport.Contains("Primary member analyses: 17"),"Previous part worker counters leaked into retry");
            Console.WriteLine("PASS running combined child, exact project settings, optional live worker diagnostics, preserved progress and coordination retry identity");
        }
        finally {Optimizing(root,false);Directory.Delete(directory,true);}
    }
}
