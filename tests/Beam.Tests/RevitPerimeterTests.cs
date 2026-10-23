using System;
using System.Linq;
using EngineeringDashboard;

internal static class RevitPerimeterTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static BeamCalculatorViewModel Model()=>new BeamCalculatorViewModel {UseTowers=true,TowerLegCapacityKn=70,AllowCustomH20Lengths=false,MaximumConcreteEndOverhangMm=500,FloorHeight=4000};
    private static double Length(RevitSelectedBeam b)=>Math.Sqrt(Math.Pow(b.End.X-b.Start.X,2)+Math.Pow(b.End.Y-b.Start.Y,2));
    internal static void Run()
    {
        var source=RevitAlignmentFixtures.ColumnTerminations();string original=RevitImportDiagnostics.CaptureSelection(source);
        var result=RevitStructureImport.Build(source,"523456");
        Check(result.CanImport,"full 76-element report imports: "+string.Join(";",result.Problems));
        Check(result.Areas.Length==12 && result.Areas.Count(a=>a.Result.Configuration.Bays.Length>0)==2,"two connected calculation strips and ten column-supported members");
        var memberIds=new[] {"523745","523749","523777","523780","523806","523814","523820","523822","523828","523832"};
        var standalone=result.Areas.Where(a=>a.Result.Configuration.Beams.Length==1).ToArray();
        Check(standalone.SelectMany(a=>a.SourceIds).OrderBy(id=>id).SequenceEqual(memberIds.OrderBy(id=>id)),"all ten reported members retain their column-to-column calculations");
        var columnBeam=Model();standalone.Single(a=>a.SourceIds[0]=="523745").Result.Apply(columnBeam);columnBeam.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(columnBeam.IsCalculationValid && columnBeam.TowerResult.Utilization<=1+1e-7 && columnBeam.TowerResult.MaxLegKn<=70+1e-7,"523745 retains a complete checked member and tower design after import");
        var actualIds=result.Areas.SelectMany(a=>a.SourceIds).Distinct().OrderBy(id=>id);
        Check(actualIds.SequenceEqual(source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).OrderBy(id=>id)),"all 41 beam and 23 slab IDs remain on one canvas");
        foreach(var area in result.Areas)
        {
            var model=Model();area.Result.Apply(model);var input=model.CreateTowerInput(false);
            Check(input!=null,"unchanged numerical input validation passes for "+area.Label);
            Check(model.RevitContext.References.Length==12,"every calculation part retains the original column references");
            if(area.Result.Configuration.Bays.Length==0)
            {
                var expected=source.Beams.Single(b=>b.SourceId==area.SourceIds[0]);var row=area.Result.Configuration.Beams.Single();
                Check(row.WidthMm==expected.WidthMm && row.DepthMm==expected.DepthMm && Math.Abs(row.BottomElevationMm+model.RevitContext.TopElevationMm-expected.BottomMm)<1e-7,"column member section and elevation retained: "+expected.SourceId);
                if(expected.SourceId=="523745")Check(Math.Abs(row.SpanLength(area.Result.LengthMm)-13600)<.001,"523745 ends exactly at the two column faces");
            }
        }
        var networks=result.Areas.Where(a=>a.Result.Configuration.Bays.Length>0).ToArray();
        Check(networks.Select(a=>a.Result.Configuration.Beams.Length).OrderBy(n=>n).SequenceEqual(new[] {13,14}),"short T-connected row exists only on its actual side");
        bool covers=false;
        foreach(var area in networks)
        {
            var model=Model();area.Result.Apply(model);var config=area.Result.Configuration;var c=config.RevitContext;
            double wx=1000-c.OriginXmm,wy=-4526.095970535279-c.OriginYmm;
            double x=wx*c.DirectionX+wy*c.DirectionY,y=-wx*c.DirectionY+wy*c.DirectionX;var centres=model.GeometryCentres();
            for(int i=0;i<config.Bays.Length;i++)
            {
                double y0=centres[i]+config.Beams[i].WidthMm/2;
                if(y>=y0 && y<=y0+config.Bays[i].WidthMm && SlabFootprint.Contains(config.Bays[i].Outline,x,y-y0))covers=true;
            }
        }
        Check(covers,"continuous slab beyond the T beam includes the strip that would be lost by an imaginary full-width beam row");
        var pieces=result.Areas.SelectMany(a=>a.Geometry.Beams).ToArray();
        foreach(var beam in source.Beams)
        {
            var segments=pieces.Where(b=>b.SourceId==beam.SourceId).ToArray();
            Check(segments.Length>0 && segments.All(b=>b.WidthMm==beam.WidthMm && b.DepthMm==beam.DepthMm && b.BottomMm==beam.BottomMm),"source beam sizes are never substituted or dropped: "+beam.SourceId);
            Check(Math.Abs(segments.Sum(Length)-Length(beam))<=2*RevitStructureImport.ToleranceMm+1e-6,"source beam length conserved within bounded endpoint alignment: "+beam.SourceId);
            double dx=(beam.End.X-beam.Start.X)/Length(beam),dy=(beam.End.Y-beam.Start.Y)/Length(beam);
            var ranges=segments.Select(b=>new[] {(b.Start.X-beam.Start.X)*dx+(b.Start.Y-beam.Start.Y)*dy,(b.End.X-beam.Start.X)*dx+(b.End.Y-beam.Start.Y)*dy}).Select(r=>new[] {r.Min(),r.Max()}).OrderBy(r=>r[0]).ToArray();
            Check(ranges.Zip(ranges.Skip(1),(a,b)=>a[1]<=b[0]+1e-6).All(v=>v),"calculation pieces never duplicate a beam interval: "+beam.SourceId);
        }
        var panels=result.Areas.SelectMany(a=>a.Geometry.Slabs).ToArray();
        foreach(var slab in source.Slabs)
        {
            var segments=panels.Where(s=>s.SourceId==slab.SourceId).ToArray();
            Check(segments.Length>0 && segments.All(s=>s.ThicknessMm==slab.ThicknessMm && s.TopMm==slab.TopMm),"all floor sections retained: "+slab.SourceId);
            double difference=Math.Abs(segments.Sum(s=>ImportPolygon.Area(s.Outline))-ImportPolygon.Area(slab.Outline));
            Check(difference<=ImportPolygon.Perimeter(slab.Outline)*RevitStructureImport.ToleranceMm+1,"source slab area is conserved within 5 mm outline alignment: "+slab.SourceId);
            var ordered=segments.OrderBy(s=>s.Outline.Min(p=>p.X)).ToArray();
            Check(ordered.Zip(ordered.Skip(1),(a,b)=>a.Outline.Max(p=>p.X)<=b.Outline.Min(p=>p.X)+1e-6).All(v=>v),"split floor patches do not overlap: "+slab.SourceId);
        }
        Check(original==RevitImportDiagnostics.CaptureSelection(source),"import never edits the captured Revit source geometry");
        var combined=Model();result.Apply(combined);string saved=combined.NetworkConfigurationXml;var copy=Model();copy.BeamLengthMm=combined.BeamLengthMm;copy.NetworkConfigurationXml=saved;
        Check(copy.RevitSceneModels().Length==12 && copy.RevitSceneModels().All(p=>p.Item2.RevitContext.References.Length==12),"save/load retains all network and column-member parts");
        foreach(var part in copy.RevitDrawings)copy.ActivateRevitDrawing(part.Key);
        Check(copy.RevitDrawings.Length==12 && copy.TowerLegCapacityKn==70 && !copy.AllowCustomH20Lengths,"selection across all parts preserves geometry and project settings");
        var reverseAxis=RevitStructureImport.Build(RevitAlignmentFixtures.ColumnTerminations(),"523745");
        Check(reverseAxis.CanImport && reverseAxis.Areas.Length==12,"column-supported classification is independent of which beam first defines the main direction");
        var missingColumn=RevitAlignmentFixtures.ColumnTerminations();missingColumn.Boundaries=missingColumn.Boundaries.Where(c=>c.SourceId!="523381").ToArray();
        Check(!RevitStructureImport.Build(missingColumn,"523456").CanImport,"a missing end column cannot be invented");
        var incomplete=RevitAlignmentFixtures.ColumnTerminations();incomplete.Beams.Single(b=>b.SourceId=="523745").End.Y+=300;
        Check(!RevitStructureImport.Build(incomplete,"523456").CanImport,"a real unsupported 300 mm end gap remains blocked");
        var bad=RevitAlignmentFixtures.ColumnTerminations();bad.Slabs[0].Problem="Slab openings are unsupported";
        Check(!RevitStructureImport.Build(bad,"523456").CanImport,"column handling cannot hide an unsupported slab opening");
        foreach(bool reverse in new[] {false,true})
        {
            var rotated=RevitAlignmentFixtures.ColumnTerminations();double angle=.6;Func<ImportPoint,ImportPoint> rotate=p=>new ImportPoint(100000+p.X*Math.Cos(angle)-p.Y*Math.Sin(angle),-200000+p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));
            foreach(var b in rotated.Beams){var a=rotate(b.Start);b.Start=rotate(b.End);b.End=a;}
            foreach(var s in rotated.Slabs)s.Outline=(reverse ? s.Outline.Reverse() : s.Outline).Select(rotate).ToArray();
            foreach(var c in rotated.Boundaries)c.Outline=(reverse ? c.Outline.Reverse() : c.Outline).Select(rotate).ToArray();
            var r=RevitStructureImport.Build(rotated,"523456");Check(r.CanImport,"rotated column connections and reversed footprints: "+reverse+": "+string.Join(";",r.Problems));
            var rotatedModel=Model();r.Apply(rotatedModel);Check(rotatedModel.RevitSceneModels().All(p=>p.Item2.CreateTowerInput(false)!=null),"every rotated/reversed part reaches the same numerical input factory");
        }
        string report=RevitImportDiagnostics.Build(source,original,"523456",result,true,"",null,"");
        Check(report.Contains("Detached calculation-part geometry") && report.Contains("Beam 523745 · between columns"),"diagnostic records actual calculation pieces and perimeter ownership");
        Console.WriteLine("Passed "+checks+" Revit perimeter/column connection checks. Full report: 76 selected elements, 64 beam/slab IDs, 12 combined drawing parts.");
    }
}
