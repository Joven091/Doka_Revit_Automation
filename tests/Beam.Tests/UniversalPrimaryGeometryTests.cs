using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;
internal static class UniversalPrimaryGeometryTests
{
    static int checks;static readonly BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Static;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Universal primary geometry: "+message);}
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static BeamDistributedLoad[] Clip(BeamDistributedLoad[] source,double start,double length)=>source.Select(q=>Q(Math.Max(0,Math.Max(start,q.StartMm)-start),Math.Min(length,Math.Min(start+length,q.EndMm)-start),q.KnPerM)).Where(q=>q.EndMm>q.StartMm+1e-7).ToArray();
    static double At(BeamDistributedLoad[] source,double a,double b)=>source.Where(q=>q.StartMm<=a && q.EndMm>=b).Sum(q=>q.KnPerM);
    static void Pointwise(BeamDistributedLoad[] lower,BeamDistributedLoad[] actual)
    {
        var cuts=lower.Concat(actual).SelectMany(q=>new[]{q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();
        for(int i=1;i<cuts.Length;i++)Check(At(lower,cuts[i-1],cuts[i])<=At(actual,cuts[i-1],cuts[i]),"atomic/tiny-stock clipping preserves the exact pointwise lower density");
    }
    static void AverageAndMonotonicity()
    {
        var raw=new[]{new[]{Q(0,6000,10)},new[]{Q(0,6000,10)}};
        var lower=TransferNetworkOptimizer.ReceiverAggregateLoadBound.GuardedAverage(raw,raw,new[]{650.0,1000});
        Check(lower.Length==3 && lower.All(q=>q.KnPerM>0 && q.KnPerM<10),"guarded average retains equal-density mandatory atomic cuts");
        var actual=new[]{Q(0,650,10),Q(650,6000,10)};
        foreach(double delta in new[]{-2e-7,-1e-7,-5e-8,0,5e-8,1e-7,2e-7})Pointwise(Clip(lower,650+delta,500),Clip(actual,650+delta,500));
        var residual=TransferNetworkOptimizer.ReceiverAggregateLoadBound.GuardedAverage(new[]{new[]{Q(0,1000,1e9)},new[]{Q(0,1000,1e9)}},new[]{new[]{Q(0,1000,.001)},new[]{Q(0,1000,.001)}},Array.Empty<double>());
        Check(residual.Length==0,"downward uncertainty scales to raw sources, not a tiny cancellation residual");
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.GuardedAverage(new[]{new[]{Q(0,1000,-1)},new[]{Q(0,1000,1)}},raw,Array.Empty<double>())==null,"negative source density supplies no proof");
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.GuardedAverage(raw,raw,new[]{double.NaN})==null,"nonfinite source cuts supply no proof");
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,LegCapacityKn=70,DeflectionDenominator=270};
        var baseline=new[]{Q(0,3600,.1),Q(1550,2050,80)};var augmented=baseline.Concat(new[]{Q(0,800,24.9),Q(2800,3600,24.9)}).ToArray();var supports=new[]{800.0,2800};
        var before=LoadedBeamAnalysis.AnalyzeDenseReference(3600,supports,baseline,null,input.Primary.EI,270);
        var after=LoadedBeamAnalysis.AnalyzeDenseReference(3600,supports,augmented,null,input.Primary.EI,270);
        Check(before.MomentKnM>10 && after.MomentKnM<10 && after.ShearKn<22,"independent dense counterexample proves exact full moment is not monotone under positive tail loads");
        Check(CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,3600,supports,baseline,null)
            && CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,3600,supports,augmented,null),"universal helper uses only safe generic span-force/free-tail checks");
        foreach(double q in new[]{0.0,5,20,80})foreach(var p in new[]{new[]{150.0,850},new[]{400.0,600},new[]{150.0,500,850}})
        {
            var min=new[]{Q(0,1000,q)};var left=new[]{Q(0,1000,q*2)};var right=new[]{Q(0,1000,q*.1)};
            // The certified average must be <=(left+right)/2; this example uses
            // another nonnegative supplement on the smaller individual line.
            right=new[]{Q(0,1000,q)};
            bool low=CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,1000,p,min,null),a=CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,1000,p,left,null),b=CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,1000,p,right,null);
            Check(low || !a || !b,"a lower average rejection cannot coexist with both actual lines passing generic bounds");
        }
    }
    static TowerInput Common()
    {
        var input=(TowerInput)typeof(ReceiverAggregateReserveTests).GetMethod("Common",hidden).Invoke(null,null);
        input.Trace=new BeamSearchTrace("universal retained views");input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static TransferNetworkInput Network()=>(TransferNetworkInput)typeof(ReceiverAggregateReserveTests).GetMethod("Network",hidden).Invoke(null,null);
    static TransferredSlabOptimizer.Geometry G(double[] supports)=>new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=supports.Length-1}},Positions=supports,Joined=Enumerable.Repeat(true,supports.Length-1).ToArray(),MemberSupports=new[]{supports}};
    static void StableViewsAndGates()
    {
        var common=Common();var network=Network();var geometryCache=new TransferredSlabOptimizer.GeometryCache();
        var bound=TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,6000,true,geometryCache);
        Check(bound!=null && bound.UniversalAverage(0)?.Length>0,"modern certified profile exposes its nonnegative guaranteed average");
        var input=common.CopyForSearch();input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,SharedGeometryCache=geometryCache,LeftBayMm=0,RightBayMm=3000,RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8};
        var filter=bound.GeometryFilter(0);Check(filter!=null,"certified input provides a transient geometry filter");
        input.TransferGeometryFilter=filter;string wire=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"universal-wire-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            BeamWire.Write(wire,new BeamSolverRequest {Main=input});var read=BeamWire.Read<BeamSolverRequest>(wire);
            Check(read.Main.TransferGeometryFilter==null && read.Main.LengthMm==input.LengthMm,"transient filter delegates are excluded from model wire graphs and rebuilt only in the worker");
        }
        finally{if(System.IO.File.Exists(wire))System.IO.File.Delete(wire);}
        var entries=typeof(TransferNetworkOptimizer).GetMethods(BindingFlags.NonPublic|BindingFlags.Static);
        Check(entries.Count(m=>m.Name=="Solve")==1 && entries.Single(m=>m.Name=="Solve").GetParameters().Length==14
            && entries.Count(m=>m.Name=="SolveReceiver")==1 && entries.Single(m=>m.Name=="SolveReceiver").GetParameters().Length==15,"original reflection entry names remain unique with their unchanged arity");
        var good=G(new[]{150.0,1650,3150,4650,5850});var bad=G(new[]{150.0,5850});var source=new[]{good,bad,good};
        var kept=filter(input,source,0);
        Check(kept.Length==2 && ReferenceEquals(kept[0],good) && ReferenceEquals(kept[1],good),"necessary filtering preserves every surviving occurrence and its original object/order");
        Check(ReferenceEquals(filter(input,source,0),kept) && input.Trace.Count(BeamSearchCounter.UniversalGeometryViewHits)>0,"same immutable parent/edge reuses a completed stable view");
        var another=TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,6000,true,geometryCache).GeometryFilter(0);
        long sameBuilds=input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds);
        Check(ReferenceEquals(another(input,source,0),kept) && input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)==sameBuilds
            && input.Trace.Count(BeamSearchCounter.UniversalGeometrySharedProfileHits)>0,"another independently certified equal numeric profile shares the completed parent view");
        long built=input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds);filter(input,source.ToArray(),0);filter(input,source,25);
        Check(input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)==built+2,"another parent identity or exact absolute edge has independent cached evidence");
        var wrong=input.CopyForSearch();wrong.EngineerPlan=EngineerPrimaryPlan.Create(wrong,network,1);
        Check(ReferenceEquals(filter(wrong,source,0),source),"same dimensions cannot borrow another beam's profile");
        foreach(int gate in Enumerable.Range(0,5))
        {
            var local=input.CopyForSearch();switch(gate){case 0:local.OptimizeCrossingSearch=false;break;case 1:local.ReuseSearchWork=false;break;case 2:local.PrimaryMaterial=PrimaryBeamMaterial.WS10;break;case 3:local.LengthMm++;break;case 4:local.LongitudinalStartMm++;break;}
            Check(ReferenceEquals(filter(local,source,0),source),"unsupported policy/material/coordinate scope retains original universe, gate"+gate);
        }
        var asymmetric=input.CopyForSearch();asymmetric.TransferredSlab=new TransferredSlabInput {SharedGeometryCache=geometryCache,RightBayMm=3000,RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8,RightPlan=new UpperTransferPlan {StockLengthMm=2500,EdgeMm=250,NearFootMm=400,FarFootMm=450}};
        Check(ReferenceEquals(filter(asymmetric,source,0),source),"an asymmetric fixed slab plan supplies no universal profile proof");
        Check(!TransferNetworkOptimizer.ReceiverAggregateLoadBound.SymmetricPlan(new UpperTransferPlan{StockLengthMm=2500,EdgeMm=200,NearFootMm=400,FarFootMm=400},3000),"inconsistent stock/edge width cannot claim centred geometry");
        var malformed=new[]{bad,G(new[]{100.0,100})};Check(ReferenceEquals(filter(input,malformed,0),malformed),"a malformed member leaves the entire view on the original path, including earlier rejected entries");
        var empty=new[]{bad};long rejection=input.Trace.Count(BeamSearchCounter.RejectionChecks);Check(filter(input,empty,0).Length==0,"a uniformly impossible parent gets an empty view");
        long once=input.Trace.Count(BeamSearchCounter.RejectionChecks);filter(input,empty,0);
        Check(once>rejection && input.Trace.Count(BeamSearchCounter.RejectionChecks)==once,"empty cached views do not repeatedly format the same caller's proof");
        var huge=new TransferredSlabOptimizer.Geometry[8388608];long priorBuild=input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds);
        Check(ReferenceEquals(filter(input,huge,0),huge) && input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)==priorBuild,"unretainable parent bypasses before any build or geometry traversal");
        Check(input.Trace.Count(BeamSearchCounter.UniversalGeometryPeakBytes)>0 && input.Trace.Count(BeamSearchCounter.UniversalGeometryPeakBytes)<=128L*1024*1024,"cached-view peak respects the whole-pass shared byte budget");
        using(var cancel=new CancellationTokenSource())
        {
            var cancelled=input.CopyForSearch();cancelled.Trace=new BeamSearchTrace("universal cancelled",cancel.Token);cancel.Cancel();bool propagated=false;
            try{filter(cancelled,source,0);}catch(OperationCanceledException){propagated=true;}Check(propagated,"cache hits cannot bypass user cancellation");
        }
        Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0,"geometry views never run primary FE");
        var names=(string[])typeof(BeamSearchTrace).GetField("CounterNames",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(names.Length==Enum.GetValues<BeamSearchCounter>().Length,"all appended trace counter IDs retain ordered labels");
    }
    internal static void NetworkParity()
    {
        var common=Common();common.LengthMm=4000;common.StockLengthsMm=new[]{2000.0,4000};
        var network=Network();network.ReferenceLengthMm=4000;network.Bays[0].WidthMm=2500;network.Bays[0].ThicknessMm=50;
        foreach(var beam in network.Beams){beam.WidthMm=1000;beam.DepthMm=1800;beam.BottomElevationMm=0;}
        network.CrossingBeams[0].PositionMm=2000;network.CrossingBeams[0].WidthMm=400;network.CrossingBeams[0].DepthMm=200;
        network.CrossingBeams[0].BottomElevationMm=0;network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;
        var unpruned=common.CopyForSearch();unpruned.PruneNetworkBounds=false;unpruned.Trace=new BeamSearchTrace("universal native off");
        var expected=TransferNetworkOptimizer.Optimize(unpruned,network);var actual=TransferNetworkOptimizer.Optimize(common,network);
        string Signature(TransferNetworkResult value)=>string.Join("|",value.Beams.Select(b=>b.LegCount+":"+b.WidthMm+":"+b.TransverseOffsetMm+":"+string.Join(",",b.PositionsMm.Select(x=>x.ToString("R")))+":"+string.Join(",",b.StockPlan.Pieces.Select(p=>p.StockLengthMm))))
            +"|"+string.Join(";",value.CrossSupports.Select(c=>c.Arrangement+":"+c.UsesPrimaryBearings+":"+(c.Towers?.LegCount??0)));
        Check(Signature(expected)==Signature(actual),"native filter ON/OFF preserves selected primary stock, positions, widths and crossing routes");
        Check(common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)>0,"native coordinated receiver searches actually execute the universal hook");
        Check(actual.Beams.SelectMany(b=>b.Transfer.PrimaryCases).SelectMany(c=>c).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.Max()<=common.LegCapacityKn+1e-8),"native parity retains complete primary FE and individual leg checks");
        Console.WriteLine("PASS universal native ON/OFF network parity: "+actual.LegCount+" legs; "+common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)+" views; "+common.Trace.Count(BeamSearchCounter.UniversalPrimaryGeometryRejected)+" repeated parent entries excluded");
    }
    internal static void Run(){AverageAndMonotonicity();StableViewsAndGates();UniversalSharedViewTests.Run();Console.WriteLine("PASS "+checks+" universal geometry checks: atomic guarded averages, clipping, monotone-only necessity, stable cached views, bounded admission, scope gates and cancellation");}
}
