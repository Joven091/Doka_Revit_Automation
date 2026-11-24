using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    public abstract class H20Schematic : FrameworkElement
    {
        private readonly List<(Geometry Shape, bool? Primary)> faces = new List<(Geometry, bool?)>();
        protected FlexCalculatorViewModel model;
        protected Matrix view = Matrix.Identity;

        protected void ClearFaces() => faces.Clear();
        protected void AddFace(Geometry shape, bool? primary) => faces.Add((shape, primary));

        internal bool? BeamAt(Point point)
        {
            if (model?.IsCalculationValid != true || !view.HasInverse) return null;
            var inverse = view;
            inverse.Invert();
            point = inverse.Transform(point);
            for (int i = faces.Count - 1; i >= 0; i--)
                if (faces[i].Shape.Bounds.Contains(point) && faces[i].Shape.FillContains(point)) return faces[i].Primary;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var primary = BeamAt(e.GetPosition(this));
            Cursor = primary.HasValue ? Cursors.Hand : null;
            ToolTip = primary.HasValue ? (primary.Value ? "Primary H20 diagrams" : "Secondary H20 diagrams") : null;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (OpenBeamAt(e.GetPosition(this))) e.Handled = true;
        }

        internal bool OpenBeamAt(Point point)
        {
            var primary = BeamAt(point);
            if (!primary.HasValue) return false;
            var command = primary.Value ? model.ViewPrimaryBeamDiagramCommand : model.ViewSecondaryBeamDiagramCommand;
            if (!command.CanExecute(null)) return false;
            command.Execute(null);
            return true;
        }
    }

    /// <summary>Slab cutaway over perpendicular H20 layers. Display axes are normalized independently;
    /// this illustrates support relationships, not a placement drawing or a quantity take-off.</summary>
    public sealed class SlabSupportDiagram : H20Schematic
    {
        private bool? drawnLayer;
        private bool trackingFaces;
        public int DrawnPrimaryCount { get; private set; }
        public int DrawnPropCount { get; private set; }
        public int DrawnSecondaryCount { get; private set; }
        public int DrawnSecondaryMemberCount { get; private set; }
        // Zero-based support ranges share a bearing support at each illustrated joint.
        internal static int[][] SecondarySupportRanges(int supportCount)
        {
            if (supportCount < 2) throw new ArgumentOutOfRangeException(nameof(supportCount));
            var ranges = new System.Collections.Generic.List<int[]>();
            for (int first = 0; first < supportCount - 1; first += 2)
                ranges.Add(new[] { first, Math.Min(first + 2, supportCount - 1) });
            return ranges.ToArray();
        }
        internal static Rect[] SecondaryMemberPlans(int supportCount, double span, double overhang, double rowY, double beamWidth)
        {
            var ranges = SecondarySupportRanges(supportCount);
            double axisScale = 240 / ((supportCount - 1) * span + 2 * overhang);
            // Display lap only: extend both members past the shared support without reaching another support.
            double halfLap = Math.Min(14, span * axisScale * .2);
            var plans = new Rect[ranges.Length];
            for (int i = 0; i < ranges.Length; i++)
            {
                double x0 = ranges[i][0] == 0 ? -120 : -120 + (overhang + ranges[i][0] * span) * axisScale - halfLap;
                double x1 = ranges[i][1] == supportCount - 1 ? 120 : -120 + (overhang + ranges[i][1] * span) * axisScale + halfLap;
                double offset = ranges.Length == 1 ? 0 : (i % 2 == 0 ? .6 : -.6) * beamWidth;
                plans[i] = new Rect(x0,rowY+offset-beamWidth/2,x1-x0,beamWidth);
            }
            return plans;
        }
        public void SetModel(FlexCalculatorViewModel value)
        {
            model = value;
            ClearFaces();
            DrawnPrimaryCount = DrawnPropCount = DrawnSecondaryCount = DrawnSecondaryMemberCount = 0;
            InvalidateVisual();
        }
        private static Brush Color(string hex) { var brush = (Brush)new BrushConverter().ConvertFromString(hex); brush.Freeze(); return brush; }
        private static readonly Brush Concrete = Color("#AEB9C3"), ConcreteTop = Color("#DAE1E6"), ConcreteSide = Color("#85949F");
        private static readonly Brush Gold = Color("#DCA923"), GoldTop = Color("#FFE084"), GoldSide = Color("#9D7015");
        private static readonly Brush Steel = Color("#647B8D"), SteelTop = Color("#ADBDCA"), SteelSide = Color("#425764");
        private static readonly Brush Sheet = Color("#8D6D45"), SheetTop = Color("#C5A776"), SheetSide = Color("#6D5131");
        private static readonly Pen Edge = new Pen(Color("#364650"), .55);
        private static Point Project(double x, double y, double z) => new Point(190 + .85 * x + .6 * y, 365 + .25 * x - .35 * y - z);
        private void Face(DrawingContext dc, Brush fill, params Point[] points)
        {
            var shape = new StreamGeometry();
            using (var path = shape.Open()) { path.BeginFigure(points[0], true, true); for (int i = 1; i < points.Length; i++) path.LineTo(points[i], true, false); }
            shape.Freeze(); dc.DrawGeometry(fill, Edge, shape);
            if (trackingFaces) AddFace(shape, drawnLayer);
        }
        private void Box(DrawingContext dc, double x, double y, double z, double width, double length, double height, Brush front, Brush top, Brush side)
        {
            Face(dc, side, Project(x+width,y,z), Project(x+width,y+length,z), Project(x+width,y+length,z+height), Project(x+width,y,z+height));
            Face(dc, front, Project(x,y,z), Project(x+width,y,z), Project(x+width,y,z+height), Project(x,y,z+height));
            Face(dc, top, Project(x,y,z+height), Project(x+width,y,z+height), Project(x+width,y+length,z+height), Project(x,y+length,z+height));
        }
        private void H20(DrawingContext dc, double x, double y, double z, double width, double length, bool primary)
        {
            drawnLayer = primary;
            Box(dc,x,y,z,width,length,4,Gold,GoldTop,GoldSide);
            if (primary) Box(dc,x+width*.36,y,z+4,width*.28,length,12,Gold,GoldTop,GoldSide);
            else Box(dc,x,y+length*.36,z+4,width,length*.28,12,Gold,GoldTop,GoldSide);
            Box(dc,x,y,z+16,width,length,4,Gold,GoldTop,GoldSide);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); ClearFaces(); trackingFaces = false; drawnLayer = null; DrawnPrimaryCount = DrawnPropCount = DrawnSecondaryCount = DrawnSecondaryMemberCount = 0;
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
            view = new Matrix(scale, 0, 0, scale, (ActualWidth-(maxX-minX)*scale)/2-minX*scale,
                (ActualHeight-(maxY-minY)*scale)/2-minY*scale);
            dc.PushTransform(new MatrixTransform(view));
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
            trackingFaces = true; // Later sheet and concrete faces mask the timber beneath them.
            for (int i = 0; i < primaryCount; i++)
            {
                double x = -120 + (model.SecondaryBeamCantilever + i * model.SuggestedPrimaryBeamSpacing) * xScale;
                H20(dc,x-7,0,propHeight,14,250,true); DrawnPrimaryCount++;
            }
            // Show sheet joints and the shorter closing spans; cap schematic density.
            var stations=PlywoodSpacing.SheetSupportRows(-40,along+40,model.SecondaryBeamSpacing);
            if(stations.Length>18) stations=Enumerable.Range(0,18).Select(i=>stations[(int)Math.Round(i*(stations.Length-1)/17.0)]).ToArray();
            int secondaryCount=stations.Length;
            bool joined = primaryCount > 3;
            double secondaryWidth = joined ? Math.Min(10,250.0/(secondaryCount+1)/2.4) : 10;
            double edgeInset = joined ? 1.1 * secondaryWidth : 0;
            for (int i = secondaryCount - 1; i >= 0; i--)
            {
                double y = stations[i] * yScale;
                y = edgeInset + y * (250 - 2 * edgeInset) / 250;
                var plans = SecondaryMemberPlans(primaryCount,model.SuggestedPrimaryBeamSpacing,model.SecondaryBeamCantilever,y,secondaryWidth);
                // Draw the far member first. Overlapping lengths occupy adjacent lanes at the same elevation.
                for (int member = 0; member < plans.Length; member++)
                {
                    var plan = plans[member];
                    H20(dc,plan.X,plan.Y,propHeight+20,plan.Width,plan.Height,false);
                    DrawnSecondaryMemberCount++;
                }
                DrawnSecondaryCount++;
            }
            // Front portion removed to expose both H20 layers. The slab and sheet remain in contact.
            drawnLayer = null;
            Box(dc,-120,95,propHeight+40,240,155,3,Sheet,SheetTop,SheetSide);
            Box(dc,-120,95,propHeight+43,240,155,slabHeight,Concrete,ConcreteTop,ConcreteSide);
            dc.Pop();
        }
    }
}
