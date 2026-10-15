using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorViewModel
    {
        private BeamSearchTrace beamSearchTrace;
        private BeamSearchTrace lastCompletedFullSearch;
        private const string DiagnosticInputs="PlywoodDeflectionLimit,PlywoodProfileId,UseTowers,TransferSlabLoads,UseBeamNetwork,Depth,Width,BeamLengthMm,FloorHeight,NumberOfProps,TowerLegCapacityKn,PrimaryMaterial,StockLengthsText,AllowCustomH20Lengths,CustomH20Scope,IgnoreUplift,StockEndSetbackMm,MaximumConcreteEndOverhangMm,Exclude600Standalone,UpperSupportSpacingMm,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,LeftSlabBayMm,RightSlabBayMm,SlabStripWidthMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad";
        private static string DiagnosticValue(object value)=>value is double number ? number.ToString("R",CultureInfo.InvariantCulture) : Convert.ToString(value,CultureInfo.InvariantCulture);
        private string CaptureDiagnosticInputs()
        {
            var text=new StringBuilder();text.AppendLine("Units: dimensions mm; loads kN, kN/m or kN/m² as labelled.");
            foreach(string name in DiagnosticInputs.Split(','))text.AppendLine(name+" = "+DiagnosticValue(GetType().GetProperty(name).GetValue(this)));
            text.AppendLine("Prop configuration = "+(SelectedProp?.CapacityKey??"(none)"));
            text.AppendLine("Deflection criterion = "+(DeflectionLimit?.Label??"(none)"));
            text.AppendLine("Deflection denominator = "+(DeflectionLimit?.Denominator??0));
            text.AppendLine("Fixed deflection limit mm = "+(DeflectionLimit?.Millimetres??0).ToString("R",CultureInfo.InvariantCulture));
            for(int i=0;i<NetworkBeams.Count;i++)text.AppendLine(FormattableString.Invariant($"B{i+1}: width={NetworkBeams[i].WidthMm:R}; depth={NetworkBeams[i].DepthMm:R} mm; bottom={NetworkBeams[i].BottomElevationMm:R} mm"));
            for(int i=0;i<NetworkBays.Count;i++)text.AppendLine(FormattableString.Invariant($"S{i+1}: clear span={NetworkBays[i].WidthMm:R}; thickness={NetworkBays[i].ThicknessMm:R} mm"));
            for(int i=0;i<CrossingBeams.Count;i++) {var c=CrossingBeams[i];text.AppendLine(FormattableString.Invariant($"X{i+1}: slab bay={c.BayNumber}; centre={c.PositionMm:R}; width={c.WidthMm:R}; depth={c.DepthMm:R} mm; bottom={c.BottomElevationMm:R} mm; supports={c.SupportMode}"));}
            for(int i=0;i<IndependentBeams.Count;i++){var b=IndependentBeams[i];text.AppendLine(FormattableString.Invariant($"I{i+1}: independent; start=({b.StartXmm:R},{b.StartYmm:R}); end=({b.EndXmm:R},{b.EndYmm:R}); length={b.LengthMm:R}; width={b.WidthMm:R}; depth={b.DepthMm:R}; bottom={b.BottomElevationMm:R} mm; own beam load, no slab transfer"));}
            return text.ToString();
        }
        // Read-only capture on the UI thread: no validation, recalculation, model API or input commits.
        public string BuildDiagnosticReport(string remarks="",string visibleInputs="")
        {
            string current=CaptureDiagnosticInputs();var text=new StringBuilder();
            var assembly=typeof(BeamCalculatorViewModel).Assembly;
            text.AppendLine("=== BEAM FORMWORK DIAGNOSTIC REPORT v1 ===");
            text.AppendLine("Captured UTC: "+DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture));
            text.AppendLine("Assembly: "+assembly.GetName().Name+" "+assembly.GetName().Version);
            text.AppendLine("Build module ID: "+assembly.ManifestModule.ModuleVersionId);
            text.AppendLine("Runtime: "+Environment.Version+"; OS: "+Environment.OSVersion+"; 64-bit process: "+Environment.Is64BitProcess+"; logical processors: "+Environment.ProcessorCount);
            text.AppendLine("Input culture: "+CultureInfo.CurrentCulture.Name);
            text.AppendLine("Managed memory MB (approx.): "+(GC.GetTotalMemory(false)/1048576.0).ToString("0.0",CultureInfo.InvariantCulture));
            text.AppendLine("Remarks:\n"+(remarks??""));
            text.AppendLine("\nCURRENT WINDOW STATE");
            text.AppendLine("Search running: "+IsOptimizing+"; valid result: "+IsCalculationValid+"; text input errors: "+textErrors);
            text.AppendLine("Current warning: "+WarningMessage);
            text.AppendLine("\nCURRENT INPUTS\n"+current);
            text.AppendLine("\nVISIBLE / UNCOMMITTED FIELD TEXT\n"+(string.IsNullOrEmpty(visibleInputs) ? "(none supplied)" : visibleInputs));
            text.AppendLine("\nLAST / RUNNING SEARCH");
            if(beamSearchTrace==null)text.AppendLine("No tower search has been started in this session.");
            else
            {
                text.AppendLine("Current model inputs differ from search inputs: "+(current!=beamSearchTrace.Inputs));
                text.AppendLine(beamSearchTrace.Snapshot());
                text.AppendLine("\nEXACT INPUTS CAPTURED AT SEARCH START\n"+beamSearchTrace.Inputs);
            }
            if(lastCompletedFullSearch!=null && !ReferenceEquals(lastCompletedFullSearch,beamSearchTrace))
            {
                text.AppendLine("\nLAST COMPLETED FULL SEARCH");text.AppendLine(lastCompletedFullSearch.Snapshot());
                text.AppendLine("\nFULL SEARCH INPUTS\n"+lastCompletedFullSearch.Inputs);
            }
            text.AppendLine("\nPLYWOOD SECONDARY SPACING\n"+SecondarySpacingSummary+"\n"+ElementPlywoodSpacingSummary);
            if(IsCalculationValid)text.AppendLine("\nCURRENT RESULT\n"+(NetworkResult?.Summary??TowerResult?.Summary??("Prop spacing mm: "+MaximumPropSpacing.ToString("R",CultureInfo.InvariantCulture))));
            if(IsCalculationValid && IndependentBeamResults.Length>0)text.AppendLine("\nINDEPENDENT BEAM RESULTS\n"+string.Join("\n",IndependentBeamResults.Select((b,i)=>$"I{i+1}: length {b.LengthMm:0.###} mm; own beam load {b.TotalSupportedLoadKn:0.###} kN; {b.LegCount} legs; peak leg {b.MaxLegKn:0.###} kN. Slab loads remain on connected bays.")));
            if(IsCalculationValid && NetworkResult!=null)text.AppendLine("\nSELECTED NETWORK LAYOUT\n"+NetworkResult.LayoutDiagnostics);
            if(!string.IsNullOrEmpty(TowerFailureDetails))text.AppendLine("\nCURRENT FAILURE DETAILS\n"+TowerFailureDetails);
            text.AppendLine("\nLOADING POLICY\n"+(ReplayConstructionStages ? "Historical staged regression replay." : "Fully loaded beam + connected slabs + applicable crossings only. Partial construction stages are not checked."));
            text.AppendLine("\nPRIMARY MATERIAL BASIS\n"+PrimaryMaterialAssumptions);
            text.AppendLine("\nSEARCH LIMITS / RANKING");
            text.AppendLine("Normal calculation has no elapsed-time limit. The 30-second solution time is a performance target. Catalogue, custom and fallback searches run to completion within their candidate limits; user cancellation remains available.");
            text.AppendLine("Beam-only: ≤80 legs / 200000 sampled attempts; rank legs, standalone-600 count, braced bays, wider standalone braces, primary quantity, utilization. Transfer: bounded covers, geometries and cross options; rank relative economy (tower legs, upper feet and material separately), brace preference, symmetry, utilization. Network: bounded mirrored stock/group families, shared upper options and row phases, local beam-only rows and passing-plan coordination; rank relative economy, brace preference, symmetry. Custom mode compares catalogue and custom passes. Global optimality is not proven.");
            text.AppendLine("=== END BEAM FORMWORK DIAGNOSTIC REPORT ===");return text.ToString();
        }
    }
}
