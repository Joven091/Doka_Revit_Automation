using System;
using System.Linq;
using EngineeringDashboard;

internal static class RevitAlignmentTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    internal static void Run()
    {
        Check(RevitStructureImport.ToleranceMm==5 && RevitStructureImport.PrecisionMm==1,"fixed 5 mm alignment tolerance preserves section and obstruction precision");
        foreach(double gap in new[] {0.5,4.999,5,5.001,6,300})
        {
            var sample=RevitStructureImportTests.Sample();sample.Beams[2].Start.Y=400+gap;
            var result=RevitStructureImport.Build(sample,"101");
            Check(result.CanImport==(gap<=5),"crossing-to-beam alignment gap: "+gap+" mm");
            if(result.CanImport)Check(result.Configuration.CrossingBeams[0].WidthMm==650 && result.Configuration.Beams[0].DepthMm==1750 && result.Configuration.Bays[0].ThicknessMm==300,"alignment recognition preserves section sizes");
        }
        foreach(double skew in new[] {1.6,5,5.01,10})
        {
            var outline=new[] {new ImportPoint(0,0),new ImportPoint(2000,0),new ImportPoint(2000,900),new ImportPoint(1950,900),
                new ImportPoint(1950,1000),new ImportPoint(50,1000),new ImportPoint(50,900),new ImportPoint(0,900+skew)};
            var merged=ImportPolygon.OrthogonalUnion(new[] {outline},RevitStructureImport.ToleranceMm);
            Check((merged.Length>0)==(skew<=5),"corner alignment adjustment is bounded to 5 mm: "+skew);
            if(merged.Length>0)Check(merged.Length==8 && SlabFootprint.ValidEndCutbacks(merged,488),"real 50 mm corner cuts remain in the adjusted outline");
        }
        var source=RevitAlignmentFixtures.AlignedAreas();var combined=RevitStructureImport.Build(source,"523732");
        Check(source.Beams.Length==23 && source.Slabs.Length==12 && source.Boundaries.Length==12,"exact 48-element report fixture retains every structural element");
        Check(combined.CanImport,"reported 1.695 mm offsets and skewed slab corners import: "+string.Join(";",combined.Problems));
        Check(combined.Areas.Length==11 && combined.Areas.Count(a=>a.Result.Configuration.Bays.Length>0)==5,"five clear areas and six column beams share one combined drawing");
        var mappings=combined.Areas.SelectMany(a=>a.Result.Configuration.RevitContext.Sources).ToArray();
        Check(mappings.Length==35 && mappings.GroupBy(m=>m.SourceId).All(g=>g.Count()==1)
            && mappings.Select(m=>m.SourceId).OrderBy(id=>id).SequenceEqual(source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).OrderBy(id=>id)),"all beam and slab sources mapped exactly once");
        foreach(var part in combined.Areas)
        {
            var config=part.Result.Configuration;
            foreach(var map in config.RevitContext.Sources.Where(s=>s.CanvasLabel.StartsWith("B",StringComparison.Ordinal)))
            {
                int index=int.Parse(map.CanvasLabel.Substring(1))-1;var input=source.Beams.Single(b=>b.SourceId==map.SourceId);var output=config.Beams[index];
                Check(output.WidthMm==input.WidthMm && output.DepthMm==input.DepthMm && Math.Abs(output.BottomElevationMm+config.RevitContext.TopElevationMm-input.BottomMm)<1e-7,"original beam section retained: "+map.SourceId);
            }
            foreach(var map in config.RevitContext.Sources.Where(s=>s.CanvasLabel.StartsWith("S",StringComparison.Ordinal)))
                Check(config.Bays[int.Parse(map.CanvasLabel.Substring(1))-1].ThicknessMm==source.Slabs.Single(s=>s.SourceId==map.SourceId).ThicknessMm,"original slab thickness retained: "+map.SourceId);
        }
        var model=new BeamCalculatorViewModel {UseTowers=true,TowerLegCapacityKn=50,AllowCustomH20Lengths=false};combined.Apply(model);
        string xml=model.NetworkConfigurationXml;var restored=new BeamCalculatorViewModel {UseTowers=true,BeamLengthMm=model.BeamLengthMm};restored.NetworkConfigurationXml=xml;
        Check(model.RevitSceneModels().Length==11 && restored.RevitSceneModels().Length==11 && !model.AllowCustomH20Lengths && model.TowerLegCapacityKn==50,"combined canvas, save/load and project settings survive alignment repair");
        var disjoint=RevitAlignmentFixtures.AlignedAreas();var otherSpan=disjoint.Beams.Single(b=>b.SourceId=="523771");otherSpan.Start.X+=20;otherSpan.End.X+=20;
        Check(RevitStructureImport.Build(disjoint,"523732").CanImport,"a genuinely offset beam in another longitudinal span cannot block neighbouring supports here");
        var changedThickness=RevitAlignmentFixtures.AlignedAreas();changedThickness.Slabs.Single(s=>s.SourceId=="583621").ThicknessMm+=3;
        Check(!RevitStructureImport.Build(changedThickness,"523732").CanImport,"5 mm positional leeway cannot merge different slab thicknesses");
        var newReport=RevitAlignmentFixtures.ColumnTerminations();var perimeter=RevitStructureImport.Build(newReport,"523456");
        Check(newReport.Beams.Length==41 && newReport.Slabs.Length==23 && newReport.Boundaries.Length==12,"exact 76-element report fixture");
        Check(perimeter.CanImport && perimeter.Areas.Length==12,"column-supported members now import with actual connections beyond alignment tolerance");
        var edge=newReport.Beams.Single(b=>b.SourceId=="523745");var column=newReport.Boundaries.Single(b=>b.SourceId=="523381");var row=newReport.Beams.Single(b=>b.SourceId=="523864");
        Check(Math.Abs(Math.Min(edge.Start.Y,edge.End.Y)-column.Outline.Max(p=>p.Y))<1,"reported beam really stops at its column face");
        Check(Math.Min(edge.Start.Y,edge.End.Y)-(row.Start.Y+row.WidthMm/2)>299,"column face is about 300 mm from the row face, not a 5 mm placement error");
        Console.WriteLine("Passed "+checks+" Revit alignment and diagnostic replay checks. Previous report: 11 combined parts; 76-element report: 12 combined parts.");
    }
}
