using System;
using System.Linq;
using EngineeringDashboard;

internal static class RevitJunctionTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static BeamCalculatorViewModel Model()=>new BeamCalculatorViewModel {UseTowers=true,TowerLegCapacityKn=70,AllowCustomH20Lengths=false,MaximumConcreteEndOverhangMm=500,FloorHeight=4000};
    private static RevitSelectedBeam Beam(string id,double x0,double y0,double x1,double y1,double width)=>new RevitSelectedBeam {SourceId=id,Label="Beam "+id,Start=new ImportPoint(x0,y0),End=new ImportPoint(x1,y1),WidthMm=width,DepthMm=1150,BottomMm=2850};
    private static RevitSelectedSlab Slab(string id,double x0,double x1,double y0,double y1)=>new RevitSelectedSlab {SourceId=id,Label="Slab "+id,ThicknessMm=200,TopMm=4000,Outline=RevitStructureImportTests.Rect(x0,x1,y0,y1)};
    private static RevitSelectionSnapshot Sample()=>new RevitSelectionSnapshot {
        Beams=new[] {Beam("left",0,0,7000,0,800),Beam("middle-a",0,4000,3000,4000,650),Beam("middle-b",3650,4000,7000,4000,650),Beam("right",0,8000,7000,8000,800),Beam("cross",3325,400,3325,7600,650)},
        Slabs=new[] {Slab("s1",0,3000,400,3675),Slab("s2",3650,7000,400,3675),Slab("s3",0,3000,4325,7600),Slab("s4",3650,7000,4325,7600)}};
    internal static void Run()
    {
        var source=RevitJunctionFixtures.Reported();string original=RevitImportDiagnostics.CaptureSelection(source);
        Check(source.Beams.Length==39 && source.Slabs.Length==24 && source.Boundaries.Length==12,"unmodified 75-element report fixture");
        var result=RevitStructureImport.Build(source,"523475");
        Check(result.CanImport,"full 75-element junction report imports: "+string.Join(";",result.Problems));
        Check(result.Areas.Length==11 && result.Areas.Count(a=>a.Result.Configuration.Bays.Length>0)==5,"five clear areas and six column beams share one drawing");
        Check(result.Areas.SelectMany(a=>a.SourceIds).OrderBy(x=>x).SequenceEqual(source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).OrderBy(x=>x)),"every beam and slab belongs to exactly one calculation part");
        foreach(var area in result.Areas)
        {
            var model=Model();area.Result.Apply(model);
            Check(model.CreateTowerInput(false)!=null,"unchanged calculation-input validation: "+area.Label);
            Check(model.RevitContext.References.Length==12,"all twelve column references retained: "+area.Label);
            if(area.Result.Configuration.Bays.Length>0)
            {
                Check(area.Result.Configuration.Beams.Length==3 && area.Result.Configuration.Bays.Length==2,"collinear pieces do not create a false slab bay: "+area.Label);
                Check(area.Result.Configuration.Bays.All(b=>b.WidthMm>3000),"both actual slab bays retained: "+area.Label);
            }
            foreach(var b in area.Geometry.Beams)
            {
                var expected=source.Beams.Single(s=>s.SourceId==b.SourceId);
                Check(b.Start.X==expected.Start.X && b.Start.Y==expected.Start.Y && b.End.X==expected.End.X && b.End.Y==expected.End.Y
                    && b.WidthMm==expected.WidthMm && b.DepthMm==expected.DepthMm && b.BottomMm==expected.BottomMm,"raw beam geometry is not extended or replaced: "+b.SourceId);
            }
            foreach(var s in area.Geometry.Slabs)
            {
                var expected=source.Slabs.Single(f=>f.SourceId==s.SourceId);
                Check(s.ThicknessMm==expected.ThicknessMm && s.TopMm==expected.TopMm && s.Outline.Select(p=>Tuple.Create(p.X,p.Y)).SequenceEqual(expected.Outline.Select(p=>Tuple.Create(p.X,p.Y))),"raw slab outline retained: "+s.SourceId);
            }
        }
        var upper=result.Areas.Single(a=>a.SourceIds.Contains("523625"));var lower=result.Areas.Single(a=>a.SourceIds.Contains("523593"));
        Check(upper.Result.Configuration.Beams[1].RevitSource.Contains("523625") && upper.Result.Configuration.Beams[1].RevitSource.Contains("523706"),"upper connected row retains both source IDs");
        Check(new[] {"523593","523595","523550","523638"}.All(id=>lower.Result.Configuration.Beams[1].RevitSource.Contains(id)),"lower connected row retains all four source pieces");
        Check(upper.Result.Configuration.CrossingBeams.Length==2 && lower.Result.Configuration.CrossingBeams.Length==6,"all perpendicular clear-bay strips retained once");
        Check(original==RevitImportDiagnostics.CaptureSelection(source),"import never modifies the captured Revit geometry");
        var combined=Model();result.Apply(combined);var saved=combined.NetworkConfigurationXml;var restored=Model();restored.BeamLengthMm=combined.BeamLengthMm;restored.NetworkConfigurationXml=saved;
        Check(restored.RevitSceneModels().Length==11 && restored.RevitSceneModels().All(p=>p.Item2.CreateTowerInput(false)!=null),"combined save/load retains eleven valid calculation inputs");
        Check(restored.TowerLegCapacityKn==70 && !restored.AllowCustomH20Lengths,"import retains project leg and catalogue settings");
        var otherAxis=RevitStructureImport.Build(RevitJunctionFixtures.Reported(),"523885");
        Check(otherAxis.CanImport && otherAxis.Areas.Length==11,"orthogonal main direction recognizes the same selected junctions");
        var otherModel=Model();otherAxis.Apply(otherModel);
        Check(otherModel.RevitSceneModels().Length==11 && otherModel.RevitSceneModels().All(p=>p.Item2.CreateTowerInput(false)!=null),"large orthogonal area can be applied to the combined canvas");
        Check(otherModel.RevitDrawings.All(d=>d.Key.Length<=300) && otherModel.RevitDrawings.Any(d=>d.Key.StartsWith("sources:")),"large source lists have bounded keys without losing their mappings");
        var reordered=RevitJunctionFixtures.Reported();reordered.Beams=reordered.Beams.Reverse().ToArray();reordered.Slabs=reordered.Slabs.Reverse().ToArray();
        var reorderedResult=RevitStructureImport.Build(reordered,"523885");
        Check(reorderedResult.CanImport && reorderedResult.Areas.Select(a=>a.Key).OrderBy(k=>k).SequenceEqual(otherAxis.Areas.Select(a=>a.Key).OrderBy(k=>k)),"compact drawing keys are stable across source enumeration order");
        string report=RevitImportDiagnostics.Build(source,original,"523475",result,true,"",null,"");
        Check(report.Contains("fills the junction") && report.Contains("523551") && report.Contains("523565"),"diagnostics explain both selected crossings and the original endpoints");

        var sample=Sample();var simple=RevitStructureImport.Build(sample,"left");
        Check(simple.CanImport,"one selected crossing can connect a same-section row: "+string.Join(";",simple.Problems));
        var config=simple.Configuration;
        Check(config.Beams.Length==3 && config.Beams[1].SpanLength(simple.LengthMm)==7000,"crossing-filled interruption forms one complete logical row");
        // Independent volume balance: original beams are disjoint except for
        // face contact. The imported crossing strips exclude the row junction.
        double sourceVolume=sample.Beams.Sum(b=>b.LengthMm*b.WidthMm*b.DepthMm);
        double modelVolume=config.Beams.Sum(b=>b.SpanLength(simple.LengthMm)*b.WidthMm*b.DepthMm)
            +config.CrossingBeams.Sum(c=>c.WidthMm*config.Bays[c.BayNumber-1].WidthMm*c.DepthMm);
        Check(Math.Abs(sourceVolume-modelVolume)<1,"junction concrete volume is included exactly once");
        Check(config.CrossingBeams.Length==2 && config.RevitContext.Sources.Count(s=>s.SourceId=="cross")==2,"one crossing maps only to its two disjoint clear-bay segments");
        foreach(var failure in new[] {"missing crossing","partial width","unfilled interruption","different crossing section","different row section","column obstruction"})
        {
            var bad=Sample();
            if(failure=="missing crossing")bad.Beams=bad.Beams.Where(b=>b.SourceId!="cross").ToArray();
            if(failure=="partial width")bad.Beams.Single(b=>b.SourceId=="cross").End.Y=4000;
            if(failure=="unfilled interruption")bad.Beams.Single(b=>b.SourceId=="middle-b").Start.X+=25;
            if(failure=="different crossing section"){var b=bad.Beams.Single(x=>x.SourceId=="cross");b.DepthMm-=10;b.BottomMm+=10;}
            if(failure=="different row section"){var b=bad.Beams.Single(x=>x.SourceId=="middle-b");b.DepthMm-=10;b.BottomMm+=10;}
            if(failure=="column obstruction")bad.Boundaries=new[] {new RevitSelectedBoundary {SourceId="column",Label="Column",Kind="Column",BottomMm=0,TopMm=4000,Outline=RevitStructureImportTests.Rect(3100,3500,3700,4300)}};
            Check(!RevitStructureImport.Build(bad,"left").CanImport,"cannot bridge "+failure);
        }
        foreach(bool reverse in new[] {false,true})
        {
            var rotated=RevitJunctionFixtures.Reported();double angle=.6;
            Func<ImportPoint,ImportPoint> move=p=>new ImportPoint(100000+p.X*Math.Cos(angle)-p.Y*Math.Sin(angle),-200000+p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));
            foreach(var b in rotated.Beams){var start=move(b.Start);b.Start=move(b.End);b.End=start;}
            foreach(var slab in rotated.Slabs)slab.Outline=(reverse ? slab.Outline.Reverse() : slab.Outline).Select(move).ToArray();
            foreach(var boundary in rotated.Boundaries)boundary.Outline=(reverse ? boundary.Outline.Reverse() : boundary.Outline).Select(move).ToArray();
            var rotatedResult=RevitStructureImport.Build(rotated,"523475");
            Check(rotatedResult.CanImport && rotatedResult.Areas.Length==11,"rotated/reversed junctions preserve the same eleven parts: "+reverse+": "+string.Join(";",rotatedResult.Problems));
        }
        Console.WriteLine("Passed "+checks+" Revit beam-junction checks. Report: 75 elements, 39 beams, 24 slabs, 12 columns, 11 drawing parts.");
    }
}
