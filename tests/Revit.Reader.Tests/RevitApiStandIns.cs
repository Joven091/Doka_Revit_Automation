using System;
using System.Collections.Generic;
using System.Linq;

// Only this test executable sees these types. It compiles the unchanged
// production reader source against a controlled host; the shipping add-in
// separately builds against Autodesk's actual Revit 2024 API packages.
namespace Autodesk.Revit.DB
{
    public sealed class ElementId { public long Value {get;} public ElementId(long value){Value=value;} }
    public enum BuiltInCategory {OST_StructuralFraming=-2001320,OST_StructuralColumns=-2001330,OST_Columns=-2000100}
    public enum BuiltInParameter {ALL_MODEL_MARK,STRUCTURAL_SECTION_COMMON_HEIGHT,FAMILY_HEIGHT_PARAM}
    public enum StorageType {Double,String}
    public enum ViewDetailLevel {Fine}
    public sealed class ForgeTypeId {}
    public static class SpecTypeId {public static readonly ForgeTypeId Length=new ForgeTypeId();}
    public sealed class Definition {public ForgeTypeId GetDataType()=>SpecTypeId.Length;}
    public sealed class Parameter
    {
        public bool HasValue {get;set;}=true;
        public StorageType StorageType {get;set;}=StorageType.Double;
        public Definition Definition {get;}=new Definition();
        public double Number {get;set;}
        public string Text {get;set;}
        public double AsDouble()=>Number;
        public string AsString()=>Text;
    }
    public sealed class Category {public ElementId Id {get;set;} public string Name {get;set;}}
    public class Element
    {
        public ElementId Id {get;set;}
        public Category Category {get;set;}
        public Location Location {get;set;}
        public string Name {get;set;}="Rectangular";
        public Parameter Depth {get;set;}
        public Func<GeometryElement> Geometry {get;set;}
        public ElementId GetTypeId()=>new ElementId(-1);
        public Parameter get_Parameter(BuiltInParameter id)=>id==BuiltInParameter.ALL_MODEL_MARK ? null : Depth;
        public IList<Parameter> GetParameters(string name)=>new List<Parameter>();
        public GeometryElement get_Geometry(Options options)=>Geometry();
    }
    public sealed class FamilySymbol {public string FamilyName {get;set;}="Rectangular";}
    public sealed class FamilyInstance:Element {public FamilySymbol Symbol {get;}=new FamilySymbol();}
    public sealed class Floor:Element {}
    public sealed class Wall:Element {}
    public sealed class View {public string Name {get;set;}="3D";}
    public sealed class Document
    {
        public string Title {get;set;}="Controlled reader host";
        public Func<ElementId,Element> Read {get;set;}
        public Element GetElement(ElementId id)=>Read(id);
    }
    public sealed class Options:IDisposable
    {
        public ViewDetailLevel DetailLevel {get;set;}
        public bool IncludeNonVisibleObjects {get;set;}
        public bool ComputeReferences {get;set;}
        public void Dispose() {}
    }
    public abstract class Location {}
    public sealed class LocationCurve:Location {public Curve Curve {get;set;}}
    public abstract class Curve:IDisposable {public abstract XYZ GetEndPoint(int index); public void Dispose() {}}
    public sealed class Line:Curve
    {
        public XYZ Start {get;set;}
        public XYZ End {get;set;}
        public override XYZ GetEndPoint(int index)=>index==0 ? Start : End;
    }
    public sealed class XYZ
    {
        public double X {get;}
        public double Y {get;}
        public double Z {get;}
        public XYZ(double x,double y,double z){X=x;Y=y;Z=z;}
        public static readonly XYZ BasisZ=new XYZ(0,0,1);
        public double DotProduct(XYZ other)=>X*other.X+Y*other.Y+Z*other.Z;
        public XYZ Normalize(){double length=Math.Sqrt(DotProduct(this));return new XYZ(X/length,Y/length,Z/length);}
        public static XYZ operator +(XYZ a,XYZ b)=>new XYZ(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static XYZ operator -(XYZ a,XYZ b)=>new XYZ(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static XYZ operator *(XYZ a,double b)=>new XYZ(a.X*b,a.Y*b,a.Z*b);
    }
    public sealed class Transform
    {
        public static readonly Transform Identity=new Transform();
        public XYZ OfPoint(XYZ point)=>point;
        public XYZ OfVector(XYZ point)=>point;
        public Transform Multiply(Transform other)=>this;
    }
    public abstract class GeometryObject {}
    public sealed class GeometryElement:List<GeometryObject>,IDisposable {public void Dispose() {}}
    public sealed class GeometryInstance:GeometryObject
    {
        public Transform Transform {get;set;}=Transform.Identity;
        public GeometryElement Symbol {get;set;}
        public GeometryElement GetSymbolGeometry()=>Symbol;
    }
    public sealed class Solid:GeometryObject {public double Volume {get;set;} public List<Face> Faces {get;}=new List<Face>();}
    public class Face
    {
        public EdgeArrayArray EdgeLoops {get;}=new EdgeArrayArray();
        public List<XYZ> Vertices {get;}=new List<XYZ>();
        public Mesh Triangulate(){var result=new Mesh();result.Vertices.AddRange(Vertices);return result;}
    }
    public sealed class PlanarFace:Face {public XYZ FaceNormal {get;set;} public XYZ Origin {get;set;} public double Area {get;set;}}
    public sealed class EdgeArrayArray:List<EdgeArray> {public int Size=>Count;}
    public sealed class EdgeArray:List<Edge> {}
    public sealed class Edge
    {
        public XYZ Start {get;set;}
        public XYZ End {get;set;}
        public Curve AsCurveFollowingFace(PlanarFace face)=>new Line {Start=Start,End=End};
    }
    public sealed class Mesh:IDisposable {public List<XYZ> Vertices {get;}=new List<XYZ>();public void Dispose() {}}
}
namespace Autodesk.Revit.UI.Selection
{
    public sealed class Selection
    {
        public ICollection<Autodesk.Revit.DB.ElementId> Ids {get;set;}=new List<Autodesk.Revit.DB.ElementId>();
        public ICollection<Autodesk.Revit.DB.ElementId> GetElementIds()=>Ids;
    }
}
namespace Autodesk.Revit.UI
{
    public sealed class HostApplication {public string VersionName {get;set;}="Revit 2024";public string VersionBuild {get;set;}="Controlled host";}
    public sealed class UIDocument
    {
        public Autodesk.Revit.DB.Document Document {get;set;}
        public Autodesk.Revit.DB.View ActiveView {get;set;}=new Autodesk.Revit.DB.View();
        public Selection.Selection Selection {get;set;}=new Selection.Selection();
    }
    public sealed class UIApplication {public UIDocument ActiveUIDocument {get;set;} public HostApplication Application {get;set;}=new HostApplication();}
}
