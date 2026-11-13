using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class ReceiverTopologyReserveTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Receiver topology reserve: "+message);}
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=6000,
        StockLengthsMm=new[]{1250.0,2000,2450,5000},MaximumConcreteEndOverhangMm=0,StockEndSetbackMm=150,LegCapacityKn=70,
        DeflectionDenominator=270,IgnoreUplift=true,NetworkSearchWorkers=1,Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true)};
    static TransferNetworkInput Network()=>new TransferNetworkInput {PlywoodSheetLengthMm=2440,PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,
        UpperSpacingMm=305,BeamSpacingsMm=new[]{305.0,305},SlabSpacingsMm=new[]{488.0},CrossingSpacingsMm=Array.Empty<double>(),
        CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,
        Beams=new[]{new TransferBeamRow {WidthMm=10,DepthMm=10,SpanLengthMm=5000},new TransferBeamRow {WidthMm=10,DepthMm=10,SpanStartMm=1000,SpanLengthMm=5000}},
        Bays=new[]{new TransferBayRow{WidthMm=2850,ThicknessMm=10}}};
    static double[][] SharedCovers(TowerInput common,TransferNetworkInput network,TransferredSlabOptimizer.GeometryCache cache)
    {
        var shared=common.CopyForSearch();shared.LengthMm=network.SharedEnd(common.LengthMm)-network.SharedStart;shared.LongitudinalStartMm=network.SharedStart;
        return TransferNetworkOptimizer.CachedSpanStockCovers(shared,cache);
    }
    static void Counterexample()
    {
        var common=Common();var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();
        var shared=SharedCovers(common,network,cache);
        Check(shared.Min(c=>c.Length)==2,"4000 mm shared coverage needs two catalogue pieces");
        var reserve=new TransferNetworkOptimizer.ReceiverTopologyReserve(common,network,shared,cache);
        for(int beam=0;beam<2;beam++)
        {
            var local=TransferNetworkOptimizer.ReceiverStockCovers(common,network,beam,shared,cache);
            Check(local.Any(c=>c.SequenceEqual(new[]{5000.0})) && reserve.CurrentLegs(beam)==4,"a local 5000 mm beam permits one independent stock run and four legs");
        }
        Check(reserve.FutureLegs(0)==4 && reserve.FutureBays(0)==1 && reserve.FutureLegs(1)==0 && reserve.FutureBays(1)==0,"future reserves sum only actual remaining local minima");
        var boundType=typeof(TransferNetworkOptimizer).GetNestedType("EngineerBound",BindingFlags.NonPublic);
        var bound=Activator.CreateInstance(boundType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{common,network,null},null);
        boundType.GetField("legs",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bound,12);
        int old=(int)boundType.GetMethod("Ceiling",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(int),typeof(int),typeof(int)},null).Invoke(bound,new object[]{0,1,8});
        int corrected=(int)boundType.GetMethod("Ceiling",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(int),typeof(int)},null).Invoke(bound,new object[]{0,reserve.FutureLegs(0)});
        Check(old==4 && old<8 && corrected==8 && corrected>=reserve.CurrentLegs(0),"the old shared minimum prunes a feasible four-leg current receiver under a twelve-leg incumbent");
        var response=LoadedBeamAnalysis.AnalyzeDenseReference(5000,new[]{1500.0,3500},new[]{new BeamDistributedLoad{StartMm=0,EndMm=5000,KnPerM=.1}},null,common.Primary.EI,270);
        Check(response.MomentKnM<common.Primary.MomentLimitKnM && response.ShearKn<common.Primary.ShearLimitKn && response.DeflectionUtilization<1
            && response.Reactions.Max()<common.LegCapacityKn && Math.Abs(response.Reactions.Sum()-.5)<1e-9,"a light-load 5000 mm stock with two supports independently passes all member/leg/equilibrium checks");
        var slab=new UpperTransferPlan{StockLengthMm=2450,EdgeMm=200,NearFootMm=350,FarFootMm=350};
        var method=typeof(TransferNetworkOptimizer).GetMethod("SolveReceiver",BindingFlags.Static|BindingFlags.NonPublic);
        for(int beam=0;beam<2;beam++)
        {
            TowerLayout receiver;
            try{receiver=(TowerLayout)method.Invoke(null,new object[]{common,network,beam,beam==0?null:slab,beam==0?slab:null,4000.0,0.0,shared,cache,4,(double?)1524,0.0,null,null,null});}
            catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            Check(receiver!=null && receiver.LegCount==4 && receiver.StockPlan.Pieces.Length==1 && receiver.StockPlan.Pieces[0].StockLengthMm==5000,
                "the unchanged full local receiver solver actually accepts the four-leg one-stock arrangement");
            Check(receiver.Transfer.PrimaryCases.SelectMany(c=>c).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.Max()<=common.LegCapacityKn+1e-8),"the accepted local receiver retains complete primary member and leg checks");
        }
    }
    static void SuffixAndScope()
    {
        var common=Common();var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var shared=SharedCovers(common,network,cache);
        network.Beams[0].SpanLengthMm=4000;
        var mixed=new TransferNetworkOptimizer.ReceiverTopologyReserve(common,network,shared,cache);
        Check(mixed.CurrentLegs(0)==8 && mixed.CurrentLegs(1)==4 && mixed.FutureLegs(0)==4 && mixed.FutureBays(0)==1,"unequal receiver minima are retained separately rather than replaced by a shared scalar");
        var restricted=new TransferredSlabOptimizer.GeometryCache {Fixed=Array.Empty<TransferredSlabOptimizer.Geometry>()};
        Check(TransferNetworkOptimizer.ReceiverStockCovers(common,network,1,shared,restricted).Length==0,"fixed geometry retains the prior restricted-cover intersection");
        var empty=new TransferNetworkOptimizer.ReceiverTopologyReserve(common,network,shared,restricted);
        Check(empty.CurrentLegs(1)==0 && empty.FutureLegs(0)==0,"an empty restricted universe supplies no speculative positive reserve");
        foreach(int count in Enumerable.Range(2,7))
        {
            var equal=Network();equal.Beams=Enumerable.Range(0,count).Select(_=>new TransferBeamRow()).ToArray();
            var covers=new[]{new[]{2000.0,2000},new[]{1250.0,1250,1500}};
            Check(ReferenceEquals(covers,TransferNetworkOptimizer.ReceiverStockCovers(common,equal,0,covers,cache)),"same-span local cover selection preserves its exact existing list");
            var suffix=new TransferNetworkOptimizer.ReceiverTopologyReserve(common,equal,covers,cache);
            for(int beam=0;beam<count;beam++)
                Check(suffix.CurrentLegs(beam)==8 && suffix.FutureLegs(beam)==8*(count-beam-1) && suffix.FutureBays(beam)==2*(count-beam-1),"same-span suffix values exactly match the old scalar reserve formulas");
        }
    }
    internal static void Run(){Counterexample();SuffixAndScope();Console.WriteLine("PASS "+checks+" per-receiver topology reserve checks: shared/local counterexample, full local mechanical witness, suffix parity and fixed scope");}
}
