using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal enum FormworkObject { Beam, Slab, Crossing, Independent }
    /// <summary>Plan geometry uses one mm coordinate system for drawing, picking and edits.</summary>
    internal sealed class BeamFormworkCanvas : FrameworkElement
    {
        internal readonly BeamCalculatorViewModel Model;
        internal FormworkObject SelectedKind=FormworkObject.Beam;
        internal int SelectedIndex;
        internal bool Formwork, AllDimensions, AddCrossing, ShowSecondaryH20s;
        internal bool HideDimensions;
        internal double SectionPositionMm=double.NaN;
        internal event Action<double> SectionPositionChanged;
        private bool draggingSection;
        private Tuple<Rect,FormworkObject,int,string> pressedDimension;
        private Rect sectionTarget;
        private readonly List<Tuple<Rect,string>> crossingLabels=new List<Tuple<Rect,string>>();
        private readonly List<Rect> dimensionLines=new List<Rect>();
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
        internal readonly List<string> AnnotationLabels=new List<string>();
        private readonly List<DrawingGroup> annotations=new List<DrawingGroup>();
        internal readonly List<Tuple<Rect,string>> SpacingTargets=new List<Tuple<Rect,string>>();
        internal readonly List<Tuple<int,double>> AcrossDimensions=new List<Tuple<int,double>>();
        internal int DrawnSpacingDimensionCount {get;private set;}
        internal int DrawnH20ProfileCount {get;private set;}
        internal readonly List<Rect> AnnotationBounds=new List<Rect>();
        internal readonly List<Tuple<Rect,double>> HorizontalDimensionLabels=new List<Tuple<Rect,double>>();
        private readonly List<Tuple<Rect,FormworkObject,int,string>> dimensions=new List<Tuple<Rect,FormworkObject,int,string>>();
        internal event Action SelectionChanged;
        internal event Action<FormworkObject,int,string> DimensionSelected;
        internal Rect LastDimensionBounds;
        internal string DraftInputText;
        internal event Action<int,int,int> MemberSelected;
        internal event Action<int,double> CrossingRequested;
        internal event Action<int> BeamOptionsRequested;
        internal event Action<Point> GeometryContextRequested;
        internal event Action<FormworkObject,int,double> MoveRequested;
        internal Func<bool> CanInteract;
        internal string DraftCommand;
        internal Rect? DraftPreview;
        internal Point? DraftBasePoint,SnappedPoint;
        internal string SnapName;
        internal Point? DraftPointerWorld;
        internal event Action<Point> DraftPointRequested,DraftPointerChanged;
        internal bool IsDrafting=>DraftCommand!=null;
        internal void CancelDrafting()
        {DraftInputText=null;DraftCommand=null;DraftPreview=null;DraftBasePoint=null;SnappedPoint=null;SnapName=null;DraftPointerWorld=null;Cursor=Cursors.Arrow;InvalidateVisual();}
        internal Point SnapAt(Point screen)
        {
            var ys=Centres();var beams=Beams();var candidates=new List<Tuple<Point,string>>();
            for(int i=0;i<ys.Length;i++)
            {
                foreach(double x in new[] {0.0,Model.BeamLengthMm/2,Model.BeamLengthMm})candidates.Add(Tuple.Create(new Point(x,ys[i]),x==Model.BeamLengthMm/2 ? "Midpoint" : "Endpoint"));
                foreach(double x in new[] {0.0,Model.BeamLengthMm})foreach(double y in new[] {ys[i]-beams[i].WidthMm/2,ys[i]+beams[i].WidthMm/2})candidates.Add(Tuple.Create(new Point(x,y),"Corner"));
            }
            if(Model.IsNetworkMode)foreach(var c in Model.CrossingBeams)
            {
                int bay=c.BayNumber-1;if(bay<0 || bay>=ys.Length-1)continue;
                foreach(double y in new[] {ys[bay]+beams[bay].WidthMm/2,ys[bay+1]-beams[bay+1].WidthMm/2})candidates.Add(Tuple.Create(new Point(c.PositionMm,y),"Endpoint"));
                foreach(double y in new[] {ys[bay],ys[bay+1]})candidates.Add(Tuple.Create(new Point(c.PositionMm,y),"Intersection"));
                candidates.Add(Tuple.Create(new Point(c.PositionMm,(ys[bay]+beams[bay].WidthMm/2+ys[bay+1]-beams[bay+1].WidthMm/2)/2),"Midpoint"));
            }
            foreach(var beam in Model.IndependentBeams)
            {
                candidates.Add(Tuple.Create(new Point(beam.StartXmm,beam.StartYmm),"Endpoint"));
                candidates.Add(Tuple.Create(new Point(beam.EndXmm,beam.EndYmm),"Endpoint"));
                candidates.Add(Tuple.Create(new Point((beam.StartXmm+beam.EndXmm)/2,(beam.StartYmm+beam.EndYmm)/2),"Midpoint"));
            }
            var closest=candidates.OrderBy(c=>(Screen(c.Item1.X,c.Item1.Y)-screen).Length).FirstOrDefault();
            bool snapped=closest!=null && (Screen(closest.Item1.X,closest.Item1.Y)-screen).Length<=10;
            var world=World(screen);var point=snapped ? closest.Item1 : new Point(Math.Round(world.X/25)*25,Math.Round(world.Y/25)*25);
            SnappedPoint=point;SnapName=snapped ? closest.Item2 : "25 mm grid";return point;
        }
        internal void UpdateDraftPointer(Point screen)
        {if(!IsDrafting)return;DraftPointerWorld=SnapAt(screen);DraftPointerChanged?.Invoke(DraftPointerWorld.Value);InvalidateVisual();}
        private bool fitted=true;
        private Point mouseStart,panStart;
        private bool panning,dragging;
        private double dragDelta;
        private int memberBeam=-1,memberRun=-1,memberLine;
        internal int DrawnLegCount {get;private set;}
        internal int DrawnCrossH20Count {get;private set;}
        internal int DrawnSecondaryH20Count {get;private set;}
        internal int DrawnBracedBayCount {get;private set;}
        internal readonly List<Tuple<int,string,double>> CrossingSettingOut=new List<Tuple<int,string,double>>();
        internal BeamFormworkCanvas(BeamCalculatorViewModel model)
        {Model=model;Focusable=true;ClipToBounds=true;SizeChanged+=(s,e)=> {if(fitted || e.PreviousSize.Width==0)Fit();};}
        internal double[] Centres()=>Model.GeometryCentres();
        private TransferBeamRow[] Beams()=>Model.IsNetworkMode ? Model.NetworkBeams.ToArray() : new[] {new TransferBeamRow {WidthMm=Model.Width,DepthMm=Model.Depth}};
        private bool ValidGeometry()=>TowerBeamAnalysis.Finite(Model.BeamLengthMm) && Model.BeamLengthMm>0 && Model.BeamLengthMm<=50000
            && Beams().All(b=>TowerBeamAnalysis.Finite(b.WidthMm) && b.WidthMm>0 && b.WidthMm<=10000)
            && Model.IndependentBeams.All(b=>TowerBeamAnalysis.Finite(b.StartXmm) && TowerBeamAnalysis.Finite(b.StartYmm) && TowerBeamAnalysis.Finite(b.LengthMm) && b.LengthMm>0 && TowerBeamAnalysis.Finite(b.WidthMm) && b.WidthMm>0)
            && (!Model.IsNetworkMode || Model.NetworkBays.All(b=>TowerBeamAnalysis.Finite(b.WidthMm) && b.WidthMm>0 && b.WidthMm<=20000));
        private Rect Bounds()
        {
            var beams=Beams();var ys=Centres();double min=double.MaxValue,max=double.MinValue;
            for(int i=0;i<beams.Length;i++)
            {
                var result=Layout(i);double half=Math.Max(beams[i].WidthMm,result?.WidthMm??0)/2;
                min=Math.Min(min,ys[i]-half);max=Math.Max(max,ys[i]+half);
                if(result?.Transfer!=null) {min=Math.Min(min,ys[i]+result.Transfer.LowerStockStartMm);max=Math.Max(max,ys[i]+result.Transfer.LowerStockStartMm+result.Transfer.LowerStockLengthMm);}
            }
            double minX=0,maxX=Model.BeamLengthMm;
            if(Model.IsCalculationValid && Model.NetworkResult!=null)foreach(var solid in Model.NetworkResult.CrossSupports.SelectMany(c=>c.Solids))
            {minX=Math.Min(minX,solid.X0);maxX=Math.Max(maxX,solid.X1);min=Math.Min(min,solid.Y0+Model.PlanOriginYmm);max=Math.Max(max,solid.Y1+Model.PlanOriginYmm);}
            for(int i=0;i<Model.IndependentBeams.Count;i++)
            {
                var beam=Model.IndependentBeams[i];var layout=Model.IsCalculationValid ? Model.IndependentBeamResults.ElementAtOrDefault(i) : null;
                double half=Math.Max(beam.WidthMm,Math.Max(layout?.WidthMm??0,layout?.Transfer?.LowerStockLengthMm??0))/2;
                var bounds=beam.IsVertical ? new Rect(beam.StartXmm-half,beam.StartYmm,half*2,beam.LengthMm) : new Rect(beam.StartXmm,beam.StartYmm-half,beam.LengthMm,half*2);
                minX=Math.Min(minX,bounds.Left);maxX=Math.Max(maxX,bounds.Right);min=Math.Min(min,bounds.Top);max=Math.Max(max,bounds.Bottom);
            }
            return new Rect(minX,min,maxX-minX,max-min);
        }
        private TowerLayout Layout(int beam)=>!Model.IsCalculationValid ? null : Model.IsNetworkMode ? Model.NetworkResult?.Beams[beam] : Model.TowerResult;
        internal void Fit()
        {
            if(!ValidGeometry() || ActualWidth<100 || ActualHeight<100)return;
            SceneBounds=Bounds();Zoom=Math.Max(.001,Math.Min((ActualWidth-140)/SceneBounds.Width,(ActualHeight-160)/Math.Max(500,SceneBounds.Height)));
            Pan=new Point((ActualWidth-SceneBounds.Width*Zoom)/2-SceneBounds.Left*Zoom,70-SceneBounds.Top*Zoom);fitted=true;InvalidateVisual();
        }
        internal void RefitIfNeeded(){if(fitted)Fit();}
        internal void PreserveView()=>fitted=false;
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
            if(SelectedKind==FormworkObject.Crossing || SelectedKind==FormworkObject.Beam)
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
        private bool Label(DrawingContext dc,string text,Point at,Brush colour=null,bool centre=false,bool rotate=false)
        {
            var f=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,colour??Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            Rect box;
            if(rotate) {box=new Rect(at.X-f.Height/2-2,at.Y-f.Width/2-1,f.Height+4,f.Width+2);at=new Point(at.X-f.Height/2,at.Y+f.Width/2);}
            else {if(centre)at.X-=f.Width/2;box=new Rect(at.X-2,at.Y-1,f.Width+4,f.Height+2);}
            if(box.Left<0 || box.Right>ActualWidth || box.Top<0 || box.Bottom>ActualHeight || AnnotationBounds.Any(r=>r.IntersectsWith(box)))return false;
            AnnotationBounds.Add(box);AnnotationLabels.Add(text);
            // Draw masks last so later structure and dimension lines cannot cross the text.
            var drawing=new DrawingGroup();using(var overlay=drawing.Open()){overlay.DrawRectangle(Brushes.White,null,box);if(rotate)overlay.PushTransform(new RotateTransform(-90,at.X,at.Y));overlay.DrawText(f,at);if(rotate)overlay.Pop();}annotations.Add(drawing);return true;
        }
        private bool Dimension(DrawingContext dc,Point a,Point b,string text,FormworkObject kind,int index,string field,Brush colour=null,double labelOffset=-4,bool labelLeft=false)
        {
            if(HideDimensions)return false;
            dimensionLines.Add(new Rect(new Point(Math.Min(a.X,b.X)-2,Math.Min(a.Y,b.Y)-2),new Point(Math.Max(a.X,b.X)+2,Math.Max(a.Y,b.Y)+2)));
            colour=colour??Brushes.SlateGray;
            bool vertical=Math.Abs(a.X-b.X)<.1;
            var measured=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,colour,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            // Focused crossing setting-out can place short overhang labels outside their segments.
            // Collision checks still prevent text overlap at every zoom level.
            bool focusedCrossing=kind==FormworkObject.Crossing && selection.Contains(Tuple.Create(kind,index));
            if(field==null && !focusedCrossing && (vertical ? Math.Abs(b.Y-a.Y)<measured.Width+12 : Math.Abs(b.X-a.X)<measured.Width+12))return false;
            Point label=vertical ? new Point(a.X-measured.Height/2-7,(a.Y+b.Y)/2) : new Point((a.X+b.X)/2,(a.Y+b.Y)/2-measured.Height+labelOffset);
            var initial=label;
            bool placed=Label(dc,text,label,colour,!vertical,vertical);
            if(!placed && focusedCrossing)
            {
                // Preserve focused setting-out values: move crowded labels to a clear lane,
                // with a leader linking each displaced label back to its measured segment.
                double dx=measured.Width+16,dy=measured.Height+6;
                var lanes=vertical
                    ? new[] {new Vector(-dy,0),new Vector(0,-dx),new Vector(0,dx),new Vector(-dy,-dx),new Vector(-dy,dx)}
                    : new[] {new Vector(-dx,0),new Vector(dx,0),new Vector(0,-dy),new Vector(-dx,-dy),new Vector(dx,-dy),new Vector(0,-2*dy),new Vector(-dx,-2*dy),new Vector(dx,-2*dy)};
                foreach(var offset in lanes)
                {
                    label=initial+offset;
                    if(Label(dc,text,label,colour,!vertical,vertical)){placed=true;break;}
                }
                if(placed)dc.DrawLine(new Pen(colour,.7),new Point((a.X+b.X)/2,(a.Y+b.Y)/2),label+new Vector(0,measured.Height/2));
            }
            if(!placed)return false;
            dc.DrawLine(new Pen(colour,1),a,b);
            foreach(var point in new[] {a,b})dc.DrawLine(new Pen(colour,1),point+(vertical ? new Vector(-4,0) : new Vector(0,-4)),point+(vertical ? new Vector(4,0) : new Vector(0,4)));
            if(!vertical)HorizontalDimensionLabels.Add(Tuple.Create(AnnotationBounds.Last(),Math.Min(a.Y,b.Y)));
            if(field!=null)dimensions.Add(Tuple.Create(AnnotationBounds.Last(),kind,index,field));
            return true;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);crossingLabels.Clear();dimensionLines.Clear();sectionTarget=Rect.Empty;ObjectTargets.Clear();MemberTargets.Clear();LegTargets.Clear();SpacingTargets.Clear();dimensions.Clear();AnnotationLabels.Clear();AnnotationBounds.Clear();HorizontalDimensionLabels.Clear();annotations.Clear();AcrossDimensions.Clear();DrawnSpacingDimensionCount=DrawnH20ProfileCount=0;CrossingSettingOut.Clear();DrawnLegCount=DrawnCrossH20Count=DrawnBracedBayCount=0;
            DrawnSecondaryH20Count=0;
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
            if(!ValidGeometry()) {Label(dc,"Correct geometry to draw the plan.",new Point(30,30));foreach(var annotation in annotations)dc.DrawDrawing(annotation);return;}
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
                crossingLabels.Add(Tuple.Create(rect,$"X{i+1} · {c.WidthMm:0}×{c.DepthMm:0}"));
                if(!Formwork && (AllDimensions || selection.Count>0 && SelectedKind==FormworkObject.Crossing && SelectedIndex==i))
                    Dimension(dc,Screen(0,ys[bay]+beams[bay].WidthMm/2+200),Screen(c.PositionMm,ys[bay]+beams[bay].WidthMm/2+200),$"{c.PositionMm:0.###}",FormworkObject.Crossing,i,"PositionMm");
            }
            for(int i=0;i<beams.Length;i++)
            {
                var b=beams[i];var rect=new Rect(Screen(0,ys[i]-b.WidthMm/2),Screen(Model.BeamLengthMm,ys[i]+b.WidthMm/2));
                dc.DrawRectangle(Brushes.LightGray,ObjectPen(FormworkObject.Beam,i,rect,Brushes.SlateGray),rect);
                ObjectTargets.Insert(0,Tuple.Create(rect,FormworkObject.Beam,i));
                Label(dc,$"B{i+1} · {b.WidthMm:0}×{b.DepthMm:0}",new Point(rect.Left+8,rect.Top+5));

            }
            for(int i=0;i<Model.IndependentBeams.Count;i++)DrawIndependent(dc,i);
            if(Formwork)Label(dc,"Primary H20: orange · secondary H20: purple",new Point(16,16));
            if(Formwork)for(int i=0;i<beams.Length;i++)if(Layout(i)!=null)DrawSupports(dc,Layout(i),i,ys[i],selection.Contains(Tuple.Create(FormworkObject.Beam,i)) || AllDimensions || Zoom>=.08);
            if(Formwork && Model.NetworkResult!=null)DrawCrossSupports(dc);
            double bottomY=Pan.Y+SceneBounds.Bottom*Zoom+42;
            Dimension(dc,new Point(Screen(0,0).X,bottomY),new Point(Screen(Model.BeamLengthMm,0).X,bottomY),$"Beam length {Model.BeamLengthMm:0.###} mm",FormworkObject.Beam,0,"BeamLengthMm");
            if(IsDrafting)
            {
                if(DraftPreview.HasValue)
                {
                    var preview=DraftPreview.Value;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35,30,144,255)),new Pen(Brushes.DodgerBlue,1.5){DashStyle=DashStyles.Dash},new Rect(Screen(preview.Left,preview.Top),Screen(preview.Right,preview.Bottom)));
                }
                if(SnappedPoint.HasValue)
                {
                    var point=Screen(SnappedPoint.Value.X,SnappedPoint.Value.Y);
                    dc.DrawRectangle(Brushes.White,new Pen(Brushes.ForestGreen,1.5),new Rect(point.X-4,point.Y-4,8,8));
                    Label(dc,SnapName,point+new Vector(12,12),Brushes.ForestGreen);
                    if(DraftBasePoint.HasValue)dc.DrawLine(new Pen(Brushes.DodgerBlue,1){DashStyle=DashStyles.Dash},Screen(DraftBasePoint.Value.X,DraftBasePoint.Value.Y),point);
                }
            }
            if(IsDrafting && DraftInputText!=null && DraftPointerWorld.HasValue)Label(dc,DraftInputText,Screen(DraftPointerWorld.Value.X,DraftPointerWorld.Value.Y)+new Vector(14,-25),Brushes.DodgerBlue);
            if(dragging && Math.Abs(dragDelta)>2)Label(dc,$"Move {Math.Round(dragDelta/Zoom/25)*25:0} mm",mouseStart+new Vector(15,15),Brushes.DodgerBlue);
            if(selecting && (windowEnd-mouseStart).Length>4)
            {
                var colour=windowEnd.X<mouseStart.X ? Colors.SeaGreen : Colors.DodgerBlue;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25,colour.R,colour.G,colour.B)),new Pen(new SolidColorBrush(colour),1){DashStyle=windowEnd.X<mouseStart.X ? DashStyles.Dash : DashStyles.Solid},new Rect(mouseStart,windowEnd));
            }
            Label(dc,AddCrossing ? "Click a slab bay to place a crossing beam." : Formwork ? "Wheel: zoom · middle drag: pan · double-click: fit · click beam/H20: diagrams · Esc: clear" : "Click: select · Shift-click: toggle · drag blank space: window/crossing selection · Delete: remove · Esc: clear",new Point(16,ActualHeight-25),Brushes.SlateGray);
            foreach(var callout in crossingLabels)CrossingCallout(dc,callout.Item1,callout.Item2);
            if(Model.IsCalculationValid && TowerBeamAnalysis.Finite(SectionPositionMm))
            {
                double x=Screen(SectionPositionMm,0).X,top=Screen(0,SceneBounds.Top).Y-16,bottom=Screen(0,SceneBounds.Bottom).Y+16;
                dc.DrawLine(new Pen(Brushes.DarkSlateBlue,1.5){DashStyle=DashStyles.Dash},new Point(x,top),new Point(x,bottom));
                sectionTarget=new Rect(x-7,top,14,bottom-top);
                Label(dc,"A",new Point(x+7,top-16),Brushes.DarkSlateBlue);Label(dc,"A",new Point(x+7,bottom+2),Brushes.DarkSlateBlue);
            }
            foreach(var annotation in annotations)dc.DrawDrawing(annotation);
        }
        private void CrossingCallout(DrawingContext dc,Rect member,string text)
        {
            var ft=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var centre=new Point(member.Left+member.Width/2,member.Top+member.Height/2);
            var inside=new Rect(centre.X-ft.Width/2-2,centre.Y-8,ft.Width+4,ft.Height+2);
            if(member.Width>=ft.Width+12 && !dimensionLines.Any(r=>r.IntersectsWith(inside)) && Label(dc,text,new Point(centre.X,centre.Y-7),centre:true))return;
            for(int row=0;row<16;row++)foreach(int side in new[] {1,-1})foreach(int direction in new[] {1,-1})
            {
                double x=side>0 ? member.Right+14 : member.Left-ft.Width-14,y=centre.Y-7+direction*row*22;
                var box=new Rect(x-2,y-1,ft.Width+4,ft.Height+2);
                var anchor=new Point(side>0 ? member.Right : member.Left,centre.Y);
                var elbow=new Point(side>0 ? x-7 : x+ft.Width+7,centre.Y);var end=new Point(elbow.X,y+ft.Height/2);
                var lead1=new Rect(anchor,elbow);lead1.Inflate(1,1);var lead2=new Rect(elbow,end);lead2.Inflate(1,1);
                if(box.Left<2 || box.Right>ActualWidth-2 || box.Top<22 || box.Bottom>ActualHeight-30 || AnnotationBounds.Any(r=>r.IntersectsWith(box) || r.IntersectsWith(lead1) || r.IntersectsWith(lead2)) || dimensionLines.Any(r=>r.IntersectsWith(box) || r.IntersectsWith(lead1) || r.IntersectsWith(lead2)))continue;
                if(!Label(dc,text,new Point(x,y)))continue;
                var drawing=new DrawingGroup();using(var overlay=drawing.Open()){var pen=new Pen(Brushes.SlateGray,.8);overlay.DrawLine(pen,anchor,elbow);overlay.DrawLine(pen,elbow,end);overlay.DrawLine(pen,end,new Point(side>0 ? box.Left : box.Right,end.Y));}annotations.Insert(0,drawing);return;
            }
            // At distant zoom levels keep the short identifier visible when the full callout cannot fit.
            string shortName=text.Split('·')[0].Trim();
            var shortText=new FormattedText(shortName,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var shortBox=new Rect(centre.X-shortText.Width/2-2,centre.Y-8,shortText.Width+4,shortText.Height+2);
            if(!dimensionLines.Any(r=>r.IntersectsWith(shortBox)))Label(dc,shortName,new Point(centre.X,centre.Y-7),centre:true);
        }
        internal static int IndependentMemberId(int index)=>-1000-index;
        private Point IndependentScreen(IndependentBeamRow beam,double along,double across)=>beam.IsVertical ? Screen(beam.StartXmm-across,beam.StartYmm+along) : Screen(beam.StartXmm+along,beam.StartYmm+across);
        private void DrawIndependent(DrawingContext dc,int index)
        {
            var beam=Model.IndependentBeams[index];var rect=new Rect(IndependentScreen(beam,0,-beam.WidthMm/2),IndependentScreen(beam,beam.LengthMm,beam.WidthMm/2));
            dc.DrawRectangle(Brushes.LightGray,ObjectPen(FormworkObject.Independent,index,rect,Brushes.SlateGray),rect);
            ObjectTargets.Insert(0,Tuple.Create(rect,FormworkObject.Independent,index));
            Label(dc,$"I{index+1} · {beam.WidthMm:0}×{beam.DepthMm:0}",new Point(rect.Left+8,rect.Top+5));
            if(AllDimensions || selection.Contains(Tuple.Create(FormworkObject.Independent,index)))
                Dimension(dc,IndependentScreen(beam,0,beam.IsVertical ? beam.WidthMm/2 : -beam.WidthMm/2)+new Vector(beam.IsVertical ? -24 : 0,beam.IsVertical ? 0 : -24),IndependentScreen(beam,beam.LengthMm,beam.IsVertical ? beam.WidthMm/2 : -beam.WidthMm/2)+new Vector(beam.IsVertical ? -24 : 0,beam.IsVertical ? 0 : -24),$"Beam length {beam.LengthMm:0.###} mm",FormworkObject.Independent,index,"LengthMm");
            var layout=Model.IsCalculationValid ? Model.IndependentBeamResults.ElementAtOrDefault(index) : null;if(!Formwork || layout==null)return;
            int encoded=IndependentMemberId(index);
            DrawTowerFrames(dc,layout,$"I{index+1}",(along,across)=>IndependentScreen(beam,along,across));
            for(int run=0;run<layout.StockPlan.Pieces.Length;run++)for(int line=0;line<2;line++)
            {
                var piece=layout.StockPlan.Pieces[run];double across=(line==0 ? -1 : 1)*layout.WidthMm/2;
                var first=IndependentScreen(beam,piece.StartMm,across);var last=IndependentScreen(beam,piece.EndMm,across);
                PrimaryDrawing.Plan(dc,first,last,layout.Primary,memberBeam==encoded && memberRun==run && memberLine==line);
                if(!layout.Primary.IsWS10)DrawnH20ProfileCount+=2;
                MemberTargets.Add(Tuple.Create(MemberHit(first,last),encoded,run,line));
            }
            if(ShowSecondaryH20s && layout.Transfer!=null)for(int row=0;row<layout.Transfer.RowsMm.Length;row++)
            {
                var t=layout.Transfer;var first=IndependentScreen(beam,t.RowsMm[row],t.LowerStockStartMm);var last=IndependentScreen(beam,t.RowsMm[row],t.LowerStockStartMm+t.LowerStockLengthMm);
                H20Drawing.Plan(dc,first,last,1,memberBeam==encoded && memberRun==layout.StockPlan.Pieces.Length+row);DrawnSecondaryH20Count++;
                MemberTargets.Add(Tuple.Create(MemberHit(first,last),encoded,layout.StockPlan.Pieces.Length+row,0));
            }
            if(AllDimensions || selection.Contains(Tuple.Create(FormworkObject.Independent,index)))
            {
                double station=layout.PositionsMm.OrderBy(x=>Math.Abs(x-layout.LengthMm/2)).First();
                Dimension(dc,IndependentScreen(beam,station,-layout.WidthMm/2)+new Vector(beam.IsVertical ? 0 : -18,beam.IsVertical ? -18 : 0),IndependentScreen(beam,station,layout.WidthMm/2)+new Vector(beam.IsVertical ? 0 : -18,beam.IsVertical ? -18 : 0),layout.WidthMm.ToString("0.###"),FormworkObject.Independent,index,null,Brushes.Firebrick);
            }
        }
        private static Rect MemberHit(Point start,Point end)
        {var hit=new Rect(start,end);hit.Inflate(8,8);return hit;}
        private void DrawTowerFrames(DrawingContext dc,TowerLayout layout,string label,Func<double,double,Point> project)
        {
            double a=-layout.WidthMm/2,b=layout.WidthMm/2;
            for(int i=0;i<layout.PositionsMm.Length;i++)
            {
                double x=layout.PositionsMm[i];var frameA=project(x,a);var frameB=project(x,b);
                TowerPlanDrawing.Station(dc,frameA,frameB,layout.Rotated);
                if(i>=layout.Joined.Length || !layout.Joined[i])continue;
                double next=layout.PositionsMm[i+1];
                TowerPlanDrawing.BracedBay(dc,frameA,frameB,project(next,a),project(next,b),layout.Rotated);
                DrawnBracedBayCount++;
            }
            for(int i=0;i<layout.PositionsMm.Length;i++)for(int line=0;line<2;line++)
            {
                double x=layout.PositionsMm[i];var frame=project(x,b)-project(x,a);var point=project(x,line==0 ? a : b);
                TowerPlanDrawing.Leg(dc,point,(Math.Abs(frame.Y)>Math.Abs(frame.X)) != layout.Rotated);DrawnLegCount++;
                double reaction=line==0 ? layout.LeftReactions[i] : layout.RightReactions[i];
                LegTargets.Add(Tuple.Create(new Rect(point.X-6,point.Y-6,12,12),$"{label} · leg {i+1} {(line==0 ? "L" : "R")} · {reaction:0.##}/{layout.LegCapacityKn:0.##} kN"+(layout.IgnoreUplift ? $" · minimum signed reaction {layout.MinimumLegReaction(line==0,i):0.######} kN · uplift excluded" : "")));
            }
        }
        private Point SolverScreen(double x,double y)=>Screen(x,y+Model.PlanOriginYmm);
        private void DrawCrossSupports(DrawingContext dc)
        {
            foreach(var support in Model.NetworkResult.CrossSupports)
            {
                int encoded=-support.CrossingIndex-1;
                var timber=support.Solids.Where(s=>s.Member!=null).GroupBy(s=>s.Member).ToArray();
                Action<IGrouping<TransferMember,FormworkSolid>> draw=group=>
                {
                    var m=group.Key;int member=Array.IndexOf(support.Members,m);
                    double x0=group.Min(s=>s.X0),x1=group.Max(s=>s.X1),y0=group.Min(s=>s.Y0),y1=group.Max(s=>s.Y1);
                    bool alongY=y1-y0>x1-x0;
                    var first=alongY ? SolverScreen((x0+x1)/2,y0) : SolverScreen(x0,(y0+y1)/2);
                    var last=alongY ? SolverScreen((x0+x1)/2,y1) : SolverScreen(x1,(y0+y1)/2);
                    if(m.IsPrimary)PrimaryDrawing.Plan(dc,first,last,m.Primary,memberBeam==encoded && memberRun==member);
                    else H20Drawing.Plan(dc,first,last,1,memberBeam==encoded && memberRun==member);
                    DrawnCrossH20Count+=m.H20Quantity;
                    if(m.H20Count==1)DrawnSecondaryH20Count++;
                    var target=Tuple.Create(MemberHit(first,last),encoded,member,0);
                    MemberTargets.Add(target);
                    if(m.H20Count==2 && m.Name.EndsWith(" R"))Label(dc,m.StockLabel,new Point(first.X+10,(first.Y+last.Y)/2));
                };
                if(support.Towers!=null)
                {
                    var primary=timber.Where(g=>g.Key.H20Count==2).SelectMany(g=>g).ToArray();var first=primary[0];
                    double origin=first.Y0-first.Member.StartMm,centre=(primary.Min(s=>s.X0)+primary.Max(s=>s.X1))/2;
                    DrawTowerFrames(dc,support.Towers,$"X{support.CrossingIndex+1}",(station,side)=>SolverScreen(centre+side,origin+station));
                }
                foreach(var group in timber.Where(g=>g.Key.H20Count==2 || ShowSecondaryH20s))draw(group);
                if(support.Towers!=null && (AllDimensions || selection.Count>0 && SelectedKind==FormworkObject.Crossing && SelectedIndex==support.CrossingIndex))DrawCrossDimensions(dc,support,timber.SelectMany(g=>g).Where(s=>s.Member.H20Count==2).ToArray());
                foreach(var solid in support.Solids.Where(s=>!s.IsLeg && !s.IsTowerEnvelope && s.Member==null))dc.DrawRectangle(solid.IsSideShutter ? Brushes.LightSteelBlue : Brushes.SandyBrown,new Pen(solid.IsSideShutter ? Brushes.SteelBlue : Brushes.SaddleBrown,1),new Rect(SolverScreen(solid.X0,solid.Y0),SolverScreen(solid.X1,solid.Y1)));
                if(support.HasReducedShutterClearance)
                {
                    // Keep the exception visible even when general dimensions are hidden.
                    var at=SolverScreen(support.Input.PositionMm+support.Input.WidthMm/2,Model.NetworkResult.BeamCentresMm[support.Input.BayNumber-1]+Model.NetworkResult.Input.Beams[support.Input.BayNumber-1].WidthMm/2);
                    string text=$"X{support.CrossingIndex+1} shutter allowance: {support.NearShutterClearanceMm:0}/{support.FarShutterClearanceMm:0} mm (exception)";
                    for(int row=0;row<12;row++)if(Label(dc,text,at+new Vector(8,row*20),Brushes.DarkRed))break;
                }
                foreach(var bearing in support.Bearings)
                {
                    var point=SolverScreen(bearing.Xmm,bearing.Ymm);
                    string description=bearing.IsConcreteEndZone ? $"X{support.CrossingIndex+1} concrete end zone on B{bearing.Beam+1}/T{bearing.Row+1}"+(bearing.IsShutterEndZone ? $" via side shutter · height {bearing.FillerHeightMm:0.###} mm" : "")+$" · load {bearing.ReactionKn:0.###} kN included in H20, primary and leg checks"
                        : $"X{support.CrossingIndex+1} bearing {bearing.Label??($"B{bearing.Beam+1}/T{bearing.Row+1}")} · filler {bearing.FillerHeightMm:0.###} mm · reaction {bearing.ReactionKn:0.###} kN";
                    LegTargets.Add(Tuple.Create(new Rect(point.X-5,point.Y-5,10,10),description));
                    if(bearing.IsConcreteEndZone && bearing==support.Bearings.First(b=>b.IsConcreteEndZone && b.Beam==bearing.Beam))
                    {
                        var zone=new Rect(SolverScreen(bearing.FootprintStartMm,bearing.EndZoneStartMm),SolverScreen(bearing.FootprintEndMm,bearing.EndZoneEndMm));
                        dc.DrawRectangle(null,new Pen(Brushes.DarkRed,1) {DashStyle=DashStyles.Dash},zone);
                        double width=bearing.EndZoneEndMm-bearing.EndZoneStartMm;
                        bool reduced=bearing.IsShutterEndZone && width<200;
                        if(AllDimensions || reduced || SelectedKind==FormworkObject.Crossing && SelectedIndex==support.CrossingIndex)Label(dc,$"End zone on B{bearing.Beam+1}: {width:0.###} mm"+(bearing.IsShutterEndZone ? " · side shutter" : "")+(reduced ? " (exception)" : ""),new Point(zone.Right+5,zone.Top+5),Brushes.DarkRed);
                    }
                    else if(!bearing.IsConcreteEndZone && bearing.FillerHeightMm>1e-7 && (AllDimensions || SelectedKind==FormworkObject.Crossing && SelectedIndex==support.CrossingIndex))Label(dc,$"Filler {bearing.FillerHeightMm:0.###} mm",point+new Vector(5,5),Brushes.SaddleBrown);
                }
            }
        }
        private void DrawCrossDimensions(DrawingContext dc,CrossBeamSupportResult support,FormworkSolid[] primary)
        {
            int index=support.CrossingIndex,bay=support.Input.BayNumber-1;var result=Model.NetworkResult;var layout=support.Towers;
            double origin=primary[0].Y0-primary[0].Member.StartMm,centre=(primary.Min(s=>s.X0)+primary.Max(s=>s.X1))/2;
            double left=centre-layout.WidthMm/2,right=centre+layout.WidthMm/2;
            var legs=layout.PositionsMm.Select(x=>origin+x).ToArray();
            double near=result.BeamCentresMm[bay]+result.Input.Beams[bay].WidthMm/2,far=result.BeamCentresMm[bay+1]-result.Input.Beams[bay+1].WidthMm/2;
            Action<double,double,string,double> vertical=(a,b,role,offset)=>
            {
                double x=SolverScreen(left,0).X-offset;double distance=b-a;
                CrossingSettingOut.Add(Tuple.Create(index,role,distance));
                foreach(double level in new[] {a,b})dc.DrawLine(new Pen(Brushes.LightSlateGray,.5),SolverScreen(left,level),new Point(x-4,SolverScreen(0,level).Y));
                Dimension(dc,new Point(x,SolverScreen(0,a).Y),new Point(x,SolverScreen(0,b).Y),distance.ToString("0.###"),FormworkObject.Crossing,index,null);
                var hit=new Rect(new Point(x,SolverScreen(0,a).Y),new Point(x,SolverScreen(0,b).Y));hit.Inflate(8,8);LegTargets.Add(Tuple.Create(hit,$"X{index+1} · {role}: {distance:0.###} mm"));
            };
            vertical(near,legs[0],$"B{bay+1} concrete face to first tower leg",96);
            for(int i=0;i<legs.Length-1;i++)vertical(legs[i],legs[i+1],layout.Joined[i] ? "Braced tower bay" : "Gap between tower groups",96);
            vertical(legs.Last(),far,$"Last tower leg to B{bay+2} concrete face",96);
            // Keep the transverse setting-out chain above the primary stock labels
            // and the separate vertical H20 overhang dimensions.
            double y=SolverScreen(0,legs[0]).Y-58;
            CrossingSettingOut.Add(Tuple.Create(index,"Beam start to near leg column",left));
            CrossingSettingOut.Add(Tuple.Create(index,"Tower width between leg centres",layout.WidthMm));
            CrossingSettingOut.Add(Tuple.Create(index,"Far leg column to beam end",Model.BeamLengthMm-right));
            foreach(double x in new[] {0.0,left,right,Model.BeamLengthMm})
                dc.DrawLine(new Pen(Brushes.LightSlateGray,.5),SolverScreen(x,legs[0]),new Point(SolverScreen(x,0).X,y-4));
            Dimension(dc,new Point(SolverScreen(0,0).X,y),new Point(SolverScreen(left,0).X,y),left.ToString("0.###"),FormworkObject.Crossing,index,null,null,-18);
            Dimension(dc,new Point(SolverScreen(left,0).X,y),new Point(SolverScreen(right,0).X,y),layout.WidthMm.ToString("0.###"),FormworkObject.Crossing,index,null,Brushes.DarkCyan,-18);
            Dimension(dc,new Point(SolverScreen(right,0).X,y),new Point(SolverScreen(Model.BeamLengthMm,0).X,y),(Model.BeamLengthMm-right).ToString("0.###"),FormworkObject.Crossing,index,null,null,-18);
            foreach(var piece in layout.StockPlan.Pieces)
            {
                vertical(origin+piece.StartMm,legs[piece.FirstSupport],$"P{Array.IndexOf(layout.StockPlan.Pieces,piece)+1} near {layout.Primary.Name} overhang",36);
                vertical(legs[piece.LastSupport],origin+piece.EndMm,$"P{Array.IndexOf(layout.StockPlan.Pieces,piece)+1} far {layout.Primary.Name} overhang",36);
            }
        }
        private void DrawSupports(DrawingContext dc,TowerLayout layout,int beam,double centre,bool dims)
        {
            double a=centre+layout.TransverseOffsetMm-layout.WidthMm/2,b=centre+layout.TransverseOffsetMm+layout.WidthMm/2;
            var centres=Centres();
            double rowSpace=centres.Length==1 ? ActualHeight : Enumerable.Range(0,centres.Length).Where(i=>i!=beam).Min(i=>Math.Abs(centres[i]-centre)*Zoom);
            bool detail=rowSpace>=120;
            if(dims)foreach(double station in layout.PositionsMm.OrderBy(x=>Math.Abs(x-layout.LengthMm/2)))
            {
                if(!Dimension(dc,Screen(station,a)+new Vector(-18,0),Screen(station,b)+new Vector(-18,0),layout.WidthMm.ToString("0.###"),FormworkObject.Beam,beam,null,Brushes.Firebrick,4,true))continue;
                AcrossDimensions.Add(Tuple.Create(beam,layout.WidthMm));break;
            }
            for(int i=0;i<layout.Joined.Length;i++)SpacingTargets.Add(Tuple.Create(new Rect(Screen(layout.PositionsMm[i],a),Screen(layout.PositionsMm[i+1],b)),
                $"B{beam+1} · {(layout.Joined[i] ? "Braced bay" : "Gap")} {layout.PositionsMm[i+1]-layout.PositionsMm[i]:0.###} mm · across {layout.WidthMm:0.###} mm"));
            if(dims && detail)for(int i=0;i<layout.Joined.Length;)
            {
                int count=1;double step=layout.PositionsMm[i+1]-layout.PositionsMm[i];
                while(i+count<layout.Joined.Length && layout.Joined[i+count]==layout.Joined[i] && Math.Abs(layout.PositionsMm[i+count+1]-layout.PositionsMm[i+count]-step)<.001)count++;
                if(Dimension(dc,Screen(layout.PositionsMm[i],a)+new Vector(0,-29),Screen(layout.PositionsMm[i+count],a)+new Vector(0,-29),$"{(count>1 ? count+"×" : "")}{step:0.###}",FormworkObject.Beam,beam,null,layout.Joined[i] ? Brushes.Firebrick : Brushes.SlateGray))DrawnSpacingDimensionCount++;
                i+=count;
            }
            for(int run=0;run<layout.StockPlan.Pieces.Length;run++)
            {
                var p=layout.StockPlan.Pieces[run];
                for(int line=0;line<2;line++)
                {
                    double y=line==0 ? a : b;bool active=memberBeam==beam && memberRun==run && memberLine==line;
                    var first=Screen(p.StartMm,y);var last=Screen(p.EndMm,y);PrimaryDrawing.Plan(dc,first,last,layout.Primary,active,(Math.Max(3,80*Zoom)+Math.Max(1,20*Zoom))/2,Math.Max(3,80*Zoom));if(!layout.Primary.IsWS10)DrawnH20ProfileCount+=2;
                    var hit=MemberHit(first,last);hit.Inflate(0,Math.Max(0,90*Zoom-5));MemberTargets.Add(Tuple.Create(hit,beam,run,line));
                }
                if(detail)Label(dc,$"{layout.Primary.Name} · {p.StockLengthMm:0.###}",Screen((p.StartMm+p.EndMm)/2,b)+new Vector(0,10),null,true);
                if(dims && detail && (AllDimensions || memberBeam==beam && memberRun==run))
                {
                    double y=Screen(0,b).Y+37;
                    Dimension(dc,new Point(Screen(p.StartMm,0).X,y),new Point(Screen(layout.PositionsMm[p.FirstSupport],0).X,y),(layout.PositionsMm[p.FirstSupport]-p.StartMm).ToString("0.###"),FormworkObject.Beam,beam,null,null);
                    Dimension(dc,new Point(Screen(layout.PositionsMm[p.LastSupport],0).X,y),new Point(Screen(p.EndMm,0).X,y),(p.EndMm-layout.PositionsMm[p.LastSupport]).ToString("0.###"),FormworkObject.Beam,beam,null);
                }
            }
            DrawTowerFrames(dc,layout,$"B{beam+1}",(station,side)=>Screen(station,centre+layout.TransverseOffsetMm+side));
            if(ShowSecondaryH20s && layout.Transfer!=null)
            {
                var t=layout.Transfer;int runs=layout.StockPlan.Pieces.Length;
                for(int physical=0;physical<t.PhysicalRowCount;physical++)
                {
                    int row=t.PhysicalRowDesignIndex(physical);double station=t.PhysicalRowPosition(physical);
                    var m=t.LowerCases[0][row];int encoded=runs+row;
                    var first=Screen(station,centre+m.StartMm);var last=Screen(station,centre+m.StartMm+m.LengthMm);
                    H20Drawing.Plan(dc,first,last,1,memberBeam==beam && memberRun==encoded);
                    MemberTargets.Insert(0,Tuple.Create(MemberHit(first,last),beam,encoded,0));DrawnSecondaryH20Count++;
                    for(int side=0;side<2;side++)
                    {
                        if(Model.IsNetworkMode && side==0)continue; // Each shared slab H20 appears once.
                        var upper=t.UpperMembers.FirstOrDefault(u=>u.Name==(side==0 ? "Upper left" : "Upper right"));
                        if(upper==null || t.UpperScale(side==0,row)<=0)continue;
                        double beamWidth=Model.IsNetworkMode ? Model.NetworkResult.Input.Beams[beam].WidthMm : Model.Width;
                        double face=centre+(side==0 ? -1 : 1)*beamWidth/2;
                        double start=side==0 ? face-upper.StartMm-upper.LengthMm : face+upper.StartMm;
                        first=Screen(t.RowsMm[row],start);last=Screen(t.RowsMm[row],start+upper.LengthMm);
                        encoded=runs+t.RowsMm.Length+row*2+side;
                        H20Drawing.Plan(dc,first,last,1,memberBeam==beam && memberRun==encoded);
                        MemberTargets.Insert(0,Tuple.Create(MemberHit(first,last),beam,encoded,0));DrawnSecondaryH20Count++;
                    }
                }
            }
        }
        internal bool SelectAt(Point point,bool toggle=false)
        {
            if(CanInteract!=null && !CanInteract())return false;
            if(IsDrafting){DraftPointRequested?.Invoke(SnapAt(point));InvalidateVisual();return true;}
            if(AddCrossing)
            {
                var slab=ObjectTargets.FirstOrDefault(h=>h.Item2==FormworkObject.Slab && h.Item1.Contains(point));
                if(slab==null)return false;AddCrossing=false;CrossingRequested?.Invoke(slab.Item3,Math.Round(World(point).X/25)*25);InvalidateVisual();return true;
            }
            if(Formwork)foreach(var hit in MemberTargets)if(hit.Item1.Contains(point)) {SelectMember(hit.Item2,hit.Item3,hit.Item4);MemberSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4);return true;}
            foreach(var hit in dimensions)if(hit.Item1.Contains(point)){LastDimensionBounds=hit.Item1;Select(hit.Item2,hit.Item3);DimensionSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4);return true;}
            foreach(var hit in ObjectTargets)if(hit.Item1.Contains(point))
            {
                if(Formwork && hit.Item2==FormworkObject.Independent && Model.IndependentBeamResults.ElementAtOrDefault(hit.Item3)!=null)
                {Select(FormworkObject.Independent,hit.Item3);MemberSelected?.Invoke(IndependentMemberId(hit.Item3),0,0);return true;}
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
        internal bool OpenBeamOptionsAt(Point point)
        {
            if(!Formwork || !Model.IsCalculationValid || Model.NetworkResult==null || Model.IsOptimizing || CanInteract!=null && !CanInteract())return false;
            var member=MemberTargets.FirstOrDefault(h=>h.Item2>=0 && h.Item1.Contains(point));
            var beam=ObjectTargets.FirstOrDefault(h=>h.Item2==FormworkObject.Beam && h.Item1.Contains(point));
            int index=member?.Item2??beam?.Item3??-1;if(index<0)return false;
            Select(FormworkObject.Beam,index);Model.SelectNetworkBeam(index);BeamOptionsRequested?.Invoke(index);return true;
        }
        internal bool OpenGeometryContextAt(Point point)
        {
            if(Formwork || Model.IsOptimizing || CanInteract!=null && !CanInteract())return false;
            CancelDrafting();CancelSelectionGesture();AddCrossing=false;Cursor=Cursors.Arrow;
            var hit=ObjectTargets.FirstOrDefault(h=>h.Item1.Contains(point));
            if(hit!=null)Select(hit.Item2,hit.Item3);
            GeometryContextRequested?.Invoke(point);return true;
        }
        internal Tuple<int,double> BayAt(Point point)
        {
            var hit=ObjectTargets.FirstOrDefault(h=>h.Item2==FormworkObject.Slab && h.Item1.Contains(point));
            return hit==null ? null : Tuple.Create(hit.Item3,Math.Round(World(point).X/25)*25);
        }
        internal bool BeginDimensionPress(Point point)
        {
            pressedDimension=dimensions.FirstOrDefault(h=>h.Item1.Contains(point));
            if(pressedDimension==null)return false;
            CaptureMouse();return true;
        }
        internal bool EndDimensionPress(Point point)
        {
            if(pressedDimension==null)return false;
            var hit=pressedDimension;pressedDimension=null;ReleaseMouseCapture();
            if(hit.Item1.Contains(point))
            {
                LastDimensionBounds=hit.Item1;Select(hit.Item2,hit.Item3);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>DimensionSelected?.Invoke(hit.Item2,hit.Item3,hit.Item4)));
            }
            return true;
        }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);Focus();mouseStart=e.GetPosition(this);
            if(e.ChangedButton==MouseButton.Left && !Model.IsOptimizing && !IsDrafting && !AddCrossing && !sectionTarget.IsEmpty && sectionTarget.Contains(mouseStart))
            {draggingSection=true;CaptureMouse();e.Handled=true;return;}
            if(e.ChangedButton==MouseButton.Right) {e.Handled=Formwork ? OpenBeamOptionsAt(mouseStart) : OpenGeometryContextAt(mouseStart);}
            else if(e.ChangedButton==MouseButton.Middle) {panning=true;panStart=Pan;CaptureMouse();e.Handled=true;}
            else if(e.ChangedButton==MouseButton.Left)
            {
                if(IsDrafting){SelectAt(mouseStart);e.Handled=true;return;}
                if(e.ClickCount==2) {Fit();e.Handled=true;return;}
                bool toggle=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
                bool dimension=BeginDimensionPress(mouseStart);
                if(dimension){e.Handled=true;return;}
                if(SelectAt(mouseStart,toggle))
                {
                    if(!dimension)Focus();
                    dragging=!dimension && !toggle && !Formwork && selection.Count==1 && (SelectedKind==FormworkObject.Crossing || SelectedKind==FormworkObject.Beam);
                    dragDelta=0;if(dragging)CaptureMouse();e.Handled=true;
                }
                else if(!Formwork && !AddCrossing && (CanInteract==null || CanInteract()))
                {selecting=true;addWindow=toggle;windowEnd=mouseStart;HoveredObject=null;CaptureMouse();e.Handled=true;}
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);var p=e.GetPosition(this);
            if(draggingSection){SectionPositionMm=Math.Max(0,Math.Min(Model.BeamLengthMm,World(p).X));SectionPositionChanged?.Invoke(SectionPositionMm);InvalidateVisual();return;}
            if(panning) {Pan=panStart+(p-mouseStart);fitted=false;if(IsDrafting)UpdateDraftPointer(p);else InvalidateVisual();return;}
            if(IsDrafting){UpdateDraftPointer(p);return;}
            if(dragging) {dragDelta=SelectedKind==FormworkObject.Crossing ? p.X-mouseStart.X : p.Y-mouseStart.Y;InvalidateVisual();}
            if(selecting) {windowEnd=p;InvalidateVisual();}
            if(!panning && !dragging && !selecting)
            {
                HighlightAt(p);
                var tooltip=LegTargets.FirstOrDefault(h=>h.Item1.Contains(p))?.Item2??SpacingTargets.FirstOrDefault(h=>h.Item1.Contains(p))?.Item2;
                if(!Equals(ToolTip,tooltip))ToolTip=tooltip;
            }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if(draggingSection){draggingSection=false;ReleaseMouseCapture();e.Handled=true;return;}
            base.OnMouseUp(e);
            if(e.ChangedButton==MouseButton.Left && EndDimensionPress(e.GetPosition(this))){e.Handled=true;return;}
            bool moved=dragging && Math.Abs(dragDelta)>4,window=selecting;
            var end=e.GetPosition(this);
            if(panning || dragging || selecting) {panning=dragging=selecting=false;ReleaseMouseCapture();}
            if(window) {if((end-mouseStart).Length>4)SelectWindow(mouseStart,end,addWindow);else if(!addWindow)ClearSelection();}
            if(moved)MoveSelection(dragDelta/Zoom);
            InvalidateVisual();
        }
    }
}


