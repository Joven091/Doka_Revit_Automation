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
    public sealed partial class TowerDiagramWindow : Window
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
            var crossingRows=crossSupport.Towers?.Transfer;
            int row=crossingRows==null ? -1 : Array.IndexOf(crossingRows.LowerCases[0],m);
            var equivalent=row<0 ? null : EquivalentTransferredLoading.Create(crossSupport.Towers,row,0);
            if(equivalent?.IsAvailable==true){EquivalentView=new EquivalentLoadingView(equivalent,false);LoadingDiagram=null;AddChart("Loading diagram · equivalent loads",EquivalentView,true);}
            else AddMemberLoading(m,primary,crossSupport.SupportDescription);
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
            var equivalent=!primary && !upper
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
                plan==null ? (Func<BeamDistributedLoad,LoadCalculation>)null : q=>MemberLoadingView.Tributary(member,plan,SelectedLine,q),
                p=>DescribePoint(member,p));
        }
        private LoadCalculation DescribePoint(TransferMember member,BeamPointLoad point)
        {
            var t=layout?.Transfer;int raw=Array.IndexOf(member.Points,point);
            var units=t?.UnitLowerCases;bool lower=t!=null && SelectedRun>=layout.StockPlan.Pieces.Length && SelectedRun<layout.StockPlan.Pieces.Length+t.RowsMm.Length;
            if(crossSupport==null && lower && raw>=0 && units!=null && t.UnitPointLabels!=null && member.H20Count==1)
            {
                var source=Enumerable.Range(0,units.Length).Where(i=>!string.IsNullOrEmpty(units[i].LoadCase) && (member.LoadCase??"").StartsWith(units[i].LoadCase,StringComparison.Ordinal))
                    .OrderByDescending(i=>units[i].LoadCase.Length).DefaultIfEmpty(-1).First();
                if(source>=0 && source<t.UnitPointLabels.Length && raw<t.UnitPointLabels[source].Length && raw<units[source].Points.Length)
                {
                    var unit=units[source].Points[raw];
                    if(Math.Abs(unit.PositionMm-point.PositionMm)<1e-7 && Math.Abs(unit.Kn)>1e-10)
                    {
                        double width=point.Kn/unit.Kn;
                        var result=new LoadCalculation {Name=t.UnitPointLabels[source][raw]};
                        result.Lines.Add($"Unit-width reaction: {unit.Kn:0.#####} kN/m");
                        result.Lines.Add($"Actual tributary width: {width*1000:0.###} mm");
                        result.Lines.Add($"{unit.Kn:0.#####} × {width:0.#####} = {point.Kn:0.#####} kN");return result;
                    }
                }
            }
            if(network!=null && networkBeam>0 && networkBeam<=network.Beams.Length && ReferenceEquals(layout,network.Beams[networkBeam-1]) && crossSupport==null && lower && member.H20Count==1)
            {
                int row=SelectedRun-layout.StockPlan.Pieces.Length;
                foreach(var support in network.CrossSupports)foreach(var bearing in support.Bearings.Where(b=>b.Beam==networkBeam-1 && b.Row==row))
                    if(Math.Abs(bearing.Ymm-network.BeamCentresMm[networkBeam-1]-member.StartMm-point.PositionMm)<1e-7 && Math.Abs(bearing.ReactionKn-point.Kn)<1e-7)
                    {
                        var result=new LoadCalculation {Name=$"X{support.CrossingIndex+1} "+(bearing.IsShutterEndZone ? "side-shutter reaction" : "bearing reaction")};
                        result.Lines.Add($"Checked crossing reaction: {bearing.ReactionKn:0.#####} kN");return result;
                    }
            }
            return new LoadCalculation {Name="Point load "+(raw+1)};
        }
        private void AddInteractiveLoading(string caption,MemberLoadingPlot plot,double ei,string basis,Func<BeamDistributedLoad,LoadCalculation> describe=null,Func<BeamPointLoad,LoadCalculation> describePoint=null)
        {
            MemberView=new MemberLoadingView(plot,plot.LengthMm,plot.SupportPositions,plot.ReactionValues,ei,basis,describe,describePoint);
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
        internal sealed partial class LoadedResponsePlot
        {
            internal TransferMember Member { get; }
            public LoadedResponsePlot(TransferMember member, int index, int ratio)
                : this(member.LengthMm, member.SupportsMm, member.Points.Select(p => p.PositionMm).ToArray(),
                    index, ratio, index == 0 ? member.ShearLimitKn : member.MomentLimitKnM,
                    index == 0 ? member.Analysis.ShearKn : index == 1 ? member.Analysis.MomentKnM : member.Analysis.DeflectionMm,
                    member.IsWS10, (x, side) => member.Analysis.Response(x, side)[index])
            {
                Member = member;
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


