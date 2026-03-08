using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class PourBoxIsolator : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            var selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("Error", "Please select at least one element.");
                return Result.Cancelled;
            }

            XYZ minBound = null;
            XYZ maxBound = null;

            foreach (var id in selectedIds)
            {
                var element = doc.GetElement(id);
                if (element == null) continue;

                // Process element and nested elements if any (for nested families)
                ProcessElementBoundingBox(element, ref minBound, ref maxBound);
            }

            if (minBound == null || maxBound == null)
            {
                TaskDialog.Show("Error", "Could not determine bounding box for selected elements.");
                return Result.Failed;
            }

            // Expand by 500mm in all directions
            double expansion = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
            XYZ expansionVec = new XYZ(expansion, expansion, expansion);
            XYZ newMin = minBound - expansionVec;
            XYZ newMax = maxBound + expansionVec;

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
            {
                Min = newMin,
                Max = newMax
            };

            using (Transaction t = new Transaction(doc, "Isolate Pour Box"))
            {
                t.Start();

                // Find 3D ViewFamilyType
                var viewFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                if (viewFamilyType == null)
                {
                    TaskDialog.Show("Error", "Could not find a 3D ViewFamilyType.");
                    t.RollBack();
                    return Result.Failed;
                }

                // Create 3D View
                View3D view3D = View3D.CreateIsometric(doc, viewFamilyType.Id);

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                view3D.Name = $"Pour Isolation - {dateStr}";

                // Set Section Box
                view3D.IsSectionBoxActive = true;
                view3D.SetSectionBox(sectionBox);

                // Set detail level or other properties if necessary...

                t.Commit();

                uidoc.ActiveView = view3D;
            }

            return Result.Succeeded;
        }

        private void ProcessElementBoundingBox(Element element, ref XYZ minBound, ref XYZ maxBound)
        {
            BoundingBoxXYZ bbox = element.get_BoundingBox(null);

            if (bbox != null)
            {
                // BoundingBoxXYZ may have a transform applied to it.
                // We should transform its corners to world coordinates to get the true axis-aligned bounding box.
                Transform transform = bbox.Transform;

                XYZ[] corners = new XYZ[8];
                corners[0] = transform.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z));
                corners[1] = transform.OfPoint(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z));
                corners[2] = transform.OfPoint(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z));
                corners[3] = transform.OfPoint(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z));
                corners[4] = transform.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z));
                corners[5] = transform.OfPoint(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z));
                corners[6] = transform.OfPoint(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z));
                corners[7] = transform.OfPoint(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z));

                foreach (var corner in corners)
                {
                    if (minBound == null)
                    {
                        minBound = corner;
                        maxBound = corner;
                    }
                    else
                    {
                        minBound = new XYZ(
                            Math.Min(minBound.X, corner.X),
                            Math.Min(minBound.Y, corner.Y),
                            Math.Min(minBound.Z, corner.Z)
                        );
                        maxBound = new XYZ(
                            Math.Max(maxBound.X, corner.X),
                            Math.Max(maxBound.Y, corner.Y),
                            Math.Max(maxBound.Z, corner.Z)
                        );
                    }
                }
            }

            // Process nested families
            if (element is FamilyInstance familyInstance)
            {
                var subComponentIds = familyInstance.GetSubComponentIds();
                if (subComponentIds != null && subComponentIds.Count > 0)
                {
                    foreach (var subId in subComponentIds)
                    {
                        var subElement = element.Document.GetElement(subId);
                        if (subElement != null)
                        {
                            ProcessElementBoundingBox(subElement, ref minBound, ref maxBound);
                        }
                    }
                }
            }
        }
    }
}
