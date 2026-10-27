using System;
using System.Linq;
using EngineeringDashboard;

internal static class RevitStructureImportTests
{
    private static int checks;
    private static void Check(bool condition,string name) {checks++;if(!condition)throw new Exception(name);}
    private static void Equal(double expected,double actual,string name)=>Check(Math.Abs(expected-actual)<1e-6,name+": "+actual);
    internal static ImportPoint[] Rect(double x0,double x1,double y0,double y1)=>new[] {new ImportPoint(x0,y0),new ImportPoint(x1,y0),new ImportPoint(x1,y1),new ImportPoint(x0,y1)};
    internal static RevitSelectionSnapshot Sample()
    {
        return new RevitSelectionSnapshot {DocumentTitle="Selection fixture",Beams=new[] {
            new RevitSelectedBeam {SourceId="101",Label="Beam A [101]",Start=new ImportPoint(0,0),End=new ImportPoint(7600,0),WidthMm=800,DepthMm=1750,BottomMm=8250},
            new RevitSelectedBeam {SourceId="102",Label="Beam B [102]",Start=new ImportPoint(7600,3000),End=new ImportPoint(0,3000),WidthMm=800,DepthMm=1750,BottomMm=8250},
            new RevitSelectedBeam {SourceId="103",Label="Crossing [103]",Start=new ImportPoint(3650,0),End=new ImportPoint(3650,3000),WidthMm=650,DepthMm=1150,BottomMm=8850}},
            Slabs=new[] {new RevitSelectedSlab {SourceId="201",Label="Floor [201]",Outline=Rect(-400,8000,-400,3400),ThicknessMm=300,TopMm=10000}},
            Boundaries=new[] {
                new RevitSelectedBoundary {SourceId="301",Label="Wall start [301]",Kind="Wall",Outline=Rect(-200,300,-1000,4000),BottomMm=0,TopMm=12000},
                new RevitSelectedBoundary {SourceId="302",Label="Wall end [302]",Kind="Wall",Outline=Rect(7300,7800,-1000,4000),BottomMm=0,TopMm=12000}}};
    }
    private static void Rejected(Action<RevitSelectionSnapshot> change,string expected)
    {
        var s=Sample();change(s);var result=RevitStructureImport.Build(s,"101");
        Check(!result.CanImport && result.Problems.Any(p=>p.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0),"Reject "+expected+": "+string.Join(";",result.Problems));
    }
    internal static void Run()
    {
        var sample=Sample();Check(RevitStructureImport.DefaultDirection(sample)=="101","dominant parallel direction is deterministic");
        var r=RevitStructureImport.Build(sample);Check(r.CanImport,string.Join(";",r.Problems));
        Equal(7000,r.LengthMm,"clear length uses both wall faces");var c=r.Configuration;
        Check(c.Beams.Length==2 && c.Bays.Length==1 && c.CrossingBeams.Length==1,"exact imported topology");
        Equal(2200,c.Bays[0].WidthMm,"clear bay subtracts both half widths");Equal(300,c.Bays[0].ThicknessMm,"slab thickness");
        Equal(-1750,c.Beams[0].BottomElevationMm,"beam soffit uses concrete datum");Equal(-1150,c.CrossingBeams[0].BottomElevationMm,"crossing soffit");
        Equal(3350,c.CrossingBeams[0].PositionMm,"crossing measured from clear start");Equal(300,c.RevitContext.OriginXmm,"Revit origin retained");
        Equal(10000,c.RevitContext.TopElevationMm,"Revit elevation retained");Equal(0,c.RevitContext.References[0].Outline.Max(p=>p.X),"start wall reaches canvas start");
        Equal(7000,c.RevitContext.References[1].Outline.Min(p=>p.X),"end wall starts at canvas end");
        Check(c.RevitContext.Sources.Any(s=>s.CanvasLabel=="X1" && s.SourceId=="103"),"crossing source identity");
        var model=new BeamCalculatorViewModel {UseTowers=true,AllowCustomH20Lengths=false,CustomH20Scope=CustomH20LengthScope.SlabSupports,TowerLegCapacityKn=50,PrimaryMaterial=PrimaryBeamMaterial.WS10,FloorHeight=4000,StockLengthsText="1800; 3500"};
        r.Apply(model);Equal(7000,model.BeamLengthMm,"apply clear length");Check(model.IsNetworkMode && model.TransferSlabLoads,"apply enables network mode");
        Check(!model.AllowCustomH20Lengths && model.CustomH20Scope==CustomH20LengthScope.SlabSupports && model.TowerLegCapacityKn==50 && model.PrimaryMaterial==PrimaryBeamMaterial.WS10 && model.StockLengthsText=="1800; 3500" && model.FloorHeight==4000,"all project settings survive import");
        Check(!model.IsCalculationValid && model.NetworkResult==null,"import requires a fresh calculation");Equal(3000,model.GeometryCentres()[1],"canvas centres match selection");
        var restored=new BeamCalculatorViewModel {UseTowers=true,UseBeamNetwork=true,TransferSlabLoads=true};restored.NetworkConfigurationXml=model.NetworkConfigurationXml;
        Check(restored.RevitContext.DocumentTitle=="Selection fixture" && restored.RevitContext.References.Length==2,"save/load retains physical references");
        Check(restored.NetworkBeams[1].RevitSource=="Beam B [102]","save/load retains per-row provenance");
        restored.InsertNetworkBay(0,true);Check(restored.NetworkBeams[2].RevitSource=="Beam B [102]" && restored.NetworkBeams[0].RevitSource==null,"insertion never assigns an old source to a new beam");
        restored.StartNewSession();Check(restored.RevitContext==null,"new session clears Revit references");
        var rotated=Sample();double angle=.6;Func<ImportPoint,ImportPoint> rotate=p=>new ImportPoint(123456+p.X*Math.Cos(angle)-p.Y*Math.Sin(angle),-654321+p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));
        foreach(var b in rotated.Beams){b.Start=rotate(b.Start);b.End=rotate(b.End);}foreach(var f in rotated.Slabs)f.Outline=f.Outline.Select(rotate).ToArray();foreach(var b in rotated.Boundaries)b.Outline=b.Outline.Select(rotate).ToArray();
        var rr=RevitStructureImport.Build(rotated);Check(rr.CanImport,string.Join(";",rr.Problems));Equal(7000,rr.LengthMm,"rotated model clear length");Equal(3350,rr.Configuration.CrossingBeams[0].PositionMm,"rotated crossing coordinate");Equal(Math.Cos(angle),rr.Configuration.RevitContext.DirectionX,"direction retained");
        var three=Sample();three.Beams=three.Beams.Concat(new[] {new RevitSelectedBeam {SourceId="104",Label="Beam C",Start=new ImportPoint(0,6000),End=new ImportPoint(7600,6000),WidthMm=800,DepthMm=1750,BottomMm=8250}}).ToArray();
        three.Beams[2].End.Y=6000;three.Slabs[0].Outline=Rect(-400,8000,-400,6400);foreach(var b in three.Boundaries)foreach(var p in b.Outline)if(p.Y==4000)p.Y=7000;
        var tr=RevitStructureImport.Build(three);Check(tr.CanImport,string.Join(";",tr.Problems));Check(tr.Configuration.CrossingBeams.Length==2 && tr.Configuration.CrossingBeams.All(x=>x.RevitSource=="Crossing [103]"),"one Revit crossing splits exactly at main beam rows");
        Rejected(s=>s.Beams[0].WidthMm=0,"enter a valid width");Rejected(s=>s.Beams[0].DepthMm=double.NaN,"enter a valid width");
        Rejected(s=>s.Beams[0].Problem="Sloped framing","Sloped framing");Rejected(s=>s.Beams[1].End.Y=3100,"diagonal");
        var unequal=Sample();unequal.Beams[1].End.X=1000;var unequalResult=RevitStructureImport.Build(unequal,"101");Check(unequalResult.CanImport,"different clear spans import");Equal(700,unequalResult.Configuration.Beams[1].SpanStartMm,"individual start after wall trim");Equal(6300,unequalResult.Configuration.Beams[1].SpanLength(7000),"individual physical length");Rejected(s=>s.Beams[2].Start.Y=700,"partial bay");
        Rejected(s=>s.Beams[2].End.Y=4000,"crossing endpoints");Rejected(s=>s.Beams[2].BottomMm+=50,"one level");
        Rejected(s=>s.Slabs=new RevitSelectedSlab[0],"complete clear bay");Rejected(s=>s.Slabs[0].ThicknessMm=2000,"thicker");
        Rejected(s=>s.Slabs[0].Outline=Rect(1000,7000,-400,3400),"complete clear bay");
        Rejected(s=>s.Slabs[0].Outline=s.Slabs[0].Outline.Concat(new[] {new ImportPoint(0,0)}).ToArray(),"rectangular");
        Rejected(s=>s.Slabs[0].Problem="Slab openings are unsupported","Slab openings");
        Rejected(s=>s.Slabs=s.Slabs.Concat(s.Slabs).ToArray(),"exactly one slab");
        Rejected(s=>s.Boundaries=s.Boundaries.Concat(new[] {new RevitSelectedBoundary {SourceId="303",Label="Mid column",Kind="Column",Outline=Rect(3000,3500,-500,3500),BottomMm=0,TopMm=12000}}).ToArray(),"divides");
        Rejected(s=>s.Boundaries[0].Outline=Rect(-200,300,-200,4000),"partial or angled");
        Rejected(s=>s.Boundaries=s.Boundaries.Concat(new[] {new RevitSelectedBoundary {SourceId="304",Label="Bay column",Kind="Column",Outline=Rect(3000,3500,1000,1500),BottomMm=0,TopMm=12000}}).ToArray(),"bay interior");
        Rejected(s=>s.Problems=new[] {"Unrecognized model geometry"},"Unrecognized");
        var upper=Sample();foreach(var b in upper.Boundaries)b.BottomMm=20000;
        var ur=RevitStructureImport.Build(upper);Check(ur.CanImport,string.Join(";",ur.Problems));Equal(7600,ur.LengthMm,"boundaries above the selected structure do not trim spans");
        var lone=new RevitSelectionSnapshot {Beams=new[] {Sample().Beams[0]}};var single=RevitStructureImport.Build(lone);Check(single.CanImport,"standalone beam import");single.Apply(model);
        Check(!model.UseBeamNetwork && model.NetworkBeams.Count==1 && !model.TransferSlabLoads && model.RevitContext.Sources.Length==1,"standalone mode has no invented slabs");
        Check(model.StandaloneDefinition().RevitSource=="Beam A [101]","standalone keeps source");
        Check(!RevitStructureImport.Build(new RevitSelectionSnapshot()).CanImport,"empty selection has no replacement");
        var badContext=model.RevitContext;badContext.DirectionX=double.NaN;bool rejected=false;try{model.NetworkConfigurationXml=model.NetworkConfigurationXml;}catch(ArgumentException){rejected=true;}Check(rejected,"invalid persisted references rejected");
        Console.WriteLine("Passed "+checks+" Revit selection import checks.");
    }
}
