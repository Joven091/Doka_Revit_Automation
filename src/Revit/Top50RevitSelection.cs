using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    // All methods run synchronously in the dashboard's ExternalEvent callback.
    // Returned values are detached data; no Revit geometry is used by a worker.
    internal static class Top50RevitSelection
    {
        private const double Mm=304.8;
        internal static string DocumentKey(Document document)
        {
            if(document.IsWorkshared&&document.WorksharingCentralGUID!=Guid.Empty)return "central:"+document.WorksharingCentralGUID;
            return "document:"+document.CreationGUID;
        }
        private static UIDocument Project(UIApplication app)
            =>app?.ActiveUIDocument??throw new InvalidOperationException("Open a Revit project before using wall selection.");
        private static RevitWallSnapshot Snapshot(UIApplication app)
        {
            var ui=Project(app);return new RevitWallSnapshot{DocumentKey=DocumentKey(ui.Document),DocumentTitle=ui.Document.Title,DocumentPath=ui.Document.PathName,RevitVersion=app.Application.VersionName+" "+app.Application.VersionBuild,ViewName=ui.ActiveView?.Name,CapturedUtc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)};
        }
        internal static RevitWallSnapshot ReadSelected(UIApplication app)
        {
            var ui=Project(app);var source=Snapshot(app);var selected=ui.Selection.GetElementIds().OrderBy(id=>id.Value).ToArray();source.SelectedIds=selected.Select(id=>id.Value.ToString(CultureInfo.InvariantCulture)).ToArray();
            var walls=new List<RevitWallRecord>();var notes=new List<string>();
            foreach(var id in selected)
            {
                var element=ui.Document.GetElement(id);
                if(element is Wall wall)walls.Add(ReadWall(wall));
                else notes.Add("Skipped element "+id.Value+" ("+(element?.Category?.Name??"unavailable")+"). Select native Revit walls; linked model contents are not imported in this version.");
            }
            if(walls.Count==0)notes.Add("No native Revit walls were selected.");source.Walls=walls.ToArray();source.Notes=notes.ToArray();return source;
        }
        internal static RevitWallSnapshot Refresh(UIApplication app,RevitWallRequest request)
        {
            var ui=Project(app);RequireDocument(ui.Document,request.DocumentKey);var source=Snapshot(app);
            source.Walls=request.UniqueIds.Distinct(StringComparer.Ordinal).Select(id=>ui.Document.GetElement(id) is Wall wall?ReadWall(wall):new RevitWallRecord{UniqueId=id,Problem="The original Revit wall is missing or is no longer a wall."}).ToArray();
            source.SelectedIds=source.Walls.Select(w=>w.ElementId??"missing: "+w.UniqueId).ToArray();return source;
        }
        internal static RevitHighlightResult Highlight(UIApplication app,RevitWallRequest request)
        {
            var ui=Project(app);RequireDocument(ui.Document,request.DocumentKey);
            var unique=request.UniqueIds.Distinct(StringComparer.Ordinal).ToArray();
            var walls=unique.Select(id=>ui.Document.GetElement(id)).OfType<Wall>().ToArray();
            if(walls.Length>0)
            {
                var ids=walls.Select(w=>w.Id).ToArray();ui.Selection.SetElementIds(ids);
                if(request.Locate)ui.ShowElements(ids);
            }
            return new RevitHighlightResult{Requested=unique.Length,Found=walls.Length,Message=walls.Length+" of "+unique.Length+" Revit walls selected."+(walls.Length<unique.Length?" Some walls are missing; refresh the imported walls and check Diagnose.":"")+(walls.Length>0&&!request.Locate?" Current view kept; use Locate if the walls are outside this view.":"")};
        }
        private static void RequireDocument(Document document,string key)
        {if(DocumentKey(document)!=key)throw new InvalidOperationException("The active Revit project is different from this offer's source. Open the original project before refreshing or highlighting its walls.");}
        private static RevitWallRecord ReadWall(Wall wall)
        {
            var record=new RevitWallRecord{UniqueId=wall.UniqueId,ElementId=wall.Id.Value.ToString(CultureInfo.InvariantCulture),Label=wall.Name};var details=new StringBuilder();
            try
            {
                if(wall.WallType.Kind!=WallKind.Basic)throw new InvalidOperationException("Only straight basic walls are supported. Review curtain or stacked walls separately.");
                if(!(wall.Location is LocationCurve location)||!(location.Curve is Line line))throw new InvalidOperationException("Curved walls need a separate reviewed planning length.");
                var start=line.GetEndPoint(0);var end=line.GetEndPoint(1);var delta=end-start;
                if(Math.Abs(delta.Z)>.001)throw new InvalidOperationException("Sloping wall axes need a separate reviewed planning length.");
                var axis=delta.Normalize();record.CentreLengthMm=Number(line.Length*Mm);
                details.AppendLine("Axis start (ft): "+Point(start)+"; end (ft): "+Point(end));
                record.ExteriorLengthMm=FaceLength(wall,ShellLayerType.Exterior,axis,details);
                record.InteriorLengthMm=FaceLength(wall,ShellLayerType.Interior,axis,details);
                details.AppendLine("Openings are retained in face geometry and do not deduct from the overall planning length.");
            }
            catch(Exception error){record.Problem=error.Message;details.AppendLine(error.ToString());}
            record.GeometryDetails=details.ToString();return record;
        }
        private static decimal FaceLength(Wall wall,ShellLayerType side,XYZ axis,StringBuilder details)
        {
            var projections=new List<double>();
            foreach(var reference in HostObjectUtils.GetSideFaces(wall,side))
            {
                if(!(wall.GetGeometryObjectFromReference(reference) is PlanarFace face)||Math.Abs(face.FaceNormal.Z)>1e-6||Math.Abs(face.FaceNormal.DotProduct(axis))>1e-6)
                    throw new InvalidOperationException("The "+side+" forming face is not straight and vertical. Review this wall separately.");
                int points=0;
                foreach(EdgeArray loop in face.EdgeLoops)foreach(Edge edge in loop)foreach(var point in edge.AsCurve().Tessellate()){projections.Add(point.DotProduct(axis));points++;}
                details.AppendLine(side+": planar face; normal "+Point(face.FaceNormal)+"; loops="+face.EdgeLoops.Size+"; edge points="+points);
            }
            if(projections.Count<2)throw new InvalidOperationException("The "+side+" forming face could not be measured.");
            decimal result=Number((projections.Max()-projections.Min())*Mm);
            if(result<=0)throw new InvalidOperationException("The "+side+" forming face has no usable length.");
            details.AppendLine(side+" projected limits (mm): "+Number(projections.Min()*Mm)+" to "+Number(projections.Max()*Mm)+"; length="+result);return result;
        }
        private static decimal Number(double value)=>decimal.Round((decimal)value,3,MidpointRounding.AwayFromZero);
        private static string Point(XYZ p)=>p.X.ToString("R",CultureInfo.InvariantCulture)+", "+p.Y.ToString("R",CultureInfo.InvariantCulture)+", "+p.Z.ToString("R",CultureInfo.InvariantCulture);
    }
}
