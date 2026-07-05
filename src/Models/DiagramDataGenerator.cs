using System;
using System.Collections.Generic;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace MyRevitAddin.Models
{
    public class DiagramDataGenerator
    {
        public static (PlotModel shearPlot, PlotModel momentPlot, PlotModel deflectionPlot) GeneratePlots(
            int numSupports,
            double spanLength,
            double cantileverLength,
            double w, // Load in kN/m
            double EI, // Flexural rigidity in kN*m^2
            string titlePrefix)
        {
            var shearPlot = new PlotModel { Title = $"{titlePrefix} - Shear Diagram" };
            var momentPlot = new PlotModel { Title = $"{titlePrefix} - Moment Diagram" };
            var deflectionPlot = new PlotModel { Title = $"{titlePrefix} - Deflection Diagram" };

            var shearSeries = new LineSeries { Title = "Shear (V)", Color = OxyColors.Red };
            var momentSeries = new LineSeries { Title = "Moment (M)", Color = OxyColors.Blue };
            var deflectionSeries = new LineSeries { Title = "Deflection (Δ)", Color = OxyColors.Green };

            // For supports <= 2 (single span + cantilever)
            // L_total = spanLength + cantileverLength
            // For continuous beams (numSupports > 2), this gets more complex,
            // but we can generate an approximate envelope or specific span analysis.
            // Based on the FlexCalculatorViewModel multipliers, it seems we are dealing with a standard
            // envelope analysis for identical continuous spans of length L with one cantilever a.

            // To provide a continuous visual, let's plot a single representative span + cantilever
            // as this is what is analyzed mathematically in the ViewModel.
            // X = 0 is the start of the span, X = L is the support, X = L + a is the end of the cantilever.

            double L_total = spanLength + cantileverLength;
            int numPoints = 200; // Resolution of the graph
            double step = L_total / numPoints;

            // Simple approximation of continuous beam effects for the plot:
            // We'll compute the reactions first based on the multipliers used in ViewModel.
            // Let's use the 2-support case as the visual baseline (Span + Cantilever) for simplicity in visualization,
            // or we could do the exact math for 3 supports.
            // Since the user is asking for continuous data points, let's implement a robust analysis for a continuous beam
            // with 2, 3, or 4+ supports.

            // To be precise and match the multipliers, let's calculate exact reactions and moments at supports.
            // We assume a uniform load w over the entire length.

            // Let's model a beam with 'N' spans and 1 cantilever on the right.
            // However, the FlexCalculatorViewModel analyzes multipliers (which are envelopes), not a single static load case.
            // For the sake of the graph, we will plot the *worst-case* envelope or a representative static load case.
            // A static load case is more intuitive for a single graph.
            // We will plot the static load case of all spans loaded.

            double[] R = CalculateReactions(numSupports, spanLength, cantileverLength, w);

            double[] supportPositions = new double[numSupports];
            for (int i = 0; i < numSupports; i++)
            {
                supportPositions[i] = i * spanLength;
            }

            for (double x = 0; x <= L_total + 0.0001; x += step)
            {
                double V = 0;
                double M = 0;

                // V(x) = sum(R_i for pos_i < x) - w * x
                // M(x) = sum(R_i * (x - pos_i) for pos_i < x) - w * x^2 / 2

                V = -w * x;
                M = -w * x * x / 2.0;

                for (int i = 0; i < numSupports; i++)
                {
                    if (x > supportPositions[i] || Math.Abs(x - supportPositions[i]) < 1e-6) // Include reaction if past or at support
                    {
                        V += R[i];
                        if (x > supportPositions[i])
                        {
                            M += R[i] * (x - supportPositions[i]);
                        }
                    }
                }

                shearSeries.Points.Add(new DataPoint(x, V));
                momentSeries.Points.Add(new DataPoint(x, M));

                // Deflection is much more complex for continuous beams (requires integration).
                // For demonstration, we will use a simplified approximation or just the exact shape for 2-support case.
                double delta = CalculateDeflection(x, numSupports, spanLength, cantileverLength, w, EI, R, supportPositions);
                deflectionSeries.Points.Add(new DataPoint(x, delta * 1000)); // mm
            }

            shearPlot.Series.Add(shearSeries);
            momentPlot.Series.Add(momentSeries);
            deflectionPlot.Series.Add(deflectionSeries);

            // Add zero lines
            shearPlot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });
            momentPlot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });
            deflectionPlot.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.Gray, LineStyle = LineStyle.Dash });

            // Setup Axes
            shearPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)", Minimum = 0, Maximum = L_total });
            shearPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Shear (kN)" });

            momentPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)", Minimum = 0, Maximum = L_total });
            momentPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Moment (kNm)" });

            deflectionPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)", Minimum = 0, Maximum = L_total });
            deflectionPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Deflection (mm)" });

            return (shearPlot, momentPlot, deflectionPlot);
        }

        private static double[] CalculateReactions(int numSupports, double L, double a, double w)
        {
            double[] R = new double[numSupports];

            if (numSupports <= 2)
            {
                // 1 span, 2 supports, 1 cantilever
                // Sum M at support 0: R[1]*L - w*(L+a)^2 / 2 = 0 => R[1] = w*(L+a)^2 / (2L)
                R[1] = (w * Math.Pow(L + a, 2) / 2.0) / L;
                R[0] = w * (L + a) - R[1];
            }
            else if (numSupports == 3)
            {
                // 2 spans, 3 supports, 1 cantilever
                // Using three-moment equation or standard continuous beam tables for all spans loaded
                // M0 = 0
                // M2 = -w*a^2 / 2 (moment at right support due to cantilever)
                // M0*L + 2*M1*(L+L) + M2*L = -w*L^3/4 - w*L^3/4  (assuming equal I)
                // 4*L*M1 = -w*L^3/2 - (-w*a^2/2)*L
                // M1 = -w*L^2/8 + w*a^2/8
                double M1 = -w * L * L / 8.0 + w * a * a / 8.0;
                double M2 = -w * a * a / 2.0;

                // Reactions
                // Span 0 to 1
                R[0] = w * L / 2.0 + M1 / L;
                // Span 1 to 2 + cantilever
                double R1_left = w * L / 2.0 - M1 / L;
                double R1_right = w * L / 2.0 + (M2 - M1) / L;
                R[1] = R1_left + R1_right;
                R[2] = w * L / 2.0 - (M2 - M1) / L + w * a;
            }
            else // 4+ supports (approximate as 4 supports for visual, 3 spans + 1 cantilever)
            {
                // 3 spans, 4 supports, 1 cantilever
                // M0 = 0, M3 = -w*a^2/2
                // Eq1: 4*L*M1 + L*M2 = -w*L^3/2
                // Eq2: L*M1 + 4*L*M2 = -w*L^3/2 - (-w*a^2/2)*L

                // 4*M1 + M2 = -w*L^2/2
                // M1 + 4*M2 = -w*L^2/2 + w*a^2/2

                double C1 = -w * L * L / 2.0;
                double C2 = -w * L * L / 2.0 + w * a * a / 2.0;

                // 15*M2 = 4*C2 - C1
                double M2 = (4 * C2 - C1) / 15.0;
                double M1 = (C1 - M2) / 4.0;
                double M3 = -w * a * a / 2.0;

                R[0] = w * L / 2.0 + M1 / L;
                R[1] = (w * L / 2.0 - M1 / L) + (w * L / 2.0 + (M2 - M1) / L);
                R[2] = (w * L / 2.0 - (M2 - M1) / L) + (w * L / 2.0 + (M3 - M2) / L);
                R[3] = (w * L / 2.0 - (M3 - M2) / L) + w * a;
            }

            return R;
        }

        private static double CalculateDeflection(double x, int numSupports, double L, double a, double w, double EI, double[] R, double[] supportPositions)
        {
            // Macaulay's method for deflection
            // EI * d2y/dx2 = M(x) = sum(R_i * <x - pos_i>^1) - w/2 * <x>^2
            // EI * dy/dx = sum(R_i/2 * <x - pos_i>^2) - w/6 * <x>^3 + C1
            // EI * y = sum(R_i/6 * <x - pos_i>^3) - w/24 * <x>^4 + C1*x + C2

            // Boundary conditions: y(pos_i) = 0
            // Find C1, C2 using the first span (0 to L)
            // y(0) = 0 => C2 = 0
            // y(L) = 0 => R[0]/6 * L^3 - w/24 * L^4 + C1*L = 0
            // C1 = w*L^3 / 24 - R[0]*L^2 / 6

            double C2 = 0;
            double C1 = (w * Math.Pow(L, 3) / 24.0) - (R[0] * Math.Pow(L, 2) / 6.0);

            // Now evaluate deflection at x for the entire beam
            // Note: C1 and C2 only perfectly apply to the first span.
            // For continuous beams with multiple spans, each span has its own integration constants
            // if we don't use a continuous Macaulay function.
            // Actually, Macaulay's method applies to the whole beam if all loads are continuous!
            // Let's use the universal Macaulay formula:

            double term1 = 0;
            for (int i = 0; i < numSupports; i++)
            {
                if (x > supportPositions[i])
                {
                    term1 += (R[i] / 6.0) * Math.Pow(x - supportPositions[i], 3);
                }
            }

            double term2 = -(w / 24.0) * Math.Pow(x, 4);
            double term3 = C1 * x + C2;

            double EIy = term1 + term2 + term3;

            return EIy / EI;
        }
    }
}
