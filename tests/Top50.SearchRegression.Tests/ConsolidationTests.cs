using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EngineeringDashboard.Top50;

internal static class ConsolidationTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static string Key(Proposal p)=>Top50Order.Stock(p.Stock)+"/"+string.Join("|",p.Groups.Select(Top50Order.Group));
    static RankingScales Scales(IReadOnlyList<WallRow> walls,int types=18)=>new RankingScales(walls.Sum(w=>(decimal)w.LengthMicrometres)/1000000m,types,64,walls.Count);
    internal static void Run()
    {
        var walls=Top50PanelConsolidationReport.Walls();var original=Top50PanelConsolidationReport.Proposal();var limits=new SearchLimits(1,350,64,5,100,50);
        Check(Top50Validation.Validate(walls,original,limits).Count==0,"Captured original allocation is valid.");
        var variants=PanelSizeConsolidation.Improve(original,walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,Scales(walls),p=>true,CancellationToken.None);
        var improved=variants.Single(p=>!p.Stock.ContainsKey(500)&&p.Stock.TryGetValue(750,out int n)&&n==2);
        Check(Top50Validation.Validate(walls,improved,limits).Count==0,"The substitution closes every wall, retains mandatory simultaneous longest walls and respects reusable stock.");
        Check(improved.PanelCount==23&&Top50PlannerPolicy.TotalPanels(improved)==46&&improved.Stock.Count==5,"Both-face quantities stay at 46 while types reduce from six to five.");
        Check(improved.StockMicrometres==99500000&&improved.Groups.Count==10,"Offered LM is 99.50 and casting groups stay at ten.");
        Check(improved.Groups.Select(g=>string.Join(",",g.Walls.Select(a=>a.WallId))).SequenceEqual(original.Groups.Select(g=>string.Join(",",g.Walls.Select(a=>a.WallId)))),"Group membership and its displayed order are retained.");
        Check(improved.Groups.SelectMany(g=>g.Walls).Where(a=>walls.Single(w=>w.Id==a.WallId).LengthMm==1600).All(a=>a.Pattern.PanelsMm.SequenceEqual(new[]{750,750})&&a.Pattern.InternalGapsMicrometres.Single()==100000),"All nine 1600 mm walls use two 750s and a 100 mm internal filler.");
        Check(improved.Groups[1].Walls.Single(w=>w.WallId=="W-16").Pattern.InternalGapsMicrometres.Single()==60000,"The 2810 mm replacement keeps a 60 mm filler.");
        Check(improved.Groups.All(g=>g.Walls.Sum(a=>a.Pattern.PanelsMm.Count(p=>p==750))==2),"Exactly two reusable 750s per face cover every group.");
        Check(original.Stock.ContainsKey(500)&&original.Groups[0].Walls.Last().Pattern.PanelsMm.Contains(500),"Original checked results are not mutated.");
        Check(improved.RetainedBound==0&&improved.RetainedModelStatus.Contains("not re-solved"),"An unchanged feasible allocation does not inherit a solver optimality claim for its changed inventory.");
        Check(ProposalRanking.Rank(new[]{RankingMetrics.From(original),RankingMetrics.From(improved)},PlanningPreset.BalancedOptimization,2,Scales(walls))[0].Id==improved.Id,"Current balanced policy prefers consolidation.");
        Check(ProposalRanking.Rank(new[]{RankingMetrics.From(original),RankingMetrics.From(improved)},PlanningPreset.EconomicalOffer,2,Scales(walls))[0].Id==original.Id,"Economical policy retains the shorter original inventory.");
        var restricted=PanelSizeConsolidation.Improve(original,walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,Scales(walls),p=>p.StockMicrometres<=original.StockMicrometres,CancellationToken.None);
        Check(!restricted.Any(p=>Key(p)==Key(improved)),"The improvement cannot escape a required offer-length band.");
        var notPermitted=PanelSizeConsolidation.Improve(original,walls,Precision.CatalogMm.Where(n=>n!=750).ToArray(),limits,PlanningPreset.BalancedOptimization,Scales(walls),p=>true,CancellationToken.None);
        Check(!notPermitted.Any(p=>!p.Stock.ContainsKey(500)&&p.Stock.ContainsKey(750)),"Replacement targets must be permitted panel sizes.");
        var copied=NativeProjectStore.FromDto(NativeProjectStore.ToDto(improved));
        Check(Key(copied)==Key(improved)&&Top50Validation.Validate(walls,copied,limits).Count==0,"Consolidated inventory, arrangements and honest metadata survive save/load.");
        Rules();
        using(var stop=new CancellationTokenSource())
        {
            bool cancelled=false;
            try {PanelSizeConsolidation.Improve(original,walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,Scales(walls),p=>{stop.Cancel();return true;},stop.Token);}
            catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Cancellation during consolidation cannot publish a partial shortlist.");
        }
        decimal percentage=original.StockMicrometres/walls.Sum(w=>(decimal)w.LengthMicrometres)*100;
        var banded=new Top50Search(new FixedInventory(original),new FixedAllocation(original)).Run(walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,3,CancellationToken.None,percentage:percentage,bandPoints:0.000001m);
        Check(banded.Proposals.Count==1&&banded.Proposals[0].StockMicrometres==original.StockMicrometres,"Full percentage search cannot add the longer consolidation outside the required band.");
        var clock=Stopwatch.StartNew();
        var run=new Top50Search().Run(walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,3,CancellationToken.None);clock.Stop();
        Check(run.Proposals.Count==3&&run.Proposals[0].Stock.ContainsKey(750)&&!run.Proposals[0].Stock.ContainsKey(500)&&run.Proposals[0].Stock.Count==5,"Full search places the reported replacement first before the display limit.");
        Check(run.Proposals[0].StockMicrometres==99500000&&run.Proposals[0].Groups.Count==10&&run.Proposals.All(p=>Top50Validation.Validate(walls,p,limits).Count==0),"The full search returns independently validated unchanged-quantity offers.");
        Check(run.Proposals.Select(Key).Distinct().Count()==run.Proposals.Count,"Equivalent inventory/arrangement pairs do not repeat in the shortlist.");
        Check(run.Log.Any(line=>line.Contains("consolidation checked")),"Diagnostics record the consolidation pass.");
        var again=new Top50Search().Run(walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,3,CancellationToken.None);
        Check(run.Proposals.Select(p=>p.Id+"/"+Key(p)).SequenceEqual(again.Proposals.Select(p=>p.Id+"/"+Key(p))),"Repeated complete searches produce the same consolidated offers.");
        Console.WriteLine("Reported 65-wall consolidation search: "+clock.Elapsed.TotalSeconds.ToString("0.000")+" s; 46 panels / 99.50 LM / five sizes / ten groups.");
        Console.WriteLine("PASS "+checks+" panel consolidation checks.");
    }
    sealed class FixedInventory:ITop50InventoryCandidateSource
    {
        readonly Proposal source;internal FixedInventory(Proposal source){this.source=source;}
        public IEnumerable<IDictionary<int,int>> Generate(IReadOnlyList<WallRow> walls,IReadOnlyList<int> catalog,SearchLimits limits,CancellationToken token)
        {yield return source.Stock.ToDictionary(p=>p.Key,p=>p.Value);}
    }
    sealed class FixedAllocation:ITop50InventorySolver
    {
        readonly Proposal source;internal FixedAllocation(Proposal source){this.source=source;}
        public Proposal Solve(string id,IReadOnlyList<WallRow> walls,IDictionary<int,int> stock,SearchLimits limits,CancellationToken token,IProgress<string> progress=null)
        {return new Proposal(id,stock,source.Groups);}
    }
    static void Rules()
    {
        var wall=new[] {new WallRow {Id="fill",LengthMicrometres=1000000,Included=true,Status=WallReviewStatus.Valid}};
        var source=new Proposal("fill",new Dictionary<int,int>{{400,1},{600,1}},new[]{new CastingGroup(new[]{new WallArrangement("fill",new PanelPattern(new[]{600,400},new long[]{0}))})});
        foreach(var limits in new[]{new SearchLimits(0,350,4,1,1,50),new SearchLimits(1,150,4,1,1,50)})
            Check(PanelSizeConsolidation.Improve(source,wall,new[]{400,600},limits,PlanningPreset.BalancedOptimization,Scales(wall,2),p=>true,CancellationToken.None).Count==0,"Filler count/width bounds reject an overlong or underfilled substitution.");
        var permitted=new SearchLimits(1,350,4,1,1,50);
        var filled=PanelSizeConsolidation.Improve(source,wall,new[]{400,600},permitted,PlanningPreset.BalancedOptimization,Scales(wall,2),p=>true,CancellationToken.None).Single();
        Check(filled.Stock.Single().Key==400&&filled.Groups[0].Walls[0].Pattern.InternalGapsMicrometres.Single()==200000,"A smaller replacement creates an internal filler only when allowed.");
        var kitWalls=new[]{new WallRow {Id="K1",LengthMicrometres=1600000,Included=true,Status=WallReviewStatus.Valid},new WallRow {Id="K2",LengthMicrometres=1600000,Included=true,Status=WallReviewStatus.Valid}};
        var kits=new SearchLimits(1,350,4,2,1,50,requireIdenticalKits:true);
        var kitSource=new Proposal("kits",new Dictionary<int,int>{{500,2},{750,2}},new[]{new CastingGroup(kitWalls.Select(w=>new WallArrangement(w.Id,new PanelPattern(new[]{750,500},new long[]{350000})) ))}) {IdenticalKitCount=2};
        var kit=PanelSizeConsolidation.Improve(kitSource,kitWalls,new[]{500,750},kits,PlanningPreset.BalancedOptimization,Scales(kitWalls,2),p=>true,CancellationToken.None).Single();
        Check(kit.IdenticalKitCount==2&&kit.Stock[750]==4&&Top50Validation.Validate(kitWalls,kit,kits).Count==0,"Consolidation preserves repeated-kit counts and per-kit wall fit.");
    }
}
