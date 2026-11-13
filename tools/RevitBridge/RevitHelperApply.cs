using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using EngineeringDashboard.DesktopHost;

namespace EngineeringDashboard
{
    internal static class RevitHelperApply
    {
        internal static HelperApplyResult Apply(UIApplication app,HelperApplyRequest values,Action guard)
        {
            values.Validate();guard();
            var ui=app.ActiveUIDocument??throw new InvalidOperationException("Open the original Revit project first.");var doc=ui.Document;
            Action sameDocument=()=>{guard();if(app.ActiveUIDocument?.Document!=doc||Top50RevitSelection.DocumentKey(doc)!=values.DocumentKey)throw new InvalidOperationException("The active Revit project changed. Return to the original project and retry; no helper values were applied.");};
            sameDocument();List<FamilyInstance> instances;
            try
            {
                instances=ui.Selection.PickElementsByRectangle(new HelperFilter(values),values.Kind=="beam"?"Drag a window around matching beam helper lines. Esc cancels.":"Drag a window around slab helper lines to apply this calculation. Esc cancels.")
                    .OfType<FamilyInstance>().GroupBy(i=>i.Id).Select(g=>g.First()).ToList();
            }
            catch(Autodesk.Revit.Exceptions.OperationCanceledException){return new HelperApplyResult{Cancelled=true};}
            sameDocument();
            if(instances.Count==0)return new HelperApplyResult{Message="No matching helper lines were selected. No values were changed."};
            var result=new HelperApplyResult();var reasons=new List<string>();
            ui.Selection.SetElementIds(instances.Select(i=>i.Id).ToList());
            using(var transaction=new Transaction(doc,values.Kind=="beam"?"Apply Beam Calculator Values":"Apply Slab Calculator Values"))
            {
                transaction.Start();
                foreach(var instance in instances)
                {
                    sameDocument();
                    var parameters=new List<Parameter>{instance.LookupParameter("Max Prop spacing")??instance.LookupParameter("Max Prop Spacing"),instance.LookupParameter("Start Cantilever"),instance.LookupParameter("End Cantilever")};
                    var names=new List<string>{"Max Prop spacing","Start Cantilever","End Cantilever"};
                    var settings=new List<double>{values.PropSpacingMm,values.PrimaryCantileverMm,values.PrimaryCantileverMm};
                    if(values.Kind=="slab")
                    {parameters.Add(instance.LookupParameter("Primary Beam Spacing")??instance.LookupParameter("Primary Beam spacing")??instance.LookupParameter("Max Beam Spacing")??instance.LookupParameter("Max Beam spacing"));names.Add("Primary Beam Spacing");settings.Add(values.PrimarySpacingMm);}
                    var invalid=Enumerable.Range(0,parameters.Count).Where(i=>!IsLength(parameters[i])||parameters[i].IsReadOnly).Select(i=>names[i]).ToArray();
                    if(invalid.Length>0){result.Skipped++;reasons.Add(instance.Id+": missing, read-only or non-length parameter: "+string.Join(", ",invalid)+".");continue;}
                    using(var change=new SubTransaction(doc))
                    {
                        change.Start();
                        try
                        {
                            for(int i=0;i<parameters.Count;i++)if(!parameters[i].Set(settings[i]/304.8))throw new InvalidOperationException("A parameter rejected its value.");
                            if(change.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Instance update was not committed.");result.Updated++;
                        }
                        catch(Exception error)when(error is ArgumentException||error is InvalidOperationException||error is Autodesk.Revit.Exceptions.ArgumentException||error is Autodesk.Revit.Exceptions.InvalidOperationException)
                        {if(change.GetStatus()==TransactionStatus.Started)change.RollBack();result.Failed++;reasons.Add(instance.Id+": "+error.Message);}
                    }
                }
                sameDocument();if(transaction.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("The helper transaction did not commit. No successful updates are reported.");
            }
            result.Message="Updated: "+result.Updated+"\nSkipped: "+result.Skipped+"\nFailed: "+result.Failed+(reasons.Count==0?"":"\n\n"+string.Join("\n",reasons.Take(5))+(reasons.Count>5?"\nAdditional instances also skipped or failed.":""));return result;
        }
        static bool IsLength(Parameter p)=>p!=null&&p.StorageType==StorageType.Double&&p.Definition.GetDataType()==SpecTypeId.Length;
        sealed class HelperFilter:ISelectionFilter
        {
            readonly HelperApplyRequest values;internal HelperFilter(HelperApplyRequest values){this.values=values;}
            public bool AllowElement(Element element)
            {
                var instance=element as FamilyInstance;if(instance?.Symbol==null||instance.Symbol.FamilyName!="PrimaryBeam")return false;
                var kind=instance.LookupParameter("Structural Element Type");if(kind?.StorageType!=StorageType.String||kind.AsString()!=(values.Kind=="beam"?"Beam":"Slab"))return false;
                var depth=instance.LookupParameter("H");if(values.Kind=="beam")depth=depth??instance.Symbol.LookupParameter("H");
                if(!IsLength(depth)||Math.Abs(depth.AsDouble()*304.8-(values.Kind=="beam"?values.DepthMm:values.SlabThicknessMm))>0.1)return false;
                if(values.Kind=="slab")return true;var width=instance.LookupParameter("B")??instance.Symbol.LookupParameter("B");return IsLength(width)&&Math.Abs(width.AsDouble()*304.8-values.WidthMm)<=0.1;
            }
            public bool AllowReference(Reference reference,XYZ position)=>false;
        }
    }
}
