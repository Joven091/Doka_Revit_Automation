using System;
using System.Linq;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using EngineeringDashboard;

// This harness deliberately uses reflection for optional experiment controls so
// the same file can be compiled beside the previous revision in Windows CI.
internal static class ControlledPerformanceTests
{
    static string Option(string[] args,string name,string fallback)
    {int at=Array.IndexOf(args,name);return at<0 || at+1==args.Length ? fallback : args[at+1];}
    static void Set(object target,string field,object value)
    {target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.SetValue(target,value);}
    internal static BeamCalculatorViewModel Model(string model)
    {
        if(model=="eight-x2" || model=="eight-core")
        {
            int beams=model=="eight-x2" ? 2 : 3;
            var r=Model("eight-crossings");while(r.NetworkBeams.Count>beams+1)r.NetworkBeams.RemoveAt(r.NetworkBeams.Count-1);r.NetworkBeams.RemoveAt(0);
            while(r.NetworkBays.Count>beams)r.NetworkBays.RemoveAt(r.NetworkBays.Count-1);r.NetworkBays.RemoveAt(0);
            r.CrossingBeams.Clear();for(int bay=1;bay<beams;bay++)r.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=bay,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150});return r;
        }
        if(model=="eight-crossings")
        {
            var reported=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,
                BeamLengthMm=6900,Depth=1450,Width=800,FloorHeight=3000,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,
                CustomH20Scope=CustomH20LengthScope.Both,MaximumConcreteEndOverhangMm=75,StockEndSetbackMm=150,
                Exclude600Standalone=true,IgnoreUplift=true,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
            while(reported.NetworkBeams.Count<8)reported.AddNetworkBay();
            for(int i=0;i<8;i++)
            {
                var beam=reported.NetworkBeams[i];beam.WidthMm=i%2==0 ? 800 : 650;
                beam.DepthMm=i%2==0 ? 1750 : 1150;beam.BottomElevationMm=-beam.DepthMm;
            }
            var widths=new[] {2677.0,1875,3275,2275,2278,2272,2252};
            for(int i=0;i<widths.Length;i++)
            {
                reported.NetworkBays[i].WidthMm=widths[i];reported.NetworkBays[i].ThicknessMm=300;
                reported.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=i+1,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150});
            }
            return reported;
        }
        if(model=="local-rows"){var reported=Model("search-error");reported.CrossingBeams.RemoveAt(2);return reported;}
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,
            BeamLengthMm=model=="junction" ? 10800 : 10600,Depth=1750,Width=800,FloorHeight=3000,TowerLegCapacityKn=70,AllowCustomH20Lengths=model=="junction",MaximumConcreteEndOverhangMm=model=="junction" ? 75 : 175,StockEndSetbackMm=150,
            Exclude600Standalone=model=="junction",PlywoodDeflectionLimit=model=="junction" ? "L/270" : "L/350",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        if(model!="junction")vm.AddNetworkBay();
        if(model=="search-error")vm.AddNetworkBay();
        for(int i=0;i<vm.NetworkBeams.Count;i++){vm.NetworkBeams[i].WidthMm=i<(model=="search-error" ? 2 : 1) ? 650 : 800;vm.NetworkBeams[i].DepthMm=i<(model=="search-error" ? 2 : 1) ? 1150 : 1750;vm.NetworkBeams[i].BottomElevationMm=i<(model=="search-error" ? 2 : 1) ? -1150 : -1750;}
        vm.NetworkBays[0].WidthMm=model=="junction" ? 2575 : 3000;vm.NetworkBays[0].ThicknessMm=model=="junction" ? 300 : 200;
        if(model!="junction"){vm.NetworkBays[1].WidthMm=2125;vm.NetworkBays[1].ThicknessMm=300;}
        foreach(int bay in model=="junction" ? new[] {1} : new[] {2,1})vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=bay,PositionMm=model=="junction" ? 5375 : 5675,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150});
        if(model=="search-error")
        {
            vm.NetworkBays[1].WidthMm=3000;vm.NetworkBays[1].ThicknessMm=200;
            vm.NetworkBays[2].WidthMm=3500;vm.NetworkBays[2].ThicknessMm=300;
            vm.CrossingBeams.Clear();
            foreach(var c in new[] {new {Bay=3,X=5675},new {Bay=2,X=5675},new {Bay=3,X=2500}})
                vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=c.Bay,PositionMm=c.X,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150});
        }
        return vm;
    }
    internal static TowerInput Input(string model)
        =>(TowerInput)typeof(BeamCalculatorViewModel).GetMethod("CreateTowerInput",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Model(model),new object[] {true});
    static string Number(double x)=>Math.Round(x,7).ToString("R",CultureInfo.InvariantCulture);
    internal static string Fingerprint(TransferNetworkResult result)
    {
        var s=new StringBuilder();s.AppendLine("legs="+result.LegCount+"; H20="+result.H20Count+"; timber="+Number(result.TimberMm));
        foreach(var b in result.Beams)
        {
            s.AppendLine("beam: "+string.Join(",",b.PositionsMm.Select(Number))+"; joined="+string.Join(",",b.Joined)+"; width="+Number(b.WidthMm)+"; offset="+Number(b.TransverseOffsetMm));
            foreach(var stage in b.Transfer.PrimaryCases)foreach(var m in stage)
                s.AppendLine("primary: "+m.Name+"; start="+Number(m.StartMm)+"; length="+Number(m.LengthMm)+"; udl="+string.Join(";",m.Uniform.Select(q=>Number(q.StartMm)+","+Number(q.EndMm)+","+Number(q.KnPerM)))+"; reactions="+string.Join(",",m.Analysis.Reactions.Select(Number)));
        }
        foreach(var c in result.CrossSupports)s.AppendLine("cross: "+c.Input.BayNumber+"; position="+Number(c.Input.PositionMm)+"; spreader="+c.UsesSpreader+"; bearings="+string.Join(";",c.Bearings.Select(b=>b.Beam+","+Number(b.Xmm)+","+Number(b.Ymm)+","+Number(b.ReactionKn))));
        return s.ToString();
    }
    internal static void Validate(TowerInput input,TransferNetworkResult result)
    {
        foreach(var b in result.Beams)
        {
            if(b.MaxLegKn>input.LegCapacityKn+1e-8)throw new Exception("Reported benchmark leg capacity failed.");
            foreach(var m in b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(x=>x).Concat(b.Transfer.UpperMembers))
                if(!m.Analysis.DeflectionEvaluated || m.Utilization>1+1e-8 || !input.IgnoreUplift && m.MinimumReactionKn< -1e-7 || Math.Abs(m.Analysis.Reactions.Sum()-m.Analysis.TotalLoadKn)>1e-6*Math.Max(1,Math.Abs(m.Analysis.TotalLoadKn)))throw new Exception("Reported benchmark member check failed: "+m.Name);
        }
        foreach(var c in result.CrossSupports)
        {
            if(c.Members.Any(m=>!m.Analysis.DeflectionEvaluated || m.Utilization>1+1e-8))throw new Exception("Reported crossing capacity failed.");
            foreach(var solid in c.Solids)if(CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).Any(s=>solid.Conflicts(s)))throw new Exception("Reported crossing physical clearance failed.");
        }
    }
    public static void Run(string[] args)
    {
        string model=Option(args,"--model","reported"),variant=Option(args,"--variant","current");
        int seconds=int.Parse(Option(args,"--seconds","0"),CultureInfo.InvariantCulture),workers=int.Parse(Option(args,"--workers","4"),CultureInfo.InvariantCulture);
        bool profile=args.Contains("--profile");
        using(var cancel=new CancellationTokenSource())
        {
            if(seconds>0)cancel.CancelAfter(TimeSpan.FromSeconds(seconds));
            var input=Input(model);input.NetworkSearchWorkers=workers;input.Trace=new BeamSearchTrace("Controlled "+model+" "+variant,cancel.Token);Set(input.Trace,"EnableHotProfiling",profile);
            if(variant=="tight")input.Network=input.Network.WithSpacings(PlywoodSpacing.StandardSpacingsMm.Count-1);
            if(variant=="prior-policy"){Set(input,"SeedNetworkSearch",false);Set(input,"OptimizePrimaryResponses",false);}
            if(variant=="no-seed")Set(input,"SeedNetworkSearch",false);
            if(variant=="crossing-reference")Set(input,"OptimizeCrossingSearch",false);
            if(variant=="feasibility")Set(input,"FirstCompleteNetworkOnly",true);
            if(variant=="no-udl-reuse")Set(input,"OptimizePrimaryResponses",false);
            if(variant=="reference")Set(input,"PrepareReceiverChecks",false);
            int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);var process=Process.GetCurrentProcess();double cpuBefore=process.TotalProcessorTime.TotalSeconds;
            var timer=Stopwatch.StartNew();TransferNetworkResult result=null;bool cancelled=false;
            var task=Task.Run(()=>TransferNetworkOptimizer.Optimize(input,input.Network));
            try
            {
                while(!task.Wait(TimeSpan.FromSeconds(30)))
                {
                    Console.WriteLine("PROGRESS wall="+timer.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture));
                    Console.WriteLine(string.Join("\n",input.Trace.Snapshot().Split('\n').Where(l=>l.StartsWith("Profile ") || l.StartsWith("First complete") || l.StartsWith("Last network") || l.StartsWith("Network improvements") || l.StartsWith("Current/last ") || l.StartsWith("Latest crossing") || l.StartsWith("Primary member analyses:") || l.StartsWith("Transferred layout candidates:"))));
                }
                result=task.GetAwaiter().GetResult();
            }
            catch(Exception error)
            {
                var ex=error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.First() : error;
                if(ex is TowerOptimizationException rejected)Console.WriteLine(rejected.Details);
                if(!(ex is OperationCanceledException) || seconds<=0)throw;
                cancelled=true;
            }
            timer.Stop();input.Trace.Complete(cancelled ? "Profiling prefix cancelled as requested" : "Succeeded");
            if(result!=null)Validate(input,result);
            Console.WriteLine("PERFORMANCE model="+model+" variant="+variant+" runtime="+Environment.Version+" processors="+Environment.ProcessorCount+" requestedWorkers="+workers+" prefixOnly="+cancelled+" wall="+timer.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" cpu="+(process.TotalProcessorTime.TotalSeconds-cpuBefore).ToString("0.000",CultureInfo.InvariantCulture)+" gc="+(GC.CollectionCount(0)-gc0)+"/"+(GC.CollectionCount(1)-gc1)+"/"+(GC.CollectionCount(2)-gc2)+" managedMB="+(GC.GetTotalMemory(false)/1048576.0).ToString("0.0",CultureInfo.InvariantCulture)+" peakWorkingMB="+(process.PeakWorkingSet64/1048576.0).ToString("0.0",CultureInfo.InvariantCulture));
            Console.WriteLine(input.Trace.Snapshot());
            if(result!=null){Console.WriteLine("FINGERPRINT BEGIN\n"+Fingerprint(result)+"FINGERPRINT END");Console.WriteLine(result.LayoutDiagnostics);}
        }
    }
}
