using System;
using System.Linq;
using System.Diagnostics;
using EngineeringDashboard;

internal static class RevitTopologyTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(a)),message+": "+a+" / "+b);
    private static BeamCalculatorViewModel Model()=>new BeamCalculatorViewModel {UseTowers=true,AllowCustomH20Lengths=false,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=500,FloorHeight=4000};
    internal static void Run()
    {
        var native=RevitImportFixtures.Columns();var columns=RevitStructureImport.Build(native,"523526");
        Check(columns.CanImport,"native 13-element face-edge loops: "+string.Join(";",columns.Problems));
        Check(native.Boundaries.Select(b=>b.Outline.Length).SequenceEqual(new[] {4,6,6,8}),"regression uses actual face-edge vertices, not four-corner hulls");
        Check(native.Boundaries.All(b=>ImportPolygon.StraightCorners(b.Outline).Length==4),"all redundant native vertices simplify to four genuine corners");
        Near(3900.001160229068,columns.Configuration.Beams[0].SpanLength(columns.LengthMm),"accurate native column face limits B1");
        var sample=RevitImportFixtures.Panels();var panels=RevitStructureImport.Build(sample,"523459");
        Check(panels.CanImport,"native eight-element split beams and floors: "+string.Join(";",panels.Problems));
        Check(panels.Configuration.Beams.Length==2 && panels.Configuration.Bays.Length==1 && panels.Configuration.CrossingBeams.Length==1,"two true beam rows, one bay and crossing");
        Check(panels.Configuration.RevitContext.Sources.Count(s=>s.CanvasLabel=="B2")==2,"both connected beam IDs map to one row");
        Check(panels.Configuration.RevitContext.Sources.Count(s=>s.CanvasLabel=="S1")==2,"both floor panels map to one bay");
        Check(panels.Configuration.Bays[0].Outline.Length>=8,"real floor corner cutbacks remain in the merged bay");
        var missing=RevitImportFixtures.Panels();missing.Beams=missing.Beams.Where(b=>b.SourceId!="523488").ToArray();
        Check(!RevitStructureImport.Build(missing,"523459").CanImport,"the missing crossing strip is a real gap, never filled by a hull");
        var different=RevitImportFixtures.Panels();different.Slabs[1].ThicknessMm=300;
        Check(!RevitStructureImport.Build(different,"523459").CanImport,"different slab thicknesses cannot silently share one loading definition");
        var hole=ImportPolygon.OrthogonalUnion(new[] {RevitStructureImportTests.Rect(0,100,0,10),RevitStructureImportTests.Rect(0,100,90,100),RevitStructureImportTests.Rect(0,10,10,90),RevitStructureImportTests.Rect(90,100,10,90)},1);
        Check(hole.Length==0,"polygon union rejects an actual enclosed opening");
        var separate=ImportPolygon.OrthogonalUnion(new[] {RevitStructureImportTests.Rect(0,10,0,10),RevitStructureImportTests.Rect(20,30,0,10)},1);
        Check(separate.Length==0,"polygon union rejects disconnected panels");
        var source=RevitImportFixtures.Areas();var combined=RevitStructureImport.Build(source,"523766");
        Check(combined.CanImport,"native 36-element drawing: "+string.Join(";",combined.Problems));
        Check(combined.Areas.Length==9 && combined.Areas.Count(a=>a.Result.Configuration.Bays.Length>0)==4,"four column-bounded clear areas plus five column-to-column beams");
        Check(combined.Configuration.SupportFormatVersion==4 && combined.Configuration.RevitDrawings.Length==9,"combined drawing is an explicit new saved-file format");
        var allMappings=combined.Areas.SelectMany(a=>a.Result.Configuration.RevitContext.Sources).ToArray();
        Check(allMappings.Select(s=>s.SourceId).Distinct().OrderBy(s=>s).SequenceEqual(source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).OrderBy(s=>s)),"all eighteen beam and eight slab IDs retained without omissions");
        Check(allMappings.GroupBy(s=>s.SourceId).All(g=>g.Count()==1),"each selected concrete member is assigned exactly once");
        Check(combined.Areas.Where(a=>a.Result.Configuration.Bays.Length>0).All(a=>a.Result.Configuration.Beams.Length==2 && a.Result.Configuration.CrossingBeams.Length==1),"column gaps never become long continuous formwork members");
        Check(combined.Areas.Where(a=>a.Result.Configuration.Bays.Length==0).SelectMany(a=>a.SourceIds).OrderBy(id=>id).SequenceEqual(new[] {"523925","523927","523928","523929","523933"}),"column-to-column members retain their own span boundaries");
        var model=Model();combined.Apply(model);
        Check(model.HasCombinedRevitDrawing && model.RevitSceneModels().Length==9,"one document contains every component");
        string saved=model.NetworkConfigurationXml;var restored=Model();restored.BeamLengthMm=model.BeamLengthMm;restored.NetworkConfigurationXml=saved;
        Check(restored.RevitDrawings.Length==9 && restored.RevitSceneModels().All(p=>p.Item2.RevitContext.References.Length==10),"save/load retains the whole combined drawing and all columns");
        string first=model.ActiveRevitDrawingKey,second=model.RevitDrawings.Last().Key;double old=model.BeamSpanLength(0);
        model.SetBeamSpan(0,model.BeamSpanStart(0),old-1);model.ActivateRevitDrawing(second);model.ActivateRevitDrawing(first);
        Near(old-1,model.BeamSpanLength(0),"switching parts preserves active-area geometry edits");
        Check(!model.AllowCustomH20Lengths && model.TowerLegCapacityKn==70 && model.FloorHeight==4000,"switching parts preserves project inputs");
        combined.Apply(model);var clock=Stopwatch.StartNew();model.OptimizeTowersAsync().GetAwaiter().GetResult();clock.Stop();
        Check(model.IsCalculationValid,"all combined drawing components solve: "+model.WarningMessage+" / "+model.TowerFailureDetails);
        Check(model.RevitSceneModels().All(p=>p.Item2.IsCalculationValid),"Calculate all checks every component");
        int legs=0;
        foreach(var part in model.RevitSceneModels())
        {
            var child=part.Item2;var layouts=child.NetworkResult?.Beams??new[] {child.TowerResult};legs+=child.NetworkResult?.LegCount??child.TowerResult.LegCount;
            foreach(var layout in layouts)
            {
                Check(layout.Utilization<=1+1e-6 && layout.MaxLegKn<=child.TowerLegCapacityKn+1e-6,"member and leg capacities pass in "+part.Item1.Label);
                Check(layout.StockPlan.Pieces.All(p=>p.StartMm>=layout.LongitudinalStartMm-1e-6 && p.EndMm<=layout.LongitudinalEndMm+1e-6),"stocks remain inside the column-bounded span");
            }
        }
        Check(legs==model.CombinedLegCount,"combined quantities include every component exactly once");
        int expectedLegs=model.CombinedLegCount;model.ActivateRevitDrawing(second);Check(model.IsCalculationValid && model.TowerResult!=null && model.CombinedLegCount==expectedLegs,"selecting a solved component preserves all results and totals");
        Check(model.BuildDiagnosticReport().Contains("Combined Revit drawing calculations") && model.BuildDiagnosticReport().Contains("523933"),"diagnostics retain every component calculation and source ID");
        Console.WriteLine("Reported 36-element combined import and full calculation: "+clock.Elapsed.TotalSeconds.ToString("0.000")+" s; "+legs+" legs; 9 components.");
        var pair=Model();panels.Apply(pair);var input=pair.CreateTowerInput();Check(input.Network.Beams.Length==2 && input.Network.Bays.Length==1 && input.Network.CrossingBeams.Length==1,"split-beam/floor case reaches the unchanged engineering input factory");
        Console.WriteLine("Passed "+checks+" Revit topology and combined-drawing checks.");
    }
}
