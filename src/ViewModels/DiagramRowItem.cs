using OxyPlot;

namespace MyRevitAddin.ViewModels
{
    // Represents a single row in the UI (e.g., just Shear, or just Moment)
    public class DiagramRowItem
    {
        public string Title { get; set; }
        public string CaseText { get; set; }
        public string GeometryText { get; set; }
        public PlotModel ChartModel { get; set; }

        // Formatted strings are best here so the View just binds and displays
        public string MaxValueText { get; set; }
        public string MinValueText { get; set; }
    }
}

