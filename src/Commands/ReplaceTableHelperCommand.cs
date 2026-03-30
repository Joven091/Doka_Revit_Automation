using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ReplaceTableHelperCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // 1. Find all instances of "Table Helper" family
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var tableHelpers = collector.OfClass(typeof(FamilyInstance))
                                            .Cast<FamilyInstance>()
                                            .Where(fi => fi.Symbol != null &&
                                                         fi.Symbol.Family != null &&
                                                         fi.Symbol.Family.Name.Equals("Table Helper", StringComparison.OrdinalIgnoreCase))
                                            .ToList();

                if (tableHelpers.Count == 0)
                {
                    TaskDialog.Show("Replace Table Helper", "No 'Table Helper' families found in the project.");
                    return Result.Succeeded;
                }

                // Collect all other family symbols in the project
                FilteredElementCollector symbolCollector = new FilteredElementCollector(doc);
                var allFamilySymbols = symbolCollector.OfClass(typeof(FamilySymbol))
                                                      .Cast<FamilySymbol>()
                                                      .ToList();

                using (Transaction t = new Transaction(doc, "Replace Table Helpers"))
                {
                    t.Start();

                    int replaceCount = 0;

                    foreach (var helper in tableHelpers)
                    {
                        // 2. Get Length and Width parameters (Check Instance first, then fallback to Type)
                        Parameter lengthParam = helper.LookupParameter("Length") ?? helper.Symbol.LookupParameter("Length");
                        Parameter widthParam = helper.LookupParameter("Width") ?? helper.Symbol.LookupParameter("Width");

                        if (lengthParam == null || widthParam == null)
                        {
                            continue; // Skip if parameters are missing entirely
                        }

                        // Revit internal units are in feet. Convert to centimeters.
                        double lengthFt = lengthParam.AsDouble();
                        double widthFt = widthParam.AsDouble();

                        // 1 foot = 30.48 cm
                        int lengthCm = (int)Math.Round(lengthFt * 30.48);
                        int widthCm = (int)Math.Round(widthFt * 30.48);

                        // 3. Find matching family or type named "T-{Length}x{Width}" in cm
                        string targetFamilyName = $"T-{lengthCm}x{widthCm}";

                        // Check if either the Type Name (Symbol Name) OR the Family Name matches
                        var matchingSymbol = allFamilySymbols.FirstOrDefault(fs =>
                            fs.Name.Equals(targetFamilyName, StringComparison.OrdinalIgnoreCase) ||
                            (fs.Family != null && fs.Family.Name.Equals(targetFamilyName, StringComparison.OrdinalIgnoreCase)));

                        if (matchingSymbol != null)
                        {
                            // Ensure the symbol is active before using it
                            if (!matchingSymbol.IsActive)
                            {
                                matchingSymbol.Activate();
                                doc.Regenerate(); 
                            }

                            // 4. Replace using the safer ChangeTypeId method
                            if (helper.IsValidType(matchingSymbol.Id))
                            {
                                helper.ChangeTypeId(matchingSymbol.Id);
                                replaceCount++;
                            }
                        }
                    }

                    t.Commit();

                    TaskDialog.Show("Replace Table Helper", $"Successfully replaced {replaceCount} Table Helpers.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}