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
                List<CurveLoop> validCurveLoops = new List<CurveLoop>();
                List<double> loopAreas = new List<double>();

                // 4. Create proper CurveLoops and compute their individual areas
                foreach (var loopCurves in closedLoops)
                {
                    try
                    {
                        CurveLoop loop = CurveLoop.Create(loopCurves);
                        if (!loop.IsOpen() && loop.HasPlane())
                        {
                            validCurveLoops.Add(loop);
                            loopAreas.Add(CalculateLoopArea(loop));
                        }
                    }
                    catch
                    {
                        // Skip if a grouped list of curves fails to form a valid, planar CurveLoop
                    }
                }

                // 5. Determine Outer Boundaries vs Holes via Polygon Containment Test
                if (validCurveLoops.Count > 0)
                {
                    double totalOuterAreaInternal = 0.0;
                    double totalHolesAreaInternal = 0.0;
                    int holesCount = 0;
                    int outerCount = 0;

                    for (int i = 0; i < validCurveLoops.Count; i++)
                    {
                        CurveLoop currentLoop = validCurveLoops[i];
                        int containingLoopsCount = 0;

                        // Check how many other loops contain this loop
                        for (int j = 0; j < validCurveLoops.Count; j++)
                        {
                            if (i == j) continue; // Don't check against itself

                            CurveLoop otherLoop = validCurveLoops[j];
                            if (IsLoopInsideOtherLoop(currentLoop, otherLoop))
                            {
                                containingLoopsCount++;
                            }
                        }

                        // If it's contained inside an Even number of loops (0, 2, 4), it's an OUTER boundary
                        // If it's contained inside an Odd number of loops (1, 3, 5), it's a HOLE
                        if (containingLoopsCount % 2 == 0)
                        {
                            totalOuterAreaInternal += loopAreas[i];
                            outerCount++;
                        }
                        else
                        {
                            totalHolesAreaInternal += loopAreas[i];
                            holesCount++;
                        }
                    }

                    double netAreaInternal = totalOuterAreaInternal - totalHolesAreaInternal;

                    double outerAreaM2 = UnitUtils.ConvertFromInternalUnits(totalOuterAreaInternal, UnitTypeId.SquareMeters);
                    double holesAreaM2 = UnitUtils.ConvertFromInternalUnits(totalHolesAreaInternal, UnitTypeId.SquareMeters);
                    double netAreaM2 = UnitUtils.ConvertFromInternalUnits(netAreaInternal, UnitTypeId.SquareMeters);

                    dialogMessage += $"\n--- Area Calculation ---";
                    dialogMessage += $"\nOuter Boundaries ({outerCount}): {outerAreaM2:F2} m²";
                    if (holesCount > 0)
                    {
                        dialogMessage += $"\nSubtracted Holes ({holesCount}): -{holesAreaM2:F2} m²";
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
        /// Optimized to handle unorganized and disconnected segments more robustly.
        /// </summary>
        private List<List<Curve>> SortCurvesIntoClosedLoops(List<Curve> curves)
        {
            List<List<Curve>> allClosedLoops = new List<List<Curve>>();
            if (curves == null || curves.Count < 3) return allClosedLoops;

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

                while (!loopClosed && remaining.Count > 0)
                {
                    bool foundMatch = false;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        Curve candidate = remaining[i];

                        // Check if candidate start matches current end
                        if (candidate.GetEndPoint(0).IsAlmostEqualTo(currentEnd, tolerance))
                        {
                            current = candidate;
                            currentLoop.Add(current);
                            remaining.RemoveAt(i);
                            currentEnd = current.GetEndPoint(1);
                            foundMatch = true;
                            break;
                        }
                        // Check if candidate end matches current end (requires reversal)
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
                        // Try to search backwards from loopStart to see if we missed connecting lines
                        // before breaking out to start a new loop
                        bool foundBackwardsMatch = false;
                        for (int i = 0; i < remaining.Count; i++)
                        {
                            Curve candidate = remaining[i];
                            if (candidate.GetEndPoint(1).IsAlmostEqualTo(loopStart, tolerance))
                            {
                                current = candidate;
                                currentLoop.Insert(0, current);
                                remaining.RemoveAt(i);
                                loopStart = current.GetEndPoint(0);
                                foundBackwardsMatch = true;
                                break;
                            }
                            else if (candidate.GetEndPoint(0).IsAlmostEqualTo(loopStart, tolerance))
                            {
                                current = candidate.CreateReversed();
                                currentLoop.Insert(0, current);
                                remaining.RemoveAt(i);
                                loopStart = current.GetEndPoint(0);
                                foundBackwardsMatch = true;
                                break;
                            }
                        }

                        if (!foundBackwardsMatch)
                        {
                            break; // Truly dead end
                        }
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

        /// <summary>
        /// Calculates the area of a closed, planar CurveLoop using Green's Theorem.
        /// </summary>
        private double CalculateLoopArea(CurveLoop loop)
        {
            if (loop == null || loop.IsOpen() || !loop.HasPlane()) return 0.0;

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
            return Math.Abs(area) / 2.0;
        }

        /// <summary>
        /// Checks if an inner loop is completely contained within an outer loop using Ray Casting.
        /// It projects points onto the local 2D plane of the outer loop to handle any view orientation.
        /// </summary>
        private bool IsLoopInsideOtherLoop(CurveLoop innerLoop, CurveLoop outerLoop)
        {
            if (!outerLoop.HasPlane()) return false;

            Plane plane = outerLoop.GetPlane();
            Transform transformToLocal = Transform.Identity;
            transformToLocal.BasisX = plane.XVec;
            transformToLocal.BasisY = plane.YVec;
            transformToLocal.BasisZ = plane.Normal;
            transformToLocal.Origin = plane.Origin;

            Transform inverseTransform = transformToLocal.Inverse;

            // Pick a point on the inner loop to cast a ray from.
            // We use the midpoint of the first curve to minimize chances of hitting vertices exactly.
            Curve firstInnerCurve = innerLoop.ElementAt(0);
            XYZ testPoint3D = firstInnerCurve.Evaluate(0.5, true);
            XYZ testPoint = inverseTransform.OfPoint(testPoint3D);

            int intersectionCount = 0;

            foreach (Curve curve in outerLoop)
            {
                IList<XYZ> pts3D = curve.Tessellate();
                for (int i = 0; i < pts3D.Count - 1; i++)
                {
                    XYZ p1 = inverseTransform.OfPoint(pts3D[i]);
                    XYZ p2 = inverseTransform.OfPoint(pts3D[i + 1]);

                    // Check if the test ray (horizontal, +X direction in local coordinates) intersects the line segment p1-p2
                    // A point is strictly inside if it has an odd number of intersections

                    // We check if the segment crosses the local Y level of the testPoint
                    if ((p1.Y > testPoint.Y) != (p2.Y > testPoint.Y))
                    {
                        // Calculate local X intersection of the segment with the ray's Y
                        double intersectX = p1.X + (testPoint.Y - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y);

                        // If intersection is to the right of testPoint, it counts
                        if (intersectX > testPoint.X)
                        {
                            intersectionCount++;
                        }
                    }
                }
            }

            // Odd number of intersections means the point is inside the polygon
            return intersectionCount % 2 != 0;
        }
    }
}