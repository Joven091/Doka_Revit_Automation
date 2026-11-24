using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal sealed class LoadCalculation
    {
        internal string Name;
        internal readonly List<string> Lines=new List<string>();
    }

    internal sealed class CalculationCard : Border
    {
        internal readonly StackPanel Lines=new StackPanel();
        internal readonly Button CloseButton;
        internal bool IsOpen=>Visibility==Visibility.Visible;
        internal string[] Text=>Lines.Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
        internal CalculationCard()
        {
            Visibility=Visibility.Collapsed;Padding=new Thickness(12);Margin=new Thickness(0,0,0,8);
            Background=new SolidColorBrush(Color.FromRgb(247,250,253));BorderBrush=Brushes.LightGray;BorderThickness=new Thickness(1);
            CloseButton=new Button {Content="×",Width=28,Height=28,MinWidth=0,MinHeight=0,Padding=new Thickness(0),FontSize=16,HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close calculation"};
            var body=new StackPanel();body.Children.Add(CloseButton);body.Children.Add(Lines);Child=body;
        }
        internal void Begin(){Lines.Children.Clear();Visibility=Visibility.Visible;}
        internal void Line(string text,bool strong=false,Brush color=null)
            =>Lines.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=strong ? FontWeights.SemiBold : FontWeights.Normal,Foreground=color??Brushes.DimGray,Margin=new Thickness(0,5,0,5)});
    }

    internal sealed partial class MemberLoadingView : Grid
    {
        internal readonly TowerDiagramWindow.MemberLoadingPlot Plot;
        internal readonly Border DetailsPanel;
        internal readonly CalculationCard LoadCard=new CalculationCard(),ReactionCard=new CalculationCard();
        internal Button CloseDetailsButton=>active==ReactionCard ? ReactionCard.CloseButton : LoadCard.CloseButton;
        internal bool IsDetailsOpen=>LoadCard.IsOpen || ReactionCard.IsOpen;
        internal string[] VisibleCalculation=>active?.Text??new string[0];
        internal double[] LastReactionContributions {get;private set;}
        internal readonly string Basis;
        private CalculationCard active;
        private readonly Func<BeamDistributedLoad[],BeamPointLoad[],double[]> response;
        private readonly double[] supports,reactions;
        private readonly Func<BeamDistributedLoad,LoadCalculation> describe;
        private readonly Func<BeamPointLoad,LoadCalculation> describePoint;
        private double[][] contributions;
        internal MemberLoadingView(TowerDiagramWindow.MemberLoadingPlot plot,double[] supports,double[] reactions,string basis,Func<BeamDistributedLoad[],BeamPointLoad[],double[]> response,Func<BeamDistributedLoad,LoadCalculation> describe=null,Func<BeamPointLoad,LoadCalculation> describePoint=null)
        {
            Plot=plot;this.supports=supports;this.reactions=reactions;this.response=response;Basis=basis;this.describe=describe;this.describePoint=describePoint;
            Background=Brushes.White;
            RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            ColumnDefinitions.Add(new ColumnDefinition());ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});Children.Add(plot);
            var cards=new StackPanel();cards.Children.Add(LoadCard);cards.Children.Add(ReactionCard);
            DetailsPanel=new Border {Width=290,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed,Child=cards};
            SetColumn(DetailsPanel,1);Children.Add(DetailsPanel);
            LoadCard.CloseButton.Click+=(s,e)=> {LoadCard.Visibility=Visibility.Collapsed;plot.ClearLoadSelection();UpdateCards();};
            ReactionCard.CloseButton.Click+=(s,e)=> {ReactionCard.Visibility=Visibility.Collapsed;plot.ClearReactionSelection();LastReactionContributions=null;UpdateCards();};
            plot.Selected+=(kind,index)=>ShowCalculation(kind,index);SizeChanged+=(s,e)=>LayoutDetails(ActualWidth);
        }
        internal void CloseAllDetails(){LoadCard.Visibility=ReactionCard.Visibility=Visibility.Collapsed;Plot.ClearSelection();LastReactionContributions=null;UpdateCards();}
        private void UpdateCards(){DetailsPanel.Visibility=IsDetailsOpen ? Visibility.Visible : Visibility.Collapsed;LayoutDetails(ActualWidth);}
        protected override Size MeasureOverride(Size constraint)
        {LayoutDetails(double.IsInfinity(constraint.Width) ? ActualWidth : constraint.Width);return base.MeasureOverride(constraint);}
        private void LayoutDetails(double width)
        {
            bool narrow=width>0 && width<800;DetailsPanel.Width=narrow ? double.NaN : 290;DetailsPanel.Margin=narrow ? new Thickness(0,10,0,0) : new Thickness(12,0,0,0);
            DetailsPanel.HorizontalAlignment=narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            SetRow(DetailsPanel,narrow ? 1 : 0);SetColumn(DetailsPanel,narrow ? 0 : 1);SetColumnSpan(DetailsPanel,narrow ? 2 : 1);
        }
        private LoadCalculation Uniform(int index)
        {
            var q=Plot.CombinedUniform[index];var custom=describe?.Invoke(q);if(custom!=null)return custom;
            return new LoadCalculation {Name="Distributed load "+(index+1)};
        }
        private LoadCalculation PointLoad(int index)=>describePoint?.Invoke(Plot.PointLoads[index])??new LoadCalculation {Name="Point load "+(index+1)};
        private void ShowCalculation(int kind,int index)
        {
            active=kind==2 ? ReactionCard : LoadCard;active.Begin();
            if(kind==2)
            {
                active.Line($"Reaction R{index+1}",true,Brushes.SeaGreen);
                // Retain a numeric regression hook without displaying a component breakdown.
                if(response!=null && contributions==null)contributions=Plot.CombinedUniform.Select(q=>response(new[] {q},new BeamPointLoad[0])).Concat(Plot.PointLoads.Select(p=>response(new BeamDistributedLoad[0],new[] {p}))).ToArray();
                LastReactionContributions=contributions?.Select(r=>r[index]).ToArray();
                active.Line($"Support reaction = {LoadValue.Format(reactions[index],5)} kN",true,Brushes.SeaGreen);
                active.Line($"Distance from left end: {supports[index]:0.###} mm");
            }
            else if(kind==0)
            {
                var q=Plot.CombinedUniform[index];var c=Uniform(index);active.Line(c.Name,true,Brushes.Firebrick);
                foreach(string line in c.Lines)active.Line(line);
                active.Line($"q = {LoadValue.Format(q.KnPerM,5)} kN/m",true,Brushes.Firebrick);
                active.Line($"Distance from left end: {q.StartMm:0.###}–{q.EndMm:0.###} mm");
            }
            else
            {
                var p=Plot.PointLoads[index];var c=PointLoad(index);active.Line(c.Name,true,Brushes.Firebrick);foreach(string line in c.Lines)active.Line(line);
                active.Line($"Load = {LoadValue.Format(p.Kn,5)} kN",true,Brushes.Firebrick);active.Line($"Distance from left end: {p.PositionMm:0.###} mm");
            }
            UpdateCards();
        }
    }
}
