using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EngineeringDashboard.Top50
{
    public sealed class ProjectWallDto
    {
        public string Id {get;set;}
        public decimal LengthMm {get;set;}
        public bool Included {get;set;}
        public bool Acknowledged {get;set;}
        public string Reason {get;set;}
        public bool Manual {get;set;}
    }
    public sealed class PatternDto { public string WallId {get;set;} public int[] PanelsMm {get;set;} public long[] GapsMicrometres {get;set;} }
    public sealed class StockDto {public int SizeMm {get;set;}public int Quantity {get;set;}}
    public sealed class ProposalDto {public string Id {get;set;}public StockDto[] Stock {get;set;}public PatternDto[][] Groups {get;set;}public string Status {get;set;}public double Objective {get;set;}public double Bound {get;set;}public int KitCount {get;set;}public int Rank {get;set;}public bool Pareto {get;set;}}
    public sealed class NativeTop50Project
    {
        public string Kind {get;set;}="Doka.Top50.Native";
        public int Version {get;set;}=2;
        public string Name {get;set;}="Top50 project";
        public string SourceName {get;set;}
        public string SourceBase64 {get;set;}
        public string SourceSha256 {get;set;}
        public ProjectWallDto[] Walls {get;set;}
        public ProposalDto[] Proposals {get;set;}=Array.Empty<ProposalDto>();
        public string SelectedId {get;set;}
        public string[] SearchLog {get;set;}=Array.Empty<string>();
        public int LongestCount {get;set;}=5;
        public int[] CatalogMm {get;set;}=Precision.CatalogMm.ToArray();
        public int Preset {get;set;}=1;
        public int MaxComponents {get;set;}=12;
        public int MaxFillers {get;set;}=1;
        public int MaxFillerMm {get;set;}=350;
        public int MaxGroups {get;set;}=64;
        public int CandidateDisplayLimit {get;set;}=4;
        public double Seconds {get;set;}=20;
        // Preserve the old JSON member for reading V1 projects. V2 readers treat
        // this value as effort; V1 applications reject V2 instead of misreading it.
        [System.Runtime.Serialization.IgnoreDataMember]
        public double SearchEffort {get=>Seconds;set=>Seconds=value;}
        public int SearchPolicyVersion {get;set;}
        public int Seed {get;set;}=50;
        public int OfferingMode {get;set;}
        public decimal Percentage {get;set;}=45;
        public decimal BandPoints {get;set;}=10;
        public bool AllowExpansion {get;set;}
        public bool RequireIdenticalKits {get;set;}
        public decimal? UnitOverride {get;set;}
        public string UnitOverrideReason {get;set;}
        public int? ConnectionLevels {get;set;}
        public int? ConnectionFaces {get;set;}
        public bool ConnectionReviewed {get;set;}
        public bool AdjustableOnDirect {get;set;}=true;
    }
    public static class NativeProjectStore
    {
        public static NativeTop50Project Capture(ImportSnapshot input,IEnumerable<Proposal> proposals,string selectedId)
        {
            var bytes=string.IsNullOrEmpty(input.SourcePath)?Array.Empty<byte>():File.ReadAllBytes(input.SourcePath);string hash=Hash(bytes);
            if(hash!=input.SourceSha256)throw new InvalidDataException("The original source file changed. Reimport or use the preserved project source.");
            return new NativeTop50Project{SourceName=Path.GetFileName(input.SourcePath??""),SourceBase64=Convert.ToBase64String(bytes),SourceSha256=hash,UnitOverride=input.UnitOverride,UnitOverrideReason=input.UnitOverrideReason,Walls=input.Walls.Select(w=>new ProjectWallDto{Id=w.Id,LengthMm=w.LengthMm,Included=w.Included,Acknowledged=w.Acknowledged,Reason=w.ReviewReason,Manual=w.Id.StartsWith("MANUAL-",StringComparison.Ordinal)}).ToArray(),Proposals=proposals.Select(ToDto).ToArray(),SelectedId=selectedId};
        }
        public static void Save(NativeTop50Project project,string path)
        {
            var target=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=File.Create(temporary)){new DataContractJsonSerializer(typeof(NativeTop50Project)).WriteObject(stream,project);stream.Flush(true);}
                if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public static NativeTop50Project Load(string path)
        {
            NativeTop50Project project;using(var stream=File.OpenRead(path))project=(NativeTop50Project)new DataContractJsonSerializer(typeof(NativeTop50Project)).ReadObject(stream);
            if(project==null||project.Kind!="Doka.Top50.Native"||(project.Version!=1&&project.Version!=2)||project.Walls==null)throw new InvalidDataException("Unsupported Top50 project kind or version.");
            if(Hash(Convert.FromBase64String(project.SourceBase64))!=project.SourceSha256)throw new InvalidDataException("Embedded DXF checksum mismatch.");
            return project;
        }
        public static ImportSnapshot RestoreInput(NativeTop50Project project,string sourceCacheDirectory)
        {
            Directory.CreateDirectory(sourceCacheDirectory);string path=Path.Combine(sourceCacheDirectory,project.SourceSha256+".dxf");
            var bytes=Convert.FromBase64String(project.SourceBase64);if(Hash(bytes)!=project.SourceSha256)throw new InvalidDataException("Embedded source changed.");
            ImportSnapshot input;
            if(bytes.Length==0)input=new ImportSnapshot{SourcePath="",SourceSha256=Hash(bytes),InsUnits=4,Walls=Array.Empty<WallRow>()};
            else
            {
                if(!File.Exists(path))File.WriteAllBytes(path,bytes);else if(Hash(File.ReadAllBytes(path))!=project.SourceSha256)throw new InvalidDataException("Cached source checksum mismatch.");
                input=new Top50DxfImporter().Import(path,new DxfImportOptions{MmPerUnitOverride=project.UnitOverride,OverrideReason=project.UnitOverrideReason});
            }
            var rows=input.Walls.ToList();
            foreach(var manual in project.Walls.Where(w=>w.Manual))
            {
                if(!manual.Id.StartsWith("MANUAL-",StringComparison.Ordinal)||!manual.Acknowledged)throw new InvalidDataException("Manual walls require reviewed stable identities.");
                var row=new WallRow{Id=manual.Id,Handle="",Layer="Manual",SourceUnits=4,MmPerUnit=1,ProjectedRawMm=manual.LengthMm,EndpointDistanceMm=manual.LengthMm};row.Review(manual.LengthMm,manual.Included,manual.Reason);rows.Add(row);
            }
            input.Walls=rows.AsReadOnly();
            var saved=project.Walls.ToDictionary(w=>w.Id);
            if(input.Walls.Count!=saved.Count||input.Walls.Any(w=>!saved.ContainsKey(w.Id)))throw new InvalidDataException("Source wall identities differ from saved project.");
            foreach(var row in input.Walls)
            {
                var prior=saved[row.Id];
                if(prior.Acknowledged)row.Review(prior.LengthMm,prior.Included,prior.Reason);
                else if(prior.LengthMm!=row.LengthMm||prior.Included!=row.Included)throw new InvalidDataException("Unreviewed source values were modified.");
            }
            return input;
        }
        public static ProposalDto ToDto(Proposal p)=>new ProposalDto{Id=p.Id,Stock=p.Stock.Select(x=>new StockDto{SizeMm=x.Key,Quantity=x.Value}).ToArray(),Groups=p.Groups.Select(g=>g.Walls.Select(a=>new PatternDto{WallId=a.WallId,PanelsMm=a.Pattern.PanelsMm.ToArray(),GapsMicrometres=a.Pattern.InternalGapsMicrometres.ToArray()}).ToArray()).ToArray(),Status=p.RetainedModelStatus,Objective=p.RetainedObjective,Bound=p.RetainedBound,KitCount=p.IdenticalKitCount,Rank=p.Rank,Pareto=p.Pareto};
        public static Proposal FromDto(ProposalDto p)=>new Proposal(p.Id,p.Stock.ToDictionary(x=>x.SizeMm,x=>x.Quantity),p.Groups.Select(g=>new CastingGroup(g.Select(a=>new WallArrangement(a.WallId,new PanelPattern(a.PanelsMm,a.GapsMicrometres)))))){RetainedModelStatus=p.Status,RetainedObjective=p.Objective,RetainedBound=p.Bound,IdenticalKitCount=p.KitCount,Rank=p.Rank,Pareto=p.Pareto};
        private static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    }
    public static class Top50Workbook
    {
        public static readonly string[] SheetNames={"Summary","Wall Schedule","Panel Inventory","Panel Arrangements","Casting Schedule","Material Utilization","Connector Schedule","Solution Comparison","Engineering Checks"};
        public static void Export(ImportSnapshot input,Proposal selected,IReadOnlyList<Proposal> alternatives,SearchLimits limits,string path,NativeTop50Project connectorSettings=null)
        {
            using var runtimeScope=Top50Runtime.Enter();
            var connectors=ConnectorAllocation.Calculate(selected,connectorSettings??new NativeTop50Project());
            var issues=Top50Validation.Validate(input.Walls,selected,limits);if(issues.Count>0)throw new InvalidDataException("Only independently validated complete results may export: "+string.Join("; ",issues));
            var labels=new Top50WallLabels(input.Walls);
            var offers=new Top50OfferPresentation(input,connectorSettings??new NativeTop50Project(),alternatives);
            var tables=SheetNames.ToDictionary(n=>n,n=>new List<object[]>());
            tables["Summary"].Add(new object[]{"Metric","Value"});
            tables["Summary"].AddRange(new[]{new object[]{"Selected rank",offers.Rank(selected)},new object[]{"Panel types",selected.Stock.Count},new object[]{"Offered panels",selected.PanelCount},new object[]{"Offered length m",selected.StockMicrometres/1000000m},new object[]{"Casting groups",selected.Groups.Count},new object[]{"Mean utilization",selected.MeanUtilization},new object[]{"Minimum utilization",selected.MinimumUtilization},new object[]{"Connector units",connectors.Description},new object[]{"Source SHA-256",input.SourceSha256},new object[]{"Why this rank",offers.Reason(selected)}});
            tables["Wall Schedule"].Add(new object[]{"Wall","Included","Length mm","Projected raw mm","Aligned endpoint mm","Stored measurement mm","Layer","Review","Reason"});
            tables["Wall Schedule"].AddRange(input.Walls.Select(w=>new object[]{labels[w.Id],w.Included,w.LengthMm,w.ProjectedRawMm,w.EndpointDistanceMm,w.StoredMeasurementMm,w.Layer,string.Join("; ",w.Diagnostics),w.ReviewReason}));
            tables["Panel Inventory"].Add(new object[]{"Nominal length m","Stock quantity","Total m"});tables["Panel Inventory"].AddRange(selected.Stock.OrderBy(s=>s.Key).Select(s=>new object[]{s.Key/1000m,s.Value,s.Key*s.Value/1000m}));
            tables["Panel Arrangements"].Add(new object[]{"Casting Group","Wall","Panel lengths mm","Internal gaps mm","Closure mm"});
            tables["Casting Schedule"].Add(new object[]{"Casting Group","Walls","Installed m","Use fraction","Direct locations","Filler locations","Wall names"});
            tables["Material Utilization"].Add(new object[]{"Casting Group","Nominal mm","Used","Idle"});
            tables["Connector Schedule"].Add(new object[]{"Casting Group","Direct locations","Filler locations","Direct units","Filler units","Reusable direct-only connectors","Reusable universal connectors","Quantity basis"});
            for(int i=0;i<selected.Groups.Count;i++)
            {
                var g=selected.Groups[i];string id=Top50OfferPresentation.CastingGroup(i);
                tables["Casting Schedule"].Add(new object[]{id,g.Walls.Count,g.InstalledPanelMicrometres/1000000m,(decimal)g.InstalledPanelMicrometres/selected.StockMicrometres,g.Walls.Sum(a=>a.Pattern.DirectLocations),g.Walls.Sum(a=>a.Pattern.FillerLocations),string.Join(", ",g.Walls.Select(a=>labels[a.WallId]))});
                foreach(var a in g.Walls)tables["Panel Arrangements"].Add(new object[]{id,labels[a.WallId],string.Join(" + ",a.Pattern.PanelsMm),string.Join(", ",a.Pattern.InternalGapsMicrometres.Select(x=>(x/1000m).ToString(CultureInfo.InvariantCulture))),a.Pattern.LengthMicrometres/1000m});
                foreach(var size in selected.Stock){int used=g.Walls.Sum(a=>a.Pattern.PanelsMm.Count(n=>n==size.Key));tables["Material Utilization"].Add(new object[]{id,size.Key,used,size.Value-used});}
                tables["Connector Schedule"].Add(new object[]{id,g.Walls.Sum(a=>a.Pattern.DirectLocations),g.Walls.Sum(a=>a.Pattern.FillerLocations),connectors.Determined?(object)connectors.DirectUnits[i]:"Not determined",connectors.Determined?(object)connectors.FillerUnits[i]:"Not determined",connectors.Determined?(object)connectors.StandardStock:"Not determined",connectors.Determined?(object)connectors.AdjustableStock:"Not determined",connectors.Description});
            }
            tables["Solution Comparison"].Add(new object[]{"Rank","Types","Panels","Stock m","Casting Groups","Why this rank"});
            tables["Solution Comparison"].AddRange(alternatives.OrderBy(p=>offers.RankNumber(p)).Select(p=>new object[]{offers.Rank(p),p.Stock.Count,p.PanelCount,p.StockMicrometres/1000000m,p.Groups.Count,offers.Reason(p)}));
            tables["Engineering Checks"].Add(new object[]{"Check","Evidence"});
            tables["Engineering Checks"].AddRange(new[]{new object[]{"Measurement policy",input.MeasurementPolicy},new object[]{"Allocation validation","Passed"},new object[]{"Retained solver status",selected.RetainedModelStatus},new object[]{"Retained objective",selected.RetainedObjective},new object[]{"Retained bound",selected.RetainedBound},new object[]{"Global optimality","Not established; retained-pattern bounds only"},new object[]{"Engineering review","Planning proposal only. Doka assembly, walings, ties, pressure, height, corners, clearance, collisions and two-face design require engineering verification. Casting groups are mathematical equipment allocations, not site-approved pour sequences."}});
            using(var document=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook))
            {
                var workbook=document.AddWorkbookPart();workbook.Workbook=new Workbook();var sheets=workbook.Workbook.AppendChild(new Sheets());uint index=1;
                foreach(var name in SheetNames)
                {
                    var part=workbook.AddNewPart<WorksheetPart>();var data=new SheetData();
                    foreach(var values in tables[name])
                    {
                        var row=new Row();foreach(var value in values)
                        {
                            bool numeric=value is int||value is long||value is decimal||value is double;
                            row.Append(numeric?new Cell{DataType=CellValues.Number,CellValue=new CellValue(Convert.ToString(value,CultureInfo.InvariantCulture))}:new Cell{DataType=CellValues.InlineString,InlineString=new InlineString(new Text(Convert.ToString(value,CultureInfo.InvariantCulture)??""))});
                        }data.Append(row);
                    }
                    part.Worksheet=new Worksheet(new SheetViews(new SheetView(new Pane{VerticalSplit=1,TopLeftCell="A2",ActivePane=PaneValues.BottomLeft,State=PaneStateValues.Frozen}){WorkbookViewId=0}),data);
                    sheets.Append(new Sheet{Id=workbook.GetIdOfPart(part),SheetId=index++,Name=name});part.Worksheet.Save();
                }workbook.Workbook.Save();
            }
        }
    }
}
