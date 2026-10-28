// Explicit test-only API stand-ins. These are never packaged with the add-in.
using System;
using System.Collections.Generic;
using System.Linq;
namespace Autodesk.Revit.DB
{
    public class XYZ
    {
        public double X,Y,Z;public XYZ(double x,double y,double z){X=x;Y=y;Z=z;}
        public static XYZ operator -(XYZ a,XYZ b)=>new XYZ(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public double DotProduct(XYZ other)=>X*other.X+Y*other.Y+Z*other.Z;
        public XYZ Normalize(){double n=Math.Sqrt(DotProduct(this));return new XYZ(X/n,Y/n,Z/n);}
    }
    public class ElementId {public long Value;public ElementId(long value){Value=value;}}
    public class Category {public string Name="Walls";}
    public class Element {public string UniqueId,Name;public ElementId Id;public Category Category=new Category();}
    public class Location {}
    public class LocationCurve:Location {public Curve Curve;}
    public abstract class Curve {public abstract double Length{get;}public abstract XYZ GetEndPoint(int i);public abstract IList<XYZ> Tessellate();}
    public class Line:Curve
    {
        public XYZ Start,End;public override double Length=>Math.Sqrt((End-Start).DotProduct(End-Start));public override XYZ GetEndPoint(int i)=>i==0?Start:End;public override IList<XYZ> Tessellate()=>new[]{Start,End};
    }
    public class Arc:Curve {public override double Length=>1;public override XYZ GetEndPoint(int i)=>new XYZ(i,0,0);public override IList<XYZ> Tessellate()=>new[]{GetEndPoint(0),GetEndPoint(1)};}
    public enum WallKind {Basic,Curtain,Stacked}
    public class WallType {public WallKind Kind;}
    public enum ShellLayerType {Exterior,Interior}
    public class GeometryObject {}
    public class Edge {public Curve Curve;public Curve AsCurve()=>Curve;}
    public class EdgeArray:List<Edge> {}
    public class EdgeArrayArray:List<EdgeArray> {public int Size=>Count;}
    public class PlanarFace:GeometryObject {public XYZ FaceNormal=new XYZ(0,1,0);public EdgeArrayArray EdgeLoops=new EdgeArrayArray();}
    public class Reference {public GeometryObject Geometry;}
    public class HostObject:Element {}
    public class Wall:HostObject
    {
        public WallType WallType=new WallType();public Location Location;
        public Dictionary<ShellLayerType,List<Reference>> Sides=new Dictionary<ShellLayerType,List<Reference>>();
        public GeometryObject GetGeometryObjectFromReference(Reference reference)=>reference.Geometry;
    }
    public static class HostObjectUtils {public static IList<Reference> GetSideFaces(HostObject host,ShellLayerType side)=>((Wall)host).Sides[side];}
    public class View {public string Name="Plan";}
    public class Document
    {
        public bool IsWorkshared;public Guid WorksharingCentralGUID=Guid.NewGuid(),CreationGUID=Guid.NewGuid();public string Title="Source",PathName="C:\\Source.rvt";
        public List<Element> Elements=new List<Element>();public Element GetElement(ElementId id)=>Elements.FirstOrDefault(e=>e.Id.Value==id.Value);public Element GetElement(string id)=>Elements.FirstOrDefault(e=>e.UniqueId==id);
    }
}
namespace Autodesk.Revit.UI
{
    public class Selection {public List<DB.ElementId> Ids=new List<DB.ElementId>();public int Sets;public ICollection<DB.ElementId> GetElementIds()=>Ids;public void SetElementIds(ICollection<DB.ElementId> ids){Ids=ids.ToList();Sets++;}}
    public class UIDocument {public DB.Document Document=new DB.Document();public DB.View ActiveView=new DB.View();public Selection Selection=new Selection();public int Located;public void ShowElements(ICollection<DB.ElementId> ids){Located++;}}
    public class ApplicationInfo {public string VersionName="Revit test stand-in",VersionBuild="2024";}
    public class UIApplication {public UIDocument ActiveUIDocument=new UIDocument();public ApplicationInfo Application=new ApplicationInfo();}
}
