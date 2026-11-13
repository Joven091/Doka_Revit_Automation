using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class EarlyUniversalProfileTests
{
    static int checks;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Early universal profile: "+message);}
    static TowerInput Common()=>(TowerInput)typeof(ReceiverAggregateReserveTests).GetMethod("Common",hidden).Invoke(null,null);
    static TransferNetworkInput Network()
    {
        var network=(TransferNetworkInput)typeof(ReceiverAggregateReserveTests).GetMethod("Network",hidden).Invoke(null,null);
        network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;
        network.CrossingBeams[0].BottomElevationMm=-1550;return network;
    }
    static double At(BeamDistributedLoad[] source,double x)=>source.Where(q=>q.StartMm<=x && q.EndMm>x).Sum(q=>q.KnPerM);
    static string Signature(BeamDistributedLoad[] source)=>string.Join(";",source.Select(q=>q.StartMm.ToString("R")+":"+q.EndMm.ToString("R")+":"+q.KnPerM.ToString("R")));
    static BeamDistributedLoad[] Clip(BeamDistributedLoad[] source,double start,double length)=>source.Select(q=>new BeamDistributedLoad {StartMm=Math.Max(0,Math.Max(start,q.StartMm)-start),EndMm=Math.Min(length,Math.Min(start+length,q.EndMm)-start),KnPerM=q.KnPerM}).Where(q=>q.EndMm>q.StartMm+1e-7).ToArray();
    static TransferNetworkOptimizer.ReceiverAggregateLoadBound Bound(TowerInput common,TransferNetworkInput network,TransferredSlabOptimizer.GeometryCache cache=null)
        =>TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,6000,true,cache??new TransferredSlabOptimizer.GeometryCache());
    static TowerInput Receiver(TowerInput common,TransferNetworkInput network,TransferredSlabOptimizer.GeometryCache cache)
    {
        var input=common.CopyForSearch();input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);
        input.DeferCrossingChecks=true;input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,SharedGeometryCache=cache,EngineerNetwork=network,EngineerBeam=0,
            EngineerReceiverCount=3,FixedCoveredLengthMm=6000,RightBayMm=3000,RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8};
        input.Trace=new BeamSearchTrace("early universal profile");return input;
    }
    static TransferredSlabOptimizer.Geometry G(double[] supports)=>new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=supports.Length-1}},
        Positions=supports,Joined=Enumerable.Repeat(true,supports.Length-1).ToArray(),MemberSupports=new[]{supports}};
    static void Coefficient()
    {
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.SharedConcreteCoefficient(599)==0
            && TransferNetworkOptimizer.ReceiverAggregateLoadBound.SharedConcreteCoefficient(double.NaN)==0,"unsupported axis domain supplies no added force");
        foreach(double span in new[]{600.0,PortableTestRuntime.NextUp(600),650,1000,2200,3000,4500,10000})
        {
            double coefficient=TransferNetworkOptimizer.ReceiverAggregateLoadBound.SharedConcreteCoefficient(span),limit=Math.Min(350,span/2-150);
            Check(coefficient>0 && coefficient<=span/2,"coefficient is positive and covered by the direct-primary planning arm");
            foreach(double near in new[]{150.0,(150+limit)/2,limit})foreach(double far in new[]{150.0,(150+limit)/2,limit})
            foreach(double leftTail in new[]{0.0,near/2,near})foreach(double rightTail in new[]{0.0,far/2,far})
            {
                double length=span-leftTail-rightTail,a=near,b=span-far;
                double reaction=length*(leftTail+length/2-a)/(b-a)+rightTail;
                double other=length-reaction+rightTail+leftTail;
                Check(coefficient<=reaction && coefficient<=other,"analytic lower coefficient covers asymmetric axes, stock offsets and both concrete end strips");
            }
        }
    }
    static void ProfilesAndFallbacks()
    {
        var common=Common();var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var bound=Bound(common,network,cache);
        Check(EngineerPrimaryPlan.Eligible(network,network.CrossingBeams[0]) && !CrossBeamSupportOptimizer.PrimaryBearingPossible(common,network,network.CrossingBeams[0]),"fixture exercises the actual added-crossing predicates");
        var weak=bound.UniversalAverage(0);var early=bound.UniversalEarlyAverage(0);
        Check(At(early,3000)>At(weak,3000) && At(early,1000)>At(weak,1000),"early admission restores raw normal slab and reserves mandatory shared concrete");
        Check(early.Any(q=>q.EndMm==2675) && early.Any(q=>q.StartMm==3325),"crossing footprint atomic boundaries remain exact");
        double coefficient=TransferNetworkOptimizer.ReceiverAggregateLoadBound.SharedConcreteCoefficient(3000),own=network.AreaLoad(1750)*.8;
        Check(2*At(early,3000)<=own+network.AreaLoad(900)*coefficient/1000,"density is guarded downward from raw own plus concrete coefficient");
        // Equal added sets use a coverage-independent numeric source envelope.
        var another=TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,5900,true,cache);
        Check(Signature(early)==Signature(another.UniversalEarlyAverage(0)),"equivalent early profiles share exact values across coverage trials");
        var map=network.CrossSlabLayout(6000,6000,0);map.Edges.Clear();map.Edges.Add(new SlabEdgeReaction {Crossing=0,PositionMm=2675,WidthMm=-10});
        var signed=Bound(common,network);Check(signed!=null && Signature(signed.UniversalEarlyAverage(0))==Signature(signed.UniversalAverage(0)),"a signed ledger component excludes the added crossing and keeps the weak physical proof");
        map.Edges.Clear();map.Edges.Add(new SlabEdgeReaction {Crossing=0,PositionMm=2675,WidthMm=double.NaN});
        Check(Bound(common,network)==null,"a nonfinite ledger source supplies no aggregate certificate");
        network=Network();network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;bound=Bound(common,network);
        Check(Signature(bound.UniversalEarlyAverage(0))==Signature(bound.UniversalAverage(0)),"empty added sets retain the weak profile");
    }
    static void NativeIdentityAndCount()
    {
        var common=Common();var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var input=Receiver(common,network,cache);var bound=Bound(common,network,cache);
        Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> preparation=layout=>(g,e)=>true;
        Func<TowerLayout,bool> acceptance=layout=>true;input.TransferReceiverPreparation=preparation;input.TransferLayoutAcceptance=acceptance;
        var filter=bound.GeometryFilterWithNativeChecks(0,preparation,acceptance);var weak=bound.GeometryFilter(0);
        var early=(Func<TowerInput,TransferredSlabOptimizer.Geometry[],double,TransferredSlabOptimizer.Geometry[]>)typeof(TransferNetworkOptimizer.ReceiverAggregateLoadBound)
            .GetMethod("GeometryFilterForAverage",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(bound,new object[]{0,bound.UniversalEarlyAverage(0),"Early shared crossing trial lower load"});
        var source=Enumerable.Range(2,9).Select(n=>G(Enumerable.Range(0,n).Select(k=>150+5700.0*k/(n-1)).ToArray())).ToArray();
        var earlyKept=early(input,source,0);var weakKept=weak(input,source,0);
        Check(earlyKept.Length<=weakKept.Length,"stronger nonnegative average cannot add native geometry");
        for(int count=3;count<=8;count++)
        {
            input.TransferredSlab.EngineerReceiverCount=count;
            Check(ReferenceEquals(filter(input,source,0),count<8 ? earlyKept : weakKept),"native phase selection is exact for refinement "+count);
        }
        input.TransferredSlab.EngineerReceiverCount=3;
        foreach(int gate in Enumerable.Range(0,7))
        {
            var other=input.CopyForSearch();other.TransferredSlab=input.TransferredSlab.Copy();
            switch(gate){case 0:other.TransferReceiverPreparation=layout=>(g,e)=>true;break;case 1:other.TransferLayoutAcceptance=layout=>true;break;
                case 2:other.ReuseTransversePreparations=false;break;case 3:other.PrepareReceiverChecks=false;break;case 4:other.DeferTransferLayout=false;break;
                case 5:other.DeferCrossingChecks=false;break;case 6:other.TransferredSlab.EngineerReceiverCount=2;break;}
            Check(ReferenceEquals(filter(other,source,0),weakKept),"unknown callbacks or unsupported preparation scope use weak physical loads, gate "+gate);
        }
        Check(ReferenceEquals(bound.GeometryFilterWithNativeChecks(0,null,acceptance)(input,source,0),weakKept),"missing native preparation cannot enable admission loads");
        Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0,"phase selection and masks perform no primary FE");
        Console.WriteLine("Early native family sample: "+source.Length+" original; "+weakKept.Length+" weak survivors; "+earlyKept.Length+" early survivors");
    }
    static void ActualSharedAndPrimaryArms()
    {
        var common=Common();var network=Network();network.Bays[0].ThicknessMm=50;network.CrossingBeams[0].DepthMm=400;common.TransferredSlab=new TransferredSlabInput {FixedCoveredLengthMm=6000};
        var basis=new TransferMember {StartMm=-1000,LengthMm=2000,SupportsMm=new[]{238.0,1762},Uniform=new[]{new BeamDistributedLoad {StartMm=600,EndMm=1400,KnPerM=network.AreaLoad(1750)}},
            Points=new[]{new BeamPointLoad {PositionMm=1900,Kn=network.AreaLoad(network.Bays[0].ThicknessMm)*1.5}}};
        var plan=EngineerPrimaryPlan.Create(common,network,0).WithGeometry(common,basis,new[]{"Right slab H20"});
        double[] rows=Enumerable.Range(0,8).Select(i=>2675+40+(650-80)*i/7.0).ToArray();
        var lower=rows.Select(x=>new TransferMember {StartMm=basis.StartMm,LengthMm=basis.LengthMm,SupportsMm=basis.SupportsMm,
            Uniform=basis.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*.1}).ToArray(),
            Points=basis.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*.1}).ToArray()}).ToArray();
        foreach(var member in lower)member.Analysis=LoadedBeamAnalysis.Analyze(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,270);
        var probe=new TowerLayout {WidthMm=1524,LengthMm=6000,PositionsMm=Array.Empty<double>(),Joined=Array.Empty<bool>(),StockPlan=new TowerStockPlan {Pieces=Array.Empty<TowerStockPiece>()},
            Transfer=new TransferredSlabResult {PrimaryPlan=plan,RowsMm=rows,LowerStockStartMm=-1000,LowerStockLengthMm=2000,LowerCases=new[]{lower},CaseNames=new[]{"Full"},EngineerReceiverCount=3}};
        common.EngineerPlan=plan;common.TransferredSlab.EngineerNetwork=network;common.TransferredSlab.EngineerBeam=0;
        var mirror=new TransferNetworkResult {Input=network,Beams=new[]{probe,probe},Bays=new[]{new UpperTransferPlan()},BeamCentresMm=new[]{0.0,3800},SlabCoveredLengthMm=6000};
        var sharedMethod=typeof(CrossBeamSupportOptimizer).GetMethod("Shared",hidden);
        var bound=Bound(common,network);var average=bound.UniversalEarlyAverage(0);int shared=0;
        foreach(int count in Enumerable.Range(3,5))
        {
            probe.Transfer.EngineerReceiverCount=count;var arguments=new object[]{common,mirror,0,null};
            var support=(CrossBeamSupportResult)sharedMethod.Invoke(null,arguments);Check(support!=null,"native Shared stock/axis/ledger detail is available at count "+count+": "+arguments[3]);shared++;
            var own=support.Bearings.Where(b=>b.Beam==0).Select(b=>b.Copy()).ToArray();
            var local=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[]{network.Beams[0]}},Beams=new[]{probe},BeamCentresMm=new[]{0.0}};
            var resolved=plan.Resolve(common,local,0,new[]{new CrossBeamSupportResult {Input=network.CrossingBeams[0],Bearings=own,UsesSpreader=true}});
            var cuts=average.Concat(resolved.Lines[0]).Concat(resolved.Lines[1]).SelectMany(q=>new[]{q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();
            for(int i=1;i<cuts.Length;i++){double x=(cuts[i-1]+cuts[i])/2;Check(At(average,x)<=(At(resolved.Lines[0],x)+At(resolved.Lines[1],x))/2,"actual native Shared Resolve retains the guarded average on both signed primary lines");}
            var planning=plan.Resolve(common,local,0,Array.Empty<CrossBeamSupportResult>());
            foreach(double x in new[]{1000.0,3000,5000})Check(At(average,x)<=(At(planning.Lines[0],x)+At(planning.Lines[1],x))/2,"primary OR arm's unresolved eligible crossing keeps half-span concrete and raw normal ledger");
            foreach(double cut in new[]{2675.0,3325})foreach(double delta in new[]{-2e-7,-1e-7,-5e-8,0,5e-8,1e-7,2e-7})
            {
                double start=cut+delta;var lo=Clip(average,start,500);var left=resolved.Loads(start,500,0);var right=resolved.Loads(start,500,1);
                var clippedCuts=lo.Concat(left).Concat(right).SelectMany(q=>new[]{q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();
                for(int i=1;i<clippedCuts.Length;i++){double x=(clippedCuts[i-1]+clippedCuts[i])/2;Check(At(lo,x)<=(At(left,x)+At(right,x))/2,"tiny stock-window clipping preserves the early Shared admission lower profile");}
            }
            var prepared=CrossBeamSupportOptimizer.PrepareReceiver(common,network,0,probe);Check(prepared!=null && !ReferenceEquals(prepared,CrossBeamSupportOptimizer.FailedReceiver),"native prepared Shared arm reaches its primary callback at count "+count);
            int accepted=0;
            foreach(int n in Enumerable.Range(2,9))
            {
                var geometry=G(Enumerable.Range(0,n).Select(k=>150+5700.0*k/(n-1)).ToArray());
                bool necessary=CrossBeamSupportOptimizer.GenericPrimaryForcesFit(common,6000,geometry.MemberSupports[0],average,null),full=prepared(geometry,0);
                Check(necessary || !full,"early geometry rejection retains the native complete prepared callback outcome");if(full)accepted++;
            }
            Check(accepted>0,"native prepared retention includes actual passing primary geometries");
        }
        Check(shared==5,"all early refinement counts exercised actual Shared production mechanics");
        var direct=new TransferCrossingBeamRow {BayNumber=1,PositionMm=1000,WidthMm=400,DepthMm=100,BottomElevationMm=-1650,SupportMode=CrossBeamSupportMode.Automatic};
        network.CrossingBeams=network.CrossingBeams.Concat(new[]{direct}).ToArray();network.CrossingSpacingsMm=new[]{305.0,305};
        Check(!EngineerPrimaryPlan.Eligible(network,direct) && CrossBeamSupportOptimizer.PrimaryBearingPossible(common,network,direct),"mixed fixture separately enables the direct-primary OR arm");
        var directSupport=CrossBeamSupportOptimizer.PrimaryBearing(common,mirror,1,out string failure);
        Check(directSupport!=null,"native direct-primary support is available in the mixed graph: "+failure);
        var ownDirect=directSupport.Bearings.Where(b=>b.Beam==0).Select(b=>b.Copy()).ToArray();
        var mixedPlan=EngineerPrimaryPlan.Create(common,network,0).WithGeometry(common,basis,new[]{"Right slab H20"});probe.Transfer.PrimaryPlan=mixedPlan;
        var directLocal=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[]{network.Beams[0]}},Beams=new[]{probe},BeamCentresMm=new[]{0.0}};
        var directResolved=mixedPlan.Resolve(common,directLocal,0,new[]{new CrossBeamSupportResult {Input=direct,Bearings=ownDirect}});
        var mixedAverage=Bound(common,network).UniversalEarlyAverage(0);
        foreach(double x in new[]{500.0,1000,2000,3000,5000})Check(At(mixedAverage,x)<=(At(directResolved.Lines[0],x)+At(directResolved.Lines[1],x))/2,"nonempty native primary OR arm retains the added eligible crossing planning contribution");
    }
    internal static void NativeParity()
    {
        var common=Common();common.LengthMm=2450;common.StockLengthsMm=new[]{1250.0,1800,2450,2900};common.MaximumConcreteEndOverhangMm=175;common.AllowSeparateSlabFallback=false;
        var network=new TransferNetworkInput {ReferenceLengthMm=2450,PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
            BeamSpacingsMm=new[]{305.0,305},SlabSpacingsMm=new[]{488.0},CrossingSpacingsMm=new[]{350.0},UseCrossingSlabLedgers=true,
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=new[]{new TransferBayRow {WidthMm=2200,ThicknessMm=200}},
            CrossingBeams=new[]{new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600,SupportMode=CrossBeamSupportMode.Automatic}}};
        Check(EngineerPrimaryPlan.Eligible(network,network.CrossingBeams[0]) && !CrossBeamSupportOptimizer.PrimaryBearingPossible(common,network,network.CrossingBeams[0]),"native network parity really uses the stronger eligible phase");
        var off=common.CopyForSearch();off.PruneNetworkBounds=false;var expected=TransferNetworkOptimizer.Optimize(off,network);
        common.Trace=new BeamSearchTrace("early native parity");var actual=TransferNetworkOptimizer.Optimize(common,network);
        Check(ControlledPerformanceTests.Fingerprint(actual)==ControlledPerformanceTests.Fingerprint(expected),"early native pruning ON/OFF preserves exact complete selection and forces");
        ControlledPerformanceTests.Validate(common,actual);
        Check(common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)>0,"native receiver searches run the post-installed exact callback filter");
        Check(actual.Beams.Any(b=>b.Transfer.EngineerReceiverCount<8),"native parity includes a selected early shared refinement");
        Console.WriteLine("PASS early native ON/OFF complete parity: "+actual.LegCount+" legs; "+common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)+" views; "+common.Trace.Count(BeamSearchCounter.UniversalPrimaryGeometryRejected)+" entries excluded");
    }
    internal static void Run(){Coefficient();ProfilesAndFallbacks();NativeIdentityAndCount();ActualSharedAndPrimaryArms();Console.WriteLine("PASS "+checks+" early universal profile checks");}
}
