using System;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Slab cutaway over perpendicular H20 layers. Display axes are normalized independently;
    /// this illustrates support relationships, not a placement drawing or a quantity take-off.</summary>
    public sealed class SlabSupportDiagram : FrameworkElement
    {
        private FlexCalculatorViewModel model;
        public int DrawnPrimaryCount { get; private set; }
        public int DrawnPropCount { get; private set; }
        public int DrawnSecondaryCount { get; private set; }
        public void SetModel(FlexCalculatorViewModel value)
        {
            model = value;
            DrawnPrimaryCount = DrawnPropCount = DrawnSecondaryCount = 0;
            InvalidateVisual();
        }
        private static Brush Color(string hex) { var brush = (Brush)new BrushConverter().ConvertFromString(hex); brush.Freeze(); return brush; }
        private static readonly Brush Concrete = Color("#AEB9C3"), ConcreteTop = Color("#DAE1E6"), ConcreteSide = Color("#85949F");
        private static readonly Brush Gold = Color("#DCA923"), GoldTop = Color("#FFE084"), GoldSide = Color("#9D7015");
        private static readonly Brush Steel = Color("#647B8D"), SteelTop = Color("#ADBDCA"), SteelSide = Color("#425764");
        private static readonly Brush Sheet = Color("#8D6D45"), SheetTop = Color("#C5A776"), SheetSide = Color("#6D5131");
        private static readonly Pen Edge = new Pen(Color("#364650"), .55);
        private static Point Project(double x, double y, double z) => new Point(190 + .85 * x + .6 * y, 365 + .25 * x - .35 * y - z);
        private static void Face(DrawingContext dc, Brush fill, params Point[] points)
        {
            var shape = new StreamGeometry();
            using (var path = shape.Open()) { path.BeginFigure(points[0], true, true); for (int i = 1; i < points.Length; i++) path.LineTo(points[i], true, false); }
            shape.Freeze(); dc.DrawGeometry(fill, Edge, shape);
        }
        private static void Box(DrawingContext dc, double x, double y, double z, double width, double length, double height, Brush front, Brush top, Brush side)
        {
            Face(dc, side, Project(x+width,y,z), Project(x+width,y+length,z), Project(x+width,y+length,z+height), Project(x+width,y,z+height));
            Face(dc, front, Project(x,y,z), Project(x+width,y,z), Project(x+width,y,z+height), Project(x,y,z+height));
            Face(dc, top, Project(x,y,z+height), Project(x+width,y,z+height), Project(x+width,y+length,z+height), Project(x,y+length,z+height));
        }
        private static void H20(DrawingContext dc, double x, double y, double z, double width, double length, bool primary)
        {
            Box(dc,x,y,z,width,length,4,Gold,GoldTop,GoldSide);
            if (primary) Box(dc,x+width*.36,y,z+4,width*.28,length,12,Gold,GoldTop,GoldSide);
            else Box(dc,x,y+length*.36,z+4,width,length*.28,12,Gold,GoldTop,GoldSide);
            Box(dc,x,y,z+16,width,length,4,Gold,GoldTop,GoldSide);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); DrawnPrimaryCount = DrawnPropCount = DrawnSecondaryCount = 0;
            if (model == null || !model.IsCalculationValid) return;
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            int primaryCount = model.PrimaryBeamCount == "5+" ? 5 : int.Parse(model.PrimaryBeamCount);
            int propCount = model.PropCount == "5+" ? 5 : int.Parse(model.PropCount);
            double across = (primaryCount - 1) * model.SuggestedPrimaryBeamSpacing + 2 * model.SecondaryBeamCantilever;
            double along = (propCount - 1) * model.PropSpacing + 2 * model.PrimaryBeamCantilever;
            // Keep the entered support/overhang proportions on each axis while making the section readable.
            double xScale = 240 / across, yScale = 250 / along;
            double propHeight = Math.Max(110, Math.Min(195, model.PropExtensionMm / 16));
            double slabHeight = Math.Max(16, Math.Min(48, model.SlabThickness / 10));
            // Fit the projected bounds, including plates and the tallest slab, without clipping.
            double minX = Project(-128,-7,0).X, maxX = Project(128,257,0).X;
            double minY = Project(-128,257,propHeight+43+slabHeight).Y, maxY = Project(128,-7,0).Y;
            double scale = Math.Min(ActualWidth / (maxX-minX+24), ActualHeight / (maxY-minY+24));
            dc.PushTransform(new TranslateTransform((ActualWidth-(maxX-minX)*scale)/2-minX*scale,
                (ActualHeight-(maxY-minY)*scale)/2-minY*scale));
            dc.PushTransform(new ScaleTransform(scale, scale));
            for (int j = propCount - 1; j >= 0; j--)
            for (int i = 0; i < primaryCount; i++)
            {
                double x = -120 + (model.SecondaryBeamCantilever + i * model.SuggestedPrimaryBeamSpacing) * xScale;
                double y = (model.PrimaryBeamCantilever + j * model.PropSpacing) * yScale;
                Box(dc,x-8,y-7,0,16,14,3,Steel,SteelTop,SteelSide);
                Box(dc,x-3,y-3,3,6,6,propHeight-6,Steel,SteelTop,SteelSide);
                Box(dc,x-8,y-7,propHeight-3,16,14,3,Steel,SteelTop,SteelSide);
                DrawnPropCount++;
            }
            for (int i = 0; i < primaryCount; i++)
            {
                double x = -120 + (model.SecondaryBeamCantilever + i * model.SuggestedPrimaryBeamSpacing) * xScale;
                H20(dc,x-7,0,propHeight,14,250,true); DrawnPrimaryCount++;
            }
            // Symmetrically centre the secondary pattern; cap visual density, never infer procurement quantities.
            double intervals = Math.Floor(along / model.SecondaryBeamSpacing);
            bool dense = intervals >= 18; // Includes overflow of along/spacing for tiny positive spacing.
            int secondaryCount = dense ? 18 : (int)intervals + 1;
            double occupied = dense ? along : intervals * model.SecondaryBeamSpacing;
            double start = (along - occupied) / 2;
            for (int i = secondaryCount - 1; i >= 0; i--)
            {
                double y = (secondaryCount == 1 ? along / 2 : start + occupied * i / (secondaryCount - 1)) * yScale;
                H20(dc,-120,y-5,propHeight+20,240,10,false); DrawnSecondaryCount++;
            }
            // Front portion removed to expose both H20 layers. The slab and sheet remain in contact.
            Box(dc,-120,95,propHeight+40,240,155,3,Sheet,SheetTop,SheetSide);
            Box(dc,-120,95,propHeight+43,240,155,slabHeight,Concrete,ConcreteTop,ConcreteSide);
            dc.Pop(); dc.Pop();
        }
    }
}
