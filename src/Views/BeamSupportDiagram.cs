using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace EngineeringDashboard
{
    /// <summary>Typed, culture-independent axonometric schematic of the calculated layout.</summary>
    public sealed class BeamSupportDiagram : FrameworkElement
    {
        private BeamCalculatorViewModel model;
        internal readonly List<Tuple<Rect, int, int, int>> MemberTargets = new List<Tuple<Rect, int, int, int>>();
        public event Action<int, int, int> MemberSelected;
        private int selectedRun = -1, selectedLine, selectedMember;
        private bool isSelectionEnabled;
        public bool IsSelectionEnabled
        {
            get => isSelectionEnabled;
            set { isSelectionEnabled = value; Cursor = value ? Cursors.Hand : Cursors.Arrow; }
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            if (model?.UseTowers != true) return base.MeasureOverride(availableSize);
            return new Size(Math.Max(MinWidth, double.IsInfinity(availableSize.Width) ? MinWidth : availableSize.Width), double.IsNaN(Height) ? 0 : Height);
        }
        public int DrawnEndDimensionCount { get; private set; }
        public void SelectMember(int run, int line, int member) { selectedRun = run; selectedLine = line; selectedMember = member; InvalidateVisual(); }
        internal bool SelectAt(Point point)
        {
            if (!IsSelectionEnabled) return false;
            foreach (var hit in MemberTargets) if (hit.Item1.Contains(point))
            { SelectMember(hit.Item2, hit.Item3, hit.Item4); MemberSelected?.Invoke(hit.Item2, hit.Item3, hit.Item4); return true; }
            return false;
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        { base.OnMouseLeftButtonDown(e); if (SelectAt(e.GetPosition(this))) e.Handled = true; }
        public int DrawnPropCount { get; private set; }
        public int DrawnSlabCount { get; private set; }
        public int DrawnTowerLegCount { get; private set; }
        public int DrawnStockPieceCount { get; private set; }
        public int DrawnBayDimensionCount { get; private set; }
        public int DrawnGapDimensionCount { get; private set; }
        public int DrawnTransferRowCount { get; private set; }
        public int DrawnFootCount { get; private set; }
        public int DrawnH20ProfileCount {get;private set;}
        public void SetModel(BeamCalculatorViewModel value)
        {
            model = value;
            double shortest = double.MaxValue;
            if (model?.UseTowers == true && model.IsCalculationValid && model.TowerResult != null)
            {
                foreach (var piece in model.TowerResult.StockPlan.Pieces) shortest = Math.Min(shortest, piece.StockLengthMm);
                var p = model.TowerResult.PositionsMm;
                for (int i = 1; i < p.Length; i++) shortest = Math.Min(shortest, p[i] - p[i - 1]);
            }
            // Long plans scroll horizontally so stock length labels remain readable.
            MinWidth = shortest < double.MaxValue ? Math.Max(50 + 78 * model.TowerResult.LengthMm / shortest, 50 + 190 * model.TowerResult.StockPlan.Pieces.Length) : 0;
            if (model?.TowerResult?.Transfer != null) MinWidth = Math.Max(1000, Math.Max(MinWidth, 60 + model.TowerResult.PositionsMm.Length * 90));
            Cursor = IsSelectionEnabled ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
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
            base.OnRender(dc); DrawnPropCount = DrawnSlabCount = DrawnTowerLegCount = DrawnStockPieceCount = 0;
            DrawnBayDimensionCount = DrawnGapDimensionCount = DrawnEndDimensionCount = 0; MemberTargets.Clear();
            DrawnTransferRowCount = DrawnFootCount = DrawnH20ProfileCount = 0;
            if(model?.TowerResult?.Transfer != null) dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
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
            double scale = (ActualWidth - 50) / layout.LengthMm;
            double planHeight = layout.Transfer == null ? ActualHeight : 350;
            double x0 = 25, cy = planHeight / 2 - 30;
            double extent = Math.Max(layout.WidthMm,model.Width);
            double transverseScale = Math.Min(scale, (planHeight - 200) / extent);
            double top = cy - layout.WidthMm * transverseScale / 2, bottom = cy + layout.WidthMm * transverseScale / 2;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            dc.DrawRectangle(ConcreteTop, null, new Rect(x0, cy - model.Width * transverseScale / 2, layout.LengthMm * scale, model.Width * transverseScale));
            if(layout.Transfer!=null) {Label(dc,$"mm · P = 2×H20/line · secondaries = single H20 @ {layout.Transfer.EnteredUpperSpacingMm:0}",x0,8);Label(dc,"Cyan: frames · yellow: crosses",x0,25);}
            if(layout.Transfer==null)Label(dc,"mm · cyan: frames · yellow: crosses",x0,8);
            for (int i = 0; i < layout.StockPlan.Pieces.Length; i++)
            {
                var piece = layout.StockPlan.Pieces[i];
                for (int line = 0; line < 2; line++) for (int member = 0; member < 2; member++)
                {
                    double y = line == 0 ? top : bottom, offset = member == 0 ? -5 : 5;
                    bool selected = i == selectedRun && line == selectedLine && (selectedMember < 0 || member == selectedMember);
                    dc.DrawLine(new Pen(selected ? Brushes.DodgerBlue : i % 2 == 0 ? Gold : GoldSide, selected ? 4 : 2), new Point(x0 + piece.StartMm * scale, y + offset), new Point(x0 + piece.EndMm * scale, y + offset));
                    MemberTargets.Add(Tuple.Create(new Rect(x0 + piece.StartMm * scale, y + offset - 4, piece.StockLengthMm * scale, 8), i, line, member));
                    DrawnStockPieceCount++;
                }
                double dimY = bottom + (layout.Transfer == null ? 65 : 55);
                double[] ends = { piece.StartMm, piece.EndMm }, legs = { layout.PositionsMm[piece.FirstSupport], layout.PositionsMm[piece.LastSupport] };
                for (int end = 0; end < 2; end++)
                {
                    double a = x0 + ends[end] * scale, b = x0 + legs[end] * scale;
                    dc.DrawLine(new Pen(Steel, 1), new Point(a, dimY), new Point(b, dimY));
                    foreach (double x in new[] { a, b }) dc.DrawLine(new Pen(Steel, 1), new Point(x, dimY - 3), new Point(x, dimY + 3));
                    Label(dc, Math.Abs(ends[end] - legs[end]).ToString("0.###") + (layout.Transfer==null ? " mm" : ""), (a + b) / 2, dimY - 16, true);
                    DrawnEndDimensionCount++;
                }
                Label(dc, "P" + (i + 1) + (layout.Transfer==null ? "\n2 × " : " · ") + piece.StockLengthMm.ToString("0.###") + (layout.Transfer==null ? " mm" : ""),
                    x0 + (piece.StartMm + piece.StockLengthMm / 2) * scale, bottom + 12, true);
                if (i > 0) dc.DrawLine(new Pen(ConcreteSide, 1) { DashStyle = DashStyles.Dot }, new Point(x0 + piece.StartMm * scale, top - 8), new Point(x0 + piece.StartMm * scale, bottom + 15));
            }
            for (int i = 0; i < layout.PositionsMm.Length; i++)
            {
                double x = x0 + layout.PositionsMm[i] * scale;
                TowerPlanDrawing.Station(dc,new Point(x,top),new Point(x,bottom),layout.Rotated);
                foreach (double y in new[] { top, bottom }) { TowerPlanDrawing.Leg(dc,new Point(x,y),!layout.Rotated); DrawnTowerLegCount++; }
                if(layout.Transfer==null)Label(dc, (i + 1).ToString(), x - 4, top - 22);
                if (i < layout.Joined.Length && layout.Joined[i])
                {
                    double next = x0 + layout.PositionsMm[i + 1] * scale;
                    TowerPlanDrawing.BracedBay(dc,new Point(x,top),new Point(x,bottom),new Point(next,top),new Point(next,bottom),layout.Rotated);
                }
                if (i < layout.Joined.Length && layout.Transfer==null)
                {
                    double next = x0 + layout.PositionsMm[i + 1] * scale, y = top - (layout.Transfer==null ? 32 : 14);
                    Brush colour=layout.Joined[i] ? Brushes.Firebrick : Steel;
                    dc.DrawLine(new Pen(colour, 1), new Point(x, y), new Point(next, y));
                    foreach (double end in new[] { x, next }) dc.DrawLine(new Pen(colour, 1), new Point(end, y - 3), new Point(end, y + 3));
                    Label(dc,(layout.PositionsMm[i+1]-layout.PositionsMm[i]).ToString("0.###"),(x+next)/2,y-15,true,colour);
                    if (layout.Joined[i]) DrawnBayDimensionCount++; else DrawnGapDimensionCount++;
                }
            }
            if(layout.Transfer!=null) for(int i=0;i<layout.Joined.Length;)
            {
                int count=1;double interval=layout.PositionsMm[i+1]-layout.PositionsMm[i],y=top-14;
                while(i+count<layout.Joined.Length && layout.Joined[i+count]==layout.Joined[i] && Math.Abs(layout.PositionsMm[i+count+1]-layout.PositionsMm[i+count]-interval)<1e-7)count++;
                double a=x0+layout.PositionsMm[i]*scale,b=x0+layout.PositionsMm[i+count]*scale;
                Brush colour=layout.Joined[i] ? Brushes.Firebrick : Steel;
                dc.DrawLine(new Pen(colour,1),new Point(a,y),new Point(b,y));
                for(int j=0;j<=count;j++) {double x=x0+layout.PositionsMm[i+j]*scale;dc.DrawLine(new Pen(colour,1),new Point(x,y-3),new Point(x,y+3));}
                Label(dc,$"{(count>1 ? count+"×" : "")}{interval:0.###}",(a+b)/2,y-15,true,colour);
                if(layout.Joined[i])DrawnBayDimensionCount+=count;else DrawnGapDimensionCount+=count;i+=count;
            }
            dc.DrawLine(new Pen(Steel, 1), new Point(x0, bottom + (layout.Transfer == null ? 91 : 121)), new Point(x0 + layout.LengthMm * scale, bottom + (layout.Transfer == null ? 91 : 121)));
            Label(dc, "Concrete beam: " + layout.LengthMm.ToString("0.###") + " mm", ActualWidth / 2, bottom + (layout.Transfer == null ? 96 : 126), true);
            Label(dc, (layout.Rotated ? "Cross" : "Frame") + " across: " + layout.WidthMm.ToString("0") + " mm · concrete end overhang: " + layout.ConcreteEndOverhangMm.ToString("0.###") + " mm/end", x0, bottom + (layout.Transfer == null ? 113 : 143));
            if(layout.Transfer != null) DrawTransferSection(dc,layout);
        }
        private void DrawTransferSection(DrawingContext dc,TowerLayout layout)
        {
            var t=layout.Transfer; double y0=455, leftBay=t.LeftUpperStockMm>0 ? model.LeftSlabBayMm : 0, rightBay=t.RightUpperStockMm>0 ? model.RightSlabBayMm : 0;
            double min=-model.Width/2-leftBay-(leftBay>0 ? model.Width : 0),max=model.Width/2+rightBay+(rightBay>0 ? model.Width : 0);
            foreach(double centre in new[] {0.0,leftBay>0 ? -model.Width-leftBay : double.NaN,rightBay>0 ? model.Width+rightBay : double.NaN}) if(!double.IsNaN(centre)) {
                double start=centre+(centre==0 ? t.LowerStockStartMm : -t.LowerStockStartMm-t.LowerStockLengthMm);
                min=Math.Min(min,start);max=Math.Max(max,start+t.LowerStockLengthMm); }
            double scale=(ActualWidth-60)/(max-min); Func<double,double> xx=x=>30+(x-min)*scale;
            Label(dc,"Load-transfer section · transverse dimensions (mm); vertical schematic",30,y0-22);
            double beamBottom=y0+110,beamTop=y0+20;
            foreach(double centre in new[] {0.0,leftBay>0 ? -model.Width-leftBay : double.NaN,rightBay>0 ? model.Width+rightBay : double.NaN})
                if(!double.IsNaN(centre))
                {
                    dc.DrawRectangle(ConcreteTop,new Pen(ConcreteSide,1),new Rect(xx(centre-model.Width/2),beamTop,model.Width*scale,beamBottom-beamTop));
                    double start=xx(centre+(centre==0 ? t.LowerStockStartMm : -t.LowerStockStartMm-t.LowerStockLengthMm));
                    bool selected=centre==0 && selectedRun==layout.StockPlan.Pieces.Length+Array.IndexOf(t.TributaryWidthsMm,System.Linq.Enumerable.Max(t.TributaryWidthsMm));
                    H20Drawing.Longitudinal(dc,new Rect(start,beamBottom,t.LowerStockLengthMm*scale,20),selected);DrawnH20ProfileCount++;
                    foreach(double support in new[] {-layout.WidthMm/2,layout.WidthMm/2})
                    {
                        foreach(int pair in new[] {-1,1}) {H20Drawing.EndSection(dc,new Rect(xx(centre+support+pair*40-40),beamBottom+20,80*scale,20));DrawnH20ProfileCount++;}
                        dc.DrawLine(new Pen(Steel,3),new Point(xx(centre+support),beamBottom+40),new Point(xx(centre+support),beamBottom+65));
                    }
                }
            Action<double,double,double,string> dim=(a,b,y,label)=>
            {
                dc.DrawLine(new Pen(Steel,1),new Point(xx(a),y),new Point(xx(b),y));
                foreach(double end in new[] {a,b}) dc.DrawLine(new Pen(Steel,1),new Point(xx(end),y-3),new Point(xx(end),y+3));
                Label(dc,label,(xx(a)+xx(b))/2,y+3,true);
            };
            int maxRow=Array.IndexOf(t.TributaryWidthsMm,System.Linq.Enumerable.Max(t.TributaryWidthsMm));
            int lowerEncoded=layout.StockPlan.Pieces.Length+maxRow;
            MemberTargets.Add(Tuple.Create(new Rect(xx(t.LowerStockStartMm),beamBottom, t.LowerStockLengthMm*scale,20),lowerEncoded,0,0));
            dim(t.LowerStockStartMm,t.LowerStockStartMm+t.LowerStockLengthMm,beamBottom+107,$"Secondary {t.LowerStockLengthMm:0}");
            for(int side=0;side<2;side++)
            {
                double bay=side==0 ? leftBay : rightBay;if(bay==0)continue;
                double sign=side==0 ? -1 : 1,face=sign*model.Width/2,foot=side==0 ? t.LeftFootMm : t.RightFootMm,edge=side==0 ? t.LeftUpperEdgeMm : t.RightUpperEdgeMm,stock=side==0 ? t.LeftUpperStockMm : t.RightUpperStockMm;
                double near=face+sign*foot,far=face+sign*(bay-foot),a=face+sign*edge,b=face+sign*(edge+stock);
                // Upper assembly is represented only by vertical feet and its loaded bearer.
                dc.DrawRectangle(ConcreteTop,null,new Rect(Math.Min(xx(face),xx(face+sign*bay)),beamTop-10,bay*scale,10));
                foreach(double x in new[] {near,far})
                {
                    dc.DrawLine(new Pen(Brushes.DarkRed,2),new Point(xx(x),beamTop+20),new Point(xx(x),beamBottom));
                    dc.DrawLine(new Pen(Brushes.DarkRed,2),new Point(xx(x)-4,beamBottom),new Point(xx(x),beamBottom));
                    dc.DrawLine(new Pen(Brushes.DarkRed,2),new Point(xx(x)+4,beamBottom),new Point(xx(x),beamBottom));
                }
                foreach(double shutter in new[] {face+sign*100,face+sign*(bay-100)})
                {
                    dc.DrawLine(new Pen(Brushes.SteelBlue,2),new Point(xx(shutter),beamTop-10),new Point(xx(shutter),beamBottom));
                    dc.DrawLine(new Pen(Brushes.SteelBlue,2),new Point(xx(shutter)-4,beamBottom),new Point(xx(shutter),beamBottom));
                    dc.DrawLine(new Pen(Brushes.SteelBlue,2),new Point(xx(shutter)+4,beamBottom),new Point(xx(shutter),beamBottom));
                }
                int encoded=layout.StockPlan.Pieces.Length+t.RowsMm.Length+maxRow*2+side;
                bool selected=encoded==selectedRun;
                var upperRect=new Rect(Math.Min(xx(a),xx(b)),beamTop,stock*scale,20);
                H20Drawing.Longitudinal(dc,upperRect,selected);DrawnH20ProfileCount++;
                MemberTargets.Add(Tuple.Create(upperRect,encoded,0,0));
                dim(face,near,beamBottom+45,$"Face → foot {foot:0}");
                dim(face+sign*bay,far,beamBottom+45,$"{foot:0}");
                dim(a,near,beamTop-36,$"End → foot {foot-edge:0}");
                dim(a,b,beamTop-64,$"Slab H20 1 × {stock:0}{((side==0 ? t.LeftUpperCustom : t.RightUpperCustom) ? " custom" : "")} · bay {bay:0}");
            }
            dim(t.LowerStockStartMm,-layout.WidthMm/2,beamBottom+78,$"End → primary {-layout.WidthMm/2-t.LowerStockStartMm:0}");
            dim(layout.WidthMm/2,t.LowerStockStartMm+t.LowerStockLengthMm,beamBottom+78,$"{t.LowerStockStartMm+t.LowerStockLengthMm-layout.WidthMm/2:0}");

        }
        private void Label(DrawingContext dc, string value, double x, double y, bool centered = false,Brush colour=null)
        {
            var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, colour??SteelSide, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (centered) { text.MaxTextWidth = Math.Max(1, text.Width); text.TextAlignment = TextAlignment.Center; x -= text.MaxTextWidth / 2; }
            dc.DrawText(text, new Point(x, y));
        }
    }
}
