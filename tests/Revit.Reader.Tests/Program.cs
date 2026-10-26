using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using EngineeringDashboard;

internal static class ReaderTestsProgram
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static XYZ Point(double x,double y,double z)=>new XYZ(x/304.8,y/304.8,z/304.8);
    private sealed class Host
    {
        internal UIApplication App;
        internal readonly List<long> Visited=new List<long>(),GeometryRead=new List<long>();
        internal readonly HashSet<int> Threads=new HashSet<int>();
    }
    private static GeometryElement Box(double x0,double x1,double y0,double y1,double z0,double z1)
    {
        var solid=new Solid {Volume=(x1-x0)*(y1-y0)*(z1-z0)/Math.Pow(304.8,3)};
        void Face(XYZ normal,double area,params XYZ[] points)
        {
            var face=new PlanarFace {FaceNormal=normal,Origin=points[0],Area=area/(304.8*304.8)};face.Vertices.AddRange(points);
            var loop=new EdgeArray();for(int i=0;i<points.Length;i++)loop.Add(new Edge {Start=points[i],End=points[(i+1)%points.Length]});face.EdgeLoops.Add(loop);solid.Faces.Add(face);
        }
        Face(new XYZ(0,0,1),(x1-x0)*(y1-y0),Point(x0,y0,z1),Point(x1,y0,z1),Point(x1,y1,z1),Point(x0,y1,z1));
        Face(new XYZ(0,0,-1),(x1-x0)*(y1-y0),Point(x0,y0,z0),Point(x0,y1,z0),Point(x1,y1,z0),Point(x1,y0,z0));
        Face(new XYZ(-1,0,0),(y1-y0)*(z1-z0),Point(x0,y0,z0),Point(x0,y0,z1),Point(x0,y1,z1),Point(x0,y1,z0));
        Face(new XYZ(1,0,0),(y1-y0)*(z1-z0),Point(x1,y0,z0),Point(x1,y1,z0),Point(x1,y1,z1),Point(x1,y0,z1));
        Face(new XYZ(0,-1,0),(x1-x0)*(z1-z0),Point(x0,y0,z0),Point(x1,y0,z0),Point(x1,y0,z1),Point(x0,y0,z1));
        Face(new XYZ(0,1,0),(x1-x0)*(z1-z0),Point(x0,y1,z0),Point(x0,y1,z1),Point(x1,y1,z1),Point(x1,y1,z0));
        return new GeometryElement {solid};
    }
    private static Element Beam(long id)
    {
        double x=(id%500)*1300;
        return new FamilyInstance {Id=new ElementId(id),Category=new Category {Id=new ElementId((long)BuiltInCategory.OST_StructuralFraming),Name="Structural Framing"},
            Depth=new Parameter {Number=600/304.8},Location=new LocationCurve {Curve=new Line {Start=Point(x,0,4000),End=Point(x+1000,0,4000)}},Geometry=()=>Box(x,x+1000,-150,150,3400,4000)};
    }
    private static Host Fixture(int count,Func<long,Element> element=null)
    {
        var result=new Host();var document=new Document();element=element??Beam;
        document.Read=id=>
        {
            result.Threads.Add(Thread.CurrentThread.ManagedThreadId);
            if(id.Value==-1)return new Element();
            result.Visited.Add(id.Value);var e=element(id.Value);
            if(e?.Geometry!=null){var geometry=e.Geometry;e.Geometry=()=>{result.Threads.Add(Thread.CurrentThread.ManagedThreadId);result.GeometryRead.Add(id.Value);return geometry();};}
            return e;
        };
        result.App=new UIApplication {ActiveUIDocument=new UIDocument {Document=document}};
        result.App.ActiveUIDocument.Selection.Ids=Enumerable.Range(1,count).Reverse().Select(i=>new ElementId(i)).ToArray();
        return result;
    }
    private static int Main()
    {
        try
        {
            int thread=Thread.CurrentThread.ManagedThreadId;
            foreach(int count in new[] {199,200,201,432,1001})
            {
                var host=Fixture(count);var selection=RevitStructureSelection.Read(host.App);
                Check(host.Visited.Count==count,"production reader visits every element at selection size "+count+"; visited="+host.Visited.Count);
                Check(selection.SelectedCount==count && selection.SelectedIds.Length==count,"selected IDs are not truncated: "+count);
                Check(selection.SelectedIds.SequenceEqual(Enumerable.Range(1,count).Select(i=>i.ToString(System.Globalization.CultureInfo.InvariantCulture))),"all selected IDs retain deterministic order: "+count);
                Check(host.Visited.SequenceEqual(Enumerable.Range(1,count).Select(i=>(long)i)),"every selected ID is fetched exactly once: "+count);
                Check(selection.Beams.Length==count && selection.Elements.Length==count && selection.Problems.Length==0,"geometry is read after selection, without a count gate: "+count);
                Check(selection.Elements.All(e=>e.Status=="Read") && selection.Beams.All(b=>Math.Abs(b.WidthMm-300)<1e-5 && Math.Abs(b.DepthMm-600)<1e-5),"the actual reader extracts supported geometry and nominal depth: "+count);
                Check(host.GeometryRead.Count==count && host.Threads.SetEquals(new[] {thread}),"all host reads stay on the calling Revit thread: "+count);
                string report=RevitImportDiagnostics.Build(selection,null,null,null,false,"",null,"");
                Check(report.Contains("Selected elements: "+count) && report.Contains("Element "+count+" | Structural Framing"),"diagnostics include the last selected element: "+count);
                string captured=report.Split(new[] {"captured IDs: "},StringSplitOptions.None)[1].Split('\n')[0].Trim();
                Check(captured.Split(',').Length==count,"the copied diagnostic includes every captured ID: "+count);
            }
            var mixed=Fixture(432,id=>
            {
                if(id==2)return new Floor {Id=new ElementId(id),Category=new Category {Id=new ElementId(20),Name="Floors"},Geometry=()=>Box(0,1000,0,1000,3800,4000)};
                if(id==3)return new FamilyInstance {Id=new ElementId(id),Category=new Category {Id=new ElementId((long)BuiltInCategory.OST_StructuralColumns),Name="Structural Columns"},Geometry=()=>Box(0,800,0,800,0,3800)};
                if(id==4)return new Wall {Id=new ElementId(id),Category=new Category {Id=new ElementId(30),Name="Walls"},Geometry=()=>Box(0,2000,0,300,0,3800)};
                if(id==5)return new Element {Id=new ElementId(id),Category=new Category {Id=new ElementId(40),Name="Generic Models"}};
                return Beam(id);
            });
            var mixedRead=RevitStructureSelection.Read(mixed.App);
            Check(mixedRead.SelectedCount==432 && mixedRead.Elements.Length==432,"large mixed-category selection visits every record");
            Check(mixedRead.Beams.Length==428 && mixedRead.Slabs.Length==1 && mixedRead.Boundaries.Length==2,"supported beam, floor, column and wall categories are extracted");
            Check(mixedRead.Problems.Length==0 && mixedRead.Elements.Single(e=>e.SourceId=="5").Status=="Skipped","unsupported categories are identified without blocking valid elements");
            Check(mixedRead.Elements.Last().SourceId=="432" && mixedRead.Elements.Last().Status=="Read","later supported elements are still read after skipped categories");
            Check(mixed.Threads.SetEquals(new[] {thread}),"mixed geometry extraction remains synchronous on the host thread");

            var failures=Fixture(432,id=>{var e=Beam(id);e.Geometry=()=>throw new InvalidOperationException("geometry failure "+id);return e;});
            var failed=RevitStructureSelection.Read(failures.App);var blocked=RevitStructureImport.Build(failed);
            Check(failures.Visited.Count==432 && failed.SelectedIds.Length==432 && failed.Elements.All(e=>e.Status=="Failed"),"per-element geometry errors retain every ID and do not stop the selection traversal");
            Check(!blocked.CanImport && blocked.Problems.Length==432,"failed geometry remains blocked with its actual reader failures");
            Check(!blocked.Problems.Any(p=>p.Contains("Select at least one straight")),"zero read beams cannot add a misleading missing-selection instruction");
            string failureReport=RevitImportDiagnostics.Build(failed,null,null,blocked,false,"",null,"");
            Check(failureReport.Contains("geometry failure 432") && !failureReport.Contains("Select at least one straight"),"last-element failures remain visible in the copied report without misleading follow-on errors");
            Check(failures.Threads.SetEquals(new[] {thread}),"exception handling does not move host access to a background thread");

            var empty=RevitStructureSelection.Read(Fixture(0).App);var emptyResult=RevitStructureImport.Build(empty);
            Check(empty.SelectedCount==0 && empty.SelectedIds.Length==0 && empty.Problems.Length==1 && emptyResult.Problems.SequenceEqual(empty.Problems),"empty selection retains its precise reader instruction without a second generic error");
            var missing=RevitStructureSelection.Read(null);var missingResult=RevitStructureImport.Build(missing);
            Check(missing.Problems.Length==1 && missingResult.Problems.SequenceEqual(missing.Problems),"missing document retains only its real reader error");
            var unsupported=Fixture(432,id=>new Element {Id=new ElementId(id),Category=new Category {Id=new ElementId(40),Name="Generic Models"}});
            var noBeams=RevitStructureSelection.Read(unsupported.App);var noBeamResult=RevitStructureImport.Build(noBeams);
            Check(noBeams.SelectedIds.Length==432 && noBeams.Elements.Length==432 && noBeams.Problems.Length==0,"all unsupported IDs are still retained in a large selection");
            Check(!noBeamResult.CanImport && noBeamResult.Problems.Any(p=>p.Contains("Select at least one straight")),"a genuinely beam-free selection still receives the correct geometry instruction");
            Console.WriteLine("Passed "+checks+" production Revit reader-path checks using test-only API stand-ins. Selection sizes: 199, 200, 201, 432 and 1001; all IDs retained and geometry reads stay on the host thread.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
