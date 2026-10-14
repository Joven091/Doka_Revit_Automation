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
        internal readonly Button CloseDetailsButton;
        internal readonly StackPanel Details=new StackPanel();
        internal bool IsDetailsOpen=>DetailsPanel.Visibility==Visibility.Visible;
        internal string[] VisibleCalculation=>Details.Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
        private readonly EquivalentTransferredLoading model;
        internal EquivalentLoadingView(EquivalentTransferredLoading model,bool externalEnds,Action showChecks)
        {
            this.model=model;Background=Brushes.White;Margin=new Thickness(0,8,0,8);
            RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            ColumnDefinitions.Add(new ColumnDefinition());ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var header=new DockPanel {Margin=new Thickness(12,8,12,4)};
            var reference=new TextBlock {Text=$"Reference {model.ReferenceSpacingMm:0.###} mm",Foreground=Brushes.SteelBlue,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(reference,Dock.Right);header.Children.Add(reference);
            header.Children.Add(new TextBlock {Text="Equivalent loads",FontWeight=FontWeights.SemiBold});SetColumnSpan(header,2);Children.Add(header);
            var close=new Button {Content="×",Width=28,Height=26,HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close calculation"};CloseDetailsButton=close;
            var detailBody=new StackPanel();detailBody.Children.Add(close);detailBody.Children.Add(Details);
            DetailsPanel=new Border {Width=244,Padding=new Thickness(12),Background=new SolidColorBrush(Color.FromRgb(247,250,253)),BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(1,0,0,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed,Child=detailBody};
            SetColumn(DetailsPanel,1);SetRow(DetailsPanel,1);Children.Add(DetailsPanel);
            SizeChanged+=(s,e)=>LayoutDetails();
            close.Click+=(s,e)=> {DetailsPanel.Visibility=Visibility.Collapsed;Plot?.ClearSelection();};
            if(!model.IsAvailable)
            {
                var body=new StackPanel {Margin=new Thickness(12)};body.Children.Add(new TextBlock {Text=model.UnavailableReason,TextWrapping=TextWrapping.Wrap});
                var checks=new Button {Content="Secondary design checks",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};checks.Click+=(s,e)=>showChecks();body.Children.Add(checks);SetRow(body,1);Children.Add(body);return;
            }
            Plot=new EquivalentLoadingPlot(model);SetRow(Plot,1);Children.Add(Plot);Plot.Selected+=(component,reaction)=>ShowCalculation(component,reaction);
            var footer=new DockPanel {Margin=new Thickness(12,4,12,10)};
            if(externalEnds) {var status=new TextBlock {Text="End supports unchecked",Foreground=Brushes.DarkGoldenrod,ToolTip="External column-side end supports are assumed; capacity requires separate verification."};DockPanel.SetDock(status,Dock.Right);footer.Children.Add(status);}
            footer.Children.Add(new TextBlock {Text=$"Primary UDL L/R {model.PrimaryTransferKnM[0]+model.PrimarySelfWeightKnM[0]:0.###} / {model.PrimaryTransferKnM[1]+model.PrimarySelfWeightKnM[1]:0.###} kN/m",FontWeight=FontWeights.SemiBold});SetRow(footer,3);SetColumnSpan(footer,2);Children.Add(footer);
        }
        private void LayoutDetails()
        {
            bool narrow=ActualWidth>0 && ActualWidth<600;
            DetailsPanel.Width=narrow ? double.NaN : 244;DetailsPanel.HorizontalAlignment=narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            SetColumn(DetailsPanel,narrow ? 0 : 1);SetRow(DetailsPanel,narrow ? 2 : 1);SetColumnSpan(DetailsPanel,narrow ? 2 : 1);
        }
        private void Line(string text,bool strong=false,Brush color=null)
            =>Details.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=strong ? FontWeights.SemiBold : FontWeights.Normal,Foreground=color??Brushes.DimGray,Margin=new Thickness(0,5,0,5)});
        private void ShowCalculation(int component,int reaction)
        {
            Details.Children.Clear();DetailsPanel.Visibility=Visibility.Visible;LayoutDetails();
            if(component>=0)
            {
                var c=model.Components[component];string unit=c.IsUniform ? "kN/m" : "kN";
                Line(c.Name,true,EquivalentLoadingPlot.Color(c));
                Line($"{c.SourceValue:0.#####} {unit} · {c.NominalSpacingMm:0.###} mm");
                Line($"× {c.ReferenceSpacingMm:0.###} / {c.NominalSpacingMm:0.###}");
                Line($"= {c.EquivalentValue:0.#####} {unit}",true,EquivalentLoadingPlot.Color(c));
                Line(c.IsUniform ? $"{c.PositionMm:0.###}–{c.EndMm:0.###} mm" : $"x {c.PositionMm:0.###} mm");
            }
            else
            {
                Line(reaction==0 ? "Left reaction" : "Right reaction",true,Brushes.Teal);
                foreach(var c in model.Components)Line($"{c.Name}: {c.ReactionsKn[reaction]:0.#####} kN",color:EquivalentLoadingPlot.Color(c));
                double r=model.Member.Analysis.Reactions[reaction],reference=model.ReferenceSpacingMm/1000;
                Line($"R = {r:0.#####} kN",true,Brushes.Teal);
                Line($"{r:0.#####} / {reference:0.###} = {model.PrimaryTransferKnM[reaction]:0.#####} kN/m",true,Brushes.Teal);
                if(model.PrimarySelfWeightKnM[reaction]!=0)Line($"+ {model.PrimarySelfWeightKnM[reaction]:0.#####} kN/m primary self-weight");
            }
        }
    }
    internal sealed class EquivalentLoadingPlot : FrameworkElement
    {
        internal sealed class Target {internal Rect Bounds;internal int Component=-1,Reaction=-1;}
        internal readonly List<Target> Targets=new List<Target>();
        internal readonly List<Rect> TextBounds=new List<Rect>();
        internal event Action<int,int> Selected;
        private readonly EquivalentTransferredLoading model;
        private int selectedComponent=-1,selectedReaction=-1;
        internal static Brush Color(EquivalentLoadingComponent c)=>c.IsUniform ? Brushes.IndianRed : c.Name.Contains("shutter") ? Brushes.DarkOrange : Brushes.SteelBlue;
        internal EquivalentLoadingPlot(EquivalentTransferredLoading model)
        {this.model=model;Height=350;MinWidth=340;Focusable=true;Cursor=Cursors.Hand;MouseLeftButtonDown+=(s,e)=> {if(SelectAt(e.GetPosition(this)))e.Handled=true;};}
        internal bool SelectAt(Point point)
        {
            var target=Targets.LastOrDefault(t=>t.Bounds.Contains(point));if(target==null)return false;
            selectedComponent=target.Component;selectedReaction=target.Reaction;InvalidateVisual();Selected?.Invoke(target.Component,target.Reaction);return true;
        }
        internal void ClearSelection(){selectedComponent=selectedReaction=-1;InvalidateVisual();}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);Targets.Clear();TextBounds.Clear();dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,Height));
            double left=38,right=Math.Max(left+40,ActualWidth-38),yBeam=205;
            Func<double,double> x=position=>left+(right-left)*position/model.Member.LengthMm;
            Func<string,double,double,Brush,bool,Rect> text=(label,centre,y,color,strong)=>
            {
                var ft=new FormattedText(label,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,strong ? FontWeights.SemiBold : FontWeights.Normal,FontStretches.Normal),12,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double px=Math.Max(2,Math.Min(ActualWidth-ft.Width-2,centre-ft.Width/2));var box=new Rect(px,y,ft.Width,ft.Height);TextBounds.Add(box);dc.DrawText(ft,new Point(px,y));return box;
            };
            Action<double,double,double,Brush> arrow=(px,from,to,color)=> {var pen=new Pen(color,2);dc.DrawLine(pen,new Point(px,from),new Point(px,to));double sign=to>from ? -1 : 1;dc.DrawLine(pen,new Point(px,to),new Point(px-4,to+sign*7));dc.DrawLine(pen,new Point(px,to),new Point(px+4,to+sign*7));};
            var occupied=new List<Rect>();
            for(int i=0;i<model.Components.Length;i++)
            {
                var c=model.Components[i];var color=Color(c);double centre=x(c.IsUniform ? (c.PositionMm+c.EndMm)/2 : c.PositionMm),labelY=16;
                // Stagger only colliding value/name blocks; source positions remain exact.
                Rect estimate=new Rect(Math.Max(2,centre-62),labelY,124,38);
                while(occupied.Any(b=>b.IntersectsWith(estimate))) {labelY+=42;estimate.Y=labelY;}
                occupied.Add(estimate);
                string name=c.Name.Replace("Left ","L ").Replace("Right ","R ");
                var title=text(name,centre,labelY,color,false);var value=text($"{c.EquivalentValue:0.###} {(c.IsUniform ? "kN/m" : "kN")}",centre,labelY+18,color,true);
                var hit=Rect.Union(title,value);hit.Inflate(5,3);Targets.Add(new Target {Bounds=hit,Component=i});
                if(selectedComponent==i)dc.DrawRoundedRectangle(null,new Pen(color,1),hit,3,3);
                double top=Math.Max(115,labelY+44);
                if(c.IsUniform)
                {
                    text($"{c.EndMm-c.PositionMm:0.###} mm",centre,top-25,Brushes.DimGray,false);
                    dc.DrawLine(new Pen(Brushes.Gray,.7),new Point(x(c.PositionMm),top-5),new Point(x(c.EndMm),top-5));
                    dc.DrawLine(new Pen(color,1),new Point(x(c.PositionMm),top),new Point(x(c.EndMm),top));
                    int arrows=Math.Max(2,(int)((x(c.EndMm)-x(c.PositionMm))/32));for(int a=0;a<=arrows;a++)arrow(x(c.PositionMm+(c.EndMm-c.PositionMm)*a/arrows),top,yBeam-9,color);
                    Targets.Add(new Target {Bounds=new Rect(x(c.PositionMm)-5,top,x(c.EndMm)-x(c.PositionMm)+10,yBeam-top),Component=i});
                }
                else {arrow(x(c.PositionMm),top,yBeam-9,color);Targets.Add(new Target {Bounds=new Rect(x(c.PositionMm)-7,top,14,yBeam-top),Component=i});}
            }
            dc.DrawRectangle(Brushes.Wheat,new Pen(Brushes.Tan,1),new Rect(left,yBeam,right-left,10));
            for(int i=0;i<model.Member.SupportsMm.Length;i++)
            {
                double px=x(model.Member.SupportsMm[i]);var triangle=new StreamGeometry();using(var g=triangle.Open()){g.BeginFigure(new Point(px,yBeam+11),true,true);g.LineTo(new Point(px-12,yBeam+27),true,false);g.LineTo(new Point(px+12,yBeam+27),true,false);}dc.DrawGeometry(Brushes.Gainsboro,new Pen(Brushes.Gray,1),triangle);
                double reaction=model.Member.Analysis.Reactions[i];arrow(px,reaction>=0 ? 259 : 235,reaction>=0 ? 235 : 259,Brushes.Teal);
                var box=text($"R {(i==0 ? "L" : "R")} {reaction:0.###} kN",px,266,Brushes.Teal,true);var hit=box;hit.Inflate(5,4);Targets.Add(new Target {Bounds=hit,Reaction=i});Targets.Add(new Target {Bounds=new Rect(px-8,233,16,28),Reaction=i});
                if(selectedReaction==i)dc.DrawRoundedRectangle(null,new Pen(Brushes.Teal,1),hit,3,3);
            }
            var dimensions=new[] {0.0}.Concat(model.Member.SupportsMm).Concat(new[] {model.Member.LengthMm}).ToArray();
            for(int i=1;i<dimensions.Length;i++) {double a=x(dimensions[i-1]),b=x(dimensions[i]);dc.DrawLine(new Pen(Brushes.Gray,.7),new Point(a,314),new Point(b,314));dc.DrawLine(new Pen(Brushes.Gray,.7),new Point(a,309),new Point(a,319));dc.DrawLine(new Pen(Brushes.Gray,.7),new Point(b,309),new Point(b,319));text($"{dimensions[i]-dimensions[i-1]:0.###}",(a+b)/2,293,Brushes.DimGray,false);}
            text($"H20 {model.Member.LengthMm:0.###} mm",(left+right)/2,329,Brushes.DimGray,false);
        }
    }
}
