using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal sealed class EquivalentLoadingView : Grid
    {
        internal readonly EquivalentLoadingPlot Plot;
        internal readonly Border DetailsPanel;
        internal readonly CalculationCard LoadCard=new CalculationCard(),ReactionCard=new CalculationCard();
        private CalculationCard active;
        internal Button CloseDetailsButton=>active==ReactionCard ? ReactionCard.CloseButton : LoadCard.CloseButton;
        internal bool IsDetailsOpen=>LoadCard.IsOpen || ReactionCard.IsOpen;
        internal string[] VisibleCalculation=>active?.Text??new string[0];
        internal readonly EquivalentTransferredLoading model;
        internal EquivalentLoadingView(EquivalentTransferredLoading model,bool externalEnds)
        {
            this.model=model;Background=Brushes.White;
            for(int i=0;i<4;i++)RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            ColumnDefinitions.Add(new ColumnDefinition());ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var header=new TextBlock {Text=$"Reference spacing {model.ReferenceSpacingMm:0.###} mm"+(model.HasPositionedLoads ? " · physical load positions" : model.IsDesignSlice ? " · transfer at primary bearings" : ""),FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6),ToolTip=model.HasPositionedLoads ? "Each source is converted to the beam reference spacing at its physical transverse position. The calculated bearing reactions determine the primary UDL. Actual row loads remain in the member checks below." : model.IsDesignSlice ? "Representative primary-transfer slice. Beam load is shared equally; assigned slab and crossing loads act at the receiving primary bearings. Actual physical secondary response is shown below." : "Loads converted from slab spacing to the beam reference spacing."};
            SetColumnSpan(header,2);Children.Add(header);
            var cards=new StackPanel();cards.Children.Add(LoadCard);cards.Children.Add(ReactionCard);
            DetailsPanel=new Border {Width=290,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed,Child=cards};
            SetColumn(DetailsPanel,1);SetRow(DetailsPanel,1);Children.Add(DetailsPanel);SizeChanged+=(s,e)=>LayoutDetails(ActualWidth);
            LoadCard.CloseButton.Click+=(s,e)=> {LoadCard.Visibility=Visibility.Collapsed;Plot?.ClearLoadSelection();UpdateCards();};
            ReactionCard.CloseButton.Click+=(s,e)=> {ReactionCard.Visibility=Visibility.Collapsed;Plot?.ClearReactionSelection();UpdateCards();};
            if(!model.IsAvailable)
            {
                var body=new TextBlock {Text=model.UnavailableReason,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)};SetRow(body,1);Children.Add(body);return;
            }
            Plot=new EquivalentLoadingPlot(model);SetRow(Plot,1);Children.Add(Plot);Plot.Selected+=(component,reaction)=>ShowCalculation(component,reaction);
            if(externalEnds){var status=new TextBlock {Text="End supports unchecked",FontSize=12,Foreground=Brushes.DarkGoldenrod,ToolTip="External column-side end supports are assumed; capacity requires separate verification."};SetRow(status,3);SetColumnSpan(status,2);Children.Add(status);}
        }
        internal void CloseAllDetails(){LoadCard.Visibility=ReactionCard.Visibility=Visibility.Collapsed;Plot?.ClearSelection();UpdateCards();}
        private void UpdateCards(){DetailsPanel.Visibility=IsDetailsOpen ? Visibility.Visible : Visibility.Collapsed;LayoutDetails(ActualWidth);}
        protected override Size MeasureOverride(Size constraint){LayoutDetails(double.IsInfinity(constraint.Width) ? ActualWidth : constraint.Width);return base.MeasureOverride(constraint);}
        private void LayoutDetails(double width)
        {
            bool narrow=width>0 && width<800;DetailsPanel.Width=narrow ? double.NaN : 290;DetailsPanel.Margin=narrow ? new Thickness(0,10,0,0) : new Thickness(12,0,0,0);
            DetailsPanel.HorizontalAlignment=narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            SetColumn(DetailsPanel,narrow ? 0 : 1);SetRow(DetailsPanel,narrow ? 2 : 1);SetColumnSpan(DetailsPanel,narrow ? 2 : 1);
        }
        private void ShowCalculation(int component,int reaction)
        {
            active=component>=0 ? LoadCard : ReactionCard;active.Begin();
            if(component>=0)
            {
                var c=model.Components[component];string unit=c.IsUniform ? "kN/m" : "kN";
                active.Line(c.Name,true,Brushes.Firebrick);
                if(!string.IsNullOrEmpty(c.SourceCalculation))active.Line(c.SourceCalculation);
                active.Line($"Source design load: {LoadValue.Format(c.SourceValue,5)} {unit}");
                active.Line($"{c.SpacingLabel}: {c.NominalSpacingMm:0.###} mm");
                active.Line($"Beam secondary spacing: {c.ReferenceSpacingMm:0.###} mm");
                if(model.HasPositionedLoads)active.Line($"Load position: {c.PositionMm:0.###} mm from H20 end");
                if(Math.Abs(c.AllocationFactor-1)>1e-9)active.Line($"Remaining normal-zone slab allocation: {c.AllocationFactor*100:0.###}%");
                active.Line($"{LoadValue.Format(c.SourceValue,5)} × {c.ReferenceSpacingMm:0.###} / {c.NominalSpacingMm:0.###}"+(Math.Abs(c.AllocationFactor-1)>1e-9 ? $" × {c.AllocationFactor:0.#####}" : ""));
                active.Line($"Equivalent load = {LoadValue.Format(c.EquivalentValue,5)} {unit}",true,Brushes.Firebrick);
            }
            else
            {
                double r=model.Member.Analysis.Reactions[reaction],reference=model.ReferenceSpacingMm/1000;
                active.Line($"R{reaction+1} · Primary UDL",true,Brushes.SeaGreen);
                active.Line($"Equivalent reaction: {LoadValue.Format(r,5)} kN");
                active.Line($"Beam secondary spacing: {model.ReferenceSpacingMm:0.###} mm ({reference:0.###} m)");
                active.Line($"UDL = {LoadValue.Format(r,5)} / {reference:0.###}");
                active.Line($"Primary UDL = {LoadValue.Format(model.PrimaryTransferKnM[reaction],5)} kN/m",true,Brushes.SeaGreen);
            }
            UpdateCards();
        }
    }
    // Presentation only: conversion and signed analysis are owned by the existing model.
    internal sealed class EquivalentLoadingPlot : FrameworkElement
    {
        internal sealed class Target {internal Rect Bounds;internal int Component=-1,Reaction=-1;}
        internal readonly List<Target> Targets=new List<Target>();
        internal readonly List<Rect> TextBounds=new List<Rect>();
        internal readonly List<string> DrawnLabels=new List<string>();
        internal event Action<int,int> Selected;
        internal readonly EquivalentTransferredLoading model;
        private int selectedComponent=-1,selectedReaction=-1;
        internal static Brush Color(EquivalentLoadingComponent c)=>Brushes.Firebrick;
        internal EquivalentLoadingPlot(EquivalentTransferredLoading model)
        {this.model=model;Focusable=true;Cursor=Cursors.Hand;MouseLeftButtonUp+=(s,e)=> {if(SelectAt(e.GetPosition(this)))e.Handled=true;};}
        internal bool SelectAt(Point point)
        {
            var target=Targets.LastOrDefault(t=>t.Bounds.Contains(point));if(target==null)return false;
            if(target.Component>=0)selectedComponent=target.Component;else selectedReaction=target.Reaction;InvalidateVisual();Selected?.Invoke(target.Component,target.Reaction);return true;
        }
        internal void ClearSelection(){selectedComponent=selectedReaction=-1;InvalidateVisual();}
        internal void ClearLoadSelection(){selectedComponent=-1;InvalidateVisual();}
        internal void ClearReactionSelection(){selectedReaction=-1;InvalidateVisual();}
        private FormattedText Text(string value,Brush color)=>new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        private sealed class Annotation
        {internal string Value;internal Rect Bounds;internal double Anchor;internal int Component=-1,Reaction=-1;}
        private sealed class Layout
        {
            internal readonly List<Annotation> Values=new List<Annotation>(),Reactions=new List<Annotation>(),Dimensions=new List<Annotation>();
            internal readonly List<Rect> Occupied=new List<Rect>();
            internal double BeamY,DimensionY,ExtentY,Height;
        }
        private Annotation Place(Layout layout,string value,double anchor,double y,double width,int component=-1,int reaction=-1)
        {
            var ft=Text(value,Brushes.DarkSlateGray);
            double x=Math.Max(4,Math.Min(width-ft.Width-4,anchor-ft.Width/2));
            var box=new Rect(x-3,y-2,ft.Width+6,ft.Height+4);
            while(layout.Occupied.Any(b=>b.IntersectsWith(box)))box.Offset(0,ft.Height+8);
            layout.Occupied.Add(box);
            return new Annotation {Value=value,Bounds=box,Anchor=anchor,Component=component,Reaction=reaction};
        }
        private Layout ArrangeLabels(double width)
        {
            var layout=new Layout();double left=65,right=Math.Max(left+20,width-20);
            Func<double,double> x=position=>left+(right-left)*position/model.Member.LengthMm;
            for(int i=0;i<model.Components.Length;i++)
            {
                var c=model.Components[i];
                layout.Values.Add(Place(layout,$"{LoadValue.Format(c.EquivalentValue)} {(c.IsUniform ? "kN/m" : "kN")}",x(c.IsUniform ? (c.PositionMm+c.EndMm)/2 : c.PositionMm),8,width,i));
            }
            layout.BeamY=Math.Max(95,layout.Values.Select(a=>a.Bounds.Bottom).DefaultIfEmpty(0).Max()+66);
            for(int i=0;i<model.Member.SupportsMm.Length;i++)
                layout.Reactions.Add(Place(layout,$"R{i+1}: {model.Member.Analysis.Reactions[i]:0.##} kN",x(model.Member.SupportsMm[i]),layout.BeamY+94,width,reaction:i));
            layout.DimensionY=layout.Reactions.Max(a=>a.Bounds.Bottom)+18;
            var stations=Stations();
            for(int i=1;i<stations.Length;i++)
                layout.Dimensions.Add(Place(layout,$"{stations[i]-stations[i-1]:0.###}",x((stations[i]+stations[i-1])/2),layout.DimensionY+7,width));
            layout.ExtentY=layout.Dimensions.Max(a=>a.Bounds.Bottom)+18;
            foreach(var c in model.Components.Where(c=>c.IsUniform))
                layout.Dimensions.Add(Place(layout,$"{c.EndMm-c.PositionMm:0.###} mm",x((c.PositionMm+c.EndMm)/2),layout.ExtentY+7,width));
            double endY=layout.Dimensions.Max(a=>a.Bounds.Bottom)+8;
            layout.Dimensions.Add(Place(layout,"0",left,endY,width));
            layout.Dimensions.Add(Place(layout,$"H20 {model.Member.LengthMm:0.###} mm",right,endY,width));
            layout.Height=Math.Max(270,layout.Occupied.Max(b=>b.Bottom)+12);return layout;
        }
        private double[] Stations()=>new[] {0.0}.Concat(model.Member.SupportsMm).Concat(new[] {model.Member.LengthMm}).Distinct().OrderBy(x=>x).ToArray();
        protected override Size MeasureOverride(Size available)
        {
            double width=double.IsInfinity(available.Width) ? 900 : available.Width;
            return new Size(width,ArrangeLabels(width).Height);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);Targets.Clear();TextBounds.Clear();DrawnLabels.Clear();
            var layout=ArrangeLabels(ActualWidth);double left=65,right=Math.Max(left+20,ActualWidth-20),beamY=layout.BeamY;
            Func<double,double> x=position=>left+(right-left)*position/model.Member.LengthMm;
            Action<Annotation,Brush> label=(a,color)=>
            {
                TextBounds.Add(a.Bounds);DrawnLabels.Add(a.Value);dc.DrawRectangle(Brushes.White,null,a.Bounds);dc.DrawText(Text(a.Value,color),new Point(a.Bounds.Left+3,a.Bounds.Top+2));
                if(a.Component>=0 || a.Reaction>=0)
                {
                    Targets.Add(new Target {Bounds=a.Bounds,Component=a.Component,Reaction=a.Reaction});
                    if(a.Component>=0 && selectedComponent==a.Component || a.Reaction>=0 && selectedReaction==a.Reaction)
                        {
                        bool reaction=a.Reaction>=0;var selectedColor=reaction ? Brushes.SeaGreen : Brushes.Firebrick;
                        dc.DrawRoundedRectangle(new SolidColorBrush(reaction ? System.Windows.Media.Color.FromRgb(230,248,235) : System.Windows.Media.Color.FromRgb(255,236,236)),new Pen(selectedColor,1),a.Bounds,3,3);
                        dc.DrawText(Text(a.Value,selectedColor),new Point(a.Bounds.Left+3,a.Bounds.Top+2));
                    }
                }
            };
            double stroke=1.5;
            Action<double,double,double,Brush> arrow=(px,from,to,color)=>
            {
                double sign=Math.Sign(to-from);var pen=new Pen(color,stroke);dc.DrawLine(pen,new Point(px,from),new Point(px,to));
                dc.DrawLine(pen,new Point(px,to),new Point(px-4,to-sign*7));dc.DrawLine(pen,new Point(px,to),new Point(px+4,to-sign*7));
            };
            Action<double,double,double> dimension=(a,b,y)=>
            {
                dc.DrawLine(new Pen(Brushes.Gray,1),new Point(a,y),new Point(b,y));
                foreach(double px in new[] {a,b})dc.DrawLine(new Pen(Brushes.Gray,1),new Point(px,y-4),new Point(px,y+4));
            };
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
            dc.DrawLine(new Pen(Brushes.DarkSlateGray,4),new Point(left,beamY),new Point(right,beamY));
            double maximum=model.Components.Where(c=>c.IsUniform).Select(c=>Math.Abs(c.EquivalentValue)).DefaultIfEmpty(1).Max();
            for(int i=0;i<model.Components.Length;i++)
            {
                var c=model.Components[i];stroke=i==selectedComponent ? 3 : 1.5;double sign=c.EquivalentValue>=0 ? 1 : -1,tip=beamY-sign*5;
                double from=sign>0 ? beamY-57 : beamY+57;
                if(c.IsUniform)
                {
                    from=tip-sign*52*Math.Abs(c.EquivalentValue)/Math.Max(maximum,1e-10);
                    double a=x(c.PositionMm),b=x(c.EndMm);dc.DrawLine(new Pen(Brushes.Firebrick,i==selectedComponent ? 2.5 : 1),new Point(a,from),new Point(b,from));
                    int arrows=Math.Max(1,(int)((b-a)/45));for(int j=0;j<=arrows;j++)arrow(a+(b-a)*j/arrows,from,tip,Brushes.Firebrick);
                    Targets.Add(new Target {Bounds=new Rect(a-5,Math.Min(from,tip),b-a+10,Math.Abs(tip-from)+2),Component=i});
                }
                else
                {
                    arrow(x(c.PositionMm),from,tip,Brushes.Firebrick);
                    Targets.Add(new Target {Bounds=new Rect(x(c.PositionMm)-7,Math.Min(from,tip),14,Math.Abs(tip-from)+2),Component=i});
                }
                var value=layout.Values[i];
                // Leaders stay between the packed labels and the load arrows.
                dc.DrawLine(new Pen(Brushes.LightGray,.7),new Point(value.Bounds.Left+value.Bounds.Width/2,value.Bounds.Bottom+2),new Point(value.Anchor,beamY-59));
            }
            for(int i=0;i<model.Member.SupportsMm.Length;i++)
            {
                stroke=i==selectedReaction ? 3 : 1.5;double px=x(model.Member.SupportsMm[i]);var triangle=new StreamGeometry();using(var g=triangle.Open()){g.BeginFigure(new Point(px,beamY+3),true,true);g.LineTo(new Point(px-7,beamY+15),true,false);g.LineTo(new Point(px+7,beamY+15),true,false);}dc.DrawGeometry(Brushes.SlateGray,null,triangle);
                dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(px-10,beamY+19),new Point(px+10,beamY+19));
                double reaction=model.Member.Analysis.Reactions[i];arrow(px,reaction>=0 ? beamY+90 : beamY+60,reaction>=0 ? beamY+60 : beamY+90,Brushes.SeaGreen);
                Targets.Add(new Target {Bounds=new Rect(px-8,beamY+58,16,34),Reaction=i});
            }
            var stations=Stations();for(int i=1;i<stations.Length;i++)dimension(x(stations[i-1]),x(stations[i]),layout.DimensionY);
            foreach(var c in model.Components.Where(c=>c.IsUniform))dimension(x(c.PositionMm),x(c.EndMm),layout.ExtentY);
            // Labels render last so all individual values remain readable and selectable.
            foreach(var a in layout.Values)label(a,Brushes.DarkSlateGray);
            foreach(var a in layout.Reactions)label(a,Brushes.DarkSlateGray);
            foreach(var a in layout.Dimensions)label(a,Brushes.DimGray);
        }
    }
}
