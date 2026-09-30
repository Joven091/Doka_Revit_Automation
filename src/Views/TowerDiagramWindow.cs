using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Read-only response plots for the exact stock members used by the optimizer.</summary>
    public sealed class TowerDiagramWindow : Window
    {
        internal int SelectedRun { get; private set; } = -1;
        internal int SelectedLine { get; private set; }
        public void SelectMember(int run, int line, int member)
        { SelectedRun = run; SelectedLine = line; Refresh(); }
        internal TowerBeamAnalysis Analysis { get; private set; }
        private readonly TowerLayout layout;
        private readonly StackPanel charts = new StackPanel();
        public TowerDiagramWindow(BeamCalculatorViewModel model)
        {
            layout = model.TowerResult ?? throw new ArgumentException("Optimize the tower layout first.");
            Title = "Select H20 — shear, moment and deflection"; Width = 900; Height = 850;
            var panel = new StackPanel { Margin = new Thickness(20), Background = Brushes.White };
            panel.Children.Add(new Expander { Header = "How to read the diagrams", Content = new TextBlock { Text = "Each plot represents the selected double-H20 pair with equal load sharing. Pair limits: shear 22 kN and moment 10 kNm. Pair reactions equal the supporting leg loads. Lower plan dimensions run from each H20 end to its nearest support-leg centre. Positive moment = sagging; negative deflection = downward. Dashed horizontal lines are permissible limits; vertical lines mark supports. Curves are sampled; peak values use exact extrema.", TextWrapping = TextWrapping.Wrap } });
            panel.Children.Add(charts);
            Content = new ScrollViewer { Content = panel, Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                }
        private void Refresh()
        {
            if (SelectedRun < 0) return;
            var piece = layout.StockPlan.Pieces[SelectedRun];
            var supports = layout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => (x - piece.StartMm) / 1000).ToArray();
            double q = SelectedLine == 0 ? layout.LeftLineLoad : layout.RightLineLoad;
            Title = $"P{SelectedRun + 1} · {(SelectedLine == 0 ? "Left" : "Right")} · double H20 — response diagrams";
            Analysis = TowerBeamAnalysis.Analyze(piece.StockLengthMm / 1000, supports, q, TowerOptimizer.PairEI);
            charts.Children.Clear();
            charts.Children.Add(new TextBlock { Text = $"UDL: {q:0.000} kN/m · loaded length: {piece.StockLengthMm:0.###} mm\n" + string.Join("   ", Analysis.Reactions.Select((v, i) => $"Support {piece.FirstSupport + i + 1}: leg load {v:0.00} kN")), TextWrapping = TextWrapping.Wrap });
            string[] names = { "Shear (kN)", "Moment (kNm)", "Deflection (mm)" };
            double[] peaks = { Analysis.Shear, Analysis.Moment, Analysis.DeflectionMm }, limits = { TowerOptimizer.PairShear, TowerOptimizer.PairMoment, layout.DeflectionLimitMm };
            for (int i = 0; i < 3; i++)
            {
                charts.Children.Add(new TextBlock { Text = $"{names[i]} · peak {peaks[i]:0.000} / permissible {limits[i]:0.000}", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 0) });
                charts.Children.Add(new ResponsePlot(Analysis, supports, piece.StockLengthMm / 1000, i, peaks[i], limits[i]));
            }
        }
        private sealed class ResponsePlot : FrameworkElement
        {
            readonly TowerBeamAnalysis analysis; readonly double[] supports; readonly double length, range, limit; readonly int index;
            public ResponsePlot(TowerBeamAnalysis a, double[] s, double l, int i, double peak, double allowable)
            { analysis = a; supports = s; length = l; index = i; limit = allowable; range = Math.Max(peak, allowable) * 1.15; Height = 245; }
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc); double left = 65, right = Math.Max(left + 20, ActualWidth - 20), top = 20, bottom = 175;
                Func<double, double> xx = x => left + x / length * (right - left);
                Func<double, double> yy = y => (top + bottom) / 2 - y / range * (bottom - top) / 2;
                Action<string, double, double> label = (s, x, y) => dc.DrawText(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, Height));
                foreach (double value in new[] { -limit, 0.0, limit })
                { dc.DrawLine(new Pen(value == 0 ? Brushes.Gray : Brushes.IndianRed, 1) { DashStyle = value == 0 ? DashStyles.Solid : DashStyles.Dash }, new Point(left, yy(value)), new Point(right, yy(value))); label(value.ToString("0.##"), 3, yy(value) - 7); }
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
                var stations = new[] { 0.0 }.Concat(supports).Concat(new[] { length }).Distinct().ToArray();
                for (int i = 1; i < stations.Length; i++)
                {
                    double a = xx(stations[i-1]), b = xx(stations[i]), y = bottom + 34;
                    dc.DrawLine(new Pen(Brushes.Gray, 1), new Point(a,y), new Point(b,y));
                    foreach (double end in new[] {a,b}) dc.DrawLine(new Pen(Brushes.Gray,1),new Point(end,y-3),new Point(end,y+3));
                    label($"{(stations[i]-stations[i-1])*1000:0.###}", (a+b)/2-14, y+5);
                }
                label("End offsets and support spans (mm)", left, bottom + 56);
            }
        }
    }
}
