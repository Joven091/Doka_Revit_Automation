using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    public enum WallReviewStatus { Valid, NeedsReview, Excluded }

    public sealed class SourceCode
    {
        public int Code { get; }
        public string Value { get; }
        public SourceCode(int code, string value) { Code=code; Value=value; }
    }

    public sealed class WallRow
    {
        public string Id { get; internal set; }
        public string Handle { get; internal set; }
        public string Layer { get; internal set; }
        public int DimensionType { get; internal set; }
        public int SourceUnits { get; internal set; }
        public decimal MmPerUnit { get; internal set; }
        public decimal ProjectedRawMm { get; internal set; }
        public decimal EndpointDistanceMm { get; internal set; }
        public decimal? StoredMeasurementMm { get; internal set; }
        public decimal LabelScale { get; internal set; }
        public string Text { get; internal set; }
        public long LengthMicrometres { get; internal set; }
        public WallReviewStatus Status { get; internal set; }
        public bool Included { get; internal set; }
        public bool Acknowledged { get; private set; }
        public string ReviewReason { get; private set; }="";
        public IReadOnlyList<string> Diagnostics { get; internal set; }=Array.Empty<string>();
        public IReadOnlyList<SourceCode> RawCodes { get; internal set; }=Array.Empty<SourceCode>();
        public decimal LengthMm => LengthMicrometres/1000m;
        public WallRow Snapshot()=>new WallRow{Id=Id,Handle=Handle,Layer=Layer,DimensionType=DimensionType,SourceUnits=SourceUnits,MmPerUnit=MmPerUnit,ProjectedRawMm=ProjectedRawMm,EndpointDistanceMm=EndpointDistanceMm,StoredMeasurementMm=StoredMeasurementMm,LabelScale=LabelScale,Text=Text,LengthMicrometres=LengthMicrometres,Status=Status,Included=Included,Acknowledged=Acknowledged,ReviewReason=ReviewReason,Diagnostics=Array.AsReadOnly(Diagnostics.ToArray()),RawCodes=Array.AsReadOnly(RawCodes.ToArray())};
        public void Review(decimal measuredMm, bool include, string reason)
        {
            if(string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Record the measurement review reason.");
            var length=Precision.ToMicrometres(measuredMm);
            if(length<0 || (include && length<=0)) throw new ArgumentException("Included walls need positive measured geometry.");
            LengthMicrometres=length;Included=include;Acknowledged=true;ReviewReason=reason;
            Status=include?WallReviewStatus.Valid:WallReviewStatus.Excluded;
        }
    }

    public static class Precision
    {
        public static long ToMicrometres(decimal mm) => checked((long)(decimal.Round(mm,3,MidpointRounding.AwayFromZero)*1000m));
        public static readonly int[] CatalogMm={500,750,1000,1250,1500,1750,2000,2250,2440,2500,2750,3000,3500,4000,4500,5000,5500,6000};
    }

    public sealed class ImportSnapshot
    {
        public string SourcePath { get; internal set; }
        public string SourceSha256 { get; internal set; }
        public int InsUnits { get; internal set; }
        public decimal? UnitOverride {get;internal set;}
        public string UnitOverrideReason {get;internal set;}="";
        public IReadOnlyList<WallRow> Walls { get; internal set; }
        public string MeasurementPolicy => "Original V1 angle-projection compatibility audit; nearest micrometre, half up. Aligned endpoint distance retained separately; conflicts require review.";
    }

    public sealed class DxfImportOptions
    {
        public string Layer { get; set; }="Wall_Length";
        public decimal? MmPerUnitOverride { get; set; }
        public string OverrideReason { get; set; }="";
    }

    public sealed class SearchLimits
    {
        public int MaxInternalFillers { get; }
        public int MaxFillerMm { get; }
        public int PatternsPerLength { get; }
        public int MaximumGroups { get; }
        public int LongestSimultaneous { get; }
        // Seconds is retained as a source-compatible alias for older callers.
        // It now selects repeatable work, never a wall-clock deadline.
        public double Seconds => SearchEffort;
        public double SearchEffort { get; }
        public double SolverDeterministicWork { get; }
        public long PatternVisitLimit { get; }
        public int Seed { get; }
        public SearchLimits(int maxInternalFillers,int maxFillerMm,int maximumGroups,int longestSimultaneous,double seconds,int seed,int patternsPerLength=4,double? solverDeterministicWork=null)
        {
            double work=solverDeterministicWork??seconds;
            if(maxInternalFillers<0 || maxFillerMm<0 || maxFillerMm>350 || maximumGroups<1 || longestSimultaneous<1 || seconds<=0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || work<=0 || double.IsNaN(work) || double.IsInfinity(work) || patternsPerLength<1 || patternsPerLength>4) throw new ArgumentOutOfRangeException(nameof(maxInternalFillers));
            MaxInternalFillers=maxInternalFillers;MaxFillerMm=maxFillerMm;MaximumGroups=maximumGroups;LongestSimultaneous=longestSimultaneous;SearchEffort=seconds;SolverDeterministicWork=work;Seed=seed;PatternsPerLength=patternsPerLength;
            PatternVisitLimit=(long)Math.Max(1,Math.Ceiling(Math.Min(1000000000,seconds*50000)));
        }
    }

    public sealed class PanelPattern
    {
        public IReadOnlyList<int> PanelsMm { get; }
        public IReadOnlyList<long> InternalGapsMicrometres { get; }
        public long LengthMicrometres => PanelsMm.Sum(x=>(long)x*1000)+InternalGapsMicrometres.Sum();
        public int FillerLocations => InternalGapsMicrometres.Count(x=>x>0);
        public int DirectLocations => InternalGapsMicrometres.Count(x=>x==0);
        public PanelPattern(IEnumerable<int> panels,IEnumerable<long> gaps) { PanelsMm=Array.AsReadOnly(panels.ToArray());InternalGapsMicrometres=Array.AsReadOnly(gaps.ToArray()); }
    }

    public sealed class WallArrangement
    {
        public string WallId { get; }
        public PanelPattern Pattern { get; }
        public WallArrangement(string wallId,PanelPattern pattern) {WallId=wallId;Pattern=pattern;}
    }
    public sealed class CastingGroup
    {
        public IReadOnlyList<WallArrangement> Walls { get; }
        public CastingGroup(IEnumerable<WallArrangement> walls) {Walls=Array.AsReadOnly(walls.ToArray());}
        public long InstalledPanelMicrometres => Walls.Sum(a=>a.Pattern.PanelsMm.Sum(p=>(long)p*1000));
    }
    public sealed class Proposal
    {
        public string Id { get; }
        public IReadOnlyDictionary<int,int> Stock { get; }
        public IReadOnlyList<CastingGroup> Groups { get; }
        public string RetainedModelStatus { get; internal set; }
        public double RetainedObjective { get; internal set; }
        public double RetainedBound { get; internal set; }
        public int IdenticalKitCount { get; internal set; }
        public int Rank { get; internal set; }
        public bool Pareto { get; internal set; }
        public Proposal(string id,IDictionary<int,int> stock,IEnumerable<CastingGroup> groups)
        { Id=id;Stock=new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(stock.OrderBy(p=>p.Key).ToDictionary(p=>p.Key,p=>p.Value));Groups=Array.AsReadOnly(groups.ToArray()); }
        public long StockMicrometres => Stock.Sum(x=>(long)x.Key*x.Value*1000);
        public int PanelCount => Stock.Sum(x=>x.Value);
        public decimal MeanUtilization => Groups.Count==0||StockMicrometres==0?0:Groups.Sum(g=>(decimal)g.InstalledPanelMicrometres)/(Groups.Count*StockMicrometres);
        public decimal MinimumUtilization => Groups.Count==0||StockMicrometres==0?0:Groups.Min(g=>(decimal)g.InstalledPanelMicrometres/StockMicrometres);
        public int FillerLocations => Groups.Sum(g=>g.Walls.Sum(a=>a.Pattern.FillerLocations));
        public int PanelChanges => Enumerable.Range(1,Math.Max(0,Groups.Count-1)).Sum(i=>Stock.Keys.Sum(size=>Math.Abs(Groups[i].Walls.Sum(a=>a.Pattern.PanelsMm.Count(p=>p==size))-Groups[i-1].Walls.Sum(a=>a.Pattern.PanelsMm.Count(p=>p==size)))));
    }

    public interface ITop50Importer { ImportSnapshot Import(string path,DxfImportOptions options); }
    public interface ITop50PatternSource { IReadOnlyList<PanelPattern> Enumerate(long lengthMicrometres,IEnumerable<int> catalogMm,SearchLimits limits,CancellationToken cancellation); }
    public interface ITop50InventorySolver
    { Proposal Solve(string id,IReadOnlyList<WallRow> walls,IDictionary<int,int> stock,SearchLimits limits,CancellationToken cancellation,IProgress<string> progress=null); }
    // Candidate discovery is intentionally injected. Original catalog strategies are not established by one recording.
    public interface ITop50InventoryCandidateSource
    { IEnumerable<IDictionary<int,int>> Generate(IReadOnlyList<WallRow> walls,IReadOnlyList<int> allowedCatalogMm,SearchLimits limits,CancellationToken cancellation); }

    public static class Top50Order
    {
        public static string Stock(IEnumerable<KeyValuePair<int,int>> stock)=>string.Join(";",stock.OrderBy(p=>p.Key).Select(p=>p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        public static string Group(CastingGroup group)=>string.Join(";",group.Walls.OrderBy(w=>w.WallId,StringComparer.Ordinal).Select(w=>w.WallId+":"+string.Join(",",w.Pattern.PanelsMm.Select(p=>p.ToString(System.Globalization.CultureInfo.InvariantCulture)))+"/"+string.Join(",",w.Pattern.InternalGapsMicrometres.Select(g=>g.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
    }
}
