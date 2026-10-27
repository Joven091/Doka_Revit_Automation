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
    public sealed class Top50ViewModel : INotifyPropertyChanged
    {
        public Top50ViewModel(){Top50Runtime.Initialize();}
        private ImportSnapshot input;
        private Proposal selected;
        private CancellationTokenSource cancellation;
        private int revision;
        public NativeTop50Project Settings {get;private set;}=new NativeTop50Project();
        public ImportSnapshot Input=>input;
        public IReadOnlyList<Proposal> Proposals {get;private set;}=Array.Empty<Proposal>();
        public IReadOnlyList<string> SearchLog {get;private set;}=Array.Empty<string>();
        public Proposal Selected {get=>selected;set{if(selected==value)return;selected=value;Dirty=true;Notify();Notify(nameof(Dirty));}}
        public int CurrentPage {get;set;}
        public int SelectedGroupIndex {get;set;}
        public bool IsBusy {get;private set;}
        public bool IsStale {get;private set;}
        public bool Dirty {get;private set;}
        public string Status {get;private set;}="Import a DXF or add reviewed wall dimensions.";
        public string ProjectPath {get;private set;}
        public SearchLimits Limits=>new SearchLimits(Settings.MaxComponents,Settings.MaxFillers,Settings.MaxFillerMm,Settings.MaxGroups,Settings.OfferingMode==1?1:Settings.LongestCount,Settings.Seconds,Settings.Seed);
        public event PropertyChangedEventHandler PropertyChanged;
        public void Import(string path,DxfImportOptions options=null)
        {
            if(IsBusy)throw new InvalidOperationException("Cancel the current optimization first.");
            input=new Top50DxfImporter().Import(path,options??new DxfImportOptions());Proposals=Array.Empty<Proposal>();selected=null;IsStale=false;Dirty=true;revision++;
            Status="Imported "+input.Walls.Count+" dimension rows; review flagged measurements individually.";Refresh();
        }
        public void Changed(bool affectsCalculation=true)
        {if(affectsCalculation){revision++;IsStale=Proposals.Count>0;}Dirty=true;Status=IsStale?"Inputs changed. Existing proposals are stale; optimize again before exporting.":"Inputs updated.";foreach(var n in new[]{nameof(Dirty),nameof(IsStale),nameof(Status)})Notify(n);}
        public void AddManual(decimal lengthMm,string reason)
        {
            if(IsBusy)throw new InvalidOperationException("Cancel optimization before editing walls.");
            if(input==null)input=new ImportSnapshot{SourcePath="",SourceSha256="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",InsUnits=4,Walls=Array.Empty<WallRow>()};
            int next=input.Walls.Where(w=>w.Id.StartsWith("MANUAL-",StringComparison.Ordinal)).Select(w=>int.Parse(w.Id.Substring(7),System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max()+1;
            var row=new WallRow{Id="MANUAL-"+next.ToString("000",System.Globalization.CultureInfo.InvariantCulture),Handle="",Layer="Manual",SourceUnits=4,MmPerUnit=1,ProjectedRawMm=lengthMm,EndpointDistanceMm=lengthMm};row.Review(lengthMm,true,reason);input.Walls=Array.AsReadOnly(input.Walls.Concat(new[]{row}).ToArray());Changed();Notify(nameof(Input));
        }
        public async Task OptimizeAsync()
        {
            if(IsBusy)return;if(input==null)throw new InvalidOperationException("Import measured walls first.");
            var walls=input.Walls.Select(w=>w.Snapshot()).ToArray();var catalog=Settings.CatalogMm.ToArray();var limits=Limits;
            var preset=(PlanningPreset)Settings.Preset;var displayLimit=Settings.CandidateDisplayLimit;var percentage=Settings.OfferingMode==1?(decimal?)Settings.Percentage:null;var band=Settings.BandPoints;var expand=Settings.AllowExpansion;var identical=Settings.RequireIdenticalKits;
            int started=revision;cancellation=new CancellationTokenSource();var token=cancellation.Token;IsBusy=true;Status="Searching retained inventory models…";Refresh();
            var progress=new Progress<string>(text=>{if(!IsBusy||token.IsCancellationRequested)return;Status=text;Notify(nameof(Status));});
            try
            {
                var result=await Task.Run(()=>new Top50Search().Run(walls,catalog,limits,preset,displayLimit,token,progress,percentage,band,expand,identical));
                if(started!=revision||token.IsCancellationRequested)return;
                SearchLog=result.Log;
                if(result.Proposals.Count==0){Status="No complete proposal found within the selected search band and budget. This is not a proof of infeasibility.";return;}
                AcceptValidatedRun(result);
            }
            catch(OperationCanceledException){Status="Search cancelled; previous proposals retained.";}
            finally{IsBusy=false;cancellation.Dispose();cancellation=null;Refresh();}
        }
        public void Cancel(){cancellation?.Cancel();Status="Cancelling at the next solver checkpoint…";Notify(nameof(Status));}
        internal void AcceptValidatedRun(SearchRun run)
        {
            foreach(var p in run.Proposals)if(Top50Validation.Validate(input.Walls,p,Limits).Count>0)throw new InvalidOperationException("Only independently validated proposals can enter the UI.");
            Proposals=run.Proposals;SearchLog=run.Log;selected=Proposals.FirstOrDefault();IsStale=false;Dirty=true;Status="Validated "+Proposals.Count+" displayed proposal(s). Feasible / best found; global optimum not established.";Refresh();
        }
        public void Save(string path)
        {
            if(input==null)throw new InvalidOperationException("There is no source project to save.");
            var document=NativeProjectStore.Capture(input,IsStale?Array.Empty<Proposal>():Proposals,selected?.Id);
            document.LongestCount=Settings.LongestCount;document.CatalogMm=Settings.CatalogMm.ToArray();document.Preset=Settings.Preset;document.MaxComponents=Settings.MaxComponents;document.MaxFillers=Settings.MaxFillers;document.MaxFillerMm=Settings.MaxFillerMm;document.MaxGroups=Settings.MaxGroups;document.CandidateDisplayLimit=Settings.CandidateDisplayLimit;document.Seconds=Settings.Seconds;document.Seed=Settings.Seed;document.OfferingMode=Settings.OfferingMode;document.Percentage=Settings.Percentage;document.BandPoints=Settings.BandPoints;document.AllowExpansion=Settings.AllowExpansion;document.RequireIdenticalKits=Settings.RequireIdenticalKits;document.SearchLog=SearchLog.ToArray();
            document.ConnectionLevels=Settings.ConnectionLevels;document.ConnectionFaces=Settings.ConnectionFaces;document.ConnectionReviewed=Settings.ConnectionReviewed;document.AdjustableOnDirect=Settings.AdjustableOnDirect;
            NativeProjectStore.Save(document,path);ProjectPath=path;Dirty=false;Status=IsStale?"Saved inputs. Recalculate stale proposals before export.":"Project saved.";Refresh();
        }
        public void Open(string path,string cache)
        {
            if(IsBusy)throw new InvalidOperationException("Cancel optimization before opening another project.");
            var document=NativeProjectStore.Load(path);var restored=NativeProjectStore.RestoreInput(document,cache);
            var restoredLimits=new SearchLimits(document.MaxComponents,document.MaxFillers,document.MaxFillerMm,document.MaxGroups,document.OfferingMode==1?1:document.LongestCount,document.Seconds,document.Seed);
            var proposals=document.Proposals.Select(NativeProjectStore.FromDto).ToArray();
            foreach(var p in proposals)if(Top50Validation.Validate(restored.Walls,p,restoredLimits).Count>0)throw new InvalidOperationException("Saved proposal validation failed; original project remains unchanged.");
            input=restored;Settings=document;Proposals=Array.AsReadOnly(proposals);selected=proposals.FirstOrDefault(p=>p.Id==document.SelectedId)??proposals.FirstOrDefault();SearchLog=document.SearchLog??Array.Empty<string>();ProjectPath=path;IsStale=false;Dirty=false;revision++;Status="Project opened and cached proposals independently revalidated.";Refresh();
        }
        public void Export(string path)
        {
            if(IsBusy||IsStale||selected==null)throw new InvalidOperationException("Select a current validated proposal before exporting.");
            Top50Workbook.Export(input,selected,Proposals,Limits,path,Settings);Status="Exported selected "+selected.Id+" to nine-sheet workbook.";Notify(nameof(Status));
        }
        private void Refresh(){foreach(var name in new[]{nameof(Input),nameof(Proposals),nameof(Selected),nameof(IsBusy),nameof(IsStale),nameof(Dirty),nameof(Status),nameof(Settings)})Notify(name);}
        private void Notify([CallerMemberName]string name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
}
