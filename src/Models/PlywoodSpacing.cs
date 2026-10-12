using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class PlywoodChartDomain
    {
        public string Reference { get; }
        public double MinimumSpanMm { get; }
        public double MaximumSpanMm { get; }
        public double MinimumPressureKnM2 { get; }
        public double MaximumPressureKnM2 { get; }
        internal PlywoodChartDomain(string reference, double minimumSpan, double maximumSpan, double minimumPressure, double maximumPressure)
        {
            if (string.IsNullOrWhiteSpace(reference)
                || new[] {minimumSpan, maximumSpan, minimumPressure, maximumPressure}.Any(x=>double.IsNaN(x) || double.IsInfinity(x) || x<=0)
                || maximumSpan<minimumSpan || maximumPressure<minimumPressure)
                throw new ArgumentException("Invalid plywood chart domain.");
            Reference=reference; MinimumSpanMm=minimumSpan; MaximumSpanMm=maximumSpan;
            MinimumPressureKnM2=minimumPressure; MaximumPressureKnM2=maximumPressure;
        }
        public override string ToString() => $"{Reference}: {MinimumSpanMm:0}–{MaximumSpanMm:0} mm, {MinimumPressureKnM2:0.###}–{MaximumPressureKnM2:0.###} kN/m²";
    }

    public sealed class PlywoodProfile
    {
        public string Id { get; }
        public string Label { get; }
        public double ThicknessMm { get; }
        public IReadOnlyList<PlywoodChartDomain> Charts { get; }
        public string ChartReference => string.Join(" + ", Charts.Select(x=>x.Reference));
        public string Note { get; }
        public double DeflectionCoefficient { get; }
        public double MomentEnvelope { get; }
        public double ShearEnvelope { get; }
        public double MinimumSpanMm => Charts.Min(x=>x.MinimumSpanMm);
        public double MaximumSpanMm => Charts.Max(x=>x.MaximumSpanMm);
        public double MinimumPressureKnM2 => Charts.Min(x=>x.MinimumPressureKnM2);
        public double MaximumPressureKnM2 => Charts.Max(x=>x.MaximumPressureKnM2);
        public string Basis => $"{Label} · chart {ChartReference} · approximate chart fit. "
            + "Continuous equal spans and uniform pressure as illustrated; spacing is between plywood supports. "
            + "Uses the selected L/500, L/350 or L/270 criterion, bending M and shear Q. "
            + "Results round down to 5 mm within the applicable chart domain: " + string.Join("; ", Charts) + ". Lower pressures use the chart minimum. "
            + "Legacy fixed H20 deflection records use L/500 for plywood. Secondary H20 member strength, "
            + "sheet orientation, joints and end/edge support details require separate checks. " + Note;

        internal PlywoodProfile(string id, string label, double thickness, string chartReference,
            double deflectionCoefficient, double momentEnvelope, double shearEnvelope,
            double minimumSpan, double maximumSpan, double minimumPressure, double maximumPressure, string note,
            params PlywoodChartDomain[] additionalCharts)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(chartReference)
                || new[] {thickness, deflectionCoefficient, momentEnvelope, shearEnvelope, minimumSpan, maximumSpan, minimumPressure, maximumPressure}
                    .Any(x=>double.IsNaN(x) || double.IsInfinity(x) || x<=0)
                || maximumSpan<minimumSpan || maximumPressure<minimumPressure)
                throw new ArgumentException("Invalid plywood chart profile.");
            if (additionalCharts==null || additionalCharts.Any(x=>x==null)) throw new ArgumentException("Invalid additional plywood charts.");
            Id=id; Label=label; ThicknessMm=thickness; Note=note??"";
            Charts=Array.AsReadOnly(new[] {new PlywoodChartDomain(chartReference, minimumSpan, maximumSpan, minimumPressure, maximumPressure)}.Concat(additionalCharts).ToArray());
            DeflectionCoefficient=deflectionCoefficient; MomentEnvelope=momentEnvelope; ShearEnvelope=shearEnvelope;
        }
    }

    public static class PlywoodCatalog
    {
        // Fits to the supplied raster charts; each retains its own plotted domain.
        // Coefficients use L in metres, p in kN/m² and displacement in mm.
        public static IReadOnlyList<PlywoodProfile> All { get; } = Array.AsReadOnly(new[] {
            new PlywoodProfile("dokaplex-9mm-9732-121", "Dokaplex · 9 mm", 9, "9732-121",
                20.0, 1.40, 8.0, 100, 500, 5, 80,
                "The source describes curved surfaces/inliners and use of this graph for low-loading form facing."),
            new PlywoodProfile("doka-18mm-9732-103", "Doka plywood · 18 mm", 18, "9732-103",
                2.30, 5.60, 16.0, 200, 600, 2.5, 60, ""),
            new PlywoodProfile("doka-21mm-9732-104", "Doka plywood · 21 mm", 21, "9732-104",
                1.40, 7.80, 19.0, 200, 600, 2.5, 80,
                "Full chart: 200–600 mm up to 80 kN/m². Detailed chart: 100–300 mm up to 100 kN/m².",
                new PlywoodChartDomain("9732-120", 100, 300, 2.5, 100))
        });
        public static PlywoodProfile Default => All.Single(x=>x.Id=="doka-18mm-9732-103");
        public static PlywoodProfile FromId(string id) => All.SingleOrDefault(x=>x.Id==id)
            ?? throw new ArgumentException("The saved plywood profile is unknown: " + id);
    }

    /// <summary>Chart-specific fits for the illustrated continuous, equal-span, uniformly loaded sheets.</summary>
    public static class PlywoodSpacing
    {
        public static string Basis => PlywoodCatalog.Default.Basis;

        public const double SheetLengthMm = 2440;
        public const double SheetWidthMm = 1220;
        public static IReadOnlyList<double> StandardSpacingsMm { get; } = Array.AsReadOnly(new[] {488.0,407,350,305,275,250});
        public static string SheetBasis => "2440 × 1220 mm sheets. Maximum spacings: 488, 407, 350, 305, 275 or 250 mm; the largest passing value is used. "
            + "The 2440 mm direction follows the secondary-row setting out. Each sheet joint has a support row; the final span in a sheet can be shorter.";

        public static PlywoodSpacingResult CalculateForSheet(PlywoodProfile profile, double pressureKnM2, int denominator)
        {
            var chart=Calculate(profile,pressureKnM2,denominator);
            double spacing=StandardSpacingsMm.FirstOrDefault(x=>x<=chart.MaximumSpacingMm && x>=chart.Chart.MinimumSpanMm);
            if (spacing==0) throw new ArgumentException($"{profile.Label}: plywood permits at most {chart.MaximumSpacingMm:0} mm; none of the listed sheet spacings (minimum 250 mm) passes. Select a stronger plywood or revise the load.");
            double l=spacing/1000;
            return new PlywoodSpacingResult(profile,chart.Chart,pressureKnM2,chart.ChartPressureKnM2,denominator,spacing,
                "Sheet spacing · " + chart.Governing,chart.MomentSpacingMm,chart.ShearSpacingMm,chart.DeflectionSpacingMm,
                profile.DeflectionCoefficient*chart.ChartPressureKnM2*Math.Pow(l,4),
                chart.ChartPressureKnM2*l*l/profile.MomentEnvelope,chart.ChartPressureKnM2*l/profile.ShearEnvelope)
                { ChartMaximumSpacingMm=chart.MaximumSpacingMm };
        }

        // Absolute stations retain sheet joints even when primary coverage is centred or longer.
        // End supports normally sit 40 mm inside covered stock; nearby sheet joints take priority. A partial span is shared between
        // the final two intervals so adjacent 80 mm H20 flanges cannot overlap.
        public static double[] SheetSupportRows(double startMm, double endMm, double maximumSpacingMm, double sheetLengthMm=SheetLengthMm)
        {
            if (new[] {startMm,endMm,maximumSpacingMm,sheetLengthMm}.Any(x=>double.IsNaN(x)||double.IsInfinity(x))
                || endMm-startMm<=80 || endMm-startMm>50000 || maximumSpacingMm<100 || sheetLengthMm<=0)
                throw new ArgumentException("Invalid sheet support geometry.");
            double start=startMm+40,end=endMm-40;
            var joints=new List<double>();
            for (double joint=(Math.Floor(startMm/sheetLengthMm)+1)*sheetLengthMm;joint<endMm;joint+=sheetLengthMm) joints.Add(joint);
            var anchors=new List<double>();
            if (joints.Count==0 || joints[0]-start>=80) anchors.Add(start);
            anchors.AddRange(joints);
            if (anchors.Count==0 || end-anchors.Last()>=80) anchors.Add(end);
            var rows=new List<double> {anchors[0]};
            for (int i=1;i<anchors.Count;i++)
            {
                double a=anchors[i-1],b=anchors[i];
                int full=(int)Math.Floor((b-a)/maximumSpacingMm);
                double tail=b-a-full*maximumSpacingMm;
                int nominal=tail>1e-7 && tail<80 ? Math.Max(0,full-1) : full;
                for (int n=1;n<=nominal;n++) rows.Add(a+n*maximumSpacingMm);
                double remaining=b-rows.Last();
                if (remaining>maximumSpacingMm+1e-7) rows.Add(rows.Last()+remaining/2);
                if (b-rows.Last()>1e-7) rows.Add(b);
            }
            return rows.ToArray();
        }

        public static PlywoodSpacingResult Calculate(double pressureKnM2, int denominator)
            => Calculate(PlywoodCatalog.Default, pressureKnM2, denominator);

        public static PlywoodSpacingResult Calculate(PlywoodProfile profile, double pressureKnM2, int denominator)
        {
            if (profile==null) throw new ArgumentException("Select a plywood product and thickness.");
            if (double.IsNaN(pressureKnM2) || double.IsInfinity(pressureKnM2) || pressureKnM2 <= 0)
                throw new ArgumentException("Plywood pressure must be a finite positive number.");
            if (pressureKnM2 > profile.MaximumPressureKnM2)
                throw new ArgumentException($"Plywood pressure exceeds the supplied {profile.ThicknessMm:0} mm chart's {profile.MaximumPressureKnM2:0.###} kN/m² range; no secondary spacing is available.");
            if (denominator != 500 && denominator != 350 && denominator != 270)
                throw new ArgumentException("Select L/500, L/350 or L/270 for plywood.");
            double p = Math.Max(profile.MinimumPressureKnM2, pressureKnM2);
            double moment = 1000 * Math.Sqrt(profile.MomentEnvelope / p);
            double shear = 1000 * profile.ShearEnvelope / p;
            double deflection = 1000 * Math.Pow(1000 / (denominator * profile.DeflectionCoefficient * p), 1.0 / 3);
            double structuralLimit = Math.Min(moment, Math.Min(shear, deflection));
            // Evaluate chart domains independently; never fill gaps or extrapolate a high-load
            // detailed curve into the full chart's longer-span domain.
            var chart = profile.Charts.Where(x=>p>=x.MinimumPressureKnM2 && p<=x.MaximumPressureKnM2)
                .OrderByDescending(x=>x.MaximumSpanMm)
                .FirstOrDefault(x=>Math.Floor(Math.Min(structuralLimit,x.MaximumSpanMm)/5)*5>=x.MinimumSpanMm);
            if (chart==null)
                throw new ArgumentException("No secondary spacing within the supplied plywood chart domains satisfies these inputs.");
            double limit = Math.Min(chart.MaximumSpanMm, structuralLimit);
            string governing = limit == chart.MaximumSpanMm ? "Chart span limit" : limit == moment ? "Bending M" : limit == shear ? "Shear Q" : "Deflection";
            double spacing = Math.Floor(limit / 5) * 5;
            if (spacing < profile.MinimumSpanMm)
                throw new ArgumentException($"No secondary spacing within the supplied chart's {profile.MinimumSpanMm:0}–{profile.MaximumSpanMm:0} mm range satisfies these inputs.");
            double l = spacing / 1000;
            return new PlywoodSpacingResult(profile, chart, pressureKnM2, p, denominator, spacing, governing,
                moment, shear, deflection, profile.DeflectionCoefficient * p * Math.Pow(l, 4),
                p * l * l / profile.MomentEnvelope, p * l / profile.ShearEnvelope);
        }
    }

    public sealed class PlywoodSpacingResult
    {
        public PlywoodProfile Profile { get; }
        public PlywoodChartDomain Chart { get; }
        public double ChartMaximumSpacingMm { get; internal set; }
        public double PressureKnM2 { get; }
        public double ChartPressureKnM2 { get; }
        public int Denominator { get; }
        public double MaximumSpacingMm { get; }
        public string Governing { get; }
        public double MomentSpacingMm { get; }
        public double ShearSpacingMm { get; }
        public double DeflectionSpacingMm { get; }
        public double DeflectionMm { get; }
        public double AllowableDeflectionMm => MaximumSpacingMm / Denominator;
        public double MomentUtilization { get; }
        public double ShearUtilization { get; }
        internal PlywoodSpacingResult(PlywoodProfile profile, PlywoodChartDomain chart, double pressure, double chartPressure, int denominator, double spacing,
            string governing, double momentSpacing, double shearSpacing, double deflectionSpacing,
            double deflection, double momentUse, double shearUse)
        {
            Profile = profile; Chart=chart; PressureKnM2 = pressure; ChartPressureKnM2 = chartPressure; Denominator = denominator;
            MaximumSpacingMm = spacing; ChartMaximumSpacingMm=spacing; Governing = governing; MomentSpacingMm = momentSpacing;
            ShearSpacingMm = shearSpacing; DeflectionSpacingMm = deflectionSpacing; DeflectionMm = deflection;
            MomentUtilization = momentUse; ShearUtilization = shearUse;
        }
    }
}
