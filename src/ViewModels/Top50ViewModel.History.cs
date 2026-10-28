using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel
    {
        private sealed class EditState
        {
            public ImportSnapshot Input;public NativeTop50Project Settings;public Proposal[] Proposals;public string SelectedId;public string[] SearchLog;
            public bool Stale;public long Token;public int Page,Group;
        }
        private readonly Stack<EditState> undo=new Stack<EditState>(),redo=new Stack<EditState>();
        private EditState checkpoint;
        private long stateToken,nextStateToken,savedStateToken;
        public bool CanUndo=>!IsBusy&&undo.Count>0;
        public bool CanRedo=>!IsBusy&&redo.Count>0;
        private static NativeTop50Project CopySettings(NativeTop50Project settings)
        {
            var serializer=new DataContractJsonSerializer(typeof(NativeTop50Project));
            using(var stream=new MemoryStream()){serializer.WriteObject(stream,settings);stream.Position=0;return (NativeTop50Project)serializer.ReadObject(stream);}
        }
        private static ImportSnapshot CopyInput(ImportSnapshot source)=>source==null?null:new ImportSnapshot{SourcePath=source.SourcePath,SourceKind=source.SourceKind,RevitSource=source.RevitSource?.Copy(),SourceSha256=source.SourceSha256,InsUnits=source.InsUnits,UnitOverride=source.UnitOverride,UnitOverrideReason=source.UnitOverrideReason,Walls=Array.AsReadOnly(source.Walls.Select(w=>w.Snapshot()).ToArray()),RemovedWalls=Array.AsReadOnly(source.RemovedWalls.Select(w=>w.Snapshot()).ToArray())};
        private EditState CaptureEdit()=>new EditState{Input=CopyInput(input),Settings=CopySettings(Settings),Proposals=Proposals.ToArray(),SelectedId=selected?.Id,SearchLog=SearchLog.ToArray(),Stale=IsStale,Token=stateToken,Page=CurrentPage,Group=SelectedGroupIndex};
        private void NewToken(){stateToken=++nextStateToken;Dirty=stateToken!=savedStateToken;}
        private void RememberEdit(){checkpoint=CaptureEdit();Notify(nameof(CanUndo));Notify(nameof(CanRedo));}
        private void ResetEdits(bool saved)
        {undo.Clear();redo.Clear();NewToken();savedStateToken=saved?stateToken:-1;Dirty=!saved;RememberEdit();}
        private void RequireEditable(){if(IsBusy)throw new InvalidOperationException("Cancel optimization before editing the project.");}
        public void EditSettings(Action<NativeTop50Project> edit)
        {
            RequireEditable();if(edit==null)throw new ArgumentNullException(nameof(edit));var before=CopySettings(Settings);
            try{edit(Settings);}catch{Settings=before;throw;}Changed();
        }
        public void ReviewWall(string id,decimal lengthMm,bool include,string reason)
        {
            RequireEditable();var wall=input?.Walls.SingleOrDefault(w=>w.Id==id)??throw new ArgumentException("Select an existing wall.");
            wall.Review(lengthMm,include,reason);Changed();Notify(nameof(Input));
        }
        public void DeleteWalls(IEnumerable<string> ids)
        {
            RequireEditable();var selectedIds=new HashSet<string>(ids??Array.Empty<string>(),StringComparer.Ordinal);if(selectedIds.Count==0)return;
            if(input==null||selectedIds.Any(id=>!input.Walls.Any(w=>w.Id==id)))throw new ArgumentException("Select existing walls to delete.");
            var removed=input.Walls.Where(w=>selectedIds.Contains(w.Id)).ToArray();
            input.RemovedWalls=Array.AsReadOnly(input.RemovedWalls.Concat(removed).ToArray());input.Walls=Array.AsReadOnly(input.Walls.Where(w=>!selectedIds.Contains(w.Id)).ToArray());
            Changed();Notify(nameof(Input));
        }
        public void Undo(){RestoreHistory(undo,redo,"Undo");}
        public void Redo(){RestoreHistory(redo,undo,"Redo");}
        private void RestoreHistory(Stack<EditState> from,Stack<EditState> to,string action)
        {
            RequireEditable();if(from.Count==0)return;
            TrackOperation(action,"Project input history",()=>
            {
                to.Push(CaptureEdit());var state=from.Pop();input=CopyInput(state.Input);Settings=CopySettings(state.Settings);Top50PlannerPolicy.Apply(Settings);
                Proposals=Array.AsReadOnly(state.Proposals);selected=Proposals.FirstOrDefault(p=>p.Id==state.SelectedId);SearchLog=state.SearchLog;IsStale=state.Stale;
                stateToken=state.Token;Dirty=stateToken!=savedStateToken;CurrentPage=state.Page;SelectedGroupIndex=state.Group;revision++;
                Status=action+" applied."+(IsStale?" Optimize again to update the results.":"");RememberEdit();Refresh();
            });
        }
    }
}
