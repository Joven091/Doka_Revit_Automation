using System;
using System.Linq;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel
    {
        public string LastRevitReport {get;private set;}="No Revit operation in this session.";
        public void RecordRevitOperation(string action,string report){LastRevitReport=action+"\n"+report;Notify(nameof(LastRevitReport));}
        public void AddRevitWalls(RevitWallSnapshot snapshot)
        {
            RequireEditable();
            if(input==null){ImportRevit(snapshot);return;}
            if(input.RevitSource==null||snapshot==null||snapshot.DocumentKey!=input.RevitSource.DocumentKey)throw new ArgumentException("Add walls from the original imported Revit project. DXF projects can add manually measured walls.");
            var incoming=Top50RevitSource.Create(snapshot);
            var previous=input.RevitSource.Copy();var known=new System.Collections.Generic.HashSet<string>(previous.Walls.Select(w=>w.UniqueId),StringComparer.Ordinal);
            var added=incoming.Walls.Where(w=>!known.Contains(w.Revit.UniqueId)).ToArray();
            var selectedIds=new System.Collections.Generic.HashSet<string>(snapshot.Walls.Select(w=>w.UniqueId),StringComparer.Ordinal);
            var restored=input.RemovedWalls.Where(w=>w.Revit!=null&&selectedIds.Contains(w.Revit.UniqueId)).Select(w=>w.Snapshot()).ToArray();
            if(added.Length==0&&restored.Length==0){Status="No new walls added. Selected walls are already in this project; duplicates were skipped.";Refresh();return;}
            previous.Walls=previous.Walls.Concat(added.Select(w=>w.Revit.Copy())).ToArray();
            previous.SelectedIds=previous.SelectedIds.Concat(snapshot.SelectedIds).Distinct(StringComparer.Ordinal).ToArray();
            previous.Notes=previous.Notes.Concat(new[]{"Additional Revit selection captured "+snapshot.CapturedUtc+"; original measurements and pour assignments retained."}).ToArray();
            var merged=Top50RevitSource.Create(previous);
            merged.Walls=Array.AsReadOnly(input.Walls.Select(w=>w.Snapshot()).Concat(restored).Concat(added).ToArray());
            var restoredIds=new System.Collections.Generic.HashSet<string>(restored.Select(w=>w.Id),StringComparer.Ordinal);
            merged.RemovedWalls=Array.AsReadOnly(input.RemovedWalls.Where(w=>!restoredIds.Contains(w.Id)).Select(w=>w.Snapshot()).ToArray());
            input=merged;Changed();RecordRevitOperation("Add Revit walls",Top50RevitSource.Describe(snapshot));
            Status="Added "+added.Length+" new walls and restored "+restored.Length+" removed walls. Existing walls and pour assignments retained; duplicate walls skipped.";Refresh();
        }
        public void ImportRevit(RevitWallSnapshot snapshot)
        {
            RequireEditable();RecordRevitOperation("Use Revit selection",Top50RevitSource.Describe(snapshot));
            TrackOperation("Use Revit selection",snapshot?.DocumentTitle,()=>
            {
                var incoming=Top50RevitSource.Create(snapshot);
                if(!incoming.Walls.Any(w=>w.Included))throw new ArgumentException("No supported straight walls were measured. Open Diagnose for the selected IDs and geometry errors.");
                input=incoming;Proposals=Array.Empty<Proposal>();selected=null;SearchLog=Array.Empty<string>();SelectedGroupIndex=0;Changed();IsStale=false;Status="Imported "+input.Walls.Count+" Revit walls using the longer forming face. Review unequal faces before offering.";Refresh();
                Settings.Pours=Array.Empty<PourDefinition>();Settings.SelectedPourId=null;ClearPourChecks();RememberEdit();Refresh();
            });
        }
        public void RefreshRevit(RevitWallSnapshot snapshot)
        {
            RequireEditable();if(input?.RevitSource==null||snapshot.DocumentKey!=input.RevitSource.DocumentKey)throw new ArgumentException("Refresh requires the original Revit project.");
            var incoming=Top50RevitSource.Create(snapshot);var previous=input.AllWalls.Where(w=>w.Revit!=null).ToDictionary(w=>w.Id);
            if(incoming.Walls.Count!=previous.Count||incoming.Walls.Any(w=>!previous.ContainsKey(w.Id)))throw new ArgumentException("The refreshed source must retain every original Revit wall identity.");
            foreach(var row in incoming.Walls)
            {
                var old=previous[row.Id];if(row.Status!=WallReviewStatus.Valid)continue;
                if(old.Acknowledged&&old.ProjectedRawMm==row.ProjectedRawMm)row.Review(old.LengthMm,old.Included,old.ReviewReason);
                else if(!old.Included)row.Review(row.LengthMm,false,"Existing exclusion retained after Revit refresh.");
            }
            var removed=input.RemovedWalls.Select(w=>w.Id).ToArray();var active=incoming.Walls.Where(w=>!removed.Contains(w.Id)).Concat(input.Walls.Where(w=>w.Revit==null).Select(w=>w.Snapshot())).ToArray();
            incoming.RemovedWalls=incoming.Walls.Where(w=>removed.Contains(w.Id)).Concat(input.RemovedWalls.Where(w=>w.Revit==null).Select(w=>w.Snapshot())).ToArray();incoming.Walls=active;
            input=incoming;Changed();RecordRevitOperation("Refresh imported walls",Top50RevitSource.Describe(snapshot));Status="Revit walls refreshed. Review changed or missing walls, then Optimize again.";Refresh();
        }
    }
}
