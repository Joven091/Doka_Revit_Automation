using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static void Check(bool test,string name){if(!test)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    private static int Main(string[] args)
    {
        Check(Precision.ToMicrometres(1.2345m)==1235,"micrometre midpoint rounds half up");
        Check(Precision.ToMicrometres(19204.608294923666m)==19204608,"original V1 one-time precision");
        var source=new Top50DxfImporter().Import(args[0],new DxfImportOptions());
        Check(source.SourceSha256=="4de6bfde8c4a86d6de2997d07f6074ebed9d5d3e9a3d70198bc1974e097d52b5","reference byte identity");
        Check(source.InsUnits==4&&source.Walls.Count==65,"65 mm-unit source dimension rows");
        var valid=source.Walls.Where(w=>w.Included).ToArray();
        Check(valid.Length==63&&source.Walls.Count(w=>w.Status==WallReviewStatus.NeedsReview)==2,"original 63 included / 2 review scope");
        Check(valid.Sum(w=>w.LengthMicrometres)==798470688,"rounded-once source total");
        Check(valid.Max(w=>w.LengthMicrometres)==19204608,"rounded-once longest wall");
        var distribution=valid.GroupBy(w=>w.LengthMicrometres).ToDictionary(g=>g.Key,g=>g.Count());
        Check(distribution.Count==6&&distribution[1600000]==9&&distribution[2810000]==2&&distribution[7200000]==10&&distribution[9200000]==10&&distribution[19200000]==21&&distribution[19204608]==11,"recorded distribution from real DXF");
        var conflict=source.Walls.Single(w=>w.Handle=="4054B28");
        Check(conflict.Id=="DXF-4054B28"&&!conflict.Included&&conflict.ProjectedRawMm<1&&conflict.EndpointDistanceMm>19199&&conflict.StoredMeasurementMm>19199,"aligned interpretation conflict retains both geometry interpretations and stored value");
        Check(conflict.RawCodes.Any(c=>c.Code==42)&&!conflict.Acknowledged,"raw measurement evidence and acknowledgement state");
        Check(valid.All(w=>w.Id=="DXF-"+w.Handle)&&valid.All(w=>w.LabelScale==1),"original handle IDs and annotation scale");
        try{conflict.Review(19200,true,"");throw new Exception("missing reason accepted");}catch(ArgumentException){checks++;}
        var limits=new SearchLimits(1,350,15,5,10,50);
        var patterns=new FewestComponentPatterns();
        var p1600=patterns.Enumerate(1600000,new[]{500,750},limits,CancellationToken.None);
        Check(p1600.Any(p=>p.PanelsMm.SequenceEqual(new[]{750,500})&&p.InternalGapsMicrometres.SequenceEqual(new long[]{350000})),"internal 350mm exact filler fixture");
        Check(patterns.Enumerate(400000,Precision.CatalogMm,limits,CancellationToken.None).Count==0,"short wall cannot overrun a panel");
        var goldenStock=new Dictionary<int,int>{{500,1},{750,1},{2000,5},{3500,5},{4500,15}};
        var recorded=Top50RecordedFixture.Create(source);
        Check(Top50Validation.Validate(source.Walls,recorded,limits).Count==0,"all 63 recorded group members independently validate with reconstructed compatible patterns");
        Check(decimal.Round(recorded.MeanUtilization*100,2)==74.12m&&decimal.Round(recorded.MinimumUtilization*100,2)==35.58m&&recorded.PanelChanges==93,"recorded S3 utilization and panel changes follow from complete fixture, not display constants");
        Check(recorded.Groups.Select(g=>g.InstalledPanelMicrometres/1000000m).SequenceEqual(new[]{96.250m,34.250m,79.750m,65.250m,65.250m,78.750m,34.250m,96.250m,92.250m,96.250m,46.250m}),"every recorded group installed total matches");
        Check(ConnectorAllocation.Calculate(recorded,new NativeTop50Project()).AssumedSingleLevel,"default connector quantities use the agreed one-level, per-face assumption");
        var connectionSettings=new NativeTop50Project{ConnectionLevels=2,ConnectionFaces=2,ConnectionReviewed=true,AdjustableOnDirect=true};var connector=ConnectorAllocation.Calculate(recorded,connectionSettings);
        Check(connector.Determined&&connector.AdjustableStock==40&&connector.StandardStock==44,"reviewed connector stock uses peak simultaneous total and permits spare adjustable on direct joints");
        connectionSettings.AdjustableOnDirect=false;Check(ConnectorAllocation.Calculate(recorded,connectionSettings).StandardStock==60,"direct substitution assumption affects only compatible reviewed connector units");
        Check(goldenStock.Sum(p=>p.Key*p.Value)==96250&&goldenStock.Sum(p=>p.Value)==27,"observed stock arithmetic is fixture only");
        var g03Ids=new[]{"4054B7D","4054B8C","4054BA5","4054BBE","4054BD7","4054BF0","4054C09"};
        var g03=new List<WallArrangement>();
        g03.AddRange(g03Ids.Select(id=>new WallArrangement("DXF-"+id,new PanelPattern(new[]{4500,4500},new long[]{200000}))));
        g03.AddRange(new[]{"4054B4B","4054B50"}.Select(id=>new WallArrangement("DXF-"+id,new PanelPattern(new[]{3500,3500},new long[]{200000}))));
        g03.Add(new WallArrangement("DXF-4054B82",new PanelPattern(new[]{2000,750},new long[]{60000})));
        Check(g03.All(a=>a.Pattern.LengthMicrometres==valid.Single(w=>w.Id==a.WallId).LengthMicrometres),"all ten observed G03 arrangements close to their actual source walls");
        var g03Group=new CastingGroup(g03);
        Check(g03Group.InstalledPanelMicrometres==79750000&&g03.Sum(a=>a.Pattern.FillerLocations)==10&&g03.Sum(a=>a.Pattern.DirectLocations)==0,"G03 79.750m / ten fillers / zero direct joints");
        var partial=new Proposal("partial",goldenStock,new[]{g03Group});
        Check(Top50Validation.Validate(source.Walls,partial,limits).Any(e=>e.StartsWith("Included wall not assigned")),"partial reference arrangement is never accepted as a complete result");
        var ranked=ProposalRanking.Rank(new[]{new RankingMetrics("S3",27,96.250m,5,11,.7412m,63,.3558m),new RankingMetrics("S4",24,99m,8,12,.6606m,63,.4672m),new RankingMetrics("S5",23,100.250m,5,11,.7137m,63,.3142m),new RankingMetrics("S1",23,99.250m,6,11,.7188m,63,.1184m)},PlanningPreset.BalancedOptimization,4,new RankingScales(valid.Sum(w=>w.LengthMm)/1000,18,15,63));
        Check(ranked.Count==2&&ranked.All(c=>c.PanelCount==23),"quantity-first ranking excludes the higher-count historical offers");
        Check(ranked.All(c=>c.Pareto),"Pareto tradeoffs compare the eligible 23-panel alternatives");
        var generated=new CatalogCutoffCandidates().Generate(source.Walls,Precision.CatalogMm,limits,CancellationToken.None).ToArray();
        Check(generated.Any(stock=>stock.Count==goldenStock.Count&&goldenStock.All(p=>stock.TryGetValue(p.Key,out var count)&&count==p.Value)),"general catalog-cutoff discovery includes observed inventory without a case-specific stock constant");
        var cancel=new CancellationToken(true);
        try{patterns.Enumerate(19200000,Precision.CatalogMm,limits,cancel);throw new Exception("cancellation ignored");}catch(OperationCanceledException){checks++;}
        var clock=Stopwatch.StartNew();
        var proposal=new RetainedInventorySolver().Solve("fixture-stock-general-allocation",source.Walls,goldenStock,limits,CancellationToken.None);
        Check(proposal!=null,"general retained model produces a complete allocation for the observed inventory");
        Check(Top50Validation.Validate(source.Walls,proposal,limits).Count==0,"complete allocation independently validated");
        Check(proposal.FillerLocations==63,"one internal filler for every included reference wall");
        var four=Enumerable.Range(1,4).Select(i=>new WallRow{Id="MANUAL-"+i,LengthMicrometres=5000000,Included=true,Status=WallReviewStatus.Valid}).ToArray();
        var percentageRun=new Top50Search().Run(four,new[]{5000},new SearchLimits(1,350,4,1,3,50),PlanningPreset.BalancedOptimization,4,CancellationToken.None,percentage:45,bandPoints:10);
        Check(percentageRun.Proposals.Count>0&&percentageRun.Proposals.All(p=>p.StockMicrometres==10000000&&p.Groups.Count==2),"percentage band uses whole walls and a valid 50-percent stock proposal for a 45-percent reference");
        var identicalRun=new Top50Search().Run(four,new[]{5000},new SearchLimits(1,350,4,2,3,50),PlanningPreset.BalancedOptimization,4,CancellationToken.None,identicalKits:true);
        Check(identicalRun.Proposals.Any(p=>p.IdenticalKitCount==2&&Top50Validation.Validate(four,p,new SearchLimits(1,350,4,2,3,50)).Count==0),"declared identical kits are validated against actual required-wall arrangements");
        Console.WriteLine("GENERAL ALLOCATION groups="+proposal.Groups.Count+" mean="+proposal.MeanUtilization.ToString("P4",CultureInfo.InvariantCulture)+" minimum="+proposal.MinimumUtilization.ToString("P4",CultureInfo.InvariantCulture)+" status="+proposal.RetainedModelStatus+" objective="+proposal.RetainedObjective+" bound="+proposal.RetainedBound+" elapsed="+clock.Elapsed.TotalSeconds.ToString("F3",CultureInfo.InvariantCulture));
        Console.WriteLine("NOTE: allocation is independently valid; exact original group membership, all candidates and hidden settings remain unverified.");
        Directory.CreateDirectory(args[1]);
        File.WriteAllLines(Path.Combine(args[1],"core-checks.txt"),new[]{checks+" checks passed", "groups="+proposal.Groups.Count,"mean="+proposal.MeanUtilization.ToString(CultureInfo.InvariantCulture),"minimum="+proposal.MinimumUtilization.ToString(CultureInfo.InvariantCulture),"status="+proposal.RetainedModelStatus});
        if(args.Contains("--discover"))
        {
            var discovery=new Top50Search().Run(source.Walls,Precision.CatalogMm,new SearchLimits(1,350,15,5,5,50),PlanningPreset.BalancedOptimization,4,CancellationToken.None);
            Check(discovery.Proposals.Count>0&&discovery.Proposals.All(p=>Top50Validation.Validate(source.Walls,p,limits).Count==0),"fresh end-to-end discovery returns independently validated proposals");
            foreach(var p in discovery.Proposals)Console.WriteLine("DISCOVERY "+p.Id+" rank="+p.Rank+" stock="+p.StockMicrometres/1000000m+" groups="+p.Groups.Count+" mean="+p.MeanUtilization+" minimum="+p.MinimumUtilization);
            var document=NativeProjectStore.Capture(source,discovery.Proposals,discovery.Proposals[0].Id);document.MaxGroups=15;document.Seconds=5;document.SearchLog=discovery.Log.ToArray();NativeProjectStore.Save(document,Path.Combine(args[1],"fresh-discovery.top50"));
            File.WriteAllLines(Path.Combine(args[1],"discovery-log.txt"),discovery.Log);
        }
        return 0;
    }
}
