using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Read-only response plots and labelled equivalent-UDL views of optimized stock members.</summary>
    public sealed class TowerDiagramWindow : Window
    {
        internal int SelectedRun { get; private set; } = -1;
        internal int SelectedLine { get; private set; }
        public void SelectMember(int run, int line, int member)
        { SelectedRun = run; SelectedLine = line; Refresh(); }
        internal TowerBeamAnalysis Analysis { get; private set; }
        internal LoadedBeamAnalysis LoadedAnalysis { get; private set; }
        internal TransferMember SelectedTransferMember { get; private set; }
        internal TransferMember DisplayedTransferMember { get; private set; }
        internal LoadedBeamAnalysis ActualLoadedAnalysis { get; private set; }
        internal bool UseEquivalentUdlForPrimary {get;private set;}=true;
        internal bool IsEquivalentUdlView {get;private set;}
        internal MemberLoadingPlot LoadingDiagram {get;private set;}
        internal int SelectedLoadCase { get; private set; } = -1;
        private readonly TowerLayout layout;
        private readonly string beamLabel;
        private readonly StackPanel charts = new StackPanel();
        public TowerDiagramWindow(BeamCalculatorViewModel model)
        {
            beamLabel=model.IsNetworkMode ? $"B{model.SelectedNetworkBeam+1} · " : "";
            layout = model.TowerResult ?? throw new ArgumentException("Optimize the tower layout first.");
            Title = "Select H20 — shear, moment and deflection"; Width = 960; Height = 850; MinWidth=760;MinHeight=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Segoe UI");FontSize=13;
            var panel = new StackPanel { Margin = new Thickness(16), Background=Brushes.WhiteSmoke };
            panel.Children.Add(new Expander { Header = "How to read the diagrams", Content = new TextBlock { Text = (layout.Transfer != null ? "Primary members are H20 pairs: M=10 kNm, V=22 kN. Slab and lower secondary members are single H20s: M=5 kNm, V=11 kN. Use the case buttons to check beam/slab loading stages. Lower plots include upper-foot and shutter point reactions; slab plots use the marked row. Total slab movement is reported separately. " : "Each plot represents a double-H20 pair with equal load sharing: M=10 kNm, V=22 kN. ") + "Positive moment = sagging; negative deflection = downward. Deflection uses each support span or overhang. Dashed lines show permissible limits; vertical lines mark supports; the loading diagram shows applied loads and supports. Primary pairs open in an equivalent UDL view; clear its checkbox to show the actual concentrated reactions. The UDL approximation is for viewing only; layout checks, quantities and reported utilization use the actual loads. Curves are sampled; peaks use exact extrema.", TextWrapping = TextWrapping.Wrap } });
            panel.Children.Add(charts);
            Background=Brushes.WhiteSmoke;
            Content = new ScrollViewer { Content = panel, Background = Brushes.WhiteSmoke, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                }
        private void Refresh()
        {
            if (SelectedRun < 0) return;
            if (layout.Transfer != null) { RefreshTransfer(); return; }
            var piece = layout.StockPlan.Pieces[SelectedRun];
            var supports = layout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => (x - piece.StartMm) / 1000).ToArray();
            double q = SelectedLine == 0 ? layout.LeftLineLoad : layout.RightLineLoad;
            Title = beamLabel+$"P{SelectedRun + 1} · {(SelectedLine == 0 ? "Left" : "Right")} · double H20 — response diagrams";
            Analysis = TowerBeamAnalysis.Analyze(piece.StockLengthMm / 1000, supports, q, TowerOptimizer.PairEI);
            charts.Children.Clear();
            charts.Children.Add(new TextBlock { Text = $"UDL: {q:0.000} kN/m · loaded length: {piece.StockLengthMm:0.###} mm", TextWrapping = TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
            AddLoadDetails(string.Join("   ",Analysis.Reactions.Select((v,i)=>$"Support {piece.FirstSupport+i+1}: leg load {v:0.00} kN")));
            LoadingDiagram=new MemberLoadingPlot(piece.StockLengthMm,supports.Select(x=>x*1000).ToArray(),Analysis.Reactions,new[] {new BeamDistributedLoad {StartMm=0,EndMm=piece.StockLengthMm,KnPerM=q}},new BeamPointLoad[0]);
            AddChart("Loading diagram",LoadingDiagram,true);
            string[] names = { "Shear (kN)", "Moment (kNm)", "Deflection (mm)" };
            double[] peaks = { Analysis.Shear, Analysis.Moment, Analysis.DeflectionMm }, limits = { TowerOptimizer.PairShear, TowerOptimizer.PairMoment, Analysis.DeflectionRegions.Max(x => x.AllowableMm(layout.DeflectionLimitMm, layout.DeflectionDenominator)) };
            for (int i = 0; i < 3; i++)
            {
                string caption=i == 2 && layout.DeflectionDenominator > 0
                    ? $"{names[i]} · peak {peaks[i]:0.000} · L/{layout.DeflectionDenominator} · utilization {Analysis.DeflectionUtilization(layout.DeflectionLimitMm, layout.DeflectionDenominator)*100:0.0}%"
                    : $"{names[i]} · peak {peaks[i]:0.000} / permissible {limits[i]:0.000}";
                AddChart(caption,new ResponsePlot(Analysis,supports,piece.StockLengthMm/1000,i,peaks[i],limits[i],layout.DeflectionDenominator));
            }
        }
        private void RefreshTransfer()
        {
            var t=layout.Transfer; int runs=layout.StockPlan.Pieces.Length;
            if(SelectedLoadCase<0) SelectedLoadCase=t.CaseNames.Length-1;
            TransferMember m;
            bool upper=SelectedRun>=runs+t.RowsMm.Length;
            if(SelectedRun<runs) m=t.PrimaryCases[SelectedLoadCase][SelectedRun+SelectedLine*runs];
            else if(!upper) m=t.LowerCases[SelectedLoadCase][SelectedRun-runs];
            else
            {
                int encoded=SelectedRun-runs-t.RowsMm.Length,row=encoded/2,side=encoded%2;
                var original=t.UpperMembers.Single(x=>x.Name==(side==0 ? "Upper left" : "Upper right"));
                double scale=t.UpperScale(side==0,row)/(side==0 ? t.LeftUpperScales : t.RightUpperScales).Max();
                m=new TransferMember {Name=original.Name+" · T"+(row+1),LoadCase=original.LoadCase,LengthMm=original.LengthMm,SupportsMm=original.SupportsMm,Points=original.Points,
                    H20Count=original.H20Count,Uniform=original.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*scale}).ToArray(),Analysis=original.Analysis.Scale(scale)};
            }
            SelectedTransferMember=m; ActualLoadedAnalysis=m.Analysis; Analysis=null;
            bool primary=SelectedRun<runs;
            IsEquivalentUdlView=primary && UseEquivalentUdlForPrimary;
            if(IsEquivalentUdlView)m=EquivalentUniformMember(m,layout.DeflectionDenominator);
            DisplayedTransferMember=m;LoadedAnalysis=m.Analysis;
            Title=beamLabel+m.Name+(m.H20Count==1 ? " · single H20" : " · double H20")+" — response diagrams";
            charts.Children.Clear();
            if(!upper)
            {
                var choices=new WrapPanel();
                for(int c=0;c<t.CaseNames.Length;c++)
                {
                    int selected=c; var button=new Button {Content=t.CaseNames[c],Margin=new Thickness(0,8,6,8),Padding=new Thickness(8,4,8,4),FontWeight=c==SelectedLoadCase ? FontWeights.Bold : FontWeights.Normal};
                    button.Click+=(s,e)=> {SelectedLoadCase=selected;RefreshTransfer();}; choices.Children.Add(button);
                }
                charts.Children.Add(choices);
            }
            if(primary)
            {
                var view=new CheckBox {Content="Equivalent UDL view",IsChecked=UseEquivalentUdlForPrimary,Margin=new Thickness(0,4,0,6)};
                view.Checked+=(sender,args)=> {UseEquivalentUdlForPrimary=true;RefreshTransfer();};
                view.Unchecked+=(sender,args)=> {UseEquivalentUdlForPrimary=false;RefreshTransfer();};
                charts.Children.Add(view);
                if(IsEquivalentUdlView)
                {
                    double q=m.Uniform[0].KnPerM;
                    charts.Children.Add(new Border {Background=new SolidColorBrush(Color.FromRgb(255,246,219)),Padding=new Thickness(8),Margin=new Thickness(0,0,0,8),
                        Child=new TextBlock {Text=$"Equivalent UDL approximation · {q:0.000} kN/m. Actual point loads govern layout checks; the curves below are for viewing.",TextWrapping=TextWrapping.Wrap}});
                }
            }
            charts.Children.Add(new TextBlock {Text=m.LoadCase+(m.H20Count==1 ? " · load on this H20 " : " · load on this pair ")+m.Analysis.TotalLoadKn.ToString("0.00")+" kN",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4)});
            if(IsEquivalentUdlView)charts.Children.Add(new TextBlock {Text=$"Actual checked peaks: shear {ActualLoadedAnalysis.ShearKn:0.000} kN · moment {ActualLoadedAnalysis.MomentKnM:0.000} kNm · deflection {ActualLoadedAnalysis.DeflectionMm:0.000} mm. These demands govern the layout.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,4)});
            AddLoadDetails(string.Join("   ",m.Analysis.Reactions.Select((v,i)=>$"Support {i+1} @ {m.SupportsMm[i]:0.###} mm: {v:0.00} kN"))
                +(m.Points.Length==0 ? "" : "\nPoint loads from stock end: "+string.Join("; ",m.Points.Select(p=>$"{p.PositionMm:0.###} mm → {p.Kn:0.00} kN"))),
                IsEquivalentUdlView ? "Approximate UDL support reactions (view only)" : "Support reactions and point loads");
            LoadingDiagram=new MemberLoadingPlot(m.LengthMm,m.SupportsMm,m.Analysis.Reactions,m.Uniform,m.Points);
            AddChart("Loading diagram"+(IsEquivalentUdlView ? " · equivalent UDL" : " · actual loads"),LoadingDiagram,true);
            string[] names={"Shear (kN)","Moment (kNm)","Relative member deflection (mm)"};
            double[] peaks={m.Analysis.ShearKn,m.Analysis.MomentKnM,m.Analysis.DeflectionMm};
            for(int i=0;i<3;i++)
            {
                string caption=i==2 ? $"{names[i]} · peak {peaks[i]:0.000} · L/{layout.DeflectionDenominator} · utilization {m.Analysis.DeflectionUtilization*100:0.0}%" : $"{names[i]} · peak {peaks[i]:0.000} / {(i==0 ? m.ShearLimitKn : m.MomentLimitKnM):0.000}";
                AddChart((IsEquivalentUdlView ? "Equivalent UDL · " : "")+caption,new LoadedResponsePlot(m,i,layout.DeflectionDenominator));
            }
        }
        internal static TransferMember EquivalentUniformMember(TransferMember actual,int denominator)
        {
            // Preserve the selected stock's total applied load and actual supports.
            // This creates a separate display response; no optimizer member is mutated.
            double q=actual.Analysis.TotalLoadKn*1000/actual.LengthMm;
            var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=actual.LengthMm,KnPerM=q}};
            return new TransferMember {Name=actual.Name,LoadCase=actual.LoadCase,StartMm=actual.StartMm,
                LengthMm=actual.LengthMm,SupportsMm=(double[])actual.SupportsMm.Clone(),H20Count=actual.H20Count,
                Points=new BeamPointLoad[0],Uniform=uniform,
                Analysis=LoadedBeamAnalysis.Analyze(actual.LengthMm,actual.SupportsMm,uniform,null,TowerOptimizer.PairEI*actual.H20Count/2,denominator)};
        }
        private void AddLoadDetails(string text,string header="Support reactions and point loads")
        {charts.Children.Add(new Expander {Header=header,Margin=new Thickness(0,4,0,4),Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,6,4,8)}});}
        private void AddChart(string caption,FrameworkElement plot,bool loading=false)
        {
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock {Text=caption,FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(51,51,51)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)});
            panel.Children.Add(plot);
            panel.Children.Add(new TextBlock {Text=loading ? "Red: applied load · green: support reactions · dimensions in mm. Full values are available under support reactions above." : "Distance from the left stock end (mm). Dashed verticals: supports · red dashed lines: permissible limits.",FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
            charts.Children.Add(new Border {Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(224,224,224)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Margin=new Thickness(0,12,0,4),Child=panel});
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
            internal int DrawnSupportCount {get;private set;}
            internal int DrawnPointCount {get;private set;}
            internal int DrawnUniformCount {get;private set;}
            internal readonly System.Collections.Generic.List<Rect> LabelBounds=new System.Collections.Generic.List<Rect>();
            internal MemberLoadingPlot(double l,double[] s,double[] r,BeamDistributedLoad[] u,BeamPointLoad[] p)
            {length=l;supports=s;reactions=r;uniform=u;points=p;Height=215;}
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);LabelBounds.Clear();DrawnSupportCount=DrawnPointCount=DrawnUniformCount=0;
                double left=65,right=Math.Max(left+20,ActualWidth-20),beamY=85;
                Func<double,double> xx=x=>left+x/length*(right-left);
                Action<string,double,double> label=(value,x,y)=>
                {
                    var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.DarkSlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    x=Math.Max(2,Math.Min(ActualWidth-text.Width-2,x-text.Width/2));var box=new Rect(x-2,y-1,text.Width+4,text.Height+2);
                    if(LabelBounds.Any(b=>b.IntersectsWith(box)))return;
                    LabelBounds.Add(box);dc.DrawText(text,new Point(x,y));
                };
                Action<double,double,double,Brush> arrow=(x,from,to,colour)=>
                {
                    double sign=Math.Sign(to-from);var pen=new Pen(colour,1.5);dc.DrawLine(pen,new Point(x,from),new Point(x,to));
                    dc.DrawLine(pen,new Point(x,to),new Point(x-4,to-sign*7));dc.DrawLine(pen,new Point(x,to),new Point(x+4,to-sign*7));
                };
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,Height));
                dc.DrawLine(new Pen(Brushes.DarkSlateGray,4),new Point(left,beamY),new Point(right,beamY));
                for(int i=0;i<uniform.Length;i++)
                {
                    var q=uniform[i];double y=25+i*19,a=xx(q.StartMm),b=xx(q.EndMm);
                    dc.DrawLine(new Pen(Brushes.Firebrick,1),new Point(a,y+18),new Point(b,y+18));
                    int count=Math.Max(1,(int)((b-a)/45));
                    for(int k=0;k<=count;k++)arrow(a+(b-a)*k/count,y+18,beamY-5,Brushes.Firebrick);
                    label($"{q.KnPerM:0.###} kN/m",(a+b)/2,y-2);DrawnUniformCount++;
                }
                foreach(var p in points)
                {
                    double x=xx(p.PositionMm);arrow(x,48,beamY-5,Brushes.Firebrick);label($"{p.Kn:0.##} kN",x,26);DrawnPointCount++;
                }
                for(int i=0;i<supports.Length;i++)
                {
                    double x=xx(supports[i]);var triangle=new StreamGeometry();using(var c=triangle.Open()){c.BeginFigure(new Point(x,beamY+3),true,true);c.LineTo(new Point(x-7,beamY+15),true,false);c.LineTo(new Point(x+7,beamY+15),true,false);}
                    dc.DrawGeometry(Brushes.SlateGray,null,triangle);dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(x-10,beamY+19),new Point(x+10,beamY+19));
                    arrow(x,reactions[i]>=0 ? 145 : 110,reactions[i]>=0 ? 110 : 145,Brushes.SeaGreen);label($"R{i+1}: {reactions[i]:0.##} kN",x,148);DrawnSupportCount++;
                }
                var stations=new[] {0.0}.Concat(supports).Concat(new[] {length}).Distinct().OrderBy(x=>x).ToArray();
                for(int i=1;i<stations.Length;i++)
                {
                    double a=xx(stations[i-1]),b=xx(stations[i]),y=180;dc.DrawLine(new Pen(Brushes.Gray,1),new Point(a,y),new Point(b,y));
                    foreach(double x in new[] {a,b})dc.DrawLine(new Pen(Brushes.Gray,1),new Point(x,y-4),new Point(x,y+4));
                    string value=$"{stations[i]-stations[i-1]:0.###}";if(b-a>value.Length*7+8)label(value,(a+b)/2,y+5);
                }
                label("0",left,200);label($"{length:0.###} mm",right,200);
            }
        }
        private sealed class LoadedResponsePlot : FrameworkElement
        {
            readonly TransferMember member; readonly int index,denominator;
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
                for(int i=1;i<stations.Length;i++)
                {
                    double allowable=index==2 ? (stations[i]-stations[i-1])/denominator : limit;
                    foreach(double sign in new[] {-1.0,1.0}) dc.DrawLine(new Pen(Brushes.IndianRed,1){DashStyle=DashStyles.Dash},new Point(xx(stations[i-1]),yy(sign*allowable)),new Point(xx(stations[i]),yy(sign*allowable)));
                    if(index==2) label($"±{allowable:0.##}",(xx(stations[i-1])+xx(stations[i]))/2-14,yy(allowable)-14);
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
            readonly TowerBeamAnalysis analysis; readonly double[] supports; readonly double length, range, limit; readonly int index, denominator;
            public ResponsePlot(TowerBeamAnalysis a, double[] s, double l, int i, double peak, double allowable, int ratio)
            { analysis = a; denominator = ratio; supports = s; length = l; index = i; limit = allowable; range = Math.Max(.001,Math.Max(peak, allowable) * 1.15); Height = 220; }
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


