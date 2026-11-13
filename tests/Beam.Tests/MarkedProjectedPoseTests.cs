using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class MarkedProjectedPoseTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Marked projected pose: "+message);}
    static TowerInput Input(BeamSearchTrace trace=null)
    {
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,DeflectionDenominator=270,IgnoreUplift=true,Trace=trace??new BeamSearchTrace("marked projected pose")};
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static TransferNetworkResult Network(double depth=1150)=>new TransferNetworkResult {
        Input=new TransferNetworkInput {Beams=new[]{new TransferBeamRow {WidthMm=800,DepthMm=800},new TransferBeamRow {WidthMm=800,DepthMm=800}},Bays=new[]{new TransferBayRow {WidthMm=5400,ThicknessMm=200}},
            CrossingBeams=new[]{new TransferCrossingBeamRow {BayNumber=1,PositionMm=3000,WidthMm=650,DepthMm=depth}}},
        Beams=new[]{new TowerLayout {LengthMm=6000},new TowerLayout {LengthMm=6000}},Bays=new[]{new UpperTransferPlan()},BeamCentresMm=new[]{0.0,6200}};
    static string Original(TowerInput input,TransferNetworkResult network,CrossBeamSupportOptimizer.ProjectedJunctionWork work,double width,double offset,double spacing)
    {
        var method=typeof(CrossBeamSupportOptimizer).GetMethod("ProjectedJunction",BindingFlags.NonPublic|BindingFlags.Static);
        var args=new object[]{input,network,0,spacing,width,offset,0.0,200.0,200.0,Array.Empty<FormworkSolid>(),null,1524.0,CrossShutterAllowance.Standard,null,work};
        try{Check(method.Invoke(null,args)==null,"reference pose is rejected before any rows or primary geometry");return (string)args[10];}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void SortedReplayParity()
    {
        var reference=Input();var input=Input();var network=Network();var oldWork=new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>());
        var work=new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>());var mark=new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0);
        Check(!mark.Computed,"constructing retained descriptors does not evaluate loads or diagnostics");
        // This is the same sorted descriptor/spacing traversal on both paths.
        // Width and offset differ; the central span, bay and shift are identical.
        foreach(double width in new[]{1000.0,1500,2000})foreach(double offset in new[]{0.0,25,-25,50,-50,75,-75,100,-100,125,-125,150,-150})
        foreach(double spacing in new[]{350.0,305,244})
        {
            string expected=Original(reference,network,oldWork,width,offset,spacing);
            Check(CrossBeamSupportOptimizer.TryRejectMarkedProjectedPose(input,network,0,work,mark,width,offset,0,1524,out string actual) && actual==expected,"marked failure preserves each original sorted failure reason and pitch-cache hit");
        }
        foreach(var counter in new[]{BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks,BeamSearchCounter.ProjectedPrimaryScreenFailures,BeamSearchCounter.ProjectedPoseFailuresReused})
            Check(input.Trace.Count(counter)==reference.Trace.Count(counter),"original diagnostic/rejection event counts are preserved: "+counter);
        Check(input.Diagnostics.Exception("same sequence").Details==reference.Diagnostics.Exception("same sequence").Details,"closest failing example, tolerance, count and diagnostic text remain exact");
        Check(input.Trace.Count(BeamSearchCounter.ProjectedPoseForceProofs)==1 && input.Trace.Count(BeamSearchCounter.ProjectedPrimaryScreenFailures)==39,"one physical scalar proof covers39 width/offset checks while preserving39 rejection events");
        Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0 && input.Trace.Count(BeamSearchCounter.LowerAnalyses)==0,"marked rejection performs no FE");
    }
    static void LocalCacheAndScope()
    {
        var input=Input();var network=Network();var work=new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>());var mark=new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0);
        work.PoseFailures.Store(Tuple.Create(1000.0,0.0,0.0,1524.0),"Physical overlap: exact same local obstacle");
        Check(CrossBeamSupportOptimizer.TryRejectMarkedProjectedPose(input,network,0,work,mark,1000,0,0,1524,out string reason) && reason.StartsWith("Physical overlap:") && !mark.Computed,"original obstacle-local cache is consulted first and its reason is retained");
        Check(input.Trace.Count(BeamSearchCounter.ProjectedPoseForceProofs)==0 && input.Trace.Count(BeamSearchCounter.ProjectedPoseFailuresReused)==1,"a local mixed-cache hit never becomes a shared physical proof");
        foreach(int gate in Enumerable.Range(0,4))
        {
            var local=Input();var localMark=new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0);
            switch(gate){case 0:local.ReuseSearchWork=false;break;case 1:local.OptimizeCrossingJunctions=false;break;case 2:local.PrecheckProjectedPrimaries=false;break;
                case 3:local.Trace=new BeamSearchTrace("deadline original path",searchTimeLimit:TimeSpan.FromMinutes(1));break;}
            Check(!CrossBeamSupportOptimizer.TryRejectMarkedProjectedPose(local,network,0,work,localMark,1500,0,0,1524,out _) && !localMark.Computed,"disabled/deadline paths stay on the original invocation, gate "+gate);
        }
        var passing=Input();var passMark=new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0);
        Check(!CrossBeamSupportOptimizer.TryRejectMarkedProjectedPose(passing,Network(1),0,new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>()),passMark,1000,0,0,1524,out _),"a possible pose retains the original full projected crossing search");
        Check(passMark.Computed && passing.Trace.Count(BeamSearchCounter.ProjectedPrimaryScreenFailures)==0,"possible poses supply no failure diagnostic or failure cache");
        using(var cancel=new CancellationTokenSource())
        {
            var cancelled=Input(new BeamSearchTrace("cancelled shared pose",cancel.Token));cancel.Cancel();bool propagated=false;
            try{CrossBeamSupportOptimizer.TryRejectMarkedProjectedPose(cancelled,network,0,work,mark,1000,0,0,1524,out _);}catch(OperationCanceledException){propagated=true;}
            Check(propagated,"local and shared proof reuse cannot bypass cancellation");
        }
        var names=(string[])typeof(BeamSearchTrace).GetField("CounterNames",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(names.Length==Enum.GetValues<BeamSearchCounter>().Length,"the once-per-proof trace counter retains a matching appended label");
    }
    static void ActualSortedBuilder()
    {
        var network=Network();var reference=Input(new BeamSearchTrace("original marked-pose bypass",searchTimeLimit:TimeSpan.FromMinutes(1)));var actual=Input();actual.ReuseCrossingLocalWork=false;
        var method=typeof(CrossBeamSupportOptimizer).GetMethod("SymmetricJunction",BindingFlags.NonPublic|BindingFlags.Static);
        string Run(TowerInput input)
        {
            var args=new object[]{input,network,0,new[]{350.0,305},new[]{1000.0,1500,2000},new[]{0.0,25,-25},200.0,200.0,
                Array.Empty<FormworkSolid>(),Array.Empty<FormworkSolid>(),new System.Collections.Generic.List<CrossBeamBearing>(),Array.Empty<FormworkSolid>(),null,CrossShutterAllowance.Standard,null};
            try{Check(method.Invoke(null,args)==null,"actual sorted projected builder retains the same failed family");return (string)args[14];}
            catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        Check(Run(reference)==Run(actual),"actual sorted builder preserves the final original failure reason");
        Check(reference.Diagnostics.Exception("native sorted family").Details==actual.Diagnostics.Exception("native sorted family").Details,"actual descriptor construction/sorting preserves exact failure sequence diagnostics");
        foreach(var counter in new[]{BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks,BeamSearchCounter.ProjectedPrimaryScreenFailures,BeamSearchCounter.ProjectedPoseFailuresReused})
            Check(actual.Trace.Count(counter)==reference.Trace.Count(counter),"native builder preserves original event counts: "+counter);
        long proofs=actual.Trace.Count(BeamSearchCounter.ProjectedPoseForceProofs),events=actual.Trace.Count(BeamSearchCounter.ProjectedPrimaryScreenFailures);
        Check(proofs>0 && proofs<events && reference.Trace.Count(BeamSearchCounter.ProjectedPoseForceProofs)==0,"native builder shares physical proofs while the original deadline path retains every scalar invocation");
        Console.WriteLine("Native sorted projected family: "+proofs+" shared proofs for "+events+" original scalar rejection events");
    }
    internal static void Run(){SortedReplayParity();LocalCacheAndScope();ActualSortedBuilder();Console.WriteLine("PASS "+checks+" marked projected pose checks: lazy shared proofs, exact sorted rejection/diagnostic/counter replay, local-cache precedence, unknown/pass retention and cancellation");}
}
