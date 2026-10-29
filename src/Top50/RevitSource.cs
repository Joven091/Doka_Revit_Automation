using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace EngineeringDashboard.Top50
{
    public static class Top50RevitSource
    {
        public static byte[] Encode(RevitWallSnapshot source)
        {using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(RevitWallSnapshot)).WriteObject(stream,source);return stream.ToArray();}}
        public static RevitWallSnapshot Decode(byte[] bytes)
        {using(var stream=new MemoryStream(bytes))return (RevitWallSnapshot)new DataContractJsonSerializer(typeof(RevitWallSnapshot)).ReadObject(stream);}
        public static string Hash(byte[] bytes)
        {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static string RowId(string documentKey,string uniqueId)=>"BIM-"+Hash(Encoding.UTF8.GetBytes(documentKey)).Substring(0,16)+"-"+uniqueId;
        public static ImportSnapshot Create(RevitWallSnapshot source)
        {
            if(source==null||string.IsNullOrWhiteSpace(source.DocumentKey))throw new ArgumentException("Open a Revit project and select straight native walls.");
            if(source.Walls==null||source.Walls.Any(w=>string.IsNullOrWhiteSpace(w.UniqueId))||source.Walls.GroupBy(w=>w.UniqueId).Any(g=>g.Count()>1))throw new ArgumentException("Revit wall identities must be present and unique.");
            var copy=source.Copy();
            var rows=copy.Walls.Select(w=>
            {
                bool valid=string.IsNullOrEmpty(w.Problem)&&w.ExteriorLengthMm>0&&w.InteriorLengthMm>0;
                var notes=new System.Collections.Generic.List<string>();
                if(!valid)notes.Add(w.Problem??"Both straight forming faces must be measured before including this wall.");
                if(valid)notes.Add("Revit · longer forming face: "+w.PlanningLengthMm.ToString("0.###",CultureInfo.InvariantCulture)+" mm.");
                if(valid&&w.ExteriorLengthMm!=w.InteriorLengthMm)notes.Add("Review unequal faces: exterior "+w.ExteriorLengthMm+" mm; interior "+w.InteriorLengthMm+" mm. Quantities allow for the longer face on both sides; check the shorter-face arrangement.");
                return new WallRow{Id=RowId(copy.DocumentKey,w.UniqueId),Handle=w.ElementId,Layer="Revit",SourceUnits=4,MmPerUnit=1,ProjectedRawMm=w.PlanningLengthMm,EndpointDistanceMm=w.CentreLengthMm,StoredMeasurementMm=w.PlanningLengthMm,LengthMicrometres=Precision.ToMicrometres(w.PlanningLengthMm),Included=valid,Status=valid?WallReviewStatus.Valid:WallReviewStatus.NeedsReview,Diagnostics=notes.AsReadOnly(),Revit=w.Copy()};
            }).ToArray();
            return new ImportSnapshot{SourceKind="Revit",SourcePath="",SourceSha256=Hash(Encode(copy)),InsUnits=4,RevitSource=copy,Walls=Array.AsReadOnly(rows)};
        }
        public static string Describe(RevitWallSnapshot source)
        {
            if(source==null)return "No Revit source.";
            var text=new StringBuilder();text.AppendLine("Revit document: "+source.DocumentTitle+"; identity: "+source.DocumentKey+"; path: "+source.DocumentPath);
            text.AppendLine("Revit: "+source.RevitVersion+"; view: "+source.ViewName+"; captured UTC: "+source.CapturedUtc);
            text.AppendLine("Selected IDs: "+string.Join(", ",source.SelectedIds));text.AppendLine("Length basis: longer straight forming face; one row per physical wall; two-face equipment allowance.");
            foreach(var w in source.Walls)text.AppendLine("Wall "+w.ElementId+" | "+w.UniqueId+" | "+w.Label+" | centre mm="+w.CentreLengthMm+"; exterior mm="+w.ExteriorLengthMm+"; interior mm="+w.InteriorLengthMm+"; planning mm="+w.PlanningLengthMm+"; problem="+w.Problem+"\n"+w.GeometryDetails);
            foreach(var note in source.Notes)text.AppendLine(note);return text.ToString();
        }
    }
}
