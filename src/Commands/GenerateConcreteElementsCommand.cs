using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class GenerateConcreteElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;

            if (doc == null || uidoc == null || activeView == null)
            {
                message = "Active document or view is required.";
                return Result.Failed;
            }

            Reference cadFileRef = null;
            try
            {
                cadFileRef = uidoc.Selection.PickObject(ObjectType.Element, new ImportInstanceSelectionFilter(), "Please select the linked CAD file.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (cadFileRef == null)
            {
                message = "No CAD file selected.";
                return Result.Failed;
            }

            ImportInstance cadInstance = doc.GetElement(cadFileRef) as ImportInstance;
            if (cadInstance == null)
            {
                message = "The selected element is not an ImportInstance.";
                return Result.Failed;
            }

            Level baseLevel = activeView.GenLevel;
            if (baseLevel == null)
            {
                message = "Active view does not have an associated Level.";
                return Result.Failed;
            }

            Level topLevel = GetNextLevelUp(doc, baseLevel);
            if (topLevel == null)
            {
                message = "Could not find a level above the base level.";
                return Result.Failed;
            }

            // Geometry Extraction
            Options options = new Options();
            GeometryElement geoElement = cadInstance.get_Geometry(options);
            if (geoElement == null)
            {
                message = "Failed to retrieve geometry from CAD instance.";
                return Result.Failed;
            }

            // Keep track of extracted elements by layer
            Dictionary<string, List<Curve>> layerLines = new Dictionary<string, List<Curve>>();
            Dictionary<string, List<CurveLoop>> layerPolylines = new Dictionary<string, List<CurveLoop>>();

            foreach (GeometryObject geoObj in geoElement)
            {
                if (geoObj is GeometryInstance geoInst)
                {
                    GeometryElement instGeo = geoInst.GetInstanceGeometry();
                    foreach (GeometryObject instObj in instGeo)
                    {
                        ProcessGeometryObject(doc, instObj, layerLines, layerPolylines);
                    }
                }
                else
                {
                    ProcessGeometryObject(doc, geoObj, layerLines, layerPolylines);
                }
            }

            // Create Elements
            List<Element> createdBeams = new List<Element>();
            List<Element> createdWalls = new List<Element>();
            List<Element> createdSlabs = new List<Element>();

            using (Transaction t = new Transaction(doc, "Generate Concrete Elements"))
            {
                t.Start();

                // Generate Beams
                foreach (var layer in layerLines.Keys.Where(k => k.StartsWith("FW_BM")))
                {
                    FamilySymbol symbol = FindMatchingType<FamilySymbol>(doc, layer);
                    if (symbol != null && symbol.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                    {
                        if (!symbol.IsActive) symbol.Activate();
                        foreach (Curve curve in layerLines[layer])
                        {
                            FamilyInstance beam = doc.Create.NewFamilyInstance(curve, symbol, baseLevel, StructuralType.Beam);
                            Parameter zJustification = beam.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
                            // 0 is Top, 1 is Center, 2 is Bottom, 3 is Other in most Revit versions. Use 0 for Top justification
                            if (zJustification != null) zJustification.Set(0);
                            createdBeams.Add(beam);
                        }
                    }
                }

                // Generate Columns
                foreach (var layer in layerLines.Keys.Where(k => k.StartsWith("FW_COL")))
                {
                    FamilySymbol symbol = FindMatchingType<FamilySymbol>(doc, layer);
                    if (symbol != null && symbol.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                    {
                        if (!symbol.IsActive) symbol.Activate();
                        foreach (Curve curve in layerLines[layer])
                        {
                            XYZ location = (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2;
                            FamilyInstance col = doc.Create.NewFamilyInstance(location, symbol, baseLevel, StructuralType.Column);

                            Parameter topLevelParam = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                            if (topLevelParam != null) topLevelParam.Set(topLevel.Id);

                            Parameter baseLevelParam = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                            if (baseLevelParam != null) baseLevelParam.Set(baseLevel.Id);
                        }
                    }
                }

                // Generate Walls
                foreach (var layer in layerLines.Keys.Where(k => k.StartsWith("FW_WALL")))
                {
                    WallType type = FindMatchingType<WallType>(doc, layer);
                    if (type != null)
                    {
                        foreach (Curve curve in layerLines[layer])
                        {
                            Wall wall = Wall.Create(doc, curve, type.Id, baseLevel.Id, 10, 0, false, true);
                            Parameter topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                            if (topConstraint != null) topConstraint.Set(topLevel.Id);
                            createdWalls.Add(wall);
                        }
                    }
                }

                // Generate Slabs
                FloorType slabType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(); // Replace with generic floor or match layer if floor types are specified

                List<CurveLoop> perimeterLoops = new List<CurveLoop>();
                List<CurveLoop> voidLoops = new List<CurveLoop>();

                foreach (var layer in layerPolylines.Keys)
                {
                    if (layer.Contains("FW_SLAB PERIMETER")) perimeterLoops.AddRange(layerPolylines[layer]);
                    else if (layer.Contains("FW_SLAB VOID")) voidLoops.AddRange(layerPolylines[layer]);
                }

                // Simplified Ray-Casting algorithm alternative: Just pairing every inner void with outer if there is only 1.
                // Creating Floor with the outer loop and adding inner void.
                // Since `Floor.Create` takes an `IList<CurveLoop>`, we can group them up.
                if (perimeterLoops.Count > 0 && slabType != null)
                {
                    foreach (CurveLoop perimeterLoop in perimeterLoops)
                    {
                        IList<CurveLoop> loopsToCreate = new List<CurveLoop> { perimeterLoop };

                        // Check if voids belong to this perimeter using bounding box approach or raycasting
                        // For simplicity in this structure: add all voids (assumes voids correctly mapped or simple case)
                        foreach (CurveLoop voidLoop in voidLoops)
                        {
                            // A proper point-in-polygon ray casting algorithm should ideally be here.
                            if (IsInside(perimeterLoop, voidLoop.First().GetEndPoint(0)))
                            {
                                loopsToCreate.Add(voidLoop);
                            }
                        }

                        Floor slab = Floor.Create(doc, loopsToCreate, slabType.Id, baseLevel.Id);
                        createdSlabs.Add(slab);
                    }
                }

                // Auto-Join Geometry
                foreach (Element slab in createdSlabs)
                {
                    foreach (Element beam in createdBeams)
                    {
                        try
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, slab, beam))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, slab, beam);
                            }
                        }
                        catch (Exception) { /* Ignore if join fails due to non-intersection */ }
                    }

                    foreach (Element wall in createdWalls)
                    {
                        try
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, slab, wall))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, slab, wall);
                            }
                        }
                        catch (Exception) { /* Ignore if join fails due to non-intersection */ }
                    }
                }

                t.Commit();
            }

            return Result.Succeeded;
        }

        private bool IsInside(CurveLoop polygon, XYZ pt)
        {
            // Ray-Casting Algorithm (Even-Odd rule) for 2D X-Y plane
            int intersections = 0;

            foreach (Curve curve in polygon)
            {
                if (curve is Line line)
                {
                    XYZ p1 = line.GetEndPoint(0);
                    XYZ p2 = line.GetEndPoint(1);

                    // Check if point is exactly on the line (ignoring for inside test)
                    if (Math.Abs(p1.Y - p2.Y) < 0.0001 && Math.Abs(pt.Y - p1.Y) < 0.0001)
                    {
                        if (pt.X >= Math.Min(p1.X, p2.X) && pt.X <= Math.Max(p1.X, p2.X))
                            return true; // Point is on horizontal boundary
                        continue;
                    }

                    if (pt.Y > Math.Min(p1.Y, p2.Y) && pt.Y <= Math.Max(p1.Y, p2.Y))
                    {
                        if (pt.X <= Math.Max(p1.X, p2.X))
                        {
                            if (p1.Y != p2.Y)
                            {
                                double xInters = (pt.Y - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y) + p1.X;
                                if (p1.X == p2.X || pt.X <= xInters)
                                {
                                    intersections++;
                                }
                            }
                        }
                    }
                }
                // Ignoring arcs for now or assume lines. CAD polylines converted to CurveLoops are mostly lines here.
            }
            return intersections % 2 != 0;
        }

        private void ProcessGeometryObject(Document doc, GeometryObject geoObj, Dictionary<string, List<Curve>> layerLines, Dictionary<string, List<CurveLoop>> layerPolylines)
        {
            if (geoObj.GraphicsStyleId == ElementId.InvalidElementId) return;

            GraphicsStyle style = doc.GetElement(geoObj.GraphicsStyleId) as GraphicsStyle;
            if (style == null || style.GraphicsStyleCategory == null) return;

            string layerName = style.GraphicsStyleCategory.Name;

            if (geoObj is Line line)
            {
                if (!layerLines.ContainsKey(layerName)) layerLines[layerName] = new List<Curve>();

                // Convert to feet
                XYZ start = ConvertToFeet(line.GetEndPoint(0));
                XYZ end = ConvertToFeet(line.GetEndPoint(1));
                if (start.DistanceTo(end) > 0.001) // Prevent zero length lines
                {
                    layerLines[layerName].Add(Line.CreateBound(start, end));
                }
            }
            else if (geoObj is PolyLine polyline)
            {
                if (!layerPolylines.ContainsKey(layerName)) layerPolylines[layerName] = new List<CurveLoop>();

                CurveLoop loop = new CurveLoop();
                IList<XYZ> points = polyline.GetCoordinates();
                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ start = ConvertToFeet(points[i]);
                    XYZ end = ConvertToFeet(points[i + 1]);
                    if (start.DistanceTo(end) > 0.001)
                    {
                        loop.Append(Line.CreateBound(start, end));
                    }
                }

                // Close the loop if necessary
                XYZ firstPt = ConvertToFeet(points[0]);
                XYZ lastPt = ConvertToFeet(points[points.Count - 1]);
                if (firstPt.DistanceTo(lastPt) > 0.001)
                {
                    loop.Append(Line.CreateBound(lastPt, firstPt));
                }

                layerPolylines[layerName].Add(loop);
            }
            // Add support for Arcs and other curves if needed
        }

        private XYZ ConvertToFeet(XYZ pt)
        {
            // The constraint states CAD is in mm, must convert to Revit internal units (feet)
            double conversionFactor = 304.8;
            return new XYZ(pt.X / conversionFactor, pt.Y / conversionFactor, pt.Z / conversionFactor);
        }

        private Level GetNextLevelUp(Document doc, Level baseLevel)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(l => l.Elevation > baseLevel.Elevation)
                .OrderBy(l => l.Elevation)
                .FirstOrDefault();
        }

        private T FindMatchingType<T>(Document doc, string layerName) where T : ElementType
        {
            // Extract all numbers from the layer name, e.g. FW_BM_250X800 -> "250", "800"
            var matches = Regex.Matches(layerName, @"\d+");
            List<string> numbers = new List<string>();
            foreach (Match match in matches)
            {
                numbers.Add(match.Value);
            }

            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(T))
                .Cast<T>()
                .ToList();

            foreach (var type in types)
            {
                bool allMatch = true;
                foreach (var number in numbers)
                {
                    if (!type.Name.Contains(number))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch && numbers.Count > 0)
                {
                    return type;
                }
            }
            return null;
        }
    }

    public class ImportInstanceSelectionFilter : ISelectionFilter
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
