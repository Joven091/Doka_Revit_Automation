using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class LineLengthCalculator : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // Get selected elements
                var selectedIds = uidoc.Selection.GetElementIds();

                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Line Length Calculator", "No elements selected.");
                    return Result.Succeeded;
                }

                double totalLengthInternal = 0.0;
                int lineCount = 0;

                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem != null && elem is CurveElement curveElement)
                    {
                        if (curveElement.GeometryCurve != null)
                        {
                            totalLengthInternal += curveElement.GeometryCurve.Length;
                            lineCount++;
                        }
                    }
                }

                if (lineCount == 0)
                {
                    TaskDialog.Show("Line Length Calculator", "No lines (CurveElements) found in the selection.");
                    return Result.Succeeded;
                }

                // Convert from internal units (feet) to millimeters
                double totalLengthMm = UnitUtils.ConvertFromInternalUnits(totalLengthInternal, UnitTypeId.Millimeters);

                TaskDialog.Show("Line Length Calculator",
                    $"Selected Lines: {lineCount}\nTotal Length: {totalLengthMm:F2} mm");

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
