#if DASHBOARD_DESKTOP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngineeringDashboard.Backpropping;
using EngineeringDashboard.DesktopHost;

namespace EngineeringDashboard.DesktopHost
{
    public sealed class BackproppingRestartState
    {
        public List<SlabInput> Slabs {get;set;}
        public Dictionary<int,string> Maximums {get;set;}
        public Dictionary<int,string> Extensions {get;set;}
        public string ProjectMaximum {get;set;}
        public string[] Draft {get;set;}
        public int Curve {get;set;}
        public int Editing {get;set;}
        public bool Dirty {get;set;}
        public bool HadResults {get;set;}
    }
}
namespace EngineeringDashboard
{
    public partial class BackproppingWindow
    {
        internal static BackproppingRestartState CaptureRestartState(BackproppingWindow active)
        {
            var source=active?.CaptureSession()??retained;if(source==null)return null;
            return new BackproppingRestartState{Slabs=source.Slabs.Select(s=>s.Copy()).ToList(),Maximums=new Dictionary<int,string>(source.Maximums),Extensions=active==null?new Dictionary<int,string>():new Dictionary<int,string>(active.extensionDrafts),ProjectMaximum=source.ProjectMaximum,Draft=source.Draft.ToArray(),Curve=source.Curve,Editing=source.Editing,Dirty=source.Dirty,HadResults=source.HadResults};
        }
        internal static void ValidateRestartState(BackproppingRestartState state)
        {
            if(state==null)return;
            if(state.Slabs==null||state.Maximums==null||state.Extensions==null||state.Draft?.Length!=6||state.Curve<0||state.Curve>2||state.Editing < -1||state.Editing>=state.Slabs.Count)throw new InvalidDataException("Invalid backpropping restart state.");
        }
        internal static void RestoreRestartState(BackproppingRestartState state,BackproppingWindow active=null)
        {
            ValidateRestartState(state);if(state==null)return;
            var restored=new SessionState{Slabs=state.Slabs.Select(s=>s.Copy()).ToList(),Maximums=new Dictionary<int,string>(state.Maximums),ProjectMaximum=state.ProjectMaximum,Draft=state.Draft.ToArray(),Curve=state.Curve,Editing=state.Editing,Dirty=state.Dirty,HadResults=false};
            retained=restored;
            if(active!=null){active.RestoreSession(restored);active.extensionDrafts.Clear();foreach(var item in state.Extensions)active.extensionDrafts[item.Key]=item.Value;active.Refresh();active.InvalidateResults();active.ResultState.Text="Inputs and drafts restored after the update — calculate spacing.";}
        }
    }
}
#endif
