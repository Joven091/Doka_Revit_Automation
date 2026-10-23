using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EngineeringDashboard
{
    internal static class RevitImportDiagnostics
    {
        internal static string Number(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        internal static string Point(ImportPoint point)=>point==null ? "unavailable" : "("+Number(point.X)+", "+Number(point.Y)+")";
        private static void Lines(StringBuilder text,string heading,string[] lines)
        {text.AppendLine(heading+" ("+(lines?.Length??0)+"):");foreach(string line in lines??new string[0])text.AppendLine("  "+line);}
        internal static string CaptureSelection(RevitSelectionSnapshot source)
        {
            var text=new StringBuilder();
            text.AppendLine("Document: "+source.DocumentTitle);text.AppendLine("Revit: "+source.RevitVersion+"; build: "+source.RevitBuild);text.AppendLine("Active view: "+source.ActiveView);
            text.AppendLine("Selected elements: "+(source.SelectedCount<0 ? "unavailable" : source.SelectedCount.ToString(CultureInfo.InvariantCulture))+"; captured IDs: "+string.Join(", ",source.SelectedIds));
            text.AppendLine("Read elements: "+source.Elements.Length+"; beams: "+source.Beams.Length+"; slabs: "+source.Slabs.Length+"; columns/walls: "+source.Boundaries.Length);
            Lines(text,"Selection read problems",source.Problems);Lines(text,"Selection notes",source.Notes);
            if(!string.IsNullOrEmpty(source.ReadException))text.AppendLine("Selection read exception:\n"+source.ReadException);
            foreach(var element in source.Elements)
            {
                text.AppendLine("Element "+element.SourceId+" | "+element.Label+" | "+element.Status);
                text.AppendLine("  Category: "+element.Category+"; class: "+element.ElementClass+"; family/type: "+element.FamilyType);
                text.AppendLine(element.Details);if(!string.IsNullOrEmpty(element.Exception))text.AppendLine(element.Exception);
            }
            AppendGeometry(text,source);return text.ToString();
        }
        private static void AppendGeometry(StringBuilder text,RevitSelectionSnapshot source)
        {
            text.AppendLine("Geometry values: millimetres; elevations use the Revit model datum.");
            foreach(var b in source.Beams)
            {
                text.AppendLine("Beam "+b.SourceId+" | "+b.Label+" | start="+Point(b.Start)+"; end="+Point(b.End));
                text.AppendLine("  width="+Number(b.WidthMm)+"; depth="+Number(b.DepthMm)+"; soffit="+Number(b.BottomMm)+"; top="+Number(b.BottomMm+b.DepthMm));
                if(!string.IsNullOrEmpty(b.Problem))text.AppendLine("  Read problem: "+b.Problem);
            }
            foreach(var s in source.Slabs)
            {
                text.AppendLine("Slab "+s.SourceId+" | "+s.Label+" | thickness="+Number(s.ThicknessMm)+"; top="+Number(s.TopMm));
                text.AppendLine("  Outline: "+string.Join("; ",(s.Outline??new ImportPoint[0]).Select(Point)));
                if(!string.IsNullOrEmpty(s.Problem))text.AppendLine("  Read problem: "+s.Problem);
            }
            foreach(var b in source.Boundaries)
            {
                text.AppendLine(b.Kind+" "+b.SourceId+" | "+b.Label+" | bottom="+Number(b.BottomMm)+"; top="+Number(b.TopMm));
                text.AppendLine("  Outline: "+string.Join("; ",(b.Outline??new ImportPoint[0]).Select(Point)));
                if(!string.IsNullOrEmpty(b.Problem))text.AppendLine("  Read problem: "+b.Problem);
            }
        }
        internal static string Build(RevitSelectionSnapshot source,string originalSelection,string directionId,RevitStructureImportResult result,
            bool previewCanImport,string reviewState,string previewException,string remarks)
        {
            var text=new StringBuilder();var assembly=typeof(RevitImportDiagnostics).Assembly;
            text.AppendLine("=== REVIT DRAWING IMPORT DIAGNOSTIC REPORT v1 ===");
            text.AppendLine("Captured UTC: "+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
            text.AppendLine("Assembly: "+assembly.GetName().Name+" "+assembly.GetName().Version+"; MVID: "+assembly.ManifestModule.ModuleVersionId);
            text.AppendLine("Runtime: "+Environment.Version+"; OS: "+Environment.OSVersion+"; 64-bit: "+Environment.Is64BitProcess);
            text.AppendLine("Remarks: "+(remarks??""));
            text.AppendLine("Preview status: "+(previewCanImport ? "Ready to import" : "Blocked"));
            text.AppendLine("Chosen main-direction element ID: "+(directionId??"none"));
            text.AppendLine("Geometry tolerance (mm): "+Number(RevitStructureImport.ToleranceMm));
            text.AppendLine("Section and obstruction precision (mm): "+Number(RevitStructureImport.PrecisionMm));
            text.AppendLine("--- Original Revit selection (before review edits) ---");text.AppendLine(originalSelection??CaptureSelection(source));
            text.AppendLine("--- Current reviewed geometry ---");AppendGeometry(text,source);
            text.AppendLine("--- Review table and validation ---");text.AppendLine(reviewState??"No review text available.");
            text.AppendLine("--- Preview exceptions ---");text.AppendLine(string.IsNullOrEmpty(previewException) ? "None" : previewException);
            text.AppendLine("--- Import result ---");
            if(result==null)text.AppendLine("No result was built.");
            else
            {
                text.AppendLine("Geometry builder accepts selection: "+result.CanImport);Lines(text,"Import problems",result.Problems);Lines(text,"Import notes",result.Notes);
                foreach(var area in result.Areas)
                {
                    text.AppendLine("Combined drawing part: "+area.Label+"; direction="+area.DirectionId+"; source IDs="+string.Join(", ",area.SourceIds)+"; ready="+area.Result.CanImport);
                    foreach(var mapping in area.Result.Configuration?.RevitContext?.Sources??new RevitSourceMapping[0])text.AppendLine("  "+mapping.CanvasLabel+" <- "+mapping.SourceId+" | "+mapping.SourceLabel);
                }
                if(result.Configuration!=null)
                {
                    var c=result.Configuration;var context=c.RevitContext;
                    text.AppendLine("Drawing length (mm): "+Number(result.LengthMm));
                    foreach(var b in c.Beams)text.AppendLine(b.Label+" actual span (mm): start="+Number(b.SpanStartMm)+"; end="+Number(b.SpanEnd(result.LengthMm))+"; length="+Number(b.SpanLength(result.LengthMm)));
                    text.AppendLine("Canvas rows: beams="+c.Beams.Length+"; bays="+c.Bays.Length+"; crossings="+c.CrossingBeams.Length);
                    if(context!=null)text.AppendLine("Canvas origin in Revit (mm): "+Point(new ImportPoint(context.OriginXmm,context.OriginYmm))+"; direction="+Point(new ImportPoint(context.DirectionX,context.DirectionY))+"; concrete datum="+Number(context.TopElevationMm));
                    foreach(var s in context?.Sources??new RevitSourceMapping[0])text.AppendLine(s.CanvasLabel+" <- "+s.SourceId+" | "+s.SourceLabel);
                }
            }
            text.AppendLine("=== END REVIT DRAWING IMPORT DIAGNOSTIC REPORT ===");return text.ToString();
        }
    }
}
