using System;
using System.Collections.Generic;
using System.Linq;
using EngineeringDashboard;

namespace MyRevitAddin.ViewModels
{
    public class DiagramWindowViewModel
    {
        public List<DiagramRowItem> DiagramRows { get; } = new List<DiagramRowItem>();

        public static DiagramWindowViewModel ForAccess(SlabBeamAnalysis beam, string name, string loadCase)
        {
            var result = new DiagramWindowViewModel();
            result.AddCase(beam, $"{name} · {loadCase} · {beam.Supports.Length} actual supports");
            if (beam.Loads.Any(q => q.IsAccess))
            {
                result.DiagramRows[0].GeometryText += "\n" + string.Join(" · ", beam.Loads.Select(q =>
                    $"{(q.IsAccess ? "Access" : "Slab")} {q.Start * 1000:0.##}–{q.End * 1000:0.##} mm: {q.Load:0.###} kN/m"));
            }
            return result;
        }

        public static DiagramWindowViewModel ForSelection(int selection, double span, double cantilever, double load,
            double ei, string name, int denominator = 350)
        {
            var result = new DiagramWindowViewModel();
            var counts = selection == 5 ? new[] { 5, 6 } : new[] { selection };
            var beams = counts.Select(count => SlabBeamAnalysis.Analyze(count, span, cantilever, cantilever,
                new[] { new SlabBeamLoad { End = cantilever + (count - 1) * span + cantilever, Load = load } }, ei, denominator)).ToArray();
            foreach (var beam in beams)
            {
                var label = name + (selection == 5 ? " · 5+ check" : "") + $" · {beam.Supports.Length} supports";
                result.AddCase(beam, label);
            }
            if (selection == 5)
            {
                foreach (var row in result.DiagramRows)
                {
                    double largest = result.DiagramRows.Where(r => r.Index == row.Index).Max(r => r.Magnitude);
                    if (row.Magnitude >= largest - 1e-10 * Math.Max(1, largest))
                        row.CaseText += row.Index == 0 ? " · Governing reaction" : " · Governing magnitude";
                }
            }
            return result;
        }

        private void AddCase(SlabBeamAnalysis beam, string label)
        {
            string geometry = $"Left overhang {beam.LeftCantilever * 1000:0.##} mm · Support spans {beam.Span * 1000:0.##} mm · Right overhang {beam.RightCantilever * 1000:0.##} mm";
            string[] titles = { "Loading diagram", "Shear force", "Bending moment", "Deflection" };
            for (int index = 0; index < titles.Length; index++)
            {
                DiagramRows.Add(new DiagramRowItem
                {
                    Title = titles[index], CaseText = label, Beam = beam, Index = index,
                    GeometryText = index == 0 ? geometry + ". Click a load or reaction for details."
                        : "Distance from the left beam end. Dashed vertical lines mark supports. Red dashed lines show the permissible limits."
                            + (index == 3 ? " Negative deflection is downward." : "")
                });
            }
        }
    }
}
