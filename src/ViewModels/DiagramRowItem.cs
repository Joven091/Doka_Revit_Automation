using System;
using EngineeringDashboard;

namespace MyRevitAddin.ViewModels
{
    public class DiagramRowItem
    {
        public string Title { get; set; }
        public string CaseText { get; set; }
        public string GeometryText { get; set; }
        public SlabBeamAnalysis Beam { get; set; }
        public int Index { get; set; }

        public double Magnitude => Index == 0 ? Beam.Reaction : Index == 1 ? Beam.Shear : Index == 2 ? Beam.Moment
            : Math.Max(Beam.MaximumDeflection, -Beam.MinimumDeflection) * 1000;
        public string MaxValueText => Index == 0 ? "" : Index == 1 ? $"Max: {Beam.MaximumShear:0.00} kN"
            : Index == 2 ? $"Max: {Beam.MaximumMoment:0.00} kNm" : $"Maximum downward: {Beam.MaximumDeflection * 1000:0.00} mm";
        public string MinValueText => Index == 0 ? "" : Index == 1 ? $"Min: {Beam.MinimumShear:0.00} kN"
            : Index == 2 ? $"Min: {Beam.MinimumMoment:0.00} kNm" : $"Maximum upward: {-Beam.MinimumDeflection * 1000:0.00} mm";

        internal double ResponseAt(double positionMm, bool rightSide)
        {
            var values = Beam.Response(positionMm / 1000, rightSide);
            return Index == 3 ? -1000 * values[2] : values[Index - 1];
        }
    }
}
