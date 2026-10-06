using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Section camera; child coordinates remain unchanged for member picking.</summary>
    internal sealed class FormworkDrawingViewport : Decorator
    {
        internal double Scale {get;private set;}=1;
        internal Point Pan {get;private set;}
        private bool fitted=true,panning;
        private Point start,panStart;
        internal FormworkDrawingViewport(FrameworkElement child)
        {
            Child=child;ClipToBounds=true;Focusable=true;SizeChanged+=(s,e)=> {if(fitted)Fit();};
        }
        protected override Size MeasureOverride(Size constraint)
        {
            Child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            return new Size(double.IsInfinity(constraint.Width) ? Child.DesiredSize.Width : constraint.Width,double.IsInfinity(constraint.Height) ? Child.DesiredSize.Height : constraint.Height);
        }
        protected override Size ArrangeOverride(Size size) {Child.Arrange(new Rect(new Point(),Child.DesiredSize));if(fitted)Fit(size);return size;}
        internal void ResetView(){fitted=true;panning=false;if(IsMouseCaptured)ReleaseMouseCapture();Fit();}
        internal void Fit()=>Fit(new Size(ActualWidth,ActualHeight));
        private void Fit(Size size)
        {
            if(size.Width<1 || size.Height<1 || Child.DesiredSize.Width<1 || Child.DesiredSize.Height<1)return;
            Scale=Math.Min((size.Width-20)/Child.DesiredSize.Width,(size.Height-20)/Child.DesiredSize.Height);
            Pan=new Point((size.Width-Child.DesiredSize.Width*Scale)/2,(size.Height-Child.DesiredSize.Height*Scale)/2);fitted=true;Transform();
        }
        private void Transform(){Child.RenderTransform=new MatrixTransform(Scale,0,0,Scale,Pan.X,Pan.Y);InvalidateVisual();}
        internal void ZoomAt(double factor,Point anchor)
        {
            double next=Math.Max(.05,Math.Min(8,Scale*factor)),ratio=next/Scale;
            Pan=new Point(anchor.X-(anchor.X-Pan.X)*ratio,anchor.Y-(anchor.Y-Pan.Y)*ratio);Scale=next;fitted=false;Transform();
        }
        internal void PanBy(Vector delta){Pan+=delta;fitted=false;Transform();}
        protected override void OnRender(DrawingContext dc){dc.DrawRectangle(Brushes.White,null,new Rect(new Point(),RenderSize));}
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e){ZoomAt(e.Delta>0 ? 1.2 : 1/1.2,e.GetPosition(this));e.Handled=true;}
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if(e.ChangedButton==MouseButton.Middle){start=e.GetPosition(this);panStart=Pan;panning=true;CaptureMouse();e.Handled=true;}
            else if(e.ChangedButton==MouseButton.Left && e.ClickCount==2){Fit();e.Handled=true;}
        }
        protected override void OnMouseMove(MouseEventArgs e){if(panning){Pan=panStart+(e.GetPosition(this)-start);fitted=false;Transform();}}
        protected override void OnMouseUp(MouseButtonEventArgs e){if(panning){panning=false;ReleaseMouseCapture();e.Handled=true;}}
    }
}

