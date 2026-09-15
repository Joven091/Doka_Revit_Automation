using System;
using System.Collections.Generic;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace MyRevitAddin.Models
{
    public class DiagramDataGenerator
    {
        public static (PlotModel loadPlot, PlotModel shearPlot, PlotModel momentPlot, PlotModel deflectionPlot, double maxShear, double minShear, double maxMoment, double minMoment, double maxDeflection, double minDeflection) GeneratePlots(
            int numSupports,
            double spanLength,
            double leftCantilever,
            double rightCantilever,
            double w, // Load in kN/m
            double EI, // Flexural rigidity in kN*m^2
            string titlePrefix)
        {
var loadPlot = new PlotModel { Title = $"{titlePrefix} - Loading Diagram", PlotMargins = new OxyThickness(85, 20, 35, 55), PlotAreaBorderThickness = new OxyThickness(0) };
var shearPlot = new PlotModel { Title = $"{titlePrefix} - Shear Diagram", PlotMargins = new OxyThickness(85, 20, 35, 55), Background = OxyColor.Parse("#FFFFD9") };
var momentPlot = new PlotModel { Title = $"{titlePrefix} - Moment Diagram", PlotMargins = new OxyThickness(85, 20, 35, 55), Background = OxyColor.Parse("#DCFADC") };
var deflectionPlot = new PlotModel { Title = $"{titlePrefix} - Deflection Diagram", PlotMargins = new OxyThickness(85, 20, 35, 55), Background = OxyColor.Parse("#DAFFFF") };

            var lineCol = OxyColor.Parse("#0000FF");
            var shearSeries = new LineSeries { Title = "Shear (V)", Color = lineCol };
            var momentSeries = new LineSeries { Title = "Moment (M)", Color = lineCol };
            var deflectionSeries = new LineSeries { Title = "Deflection (Δ)", Color = lineCol };

            // A representative full-UDL equal-span beam with two end cantilevers.
            // Five-plus design envelopes are computed separately by SlabSupportEnvelope.
            double L_total = (numSupports - 1) * spanLength + leftCantilever + rightCantilever;
            int numPoints = 200 * Math.Max(1, numSupports - 1);
            double step = L_total / numPoints;

            double[] R = CalculateReactions(numSupports, spanLength, leftCantilever, rightCantilever, w);

            double[] supportPositions = new double[numSupports];
            for (int i = 0; i < numSupports; i++)
            {
                supportPositions[i] = leftCantilever + i * spanLength;
            }

            // Integration constants for deflection (Macaulay's method)
            // Boundary conditions: y(support[0]) = 0, y(support[1]) = 0
            double x_0 = supportPositions[0];
            double x_1 = supportPositions[1];

            double C1 = ((w / 24.0) * (Math.Pow(x_1, 4) - Math.Pow(x_0, 4)) - (R[0] / 6.0) * Math.Pow(spanLength, 3)) / spanLength;
            double C2 = (w / 24.0) * Math.Pow(x_0, 4) - C1 * x_0;

            double maxShear = double.MinValue;
            double minShear = double.MaxValue;
            double maxMoment = double.MinValue;
            double minMoment = double.MaxValue;
            double maxDeflection = double.MinValue;
            double minDeflection = double.MaxValue;

            var evalPoints = new List<Tuple<double, bool>>();
            for (int sample = 0; sample <= numPoints; sample++)
            {
                evalPoints.Add(new Tuple<double, bool>(L_total * sample / numPoints, true));
            }
            foreach (var sp in supportPositions)
            {
                evalPoints.Add(new Tuple<double, bool>(sp, false));
                evalPoints.Add(new Tuple<double, bool>(sp, true));
            }

            evalPoints.Sort((a, b) =>
            {
                int cmp = a.Item1.CompareTo(b.Item1);
                if (cmp == 0) return a.Item2.CompareTo(b.Item2);
                return cmp;
            });

            double previousX = -1.0;
            bool previousBool = false;

            foreach (var ep in evalPoints)
            {
                double x = ep.Item1;
                bool includeAtSupport = ep.Item2;

                if (Math.Abs(x - previousX) < 1e-9 && includeAtSupport == previousBool)
                    continue;

                previousX = x;
                previousBool = includeAtSupport;

                // Initialize uniform load terms
                double V = -w * x;
                double M = -w * Math.Pow(x, 2) / 2.0;
                double EIy_term1 = 0; // Sum of reaction terms for deflection

                for (int i = 0; i < numSupports; i++)
                {
                    double sp = supportPositions[i];
                    bool pastSupport = x > sp + 1e-7;
                    bool atSupport = Math.Abs(x - sp) <= 1e-7;

                    if (pastSupport || (atSupport && includeAtSupport))
                    {
                        // Macaulay's method for Shear: <x - a>^0
                        V += R[i];
                    }

                    if (pastSupport || atSupport)
                    {
                        // Macaulay's method for Moment: <x - a>^1
                        M += R[i] * (x - sp);
                        // Macaulay's method for Deflection: <x - a>^3
                        EIy_term1 += (R[i] / 6.0) * Math.Pow(x - sp, 3);
                    }
                }

                // Macaulay's method for Deflection continued: universal formula
                double EIy_term2 = -(w / 24.0) * Math.Pow(x, 4);
                double EIy_term3 = C1 * x + C2;
                double delta = (EIy_term1 + EIy_term2 + EIy_term3) / EI;

                double delta_mm = delta * 1000;

                shearSeries.Points.Add(new DataPoint(x, V));
                momentSeries.Points.Add(new DataPoint(x, M));
                deflectionSeries.Points.Add(new DataPoint(x, delta_mm)); // mm

                maxShear = Math.Max(maxShear, V);
                minShear = Math.Min(minShear, V);
                maxMoment = Math.Max(maxMoment, M);
                minMoment = Math.Min(minMoment, M);
                maxDeflection = Math.Max(maxDeflection, delta_mm);
                minDeflection = Math.Min(minDeflection, delta_mm);
            }

            shearPlot.Series.Add(shearSeries);
            momentPlot.Series.Add(momentSeries);
            deflectionPlot.Series.Add(deflectionSeries);

            foreach (var plot in new[] { shearPlot, momentPlot, deflectionPlot })
                foreach (double position in supportPositions)
                    plot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation {
                        Type = OxyPlot.Annotations.LineAnnotationType.Vertical, X = position,
                        Color = OxyColors.Gray, LineStyle = LineStyle.Dash, StrokeThickness = 1,
                        Layer = OxyPlot.Annotations.AnnotationLayer.BelowSeries });

            // Add zero lines
            shearPlot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });
            momentPlot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });
            deflectionPlot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });

            // Add bearing annotations
            var supportOutline = new ScreenPoint[] { new ScreenPoint(0, 0), new ScreenPoint(-1, 1.732), new ScreenPoint(1, 1.732) };



            // --- LOADING DIAGRAM ---
            // Hide axes on the loading diagram
            loadPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = -0.06 * L_total, Maximum = 1.06 * L_total, IsAxisVisible = false });
            loadPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = -6, Maximum = 5, IsAxisVisible = false });

            // Beam line
            var beamSeries = new LineSeries { Color = OxyColors.Black, StrokeThickness = 3 };
            beamSeries.Points.Add(new DataPoint(0, 0));
            beamSeries.Points.Add(new DataPoint(L_total, 0));
            loadPlot.Series.Add(beamSeries);

            // UDL Rectangle
            var udlAnnotation = new OxyPlot.Annotations.RectangleAnnotation
            {
                MinimumX = 0,
                MaximumX = L_total,
                MinimumY = 0,
                MaximumY = 2,
                Fill = OxyColor.FromAColor(100, OxyColors.Orange),
                Stroke = OxyColors.Orange,
                StrokeThickness = 1
            };
            loadPlot.Annotations.Add(udlAnnotation);

            // UDL Label
            var udlLabel = new OxyPlot.Annotations.TextAnnotation
            {
                Text = $"{Math.Round(w, 2)} kN/m",
                TextPosition = new DataPoint(L_total / 2, 2.7),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                FontSize = 12
            };
            loadPlot.Annotations.Add(udlLabel);

            // Ticks and Spans
            double lastPos = 0;
            for (int i = 0; i <= supportPositions.Length; i++)
            {
                double currentPos = i < supportPositions.Length ? supportPositions[i] : L_total;

                // Only draw span if it's > 0 (avoid zero-length left/right cantilevers)
                double spanDist = currentPos - lastPos;
                if (spanDist > 1e-6)
                {
                    // Tick line at end of span (if not the very end, or if we want one at the end)
                    if (i < supportPositions.Length)
                    {
                        var tick = new LineSeries { Color = OxyColors.Gray, StrokeThickness = 1 };
                        tick.Points.Add(new DataPoint(currentPos, -0.2));
                        tick.Points.Add(new DataPoint(currentPos, -1.5));
                        loadPlot.Series.Add(tick);
                    }
                }

                if (i == 0 && spanDist > 1e-6)
                {
                    // Start tick if there was a left cantilever
                    var startTick = new LineSeries { Color = OxyColors.Gray, StrokeThickness = 1 };
                    startTick.Points.Add(new DataPoint(0, -0.2));
                    startTick.Points.Add(new DataPoint(0, -1.5));
                    loadPlot.Series.Add(startTick);
                }

                if (i == supportPositions.Length && spanDist > 1e-6)
                {
                    // End tick if there was a right cantilever
                    var endTick = new LineSeries { Color = OxyColors.Gray, StrokeThickness = 1 };
                    endTick.Points.Add(new DataPoint(L_total, -0.2));
                    endTick.Points.Add(new DataPoint(L_total, -1.5));
                    loadPlot.Series.Add(endTick);
                }

                if (i < supportPositions.Length)
                {
                     // Support Triangle
                    var supportAnnotation = new OxyPlot.Annotations.PointAnnotation
                    {
                        X = currentPos,
                        Y = 0,
                        Shape = MarkerType.Custom,
                        CustomOutline = supportOutline,
                        Fill = OxyColors.Blue,
                        Size = 8
                    };
                    loadPlot.Annotations.Add(supportAnnotation);

                    // Reaction Text (below vertical tick line)
                    var reactionLabel = new OxyPlot.Annotations.TextAnnotation
                    {
                        Text = $"S{i + 1}\n{R[i]:0.00} kN",
                        TextPosition = new DataPoint(currentPos, -1.5),
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                        TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
                        FontSize = 12,
                        TextColor = OxyColors.Black, Stroke = OxyColors.Transparent
                    };
                    loadPlot.Annotations.Add(reactionLabel);
                }

                lastPos = currentPos;
            }

            // Setup Axes (existing)

            // Dynamic scaling and capacity envelopes
            double shearCapacity = 11.0;
            double momentCapacity = 5.0;

            double actualMaxShear = Math.Max(Math.Abs(maxShear), Math.Abs(minShear));
            double shearLimit = Math.Max(actualMaxShear, shearCapacity);

            double actualMaxMoment = Math.Max(Math.Abs(maxMoment), Math.Abs(minMoment));
            double momentLimit = Math.Max(actualMaxMoment, momentCapacity);

            double actualMaxDeflection = Math.Max(Math.Abs(maxDeflection), Math.Abs(minDeflection));
            double deflectionLimit = Math.Max(actualMaxDeflection, 1.0); // Set minimum limit to 1.0 to prevent 0 scale

            shearPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)", Minimum = -0.06 * L_total, Maximum = 1.06 * L_total });
            shearPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Shear (kN)", Minimum = -shearLimit, Maximum = shearLimit });

            momentPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)", Minimum = -0.06 * L_total, Maximum = 1.06 * L_total });
            momentPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Moment (kNm)", Minimum = -momentLimit, Maximum = momentLimit });

            deflectionPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)", Minimum = -0.06 * L_total, Maximum = 1.06 * L_total });
            deflectionPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Deflection (mm)", Minimum = -deflectionLimit, Maximum = deflectionLimit });

            return (loadPlot, shearPlot, momentPlot, deflectionPlot, maxShear, minShear, maxMoment, minMoment, maxDeflection, minDeflection);
        }

        private static double[] CalculateReactions(int numSupports, double L, double aL, double aR, double w)
        {
            var moments = EngineeringDashboard.ContinuousBeamResponse.SupportMoments(numSupports, L, aL, aR, w);
            return EngineeringDashboard.ContinuousBeamResponse.SupportReactions(moments, L, aL, aR, w);
        }
    }
}
