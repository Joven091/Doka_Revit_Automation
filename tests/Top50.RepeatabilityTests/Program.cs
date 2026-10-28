using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    private static void Fails(Action action,string name){bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,name);}
    private static RankingMetrics Metric(string id,int panels,decimal metres,int groups=11,int types=5,decimal mean=.74m,decimal minimum=.35m)
        =>new RankingMetrics(id,panels,metres,types,groups,mean,63,minimum,stableKey:id);
    private static WallRow Wall(string id,long micrometres)=>new WallRow{Id=id,LengthMicrometres=micrometres,Included=true,Status=WallReviewStatus.Valid};
    private static SearchLimits Limits(int longest=1,double effort=1)=>new SearchLimits(1,350,64,longest,effort,50);
    private static string Signature(SearchRun run)
    {
        var text=new StringBuilder();text.AppendLine(run.Attempted+"/"+run.Unsearched+"/"+run.SkippedForPanelQuantity);
        foreach(var p in run.Proposals)
        {
            text.AppendLine(p.Id+"|"+p.Rank+"|"+p.Pareto+"|"+Top50Order.Stock(p.Stock)+"|"+p.RetainedModelStatus+"|"+p.RetainedObjective.ToString("R",CultureInfo.InvariantCulture)+"|"+p.RetainedBound.ToString("R",CultureInfo.InvariantCulture));
            foreach(var g in p.Groups)text.AppendLine(Top50Order.Group(g));
        }
        return text.ToString();
    }
    private static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    private static int Main(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
            if(args.Contains("--signature")){Distribution(output,args.Contains("--reverse"));return 0;}
            Ranking();FixedWork();Projects(output);AutomaticPanelCounts(output);
            File.WriteAllText(Path.Combine(output,"checks.txt"),checks+" quantity/work/project checks passed. Fresh-process native allocation checks run separately.");
            Console.WriteLine(checks+" quantity/work/project checks passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void Ranking()
    {
        var scales=new RankingScales(798.470688m,18,64,63);
        foreach(PlanningPreset preset in Enum.GetValues(typeof(PlanningPreset)))
        {
            var candidates=new[]{Metric("sixty",60,95,10,4,.82815789m,.40526316m),Metric("twenty-seven",27,96.25m),Metric("twenty-three",23,100.25m)};
            var ranked=ProposalRanking.Rank(candidates,preset,100,scales);
            Check(ranked.Count==1&&ranked[0].Id=="twenty-three","23 panels beats 27 and 60 under "+preset+", regardless of metre/group advantages");
            Check(!candidates[0].Pareto&&!candidates[1].Pareto,"higher quantities are outside the client shortlist under "+preset);
        }
        var a=Metric("a",23,99.25m,11,6,.7188m,.1184m);var b=Metric("b",23,100.25m,11,5,.7137m,.3142m);
        var first=ProposalRanking.Rank(new[]{a,b},PlanningPreset.BalancedOptimization,2,scales).Select(x=>x.Id).ToArray();decimal score=a.BalancedScore;
        var outsider=Metric("outsider",23,1000,64,18,0,0);
        var second=ProposalRanking.Rank(new[]{outsider,b,a},PlanningPreset.BalancedOptimization,2,scales).Select(x=>x.Id).ToArray();
        Check(a.BalancedScore==score&&first.SequenceEqual(second),"a dominated extra candidate cannot change existing scores or order");
        var tie1=Metric("z-id",23,100);var tie2=new RankingMetrics("a-id",23,100,5,11,.74m,63,.35m,stableKey:"zz-inventory");
        Check(ProposalRanking.Rank(new[]{tie2,tie1},PlanningPreset.BalancedOptimization,2,scales)[0].Id=="z-id","canonical inventory key resolves an equal score before scenario ID");
        Fails(()=>Metric("bad",0,100),"zero panel count cannot enter ranking");
    }
    private sealed class Candidates:ITop50InventoryCandidateSource
    {
        private readonly IDictionary<int,int>[] stocks;
        public Candidates(params IDictionary<int,int>[] stocks){this.stocks=stocks;}
        public IEnumerable<IDictionary<int,int>> Generate(IReadOnlyList<WallRow>w,IReadOnlyList<int>c,SearchLimits l,CancellationToken t)=>stocks;
    }
    private sealed class Solver:ITop50InventorySolver
    {
        public readonly List<double> Quotas=new List<double>();public int DelayMs;
        public Proposal Solve(string id,IReadOnlyList<WallRow>w,IDictionary<int,int>s,SearchLimits l,CancellationToken t,IProgress<string>p=null)
        {
            Quotas.Add(l.SolverDeterministicWork);if(DelayMs>0)Thread.Sleep(DelayMs);t.ThrowIfCancellationRequested();
            var panels=s.OrderBy(x=>x.Key).SelectMany(x=>Enumerable.Repeat(x.Key,x.Value)).ToArray();
            return new Proposal(id,s,new[]{new CastingGroup(new[]{new WallArrangement(w[0].Id,new PanelPattern(panels,new long[panels.Length-1]))})}){RetainedModelStatus="Controlled exact allocation"};
        }
    }
    private static void FixedWork()
    {
        var walls=new[]{Wall("wall",5000000)};var limits=Limits();
        var stocks=new[]{new Dictionary<int,int>{{2500,2}},new Dictionary<int,int>{{4500,1},{500,1}},new Dictionary<int,int>{{3000,1},{2000,1}}};
        var fast=new Solver();var fastRun=new Top50Search(new Candidates(stocks),fast).Run(walls,new[]{500,2000,2500,3000,4500},limits,PlanningPreset.BalancedOptimization,4,CancellationToken.None);
        var slow=new Solver{DelayMs=450};var clock=Stopwatch.StartNew();
        var slowRun=new Top50Search(new Candidates(Enumerable.Reverse(stocks).ToArray()),slow).Run(walls,new[]{4500,3000,2500,2000,500},limits,PlanningPreset.BalancedOptimization,4,CancellationToken.None);clock.Stop();
        Check(clock.Elapsed.TotalSeconds>limits.SearchEffort,"delayed solver exceeds the legacy second value");
        Check(fast.Quotas.Count==3&&slow.Quotas.Count==3&&fast.Quotas.SequenceEqual(slow.Quotas),"all inventories receive the same fixed work despite delay and input order");
        Check(Signature(fastRun)==Signature(slowRun)&&!slowRun.BudgetExpired,"delays cannot change allocations, shortlist, ranks or stop the search");
        var low=new Dictionary<int,int>{{5000,1}};var tierSolver=new Solver();
        var tier=new Top50Search(new Candidates(stocks[0],low,stocks[1]),tierSolver).Run(walls,new[]{500,2500,4500,5000},limits,PlanningPreset.FastestConstruction,20,CancellationToken.None);
        Check(tierSolver.Quotas.Count==1&&tier.Proposals.Single().PanelCount==1&&tier.SkippedForPanelQuantity==2,"higher-quantity tiers cannot enter the offer or consume allocation work after a passing lower tier");
        bool patternStopped=false;try{new FewestComponentPatterns().Enumerate(19204608,Precision.CatalogMm,Limits(effort:1e-9),CancellationToken.None);}catch(TimeoutException){patternStopped=true;}
        Check(patternStopped,"pattern enumeration respects a repeatable node limit");
        Fails(()=>Limits(effort:double.NaN),"NaN effort rejected");Fails(()=>Limits(effort:double.PositiveInfinity),"infinite effort rejected");
        bool cancelled=false;try{new Top50Search(new Candidates(low),new Solver()).Run(walls,new[]{5000},limits,PlanningPreset.BalancedOptimization,4,new CancellationToken(true));}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled,"user cancellation remains available independently of work limits");
        var four=Enumerable.Range(1,4).Select(i=>Wall("wall-"+i,5000000)).ToArray();
        var band=new Top50Search().Run(four,new[]{5000},Limits(effort:3),PlanningPreset.BalancedOptimization,4,CancellationToken.None,percentage:45,bandPoints:10,allowExpansion:true);
        Check(band.Proposals.Count>0&&band.Proposals.All(p=>p.PanelCount==2),"an eligible percentage band takes precedence over a lower-count out-of-band offer");
    }
    private static void Projects(string output)
    {
        var vm=new Top50ViewModel();for(int i=0;i<4;i++)vm.AddManual(5000,"Controlled repeatability fixture");
        vm.Settings.CatalogMm=new[]{5000};vm.Settings.LongestCount=2;vm.Settings.SearchEffort=2;
        vm.OptimizeAsync().GetAwaiter().GetResult();Check(vm.Selected!=null&&vm.Selected.PanelCount==2,"fresh native search satisfies simultaneous longest walls with two panels");
        var path=Path.Combine(output,"current.top50");vm.Save(path);var document=NativeProjectStore.Load(path);
        Check(document.Version==3&&document.SearchPolicyVersion==2&&document.SearchEffort==2,"V3 project persists repeatable effort and automatic-count search policy");
        var reopened=new Top50ViewModel();reopened.Open(path,Path.Combine(output,"cache"));
        Check(!reopened.Dirty&&Top50Order.Stock(reopened.Selected.Stock)==Top50Order.Stock(vm.Selected.Stock)&&reopened.Selected.PanelCount==2,"current project reopens without changing the chosen inventory or dirty state");
        var before=Signature(new SearchRun{Proposals=vm.Proposals});reopened.OptimizeAsync().GetAwaiter().GetResult();
        Check(before==Signature(new SearchRun{Proposals=reopened.Proposals}),"reopened project repeats the full proposal and allocation");
        var rows=vm.Input.Walls.ToArray();
        var high=new Proposal("old-high",new Dictionary<int,int>{{5000,4}},new[]{new CastingGroup(rows.Select(w=>new WallArrangement(w.Id,new PanelPattern(new[]{5000},new long[0]))))});
        var low=new Proposal("old-low",new Dictionary<int,int>{{5000,2}},new[]{new CastingGroup(rows.Take(2).Select(w=>new WallArrangement(w.Id,new PanelPattern(new[]{5000},new long[0])))),new CastingGroup(rows.Skip(2).Select(w=>new WallArrangement(w.Id,new PanelPattern(new[]{5000},new long[0]))))});
        document.Version=1;document.SearchPolicyVersion=0;document.Proposals=new[]{NativeProjectStore.ToDto(high),NativeProjectStore.ToDto(low)};document.SelectedId=high.Id;
        var legacy=Path.Combine(output,"legacy.top50");NativeProjectStore.Save(document,legacy);var bytes=File.ReadAllBytes(legacy);
        reopened.Open(legacy,Path.Combine(output,"cache"));
        Check(reopened.Proposals.Count==1&&reopened.Selected.Id==low.Id&&reopened.Dirty,"legacy higher-count selection is replaced by the validated lower-count shortlist");
        Check(bytes.SequenceEqual(File.ReadAllBytes(legacy)),"opening a legacy project leaves its original file intact");
        Check(reopened.Status.Contains("current repeatable search"),"legacy allocations are distinguished from a fresh repeatable solve");
        var migrated=Path.Combine(output,"migrated.top50");reopened.Save(migrated);
        Check(NativeProjectStore.Load(migrated).Version==3,"saving migration writes V3 so old readers reject automatic-count projects");
        var invalid=new Proposal("too-many-castings",low.Stock.ToDictionary(p=>p.Key,p=>p.Value),low.Groups);
        Check(Top50Validation.Validate(vm.Input.Walls,invalid,new SearchLimits(1,350,1,2,2,50)).Contains("Casting group limit exceeded."),"a lower panel count cannot bypass the casting-group limit");
    }
    private static void AutomaticPanelCounts(string output)
    {
        var patterns=new FewestComponentPatterns();var limits=Limits(effort:3);
        // Independent exhaustive oracle: enumerate all component counts that can
        // physically fit, without the production minimum-count pruning.
        foreach(var catalog in new[]{new[]{500},new[]{500,750},new[]{500,750,1250},new[]{750,1000}})
        for(long length=100000;length<=8500000;length+=100000)
        {
            var sizes=catalog.OrderByDescending(n=>n).ToArray();var expected=new List<string>();var parts=new List<int>();
            void Visit(int left,int start,long sum,int count)
            {
                if(expected.Count==4)return;
                if(left==0){long remainder=length-sum;if(remainder>=0&&remainder<=Math.Min(1,count-1)*350000L)expected.Add(string.Join(",",parts)+"|"+remainder);return;}
                for(int i=start;i<sizes.Length;i++){if(sum+sizes[i]*1000L>length)continue;parts.Add(sizes[i]);Visit(left-1,i,sum+sizes[i]*1000L,count);parts.RemoveAt(parts.Count-1);}
            }
            for(int count=1;count<=length/(sizes.Last()*1000L)&&expected.Count<4;count++)Visit(count,0,0,count);
            var actual=patterns.Enumerate(length,catalog,limits,CancellationToken.None).Select(pattern=>string.Join(",",pattern.PanelsMm)+"|"+pattern.InternalGapsMicrometres.Sum());
            Check(expected.SequenceEqual(actual),"automatic bounds match exhaustive patterns: "+length+" / "+string.Join(",",catalog));
        }
        var thirteen=patterns.Enumerate(6500000,new[]{500},limits,CancellationToken.None);
        Check(thirteen.Count==1&&thirteen[0].PanelsMm.Count==13,"6500 mm wall may use thirteen 500 mm panels without a user ceiling");
        var sixtyOne=patterns.Enumerate(30500000,new[]{500},limits,CancellationToken.None);
        Check(sixtyOne.Count==1&&sixtyOne[0].PanelsMm.Count==61,"component count follows a longer wall rather than a replacement fixed ceiling");
        var shorter=patterns.Enumerate(6500000,new[]{500,1250,3000},limits,CancellationToken.None);
        Check(shorter[0].PanelsMm.Count==3&&shorter.All(pattern=>pattern.LengthMicrometres==6500000),"optimizer still starts with fewer panels and closes the wall exactly");
        Check(patterns.Enumerate(6800000,new[]{500},limits,CancellationToken.None).Single().InternalGapsMicrometres.Sum()==300000,"geometry-derived counts preserve a permitted internal filler");
        Check(patterns.Enumerate(6900000,new[]{500},limits,CancellationToken.None).Count==0,"automatic counts cannot bypass maximum filler width or overrun the wall");
        var rows=new[]{Wall("thirteen",6500000)};
        var offer=new Proposal("thirteen",new Dictionary<int,int>{{500,13}},new[]{new CastingGroup(new[]{new WallArrangement(rows[0].Id,thirteen[0])})});
        Check(Top50Validation.Validate(rows,offer,limits).Count==0,"independent validation accepts a complete thirteen-panel arrangement");
        var vm=new Top50ViewModel();vm.AddManual(6500,"Automatic component regression");vm.Settings.CatalogMm=new[]{500};vm.Settings.LongestCount=1;vm.Settings.SearchEffort=3;vm.OptimizeAsync().GetAwaiter().GetResult();
        Check(vm.Selected.PanelCount==13&&vm.Selected.Groups.Single().Walls.Single().Pattern.PanelsMm.Count==13,"shipping search and native allocation find the formerly blocked thirteen-panel wall");
        string path=Path.Combine(output,"automatic-count.top50");vm.Save(path);
        string json=File.ReadAllText(path);Check(!json.Contains("MaxComponents")&&NativeProjectStore.Load(path).Version==3,"new projects omit the removed component limit and identify V3 semantics");
        var legacy=NativeProjectStore.Load(path);legacy.Version=2;string legacyPath=Path.Combine(output,"legacy-component-limit.top50");NativeProjectStore.Save(legacy,legacyPath);
        json=File.ReadAllText(legacyPath);File.WriteAllText(legacyPath,"{\"MaxComponents\":1,"+json.Substring(1));var original=File.ReadAllBytes(legacyPath);
        var restored=new Top50ViewModel();restored.Open(legacyPath,Path.Combine(output,"automatic-cache"));restored.OptimizeAsync().GetAwaiter().GetResult();
        Check(restored.Selected.PanelCount==13&&original.SequenceEqual(File.ReadAllBytes(legacyPath)),"old project component limits are ignored without overwriting the original file");
    }
    private static void Distribution(string output,bool reverse)
    {
        // This distribution is already recorded in Top50.CoreTests. Neutral IDs
        // exercise discovery; no reference inventory or allocation is injected.
        var sizes=new[]{1600000L,2810000,7200000,9200000,19200000,19204608};var counts=new[]{9,2,10,10,21,11};var rows=new List<WallRow>();
        for(int i=0;i<sizes.Length;i++)for(int n=0;n<counts[i];n++)rows.Add(Wall("wall-"+(rows.Count+1).ToString("D3",CultureInfo.InvariantCulture),sizes[i]));
        var catalog=Precision.CatalogMm.ToArray();if(reverse){rows.Reverse();Array.Reverse(catalog);CultureInfo.CurrentCulture=new CultureInfo("de-DE");}
        var limits=Limits(5,20);var clock=Stopwatch.StartNew();
        var run=new Top50Search().Run(rows,catalog,limits,PlanningPreset.BalancedOptimization,4,CancellationToken.None);clock.Stop();
        Check(run.Proposals.Count>0,"fresh native search finds a complete offer for the recorded distribution");
        Check(run.Proposals.All(p=>p.PanelCount==run.Proposals[0].PanelCount&&p.PanelCount<=27),"recorded distribution excludes the 60-panel offer and shows only the passing minimum-count tier");
        foreach(var p in run.Proposals)Check(Top50Validation.Validate(rows,p,limits).Count==0,"allocation independently validates for "+p.Id);
        string signature=Signature(run);File.WriteAllText(Path.Combine(output,"signature.txt"),signature);File.WriteAllText(Path.Combine(output,"signature.sha256"),Hash(signature));
        File.WriteAllLines(Path.Combine(output,"search-log.txt"),run.Log);
        File.WriteAllText(Path.Combine(output,"summary.txt"),"Elapsed seconds: "+clock.Elapsed.TotalSeconds.ToString("F3",CultureInfo.InvariantCulture)+"\nPanels: "+run.Proposals[0].PanelCount+"\nStock metres: "+(run.Proposals[0].StockMicrometres/1000000m).ToString(CultureInfo.InvariantCulture)+"\nGroups: "+run.Proposals[0].Groups.Count+"\nAttempted inventories: "+run.Attempted+"\nSkipped higher counts: "+run.SkippedForPanelQuantity+"\n");
        Console.WriteLine(File.ReadAllText(Path.Combine(output,"summary.txt")));Console.WriteLine("SIGNATURE "+Hash(signature));
    }
}
