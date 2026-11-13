using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using EngineeringDashboard;
using Bound=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;
internal static class RetainedNormalWitnessTests
{
    static int checks;static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Retained normal witness: "+text);}
    static TowerInput Common()=>(TowerInput)typeof(ExteriorNormalWitnessTests).GetMethod("Common",hidden).Invoke(null,null);
    static TransferNetworkInput Network()=>(TransferNetworkInput)typeof(ExteriorNormalWitnessTests).GetMethod("Network",hidden).Invoke(null,null);
    static UpperTransferPlan Plan(double foot)=>new UpperTransferPlan {StockLengthMm=5000,EdgeMm=200,NearFootMm=foot,FarFootMm=foot};
    static TransferredSlabOptimizer.Geometry G(double[] p)=>new TransferredSlabOptimizer.Geometry {Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=p.Length-1}},Positions=p,Joined=Enumerable.Repeat(true,p.Length-1).ToArray(),MemberSupports=new[]{p}};
    static void Certificate()
    {
        var plans=new[]{Plan(600),Plan(900)};var certificate=Bound.RetainedNormalPlans.TryCreate(5400,plans);
        Check(certificate!=null,"complete supported retained options create a certificate");
        foreach(var plan in plans)
        {
            double h=(plan.StockLengthMm*plan.NearFootMm+200*plan.EdgeMm)/5400;
            Check(certificate.CentroidLower<=h && certificate.Contains(plan) && certificate.Contains(plan.Reverse()),"minimum includes every retained member and symmetric reverse copy by value");
        }
        Check(certificate.CentroidLower>Bound.NormalCentroidLower(5400),"passing retained domain can strengthen the unrestricted centroid minimum");
        Check(!certificate.Contains(Plan(750)) && !certificate.Contains(null),"an unretained valid geometry or missing plan cannot consume the minimum");
        Check(Bound.RetainedNormalPlans.TryCreate(5400,null)==null && Bound.RetainedNormalPlans.TryCreate(5400,Array.Empty<UpperTransferPlan>())==null,"missing retained universes remain unknown");
        Check(Bound.RetainedNormalPlans.TryCreate(5400,new[]{Plan(600),null})==null && Bound.RetainedNormalPlans.TryCreate(5400,new[]{Plan(250)})==null,"a null or unsupported retained member disables refinement");
        double minimum=certificate.CentroidLower;plans[0].NearFootMm=plans[0].FarFootMm=350;plans[1]=Plan(350);
        Check(certificate.CentroidLower==minimum && certificate.Contains(Plan(600)) && certificate.Contains(Plan(900)) && !certificate.Contains(plans[0]),"certificate owns numeric snapshots independently of caller objects and arrays");
    }
    static void GuardsAndReuse()
    {
        foreach(int beam in new[]{0,1})
        {
            var common=Common();common.Trace=new BeamSearchTrace("retained normal guards");var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var bound=Bound.TryCreate(common,network,6000,true,cache);
            var plans=new[]{Plan(600),Plan(900)};bound.BindRetainedNormalOptions(new[]{plans});
            var input=common.CopyForSearch();input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,beam);input.DeferCrossingChecks=true;
            input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,SharedGeometryCache=cache,EngineerNetwork=network,EngineerBeam=beam,EngineerReceiverCount=8,FixedTowerWidthMm=1000,
                BeamWidthMm=800,BeamCentreOffsetMm=0,LeftBayMm=beam==0?0:5400,RightBayMm=beam==0?5400:0,LeftAreaLoadKnM2=beam==0?0:network.AreaLoad(200),RightAreaLoadKnM2=beam==0?network.AreaLoad(200):0,BeamLineLoadKnM=network.AreaLoad(1750)*.8};
            void Facing(UpperTransferPlan plan){if(beam==0)input.TransferredSlab.RightPlan=plan;else input.TransferredSlab.LeftPlan=plan?.Reverse();}
            Func<TowerLayout,Func<TransferredSlabOptimizer.Geometry,double,bool>> prepare=l=>(g,e)=>true;Func<TowerLayout,bool> accept=l=>true;input.TransferReceiverPreparation=prepare;input.TransferLayoutAcceptance=accept;
            var source=Enumerable.Range(2,12).Select(n=>G(Enumerable.Range(0,n).Select(k=>150+5700.0*k/(n-1)).ToArray())).ToArray();
            var baseline=bound.GeometryFilterForNativeReceiver(beam,prepare,accept,1000,0);var filter=bound.GeometryFilterForRetainedOptions(beam,prepare,accept,1000,0);
            Facing(plans[0]);var universal=baseline(input,source,0);var first=filter(input,source,0);long builds=input.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds);
            Check(first.All(g=>universal.Any(u=>ReferenceEquals(g,u))),"retained witness preserves reference order within the original retained parent");
            Facing(plans[1]);Check(ReferenceEquals(filter(input,source,0),first) && common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)==builds,"different retained plans share one exact profile and cached view");
            Facing(Plan(750));Check(ReferenceEquals(filter(input,source,0),baseline(input,source,0)),"unretained valid current plans use universal fallback instead of a per-plan profile");
            Facing(null);Check(ReferenceEquals(filter(input,source,0),baseline(input,source,0)),"missing current plans retain universal behavior");
            plans[0].NearFootMm=plans[0].FarFootMm=350;Facing(plans[0]);Check(ReferenceEquals(filter(input,source,0),baseline(input,source,0)),"mutated four-field geometry cannot consume the captured retained domain");
            Facing(Plan(600));bound.BindRetainedNormalOptions(new[]{new[]{Plan(350)}});Check(ReferenceEquals(filter(input,source,0),first),"binding is once per immutable aggregate context");
            var other=Bound.TryCreate(common,network,6000,true,cache);other.BindRetainedNormalOptions(new[]{new UpperTransferPlan[]{Plan(600),null}});
            Check(ReferenceEquals(other.GeometryFilterForRetainedOptions(beam,prepare,accept,1000,0)(input,source,0),baseline(input,source,0)),"unsupported retained domain selects the universal signature");
        }
    }
    static void ActualOptionsData()
    {
        var common=ReportedScenarioTests.DiagnosticPart(3,1).Item2.CreateTowerInput();common.CoordinateCrossingLayouts=true;
        common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,ignoreUplift:common.IgnoreUplift);var network=common.Network;
        double coverage=network.SharedEnd(common.LengthMm)-network.SharedStart;var bound=Bound.TryCreate(common,network,coverage,true,new TransferredSlabOptimizer.GeometryCache());var rows=new List<object>();
        var optionsMethod=typeof(TransferNetworkOptimizer).GetMethod("Options",hidden);
        foreach(int beam in new[]{0,network.Beams.Length-1})
        {
            int bay=beam==0 ? 0 : network.Bays.Length-1;var widths=TransferNetworkOptimizer.EffectiveWidthsMm(common,network,bay,coverage,0);
            Check(widths!=null,"actual reported source has native effective slab widths");double tributary=Math.Max(widths.Max(),network.BaySpacing(bay));
            var options=((IEnumerable<UpperTransferPlan>)optionsMethod.Invoke(null,new object[]{common,network,bay,tributary,"retained-options data probe"})).ToArray();
            var certificate=Bound.RetainedNormalPlans.TryCreate(network.Bays[bay].WidthMm,options);var universal=bound.ExteriorNormalWitness(beam);var retained=universal.ForRetainedUpper(certificate);
            Check(certificate!=null && retained!=null && options.All(certificate.Contains),"actual retained native options are fully certified without changing their order");
            rows.Add(new {Scenario=3,Beam=beam+1,Coverage=coverage,Phase=0,Tributary=tributary,Count=options.Length,UniversalCentroid=universal.CentroidLower,RetainedCentroid=certificate.CentroidLower,
                UniversalAllWidths=universal.Cells.Min(c=>c.KnPerM),RetainedAllWidths=retained.Cells.Min(c=>c.KnPerM),Retained1000=retained.ForSetting(1000,0).Cells.Min(c=>c.KnPerM),Retained1524=retained.ForSetting(1524,0).Cells.Min(c=>c.KnPerM),
                Options=options.Select(p=>new {p.StockLengthMm,p.EdgeMm,p.NearFootMm,p.FarFootMm,Centroid=(p.StockLengthMm*p.NearFootMm+200*p.EdgeMm)/network.Bays[bay].WidthMm}).ToArray()});
        }
        Console.WriteLine("RETAINED_NORMAL_SOURCE_DATA "+JsonSerializer.Serialize(rows));
    }
    internal static void Run(){Certificate();GuardsAndReuse();ActualOptionsData();Console.WriteLine("PASS "+checks+" retained normal witness checks: immutable membership, original retained universe, shared profiles, conservative fallbacks and native Options-only source data");}
}
