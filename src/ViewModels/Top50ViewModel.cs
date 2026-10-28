using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel : INotifyPropertyChanged
    {
        public Top50ViewModel(){Top50Runtime.Initialize();Top50PlannerPolicy.Apply(Settings);checkpoint=CaptureEdit();}
        private ImportSnapshot input;
        private Proposal selected;
        private CancellationTokenSource cancellation;
        private int revision;
        public NativeTop50Project Settings {get;private set;}=new NativeTop50Project();
        public ImportSnapshot Input=>input;
        public IReadOnlyList<Proposal> Proposals {get;private set;}=Array.Empty<Proposal>();
        public IReadOnlyList<string> SearchLog {get;private set;}=Array.Empty<string>();
        public Proposal Selected {get=>selected;set{if(selected==value)return;selected=value;NewToken();RememberEdit();Notify();Notify(nameof(Dirty));}}
        public int CurrentPage {get;set;}
        public int SelectedGroupIndex {get;set;}
        public bool IsBusy {get;private set;}
        public bool IsStale {get;private set;}
        public bool Dirty {get;private set;}
        public string Status {get;private set;}="Import a DXF or add reviewed wall dimensions.";
        public string ProjectPath {get;private set;}
        public SearchLimits Limits=>new SearchLimits(Settings.MaxFillers,Settings.MaxFillerMm,Settings.MaxGroups,Settings.OfferingMode==1?1:Settings.LongestCount,Top50PlannerPolicy.ThoroughSearchEffort,Settings.Seed);
        public event PropertyChangedEventHandler PropertyChanged;
        public void Import(string path,DxfImportOptions options=null)
        {
            TrackOperation("Import DXF",path,()=>
            {
            if(IsBusy)throw new InvalidOperationException("Cancel the current optimization first.");
            input=new Top50DxfImporter().Import(path,options??new DxfImportOptions());Proposals=Array.Empty<Proposal>();selected=null;IsStale=false;Dirty=true;revision++;
            Status="Imported "+input.Walls.Count+" dimension rows; review flagged measurements individually.";ResetEdits(false);Refresh();
            });
        }
        public void Changed(bool affectsCalculation=true)
        {
            RequireEditable();Top50PlannerPolicy.Apply(Settings);undo.Push(checkpoint);redo.Clear();
            if(affectsCalculation){revision++;IsStale=Proposals.Count>0;}NewToken();
            Status=IsStale?"Inputs changed. Optimize again to update the results.":"Inputs updated.";RememberEdit();
            foreach(var name in new[]{nameof(Dirty),nameof(IsStale),nameof(Status)})Notify(name);
        }
        public void AddManual(decimal lengthMm,string reason)
        {
            if(IsBusy)throw new InvalidOperationException("Cancel optimization before editing walls.");
            if(input==null)input=new ImportSnapshot{SourcePath="",SourceSha256="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",InsUnits=4,Walls=Array.Empty<WallRow>()};
            int next=input.AllWalls.Where(w=>w.Id.StartsWith("MANUAL-",StringComparison.Ordinal)).Select(w=>int.Parse(w.Id.Substring(7),System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max()+1;
            var row=new WallRow{Id="MANUAL-"+next.ToString("000",System.Globalization.CultureInfo.InvariantCulture),Handle="",Layer="Manual",SourceUnits=4,MmPerUnit=1,ProjectedRawMm=lengthMm,EndpointDistanceMm=lengthMm};row.Review(lengthMm,true,reason);input.Walls=Array.AsReadOnly(input.Walls.Concat(new[]{row}).ToArray());Changed();Notify(nameof(Input));
        }
        public async Task OptimizeAsync()
        {
            if(IsBusy)return;
            Top50PlannerPolicy.Apply(Settings);
            var attempt=BeginOperation("Optimize","");
            var search=BeginSearchDiagnostic();
            try
            {
            if(input==null)throw new InvalidOperationException("Import measured walls first.");
            var walls=input.Walls.Select(w=>w.Snapshot()).ToArray();var catalog=Settings.CatalogMm.ToArray();var limits=Limits;
            var preset=(PlanningPreset)Settings.Preset;var displayLimit=Top50PlannerPolicy.DisplayLimit;var percentage=Settings.OfferingMode==1?(decimal?)Settings.Percentage:null;var band=Settings.BandPoints;var expand=Settings.AllowExpansion;var identical=Settings.RequireIdenticalKits;
            int started=revision;cancellation=new CancellationTokenSource();var token=cancellation.Token;IsBusy=true;Status="Searching retained inventory models…";Refresh();
            var progress=new Progress<string>(text=>{if(!IsBusy||token.IsCancellationRequested)return;search.Progress.Add(DateTime.UtcNow.ToString("O",System.Globalization.CultureInfo.InvariantCulture)+" | "+text);Status=text;Notify(nameof(Status));});
            try
            {
                var result=await Task.Run(()=>new Top50Search().Run(walls,catalog,limits,preset,displayLimit,token,progress,percentage,band,expand,identical));
                search.Attempted=result.Attempted;search.Unsearched=result.Unsearched;search.BudgetExpired=result.BudgetExpired;search.Log=result.Log.ToArray();
                if(started!=revision||token.IsCancellationRequested){search.Outcome=token.IsCancellationRequested?"Cancelled; returned proposals not accepted.":"Inputs changed; returned proposals not accepted.";return;}
                SearchLog=result.Log;
                if(result.Proposals.Count==0){search.Outcome="No complete proposal found within the selected search band and budget; infeasibility not established.";Status="No complete proposal found within the selected search band and budget. This is not a proof of infeasibility.";return;}
                AcceptValidatedRun(result);
                search.Outcome="Accepted "+result.Proposals.Count+" independently validated proposal(s).";
            }
            catch(OperationCanceledException){search.Outcome="Cancelled; previous proposals retained.";Status="Search cancelled; previous proposals retained.";}
            finally{IsBusy=false;cancellation.Dispose();cancellation=null;Refresh();}
            }
            catch(Exception error){search.Outcome="Failed";FailOperation(attempt,error);throw;}
            finally{search.Clock.Stop();EndOperation(attempt,search.Outcome);}
        }
        public void Cancel(){cancellation?.Cancel();Status="Cancelling at the next solver checkpoint…";Notify(nameof(Status));}
        internal void AcceptValidatedRun(SearchRun run)
        {
            foreach(var p in run.Proposals)if(Top50Validation.Validate(input.Walls,p,Limits).Count>0)throw new InvalidOperationException("Only independently validated proposals can enter the UI.");
            Proposals=QuantityShortlist(run.Proposals,input,Settings);SearchLog=run.Log;selected=Proposals.FirstOrDefault();Settings.SearchPolicyVersion=Top50PlannerPolicy.SearchPolicyVersion;IsStale=false;NewToken();RememberEdit();Status="Validated "+Proposals.Count+" displayed proposal(s); fewest panel count found: "+(selected==null?"none":Top50PlannerPolicy.TotalPanels(selected).ToString()+" (both faces)")+". Global optimum not established.";Refresh();
        }
        public void Save(string path)
        {
            TrackOperation("Save project",path,()=>
            {
            if(IsBusy)throw new InvalidOperationException("Cancel optimization before saving the project.");
            if(input==null)throw new InvalidOperationException("There is no source project to save.");
            Top50PlannerPolicy.Apply(Settings);
            var document=NativeProjectStore.Capture(input,IsStale?Array.Empty<Proposal>():Proposals,selected?.Id);
            document.LongestCount=Settings.LongestCount;document.CatalogMm=Settings.CatalogMm.ToArray();document.Preset=Settings.Preset;document.MaxFillers=Settings.MaxFillers;document.MaxFillerMm=Settings.MaxFillerMm;document.MaxGroups=Settings.MaxGroups;document.CandidateDisplayLimit=Settings.CandidateDisplayLimit;document.Seconds=Settings.Seconds;document.Seed=Settings.Seed;document.OfferingMode=Settings.OfferingMode;document.Percentage=Settings.Percentage;document.BandPoints=Settings.BandPoints;document.AllowExpansion=Settings.AllowExpansion;document.RequireIdenticalKits=Settings.RequireIdenticalKits;document.SearchLog=SearchLog.ToArray();
            document.ConnectionLevels=Settings.ConnectionLevels;document.ConnectionFaces=Settings.ConnectionFaces;document.ConnectionReviewed=Settings.ConnectionReviewed;document.AdjustableOnDirect=Settings.AdjustableOnDirect;
            document.SearchPolicyVersion=Settings.SearchPolicyVersion;
            NativeProjectStore.Save(document,path);Settings.Version=Top50PlannerPolicy.ProjectVersion;ProjectPath=path;savedStateToken=stateToken;Dirty=false;RememberEdit();Status=IsStale?"Saved inputs. Optimize again to update the results.":"Project saved.";Refresh();
            });
        }
        public void Open(string path,string cache)
        {
            TrackOperation("Open project",path,()=>
            {
            if(IsBusy)throw new InvalidOperationException("Cancel optimization before opening another project.");
            var document=NativeProjectStore.Load(path);Top50PlannerPolicy.Apply(document);var restored=NativeProjectStore.RestoreInput(document,cache);
            var restoredLimits=new SearchLimits(document.MaxFillers,document.MaxFillerMm,document.MaxGroups,document.OfferingMode==1?1:document.LongestCount,document.Seconds,document.Seed);
            var proposals=document.Proposals.Select(NativeProjectStore.FromDto).ToArray();
            foreach(var p in proposals)if(Top50Validation.Validate(restored.Walls,p,restoredLimits).Count>0)throw new InvalidOperationException("Saved proposal validation failed; original project remains unchanged.");
            var eligible=QuantityShortlist(proposals,restored,document);
            bool shortlistChanged=!proposals.Select(p=>p.Id).SequenceEqual(eligible.Select(p=>p.Id));
            input=restored;Settings=document;Proposals=eligible;selected=eligible.FirstOrDefault(p=>p.Id==document.SelectedId)??eligible.FirstOrDefault();SearchLog=document.SearchLog??Array.Empty<string>();ProjectPath=path;IsStale=false;Dirty=shortlistChanged||document.Version<Top50PlannerPolicy.ProjectVersion;revision++;
            Status=document.SearchPolicyVersion<Top50PlannerPolicy.SearchPolicyVersion?"Saved proposals revalidated and quantity-first shortlist applied. Optimize to run the current repeatable search with automatic panel counts.":"Project opened and cached proposals independently revalidated.";ResetEdits(!Dirty);Refresh();
            });
        }
        private static IReadOnlyList<Proposal> QuantityShortlist(IEnumerable<Proposal> proposals,ImportSnapshot source,NativeTop50Project settings)
        {
            var all=proposals.ToArray();if(all.Length==0)return Array.Empty<Proposal>();
            var included=source.Walls.Where(w=>w.Included).ToArray();decimal length=included.Sum(w=>w.LengthMm)/1000;
            var target=settings.OfferingMode==1?(decimal?)(length*settings.Percentage/100):null;
            var scales=new RankingScales(length,settings.CatalogMm.Distinct().Count(),settings.MaxGroups,included.Length*settings.MaxFillers,target);
            var ranked=ProposalRanking.Rank(all.Select(p=>RankingMetrics.From(p,target.HasValue?p.StockMicrometres/1000000m-target.Value:0)),(PlanningPreset)settings.Preset,Top50PlannerPolicy.DisplayLimit,scales);
            var shown=ranked.Select(r=>all.Single(p=>p.Id==r.Id)).ToArray();for(int i=0;i<shown.Length;i++){shown[i].Rank=i+1;shown[i].Pareto=ranked[i].Pareto;}
            return Array.AsReadOnly(shown);
        }
        public void Export(string path)=>TrackOperation("Export workbook",path,()=>{throw new NotSupportedException("Excel export is not available in this version.");});
        private void Refresh(){foreach(var name in new[]{nameof(Input),nameof(Proposals),nameof(Selected),nameof(IsBusy),nameof(IsStale),nameof(Dirty),nameof(Status),nameof(Settings),nameof(CanUndo),nameof(CanRedo)})Notify(name);}
        private void Notify([CallerMemberName]string name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
}
