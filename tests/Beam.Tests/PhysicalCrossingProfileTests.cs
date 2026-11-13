using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using EngineeringDashboard;

internal static class PhysicalCrossingProfileTests
{
    static int checks;static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Physical crossing profile: "+message);}
    static TowerInput Common()=>(TowerInput)typeof(ReceiverAggregateReserveTests).GetMethod("Common",hidden).Invoke(null,null);
    static TransferNetworkInput Network()=>(TransferNetworkInput)typeof(ReceiverAggregateReserveTests).GetMethod("Network",hidden).Invoke(null,null);
    static TransferNetworkOptimizer.ReceiverAggregateLoadBound Bound(TowerInput common,TransferNetworkInput network,double cover=6000,bool carried=true)
        =>TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(common,network,cover,carried,new TransferredSlabOptimizer.GeometryCache());
    static double At(BeamDistributedLoad[] source,double x)=>source.Where(q=>q.StartMm<=x && q.EndMm>x).Sum(q=>q.KnPerM);
    static string Signature(BeamDistributedLoad[] source)=>string.Join(";",source.Select(q=>q.StartMm.ToString("R")+":"+q.EndMm.ToString("R")+":"+q.KnPerM.ToString("R")));
    static void DedicatedRouteGates()
    {
        var input=Common();
        foreach(double end in new[]{0.0,150,PortableTestRuntime.NextDown(175),175,200,500})
        foreach(bool carried in new[]{false,true})
        {
            input.MaximumConcreteEndOverhangMm=end;
            double deeper=TransferNetworkOptimizer.ReceiverAggregateLoadBound.DedicatedConcreteCoefficient(input,-1750,-1150,carried);
            double equal=TransferNetworkOptimizer.ReceiverAggregateLoadBound.DedicatedConcreteCoefficient(input,-1150,-1150,carried);
            Check(end<175 ? deeper>174.9 && deeper<175 : deeper==0,"full-span dedicated bypass or deeper shutter allowance is respected at exact/ULP threshold");
            Check(end<175 && carried ? equal>499.9 && equal<500 : equal==0,"equal-soffit500 requires a carried adjacent slab and no full-span bypass");
        }
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.DedicatedConcreteCoefficient(input,-500,-1150,true)==0,"a receiver above the crossing cannot borrow a junction end-zone floor");
        input.MaximumConcreteEndOverhangMm=double.NaN;
        Check(TransferNetworkOptimizer.ReceiverAggregateLoadBound.DedicatedConcreteCoefficient(input,-1750,-1150,true)==0,"nonfinite route settings supply no coefficient");
        var common=Common();var network=Network();Check(Bound(common,network,carried:false)==null,"an excluded/unknown slab universe cannot borrow the all-carried500 certificate");
        var baseline=Bound(common,network);var weak=baseline.UniversalAverage(0);
        Check(At(weak,3000)>network.AreaLoad(1750)*.8/2,"a required dedicated shutter end zone increases the terminal physical crossing density");
        common.MaximumConcreteEndOverhangMm=175;var bypass=Bound(common,network);
        Check(At(bypass.UniversalAverage(0),3000)<network.AreaLoad(1750)*.8/2,"allowed full-span dedicated routes retain the original own-only crossing lower density");
        common.MaximumConcreteEndOverhangMm=150;network.Beams[0].BottomElevationMm=network.CrossingBeams[0].BottomElevationMm;
        var equalBound=Bound(common,network);double coefficient=equalBound.PhysicalConcreteCoefficient(0,network.CrossingBeams[0]);
        Check(coefficient>499.9 && coefficient<500,"equal-soffit carried receiver uses a guarded500mm dedicated minimum");
        var map=network.CrossSlabLayout(6000,6000,0);map.Edges.Clear();map.Edges.Add(new SlabEdgeReaction {Crossing=0,PositionMm=2675,WidthMm=-10});
        var signed=Bound(common,network);Check(signed!=null && At(signed.UniversalAverage(0),3000)<network.AreaLoad(1750)*.8/2,"negative source ledger keeps the original physical crossing proof rather than dropping a negative term");
    }
    static void PrimaryAndSharedRoutes()
    {
        var input=Common();var network=Network();network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;
        foreach(double span in new[]{1500.0,2200,3000,4500,5400})
        {
            network.Bays[0].WidthMm=span;
            for(int side=0;side<2;side++)
            {
                double floor=TransferNetworkOptimizer.ReceiverAggregateLoadBound.PrimaryConcreteCoefficient(input,network,network.CrossingBeams[0],side);
                foreach(double leftWidth in TowerOptimizer.BaysMm.Concat(new[]{1524.0}))foreach(double rightWidth in TowerOptimizer.BaysMm.Concat(new[]{1524.0}))
                foreach(double leftOffset in leftWidth==1000 ? new[]{-25.0,0,25} : new[]{0.0})foreach(double rightOffset in rightWidth==1000 ? new[]{-25.0,0,25} : new[]{0.0})
                {
                    double a=leftOffset+leftWidth/2-network.Beams[0].WidthMm/2,b=span+rightOffset-rightWidth/2+network.Beams[1].WidthMm/2;
                    if(b<=a+1e-7 || a<input.StockEndSetbackMm-1e-7 || b>span-input.StockEndSetbackMm+1e-7)continue;
                    foreach(double leftTail in new[]{0.0,a/2,a})foreach(double rightTail in new[]{0.0,(span-b)/2,span-b})
                    {
                        double length=span-leftTail-rightTail,far=length*(leftTail+length/2-a)/(b-a)+rightTail;
                        double actual=side==0 ? span-far : far;
                        Check(floor<=Math.Max(0,actual),"quantified primary coefficient covers all native widths/offsets, signed stock reactions and concrete end strips");
                    }
                }
            }
        }
        network=Network();network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;network.CrossingBeams[0].BottomElevationMm=-1550;
        var bound=Bound(input,network);
        Check(bound!=null && !CrossBeamSupportOptimizer.PrimaryBearingPossible(input,network,network.CrossingBeams[0]),"Shared/dedicated fixture cannot borrow an unsupported primary route");
        Check(bound.PhysicalConcreteCoefficient(0,network.CrossingBeams[0])>174.9 && bound.PhysicalConcreteCoefficient(0,network.CrossingBeams[0])<175,"physical minimum takes the weaker dedicated detail across all early and late refinements");
        Check(At(bound.UniversalEarlyAverage(0),3000)>At(bound.UniversalAverage(0),3000),"existing early Shared admission remains a separate stronger load case");
    }
    static void NativeLowerStockExtension()
    {
        var input=Common();input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=650,BeamLineLoadKnM=20,LeftBayMm=3000,RightBayMm=3000,
            LeftAreaLoadKnM2=5,RightAreaLoadKnM2=5,UpperSpacingMm=305};
        var crossOptions=typeof(TransferredSlabOptimizer).GetMethod("CrossOptions",hidden);int options=0;
        foreach(double width in TowerOptimizer.BaysMm.Concat(new[]{1524.0}))foreach(double offset in new[]{-25.0,0,25})
        {
            input.TransferredSlab.BeamCentreOffsetMm=-offset;
            foreach(var cross in (IEnumerable)crossOptions.Invoke(null,new object[]{input,width,305.0}))
            {
                var type=cross.GetType();double start=(double)type.GetField("Start",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(cross)+offset;
                double stock=(double)type.GetField("Stock",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(cross);
                foreach(bool left in new[]{false,true})
                {
                    var upper=type.GetField(left ? "Left" : "Right",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(cross);
                    double foot=(double)upper.GetType().GetField("Foot",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(upper);
                    double extension=left ? -start-325 : start+stock-325;
                    Check(foot>=350 && extension>=foot+150-1e-7 && extension>=500-1e-7,"every retained native CrossOption encloses slab foot+150 after exact concrete-relative offset translation");options++;
                }
            }
        }
        Check(options>0,"lower-stock proof covers actual generated retained transverse options");
    }
    static void CallerScope()
    {
        var input=Common();var network=Network();var cache=new TransferredSlabOptimizer.GeometryCache();var bound=TransferNetworkOptimizer.ReceiverAggregateLoadBound.TryCreate(input,network,6000,true,cache);
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.TransferredSlab=new TransferredSlabInput {SharedGeometryCache=cache,RightBayMm=3000,
            RightAreaLoadKnM2=network.AreaLoad(200),BeamLineLoadKnM=network.AreaLoad(1750)*.8};
        var supports=new[]{150.0,5850};var source=new[]{new TransferredSlabOptimizer.Geometry {Positions=supports,Joined=new[]{true},MemberSupports=new[]{supports},
            Stock=new[]{new TowerStockPiece {StartMm=0,StockLengthMm=6000,FirstSupport=0,LastSupport=1}}}};
        var filter=bound.GeometryFilter(0);Check(filter(input,source,0).Length==0,"matching input really activates the physical filter");
        var changed=input.CopyForSearch();changed.MaximumConcreteEndOverhangMm=175;
        Check(ReferenceEquals(filter(changed,source,0),source),"another end-overhang policy cannot borrow a no-fullspan dedicated certificate");
        changed=input.CopyForSearch();changed.StockEndSetbackMm++;
        Check(ReferenceEquals(filter(changed,source,0),source),"another end-setback policy cannot borrow the fixed primary-axis coefficient");
        changed=input.CopyForSearch();changed.StockEndSetbackMm=PortableTestRuntime.NextUp(input.StockEndSetbackMm);
        Check(ReferenceEquals(filter(changed,source,0),source),"an adjacent-double stock-end policy retains the original scope fallback");
    }
    static void ActualScenario3InteriorDensity()
    {
        var common=ReportedScenarioTests.DiagnosticPart(3,1).Item2.CreateTowerInput();var network=common.Network;
        common.CoordinateCrossingLayouts=true;network.Validate(common);
        var bound=Bound(common,network,13400);Check(bound!=null,"actual scenario3 carries a certified physical source profile");
        int beam=1;var profile=bound.GetType().GetMethod("ForBeam",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(bound,new object[]{beam});
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var raw=(BeamDistributedLoad[][])profile.GetType().GetField("Raw",fields).GetValue(profile);
        var reduced=(BeamDistributedLoad[][])profile.GetType().GetField("Reduced",fields).GetValue(profile);
        var cuts=(double[])bound.GetType().GetMethod("UniversalCuts",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(bound,new object[]{beam});
        var before=TransferNetworkOptimizer.ReceiverAggregateLoadBound.GuardedAverage(raw,reduced,cuts);var physical=bound.UniversalAverage(beam);
        var adjacent=network.CrossingBeams.Where(c=>c.BayNumber==beam || c.BayNumber==beam+1).ToArray();
        Check(adjacent.Length==6 && adjacent.All(c=>!EngineerPrimaryPlan.Eligible(network,c)),"actual B2 has two noneligible adjacent crossing families");
        foreach(var crossing in adjacent)
        {
            double coefficient=bound.PhysicalConcreteCoefficient(beam,crossing);
            Check(coefficient>499.9 && coefficient<500,"each actual equal-soffit carried junction retains the guarded500mm floor");
            double added=At(physical,crossing.PositionMm)-At(before,crossing.PositionMm);
            Check(added>15.7 && added<15.8251,"both aligned actual bay crossings add their physical floor to the interior paired mean");
            Check(!TransferNetworkOptimizer.ReceiverAggregateLoadBound.PrimitiveWindowSafe(bound.UniversalClippingIntervals(beam),crossing.PositionMm-crossing.WidthMm/2-5e-8,1000),
                "actual crossing endpoints retain the complete primitive tiny-fragment fallback");
        }
        Check(ReferenceEquals(bound.UniversalEarlyAverage(beam),physical),"no early-added crossing already delegates to the physical profile at every receiver count");
    }
    static void SeparateLoadCasesCannotBeMaxed()
    {
        var input=Common();var supports=new[]{0.0,2000};
        var admission=new[]{new BeamDistributedLoad {EndMm=1000,KnPerM=28}};
        var physical=new[]{new BeamDistributedLoad {StartMm=1000,EndMm=2000,KnPerM=28}};
        var hybrid=new[]{new BeamDistributedLoad {EndMm=2000,KnPerM=28}};
        Check(CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,2000,supports,admission,null)
            && CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,2000,supports,physical,null),"each distinct mandatory load case satisfies the original necessary screen");
        Check(!CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,2000,supports,hybrid,null),"pointwise-max across distinct cases can invent a failing combined demand");
        foreach(var loads in new[]{admission,physical})
        {
            var member=LoadedBeamAnalysis.AnalyzeDenseReference(2000,supports,loads,null,input.Primary.EI,input.DeflectionDenominator);
            Check(member.MomentKnM<input.Primary.MomentLimitKnM && member.ShearKn<input.Primary.ShearLimitKn && member.DeflectionUtilization<1,
                "independent full dense statics confirms the separate cases pass strength and deflection");
        }
    }
    internal static void NativeParity()
    {
        var common=Common();common.LengthMm=2450;common.StockLengthsMm=new[]{1250.0,1800,2450,2900};common.MaximumConcreteEndOverhangMm=150;common.AllowSeparateSlabFallback=false;
        var network=new TransferNetworkInput {ReferenceLengthMm=2450,PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
            BeamSpacingsMm=new[]{305.0,305},SlabSpacingsMm=new[]{488.0},CrossingSpacingsMm=new[]{350.0},UseCrossingSlabLedgers=true,
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=new[]{new TransferBayRow {WidthMm=2200,ThicknessMm=200}},
            CrossingBeams=new[]{new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600,SupportMode=CrossBeamSupportMode.DedicatedTowers}}};
        var off=common.CopyForSearch();off.PruneNetworkBounds=false;var expected=TransferNetworkOptimizer.Optimize(off,network);
        common.Trace=new BeamSearchTrace("physical crossing native parity");var actual=TransferNetworkOptimizer.Optimize(common,network);
        Check(ControlledPerformanceTests.Fingerprint(actual)==ControlledPerformanceTests.Fingerprint(expected),"physical crossing masks ON/OFF preserve exact complete selection and forces");
        ControlledPerformanceTests.Validate(common,actual);
        Check(common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)>0,"native terminal masks execute under the strict end-overhang route gate");
        Check(actual.CrossSupports[0].Bearings.Any(b=>b.Beam==0 && b.IsConcreteEndZone) && actual.CrossSupports[0].Bearings.Any(b=>b.Beam==1 && b.IsConcreteEndZone),"selected dedicated detail really retains checked concrete end zones on both receivers");
        Console.WriteLine("PASS physical crossing native ON/OFF exact parity: "+actual.LegCount+" legs; "+common.Trace.Count(BeamSearchCounter.UniversalGeometryViewBuilds)+" views");
    }
    internal static void Run(){DedicatedRouteGates();PrimaryAndSharedRoutes();NativeLowerStockExtension();CallerScope();ActualScenario3InteriorDensity();SeparateLoadCasesCannotBeMaxed();Console.WriteLine("PASS "+checks+" physical crossing profile checks");}
}
