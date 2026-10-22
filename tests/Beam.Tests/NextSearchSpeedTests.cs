using System;
using System.Linq;
using System.Diagnostics;
using EngineeringDashboard;

internal static class NextSearchSpeedTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Next search speed: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<=2e-7*Math.Max(1,Math.Abs(expected)),message+$" ({actual:R}/{expected:R})");
    static void Same(LoadedBeamAnalysis actual,LoadedBeamAnalysis expected,double length)
    {
        Near(actual.TotalLoadKn,expected.TotalLoadKn,"force");Near(actual.MomentKnM,expected.MomentKnM,"moment");Near(actual.ShearKn,expected.ShearKn,"shear");
        Near(actual.DeflectionMm,expected.DeflectionMm,"deflection");Near(actual.DeflectionUtilization,expected.DeflectionUtilization,"local limit");
        for(int i=0;i<actual.Reactions.Length;i++)Near(actual.Reactions[i],expected.Reactions[i],"reaction");
        for(int i=0;i<=12;i++)for(int component=0;component<3;component++)Near(actual.Response(length*i/12)[component],expected.Response(length*i/12)[component],"response curve");
    }
    static TowerInput Common(bool optimized,int workers=1)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,SeedNetworkSearch=optimized,OptimizePrimaryResponses=optimized,
        LengthMm=2450,StockLengthsMm=new[] {1250.0,1800,2450},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=workers,Trace=new BeamSearchTrace("next optimization regression")};
    static TransferNetworkInput Network()=>new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
        BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new double[0],
        Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=200}}};
    static void Valid(TransferNetworkResult result)
    {
        foreach(var beam in result.Beams)
        {
            Check(beam.MaxLegKn<=beam.LegCapacityKn+1e-8,"every leg passes");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8,"all member checks complete and pass");
                Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"selected member equilibrium");
            }
        }
    }
    public static void Run()
    {
        var random=new Random(101807);var shared=new TransferredSlabOptimizer.SearchWorkCache();
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(int denominator in new[] {270,350,500})
        for(int sample=0;sample<32;sample++)
        {
            double length=2450+sample%4*500,tail=150+sample%5*25;
            var supports=sample%2==0 ? new[] {tail,length-tail} : new[] {tail,length*.45,length-tail};
            var input=new TowerInput {PrimaryMaterial=material,LegCapacityKn=70,IgnoreUplift=true,StagePrimaryChecks=false,DeflectionDenominator=denominator,Trace=new BeamSearchTrace("unit UDL reference")};
            foreach(double q in new[] {1.0,5+random.NextDouble()*70,0})
            {
                var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=q}};
                var actual=TransferredSlabOptimizer.AnalyzePrimary(input,length,supports,loads,new BeamPointLoad[0],shared.Matrix,shared);
                var expected=LoadedBeamAnalysis.AnalyzeDenseReference(length,supports,loads,new BeamPointLoad[0],input.Primary.EI,denominator);
                Same(actual,expected,length);
            }
            Check(input.Trace.Count(BeamSearchCounter.UniformPrimaryCacheHits)>0,"same geometry reuses its complete unit response");
        }
        var control=new TowerInput {IgnoreUplift=true,StagePrimaryChecks=false,DeflectionDenominator=270};
        var discontinuous=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1800,KnPerM=20},new BeamDistributedLoad {StartMm=1800,EndMm=2450,KnPerM=50}};
        var stations=new[] {300.0,1300,2150};
        Same(TransferredSlabOptimizer.AnalyzePrimary(control,2450,stations,discontinuous,new BeamPointLoad[0],shared.Matrix,shared),LoadedBeamAnalysis.AnalyzeSigned(2450,stations,discontinuous,new BeamPointLoad[0],control.Primary.EI,270),2450);
        var unit=LoadedBeamAnalysis.Analyze(2450,stations,new[] {new BeamDistributedLoad {StartMm=0,EndMm=2450,KnPerM=1}},null,control.Primary.EI,270);
        foreach(double q in new[] {10/unit.MomentKnM,22/unit.ShearKn,1/unit.DeflectionUtilization,70/unit.Reactions.Max()})
        {
            var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2450,KnPerM=q}};
            var actual=TransferredSlabOptimizer.AnalyzePrimary(control,2450,stations,loads,new BeamPointLoad[0],shared.Matrix,shared);
            var expected=LoadedBeamAnalysis.AnalyzeSigned(2450,stations,loads,new BeamPointLoad[0],control.Primary.EI,270);
            Same(actual,expected,2450);
            Check((actual.MomentKnM<=10+1e-8)==(expected.MomentKnM<=10+1e-8) && (actual.ShearKn<=22+1e-8)==(expected.ShearKn<=22+1e-8) && (actual.DeflectionUtilization<=1+1e-8)==(expected.DeflectionUtilization<=1+1e-8),"capacity-boundary decisions match");
        }
        // Receiving-beam checks carry their worker cache on TransferredSlab,
        // whereas complete-network checks carry it directly on TowerInput.
        var receiving=new TowerInput {IgnoreUplift=true,DeflectionDenominator=270,Trace=new BeamSearchTrace("receiving primary reuse"),TransferredSlab=new TransferredSlabInput {SharedSearchWork=new TransferredSlabOptimizer.SearchWorkCache()}};
        var analyze=typeof(CrossBeamSupportOptimizer).GetMethod("Analyze",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        foreach(double q in new[] {20.0,25})
        {
            var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2450,KnPerM=q}};
            var source=new TransferMember {LengthMm=2450,SupportsMm=stations,H20Count=2,Uniform=loads,Points=new BeamPointLoad[0]};
            var result=(TransferMember)analyze.Invoke(null,new object[] {source,source.Points,loads,receiving,"fully loaded",false});
            Same(result.Analysis,LoadedBeamAnalysis.AnalyzeSigned(2450,stations,loads,source.Points,control.Primary.EI,270),2450);
        }
        Check(receiving.Trace.Count(BeamSearchCounter.UniformPrimaryCacheHits)>0,"receiving primary checks reuse the attached worker's shape cache");
        var lru=new TransferredSlabOptimizer.ResponseCache<int,string>(2);lru.Store(1,"first");lru.Store(2,"second");Check(lru.TryGetValue(1,out var first),"read retained");lru.Store(3,"third");
        Check(lru.TryGetValue(1,out first) && !lru.TryGetValue(2,out var second) && lru.Count==2,"recent response survives bounded replacement");
        Check(TransferNetworkOptimizer.SearchWorkers(8,12)==8 && TransferNetworkOptimizer.SearchWorkers(6,12)==6 && TransferNetworkOptimizer.SearchWorkers(8,4)==4,"worker experiments respect available processors");
        var baseline=Common(false);var original=TransferNetworkOptimizer.Optimize(baseline,Network());Valid(original);
        var optimized=Common(true);var revised=TransferNetworkOptimizer.Optimize(optimized,Network());Valid(revised);
        Check(revised.LegCount==original.LegCount,"earlier candidate retains support count");Near(revised.TimberMm,original.TimberMm,"earlier candidate retains timber preference");
        for(int i=0;i<revised.Beams.Length;i++)
        {
            Check(revised.Beams[i].PositionsMm.SequenceEqual(original.Beams[i].PositionsMm),"centred support stations retained");
            for(int stage=0;stage<revised.Beams[i].Transfer.PrimaryCases.Length;stage++)for(int member=0;member<revised.Beams[i].Transfer.PrimaryCases[stage].Length;member++)
            {var a=revised.Beams[i].Transfer.PrimaryCases[stage][member];var b=original.Beams[i].Transfer.PrimaryCases[stage][member];Same(a.Analysis,b.Analysis,a.LengthMm);}
        }
        Check(optimized.Trace.Count(BeamSearchCounter.SeedNetworkCandidates)>0,"a fully checked seed is found before normal expansion");
        Check(optimized.Trace.Count(BeamSearchCounter.PrimaryLoadCacheHits)>0,"repeated load clipping is reused");
        Console.WriteLine($"Passed {checks} next-search numerical, bounded-cache and complete-network checks.");
    }
    public static void Workers()
    {
        TransferNetworkResult reference=null;
        foreach(int requested in new[] {4,6,8})
        {
            var common=Common(true,requested);common.LengthMm=4900;common.MaximumConcreteEndOverhangMm=175;common.StockLengthsMm=TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths);var timer=Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,Network());timer.Stop();Valid(result);
            if(reference!=null){Check(result.LegCount==reference.LegCount,"worker leg count parity");Near(result.TimberMm,reference.TimberMm,"worker material parity");}
            reference=result;
            Console.WriteLine($"WORKERS requested={requested} actual={TransferNetworkOptimizer.SearchWorkers(requested,Environment.ProcessorCount)} processors={Environment.ProcessorCount} seconds={timer.Elapsed.TotalSeconds:0.000} managedMB={GC.GetTotalMemory(false)/1048576.0:0.0} peakWorkingMB={Process.GetCurrentProcess().PeakWorkingSet64/1048576.0:0.0} legs={result.LegCount}");
        }
    }
}
