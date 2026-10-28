using System;
using System.Linq;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel
    {
        public string LastRevitReport {get;private set;}="No Revit operation in this session.";
        public void RecordRevitOperation(string action,string report){LastRevitReport=action+"\n"+report;Notify(nameof(LastRevitReport));}
        public void ImportRevit(RevitWallSnapshot snapshot)
        {
            RequireEditable();RecordRevitOperation("Use Revit selection",Top50RevitSource.Describe(snapshot));
            TrackOperation("Use Revit selection",snapshot?.DocumentTitle,()=>
            {
                var incoming=Top50RevitSource.Create(snapshot);
                if(!incoming.Walls.Any(w=>w.Included))throw new ArgumentException("No supported straight walls were measured. Open Diagnose for the selected IDs and geometry errors.");
                input=incoming;Proposals=Array.Empty<Proposal>();selected=null;SearchLog=Array.Empty<string>();SelectedGroupIndex=0;Changed();IsStale=false;Status="Imported "+input.Walls.Count+" Revit walls using the longer forming face. Review unequal faces before offering.";Refresh();
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
