import re

path = "src/Models/DiagramDataGenerator.cs"
with open(path, 'r') as f:
    content = f.read()

# 1. Update signature
content = content.replace(
    "public static (PlotModel shearPlot, PlotModel momentPlot, PlotModel deflectionPlot, double maxShear, double minShear, double maxMoment, double minMoment, double maxDeflection, double minDeflection) GeneratePlots(",
    "public static (PlotModel loadPlot, PlotModel shearPlot, PlotModel momentPlot, PlotModel deflectionPlot, double maxShear, double minShear, double maxMoment, double minMoment, double maxDeflection, double minDeflection) GeneratePlots(")

# 2. Add loadPlot init
content = content.replace(
    "var shearPlot = new PlotModel { Title = $\"{titlePrefix} - Shear Diagram\" };",
    "var loadPlot = new PlotModel { Title = $\"{titlePrefix} - Loading Diagram\", PlotMargins = new OxyThickness(50, 20, 20, 40) };\n            var shearPlot = new PlotModel { Title = $\"{titlePrefix} - Shear Diagram\", PlotMargins = new OxyThickness(50, 20, 20, 40) };")

content = content.replace(
    "var momentPlot = new PlotModel { Title = $\"{titlePrefix} - Moment Diagram\" };",
    "var momentPlot = new PlotModel { Title = $\"{titlePrefix} - Moment Diagram\", PlotMargins = new OxyThickness(50, 20, 20, 40) };")

content = content.replace(
    "var deflectionPlot = new PlotModel { Title = $\"{titlePrefix} - Deflection Diagram\" };",
    "var deflectionPlot = new PlotModel { Title = $\"{titlePrefix} - Deflection Diagram\", PlotMargins = new OxyThickness(50, 20, 20, 40) };")


# 3. Add load plot generation code before Axes setup
load_plot_code = """

            // --- LOADING DIAGRAM ---
            // Hide axes on the loading diagram
            loadPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0, Maximum = L_total, IsAxisVisible = false });
            loadPlot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = -2, Maximum = 3, IsAxisVisible = false });

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
                MaximumY = 1,
                Fill = OxyColor.FromAColor(100, OxyColors.Orange),
                Stroke = OxyColors.Orange,
                StrokeThickness = 1
            };
            loadPlot.Annotations.Add(udlAnnotation);

            // UDL Label
            var udlLabel = new OxyPlot.Annotations.TextAnnotation
            {
                Text = $"{Math.Round(w, 2)} kN/m",
                TextPosition = new DataPoint(L_total / 2, 1.2),
                HorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                VerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
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
                    var spanLabel = new OxyPlot.Annotations.TextAnnotation
                    {
                        Text = $"{Math.Round(spanDist * 1000)}",
                        TextPosition = new DataPoint(lastPos + spanDist / 2, -1.0),
                        HorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                        VerticalAlignment = OxyPlot.VerticalAlignment.Top,
                        FontSize = 11
                    };
                    loadPlot.Annotations.Add(spanLabel);

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
                        Shape = MarkerType.Triangle,
                        Fill = OxyColors.Blue,
                        Size = 8
                    };
                    loadPlot.Annotations.Add(supportAnnotation);
                }

                lastPos = currentPos;
            }

            // Setup Axes (existing)
"""

content = content.replace("            // Setup Axes", load_plot_code)

# 4. Update return
content = content.replace(
    "return (shearPlot, momentPlot, deflectionPlot, maxShear, minShear, maxMoment, minMoment, maxDeflection, minDeflection);",
    "return (loadPlot, shearPlot, momentPlot, deflectionPlot, maxShear, minShear, maxMoment, minMoment, maxDeflection, minDeflection);")

with open(path, 'w') as f:
    f.write(content)
