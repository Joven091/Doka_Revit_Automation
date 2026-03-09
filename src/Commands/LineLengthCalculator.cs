using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class LineLengthAndAreaCalculator : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // 1. Get selected elements
                var selectedIds = uidoc.Selection.GetElementIds();

                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Smart Area Calculator", "No elements selected.");
                    return Result.Succeeded;
                }

                double totalLengthInternal = 0.0;
                int lineCount = 0;
                List<Curve> allCurves = new List<Curve>();

                // 2. Collect all valid curves and total length
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem != null && elem is CurveElement curveElement)
                    {
                        if (curveElement.GeometryCurve != null)
                        {
                            Curve c = curveElement.GeometryCurve;
                            allCurves.Add(c);
                            totalLengthInternal += c.Length;
                            lineCount++;
                        }
                    }
                }

                if (lineCount == 0)
                {
                    TaskDialog.Show("Smart Area Calculator", "No lines found in the selection.");
                    return Result.Succeeded;
                }

                double totalLengthM = UnitUtils.ConvertFromInternalUnits(totalLengthInternal, UnitTypeId.Meters);
                string dialogMessage = $"Selected Lines: {lineCount}\nTotal Length: {totalLengthM:F2} m\n";

                // 3. Sort curves into multiple closed loops
                List<List<Curve>> closedLoops = SortCurvesIntoClosedLoops(allCurves);
                
                List<double> loopAreas = new List<double>();

                // 4. Calculate the area for each closed loop
                foreach (var loopCurves in closedLoops)
                {
                    try
                    {
                        CurveLoop loop = CurveLoop.Create(loopCurves);
                        if (!loop.IsOpen() && loop.HasPlane())
                        {
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
                            loopAreas.Add(Math.Abs(area) / 2.0);
                        }
                    }
                    catch
                    {
                        // Skip if a grouped list of curves fails to form a valid, planar CurveLoop
                    }
                }

                // 5. Determine Outer Boundary vs Holes
                if (loopAreas.Count > 0)
                {
                    // Sort areas in descending order (largest first)
                    loopAreas = loopAreas.OrderByDescending(a => a).ToList();

                    double outerAreaInternal = loopAreas[0];
                    double holesAreaInternal = 0.0;

                    // Sum up all the smaller loops as holes
                    for (int i = 1; i < loopAreas.Count; i++)
                    {
                        holesAreaInternal += loopAreas[i];
                    }

                    double netAreaInternal = outerAreaInternal - holesAreaInternal;

                    double outerAreaM2 = UnitUtils.ConvertFromInternalUnits(outerAreaInternal, UnitTypeId.SquareMeters);
                    double holesAreaM2 = UnitUtils.ConvertFromInternalUnits(holesAreaInternal, UnitTypeId.SquareMeters);
                    double netAreaM2 = UnitUtils.ConvertFromInternalUnits(netAreaInternal, UnitTypeId.SquareMeters);

                    dialogMessage += $"\n--- Area Calculation ---";
                    dialogMessage += $"\nOuter Boundary: {outerAreaM2:F2} m²";
                    if (loopAreas.Count > 1)
                    {
                        dialogMessage += $"\nSubtracted Holes ({loopAreas.Count - 1}): -{holesAreaM2:F2} m²";
                    }
                    dialogMessage += $"\nNet Area: {netAreaM2:F2} m²";
                }
                else
                {
                    dialogMessage += $"\nNo closed loops were found to calculate area.";
                }

                TaskDialog.Show("Smart Area Calculator", dialogMessage);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Groups a random pile of curves into individual, closed continuous loops.
        /// </summary>
        private List<List<Curve>> SortCurvesIntoClosedLoops(List<Curve> curves)
        {
            List<List<Curve>> allClosedLoops = new List<List<Curve>>();
            if (curves == null || curves.Count < 3) return allClosedLoops; // Need at least 3 lines for a closed loop

            List<Curve> remaining = new List<Curve>(curves);
            double tolerance = 1e-5;

            while (remaining.Count > 0)
            {
                List<Curve> currentLoop = new List<Curve>();
                Curve current = remaining[0];
                currentLoop.Add(current);
                remaining.RemoveAt(0);

                XYZ loopStart = current.GetEndPoint(0);
                XYZ currentEnd = current.GetEndPoint(1);
                bool loopClosed = false;

                while (!loopClosed)
                {
                    bool foundMatch = false;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        Curve candidate = remaining[i];

                        if (candidate.GetEndPoint(0).IsAlmostEqualTo(currentEnd, tolerance))
                        {
                            current = candidate;
                            currentLoop.Add(current);
                            remaining.RemoveAt(i);
                            currentEnd = current.GetEndPoint(1);
                            foundMatch = true;
                            break;
                        }
                        else if (candidate.GetEndPoint(1).IsAlmostEqualTo(currentEnd, tolerance))
                        {
                            current = candidate.CreateReversed();
                            currentLoop.Add(current);
                            remaining.RemoveAt(i);
                            currentEnd = current.GetEndPoint(1);
                            foundMatch = true;
                            break;
                        }
                    }

                    if (!foundMatch)
                    {
                        // We hit a dead end. This chain of lines doesn't close. Break out to start a new loop.
                        break; 
                    }

                    // Check if the end of our current chain touches the very beginning of the loop
                    if (currentEnd.IsAlmostEqualTo(loopStart, tolerance))
                    {
                        allClosedLoops.Add(currentLoop);
                        loopClosed = true;
                    }
                }
            }

            return allClosedLoops;
        }
    }
}