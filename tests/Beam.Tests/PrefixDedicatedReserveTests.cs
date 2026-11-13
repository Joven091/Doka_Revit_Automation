using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class PrefixDedicatedReserveTests
{
    static int checks,windows;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Prefix dedicated reserve: "+message);}
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,StockEndSetbackMm=150,IgnoreUplift=true,DeflectionDenominator=270,LegCapacityKn=70,
        StockLengthsMm=new[]{1250.0,1800,2000,2450,2900,3300,3900,4900},AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.BeamSupports};
    static TowerLayout Layout(double width,double offset=0)=>new TowerLayout {WidthMm=width,TransverseOffsetMm=offset,PositionsMm=new[]{0.0,1000,2000,3000},Joined=new[]{true,true,true},StockPlan=new TowerStockPlan {Pieces=Array.Empty<TowerStockPiece>()}};
    static void GeometryOracle()
    {
        var common=Common();var fixture=PrimaryBearingTests.Fixture();var input=fixture.Input;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegs(common,input)==0,"original input-only lower bound remains unchanged");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,new[]{Layout(800)})==4,"known near axis cannot retain the stock end setback");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,new[]{Layout(1000)})==0,"an unknown opposite receiver is kept optimistic");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,new[]{Layout(1000),Layout(1000)})==4,"known far axis proves the same crossing mandatory once");
        foreach(double left in new[]{600.0,800,1000,1500,1524,2000})foreach(double right in new[]{600.0,800,1000,1500,1524,2000})
        foreach(double offset in new[]{-25.0,0,25})foreach(double setback in new[]{25.0,150,300})
        {
            common.StockEndSetbackMm=setback;var known=new[]{Layout(left,offset),Layout(right,-offset)};
            int minimum=TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,known);if(minimum==0)continue;
            double face=input.Beams[0].WidthMm/2,span=input.Bays[0].WidthMm,near=offset+left/2,far=fixture.BeamCentresMm[1]-offset-right/2;
            var stocks=common.StockLengthsMm.Where(s=>s<=span+1e-7).Concat(new[]{25*Math.Ceiling((far-near+2*setback)/25)}.Where(s=>s<=span+1e-7));
            foreach(double stock in stocks)
            {
                double earliest=Math.Max(0,far+setback-stock-face),latest=Math.Min(span-stock,near-setback-face);
                Check(earliest>latest+1e-7,"every original catalogue/custom secondary stock has an empty exact original start interval");windows++;
            }
        }
        Check(windows>100,"geometry oracle covers many native stock and axis combinations");
        common.StockEndSetbackMm=150;var impossible=PrimaryBearingTests.Fixture();impossible.Beams[0].WidthMm=800;
        Check(CrossBeamSupportOptimizer.PrimaryBearing(common,impossible,0,out _)==null,"unchanged complete direct-primary route rejects an actual certified prefix");
        var passing=PrimaryBearingTests.Fixture();Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,passing.Input,passing.Beams)==0
            && CrossBeamSupportOptimizer.PrimaryBearing(common,passing,0,out _)!=null,"known passing native primary detail remains unreserved");
    }
    static void ScopeAndUnion()
    {
        var common=Common();var input=PrimaryBearingTests.Fixture().Input;var narrow=new[]{Layout(800)};
        foreach(double delta in new[]{-1e-7,0,1e-7})
            Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,new[]{Layout(input.Beams[0].WidthMm+2*(150+delta))})==0,"near-tolerance endpoint retains the original route");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,null)==0 && TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,Array.Empty<TowerLayout>())==0
            && TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,new TowerLayout[]{null})==0,"unknown prefix axes add no reserve");
        foreach(double value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,new[]{Layout(value)})==0,"nonfinite prefix axes retain the original bound");
        var off=common.CopyForSearch();off.ReuseSearchWork=false;Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(off,input,narrow)==0,"reference path retains input-only bound");
        var timed=common.CopyForSearch();timed.Trace=new BeamSearchTrace("original timed reserve",searchTimeLimit:TimeSpan.FromMinutes(1));
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(timed,input,narrow)==0,"timed path retains input-only bound");
        input.CrossingBeams[0].SupportMode=CrossBeamSupportMode.ExistingSupportsOnly;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,narrow)==0,"existing-support-only failure never invents dedicated permission");
        input.CrossingBeams[0].SupportMode=CrossBeamSupportMode.LegacySlabRows;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,narrow)==0,"legacy slab rows remain excluded");
        input.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,narrow)==4,"forced dedicated crossing is counted exactly once");
        input.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;input.CrossingBeams[0].BottomElevationMm=-2000;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegs(common,input)==4 && TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,narrow)==4,"old mandatory automatic crossing is not counted twice");
        input=PrimaryBearingTests.Fixture().Input;input.CrossingBeams=input.CrossingBeams.Concat(new[]{new TransferCrossingBeamRow {BayNumber=1,SupportMode=CrossBeamSupportMode.DedicatedTowers}}).ToArray();
        Check(TransferNetworkOptimizer.MinimumDedicatedLegs(common,input)==4 && TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,narrow)==8,"old mandatory and newly certified crossings contribute a union");
        input=PrimaryBearingTests.Fixture().Input;input.CrossingBeams[0].BottomElevationMm=-900;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(common,input,narrow)==0,"valid shared filler leaves its route available despite narrow primary axes");
        var scenario=ReportedScenarioTests.DiagnosticPart(3,1).Item2.CreateTowerInput();scenario.CoordinateCrossingLayouts=true;
        var n=scenario.Network;Check(TransferNetworkOptimizer.MinimumDedicatedLegs(scenario,n)==0,"actual scenario3 has no input-only mandatory towers");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(scenario,n,new[]{Layout(1000)})==12,"scenario3 narrow B1 proves three dedicated crossings");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegsForPrefix(scenario,n,new[]{Layout(1000),Layout(1000),Layout(1000)})==24,"scenario3 narrow B3 proves the next three while valid shared bay remains optimistic");
        Console.WriteLine("Scenario3 narrow known-axis reserve: B1=12 extra legs; B1/B2/B3=24 extra legs");
    }
    static void BoundPropagation()
    {
        var common=Common();var input=PrimaryBearingTests.Fixture().Input;
        var owner=typeof(TransferNetworkOptimizer);var boundType=owner.GetNestedType("EngineerBound",BindingFlags.NonPublic);var stateType=owner.GetNestedType("State",BindingFlags.NonPublic);
        var bound=Activator.CreateInstance(boundType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{common,input,null},null);
        boundType.GetField("legs",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bound,20);
        var state=Activator.CreateInstance(stateType,true);stateType.GetField("Beams").SetValue(state,new[]{Layout(800)});stateType.GetField("Legs").SetValue(state,8);
        int scalar=(int)boundType.GetMethod("Ceiling",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(int),typeof(int)},null).Invoke(bound,new object[]{8,4});
        int suffix=(int)boundType.GetMethod("Ceiling",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(int),typeof(int),typeof(int)},null).Invoke(bound,new object[]{8,1,4});
        Check(scalar==8 && suffix==8 && !boundType.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Any(m=>m.Name=="Ceiling" && m.GetParameters()[0].ParameterType==stateType),"future-solve ceilings keep exact original values so adaptive receiver candidate trajectories remain unchanged");
        var best=new TransferNetworkResult {Beams=new[]{new TowerLayout {WidthMm=1524,PositionsMm=Enumerable.Range(0,7).Select(i=>i*1000.0).ToArray(),Joined=Enumerable.Repeat(true,6).ToArray(),StockPlan=new TowerStockPlan {Pieces=Array.Empty<TowerStockPiece>()}}},Bays=Array.Empty<UpperTransferPlan>(),CrossSupports=Array.Empty<CrossBeamSupportResult>()};
        boundType.GetField("best",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bound,best);
        Check(!(bool)boundType.GetMethod("MayImprove",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{stateType,typeof(int),typeof(int)},null).Invoke(bound,new object[]{state,4,1})
            && !(bool)boundType.GetMethod("MayImprove",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{stateType,typeof(int),typeof(int),typeof(int)},null).Invoke(bound,new object[]{state,1,4,1}),"both after-Extend rank paths reject a proven sixteen-leg branch against fourteen-leg incumbent");
    }
    internal static void NativeParity()
    {
        var common=Common();common.LengthMm=4000;common.StockLengthsMm=new[]{2000.0,4000};common.CustomH20Scope=CustomH20LengthScope.Both;common.MaximumConcreteEndOverhangMm=150;common.LegCapacityKn=70;common.DeflectionDenominator=270;common.NetworkSearchWorkers=1;
        var input=new TransferNetworkInput {ReferenceLengthMm=4000,PlywoodSheetLengthMm=2440,PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,UpperSpacingMm=305,
            BeamSpacingsMm=new[]{305.0,305},SlabSpacingsMm=new[]{488.0},CrossingSpacingsMm=new[]{305.0},UseCrossingSlabLedgers=true,
            Beams=new[]{new TransferBeamRow {WidthMm=1000,DepthMm=1800,BottomElevationMm=0},new TransferBeamRow {WidthMm=1000,DepthMm=1800,BottomElevationMm=0}},
            Bays=new[]{new TransferBayRow {WidthMm=2500,ThicknessMm=50}},CrossingBeams=new[]{new TransferCrossingBeamRow {BayNumber=1,PositionMm=2000,WidthMm=400,DepthMm=200,BottomElevationMm=0,SupportMode=CrossBeamSupportMode.Automatic}}};
        var off=common.CopyForSearch();off.PruneNetworkBounds=false;var expected=TransferNetworkOptimizer.Optimize(off,input);var actual=TransferNetworkOptimizer.Optimize(common,input);
        Check(ControlledPerformanceTests.Fingerprint(expected)==ControlledPerformanceTests.Fingerprint(actual),"complete bounds ON/OFF preserves exact selected geometry, crossing detail, forces and engineering rank");
        ControlledPerformanceTests.Validate(common,actual);Console.WriteLine("PASS prefix dedicated reserve native ON/OFF exact network parity: "+actual.LegCount+" legs");
    }
    internal static void Run(){GeometryOracle();ScopeAndUnion();BoundPropagation();Console.WriteLine("PASS "+checks+" prefix dedicated reserve checks including "+windows+" exact original stock-start intervals");}
}
