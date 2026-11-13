using System;
using System.Linq;
using EngineeringDashboard;

internal static class ReceiverAggregateReserveTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Receiver aggregate reserve: "+message);}
    static void Near(double actual,double expected,double tolerance,string message)=>Check(Math.Abs(actual-expected)<=tolerance,message+" "+actual+" / "+expected);
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=6000,
        StockLengthsMm=new[]{2000.0,3000,5000,6000},MaximumConcreteEndOverhangMm=0,StockEndSetbackMm=150,LegCapacityKn=70,
        DeflectionDenominator=270,IgnoreUplift=true,NetworkSearchWorkers=1};
    static TransferNetworkInput Network()=>new TransferNetworkInput {ReferenceLengthMm=6000,PlywoodSheetLengthMm=2440,PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,
        UpperSpacingMm=305,BeamSpacingsMm=new[]{305.0,305},SlabSpacingsMm=new[]{488.0},CrossingSpacingsMm=new[]{305.0},UseCrossingSlabLedgers=true,
        Beams=new[]{new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=800,DepthMm=1750}},
        Bays=new[]{new TransferBayRow{WidthMm=3000,ThicknessMm=200}},
        CrossingBeams=new[]{new TransferCrossingBeamRow {BayNumber=1,PositionMm=3000,WidthMm=650,DepthMm=900,BottomElevationMm=-900,SupportMode=CrossBeamSupportMode.DedicatedTowers}}};
    static TransferNetworkOptimizer.ReceiverAggregateLoadBound Bound(TowerInput common,TransferNetworkInput network,double cover=6000)
        =>TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,cover,true,new TransferredSlabOptimizer.GeometryCache());
    static void ForceAndSuffix()
    {
        var common=Common();var network=Network();var bound=Bound(common,network);
        Check(bound!=null,"the exact modern fully loaded terminal domain supplies a bound");
        double stockForce=bound.StockForce(0,0,6000),own=network.AreaLoad(1750)*.8*6;
        Check(stockForce>=own-1e-3 && stockForce>0,"maximum ledger deduction never removes beam concrete");
        var covers=new[]{new[]{6000.0},new[]{3000.0,3000},new[]{2000.0,2000,2000}};
        int minimum=bound.MinimumLegs(0,covers);
        Check(minimum>4 && minimum<=12,"physical reserve strengthens a single-run topology without exceeding the three-piece topology");
        var reserve=new TransferNetworkOptimizer.ReceiverTopologyReserve(common,network,covers,new TransferredSlabOptimizer.GeometryCache(),bound);
        Check(reserve.CurrentLegs(0)==minimum && reserve.CurrentLegs(1)==minimum && reserve.FutureLegs(0)==minimum && reserve.FutureLegs(1)==0,"current and future leg reserves use each independent receiver minimum");
        Check(reserve.FutureBays(0)==1 && reserve.FutureBays(1)==0,"bay ranking retains the prior topology reserve");
        Check(bound.MinimumLegs(0,Array.Empty<double[]>())==0 && bound.MinimumLegs(0,new[]{new[]{5000.0}})==0,"empty or mismatched slab coverage supplies no speculative bound");
        int catalogue=bound.MinimumLegs(0,new[]{new[]{2000.0,2000,2000}});
        int custom=bound.MinimumLegs(0,new[]{new[]{6000.0}});
        Check(bound.MinimumLegs(0,new[]{new[]{2000.0,2000,2000},new[]{6000.0}})==Math.Min(catalogue,custom),"catalogue/custom union takes the weaker complete-universe minimum");
        network.Beams[0].SpanLengthMm=5000;network.Beams[1].SpanStartMm=1000;network.Beams[1].SpanLengthMm=5000;
        var localBound=Bound(common,network,4000);var cache=new TransferredSlabOptimizer.GeometryCache();
        var localReserve=new TransferNetworkOptimizer.ReceiverTopologyReserve(common,network,new[]{new[]{2000.0,2000}},cache,localBound);
        for(int beam=0;beam<2;beam++)
        {
            var local=TransferNetworkOptimizer.ReceiverStockCovers(common,network,beam,new[]{new[]{2000.0,2000}},cache);
            Check(localReserve.CurrentLegs(beam)==Math.Max(4*local.Min(c=>c.Length),localBound.MinimumLegs(beam,local)),"DifferentSpans uses its own full local cover universe");
        }
        double expectedOwn=network.AreaLoad(1750)*.8*5;
        Check(localBound.StockForce(0,0,5000)>=expectedOwn-1e-3 && localBound.StockForce(1,1000,5000)>=expectedOwn-1e-3,"actual local origins retain own-concrete force on both unequal spans");
        Check(localReserve.FutureBays(0)==1,"the stronger force reserve does not inflate bay ranking");
    }
    static void SignedAndMargins()
    {
        Near(TransferNetworkOptimizer.ReceiverAggregateLoadBound.MaximumLedgerHalf(-10,3000),0,0,"negative ledger is not deducted from normal slab");
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.MaximumLedgerHalf(10,3000)>15,"positive ledger maximum is guarded upward");
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.LowerForce(1,1e9)==0,"force allowance scales to raw cancellation terms");
        var common=Common();double p=Math.Min(common.LegCapacityKn,2*common.Primary.ShearLimitKn);
        for(int n=2;n<=40;n++)foreach(double left in new[]{-n*p,-n*p/2,0,n*p,Math.BitIncrement(n*p)})
        foreach(double right in new[]{-n*p,0,n*p,Math.BitIncrement(n*p)})
        {
            if(!CrossBeamSupportOptimizer.PrimarySupportCountFits(common,n,left) || !CrossBeamSupportOptimizer.PrimarySupportCountFits(common,n,right))continue;
            double sum=left+right,force=TransferNetworkOptimizer.ReceiverAggregateLoadBound.LowerForce(Math.Max(0,sum),Math.Abs(left)+Math.Abs(right));
            Check(CrossBeamSupportOptimizer.PrimarySupportCountFloor(common,force/2)<=n,"half aggregate cannot reject two signed lines passing the old exact count predicate, including ULP boundaries");
        }
        var network=Network();network.Bays[0].WidthMm=1500;network.Beams[0].WidthMm=200;network.Beams[1].WidthMm=1000;
        Check(!TransferNetworkOptimizer.ReceiverAggregateLoadBound.PrimaryAxesCertified(common,network,network.CrossingBeams[0]),"a negative receiving aggregate in any permitted width/offset family invalidates the certificate");
        var response=LoadedBeamAnalysis.AnalyzeDenseReference(1000,new[]{650.0,750},new[]{new BeamDistributedLoad{StartMm=0,EndMm=1000,KnPerM=.001}},null,TowerOptimizer.PairEI/2,270);
        Check(response.Reactions[1]+.001*.25<0 && response.Reactions.Sum()>0,"independent FE confirms conservation alone permits a negative crossing receiving total");
        network=Network();network.Bays[0].WidthMm=4500;
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.PrimaryAxesCertified(common,network,network.CrossingBeams[0]),"all49 primary axis/offset combinations certify a well-separated bay");
        var map=network.CrossSlabLayout(6000,6000,0);map.Edges.Clear();map.Edges.Add(new SlabEdgeReaction{Crossing=0,PositionMm=2675,WidthMm=-10});
        Check(Bound(common,network)!=null,"a signed negative ledger with positive combined concrete load is supported");
        map.Edges.Clear();map.Edges.Add(new SlabEdgeReaction{Crossing=0,PositionMm=2675,WidthMm=-1e9});
        Check(Bound(common,network)==null,"negative combined crossing force falls back instead of dropping a negative contribution");
    }
    static void ScopeAndSelfWeight()
    {
        var common=Common();var network=Network();var baseline=Bound(common,network);double h20=baseline.StockForce(0,0,2000);
        common.PrimaryMaterial=PrimaryBeamMaterial.WS10;var steel=Bound(common,network);
        double weight=2*common.Primary.SelfWeightKnM(2000)*2;
        Near(steel.StockForce(0,0,2000)-h20,weight,1e-5,"both WS10 catalogue self weights remain in the aggregate");
        Check(steel.MinimumLegs(0,new[]{new[]{2025.0,3975}})==0,"unsupported WS10 stock is unknown and preserves ordinary validation");
        foreach(int mode in Enumerable.Range(0,8))
        {
            common=Common();network=Network();var geometry=new TransferredSlabOptimizer.GeometryCache();bool carried=true;
            switch(mode)
            {
                case 0:carried=false;break;
                case 1:common.FullLoadingOnly=false;break;
                case 2:common.CoordinateCrossingLayouts=false;break;
                case 3:common.PruneNetworkBounds=false;break;
                case 4:common.OptimizeCrossingSearch=false;break;
                case 5:geometry.Fixed=Array.Empty<TransferredSlabOptimizer.Geometry>();break;
                case 6:network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.LegacySlabRows;break;
                case 7:network.PlywoodSheetLengthMm=0;break;
            }
            Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,6000,carried,geometry)==null,"unsupported, excluded, disabled or fixed domain retains topology, mode"+mode);
        }
        common=Common();common.Trace=new BeamSearchTrace("aggregate deadline",searchTimeLimit:TimeSpan.FromSeconds(30));
        Check(Bound(common,Network())==null,"a deadline preserves the previous path");
        common=Common();using var cancelled=new System.Threading.CancellationTokenSource();
        common.Trace=new BeamSearchTrace("aggregate cancellation",cancelled.Token);cancelled.Cancel();bool propagated=false;
        try{Bound(common,Network());}catch(OperationCanceledException){propagated=true;}
        Check(propagated,"input-only preparation never swallows cancellation");
    }
    static void ProfileClipping()
    {
        var common=Common();var network=Network();network.Bays[0].ThicknessMm=1e9;
        var plan=EngineerPrimaryPlan.Create(common,network,0);var zones=plan.WithoutCrossingLoads();
        var force=typeof(TransferNetworkOptimizer.ReceiverAggregateLoadBound).GetMethod("Force",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        foreach(double delta in new[]{-2e-7,-1e-7,-5e-8,0,5e-8,1e-7,2e-7})foreach(double start in new[]{0.0,675+delta,2675-delta,3325-delta})
        {
            double length=start==2675-delta ? 650+2*delta : 2000;
            double expected=Enumerable.Range(0,2).Sum(line=>plan.Loads(start,length,line,active:Array.Empty<int>()).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000));
            double actual=(double)force.Invoke(null,new object[]{zones,start,length,0.0});
            Near(actual,expected,1e-8+Math.Abs(expected)*1e-12,"hoisted profiles preserve source clipping around tiny normal-zone slivers");
        }
    }
    internal static void Run(){checks=0;ForceAndSuffix();SignedAndMargins();ScopeAndSelfWeight();ProfileClipping();Console.WriteLine("PASS receiver aggregate reserve: "+checks+" assertions");}
    internal static void NetworkParity()
    {
        var common=Common();common.LengthMm=4000;common.StockLengthsMm=new[]{2000.0,4000};
        var network=Network();network.ReferenceLengthMm=4000;network.Bays[0].WidthMm=2500;network.Bays[0].ThicknessMm=50;
        foreach(var beam in network.Beams){beam.WidthMm=1000;beam.DepthMm=1800;beam.BottomElevationMm=0;}
        network.CrossingBeams[0].PositionMm=2000;network.CrossingBeams[0].WidthMm=400;network.CrossingBeams[0].DepthMm=200;
        network.CrossingBeams[0].BottomElevationMm=0;network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;
        common.Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true);
        var bound=Bound(common,network,4000);
        Check(bound!=null && bound.MinimumLegs(0,new[]{new[]{4000.0}})>4,"the native parity fixture activates a stronger physical reserve");
        var unpruned=common.CopyForSearch();unpruned.PruneNetworkBounds=false;var expected=TransferNetworkOptimizer.Optimize(unpruned,network);
        common.PruneNetworkBounds=true;var actual=TransferNetworkOptimizer.Optimize(common,network);
        string Signature(TransferNetworkResult value)=>string.Join("|",value.Beams.Select(b=>b.LegCount+":"+b.WidthMm+":"+b.TransverseOffsetMm+":"+string.Join(",",b.PositionsMm.Select(x=>x.ToString("R")))+":"+string.Join(",",b.StockPlan.Pieces.Select(p=>p.StockLengthMm))))
            +"|"+string.Join(";",value.CrossSupports.Select(c=>c.Arrangement+":"+c.UsesPrimaryBearings+":"+(c.Towers?.LegCount??0)));
        Check(Signature(actual)==Signature(expected),"native prune ON/OFF preserves selected stock, supports, widths and crossing route");
        Check(actual.Beams.SelectMany(b=>b.Transfer.PrimaryCases).SelectMany(c=>c).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.Max()<=common.LegCapacityKn+1e-8),"native parity retains every full primary strength and individual leg check");
        Console.WriteLine("PASS native aggregate reserve ON/OFF network parity: "+actual.LegCount+" legs, "+actual.CrossSupports.Length+" crossing");
    }
}
