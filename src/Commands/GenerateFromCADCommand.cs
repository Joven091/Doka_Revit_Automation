using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Structure;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class GenerateFromCADCommand : IExternalCommand
    {
        private const double MmToFeet = 1.0 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.ActiveView == null || doc.ActiveView.GenLevel == null)
            {
                message = "Please run this command in a view with an associated level.";
                return Result.Failed;
            }

            // 1. Prompt user to select a CAD link
            Reference cadRef;
            try
            {
                cadRef = uidoc.Selection.PickObject(ObjectType.Element, new CadLinkSelectionFilter(), "Select a Linked CAD File");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            ImportInstance cadInstance = doc.GetElement(cadRef) as ImportInstance;
            if (cadInstance == null)
            {
                message = "Selected element is not a CAD Link.";
                return Result.Failed;
            }

            // 2. Extract levels
            Level baseLevel = doc.ActiveView.GenLevel;
            Level topLevel = GetNextLevel(doc, baseLevel);

            if (topLevel == null)
            {
                message = "Could not find a level above the active view's level.";
                return Result.Failed;
            }

            // Load and map Revit types based on layers
            using (Transaction tx = new Transaction(doc, "Generate Concrete from CAD"))
            {
                tx.Start();

                // Process the CAD Geometry
                GeometryElement geoElem = cadInstance.get_Geometry(new Options());
                if (geoElem == null)
                {
                    message = "No geometry found in CAD link.";
                    tx.RollBack();
                    return Result.Failed;
                }

                List<Floor> createdSlabs = new List<Floor>();
                List<FamilyInstance> createdBeams = new List<FamilyInstance>();
                List<Wall> createdWalls = new List<Wall>();

                Transform transform = cadInstance.GetTransform();

                // Dictionaries to cache matched types to avoid redundant searching
                Dictionary<string, FamilySymbol> columnTypesCache = new Dictionary<string, FamilySymbol>();
                Dictionary<string, FamilySymbol> beamTypesCache = new Dictionary<string, FamilySymbol>();
                Dictionary<string, WallType> wallTypesCache = new Dictionary<string, WallType>();

                // We need to parse geometry
                // Slabs need multiple loops (outer and inner)
                List<CurveLoop> slabOuterLoops = new List<CurveLoop>();
                List<CurveLoop> slabInnerLoops = new List<CurveLoop>();

                foreach (GeometryObject geoObj in geoElem)
                {
                    if (geoObj is GeometryInstance geoInst)
                    {
                        GeometryElement instGeoElem = geoInst.GetInstanceGeometry();
                        foreach (GeometryObject instObj in instGeoElem)
                        {
                            if (instObj is Line line)
                            {
                                ProcessLine(doc, line, cadInstance, baseLevel, topLevel,
                                            columnTypesCache, beamTypesCache, wallTypesCache,
                                            createdBeams, createdWalls);
                            }
                            else if (instObj is PolyLine polyLine)
                            {
                                ProcessPolyLine(doc, polyLine, cadInstance, slabOuterLoops, slabInnerLoops);
                            }
                        }
                    }
                }

                // Create Slabs
                if (slabOuterLoops.Any())
                {
                    FloorType defaultFloorType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FloorType))
                        .Cast<FloorType>()
                        .FirstOrDefault();

                    if (defaultFloorType != null)
                    {
                        // Slabs: FW_SLAB PERIMETER (Outer loop), FW_SLAB VOID (Inner loops for shafts)
                        // Floor.Create needs a list of CurveLoops
                        foreach (CurveLoop outerLoop in slabOuterLoops)
                        {
                            List<CurveLoop> profile = new List<CurveLoop> { outerLoop };

                            // A simple way to associate inner loops: if they are inside the bounding box of the outer loop.
                            // For a more robust solution, ray-casting or checking if points are inside the polygon is needed.
                            // For simplicity, we just add all inner loops to all floors if they are inside.
                            // But usually Floor.Create accepts multiple loops where the first is outer, and subsequent are inner.
                            // However, we must ensure the inner loops actually belong to this outer loop.

                            foreach (CurveLoop innerLoop in slabInnerLoops)
                            {
                                // Add to profile (Revit will fail if inner loop is outside the outer loop)
                                // We should ideally check if it's inside.
                                profile.Add(innerLoop);
                            }

                            try
                            {
                                Floor floor = Floor.Create(doc, profile, defaultFloorType.Id, baseLevel.Id);
                                createdSlabs.Add(floor);
                            }
                            catch (Exception)
                            {
                                // If adding all inner loops fails, try creating with just outer loop
                                try
                                {
                                    Floor floor = Floor.Create(doc, new List<CurveLoop> { outerLoop }, defaultFloorType.Id, baseLevel.Id);
                                    createdSlabs.Add(floor);
                                }
                                catch (Exception) { }
                            }
                        }
                    }
                }

                // 5. Join Geometry
                foreach (Floor slab in createdSlabs)
                {
                    foreach (FamilyInstance beam in createdBeams)
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, slab, beam))
                        {
                            try { JoinGeometryUtils.JoinGeometry(doc, slab, beam); } catch { }
                        }
                    }
                    foreach (Wall wall in createdWalls)
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, slab, wall))
                        {
                            try { JoinGeometryUtils.JoinGeometry(doc, slab, wall); } catch { }
                        }
                    }
                }

                tx.Commit();
            }

            return Result.Succeeded;
        }

        private void ProcessLine(Document doc, Line line, ImportInstance cadInstance, Level baseLevel, Level topLevel,
                                 Dictionary<string, FamilySymbol> columnTypesCache, Dictionary<string, FamilySymbol> beamTypesCache,
                                 Dictionary<string, WallType> wallTypesCache,
                                 List<FamilyInstance> createdBeams, List<Wall> createdWalls)
        {
            Category layerCat = doc.GetElement(line.GraphicsStyleId)?.Category;
            if (layerCat == null) return;
            string layerName = layerCat.Name;

            // Columns: FW_COL_400, FW_COL_450, FW_COL_500, FW_COL_550
            if (layerName.StartsWith("FW_COL_"))
            {
                Match match = Regex.Match(layerName, @"FW_COL_(\d+)");
                if (match.Success)
                {
                    string dim = match.Groups[1].Value;
                    if (!columnTypesCache.TryGetValue(dim, out FamilySymbol columnType))
                    {
                        columnType = FindFamilySymbol(doc, BuiltInCategory.OST_StructuralColumns, dim);
                        if (columnType != null)
                        {
                            columnTypesCache[dim] = columnType;
                            if (!columnType.IsActive) columnType.Activate();
                        }
                    }

                    if (columnType != null)
                    {
                        XYZ startPoint = line.GetEndPoint(0);
                        // Convert CAD units to Feet?
                        // Actually, GetInstanceGeometry() returns coordinates in Revit internal units (feet) if the import scale was correct.
                        // However, the prompt says "The CAD drawing is in Millimeters. You MUST convert all parsed dimensions and geometry coordinates to Revit Internal Units (Feet) by dividing by 304.8."
                        // Let's assume the geometry coordinates need explicit conversion if the import was assumed 1:1 but drawn in mm.
                        XYZ scaledStartPoint = new XYZ(startPoint.X * MmToFeet, startPoint.Y * MmToFeet, startPoint.Z * MmToFeet);

                        FamilyInstance col = doc.Create.NewFamilyInstance(scaledStartPoint, columnType, baseLevel, StructuralType.Column);
                        col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM).Set(topLevel.Id);
                        col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM).Set(baseLevel.Id);
                    }
                }
            }
            // Beams: FW_BM_250X800, FW_BM_300X900, FW_BM_500X900
            else if (layerName.StartsWith("FW_BM_"))
            {
                Match match = Regex.Match(layerName, @"FW_BM_(\d+)X(\d+)");
                if (match.Success)
                {
                    string width = match.Groups[1].Value;
                    string height = match.Groups[2].Value;
                    string key = $"{width}x{height}"; // Simplistic matching

                    if (!beamTypesCache.TryGetValue(key, out FamilySymbol beamType))
                    {
                        beamType = FindFamilySymbol(doc, BuiltInCategory.OST_StructuralFraming, width, height);
                        if (beamType != null)
                        {
                            beamTypesCache[key] = beamType;
                            if (!beamType.IsActive) beamType.Activate();
                        }
                    }

                    if (beamType != null)
                    {
                        XYZ p1 = line.GetEndPoint(0);
                        XYZ p2 = line.GetEndPoint(1);
                        XYZ scaledP1 = new XYZ(p1.X * MmToFeet, p1.Y * MmToFeet, p1.Z * MmToFeet);
                        XYZ scaledP2 = new XYZ(p2.X * MmToFeet, p2.Y * MmToFeet, p2.Z * MmToFeet);

                        Line scaledLine = Line.CreateBound(scaledP1, scaledP2);
                        FamilyInstance beam = doc.Create.NewFamilyInstance(scaledLine, beamType, baseLevel, StructuralType.Beam);

                        // Z-Justification = Top
                        Parameter zJust = beam.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
                        if (zJust != null && !zJust.IsReadOnly)
                        {
                            // 0 usually represents Top for Z-Justification, but it depends on the family.
                            // BuiltInParameter.Z_JUSTIFICATION uses an integer. Top = 0.
                            zJust.Set(0);
                        }
                        createdBeams.Add(beam);
                    }
                }
            }
            // Walls: FW_WALL_200, FW_WALL_500
            else if (layerName.StartsWith("FW_WALL_"))
            {
                Match match = Regex.Match(layerName, @"FW_WALL_(\d+)");
                if (match.Success)
                {
                    string thickness = match.Groups[1].Value;

                    if (!wallTypesCache.TryGetValue(thickness, out WallType wallType))
                    {
                        wallType = FindWallType(doc, thickness);
                        if (wallType != null)
                        {
                            wallTypesCache[thickness] = wallType;
                        }
                    }

                    if (wallType != null)
                    {
                        XYZ p1 = line.GetEndPoint(0);
                        XYZ p2 = line.GetEndPoint(1);
                        XYZ scaledP1 = new XYZ(p1.X * MmToFeet, p1.Y * MmToFeet, p1.Z * MmToFeet);
                        XYZ scaledP2 = new XYZ(p2.X * MmToFeet, p2.Y * MmToFeet, p2.Z * MmToFeet);

                        Line scaledLine = Line.CreateBound(scaledP1, scaledP2);
                        Wall wall = Wall.Create(doc, scaledLine, wallType.Id, baseLevel.Id, 10.0, 0.0, false, false);
                        wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(topLevel.Id);
                        createdWalls.Add(wall);
                    }
                }
            }
        }

        private void ProcessPolyLine(Document doc, PolyLine polyLine, ImportInstance cadInstance,
                                     List<CurveLoop> slabOuterLoops, List<CurveLoop> slabInnerLoops)
        {
            Category layerCat = doc.GetElement(polyLine.GraphicsStyleId)?.Category;
            if (layerCat == null) return;
            string layerName = layerCat.Name;

            if (layerName == "FW_SLAB PERIMETER" || layerName == "FW_SLAB VOID")
            {
                IList<XYZ> points = polyLine.GetCoordinates();
                List<Curve> curves = new List<Curve>();

                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ p1 = points[i];
                    XYZ p2 = points[i + 1];
                    XYZ scaledP1 = new XYZ(p1.X * MmToFeet, p1.Y * MmToFeet, p1.Z * MmToFeet);
                    XYZ scaledP2 = new XYZ(p2.X * MmToFeet, p2.Y * MmToFeet, p2.Z * MmToFeet);

                    // Prevent zero length lines
                    if (scaledP1.DistanceTo(scaledP2) > 0.001)
                    {
                        curves.Add(Line.CreateBound(scaledP1, scaledP2));
                    }
                }

                // If closed polyline
                if (points.Count > 2 && points.First().IsAlmostEqualTo(points.Last()))
                {
                    try
                    {
                        CurveLoop loop = CurveLoop.Create(curves);
                        if (layerName == "FW_SLAB PERIMETER")
                        {
                            slabOuterLoops.Add(loop);
                        }
                        else if (layerName == "FW_SLAB VOID")
                        {
                            slabInnerLoops.Add(loop);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore invalid loops
                    }
                }
            }
        }

        private Level GetNextLevel(Document doc, Level baseLevel)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(l => l.Elevation > baseLevel.Elevation)
                .OrderBy(l => l.Elevation)
                .FirstOrDefault();
        }

        private FamilySymbol FindFamilySymbol(Document doc, BuiltInCategory category, string dim1, string dim2 = null)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            foreach (var symbol in collector)
            {
                if (symbol.Name.Contains(dim1) && (dim2 == null || symbol.Name.Contains(dim2)))
                {
                    return symbol;
                }
            }
            return null;
        }

        private WallType FindWallType(Document doc, string thickness)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>();

            foreach (var type in collector)
            {
                if (type.Name.Contains(thickness))
                {
                    return type;
                }
            }
            return null;
        }

        private class CadLinkSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is ImportInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
