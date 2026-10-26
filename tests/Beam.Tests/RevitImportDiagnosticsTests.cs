using System;
using System.Globalization;
using EngineeringDashboard;

internal static class RevitImportDiagnosticsTests
{
    private static int checks;
    private static void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
    internal static void Run()
    {
        var source=RevitStructureImportTests.Sample();source.RevitVersion="Revit 2024";source.RevitBuild="fixture-build";source.ActiveView="Level 2";
        source.SelectedCount=7;source.SelectedIds=new[] {"101","102","103","201","301","302","999"};
        source.Elements=new[] {
            new RevitSelectionElement {SourceId="101",Label="Beam A",Category="Structural Framing",FamilyType="Rectangular / 800x1750",ElementClass="FamilyInstance",Status="Read",Details="Geometry: 24 vertices; top levels: 10000"},
            new RevitSelectionElement {SourceId="999",Label="Other [999]",Category="Generic Models",ElementClass="FamilyInstance",Status="Skipped",Details="Unsupported category"}};
        source.Notes=new[] {"Other [999] was skipped"};string original=RevitImportDiagnostics.CaptureSelection(source);
        var result=RevitStructureImport.Build(source,"101");
        string Report(bool canImport,string review="",string exception=null)=>RevitImportDiagnostics.Build(source,original,"101",result,canImport,review,exception,"Cannot import my bay");
        string report=Report(true);
        Check(report.StartsWith("=== REVIT DRAWING IMPORT DIAGNOSTIC REPORT v1 ==="),"report has a distinct import header");
        Check(report.Contains("MVID:") && report.Contains("Runtime:") && report.Contains("Captured UTC:"),"build/runtime identifiers captured");
        Check(report.Contains("Revit 2024") && report.Contains("fixture-build") && report.Contains("Level 2"),"Revit host context captured");
        Check(report.Contains("Selected elements: 7") && report.Contains("999") && report.Contains("Unsupported category"),"skipped selected elements are retained");
        Check(report.Contains("Rectangular / 800x1750") && report.Contains("24 vertices"),"family and geometry evidence retained");
        Check(report.Contains("Chosen main-direction element ID: 101") && report.Contains("Geometry tolerance (mm): 5") && report.Contains("Section and obstruction precision (mm): 1"),"alignment leeway and physical validation precision captured separately");
        Check(report.Contains("Drawing length (mm): 7000") && report.Contains("X1 <- 103"),"mapped canvas geometry is traceable");
        Check(report.Contains("Cannot import my bay") && report.Contains("Remarks:"),"user remarks included");
        source.Beams[0].WidthMm=0;result=RevitStructureImport.Build(source,"101");report=Report(false,"Beam A | Width (mm) | raw text: bad number");
        Check(report.Contains("Preview status: Blocked") && report.Contains("enter a valid width"),"blocking reasons included");
        Check(report.Contains("raw text: bad number"),"invalid review input text included");
        int reviewed=report.IndexOf("--- Current reviewed geometry ---",StringComparison.Ordinal);
        Check(report.Substring(0,reviewed).Contains("width=800") && report.Substring(reviewed).Contains("width=0"),"original read and edited dimensions remain distinct");
        Check(source.Beams[0].WidthMm==0 && source.Slabs[0].ThicknessMm==300,"diagnostics never alters selected geometry");
        result=null;report=Report(false,"",new InvalidOperationException("Preview fixture error").ToString());
        Check(report.Contains("Preview fixture error") && report.Contains("No result was built."),"unexpected preview failure can be diagnosed without a result");
        var empty=new RevitSelectionSnapshot {Problems=new[] {"Revit selection could not be read"},ReadException=new InvalidOperationException("Reader fixture error").ToString()};
        report=RevitImportDiagnostics.Build(empty,null,null,null,false,"",null,"");
        Check(report.Contains("Reader fixture error") && report.Contains("element ID: none"),"read failure/empty selection report available");
        var readerFailure=RevitStructureImport.Build(empty);
        Check(readerFailure.Problems.Length==1 && readerFailure.Problems[0]==empty.Problems[0],"no geometry preserves the reader's actual failure without a missing-beam instruction");
        var readerReport=RevitImportDiagnostics.Build(empty,null,null,readerFailure,false,"",null,"");
        Check(readerReport.Contains("Reader fixture error") && !readerReport.Contains("Select at least one straight"),"the copied blocked report excludes the misleading follow-on geometry error");
        var culture=CultureInfo.CurrentCulture;
        try {CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Check(RevitImportDiagnostics.Number(1.2345)=="1.2345","report numbers are independent of Windows locale");}
        finally {CultureInfo.CurrentCulture=culture;}
        Check(RevitImportDiagnostics.Point(null)=="unavailable" && RevitImportDiagnostics.Number(double.NaN)=="NaN","missing/invalid numbers can be reported");
        Console.WriteLine("Passed "+checks+" Revit import diagnostic checks.");
    }
}
