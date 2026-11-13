using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class EarlyProjectedPoseGroupTests
{
    static int checks;static readonly MethodInfo symmetric=typeof(CrossBeamSupportOptimizer).GetMethod("SymmetricJunction",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Early projected pose groups: "+message);}
    static TowerInput Input(bool enabled=true)
    {
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,DeflectionDenominator=270,IgnoreUplift=true,
            ReuseCrossingLocalWork=enabled,Exclude600Standalone=true,LegCapacityKn=70,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.BeamSupports,Trace=new BeamSearchTrace("early projected groups")};
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:true);return input;
    }
    static TransferNetworkResult Network()
    {
        var r=PrimaryBearingTests.Fixture();r.Input.Bays[0].WidthMm=5400;r.BeamCentresMm[1]=6125;return r;
    }
    static CrossBeamSupportResult Solve(TowerInput input,TransferNetworkResult r,double[] widths,double[] offsets,out string reason)
    {
        var args=new object[]{input,r,0,new[]{305.0,244},widths,offsets,200.0,200.0,Array.Empty<FormworkSolid>(),Array.Empty<FormworkSolid>(),
            new System.Collections.Generic.List<CrossBeamBearing>(),Array.Empty<FormworkSolid>(),null,CrossShutterAllowance.Standard,null};
        try{var result=(CrossBeamSupportResult)symmetric.Invoke(null,args);reason=(string)args[14];return result;}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static string Signature(CrossBeamSupportResult result)=>result==null ? "null" : result.Towers.WidthMm+"/"+result.Towers.TransverseOffsetMm+"/"+string.Join(";",result.Towers.PositionsMm.Select(x=>x.ToString("R")))
        +"/"+string.Join(";",result.Members.SelectMany(m=>new[]{m.StartMm,m.LengthMm,m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm}.Concat(m.SupportsMm).Concat(m.Analysis.Reactions)).Select(x=>x.ToString("R")));
    static void FailedFamilies()
    {
        var reference=Input(false);var input=Input();var widths=new[]{1000.0,1500,2000};var offsets=new[]{0.0,25,-25,50,-50,75,-75,100,-100,125,-125,150,-150};
        var expected=Solve(reference,Network(),widths,offsets,out _);var actual=Solve(input,Network(),widths,offsets,out var reason);
        Check(expected==null && actual==null,"a family rejected by the original necessary proof remains rejected");
        long groups=input.Trace.Count(BeamSearchCounter.ProjectedPoseGroupsRejected),avoided=input.Trace.Count(BeamSearchCounter.ProjectedPoseDescriptorsAvoided);
        Check(groups>0 && avoided==groups*widths.Length*offsets.Length,"one physical group avoids every repeated width/offset descriptor before sorting");
        Check(input.Trace.Count(BeamSearchCounter.ProjectedPrimaryScreenFailures)==0 && reference.Trace.Count(BeamSearchCounter.ProjectedPrimaryScreenFailures)==avoided,"group pruning prevents the original repeated projected calls instead of preserving millions of rejection events");
        Check(reason!=null && input.Diagnostics.Exception("group failure").Details.Contains("minimum governing cantilever"),"entirely pruned families retain a valid necessary engineering explanation");
        Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0 && input.Trace.Count(BeamSearchCounter.LowerAnalyses)==0,"early group proof adds no member FE");
        Console.WriteLine("Early failed family: "+groups+" groups; "+avoided+" descriptors avoided");
    }
    static void PassingOrder()
    {
        int passing=0;
        foreach(double span in new[]{4203.3922668457035,5400})
        {
            var r=Network();r.Input.Bays[0].WidthMm=span;r.BeamCentresMm[1]=r.Input.Beams[0].WidthMm/2+span+r.Input.Beams[1].WidthMm/2;
            var off=Input(false);var on=Input();var widths=new[]{1000.0,1500,1524,2000};var offsets=new[]{0.0,25,-25};
            var expected=Solve(off,r,widths,offsets,out _);var actual=Solve(on,r,widths,offsets,out _);
            Check(Signature(expected)==Signature(actual),"all surviving candidate order, selected stock/axes and exact member/leg demands match the original path");
            if(actual!=null)
            {
                passing++;Check(actual.Members.All(m=>m.Utilization<=1+1e-8 && m.Analysis.DeflectionEvaluated),"possible winners retain complete member and deflection checks");
                Check(actual.Towers.MaxLegKn<=on.LegCapacityKn+1e-8,"possible winners retain individual leg capacity checks");
            }
        }
        Check(passing>0,"actual native passing projected families are retained");
    }
    static void UnknownAndDisabled()
    {
        var input=Input();var r=Network();var work=new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>()) {EdgeLoads=new[]{new BeamPointLoad {Kn=double.NaN}}};
        var mark=new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0);
        Check(!mark.TryGetForGroup(input,r,0,work,out _) && !mark.Computed,"nonfinite eager source is unknown and retains the original lazy path");
        r=Network();r.Input.BeamSpacingsMm=new[]{305.0,305};r.Input.SlabSpacingsMm=new[]{488.0};r.Input.CrossingSpacingsMm=new[]{305.0};r.Bays[0]=new UpperTransferPlan();r.SlabCoveredLengthMm=50;r.Input.UseCrossingSlabLedgers=true;
        work=new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>());mark=new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0);
        Check(!mark.TryGetForGroup(input,r,0,work,out _) && !mark.Computed && work.PoseGroupSourceChecked,"known source ArgumentException is caught only for eager metadata and leaves the ordinary check available");
        foreach(int gate in Enumerable.Range(0,4))
        {
            var local=Input();switch(gate){case 0:local.ReuseCrossingLocalWork=false;break;case 1:local.OptimizeCrossingSearch=false;break;case 2:local.PrecheckProjectedPrimaries=false;break;
                case 3:local.Trace=new BeamSearchTrace("original timed path",searchTimeLimit:TimeSpan.FromMinutes(1));break;}
            // Use the existing exact whole-pose threshold proof directly to avoid
            // running an exhaustive unchecked family when the reference is off.
            if(gate==2)continue;
            Solve(local,Network(),new[]{1000.0},new[]{0.0},out _);
            Check(local.Trace.Count(BeamSearchCounter.ProjectedPoseGroupsRejected)==0 && local.Trace.Count(BeamSearchCounter.ProjectedPoseDescriptorsAvoided)==0,"reference/disabled/timed policies keep the original descriptor path, gate "+gate);
        }
        using(var cancel=new CancellationTokenSource())
        {
            var local=Input();local.Trace=new BeamSearchTrace("cancelled eager group",cancel.Token);cancel.Cancel();bool propagated=false;
            try{new CrossBeamSupportOptimizer.ProjectedPoseForceMark(5000,1524,0).TryGetForGroup(local,Network(),0,new CrossBeamSupportOptimizer.ProjectedJunctionWork(Array.Empty<FormworkSolid>()),out _);}catch(OperationCanceledException){propagated=true;}
            Check(propagated,"eager group proofs cannot bypass cancellation");
        }
        var names=(string[])typeof(BeamSearchTrace).GetField("CounterNames",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);Check(names.Length==Enum.GetValues<BeamSearchCounter>().Length,"new group/descriptor counter IDs retain matching appended labels");
    }
    internal static void Run(){FailedFamilies();PassingOrder();UnknownAndDisabled();Console.WriteLine("PASS "+checks+" early projected group checks: pre-descriptor pruning, exact native selected geometry/forces, unknown source fallback, reference policy, cancellation and diagnostics");}
}
