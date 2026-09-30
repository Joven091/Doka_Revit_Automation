using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Typed, culture-independent axonometric schematic of the calculated layout.</summary>
    public sealed class BeamSupportDiagram : FrameworkElement
    {
        private BeamCalculatorViewModel model;
        public int DrawnPropCount { get; private set; }
        public int DrawnSlabCount { get; private set; }
        public int DrawnTowerLegCount { get; private set; }
        public void SetModel(BeamCalculatorViewModel value) { model = value; InvalidateVisual(); }
        private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex);
        private static readonly Brush Concrete = Brush("#AEB9C3"), ConcreteTop = Brush("#DAE1E6"), ConcreteSide = Brush("#85949F");
        private static readonly Brush Gold = Brush("#DCA923"), GoldTop = Brush("#FFE084"), GoldSide = Brush("#9D7015");
        private static readonly Brush Steel = Brush("#647B8D"), SteelTop = Brush("#ADBDCA"), SteelSide = Brush("#425764");
        private static Point Project(double x, double y, double z) => new Point(190 + .85 * x + .6 * y, 360 + .25 * x - .35 * y - z);
        private static void Face(DrawingContext dc, Brush fill, params Point[] points)
        {
            var shape = new StreamGeometry();
            using (var path = shape.Open()) { path.BeginFigure(points[0], true, true); for (int i = 1; i < points.Length; i++) path.LineTo(points[i], true, false); }
            shape.Freeze(); dc.DrawGeometry(fill, new Pen(Brush("#364650"), .55), shape);
        }
        private static void Box(DrawingContext dc, double x, double y, double z, double w, double l, double h, Brush front, Brush top, Brush side)
        {
            Face(dc, side, Project(x+w,y,z), Project(x+w,y+l,z), Project(x+w,y+l,z+h), Project(x+w,y,z+h));
            Face(dc, front, Project(x,y,z), Project(x+w,y,z), Project(x+w,y,z+h), Project(x,y,z+h));
            Face(dc, top, Project(x,y,z+h), Project(x+w,y,z+h), Project(x+w,y+l,z+h), Project(x,y+l,z+h));
        }
        private static void H20(DrawingContext dc, double x, double y, double z, double w, double l, bool alongBeam)
        {
            Box(dc,x,y,z,w,l,4,Gold,GoldTop,GoldSide);
            if (alongBeam) Box(dc,x+w*.36,y,z+4,w*.28,l,12,Gold,GoldTop,GoldSide);
            else Box(dc,x,y+l*.36,z+4,w,l*.28,12,Gold,GoldTop,GoldSide);
            Box(dc,x,y,z+16,w,l,4,Gold,GoldTop,GoldSide);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); DrawnPropCount = DrawnSlabCount = DrawnTowerLegCount = 0;
            if (model == null || !model.IsCalculationValid) return;
            if (model.UseTowers) { DrawTowerPlan(dc); return; }
            // Ratios across the beam are geometric; longitudinal length/prop height are schematic.
            double scale = Math.Min(ActualWidth / 470, ActualHeight / 440);
            if (scale <= 0) return;
            dc.PushTransform(new TranslateTransform((ActualWidth-470*scale)/2, (ActualHeight-440*scale)/2));
            dc.PushTransform(new ScaleTransform(scale,scale));
            double transverseScale = 240 / (model.Width + Math.Max(1400, model.HasAnySlab ? 2 * model.SlabStripWidthMm : 0));
            double bw = model.Width * transverseScale, strip = model.SlabStripWidthMm * transverseScale;
            double lineX = model.PrimaryLineSpacingMm * transverseScale / 2; // (beam width + 1400) / 4 after scaling.
            int count = model.NumberOfProps == "2" ? 2 : model.NumberOfProps == "3" ? 3 : 4;
            // Use the calculated span-to-overhang ratio, including 25 mm rounding.
            double ratio = model.PrimaryBeamCantilever / model.MaximumPropSpacing;
            double length = 250, span = length / (count-1+2*ratio), first = ratio*span;
            var ys = new List<double>(); for (int i=0;i<count;i++) ys.Add(first+i*span);
            // Back to front: props meet the underside of the primary H20 at z=150.
            for (int i=count-1;i>=0;i--) foreach (double x in new[]{-lineX,lineX})
            {
                double y=ys[i];
                Box(dc,x-9,y-8,0,18,16,3,Steel,SteelTop,SteelSide);
                Box(dc,x-3,y-3,3,6,6,147,Steel,SteelTop,SteelSide);
                Box(dc,x-8,y-7,147,16,14,3,Steel,SteelTop,SteelSide);
                DrawnPropCount++;
            }
            foreach(double x in new[]{-lineX,lineX}) H20(dc,x-7,0,150,14,length,true);
            // Secondary H20s run across the primary lines, perpendicular in plan.
            for(int i=4;i>=0;i--) H20(dc,-(model.Width+1400)*transverseScale/2,8+i*57,170,(model.Width+1400)*transverseScale,12,false);
            // A thin soffit sheet transfers the concrete beam load to the secondaries.
            Box(dc,-bw/2-3,0,190,bw+6,length,3,Brush("#8D6D45"),Brush("#C5A776"),Brush("#6D5131"));
            double depth = Math.Max(35, Math.Min(100, model.Depth * .11));
            double topZ = 193 + depth;
            // Optional monolithic slab wings are connected to the concrete beam.
            if(model.HasLeftSlab) { double h=depth*model.LeftSlabThickness/model.Depth; Box(dc,-bw/2-strip,0,topZ-h,strip,length,h,Concrete,ConcreteTop,ConcreteSide); DrawnSlabCount++; }
            Box(dc,-bw/2,0,193,bw,length,depth,Concrete,ConcreteTop,ConcreteSide);
            if(model.HasRightSlab) { double h=depth*model.RightSlabThickness/model.Depth; Box(dc,bw/2,0,topZ-h,strip,length,h,Concrete,ConcreteTop,ConcreteSide); DrawnSlabCount++; }
            dc.Pop(); dc.Pop();
        }
        private void DrawTowerPlan(DrawingContext dc)
        {
            var layout = model.TowerResult; if (layout == null || ActualWidth < 80 || ActualHeight < 80) return;
            double scale = Math.Min((ActualWidth - 50) / layout.LengthMm, (ActualHeight - 95) / Math.Max(layout.WidthMm, model.Width));
            double x0 = (ActualWidth - layout.LengthMm * scale) / 2, cy = ActualHeight / 2 - 15;
            double top = cy - layout.WidthMm * scale / 2, bottom = cy + layout.WidthMm * scale / 2;
            var frame = new Pen(SteelSide, 2); var cross = new Pen(Steel, 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(ConcreteTop, null, new Rect(x0, cy - model.Width * scale / 2, layout.LengthMm * scale, model.Width * scale));
            foreach (double y in new[] { top, bottom }) foreach (double offset in new[] { -3.0, 3.0 })
                dc.DrawLine(new Pen(Gold, 2), new Point(x0, y + offset), new Point(x0 + layout.LengthMm * scale, y + offset));
            for (int i = 0; i < layout.PositionsMm.Length; i++)
            {
                double x = x0 + layout.PositionsMm[i] * scale;
                dc.DrawLine(layout.Rotated ? cross : frame, new Point(x, top), new Point(x, bottom));
                foreach (double y in new[] { top, bottom }) { dc.DrawEllipse(SteelSide, null, new Point(x, y), 3.5, 3.5); DrawnTowerLegCount++; }
                Label(dc, (i + 1).ToString(), x - 4, top - 22);
                if (i < layout.Joined.Length && layout.Joined[i])
                {
                    double next = x0 + layout.PositionsMm[i + 1] * scale;
                    foreach (double y in new[] { top, bottom }) dc.DrawLine(layout.Rotated ? frame : cross, new Point(x, y), new Point(next, y));
                }
            }
            dc.DrawLine(new Pen(Steel, 1), new Point(x0, bottom + 25), new Point(x0 + layout.LengthMm * scale, bottom + 25));
            Label(dc, layout.LengthMm.ToString("0.###") + " mm", ActualWidth / 2 - 30, bottom + 30);
            Label(dc, layout.WidthMm.ToString("0") + " mm between primary lines", x0, Math.Max(0, top - 45));
        }
        private void Label(DrawingContext dc, string value, double x, double y)
        {
            dc.DrawText(new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, SteelSide, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        }
    }
}
