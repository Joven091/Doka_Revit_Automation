using System;
using System.Linq;
using System.Runtime.Serialization;

namespace EngineeringDashboard.Top50
{
    public sealed class Top50RevitConnection
    {
        public Func<System.Threading.Tasks.Task<RevitWallSnapshot>> ReadSelection {get;set;}
        public Func<RevitWallRequest,System.Threading.Tasks.Task<RevitWallSnapshot>> Refresh {get;set;}
        public Func<RevitWallRequest,System.Threading.Tasks.Task<RevitHighlightResult>> Highlight {get;set;}
    }
    public sealed class RevitWallRecord
    {
        public string UniqueId {get;set;}
        public string ElementId {get;set;}
        public string Label {get;set;}
        public decimal CentreLengthMm {get;set;}
        public decimal? ExteriorLengthMm {get;set;}
        public decimal? InteriorLengthMm {get;set;}
        public string Problem {get;set;}
        public string GeometryDetails {get;set;}
        [IgnoreDataMember] public decimal PlanningLengthMm=>Math.Max(ExteriorLengthMm??0,InteriorLengthMm??0);
        public RevitWallRecord Copy()=>(RevitWallRecord)MemberwiseClone();
    }
    public sealed class RevitWallSnapshot
    {
        public string DocumentKey {get;set;}
        public string DocumentTitle {get;set;}
        public string DocumentPath {get;set;}
        public string RevitVersion {get;set;}
        public string ViewName {get;set;}
        public string CapturedUtc {get;set;}
        public string[] SelectedIds {get;set;}=Array.Empty<string>();
        public RevitWallRecord[] Walls {get;set;}=Array.Empty<RevitWallRecord>();
        public string[] Notes {get;set;}=Array.Empty<string>();
        public RevitWallSnapshot Copy()=>new RevitWallSnapshot{DocumentKey=DocumentKey,DocumentTitle=DocumentTitle,DocumentPath=DocumentPath,RevitVersion=RevitVersion,ViewName=ViewName,CapturedUtc=CapturedUtc,SelectedIds=SelectedIds.ToArray(),Walls=Walls.Select(w=>w.Copy()).ToArray(),Notes=Notes.ToArray()};
    }
    public sealed class RevitWallRequest
    {
        public string DocumentKey {get;set;}
        public string[] UniqueIds {get;set;}=Array.Empty<string>();
        public bool Locate {get;set;}
    }
    public sealed class RevitHighlightResult
    {
        public int Requested {get;set;}
        public int Found {get;set;}
        public string Message {get;set;}
    }
}
