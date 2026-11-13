using System;
using System.Linq;
using EngineeringDashboard;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;

namespace MyRevitAddin.Models
{
    public static class SlabAccessPlots
    {
        public static (PlotModel load, PlotModel shear, PlotModel moment, PlotModel deflection,
            double maxShear, double minShear, double maxMoment, double minMoment, double maxDeflection, double minDeflection)
            Generate(SlabBeamAnalysis beam)
        {
            PlotModel Make(string unit)
            {
                var p = new PlotModel { PlotMargins = new OxyThickness(65, 24, 30, 45), PlotAreaBorderThickness = new OxyThickness(0) };
                p.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance from left beam end (m)", Minimum = 0, Maximum = beam.Length });
                p.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = unit });
                return p;
            }
            var load = Make(""); var shear = Make("kN"); var moment = Make("kNm"); var deflection = Make("mm");
            load.Axes[1].Minimum = -2; load.Axes[1].Maximum = 3.3; load.Axes[1].IsAxisVisible = false;
            var baseline = new LineSeries { Color = OxyColors.DarkSlateGray, StrokeThickness = 3 };
            baseline.Points.Add(new DataPoint(0, 0)); baseline.Points.Add(new DataPoint(beam.Length, 0)); load.Series.Add(baseline);
            double maximumLoad = Math.Max(1e-9, beam.Loads.Max(q => q.Load));
            foreach (var q in beam.Loads)
            {
                var color = q.IsAccess ? OxyColor.Parse("#1261A0") : OxyColor.Parse("#C62828");
                double height = q.Load == 0 ? 0 : .45 + 1.35 * q.Load / maximumLoad;
                load.Annotations.Add(new RectangleAnnotation { MinimumX = q.Start, MaximumX = q.End, MinimumY = 0, MaximumY = height,
                    Fill = OxyColor.FromAColor(18, color), Stroke = color, StrokeThickness = 1 });
                int arrows = Math.Max(2, (int)Math.Round((q.End - q.Start) / beam.Length * 16));
                if (q.Load > 0) for (int i = 0; i <= arrows; i++)
                {
                    double x = q.Start + (q.End - q.Start) * i / arrows;
                    load.Annotations.Add(new ArrowAnnotation { StartPoint = new DataPoint(x, height), EndPoint = new DataPoint(x, 0), Color = color, StrokeThickness = 1 });
                }
                load.Annotations.Add(new TextAnnotation { Text = (q.IsAccess ? "Access" : "Slab") + $"\n{q.Load:0.###} kN/m",
                    TextPosition = new DataPoint((q.Start + q.End) / 2, 2.15), TextColor = color, Stroke = OxyColors.Transparent,
                    TextHorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 });
                if (q.Start > 0) foreach (var plot in new[] { load, shear, moment, deflection })
                    plot.Annotations.Add(new LineAnnotation { Type = LineAnnotationType.Vertical, X = q.Start, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });
            }
            var outline = new[] { new ScreenPoint(0, 0), new ScreenPoint(-1, 1.732), new ScreenPoint(1, 1.732) };
            for (int i = 0; i < beam.Supports.Length; i++)
            {
                load.Annotations.Add(new PointAnnotation { X = beam.Supports[i], Y = 0, Shape = MarkerType.Custom, CustomOutline = outline, Fill = OxyColors.SlateGray, Size = 7 });
                load.Annotations.Add(new TextAnnotation { Text = $"R{i + 1}: {beam.Reactions[i]:0.00} kN", TextPosition = new DataPoint(beam.Supports[i], -.9),
                    Stroke = OxyColors.Transparent, TextHorizontalAlignment = HorizontalAlignment.Center, TextColor = OxyColors.ForestGreen, FontSize = 10 });
                foreach (var plot in new[] { shear, moment, deflection })
                    plot.Annotations.Add(new LineAnnotation { Type = LineAnnotationType.Vertical, X = beam.Supports[i], Color = OxyColors.LightGray, LineStyle = LineStyle.Dot });
            }
            var v = new LineSeries { Color = OxyColors.SteelBlue }; var m = new LineSeries { Color = OxyColors.SteelBlue }; var y = new LineSeries { Color = OxyColors.SteelBlue };
            var samples = Enumerable.Range(0, 301).Select(i => beam.Length * i / 300).Concat(beam.Supports).Concat(beam.Loads.SelectMany(q => new[] { q.Start, q.End })).Distinct().OrderBy(x => x);
            foreach (double x in samples)
            {
                var left = beam.Response(x, false); var right = beam.Response(x);
                v.Points.Add(new DataPoint(x, left[0])); v.Points.Add(new DataPoint(x, right[0]));
                m.Points.Add(new DataPoint(x, right[1])); y.Points.Add(new DataPoint(x, -right[2] * 1000));
            }
            shear.Series.Add(v); moment.Series.Add(m); deflection.Series.Add(y);
            return (load, shear, moment, deflection, beam.MaximumShear, beam.MinimumShear, beam.MaximumMoment, beam.MinimumMoment,
                -beam.MinimumDeflection * 1000, -beam.MaximumDeflection * 1000);
        }
    }
}
