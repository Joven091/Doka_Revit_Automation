using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using EngineeringDashboard;
using MyRevitAddin.ViewModels;

namespace MyRevitAddin.Views
{
    public partial class DiagramWindow : Window
    {
        public string WindowTitle { get => Title; set => Title = value; }
        public DiagramWindow() { InitializeComponent(); }
    }

    public sealed class SlabPlotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DiagramRowItem row)) return null;
            var beam = row.Beam;
            double length = beam.Length * 1000;
            var supports = beam.Supports.Select(x => x * 1000).ToArray();
            if (row.Index == 0)
            {
                var loads = beam.Loads.Select(q => new BeamDistributedLoad
                    { StartMm = q.Start * 1000, EndMm = q.End * 1000, KnPerM = q.Load }).ToArray();
                var plot = new TowerDiagramWindow.MemberLoadingPlot(length, supports, beam.Reactions, loads, Array.Empty<BeamPointLoad>());
                return new MemberLoadingView(plot, supports, beam.Reactions, row.CaseText, null, band =>
                {
                    var zones = beam.Loads.Where(q => q.End * 1000 > band.StartMm && q.Start * 1000 < band.EndMm)
                        .Select(q => q.IsAccess ? "Access" : "Slab").Distinct();
                    return new LoadCalculation { Name = string.Join(" / ", zones) + " distributed load" };
                });
            }
            var boundaries = beam.Loads.SelectMany(q => new[] { q.Start * 1000, q.End * 1000 }).ToArray();
            return new TowerDiagramWindow.LoadedResponsePlot(length, supports, boundaries, row.Index - 1,
                (int)beam.DeflectionDenominator, row.Index == 1 ? FlexCalculatorViewModel.MAX_H20_SHEAR_KN : FlexCalculatorViewModel.MAX_H20_MOMENT_KNM,
                row.Magnitude, false, row.ResponseAt);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
