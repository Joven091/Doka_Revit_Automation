#if DASHBOARD_DESKTOP
using System;
using System.Linq;
using System.IO;
using EngineeringDashboard.Top50;
using EngineeringDashboard.DesktopHost;

namespace EngineeringDashboard.DesktopHost
{
    public sealed class Top50RestartState
    {
        public NativeTop50Project Input {get;set;}
        public NativeTop50Project Settings {get;set;}
        public string ProjectPath {get;set;}
        public bool Dirty {get;set;}
        public bool HadResults {get;set;}
        public int Page {get;set;}
        public int Group {get;set;}
        public string[] SearchLog {get;set;}
    }
}
namespace EngineeringDashboard
{
    public sealed partial class Top50ViewModel
    {
        internal Top50RestartState CaptureRestartState()
        {
            if(IsBusy)throw new InvalidOperationException("Finish or cancel Top50 optimization before updating.");
            return new Top50RestartState{Input=input==null?null:NativeProjectStore.Capture(input,Array.Empty<Proposal>(),null),Settings=CopySettings(Settings),ProjectPath=ProjectPath,Dirty=Dirty,HadResults=Proposals.Count>0||IsStale,Page=CurrentPage,Group=SelectedGroupIndex,SearchLog=SearchLog.ToArray()};
        }
        internal static Top50ViewModel RestoreRestartState(Top50RestartState state)
        {
            if(state==null||state.Settings==null||state.Page<0||state.Page>3||state.Group<0)throw new InvalidDataException("Invalid Top50 restart state.");
            var settings=CopySettings(state.Settings);Top50PlannerPolicy.Apply(settings);Top50PlannerPolicy.ValidateSettings(settings);
            var restored=state.Input==null?null:NativeProjectStore.RestoreInput(state.Input,Path.Combine(DashboardPaths.Root,"Top50Sources"));
            var vm=new Top50ViewModel{Settings=settings,input=restored,ProjectPath=state.ProjectPath,CurrentPage=state.Page,SelectedGroupIndex=state.Group,IsStale=state.HadResults,SearchLog=state.SearchLog??Array.Empty<string>()};
            vm.stateToken=1;vm.nextStateToken=1;vm.savedStateToken=state.Dirty?0:1;vm.Dirty=state.Dirty;
            vm.Status="Inputs restored after the Dashboard update. Optimize to calculate a new equipment offer.";
            vm.RememberEdit();return vm;
        }
    }
    public sealed partial class Top50Window
    {
        internal bool RestartBusy=>model.IsBusy||revitBusy||highlighting;
    }
}
#endif
