using System;
using System.Collections.ObjectModel;
using OxyPlot;
using EngineeringDashboard;
using MyRevitAddin.Models;

namespace MyRevitAddin.ViewModels
{
    public class DiagramWindowViewModel
    {
        // The View will bind to this collection
        public ObservableCollection<DiagramRowItem> DiagramRows { get; set; }

        public static DiagramWindowViewModel ForSelection(int selection, double span, double cantilever, double load, double ei, string name)
        {
            var five = ContinuousBeamResponse.Analyze(selection, span, cantilever, load, ei);
            var six = selection == 5 ? ContinuousBeamResponse.Analyze(6, span, cantilever, load, ei) : five;
            DiagramWindowViewModel result = null;
            foreach (int count in selection == 5 ? new[] { 5, 6 } : new[] { selection })
            {
                var model = new DiagramWindowViewModel(DiagramDataGenerator.GeneratePlots(count, span, cantilever, cantilever, load, ei, name));
                var current = count == 6 ? six : five;
                double[] values = { current.Reaction, current.Shear, current.Moment, Math.Max(current.SpanDeflection, current.CantileverDeflection) };
                double[] limits = { Math.Max(five.Reaction, six.Reaction), Math.Max(five.Shear, six.Shear), Math.Max(five.Moment, six.Moment), Math.Max(Math.Max(five.SpanDeflection, five.CantileverDeflection), Math.Max(six.SpanDeflection, six.CantileverDeflection)) };
                for (int i = 0; i < model.DiagramRows.Count; i++)
                {
                    var row = model.DiagramRows[i];
                    row.CaseText = name + " � " + (selection == 5 ? "5+ check � " : "") + count + " supports";
                    if (selection == 5 && values[i] >= limits[i] - 1e-10 * Math.Max(1, limits[i]))
                        row.CaseText += i == 0 ? " � Governing reaction" : " � Governing magnitude";
                    row.GeometryText = i == 0
                        ? $"Left overhang: {cantilever * 1000:0.##} mm � Equal spans: {span * 1000:0.##} mm � Right overhang: {cantilever * 1000:0.##} mm"
                        : "Dashed vertical lines mark supports. Distance is measured from the left beam end.";
                    if (i == 3) row.GeometryText += " Negative = downward; positive = upward.";
                    row.ChartModel.Title = null;
                }
                if (result == null) result = model;
                else foreach (var row in model.DiagramRows) result.DiagramRows.Add(row);
            }
            return result;
        }

        public DiagramWindowViewModel(
            (PlotModel load, PlotModel shear, PlotModel moment, PlotModel deflection,
             double maxShear, double minShear,
             double maxMoment, double minMoment,
             double maxDeflection, double minDeflection) backendData)
        {
            DiagramRows = new ObservableCollection<DiagramRowItem>
            {
                // 0. Loading Diagram Data
                new DiagramRowItem
                {
                    Title = "Loading Diagram",
                    ChartModel = backendData.load,
                    MaxValueText = "", // No max/min text for loading diagram
                    MinValueText = ""
                },

                // 1. Shear Data
                new DiagramRowItem
                {
                    Title = "Shear Force (V)",
                    ChartModel = backendData.shear,
                    MaxValueText = $"Max: {Math.Round(backendData.maxShear, 2)} kN",
                    MinValueText = $"Min: {Math.Round(backendData.minShear, 2)} kN"
                },

                // 2. Bending Moment Data
                new DiagramRowItem
                {
                    Title = "Bending Moment (M)",
                    ChartModel = backendData.moment,
                    MaxValueText = $"Max: {Math.Round(backendData.maxMoment, 2)} kNm",
                    MinValueText = $"Min: {Math.Round(backendData.minMoment, 2)} kNm"
                },

                // 3. Deflection Data
                new DiagramRowItem
                {
                    Title = "Deflection",
                    ChartModel = backendData.deflection,
                    MaxValueText = $"Maximum downward: {Math.Max(0, -backendData.minDeflection):0.00} mm",
                    MinValueText = $"Maximum upward: {Math.Max(0, backendData.maxDeflection):0.00} mm"
                }
            };
        }
    }
}

