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

    // Details always describe the loads passed to the displayed member analysis.
    // Reaction contributions are linear unit responses, never replacement design cases.
    internal sealed class MemberLoadingView : Grid
    {
        internal readonly TowerDiagramWindow.MemberLoadingPlot Plot;
        internal readonly Border DetailsPanel;
        internal readonly Button CloseDetailsButton;
        private readonly StackPanel details=new StackPanel();
        internal bool IsDetailsOpen=>DetailsPanel.Visibility==Visibility.Visible;
        internal string[] VisibleCalculation=>details.Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
        internal double[] LastReactionContributions {get;private set;}
        internal readonly string Basis;
        private readonly double length,ei;
        private readonly double[] supports,reactions;
        private readonly Func<BeamDistributedLoad,LoadCalculation> describe;
        private double[][] contributions;

        internal MemberLoadingView(TowerDiagramWindow.MemberLoadingPlot plot,double length,double[] supports,double[] reactions,double ei,string basis,Func<BeamDistributedLoad,LoadCalculation> describe=null)
        {
            Plot=plot;this.length=length;this.supports=supports;this.reactions=reactions;this.ei=ei;Basis=basis;this.describe=describe;
            Background=Brushes.White;
            RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            ColumnDefinitions.Add(new ColumnDefinition());ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});Children.Add(plot);
            CloseDetailsButton=new Button {Content="×",Width=28,Height=28,MinWidth=0,MinHeight=0,Padding=new Thickness(0),FontSize=16,HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close calculation"};
            var body=new StackPanel();body.Children.Add(CloseDetailsButton);body.Children.Add(details);
            DetailsPanel=new Border {Width=260,Padding=new Thickness(12),Background=new SolidColorBrush(Color.FromRgb(247,250,253)),BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(1,0,0,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed,Child=body};
            SetColumn(DetailsPanel,1);Children.Add(DetailsPanel);
            CloseDetailsButton.Click+=(s,e)=> {DetailsPanel.Visibility=Visibility.Collapsed;plot.ClearSelection();LastReactionContributions=null;};
            plot.Selected+=(kind,index)=>ShowCalculation(kind,index);
            SizeChanged+=(s,e)=>LayoutDetails(ActualWidth);
        }
        protected override Size MeasureOverride(Size constraint)
        {LayoutDetails(double.IsInfinity(constraint.Width) ? ActualWidth : constraint.Width);return base.MeasureOverride(constraint);}
        private void LayoutDetails(double width)
        {
            bool narrow=width>0 && width<800;
            DetailsPanel.Width=narrow ? double.NaN : 260;
            DetailsPanel.HorizontalAlignment=narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            SetRow(DetailsPanel,narrow ? 1 : 0);SetColumn(DetailsPanel,narrow ? 0 : 1);SetColumnSpan(DetailsPanel,narrow ? 2 : 1);
        }
        private void Line(string value,bool strong=false,Brush color=null)
            =>details.Children.Add(new TextBlock {Text=value,TextWrapping=TextWrapping.Wrap,FontWeight=strong ? FontWeights.SemiBold : FontWeights.Normal,Foreground=color??Brushes.DimGray,Margin=new Thickness(0,5,0,5)});
        private LoadCalculation Uniform(int index)
        {
            var q=Plot.CombinedUniform[index];var custom=describe?.Invoke(q);if(custom!=null)return custom;
            var result=new LoadCalculation {Name="Distributed load "+(index+1)};
            double middle=(q.StartMm+q.EndMm)/2;
            var sources=Plot.SourceUniform.Where(s=>s.StartMm<middle && s.EndMm>middle && Math.Abs(s.KnPerM)>1e-10).ToArray();
            if(sources.Length>1)result.Lines.Add("Combined UDL: "+string.Join(" + ",sources.Select(s=>s.KnPerM.ToString("0.#####",CultureInfo.CurrentCulture)))+$" = {q.KnPerM:0.#####} kN/m");
            return result;
        }
        private void ShowCalculation(int kind,int index)
        {
            details.Children.Clear();LastReactionContributions=null;DetailsPanel.Visibility=Visibility.Visible;LayoutDetails(ActualWidth);
            if(kind==2)
            {
                Line($"Reaction R{index+1}",true,Brushes.SeaGreen);Line(Basis);
                if(contributions==null)
                {
                    contributions=Plot.CombinedUniform.Select(q=>Response(new[] {q},new BeamPointLoad[0]))
                        .Concat(Plot.PointLoads.Select(p=>Response(new BeamDistributedLoad[0],new[] {p}))).ToArray();
                }
                LastReactionContributions=contributions.Select(r=>r[index]).ToArray();
                for(int i=0;i<contributions.Length;i++)
                {
                    double value=contributions[i][index];if(Math.Abs(value)<=1e-10)continue;
                    string source=i<Plot.CombinedUniform.Length ? Uniform(i).Name+$" ({Plot.CombinedUniform[i].StartMm:0.###}–{Plot.CombinedUniform[i].EndMm:0.###} mm)" : "Point load "+(i-Plot.CombinedUniform.Length+1)+$" ({Plot.PointLoads[i-Plot.CombinedUniform.Length].PositionMm:0.###} mm)";
                    Line($"{source}: {value:0.#####} kN");
                }
                Line($"Σ load contributions = {LastReactionContributions.Sum():0.#####} kN",true,Brushes.SeaGreen);
                Line($"R{index+1} = {reactions[index]:0.#####} kN",true,Brushes.SeaGreen);
                Line($"Support at {supports[index]:0.###} mm from stock start");
            }
            else if(kind==0)
            {
                var q=Plot.CombinedUniform[index];var c=Uniform(index);Line(c.Name,true,Brushes.Firebrick);Line(Basis);
                foreach(string line in c.Lines)Line(line);
                Line($"q = {q.KnPerM:0.#####} kN/m",true,Brushes.Firebrick);
                Line($"Stock interval {q.StartMm:0.###}–{q.EndMm:0.###} mm");
                Line($"{q.KnPerM:0.#####} × {(q.EndMm-q.StartMm)/1000:0.#####} = {q.KnPerM*(q.EndMm-q.StartMm)/1000:0.#####} kN");
            }
            else
            {
                var p=Plot.PointLoads[index];Line("Point load "+(index+1),true,Brushes.Firebrick);Line(Basis);
                Line($"P = {p.Kn:0.#####} kN",true,Brushes.Firebrick);Line($"Stock position {p.PositionMm:0.###} mm");
            }
        }
        private double[] Response(BeamDistributedLoad[] uniform,BeamPointLoad[] points)
            =>LoadedBeamAnalysis.AnalyzeSigned(length,supports,uniform,points,ei,270).Reactions;

        internal static LoadCalculation Tributary(TransferMember member,EngineerPrimaryPlan plan,int line,BeamDistributedLoad q)
        {
            var n=plan.Network;var beam=n.Beams[plan.Beam];int bay=line==0 ? plan.Beam-1 : plan.Beam;
            double position=member.StartMm+(q.StartMm+q.EndMm)/2;
            var crossing=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).FirstOrDefault(x=>x.C.BayNumber==bay+1 && position>=x.C.PositionMm-x.C.WidthMm/2 && position<=x.C.PositionMm+x.C.WidthMm/2);
            bool slab=bay>=0 && bay<n.Bays.Length && plan.CarriesBay(bay);
            var result=new LoadCalculation {Name=crossing!=null ? $"Beam + X{crossing.I+1}" : slab ? "Beam + Slab" : "Beam"};
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
