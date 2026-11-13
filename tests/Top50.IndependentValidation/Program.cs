using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static bool quiet;
    private static bool baseline;
    private static readonly CancellationToken Token=CancellationToken.None;
    private static void Check(bool value,string message)
    { if(!value)throw new Exception("FAIL: "+message);checks++;if(!quiet)Console.WriteLine("PASS: "+message); }
    private static WallRow[] Walls(params decimal[] lengths)=>lengths.Select((mm,i)=>new WallRow{Id="W"+i,LengthMicrometres=Precision.ToMicrometres(mm),Included=true,Status=WallReviewStatus.Valid}).ToArray();
    private static SearchLimits Limits(int fillers=1,int groups=8,int longest=1)=>new SearchLimits(fillers,350,groups,longest,3,50);
    private static string Key(Proposal p)=>Top50Order.Stock(p.Stock)+"|"+string.Join("|",p.Groups.Select(Top50Order.Group));

    // Complete independent oracle: enumerate all stock-bounded integer count vectors,
    // then all labelled wall-to-bin assignments. No production pattern, validator,
    // candidate generator, objective, or solver is used for these expected values.
    private static List<int[]> Patterns(long wall,int[] sizes,int[] counts,int fillers,int maxFiller)
    {
        var result=new List<int[]>();var used=new int[sizes.Length];
        void Visit(int index,long panelSum,int panelCount)
        {
            if(index==sizes.Length)
            {
                long gap=wall-panelSum;
                if(panelCount>0&&gap>=0&&gap<=(long)Math.Min(fillers,panelCount-1)*maxFiller*1000)result.Add((int[])used.Clone());
                return;
            }
            for(int n=0;n<=counts[index]&&panelSum+(long)n*sizes[index]*1000<=wall;n++)
            {used[index]=n;Visit(index+1,panelSum+(long)n*sizes[index]*1000,panelCount+n);}
        }
        Visit(0,0,0);return result;
    }
    private static int? MinimumGroups(WallRow[] walls,IDictionary<int,int> stock,SearchLimits limits,int kits=0)
    {
        var sizes=stock.Keys.OrderBy(s=>s).ToArray();var counts=sizes.Select(s=>stock[s]).ToArray();
        var sorted=walls.Where(w=>w.Included).OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).ToArray();
        var options=sorted.Select((w,index)=>Patterns(w.LengthMicrometres,sizes,counts,limits.MaxInternalFillers,limits.MaxFillerMm).Where(p=>kits==0||index>=limits.LongestSimultaneous||Enumerable.Range(0,sizes.Length).All(i=>p[i]<=counts[i]/kits)).ToList()).ToArray();
        if(options.Any(p=>p.Count==0))return null;
        for(int groupCount=1;groupCount<=Math.Min(limits.MaximumGroups,sorted.Length);groupCount++)
        {
            var loads=Enumerable.Range(0,groupCount).Select(_=>new int[sizes.Length]).ToArray();
            var members=new int[groupCount];
            bool Place(int wall)
            {
                if(wall==sorted.Length)return true;
                int last=wall<limits.LongestSimultaneous?0:groupCount-1;
                for(int g=0;g<=last;g++)
                {
                    foreach(var pattern in options[wall])
                    {
                        if(Enumerable.Range(0,sizes.Length).Any(i=>loads[g][i]+pattern[i]>counts[i]))continue;
                        for(int i=0;i<sizes.Length;i++)loads[g][i]+=pattern[i];members[g]++;
                        if(Place(wall+1))return true;
                        members[g]--;for(int i=0;i<sizes.Length;i++)loads[g][i]-=pattern[i];
                    }
                    if(members[g]==0)break;
                }
                return false;
            }
            if(Place(0))return groupCount;
        }
        return null;
    }
    private static void IndependentValidate(WallRow[] walls,Proposal proposal,SearchLimits limits)
    {
        var remaining=walls.Where(w=>w.Included).ToDictionary(w=>w.Id,w=>w.LengthMicrometres);
        Check(proposal.Groups.Count<=limits.MaximumGroups,"independent group limit "+proposal.Id);
        foreach(var group in proposal.Groups)
        {
            Check(group.Walls.Count>0,"independent nonempty group "+proposal.Id);
            var use=new Dictionary<int,int>();
            foreach(var arrangement in group.Walls)
            {
                Check(remaining.TryGetValue(arrangement.WallId,out long wall),"independent unique included wall "+arrangement.WallId);remaining.Remove(arrangement.WallId);
                var p=arrangement.Pattern;
                Check(p.PanelsMm.Count>0&&p.PanelsMm.All(s=>s>0)&&p.InternalGapsMicrometres.Count==p.PanelsMm.Count-1,"independent positive components and internal joints "+arrangement.WallId);
                Check(p.PanelsMm.Sum(s=>(long)s*1000)+p.InternalGapsMicrometres.Sum()==wall,"independent exact micrometre closure "+arrangement.WallId);
                Check(p.InternalGapsMicrometres.All(g=>g>=0&&g<=(long)limits.MaxFillerMm*1000)&&p.InternalGapsMicrometres.Count(g=>g>0)<=limits.MaxInternalFillers,"independent filler limits "+arrangement.WallId);
                foreach(int size in p.PanelsMm){use.TryGetValue(size,out int count);use[size]=count+1;}
            }
            Check(use.All(s=>proposal.Stock.TryGetValue(s.Key,out int have)&&have>=s.Value),"independent simultaneous stock capacity "+proposal.Id);
        }
        Check(remaining.Count==0,"independent full wall coverage "+proposal.Id);
        var longest=new HashSet<string>(walls.Where(w=>w.Included).OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(limits.LongestSimultaneous).Select(w=>w.Id));
        Check(proposal.Groups.Any(g=>longest.IsSubsetOf(g.Walls.Select(a=>a.WallId))),"independent longest simultaneous "+proposal.Id);
        decimal stockLm=proposal.Stock.Sum(s=>(decimal)s.Key*s.Value)/1000;
        var installed=proposal.Groups.Select(g=>g.Walls.Sum(w=>w.Pattern.PanelsMm.Sum(s=>(decimal)s))/1000).ToArray();
        Check(proposal.MeanUtilization==installed.Sum()/installed.Length/stockLm&&proposal.MinimumUtilization==installed.Min()/stockLm,"independent panel-only utilization "+proposal.Id);
    }
    private static void ExactCase(string name,decimal[] lengths,Dictionary<int,int> stock,SearchLimits limits,int? expected)
    {
        var walls=Walls(lengths);var oracle=MinimumGroups(walls,stock,limits);
        Check(oracle==expected,"hand/exhaustive expected groups "+name+" = "+(expected?.ToString()??"infeasible"));
        var result=new RetainedInventorySolver().Solve(name,walls,stock,limits,Token);
        Check(result?.Groups.Count==oracle,"solver agrees with complete oracle "+name);
        if(result!=null){IndependentValidate(walls,result,limits);Check(result.RetainedModelStatus=="Optimal"&&result.RetainedObjective==expected&&result.RetainedBound==expected,"retained group optimum/bound "+name);}
    }
    private static void ConnectorOracle()
    {
        // Brute force both equipment stock variables. Adjustable connectors may fill
        // every direct demand; only adjustable may fill filler demand. Lexicographic
        // expected stock minimizes adjustable first, then standard, as product does.
        for(int d0=0;d0<=3;d0++)for(int f0=0;f0<=3;f0++)for(int d1=0;d1<=3;d1++)for(int f1=0;f1<=3;f1++)
        {
            CastingGroup Group(int d,int f)=>new CastingGroup(new[]{new WallArrangement("d",new PanelPattern(Enumerable.Repeat(500,d+f+1),Enumerable.Repeat(0L,d).Concat(Enumerable.Repeat(1000L,f))))});
            var proposal=new Proposal("connectors",new Dictionary<int,int>{{500,16}},new[]{Group(d0,f0),Group(d1,f1)});
            var actual=ConnectorAllocation.Calculate(proposal,new NativeTop50Project{ConnectionLevels=9,ConnectionFaces=1,AdjustableOnDirect=false});
            var expected=(from adjustable in Enumerable.Range(0,15) from standard in Enumerable.Range(0,15) where adjustable>=2*f0&&adjustable>=2*f1&&adjustable+standard>=2*(d0+f0)&&adjustable+standard>=2*(d1+f1) orderby adjustable,standard select (adjustable,standard)).First();
            if(actual.AdjustableStock!=expected.adjustable||actual.StandardStock!=expected.standard)throw new Exception("Connector procurement oracle disagreement");
            if(actual.Levels!=1||actual.Faces!=2)throw new Exception("Connector basis changed");
        }
        Check(true,"256 connector procurement vectors: correct one-level both-face maxima with adjustable reuse");
    }
    private static void RankingOracle()
    {
        var scales=new RankingScales(20,10,10,10);
        RankingMetrics[] Offers()=>new[]{
            new RankingMetrics("many",6,6,1,1,1,0,1),
            new RankingMetrics("economy",5,10,2,5,.8m,0,.6m),
            new RankingMetrics("fast",5,12,2,2,.8m,2,.6m),
            new RankingMetrics("balanced",5,11,1,3,.9m,0,.8m)};
        var economy=ProposalRanking.Rank(Offers(),PlanningPreset.EconomicalOffer,10,scales);
        var fastest=ProposalRanking.Rank(Offers(),PlanningPreset.FastestConstruction,10,scales);
        var balanced=ProposalRanking.Rank(Offers(),PlanningPreset.BalancedOptimization,10,scales);
        Check(economy.Count==3&&fastest.Count==3&&balanced.Count==3&&economy.Concat(fastest).Concat(balanced).All(p=>p.PanelCount==5),"all presets reject higher panel count despite better other metrics");
        Check(economy[0].Id=="economy"&&fastest[0].Id=="fast"&&balanced[0].Id=="balanced","three presets select hand-computed distinct equal-count tradeoffs");
        Check(balanced[0].BalancedScore==(.55m+.1m+.3m+.1m+0m+.2m+0m)/7,"balanced equal-scale arithmetic independently calculated");
        var ties=new[]{new RankingMetrics("Z",2,1,1,1,1,0,1,stableKey:"b"),new RankingMetrics("A",2,1,1,1,1,0,1,stableKey:"a")};
        Check(ProposalRanking.Rank(ties,PlanningPreset.BalancedOptimization,10,scales).Select(x=>x.Id).SequenceEqual(new[]{"A","Z"}),"metric ties use stable ordinal inventory key");
        Check(ProposalRanking.Rank(Enumerable.Reverse(ties),PlanningPreset.BalancedOptimization,10,scales).Select(x=>x.Id).SequenceEqual(new[]{"A","Z"}),"metric ties independent of input ordering");
    }
    private static async Task UiState()
    {
        var vm=new Top50ViewModel();vm.AddManual(1000,"QA exact wall");vm.EditSettings(s=>{s.CatalogMm=new[]{1000};s.LongestCount=1;});await vm.OptimizeAsync();
        Check(vm.Proposals.Count==1&&!vm.IsStale&&vm.Selected!=null,"view model accepts valid single wall");
        Check(Top50PlannerPolicy.TotalPanels(vm.Selected)==2&&Top50PlannerPolicy.OfferedLmPerFace(vm.Selected)==1&&Top50PlannerPolicy.Inventory(vm.Selected).Single().Quantity==2,"native counts both faces and LM one face");
        vm.ReviewWall(vm.Input.Walls[0].Id,1100,true,"QA changed length");
        Check(vm.IsStale&&new Top50OfferPresentation(vm.Input,vm.Settings,vm.Proposals,vm.IsStale).Reason(vm.Selected).Contains("inputs have changed"),"editing wall stales previous ranking and reason");
        await vm.OptimizeAsync();
        Check(vm.IsStale&&vm.Status.Contains("not a proof of infeasibility"),"failed restrictive rerun retains stale result and truthful status");
        vm.Undo();Check(!vm.IsStale&&vm.Input.Walls.Single().LengthMm==1000,"undo restores corresponding valid result state");
        vm.Redo();Check(vm.IsStale&&vm.Input.Walls.Single().LengthMm==1100,"redo restores stale state");
        string file=Path.Combine(AppContext.BaseDirectory,"stale.top50");vm.Save(file);
        Check(NativeProjectStore.Load(file).Proposals.Length==0,"stale proposal is excluded from native persistence");
        try{vm.Export(Path.Combine(AppContext.BaseDirectory,"forbidden.xlsx"));throw new Exception("Excel unexpectedly exported");}catch(NotSupportedException){Check(true,"Excel remains disabled");}
    }
    private static async Task UiMethodSwitchProbe()
    {
        var vm=new Top50ViewModel();for(int i=0;i<4;i++)vm.AddManual(1000,"QA identical wall");
        vm.EditSettings(s=>{s.CatalogMm=new[]{1000};s.LongestCount=2;s.RequireIdenticalKits=true;});await vm.OptimizeAsync();
        vm.EditSettings(s=>s.OfferingMode=1);
        try
        {
            await vm.OptimizeAsync();
            Check(vm.Settings.RequireIdenticalKits&&vm.Settings.OfferingMode==1&&vm.Proposals.Count>0&&!vm.IsStale,"QA-001 percentage mode ignores hidden fixed preference and preserves it");
            Check(vm.Proposals.All(p=>p.IdenticalKitCount==0),"QA-001 percentage offers do not claim fixed kits");
            vm.EditSettings(s=>s.OfferingMode=0);await vm.OptimizeAsync();
            Check(vm.Proposals.Count>0&&vm.Proposals.All(p=>p.IdenticalKitCount==2),"QA-001 switching back restores two identical fixed kits");
        }
        catch(ArgumentException e)when(baseline){Console.WriteLine("CONFIRMED DEFECT QA-001: selecting Percentage reference after identical fixed kits throws: "+e.Message);}
    }
    private static void IdenticalKitAllocationProbe()
    {
        var walls=Walls(500,500,1500);var stock=new Dictionary<int,int>{{750,2},{500,2}};var limits=Limits(longest:2);
        Check(MinimumGroups(walls,stock,limits)==1&&MinimumGroups(walls,stock,limits,kits:2)==2,"independent identical-kit oracle: pooled optimum1 violates kits; kit-constrained optimum2");
        try
        {
            var run=new Top50Search().Run(walls,new[]{500,750,1000},limits,PlanningPreset.BalancedOptimization,3,Token,identicalKits:true);
            Check(run.Proposals.Count>0&&run.Proposals.All(p=>p.PanelCount==4&&p.Groups.Count==2&&p.IdenticalKitCount==2),"QA-003 actual kit-constrained search finds four-panel two-group offers");
            foreach(var proposal in run.Proposals)
            {
                IndependentValidate(walls,proposal,limits);
                var longest=new HashSet<string>(walls.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(2).Select(w=>w.Id));
                Check(proposal.Groups.SelectMany(g=>g.Walls).Where(w=>longest.Contains(w.WallId)).All(w=>w.Pattern.PanelsMm.GroupBy(s=>s).All(g=>g.Count()<=proposal.Stock[g.Key]/2)),"QA-003 actual selected required-wall patterns each fit one kit");
            }
        }
        catch(InvalidOperationException e)when(baseline){Console.WriteLine("CONFIRMED DEFECT QA-003: ordinary identical-kit search fails after choosing pooled allocation: "+e.Message);}
    }
    private static async Task CachedProposalProbes()
    {
        var vm=new Top50ViewModel();for(int i=0;i<4;i++)vm.AddManual(1000,"QA saved wall");
        vm.EditSettings(s=>{s.CatalogMm=new[]{1000};s.LongestCount=2;});await vm.OptimizeAsync();
        string original=Path.Combine(AppContext.BaseDirectory,"cached-original.top50");vm.Save(original);
        async Task Probe(string label,Action<NativeTop50Project> change)
        {
            var document=NativeProjectStore.Load(original);change(document);string file=Path.Combine(AppContext.BaseDirectory,"cached-"+label+".top50");NativeProjectStore.Save(document,file);
            var reopened=new Top50ViewModel();reopened.AddManual(750,"Prior in-memory project must survive rejection");var prior=reopened.Input;
            try
            {
                reopened.Open(file,Path.Combine(AppContext.BaseDirectory,"cache"));
                if(baseline&&reopened.Proposals.Count>0&&!reopened.IsStale)Console.WriteLine("CONFIRMED DEFECT QA-002-"+label+": inconsistent cached offer accepted: count="+reopened.Proposals.Count+"; stale="+reopened.IsStale+"; status="+reopened.Status);
                else
                {
                    Check(reopened.Proposals.Count==0&&reopened.Input.Walls.Count==4,"QA-002 incompatible cached "+label+" offer discarded while valid input opens");
                    Check(reopened.Status.IndexOf("optimize",StringComparison.OrdinalIgnoreCase)>=0,"QA-002 discarded "+label+" cache explains reoptimization");
                    Check(label!="catalog"||reopened.Settings.CatalogMm.SequenceEqual(new[]{500}),"QA-002 changed permitted catalog preserved after cache discard");
                    Check(label!="band"||reopened.Settings.OfferingMode==1&&reopened.Settings.Percentage==45&&reopened.Settings.BandPoints==1,"QA-002 active percentage requirements preserved after cache discard");
                    Check(label!="kits"||reopened.Settings.RequireIdenticalKits&&reopened.Settings.LongestCount==2,"QA-002 fixed kit preference preserved after cache discard");
                }
            }
            catch(Exception e)when(e is InvalidDataException||e is InvalidOperationException)
            {
                Check(ReferenceEquals(prior,reopened.Input)&&reopened.Input.Walls.Single().LengthMm==750,"QA-002 rejected "+label+" Open preserves original in-memory project");
                if(!baseline)throw new Exception("Policy-incompatible cached "+label+" offer should be discarded while valid input/settings open.",e);
            }
            await Task.CompletedTask;
        }
        await Probe("catalog",s=>s.CatalogMm=new[]{500});
        await Probe("band",s=>{s.OfferingMode=1;s.Percentage=45;s.BandPoints=1;});
        await Probe("kits",s=>{s.RequireIdenticalKits=true;foreach(var p in s.Proposals){p.Stock[0].Quantity=3;p.KitCount=0;}});
    }
    private static void ImportOracle()
    {
        string Fixture(int units)=>string.Join("\n",new[]{"0","SECTION","2","HEADER","9","$ACADVER","1","AC1015","9","$INSUNITS","70",units.ToString(),"0","ENDSEC","0","SECTION","2","ENTITIES","0","DIMENSION","5","AA1","8","Wall_Length","2","*D1","10","0","20","0","30","0","70","0","13","0","23","0","33","0","14","1.2345","24","0","34","0","42","1.2345","50","0","0","DIMENSION","5","AA2","8","Wall_Length","2","*D2","10","0","20","0","30","0","70","1","13","0","23","10","33","0","14","0","24","2010","34","0","42","2000","50","0","0","ENDSEC","0","EOF"})+"\n";
        string file=Path.Combine(AppContext.BaseDirectory,"oracle.dxf");File.WriteAllText(file,Fixture(4));
        var imported=new Top50DxfImporter().Import(file,new DxfImportOptions());
        Check(imported.Walls.Single(w=>w.Handle=="AA1").LengthMicrometres==1235,"synthetic DXF exact decimal half-up reference");
        var conflicted=imported.Walls.Single(w=>w.Handle=="AA2");
        Check(!conflicted.Included&&conflicted.Status==WallReviewStatus.NeedsReview&&conflicted.ProjectedRawMm==0&&conflicted.EndpointDistanceMm==2000&&conflicted.StoredMeasurementMm==2000,"aligned DXF conflicting projection retained and excluded pending review");
        File.WriteAllText(file,Fixture(6));var metres=new Top50DxfImporter().Import(file,new DxfImportOptions());
        Check(metres.Walls.Single(w=>w.Handle=="AA1").LengthMicrometres==1234500,"metre DXF converts physical units before one rounding step");
        File.WriteAllText(file,Fixture(0));var unknown=new Top50DxfImporter().Import(file,new DxfImportOptions());
        Check(unknown.Walls.All(w=>!w.Included),"unitless DXF requires review");
        var overridden=new Top50DxfImporter().Import(file,new DxfImportOptions{MmPerUnitOverride=1000,OverrideReason="Reviewed metre drawing"});
        Check(overridden.Walls.Single(w=>w.Handle=="AA1").LengthMicrometres==1234500&&overridden.Walls.Single(w=>w.Handle=="AA1").Included,"reviewed units override explicitly enables correct rounded physical length");
    }
    private static void ExhaustiveSweep()
    {
        int cases=0,complete=0,boundedOmissions=0;
        decimal[] lengths={500,750,1000,1250,1350,1500};quiet=true;
        for(int a=1;a<=3;a++)for(int b=1;b<=3;b++)for(int i=0;i<lengths.Length;i++)for(int j=i;j<lengths.Length;j++)for(int fillers=0;fillers<=1;fillers++)for(int simultaneous=1;simultaneous<=2;simultaneous++)
        {
            var walls=Walls(lengths[i],lengths[j]);var stock=new Dictionary<int,int>{{500,a},{750,b}};var limits=Limits(fillers,longest:simultaneous);var expected=MinimumGroups(walls,stock,limits);cases++;
            var actual=new RetainedInventorySolver().Solve("sweep",walls,stock,limits,Token);
            bool completePatterns=walls.All(w=>Patterns(w.LengthMicrometres,new[]{500,750},new[]{(int)(w.LengthMm/500),(int)(w.LengthMm/750)},fillers,350).Count<=4);
            if(completePatterns){complete++;Check(actual?.Groups.Count==expected,"exhaustive sweep complete-model exact agreement");}
            else if(actual?.Groups.Count!=expected)boundedOmissions++;
            Check(actual==null||expected.HasValue&&actual.Groups.Count>=expected.Value,"exhaustive oracle lower bound");
            if(actual!=null)IndependentValidate(walls,actual,limits);
        }
        quiet=false;Check(true,"exhaustive sweep "+cases+" tiny two-wall cases; "+complete+" complete-pattern exact optimum agreements; "+boundedOmissions+" bounded omissions");
    }
    private static void IdenticalKitExhaustiveSweep()
    {
        int cases=0;decimal[] lengths={500,750,1000,1250,1500};quiet=true;
        for(int a=2;a<=4;a+=2)for(int b=2;b<=4;b+=2)for(int i=0;i<lengths.Length;i++)for(int j=i;j<lengths.Length;j++)for(int fillers=0;fillers<=1;fillers++)
        {
            var walls=Walls(500,lengths[i],lengths[j]);var stock=new Dictionary<int,int>{{500,a},{750,b}};
            var limits=new SearchLimits(fillers,350,3,2,3,50,requireIdenticalKits:true);
            var expected=MinimumGroups(walls,stock,limits,kits:2);var actual=new RetainedInventorySolver().Solve("kit-sweep",walls,stock,limits,Token);cases++;
            Check(actual?.Groups.Count==expected,"independent complete identical-kit optimum/infeasibility");
            if(actual==null)continue;IndependentValidate(walls,actual,limits);
            Check(actual.IdenticalKitCount==2,"direct kit allocation declares correct kit count");
            var required=new HashSet<string>(walls.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(2).Select(w=>w.Id));
            Check(actual.Groups.SelectMany(g=>g.Walls).Where(w=>required.Contains(w.WallId)).All(w=>w.Pattern.PanelsMm.GroupBy(s=>s).All(g=>g.Count()<=stock[g.Key]/2)),"independent actual required kit counts");
            Check(actual.RetainedModelStatus=="Optimal"&&actual.RetainedObjective==expected&&actual.RetainedBound==expected,"exact independent kit objective/bound");
        }
        quiet=false;Check(true,"exhaustive identical-kit sweep "+cases+" mixed-length three-wall cases with exact expected groups and actual per-wall kit checks");
        var capped=new SearchLimits(1,350,1,2,3,50,requireIdenticalKits:true);var witness=Walls(500,500,1500);var witnessStock=new Dictionary<int,int>{{750,2},{500,2}};
        Check(MinimumGroups(witness,witnessStock,capped,kits:2)==null&&new RetainedInventorySolver().Solve("kit-cap",witness,witnessStock,capped,Token)==null,"kit-constrained witness infeasible under one-group cap despite pooled feasibility");
        Check(new RetainedInventorySolver().Solve("indivisible",Walls(500,500),new Dictionary<int,int>{{500,3}},new SearchLimits(1,350,3,2,3,50,requireIdenticalKits:true),Token)==null,"nondivisible inventory cannot form two identical kits");
        var repeated=Walls(1500,1500,1500);var repeatedStock=new Dictionary<int,int>{{500,4},{750,2}};var repeatedLimits=new SearchLimits(1,350,3,2,3,50,requireIdenticalKits:true);
        Check(MinimumGroups(repeated,repeatedStock,repeatedLimits,kits:2)==2,"independent repeated-length kit witness needs two groups");
        var repeatedActual=new RetainedInventorySolver().Solve("required-and-pooled-same-length",repeated,repeatedStock,repeatedLimits,Token);
        Check(repeatedActual!=null&&repeatedActual.Groups.Count==2,"same-length required walls fit one kit while nonrequired wall can use pooled stock");
        IndependentValidate(repeated,repeatedActual,repeatedLimits);
        Check(repeatedActual.Groups.SelectMany(g=>g.Walls).Where(w=>w.WallId=="W0"||w.WallId=="W1").All(w=>w.Pattern.PanelsMm.GroupBy(s=>s).All(g=>g.Count()<=repeatedStock[g.Key]/2)),"required IDs receive compatible selected patterns in tied-length reconstruction");
    }
    private static void RecoveredIdenticalSeeds()
    {
        var cases=new[]{new[]{500m,500,3000},new[]{500m,1000,3000},new[]{1000m,1000,3000},new[]{1500m,1500,3000}};
        int[] expectedMinimumPanels={8,8,6,8};
        for(int index=0;index<cases.Length;index++)
        {
            var walls=Walls(cases[index]);var limits=new SearchLimits(1,350,8,2,3,50,requireIdenticalKits:true);
            int minimum=int.MaxValue;decimal leastLm=decimal.MaxValue;
            // Every identical stock with <=8 panels has <=4 panels per kit.
            // Enumerate the entire three-size inventory space in that tier.
            for(int n500=0;n500<=4;n500++)for(int n750=0;n750<=4;n750++)for(int n1000=0;n1000<=4;n1000++)
            {
                int perKit=n500+n750+n1000;if(perKit==0||perKit>4)continue;
                var stock=new Dictionary<int,int>();if(n500>0)stock[500]=2*n500;if(n750>0)stock[750]=2*n750;if(n1000>0)stock[1000]=2*n1000;
                if(!MinimumGroups(walls,stock,limits,kits:2).HasValue)continue;
                minimum=Math.Min(minimum,2*perKit);leastLm=Math.Min(leastLm,stock.Sum(s=>s.Key*s.Value)/1000m);
            }
            Check(minimum==expectedMinimumPanels[index],"AP-01b complete <=8-panel inventory oracle minimum for "+string.Join(",",cases[index])+" = "+minimum);
            Check(leastLm==5.5m,"AP-01b independent feasible stock LM reference = 5.5 per face (not a retained-search guarantee)");
            var witnessStock=index==2?new Dictionary<int,int>{{1000,6}}:new Dictionary<int,int>{{1000,6},{500,2}};
            Check(MinimumGroups(walls,witnessStock,limits,kits:2)==1,"AP-01b constructive complete-kit stock packs all three walls in one group");
            var witness=new RetainedInventorySolver().Solve("one-group-completion-witness",walls,witnessStock,limits,Token);
            Check(witness!=null&&witness.Groups.Count==1,"AP-01b actual fixed-stock solver respects one-group witness");IndependentValidate(walls,witness,limits);
            var found=new Top50Search().Run(walls,new[]{500,750,1000},limits,PlanningPreset.BalancedOptimization,3,Token,identicalKits:true);
            Check(found.Proposals.Count>0,"AP-01b previously empty identical-kit input now returns a complete offer");
            foreach(var proposal in found.Proposals)
            {
                IndependentValidate(walls,proposal,limits);
                Check(proposal.PanelCount==minimum,"AP-01b selected tier reaches independently proven tiny panel minimum");
                Check(proposal.IdenticalKitCount==2&&proposal.Stock.All(s=>new[]{500,750,1000}.Contains(s.Key)&&s.Value%2==0),"AP-01b uses only allowed lengths and exactly two repeated kits");
                var required=new HashSet<string>(walls.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(2).Select(w=>w.Id));
                Check(proposal.Groups.SelectMany(g=>g.Walls).Where(w=>required.Contains(w.WallId)).All(w=>w.Pattern.PanelsMm.GroupBy(s=>s).All(g=>g.Count()<=proposal.Stock[g.Key]/2)),"AP-01b actual required-wall patterns each fit one selected kit");
                Console.WriteLine("RECOVERED walls="+string.Join(",",cases[index])+" panels="+proposal.PanelCount+" LM="+proposal.StockMicrometres/1000000m+" groups="+proposal.Groups.Count+" fullStockGroupLowerBound="+MinimumGroups(walls,proposal.Stock.ToDictionary(s=>s.Key,s=>s.Value),limits,kits:2));
            }
        }
    }
    private static void DiscreteSettings()
    {
        int Throws(Action action,string field)
        {
            try{action();throw new Exception("Expected field-specific rejection for "+field);}
            catch(ArgumentException e){Check(e.Message.Contains(field),"field-specific validation for "+field);return 1;}
        }
        const string countField="Longest walls required simultaneously";
        Throws(()=>Top50PlannerPolicy.WholeNumber(1.9m,countField,1),countField);
        Throws(()=>Top50PlannerPolicy.WholeNumber(0,countField,1),countField);
        Throws(()=>Top50PlannerPolicy.WholeNumber((decimal)int.MaxValue+1,countField,1),countField);
        Check(Top50PlannerPolicy.WholeNumber(2,countField,1)==2,"whole valid count preserved");
        Throws(()=>Top50PlannerPolicy.WholeNumber(350.001m,"Maximum filler width",0,350),"Maximum filler width");
        Throws(()=>Top50PlannerPolicy.WholeNumber(351,"Maximum filler width",0,350),"Maximum filler width");
        var vm=new Top50ViewModel();int before=vm.Settings.LongestCount;
        Throws(()=>vm.EditSettings(s=>s.LongestCount=Top50PlannerPolicy.WholeNumber(2.5m,countField,1)),countField);
        Check(vm.Settings.LongestCount==before&&!vm.CanUndo&&!vm.Dirty,"invalid fractional edit preserves valid settings and history");
        Throws(()=>vm.EditSettings(s=>s.MaxFillerMm=351),"Maximum filler width");
        Check(vm.Settings.MaxFillerMm==350&&!vm.CanUndo&&!vm.Dirty,"invalid integer domain edit preserves settings and history");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{CatalogMm=Array.Empty<int>()}),"panel length");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{CatalogMm=new[]{0}}),"panel length");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{OfferingMode=99}),"Fixed longest-wall sets");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{Preset=99}),"planning preset");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{MaxGroups=0}),"allocation groups");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{MaxFillers=-1}),"internal fillers");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{OfferingMode=1,Percentage=0}),"percentage");
        Throws(()=>Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{OfferingMode=1,BandPoints=-1}),"Search range");
        Top50PlannerPolicy.ValidateSettings(new NativeTop50Project{OfferingMode=1,Percentage=45.25m,BandPoints=1.25m,LongestCount=0,RequireIdenticalKits=true});
        Check(true,"fractional percentages valid and hidden fixed preference/count ignored in percentage mode");
    }
    private static async Task CachedCollectionPolicy()
    {
        var vm=new Top50ViewModel();for(int i=0;i<4;i++)vm.AddManual(1000,"QA cached collection");vm.EditSettings(s=>{s.CatalogMm=new[]{1000};s.LongestCount=2;});await vm.OptimizeAsync();
        string original=Path.Combine(AppContext.BaseDirectory,"collection-original.top50");vm.Save(original);
        var outside=new Proposal("outside",new Dictionary<int,int>{{1000,1}},vm.Input.Walls.Select(w=>new CastingGroup(new[]{new WallArrangement(w.Id,new PanelPattern(new[]{1000},Array.Empty<long>()))})));
        NativeTop50Project Document()=>NativeProjectStore.Load(original);
        Top50ViewModel Open(NativeTop50Project document,string name)
        {
            string file=Path.Combine(AppContext.BaseDirectory,name+".top50");NativeProjectStore.Save(document,file);var restored=new Top50ViewModel();restored.Open(file,Path.Combine(AppContext.BaseDirectory,"cache"));return restored;
        }
        var mixed=Document();mixed.OfferingMode=1;mixed.Percentage=45;mixed.BandPoints=10;mixed.AllowExpansion=true;mixed.Proposals=mixed.Proposals.Concat(new[]{NativeProjectStore.ToDto(outside)}).ToArray();mixed.SelectedId="outside";
        var inside=Open(mixed,"mixed-expanded");
        Check(inside.Proposals.Count>0&&inside.Proposals.All(p=>p.StockMicrometres==2000000)&&inside.Selected.Id!="outside","cached in-band collection wins before lower-count expanded offer");
        var onlyOutside=Document();onlyOutside.OfferingMode=1;onlyOutside.Percentage=45;onlyOutside.BandPoints=10;onlyOutside.AllowExpansion=true;onlyOutside.Proposals=new[]{NativeProjectStore.ToDto(outside)};
        var fallback=Open(onlyOutside,"expanded-fallback");
        Check(fallback.Proposals.Count==1&&fallback.Proposals.Single().Id=="outside"&&!fallback.IsStale,"out-of-band-only cache remains eligible when fallback expansion is allowed");
        var hidden=Document();hidden.OfferingMode=1;hidden.RequireIdenticalKits=true;hidden.LongestCount=0;
        Check(Open(hidden,"hidden-fixed-preference").Proposals.Count==1,"percentage cache ignores hidden identical preference and fixed count");
        for(int version=1;version<=5;version++)
        {
            var legacy=Document();legacy.Version=version;legacy.SearchPolicyVersion=0;
            var restored=Open(legacy,"legacy-policy-"+version);
            Check(restored.Proposals.Count==1&&!restored.IsStale,"geometrically and policy-valid V"+version+" cache preserved");
        }
        var broken=Document();broken.Proposals[0].Groups[0][0].GapsMicrometres=new long[]{1};
        string badFile=Path.Combine(AppContext.BaseDirectory,"broken-geometry.top50");NativeProjectStore.Save(broken,badFile);
        var prior=new Top50ViewModel();prior.AddManual(750,"Keep this source on corrupt Open");var priorInput=prior.Input;
        try{prior.Open(badFile,Path.Combine(AppContext.BaseDirectory,"cache"));throw new Exception("Corrupt cached geometry accepted");}
        catch(InvalidOperationException){Check(ReferenceEquals(prior.Input,priorInput)&&prior.Input.Walls.Single().LengthMm==750,"corrupt geometry rejected atomically before cache policy filtering");}
        var fresh=new Top50ViewModel();fresh.AddManual(1000,"Fresh acceptance policy");fresh.EditSettings(s=>{s.CatalogMm=new[]{500};s.LongestCount=1;});
        var forbidden=new Proposal("forbidden-catalog",new Dictionary<int,int>{{1000,1}},new[]{new CastingGroup(new[]{new WallArrangement(fresh.Input.Walls.Single().Id,new PanelPattern(new[]{1000},Array.Empty<long>()))})});
        try{fresh.AcceptValidatedRun(new SearchRun{Proposals=new[]{forbidden}});throw new Exception("Fresh policy-incompatible offer accepted");}
        catch(InvalidOperationException){Check(fresh.Proposals.Count==0,"fresh acceptance enforces active catalog policy");}
    }
    private static async Task CompletionOutcomes()
    {
        var empty=new Top50ViewModel();empty.AddManual(1000,"No-result fixture");empty.EditSettings(s=>{s.CatalogMm=new[]{750};s.LongestCount=1;s.MaxFillers=0;});await empty.OptimizeAsync();
        Check(!empty.IsBusy&&empty.Proposals.Count==0&&empty.Status.Contains("not a proof of infeasibility"),"first no-result outcome explains bounded failure");
        var canceled=new Top50ViewModel();for(int i=0;i<10;i++)canceled.AddManual(19204.608m,"Cancellation fixture");canceled.EditSettings(s=>s.LongestCount=2);
        var task=canceled.OptimizeAsync();canceled.Cancel();await task;
        Check(!canceled.IsBusy&&canceled.Proposals.Count==0&&canceled.Status.IndexOf("cancel",StringComparison.OrdinalIgnoreCase)>=0,"first canceled search has explicit completion status");
    }
    private static void RetentionLimitation()
    {
        var stock=new Dictionary<int,int>{{1000,1},{750,1},{500,2}};var walls=Walls(3000);var limits=Limits();
        Check(MinimumGroups(walls,stock,limits)==1,"complete oracle proves retained omission example feasible: 1000+750+500+500+250 filler");
        var result=new RetainedInventorySolver().Solve("retained-omission",walls,stock,limits,Token);
        Check(result==null,"documented limitation reproduced: four retained multisets omit stock-feasible arrangement");
        Console.WriteLine("OBSERVATION retained omission: complete feasible, retained returns no proposal; no global infeasibility follows.");
    }
    private static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        baseline=args.Contains("--baseline");
        if(args.Contains("--recorded"))
        {
            var lengths=new[]{1600m,2810,7200,9200,19200,19204.608m};var counts=new[]{9,2,10,10,21,11};
            var walls=Walls(lengths.SelectMany((length,index)=>Enumerable.Repeat(length,counts[index])).ToArray());
            var catalog=Precision.CatalogMm.ToArray();
            if(args.Contains("--reverse")){Array.Reverse(walls);Array.Reverse(catalog);CultureInfo.CurrentCulture=new CultureInfo("de-DE");}
            var limits=new SearchLimits(1,350,64,5,100,50);var clock=System.Diagnostics.Stopwatch.StartNew();
            bool recordedIdentical=args.Contains("--identical");
            var recordedRun=new Top50Search().Run(walls,catalog,limits,PlanningPreset.BalancedOptimization,3,Token,identicalKits:recordedIdentical);
            Check(recordedRun.Proposals.Count>0,"recorded63wall distribution returns proposals");
            foreach(var p in recordedRun.Proposals)
            {
                IndependentValidate(walls,p,limits);
                if(recordedIdentical)
                {
                    Check(p.IdenticalKitCount==5&&p.Stock.All(s=>catalog.Contains(s.Key)&&s.Value%5==0),"recorded identical inventory has five repeated permitted kits");
                    var required=new HashSet<string>(walls.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(5).Select(w=>w.Id));
                    Check(p.Groups.SelectMany(g=>g.Walls).Where(w=>required.Contains(w.WallId)).All(w=>w.Pattern.PanelsMm.GroupBy(s=>s).All(g=>g.Count()<=p.Stock[g.Key]/5)),"recorded actual longest-wall patterns each fit one of five kits");
                }
                Console.WriteLine("RECORDED identical="+recordedIdentical+" proposal="+p.Id+" rank="+p.Rank+" panelsPerFace="+p.PanelCount+" panelsBothFaces="+Top50PlannerPolicy.TotalPanels(p)+" stockLMPerFace="+p.StockMicrometres/1000000m+" groups="+p.Groups.Count+" mean="+p.MeanUtilization+" minimum="+p.MinimumUtilization+" retained="+p.RetainedModelStatus);
            }
            string signature=recordedRun.Attempted+"/"+recordedRun.Unsearched+"/"+recordedRun.SkippedForPanelQuantity+"\n"+string.Join("\n",recordedRun.Proposals.Select(p=>p.Id+"/"+p.Rank+"/"+p.Pareto+"/"+p.RetainedModelStatus+"/"+p.RetainedObjective.ToString("R",CultureInfo.InvariantCulture)+"/"+p.RetainedBound.ToString("R",CultureInfo.InvariantCulture)+"/"+Key(p)))+"\n";
            string output=args.FirstOrDefault(a=>a.StartsWith("--output=",StringComparison.Ordinal))?.Substring(9)??AppContext.BaseDirectory;Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"independent-signature.txt"),signature);File.WriteAllLines(Path.Combine(output,"independent-search-log.txt"),recordedRun.Log);
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature))).Replace("-","").ToLowerInvariant();
            Console.WriteLine("RECORDED "+checks+" independent arrangement assertions; elapsedSeconds="+clock.Elapsed.TotalSeconds.ToString("F3",CultureInfo.InvariantCulture));Console.WriteLine("INDEPENDENT SIGNATURE "+hash);return 0;
        }
        if(args.Contains("--kits-sweep"))
        {
            int cases=0,offers=0,empty=0;decimal[] lengths={500,750,1000,1250,1500,1750,2000,2250,2500,2750,3000};quiet=true;
            for(int a=0;a<lengths.Length;a++)for(int b=a;b<lengths.Length;b++)for(int c=b;c<lengths.Length;c++)
            {
                try
                {
                    var walls=Walls(lengths[a],lengths[b],lengths[c]);var limits=Limits(longest:2);
                    var found=new Top50Search().Run(walls,new[]{500,750,1000},limits,PlanningPreset.BalancedOptimization,3,Token,identicalKits:true);cases++;
                    if(found.Proposals.Count==0){empty++;Console.WriteLine("KITS NO OFFER walls="+lengths[a]+","+lengths[b]+","+lengths[c]+"; bounded candidate/model result, not infeasibility proof");}
                    foreach(var proposal in found.Proposals)
                    {
                        offers++;IndependentValidate(walls,proposal,limits);
                        Check(proposal.IdenticalKitCount==2&&proposal.Stock.Values.All(n=>n%2==0),"ordinary kit sweep divisibility/metadata");
                        var required=new HashSet<string>(walls.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(2).Select(w=>w.Id));
                        Check(proposal.Groups.SelectMany(g=>g.Walls).Where(w=>required.Contains(w.WallId)).All(w=>w.Pattern.PanelsMm.GroupBy(s=>s).All(g=>g.Count()<=proposal.Stock[g.Key]/2)),"ordinary kit sweep actual required-wall kit fit");
                    }
                }
                catch(Exception e){Console.WriteLine("KITS FAILURE walls="+lengths[a]+","+lengths[b]+","+lengths[c]+" "+e);return 1;}
            }
            Check(empty==0,"AP-01b all 286 controlled kit inputs produce complete offers");Console.WriteLine("KITS SWEEP PASS cases="+cases+" offers="+offers+" noOffer="+empty+" independentAssertions="+checks);return 0;
        }
        Check(Precision.ToMicrometres(1.2345m)==1235&&Precision.ToMicrometres(1.234499m)==1234&&Precision.ToMicrometres(19204.608294923666m)==19204608,"independent exact decimal rounding half away from zero once");
        ExactCase("single",new[]{1000m},new(){{1000,1}},Limits(),1);
        ExactCase("identical sequential",new[]{1000m,1000m,1000m},new(){{1000,1}},Limits(),3);
        ExactCase("identical simultaneous",new[]{1000m,1000m,1000m},new(){{1000,2}},Limits(longest:2),2);
        ExactCase("varied",new[]{2000m,1500m,1000m},new(){{1000,2},{500,1}},Limits(fillers:0),2);
        ExactCase("very long",new[]{120000m},new(){{6000,20}},Limits(fillers:0),1);
        ExactCase("short infeasible",new[]{400m},new(){{500,1}},Limits(),null);
        ExactCase("filler exact max",new[]{1350m},new(){{500,2}},Limits(),1);
        ExactCase("filler beyond max",new[]{1350.001m},new(){{500,2}},Limits(),null);
        ExactCase("filler disabled",new[]{1350m},new(){{500,2}},Limits(fillers:0),null);
        ExactCase("group cap infeasible",new[]{1000m,1000m},new(){{1000,1}},Limits(groups:1),null);
        ExactCase("simultaneous infeasible",new[]{1000m,1000m},new(){{1000,1}},Limits(longest:2),null);
        ConnectorOracle();RankingOracle();RetentionLimitation();ImportOracle();ExhaustiveSweep();await UiState();await UiMethodSwitchProbe();await CachedProposalProbes();IdenticalKitAllocationProbe();
        if(!baseline){IdenticalKitExhaustiveSweep();RecoveredIdenticalSeeds();DiscreteSettings();await CachedCollectionPolicy();await CompletionOutcomes();}
        var four=Walls(1000,1000,1000,1000);var run=new Top50Search().Run(four,new[]{1000},Limits(),PlanningPreset.BalancedOptimization,3,Token,percentage:45,bandPoints:10);
        Check(run.Proposals.Single().StockMicrometres==2000000&&run.Proposals.Single().Groups.Count==2,"percentage whole-wall offer within 35–55 percent band = 50 percent");
        IndependentValidate(four,run.Proposals.Single(),Limits());
        Check(new Top50Search().Run(four,new[]{1000},Limits(),PlanningPreset.BalancedOptimization,3,Token,percentage:45,bandPoints:1).Proposals.Count==0,"restrictive 44–46 percent band returns none, without invented partial walls");
        var identical=new Top50Search().Run(four,new[]{1000},Limits(longest:2),PlanningPreset.BalancedOptimization,3,Token,identicalKits:true);
        Check(identical.Proposals.Single().IdenticalKitCount==2&&identical.Proposals.Single().PanelCount==2,"fixed identical kits preserved");
        var repeated=new Top50Search().Run(Enumerable.Reverse(four).ToArray(),new[]{1000},Limits(longest:2),PlanningPreset.BalancedOptimization,3,Token,identicalKits:true);
        Check(Key(identical.Proposals.Single())==Key(repeated.Proposals.Single()),"wall order does not change deterministic tied allocations");
        Console.WriteLine("INDEPENDENT QA: "+checks+" checks passed.");return 0;
    }
}
