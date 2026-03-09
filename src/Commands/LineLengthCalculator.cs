using System;
using System.Collections.Generic;
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
                List<Curve> curves = new List<Curve>();

                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem != null && elem is CurveElement curveElement)
                    {
                        if (curveElement.GeometryCurve != null)
                        {
                            Curve c = curveElement.GeometryCurve;
                            curves.Add(c);
                            totalLengthInternal += c.Length;
                            lineCount++;
                        }
                    }
                }

                if (lineCount == 0)
                {
                    TaskDialog.Show("Line Length Calculator", "No lines (CurveElements) found in the selection.");
                    return Result.Succeeded;
                }

                // Convert from internal units (feet) to meters
                double totalLengthM = UnitUtils.ConvertFromInternalUnits(totalLengthInternal, UnitTypeId.Meters);

                string dialogMessage = $"Selected Lines: {lineCount}\nTotal Length: {totalLengthM:F2} m";

                bool isClosed = false;
                double areaInternal = 0.0;

                try
                {
                    // Attempt to create a curve loop. If they form a continuous loop, this might succeed.
                    // Often curves need to be ordered.
                    CurveLoop loop = CurveLoop.Create(curves);
                    if (!loop.IsOpen() && loop.HasPlane())
                    {
                        isClosed = true;

                        // We can calculate area via Green's Theorem for a planar loop
                        // Need the normal of the plane to ensure correct sign or just take absolute value.
                        XYZ normal = loop.GetPlane().Normal;

                        double area = 0.0;
                        foreach (Curve curve in loop)
                        {
                            IList<XYZ> pts = curve.Tessellate();
                            for (int i = 0; i < pts.Count - 1; i++)
                            {
                                XYZ p1 = pts[i];
                                XYZ p2 = pts[i + 1];
                                XYZ cross = p1.CrossProduct(p2);
                                area += cross.DotProduct(normal);
                            }
                        }
                        areaInternal = Math.Abs(area) / 2.0;
                    }
                }
                catch
                {
                    // Catch exception if curves do not form a loop
                }

                if (isClosed)
                {
                    double areaM2 = UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareMeters);
                    dialogMessage += $"\nArea: {areaM2:F2} m²";
                }

                TaskDialog.Show("Line Length Calculator", dialogMessage);

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
