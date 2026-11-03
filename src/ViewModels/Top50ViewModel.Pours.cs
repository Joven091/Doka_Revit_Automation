using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel
    {
        public IReadOnlyList<PourDefinition> Pours=>Array.AsReadOnly(PourPlan.Copy(Settings.Pours));
        public IReadOnlyList<PourCheck> PourChecks {get;private set;}=Array.Empty<PourCheck>();
        public string SelectedPourId {get=>Settings.SelectedPourId;set=>Settings.SelectedPourId=value;}
        public int UnassignedWallCount=>input?.Walls.Count(w=>w.Included&&!Settings.Pours.Any(p=>p.WallIds.Contains(w.Id)))??0;
        void ClearPourChecks(){PourChecks=Array.Empty<PourCheck>();Notify(nameof(PourChecks));}
        void EditPours(Action<List<PourDefinition>> edit)
        {
            RequireEditable();var next=PourPlan.Copy(Settings.Pours).ToList();edit(next);
            PourPlan.Validate(next,input?.AllWalls??Array.Empty<WallRow>());Settings.Pours=next.ToArray();Changed(false);Refresh();
        }
        public string AddPour()
        {
            if(input==null)throw new InvalidOperationException("Import walls before adding pours.");
            string id=Guid.NewGuid().ToString("N");int number=Settings.Pours.Length+1;
            while(Settings.Pours.Any(p=>p.Name=="Pour "+number.ToString("00")))number++;
            EditPours(p=>p.Add(new PourDefinition{Id=id,Name="Pour "+number.ToString("00")}));SelectedPourId=id;return id;
        }
        public void RenamePour(string id,string name)=>EditPours(p=>FindPour(p,id).Name=name?.Trim());
        public void MovePour(string id,int offset)
        {
            if(offset!=1&&offset!=-1)throw new ArgumentException("Move a pour up or down one position.");
            EditPours(p=>{var item=FindPour(p,id);int from=p.IndexOf(item),to=from+offset;if(to<0||to>=p.Count)throw new ArgumentException("The pour is already at the end of the order.");p.RemoveAt(from);p.Insert(to,item);});
        }
        public void DeletePour(string id){EditPours(p=>p.Remove(FindPour(p,id)));SelectedPourId=Settings.Pours.FirstOrDefault()?.Id;}
        public void AssignWalls(string pourId,IEnumerable<string> wallIds)
        {
            var ids=(wallIds??Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            if(input==null||ids.Any(id=>!input.Walls.Any(w=>w.Id==id&&w.Included)))throw new ArgumentException("Assign included, imported walls to the pour.");
            EditPours(p=>
            {
                var target=FindPour(p,pourId);var keep=target.WallIds.ToArray();
                foreach(var pour in p)pour.WallIds=pour.WallIds.Where(id=>!ids.Contains(id)).ToArray();
                target.WallIds=keep.Concat(ids).Distinct(StringComparer.Ordinal).ToArray();
            });
        }
        public void UnassignWalls(string pourId,IEnumerable<string> wallIds)
        {var ids=(wallIds??Array.Empty<string>()).ToArray();EditPours(p=>{var target=FindPour(p,pourId);target.WallIds=target.WallIds.Where(id=>!ids.Contains(id)).ToArray();});}
        static PourDefinition FindPour(IEnumerable<PourDefinition> pours,string id)=>pours.SingleOrDefault(p=>p.Id==id)??throw new ArgumentException("Select an existing pour.");
        public void AssignRevitSelection(string pourId,RevitWallSnapshot snapshot)
        {
            RequireEditable();if(input?.RevitSource==null||snapshot==null||snapshot.DocumentKey!=input.RevitSource.DocumentKey)throw new ArgumentException("Select walls from the original imported Revit project.");
            var unique=(snapshot.Walls??Array.Empty<RevitWallRecord>()).Select(w=>w.UniqueId).Distinct(StringComparer.Ordinal).ToArray();
            var matched=input.Walls.Where(w=>w.Revit!=null&&unique.Contains(w.Revit.UniqueId)).Select(w=>w.Id).ToArray();
            if(unique.Length==0||matched.Length!=unique.Length)throw new ArgumentException("Select included walls already imported into this planner. Import the full wall selection first, then assign its walls to pours.");
            AssignWalls(pourId,matched);RecordRevitOperation("Assign Revit selection to pour",Top50RevitSource.Describe(snapshot));
        }
        public async Task CheckPoursAsync()
        {
            if(IsBusy)return;
            if(Settings.Pours.Length==0)throw new InvalidOperationException("Add a pour and assign its walls first.");
            if(selected==null||IsStale)await OptimizeAsync();
            if(selected==null||IsStale)return; // Preserve the optimization's cancellation/no-result explanation.
            var operation=BeginOperation("Check pour sequence","Fixed wall memberships and order; selected equipment offer "+selected.Id);
            var pours=PourPlan.Copy(Settings.Pours);var walls=input.Walls.Select(w=>w.Snapshot()).ToArray();var offer=selected;var limits=Limits;var catalog=Settings.CatalogMm.ToArray();
            cancellation=new CancellationTokenSource();var token=cancellation.Token;int started=revision;IsBusy=true;Status="Checking your pour sequence…";Refresh();
            try
            {
                var progress=new Progress<string>(message=>{if(IsBusy&&!token.IsCancellationRequested){Status=message;Notify(nameof(Status));}});
                var result=await Task.Run(()=>PourSequencePlanner.Check(walls,pours,offer,limits,catalog,token,progress));
                token.ThrowIfCancellationRequested();if(started!=revision||selected!=offer)throw new OperationCanceledException();
                PourChecks=result;Status=result.Count(c=>c.WithinStock)+" of "+result.Count+" pours fit the selected offer. "+UnassignedWallCount+" included walls remain unassigned. Equipment reuse assumes the previous pour has been stripped.";
                EndOperation(operation,Status);
            }
            catch(OperationCanceledException){Status="Pour check cancelled. Your wall assignments and order were retained.";EndOperation(operation,Status);}
            catch(Exception error){FailOperation(operation,error);Status="Pour check failed: "+error.Message;throw;}
            finally{if(operation.Clock.IsRunning)EndOperation(operation);IsBusy=false;cancellation.Dispose();cancellation=null;Refresh();}
        }
    }
}
