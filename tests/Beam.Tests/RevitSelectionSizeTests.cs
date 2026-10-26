using System;
using System.Linq;
using EngineeringDashboard;

internal static class RevitSelectionSizeTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static BeamCalculatorViewModel Model()=>new BeamCalculatorViewModel {UseTowers=true,TowerLegCapacityKn=70,AllowCustomH20Lengths=false,MaximumConcreteEndOverhangMm=500,FloorHeight=4000};
    private static RevitSelectedBeam Beam(string id,double x0,double y0,double x1,double y1,double width)=>new RevitSelectedBeam {SourceId=id,Label=id,Start=new ImportPoint(x0,y0),End=new ImportPoint(x1,y1),WidthMm=width,DepthMm=1150,BottomMm=2850};
    private static RevitSelectedSlab Slab(string id,double x0,double x1,double y0,double y1)=>new RevitSelectedSlab {SourceId=id,Label=id,ThicknessMm=200,TopMm=4000,Outline=RevitStructureImportTests.Rect(x0,x1,y0,y1)};
    private static RevitSelectionSnapshot Crossing()=>new RevitSelectionSnapshot {
        Beams=new[] {Beam("lower",0,0,7000,0,800),Beam("upper",0,4000,7000,4000,800),Beam("cross-a",3300,400,3300,1800,650),Beam("cross-b",3300,1800,3300,3600,650)},
        Slabs=new[] {Slab("left",0,2975,400,3600),Slab("right",3625,7000,400,3600)}};
    internal static void Run()
    {
        var source=RevitSelectionSizeFixtures.Reported();string original=RevitImportDiagnostics.CaptureSelection(source);
        Check(source.Beams.Length==49 && source.Slabs.Length==29 && source.Boundaries.Length==12,"original 90-element geometry is frozen without editing endpoints");
        var result=RevitStructureImport.Build(source,"523462");
        Check(result.CanImport,"90-element report imports: "+string.Join(";",result.Problems));
        Check(result.Areas.Length==12 && result.Areas.Count(a=>a.Result.Configuration.Bays.Length>0)==2,"two calculation strips and ten column-supported members share the canvas");
        Check(result.Areas.SelectMany(a=>a.SourceIds).Distinct().OrderBy(x=>x).SequenceEqual(source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).OrderBy(x=>x)),"all 78 beam and slab IDs survive partitioning");
        Check(result.Areas.SelectMany(a=>a.Result.Configuration.RevitContext.Sources).Select(s=>s.SourceId).Distinct().OrderBy(x=>x).SequenceEqual(source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).OrderBy(x=>x)),"every selected beam/slab ID also survives in calculation source mappings");
        foreach(var area in result.Areas)
        {
            var model=Model();area.Result.Apply(model);
            Check(model.CreateTowerInput(false)!=null,"existing calculation-input checks accept "+area.Label);
            Check(model.RevitContext.References.Length==12,"all twelve column references retained: "+area.Label);
        }
        var crossing=result.Areas.SelectMany(a=>a.Result.Configuration.CrossingBeams).Single(c=>c.RevitSource.Contains("524539"));
        Check(crossing.RevitSource.Contains("523473"),"both original crossing pieces are reported on the connected crossing");
        var floorPieces=result.Areas.SelectMany(a=>a.Geometry.Slabs).Where(s=>s.SourceId=="583857").ToArray();
        var tSlab=source.Slabs.Single(s=>s.SourceId=="583857");
        Check(floorPieces.Length>0 && Math.Abs(floorPieces.Sum(s=>ImportPolygon.Area(s.Outline))-ImportPolygon.Area(tSlab.Outline))<=ImportPolygon.Area(tSlab.Outline)*1e-6,"slab area at the T junction is retained within one part per million");
        foreach(double fx in new[] {.25,.5,.75})foreach(double fy in new[] {.25,.5,.75})
        {
            double x=tSlab.Outline.Min(p=>p.X)+(tSlab.Outline.Max(p=>p.X)-tSlab.Outline.Min(p=>p.X))*fx,y=tSlab.Outline.Min(p=>p.Y)+(tSlab.Outline.Max(p=>p.Y)-tSlab.Outline.Min(p=>p.Y))*fy;
            if(SlabFootprint.Contains(tSlab.Outline,x,y))Check(floorPieces.Any(s=>SlabFootprint.Contains(s.Outline,x,y)),"continuous slab interior at the T junction remains covered");
        }
        foreach(var beam in source.Beams)
        {
            var pieces=result.Areas.SelectMany(a=>a.Geometry.Beams).Where(b=>b.SourceId==beam.SourceId).ToArray();
            Check(pieces.Length>0 && pieces.All(b=>b.WidthMm==beam.WidthMm && b.DepthMm==beam.DepthMm && b.BottomMm==beam.BottomMm),"source beam sections and soffits retained: "+beam.SourceId);
            Check(Math.Abs(pieces.Sum(b=>b.LengthMm)-beam.LengthMm)<=2*RevitStructureImport.ToleranceMm+1e-6,"source beam length conserved: "+beam.SourceId);
            double dx=(beam.End.X-beam.Start.X)/beam.LengthMm,dy=(beam.End.Y-beam.Start.Y)/beam.LengthMm;
            var ranges=pieces.Select(b=>new[] {(b.Start.X-beam.Start.X)*dx+(b.Start.Y-beam.Start.Y)*dy,(b.End.X-beam.Start.X)*dx+(b.End.Y-beam.Start.Y)*dy}).Select(r=>new[] {r.Min(),r.Max()}).OrderBy(r=>r[0]).ToArray();
            Check(ranges.Zip(ranges.Skip(1),(a,b)=>a[1]<=b[0]+1e-6).All(v=>v),"no source beam interval is duplicated: "+beam.SourceId);
        }
        foreach(var slab in source.Slabs)
        {
            var pieces=result.Areas.SelectMany(a=>a.Geometry.Slabs).Where(s=>s.SourceId==slab.SourceId).ToArray();
            Check(pieces.Length>0 && pieces.All(s=>s.ThicknessMm==slab.ThicknessMm && s.TopMm==slab.TopMm),"source slab section retained: "+slab.SourceId);
            Check(Math.Abs(pieces.Sum(s=>ImportPolygon.Area(s.Outline))-ImportPolygon.Area(slab.Outline))<=ImportPolygon.Perimeter(slab.Outline)*RevitStructureImport.ToleranceMm+1,"source slab area conserved within plan alignment: "+slab.SourceId);
            var ranges=pieces.Select(s=>new[] {s.Outline.Min(p=>p.X),s.Outline.Max(p=>p.X)}).OrderBy(r=>r[0]).ToArray();
            Check(ranges.Zip(ranges.Skip(1),(a,b)=>a[1]<=b[0]+1e-6).All(v=>v),"source slab pieces do not overlap: "+slab.SourceId);
        }
        Check(original==RevitImportDiagnostics.CaptureSelection(source),"import never modifies the captured selection");
        var combined=Model();result.Apply(combined);var saved=combined.NetworkConfigurationXml;var copy=Model();copy.BeamLengthMm=combined.BeamLengthMm;copy.NetworkConfigurationXml=saved;
        Check(copy.RevitSceneModels().Length==12 && copy.RevitSceneModels().All(p=>p.Item2.CreateTowerInput(false)!=null),"combined save/load preserves all twelve calculation parts");
        Check(copy.TowerLegCapacityKn==70 && !copy.AllowCustomH20Lengths,"project permissions and leg settings survive import");
        foreach(string axis in new[] {"523473","523462"})
        {
            var rotated=RevitSelectionSizeFixtures.Reported();double angle=.6;
            Func<ImportPoint,ImportPoint> move=p=>new ImportPoint(100000+p.X*Math.Cos(angle)-p.Y*Math.Sin(angle),-200000+p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));
            foreach(var b in rotated.Beams){var start=move(b.Start);b.Start=move(b.End);b.End=start;}
            foreach(var s in rotated.Slabs)s.Outline=s.Outline.Reverse().Select(move).ToArray();
            foreach(var c in rotated.Boundaries)c.Outline=c.Outline.Reverse().Select(move).ToArray();
            var r=RevitStructureImport.Build(rotated,axis);
            Check(r.CanImport && r.Areas.Length==12,"both rotated/reversed main directions recognize the T face: "+axis+": "+string.Join(";",r.Problems));
        }
        var fragments=new RevitSelectionSnapshot {Beams=Enumerable.Range(0,221).Select(i=>Beam("piece"+i,i*100,0,(i+1)*100,0,650)).ToArray()};
        var longRow=RevitStructureImport.Build(fragments,"piece0");
        Check(longRow.CanImport && longRow.Configuration.Beams.Length==1 && longRow.Configuration.RevitContext.Sources.Length==221,"221 connected source elements are not stopped by a replacement raw-beam or source-mapping cap");
        var longModel=Model();longRow.Apply(longModel);var longSaved=longModel.NetworkConfigurationXml;var longCopy=Model();longCopy.BeamLengthMm=longModel.BeamLengthMm;longCopy.NetworkConfigurationXml=longSaved;
        Check(longCopy.RevitContext.Sources.Length==221 && longCopy.CreateTowerInput(false)!=null,"all 221 source IDs survive save/load and calculation-input validation");
        var panels=new RevitSelectionSnapshot {Beams=new[] {Beam("b1",0,0,7000,0,800),Beam("b2",0,4000,7000,4000,800)},Slabs=Enumerable.Range(0,50).Select(i=>Slab("panel"+i,i*140,(i+1)*140,400,3600)).ToArray()};
        var panelResult=RevitStructureImport.Build(panels,"b1");
        Check(panelResult.CanImport && panelResult.Configuration.Bays.Length==1 && panelResult.Configuration.RevitContext.Sources.Count(s=>s.CanvasLabel=="S1")==50,"fifty touching slab panels form one bay without a raw-panel count cap");
        var manyAreas=new RevitSelectionSnapshot {
            Beams=Enumerable.Range(0,46).SelectMany(i=>new[] {Beam("low"+i,i*2000,0,i*2000+1000,0,800),Beam("high"+i,i*2000,4000,i*2000+1000,4000,800)}).ToArray(),
            Slabs=Enumerable.Range(0,46).Select(i=>Slab("area"+i,i*2000,i*2000+1000,400,3600)).ToArray()};
        var manyResult=RevitStructureImport.Build(manyAreas,"low0");
        Check(manyResult.CanImport && manyResult.Areas.Length==46,"forty-six independent areas do not meet a replacement combined-drawing count gate");
        var manyModel=Model();manyResult.Apply(manyModel);var manySaved=manyModel.NetworkConfigurationXml;var manyCopy=Model();manyCopy.BeamLengthMm=manyModel.BeamLengthMm;manyCopy.NetworkConfigurationXml=manySaved;
        Check(manyCopy.RevitSceneModels().Length==46 && manyCopy.RevitSceneModels().All(p=>p.Item2.CreateTowerInput(false)!=null),"all forty-six areas retain their existing input checks through save/load");
        var joined=Crossing();var joinedResult=RevitStructureImport.Build(joined,"lower");
        Check(joinedResult.CanImport && joinedResult.Configuration.CrossingBeams.Length==1,"touching crossing pieces form one complete crossing");
        Check(joinedResult.Configuration.CrossingBeams[0].RevitSource.Contains("cross-a") && joinedResult.Configuration.CrossingBeams[0].RevitSource.Contains("cross-b"),"joined crossing retains both displayed source IDs");
        double rawVolume=joined.Beams.Sum(b=>b.LengthMm*b.WidthMm*b.DepthMm);
        double modeledVolume=joinedResult.Configuration.Beams.Sum(b=>b.SpanLength(joinedResult.LengthMm)*b.WidthMm*b.DepthMm)+joinedResult.Configuration.CrossingBeams.Sum(c=>c.WidthMm*joinedResult.Configuration.Bays[c.BayNumber-1].WidthMm*c.DepthMm);
        Check(Math.Abs(rawVolume-modeledVolume)<1,"joined crossing concrete is counted once");
        foreach(bool gap in new[] {false,true})
        {
            var bad=Crossing();if(gap)bad.Beams[3].Start.Y+=10;else {bad.Beams[3].DepthMm-=10;bad.Beams[3].BottomMm+=10;}
            Check(!RevitStructureImport.Build(bad,"lower").CanImport,"crossing merge rejects "+(gap ? "a real uncovered gap" : "a changed section"));
        }
        var multi=Crossing();multi.Beams=multi.Beams.Concat(new[] {Beam("middle",0,2000,7000,2000,800)}).ToArray();multi.Beams[2].End.Y=2000;multi.Beams[3].Start.Y=2000;
        multi.Slabs=new[] {Slab("s1",0,2975,400,1600),Slab("s2",3625,7000,400,1600),Slab("s3",0,2975,2400,3600),Slab("s4",3625,7000,2400,3600)};
        var multiResult=RevitStructureImport.Build(multi,"lower");
        Check(multiResult.CanImport && multiResult.Configuration.CrossingBeams.Length==2,"one connected crossing can span two complete bays");
        Check(multiResult.Configuration.CrossingBeams[0].RevitSource=="cross-a" && multiResult.Configuration.CrossingBeams[1].RevitSource=="cross-b","each bay names only the original source piece that actually contributes to it");
        Console.WriteLine("Passed "+checks+" Revit selection-size and T-junction checks. Full report: 90 elements, 49 beams, 29 slabs, 12 columns, 12 drawing parts.");
    }
}
