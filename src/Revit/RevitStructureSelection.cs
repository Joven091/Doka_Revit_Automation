using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace EngineeringDashboard
{
    /// <summary>Read-only adapter. Run only inside a Revit external command or ExternalEvent callback.</summary>
    internal static class RevitStructureSelection
    {
        private const double MmPerFoot=304.8;
        private sealed class GeometryData
        {
            internal readonly List<XYZ> Points=new List<XYZ>();
            internal readonly List<XYZ> Normals=new List<XYZ>();
            internal bool HasCurvedFaces,HasTopOpening;
            internal double ProjectedTopArea;
            internal readonly List<ImportPoint[]> TopOutlines=new List<ImportPoint[]>();
            internal readonly List<double> TopLevels=new List<double>(),BottomLevels=new List<double>();
        }
        internal static RevitSelectionSnapshot Read(UIApplication app)
        {
            var ui=app?.ActiveUIDocument;
            if(ui==null)return new RevitSelectionSnapshot {Problems=new[] {"Open a Revit project and select its beams, slabs, columns and walls first."}};
            var ids=ui.Selection.GetElementIds();
            var snapshot=new RevitSelectionSnapshot {DocumentTitle=ui.Document.Title,RevitVersion=app.Application.VersionName,RevitBuild=app.Application.VersionBuild,
                ActiveView=ui.ActiveView?.Name??"",SelectedCount=ids.Count,SelectedIds=ids.OrderBy(i=>i.Value).Select(i=>i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()};
            if(ids.Count==0){snapshot.Problems=new[] {"Select beams, slabs, columns and walls in Revit before opening the calculator."};return snapshot;}
            var beams=new List<RevitSelectedBeam>();var slabs=new List<RevitSelectedSlab>();var boundaries=new List<RevitSelectedBoundary>();var problems=new List<string>();var notes=new List<string>();
            var elements=new List<RevitSelectionElement>();
            foreach(var id in ids.OrderBy(i=>i.Value))
            {
                string sourceId=id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),label="Element ["+id.Value+"]";
                var record=new RevitSelectionElement {SourceId=sourceId,Label=label,Status="Reading"};elements.Add(record);
                try
                {
                    var e=ui.Document.GetElement(id);
                    if(e?.Category==null){record.Status="Skipped";record.Details="Unavailable element or category.";notes.Add(label+": an unavailable element was skipped.");continue;}
                    label=Label(e);record.Label=label;record.Category=e.Category.Name+" ("+e.Category.Id.Value+")";record.ElementClass=e.GetType().FullName;
                    var type=ui.Document.GetElement(e.GetTypeId());record.FamilyType=(e as FamilyInstance)?.Symbol?.FamilyName+" / "+type?.Name;
                    long category=e.Category.Id.Value;
                    bool beam=category==(long)BuiltInCategory.OST_StructuralFraming;
                    bool slab=e is Floor;
                    bool boundary=e is Wall || category==(long)BuiltInCategory.OST_StructuralColumns || category==(long)BuiltInCategory.OST_Columns;
                    if(!beam && !slab && !boundary){record.Status="Skipped";record.Details="Not a beam, floor, column or wall in the current document.";notes.Add(label+": skipped (select model beams, floors, columns and walls in the current document). ");continue;}
                    var geometry=new GeometryData();
                    using(var options=new Options {DetailLevel=ViewDetailLevel.Fine,IncludeNonVisibleObjects=false,ComputeReferences=false})
                    {
                        using(var root=e.get_Geometry(options))if(root!=null)Collect(root,Transform.Identity,geometry);
                    }
                    record.Details=GeometryDetails(geometry);
                    if(geometry.Points.Count==0){record.Status="Blocked";problems.Add(label+": no physical geometry could be read.");continue;}
                    record.Status="Read";
                    double bottom=geometry.Points.Min(p=>p.Z)*MmPerFoot,top=geometry.Points.Max(p=>p.Z)*MmPerFoot;
                    if(beam)
                    {
                        var line=(e.Location as LocationCurve)?.Curve as Line;
                        record.Details+="\nLocation curve class: "+(e.Location as LocationCurve)?.Curve?.GetType().FullName;
                        if(line==null){record.Status="Blocked";problems.Add(label+": only straight horizontal beams can be imported.");continue;}
                        var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);var direction=b-a;
                        record.Details+="\nLocation start XYZ (mm): "+Xyz(a)+"; end XYZ (mm): "+Xyz(b);
                        if(Math.Abs(direction.Z)*MmPerFoot>RevitSelectionGeometry.PrecisionMm || Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y)*MmPerFoot<=1)
                        {record.Status="Blocked";problems.Add(label+": sloped or vertical framing cannot be imported.");continue;}
                        var along=new XYZ(direction.X,direction.Y,0).Normalize();var across=new XYZ(-along.Y,along.X,0);
                        var offsets=geometry.Points.Select(p=>p.DotProduct(across)).ToArray();
                        double mid=(offsets.Min()+offsets.Max())/2;
                        // The physical section centre may be offset from the family location line.
                        double correction=mid-a.DotProduct(across);a+=across*correction;b+=across*correction;
                        // Respect physical end cutbacks/extensions as well as family justification.
                        // The column/wall pass can then trim a beam that still reaches a support centre.
                        var stations=geometry.Points.Select(p=>p.DotProduct(along)).ToArray();
                        a+=along*(stations.Min()-a.DotProduct(along));b+=along*(stations.Max()-b.DotProduct(along));
                        double? nominalDepth=ReadDepth(e,type,out string depthSource);
                        bool rectangular=!geometry.HasCurvedFaces && Level(geometry.TopLevels) && (nominalDepth.HasValue || Level(geometry.BottomLevels))
                            && geometry.Normals.All(n=>Axis(n,along) || Axis(n,across) || Axis(n,XYZ.BasisZ));
                        double depth=nominalDepth??(top-bottom);
                        record.Details+="\nBeam depth (mm): "+RevitSelectionGeometry.Number(depth)+"; source: "+depthSource
                            +"\nJoined geometry soffit (mm): "+RevitSelectionGeometry.Number(bottom)+"; section soffit (mm): "+RevitSelectionGeometry.Number(top-depth)
                            +"\nRectangular section check: "+rectangular+"; transverse family offset (mm): "+RevitSelectionGeometry.Number(correction*MmPerFoot);
                        if(!rectangular)record.Status="Blocked";
                        beams.Add(new RevitSelectedBeam {SourceId=sourceId,Label=label,Start=Point(a),End=Point(b),WidthMm=(offsets.Max()-offsets.Min())*MmPerFoot,DepthMm=depth,BottomMm=top-depth,
                            Problem=rectangular ? null : "The physical section is not a horizontal rectangular beam. Use a separate manual drawing."});
                    }
                    else if(slab)
                    {
                        // Preserve concave corner cutbacks; a convex hull invents bearing surface.
                        var outline=geometry.TopOutlines.OrderByDescending(PlanArea).FirstOrDefault()??new ImportPoint[0];
                        bool flat=!geometry.HasCurvedFaces && Level(geometry.TopLevels) && Level(geometry.BottomLevels)
                            && geometry.Normals.All(n=>Math.Abs(n.Z)<1e-6 || Axis(n,XYZ.BasisZ));
                        double area=PlanArea(outline);
                        bool complete=Math.Abs(area-geometry.ProjectedTopArea)<=Math.Max(1,area*1e-6);
                        record.Details+="\nFlat slab check: "+flat+"; footprint area check: "+complete+"; hull area (mm2): "+RevitSelectionGeometry.Number(area);
                        if(geometry.HasTopOpening || !complete || !flat)record.Status="Blocked";
                        slabs.Add(new RevitSelectedSlab {SourceId=sourceId,Label=label,Outline=outline,ThicknessMm=top-bottom,TopMm=top,
                            Problem=geometry.HasTopOpening || !complete ? "Slab openings or disconnected top faces are not supported by this canvas." : flat ? null : "Only flat slabs of constant thickness can be imported."});
                    }
                    else
                    {
                        // Mesh coordinates can lose fractions of a millimetre at
                        // project coordinates. Retain the double-precision face
                        // edges for beam limits, references and slab intersections.
                        var outline=geometry.TopOutlines.OrderByDescending(PlanArea).FirstOrDefault()??new ImportPoint[0];
                        double area=PlanArea(outline),tolerance=RevitSelectionGeometry.PrecisionMm;
                        bool complete=!geometry.HasCurvedFaces && !geometry.HasTopOpening
                            && RevitSelectionGeometry.BoundaryMatches(outline,geometry.Points.Select(Point).ToArray(),geometry.ProjectedTopArea,tolerance);
                        record.Details+="\nBoundary footprint source: planar face edge loop; geometry tolerance (mm): "+RevitSelectionGeometry.Number(tolerance)
                            +"\nBoundary footprint check: "+complete+"; outline area (mm2): "+RevitSelectionGeometry.Number(area)
                            +"; projected top area difference (mm2): "+RevitSelectionGeometry.Number(Math.Abs(area-geometry.ProjectedTopArea))
                            +"\nMesh hull area (mm2), reference only: "+RevitSelectionGeometry.Number(PlanArea(Hull(geometry.Points)));
                        if(!complete)record.Status="Blocked";
                        boundaries.Add(new RevitSelectedBoundary {SourceId=sourceId,Label=label,Kind=e is Wall ? "Wall" : "Column",Outline=outline,BottomMm=bottom,TopMm=top,
                            Problem=complete ? null : "Irregular column/wall footprints need a separate manual drawing."});
                    }
                }
                catch(Exception ex)
                {record.Status="Failed";record.Exception=ex.ToString();problems.Add(label+": geometry could not be read. "+ex.Message);}
            }
            snapshot.Beams=beams.ToArray();snapshot.Slabs=slabs.ToArray();snapshot.Boundaries=boundaries.ToArray();snapshot.Problems=problems.ToArray();snapshot.Notes=notes.ToArray();snapshot.Elements=elements.ToArray();return snapshot;
        }
        private static string Xyz(XYZ p)=>"("+RevitSelectionGeometry.Number(p.X*MmPerFoot)+", "+RevitSelectionGeometry.Number(p.Y*MmPerFoot)+", "+RevitSelectionGeometry.Number(p.Z*MmPerFoot)+")";
        private static string GeometryDetails(GeometryData data)
        {
            var text=new StringBuilder();
            text.AppendLine("Geometry: "+data.Points.Count+" triangulated vertices; "+data.Normals.Count+" planar faces; curved faces="+data.HasCurvedFaces+"; top opening="+data.HasTopOpening);
            text.AppendLine("Top face levels (mm): "+string.Join(", ",data.TopLevels.Distinct().Select(z=>RevitSelectionGeometry.Number(z*MmPerFoot))));
            text.AppendLine("Bottom face levels (mm): "+string.Join(", ",data.BottomLevels.Distinct().Select(z=>RevitSelectionGeometry.Number(z*MmPerFoot))));
            text.AppendLine("Projected top area (mm2): "+RevitSelectionGeometry.Number(data.ProjectedTopArea));
            text.AppendLine("Face normals: "+string.Join("; ",data.Normals.Select(n=>"("+RevitSelectionGeometry.Number(n.X)+", "+RevitSelectionGeometry.Number(n.Y)+", "+RevitSelectionGeometry.Number(n.Z)+")").Distinct()));
            if(data.Points.Count>0)text.AppendLine("Physical world bounds XYZ (mm): "+Xyz(new XYZ(data.Points.Min(p=>p.X),data.Points.Min(p=>p.Y),data.Points.Min(p=>p.Z)))+" to "+Xyz(new XYZ(data.Points.Max(p=>p.X),data.Points.Max(p=>p.Y),data.Points.Max(p=>p.Z))));
            return text.ToString();
        }
        private static double? ReadDepth(Element instance,Element type,out string source)
        {
            // Read a length parameter, never digits in a type/family name. Prefer
            // Revit's section-height definition, then conventional family fields.
            foreach(string name in new[] {"STRUCTURAL_SECTION_COMMON_HEIGHT","FAMILY_HEIGHT_PARAM"})
                if(Enum.TryParse(name,out BuiltInParameter id))foreach(var owner in new[] {instance,type})
                {
                    var parameter=owner?.get_Parameter(id);var value=LengthParameter(parameter);
                    if(value.HasValue){source=(owner==instance ? "instance " : "type ")+name;return value;}
                }
            foreach(string name in new[] {"h","Height","Depth","Overall Height"})foreach(var owner in new[] {instance,type})
            {
                var values=(owner?.GetParameters(name)??new List<Parameter>()).Select(p=>LengthParameter(p)).Where(v=>v.HasValue).Select(v=>v.Value).Distinct().ToArray();
                if(values.Length==1){source=(owner==instance ? "instance " : "type ")+name;return values[0];}
            }
            source="rectangular physical geometry (no section-depth parameter)";return null;
        }
        private static double? LengthParameter(Parameter parameter)
        {
            if(parameter==null || !parameter.HasValue || parameter.StorageType!=StorageType.Double || parameter.Definition.GetDataType()!=SpecTypeId.Length)return null;
            double value=parameter.AsDouble()*MmPerFoot;
            return RevitSelectionGeometry.Finite(value) && value>0 && value<=10000 ? value : (double?)null;
        }
        private static string Label(Element e)
        {
            string mark=e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string text=string.IsNullOrWhiteSpace(mark) ? e.Category.Name : mark;
            return text.Substring(0,Math.Min(200,text.Length))+" ["+e.Id.Value+"]";
        }
        private static bool Axis(XYZ a,XYZ b)=>Math.Abs(Math.Abs(a.DotProduct(b))-1)<1e-6;
        private static bool Level(List<double> levels)=>levels.Count>0 && (levels.Max()-levels.Min())*MmPerFoot<=RevitSelectionGeometry.PrecisionMm;
        private static double PlanArea(ImportPoint[] points)
        {if(points.Length<3)return 0;double sum=0;var origin=points[0];for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];sum+=(a.X-origin.X)*(b.Y-origin.Y)-(a.Y-origin.Y)*(b.X-origin.X);}return Math.Abs(sum)/2;}
        private static ImportPoint Point(XYZ p)=>new ImportPoint(p.X*MmPerFoot,p.Y*MmPerFoot);
        private static void Collect(GeometryElement element,Transform transform,GeometryData data)
        {
            foreach(var item in element)
            {
                if(item is GeometryInstance instance)
                {using(var symbol=instance.GetSymbolGeometry())if(symbol!=null)Collect(symbol,transform.Multiply(instance.Transform),data);}
                else if(item is Solid solid && solid.Volume>1e-9)
                {
                    foreach(Face face in solid.Faces)
                    {
                        if(face is PlanarFace planar)
                        {
                            var normal=transform.OfVector(planar.FaceNormal).Normalize();data.Normals.Add(normal);
                            if(normal.Z>.999999 && planar.EdgeLoops.Size>1)data.HasTopOpening=true;
                            if(normal.Z>.999999)
                            {
                                data.TopLevels.Add(transform.OfPoint(planar.Origin).Z);data.ProjectedTopArea+=planar.Area*normal.Z*MmPerFoot*MmPerFoot;
                                foreach(EdgeArray loop in planar.EdgeLoops)
                                {
                                    var vertices=new List<ImportPoint>();
                                    foreach(Edge edge in loop)using(var curve=edge.AsCurveFollowingFace(planar))vertices.Add(Point(transform.OfPoint(curve.GetEndPoint(0))));
                                    if(vertices.Count>=3)data.TopOutlines.Add(vertices.ToArray());
                                }
                            }
                            if(normal.Z<-.999999)data.BottomLevels.Add(transform.OfPoint(planar.Origin).Z);
                        }
                        else data.HasCurvedFaces=true;
                        using(var mesh=face.Triangulate())foreach(var vertex in mesh.Vertices)data.Points.Add(transform.OfPoint(vertex));
                    }
                }
            }
        }
        // Triangulated-mesh hull for diagnostics; physical face edges define boundaries.
        private static ImportPoint[] Hull(IEnumerable<XYZ> vertices)
        {
            var points=vertices.Select(Point).GroupBy(p=>Tuple.Create(Math.Round(p.X,4),Math.Round(p.Y,4))).Select(g=>g.First()).OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray();
            if(points.Length<3)return points;
            Func<ImportPoint,ImportPoint,ImportPoint,double> cross=(o,a,b)=>(a.X-o.X)*(b.Y-o.Y)-(a.Y-o.Y)*(b.X-o.X);
            var hull=new List<ImportPoint>();
            foreach(var p in points){while(hull.Count>=2 && cross(hull[hull.Count-2],hull[hull.Count-1],p)<=.001)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;
            for(int i=points.Length-2;i>=0;i--){var p=points[i];while(hull.Count>lower && cross(hull[hull.Count-2],hull[hull.Count-1],p)<=.001)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            hull.RemoveAt(hull.Count-1);return hull.ToArray();
        }
    }
}
