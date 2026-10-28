using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using EngineeringDashboard;

internal static class ReportedCombinedTests
{
    internal static TowerInput Input(int area)
    {
        using(var stream=typeof(ReportedCombinedTests).Assembly.GetManifestResourceStream("Combined20261028.area"+area+".xml"))
        {
            if(stream==null)throw new Exception("Missing reported area fixture "+area);
            var root=XElement.Load(stream);
            var vm=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,TransferSlabLoads=true,UseBeamNetwork=true,
                BeamLengthMm=double.Parse(root.Attribute("LengthMm").Value,CultureInfo.InvariantCulture),Depth=500,Width=200,FloorHeight=3000,
                TowerLegCapacityKn=70,AllowCustomH20Lengths=false,IgnoreUplift=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,
                Exclude600Standalone=false,PlywoodDeflectionLimit="L/350",StockLengthsText="1250; 1800; 2450; 2650; 2900; 3300; 3600; 3900; 4500; 4900",
                DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
            vm.NetworkConfigurationXml=root.Element("TransferNetworkConfiguration").ToString();
            return vm.CreateTowerInput();
        }
    }
    internal static void Boundaries()
    {
        // The reported split run produced 2450.0000000000005 after subtracting its origin.
        double start=2500.485651397706,length=2450;
        if(start+length-start<=length)throw new Exception("Boundary regression did not reproduce endpoint roundoff on this runtime.");
        var plan=new EngineerPrimaryPlan {Lines=new[]{new[]{new BeamDistributedLoad {StartMm=0,EndMm=6000,KnPerM=5}},Array.Empty<BeamDistributedLoad>()}};
        var loads=plan.Loads(start,length,0);
        if(loads.Length!=1||loads[0].StartMm!=0||loads[0].EndMm!=length)throw new Exception("Generated member-local loads extend outside the exact stock boundary: "+string.Join(";",loads.Select(q=>q.StartMm.ToString("R")+"/"+q.EndMm.ToString("R")+"/"+q.KnPerM)));
        var analysis=LoadedBeamAnalysis.Analyze(length,new[]{300.0,2150},loads,null,450,270);
        if(Math.Abs(analysis.TotalLoadKn-12.25)>1e-10||Math.Abs(analysis.Reactions.Sum()-analysis.TotalLoadKn)>1e-10)throw new Exception("Boundary clipping changed load equilibrium.");
        foreach(int area in Enumerable.Range(1,5))if(Input(area).Network.Beams.Length!=2)throw new Exception("Reported fixture lost its complete beam pair.");
        var trace=new BeamSearchTrace("worker diagnostic counter regression");
        foreach(BeamSearchCounter counter in Enum.GetValues(typeof(BeamSearchCounter)))trace.Count(counter);
        var counters=new[] {BeamSearchCounter.NetworkTransverseCacheHits,BeamSearchCounter.LowerResponseCacheHits,BeamSearchCounter.CrossCacheHits,BeamSearchCounter.GeometryCacheHits};
        System.Threading.Tasks.Parallel.For(0,4,worker=>{for(int i=0;i<10007;i++)foreach(var counter in counters)trace.Increment(counter);});
        foreach(var counter in counters)if(trace.Count(counter)!=40028)throw new Exception("Live worker counter lost events: "+counter);
        trace.Complete("Succeeded");
        foreach(var counter in counters)if(trace.Count(counter)!=40028)throw new Exception("Final worker counter lost events: "+counter);
        Console.WriteLine("PASS exact member-boundary clipping, force conservation worker counter totals and five detached area fixtures");
    }
    static string Fingerprint(TransferNetworkResult result)
    {
        var text=new StringBuilder(ControlledPerformanceTests.Fingerprint(result));
        Action<string,TransferMember> append=(context,m)=>
        {
            Func<double,string> number=x=>x.ToString("R",CultureInfo.InvariantCulture);
            text.AppendLine("member: "+context+"/"+m.Name+"; primary="+m.IsPrimary+"; WS10="+m.IsWS10+"; count="+m.H20Count
                +"; values="+string.Join(",",new[] {m.StartMm,m.LengthMm,m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization,m.Utilization}.Select(number))
                +"; supports="+string.Join(",",m.SupportsMm.Select(number))+"; reactions="+string.Join(",",m.Analysis.Reactions.Select(number))
                +"; udl="+string.Join(";",m.Uniform.Select(q=>string.Join(",",new[] {q.StartMm,q.EndMm,q.KnPerM}.Select(number))))
                +"; points="+string.Join(";",m.Points.Select(q=>number(q.PositionMm)+","+number(q.Kn))));
        };
        for(int beam=0;beam<result.Beams.Length;beam++)
        {
            var transfer=result.Beams[beam].Transfer;
            foreach(var m in transfer.PrimaryCases.SelectMany(c=>c).Concat(transfer.LowerCases.SelectMany(c=>c)).Concat(transfer.UpperMembers))append("beam "+beam,m);
        }
        for(int crossing=0;crossing<result.CrossSupports.Length;crossing++)foreach(var m in result.CrossSupports[crossing].Members)append("crossing "+crossing,m);
        return text.ToString();
    }
    static string Option(string[] args,string name,string fallback){int at=Array.IndexOf(args,name);return at<0||at+1==args.Length?fallback:args[at+1];}
    internal static void Run(string[] args)
    {
        Boundaries();if(args.Contains("--combined-boundaries"))return;
        var areas=Option(args,"--areas","1,2,3,4,5").Split(',').Select(int.Parse).ToArray();
        int seconds=int.Parse(Option(args,"--seconds","0"));string output=Option(args,"--output","artifacts/combined-reported");Directory.CreateDirectory(output);
        int workers=int.Parse(Option(args,"--workers","4"));
        int areaWorkers=Math.Max(1,Math.Min(Math.Min(workers,areas.Length),int.Parse(Option(args,"--area-workers","1"))));
        int perAreaWorkers=Math.Max(1,workers/areaWorkers);
        var wall=Stopwatch.StartNew();
        if(areaWorkers==1)foreach(int area in areas)SolveArea(args,area,seconds,output,perAreaWorkers);
        else
        {
            var pending=new System.Collections.Concurrent.ConcurrentQueue<int>(areas);
            // Coordinators only wait/report; keep them outside the numerical
            // thread pool and start the requested area order without partitioning.
            var tasks=Enumerable.Range(0,areaWorkers).Select(_=>Task.Factory.StartNew(()=>
            {while(pending.TryDequeue(out int area))SolveArea(args,area,seconds,output,perAreaWorkers);},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)).ToArray();
            Task.WaitAll(tasks);
        }
        Console.WriteLine("TOTAL seconds="+wall.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" areaWorkers="+areaWorkers+" workersPerArea="+perAreaWorkers);
    }
    static void SolveArea(string[] args,int area,int seconds,string output,int workers)
    {
        using(var cancel=new CancellationTokenSource())
        {
            if(seconds>0)cancel.CancelAfter(TimeSpan.FromSeconds(seconds));
            var input=Input(area);input.Trace=new BeamSearchTrace("Detached reported area "+area,cancel.Token){EnableHotProfiling=args.Contains("--profile")};
            input.NetworkSearchWorkers=workers;
            if(args.Contains("--late-projected-primary"))typeof(TowerInput).GetField("PrecheckProjectedPrimaries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(input,false);
            if(args.Contains("--first"))input.FirstCompleteNetworkOnly=true;
            var timer=Stopwatch.StartNew();TransferNetworkResult result=null;Exception failure=null;
            try
            {
                var task=Task.Run(()=>TransferNetworkOptimizer.Optimize(input,input.Network));
                int ticks=0;
                while(!task.IsCompleted){Thread.Sleep(100);if(++ticks%200==0)Console.WriteLine("PROGRESS area="+area+" seconds="+timer.Elapsed.TotalSeconds.ToString("0.0",CultureInfo.InvariantCulture)+" candidates="+input.Trace.Count(BeamSearchCounter.CompleteNetworkCandidates)+" stage="+input.Trace.ProgressText);}
                result=task.GetAwaiter().GetResult();ControlledPerformanceTests.Validate(input,result);
                input.Trace.Complete("Succeeded",summary:result.Summary);
                Console.WriteLine("RESULT area="+area+" seconds="+timer.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" legs="+result.LegCount+" H20="+result.H20Count+" separate="+result.SeparateSlabCount+" maxLeg="+result.Beams.Max(b=>b.MaxLegKn).ToString("0.###",CultureInfo.InvariantCulture));
            }
            catch(Exception e){failure=e;input.Trace.Complete(e is OperationCanceledException?"Cancelled benchmark":"Failed",e.ToString());Console.WriteLine("RESULT area="+area+" seconds="+timer.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" failure="+e.GetType().Name+" "+e.Message);}
            File.WriteAllText(Path.Combine(output,"area"+area+".txt"),input.Trace.Snapshot()+"\n"+(result==null?(failure as TowerOptimizationException)?.Details:result.Summary+"\n"+result.Schedule+"\n"+Fingerprint(result)));
            if(failure!=null)throw failure;
        }
    }
}
