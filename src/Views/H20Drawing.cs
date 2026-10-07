using System;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>H20 section conventions: 200 mm depth, 80 mm flanges,
    /// 40 mm flange thickness and a 27 mm web. Bounds come from drawing geometry.</summary>
    internal static class H20Drawing
    {
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
}
