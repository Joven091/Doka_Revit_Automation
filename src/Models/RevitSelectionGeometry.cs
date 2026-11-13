using System;
using System.Linq;

namespace EngineeringDashboard
{
    internal static class RevitSelectionGeometry
    {
        internal const double PrecisionMm=1;
        internal static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        internal static string Number(double value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
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
            if(p==null || p.Length<3 || p.Length>64 || p.Any(v=>v==null || !Finite(v.X) || !Finite(v.Y)))return false;
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
            if(!Convex(outline,tolerance) || mesh==null || mesh.Length==0 || !Finite(projectedArea) || projectedArea<=0)return false;
            // The area allowance follows the same linear edge tolerance. Every
            // projected mesh vertex must also match the real face perimeter;
            // an enlarged base, remote component or genuine recess cannot be
            // accepted merely by increasing an area-equality threshold.
            if(Math.Abs(Area(outline)-projectedArea)>Perimeter(outline)*tolerance+Math.PI*tolerance*tolerance)return false;
            double sign=Math.Sign(SignedArea(outline));
            foreach(var v in mesh)
            {
                if(v==null || !Finite(v.X) || !Finite(v.Y))return false;
                for(int i=0;i<outline.Length;i++)
                    if(sign*Cross(outline[i],outline[(i+1)%outline.Length],v)/Distance(outline[i],outline[(i+1)%outline.Length])< -tolerance)return false;
            }
            return true;
        }
    }
}
