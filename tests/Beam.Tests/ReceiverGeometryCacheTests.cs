using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using EngineeringDashboard;

// Tests a prepared fixed load plan directly; no network search is performed.
internal static class ReceiverGeometryCacheTests
{
    static readonly MethodInfo prepare=typeof(CrossBeamSupportOptimizer).GetMethod("PrepareRecheck",BindingFlags.NonPublic|BindingFlags.Static);
    static void Check(bool value,string message) {if(!value)throw new Exception("Receiver geometry cache: "+message);}
    static TowerInput Input(double capacity=70,CancellationToken token=default)=>new TowerInput {
        EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=capacity,DeflectionDenominator=270,
        IgnoreUplift=true,ReuseSearchWork=true,OptimizeCrossingSearch=true,
        Trace=new BeamSearchTrace("Independent receiver geometry cache",token) {EnableHotProfiling=true},
        TransferredSlab=new TransferredSlabInput(),Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true)};
    static TransferredSlabOptimizer.Geometry Geometry()=>new TransferredSlabOptimizer.Geometry {
        Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=2450,FirstSupport=0,LastSupport=1}},
        Positions=new[] {200.0,2250},Joined=new[] {true},MemberSupports=new[] {new[] {200.0,2250}}};
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Prepare(TowerInput input)
    {
        var crossing=new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=100,DepthMm=200,BottomElevationMm=-200};
        var network=new TransferNetworkInput {
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=200,DepthMm=300,BottomElevationMm=-300}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},CrossingBeams=new[] {crossing}};
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1800,KnPerM=.5}};
        var lower=new TransferMember {H20Count=1,Name="T1",LengthMm=1800,StartMm=-900,SupportsMm=new[] {138.0,1662},
            Uniform=uniform,Points=Array.Empty<BeamPointLoad>(),Analysis=LoadedBeamAnalysis.Analyze(1800,new[] {138.0,1662},uniform,null,TowerOptimizer.PairEI/2,270)};
        var probe=new TowerLayout {PrimaryMaterial=input.PrimaryMaterial,LengthMm=2450,WidthMm=1524,PositionsMm=new[] {200.0,2250},Joined=new[] {true},
            StockPlan=new TowerStockPlan {Pieces=Geometry().Stock},
            Transfer=new TransferredSlabResult {PrimaryPlan=EngineerPrimaryPlan.Create(input,network,0,false,false),RowsMm=new[] {1225.0},
                CaseNames=new[] {"Beam"},LowerCases=new[] {new[] {lower}}}};
        // A zero-force primary bearing makes the crossing assignment explicit,
        // retaining the ordinary lower-only preparation and resolved-plan path.
        var support=new CrossBeamSupportResult {Input=crossing,CrossingIndex=0,Bearings=new[] {
            new CrossBeamBearing {Beam=0,Row=-1,PrimaryLine=0,Xmm=1225,Ymm=0,ReactionKn=0}}};
        try{return (Func<TransferredSlabOptimizer.Geometry,double,bool>)prepare.Invoke(null,new object[] {input,network,0,probe,new[] {support}});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static long Calls(TowerInput input)
    {
        var found=Regex.Match(input.Trace.Snapshot(),@"Profile PrimaryRecheck: calls=(\d+)");
        Check(found.Success,"hot diagnostic counter unavailable");return long.Parse(found.Groups[1].Value,CultureInfo.InvariantCulture);
    }
    internal static void Run()
    {
        var input=Input();var check=Prepare(input);var geometry=Geometry();
        Check(check(geometry,0),"fixed load plan should pass");long first=Calls(input);
        for(int i=0;i<12;i++)Check(check(geometry,0),"retained passing geometry");
        Check(Calls(input)==first,"exact repeated geometry should avoid member traversal");
        Check(check(geometry,1e-9),"precise changed edge still passes");
        Check(Calls(input)==first+1,"precise edge is part of the key");
        Check(check(Geometry(),0),"equivalent independent geometry still passes");
        Check(Calls(input)==first+2,"geometry identity is part of the key");
        var low=Input(.01);var lowCheck=Prepare(low);var lowGeometry=Geometry();
        Check(!lowCheck(lowGeometry,0),"a new fixed leg-capacity context must reject");long rejected=Calls(low);
        Check(!lowCheck(lowGeometry,0) && Calls(low)==rejected,"exact rejected geometry is retained");
        // Faults are not rejection evidence. Repairing this deliberately invalid
        // test candidate after an exception must execute a fresh check.
        var fault=Geometry();fault.MemberSupports=new double[][] {null};bool threw=false;
        try {check(fault,0);}catch(NullReferenceException){threw=true;}
        Check(threw,"invalid test candidate should throw");fault.MemberSupports=Geometry().MemberSupports;
        Check(check(fault,0),"an exception must not publish a failed whole-geometry entry");
        using(var cancel=new CancellationTokenSource())
        {
            var cancellable=Input(70,cancel.Token);var cancelCheck=Prepare(cancellable);var retained=Geometry();
            Check(cancelCheck(retained,0),"cancellation fixture initially passes");cancel.Cancel();bool cancelled=false;
            try {cancelCheck(retained,0);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cancellation is checked before a retained result");
        }
        var bounded=Input();var boundedCheck=Prepare(bounded);var oldest=Geometry();Check(boundedCheck(oldest,0),"initial eviction fixture");
        for(int i=0;i<4096;i++)Check(boundedCheck(Geometry(),0),"bounded independent candidate");
        long before=Calls(bounded);Check(boundedCheck(oldest,0),"evicted candidate is checked again");
        Check(Calls(bounded)==before+1,"whole-geometry history is bounded to 4096 entries");
        Console.WriteLine("PASS exact receiver geometry/edge reuse, failed results, new leg context, exception retry, cancellation and bounded eviction");
    }
}
