using System;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>H20 section conventions: 200 mm depth, 80 mm flanges,
    /// 40 mm flange thickness and a 27 mm web. Bounds come from drawing geometry.</summary>
    internal static class H20Drawing
    {
        internal static void Plan(DrawingContext dc,Point start,Point end,int count,bool active,double separation=3)
        {
            var delta=end-start;var normal=new Vector(-delta.Y,delta.X);normal.Normalize();normal*=separation;
            var pen=new Pen(active ? Brushes.DodgerBlue : Brushes.Goldenrod,active || count==2 ? 2 : 1.3);
            foreach(double side in count==2 ? new[] {-1.0,1} : new[] {0.0})dc.DrawLine(pen,start+normal*side,end+normal*side);
        }
        private static readonly Brush Flange=new SolidColorBrush(Color.FromRgb(231,183,70));
        private static readonly Brush Web=new SolidColorBrush(Color.FromRgb(185,137,73));
        private static readonly Brush Grain=new SolidColorBrush(Color.FromArgb(80,113,76,35));
        private static Pen Outline(bool selected)=>new Pen(selected ? Brushes.DodgerBlue : Brushes.SaddleBrown,selected ? 1.5 : .65);
        internal static void Longitudinal(DrawingContext dc,Rect bounds,bool selected=false)
        {
            if(bounds.Width<=0 || bounds.Height<=0)return;
            double flange=bounds.Height*.2;
            dc.DrawRectangle(Web,null,bounds);
            dc.DrawRectangle(Flange,null,new Rect(bounds.Left,bounds.Top,bounds.Width,flange));
            dc.DrawRectangle(Flange,null,new Rect(bounds.Left,bounds.Bottom-flange,bounds.Width,flange));
            var edge=Outline(selected);
            dc.DrawRectangle(null,edge,bounds);
            foreach(double y in new[] {bounds.Top+flange,bounds.Bottom-flange})
                dc.DrawLine(edge,new Point(bounds.Left,y),new Point(bounds.Right,y));
            if(bounds.Width>30 && bounds.Height>8)
                foreach(double y in new[] {bounds.Top+flange/2,bounds.Bottom-flange/2})
                    dc.DrawLine(new Pen(Grain,.5),new Point(bounds.Left+3,y),new Point(bounds.Right-3,y));
        }
        internal static void EndSection(DrawingContext dc,Rect bounds,bool selected=false)
        {
            if(bounds.Width<=0 || bounds.Height<=0)return;
            double flange=bounds.Height*.2,web=bounds.Width*27/80;
            var shape=new StreamGeometry();
            using(var path=shape.Open())
            {
                path.BeginFigure(new Point(bounds.Left,bounds.Top),true,true);
                double a=bounds.Left+(bounds.Width-web)/2,b=a+web;
                foreach(var p in new[] {new Point(bounds.Right,bounds.Top),new Point(bounds.Right,bounds.Top+flange),
                    new Point(b,bounds.Top+flange),new Point(b,bounds.Bottom-flange),new Point(bounds.Right,bounds.Bottom-flange),
                    new Point(bounds.Right,bounds.Bottom),new Point(bounds.Left,bounds.Bottom),new Point(bounds.Left,bounds.Bottom-flange),
                    new Point(a,bounds.Bottom-flange),new Point(a,bounds.Top+flange),new Point(bounds.Left,bounds.Top+flange)})path.LineTo(p,true,false);
            }
            shape.Freeze();dc.DrawGeometry(Flange,Outline(selected),shape);
            dc.DrawRectangle(Web,null,new Rect(bounds.Left+(bounds.Width-web)/2,bounds.Top+flange,web,bounds.Height-2*flange));
        }
    }
    /// <summary>Schematic Doka plan symbol anchored to calculated leg centres.</summary>
    internal static class TowerPlanDrawing
    {
        private static readonly Pen Frame=new Pen(Brushes.DarkCyan,1.1);
        private static readonly Pen Cross=new Pen(Brushes.Gold,1.1);
        private static void PairedLine(DrawingContext dc,Pen pen,Point a,Point b)
        {
            var delta=b-a;if(delta.Length<.1)return;
            var offset=new Vector(-delta.Y,delta.X);offset.Normalize();
            foreach(double side in new[] {-1.0,1.0})dc.DrawLine(pen,a+offset*side,b+offset*side);
        }
        internal static void Station(DrawingContext dc,Point a,Point b,bool rotated)=>PairedLine(dc,rotated ? Cross : Frame,a,b);
        internal static void BracedBay(DrawingContext dc,Point a,Point b,Point c,Point d,bool rotated)
        {
            // Rotated towers have their frame planes along the primary axis.
            var connecting=rotated ? Frame : Cross;
            PairedLine(dc,connecting,a,c);PairedLine(dc,connecting,b,d);
            PairedLine(dc,Cross,a,d);PairedLine(dc,Cross,b,c);
        }
        internal static void Leg(DrawingContext dc,Point at,bool verticalFrame)
        {
            double w=verticalFrame ? 6 : 10,h=verticalFrame ? 10 : 6;
            var bounds=new Rect(at.X-w/2,at.Y-h/2,w,h);
            dc.DrawRectangle(Brushes.White,Frame,bounds);
            dc.DrawLine(new Pen(Brushes.DarkCyan,.65),new Point(bounds.Left+1,at.Y),new Point(bounds.Right-1,at.Y));
            dc.DrawLine(new Pen(Brushes.DarkCyan,.65),new Point(at.X,bounds.Top+1),new Point(at.X,bounds.Bottom-1));
            dc.DrawEllipse(Brushes.DarkCyan,null,at,1,1);
        }
    }

}
