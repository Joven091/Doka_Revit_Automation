using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace EngineeringDashboard
{
    // Revit is read on its command thread. Everything below operates on detached mm data.
    public sealed class ImportPoint
    {
        public double X {get;set;}
        public double Y {get;set;}
        public ImportPoint() {}
        public ImportPoint(double x,double y) {X=x;Y=y;}
    }
    public sealed class RevitReferenceShape
    {
        public string SourceId {get;set;}
        public string Label {get;set;}
        public string Kind {get;set;}
        public ImportPoint[] Outline {get;set;}
    }
    public sealed class RevitSourceMapping
    {
        public string CanvasLabel {get;set;}
        public string SourceId {get;set;}
        public string SourceLabel {get;set;}
    }
    public sealed class RevitImportContext
    {
        public string DocumentTitle {get;set;}
        public double OriginXmm {get;set;}
        public double OriginYmm {get;set;}
        public double DirectionX {get;set;}
        public double DirectionY {get;set;}
        public double TopElevationMm {get;set;}
        public RevitReferenceShape[] References {get;set;}=new RevitReferenceShape[0];
        public RevitSourceMapping[] Sources {get;set;}=new RevitSourceMapping[0];
        internal void Validate()
        {
            if(new[] {OriginXmm,OriginYmm,DirectionX,DirectionY,TopElevationMm}.Any(v=>!TowerBeamAnalysis.Finite(v))
                || Math.Abs(DirectionX*DirectionX+DirectionY*DirectionY-1)>1e-6 || References==null || References.Length>100
                || Sources==null || Sources.Length>100 || (DocumentTitle?.Length??0)>256)throw new ArgumentException("Invalid Revit import reference.");
            foreach(var r in References)
                if(r==null || r.Outline==null || r.Outline.Length<3 || r.Outline.Length>64 || (r.Label?.Length??0)>256 || (r.SourceId?.Length??0)>64
                    || r.Outline.Any(p=>p==null || !TowerBeamAnalysis.Finite(p.X) || !TowerBeamAnalysis.Finite(p.Y) || Math.Abs(p.X)>1000000 || Math.Abs(p.Y)>1000000))
                    throw new ArgumentException("Invalid column or wall reference outline.");
            if(Sources.Any(s=>s==null || (s.CanvasLabel?.Length??0)>32 || (s.SourceId?.Length??0)>64 || (s.SourceLabel?.Length??0)>256))
                throw new ArgumentException("Invalid Revit element reference.");
        }
    }
    internal sealed class RevitSelectedBeam
    {
        public string SourceId {get;set;}
        public string Label {get;set;}
        public ImportPoint Start {get;set;}
        public ImportPoint End {get;set;}
        public double LengthMm=>Start==null || End==null ? 0 : Math.Sqrt(Math.Pow(End.X-Start.X,2)+Math.Pow(End.Y-Start.Y,2));
        public double WidthMm {get;set;}
        public double DepthMm {get;set;}
        public double BottomMm {get;set;}
        public string Problem {get;set;}
    }
    internal sealed class RevitSelectedSlab
    {
        public string SourceId {get;set;}
        public string Label {get;set;}
        public ImportPoint[] Outline {get;set;}
        public double ThicknessMm {get;set;}
        public double TopMm {get;set;}
        public string Problem {get;set;}
    }
    internal sealed class RevitSelectedBoundary
    {
        public string SourceId {get;set;}
        public string Label {get;set;}
        public string Kind {get;set;}
        public ImportPoint[] Outline {get;set;}
        public double BottomMm {get;set;}
        public double TopMm {get;set;}
        public string Problem {get;set;}
    }
    internal sealed class RevitSelectionSnapshot
    {
        internal string DocumentTitle="";
        internal string RevitVersion="",RevitBuild="",ActiveView="",ReadException="";
        internal int SelectedCount=-1;
        internal string[] SelectedIds=new string[0];
        internal RevitSelectionElement[] Elements=new RevitSelectionElement[0];
        internal RevitSelectedBeam[] Beams=new RevitSelectedBeam[0];
        internal RevitSelectedSlab[] Slabs=new RevitSelectedSlab[0];
        internal RevitSelectedBoundary[] Boundaries=new RevitSelectedBoundary[0];
        internal string[] Problems=new string[0];
        internal string[] Notes=new string[0];
    }
    internal sealed class RevitSelectionElement
    {
        internal string SourceId="",Label="",Category="",FamilyType="",ElementClass="",Status="",Details="",Exception="";
    }
    internal sealed class RevitStructureImportResult
    {
        internal RevitImportArea[] Areas=new RevitImportArea[0];
        internal string AreaKey="",AreaLabel="";
        internal TransferNetworkConfiguration Configuration;
        internal double LengthMm;
        internal string[] Problems=new string[0];
        internal string[] Notes=new string[0];
        internal bool CanImport=>Configuration!=null && Problems.Length==0;
        internal string ConfigurationXml
        {
            get {if(!CanImport)throw new ArgumentException("Review the Revit selection before importing.");
                using(var text=new StringWriter(CultureInfo.InvariantCulture)){new XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,Configuration);return text.ToString();}}
        }
        internal void Apply(BeamCalculatorViewModel model)
        {
            if(model.IsOptimizing)throw new InvalidOperationException("Finish or cancel the current calculation before importing.");
            // Validate the complete replacement in a detached model first. Project settings survive.
            var xml=ConfigurationXml;
            var candidate=new BeamCalculatorViewModel {UseTowers=true,BeamLengthMm=LengthMm,UseBeamNetwork=Configuration.Beams.Length>1,TransferSlabLoads=Configuration.Beams.Length>1};
            candidate.NetworkConfigurationXml=xml;
            model.UseTowers=true;model.HasSelectedSupportSystem=true;model.BeamLengthMm=LengthMm;
            model.HasLeftSlab=model.HasRightSlab=false;
            model.UseBeamNetwork=Configuration.Beams.Length>1;model.TransferSlabLoads=Configuration.Beams.Length>1;
            model.NetworkConfigurationXml=xml;
        }
    }
    internal sealed class RevitImportArea
    {
        public string Key {get;set;}
        public string Label {get;set;}
        internal string DirectionId;
        internal string[] SourceIds;
        internal RevitStructureImportResult Result;
    }
    internal static class ImportPolygon
    {
        internal static ImportPoint[] StraightCorners(ImportPoint[] outline)
        {
            if(outline==null)return null;
            var points=outline.ToList();bool changed=true;
            // Revit joins split a straight face edge into several edges. Remove
            // redundant vertices only; preserve real chamfers, notches and cuts.
            while(changed && points.Count>3)
            {
                changed=false;
                for(int i=0;i<points.Count;i++)
                {
                    var a=points[(i+points.Count-1)%points.Count];var b=points[i];var c=points[(i+1)%points.Count];
                    if(a==null || b==null || c==null)return outline;
                    double length=Distance(a,c);
                    if(length>1e-7 && Math.Abs(Cross(a,c,b))/length<=1e-7
                        && (b.X-a.X)*(b.X-c.X)+(b.Y-a.Y)*(b.Y-c.Y)<=1e-7)
                    {points.RemoveAt(i);changed=true;break;}
                }
            }
            return points.ToArray();
        }
        internal static ImportPoint[] OrthogonalUnion(IEnumerable<ImportPoint[]> outlines,double tolerance)
        {
            var polygons=outlines.Where(p=>p!=null && p.Length>=3).ToArray();
            if(polygons.Length==0)return new ImportPoint[0];
            double[] Coordinates(IEnumerable<double> values)
            {
                var sorted=values.Distinct().OrderBy(v=>v).ToArray();var groups=new List<double>();
                for(int i=0;i<sorted.Length;){double first=sorted[i];groups.Add(first);while(i<sorted.Length && sorted[i]-first<=tolerance)i++;}
                return groups.ToArray();
            }
            var xs=Coordinates(polygons.SelectMany(p=>p.Select(v=>v.X)));var ys=Coordinates(polygons.SelectMany(p=>p.Select(v=>v.Y)));
            if(xs.Length<2 || ys.Length<2 || xs.Length*ys.Length>250000)return new ImportPoint[0];
            double Snap(double v,double[] axis)=>axis.OrderBy(c=>Math.Abs(c-v)).First();
            polygons=polygons.Select(p=>p.Select(v=>new ImportPoint(Snap(v.X,xs),Snap(v.Y,ys))).ToArray()).ToArray();
            if(polygons.Any(p=>Enumerable.Range(0,p.Length).Any(i=>Math.Abs(p[i].X-p[(i+1)%p.Length].X)>1e-7 && Math.Abs(p[i].Y-p[(i+1)%p.Length].Y)>1e-7)))return new ImportPoint[0];
            var edges=new HashSet<Tuple<int,int,int,int>>();
            void Edge(int x0,int y0,int x1,int y1)
            {if(!edges.Remove(Tuple.Create(x1,y1,x0,y0)))edges.Add(Tuple.Create(x0,y0,x1,y1));}
            for(int x=0;x<xs.Length-1;x++)for(int y=0;y<ys.Length-1;y++)
            {
                double cx=(xs[x]+xs[x+1])/2,cy=(ys[y]+ys[y+1])/2;
                if(!polygons.Any(p=>SlabFootprint.Contains(p,cx,cy)))continue;
                Edge(x,y,x+1,y);Edge(x+1,y,x+1,y+1);Edge(x+1,y+1,x,y+1);Edge(x,y+1,x,y);
            }
            if(edges.Count==0)return new ImportPoint[0];
            var outgoing=edges.GroupBy(e=>Tuple.Create(e.Item1,e.Item2)).ToDictionary(g=>g.Key,g=>g.ToArray());
            if(outgoing.Any(g=>g.Value.Length!=1))return new ImportPoint[0];
            var firstEdge=edges.OrderBy(e=>e.Item1).ThenBy(e=>e.Item2).First();var edge=firstEdge;var result=new List<ImportPoint>();
            do
            {
                result.Add(new ImportPoint(xs[edge.Item1],ys[edge.Item2]));edges.Remove(edge);
                if(!outgoing.TryGetValue(Tuple.Create(edge.Item3,edge.Item4),out var next))return new ImportPoint[0];
                edge=next[0];if(result.Count>outgoing.Count)return new ImportPoint[0];
            }while(!edge.Equals(firstEdge));
            // More than one loop means disconnected panels or an opening. Never
            // replace them by their bounding rectangle or convex hull.
            return edges.Count==0 ? StraightCorners(result.ToArray()) : new ImportPoint[0];
        }
        internal static double SignedArea(ImportPoint[] p)
        {
            if(p==null || p.Length<3)return 0;
            var o=p[0];double twice=0;
            for(int i=0;i<p.Length;i++){var a=p[i];var b=p[(i+1)%p.Length];twice+=(a.X-o.X)*(b.Y-o.Y)-(a.Y-o.Y)*(b.X-o.X);}
            return twice/2;
        }
        internal static double Area(ImportPoint[] p)=>Math.Abs(SignedArea(p));
        internal static double Perimeter(ImportPoint[] p)=>Enumerable.Range(0,p.Length).Sum(i=>Distance(p[i],p[(i+1)%p.Length]));
        private static double Distance(ImportPoint a,ImportPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
        private static double Cross(ImportPoint a,ImportPoint b,ImportPoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        internal static bool Convex(ImportPoint[] p,double tolerance)
        {
            if(p==null || p.Length<3 || p.Length>64 || p.Any(v=>v==null || !TowerBeamAnalysis.Finite(v.X) || !TowerBeamAnalysis.Finite(v.Y)))return false;
            double area=SignedArea(p);if(Math.Abs(area)<1e-6)return false;double sign=Math.Sign(area);
            for(int i=0;i<p.Length;i++)
            {
                var a=p[i];var b=p[(i+1)%p.Length];double length=Distance(a,b);if(length<1e-7)return false;
                if(p.Any(v=>sign*Cross(a,b,v)/length< -tolerance))return false;
            }
            return true;
        }
        internal static bool BoundaryMatches(ImportPoint[] outline,ImportPoint[] mesh,double projectedArea,double tolerance)
        {
            if(!Convex(outline,tolerance) || mesh==null || mesh.Length==0 || !TowerBeamAnalysis.Finite(projectedArea) || projectedArea<=0)return false;
            // The area allowance follows the same linear edge tolerance. Every
            // projected mesh vertex must also match the real face perimeter;
            // an enlarged base, remote component or genuine recess cannot be
            // accepted merely by increasing an area-equality threshold.
            if(Math.Abs(Area(outline)-projectedArea)>Perimeter(outline)*tolerance+Math.PI*tolerance*tolerance)return false;
            double sign=Math.Sign(SignedArea(outline));
            foreach(var v in mesh)
            {
                if(v==null || !TowerBeamAnalysis.Finite(v.X) || !TowerBeamAnalysis.Finite(v.Y))return false;
                for(int i=0;i<outline.Length;i++)
                    if(sign*Cross(outline[i],outline[(i+1)%outline.Length],v)/Distance(outline[i],outline[(i+1)%outline.Length])< -tolerance)return false;
            }
            return true;
        }
        internal static ImportPoint[] ClipConvex(ImportPoint[] subject,ImportPoint[] boundary,double inset)
        {
            var points=subject.ToList();double sign=Math.Sign(SignedArea(boundary));
            for(int edge=0;edge<boundary.Length && points.Count>0;edge++)
            {
                var a=boundary[edge];var b=boundary[(edge+1)%boundary.Length];double length=Distance(a,b);
                Func<ImportPoint,double> distance=p=>sign*Cross(a,b,p)/length-inset;
                var clipped=new List<ImportPoint>();
                for(int i=0;i<points.Count;i++)
                {
                    var p=points[i];var q=points[(i+1)%points.Count];double dp=distance(p),dq=distance(q);
                    if(dp>=0)clipped.Add(p);
                    if(dp*dq<0){double f=dp/(dp-dq);clipped.Add(new ImportPoint(p.X+(q.X-p.X)*f,p.Y+(q.Y-p.Y)*f));}
                }
                points=clipped;
            }
            return points.ToArray();
        }
        internal static bool InteriorOverlap(ImportPoint[] slab,ImportPoint[] boundary,double x0,double x1,double y0,double y1,double tolerance)
        {
            if(x1<=x0 || y1<=y0)return false;
            // Apply the linear tolerance once, at the boundary perimeter. Also
            // shrinking the bay would silently turn 1 mm into a 2 mm allowance.
            var interior=SlabFootprint.Clip(slab,x0,x1,y0,y1);
            return interior.Length>=3 && Area(ClipConvex(interior,boundary,tolerance))>1e-6;
        }
    }
    internal static class SlabFootprint
    {
        internal static double EndAllowance(double thickness)=>TowerBeamAnalysis.Finite(thickness) && thickness>0
            ? PlywoodSpacing.SlabEndStripLimitMm(25*thickness/1000+BeamCalculatorViewModel.HeapingLoad(25*thickness/1000)+.75+.4,null,0) : 0;
        internal static bool Contains(ImportPoint[] p,double x,double y)
        {
            if(p==null)return true;bool inside=false;
            for(int i=0,j=p.Length-1;i<p.Length;j=i++)
            {
                var a=p[j];var b=p[i];double cross=(x-a.X)*(b.Y-a.Y)-(y-a.Y)*(b.X-a.X);
                if(Math.Abs(cross)<1e-5 && x>=Math.Min(a.X,b.X)-1e-6 && x<=Math.Max(a.X,b.X)+1e-6 && y>=Math.Min(a.Y,b.Y)-1e-6 && y<=Math.Max(a.Y,b.Y)+1e-6)return true;
                if((a.Y>y)!=(b.Y>y) && x<(b.X-a.X)*(y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
            }
            return inside;
        }
        internal static bool ValidEndCutbacks(ImportPoint[] p,double allowance)
        {
            if(p==null || p.Length<4 || p.Length>64 || p.Any(v=>v==null || !TowerBeamAnalysis.Finite(v.X) || !TowerBeamAnalysis.Finite(v.Y)))return false;
            double x0=p.Min(v=>v.X),x1=p.Max(v=>v.X),y0=p.Min(v=>v.Y),y1=p.Max(v=>v.Y);
            if(x1-x0<=1 || y1-y0<=1 || allowance<=0)return false;
            // Validate the ordered polygon. Disallow crossings, duplicate edges and
            // interior notches; only the four end corners may remove concrete.
            for(int i=0;i<p.Length;i++)
            {
                var a=p[i];var b=p[(i+1)%p.Length];if(Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)<1e-5)return false;
                for(int j=i+1;j<p.Length;j++)
                {
                    if(j==i+1 || i==0 && j==p.Length-1)continue;
                    var c=p[j];var d=p[(j+1)%p.Length];
                    double abC=Cross(a,b,c),abD=Cross(a,b,d),cdA=Cross(c,d,a),cdB=Cross(c,d,b);
                    if(abC*abD<0 && cdA*cdB<0)return false;
                    if(Math.Abs(abC)<1e-6 && Between(c,a,b) || Math.Abs(abD)<1e-6 && Between(d,a,b))return false;
                }
            }
            var xs=p.Select(v=>v.X).Concat(new[] {x0+allowance,x1-allowance}).Where(x=>x>=x0 && x<=x1).Distinct().OrderBy(x=>x).ToArray();
            var ys=p.Select(v=>v.Y).Concat(new[] {y0+allowance,y1-allowance}).Where(y=>y>=y0 && y<=y1).Distinct().OrderBy(y=>y).ToArray();
            if(!Contains(p,(x0+x1)/2,(y0+y1)/2))return false;
            for(int i=1;i<xs.Length;i++)for(int j=1;j<ys.Length;j++)
            {
                double x=(xs[i-1]+xs[i])/2,y=(ys[j-1]+ys[j])/2;
                if(!Contains(p,x,y) && !(Math.Min(x-x0,x1-x)<=allowance+1 && Math.Min(y-y0,y1-y)<=allowance+1))return false;
            }
            return true;
        }
        private static double Cross(ImportPoint a,ImportPoint b,ImportPoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        private static bool Between(ImportPoint p,ImportPoint a,ImportPoint b)=>p.X>=Math.Min(a.X,b.X)-1e-6 && p.X<=Math.Max(a.X,b.X)+1e-6 && p.Y>=Math.Min(a.Y,b.Y)-1e-6 && p.Y<=Math.Max(a.Y,b.Y)+1e-6;
        internal static ImportPoint[] Clip(ImportPoint[] outline,double x0,double x1,double y0,double y1)
        {
            var p=outline.ToList();
            foreach(var edge in new[] {Tuple.Create(0,x0,1),Tuple.Create(0,x1,-1),Tuple.Create(1,y0,1),Tuple.Create(1,y1,-1)})
            {
                var next=new List<ImportPoint>();if(p.Count==0)break;
                Func<ImportPoint,double> distance=v=>((edge.Item1==0 ? v.X : v.Y)-edge.Item2)*edge.Item3;
                for(int i=0;i<p.Count;i++)
                {
                    var a=p[i];var b=p[(i+1)%p.Count];double da=distance(a),db=distance(b);
                    if(da>=-1e-7)next.Add(new ImportPoint(a.X,a.Y));
                    if(da*db<0){double f=da/(da-db);next.Add(new ImportPoint(a.X+(b.X-a.X)*f,a.Y+(b.Y-a.Y)*f));}
                }
                p=next;
            }
            return p.Select(v=>new ImportPoint(Math.Max(x0,Math.Min(x1,v.X)),Math.Max(y0,Math.Min(y1,v.Y))))
                .GroupBy(v=>Tuple.Create(Math.Round(v.X,6),Math.Round(v.Y,6))).Select(g=>g.First()).ToArray();
        }
    }
    internal static class RevitStructureImport
    {
        internal const double ToleranceMm=1;
        private sealed class Box
        {
            internal double X0,X1,Y0,Y1;
            internal Box(ImportPoint[] p) {X0=p.Min(v=>v.X);X1=p.Max(v=>v.X);Y0=p.Min(v=>v.Y);Y1=p.Max(v=>v.Y);}
            internal bool Covers(double x0,double x1,double y0,double y1)=>X0<=x0+ToleranceMm && X1>=x1-ToleranceMm && Y0<=y0+ToleranceMm && Y1>=y1-ToleranceMm;
        }
        private sealed class Beam
        {
            internal RevitSelectedBeam Source;
            internal RevitSelectedBeam[] Parts;
            internal double Start,End,Centre,Bottom;
        }
        private static List<Beam> ConnectedBeams(List<Beam> beams,List<string> notes)
        {
            var result=new List<Beam>();
            foreach(var beam in beams.OrderBy(b=>b.Centre).ThenBy(b=>b.Start).ThenBy(b=>b.Source.SourceId,StringComparer.Ordinal))
            {
                var preceding=result.LastOrDefault(b=>Math.Abs(b.Centre-beam.Centre)<=ToleranceMm && Math.Abs(b.Source.WidthMm-beam.Source.WidthMm)<=ToleranceMm
                    && Math.Abs(b.Source.DepthMm-beam.Source.DepthMm)<=ToleranceMm && Math.Abs(b.Bottom-beam.Bottom)<=ToleranceMm
                    && beam.Start<=b.End+ToleranceMm && beam.End>=b.Start-ToleranceMm);
                if(preceding==null){result.Add(beam);continue;}
                preceding.Start=Math.Min(preceding.Start,beam.Start);preceding.End=Math.Max(preceding.End,beam.End);
                preceding.Parts=preceding.Parts.Concat(beam.Parts).ToArray();
                notes.Add("Connected beam segments: "+string.Join(", ",preceding.Parts.Select(p=>p.SourceId))+". Original Revit IDs retained.");
            }
            return result.OrderBy(b=>b.Centre).ThenBy(b=>b.Source.SourceId,StringComparer.Ordinal).ToList();
        }
        internal static string DefaultDirection(RevitSelectionSnapshot snapshot)
        {
            return snapshot.Beams.Where(b=>b.Start!=null && b.End!=null).OrderByDescending(b=>snapshot.Beams
                .Where(c=>c.Start!=null && c.End!=null && Parallel(b,c)).Sum(c=>Length(c))).ThenBy(b=>b.SourceId,StringComparer.Ordinal).FirstOrDefault()?.SourceId;
        }
        private static double Length(RevitSelectedBeam b)=>Math.Sqrt(Math.Pow(b.End.X-b.Start.X,2)+Math.Pow(b.End.Y-b.Start.Y,2));
        private static bool Parallel(RevitSelectedBeam a,RevitSelectedBeam b)
        {
            double la=Length(a),lb=Length(b);return la>0 && lb>0 && Math.Abs((a.End.X-a.Start.X)*(b.End.Y-b.Start.Y)-(a.End.Y-a.Start.Y)*(b.End.X-b.Start.X))<=la*lb*1e-6;
        }
        private static bool Finite(ImportPoint p)=>p!=null && TowerBeamAnalysis.Finite(p.X) && TowerBeamAnalysis.Finite(p.Y);
        // A rectangular floor must actually have four orthogonal corners, not merely a rectangular bounding box.
        private static bool Rectangle(ImportPoint[] outline)
        {
            outline=ImportPolygon.StraightCorners(outline);
            if(outline==null || outline.Length!=4 || outline.Any(p=>!Finite(p)))return false;
            var b=new Box(outline);return b.X1-b.X0>ToleranceMm && b.Y1-b.Y0>ToleranceMm
                && new[] {new ImportPoint(b.X0,b.Y0),new ImportPoint(b.X0,b.Y1),new ImportPoint(b.X1,b.Y0),new ImportPoint(b.X1,b.Y1)}
                    .All(c=>outline.Any(p=>Math.Abs(c.X-p.X)<=ToleranceMm && Math.Abs(c.Y-p.Y)<=ToleranceMm));
        }
        internal static RevitStructureImportResult Build(RevitSelectionSnapshot snapshot,string directionId=null)
        {
            var complete=BuildSingle(snapshot,directionId);
            if(complete.CanImport)return complete;
            var areas=ClearAreas(snapshot,directionId);
            if(areas.Length<2)return complete;
            var problems=areas.Where(a=>!a.Result.CanImport).SelectMany(a=>a.Result.Problems.Select(p=>a.Label+": "+p)).ToArray();
            if(problems.Length>0)return new RevitStructureImportResult {Areas=areas,Problems=problems};
            var drawings=areas.Select(a=>new RevitDrawingDefinition {Key=a.Key,Label=a.Label,LengthMm=a.Result.LengthMm,ConfigurationXml=a.Result.ConfigurationXml}).ToArray();
            var configuration=BeamCalculatorViewModel.ReadNetwork(drawings[0].ConfigurationXml);
            configuration.SupportFormatVersion=4;configuration.RevitDrawings=drawings;configuration.ActiveRevitDrawingKey=drawings[0].Key;
            return new RevitStructureImportResult {Configuration=configuration,LengthMm=drawings[0].LengthMm,Areas=areas,
                Notes=areas.SelectMany(a=>a.Result.Notes.Select(n=>a.Label+": "+n)).Concat(new[] {"Combined canvas: "+areas.Count(a=>a.Label.StartsWith("Area ",StringComparison.Ordinal))+" clear areas and "+areas.Count(a=>!a.Label.StartsWith("Area ",StringComparison.Ordinal))+" column-to-column beams. All members retain their Revit positions; columns separate the support calculations."}).ToArray()};
        }
        private static RevitImportArea[] ClearAreas(RevitSelectionSnapshot source,string directionId)
        {
            var chosen=source.Beams.FirstOrDefault(b=>b.SourceId==(directionId??DefaultDirection(source)));
            if(chosen==null || source.Beams.Length>45 || source.Slabs.Length==0 || source.Slabs.Length>30 || source.Boundaries.Length>100 || source.Problems.Length>0 || source.Beams.Any(b=>b.Problem!=null || !Finite(b.Start) || !Finite(b.End)
                || new[] {b.WidthMm,b.DepthMm,b.BottomMm}.Any(v=>!TowerBeamAnalysis.Finite(v)) || b.WidthMm<=0 || b.DepthMm<=0)
                || source.Slabs.Any(s=>s.Problem!=null || !TowerBeamAnalysis.Finite(s.ThicknessMm) || s.ThicknessMm<=0 || !TowerBeamAnalysis.Finite(s.TopMm))
                || source.Boundaries.Any(b=>b.Problem!=null || !ImportPolygon.Convex(b.Outline,ToleranceMm)))return new RevitImportArea[0];
            double length=Length(chosen);if(length<=ToleranceMm)return new RevitImportArea[0];
            double dx=(chosen.End.X-chosen.Start.X)/length,dy=(chosen.End.Y-chosen.Start.Y)/length;
            if(dx< -1e-6 || Math.Abs(dx)<=1e-6 && dy<0){dx=-dx;dy=-dy;}
            Func<ImportPoint,ImportPoint> local=p=>new ImportPoint((p.X-chosen.Start.X)*dx+(p.Y-chosen.Start.Y)*dy,-(p.X-chosen.Start.X)*dy+(p.Y-chosen.Start.Y)*dx);
            var parallel=new List<Beam>();var crossing=new List<Beam>();
            foreach(var s in source.Beams)
            {
                var a=local(s.Start);var b=local(s.End);bool along=Math.Abs(a.Y-b.Y)<=ToleranceMm,across=Math.Abs(a.X-b.X)<=ToleranceMm;
                if(!along && !across)return new RevitImportArea[0];
                (along ? parallel : crossing).Add(new Beam {Source=s,Parts=new[] {s},Start=along ? Math.Min(a.X,b.X) : Math.Min(a.Y,b.Y),
                    End=along ? Math.Max(a.X,b.X) : Math.Max(a.Y,b.Y),Centre=along ? (a.Y+b.Y)/2 : (a.X+b.X)/2,Bottom=s.BottomMm});
            }
            var floors=source.Slabs.Select(s=>Tuple.Create(s,s.Outline?.Select(local).ToArray())).ToArray();
            if(floors.Any(s=>!SlabFootprint.ValidEndCutbacks(s.Item2,SlabFootprint.EndAllowance(s.Item1.ThicknessMm))))return new RevitImportArea[0];
            double datum=source.Slabs[0].TopMm;
            if(source.Slabs.Any(s=>Math.Abs(s.TopMm-datum)>ToleranceMm) || source.Beams.Any(b=>Math.Abs(b.BottomMm+b.DepthMm-datum)>ToleranceMm))return new RevitImportArea[0];
            var nodes=ConnectedBeams(parallel,new List<string>()).ToArray();
            var edges=new List<Tuple<int,int,string[]>>();
            for(int i=0;i<nodes.Length;i++)for(int j=i+1;j<nodes.Length;j++)
            {
                var a=nodes[i];var b=nodes[j];
                if(b.Centre-a.Centre<=ToleranceMm || nodes.Any(n=>n.Centre>a.Centre+ToleranceMm && n.Centre<b.Centre-ToleranceMm))continue;
                double x0=Math.Max(a.Start,b.Start),x1=Math.Min(a.End,b.End),y0=a.Centre+a.Source.WidthMm/2,y1=b.Centre-b.Source.WidthMm/2;
                if(x1-x0<=ToleranceMm || y1-y0<500)continue;
                var panels=floors.Where(f=> {var box=new Box(f.Item2);return box.Y0<=y0+ToleranceMm && box.Y1>=y1-ToleranceMm && box.X1>x0+ToleranceMm && box.X0<x1-ToleranceMm;}).Select(f=>f.Item1.SourceId).ToArray();
                if(panels.Length>0)edges.Add(Tuple.Create(i,j,panels));
            }
            var visited=new HashSet<int>();var assigned=new HashSet<string>();var areas=new List<RevitImportArea>();int number=0;
            foreach(int seed in Enumerable.Range(0,nodes.Length))
            {
                if(visited.Contains(seed))continue;
                var component=new HashSet<int>();var pending=new Queue<int>();pending.Enqueue(seed);
                while(pending.Count>0)
                {
                    int node=pending.Dequeue();if(!component.Add(node))continue;visited.Add(node);
                    foreach(var edge in edges.Where(e=>e.Item1==node || e.Item2==node))pending.Enqueue(edge.Item1==node ? edge.Item2 : edge.Item1);
                }
                var cluster=component.Select(i=>nodes[i]).ToArray();
                var panelIds=new HashSet<string>(edges.Where(e=>component.Contains(e.Item1) && component.Contains(e.Item2)).SelectMany(e=>e.Item3));
                var members=cluster.SelectMany(n=>n.Parts).ToList();
                if(panelIds.Count>0)members.AddRange(crossing.Where(c=>cluster.Any(n=>c.Centre+c.Source.WidthMm/2>n.Start+ToleranceMm && c.Centre-c.Source.WidthMm/2<n.End-ToleranceMm
                    && c.End>=n.Centre-n.Source.WidthMm/2-ToleranceMm && c.Start<=n.Centre+n.Source.WidthMm/2+ToleranceMm)).Select(c=>c.Source));
                foreach(var member in members)assigned.Add(member.SourceId);
                string label=panelIds.Count>0 ? "Area "+(++number)+" · "+panelIds.Count+" slab panels" : "Beam "+string.Join(" + ",members.Select(m=>m.SourceId));
                AddArea(members.ToArray(),source.Slabs.Where(s=>panelIds.Contains(s.SourceId)).ToArray(),cluster[0].Source.SourceId,label);
            }
            foreach(var beam in source.Beams.Where(b=>!assigned.Contains(b.SourceId)))AddArea(new[] {beam},new RevitSelectedSlab[0],beam.SourceId,"Beam "+beam.SourceId+" · between columns");
            // A selected panel must belong to an offered drawing. Never enable a
            // partial import by silently discarding unexplained floor geometry.
            if(source.Slabs.Any(s=>!areas.Any(a=>a.SourceIds.Contains(s.SourceId))))return new RevitImportArea[0];
            return areas.OrderByDescending(a=>a.Label.StartsWith("Area ",StringComparison.Ordinal)).ThenBy(a=>a.Label,StringComparer.Ordinal).ToArray();
            void AddArea(RevitSelectedBeam[] members,RevitSelectedSlab[] panels,string axis,string label)
            {
                var selection=new RevitSelectionSnapshot {DocumentTitle=source.DocumentTitle,Beams=members,Slabs=panels,Boundaries=source.Boundaries};
                var ids=members.Select(b=>b.SourceId).Concat(panels.Select(s=>s.SourceId)).Distinct().OrderBy(id=>id,StringComparer.Ordinal).ToArray();
                areas.Add(new RevitImportArea {Key=string.Join("/",ids),Label=label,SourceIds=ids,DirectionId=axis,Result=BuildSingle(selection,axis)});
            }
        }
        private static RevitStructureImportResult BuildSingle(RevitSelectionSnapshot snapshot,string directionId=null)
        {
            var result=new RevitStructureImportResult();var problems=new List<string>(snapshot.Problems);var notes=new List<string>(snapshot.Notes);
            var chosen=snapshot.Beams.FirstOrDefault(b=>b.SourceId==(directionId??DefaultDirection(snapshot)));
            if(chosen==null || !Finite(chosen.Start) || !Finite(chosen.End) || Length(chosen)<ToleranceMm)
            {problems.Add("Select at least one straight, horizontal beam in Revit.");result.Problems=problems.ToArray();return result;}
            if(snapshot.Beams.Length>45 || snapshot.Slabs.Length>30 || snapshot.Boundaries.Length>100)
            {result.Problems=new[] {"Select a smaller beam/slab area (up to 15 parallel beams, 30 crossings and 100 boundary references)."};return result;}
            double dx=(chosen.End.X-chosen.Start.X)/Length(chosen),dy=(chosen.End.Y-chosen.Start.Y)/Length(chosen);
            if(dx< -1e-6 || Math.Abs(dx)<=1e-6 && dy<0){dx=-dx;dy=-dy;}
            double ox=chosen.Start.X,oy=chosen.Start.Y;
            Func<ImportPoint,ImportPoint> local=p=>new ImportPoint((p.X-ox)*dx+(p.Y-oy)*dy,-(p.X-ox)*dy+(p.Y-oy)*dx);
            var references=new List<RevitReferenceShape>();var boundaries=new List<Tuple<RevitSelectedBoundary,ImportPoint[]>>();
            foreach(var s in snapshot.Boundaries)
            {
                if(s.Problem!=null || !ImportPolygon.Convex(s.Outline,ToleranceMm)) {problems.Add(s.Label+": "+(s.Problem??"Cannot read a complete convex boundary footprint."));continue;}
                var outline=s.Outline.Select(local).ToArray();boundaries.Add(Tuple.Create(s,outline));
                references.Add(new RevitReferenceShape {SourceId=s.SourceId,Label=s.Label,Kind=s.Kind,Outline=outline});
            }
            var beams=new List<Beam>();var crosses=new List<Beam>();
            foreach(var s in snapshot.Beams)
            {
                if(s.Problem!=null || !Finite(s.Start) || !Finite(s.End)){problems.Add(s.Label+": "+(s.Problem??"Cannot read beam endpoints."));continue;}
                if(new[] {s.WidthMm,s.DepthMm,s.BottomMm}.Any(v=>!TowerBeamAnalysis.Finite(v)) || s.WidthMm<=0 || s.WidthMm>10000 || s.DepthMm<=0 || s.DepthMm>10000)
                {problems.Add(s.Label+": enter a valid width, depth and soffit elevation in the review table.");continue;}
                var a=local(s.Start);var b=local(s.End);
                bool along=Math.Abs(a.Y-b.Y)<=ToleranceMm,across=Math.Abs(a.X-b.X)<=ToleranceMm;
                if(!along && !across){problems.Add(s.Label+": diagonal beams are not supported by this canvas. Select a rectangular beam/slab area.");continue;}
                var beam=new Beam {Source=s,Parts=new[] {s},Start=along ? Math.Min(a.X,b.X) : Math.Min(a.Y,b.Y),End=along ? Math.Max(a.X,b.X) : Math.Max(a.Y,b.Y),Centre=along ? (a.Y+b.Y)/2 : (a.X+b.X)/2,Bottom=s.BottomMm};
                foreach(var boundary in boundaries)
                {
                    if(boundary.Item1.TopMm<s.BottomMm-ToleranceMm || boundary.Item1.BottomMm>s.BottomMm+s.DepthMm+ToleranceMm)continue;
                    var outline=along ? boundary.Item2 : boundary.Item2.Select(p=>new ImportPoint(p.Y,p.X)).ToArray();var box=new Box(outline);
                    if(box.Y1<=beam.Centre-s.WidthMm/2+ToleranceMm || box.Y0>=beam.Centre+s.WidthMm/2-ToleranceMm || box.X1<=beam.Start+ToleranceMm || box.X0>=beam.End-ToleranceMm)continue;
                    if(!Rectangle(outline) || box.Y0>beam.Centre-s.WidthMm/2+ToleranceMm || box.Y1<beam.Centre+s.WidthMm/2-ToleranceMm)
                    {problems.Add(s.Label+": "+boundary.Item1.Label+" has a partial or angled end face. Set this beam's clear limits manually in a separate drawing.");continue;}
                    if(box.X0<=beam.Start+ToleranceMm && box.X1<beam.End-ToleranceMm){beam.Start=box.X1;notes.Add(s.Label+": start at the face of "+boundary.Item1.Label+".");}
                    else if(box.X1>=beam.End-ToleranceMm && box.X0>beam.Start+ToleranceMm){beam.End=box.X0;notes.Add(s.Label+": end at the face of "+boundary.Item1.Label+".");}
                    else problems.Add(s.Label+": "+boundary.Item1.Label+" divides the selected span. Import one clear span at a time.");
                }
                if(beam.End-beam.Start<=ToleranceMm){problems.Add(s.Label+": no clear span remains between the selected boundaries.");continue;}
                (along ? beams : crosses).Add(beam);
            }
            beams=ConnectedBeams(beams,notes);
            if(beams.Count==0 || beams.Count>15){problems.Add("The drawing requires 1–15 parallel beams.");result.Problems=problems.Distinct().ToArray();return result;}
            double start=beams.Min(b=>b.Start),end=beams.Max(b=>b.End),length=end-start;
            if(length>50000)problems.Add("The clear beam length exceeds the canvas limit of 50,000 mm.");
            if(beams.Zip(beams.Skip(1),(a,b)=>Math.Min(a.End,b.End)-Math.Max(a.Start,b.Start)).Any(overlap=>overlap<=ToleranceMm))problems.Add("Adjacent parallel beams need an overlapping clear span to support a slab bay.");
            var floorBoxes=new List<Tuple<RevitSelectedSlab,Box>>();
            var floorOutlines=new Dictionary<string,ImportPoint[]>();
            if(snapshot.Slabs.GroupBy(s=>s.SourceId).Any(g=>g.Count()>1))
            {result.Problems=problems.Concat(new[] {"Select exactly one slab element per Revit ID; duplicate slab entries cannot be imported."}).ToArray();return result;}
            foreach(var s in snapshot.Slabs)
            {
                var outline=s.Outline?.Select(local).ToArray();
                if(s.Problem!=null || !SlabFootprint.ValidEndCutbacks(outline,SlabFootprint.EndAllowance(s.ThicknessMm)) || !TowerBeamAnalysis.Finite(s.ThicknessMm) || s.ThicknessMm<=0 || !TowerBeamAnalysis.Finite(s.TopMm))
                {problems.Add(s.Label+": "+(s.Problem??"Use a flat rectangular slab or a slab with corner cutbacks, without openings; review its thickness."));continue;}
                floorBoxes.Add(Tuple.Create(s,new Box(outline)));floorOutlines[s.SourceId]=outline;
            }
            double datum=floorBoxes.Count>0 ? floorBoxes[0].Item1.TopMm : beams[0].Source.BottomMm+beams[0].Source.DepthMm;
            if(beams.Concat(crosses).Any(b=>Math.Abs(b.Source.BottomMm+b.Source.DepthMm-datum)>ToleranceMm)
                || floorBoxes.Any(s=>Math.Abs(s.Item1.TopMm-datum)>ToleranceMm))problems.Add("Selected beams and slabs must share a concrete top elevation. Import one level at a time.");
            var rows=beams.Select((b,i)=>new TransferBeamRow {Label="B"+(i+1),SpanStartMm=b.Start-start,SpanLengthMm=b.End-b.Start,WidthMm=b.Source.WidthMm,DepthMm=b.Source.DepthMm,BottomElevationMm=b.Bottom-datum,RevitSource=string.Join("; ",b.Parts.Select(p=>p.Label))}).ToArray();
            var bays=new List<TransferBayRow>();var mappings=new List<RevitSourceMapping>();var usedFloors=new HashSet<string>();
            for(int i=0;i<beams.Count;i++)foreach(var part in beams[i].Parts)mappings.Add(Map("B"+(i+1),part.SourceId,part.Label));
            for(int i=0;i<beams.Count-1;i++)
            {
                double y0=beams[i].Centre+rows[i].WidthMm/2,y1=beams[i+1].Centre-rows[i+1].WidthMm/2;
                if(y1-y0<500 || y1-y0>20000)problems.Add("B"+(i+1)+"–B"+(i+2)+": clear slab bay must be between 500 and 20,000 mm.");
                double bayStart=Math.Min(beams[i].Start,beams[i+1].Start),bayEnd=Math.Max(beams[i].End,beams[i+1].End);
                var covering=floorBoxes.Where(s=>s.Item2.Y0<=y0+ToleranceMm && s.Item2.Y1>=y1-ToleranceMm
                    && s.Item2.X1>bayStart+ToleranceMm && s.Item2.X0<bayEnd-ToleranceMm).ToArray();
                if(covering.Length==0){problems.Add("S"+(i+1)+": select slab panels covering the complete clear bay.");continue;}
                var floor=covering[0].Item1;
                if(covering.Any(p=>Math.Abs(p.Item1.ThicknessMm-floor.ThicknessMm)>ToleranceMm || Math.Abs(p.Item1.TopMm-floor.TopMm)>ToleranceMm))
                {problems.Add("S"+(i+1)+": slab panels must have matching thickness and top elevation.");continue;}
                var clipped=covering.Select(p=>SlabFootprint.Clip(floorOutlines[p.Item1.SourceId],bayStart,bayEnd,y0,y1)).ToArray();
                ImportPoint[] bayOutline;
                if(covering.Length==1 && covering[0].Item2.Covers(bayStart,bayEnd,y0,y1))bayOutline=clipped[0];
                else
                {
                    var strips=crosses.Where(c=>c.Start<=y0+ToleranceMm && c.End>=y1-ToleranceMm && c.Centre+c.Source.WidthMm/2>bayStart && c.Centre-c.Source.WidthMm/2<bayEnd)
                        .Select(c=>new[] {new ImportPoint(Math.Max(bayStart,c.Centre-c.Source.WidthMm/2),y0),new ImportPoint(Math.Min(bayEnd,c.Centre+c.Source.WidthMm/2),y0),
                            new ImportPoint(Math.Min(bayEnd,c.Centre+c.Source.WidthMm/2),y1),new ImportPoint(Math.Max(bayStart,c.Centre-c.Source.WidthMm/2),y1)});
                    bayOutline=ImportPolygon.OrthogonalUnion(clipped.Concat(strips),ToleranceMm);
                    if(!SlabFootprint.ValidEndCutbacks(bayOutline,SlabFootprint.EndAllowance(floor.ThicknessMm)) || !new Box(bayOutline).Covers(bayStart,bayEnd,y0,y1))
                    {problems.Add("S"+(i+1)+": selected slab panels and crossing beams do not cover a complete clear bay; a gap, opening or unsupported outline remains.");continue;}
                    notes.Add("S"+(i+1)+": "+covering.Length+" selected slab panels form one bay. Crossing strips retain their beam loads and are counted once.");
                }
                foreach(var panel in covering)usedFloors.Add(panel.Item1.SourceId);
                foreach(var boundary in boundaries)
                {
                    var box=new Box(boundary.Item2);
                    if(boundary.Item1.TopMm>=floor.TopMm-floor.ThicknessMm-ToleranceMm && boundary.Item1.BottomMm<=floor.TopMm+ToleranceMm
                        && box.X1>bayStart+ToleranceMm && box.X0<bayEnd-ToleranceMm && box.Y1>y0+ToleranceMm && box.Y0<y1-ToleranceMm
                        && clipped.Any(p=>ImportPolygon.InteriorOverlap(p,boundary.Item2,bayStart,bayEnd,y0,y1,ToleranceMm)))
                        problems.Add("S"+(i+1)+": "+boundary.Item1.Label+" intersects the bay interior. Select one clear rectangular area.");
                }
                if(floor.ThicknessMm>Math.Min(rows[i].DepthMm,rows[i+1].DepthMm))problems.Add(floor.Label+": slab is thicker than an adjacent beam.");
                var footprint=bayOutline.Select(p=>new ImportPoint(p.X-start,p.Y-y0)).ToArray();
                bays.Add(new TransferBayRow {WidthMm=y1-y0,ThicknessMm=floor.ThicknessMm,RevitSource=string.Join("; ",covering.Select(p=>p.Item1.Label)),Outline=footprint});
                if(!Rectangle(footprint))notes.Add("S"+(i+1)+": actual corner cutbacks retained for support placement; nominal rectangular bay loading retained.");
                foreach(var panel in covering)mappings.Add(Map("S"+(i+1),panel.Item1.SourceId,panel.Item1.Label));
            }
            foreach(var f in floorBoxes)if(!usedFloors.Contains(f.Item1.SourceId))problems.Add(f.Item1.Label+": this slab does not match a complete bay between the selected parallel beams.");
            var crossingRows=new List<TransferCrossingBeamRow>();
            foreach(var c in crosses.OrderBy(b=>b.Centre).ThenBy(b=>b.Start))
            {
                int matched=0;
                bool startsOnBeam=beams.Any(b=>c.Start>=b.Centre-b.Source.WidthMm/2-ToleranceMm && c.Start<=b.Centre+b.Source.WidthMm/2+ToleranceMm);
                bool endsOnBeam=beams.Any(b=>c.End>=b.Centre-b.Source.WidthMm/2-ToleranceMm && c.End<=b.Centre+b.Source.WidthMm/2+ToleranceMm);
                if(!startsOnBeam || !endsOnBeam)problems.Add(c.Source.Label+": crossing endpoints must meet selected parallel beams; partial bay segments cannot be imported.");
                for(int i=0;i<beams.Count-1;i++)
                {
                    double y0=beams[i].Centre+rows[i].WidthMm/2,y1=beams[i+1].Centre-rows[i+1].WidthMm/2;
                    if(c.Start>y0+ToleranceMm || c.End<y1-ToleranceMm)continue;
                    if(c.Centre-c.Source.WidthMm/2<Math.Max(beams[i].Start,beams[i+1].Start)-ToleranceMm || c.Centre+c.Source.WidthMm/2>Math.Min(beams[i].End,beams[i+1].End)+ToleranceMm){problems.Add(c.Source.Label+": the crossing lies outside the clear beam span.");continue;}
                    crossingRows.Add(new TransferCrossingBeamRow {BayNumber=i+1,PositionMm=c.Centre-start,WidthMm=c.Source.WidthMm,DepthMm=c.Source.DepthMm,BottomElevationMm=c.Bottom-datum,SupportMode=CrossBeamSupportMode.Automatic,RevitSource=c.Source.Label});
                    mappings.Add(Map("X"+crossingRows.Count,c.Source.SourceId,c.Source.Label));matched++;
                }
                if(matched==0 || c.Start<beams[0].Centre-rows[0].WidthMm/2-ToleranceMm || c.End>beams.Last().Centre+rows.Last().WidthMm/2+ToleranceMm)
                    problems.Add(c.Source.Label+": crossing endpoints must connect the selected parallel beams and cover complete slab bays.");
            }
            if(crossingRows.Count>30)problems.Add("Use at most 30 crossing beam segments.");
            foreach(var group in crossingRows.GroupBy(c=>c.BayNumber))
            {
                var ordered=group.OrderBy(c=>c.PositionMm).ToArray();for(int i=1;i<ordered.Length;i++)
                    if(ordered[i].PositionMm-ordered[i].WidthMm/2<ordered[i-1].PositionMm+ordered[i-1].WidthMm/2-ToleranceMm)problems.Add("Selected crossing beams overlap in slab bay S"+group.Key+".");
            }
            foreach(var r in references)foreach(var p in r.Outline){p.X-=start;p.Y-=beams[0].Centre;}
            var context=new RevitImportContext {DocumentTitle=snapshot.DocumentTitle,OriginXmm=ox+start*dx-beams[0].Centre*dy,OriginYmm=oy+start*dy+beams[0].Centre*dx,DirectionX=dx,DirectionY=dy,TopElevationMm=datum,References=references.ToArray(),Sources=mappings.ToArray()};
            try {context.Validate();}catch(ArgumentException ex){problems.Add(ex.Message);}
            result.LengthMm=length;result.Problems=problems.Distinct().ToArray();result.Notes=notes.Distinct().ToArray();
            if(result.Problems.Length==0)result.Configuration=new TransferNetworkConfiguration {SupportFormatVersion=3,PlanOriginYmm=0,Beams=rows,Bays=bays.ToArray(),CrossingBeams=crossingRows.ToArray(),IndependentBeams=new IndependentBeamRow[0],RevitContext=context};
            return result;
        }
        private static RevitSourceMapping Map(string canvas,string id,string label)=>new RevitSourceMapping {CanvasLabel=canvas,SourceId=id,SourceLabel=label};
    }
}
