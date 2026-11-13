using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using EngineeringDashboard;
using Bound=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;
internal static class ExteriorNormalWitnessTests
{
    static int checks;static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Exterior normal witness: "+text);}
    static TowerInput Common()=>(TowerInput)typeof(ReceiverAggregateReserveTests).GetMethod("Common",hidden).Invoke(null,null);
    static TransferNetworkInput Network()
    {
        var n=(TransferNetworkInput)typeof(ReceiverAggregateReserveTests).GetMethod("Network",hidden).Invoke(null,null);n.Bays[0].WidthMm=5400;
        foreach(var b in n.Beams)b.BottomElevationMm=-1750;n.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;n.CrossingBeams[0].BottomElevationMm=-900;return n;
    }
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static TransferredSlabOptimizer.Geometry G(double[] p)=>new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=p.Length-1}},Positions=p,Joined=Enumerable.Repeat(true,p.Length-1).ToArray(),MemberSupports=new[]{p}};
    static void CentroidAndDense()
    {
        foreach(double bay in new[]{1000.0,1500,3000,5400,30000})
        foreach(double edge in new[]{200.0,200+(bay-1000)/8,(bay-600)/2})
        foreach(double foot in new[]{edge+150,(bay-300)/2})
        {
            if(foot<edge+150 || bay-2*foot<300)continue;
            double stock=bay-2*edge,centroid=(stock*foot+2*edge*100)/bay;
            Check(centroid>=Bound.NormalCentroidLower(bay),"closed lower centroid covers every symmetric valid edge/foot endpoint");
        }
        Check(Bound.NormalCentroidLower(999)==0 && Bound.NormalCentroidLower(double.NaN)==0,"unsupported centroid domains supply no witness");
        var common=Common();var network=Network();var bound=Bound.TryCreate(common,network,6000,true,new TransferredSlabOptimizer.GeometryCache());
        foreach(int beam in new[]{0,1})
        {
            var witness=bound.ExteriorNormalWitness(beam);Check(witness!=null && witness.SlabMinimum>0 && witness.Cells.Length>0,"native outer receiver exposes its physical minimum slab density");
            double bay=network.Bays[0].WidthMm,width=network.Beams[beam].WidthMm,area=network.AreaLoad(200),ownArea=network.AreaLoad(1750);
            foreach(var setting in new[]{Tuple.Create(600.0,0.0),Tuple.Create(1000.0,0.0),Tuple.Create(1500.0,0.0),Tuple.Create(1524.0,0.0),Tuple.Create(2000.0,0.0),Tuple.Create(1000.0,-25.0),Tuple.Create(1000.0,25.0)})
            foreach(double edge in new[]{200.0,500.0,(bay-600)/2})foreach(double foot in new[]{edge+150,(bay-300)/2})
            {
                if(bay-2*foot<300)continue;double stock=bay-2*edge;
                var upper=LoadedBeamAnalysis.AnalyzeDenseReference(stock,new[]{foot-edge,bay-foot-edge},new[]{Q(0,stock,area)},null,TowerOptimizer.PairEI/2,270);
                double total=upper.Reactions[0]+area*edge/1000,factor=witness.SlabMinimum/total;
                double length=2*(width/2+foot+Math.Abs(setting.Item2)+1000),start=-length/2,sign=beam==0 ? 1 : -1;
                var lower=LoadedBeamAnalysis.AnalyzeDenseReference(length,new[]{setting.Item2-setting.Item1/2-start,setting.Item2+setting.Item1/2-start},
                    new[]{Q(-width/2-start,width/2-start,ownArea)},
                    new[]{new BeamPointLoad {PositionMm=sign*(width/2+foot)-start,Kn=upper.Reactions[0]*factor},new BeamPointLoad {PositionMm=sign*(width/2+100)-start,Kn=area*edge/1000*factor}},TowerOptimizer.PairEI/2,270);
                double facing=lower.Reactions[beam==0 ? 1 : 0];var specific=witness.ForSetting(setting.Item1,setting.Item2);
                Check(specific!=null && specific.Cells[0].KnPerM<=facing,"independent dense transverse statics retain the fixed-setting lower line density");
                Check(witness.Cells[0].KnPerM<=facing,"all-width minimum is below every actual facing reaction");
            }
        }
    }
    static void LocalIntervals()
    {
        var input=Common();Bound.NormalWitnessFailure reason;var supports=new[]{150.0,1850};
        Check(!Bound.ExteriorNormalFits(input,0,2000,supports,new[]{Q(0,2000,35)},out reason) && reason.Demand>reason.Limit,"a full normal support interval supplies a concrete shear witness");
        Check(Bound.ExteriorNormalFits(input,0,2000,supports,new[]{Q(0,1000,35),Q(1000,2000,35)},out _),"adjacent cells cannot be combined into one synthetic span load");
        Check(Bound.ExteriorNormalFits(input,0,2000,supports,new[]{Q(0,800,100),Q(1200,2000,100)},out _),"unknown crossing contributions cannot be erased between normal pieces");
        Check(!Bound.ExteriorNormalFits(input,0,3600,new[]{800.0,2800},new[]{Q(0,800,100)},out var tail) && tail.Name.Contains("tail"),"a genuine whole normal stock tail has its own independent bound");
        Check(Bound.ExteriorNormalFits(input,650-5e-8,1000,new[]{1e-8,5e-8},new[]{Q(650-5e-8,650+1e-7,1e15)},out _),"tiny local intervals cannot claim a surviving actual source atom");
        var source=new[]{G(new[]{150.0,1674,3198,4722,5850})};
        var view=(Bound.UniversalView)typeof(Bound).GetMethod("BuildUniversalView",hidden).Invoke(null,new object[]{input,source,0,new[]{Q(0,6000,1)},new Func<double,double,bool>((a,b)=>false),new[]{Q(100,5800,35)}});
        Check(view.Kept.Length==0 && view.FirstNormalFailure.HasValue,"a whole local interval proof is independent of an ambiguous different part of its stock window");
        var signature=new Bound.UniversalProfileSignature(new[]{Q(0,6000,1)},22,10,null,new[]{Q(100,5800,35)});
        Check(!signature.Equals(new Bound.UniversalProfileSignature(new[]{Q(0,6000,1)},22,10,null,new[]{Q(100,5800,34)})),"normal density bits distinguish shared view evidence");
        Check(!signature.Equals(new Bound.UniversalProfileSignature(new[]{Q(0,6000,1)},22,10)),"an average-only view cannot consume a witness-enabled rejection");
    }
    static void ProvenanceAndCache()
    {
        var common=Common();common.Trace=new BeamSearchTrace("normal witness provenance");var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var bound=Bound.TryCreate(common,network,6000,true,cache);var witness=bound.ExteriorNormalWitness(0);
        var input=common.CopyForSearch();input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.DeferCrossingChecks=true;
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,SharedGeometryCache=cache,EngineerNetwork=network,EngineerBeam=0,EngineerReceiverCount=8,FixedTowerWidthMm=1000,
            BeamWidthMm=800,BeamCentreOffsetMm=0,RightBayMm=5400,RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8};
        Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> prepare=l=>(g,e)=>true;Func<TowerLayout,bool> accept=l=>true;
        input.TransferReceiverPreparation=prepare;input.TransferLayoutAcceptance=accept;
        var filter=bound.GeometryFilterForNativeReceiver(0,prepare,accept,1000,0);var average=bound.GeometryFilter(0);
        var source=Enumerable.Range(2,10).Select(n=>G(Enumerable.Range(0,n).Select(k=>150+5700.0*k/(n-1)).ToArray())).ToArray();
        var original=average(input,source,0);var filtered=filter(input,source,0);
        Check(filtered.All(g=>original.Any(old=>ReferenceEquals(g,old))),"witness adds only independent exclusions to the existing average screen");
        Check(filtered.Length<original.Length && input.Trace.Count(BeamSearchCounter.ExteriorNormalMembersRejected)>0,"fixed-width normal-line witness removes actual eccentric span failures beyond the average bound");
        Check(ReferenceEquals(filter(input,source,0),filtered),"completed witness-enabled views are reused");
        var changed=input.CopyForSearch();changed.TransferReceiverPreparation=l=>(g,e)=>true;
        Check(ReferenceEquals(filter(changed,source,0),original),"another callback identity uses the existing average-only signature");
        var setting=witness.ForSetting(1000,0);Check(setting.Allows(input),"captured fixed native setting is eligible");
        var widthCopy=input.CopyForSearch();widthCopy.TransferredSlab=input.TransferredSlab.Copy();widthCopy.TransferredSlab.FixedTowerWidthMm=2000;
        Check(!setting.Allows(widthCopy),"a changed fixed width cannot reuse a more demanding setting proof");
        Check(!Bound.ValidNormalUpper(new UpperTransferPlan {StockLengthMm=5000,EdgeMm=200,NearFootMm=250,FarFootMm=250},5400),"equal feet alone cannot claim the required centroid reach");
        input.TransferredSlab.RightPlan=new UpperTransferPlan {StockLengthMm=5000,EdgeMm=200,NearFootMm=350,FarFootMm=350};Check(setting.Allows(input),"a valid symmetric fixed upper plan supplies the centroid domain");
        Check(input.Trace==null || input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0,"normal source bounds and views never perform primary FE");
    }
    static void SourceData()
    {
        var rows=new List<object>();
        foreach(int scenario in new[]{2,3})
        {
            var common=ReportedScenarioTests.DiagnosticPart(scenario,1).Item2.CreateTowerInput();common.CoordinateCrossingLayouts=true;var network=common.Network;
            double coverage=network.SharedEnd(common.LengthMm)-network.SharedStart;var bound=Bound.TryCreate(common,network,coverage,true,new TransferredSlabOptimizer.GeometryCache());
            Check(bound!=null,"actual reported coordinated source supplies its normal bound data");
            foreach(int beam in new[]{0,network.Beams.Length-1})
            {
                var witness=bound.ExteriorNormalWitness(beam);Check(witness!=null,"actual exterior source has normal cells");
                var variants=new[]{Tuple.Create(600.0,0.0),Tuple.Create(1000.0,0.0),Tuple.Create(1500.0,0.0),Tuple.Create(1524.0,0.0),Tuple.Create(2000.0,0.0),Tuple.Create(1000.0,-25.0),Tuple.Create(1000.0,25.0)}
                    .Select(s=>new {Width=s.Item1,Offset=s.Item2,Density=witness.ForSetting(s.Item1,s.Item2)?.Cells.FirstOrDefault()?.KnPerM}).ToArray();
                rows.Add(new {Scenario=scenario,Beam=beam+1,Coverage=coverage,witness.OwnLower,witness.OwnUpper,witness.SlabMinimum,witness.CentroidLower,CellCount=witness.Cells.Length,AllWidthsMinimum=witness.Cells.Min(c=>c.KnPerM),Variants=variants});
            }
        }
        Console.WriteLine("EXTERIOR_NORMAL_SOURCE_DATA "+JsonSerializer.Serialize(rows));
    }
    internal static void Run(){CentroidAndDense();LocalIntervals();ProvenanceAndCache();SourceData();Console.WriteLine("PASS "+checks+" exterior normal witness checks: centroid proof, independent dense reactions, isolated intervals, provenance/key guards and real-source density data");}
}
