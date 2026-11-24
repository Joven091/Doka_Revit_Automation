using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MyRevitAddin.ViewModels;

namespace EngineeringDashboard
{
    public partial class FlexCalculatorViewModel
    {
        private double primaryRightCantilever = 500, secondaryRightCantilever = 500;
        private bool primaryHasAccess, secondaryHasAccess;
        private string primaryAccessEnd = "Right", secondaryAccessEnd = "Right";
        private double primaryAccessLeft = 250, primaryAccessRight = 250, secondaryAccessLeft = 250, secondaryAccessRight = 250;
        private double totalAccessLoad = .75;
        private int exactPrimaryCount = 5, exactPropCount = 5;
        private string accessPreviewBeam = "Primary H20";
        private List<PrimaryLine> primaryLines = new List<PrimaryLine>();
        private PrimaryLine governingLine;
        private sealed class PrimaryLine
        {
            public string Name;
            public SlabBeamAnalysis Secondary, Primary;
        }
        public IReadOnlyList<string> AccessEndOptions => new[] { "Left", "Right", "Both" };
        public IReadOnlyList<string> AccessBeamOptions => new[] { "Primary H20", "Secondary H20" };
        public double PrimaryLeftCantilever { get => _primaryBeamCantilever; set { _primaryBeamCantilever = value; AccessChanged(); } }
        public double PrimaryRightCantilever { get => primaryRightCantilever; set { primaryRightCantilever = value; AccessChanged(); } }
        public double SecondaryLeftCantilever { get => _secondaryBeamCantilever; set { _secondaryBeamCantilever = value; AccessChanged(); } }
        public double SecondaryRightCantilever { get => secondaryRightCantilever; set { secondaryRightCantilever = value; AccessChanged(); } }
        public bool PrimaryHasAccess { get => primaryHasAccess; set { primaryHasAccess = value; AccessChanged(); } }
        public bool SecondaryHasAccess { get => secondaryHasAccess; set { secondaryHasAccess = value; AccessChanged(); } }
        public string PrimaryAccessEnd { get => primaryAccessEnd; set { primaryAccessEnd = value; AccessChanged(); } }
        public string SecondaryAccessEnd { get => secondaryAccessEnd; set { secondaryAccessEnd = value; AccessChanged(); } }
        public double PrimaryAccessLeftMm { get => primaryAccessLeft; set { primaryAccessLeft = value; AccessChanged(); } }
        public double PrimaryAccessRightMm { get => primaryAccessRight; set { primaryAccessRight = value; AccessChanged(); } }
        public double SecondaryAccessLeftMm { get => secondaryAccessLeft; set { secondaryAccessLeft = value; AccessChanged(); } }
        public double SecondaryAccessRightMm { get => secondaryAccessRight; set { secondaryAccessRight = value; AccessChanged(); } }
        /// <summary>Total pressure, including deck weight. Never add the slab's formwork allowance to this value.</summary>
        public double TotalAccessLoadKnM2 { get => totalAccessLoad; set { totalAccessLoad = value; AccessChanged(); } }
        public int ExactPrimaryBeamCount { get => exactPrimaryCount; set { exactPrimaryCount = value; AccessChanged(); } }
        public int ExactPropCount { get => exactPropCount; set { exactPropCount = value; AccessChanged(); } }
        public bool HasAccessExtensions => PrimaryHasAccess || SecondaryHasAccess;
        public bool UsesAccessAnalysis => HasAccessExtensions || PrimaryLeftCantilever != PrimaryRightCantilever || SecondaryLeftCantilever != SecondaryRightCantilever;
        public bool ShowExactPrimaryCount => UsesAccessAnalysis && PrimaryBeamCount == "5+";
        public bool ShowExactPropCount => UsesAccessAnalysis && PropCount == "5+";
        public int ActualPrimaryBeamCount => PrimaryBeamCount == "5+" ? (UsesAccessAnalysis ? ExactPrimaryBeamCount : 5) : int.Parse(PrimaryBeamCount);
        public int ActualPropCount => PropCount == "5+" ? (UsesAccessAnalysis ? ExactPropCount : 5) : int.Parse(PropCount);
        public bool ShowPrimaryAccessLeft => PrimaryHasAccess && (PrimaryAccessEnd == "Left" || PrimaryAccessEnd == "Both");
        public bool ShowPrimaryAccessRight => PrimaryHasAccess && (PrimaryAccessEnd == "Right" || PrimaryAccessEnd == "Both");
        public bool ShowSecondaryAccessLeft => SecondaryHasAccess && (SecondaryAccessEnd == "Left" || SecondaryAccessEnd == "Both");
        public bool ShowSecondaryAccessRight => SecondaryHasAccess && (SecondaryAccessEnd == "Right" || SecondaryAccessEnd == "Both");
        public double PrimaryLeftAccess => ShowPrimaryAccessLeft ? PrimaryAccessLeftMm / 1000 : 0;
        public double PrimaryRightAccess => ShowPrimaryAccessRight ? PrimaryAccessRightMm / 1000 : 0;
        public double SecondaryLeftAccess => ShowSecondaryAccessLeft ? SecondaryAccessLeftMm / 1000 : 0;
        public double SecondaryRightAccess => ShowSecondaryAccessRight ? SecondaryAccessRightMm / 1000 : 0;
        public string PrimarySlabEdgeText => EdgeText(PrimaryHasAccess, PrimaryAccessEnd, PrimaryLeftCantilever - PrimaryAccessLeftMm, PrimaryRightCantilever - PrimaryAccessRightMm);
        public string SecondarySlabEdgeText => EdgeText(SecondaryHasAccess, SecondaryAccessEnd, SecondaryLeftCantilever - SecondaryAccessLeftMm, SecondaryRightCantilever - SecondaryAccessRightMm);
        public string AccessGoverningSummary { get; private set; }
        public string AccessPreviewBeam { get => accessPreviewBeam; set { accessPreviewBeam = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccessPreviewAnalysis)); } }
        public SlabBeamAnalysis PrimaryAccessAnalysis => governingLine?.Primary;
        public SlabBeamAnalysis SecondaryAccessAnalysis => governingLine?.Secondary;
        public SlabBeamAnalysis AccessPreviewAnalysis => AccessPreviewBeam == "Secondary H20" ? SecondaryAccessAnalysis : PrimaryAccessAnalysis;
        private void AccessChanged([CallerMemberName] string property = null) { OnPropertyChanged(property); Calculate(); }
        private static string EdgeText(bool enabled, string end, double left, double right)
        {
            if (!enabled) return "";
            string Describe(string name, double distance) => name + " slab edge: " + Math.Abs(distance).ToString("0.##") + " mm " + (distance >= 0 ? "past" : "inside") + " the last support";
            return end == "Both" ? Describe("Left", left) + "\n" + Describe("Right", right) : end == "Left" ? Describe("Left", left) : Describe("Right", right);
        }
        private void NotifyAccessProperties()
        {
            foreach (string name in new[] { nameof(PrimaryLeftCantilever), nameof(PrimaryRightCantilever), nameof(SecondaryLeftCantilever), nameof(SecondaryRightCantilever),
                nameof(PrimaryHasAccess), nameof(SecondaryHasAccess), nameof(PrimaryAccessEnd), nameof(SecondaryAccessEnd), nameof(HasAccessExtensions), nameof(UsesAccessAnalysis),
                nameof(ShowExactPrimaryCount), nameof(ShowExactPropCount), nameof(ShowPrimaryAccessLeft), nameof(ShowPrimaryAccessRight), nameof(ShowSecondaryAccessLeft), nameof(ShowSecondaryAccessRight),
                nameof(PrimarySlabEdgeText), nameof(SecondarySlabEdgeText), nameof(AccessGoverningSummary), nameof(AccessPreviewAnalysis) }) OnPropertyChanged(name);
        }
        private void ValidateAccessInputs()
        {
            CheckNumber(PrimaryRightCantilever, "Right primary cantilever", false);
            CheckNumber(SecondaryRightCantilever, "Right secondary cantilever", false);
            if (HasAccessExtensions) CheckNumber(TotalAccessLoadKnM2, "Total access load", false);
            if (PrimaryHasAccess && !AccessEndOptions.Contains(PrimaryAccessEnd) || SecondaryHasAccess && !AccessEndOptions.Contains(SecondaryAccessEnd))
                throw new ArgumentException("Select Left, Right or Both for the access extension.");
            if (ShowPrimaryAccessLeft) CheckNumber(PrimaryAccessLeftMm, "Left primary access extension");
            if (ShowPrimaryAccessRight) CheckNumber(PrimaryAccessRightMm, "Right primary access extension");
            if (ShowSecondaryAccessLeft) CheckNumber(SecondaryAccessLeftMm, "Left secondary access extension");
            if (ShowSecondaryAccessRight) CheckNumber(SecondaryAccessRightMm, "Right secondary access extension");
            if (ShowExactPrimaryCount && ExactPrimaryBeamCount < 5 || ShowExactPropCount && ExactPropCount < 5)
                throw new ArgumentException("Enter the actual support count (at least 5). Partial loading cannot use the full-UDL 5+ envelope.");
        }
        private void ClearAccessResults()
        {
            primaryLines.Clear();
            governingLine = null;
            AccessGoverningSummary = null;
        }
        private void CalculateAccessInputs(double slabPressure, double capacity)
        {
            double divisor = double.Parse(DeflectionLimit.Substring(2), System.Globalization.CultureInfo.InvariantCulture);
            int secondaryCount = ActualPrimaryBeamCount, primaryCount = ActualPropCount;
            double primarySpan = PropSpacing / 1000, secLeft = SecondaryLeftCantilever / 1000, secRight = SecondaryRightCantilever / 1000,
                primLeft = PrimaryLeftCantilever / 1000, primRight = PrimaryRightCantilever / 1000, rowWidth = SecondaryBeamSpacing / 1000;
            double primaryLength = primLeft + (primaryCount - 1.0) * primarySpan + primRight;
            // Validate fixed primary geometry before searching the secondary span.
            SlabBeamAnalysis.Zones(primaryLength, PrimaryLeftAccess, PrimaryRightAccess, 1, 1, 1);
            double accessLoad = HasAccessExtensions ? TotalAccessLoadKnM2 * rowWidth : 0;
            string failure = null;
            List<PrimaryLine> best = null;
            double selectedSpacing = 0;
            for (int spacingMm = 4000; spacingMm >= 100; spacingMm -= 25)
            {
                double secondarySpan = spacingMm / 1000.0, secondaryLength = secLeft + (secondaryCount - 1.0) * secondarySpan + secRight;
                if (SecondaryLeftAccess + SecondaryRightAccess >= secondaryLength) continue;
                var secondary = SlabBeamAnalysis.Analyze(secondaryCount, secondarySpan, secLeft, secRight,
                    SlabBeamAnalysis.Zones(secondaryLength, SecondaryLeftAccess, SecondaryRightAccess, slabPressure * rowWidth, accessLoad, accessLoad), H20_EI, divisor);
                var accessSecondary = PrimaryHasAccess ? SlabBeamAnalysis.Analyze(secondaryCount, secondarySpan, secLeft, secRight,
                    new[] { new SlabBeamLoad { End = secondaryLength, Load = accessLoad, IsAccess = true } }, H20_EI, divisor) : null;
                const string loadCase = "All loads present";
                failure = Failure(secondary, "Secondary H20", loadCase);
                if (failure == null && accessSecondary != null) failure = Failure(accessSecondary, "Secondary H20 over the primary access zone", loadCase);
                if (failure != null) continue;

                var candidates = new List<PrimaryLine>();
                for (int line = 0; line < secondaryCount; line++)
                {
                    string lineName = loadCase + " · primary P" + (line + 1);
                    // Keep both secondary reactions paired to the same receiving primary.
                    double slabLineLoad = Math.Max(0, secondary.Reactions[line]) / rowWidth;
                    double accessLineLoad = accessSecondary == null ? 0 : Math.Max(0, accessSecondary.Reactions[line]) / rowWidth;
                    var primary = SlabBeamAnalysis.Analyze(primaryCount, primarySpan, primLeft, primRight,
                        SlabBeamAnalysis.Zones(primaryLength, PrimaryLeftAccess, PrimaryRightAccess, slabLineLoad, accessLineLoad, accessLineLoad), H20_EI, divisor);
                    failure = Failure(primary, "Primary H20", lineName);
                    if (failure == null && primary.Reaction > capacity + 1e-8)
                        failure = $"Primary prop load {primary.Reaction:0.00} kN exceeds {capacity:0.00} kN ({lineName}).";
                    if (failure != null) break;
                    candidates.Add(new PrimaryLine { Name = lineName, Secondary = secondary, Primary = primary });
                }
                if (failure != null) continue;
                selectedSpacing = spacingMm; best = candidates; break;
            }
            if (best == null)
            {
                WarningMessage = (failure == null ? "Check the slab edge and access-extension dimensions." : failure) +
                    " No primary beam spacing on the 25 mm grid passes the fully loaded check. Revise the cantilevers or support arrangement.";
                return;
            }
            primaryLines = best;
            SuggestedPrimaryBeamSpacing = selectedSpacing;
            governingLine = best.OrderByDescending(c => Math.Max(c.Primary.Reaction / capacity,
                Math.Max(c.Primary.Moment / MAX_H20_MOMENT_KNM, Math.Max(c.Primary.Shear / MAX_H20_SHEAR_KN, c.Primary.DeflectionUtilization)))).First();
            ActualPropLoad = Math.Round(best.Max(c => c.Primary.Reaction), 2);
            H20Shear = Math.Round(best.Max(c => c.Primary.Shear), 2);
            H20Utilization = Math.Round(best.Max(c => c.Primary.Shear) / MAX_H20_SHEAR_KN * 100, 1);
            UtilizationRate = Math.Round(best.Max(c => c.Primary.Reaction) / capacity * 100, 1);
            PrimaryDeflectionMm = best.Max(c => c.Primary.SpanDeflection) * 1000;
            AccessGoverningSummary = $"Governing check: {governingLine.Name}.";
            PrimaryDiagramViewModel = DiagramWindowViewModel.ForAccess(governingLine.Primary, "Primary H20", governingLine.Name);
            SecondaryDiagramViewModel = DiagramWindowViewModel.ForAccess(governingLine.Secondary, "Secondary H20", governingLine.Name);
        }
        private static string Failure(SlabBeamAnalysis result, string layer, string name)
        {
            if (result.HasUplift) return $"Uplift: {layer} loses contact at support {result.MinimumReactionSupport} ({name}).";
            if (result.Moment > MAX_H20_MOMENT_KNM + 1e-8) return $"{layer} bending {result.Moment:0.00} kNm exceeds 5.00 kNm ({name}).";
            if (result.Shear > MAX_H20_SHEAR_KN + 1e-8) return $"{layer} shear {result.Shear:0.00} kN exceeds 11.00 kN ({name}).";
            if (result.DeflectionUtilization > 1 + 1e-8) return $"{layer} deflection, including cantilever support rotation, exceeds the selected limit ({name}).";
            return null;
        }
        public string AccessReport()
        {
            return string.Join("\n", primaryLines.Select(c => c.Name + ": " +
                string.Join("; ", new[] { new { Name = "Secondary", Beam = c.Secondary }, new { Name = "Primary", Beam = c.Primary } }.Select(v =>
                    v.Name + $" moment {v.Beam.Moment:0.000}/5 kNm; shear {v.Beam.Shear:0.000}/11 kN; deflection use {v.Beam.DeflectionUtilization * 100:0.0}%; " +
                    "UDLs " + string.Join(", ", v.Beam.Loads.Select(q => $"{q.Start * 1000:0.##}–{q.End * 1000:0.##} mm: {q.Load:0.000} kN/m")) +
                    "; reactions " + string.Join(", ", v.Beam.Reactions.Select((r, i) => $"R{i + 1}={r:0.000} kN"))))));
        }
    }
}
