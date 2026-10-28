using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EngineeringDashboard.Top50;
using Google.OrTools.Sat;

internal static class Program
{
    static int checks;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    static void Cancelled(Action action,string message){try{action();throw new Exception(message);}catch(OperationCanceledException){checks++;}}
    static WallRow Wall(int n,long length)=>new WallRow{Id="W-"+n,LengthMicrometres=length,Included=true,Status=WallReviewStatus.Valid};
    static string Stock(IEnumerable<KeyValuePair<int,int>> stock)=>string.Join(";",stock.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value));
    static string Patterns(IReadOnlyList<PanelPattern> patterns)=>string.Join("|",patterns.Select(p=>string.Join(",",p.PanelsMm)+"/"+string.Join(",",p.InternalGapsMicrometres)));
    static string Result(SearchRun run)=>run.Attempted+"/"+run.Unsearched+"/"+run.SkippedForPanelQuantity+"\n"+string.Join("\n",run.Proposals.Select(p=>p.Id+"/"+p.Rank+"/"+p.Pareto+"/"+Stock(p.Stock)+"/"+p.RetainedModelStatus+"/"+p.RetainedObjective+"/"+p.RetainedBound+"/"+string.Join("|",p.Groups.Select(Top50Order.Group))));
    static void PatternReuse()
    {
        var walls=new[]{Wall(1,3500000),Wall(2,4200000),Wall(3,3500000),Wall(4,4200000),Wall(5,3500000)};
        var catalog=new[]{500,1000,1500};var cache=new CalculationPatternCache();
        for(int longest=1;longest<=5;longest++)
        {
            var limits=new SearchLimits(1,350,15,longest,1,50);
            var expected=new CatalogCutoffCandidates().Generate(walls,catalog,limits,CancellationToken.None).Select(Stock).ToArray();
            var actual=new CatalogCutoffCandidates(cache).Generate(walls,catalog,limits,CancellationToken.None).Select(Stock).ToArray();
            Check(expected.SequenceEqual(actual),"Cached candidates changed inventory order/content");
        }
        Check(cache.Preparations==6 && cache.Hits==24,"Expected six preparations instead of 30 repeated enumerations");
        var baseLimits=new SearchLimits(1,350,15,1,1,50);
        var first=cache.Enumerate(3500000,catalog,baseLimits,CancellationToken.None);int preparations=cache.Preparations;
        Check(ReferenceEquals(first,cache.Enumerate(3500000,new[]{1500,1000,500,1500},baseLimits,CancellationToken.None)),"Equivalent catalog order should reuse the same patterns");
        Check(cache.Preparations==preparations,"Catalog normalization missed a reusable preparation");
        foreach(var limits in new[]{new SearchLimits(0,350,15,1,1,50),new SearchLimits(1,200,15,1,1,50),new SearchLimits(1,350,15,1,1,50,2),new SearchLimits(1,350,15,1,2,50)})
        {
            Check(Patterns(new FewestComponentPatterns().Enumerate(3500000,catalog,limits,CancellationToken.None))==Patterns(cache.Enumerate(3500000,catalog,limits,CancellationToken.None)),"Changed filler/count/work settings reused other patterns");
        }
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();Cancelled(()=>cache.Enumerate(3500000,catalog,baseLimits,cancelled.Token),"A cache hit ignored cancellation");
        }
        var otherRun=new CalculationPatternCache();otherRun.Enumerate(3500000,catalog,baseLimits,CancellationToken.None);
        Check(otherRun.Preparations==1 && otherRun.Hits==0,"Pattern cache leaked across calculations");
        var bounded=new CalculationPatternCache();
        for(int i=0;i<=4096;i++)bounded.Enumerate(500000+i,new[]{500},baseLimits,CancellationToken.None);
        Check(bounded.Enumerate(500000,new[]{500},baseLimits,CancellationToken.None).Single().PanelsMm.Single()==500 && bounded.Preparations==4098,"Cache eviction changed the available patterns instead of recomputing");
        var searchLimits=new SearchLimits(1,350,5,1,1,50);
        var uncached=new Top50Search(new CatalogCutoffCandidates(),new RetainedInventorySolver()).Run(walls,catalog,searchLimits,PlanningPreset.BalancedOptimization,3,CancellationToken.None,percentage:50,bandPoints:50);
        var cached=new Top50Search().Run(walls,catalog,searchLimits,PlanningPreset.BalancedOptimization,3,CancellationToken.None,percentage:50,bandPoints:50);
        Check(Result(uncached)==Result(cached),"Pattern reuse changed complete ranked solver results");
        Check(cached.Proposals.Count>0,"Fixture did not produce a complete allocation");
        Console.WriteLine("PASS Top50 pattern reuse: 30 -> 6 preparations, exact candidate order and complete ranked native results");
    }
    sealed class CancelAfterPatterns:ITop50PatternSource
    {
        readonly CancellationTokenSource cancellation;readonly int last;int calls;
        internal CancelAfterPatterns(CancellationTokenSource cancellation,int last){this.cancellation=cancellation;this.last=last;}
        public IReadOnlyList<PanelPattern> Enumerate(long length,IEnumerable<int> catalog,SearchLimits limits,CancellationToken token)
        {
            var result=new FewestComponentPatterns().Enumerate(length,catalog,limits,token);
            if(++calls==last)cancellation.Cancel();return result;
        }
    }
    sealed class Progress:IProgress<string>
    {
        internal readonly List<string> Messages=new List<string>();readonly Action<string> onReport;
        internal Progress(Action<string> onReport=null){this.onReport=onReport;}
        public void Report(string message){Messages.Add(message);onReport?.Invoke(message);}
    }
    static void Cancellation()
    {
        int calls=0;
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();Cancelled(()=>CancellableSearch.Run(()=>++calls,()=>{},new object(),cancelled.Token),"Cancelled solve was accepted");
            Check(calls==0,"Already cancelled token started solving");
        }
        Check(CancellableSearch.Run(()=>7,()=>{throw new Exception("Unrequested stop");},new object(),CancellationToken.None)==7,"Uncancelled solve changed");
        // Force the exact native-wrapper-start race: first stop is consumed before
        // the operation starts, so a one-shot callback can never complete this test.
        for(int i=0;i<20;i++)using(var cancellation=new CancellationTokenSource())using(var beforeStart=new ManualResetEventSlim())using(var started=new ManualResetEventSlim())using(var stopped=new ManualResetEventSlim())
        {
            int attempts=0;
            Cancelled(()=>CancellableSearch.Run(()=>
            {
                cancellation.Cancel();
                if(!beforeStart.Wait(3000))throw new Exception("Missing early stop attempt");
                started.Set();
                if(!stopped.Wait(3000))throw new Exception("Lost cancellation during solver startup");
                return 1;
            },()=>{Interlocked.Increment(ref attempts);if(started.IsSet)stopped.Set();else beforeStart.Set();},new object(),cancellation.Token),"Cancellation returned a successful result");
            Check(attempts>=2,"Cancellation did not survive an early consumed stop");
        }
        var walls=new[]{Wall(1,3500000),Wall(2,4200000)};var stock=new Dictionary<int,int>{{500,10},{1000,10},{1500,10}};var limits=new SearchLimits(1,350,15,1,1,50);
        using(var cancellation=new CancellationTokenSource())
        {
            var progress=new Progress();
            Cancelled(()=>new RetainedInventorySolver(new CancelAfterPatterns(cancellation,2)).Solve("after-patterns",walls,stock,limits,cancellation.Token,progress),"Late pattern cancellation accepted a result");
            Check(!progress.Messages.Any(s=>s.StartsWith("Solving inventory")),"Cancelled pattern preparation still entered native solving");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var progress=new Progress(s=>{if(s.StartsWith("Solving inventory"))cancellation.Cancel();});
            Cancelled(()=>new RetainedInventorySolver().Solve("before-native",walls,stock,limits,cancellation.Token,progress),"Cancellation before native startup was accepted");
            Check(!progress.Messages.Any(s=>s.StartsWith("Retained status")),"Cancelled result was reported as successful");
        }
        var model=new CpModel();var x=model.NewBoolVar("x");model.Add(x==1);var solver=new CpSolver();
        Check(CancellableSearch.Run(()=>solver.Solve(model),solver.StopSearch,solver,CancellationToken.None)==CpSolverStatus.Optimal,"Native unrequested cancellation changed solve status");
        using(var cancellation=new CancellationTokenSource())
        {
            var running=new CpSolver{StringParameters="log_search_progress:true log_to_stdout:false num_search_workers:1 random_seed:50"};
            running.SetLogCallback(message=>cancellation.Cancel());
            Cancelled(()=>CancellableSearch.Run(()=>running.Solve(model),running.StopSearch,running,cancellation.Token),"Cancellation from the native solver was accepted as success");
            Check(cancellation.IsCancellationRequested,"Native cancellation fixture did not reach its callback");
        }
        Console.WriteLine("PASS Top50 cancellation: before preparation/native startup, repeated forced startup races, completed native solve");
    }
    static int Main()
    {
        try{PatternReuse();Cancellation();Console.WriteLine("PASS "+checks+" Top50 search regression checks");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
