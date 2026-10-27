using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel
    {
        private sealed class OperationDiagnostic
        {
            public string Name,Context,Outcome="Running";
            public DateTime StartedUtc=DateTime.UtcNow;
            public Stopwatch Clock=Stopwatch.StartNew();
            public Exception Error;
        }
        private sealed class SearchDiagnostic
        {
            public DateTime StartedUtc=DateTime.UtcNow;
            public Stopwatch Clock=Stopwatch.StartNew();
            public int Revision;
            public string Settings,Outcome="Running";
            public WallRow[] Walls;
            public int? Attempted,Unsearched;
            public bool? BudgetExpired;
            public string[] Log=Array.Empty<string>();
            public readonly List<string> Progress=new List<string>();
        }
        private readonly List<OperationDiagnostic> diagnosticOperations=new List<OperationDiagnostic>();
        private readonly List<SearchDiagnostic> diagnosticSearches=new List<SearchDiagnostic>();
        private OperationDiagnostic BeginOperation(string name,string context)
        {
            var operation=new OperationDiagnostic{Name=name,Context=context};diagnosticOperations.Add(operation);return operation;
        }
        private void FailOperation(OperationDiagnostic operation,Exception error)
        {operation.Error=error;operation.Outcome="Failed";}
        private static void EndOperation(OperationDiagnostic operation,string outcome=null)
        {operation.Clock.Stop();if(operation.Error==null)operation.Outcome=outcome??"Completed";}
        private void TrackOperation(string name,string context,Action action)
        {
            var operation=BeginOperation(name,context);
            try{action();}catch(Exception error){FailOperation(operation,error);throw;}finally{EndOperation(operation);}
        }
        public void RecordDiagnosticError(string operation,Exception error)
        {
            if(error==null||diagnosticOperations.Any(o=>ReferenceEquals(o.Error,error)))return;
            var record=BeginOperation(operation,"");FailOperation(record,error);EndOperation(record);
        }
        private SearchDiagnostic BeginSearchDiagnostic()
        {
            var search=new SearchDiagnostic{Revision=revision,Settings=DescribeSettings(Settings),Walls=input?.Walls.Select(w=>w.Snapshot()).ToArray()??Array.Empty<WallRow>()};
            diagnosticSearches.Add(search);return search;
        }
        private static string Value(object value)=>value==null?"none":Convert.ToString(value,CultureInfo.InvariantCulture).Replace("\r","\\r").Replace("\n","\\n");
        private static string DescribeSettings(NativeTop50Project settings)
        {
            var text=new StringBuilder();
            // Only scalar planner settings and the catalogue. Never include embedded source bytes or account/update configuration.
            foreach(var property in typeof(NativeTop50Project).GetProperties().OrderBy(p=>p.Name,StringComparer.Ordinal))
            {
                if(property.Name==nameof(NativeTop50Project.SourceBase64))continue;
                if(property.Name==nameof(NativeTop50Project.Seconds))continue;
                if(property.Name==nameof(NativeTop50Project.CatalogMm))text.AppendLine("CatalogMm: "+(settings.CatalogMm==null?"none":string.Join(", ",settings.CatalogMm)));
                else if(property.PropertyType==typeof(string)||property.PropertyType.IsValueType)text.AppendLine(property.Name+": "+Value(property.GetValue(settings)));
            }
            return text.ToString();
        }
        public string BuildDiagnosticReport()
        {
            var text=new StringBuilder();var assembly=typeof(Top50ViewModel).Assembly;
            text.AppendLine("=== TOP50 PLANNER DIAGNOSTIC REPORT v1 ===");
            text.AppendLine("Captured UTC: "+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
            text.AppendLine("Assembly: "+assembly.GetName().Name+" "+assembly.GetName().Version+"; MVID: "+assembly.ManifestModule.ModuleVersionId);
            text.AppendLine("Runtime: "+Environment.Version+"; OS: "+Environment.OSVersion+"; 64-bit: "+Environment.Is64BitProcess);
            text.AppendLine("--- Current planner state ---");
            text.AppendLine("Status: "+Value(Status));text.AppendLine("Busy: "+IsBusy+"; stale: "+IsStale+"; dirty: "+Dirty+"; input revision: "+revision);
            text.AppendLine("Project path: "+Value(ProjectPath));
            text.AppendLine("Selected proposal: "+Value(selected?.Id)+"; displayed proposals: "+Proposals.Count);
            text.AppendLine("Page: "+CurrentPage+"; selected casting group index: "+SelectedGroupIndex);
            text.AppendLine("--- Current settings ---");text.Append(DescribeSettings(Settings));
            text.AppendLine("OfferingMode: 0 = fixed longest-wall sets; 1 = percentage reference. Preset: "+(PlanningPreset)Settings.Preset);
            text.AppendLine("Ranking: fewest validated panel quantity first; higher-count offers excluded. Presets compare equal-count offers using fixed project scales.");
            text.AppendLine("Search effort uses repeatable pattern-node and single-worker deterministic solver work. No wall-clock deadline; elapsed seconds are observations only.");
            text.AppendLine("--- Current input geometry (millimetres) ---");
            if(input==null)text.AppendLine("No input imported or added. Inspect operation errors below.");
            else
            {
                text.AppendLine("Source path: "+Value(input.SourcePath));text.AppendLine("Source SHA-256 at import: "+Value(input.SourceSha256));
                text.AppendLine("DXF INSUNITS: "+input.InsUnits+"; mm-per-unit override: "+Value(input.UnitOverride)+"; override reason: "+Value(input.UnitOverrideReason));
                text.AppendLine("Measurement policy: "+input.MeasurementPolicy);AppendWalls(text,input.Walls);
            }
            text.AppendLine("--- Operations in this session ---");
            if(diagnosticOperations.Count==0)text.AppendLine("None");
            foreach(var operation in diagnosticOperations)
            {
                text.AppendLine(operation.StartedUtc.ToString("O",CultureInfo.InvariantCulture)+" | "+operation.Name+" | "+operation.Outcome+" | elapsed seconds: "+operation.Clock.Elapsed.TotalSeconds.ToString("F3",CultureInfo.InvariantCulture));
                if(!string.IsNullOrEmpty(operation.Context))text.AppendLine("Context: "+Value(operation.Context));
                if(operation.Error!=null)text.AppendLine(operation.Error.ToString());
            }
            text.AppendLine("--- Search attempts in this session ---");
            if(diagnosticSearches.Count==0)text.AppendLine("No optimization executed in this session; opened/cached proposals are reported separately below.");
            foreach(var search in diagnosticSearches)
            {
                text.AppendLine("Search UTC: "+search.StartedUtc.ToString("O",CultureInfo.InvariantCulture)+"; input revision: "+search.Revision);
                text.AppendLine("Outcome: "+search.Outcome+"; elapsed seconds: "+search.Clock.Elapsed.TotalSeconds.ToString("F3",CultureInfo.InvariantCulture));
                text.AppendLine("Attempted inventories: "+Value(search.Attempted)+"; unsearched inventories: "+Value(search.Unsearched)+"; elapsed-time budget expired: "+Value(search.BudgetExpired));
                text.AppendLine("Unknown counters mean the solver has not returned a complete SearchRun; they do not mean zero candidates.");
                text.AppendLine("Settings at search start:");text.Append(search.Settings);text.AppendLine("Walls at search start:");AppendWalls(text,search.Walls);
                text.AppendLine("Returned search log:");foreach(var line in search.Log)text.AppendLine(Value(line));
                text.AppendLine("Progress observed in this session:");foreach(var line in search.Progress)text.AppendLine(Value(line));
            }
            text.AppendLine("--- Stored search log (may belong to earlier retained proposals) ---");
            foreach(var line in SearchLog)text.AppendLine(Value(line));
            text.AppendLine("--- Displayed proposals and allocations ---");
            foreach(var proposal in Proposals)
            {
                text.AppendLine("Proposal "+proposal.Id+"; rank: "+proposal.Rank+"; selected: "+ReferenceEquals(proposal,selected)+"; stale: "+IsStale+"; Pareto: "+proposal.Pareto);
                text.AppendLine("Stock length mm: "+Value(proposal.StockMicrometres/1000m)+"; panels: "+proposal.PanelCount+"; casting groups: "+proposal.Groups.Count+"; identical kits: "+proposal.IdenticalKitCount);
                text.AppendLine("Mean utilization: "+Value(proposal.MeanUtilization)+"; minimum utilization: "+Value(proposal.MinimumUtilization)+"; fillers: "+proposal.FillerLocations+"; panel changes: "+proposal.PanelChanges);
                text.AppendLine("Retained model status: "+Value(proposal.RetainedModelStatus)+"; objective: "+Value(proposal.RetainedObjective)+"; bound: "+Value(proposal.RetainedBound));
                text.AppendLine("Stock (size mm x quantity): "+string.Join(", ",proposal.Stock.OrderBy(p=>p.Key).Select(p=>p.Key+" x "+p.Value)));
                for(int index=0;index<proposal.Groups.Count;index++)
                {
                    text.AppendLine("Group "+(index+1)+"; installed panel length mm: "+Value(proposal.Groups[index].InstalledPanelMicrometres/1000m));
                    foreach(var wall in proposal.Groups[index].Walls)text.AppendLine("  "+wall.WallId+" | panels mm: "+string.Join(", ",wall.Pattern.PanelsMm)+" | internal gaps mm: "+string.Join(", ",wall.Pattern.InternalGapsMicrometres.Select(g=>Value(g/1000m)))+" | total mm: "+Value(wall.Pattern.LengthMicrometres/1000m));
                }
                try
                {
                    var issues=Top50Validation.Validate(input.Walls,proposal,Limits);
                    text.AppendLine("Validation against CURRENT inputs/settings"+(IsStale?" (proposal is stale)":"")+": "+(issues.Count==0?"passed":string.Join("; ",issues)));
                }
                catch(Exception error){text.AppendLine("Current validation unavailable: "+error.GetType().Name+": "+Value(error.Message));}
            }
            text.AppendLine("--- Loaded Top50 dependencies ---");
            foreach(var dependency in AppDomain.CurrentDomain.GetAssemblies().Where(a=>new[]{"Google.OrTools","Google.Protobuf","DocumentFormat.OpenXml","DocumentFormat.OpenXml.Framework","IxMilia.Dxf","System.Memory","System.Runtime.CompilerServices.Unsafe"}.Contains(a.GetName().Name)).OrderBy(a=>a.FullName,StringComparer.Ordinal))text.AppendLine(dependency.FullName);
            text.AppendLine("Original Top50 solver parity remains unresolved. Retained-model bounds do not prove a global optimum or full-space infeasibility.");
            text.AppendLine("Diagnosis reads in-memory state only; it does not optimize, reimport, change proposals, or read account credentials.");
            text.AppendLine("=== END TOP50 PLANNER DIAGNOSTIC REPORT ===");return text.ToString();
        }
        private static void AppendWalls(StringBuilder text,IReadOnlyList<WallRow> walls)
        {
            text.AppendLine("Walls: "+walls.Count+"; included: "+walls.Count(w=>w.Included)+"; needing review: "+walls.Count(w=>w.Status==WallReviewStatus.NeedsReview)+"; included length mm: "+Value(walls.Where(w=>w.Included).Sum(w=>w.LengthMm)));
            foreach(var wall in walls)
            {
                text.AppendLine("Wall "+wall.Id+" | handle: "+Value(wall.Handle)+" | layer: "+Value(wall.Layer)+" | length mm: "+Value(wall.LengthMm)+" | included: "+wall.Included+" | status: "+wall.Status+" | acknowledged: "+wall.Acknowledged);
                text.AppendLine("  dimension type: "+wall.DimensionType+"; source units: "+wall.SourceUnits+"; mm per unit: "+Value(wall.MmPerUnit)+"; projected raw mm: "+Value(wall.ProjectedRawMm)+"; endpoint distance mm: "+Value(wall.EndpointDistanceMm)+"; stored measurement mm: "+Value(wall.StoredMeasurementMm)+"; label scale: "+Value(wall.LabelScale)+"; text: "+Value(wall.Text));
                text.AppendLine("  Review reason: "+Value(wall.ReviewReason));foreach(var note in wall.Diagnostics)text.AppendLine("  Diagnostic: "+Value(note));
                text.AppendLine("  Raw DXF codes: "+string.Join(" | ",wall.RawCodes.Select(c=>c.Code+"="+Value(c.Value))));
            }
        }
    }
}
