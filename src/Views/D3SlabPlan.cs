using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    public sealed class D3SlabPlan : FrameworkElement
    {
        private D3SlabLayout layout;
        public bool ShowLegLoads { get; set; }
        public int DrawnLegCount { get; private set; }
        public int DrawnPrimaryCount { get; private set; }
        public int DrawnSecondaryCount { get; private set; }
        public int DrawnDimensionCount { get; private set; }
        public void SetLayout(D3SlabLayout value)
        {
            layout = value; Width = value == null ? 700 : Math.Max(700, (value.CoveredLengthMm + 2 * value.EdgeXmm) * .12 + 340);
            Height = value == null ? 440 : Math.Max(440, (value.CoveredWidthMm + 2 * value.EdgeYmm) * .12 + 230); InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); DrawnLegCount = DrawnPrimaryCount = DrawnSecondaryCount = DrawnDimensionCount = 0;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (layout == null) return;
            double scale = .12, x0 = 90, y0 = 125;
            Func<double, double> xx = x => x0 + (x + layout.EdgeXmm) * scale, yy = y => y0 + (y + layout.EdgeYmm) * scale;
            Action<string, double, double> label = (s, x, y) => dc.DrawText(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
            Action<double, double, double, string, bool> dim = (a, b, position, text, horizontal) =>
            {
                var pen = new Pen(Brushes.SlateGray, 1); if (horizontal) { dc.DrawLine(pen, new Point(a, position), new Point(b, position)); foreach (double end in new[] { a, b }) dc.DrawLine(pen, new Point(end, position - 4), new Point(end, position + 4)); label(text, (a + b) / 2 - text.Length * 2.6, position - 17); }
                else { dc.DrawLine(pen, new Point(position, a), new Point(position, b)); foreach (double end in new[] { a, b }) dc.DrawLine(pen, new Point(position - 4, end), new Point(position + 4, end)); label(text, position - 62, (a + b) / 2 - 7); }
                DrawnDimensionCount++;
            };
            double fullX = layout.CoveredLengthMm + 2 * layout.EdgeXmm, fullY = layout.CoveredWidthMm + 2 * layout.EdgeYmm;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 239, 242)), new Pen(Brushes.LightSlateGray, 1), new Rect(x0, y0, fullX * scale, fullY * scale));
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dot }, new Rect(xx(0), yy(0), layout.CoveredLengthMm * scale, layout.CoveredWidthMm * scale));
            foreach (double row in layout.SecondaryRowsMm) for (int i = 0; i < layout.SecondaryStock.Length; i++)
                {
                    var stock = layout.SecondaryStock[i]; double x = xx(row) + (layout.SecondaryStock.Length == 1 ? 0 : i % 2 == 0 ? -2 : 2);
                    dc.DrawLine(new Pen(Brushes.Goldenrod, 2), new Point(x, yy(stock.StartMm)), new Point(x, yy(stock.EndMm)));
                    foreach (double end in new[] { stock.StartMm, stock.EndMm }) dc.DrawLine(new Pen(Brushes.DarkGoldenrod, 1), new Point(x - 3, yy(end)), new Point(x + 3, yy(end)));
                    DrawnSecondaryCount++;
                }
            for (int line = 0; line < layout.PrimaryLinesMm.Length; line++) foreach (var stock in layout.PrimaryStock)
                {
                    double y = yy(layout.PrimaryLinesMm[line]); foreach (double offset in new[] { -3.0, 3.0 }) { dc.DrawLine(new Pen(Brushes.DarkGoldenrod, 2), new Point(xx(stock.StartMm), y + offset), new Point(xx(stock.EndMm), y + offset)); DrawnPrimaryCount++; }
                    label($"P {stock.LengthMm:0}", (xx(stock.StartMm) + xx(stock.EndMm)) / 2 - 24, y + 5);
                }
            for (int band = 0; band < layout.PrimaryLinesMm.Length; band += 2)
                for (int station = 0; station < layout.StationsMm.Length; station++)
                {
                    double x = xx(layout.StationsMm[station]), a = yy(layout.PrimaryLinesMm[band]), b = yy(layout.PrimaryLinesMm[band + 1]);
                    dc.DrawLine(new Pen(Brushes.SlateGray, 1) { DashStyle = layout.RotatedTowers ? DashStyles.Dash : DashStyles.Solid }, new Point(x, a), new Point(x, b));
                    if (station < layout.Joined.Length && layout.Joined[station]) foreach (double y in new[] { a, b }) dc.DrawLine(new Pen(Brushes.SlateGray, 1) { DashStyle = layout.RotatedTowers ? DashStyles.Solid : DashStyles.Dash }, new Point(x, y), new Point(xx(layout.StationsMm[station + 1]), y));
                    for (int line = band; line <= band + 1; line++) { double y = yy(layout.PrimaryLinesMm[line]); dc.DrawEllipse(Brushes.SteelBlue, null, new Point(x, y), 4, 4); DrawnLegCount++; if (ShowLegLoads) label($"{layout.LegLoads[line, station]:0.0}", x + 5, y - 16); }
                }
            for (int i = 1; i < layout.StationsMm.Length; i++) dim(xx(layout.StationsMm[i - 1]), xx(layout.StationsMm[i]), y0 - 22, $"{(layout.Joined[i - 1] ? (layout.RotatedTowers ? "Frame" : "Cross") : "Gap")} {layout.StationsMm[i] - layout.StationsMm[i - 1]:0}", true);
            for (int i = 1; i < layout.PrimaryLinesMm.Length; i++) dim(yy(layout.PrimaryLinesMm[i - 1]), yy(layout.PrimaryLinesMm[i]), x0 - 18, $"{(i % 2 == 1 ? (layout.RotatedTowers ? "Cross" : "Frame") : "Gap")}\n{layout.PrimaryLinesMm[i] - layout.PrimaryLinesMm[i - 1]:0}", false);
            double bottom = y0 + fullY * scale;
            double right = x0 + fullX * scale;
            var first = layout.SecondaryStock.First(); var last = layout.SecondaryStock.Last();
            dim(yy(first.StartMm), yy(layout.PrimaryLinesMm[first.FirstSupport]), right + 158, $"S end\n{layout.PrimaryLinesMm[first.FirstSupport] - first.StartMm:0}", false);
            dim(yy(layout.PrimaryLinesMm[last.LastSupport]), yy(last.EndMm), right + 158, $"S end\n{last.EndMm - layout.PrimaryLinesMm[last.LastSupport]:0}", false);
            for (int i = 1; i < layout.SecondaryStock.Length; i++)
                dim(yy(layout.SecondaryStock[i].StartMm), yy(layout.SecondaryStock[i - 1].EndMm), right + 158, $"Lap\n{layout.SecondaryStock[i - 1].EndMm - layout.SecondaryStock[i].StartMm:0}", false);
            foreach (var stock in layout.PrimaryStock)
            {
                dim(xx(stock.StartMm), xx(layout.StationsMm[stock.FirstSupport]), bottom + 24, $"{layout.StationsMm[stock.FirstSupport] - stock.StartMm:0}", true);
                dim(xx(layout.StationsMm[stock.LastSupport]), xx(stock.EndMm), bottom + 24, $"{stock.EndMm - layout.StationsMm[stock.LastSupport]:0}", true);
            }
            dim(x0, x0 + fullX * scale, bottom + 61, $"Slab {(layout.SwapAxes ? "width" : "length")} {fullX:0} mm", true);
            dim(y0, y0 + fullY * scale, right + 225, $"Slab {(layout.SwapAxes ? "length" : "width")}\n{fullY:0} mm", false);
            label($"Setbacks: x {layout.EdgeXmm:0} mm · y {layout.EdgeYmm:0} mm · secondary spacing {layout.SecondarySpacingMm:0} mm", x0, 20);
            label($"Secondary stock: {string.Join(" + ", layout.SecondaryStock.Select(s => $"{s.LengthMm:0}"))} mm · overlap total {layout.OverlapMm:0} mm", x0, 39);
            label(ShowLegLoads ? "Leg labels: kN · solid frames / dashed crosses" : "Blue dots: tower legs · solid frames / dashed crosses", x0, 58);
        }
    }
}
