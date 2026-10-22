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

    internal sealed class MemberLoadingView : Grid
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
        private readonly double length,ei;
        private readonly double[] supports,reactions;
        private readonly Func<BeamDistributedLoad,LoadCalculation> describe;
        private readonly Func<BeamPointLoad,LoadCalculation> describePoint;
        private double[][] contributions;
        internal MemberLoadingView(TowerDiagramWindow.MemberLoadingPlot plot,double length,double[] supports,double[] reactions,double ei,string basis,Func<BeamDistributedLoad,LoadCalculation> describe=null,Func<BeamPointLoad,LoadCalculation> describePoint=null)
        {
            Plot=plot;this.length=length;this.supports=supports;this.reactions=reactions;this.ei=ei;Basis=basis;this.describe=describe;this.describePoint=describePoint;
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
                if(contributions==null)contributions=Plot.CombinedUniform.Select(q=>Response(new[] {q},new BeamPointLoad[0])).Concat(Plot.PointLoads.Select(p=>Response(new BeamDistributedLoad[0],new[] {p}))).ToArray();
                LastReactionContributions=contributions.Select(r=>r[index]).ToArray();
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
        private double[] Response(BeamDistributedLoad[] uniform,BeamPointLoad[] points)=>LoadedBeamAnalysis.AnalyzeSigned(length,supports,uniform,points,ei,270).Reactions;

        internal static LoadCalculation Tributary(TransferMember member,EngineerPrimaryPlan plan,int line,BeamDistributedLoad q)
        {
            var n=plan.Network;var beam=n.Beams[plan.Beam];int bay=line==0 ? plan.Beam-1 : plan.Beam;
            double position=member.StartMm+(q.StartMm+q.EndMm)/2;
            var crossing=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).FirstOrDefault(x=>x.C.BayNumber==bay+1 && position>=x.C.PositionMm-x.C.WidthMm/2 && position<=x.C.PositionMm+x.C.WidthMm/2);
            bool slab=bay>=0 && bay<n.Bays.Length && plan.CarriesBay(bay);
            var result=new LoadCalculation {Name=crossing!=null ? $"Beam + X{crossing.I+1}" : slab ? "Beam + Slab" : "Beam"};
            if(plan.HasPositionedLoads)
            {
                var at=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).Where(c=>(c.C.BayNumber==plan.Beam || c.C.BayNumber==plan.Beam+1) && position>=c.C.PositionMm-c.C.WidthMm/2 && position<c.C.PositionMm+c.C.WidthMm/2).ToArray();
                result.Name=at.Length>0 ? "Beam + "+string.Join(" + ",at.Select(c=>"X"+(c.I+1))) : plan.SlabLoads.Count>0 ? "Beam + Slab" : "Beam";
                double pitch=plan.ReferenceSpacingMm/1000;
                double reaction=EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,plan.LoadsAt(position))[line]*pitch;
                double primaryTransfer=at.Where(c=>plan.PrimaryBearingTransfers.ContainsKey(c.I)).Sum(c=>plan.PrimaryBearingTransfers[c.I][line]);
                result.Lines.Add($"Positioned beam-secondary reaction: {LoadValue.Format(reaction,5)} kN");
                result.Lines.Add($"Beam secondary spacing: {plan.ReferenceSpacingMm:0.###} mm ({pitch:0.###} m)");
                foreach(var c in at.Where(c=>plan.PrimaryBearingTransfers.ContainsKey(c.I) && Math.Abs(plan.PrimaryBearingTransfers[c.I][line])>1e-10))
                {
                    double transfer=plan.PrimaryBearingTransfers[c.I][line],width=c.C.WidthMm/1000;
                    result.Lines.Add($"X{c.I+1} primary-bearing reactions: {LoadValue.Format(transfer*width,5)} / {width:0.###} = {LoadValue.Format(transfer,5)} kN/m");
                }
                result.Lines.Add(Math.Abs(primaryTransfer)<1e-10 ? $"UDL = {LoadValue.Format(reaction,5)} / {pitch:0.###} = {LoadValue.Format(reaction/pitch,5)} kN/m"
                    : $"UDL = {LoadValue.Format(reaction,5)} / {pitch:0.###} + {LoadValue.Format(primaryTransfer,5)} = {LoadValue.Format(reaction/pitch+primaryTransfer,5)} kN/m");
                foreach(var c in at)result.Lines.Add($"X{c.I+1} footprint: {c.C.PositionMm-c.C.WidthMm/2:0.###}–{c.C.PositionMm+c.C.WidthMm/2:0.###} mm · full width {c.C.WidthMm:0.###} mm");
                foreach(var c in at)if(plan.CrossingResultantOffsetsMm.TryGetValue(c.I,out var offsets) && Math.Abs(offsets[line])>.001)
                    result.Lines.Add($"Physical transfer resultant offset: {offsets[line]:0.###} mm; full-width UDL design zone.");
                if(Math.Abs(member.PrimarySelfWeightKnM)>1e-10)result.Lines.Add($"Primary self-weight: {LoadValue.Format(member.PrimarySelfWeightKnM,5)} kN/m");
                return result;
            }
            if(plan.IsResolved && plan.CrossingTransfers.Count>0)
            {
                double own=n.AreaLoad(beam.DepthMm)*beam.WidthMm/2000;
                var at=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).Where(c=>(c.C.BayNumber==plan.Beam || c.C.BayNumber==plan.Beam+1) && position>=c.C.PositionMm-c.C.WidthMm/2 && position<=c.C.PositionMm+c.C.WidthMm/2).ToArray();
                if(at.Length>0)result.Name="Beam + "+string.Join(" + ",at.Select(c=>"X"+(c.I+1)));
                result.Lines.Add($"Beam: {n.AreaLoad(beam.DepthMm):0.#####} × {beam.WidthMm/1000:0.#####} ÷ 2 = {own:0.#####} kN/m");
                result.Lines.Add($"Assigned {(at.Length>0 ? "crossing" : "slab")} transfer: {LoadValue.Format(q.KnPerM-member.PrimarySelfWeightKnM-own,5)} kN/m");
                foreach(var c in at)result.Lines.Add($"X{c.I+1} footprint: {c.C.PositionMm-c.C.WidthMm/2:0.###}–{c.C.PositionMm+c.C.WidthMm/2:0.###} mm · full width {c.C.WidthMm:0.###} mm");
                if(at.Length>0)result.Lines.Add("Dedicated tower loads are allocated separately and counted once.");
                if(Math.Abs(member.PrimarySelfWeightKnM)>1e-10)result.Lines.Add($"Primary self-weight: {LoadValue.Format(member.PrimarySelfWeightKnM,5)} kN/m");
                return result;
            }
            result.Lines.Add(Pressure(n,$"B{plan.Beam+1}",beam.DepthMm));
            result.Lines.Add($"Beam: {n.AreaLoad(beam.DepthMm):0.#####} × {beam.WidthMm/1000:0.#####} ÷ 2 = {n.AreaLoad(beam.DepthMm)*beam.WidthMm/2000:0.#####} kN/m");
            if(crossing!=null || slab)
            {
                double depth=crossing?.C.DepthMm??n.Bays[bay].ThicknessMm,width=n.Bays[bay].WidthMm/2000;
                string name=crossing!=null ? $"X{crossing.I+1}" : $"S{bay+1}";
                result.Lines.Add(Pressure(n,name,depth));
                result.Lines.Add($"Tributary width: {n.Bays[bay].WidthMm:0.###} ÷ 2 = {width*1000:0.###} mm");
                result.Lines.Add($"{name}: {n.AreaLoad(depth):0.#####} × {width:0.#####} = {n.AreaLoad(depth)*width:0.#####} kN/m");
            }
            if(crossing!=null)result.Lines.Add($"X{crossing.I+1} footprint: {crossing.C.PositionMm-crossing.C.WidthMm/2:0.###}–{crossing.C.PositionMm+crossing.C.WidthMm/2:0.###} mm · full width {crossing.C.WidthMm:0.###} mm");
            if(Math.Abs(member.PrimarySelfWeightKnM)>1e-10)result.Lines.Add($"Primary self-weight: {member.PrimarySelfWeightKnM:0.#####} kN/m");
            result.Lines.Add($"Beam position {member.StartMm+q.StartMm:0.###}–{member.StartMm+q.EndMm:0.###} mm");return result;
        }
        private static string Pressure(TransferNetworkInput n,string name,double depth)
        {
            double dead=25*depth/1000;
            string allowance=n.CustomLoads ? $"{n.LiveKnM2:0.#####} + {n.FormworkKnM2:0.#####}" : $"{BeamCalculatorViewModel.HeapingLoad(dead):0.#####} + 0.75 + 0.4";
            return $"{name} pressure: {depth/1000:0.#####} × 25 + {allowance} = {n.AreaLoad(depth):0.#####} kN/m²";
        }
    }
}
