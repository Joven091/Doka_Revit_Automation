using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using IxMilia.Dxf;

namespace EngineeringDashboard.Top50
{
    public sealed class Top50DxfImporter : ITop50Importer
    {
        private static decimal Number(string text) => decimal.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
        private static string Value(IReadOnlyList<SourceCode> codes,int code,string fallback="") => codes.LastOrDefault(x=>x.Code==code)?.Value.Trim()??fallback;
        private static decimal N(IReadOnlyList<SourceCode> codes,int code,decimal fallback=0) => Number(Value(codes,code,fallback.ToString(CultureInfo.InvariantCulture)));
        private static IEnumerable<List<SourceCode>> Chunks(List<SourceCode> pairs)
        {
            List<SourceCode> current=null;
            foreach(var pair in pairs) { if(pair.Code==0) { if(current!=null)yield return current;current=new List<SourceCode>(); } if(current!=null)current.Add(pair); }
            if(current!=null)yield return current;
        }
        public ImportSnapshot Import(string path,DxfImportOptions options)
        {
            using var runtimeScope=Top50Runtime.Enter();
            if(options==null)throw new ArgumentNullException(nameof(options));
            if(options.MmPerUnitOverride.HasValue && (options.MmPerUnitOverride.Value<=0 || string.IsNullOrWhiteSpace(options.OverrideReason))) throw new ArgumentException("A positive units override requires a review reason.");
            var bytes=File.ReadAllBytes(path);
            // The current raw-code audit supports text DXF only; never silently lose binary source evidence.
            if(System.Text.Encoding.ASCII.GetString(bytes.Take(22).ToArray()).StartsWith("AutoCAD Binary DXF",StringComparison.Ordinal)) throw new NotSupportedException("Binary DXF raw audit is not yet supported. Export a text DXF.");
            DxfFile.Load(path); // Validate the source container using the pinned DXF library.
            var lines=File.ReadAllLines(path);
            if(lines.Length%2!=0)throw new InvalidDataException("DXF has an incomplete code/value pair.");
            var pairs=new List<SourceCode>();
            for(int i=0;i<lines.Length;i+=2) { if(!int.TryParse(lines[i].Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var code))throw new InvalidDataException("Invalid DXF code.");pairs.Add(new SourceCode(code,lines[i+1])); }
            int units=0;
            for(int i=0;i<pairs.Count-1;i++)if(pairs[i].Code==9 && pairs[i].Value.Trim()=="$INSUNITS")units=(int)Number(pairs[i+1].Value.Trim());
            var conversions=new Dictionary<int,decimal>{{1,25.4m},{2,304.8m},{4,1m},{5,10m},{6,1000m},{7,1000000m},{8,0.0000254m},{9,0.0254m},{10,914.4m}};
            bool known=conversions.TryGetValue(units,out var scale);
            if(!known)scale=1;
            if(options.MmPerUnitOverride.HasValue)scale=options.MmPerUnitOverride.Value;
            var styles=Chunks(pairs).Where(c=>Value(c,0)=="DIMSTYLE").GroupBy(c=>Value(c,2,Value(c,3))).ToDictionary(g=>g.Key,g=>N(g.Last(),144,1));
            var walls=new List<WallRow>();var seen=new Dictionary<string,string>();string section="";
            foreach(var codes in Chunks(pairs))
            {
                string type=Value(codes,0);
                if(type=="SECTION") {section=Value(codes,2);continue;}if(type=="ENDSEC"){section="";continue;}
                if(section!="ENTITIES" || type!="DIMENSION" || N(codes,67)!=0 || !string.Equals(Value(codes,8),options.Layer,StringComparison.OrdinalIgnoreCase))continue;
                var handle=Value(codes,5);if(handle.Length==0)throw new InvalidDataException("A dimension has no stable DXF handle.");
                int dimensionType=(int)N(codes,70)&15;
                decimal dx=N(codes,14)-N(codes,13),dy=N(codes,24)-N(codes,23),dz=N(codes,34)-N(codes,33);
                double angle=(double)N(codes,50)*Math.PI/180;
                decimal projected=(decimal)Math.Abs((double)dx*Math.Cos(angle)+(double)dy*Math.Sin(angle))*scale;
                decimal endpoint=(decimal)Math.Sqrt((double)(dx*dx+dy*dy+dz*dz))*scale;
                var issues=new List<string>();var status=WallReviewStatus.Valid;
                void Review(string message){issues.Add(message);if(status!=WallReviewStatus.Excluded)status=WallReviewStatus.NeedsReview;}
                if(dimensionType!=0 && dimensionType!=1){status=WallReviewStatus.Excluded;issues.Add("Nonlinear dimension retained for audit.");}
                if(projected<=1)Review("Original V1 angle-projection measurement is zero or near zero; review source definition points.");
                decimal? stored=codes.Any(x=>x.Code==42)?N(codes,42)*scale:(decimal?)null;
                if(stored.HasValue && Math.Abs(stored.Value-projected)>0.01m)Review("Stored measurement conflicts with the original projected measurement.");
                if(dimensionType==1 && Math.Abs(endpoint-projected)>0.01m)Review("Aligned endpoint distance differs from the original V1 zero-angle projection; both values are retained, neither is silently substituted.");
                if(N(codes,210)!=0 || N(codes,220)!=0 || N(codes,230,1)!=1)Review("Non-default dimension extrusion requires coordinate-system review.");
                if(!known && !options.MmPerUnitOverride.HasValue)Review("Unknown/unitless INSUNITS; confirm physical conversion.");
                if(options.MmPerUnitOverride.HasValue)issues.Add("Reviewed units override: "+scale.ToString(CultureInfo.InvariantCulture)+" mm/unit; "+options.OverrideReason);
                string text=Value(codes,1);
                if(text.Length>0 && text!="<>")Review("User dimension text override requires acknowledgement.");
                decimal labelScale=styles.TryGetValue(Value(codes,3),out var factor)?factor:1;
                for(int i=0;i+1<codes.Count;i++)if(codes[i].Code==1070 && codes[i].Value.Trim()=="144" && codes[i+1].Code==1040)labelScale=Number(codes[i+1].Value.Trim());
                if(labelScale!=1)Review("DIMLFAC is annotation scaling only; source label requires review.");
                string a=string.Join(",",new[]{N(codes,13),N(codes,23),N(codes,33)}),b=string.Join(",",new[]{N(codes,14),N(codes,24),N(codes,34)});
                string geometry=dimensionType+"|"+(string.CompareOrdinal(a,b)<=0?a+"|"+b:b+"|"+a)+"|"+N(codes,50);
                if(seen.TryGetValue(geometry,out var prior))Review("Coincident source geometry with DXF-"+prior+"; equal length alone is not a duplicate.");else seen.Add(geometry,handle);
                walls.Add(new WallRow{Id="DXF-"+handle,Handle=handle,Layer=Value(codes,8),DimensionType=dimensionType,SourceUnits=units,MmPerUnit=scale,ProjectedRawMm=projected,EndpointDistanceMm=endpoint,StoredMeasurementMm=stored,LabelScale=labelScale,Text=text,LengthMicrometres=Precision.ToMicrometres(projected),Status=status,Included=status==WallReviewStatus.Valid,Diagnostics=issues.AsReadOnly(),RawCodes=codes.AsReadOnly()});
            }
            if(walls.Count==0)throw new InvalidDataException("No modelspace dimensions found on the selected layer.");
            if(walls.Select(w=>w.Id).Distinct().Count()!=walls.Count)throw new InvalidDataException("Duplicate source handles cannot form stable wall IDs.");
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            return new ImportSnapshot{SourcePath=Path.GetFullPath(path),SourceSha256=hash,InsUnits=units,Walls=walls.AsReadOnly(),UnitOverride=options.MmPerUnitOverride,UnitOverrideReason=options.OverrideReason};
        }
    }
}
