using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal sealed partial class BeamFormworkCanvas
    {
        private bool singleRevitPart;
        private string revitPartPrefix,pressedRevitPart;
        private RevitImportContext combinedFrame;
        private readonly Dictionary<string,BeamFormworkCanvas> revitCanvases=new Dictionary<string,BeamFormworkCanvas>();
        internal event Action ActiveRevitPartChanged;
        internal readonly List<Tuple<string,Rect,FormworkObject,int>> CombinedObjectTargets=new List<Tuple<string,Rect,FormworkObject,int>>();
        internal int DrawnRevitPartCount {get;private set;}
        private RevitImportContext RevitFrame=>singleRevitPart ? combinedFrame : Model.HasCombinedRevitDrawing ? Model.RevitSceneModels()[0].Item2.RevitContext : null;
        private static Point ProjectRevit(Point point,RevitImportContext from,RevitImportContext to)
        {
            double wx=from.OriginXmm+point.X*from.DirectionX-point.Y*from.DirectionY,wy=from.OriginYmm+point.X*from.DirectionY+point.Y*from.DirectionX;
            return new Point((wx-to.OriginXmm)*to.DirectionX+(wy-to.OriginYmm)*to.DirectionY,-(wx-to.OriginXmm)*to.DirectionY+(wy-to.OriginYmm)*to.DirectionX);
        }
        private Point ProjectToCombined(Point point)=>RevitFrame==null ? point : ProjectRevit(point,Model.RevitContext,RevitFrame);
        private Point ProjectFromCombined(Point point)=>RevitFrame==null ? point : ProjectRevit(point,RevitFrame,Model.RevitContext);
        private Point CombinedScreen(Point point)=>new Point(Pan.X+point.X*Zoom,Pan.Y+point.Y*Zoom);
        private BeamFormworkCanvas PartCanvas(Tuple<RevitDrawingDefinition,BeamCalculatorViewModel> part)
        {
            if(!revitCanvases.TryGetValue(part.Item1.Key,out var child) || !ReferenceEquals(child.Model,part.Item2))
            {
                child=new BeamFormworkCanvas(part.Item2) {singleRevitPart=true,CanInteract=()=>!Model.IsOptimizing && (CanInteract==null || CanInteract())};
                string key=part.Item1.Key;var linked=child;
                child.SelectionChanged+=()=> {ActivatePart(key);SetSelection(linked.SelectedObjects,linked.SelectedKind,linked.SelectedIndex);};
                child.MemberSelected+=(beam,run,line)=> {ActivatePart(key);SelectMember(beam,run,line);MemberSelected?.Invoke(beam,run,line);};
                child.DimensionSelected+=(kind,index,field)=> {ActivatePart(key);LastDimensionBounds=linked.LastDimensionBounds;Select(kind,index);DimensionSelected?.Invoke(kind,index,field);};
                child.BeamOptionsRequested+=index=> {ActivatePart(key);BeamOptionsRequested?.Invoke(index);};
                child.CrossingRequested+=(bay,station)=> {ActivatePart(key);AddCrossing=false;CrossingRequested?.Invoke(bay,station);};
                revitCanvases[part.Item1.Key]=child;
            }
            child.combinedFrame=Model.RevitSceneModels()[0].Item2.RevitContext;
            int partNumber=Array.FindIndex(Model.RevitDrawings,d=>d.Key==part.Item1.Key);
            child.revitPartPrefix="D"+(partNumber+1);return child;
        }
        private void ActivatePart(string key)
        {
            if(key==Model.ActiveRevitDrawingKey)return;
            Model.ActivateRevitDrawing(key);memberBeam=-1;SelectedKind=FormworkObject.Beam;SelectedIndex=0;selection.Clear();SectionPositionMm=double.NaN;ActiveRevitPartChanged?.Invoke();InvalidateVisual();
        }
        private Rect CombinedBounds()
        {
            var result=Rect.Empty;
            foreach(var part in Model.RevitSceneModels())
            {
                var canvas=PartCanvas(part);var bounds=canvas.Bounds();
                foreach(var point in new[] {bounds.TopLeft,bounds.TopRight,bounds.BottomLeft,bounds.BottomRight})
                    result.Union(canvas.ProjectToCombined(point));
            }
            return result;
        }
        private void DrawCombinedRevit(DrawingContext dc)
        {
            DrawnRevitPartCount=0;CombinedObjectTargets.Clear();SceneBounds=CombinedBounds();
            var parts=Model.RevitSceneModels();var seen=new HashSet<string>();
            foreach(var part in parts)foreach(var reference in part.Item2.RevitContext.References)
            {
                if(!seen.Add(reference.SourceId))continue;
                var points=reference.Outline.Select(p=>CombinedScreen(ProjectRevit(new Point(p.X,p.Y),part.Item2.RevitContext,RevitFrame))).ToArray();
                var polygon=new StreamGeometry();using(var shape=polygon.Open()){shape.BeginFigure(points[0],true,true);shape.PolyLineTo(points.Skip(1).ToArray(),true,false);}
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(235,238,240)),new Pen(Brushes.SlateGray,1.2),polygon);DrawnReferenceCount++;
                Label(dc,reference.Label,new Point(points.Average(p=>p.X),points.Average(p=>p.Y)),Brushes.SlateGray,true);
            }
            foreach(var part in parts)
            {
                var child=PartCanvas(part);bool active=part.Item1.Key==Model.ActiveRevitDrawingKey;
                child.ImportPreviewMode=true;child.Formwork=Formwork && Model.IsCalculationValid;child.AllDimensions=AllDimensions && active;
                child.HideDimensions=HideDimensions || !active;child.ShowSecondaryH20s=ShowSecondaryH20s;
                child.Measure(new Size(ActualWidth,ActualHeight));child.Arrange(new Rect(0,0,ActualWidth,ActualHeight));child.Zoom=Zoom;child.Pan=Pan;
                child.selection.Clear();if(active)foreach(var item in selection)child.selection.Add(item);
                child.SelectedKind=SelectedKind;child.SelectedIndex=SelectedIndex;child.memberBeam=active ? memberBeam : -1;child.memberRun=memberRun;child.memberLine=memberLine;
                child.SectionPositionMm=active ? SectionPositionMm : double.NaN;
                child.OnRender(dc);DrawnRevitPartCount++;
                foreach(var hit in child.ObjectTargets){CombinedObjectTargets.Add(Tuple.Create(part.Item1.Key,hit.Item1,hit.Item2,hit.Item3));ObjectTargets.Add(hit);}
                MemberTargets.AddRange(child.MemberTargets);LegTargets.AddRange(child.LegTargets);SpacingTargets.AddRange(child.SpacingTargets);
                AnnotationLabels.AddRange(child.AnnotationLabels);AnnotationBounds.AddRange(child.AnnotationBounds);
                DrawnLegCount+=child.DrawnLegCount;DrawnCrossH20Count+=child.DrawnCrossH20Count;DrawnSecondaryH20Count+=child.DrawnSecondaryH20Count;
                DrawnBracedBayCount+=child.DrawnBracedBayCount;DrawnH20ProfileCount+=child.DrawnH20ProfileCount;
                if(active)sectionTarget=child.sectionTarget;
            }
            foreach(var annotation in annotations)dc.DrawDrawing(annotation);
        }
        private bool SelectCombinedAt(Point point,bool toggle)
        {
            foreach(var part in Model.RevitSceneModels())
            {
                var canvas=PartCanvas(part);
                if(!canvas.ObjectTargets.Any(h=>h.Item1.Contains(point)) && !canvas.MemberTargets.Any(h=>h.Item1.Contains(point)) && !canvas.dimensions.Any(h=>h.Item1.Contains(point)))continue;
                canvas.AddCrossing=AddCrossing;ActivatePart(part.Item1.Key);return canvas.SelectAt(point,toggle);
            }
            return false;
        }
    }
}
