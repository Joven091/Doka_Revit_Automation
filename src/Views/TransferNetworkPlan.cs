using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Main receiving beams are shown once, with actual cross-formwork stock, bearings and tower positions.</summary>
    public sealed class TransferNetworkPlan : FrameworkElement
    {
        private TransferNetworkResult result;
        private int selectedBeam=-1,selectedRun=-1,selectedLine;
        public bool IsSelectionEnabled {get;set;}
        public bool SectionOnly {get;set;}
        public bool ShowAllDimensions {get;set;}
        public bool HideDimensions {get;set;}
        public double SectionPositionMm {get;set;}=double.NaN;
        internal double ActualSectionPositionMm=>TowerBeamAnalysis.Finite(SectionPositionMm) ? Math.Max(0,Math.Min(result.Beams[0].LengthMm,SectionPositionMm)) : result.Beams[0].LengthMm/2;
        internal readonly List<Tuple<int,int,double>> SectionRows=new List<Tuple<int,int,double>>();
        private static int IntersectingRow(double[] rows,double position)
            =>Array.FindIndex(rows,x=>Math.Abs(x-position)<=40+1e-7);

        internal int DrawnConcreteEndZoneCount {get;private set;}
        private readonly List<DrawingGroup> annotations=new List<DrawingGroup>();
        internal readonly List<Rect> SectionGeometryBounds=new List<Rect>();
        internal readonly List<string> SectionLabels=new List<string>();
        private readonly List<SectionAnnotation> sectionAnnotations=new List<SectionAnnotation>();
        private double sectionTop,sectionBottom;
        private sealed class SectionAnnotation
        {internal string Value;internal double Anchor,Y,A,B;internal bool Above,Dimension;internal Brush Colour;}
        internal readonly List<Tuple<int,double>> AcrossDimensions=new List<Tuple<int,double>>();
        internal readonly List<Rect> AnnotationBounds=new List<Rect>();
        public event Action<int,int,int> MemberSelected;
        public event Action<int> SeparateSlabSelected;
        public event Action<int> BeamOptionsRequested;
        internal readonly List<Tuple<Rect,int>> SeparateBayTargets=new List<Tuple<Rect,int>>();
        public int DrawnWS10ProfileCount {get;private set;}
        public int DrawnH20ProfileCount {get;private set;}
        public int DrawnPrimarySectionCount {get;private set;}
        internal readonly List<Tuple<Rect,int,int,int>> MemberTargets=new List<Tuple<Rect,int,int,int>>();
        public int DrawnTowerLegCount {get;private set;}
        public int DrawnPrimaryLengthCount {get;private set;}
        public int DrawnSecondaryLengthCount {get;private set;}
        public int DrawnTransversePlanCount=>0;
        public int DrawnCrossingBeamCount {get;private set;}
        public void SetResult(TransferNetworkResult value)
        {
            result=value;
            MinWidth=result==null ? 0 : SectionOnly ? 520 : Math.Max(1000,80+result.Beams.Max(b=>b.LengthMm/b.StockPlan.Pieces.Min(p=>p.StockLengthMm)*160));
            Height=result==null ? 0 : SectionOnly ? double.NaN : Centres().Last()+result.Beams.Last().WidthMm*.03+800;
            InvalidateMeasure();InvalidateVisual();
        }
        public void SelectMember(int beam,int run,int line) {selectedBeam=beam;selectedRun=run;selectedLine=line;InvalidateVisual();}
        internal bool SelectAt(Point point)
        {
            if(!IsSelectionEnabled)return false;
            foreach(var hit in MemberTargets)if(hit.Item1.Contains(point)) {SelectMember(hit.Item2,hit.Item3,hit.Item4);MemberSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4);return true;}
            foreach(var hit in SeparateBayTargets)if(hit.Item1.Contains(point)){SeparateSlabSelected?.Invoke(hit.Item2);return true;}return false;
        }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);if(!IsSelectionEnabled || result==null)return;
            var hit=MemberTargets.FirstOrDefault(h=>h.Item2>=0 && h.Item1.Contains(e.GetPosition(this)));
            if(hit!=null){BeamOptionsRequested?.Invoke(hit.Item2);e.Handled=true;}
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {base.OnMouseLeftButtonDown(e);if(SelectAt(e.GetPosition(this)))e.Handled=true;}
        private double SectionTopPadding(double width)=>110+28*Math.Max(0,Math.Ceiling(result.Beams.Length*360/Math.Max(100,width-80))-2);
        protected override Size MeasureOverride(Size available)
        {
            double width=Math.Max(MinWidth,double.IsInfinity(available.Width) ? MinWidth : available.Width);
            return new Size(width,SectionOnly && result!=null ? 30+SectionTopPadding(width)+190+220 : Height);
        }
        private double[] Centres()
        {
            var values=new double[result.Beams.Length];values[0]=165;
            for(int i=1;i<values.Length;i++)values[i]=values[i-1]+Math.Max(235,(result.BeamCentresMm[i]-result.BeamCentresMm[i-1])*.055);return values;
        }
        private static readonly Brush Timber=Brushes.Goldenrod,Steel=Brushes.SlateGray,Concrete=Brushes.LightGray;
        private void Label(DrawingContext dc,string value,double x,double y,bool centre=false,Brush colour=null,bool rotate=false)
        {
            var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,colour??Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            Rect box;
            if(rotate) {box=new Rect(x-text.Height/2-2,y-text.Width/2-1,text.Height+4,text.Width+2);x-=text.Height/2;y+=text.Width/2;}
            else {if(centre)x-=text.Width/2;box=new Rect(x-2,y-1,text.Width+4,text.Height+2);}
            if(box.Left<0 || box.Right>ActualWidth || box.Top<0 || box.Bottom>ActualHeight || AnnotationBounds.Any(r=>r.IntersectsWith(box)))return;
            AnnotationBounds.Add(box);var drawing=new DrawingGroup();using(var overlay=drawing.Open()){overlay.DrawRectangle(Brushes.White,null,box);if(rotate)overlay.PushTransform(new RotateTransform(-90,x,y));overlay.DrawText(text,new Point(x,y));if(rotate)overlay.Pop();}annotations.Add(drawing);
        }
        private void Dim(DrawingContext dc,double a,double b,double y,string value,Brush colour=null)
        {
            if(HideDimensions)return;
            colour=colour??Steel;dc.DrawLine(new Pen(colour,1),new Point(a,y),new Point(b,y));foreach(double x in new[] {a,b})dc.DrawLine(new Pen(colour,1),new Point(x,y-3),new Point(x,y+3));if(Math.Abs(b-a)>=value.Length*6+8)Label(dc,value,(a+b)/2,y-19,true,colour);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);SectionRows.Clear();SectionGeometryBounds.Clear();SectionLabels.Clear();sectionAnnotations.Clear();DrawnConcreteEndZoneCount=0;AnnotationBounds.Clear();annotations.Clear();AcrossDimensions.Clear();MemberTargets.Clear();SeparateBayTargets.Clear();DrawnWS10ProfileCount=DrawnH20ProfileCount=DrawnPrimarySectionCount=0;DrawnTowerLegCount=DrawnPrimaryLengthCount=DrawnSecondaryLengthCount=DrawnCrossingBeamCount=0;if(result==null || ActualWidth<100)return;
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
            if(SectionOnly) {Section(dc,30);DrawSectionAnnotations(dc);foreach(var annotation in annotations)dc.DrawDrawing(annotation);return;}
            Label(dc,$"mm · primary = {result.Beams[0].Primary.Description}/line · secondaries = single H20"+(result.Input.HasIndividualSpacings ? " · spacings by element" : $" @ {result.Input.UpperSpacingMm:0}"),30,8);
            Label(dc,"Primary H20: orange · secondary H20: purple · cyan: frames · yellow: crosses",30,27);
            var ys=Centres();var crossSolids=result.CrossSupports.SelectMany(c=>c.Solids).ToArray();
            double minX=Math.Min(0,crossSolids.Select(s=>s.X0).DefaultIfEmpty(0).Min()),maxX=Math.Max(result.Beams[0].LengthMm,crossSolids.Select(s=>s.X1).DefaultIfEmpty(0).Max());
            double scale=(ActualWidth-80)/(maxX-minX);Func<double,double> xx=x=>40+(x-minX)*scale;
            for(int bay=0;bay<result.Bays.Length;bay++)
            {
                double a=ys[bay]+result.Input.Beams[bay].WidthMm*.055/2,b=ys[bay+1]-result.Input.Beams[bay+1].WidthMm*.055/2;
                bool separate=result.Bays[bay]==null;
                dc.DrawRectangle(separate ? Brushes.Moccasin : Brushes.AliceBlue,null,new Rect(xx(0),a,result.Beams[0].LengthMm*scale,b-a));
                if(separate)SeparateBayTargets.Add(Tuple.Create(new Rect(xx(0),a,result.Beams[0].LengthMm*scale,b-a),bay));
                bool crossed=result.Input.CrossingBeams.Any(c=>c.BayNumber==bay+1);
                Label(dc,$"S{bay+1} · bay {result.Input.Bays[bay].WidthMm:0} · t {result.Input.Bays[bay].ThicknessMm:0}{(separate ? " · separate formwork (not calculated)" : "")}",crossed ? 45 : ActualWidth/2,(a+b)/2,!crossed);
            }
            for(int i=0;i<result.Input.CrossingBeams.Length;i++)
            {
                var c=result.Input.CrossingBeams[i];int bay=c.BayNumber-1;
                double a=ys[bay]+result.Input.Beams[bay].WidthMm*.055/2,b=ys[bay+1]-result.Input.Beams[bay+1].WidthMm*.055/2;
                dc.DrawRectangle(Brushes.LightGray,new Pen(Steel,1),new Rect(xx(c.PositionMm-c.WidthMm/2),a,c.WidthMm*scale,b-a));
                double labelX=xx(c.PositionMm)+c.WidthMm*scale/2+5;
                if(labelX>ActualWidth-145)labelX=xx(c.PositionMm)-c.WidthMm*scale/2-145;
                Label(dc,$"X{i+1} · {c.WidthMm:0}×{c.DepthMm:0}",labelX,(a+b)/2+18);
                Label(dc,$"x {c.PositionMm:0}",labelX,(a+b)/2+33);DrawnCrossingBeamCount++;
            }
            for(int beam=0;beam<result.Beams.Length;beam++)
            {
                var b=result.Beams[beam];double cy=ys[beam],top=cy+(b.TransverseOffsetMm-b.WidthMm/2)*.055,bottom=cy+(b.TransverseOffsetMm+b.WidthMm/2)*.055;
                Label(dc,$"B{beam+1} · {result.Input.Beams[beam].WidthMm:0}×{result.Input.Beams[beam].DepthMm:0} · {b.LegCount} legs · {(b.Rotated ? "Cross" : "Frame")} {b.WidthMm:0} across",40,top-65);
                dc.DrawRectangle(Concrete,null,new Rect(xx(0),cy-result.Input.Beams[beam].WidthMm*.055/2,b.LengthMm*scale,result.Input.Beams[beam].WidthMm*.055));
                for(int i=0;i<b.StockPlan.Pieces.Length;i++)
                {
                    var stock=b.StockPlan.Pieces[i];
                    for(int line=0;line<2;line++)
                    {
                        double y=line==0 ? top : bottom;
                        PrimaryDrawing.Plan(dc,new Point(xx(stock.StartMm),y),new Point(xx(stock.EndMm),y),b.Primary,beam==selectedBeam && i==selectedRun && line==selectedLine,4);
                        MemberTargets.Add(Tuple.Create(new Rect(xx(stock.StartMm),y-9,stock.StockLengthMm*scale,18),beam,i,line));

                    }
                    Label(dc,$"{b.Primary.Name} · {stock.StockLengthMm:0.###}",xx(stock.StartMm+stock.StockLengthMm/2),bottom+12,true);DrawnPrimaryLengthCount++;
                    double dimY=bottom+42;
                    Dim(dc,xx(stock.StartMm),xx(b.PositionsMm[stock.FirstSupport]),dimY,(b.PositionsMm[stock.FirstSupport]-stock.StartMm).ToString("0.###"));
                    Dim(dc,xx(b.PositionsMm[stock.LastSupport]),xx(stock.EndMm),dimY,(stock.EndMm-b.PositionsMm[stock.LastSupport]).ToString("0.###"));
                    if(i>0)dc.DrawLine(new Pen(Steel,1){DashStyle=DashStyles.Dot},new Point(xx(stock.StartMm),top-6),new Point(xx(stock.StartMm),bottom+8));
                }
                for(int leg=0;leg<b.PositionsMm.Length;leg++)
                {
                    double x=xx(b.PositionsMm[leg]);var a=new Point(x,top);var c=new Point(x,bottom);
                    TowerPlanDrawing.Station(dc,a,c,b.Rotated);
                    foreach(var at in new[] {a,c}) {TowerPlanDrawing.Leg(dc,at,!b.Rotated);DrawnTowerLegCount++;}
                    if(leg<b.Joined.Length && b.Joined[leg])
                        TowerPlanDrawing.BracedBay(dc,a,c,new Point(xx(b.PositionsMm[leg+1]),top),new Point(xx(b.PositionsMm[leg+1]),bottom),b.Rotated);
                }
                double acrossX=xx(b.PositionsMm.OrderBy(x=>Math.Abs(x-b.LengthMm/2)).First())-18;
                VerticalDim(dc,acrossX,top,bottom,b.WidthMm.ToString("0.###"),Brushes.Firebrick,true);
                AcrossDimensions.Add(Tuple.Create(beam,b.WidthMm));
                for(int leg=0;leg<b.Joined.Length;)
                {
                    int count=1;double interval=b.PositionsMm[leg+1]-b.PositionsMm[leg];
                    while(leg+count<b.Joined.Length && b.Joined[leg+count]==b.Joined[leg] && Math.Abs(b.PositionsMm[leg+count+1]-b.PositionsMm[leg+count]-interval)<1e-7)count++;
                    Brush colour=b.Joined[leg] ? Brushes.Firebrick : Steel;
                    Dim(dc,xx(b.PositionsMm[leg]),xx(b.PositionsMm[leg+count]),top-39,$"{(count>1 ? count+"×" : "")}{interval:0.###}",colour);
                    for(int j=1;j<count;j++)dc.DrawLine(new Pen(colour,1),new Point(xx(b.PositionsMm[leg+j]),top-42),new Point(xx(b.PositionsMm[leg+j]),top-36));
                    leg+=count;
                }

            }
            DrawCrossPlan(dc,ys,xx);
            var last=result.Beams.Last();double endY=ys.Last()+last.WidthMm*.055/2+121;Dim(dc,xx(0),xx(last.LengthMm),endY,$"Beam length {last.LengthMm:0} · concrete beyond H20 {last.ConcreteEndOverhangMm:0.###} / end");
            Section(dc,endY+60);DrawSectionAnnotations(dc);foreach(var annotation in annotations)dc.DrawDrawing(annotation);
        }
        private void DrawCrossPlan(DrawingContext dc,double[] ys,Func<double,double> xx)
        {
            Func<double,double> yy=y=>
            {
                int bay=Math.Max(0,Math.Min(ys.Length-2,Array.FindLastIndex(result.BeamCentresMm,c=>c<=y)));
                return ys[bay]+(y-result.BeamCentresMm[bay])*(ys[bay+1]-ys[bay])/(result.BeamCentresMm[bay+1]-result.BeamCentresMm[bay]);
            };
            foreach(var support in result.CrossSupports)
            {
                int encoded=-support.CrossingIndex-1;
                if(support.HasReducedShutterClearance)Label(dc,$"X{support.CrossingIndex+1} shutter allowance: {support.NearShutterClearanceMm:0}/{support.FarShutterClearanceMm:0} mm (exception)",xx(support.Input.PositionMm+support.Input.WidthMm/2)+8,yy(result.BeamCentresMm[support.Input.BayNumber-1]+result.Input.Beams[support.Input.BayNumber-1].WidthMm/2));
                foreach(var solid in support.Solids.Where(s=>s.Member==null && !s.IsTowerEnvelope))
                {
                    var rect=new Rect(new Point(xx(solid.X0),yy(solid.Y0)),new Point(xx(solid.X1),yy(solid.Y1)));
                    if(solid.IsLeg) {TowerPlanDrawing.Leg(dc,new Point(xx((solid.X0+solid.X1)/2),yy((solid.Y0+solid.Y1)/2)),support.Towers?.Rotated==true);DrawnTowerLegCount++;}
                    else dc.DrawRectangle(solid.IsSideShutter ? Brushes.LightSteelBlue : Brushes.SandyBrown,new Pen(solid.IsSideShutter ? Brushes.SteelBlue : Brushes.SaddleBrown,1),rect);
                }
                if(support.Towers!=null)
                {
                    var timber=support.Solids.Where(s=>s.Member?.H20Count==2).ToArray();
                    double centre=(timber.Min(s=>s.X0)+timber.Max(s=>s.X1))/2,origin=timber[0].Y0-timber[0].Member.StartMm;
                    var tower=support.Towers;
                    for(int leg=0;leg<tower.PositionsMm.Length;leg++)
                    {
                        double y=yy(origin+tower.PositionsMm[leg]);var a=new Point(xx(centre-tower.WidthMm/2),y);var b=new Point(xx(centre+tower.WidthMm/2),y);
                        TowerPlanDrawing.Station(dc,a,b,tower.Rotated);
                        if(leg<tower.Joined.Length && tower.Joined[leg])
                        {
                            double next=yy(origin+tower.PositionsMm[leg+1]);
                            TowerPlanDrawing.BracedBay(dc,a,b,new Point(a.X,next),new Point(b.X,next),tower.Rotated);
                        }
                    }
                }
                foreach(var group in support.Solids.Where(s=>s.Member!=null && s.Member.H20Count==2).GroupBy(s=>s.Member))
                {
                    double x0=group.Min(s=>s.X0),x1=group.Max(s=>s.X1),y0=group.Min(s=>s.Y0),y1=group.Max(s=>s.Y1);
                    bool alongY=y1-y0>x1-x0;int member=Array.IndexOf(support.Members,group.Key);
                    var first=alongY ? new Point(xx((x0+x1)/2),yy(y0)) : new Point(xx(x0),yy((y0+y1)/2));
                    var last=alongY ? new Point(xx((x0+x1)/2),yy(y1)) : new Point(xx(x1),yy((y0+y1)/2));
                    PrimaryDrawing.Plan(dc,first,last,group.Key.Primary,selectedBeam==encoded && selectedRun==member,4);
                    var target=new Rect(first,last);target.Inflate(8,8);MemberTargets.Insert(0,Tuple.Create(target,encoded,member,0));
                }
            }
        }
        private void VerticalDim(DrawingContext dc,double x,double top,double bottom,string value,Brush colour=null,bool left=false)
        {
            if(HideDimensions)return;
            colour=colour??Steel;
            dc.DrawLine(new Pen(colour,1),new Point(x,top),new Point(x,bottom));
            foreach(double y in new[] {top,bottom})dc.DrawLine(new Pen(colour,1),new Point(x-4,y),new Point(x+4,y));
            var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,colour,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            Label(dc,value,x-text.Height/2-7,(top+bottom)/2,false,colour,true);
        }
        private void SectionNote(string value,double anchor,double y,bool above)
            =>sectionAnnotations.Add(new SectionAnnotation {Value=value,Anchor=anchor,Y=y,Above=above,Colour=Steel});
        private void SectionDimension(double a,double b,double y,string value,bool above,Brush colour=null)
            {if(!HideDimensions)sectionAnnotations.Add(new SectionAnnotation {Value=value,Anchor=(a+b)/2,A=a,B=b,Y=y-19,Above=above,Dimension=true,Colour=colour??Steel});}
        private void DrawSectionAnnotations(DrawingContext dc)
        {
            // Allocate annotation rows outside the entire detail, including crossing
            // supports. Labels no longer erase flanges, webs, bearing zones or leaders.
            var leaders=new List<DrawingGroup>();var labels=new List<DrawingGroup>();
            foreach(var note in sectionAnnotations.OrderBy(n=>n.Above ? -n.Y : n.Y))
            {
                var text=new FormattedText(note.Value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,note.Colour,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double x=Math.Max(5,Math.Min(ActualWidth-text.Width-5,note.Anchor-text.Width/2));
                var box=new Rect(x-2,note.Y-1,text.Width+4,text.Height+2);
                while(AnnotationBounds.Any(r=>r.IntersectsWith(box)) || SectionGeometryBounds.Any(r=>r.IntersectsWith(box)))
                    box.Offset(0,note.Above ? -28 : 28);
                // Exceptional out-of-range annotations stay off the detail.
                if(box.Top<2 || box.Bottom>ActualHeight-2)continue;
                AnnotationBounds.Add(box);SectionLabels.Add(note.Value);
                var drawing=new DrawingGroup();using(var overlay=drawing.Open())
                {
                    double rowY=note.Above ? box.Bottom+5 : box.Top-5;
                    var pen=new Pen(note.Colour,.7);
                    if(note.Dimension)
                    {
                        overlay.DrawLine(pen,new Point(note.A,rowY),new Point(note.B,rowY));
                        foreach(double end in new[] {note.A,note.B})overlay.DrawLine(pen,new Point(end,rowY-3),new Point(end,rowY+3));
                    }
                    else
                    {
                        double detailEdge=note.Above ? sectionTop-4 : sectionBottom+4;
                        overlay.DrawLine(pen,new Point(note.Anchor,detailEdge),new Point(note.Anchor,rowY));
                        overlay.DrawLine(pen,new Point(note.Anchor,rowY),new Point(box.Left+box.Width/2,rowY));
                    }
                }
                leaders.Add(drawing);
                var caption=new DrawingGroup();using(var overlay=caption.Open())
                {overlay.DrawRectangle(Brushes.White,null,box);overlay.DrawText(text,new Point(box.Left+2,box.Top+1));}
                labels.Add(caption);
            }
            annotations.AddRange(leaders);annotations.AddRange(labels);
        }
        private void Section(DrawingContext dc,double y0)
        {
            double cut=ActualSectionPositionMm;
            Label(dc,$"Section A–A · x = {cut:0.###} mm from beam start",30,y0-22);
            double min=double.MaxValue,max=double.MinValue;
            for(int b=0;b<result.Beams.Length;b++) {var t=result.Beams[b].Transfer;min=Math.Min(min,result.BeamCentresMm[b]+t.LowerStockStartMm);max=Math.Max(max,result.BeamCentresMm[b]+t.LowerStockStartMm+t.LowerStockLengthMm);}
            foreach(var support in result.CrossSupports)foreach(var solid in support.Solids) {min=Math.Min(min,solid.Y0);max=Math.Max(max,solid.Y1);}
            double highest=result.Input.Beams.Max(b=>b.BottomElevationMm+b.DepthMm),lowest=result.Input.Beams.Min(b=>b.BottomElevationMm-400);
            if(result.CrossSupports.Length>0) {highest=Math.Max(highest,result.CrossSupports.Max(c=>c.Input.BottomElevationMm+c.Input.DepthMm));lowest=Math.Min(lowest,result.CrossSupports.Min(c=>c.Input.BottomElevationMm-400));}
            double scale=(ActualWidth-80)/(max-min),depthScale=150/Math.Max(200,highest-lowest);Func<double,double> xx=x=>40+(x-min)*scale;double top=y0+SectionTopPadding(ActualWidth);sectionTop=top;sectionBottom=top+190;
            SectionGeometryBounds.Add(new Rect(0,sectionTop,ActualWidth,sectionBottom-sectionTop));
            for(int beam=0;beam<result.Beams.Length;beam++)
            {
                var b=result.Beams[beam];var input=result.Input.Beams[beam];var t=b.Transfer;double centre=result.BeamCentresMm[beam],bottom=top+(highest-input.BottomElevationMm)*depthScale,beamTop=top+(highest-input.BottomElevationMm-input.DepthMm)*depthScale;
                dc.DrawRectangle(Concrete,new Pen(Steel,1),new Rect(xx(centre-input.WidthMm/2),beamTop,input.WidthMm*scale,bottom-beamTop));
                int physical=IntersectingRow(Enumerable.Range(0,t.PhysicalRowCount).Select(t.PhysicalRowPosition).ToArray(),cut);
                int row=physical<0 ? -1 : t.PhysicalRowDesignIndex(physical),encoded=b.StockPlan.Pieces.Length+row;
                double start=xx(centre+t.LowerStockStartMm),end=xx(centre+t.LowerStockStartMm+t.LowerStockLengthMm);
                if(row>=0)
                {
                    var lowerRect=new Rect(start,bottom,end-start,200*depthScale);
                    H20Drawing.Longitudinal(dc,lowerRect,beam==selectedBeam && selectedRun==encoded);DrawnH20ProfileCount++;
                    MemberTargets.Add(Tuple.Create(lowerRect,beam,encoded,0));
                    SectionRows.Add(Tuple.Create(beam,row,t.PhysicalRowPosition(physical)));
                    SectionNote($"B{beam+1} · D {input.DepthMm:0.###} · T{row+1}",xx(centre),top-26,true);
                    SectionDimension(start,end,sectionBottom+24,$"H20 {t.LowerStockLengthMm:0}",false);DrawnSecondaryLengthCount++;
                }
                else SectionNote($"B{beam+1} · D {input.DepthMm:0.###} · between H20s",xx(centre),top-26,true);
                int primaryRun=Array.FindIndex(b.StockPlan.Pieces,p=>cut>=p.StartMm-1e-7 && cut<=p.EndMm+1e-7);
                if(primaryRun<0)continue;
                foreach(double support in new[] {b.TransverseOffsetMm-b.WidthMm/2,b.TransverseOffsetMm+b.WidthMm/2})
                {
                    foreach(int pair in b.Primary.IsWS10 ? new[] {0} : new[] {-1,1})
                    {
                        var primaryRect=new Rect(xx(centre+support+pair*40-(b.Primary.IsWS10 ? 76.5 : 40)),bottom+200*depthScale,(b.Primary.IsWS10 ? 153 : 80)*scale,b.Primary.DepthMm*depthScale);
                        int line=support<0 ? 0 : 1;
                        bool selected=beam==selectedBeam && selectedRun==primaryRun && selectedLine==line;
                        if(b.Primary.IsWS10) {PrimaryDrawing.EndSection(dc,primaryRect,selected);DrawnWS10ProfileCount++;}
                        else {H20Drawing.EndSection(dc,primaryRect,selected);DrawnH20ProfileCount++;}
                        DrawnPrimarySectionCount++;
                        if(primaryRun>=0)MemberTargets.Add(Tuple.Create(primaryRect,beam,primaryRun,line));
                    }
                    if(b.PositionsMm.Any(x=>Math.Abs(x-cut)<=40+1e-7))dc.DrawLine(new Pen(Steel,3),new Point(xx(centre+support),bottom+(200+b.Primary.DepthMm)*depthScale),new Point(xx(centre+support),bottom+(200+b.Primary.DepthMm)*depthScale+12));
                }

            }
            for(int bay=0;bay<result.Bays.Length;bay++)
            {
                var slab=result.Input.Bays[bay];var plan=result.Bays[bay];
                double slabFace=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2;
                double slabTop=top+(highest-Math.Max(result.Input.Beams[bay].BottomElevationMm+result.Input.Beams[bay].DepthMm,result.Input.Beams[bay+1].BottomElevationMm+result.Input.Beams[bay+1].DepthMm))*depthScale;
                double slabBottom=slabTop+slab.ThicknessMm*depthScale;
                SectionDimension(xx(slabFace),xx(slabFace+slab.WidthMm),top-55,$"S{bay+1} {slab.WidthMm:0.###} · t {slab.ThicknessMm:0.###}",true);
                if(plan==null)
                {
                    double faceX=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2;
                    var separateRect=new Rect(xx(faceX),slabTop,slab.WidthMm*scale,slab.ThicknessMm*depthScale);
                    dc.DrawRectangle(Brushes.Moccasin,new Pen(Brushes.Goldenrod,.6),separateRect);
                    SeparateBayTargets.Add(Tuple.Create(separateRect,bay));
                    double midpoint=faceX+slab.WidthMm/2;
                    SectionNote($"S{bay+1}: separate formwork · not calculated",xx(midpoint),top-85,true);
                    continue;
                }
                double face=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2,other=face+slab.WidthMm;
                double a=face+plan.EdgeMm,b=a+plan.StockLengthMm,near=face+plan.NearFootMm,far=other-plan.FarFootMm,soffit=top+(highest-Math.Max(result.Input.Beams[bay].BottomElevationMm+result.Input.Beams[bay].DepthMm,result.Input.Beams[bay+1].BottomElevationMm+result.Input.Beams[bay+1].DepthMm)+slab.ThicknessMm)*depthScale;
                dc.DrawRectangle(Brushes.LightSteelBlue,null,new Rect(xx(face),slabTop,slab.WidthMm*scale,slab.ThicknessMm*depthScale));
                var receiving=result.Beams[bay].Transfer;
                int row=Array.FindIndex(receiving.RowsMm,x=>Math.Abs(x-cut)<=40+1e-7 && receiving.RightUpperScales[Array.IndexOf(receiving.RowsMm,x)]>0);
                if(row<0 || result.SlabRowCount(bay)==0)continue;
                SectionNote($"S{bay+1} · T{row+1} · x {receiving.RowsMm[row]:0.###} mm",xx((a+b)/2),top-105,true);
                int encoded=result.Beams[bay].StockPlan.Pieces.Length+result.Beams[bay].Transfer.RowsMm.Length+row*2+1;
                var upperRect=new Rect(xx(a),soffit,plan.StockLengthMm*scale,200*depthScale);
                H20Drawing.Longitudinal(dc,upperRect,bay==selectedBeam && selectedRun==encoded);DrawnH20ProfileCount++;
                MemberTargets.Add(Tuple.Create(upperRect,bay,encoded,0));
                for(int side=0;side<2;side++)
                {
                    int beam=bay+side;double bottom=top+(highest-result.Input.Beams[beam].BottomElevationMm)*depthScale,x=side==0 ? near : far;
                    dc.DrawLine(new Pen(Brushes.DarkRed,2),new Point(xx(x),soffit+200*depthScale),new Point(xx(x),bottom));Arrow(dc,xx(x),bottom,Brushes.DarkRed);
                    double shutter=side==0 ? face+100 : other-100;dc.DrawLine(new Pen(Brushes.SteelBlue,2),new Point(xx(shutter),soffit),new Point(xx(shutter),bottom));Arrow(dc,xx(shutter),bottom,Brushes.SteelBlue);
                    if(ShowAllDimensions || selectedBeam==beam)
                    {
                        SectionDimension(xx(side==0 ? face : far),xx(side==0 ? near : other),sectionBottom+54,$"Foot {(side==0 ? plan.NearFootMm : plan.FarFootMm):0}",false);
                    }
                }
                SectionDimension(xx(a),xx(b),top-85,$"S{bay+1} H20 {plan.StockLengthMm:0}{(plan.IsCustomLength ? " custom" : "")}",true);
            }
            DrawCrossSection(dc,xx,z=>top+(highest-z)*depthScale);
        }
        private void DrawCrossSection(DrawingContext dc,Func<double,double> xx,Func<double,double> zz)
        {
            foreach(var support in result.CrossSupports.Where(c=>c.Solids.Any(s=>!s.IsTowerEnvelope && ActualSectionPositionMm>=s.X0-1e-7 && ActualSectionPositionMm<=s.X1+1e-7) || ActualSectionPositionMm>=c.Input.PositionMm-c.Input.WidthMm/2-1e-7 && ActualSectionPositionMm<=c.Input.PositionMm+c.Input.WidthMm/2+1e-7))
            {
            int bay=support.Input.BayNumber-1;double face=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2;
            if(Math.Abs(ActualSectionPositionMm-support.Input.PositionMm)<=support.Input.WidthMm/2+1e-7)dc.DrawRectangle(Concrete,new Pen(Steel,1),new Rect(new Point(xx(face),zz(support.Input.BottomElevationMm+support.Input.DepthMm)),new Point(xx(face+result.Input.Bays[bay].WidthMm),zz(support.Input.BottomElevationMm))));
            SectionNote($"X{support.CrossingIndex+1} · x {support.Input.PositionMm:0.###} · bottom {support.Input.BottomElevationMm:0.###} mm",xx(face+result.Input.Bays[bay].WidthMm/2),sectionBottom+100,false);
            foreach(var solid in support.Solids.Where(s=>!s.IsTowerEnvelope && ActualSectionPositionMm>=s.X0-1e-7 && ActualSectionPositionMm<=s.X1+1e-7))
            {
                if(solid.IsLeg) {dc.DrawLine(new Pen(Steel,2),new Point(xx((solid.Y0+solid.Y1)/2),zz(solid.Z1)),new Point(xx((solid.Y0+solid.Y1)/2),zz(solid.Z1)+35));continue;}
                var rect=new Rect(new Point(xx(solid.Y0),zz(solid.Z1)),new Point(xx(solid.Y1),zz(solid.Z0)));int member=Array.IndexOf(support.Members,solid.Member);
                bool selected=selectedBeam==-support.CrossingIndex-1 && selectedRun==member;
                if(solid.Member==null)dc.DrawRectangle(solid.IsSideShutter ? Brushes.LightSteelBlue : Brushes.SandyBrown,new Pen(solid.IsSideShutter ? Brushes.SteelBlue : Brushes.SaddleBrown,1),rect);
                else
                {
                    bool along=solid.Y1-solid.Y0>solid.X1-solid.X0;
                    if(solid.Member.IsWS10) {if(along)PrimaryDrawing.Longitudinal(dc,rect,selected);else PrimaryDrawing.EndSection(dc,rect,selected);DrawnWS10ProfileCount++;}
                    else {if(along)H20Drawing.Longitudinal(dc,rect,selected);else H20Drawing.EndSection(dc,rect,selected);DrawnH20ProfileCount++;}
                }
                if(member>=0)MemberTargets.Insert(0,Tuple.Create(rect,-support.CrossingIndex-1,member,0));
            }
            foreach(var bearing in support.Bearings.Where(b=>b.FootprintEndMm>b.FootprintStartMm ? ActualSectionPositionMm>=b.FootprintStartMm-1e-7 && ActualSectionPositionMm<=b.FootprintEndMm+1e-7 : Math.Abs(ActualSectionPositionMm-b.Xmm)<=40+1e-7).GroupBy(b=>new {b.Ymm,b.FillerHeightMm,b.IsConcreteEndZone}).Select(g=>g.First()))
            {
                if(bearing.IsConcreteEndZone)
                {
                    DrawnConcreteEndZoneCount++;
                    dc.DrawLine(new Pen(Brushes.DarkRed,3),new Point(xx(bearing.EndZoneStartMm),zz(bearing.ReceivingTopMm)),new Point(xx(bearing.EndZoneEndMm),zz(bearing.ReceivingTopMm)));
                    SectionDimension(xx(bearing.EndZoneStartMm),xx(bearing.EndZoneEndMm),sectionBottom+80,$"B{bearing.Beam+1} end {bearing.EndZoneEndMm-bearing.EndZoneStartMm:0.###} mm"+(bearing.IsShutterEndZone ? " · shutter"+(bearing.EndZoneEndMm-bearing.EndZoneStartMm<200 ? " (exception)" : "") : ""),false,Brushes.DarkRed);continue;
                }
                if(bearing.FillerHeightMm>1e-7)SectionNote($"Filler {bearing.FillerHeightMm:0.###} mm",xx(bearing.Ymm),sectionBottom+100,false);
            }
        }
        }
        private static void Arrow(DrawingContext dc,double x,double y,Brush colour) {dc.DrawLine(new Pen(colour,2),new Point(x-4,y-6),new Point(x,y));dc.DrawLine(new Pen(colour,2),new Point(x+4,y-6),new Point(x,y));}
    }
}


