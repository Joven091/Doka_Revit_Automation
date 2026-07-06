using System;
using System.Collections.ObjectModel;
using OxyPlot;

namespace MyRevitAddin.ViewModels
{
    public class DiagramWindowViewModel
    {
        // The View will bind to this collection
        public ObservableCollection<DiagramRowItem> DiagramRows { get; set; }

        public DiagramWindowViewModel(
            (PlotModel shear, PlotModel moment, PlotModel deflection,
             double maxShear, double minShear,
             double maxMoment, double minMoment,
             double maxDeflection, double minDeflection) backendData)
        {
            DiagramRows = new ObservableCollection<DiagramRowItem>
            {
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
                    Title = "Deflection (δ)",
                    ChartModel = backendData.deflection,
                    MaxValueText = $"Max: {Math.Round(backendData.maxDeflection, 2)} mm",
                    MinValueText = $"Min: {Math.Round(backendData.minDeflection, 2)} mm"
                }
            };
        }
    }
}
