using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace EngineeringDashboard
{
    public sealed partial class TowerDiagramWindow
    {
        private static Brush PlotBackground(int index)=>new SolidColorBrush(index==0 ? Color.FromRgb(255,255,217) : index==1 ? Color.FromRgb(220,250,220) : Color.FromRgb(218,255,255));
        private static void Axes(DrawingContext dc,double left,double right,double top,double bottom,double range,int index,Action<string,double,double> label)
        {
            dc.DrawRectangle(PlotBackground(index),null,new Rect(left,top,right-left,bottom-top));
            for(int tick=-2;tick<=2;tick++)
            {
                double value=range*tick/2,y=(top+bottom)/2-value/range*(bottom-top)/2;
                dc.DrawLine(new Pen(tick==0 ? Brushes.Gray : Brushes.LightGray,1),new Point(left,y),new Point(right,y));
                label(value.ToString("0.##",CultureInfo.CurrentCulture),3,y-7);
            }
            string unit=index==0 ? "Shear (kN)" : index==1 ? "Moment (kNm)" : "Deflection (mm)";
            double centre=(top+bottom)/2;
            dc.PushTransform(new RotateTransform(-90,45,centre));label(unit,45-unit.Length*2.75,centre-6);dc.Pop();
            label("Distance (mm)",(left+right)/2-35,bottom+4);
        }
        internal sealed class MemberLoadingPlot : FrameworkElement
        {
            readonly double length;readonly double[] supports,reactions;readonly BeamDistributedLoad[] uniform;readonly BeamPointLoad[] points;
            internal double LengthMm=>length;
            internal double[] SupportPositions=>supports;
            internal double[] ReactionValues=>reactions;
            internal BeamPointLoad[] PointLoads=>points;
            internal sealed class Target {internal Rect Bounds;internal int Kind,Index;}
            internal readonly System.Collections.Generic.List<Target> Targets=new System.Collections.Generic.List<Target>();
            internal event Action<int,int> Selected;
            private Target selected,selectedLoad,selectedReaction;
            internal int SelectedKind=>selected?.Kind??-1;
            internal int SelectedIndex=>selected?.Index??-1;
            internal bool SelectAt(Point point)
            {
                var hit=Targets.LastOrDefault(t=>t.Bounds.Contains(point));if(hit==null)return false;
                selected=hit;if(hit.Kind==2)selectedReaction=hit;else selectedLoad=hit;InvalidateVisual();Selected?.Invoke(hit.Kind,hit.Index);return true;
            }
            internal void ClearSelection(){selected=selectedLoad=selectedReaction=null;InvalidateVisual();}
            internal void ClearLoadSelection(){if(selected?.Kind!=2)selected=null;selectedLoad=null;InvalidateVisual();}
            internal void ClearReactionSelection(){if(selected?.Kind==2)selected=null;selectedReaction=null;InvalidateVisual();}
            protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
            {base.OnMouseLeftButtonUp(e);if(SelectAt(e.GetPosition(this)))e.Handled=true;}
            internal BeamDistributedLoad[] SourceUniform {get;}
            internal BeamDistributedLoad[] CombinedUniform=>uniform;
            internal int DrawnSupportCount {get;private set;}
            internal int DrawnReactionLabelCount {get;private set;}
            internal readonly System.Collections.Generic.List<Rect> ReactionLabelBounds=new System.Collections.Generic.List<Rect>();
            internal int DrawnPointCount {get;private set;}
            internal int DrawnUniformCount {get;private set;}
            internal readonly System.Collections.Generic.List<Rect> LabelBounds=new System.Collections.Generic.List<Rect>();
            internal MemberLoadingPlot(double l,double[] s,double[] r,BeamDistributedLoad[] u,BeamPointLoad[] p)
            {length=l;supports=s;reactions=r;SourceUniform=u??new BeamDistributedLoad[0];uniform=Combine(SourceUniform);points=(p??new BeamPointLoad[0]).Where(load=>Math.Abs(load.Kn)>1e-10).ToArray();MinHeight=270;Focusable=true;Cursor=Cursors.Hand;}
            internal static BeamDistributedLoad[] Combine(BeamDistributedLoad[] loads)
            {
                var cuts=loads.SelectMany(q=>new[] {q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();
                var bands=new System.Collections.Generic.List<BeamDistributedLoad>();
                for(int i=1;i<cuts.Length;i++)
                {
                    double middle=(cuts[i-1]+cuts[i])/2,q=loads.Where(x=>x.StartMm<middle && x.EndMm>middle).Sum(x=>x.KnPerM);
                    if(Math.Abs(q)<1e-10)continue;
                    var prior=bands.LastOrDefault();
                    if(prior!=null && prior.EndMm==cuts[i-1] && prior.KnPerM==q)prior.EndMm=cuts[i];
                    else bands.Add(new BeamDistributedLoad {StartMm=cuts[i-1],EndMm=cuts[i],KnPerM=q});
                }
                return bands.ToArray();
            }
            private sealed class LoadLabel {internal string Text;internal Rect Bounds;internal int Kind,Index;}
            private System.Collections.Generic.List<LoadLabel> PlaceLoadLabels(double width)
            {
                double left=65,right=Math.Max(left+20,width-20);var result=new System.Collections.Generic.List<LoadLabel>();
                Action<string,double,int,int> place=(value,position,kind,index)=>
                {
                    var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    double x=Math.Max(2,Math.Min(width-text.Width-2,left+position/length*(right-left)-text.Width/2));
                    var box=new Rect(x-2,8,text.Width+4,text.Height+2);
                    while(result.Any(b=>b.Bounds.IntersectsWith(box)))box.Offset(0,text.Height+8);
                    result.Add(new LoadLabel {Text=value,Bounds=box,Kind=kind,Index=index});
                };
                for(int i=0;i<uniform.Length;i++)place($"{LoadValue.Format(uniform[i].KnPerM)} kN/m",(uniform[i].StartMm+uniform[i].EndMm)/2,0,i);
                for(int i=0;i<points.Length;i++)place((Math.Abs(points[i].Kn)<.005 ? (points[i].Kn>0 ? "<0.01" : "−<0.01") : points[i].Kn.ToString("0.##",CultureInfo.CurrentCulture))+" kN",points[i].PositionMm,1,i);
                return result;
            }
            private double BeamY(double width)=>Math.Max(95,PlaceLoadLabels(width).Select(l=>l.Bounds.Bottom).DefaultIfEmpty(0).Max()+66);
            private FormattedText ReactionText(int index)=>new FormattedText($"R{index+1}: {reactions[index]:0.##} kN",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            private Rect[] PlaceReactionLabels(double width,double beamY)
            {
                double left=65,right=Math.Max(left+20,width-20);var boxes=new Rect[supports.Length];
                for(int i=0;i<supports.Length;i++)
                {
                    var text=ReactionText(i);double x=left+supports[i]/length*(right-left);
                    x=Math.Max(2,Math.Min(width-text.Width-2,x-text.Width/2));
                    var box=new Rect(x-2,beamY+94,text.Width+4,text.Height+2);
                    // Preserve every value. Nearby supports use additional rows rather
                    // than silently losing a reaction to the general annotation filter.
                    while(boxes.Take(i).Any(b=>b.IntersectsWith(box)))box.Offset(0,text.Height+8);
                    boxes[i]=box;
                }
                return boxes;
            }
            protected override Size MeasureOverride(Size availableSize)
            {
                double width=double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
                var boxes=PlaceReactionLabels(width,BeamY(width));double bottom=boxes.Select(b=>b.Bottom).DefaultIfEmpty(210).Max();
                return new Size(width,Math.Max(270,bottom+60));
            }
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);Targets.Clear();LabelBounds.Clear();ReactionLabelBounds.Clear();DrawnSupportCount=DrawnReactionLabelCount=DrawnPointCount=DrawnUniformCount=0;
                double left=65,right=Math.Max(left+20,ActualWidth-20),beamY=BeamY(ActualWidth);
                double maximum=uniform.Select(q=>Math.Abs(q.KnPerM)).DefaultIfEmpty(1).Max();
                Func<double,double> xx=x=>left+x/length*(right-left);
                Action<string,double,double,int,int> label=(value,x,y,kind,index)=>
                {
                    var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    x=Math.Max(2,Math.Min(ActualWidth-text.Width-2,x-text.Width/2));var box=new Rect(x-2,y-1,text.Width+4,text.Height+2);
                    if(LabelBounds.Any(b=>b.IntersectsWith(box)))return;
                    LabelBounds.Add(box);dc.DrawText(text,new Point(x,y));if(kind>=0)Targets.Add(new Target {Bounds=box,Kind=kind,Index=index});
                };
                double stroke=1.5;
                Action<double,double,double,Brush> arrow=(x,from,to,colour)=>
                {
                    double sign=Math.Sign(to-from);var pen=new Pen(colour,stroke);dc.DrawLine(pen,new Point(x,from),new Point(x,to));
                    dc.DrawLine(pen,new Point(x,to),new Point(x-4,to-sign*7));dc.DrawLine(pen,new Point(x,to),new Point(x+4,to-sign*7));
                };
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
                dc.DrawLine(new Pen(Brushes.DarkSlateGray,4),new Point(left,beamY),new Point(right,beamY));
                for(int i=0;i<uniform.Length;i++)
                {
                    stroke=selectedLoad?.Kind==0 && selectedLoad.Index==i ? 3 : 1.5;var q=uniform[i];double sign=Math.Sign(q.KnPerM),tip=beamY-sign*5,y=tip-sign*52*Math.Abs(q.KnPerM)/maximum,a=xx(q.StartMm),b=xx(q.EndMm);
                    dc.DrawLine(new Pen(Brushes.Firebrick,stroke>2 ? 2.5 : 1),new Point(a,y),new Point(b,y));
                    int count=Math.Max(1,(int)((b-a)/45));
                    for(int k=0;k<=count;k++)arrow(a+(b-a)*k/count,y,tip,Brushes.Firebrick);
                    Targets.Add(new Target {Bounds=new Rect(a-5,Math.Min(y,tip),b-a+10,Math.Abs(tip-y)+2),Kind=0,Index=i});
                    DrawnUniformCount++;
                }
                for(int pointIndex=0;pointIndex<points.Length;pointIndex++)
                {
                    stroke=selectedLoad?.Kind==1 && selectedLoad.Index==pointIndex ? 3 : 1.5;var p=points[pointIndex];double x=xx(p.PositionMm),sign=Math.Sign(p.Kn),tip=beamY-sign*5,from=tip-sign*52;
                    arrow(x,from,tip,Brushes.Firebrick);
                    Targets.Add(new Target {Bounds=new Rect(x-7,Math.Min(from,tip),14,Math.Abs(from-tip)+2),Kind=1,Index=pointIndex});
                    DrawnPointCount++;
                }
                var reactionBoxes=PlaceReactionLabels(ActualWidth,beamY);
                for(int i=0;i<supports.Length;i++)
                {
                    stroke=selectedReaction?.Index==i ? 3 : 1.5;double x=xx(supports[i]);var triangle=new StreamGeometry();using(var c=triangle.Open()){c.BeginFigure(new Point(x,beamY+3),true,true);c.LineTo(new Point(x-7,beamY+15),true,false);c.LineTo(new Point(x+7,beamY+15),true,false);}
                    dc.DrawGeometry(Brushes.SlateGray,null,triangle);dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(x-10,beamY+19),new Point(x+10,beamY+19));
                    arrow(x,beamY+(reactions[i]>=0 ? 90 : 60),beamY+(reactions[i]>=0 ? 60 : 90),Brushes.SeaGreen);
                    var box=reactionBoxes[i];dc.DrawText(ReactionText(i),new Point(box.Left+2,box.Top+1));
                    if(selectedReaction?.Index==i){dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(230,248,235)),new Pen(Brushes.SeaGreen,1),box,3,3);dc.DrawText(ReactionText(i),new Point(box.Left+2,box.Top+1));}
                    ReactionLabelBounds.Add(box);LabelBounds.Add(box);Targets.Add(new Target {Bounds=box,Kind=2,Index=i});Targets.Add(new Target {Bounds=new Rect(x-8,beamY+58,16,34),Kind=2,Index=i});DrawnReactionLabelCount++;DrawnSupportCount++;
                }
                double dimensionY=Math.Max(beamY+130,reactionBoxes.Select(b=>b.Bottom).DefaultIfEmpty(210).Max()+15);
                var stations=new[] {0.0}.Concat(supports).Concat(new[] {length}).Distinct().OrderBy(x=>x).ToArray();
                for(int i=1;i<stations.Length;i++)
                {
                    double a=xx(stations[i-1]),b=xx(stations[i]),y=dimensionY;dc.DrawLine(new Pen(Brushes.Gray,1),new Point(a,y),new Point(b,y));
                    foreach(double x in new[] {a,b})dc.DrawLine(new Pen(Brushes.Gray,1),new Point(x,y-4),new Point(x,y+4));
                    string value=$"{stations[i]-stations[i-1]:0.###}";if(b-a>value.Length*7+8)label(value,(a+b)/2,y+5,-1,-1);
                }
                label("0",left,dimensionY+25,-1,-1);label($"{length:0.###} mm",right,dimensionY+25,-1,-1);
                foreach(var item in PlaceLoadLabels(ActualWidth))
                {
                    double anchor=item.Kind==0 ? (uniform[item.Index].StartMm+uniform[item.Index].EndMm)/2 : points[item.Index].PositionMm;
                    dc.DrawLine(new Pen(Brushes.LightGray,.7),new Point(item.Bounds.Left+item.Bounds.Width/2,item.Bounds.Bottom+2),new Point(xx(anchor),beamY-59));
                    bool highlighted=selectedLoad?.Kind==item.Kind && selectedLoad.Index==item.Index;
                    dc.DrawRoundedRectangle(highlighted ? new SolidColorBrush(Color.FromRgb(255,236,236)) : Brushes.White,highlighted ? new Pen(Brushes.Firebrick,1) : null,item.Bounds,3,3);
                    dc.DrawText(new FormattedText(item.Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(item.Bounds.Left+2,item.Bounds.Top+1));
                    LabelBounds.Add(item.Bounds);Targets.Add(new Target {Bounds=item.Bounds,Kind=item.Kind,Index=item.Index});
                }
                if(selected!=null)
                {
                    var target=Targets.LastOrDefault(t=>t.Kind==selected.Kind && t.Index==selected.Index);
                    if(target!=null)dc.DrawRoundedRectangle(null,new Pen(selected.Kind==2 ? Brushes.SeaGreen : Brushes.Firebrick,1),target.Bounds,3,3);
                }
            }
        }
        internal sealed partial class LoadedResponsePlot : FrameworkElement
        {
            readonly double length, strengthLimit, peak;
            readonly double[] supports, additionalStations;
            readonly int index, denominator;
            readonly bool ws10;
            readonly Func<double, bool, double> response;
            internal int DiagramIndex => index;
            internal LoadedResponsePlot(double length, double[] supports, double[] additionalStations,
                int index, int denominator, double strengthLimit, double peak, bool ws10, Func<double, bool, double> response)
            {
                this.length = length;
                this.supports = supports;
                this.additionalStations = additionalStations;
                this.index = index;
                this.denominator = denominator;
                this.strengthLimit = strengthLimit;
                this.peak = peak;
                this.ws10 = ws10;
                this.response = response;
                Height = 220;
            }
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc); double left=65,right=Math.Max(left+20,ActualWidth-20),top=20,bottom=175;
                var stations=new[] {0.0}.Concat(supports).Concat(new[] {length}).Distinct().OrderBy(x=>x).ToArray();
                double limit=index!=2 ? strengthLimit : Enumerable.Range(1,stations.Length-1).Max(i=>stations[i]-stations[i-1])/denominator;
                double range=Math.Max(.001,Math.Max(peak,limit)*1.15);
                Func<double,double> xx=x=>left+x/length*(right-left),yy=y=>(top+bottom)/2-y/range*(bottom-top)/2;
                Action<string,double,double> label=(s,x,y)=>dc.DrawText(new FormattedText(s,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.Black,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,Height));Axes(dc,left,right,top,bottom,range,index,label);
                var limitStations=index==1 && ws10 ? stations.Concat(new[] {Math.Min(300,length),Math.Max(0,length-300)}).Distinct().OrderBy(x=>x).ToArray() : stations;
                for(int i=1;i<limitStations.Length;i++)
                {
                    double allowable=index==2 ? (limitStations[i]-limitStations[i-1])/denominator : index==1 && ws10 && ((limitStations[i-1]+limitStations[i])/2<300 || (limitStations[i-1]+limitStations[i])/2>length-300) ? 10 : limit;
                    foreach(double sign in new[] {-1.0,1.0}) dc.DrawLine(new Pen(Brushes.IndianRed,1){DashStyle=DashStyles.Dash},new Point(xx(limitStations[i-1]),yy(sign*allowable)),new Point(xx(limitStations[i]),yy(sign*allowable)));
                    if(index==2) label($"±{allowable:0.##}",(xx(limitStations[i-1])+xx(limitStations[i]))/2-14,yy(allowable)-14);
                }
                foreach(double s in supports) dc.DrawLine(new Pen(Brushes.LightGray,1){DashStyle=DashStyles.Dash},new Point(xx(s),top),new Point(xx(s),bottom));
                var jumps=supports.Concat(additionalStations).Distinct().ToArray();
                var positions=Enumerable.Range(0,401).Select(i=>length*i/400).Concat(jumps).Distinct().OrderBy(x=>x);Point? previous=null;
                foreach(double x in positions) foreach(bool side in jumps.Contains(x) ? new[] {false,true} : new[] {true})
                {
                    var point=new Point(xx(x),yy(response(x,side)));if(previous.HasValue)dc.DrawLine(new Pen(Brushes.SteelBlue,2),previous.Value,point);previous=point;
                }
                label("0",left,bottom+4);label($"{length:0} mm",right-60,bottom+4);
            }
        }
    }
}
