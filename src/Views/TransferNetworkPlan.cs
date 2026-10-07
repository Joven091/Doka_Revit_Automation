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
        internal readonly List<Rect> AnnotationBounds=new List<Rect>();
        public event Action<int,int,int> MemberSelected;
        internal readonly List<Tuple<Rect,int,int,int>> MemberTargets=new List<Tuple<Rect,int,int,int>>();
        public int DrawnTowerLegCount {get;private set;}
        public int DrawnPrimaryLengthCount {get;private set;}
        public int DrawnSecondaryLengthCount {get;private set;}
        public int DrawnTransversePlanCount=>0;
        public int DrawnCrossingBeamCount {get;private set;}
        public void SetResult(TransferNetworkResult value)
        {
            result=value;
            MinWidth=result==null ? 0 : SectionOnly ? 1000 : Math.Max(1000,80+result.Beams.Max(b=>b.LengthMm/b.StockPlan.Pieces.Min(p=>p.StockLengthMm)*160));
            Height=result==null ? 0 : SectionOnly ? 420 : Centres().Last()+result.Beams.Last().WidthMm*.03+505;
            InvalidateMeasure();InvalidateVisual();
        }
        public void SelectMember(int beam,int run,int line) {selectedBeam=beam;selectedRun=run;selectedLine=line;InvalidateVisual();}
        internal bool SelectAt(Point point)
        {
            if(!IsSelectionEnabled)return false;
            foreach(var hit in MemberTargets)if(hit.Item1.Contains(point)) {SelectMember(hit.Item2,hit.Item3,hit.Item4);MemberSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4);return true;}return false;
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {base.OnMouseLeftButtonDown(e);if(SelectAt(e.GetPosition(this)))e.Handled=true;}
        protected override Size MeasureOverride(Size available)=>new Size(Math.Max(MinWidth,double.IsInfinity(available.Width) ? MinWidth : available.Width),Height);
        private double[] Centres()
        {
            var values=new double[result.Beams.Length];values[0]=165;
            for(int i=1;i<values.Length;i++)values[i]=values[i-1]+Math.Max(235,(result.BeamCentresMm[i]-result.BeamCentresMm[i-1])*.055);return values;
        }
        private static readonly Brush Timber=Brushes.Goldenrod,Steel=Brushes.SlateGray,Concrete=Brushes.LightGray;
        private void Label(DrawingContext dc,string value,double x,double y,bool centre=false,Brush colour=null)
        {
            var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,colour??Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if(centre)x-=text.Width/2;var box=new Rect(x-2,y-1,text.Width+4,text.Height+2);
            if(box.Left<0 || box.Right>ActualWidth || box.Top<0 || box.Bottom>Height || AnnotationBounds.Any(r=>r.IntersectsWith(box)))return;
            AnnotationBounds.Add(box);dc.DrawRectangle(Brushes.White,null,box);dc.DrawText(text,new Point(x,y));
        }
        private void Dim(DrawingContext dc,double a,double b,double y,string value,Brush colour=null)
        {
            colour=colour??Steel;dc.DrawLine(new Pen(colour,1),new Point(a,y),new Point(b,y));foreach(double x in new[] {a,b})dc.DrawLine(new Pen(colour,1),new Point(x,y-3),new Point(x,y+3));if(Math.Abs(b-a)>=value.Length*6+8)Label(dc,value,(a+b)/2,y+4,true,colour);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);AnnotationBounds.Clear();MemberTargets.Clear();DrawnTowerLegCount=DrawnPrimaryLengthCount=DrawnSecondaryLengthCount=DrawnCrossingBeamCount=0;if(result==null || ActualWidth<100)return;
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,Height));
            if(SectionOnly) {Section(dc,55);return;}
            Label(dc,$"mm · primary P = 2×H20/line · secondaries = single H20 @ {result.Input.UpperSpacingMm:0}",30,8);
            Label(dc,"Red: braced bays · grey: gaps · beam W×D · slab t = thickness · transverse view schematic",30,27);
            var ys=Centres();var crossSolids=result.CrossSupports.SelectMany(c=>c.Solids).ToArray();
            double minX=Math.Min(0,crossSolids.Select(s=>s.X0).DefaultIfEmpty(0).Min()),maxX=Math.Max(result.Beams[0].LengthMm,crossSolids.Select(s=>s.X1).DefaultIfEmpty(0).Max());
            double scale=(ActualWidth-80)/(maxX-minX);Func<double,double> xx=x=>40+(x-minX)*scale;
            for(int bay=0;bay<result.Bays.Length;bay++)
            {
                double a=ys[bay]+result.Input.Beams[bay].WidthMm*.055/2,b=ys[bay+1]-result.Input.Beams[bay+1].WidthMm*.055/2;
                bool separate=result.Bays[bay]==null;
                dc.DrawRectangle(separate ? Brushes.Moccasin : Brushes.AliceBlue,null,new Rect(xx(0),a,result.Beams[0].LengthMm*scale,b-a));
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
                var b=result.Beams[beam];double cy=ys[beam],top=cy-b.WidthMm*.055/2,bottom=cy+b.WidthMm*.055/2;
                Label(dc,$"B{beam+1} · {result.Input.Beams[beam].WidthMm:0}×{result.Input.Beams[beam].DepthMm:0} · {b.LegCount} legs · {(b.Rotated ? "Cross" : "Frame")} {b.WidthMm:0} across",40,top-65);
                dc.DrawRectangle(Concrete,null,new Rect(xx(0),cy-result.Input.Beams[beam].WidthMm*.055/2,b.LengthMm*scale,result.Input.Beams[beam].WidthMm*.055));
                for(int i=0;i<b.StockPlan.Pieces.Length;i++)
                {
                    var stock=b.StockPlan.Pieces[i];
                    for(int line=0;line<2;line++)
                    {
                        double y=line==0 ? top : bottom;
                        foreach(double offset in new[] {-4.0,4.0})dc.DrawLine(new Pen(beam==selectedBeam && i==selectedRun && line==selectedLine ? Brushes.DodgerBlue : Timber,2),new Point(xx(stock.StartMm),y+offset),new Point(xx(stock.EndMm),y+offset));
                        MemberTargets.Add(Tuple.Create(new Rect(xx(stock.StartMm),y-9,stock.StockLengthMm*scale,18),beam,i,line));

                    }
                    Label(dc,$"P{i+1} · {stock.StockLengthMm:0}",xx(stock.StartMm+stock.StockLengthMm/2),bottom+12,true);DrawnPrimaryLengthCount++;
                    double dimY=bottom+42;
                    Dim(dc,xx(stock.StartMm),xx(b.PositionsMm[stock.FirstSupport]),dimY,(b.PositionsMm[stock.FirstSupport]-stock.StartMm).ToString("0.###"));
                    Dim(dc,xx(b.PositionsMm[stock.LastSupport]),xx(stock.EndMm),dimY,(stock.EndMm-b.PositionsMm[stock.LastSupport]).ToString("0.###"));
                    if(i>0)dc.DrawLine(new Pen(Steel,1){DashStyle=DashStyles.Dot},new Point(xx(stock.StartMm),top-6),new Point(xx(stock.StartMm),bottom+8));
                }
                for(int leg=0;leg<b.PositionsMm.Length;leg++)
                {
                    double x=xx(b.PositionsMm[leg]);dc.DrawLine(new Pen(Steel,1.5){DashStyle=b.Rotated ? DashStyles.Dash : DashStyles.Solid},new Point(x,top),new Point(x,bottom));
                    foreach(double y in new[] {top,bottom}) {dc.DrawEllipse(Steel,null,new Point(x,y),3.5,3.5);DrawnTowerLegCount++;}
                    if(leg==b.Joined.Length)continue;double next=xx(b.PositionsMm[leg+1]);
                    if(b.Joined[leg])foreach(double y in new[] {top,bottom})dc.DrawLine(new Pen(Brushes.Firebrick,1.5){DashStyle=b.Rotated ? DashStyles.Solid : DashStyles.Dash},new Point(x,y),new Point(next,y));
                }
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
            Section(dc,endY+60);
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
                foreach(var solid in support.Solids)
                {
                    var rect=new Rect(new Point(xx(solid.X0),yy(solid.Y0)),new Point(xx(solid.X1),yy(solid.Y1)));
                    int member=Array.IndexOf(support.Members,solid.Member);
                    dc.DrawRectangle(solid.IsLeg ? Steel : solid.Member==null ? Brushes.SandyBrown : Timber,new Pen(selectedBeam==encoded && selectedRun==member ? Brushes.DodgerBlue : Brushes.SaddleBrown,1),rect);
                    if(solid.IsLeg)DrawnTowerLegCount++;
                    if(member>=0)MemberTargets.Insert(0,Tuple.Create(rect,encoded,member,0));
                }
                foreach(var b in support.Bearings)dc.DrawEllipse(Brushes.DarkRed,null,new Point(xx(b.Xmm),yy(b.Ymm)),3,3);
            }
        }
        private void Section(DrawingContext dc,double y0)
        {
            Label(dc,"Section · red: slab feet · blue: shutter loads · select H20 for diagrams",30,y0);
            double min=double.MaxValue,max=double.MinValue;
            for(int b=0;b<result.Beams.Length;b++) {var t=result.Beams[b].Transfer;min=Math.Min(min,result.BeamCentresMm[b]+t.LowerStockStartMm);max=Math.Max(max,result.BeamCentresMm[b]+t.LowerStockStartMm+t.LowerStockLengthMm);}
            foreach(var support in result.CrossSupports)foreach(var solid in support.Solids) {min=Math.Min(min,solid.Y0);max=Math.Max(max,solid.Y1);}
            double highest=result.Input.Beams.Max(b=>b.BottomElevationMm+b.DepthMm),lowest=result.Input.Beams.Min(b=>b.BottomElevationMm-400);
            if(result.CrossSupports.Length>0) {highest=Math.Max(highest,result.CrossSupports.Max(c=>c.Input.BottomElevationMm+c.Input.DepthMm));lowest=Math.Min(lowest,result.CrossSupports.Min(c=>c.Input.BottomElevationMm-400));}
            double scale=(ActualWidth-80)/(max-min),depthScale=150/Math.Max(200,highest-lowest);Func<double,double> xx=x=>40+(x-min)*scale;double top=y0+100;
            for(int beam=0;beam<result.Beams.Length;beam++)
            {
                var b=result.Beams[beam];var input=result.Input.Beams[beam];var t=b.Transfer;double centre=result.BeamCentresMm[beam],bottom=top+(highest-input.BottomElevationMm)*depthScale,beamTop=top+(highest-input.BottomElevationMm-input.DepthMm)*depthScale;
                dc.DrawRectangle(Concrete,new Pen(Steel,1),new Rect(xx(centre-input.WidthMm/2),beamTop,input.WidthMm*scale,bottom-beamTop));
                Label(dc,$"B{beam+1}",xx(centre),top-20,true);
                int row=Enumerable.Range(0,t.RowsMm.Length).OrderByDescending(r=>t.LowerCases.Max(c=>c[r].Utilization)).First(),encoded=b.StockPlan.Pieces.Length+row;
                double start=xx(centre+t.LowerStockStartMm),end=xx(centre+t.LowerStockStartMm+t.LowerStockLengthMm);
                dc.DrawRectangle(Timber,new Pen(beam==selectedBeam && selectedRun==encoded ? Brushes.DodgerBlue : Brushes.SaddleBrown,1),new Rect(start,bottom,end-start,200*depthScale));
                MemberTargets.Add(Tuple.Create(new Rect(start,bottom,end-start,15),beam,encoded,0));
                foreach(double support in new[] {-b.WidthMm/2,b.WidthMm/2})dc.DrawLine(new Pen(Steel,3),new Point(xx(centre+support),bottom+13),new Point(xx(centre+support),bottom+33));
                Dim(dc,start,end,bottom+43,$"H20 {t.LowerStockLengthMm:0}");DrawnSecondaryLengthCount++;

            }
            for(int bay=0;bay<result.Bays.Length;bay++)
            {
                var slab=result.Input.Bays[bay];var plan=result.Bays[bay];
                if(plan==null)
                {
                    double midpoint=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2+slab.WidthMm/2;
                    Label(dc,$"S{bay+1}: separate slab formwork required",xx(midpoint),top-40,true);
                    Label(dc,"Not calculated; no load transfer to these beams",xx(midpoint),top-22,true);
                    continue;
                }
                double face=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2,other=face+slab.WidthMm;
                double a=face+plan.EdgeMm,b=a+plan.StockLengthMm,near=face+plan.NearFootMm,far=other-plan.FarFootMm,soffit=top+(highest-Math.Max(result.Input.Beams[bay].BottomElevationMm+result.Input.Beams[bay].DepthMm,result.Input.Beams[bay+1].BottomElevationMm+result.Input.Beams[bay+1].DepthMm)+slab.ThicknessMm)*depthScale;
                dc.DrawRectangle(Brushes.LightSteelBlue,null,new Rect(xx(face),top,slab.WidthMm*scale,slab.ThicknessMm*depthScale));
                int row=Array.IndexOf(result.Beams[bay].Transfer.RightUpperScales,result.Beams[bay].Transfer.RightUpperScales.Max());
                if(result.SlabRowCount(bay)==0)continue;
                int encoded=result.Beams[bay].StockPlan.Pieces.Length+result.Beams[bay].Transfer.RowsMm.Length+row*2+1;
                dc.DrawLine(new Pen(bay==selectedBeam && selectedRun==encoded ? Brushes.DodgerBlue : Timber,3),new Point(xx(a),soffit+7),new Point(xx(b),soffit+7));
                MemberTargets.Add(Tuple.Create(new Rect(xx(a),soffit,plan.StockLengthMm*scale,15),bay,encoded,0));
                for(int side=0;side<2;side++)
                {
                    int beam=bay+side;double bottom=top+(highest-result.Input.Beams[beam].BottomElevationMm)*depthScale,x=side==0 ? near : far;
                    dc.DrawLine(new Pen(Brushes.DarkRed,2),new Point(xx(x),soffit+13),new Point(xx(x),bottom));Arrow(dc,xx(x),bottom,Brushes.DarkRed);
                    double shutter=side==0 ? face+100 : other-100;dc.DrawLine(new Pen(Brushes.SteelBlue,2),new Point(xx(shutter),soffit),new Point(xx(shutter),bottom));Arrow(dc,xx(shutter),bottom,Brushes.SteelBlue);
                    if(ShowAllDimensions || selectedBeam==beam)
                    {
                        Label(dc,$"Edge {plan.EdgeMm:0}",xx(shutter),top-40,true);
                        Dim(dc,xx(side==0 ? face : far),xx(side==0 ? near : other),soffit+29,$"Foot { (side==0 ? plan.NearFootMm : plan.FarFootMm):0}");
                    }
                }
                Dim(dc,xx(a),xx(b),top-59,$"S{bay+1} H20 {plan.StockLengthMm:0}{(plan.IsCustomLength ? " custom" : "")}");
            }
            DrawCrossSection(dc,xx,z=>top+(highest-z)*depthScale);
        }
        private void DrawCrossSection(DrawingContext dc,Func<double,double> xx,Func<double,double> zz)
        {
            var support=result.CrossSupports.FirstOrDefault(c=>selectedBeam==-c.CrossingIndex-1)??result.CrossSupports.FirstOrDefault();if(support==null)return;
            int bay=support.Input.BayNumber-1;double face=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2;
            dc.DrawRectangle(Concrete,new Pen(Steel,1),new Rect(new Point(xx(face),zz(support.Input.BottomElevationMm+support.Input.DepthMm)),new Point(xx(face+result.Input.Bays[bay].WidthMm),zz(support.Input.BottomElevationMm))));
            Label(dc,$"X{support.CrossingIndex+1} section @ x={support.Input.PositionMm:0.###} · bottom {support.Input.BottomElevationMm:0.###} mm",xx(face),zz(support.Input.BottomElevationMm)-20);
            foreach(var solid in support.Solids)
            {
                if(solid.IsLeg) {dc.DrawLine(new Pen(Steel,2),new Point(xx((solid.Y0+solid.Y1)/2),zz(solid.Z1)),new Point(xx((solid.Y0+solid.Y1)/2),zz(solid.Z1)+35));continue;}
                var rect=new Rect(new Point(xx(solid.Y0),zz(solid.Z1)),new Point(xx(solid.Y1),zz(solid.Z0)));int member=Array.IndexOf(support.Members,solid.Member);
                dc.DrawRectangle(solid.Member==null ? Brushes.SandyBrown : Timber,new Pen(selectedBeam==-support.CrossingIndex-1 && selectedRun==member ? Brushes.DodgerBlue : Brushes.SaddleBrown,1),rect);
                if(member>=0)MemberTargets.Insert(0,Tuple.Create(rect,-support.CrossingIndex-1,member,0));
            }
            foreach(var bearing in support.Bearings.GroupBy(b=>new {b.Ymm,b.FillerHeightMm}).Select(g=>g.First()))
            {
                dc.DrawEllipse(Brushes.DarkRed,null,new Point(xx(bearing.Ymm),zz(bearing.ReceivingTopMm)),3,3);
                Label(dc,bearing.FillerHeightMm<=1e-7 ? "Direct bearing" : $"Filler {bearing.FillerHeightMm:0.###} mm",xx(bearing.Ymm)+4,zz(bearing.H20UndersideMm)+3);
            }
        }
        private static void Arrow(DrawingContext dc,double x,double y,Brush colour) {dc.DrawLine(new Pen(colour,2),new Point(x-4,y-6),new Point(x,y));dc.DrawLine(new Pen(colour,2),new Point(x+4,y-6),new Point(x,y));}
    }
}


