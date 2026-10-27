using System;
using System.IO;
using System.Linq;
using Google.OrTools.Sat;
using IxMilia.Dxf;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

internal static class Program
{
    private static int Main(string[] args)
    {
        var model = new CpModel();
        var x = model.NewIntVar(0, 1, "native-load-check");
        model.Add(x == 1);
        var solver = new CpSolver();
        solver.StringParameters = "num_search_workers:1 random_seed:50 max_time_in_seconds:2";
        if (solver.Solve(model) != CpSolverStatus.Optimal || solver.Value(x) != 1) throw new Exception("CP-SAT failed");
        Console.WriteLine("PASS net48 x64 native OR-Tools solve");
        var dxf = DxfFile.Load(args[0]);
        var dimensions = dxf.Entities.Where(e => e.GetType().Name.IndexOf("Dimension", StringComparison.Ordinal) >= 0).ToList();
        if (dimensions.Count != 65) throw new Exception("DXF dimension count: " + dimensions.Count);
        Console.WriteLine("PASS DXF load: " + dimensions.Count + " dimensions");
        Console.WriteLine("Header API: " + string.Join(",", dxf.Header.GetType().GetProperties().Select(p => p.Name).Where(n => n.IndexOf("Unit", StringComparison.OrdinalIgnoreCase)>=0)));
        foreach(var entity in dimensions.Take(2))
        {
            Console.WriteLine(entity.GetType().FullName);
            foreach(var p in entity.GetType().GetProperties().Where(p=>p.GetIndexParameters().Length==0))
                Console.WriteLine(p.Name+"="+p.GetValue(entity));
        }
        var path = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var part = workbook.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(new SheetData());
            workbook.Workbook.Append(new Sheets(new Sheet { Id=workbook.GetIdOfPart(part), SheetId=1, Name="Dependency check" }));
            workbook.Workbook.Save();
        }
        using (var document = SpreadsheetDocument.Open(path, false))
            if (document.WorkbookPart.Workbook.GetFirstChild<Sheets>().Count()!=1) throw new Exception("Workbook round-trip failed");
        Console.WriteLine("PASS Open XML workbook round trip without Office");
        return 0;
    }
}
