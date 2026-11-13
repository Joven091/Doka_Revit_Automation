using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Section and equivalent-UDL preview share the exact analyzed beam coordinates and slab edges.</summary>
    public sealed class SlabAccessDiagram : FrameworkElement
    {
        private FlexCalculatorViewModel model;
        public int DrawnAccessZones { get; private set; }
        public int DrawnSupports { get; private set; }
        public void SetModel(FlexCalculatorViewModel value) { model = value; InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); DrawnAccessZones = DrawnSupports = 0;
            var beam = model?.AccessPreviewAnalysis;
            if (beam == null || !model.IsCalculationValid || ActualWidth < 160 || ActualHeight < 180) return;
            double left = 24, right = ActualWidth - 24, width = right - left;
            Func<double, double> X = x => left + x / beam.Length * width;
            void Text(string value, double x, double y, Brush color = null, double size = 11, bool center = false)
            {
                var label = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), size, color ?? Brushes.DarkSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point(Math.Max(0, Math.Min(ActualWidth - label.Width, center ? x - label.Width / 2 : x)), y));
            }
            void Triangle(double x, double y)
            {
                var shape = new StreamGeometry();
                using (var path = shape.Open()) { path.BeginFigure(new Point(x, y), true, true); path.LineTo(new Point(x - 7, y + 10), true, false); path.LineTo(new Point(x + 7, y + 10), true, false); }
                dc.DrawGeometry(Brushes.SlateGray, null, shape);
            }
            void Arrow(double x, double y0, double y1, Brush color)
            {
                var pen = new Pen(color, 1.2); dc.DrawLine(pen, new Point(x, y0), new Point(x, y1));
                double sign = Math.Sign(y1 - y0);
                dc.DrawLine(pen, new Point(x - 3, y1 - sign * 5), new Point(x, y1));
                dc.DrawLine(pen, new Point(x + 3, y1 - sign * 5), new Point(x, y1));
            }
            Text("Section along " + model.AccessPreviewBeam.ToLowerInvariant(), left, 4, size: 13);
            dc.DrawRectangle(Brushes.BurlyWood, null, new Rect(left, 70, width, 4));
            var slab = beam.Loads.Single(q => !q.IsAccess);
            dc.DrawRectangle(Brushes.LightSteelBlue, new Pen(Brushes.SlateGray, .6), new Rect(X(slab.Start), 39, X(slab.End) - X(slab.Start), 31));
            Text("Slab", (X(slab.Start) + X(slab.End)) / 2, 44, center: true);
            for (int i = 0; i <= 12; i++)
                dc.DrawRectangle(Brushes.Goldenrod, new Pen(Brushes.DarkGoldenrod, .5), new Rect(left + (width - 7) * i / 12, 74, 7, 14));
            dc.DrawRectangle(Brushes.Gold, new Pen(Brushes.DarkGoldenrod, 1), new Rect(left, 88, width, 14));
            foreach (double support in beam.Supports) { Triangle(X(support), 103); DrawnSupports++; }
            Text("Cantilevers: " + (beam.LeftCantilever * 1000).ToString("0.##") + " / " + (beam.RightCantilever * 1000).ToString("0.##") + " mm (support to beam end)", left, 125, size: 10);
            Text(model.AccessPreviewBeam == "Secondary H20" ? "Secondary loading — slab and access" : "Primary loading — equivalent UDL", left, 166, size: 13);
            const double beamY = 274;
            double max = Math.Max(1e-9, beam.Loads.Max(q => q.Load));
            foreach (var q in beam.Loads)
            {
                Brush color = q.IsAccess ? Brushes.SteelBlue : Brushes.Firebrick;
                double x0 = X(q.Start), x1 = X(q.End), height = q.Load == 0 ? 0 : 12 + 46 * q.Load / max;
                dc.DrawLine(new Pen(color, 1), new Point(x0, beamY - height), new Point(x1, beamY - height));
                int count = Math.Max(2, (int)Math.Round((x1 - x0) / 30));
                if (q.Load > 0) for (int i = 0; i <= count; i++) Arrow(x0 + (x1 - x0) * i / count, beamY - height, beamY - 2, color);
                Text((q.IsAccess ? "Access" : "Slab") + "\n" + q.Load.ToString("0.###") + " kN/m", (x0 + x1) / 2, 191, color, 10, true);
                if (q.IsAccess)
                {
                    DrawnAccessZones++;
                    Text("Access " + ((q.End - q.Start) * 1000).ToString("0.##") + " mm", (x0 + x1) / 2, 17, Brushes.SteelBlue, 10, true);
                    double edge = q.Start == 0 ? x1 : x0;
                    dc.DrawLine(new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dash }, new Point(edge, 34), new Point(edge, beamY + 2));
                }
            }
            dc.DrawLine(new Pen(Brushes.DarkSlateGray, 3), new Point(left, beamY), new Point(right, beamY));
            for (int i = 0; i < beam.Supports.Length; i++)
            {
                double x = X(beam.Supports[i]); Triangle(x, beamY + 2); Arrow(x, beamY + 43, beamY + 19, Brushes.ForestGreen);
                Text($"R{i + 1}: {beam.Reactions[i]:0.00} kN", x, beamY + 46, Brushes.ForestGreen, 10, true);
            }
            Text("Red: slab loads · blue: total access loads", left, 348, size: 10);
            Text("The UDL changes at the slab edge.", left, 365, size: 10);
        }
    }
}
