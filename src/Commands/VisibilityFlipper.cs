using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class VisibilityFlipper : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;
                View activeView = doc.ActiveView;

                if (activeView == null)
                {
                    message = "No active view found.";
                    return Result.Failed;
                }

                // 1) Use a FilteredElementCollector to get all instances in the ActiveView.
                FilteredElementCollector collector = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType();

                List<ElementId> elementsToHide = new List<ElementId>();
                List<ElementId> elementsToUnhide = new List<ElementId>();

                foreach (Element el in collector)
                {
                    // 2) Filter the elements to only include those where el.CanBeHidden(view) is true.
                    if (el.CanBeHidden(activeView))
                    {
                        // 3) Sort the ElementIds into two separate lists: elementsToHide and elementsToUnhide by checking el.IsHidden(view).
                        if (el.IsHidden(activeView))
                        {
                            elementsToUnhide.Add(el.Id);
                        }
                        else
                        {
                            elementsToHide.Add(el.Id);
                        }
                    }
                }

                // 4) Open a single Transaction to apply ActiveView.HideElements() and ActiveView.UnhideElements().
                // 5) Include error handling to ensure lists are not empty before attempting to hide/unhide.
                using (Transaction t = new Transaction(doc, "Flip Element Visibility"))
                {
                    t.Start();

                    if (elementsToHide.Count > 0)
                    {
                        activeView.HideElements(elementsToHide);
                    }

                    if (elementsToUnhide.Count > 0)
                    {
                        activeView.UnhideElements(elementsToUnhide);
                    }

                    t.Commit();
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
