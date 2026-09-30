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
        internal ComboBox RunSelector { get; } = new ComboBox { Margin = new Thickness(0, 8, 0, 8) };
        internal ComboBox LineSelector { get; } = new ComboBox { Margin = new Thickness(0, 0, 0, 8), ItemsSource = new[] { "Left primary line — double H20", "Right primary line — double H20" }, SelectedIndex = 0 };
        internal TowerBeamAnalysis Analysis { get; private set; }
        private readonly TowerLayout layout;
        private readonly StackPanel charts = new StackPanel();
        public TowerDiagramWindow(TowerLayout result)
        {
            layout = result ?? throw new ArgumentNullException(nameof(result));
            Title = "Primary H20 — shear, moment and deflection"; Width = 900; Height = 850;
            var panel = new StackPanel { Margin = new Thickness(20), Background = Brushes.White };
            panel.Children.Add(new Expander { Header = "How to read the diagrams", Content = new TextBlock { Text = "Each plot represents two H20s sharing load equally. Single-H20 shear and moment are half the plotted values; deflection is unchanged. Positive moment = sagging; negative deflection = downward. Dashed horizontal lines are permissible limits; vertical lines mark supports. Curves are sampled; peak values use exact extrema.", TextWrapping = TextWrapping.Wrap } });
            RunSelector.ItemsSource = layout.StockPlan.Pieces.Select((p, i) => $"P{i + 1}: {p.StockLengthMm:0} mm stock · concrete beam x={p.StartMm:0}–{p.EndMm:0} mm").ToArray();
            panel.Children.Add(RunSelector); panel.Children.Add(LineSelector); panel.Children.Add(charts);
            Content = new ScrollViewer { Content = panel, Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            RunSelector.SelectionChanged += (s, e) => Refresh(); LineSelector.SelectionChanged += (s, e) => Refresh();
            RunSelector.SelectedIndex = 0;
        }
        private void Refresh()
        {
            if (RunSelector.SelectedIndex < 0) return;
            var piece = layout.StockPlan.Pieces[RunSelector.SelectedIndex];
            var supports = layout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport - piece.FirstSupport + 1).Select(x => (x - piece.StartMm) / 1000).ToArray();
            double q = LineSelector.SelectedIndex == 0 ? layout.LeftLineLoad : layout.RightLineLoad;
            Analysis = TowerBeamAnalysis.Analyze(piece.StockLengthMm / 1000, supports, q, TowerOptimizer.PairEI);
            charts.Children.Clear();
            charts.Children.Add(new TextBlock { Text = $"UDL: {q:0.000} kN/m · loaded length: {piece.StockLengthMm:0.###} mm · permissible leg load: {layout.LegCapacityKn:0.00} kN\n" + string.Join("   ", Analysis.Reactions.Select((v, i) => $"Leg {piece.FirstSupport + i + 1}: {v:0.00} kN ({v / layout.LegCapacityKn * 100:0.0}%)")), TextWrapping = TextWrapping.Wrap });
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
            { analysis = a; supports = s; length = l; index = i; limit = allowable; range = Math.Max(peak, allowable) * 1.15; Height = 210; }
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
                label("Position from start of selected stock member (mm)", left, bottom + 20);
            }
        }
    }
}
