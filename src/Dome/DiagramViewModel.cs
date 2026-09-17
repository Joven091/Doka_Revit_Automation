#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace EngineeringDashboard.Dome;

public sealed record SupportReaction(string Name, double Position, double Force)
{
    public string PositionText => $"x = {Position:0.###} m";
    public string ForceText => $"↑ {Math.Max(0, Force):0.###} kN";
}

public sealed class DiagramViewModel : ViewModelBase
{
    private static readonly OxyColor Positive = OxyColor.Parse("#2563EB");
    private static readonly OxyColor Negative = OxyColor.Parse("#B45309");
    private static readonly OxyColor Muted = OxyColor.Parse("#64748B");
    private bool showCapacityLimits = true;

    public PlotModel ShearPlot { get; } = CreateBaseModel("Shear (kN)");
    public PlotModel MomentPlot { get; } = CreateBaseModel("Moment (kNm)");
    public PlotModel DeflectionPlot { get; } = CreateBaseModel("Deflection (mm)");
    public PlotModel LoadPlot { get; } = CreateBaseModel("");
    public DesignSelection Selection { get; }
    public bool HasResult => Selection.Result is not null;
    public bool IsDesignValid => Selection.Passed;
    public string StatusText => Selection.Status;
    public string OptimalSpacingsText { get; } = "Unavailable";
    public string SecondaryBeamSpacingText { get; } = "Unavailable";
    public string ShearSummary { get; } = "Unavailable";
    public string MomentSummary { get; } = "Unavailable";
    public string DeflectionSummary { get; } = "Unavailable";
    public string LoadSummary { get; } = "Unavailable";
    public IReadOnlyList<SupportReaction> SupportReactions { get; } = Array.Empty<SupportReaction>();
    public string ReactionsText => string.Join(Environment.NewLine, SupportReactions.Select(r => $"{r.Name}: {r.PositionText}; {r.ForceText}"));
    public string ScopeText => "Uniform downward load; constant EI; bearing-only supports; symmetric overhangs. Limits: 11 kN, 5 kNm and a legacy absolute 5 mm deflection limit. Product/design basis must be confirmed. Primary beams, decking, props, bearing capacity and connections are not checked.";

    public bool ShowCapacityLimits
    {
        get => showCapacityLimits;
        set
        {
            if (!SetProperty(ref showCapacityLimits, value) || Selection.Result is not { } result) return;
            ScaleResponse(ShearPlot, result, result.GoverningShear.Magnitude, BeamDesign.ShearLimit, "kN");
            ScaleResponse(MomentPlot, result, result.GoverningMoment.Magnitude, BeamDesign.MomentLimit, "kNm");
            ScaleResponse(DeflectionPlot, result, result.GoverningDeflection.Magnitude, BeamDesign.DeflectionLimit, "mm");
        }
    }

    public DiagramViewModel(double pressure, double length)
    {
        Selection = BeamDesign.Select(pressure, length);
        if (Selection.Result is not { } result) return;
        double c = (length - Selection.Spans.Sum()) / 2;
        OptimalSpacingsText = $"{c * 1000:0.###} (c) + " + string.Join(" + ", Selection.Spans.Select(s => $"{s * 1000:0.###}")) + $" + {c * 1000:0.###} (c)";
        SecondaryBeamSpacingText = $"{Selection.SecondarySpacing * 1000:0.###} mm";
        ShearSummary = Summary(result.GoverningShear, BeamDesign.ShearLimit, "kN");
        MomentSummary = Summary(result.GoverningMoment, BeamDesign.MomentLimit, "kNm");
        DeflectionSummary = Summary(result.GoverningDeflection, BeamDesign.DeflectionLimit, "mm");
        LoadSummary = $"UDL {result.W:0.###} kN/m ↓  •  Reactions in kN ↑  •  Length {length:0.###} m";
        SupportReactions = result.Reactions.Select((force, i) => new SupportReaction(((char)('A' + i)).ToString(), result.SupportLocations[i], force)).ToArray();

        foreach (var model in new[] { LoadPlot, ShearPlot, MomentPlot, DeflectionPlot })
        {
            var axis = model.Axes.Single(a => a.Position == AxisPosition.Bottom);
            axis.Minimum = -length * 0.06;
            axis.Maximum = length * 1.06;
        }
        PlotLoading(result);
        PlotResponse(ShearPlot, result, (s, x) => s.Shear(x), result.MaximumShear, result.MinimumShear, BeamDesign.ShearLimit, "kN");
        PlotResponse(MomentPlot, result, (s, x) => s.Moment(x), result.MaximumMoment, result.MinimumMoment, BeamDesign.MomentLimit, "kNm");
        PlotResponse(DeflectionPlot, result, (s, x) => s.Deflection(x), result.MaximumDeflection, result.MinimumDeflection, BeamDesign.DeflectionLimit, "mm");
    }

    private static string Summary(Demand demand, double limit, string unit) =>
        $"{demand.Magnitude:0.###} {unit} / ±{limit:0.###} {unit} limit ({demand.Magnitude / limit:P1}) • x = {demand.Position:0.###} m";

    private static PlotModel CreateBaseModel(string verticalTitle)
    {
        var model = new PlotModel
        {
            DefaultFont = "Segoe UI", DefaultFontSize = 10,
            PlotMargins = new OxyThickness(56, 20, 12, 38), Padding = new OxyThickness(0),
            PlotAreaBorderColor = OxyColor.Parse("#CBD5E1")
        };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Title = "Distance from left end (m)", TitleFontSize = 10,
            IsZoomEnabled = false, IsPanEnabled = false, MinimumPadding = 0, MaximumPadding = 0
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = verticalTitle, TitleFontSize = 10,
            IsZoomEnabled = false, IsPanEnabled = false, MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.Parse("#E2E8F0")
        });
        return model;
    }

    // Exact ends, support-side jumps and critical points are retained. Samples never determine acceptance.
    private static List<DataPoint> Sample(BeamResult result, Func<BeamSegment, double, double> evaluate)
    {
        var points = new List<DataPoint>();
        foreach (var segment in result.Segments)
        {
            int intervals = Math.Max(1, (int)Math.Ceiling(segment.Length / 0.01));
            var locations = Enumerable.Range(0, intervals + 1).Select(i => i == intervals ? segment.Length : segment.Length * i / intervals)
                .Concat(segment.MomentLocations()).Concat(segment.DeflectionLocations()).Distinct().OrderBy(value => value);
            points.AddRange(locations.Select(x => new DataPoint(segment.Start + x, evaluate(segment, x))));
        }
        return points;
    }

    private void PlotResponse(PlotModel model, BeamResult result, Func<BeamSegment, double, double> evaluate,
        Demand maximum, Demand minimum, double limit, string unit)
    {
        var series = new TwoColorAreaSeries
        {
            Color = Positive, Color2 = Negative,
            Fill = OxyColor.FromAColor(45, Positive), Fill2 = OxyColor.FromAColor(45, Negative),
            Limit = 0, StrokeThickness = 1.8,
            TrackerFormatString = "x = {2:0.###} m\nValue = {4:0.###} " + unit
        };
        series.Points.AddRange(Sample(result, evaluate));
        model.Series.Add(series);
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal, Y = 0, Color = OxyColor.Parse("#334155"), StrokeThickness = 1, LineStyle = LineStyle.Solid
        });
        foreach (var support in SupportReactions)
            model.Annotations.Add(new LineAnnotation
            {
                Tag = "support-guide", Type = LineAnnotationType.Vertical, X = support.Position,
                Color = OxyColor.Parse("#94A3B8"), LineStyle = LineStyle.Dot, StrokeThickness = 1, Layer = AnnotationLayer.BelowSeries
            });
        if (Math.Max(maximum.Magnitude, minimum.Magnitude) < 1e-12)
            AddExtreme(model, new Demand(0, 0), "zero", result.TotalLength, unit);
        else
        {
            AddExtreme(model, maximum, "max", result.TotalLength, unit);
            AddExtreme(model, minimum, "min", result.TotalLength, unit);
        }
        ScaleResponse(model, result, Math.Max(maximum.Magnitude, minimum.Magnitude), limit, unit);
    }

    private static void AddExtreme(PlotModel model, Demand demand, string name, double length, string unit)
    {
        var color = demand.Value < 0 ? Negative : Positive;
        var marker = new PointAnnotation
        {
            Tag = "extreme:" + name, X = demand.Position, Y = demand.Value,
            Shape = MarkerType.Circle, Size = 3.2, Fill = color, Stroke = OxyColors.White, StrokeThickness = 1
        };
        model.Annotations.Add(marker);
        bool leftHalf = demand.Position <= length / 2;
        bool above = name == "min";
        model.Annotations.Add(new TextAnnotation
        {
            Tag = "extreme-label:" + name,
            Text = $"{name} {demand.Value:+0.###;-0.###;0} {unit}",
            TextPosition = new DataPoint(demand.Position, demand.Value),
            Offset = new ScreenVector(leftHalf ? 7 : -7, above ? -9 : 9),
            TextHorizontalAlignment = leftHalf ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            TextVerticalAlignment = above ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            FontSize = 10, TextColor = color, Background = OxyColor.FromAColor(230, OxyColors.White),
            Stroke = OxyColors.Transparent, Padding = new OxyThickness(2)
        });
    }

    private void ScaleResponse(PlotModel model, BeamResult result, double demand, double limit, string unit)
    {
        double extent = ShowCapacityLimits ? Math.Max(demand, limit) : demand > 1e-12 ? demand : 1;
        var yAxis = model.Axes.Single(a => a.Position == AxisPosition.Left);
        yAxis.Minimum = -extent * 1.5;
        yAxis.Maximum = extent * 1.5;
        foreach (var annotation in model.Annotations.Where(a => Equals(a.Tag, "capacity") || Equals(a.Tag, "support-label")).ToArray())
            model.Annotations.Remove(annotation);
        foreach (var support in SupportReactions)
            model.Annotations.Add(new TextAnnotation
            {
                Tag = "support-label", Text = support.Name,
                TextPosition = new DataPoint(support.Position, extent * 1.25),
                TextVerticalAlignment = VerticalAlignment.Top, FontSize = 10,
                FontWeight = FontWeights.Bold, TextColor = Muted, Stroke = OxyColors.Transparent
            });
        if (ShowCapacityLimits)
        {
            foreach (double sign in new[] { -1.0, 1.0 })
                model.Annotations.Add(new LineAnnotation
                {
                    Tag = "capacity", Type = LineAnnotationType.Horizontal, Y = sign * limit,
                    Color = OxyColor.Parse("#BE123C"), TextColor = OxyColor.Parse("#9F1239"),
                    LineStyle = LineStyle.Dash, StrokeThickness = 1, FontSize = 9
                });
        }
        model.InvalidatePlot(false);
    }

    private void PlotLoading(BeamResult result)
    {
        // Vertical positions are schematic; horizontal positions are the actual beam coordinates.
        var yAxis = LoadPlot.Axes.Single(a => a.Position == AxisPosition.Left);
        yAxis.IsAxisVisible = false; yAxis.Minimum = -1.9; yAxis.Maximum = 1.6;
        LoadPlot.PlotAreaBorderThickness = new OxyThickness(0);
        var load = new AreaSeries { Color = Positive, Fill = OxyColor.FromAColor(30, Positive), StrokeThickness = 1 };
        // Zero load has no force arrows or shaded load block.
        if (result.W > 0)
        {
            load.Points.AddRange(new[] { new DataPoint(0, 1.0), new DataPoint(result.TotalLength, 1.0) });
            load.Points2.AddRange(new[] { new DataPoint(0, .12), new DataPoint(result.TotalLength, .12) });
            LoadPlot.Series.Add(load);
            for (int i = 0; i <= 8; i++)
            {
                double x = result.TotalLength * i / 8;
                LoadPlot.Annotations.Add(new ArrowAnnotation
                {
                    Tag = "load-arrow", StartPoint = new DataPoint(x, .95), EndPoint = new DataPoint(x, .15),
                    Color = Positive, StrokeThickness = 1, HeadLength = 4, HeadWidth = 3
                });
            }
        }
        var beam = new PolygonAnnotation { Fill = OxyColor.Parse("#FDE68A"), Stroke = OxyColor.Parse("#92400E"), StrokeThickness = 1 };
        beam.Points.AddRange(new[] { new DataPoint(0, 0), new DataPoint(result.TotalLength, 0), new DataPoint(result.TotalLength, -.14), new DataPoint(0, -.14) });
        LoadPlot.Annotations.Add(beam);
        for (int i = 0; i < SupportReactions.Count; i++)
        {
            var support = SupportReactions[i];
            double halfWidth = result.TotalLength * .011;
            var triangle = new PolygonAnnotation { Fill = OxyColor.Parse("#475569"), Stroke = OxyColors.Transparent };
            triangle.Points.AddRange(new[] { new DataPoint(support.Position, -.14), new DataPoint(support.Position - halfWidth, -.4), new DataPoint(support.Position + halfWidth, -.4) });
            LoadPlot.Annotations.Add(triangle);
            if (support.Force > 1e-9)
                LoadPlot.Annotations.Add(new ArrowAnnotation
                {
                    Tag = "reaction-arrow:" + support.Name, StartPoint = new DataPoint(support.Position, -.86),
                    EndPoint = new DataPoint(support.Position, -.42), Color = OxyColor.Parse("#047857"),
                    StrokeThickness = 1.5, HeadLength = 4, HeadWidth = 3
                });
            // Evenly spaced labels with leader lines stay readable when supports are close together.
            double labelX = result.TotalLength * (i + .5) / SupportReactions.Count;
            var leader = new LineSeries { Color = Muted, StrokeThickness = .7, LineStyle = LineStyle.Dot };
            leader.Points.Add(new DataPoint(support.Position, -.88)); leader.Points.Add(new DataPoint(labelX, -1.05));
            LoadPlot.Series.Add(leader);
            LoadPlot.Annotations.Add(new TextAnnotation
            {
                Tag = "reaction-label:" + support.Name, Text = $"{support.Name}\n↑ {Math.Max(0, support.Force):0.###} kN",
                TextPosition = new DataPoint(labelX, -1.23), FontSize = 10, FontWeight = FontWeights.Bold,
                TextColor = OxyColor.Parse("#047857"), Stroke = OxyColors.Transparent
            });
        }
        LoadPlot.InvalidatePlot(true);
    }
}
