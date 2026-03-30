using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MyRevitAddin.Commands
{
    public class FamilyInstanceSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is FamilyInstance;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class AreaTableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // 1) Let the user select multiple families.
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FamilyInstanceSelectionFilter(),
                    "Select families to calculate Doka_Area");

                if (selectedRefs == null || selectedRefs.Count == 0)
                {
                    return Result.Cancelled;
                }

                // Dictionary to hold Family Name -> Sum of Doka_Area
                Dictionary<string, double> areaPerFamily = new Dictionary<string, double>();

                foreach (Reference reference in selectedRefs)
                {
                    Element elem = doc.GetElement(reference);
                    if (elem is FamilyInstance familyInstance)
                    {
                        string familyName = "";
                        if (familyInstance.Symbol != null && familyInstance.Symbol.Family != null)
                        {
                            familyName = familyInstance.Symbol.FamilyName;
                        }

                        if (string.IsNullOrEmpty(familyName))
                        {
                            familyName = "Unknown";
                        }

                        double dokaAreaValue = 0.0;

                        // Try to find Doka_Area parameter on instance first
                        Parameter areaParam = familyInstance.LookupParameter("Doka_Area");
                        if (areaParam == null && familyInstance.Symbol != null)
                        {
                            // If not found, try type parameter
                            areaParam = familyInstance.Symbol.LookupParameter("Doka_Area");
                        }

                        if (areaParam != null)
                        {
                            // Try to get double value
                            if (areaParam.StorageType == StorageType.Double)
                            {
                                dokaAreaValue = areaParam.AsDouble();
                            }
                            else if (areaParam.StorageType == StorageType.Integer)
                            {
                                dokaAreaValue = areaParam.AsInteger();
                            }
                            else if (areaParam.StorageType == StorageType.String)
                            {
                                string strVal = areaParam.AsString();
                                if (double.TryParse(strVal, out double parsedVal))
                                {
                                    dokaAreaValue = parsedVal;
                                }
                            }
                        }

                        if (areaPerFamily.ContainsKey(familyName))
                        {
                            areaPerFamily[familyName] += dokaAreaValue;
                        }
                        else
                        {
                            areaPerFamily[familyName] = dokaAreaValue;
                        }
                    }
                }

                if (areaPerFamily.Count == 0)
                {
                    TaskDialog.Show("Result", "No valid families found.");
                    return Result.Cancelled;
                }

                // 2) Create a table with two columns: 1st column is the family name, 2nd column is the value of parameter Doka_Area, with a sum total at the end.
                StringBuilder tableBuilder = new StringBuilder();

                double sumTotal = 0.0;

                // Format with tabs
                foreach (var kvp in areaPerFamily)
                {
                    // Format area with two decimal places
                    double area = kvp.Value;
                    sumTotal += area;

                    tableBuilder.Append(kvp.Key).Append("\t").Append(area.ToString("0.##")).Append("\n");
                }

                // Add Sum Total at the end
                tableBuilder.Append("Total").Append("\t").Append(sumTotal.ToString("0.##"));

                string tableText = tableBuilder.ToString();

                // 3) The output should be in text form that the user can insert on the floor plan view in revit.
                XYZ insertionPoint = uidoc.Selection.PickPoint("Select insertion point for the text note");

                using (Transaction tx = new Transaction(doc, "Create Area Table Text Note"))
                {
                    tx.Start();

                    ElementId defaultTextTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

                    TextNoteOptions options = new TextNoteOptions(defaultTextTypeId);
                    options.HorizontalAlignment = HorizontalTextAlignment.Left;
                    options.TypeId = defaultTextTypeId;

                    TextNote textNote = TextNote.Create(doc, doc.ActiveView.Id, insertionPoint, tableText, options);

                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
