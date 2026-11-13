using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using EngineeringDashboard;
using Bound=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;
internal static class KnownUpperNormalWitnessTests
{
    static int checks;static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Known upper normal witness: "+text);}
    static TowerInput Common()=>(TowerInput)typeof(ExteriorNormalWitnessTests).GetMethod("Common",hidden).Invoke(null,null);
    static TransferNetworkInput Network()=>(TransferNetworkInput)typeof(ExteriorNormalWitnessTests).GetMethod("Network",hidden).Invoke(null,null);
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static UpperTransferPlan Plan()=>new UpperTransferPlan {StockLengthMm=5000,EdgeMm=200,NearFootMm=900,FarFootMm=900};
    static TransferredSlabOptimizer.Geometry G(double[] p)=>new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=p.Length-1}},Positions=p,Joined=Enumerable.Repeat(true,p.Length-1).ToArray(),MemberSupports=new[]{p}};
    static void DenseAndSnapshot()
    {
        var common=Common();var network=Network();var bound=Bound.TryCreate(common,network,6000,true,new TransferredSlabOptimizer.GeometryCache());
        foreach(int beam in new[]{0,1})
        {
            var original=bound.ExteriorNormalWitness(beam);var plan=Plan();var known=original.ForKnownUpper(plan);
            double actualH=(plan.StockLengthMm*plan.NearFootMm+2*plan.EdgeMm*100)/5400;
            Check(known!=null && known.CentroidLower<=actualH && known.CentroidLower>original.CentroidLower,"known valid centroid strengthens the universal bound conservatively");
            Check(known.Cells.All(c=>c.KnPerM>=original.Cells.Min(q=>q.KnPerM)),"known-plan cells retain the existing universal witness");
            foreach(var setting in new[]{Tuple.Create(600.0,0.0),Tuple.Create(1000.0,0.0),Tuple.Create(1500.0,0.0),Tuple.Create(1524.0,0.0),Tuple.Create(2000.0,0.0),Tuple.Create(1000.0,-25.0),Tuple.Create(1000.0,25.0)})
            {
                double width=network.Beams[beam].WidthMm,area=network.AreaLoad(200),ownArea=network.AreaLoad(1750);
                var upper=LoadedBeamAnalysis.AnalyzeDenseReference(plan.StockLengthMm,new[]{plan.NearFootMm-plan.EdgeMm,5400-plan.FarFootMm-plan.EdgeMm},new[]{Q(0,plan.StockLengthMm,area)},null,TowerOptimizer.PairEI/2,270);
                double factor=known.SlabMinimum/(upper.Reactions[0]+area*plan.EdgeMm/1000),length=2*(width/2+plan.NearFootMm+1000),start=-length/2,sign=beam==0 ? 1 : -1;
                var lower=LoadedBeamAnalysis.AnalyzeDenseReference(length,new[]{setting.Item2-setting.Item1/2-start,setting.Item2+setting.Item1/2-start},new[]{Q(-width/2-start,width/2-start,ownArea)},
                    new[]{new BeamPointLoad {PositionMm=sign*(width/2+plan.NearFootMm)-start,Kn=upper.Reactions[0]*factor},new BeamPointLoad {PositionMm=sign*(width/2+100)-start,Kn=area*plan.EdgeMm/1000*factor}},TowerOptimizer.PairEI/2,270);
                Check(known.ForSetting(setting.Item1,setting.Item2).Cells[0].KnPerM<=lower.Reactions[beam==0 ? 1 : 0],"independent dense reactions dominate the exact known-centroid fixed-setting witness");
            }
            Check(original.ForKnownUpper(null)==null && original.ForKnownUpper(new UpperTransferPlan {StockLengthMm=5000,EdgeMm=200,NearFootMm=250,FarFootMm=250})==null,"missing or unsupported plan geometry has no known-centroid certificate");
        }
    }
    static void GuardedFallbacks()
    {
        var common=Common();common.Trace=new BeamSearchTrace("known upper normal guards");var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var bound=Bound.TryCreate(common,network,6000,true,cache);var plan=Plan();
        var input=common.CopyForSearch();input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.DeferCrossingChecks=true;
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,SharedGeometryCache=cache,EngineerNetwork=network,EngineerBeam=0,EngineerReceiverCount=8,FixedTowerWidthMm=1000,
            BeamWidthMm=800,BeamCentreOffsetMm=0,RightBayMm=5400,RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8,RightPlan=plan};
        Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> prepare=l=>(g,e)=>true;Func<TowerLayout,bool> accept=l=>true;input.TransferReceiverPreparation=prepare;input.TransferLayoutAcceptance=accept;
        var universal=bound.GeometryFilterForNativeReceiver(0,prepare,accept,1000,0);var known=bound.GeometryFilterForKnownUpper(0,prepare,accept,1000,0,plan);
        var source=Enumerable.Range(2,12).Select(n=>G(Enumerable.Range(0,n).Select(k=>150+5700.0*k/(n-1)).ToArray())).ToArray();
        var baseline=universal(input,source,0);var specific=known(input,source,0);
        Check(specific.Length<baseline.Length && specific.All(g=>baseline.Any(b=>ReferenceEquals(g,b))),"known upper geometry adds real normal-span exclusions without adding candidates");
        long builds=input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds);var equalPlan=plan.Reverse();
        Check(ReferenceEquals(bound.GeometryFilterForKnownUpper(0,prepare,accept,1000,0,equalPlan)(input,source,0),specific) && input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)==builds,"numeric-equivalent plans share evidence without a plan-reference key");
        var snapshot=bound.ExteriorNormalWitness(0).ForKnownUpper(plan).ForSetting(1000,0);Check(snapshot.Allows(input),"captured four-field plan and header are eligible");
        plan.NearFootMm=plan.FarFootMm=350;
        Check(!snapshot.Allows(input) && ReferenceEquals(known(input,source,0),universal(input,source,0)),"changed valid plan fields select the universal-centroid fallback signature");
        input.TransferredSlab.RightPlan=null;
        Check(ReferenceEquals(known(input,source,0),universal(input,source,0)),"a missing current plan uses the universal fallback");
        input.TransferredSlab.RightPlan=plan;
        Check(ReferenceEquals(bound.GeometryFilterForKnownUpper(0,prepare,accept,1000,0,null)(input,source,0),universal(input,source,0)),"a missing captured plan preserves the legacy wrapper's result");
        Check(input.TransferredSlab.FixedTowerWidthMm==1000 && input.ReceiverTransverseOffsetMm==0,"witness selection never mutates width or offset constraints");
    }
    static void ActualSourceData()
    {
        var common=ReportedScenarioTests.DiagnosticPart(3,1).Item2.CreateTowerInput();common.CoordinateCrossingLayouts=true;var network=common.Network;double coverage=network.SharedEnd(common.LengthMm)-network.SharedStart;
        var bound=Bound.TryCreate(common,network,coverage,true,new TransferredSlabOptimizer.GeometryCache());var rows=new List<object>();
        foreach(int beam in new[]{0,network.Beams.Length-1})
        {
            int bay=beam==0 ? 0 : network.Bays.Length-1;double stock=beam==0 ? 2350 : 3800,edge=(network.Bays[bay].WidthMm-stock)/2,foot=edge+(beam==0 ? 150 : 650);
            var plan=new UpperTransferPlan {StockLengthMm=stock,EdgeMm=edge,NearFootMm=foot,FarFootMm=foot};var original=bound.ExteriorNormalWitness(beam);var known=original.ForKnownUpper(plan);
            Check(known!=null,"actual selected source plan supplies a known-centroid witness");
            if(beam==network.Beams.Length-1)
                Check(original.Cells.Min(c=>c.KnPerM)<44/1.524 && known.Cells.Min(c=>c.KnPerM)>44/1.524,"far exterior known plan crosses the 1524 mm H20 span-shear threshold that the universal centroid misses");
            rows.Add(new {Scenario=3,Beam=beam+1,Coverage=coverage,Stock=stock,Edge=edge,Foot=foot,original.SlabMinimum,UniversalCentroid=original.CentroidLower,KnownCentroid=known.CentroidLower,
                UniversalAllWidths=original.Cells.Min(c=>c.KnPerM),KnownAllWidths=known.Cells.Min(c=>c.KnPerM),Known1000=known.ForSetting(1000,0).Cells.Min(c=>c.KnPerM),Known1524=known.ForSetting(1524,0).Cells.Min(c=>c.KnPerM)});
        }
        Console.WriteLine("KNOWN_UPPER_NORMAL_SOURCE_DATA "+JsonSerializer.Serialize(rows));
    }
    internal static void Run(){DenseAndSnapshot();GuardedFallbacks();ActualSourceData();Console.WriteLine("PASS "+checks+" known-upper normal witness checks: dense statics, exact snapshots, numeric cache sharing, universal fallbacks and actual source threshold");}
}
