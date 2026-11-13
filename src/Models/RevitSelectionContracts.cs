using System;
using System.Runtime.Serialization;

namespace EngineeringDashboard
{
    [DataContract]
    public sealed class ImportPoint
    {
        [DataMember]public double X {get;set;}
        [DataMember]public double Y {get;set;}
        public ImportPoint() {}
        public ImportPoint(double x,double y) {X=x;Y=y;}
    }
    [DataContract]
    internal sealed class RevitSelectedBeam
    {
        [DataMember]public string SourceId {get;set;}
        [DataMember]public string Label {get;set;}
        [DataMember]public ImportPoint Start {get;set;}
        [DataMember]public ImportPoint End {get;set;}
        public double LengthMm=>Start==null || End==null ? 0 : Math.Sqrt(Math.Pow(End.X-Start.X,2)+Math.Pow(End.Y-Start.Y,2));
        [DataMember]public double WidthMm {get;set;}
        [DataMember]public double DepthMm {get;set;}
        [DataMember]public double BottomMm {get;set;}
        [DataMember]public string Problem {get;set;}
    }
    [DataContract]
    internal sealed class RevitSelectedSlab
    {
        [DataMember]public string SourceId {get;set;}
        [DataMember]public string Label {get;set;}
        [DataMember]public ImportPoint[] Outline {get;set;}
        [DataMember]public double ThicknessMm {get;set;}
        [DataMember]public double TopMm {get;set;}
        [DataMember]public string Problem {get;set;}
    }
    [DataContract]
    internal sealed class RevitSelectedBoundary
    {
        [DataMember]public string SourceId {get;set;}
        [DataMember]public string Label {get;set;}
        [DataMember]public string Kind {get;set;}
        [DataMember]public ImportPoint[] Outline {get;set;}
        [DataMember]public double BottomMm {get;set;}
        [DataMember]public double TopMm {get;set;}
        [DataMember]public string Problem {get;set;}
    }
    [DataContract]
    internal sealed class RevitSelectionSnapshot
    {
        [DataMember]internal string DocumentTitle="";
        [DataMember]internal string RevitVersion="",RevitBuild="",ActiveView="",ReadException="";
        [DataMember]internal int SelectedCount=-1;
        [DataMember]internal string[] SelectedIds=new string[0];
        [DataMember]internal RevitSelectionElement[] Elements=new RevitSelectionElement[0];
        [DataMember]internal RevitSelectedBeam[] Beams=new RevitSelectedBeam[0];
        [DataMember]internal RevitSelectedSlab[] Slabs=new RevitSelectedSlab[0];
        [DataMember]internal RevitSelectedBoundary[] Boundaries=new RevitSelectedBoundary[0];
        [DataMember]internal string[] Problems=new string[0];
        [DataMember]internal string[] Notes=new string[0];
    }
    [DataContract]
    internal sealed class RevitSelectionElement
    {
        [DataMember]internal string SourceId="",Label="",Category="",FamilyType="",ElementClass="",Status="",Details="",Exception="";
    }
}
