using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal enum FormworkObject { Beam, Slab, Crossing }
    /// <summary>Plan geometry uses one mm coordinate system for drawing, picking and edits.</summary>
    internal sealed class BeamFormworkCanvas : FrameworkElement
    {
        internal readonly BeamCalculatorViewModel Model;
        internal FormworkObject SelectedKind=FormworkObject.Beam;
        internal int SelectedIndex;
        internal bool Formwork, AllDimensions, AddCrossing;
        private readonly HashSet<Tuple<FormworkObject,int>> selection=new HashSet<Tuple<FormworkObject,int>> {Tuple.Create(FormworkObject.Beam,0)};
        internal Tuple<FormworkObject,int>[] SelectedObjects=>selection.OrderBy(x=>x.Item1).ThenBy(x=>x.Item2).ToArray();
        internal Tuple<FormworkObject,int> HoveredObject {get;private set;}
        private bool selecting,addWindow;
        private Point windowEnd;
        internal double Zoom {get;private set;}=.06;
        internal Point Pan {get;private set;}=new Point(80,100);
        internal Rect SceneBounds {get;private set;}
        internal readonly List<Tuple<Rect,FormworkObject,int>> ObjectTargets=new List<Tuple<Rect,FormworkObject,int>>();
        internal readonly List<Tuple<Rect,int,int,int>> MemberTargets=new List<Tuple<Rect,int,int,int>>();
        internal readonly List<Tuple<Rect,string>> LegTargets=new List<Tuple<Rect,string>>();
        internal readonly List<Rect> AnnotationBounds=new List<Rect>();
        private readonly List<Tuple<Rect,FormworkObject,int,string>> dimensions=new List<Tuple<Rect,FormworkObject,int,string>>();
        internal event Action SelectionChanged;
        internal event Action<FormworkObject,int,string> DimensionSelected;
        internal event Action<int,int,int> MemberSelected;
        internal event Action<int,double> CrossingRequested;
        internal event Action<FormworkObject,int,double> MoveRequested;
        internal Func<bool> CanInteract;
        private bool fitted=true;
        private Point mouseStart,panStart;
        private bool panning,dragging;
        private double dragDelta;
        private int memberBeam=-1,memberRun=-1,memberLine;
        internal int DrawnLegCount {get;private set;}
        internal int DrawnCrossH20Count {get;private set;}
        internal BeamFormworkCanvas(BeamCalculatorViewModel model)
        {Model=model;Focusable=true;ClipToBounds=true;SizeChanged+=(s,e)=> {if(fitted || e.PreviousSize.Width==0)Fit();};}
        internal double[] Centres()
        {
            var beams=Beams();var ys=new double[beams.Length];
            for(int i=1;i<ys.Length;i++)ys[i]=ys[i-1]+beams[i-1].WidthMm/2+Model.NetworkBays[i-1].WidthMm+beams[i].WidthMm/2;
            return ys;
        }
        private TransferBeamRow[] Beams()=>Model.IsNetworkMode ? Model.NetworkBeams.ToArray() : new[] {new TransferBeamRow {WidthMm=Model.Width,DepthMm=Model.Depth}};
        private bool ValidGeometry()=>TowerBeamAnalysis.Finite(Model.BeamLengthMm) && Model.BeamLengthMm>0 && Model.BeamLengthMm<=50000
            && Beams().All(b=>TowerBeamAnalysis.Finite(b.WidthMm) && b.WidthMm>0 && b.WidthMm<=10000)
            && (!Model.IsNetworkMode || Model.NetworkBays.All(b=>TowerBeamAnalysis.Finite(b.WidthMm) && b.WidthMm>0 && b.WidthMm<=20000));
        private Rect Bounds()
        {
            var beams=Beams();var ys=Centres();double min=double.MaxValue,max=double.MinValue;
            for(int i=0;i<beams.Length;i++)
            {
                var result=Layout(i);double half=Math.Max(beams[i].WidthMm,result?.WidthMm??0)/2;
                min=Math.Min(min,ys[i]-half);max=Math.Max(max,ys[i]+half);
            }
            double minX=0,maxX=Model.BeamLengthMm;
            if(Model.IsCalculationValid && Model.NetworkResult!=null)foreach(var solid in Model.NetworkResult.CrossSupports.SelectMany(c=>c.Solids))
            {minX=Math.Min(minX,solid.X0);maxX=Math.Max(maxX,solid.X1);min=Math.Min(min,solid.Y0);max=Math.Max(max,solid.Y1);}
            return new Rect(minX,min,maxX-minX,max-min);
        }
        private TowerLayout Layout(int beam)=>!Model.IsCalculationValid ? null : Model.IsNetworkMode ? Model.NetworkResult?.Beams[beam] : Model.TowerResult;
        internal void Fit()
        {
            if(!ValidGeometry() || ActualWidth<100 || ActualHeight<100)return;
            SceneBounds=Bounds();Zoom=Math.Max(.001,Math.Min((ActualWidth-140)/SceneBounds.Width,(ActualHeight-160)/Math.Max(500,SceneBounds.Height)));
            Pan=new Point((ActualWidth-SceneBounds.Width*Zoom)/2,70-SceneBounds.Top*Zoom);fitted=true;InvalidateVisual();
        }
        internal void RefitIfNeeded(){if(fitted)Fit();}
        internal void ZoomAt(double factor,Point anchor)
        {
            double next=Math.Max(.001,Math.Min(1,Zoom*factor)),ratio=next/Zoom;
            Pan=new Point(anchor.X-(anchor.X-Pan.X)*ratio,anchor.Y-(anchor.Y-Pan.Y)*ratio);Zoom=next;fitted=false;InvalidateVisual();
        }
        internal Point Screen(double x,double y)=>new Point(Pan.X+x*Zoom,Pan.Y+y*Zoom);
        internal Point World(Point screen)=>new Point((screen.X-Pan.X)/Zoom,(screen.Y-Pan.Y)/Zoom);
        internal void PanBy(Vector delta) {Pan+=delta;fitted=false;InvalidateVisual();}
        internal void MoveSelection(double deltaMm)
        {
            if(Formwork || CanInteract!=null && !CanInteract())return;
            if(SelectedKind==FormworkObject.Crossing || SelectedKind==FormworkObject.Beam && SelectedIndex>0)
                MoveRequested?.Invoke(SelectedKind,SelectedIndex,Math.Round(deltaMm/25)*25);
        }
        internal void Select(FormworkObject kind,int index,bool notify=true)
        {SetSelection(new[] {Tuple.Create(kind,index)},kind,index,notify);}
        internal void SetSelection(IEnumerable<Tuple<FormworkObject,int>> objects,FormworkObject kind,int index,bool notify=true)
        {
            selection.Clear();foreach(var item in objects)selection.Add(item);
            SelectedKind=kind;SelectedIndex=index;memberBeam=-1;HoveredObject=null;InvalidateVisual();if(notify)SelectionChanged?.Invoke();
        }
        internal void ClearSelection()
        {SetSelection(new Tuple<FormworkObject,int>[0],SelectedKind,SelectedIndex);}
        private void Toggle(FormworkObject kind,int index)
        {
            var item=Tuple.Create(kind,index);
            if(!selection.Remove(item))selection.Add(item);
            var active=selection.Contains(item) ? item : SelectedObjects.LastOrDefault();
            if(active!=null){SelectedKind=active.Item1;SelectedIndex=active.Item2;}
            memberBeam=-1;InvalidateVisual();SelectionChanged?.Invoke();
        }
        internal void SelectWindow(Point start,Point end,bool add=false)
        {
            if(Formwork || AddCrossing || CanInteract!=null && !CanInteract())return;
            var window=new Rect(start,end);bool crossing=end.X<start.X;
            var hits=ObjectTargets.Where(h=>crossing ? window.IntersectsWith(h.Item1) : window.Contains(h.Item1)).Select(h=>Tuple.Create(h.Item2,h.Item3)).Distinct().OrderBy(h=>h.Item1).ThenBy(h=>h.Item2).ToArray();
            if(!add)selection.Clear();foreach(var hit in hits)selection.Add(hit);
            var active=hits.LastOrDefault()??SelectedObjects.LastOrDefault();
            if(active!=null){SelectedKind=active.Item1;SelectedIndex=active.Item2;}
            memberBeam=-1;InvalidateVisual();SelectionChanged?.Invoke();
        }
        internal void HighlightAt(Point point)
        {
            var hit=Formwork || AddCrossing || Model.IsOptimizing ? null : ObjectTargets.FirstOrDefault(h=>h.Item1.Contains(point));
            var next=hit==null ? null : Tuple.Create(hit.Item2,hit.Item3);
            if(Equals(next,HoveredObject))return;HoveredObject=next;InvalidateVisual();
        }
        internal void CancelSelectionGesture()
        {selecting=dragging=false;HoveredObject=null;if(IsMouseCaptured)ReleaseMouseCapture();InvalidateVisual();}
        private bool IsSelected(FormworkObject kind,int index,Rect rect)
        {
            bool selected=selection.Contains(Tuple.Create(kind,index));
            if(!selecting || (windowEnd-mouseStart).Length<=4)return selected;
            var window=new Rect(mouseStart,windowEnd);bool hit=windowEnd.X<mouseStart.X ? window.IntersectsWith(rect) : window.Contains(rect);
            return hit || addWindow && selected;
        }
        private Pen ObjectPen(FormworkObject kind,int index,Rect rect,Brush normal,double width=1)
        {
            if(IsSelected(kind,index,rect))return new Pen(Brushes.DodgerBlue,2.5);
            if(Equals(HoveredObject,Tuple.Create(kind,index)))return new Pen(Brushes.DarkOrange,2){DashStyle=DashStyles.Dash};
            return new Pen(normal,width);
        }
        internal void SelectMember(int beam,int run,int line) {memberBeam=beam;memberRun=run;memberLine=line;InvalidateVisual();}
        private bool Label(DrawingContext dc,string text,Point at,Brush colour=null,bool centre=false)
        {
            var f=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,colour??Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if(centre)at.X-=f.Width/2;
            var box=new Rect(at.X-2,at.Y-1,f.Width+4,f.Height+2);
            if(box.Left<0 || box.Right>ActualWidth || box.Top<0 || box.Bottom>ActualHeight || AnnotationBounds.Any(r=>r.IntersectsWith(box)))return false;
            AnnotationBounds.Add(box);dc.DrawRectangle(Brushes.White,null,box);dc.DrawText(f,at);return true;
        }
        private void Dimension(DrawingContext dc,Point a,Point b,string text,FormworkObject kind,int index,string field,Brush colour=null,double labelOffset=4)
        {
            colour=colour??Brushes.SlateGray;dc.DrawLine(new Pen(colour,1),a,b);
            bool vertical=Math.Abs(a.X-b.X)<.1;
            foreach(var p in new[] {a,b})dc.DrawLine(new Pen(colour,1),p+(vertical ? new Vector(-4,0) : new Vector(0,-4)),p+(vertical ? new Vector(4,0) : new Vector(0,4)));
            Point label=vertical ? new Point(a.X+7,(a.Y+b.Y)/2-7) : new Point((a.X+b.X)/2,(a.Y+b.Y)/2+labelOffset);
            if(field==null && !vertical && Math.Abs(b.X-a.X)<text.Length*7+8)return;
            if(!Label(dc,text,label,colour,!vertical))return;
            if(field!=null)dimensions.Add(Tuple.Create(new Rect(label.X-(vertical ? 0 : 38),label.Y,76,18),kind,index,field));
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);ObjectTargets.Clear();MemberTargets.Clear();LegTargets.Clear();dimensions.Clear();AnnotationBounds.Clear();DrawnLegCount=DrawnCrossH20Count=0;
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
            if(!ValidGeometry()) {Label(dc,"Correct geometry to draw the plan.",new Point(30,30));return;}
            var beams=Beams();var ys=Centres();SceneBounds=Bounds();
            for(int i=0;i<beams.Length-1;i++)
            {
                double top=ys[i]+beams[i].WidthMm/2,bottom=ys[i+1]-beams[i+1].WidthMm/2;
                var rect=new Rect(Screen(0,top),Screen(Model.BeamLengthMm,bottom));
                bool separate=Model.IsCalculationValid && Model.NetworkResult?.Bays[i]==null;
                dc.DrawRectangle(separate ? Brushes.Moccasin : Brushes.AliceBlue,ObjectPen(FormworkObject.Slab,i,rect,Brushes.LightSteelBlue),rect);
                ObjectTargets.Add(Tuple.Create(rect,FormworkObject.Slab,i));
                if(separate)
                {
                    double centre=rect.Top+rect.Height/2,x=rect.Left+rect.Width/2;
                    Label(dc,$"S{i+1} · {Model.NetworkBays[i].WidthMm:0}",new Point(x,centre-26),centre:true);
                    Label(dc,$"t {Model.NetworkBays[i].ThicknessMm:0} · separate",new Point(x,centre-8),centre:true);
                    Label(dc,"formwork required",new Point(x,centre+10),centre:true);
                }
                else Label(dc,$"S{i+1} · {Model.NetworkBays[i].WidthMm:0} · t {Model.NetworkBays[i].ThicknessMm:0}",new Point(rect.Left+18,rect.Top+rect.Height/2-8));
                if(AllDimensions || selection.Count>0 && SelectedKind==FormworkObject.Slab && SelectedIndex==i)
                    Dimension(dc,new Point(rect.Right+24,rect.Top),new Point(rect.Right+24,rect.Bottom),Model.NetworkBays[i].WidthMm.ToString("0.###"),FormworkObject.Slab,i,"WidthMm");
            }
            if(Model.IsNetworkMode)for(int i=0;i<Model.CrossingBeams.Count;i++)
            {
                var c=Model.CrossingBeams[i];int bay=c.BayNumber-1;if(bay<0 || bay>=beams.Length-1 || !TowerBeamAnalysis.Finite(c.PositionMm) || !TowerBeamAnalysis.Finite(c.WidthMm) || c.WidthMm<=0)continue;
                var rect=new Rect(Screen(c.PositionMm-c.WidthMm/2,ys[bay]+beams[bay].WidthMm/2),Screen(c.PositionMm+c.WidthMm/2,ys[bay+1]-beams[bay+1].WidthMm/2));
                dc.DrawRectangle(Brushes.LightGray,ObjectPen(FormworkObject.Crossing,i,rect,Brushes.SlateGray,2),rect);
                ObjectTargets.Insert(0,Tuple.Create(rect,FormworkObject.Crossing,i));
                Label(dc,$"X{i+1} · {c.WidthMm:0}×{c.DepthMm:0}",new Point(rect.Right+7,rect.Top+rect.Height/2-8));
                if(AllDimensions || selection.Count>0 && SelectedKind==FormworkObject.Crossing && SelectedIndex==i)
                    Dimension(dc,Screen(0,ys[bay]+beams[bay].WidthMm/2+200),Screen(c.PositionMm,ys[bay]+beams[bay].WidthMm/2+200),$"x {c.PositionMm:0.###}",FormworkObject.Crossing,i,"PositionMm");
            }
            for(int i=0;i<beams.Length;i++)
            {
                var b=beams[i];var rect=new Rect(Screen(0,ys[i]-b.WidthMm/2),Screen(Model.BeamLengthMm,ys[i]+b.WidthMm/2));
                dc.DrawRectangle(Brushes.LightGray,ObjectPen(FormworkObject.Beam,i,rect,Brushes.SlateGray),rect);
                ObjectTargets.Insert(0,Tuple.Create(rect,FormworkObject.Beam,i));
                Label(dc,$"B{i+1} · {b.WidthMm:0}×{b.DepthMm:0}",new Point(rect.Left+8,rect.Top+5));

            }
            if(Formwork)for(int i=0;i<beams.Length;i++)if(Layout(i)!=null)DrawSupports(dc,Layout(i),i,ys[i],selection.Contains(Tuple.Create(FormworkObject.Beam,i)) || AllDimensions);
            if(Formwork && Model.NetworkResult!=null)DrawCrossSupports(dc);
            double bottomY=Pan.Y+SceneBounds.Bottom*Zoom+42;
            Dimension(dc,new Point(Screen(0,0).X,bottomY),new Point(Screen(Model.BeamLengthMm,0).X,bottomY),$"Beam length {Model.BeamLengthMm:0.###} mm",FormworkObject.Beam,0,"BeamLengthMm");
            if(dragging && Math.Abs(dragDelta)>2)Label(dc,$"Move {Math.Round(dragDelta/Zoom/25)*25:0} mm",mouseStart+new Vector(15,15),Brushes.DodgerBlue);
            if(selecting && (windowEnd-mouseStart).Length>4)
            {
                var colour=windowEnd.X<mouseStart.X ? Colors.SeaGreen : Colors.DodgerBlue;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25,colour.R,colour.G,colour.B)),new Pen(new SolidColorBrush(colour),1){DashStyle=windowEnd.X<mouseStart.X ? DashStyles.Dash : DashStyles.Solid},new Rect(mouseStart,windowEnd));
            }
            Label(dc,AddCrossing ? "Click a slab bay to place a crossing beam." : Formwork ? "Wheel: zoom · middle drag: pan · double-click: fit · click beam/H20: diagrams" : "Click: select · Shift-click: toggle · drag blank space: window/crossing selection · Delete: remove · Esc: clear",new Point(16,ActualHeight-25),Brushes.SlateGray);
        }
        private void DrawCrossSupports(DrawingContext dc)
        {
            foreach(var support in Model.NetworkResult.CrossSupports)
            {
                int encoded=-support.CrossingIndex-1;
                foreach(var solid in support.Solids)
                {
                    var rect=new Rect(Screen(solid.X0,solid.Y0),Screen(solid.X1,solid.Y1));
                    if(solid.IsLeg)
                    {
                        dc.DrawRectangle(Brushes.SlateGray,null,rect);DrawnLegCount++;
                        LegTargets.Add(Tuple.Create(rect,$"X{support.CrossingIndex+1} dedicated tower · peak leg {support.Towers.MaxLegKn:0.###}/{support.Towers.LegCapacityKn:0.###} kN"));continue;
                    }
                    int member=Array.IndexOf(support.Members,solid.Member);
                    bool active=memberBeam==encoded && memberRun==member;
                    dc.DrawRectangle(solid.Member==null ? Brushes.SandyBrown : Brushes.Goldenrod,new Pen(active ? Brushes.DodgerBlue : Brushes.SaddleBrown,1),rect);
                    if(member>=0) {DrawnCrossH20Count++;MemberTargets.Insert(0,Tuple.Create(rect,encoded,member,0));}
                }
                foreach(var bearing in support.Bearings)
                {
                    var p=Screen(bearing.Xmm,bearing.Ymm);dc.DrawEllipse(Brushes.DarkRed,null,p,3,3);
                    LegTargets.Add(Tuple.Create(new Rect(p.X-5,p.Y-5,10,10),$"X{support.CrossingIndex+1} bearing {bearing.Label??($"B{bearing.Beam+1}/T{bearing.Row+1}")} @ ({bearing.Xmm:0.###}, {bearing.Ymm:0.###}) mm · filler {bearing.FillerHeightMm:0.###} mm · reaction {bearing.ReactionKn:0.###} kN · filler strength/stability not verified"));
                    if(AllDimensions || SelectedKind==FormworkObject.Crossing && SelectedIndex==support.CrossingIndex)Label(dc,bearing.FillerHeightMm<=1e-7 ? "Direct bearing" : $"Filler {bearing.FillerHeightMm:0.###} mm",p+new Vector(5,5),Brushes.SaddleBrown);
                }
            }
        }
        private void DrawSupports(DrawingContext dc,TowerLayout layout,int beam,double centre,bool dims)
        {
            double a=centre-layout.WidthMm/2,b=centre+layout.WidthMm/2;
            var pen=new Pen(layout.Rotated ? Brushes.Firebrick : Brushes.SlateGray,1.2){DashStyle=layout.Rotated ? DashStyles.Dash : DashStyles.Solid};
            for(int i=0;i<layout.PositionsMm.Length;i++)
            {
                double x=layout.PositionsMm[i];dc.DrawLine(pen,Screen(x,a),Screen(x,b));
                for(int line=0;line<2;line++) {var p=Screen(x,line==0 ? a : b);dc.DrawEllipse(Brushes.SlateGray,null,p,3.5,3.5);DrawnLegCount++;double reaction=line==0 ? layout.LeftReactions[i] : layout.RightReactions[i];LegTargets.Add(Tuple.Create(new Rect(p.X-6,p.Y-6,12,12),$"B{beam+1} · leg {i+1} {(line==0 ? "L" : "R")} · {reaction:0.##}/{layout.LegCapacityKn:0.##} kN"+(layout.IgnoreUplift ? $" · minimum signed reaction {layout.MinimumLegReaction(line==0,i):0.######} kN · uplift excluded" : "")));}
                if(i>=layout.Joined.Length)continue;double end=layout.PositionsMm[i+1];Brush colour=layout.Joined[i] && !layout.Rotated ? Brushes.Firebrick : Brushes.SlateGray;
                if(layout.Joined[i])foreach(double y in new[] {a,b})dc.DrawLine(new Pen(colour,1.5){DashStyle=layout.Rotated ? DashStyles.Solid : DashStyles.Dash},Screen(x,y),Screen(end,y));

            }
            if(dims)for(int i=0;i<layout.Joined.Length;)
            {
                int count=1;double step=layout.PositionsMm[i+1]-layout.PositionsMm[i];
                while(i+count<layout.Joined.Length && layout.Joined[i+count]==layout.Joined[i] && Math.Abs(layout.PositionsMm[i+count+1]-layout.PositionsMm[i+count]-step)<.001)count++;
                Dimension(dc,Screen(layout.PositionsMm[i],a)+new Vector(0,-29),Screen(layout.PositionsMm[i+count],a)+new Vector(0,-29),$"{(count>1 ? count+"×" : "")}{step:0.###}",FormworkObject.Beam,beam,null,layout.Joined[i] ? Brushes.Firebrick : Brushes.SlateGray);
                i+=count;
            }
            for(int run=0;run<layout.StockPlan.Pieces.Length;run++)
            {
                var p=layout.StockPlan.Pieces[run];
                for(int line=0;line<2;line++)
                {
                    double y=line==0 ? a : b;bool active=memberBeam==beam && memberRun==run && memberLine==line;
                    foreach(double offset in new[] {-3.0,3.0})dc.DrawLine(new Pen(active ? Brushes.DodgerBlue : Brushes.Goldenrod,2),Screen(p.StartMm,y)+new Vector(0,offset),Screen(p.EndMm,y)+new Vector(0,offset));
                    var start=Screen(p.StartMm,y);MemberTargets.Add(Tuple.Create(new Rect(start.X,start.Y-8,p.StockLengthMm*Zoom,16),beam,run,line));
                }
                Label(dc,$"P{run+1} · {p.StockLengthMm:0}",Screen((p.StartMm+p.EndMm)/2,b)+new Vector(0,10),null,true);
                if(dims && (AllDimensions || memberBeam==beam && memberRun==run))
                {
                    double y=Screen(0,b).Y+37;
                    Dimension(dc,new Point(Screen(p.StartMm,0).X,y),new Point(Screen(layout.PositionsMm[p.FirstSupport],0).X,y),(layout.PositionsMm[p.FirstSupport]-p.StartMm).ToString("0.###"),FormworkObject.Beam,beam,null,null,run>0 ? -19 : 4);
                    Dimension(dc,new Point(Screen(layout.PositionsMm[p.LastSupport],0).X,y),new Point(Screen(p.EndMm,0).X,y),(p.EndMm-layout.PositionsMm[p.LastSupport]).ToString("0.###"),FormworkObject.Beam,beam,null);
                }
            }

        }
        internal bool SelectAt(Point point,bool toggle=false)
        {
            if(CanInteract!=null && !CanInteract())return false;
            if(AddCrossing)
            {
                var slab=ObjectTargets.FirstOrDefault(h=>h.Item2==FormworkObject.Slab && h.Item1.Contains(point));
                if(slab==null)return false;AddCrossing=false;CrossingRequested?.Invoke(slab.Item3,Math.Round(World(point).X/25)*25);InvalidateVisual();return true;
            }
            if(Formwork)foreach(var hit in MemberTargets)if(hit.Item1.Contains(point)) {SelectMember(hit.Item2,hit.Item3,hit.Item4);MemberSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4);return true;}
            foreach(var hit in dimensions)if(hit.Item1.Contains(point)){Select(hit.Item2,hit.Item3);DimensionSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4);return true;}
            foreach(var hit in ObjectTargets)if(hit.Item1.Contains(point))
            {
                if(Formwork && hit.Item2==FormworkObject.Beam && Layout(hit.Item3)!=null)
                {
                    var layout=Layout(hit.Item3);double x=World(point).X;
                    int run=Enumerable.Range(0,layout.StockPlan.Pieces.Length).OrderBy(i=>Math.Abs(x-(layout.StockPlan.Pieces[i].StartMm+layout.StockPlan.Pieces[i].EndMm)/2)).First();
                    int line=World(point).Y<Centres()[hit.Item3] ? 0 : 1;
                    SelectMember(hit.Item3,run,line);MemberSelected?.Invoke(hit.Item3,run,line);return true;
                }
                if(toggle)Toggle(hit.Item2,hit.Item3);else Select(hit.Item2,hit.Item3);return true;}return false;
        }
        protected override void OnMouseLeave(MouseEventArgs e)
        {base.OnMouseLeave(e);HoveredObject=null;InvalidateVisual();}
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {base.OnLostMouseCapture(e);panning=dragging=selecting=false;InvalidateVisual();}
        protected override void OnMouseWheel(MouseWheelEventArgs e) {ZoomAt(e.Delta>0 ? 1.2 : 1/1.2,e.GetPosition(this));e.Handled=true;}
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);Focus();mouseStart=e.GetPosition(this);
            if(e.ChangedButton==MouseButton.Middle) {panning=true;panStart=Pan;CaptureMouse();e.Handled=true;}
            else if(e.ChangedButton==MouseButton.Left)
            {
                if(e.ClickCount==2) {Fit();e.Handled=true;return;}
                bool toggle=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
                bool dimension=dimensions.Any(h=>h.Item1.Contains(mouseStart));
                if(SelectAt(mouseStart,toggle))
                {
                    if(!dimension)Focus();
                    dragging=!dimension && !toggle && !Formwork && selection.Count==1 && (SelectedKind==FormworkObject.Crossing || SelectedKind==FormworkObject.Beam && SelectedIndex>0);
                    dragDelta=0;if(dragging)CaptureMouse();e.Handled=true;
                }
                else if(!Formwork && !AddCrossing && (CanInteract==null || CanInteract()))
                {selecting=true;addWindow=toggle;windowEnd=mouseStart;HoveredObject=null;CaptureMouse();e.Handled=true;}
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);var p=e.GetPosition(this);
            if(panning) {Pan=panStart+(p-mouseStart);fitted=false;InvalidateVisual();}
            if(dragging) {dragDelta=SelectedKind==FormworkObject.Crossing ? p.X-mouseStart.X : p.Y-mouseStart.Y;InvalidateVisual();}
            if(selecting) {windowEnd=p;InvalidateVisual();}
            if(!panning && !dragging && !selecting)
            {
                HighlightAt(p);
                var tooltip=LegTargets.FirstOrDefault(h=>h.Item1.Contains(p))?.Item2;
                if(!Equals(ToolTip,tooltip))ToolTip=tooltip;
            }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);bool moved=dragging && Math.Abs(dragDelta)>4,window=selecting;
            var end=e.GetPosition(this);
            if(panning || dragging || selecting) {panning=dragging=selecting=false;ReleaseMouseCapture();}
            if(window) {if((end-mouseStart).Length>4)SelectWindow(mouseStart,end,addWindow);else if(!addWindow)ClearSelection();}
            if(moved)MoveSelection(dragDelta/Zoom);
            InvalidateVisual();
        }
    }
}


