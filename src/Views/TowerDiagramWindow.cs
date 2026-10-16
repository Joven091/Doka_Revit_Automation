using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace EngineeringDashboard
{
    /// <summary>Read-only response plots and labelled equivalent-UDL views of optimized stock members.</summary>
    public sealed class TowerDiagramWindow : Window
    {
        internal int SelectedRun { get; private set; } = -1;
        internal int SelectedLine { get; private set; }
        public void SelectMember(int run, int line, int member)
        { if(SelectedRun!=run || SelectedLine!=line) {SelectedLoadCase=-1;}SelectedRun = run; SelectedLine = line; Refresh(); }
        internal TowerBeamAnalysis Analysis { get; private set; }
        internal LoadedBeamAnalysis LoadedAnalysis { get; private set; }
        internal TransferMember SelectedTransferMember { get; private set; }
        internal TransferMember DisplayedTransferMember { get; private set; }
        internal LoadedBeamAnalysis ActualLoadedAnalysis { get; private set; }
        internal MemberLoadingPlot LoadingDiagram {get;private set;}
        internal MemberLoadingView MemberView {get;private set;}
        internal int SelectedLoadCase { get; private set; } = -1;
        internal ComboBox LoadCaseSelector {get;private set;}
        internal EquivalentLoadingView EquivalentView {get;private set;}
        internal TextBlock ResponseBasis {get;private set;}
        private readonly TowerLayout layout;
        private readonly TransferNetworkResult network;
        private readonly CrossBeamSupportResult crossSupport;
        private readonly BeamCalculatorViewModel propModel;
        private readonly ComboBox propCount = new ComboBox { Width = 100, Margin = new Thickness(8,0,0,0) };
        private readonly string beamLabel;
        private readonly int networkBeam;
        private readonly StackPanel charts = new StackPanel();
        public TowerDiagramWindow(BeamCalculatorViewModel model)
        {
            network=model.NetworkResult;
            networkBeam=model.IsNetworkMode ? model.SelectedNetworkBeam+1 : 0;
            beamLabel=networkBeam>0 ? $"B{networkBeam} · " : "";
            if (!model.UseTowers)
            {
                if (!model.IsCalculationValid) throw new ArgumentException("Calculate a valid Dokaflex layout first.");
                propModel = model;
            }
            else layout = model.TowerResult ?? throw new ArgumentException("Optimize the tower layout first.");
            Title = "Select member — shear, moment and deflection"; Width = 960; Height = 850; MinWidth=760;MinHeight=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Segoe UI");FontSize=13;
            var panel = new StackPanel { Margin = new Thickness(16), Background=Brushes.WhiteSmoke };
            if (propModel != null)
            {
                panel.Children.Add(new TextBlock { Text = "Single primary H20 · positive moment = sagging; negative deflection = downward. Curves use the calculated uniform line load and equal prop spans. Support reactions are exact for this displayed layout; the spacing calculation retains its conservative reaction coefficients.", TextWrapping = TextWrapping.Wrap });
                if (propModel.NumberOfProps == "4+")
                {
                    var countPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,8,0,0) };
                    countPanel.Children.Add(new TextBlock { Text = "Props in displayed layout", VerticalAlignment = VerticalAlignment.Center });
                    propCount.ItemsSource = Enumerable.Range(4,9).ToArray(); propCount.SelectedItem = 4;
                    propCount.SelectionChanged += (s,e) => Refresh();
                    countPanel.Children.Add(propCount); panel.Children.Add(countPanel);
                }
            }
            panel.Children.Add(charts);
            Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/MyRevitAddin;component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            Background=Brushes.WhiteSmoke;
            Content = new ScrollViewer { Content = panel, Background = Brushes.WhiteSmoke, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                }
        internal TowerDiagramWindow(BeamCalculatorViewModel model,TowerLayout independent,string label):this(model)
        {layout=independent??throw new ArgumentNullException(nameof(independent));beamLabel=label;}
        public TowerDiagramWindow(BeamCalculatorViewModel model,CrossBeamSupportResult support):this(model)
        {crossSupport=support??throw new ArgumentNullException(nameof(support));}
        private void RefreshCross()
        {
            if(SelectedRun<0 || SelectedRun>=crossSupport.Members.Length)return;
            var m=crossSupport.Members[SelectedRun];SelectedTransferMember=DisplayedTransferMember=m;ActualLoadedAnalysis=LoadedAnalysis=m.Analysis;Analysis=null;
            bool primary=m.H20Count==2;
            Title=$"X{crossSupport.CrossingIndex+1} · {m.Name}"+" · "+m.MaterialDescription;charts.Children.Clear();
            if(network!=null && !network.DisplayStage.Crossings.Contains(crossSupport.CrossingIndex))m=ScaleDisplay(m,0);
            SelectedTransferMember=DisplayedTransferMember=m;ActualLoadedAnalysis=LoadedAnalysis=m.Analysis;
            AddMemberSummary(crossSupport.SecondarySpacingDescription,m.Utilization);
            AddMemberLoading(m,primary,crossSupport.SupportDescription);
            AddResponseCharts(m,primary);

        }
        private void Refresh()
        {
            if (SelectedRun < 0) return;
            EquivalentView=null;ResponseBasis=null;MemberView=null;
            if(crossSupport!=null) {RefreshCross();return;}
            if (propModel != null) { RefreshProps(); return; }
            if (layout.Transfer != null) { RefreshTransfer(); return; }
            var piece = layout.StockPlan.Pieces[SelectedRun];
            var supports = layout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => (x - piece.StartMm) / 1000).ToArray();
            double q = (SelectedLine == 0 ? layout.LeftLineLoad : layout.RightLineLoad)+layout.Primary.SelfWeightKnM(piece.StockLengthMm);
            Title = beamLabel+$"P{SelectedRun + 1} · {(SelectedLine == 0 ? "Left" : "Right")} · {layout.Primary.Description}";
            Analysis = TowerBeamAnalysis.Analyze(piece.StockLengthMm / 1000, supports, q, layout.Primary.EI);
            charts.Children.Clear();
            if(layout.Primary.IsWS10)charts.Children.Add(new TextBlock {Text=layout.Primary.Assumptions+$"\nM/V interaction utilization {layout.Primary.Strength(Analysis)*100:0.0}% (10 kNm end zones included).",TextWrapping=TextWrapping.Wrap});
            charts.Children.Add(new TextBlock { Text = $"UDL: {q:0.000} kN/m · loaded length: {piece.StockLengthMm:0.###} mm", TextWrapping = TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
            LoadingDiagram=new MemberLoadingPlot(piece.StockLengthMm,supports.Select(x=>x*1000).ToArray(),Analysis.Reactions,new[] {new BeamDistributedLoad {StartMm=0,EndMm=piece.StockLengthMm,KnPerM=q}},new BeamPointLoad[0]);
            AddInteractiveLoading("Loading diagram",LoadingDiagram,layout.Primary.EI,"Actual checked primary loads");
            string[] names = { "Shear (kN)", "Moment (kNm)", "Deflection (mm)" };
            double[] peaks = { Analysis.Shear, Analysis.Moment, Analysis.DeflectionMm }, limits = { layout.Primary.ShearLimitKn, layout.Primary.MomentLimitKnM, Analysis.DeflectionRegions.Max(x => x.AllowableMm(layout.DeflectionLimitMm, layout.DeflectionDenominator)) };
            for (int i = 0; i < 3; i++)
            {
                string caption=i == 2 && layout.DeflectionDenominator > 0
                    ? $"{names[i]} · peak {peaks[i]:0.000} · L/{layout.DeflectionDenominator} · utilization {Analysis.DeflectionUtilization(layout.DeflectionLimitMm, layout.DeflectionDenominator)*100:0.0}%"
                    : $"{names[i]} · peak {peaks[i]:0.000} / permissible {limits[i]:0.000}";
                AddChart(caption,new ResponsePlot(Analysis,supports,piece.StockLengthMm/1000,i,peaks[i],limits[i],layout.DeflectionDenominator,layout.Primary.IsWS10));
            }
        }
        private void RefreshProps()
        {
            int count = propModel.NumberOfProps == "4+" ? (int)propCount.SelectedItem : int.Parse(propModel.NumberOfProps);
            Analysis = propModel.AnalyzePropPrimary(SelectedLine,count);
            double span = propModel.MaximumPropSpacing / 1000, end = propModel.PrimaryBeamCantilever / 1000;
            double length = (count - 1) * span + 2 * end;
            var supports = Enumerable.Range(0,count).Select(i => end + i * span).ToArray();
            double q = SelectedLine == 0 ? propModel.LeftLineLoad : propModel.RightLineLoad;
            Title = $"Dokaflex · {(SelectedLine == 0 ? "Left" : "Right")} primary · single H20";
            charts.Children.Clear();
            charts.Children.Add(new TextBlock { Text = $"UDL: {q:0.000} kN/m · {count} props · span: {span*1000:0} mm · overhang at each end: {end*1000:0} mm · modeled length: {length*1000:0} mm", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,4) });
            LoadingDiagram = new MemberLoadingPlot(length*1000,supports.Select(x=>x*1000).ToArray(),Analysis.Reactions,
                new[] {new BeamDistributedLoad {StartMm=0,EndMm=length*1000,KnPerM=q}},new BeamPointLoad[0]);
            AddInteractiveLoading("Loading diagram",LoadingDiagram,TowerOptimizer.PairEI/2,"Actual checked primary loads");
            double[] peaks = {Analysis.Shear,Analysis.Moment,Analysis.DeflectionMm};
            double[] limits = {11,5,Analysis.DeflectionRegions.Max(x=>x.AllowableMm(propModel.DeflectionLimit.Millimetres,propModel.DeflectionLimit.Denominator))};
            string[] names = {"Shear (kN)","Moment (kNm)","Deflection (mm)"};
            for (int i=0;i<3;i++)
            {
                string caption = i==2 && propModel.DeflectionLimit.Denominator>0
                    ? $"{names[i]} · peak {peaks[i]:0.000} · {propModel.DeflectionLimit.Label} · utilization {Analysis.DeflectionUtilization(propModel.DeflectionLimit.Millimetres,propModel.DeflectionLimit.Denominator)*100:0.0}%"
                    : $"{names[i]} · peak {peaks[i]:0.000} / permissible {limits[i]:0.000}";
                AddChart(caption,new ResponsePlot(Analysis,supports,length,i,peaks[i],limits[i],propModel.DeflectionLimit.Denominator));
            }
        }
        private void RefreshTransfer()
        {
            EquivalentView=null;ResponseBasis=null;MemberView=null;
            var t=layout.Transfer; int runs=layout.StockPlan.Pieces.Length;
            bool upper=SelectedRun>=runs+t.RowsMm.Length;
            var cases=upper ? null : Enumerable.Range(0,t.CaseNames.Length).Select(c=>SelectedRun<runs ? t.PrimaryCases[c][SelectedRun+SelectedLine*runs] : t.LowerCases[c][SelectedRun-runs]).ToArray();
            bool networkLayout=network!=null && networkBeam>0 && ReferenceEquals(layout,network.Beams[networkBeam-1]);
            int governing=upper ? -1 : networkLayout ? network.SystemGoverningStage.BeamCases[networkBeam-1] : SystemLoadingStage.Governing(layout);
            if(!upper && networkLayout)SelectedLoadCase=network.DisplayStage.BeamCases[networkBeam-1];
            else if(!upper && (SelectedLoadCase<0 || SelectedLoadCase>=cases.Length))SelectedLoadCase=governing;
            TransferMember m;
            if(SelectedRun<runs) m=t.PrimaryCases[SelectedLoadCase][SelectedRun+SelectedLine*runs];
            else if(!upper) m=t.LowerCases[SelectedLoadCase][SelectedRun-runs];
            else
            {
                int encoded=SelectedRun-runs-t.RowsMm.Length,row=encoded/2,side=encoded%2;
                m=t.UpperMemberAtRow(side==0,row);
                if(networkLayout)
                {int bay=side==0 ? networkBeam-2 : networkBeam-1;if(bay<0 || bay>=network.DisplayStage.Slabs.Length || !network.DisplayStage.Slabs[bay])m=ScaleDisplay(m,0);}
            }
            SelectedTransferMember=m; ActualLoadedAnalysis=m.Analysis; Analysis=null;
            bool primary=SelectedRun<runs;
            DisplayedTransferMember=m;LoadedAnalysis=m.Analysis;
            Title=beamLabel+m.Name+" · "+m.MaterialDescription;
            charts.Children.Clear();
            LoadCaseSelector=null;
            if(!upper && t.FullLoadingOnly)
                charts.Children.Add(new TextBlock {Text="Load case: "+CaseLabel(t.CaseNames[SelectedLoadCase]),Margin=new Thickness(0,8,0,8)});
            else if(!upper)
            {
                var choices=new DockPanel {Margin=new Thickness(0,8,0,8)};
                var label=new TextBlock {Text="Load case",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};DockPanel.SetDock(label,Dock.Left);choices.Children.Add(label);
                var selector=new ComboBox {MinWidth=250,HorizontalAlignment=HorizontalAlignment.Stretch,ToolTip="Choose a checked system loading stage. The system governing stage has the highest average of bending, shear, deflection and leg-load category maxima."};
                for(int c=0;c<t.CaseNames.Length;c++)
                    selector.Items.Add(new ComboBoxItem {Content=CaseLabel(t.CaseNames[c])+(t.FullLoadingOnly ? " · fully loaded" : c==governing ? " · system governing" : ""),Tag=c});
                selector.SelectedIndex=SelectedLoadCase;
                selector.SelectionChanged+=(s,e)=> {if(selector.SelectedIndex<0 || selector.SelectedIndex==SelectedLoadCase)return;bool focused=selector.IsKeyboardFocusWithin;SelectedLoadCase=selector.SelectedIndex;if(networkLayout)network.DisplayStage=SystemLoadingStage.Select(network,network.DisplayStage,networkBeam-1,SelectedLoadCase);RefreshTransfer();if(focused)Dispatcher.BeginInvoke(new Action(()=>LoadCaseSelector?.Focus()));};
                LoadCaseSelector=selector;choices.Children.Add(selector);
                charts.Children.Add(choices);
            }
            double spacing=upper ? t.SlabSpacing((SelectedRun-runs-t.RowsMm.Length)%2==0) : t.EnteredUpperSpacingMm;
            AddMemberSummary(spacing,m.Utilization);
            var equivalent=!primary && !upper && t.UnitLowerCases.Length>0
                ? EquivalentTransferredLoading.Create(layout,SelectedRun-runs,SelectedLoadCase) : null;
            LoadingDiagram=null;
            if(equivalent!=null && equivalent.IsAvailable)
            {
                EquivalentView=new EquivalentLoadingView(equivalent,t.ColumnSupportLoads.Length>0);
                AddChart("Loading diagram · equivalent loads",EquivalentView,true);
            }
            else
            {
                AddMemberLoading(m,primary,primary ? layout.Transfer.PrimaryLoadBasis : "Actual checked secondary loads",primary ? t.PrimaryPlan : null);
            }
            if(EquivalentView!=null)
            {
                ResponseBasis=new TextBlock {Text="Member design response · actual checked loads · "+CaseLabel(m.LoadCase),FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0),
                    ToolTip="Shear, moment and deflection use the selected member's actual design analysis. Equivalent loads above express transfer per reference spacing."};
                charts.Children.Add(ResponseBasis);
            }
            AddResponseCharts(m,primary);
        }
        private string CaseLabel(string value)
        {
            if(networkBeam==0 || !value.StartsWith("Beam",StringComparison.Ordinal))return value;
            return ("B"+networkBeam+value.Substring(4)).Replace("both slabs",$"S{networkBeam-1} + S{networkBeam}").Replace("left slab","S"+(networkBeam-1)).Replace("right slab","S"+networkBeam);
        }
        private void AddResponseCharts(TransferMember m,bool primary)
        {
            if(m.IsWS10)charts.Children.Add(new TextBlock {Text=$"WS10 M/V interaction utilization {m.StrengthUtilization*100:0.0}% · moment limits 10 kNm in the first/last 300 mm, 12.3 kNm elsewhere. Both moment and shear act together; plot limits show the individual capacities.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)});
            string[] names={"Shear (kN)","Moment (kNm)","Relative member deflection (mm)"};
            double[] peaks={m.Analysis.ShearKn,m.Analysis.MomentKnM,m.Analysis.DeflectionMm};
            for(int i=0;i<3;i++)
            {
                string caption=i==2 ? $"{names[i]} · peak {peaks[i]:0.000} · L/{layout.DeflectionDenominator} · utilization {m.Analysis.DeflectionUtilization*100:0.0}%" : $"{names[i]} · peak {peaks[i]:0.000} / {(i==0 ? m.ShearLimitKn : m.MomentLimitKnM):0.000}";
                AddChart((primary ? "UDL design · " : "")+caption,new LoadedResponsePlot(m,i,layout.DeflectionDenominator));
            }
        }
        private static TransferMember ScaleDisplay(TransferMember m,double scale)=>new TransferMember {Name=m.Name,LoadCase=m.LoadCase,H20Count=m.H20Count,PrimaryMaterial=m.PrimaryMaterial,StartMm=m.StartMm,LengthMm=m.LengthMm,SupportsMm=m.SupportsMm,
            Uniform=m.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*scale}).ToArray(),Points=m.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*scale}).ToArray(),Analysis=m.Analysis.Scale(scale)};
        private void AddMemberSummary(double spacing,double utilization)=>AddMemberSummary($"{spacing:0.###} mm",utilization);
        private void AddMemberSummary(string spacing,double utilization)
        {
            charts.Children.Add(new TextBlock {Text=$"Secondary beam spacing: {spacing}",Margin=new Thickness(0,6,0,4)});
            charts.Children.Add(new TextBlock {Text=$"Utilization rate: {utilization*100:0.0}%",Margin=new Thickness(0,0,0,4)});
        }
        private void AddMemberLoading(TransferMember member,bool primary,string basis,EngineerPrimaryPlan plan=null)
        {
            LoadingDiagram=new MemberLoadingPlot(member.LengthMm,member.SupportsMm,member.Analysis.Reactions,member.Uniform,member.Points);
            AddInteractiveLoading("Loading diagram"+(primary ? " · UDL design loads" : " · actual loads"),LoadingDiagram,member.EI,basis,
                plan==null ? (Func<BeamDistributedLoad,LoadCalculation>)null : q=>MemberLoadingView.Tributary(member,plan,SelectedLine,q));
        }
        private void AddInteractiveLoading(string caption,MemberLoadingPlot plot,double ei,string basis,Func<BeamDistributedLoad,LoadCalculation> describe=null)
        {
            MemberView=new MemberLoadingView(plot,plot.LengthMm,plot.SupportPositions,plot.ReactionValues,ei,basis,describe);
            AddChart(caption,MemberView,true);
        }
        private void AddChart(string caption,FrameworkElement plot,bool loading=false)
        {
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock {Text=caption,FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(51,51,51)),Margin=new Thickness(0,0,0,6)});
            panel.Children.Add(plot);
            panel.Children.Add(new TextBlock {Text=loading ? plot is EquivalentLoadingView ? "Red: equivalent load · green: support reactions · mm. Click a load or reaction for details." : "Red: applied load · green: support reactions · mm. Click a load or reaction for details." : "Distance from the left stock end (mm). Dashed verticals: supports · red dashed lines: permissible limits.",FontSize=12,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,0)});
            var card=new Border {Child=panel};card.SetResourceReference(StyleProperty,"Card");charts.Children.Add(card);
        }
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
            private Target selected;
            internal int SelectedKind=>selected?.Kind??-1;
            internal int SelectedIndex=>selected?.Index??-1;
            internal bool SelectAt(Point point)
            {
                var hit=Targets.LastOrDefault(t=>t.Bounds.Contains(point));if(hit==null)return false;
                selected=hit;InvalidateVisual();Selected?.Invoke(hit.Kind,hit.Index);return true;
            }
            internal void ClearSelection(){selected=null;InvalidateVisual();}
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
                for(int i=0;i<uniform.Length;i++)place($"{uniform[i].KnPerM:0.###} kN/m",(uniform[i].StartMm+uniform[i].EndMm)/2,0,i);
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
                Action<double,double,double,Brush> arrow=(x,from,to,colour)=>
                {
                    double sign=Math.Sign(to-from);var pen=new Pen(colour,1.5);dc.DrawLine(pen,new Point(x,from),new Point(x,to));
                    dc.DrawLine(pen,new Point(x,to),new Point(x-4,to-sign*7));dc.DrawLine(pen,new Point(x,to),new Point(x+4,to-sign*7));
                };
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
                dc.DrawLine(new Pen(Brushes.DarkSlateGray,4),new Point(left,beamY),new Point(right,beamY));
                for(int i=0;i<uniform.Length;i++)
                {
                    var q=uniform[i];double sign=Math.Sign(q.KnPerM),tip=beamY-sign*5,y=tip-sign*52*Math.Abs(q.KnPerM)/maximum,a=xx(q.StartMm),b=xx(q.EndMm);
                    dc.DrawLine(new Pen(Brushes.Firebrick,1),new Point(a,y),new Point(b,y));
                    int count=Math.Max(1,(int)((b-a)/45));
                    for(int k=0;k<=count;k++)arrow(a+(b-a)*k/count,y,tip,Brushes.Firebrick);
                    Targets.Add(new Target {Bounds=new Rect(a-5,Math.Min(y,tip),b-a+10,Math.Abs(tip-y)+2),Kind=0,Index=i});
                    DrawnUniformCount++;
                }
                for(int pointIndex=0;pointIndex<points.Length;pointIndex++)
                {
                    var p=points[pointIndex];double x=xx(p.PositionMm),sign=Math.Sign(p.Kn),tip=beamY-sign*5,from=tip-sign*52;
                    arrow(x,from,tip,Brushes.Firebrick);
                    Targets.Add(new Target {Bounds=new Rect(x-7,Math.Min(from,tip),14,Math.Abs(from-tip)+2),Kind=1,Index=pointIndex});
                    DrawnPointCount++;
                }
                var reactionBoxes=PlaceReactionLabels(ActualWidth,beamY);
                for(int i=0;i<supports.Length;i++)
                {
                    double x=xx(supports[i]);var triangle=new StreamGeometry();using(var c=triangle.Open()){c.BeginFigure(new Point(x,beamY+3),true,true);c.LineTo(new Point(x-7,beamY+15),true,false);c.LineTo(new Point(x+7,beamY+15),true,false);}
                    dc.DrawGeometry(Brushes.SlateGray,null,triangle);dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(x-10,beamY+19),new Point(x+10,beamY+19));
                    arrow(x,beamY+(reactions[i]>=0 ? 90 : 60),beamY+(reactions[i]>=0 ? 60 : 90),Brushes.SeaGreen);
                    var box=reactionBoxes[i];dc.DrawText(ReactionText(i),new Point(box.Left+2,box.Top+1));
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
                    dc.DrawRectangle(Brushes.White,null,item.Bounds);
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
        internal sealed class LoadedResponsePlot : FrameworkElement
        {
            readonly TransferMember member; readonly int index,denominator;
            internal TransferMember Member=>member;
            internal int DiagramIndex=>index;
            public LoadedResponsePlot(TransferMember m,int i,int ratio) {member=m;index=i;denominator=ratio;Height=220;}
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc); double left=65,right=Math.Max(left+20,ActualWidth-20),top=20,bottom=175;
                var stations=new[] {0.0}.Concat(member.SupportsMm).Concat(new[] {member.LengthMm}).Distinct().OrderBy(x=>x).ToArray();
                double limit=index==0 ? member.ShearLimitKn : index==1 ? member.MomentLimitKnM : Enumerable.Range(1,stations.Length-1).Max(i=>stations[i]-stations[i-1])/denominator;
                double peak=index==0 ? member.Analysis.ShearKn : index==1 ? member.Analysis.MomentKnM : member.Analysis.DeflectionMm,range=Math.Max(.001,Math.Max(peak,limit)*1.15);
                Func<double,double> xx=x=>left+x/member.LengthMm*(right-left),yy=y=>(top+bottom)/2-y/range*(bottom-top)/2;
                Action<string,double,double> label=(s,x,y)=>dc.DrawText(new FormattedText(s,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.Black,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,Height));Axes(dc,left,right,top,bottom,range,index,label);
                var limitStations=index==1 && member.IsWS10 ? stations.Concat(new[] {Math.Min(300,member.LengthMm),Math.Max(0,member.LengthMm-300)}).Distinct().OrderBy(x=>x).ToArray() : stations;
                for(int i=1;i<limitStations.Length;i++)
                {
                    double allowable=index==2 ? (limitStations[i]-limitStations[i-1])/denominator : index==1 && member.IsWS10 && ((limitStations[i-1]+limitStations[i])/2<300 || (limitStations[i-1]+limitStations[i])/2>member.LengthMm-300) ? 10 : limit;
                    foreach(double sign in new[] {-1.0,1.0}) dc.DrawLine(new Pen(Brushes.IndianRed,1){DashStyle=DashStyles.Dash},new Point(xx(limitStations[i-1]),yy(sign*allowable)),new Point(xx(limitStations[i]),yy(sign*allowable)));
                    if(index==2) label($"±{allowable:0.##}",(xx(limitStations[i-1])+xx(limitStations[i]))/2-14,yy(allowable)-14);
                }
                foreach(double s in member.SupportsMm) dc.DrawLine(new Pen(Brushes.LightGray,1){DashStyle=DashStyles.Dash},new Point(xx(s),top),new Point(xx(s),bottom));
                var jumps=member.SupportsMm.Concat(member.Points.Select(p=>p.PositionMm)).Distinct().ToArray();
                var positions=Enumerable.Range(0,401).Select(i=>member.LengthMm*i/400).Concat(jumps).Distinct().OrderBy(x=>x);Point? previous=null;
                foreach(double x in positions) foreach(bool side in jumps.Contains(x) ? new[] {false,true} : new[] {true})
                {
                    var point=new Point(xx(x),yy(member.Analysis.Response(x,side)[index]));if(previous.HasValue)dc.DrawLine(new Pen(Brushes.SteelBlue,2),previous.Value,point);previous=point;
                }
                label("0",left,bottom+4);label($"{member.LengthMm:0} mm",right-60,bottom+4);
            }
        }
        private sealed class ResponsePlot : FrameworkElement
        {
            readonly TowerBeamAnalysis analysis; readonly double[] supports; readonly double length, range, limit; readonly int index, denominator; readonly bool ws10;
            public ResponsePlot(TowerBeamAnalysis a, double[] s, double l, int i, double peak, double allowable, int ratio,bool steel=false)
            { analysis = a; ws10=steel; denominator = ratio; supports = s; length = l; index = i; limit = allowable; range = Math.Max(.001,Math.Max(peak, allowable) * 1.15); Height = 220; }
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc); double left = 65, right = Math.Max(left + 20, ActualWidth - 20), top = 20, bottom = 175;
                Func<double, double> xx = x => left + x / length * (right - left);
                Func<double, double> yy = y => (top + bottom) / 2 - y / range * (bottom - top) / 2;
                Action<string, double, double> label = (s, x, y) => dc.DrawText(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, Height));
                Axes(dc,left,right,top,bottom,range,index,label);
                if (index == 2 && denominator > 0)
                {
                    foreach (var region in analysis.DeflectionRegions)
                    {
                        double allowable = (region.EndMm-region.StartMm)/denominator;
                        foreach (double sign in new[] { -1.0, 1.0 })
                            dc.DrawLine(new Pen(Brushes.IndianRed, 1) { DashStyle = DashStyles.Dash }, new Point(xx(region.StartMm/1000), yy(sign*allowable)), new Point(xx(region.EndMm/1000), yy(sign*allowable)));
                        label($"±{allowable:0.##}", Math.Max(left, (xx(region.StartMm/1000)+xx(region.EndMm/1000))/2-14), yy(allowable)-14);
                    }
                }
                else if(index==1 && ws10)
                {
                    var cuts=new[] {0.0,Math.Min(.3,length),Math.Max(0,length-.3),length}.Distinct().OrderBy(x=>x).ToArray();
                    for(int i=1;i<cuts.Length;i++)foreach(double sign in new[] {-1.0,1})
                    {double allowable=(cuts[i-1]+cuts[i])/2<.3 || (cuts[i-1]+cuts[i])/2>length-.3 ? 10 : 12.3;dc.DrawLine(new Pen(Brushes.IndianRed,1){DashStyle=DashStyles.Dash},new Point(xx(cuts[i-1]),yy(sign*allowable)),new Point(xx(cuts[i]),yy(sign*allowable)));}
                }
                else foreach (double value in new[] { -limit, limit })
                { dc.DrawLine(new Pen(Brushes.IndianRed, 1) { DashStyle = DashStyles.Dash }, new Point(left, yy(value)), new Point(right, yy(value))); }
                foreach (double x in supports) dc.DrawLine(new Pen(Brushes.LightGray, 1) { DashStyle = DashStyles.Dash }, new Point(xx(x), top), new Point(xx(x), bottom));
                var positions = Enumerable.Range(0, 401).Select(i => length * i / 400).Concat(supports).Distinct().OrderBy(x => x);
                Point? previous = null;
                foreach (double x in positions)
                    foreach (bool side in supports.Contains(x) ? new[] { false, true } : new[] { true })
                    {
                        var point = new Point(xx(x), yy(analysis.Response(x, side)[index]));
                        if (previous.HasValue) dc.DrawLine(new Pen(Brushes.SteelBlue, 2), previous.Value, point);
                        previous = point;
                    }
                label("0", left, bottom + 4); label($"{length * 1000:0} mm", right - 60, bottom + 4);
            }
        }
    }
}


