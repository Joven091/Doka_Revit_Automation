using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class NativeExternalWidthProofTests
{
    static int checks;
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
    static readonly MethodInfo Solve=typeof(TransferNetworkOptimizer).GetMethod("SolveCertified",BindingFlags.NonPublic|BindingFlags.Static);
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception("Native external width proof: "+message);}
    static FormworkSolid Obstacle(double y0,double y1)=>new FormworkSolid {Name="actual external column",X0=-100000,X1=100000,Y0=y0,Y1=y1,Z0=-1000000,Z1=-2000};
    static TowerInput Common()
    {
        var common=ReportedScenarioTests.DiagnosticPart(1,2).Item2.CreateTowerInput();
        common.CoordinateCrossingLayouts=true;common.LocalNetworkSearch=true;common.FirstCompleteNetworkOnly=false;
        common.Trace=new BeamSearchTrace("native external missing receiver widths");
        common.Diagnostics=new TowerSearchDiagnostics(common.DeflectionDenominator,trace:common.Trace,ignoreUplift:common.IgnoreUplift);
        // Only actual leg solids below the head meet these columns. Width 1524
        // remains clear while all three other centred widths meet a column.
        common.ExternalObstacles=new[] {Obstacle(470,530),Obstacle(710,721),Obstacle(970,1030),Obstacle(10000,10001)};
        common.ExternalObstacleIndex=null;common.NetworkReceiverWork=new NetworkReceiverCache();return common;
    }
    static TowerLayout Actual(TowerInput common,int ceiling,Dictionary<double,TowerLayout> alternatives,double? width=null,int failedThrough=0)
    {
        var network=common.Network;double edge=(network.Bays[0].WidthMm-3800)/2;
        var slab=new UpperTransferPlan {StockLengthMm=3800,EdgeMm=edge,NearFootMm=edge+650,FarFootMm=edge+650,IsCustomLength=true};
        try{return (TowerLayout)Solve.Invoke(null,new object[] {common,network,0,null,slab,5800.0,0.0,new[] {new[] {5800.0}},
            new TransferredSlabOptimizer.GeometryCache(),ceiling,width,0.0,alternatives,null,failedThrough});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static TowerInput BoundInput(TransferNetworkOptimizer.ReceiverWidthAlternatives alternatives)
    {
        var binding=alternatives.GetType().GetField("nativeExternal",Hidden).GetValue(alternatives);
        Check(binding!=null,"only the native SolveReceiverCore external AND installs a private binding");
        return (TowerInput)binding.GetType().GetField("input",Hidden).GetValue(binding);
    }
    static void ActualNativeAndMutations()
    {
        var common=Common();var alternatives=new TransferNetworkOptimizer.ReceiverWidthAlternatives();
        var selected=Actual(common,int.MaxValue,alternatives);
        Check(selected!=null && selected.WidthMm==1524 && selected.Transfer.EngineerReceiverCount==8,"the actual reported receiver completes count eight with native engineering, junction and external checks");
        Check(alternatives.Count==1 && alternatives.ContainsKey(1524),"actual external columns exclude all other centred native widths");
        var input=BoundInput(alternatives);int ceiling=selected.LegCount;
        Check(input.ExternalObstacleIndex!=null && !ReferenceEquals(input.ExternalObstacleIndex,common.ExternalObstacleIndex),"the native wrapper builds its own initially-null receiving index");
        Check(alternatives.ThroughLegs==ceiling,"actual unrestricted completion publishes its exhausted station subset");
        foreach(double width in new[] {1000.0,1500,2000})
        {
            Check(alternatives.Rejects(width,ceiling,common.Trace),"a missing external width can consume its same-call proof");
            Check(!alternatives.Rejects(width,int.MaxValue,common.Trace),"higher station counts stay searchable after a partial proof");
            var reference=Common();var original=Actual(reference,ceiling,null,width);
            Check(original==null,"the ordinary fixed-width native search independently fails at the certified budget");
        }
        Check(CrossBeamSupportOptimizer.ExternalLayoutFits(input,selected)
            && CrossBeamSupportOptimizer.CheckEngineerReceiver(input,common.Network,0,selected)
            && CrossBeamSupportOptimizer.CheckJunctionReceiver(input,common.Network,0,selected),"the selected receiver passes the original external and crossing checks again");
        // The receiving search retains provisional primary cases; the native
        // engineer/junction callbacks above recheck resolved copies. Compare
        // with the original fixed-width solve rather than treating provisional
        // full crossing loads as the final resolved load assignment.
        var nativeReference=Actual(Common(),int.MaxValue,null,1524);
        Check(nativeReference!=null && selected.PositionsMm.SequenceEqual(nativeReference.PositionsMm)
            && selected.StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(nativeReference.StockPlan.Pieces.Select(p=>p.StockLengthMm))
            && selected.MomentKnM==nativeReference.MomentKnM && selected.ShearKn==nativeReference.ShearKn && selected.Utilization==nativeReference.Utilization,
            "the ordinary native fixed-width solve selects the same geometry, ranking and full numerical response");

        void Mutation(Action change,Action restore,string label)
        {
            change();Check(alternatives.FailedThroughStations(1000,ceiling,common.Trace)==0,label+" invalidates proof consumption");
            Check(!alternatives.Complete(common,input,selected,true,false),label+" invalidates proof publication");
            restore();Check(alternatives.Complete(common,input,selected,true,false),label+" restoration leaves the exact original completed proof available");
        }
        var solids=common.ExternalObstacles;var solid=solids[0];
        var fields=typeof(FormworkSolid).GetFields(BindingFlags.Public|BindingFlags.Instance);
        foreach(string name in new[] {"X0","X1","Y0","Y1","Z0","Z1"})
        {
            var field=fields.Single(f=>f.Name==name);double saved=(double)field.GetValue(solid);
            Mutation(()=>field.SetValue(solid,Math.BitIncrement(saved)),()=>field.SetValue(solid,saved),"one ULP "+name);
        }
        Mutation(()=>solid.IsLeg=true,()=>solid.IsLeg=false,"leg flag");
        Mutation(()=>solid.IsTowerEnvelope=true,()=>solid.IsTowerEnvelope=false,"tower-envelope flag");
        Mutation(()=>solid.TowerOwner="different owner",()=>solid.TowerOwner=null,"tower ownership");
        Mutation(()=>{solids[0]=solids[1];solids[1]=solid;},()=>{solids[1]=solids[0];solids[0]=solid;},"obstacle order");
        var copy=Obstacle(solid.Y0,solid.Y1);
        Mutation(()=>solids[0]=copy,()=>solids[0]=solid,"equal-valued replacement solid");
        Mutation(()=>common.ExternalObstacles=(FormworkSolid[])solids.Clone(),()=>common.ExternalObstacles=solids,"common obstacle array identity");
        Mutation(()=>input.ExternalObstacles=(FormworkSolid[])solids.Clone(),()=>input.ExternalObstacles=solids,"input obstacle array identity");
        foreach(var target in new[] {common,input})foreach(string name in new[] {"ObstacleOriginXmm","ObstacleCentreYmm","ObstacleBottomMm"})
        {
            var field=typeof(TowerInput).GetField(name,Hidden);double saved=(double)field.GetValue(target);
            Mutation(()=>field.SetValue(target,Math.BitIncrement(saved)),()=>field.SetValue(target,saved),"exact "+(ReferenceEquals(target,common) ? "common " : "input ")+name);
        }
        var acceptance=input.TransferLayoutAcceptance;var lower=input.TransferLowerAcceptance;var preparation=input.TransferReceiverPreparation;var geometryFilter=input.TransferGeometryFilter;
        Mutation(()=>input.TransferLayoutAcceptance=l=>acceptance(l),()=>input.TransferLayoutAcceptance=acceptance,"equal-behavior replacement acceptance callback");
        Mutation(()=>input.TransferLowerAcceptance=l=>lower==null || lower(l),()=>input.TransferLowerAcceptance=lower,"replacement lower callback");
        Mutation(()=>input.TransferReceiverPreparation=l=>preparation==null ? null : preparation(l),()=>input.TransferReceiverPreparation=preparation,"replacement prepared callback");
        Mutation(()=>input.TransferGeometryFilter=(t,g,e)=>geometryFilter==null ? g : geometryFilter(t,g,e),()=>input.TransferGeometryFilter=geometryFilter,"replacement geometry-filter callback");
        Mutation(()=>input.ReceiverAlternatives=new Dictionary<double,TowerLayout>(alternatives),()=>input.ReceiverAlternatives=alternatives,"another width collector");
        var index=input.ExternalObstacleIndex;
        Mutation(()=>input.ExternalObstacleIndex=new FormworkObstacleIndex(solids),()=>input.ExternalObstacleIndex=index,"injected equal-array obstacle index");
        Mutation(()=>input.ExternalObstacleIndex=null,()=>input.ExternalObstacleIndex=index,"cleared native obstacle index");
        Mutation(()=>common.ExternalObstacleIndex=new FormworkObstacleIndex(solids),()=>common.ExternalObstacleIndex=null,"changed common index");
        Check(!alternatives.Complete(common.CopyForSearch(),input,selected,true,false),"another common input cannot borrow the native binding");
        Check(!alternatives.Complete(common,input.CopyForSearch(),selected,true,false),"another local input cannot borrow the native binding");
        Check(alternatives.Complete(common,input,selected,true,false),"original references remain authenticated");
        var fake=common.CopyForSearch();fake.TransferredSlab=input.TransferredSlab;fake.EngineerPlan=input.EngineerPlan;fake.NetworkLegCeiling=input.NetworkLegCeiling;
        var unknown=new TransferNetworkOptimizer.ReceiverWidthAlternatives();unknown[1524]=selected;fake.ReceiverAlternatives=unknown;
        unknown.BindNativeExternal(new object());
        Check(!unknown.Complete(fake,fake,selected,true,false),"an arbitrary caller or opaque object cannot authenticate an external callback");
        // White-box constructor checks cannot be reached by a normal caller:
        // the binding type itself is private to TransferNetworkOptimizer.
        var bindingType=typeof(TransferNetworkOptimizer).GetNestedType("NativeExternalWidthProof",BindingFlags.NonPublic);
        var injected=input.CopyForSearch();var injectedAlternatives=new TransferNetworkOptimizer.ReceiverWidthAlternatives();
        injected.ReceiverAlternatives=injectedAlternatives;injectedAlternatives[1524]=selected;
        injected.ExternalObstacleIndex=new FormworkObstacleIndex(solids);
        var rejectedBinding=Activator.CreateInstance(bindingType,Hidden,null,new object[] {common,injected},null);
        injected.TransferLayoutAcceptance=(Func<TowerLayout,bool>)bindingType.GetField("Acceptance",Hidden).GetValue(rejectedBinding);
        bindingType.GetMethod("SealCallbacks",Hidden).Invoke(rejectedBinding,null);injectedAlternatives.BindNativeExternal(rejectedBinding);
        injected.TransferLayoutAcceptance(selected);
        Check(!injectedAlternatives.Complete(common,injected,selected,true,false),"an index present before binding cannot be certified by invoking the native wrapper");
        var cached=typeof(NetworkReceiverCache).GetField("entries",Hidden).GetValue(common.NetworkReceiverWork);
        var entries=(System.Collections.IDictionary)cached.GetType().GetField("entries",Hidden).GetValue(cached);
        Check(entries.Count==0,"the whole receiver-search cache remains disabled for external-obstacle inputs");
        using(var source=new CancellationTokenSource())
        {
            var trace=new BeamSearchTrace("cancelled native width proof",source.Token);source.Cancel();bool cancelled=false;
            try{alternatives.Rejects(1000,ceiling,trace);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"external same-call proof consumption still observes cancellation");
        }
        // Replacing an index while the native callback executes permanently
        // loses provenance, even if a caller later reinstalls the old reference.
        input.ExternalObstacleIndex=new FormworkObstacleIndex(solids);acceptance(selected);input.ExternalObstacleIndex=index;
        Check(!alternatives.Complete(common,input,selected,true,false),"a wrapper-observed injected index cannot be relabelled native afterward");
    }
    internal static void Run()
    {
        MissingReceiverWidthProofTests.Run();ActualNativeAndMutations();
        Console.WriteLine("PASS "+checks+" native external missing-width attestation checks: actual count-eight receiver, ordinary fixed-width parity, immutable snapshots, callback/index provenance and original adaptive priority");
    }
}
