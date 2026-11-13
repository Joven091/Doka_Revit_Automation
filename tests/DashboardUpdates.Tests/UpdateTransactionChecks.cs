using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EngineeringDashboard.DesktopHost;

internal static class UpdateTransactionChecks
{
    sealed class Fixture
    {
        internal string Root,Checkpoint;
        internal ActiveDashboard Old;
        internal DashboardUpdatePlan Plan;
    }
    internal static void Run(string directory,Action<bool,string> check,Action<Action,string> reject)
    {
        var success=Make(directory,"restart-success");var environment=new Fake{DashboardPollsBeforeExit=2};var status=new List<string>();
        var result=DashboardUpdateTransaction.Apply(success.Plan,environment,CancellationToken.None,status.Add,TimeSpan.FromSeconds(2),TimeSpan.Zero);
        check(result.Version=="2.1.0"&&environment.Starts==1&&environment.Waits==2,"Dashboard update waits only for its old Dashboard, then starts one new payload");
        check(environment.RevitChecks>=4&&environment.RevitStillRunning,"Revit remains alive throughout a successful Dashboard-only update");
        check(environment.FirstExecutable==DashboardPackages.EntryPath(success.Root,result)&&environment.FirstCheckpoint==success.Checkpoint,"restart uses the activated entry and original recovery checkpoint");
        check(File.Exists(success.Checkpoint)&&status.Last().Contains("Revit stayed open"),"successful restart retains recovery work and reports the correct restart scope");
        var cancelled=Make(directory,"cancel-before");var token=new CancellationTokenSource();token.Cancel();
        bool observed=false;try{DashboardUpdateTransaction.Apply(cancelled.Plan,new Fake(),token.Token);}catch(OperationCanceledException){observed=true;}
        check(observed&&DashboardPackages.ReadActive(cancelled.Root).Generation==cancelled.Old.Generation,"cancel before activation preserves the active Dashboard");
        var aborted=Make(directory,"handoff-aborted-before-start");var abortedEnv=new Fake{AbortRequested=true};
        bool abortObserved=false;try{DashboardUpdateTransaction.Apply(aborted.Plan,abortedEnv,CancellationToken.None);}catch(OperationCanceledException){abortObserved=true;}
        check(abortObserved&&abortedEnv.Starts==0&&DashboardPackages.ReadActive(aborted.Root).Generation==aborted.Old.Generation,"aborted handoff cannot activate later when the old Dashboard closes");
        var waitingAbort=Make(directory,"handoff-aborted-while-waiting");var waitingEnv=new Fake{DashboardPollsBeforeExit=100};waitingEnv.OnWait=()=>waitingEnv.AbortRequested=true;
        bool waitAborted=false;try{DashboardUpdateTransaction.Apply(waitingAbort.Plan,waitingEnv,CancellationToken.None,null,TimeSpan.FromSeconds(2),TimeSpan.Zero);}catch(OperationCanceledException){waitAborted=true;}
        check(waitAborted&&waitingEnv.Waits==1&&waitingEnv.Starts==0&&DashboardPackages.ReadActive(waitingAbort.Root).Generation==waitingAbort.Old.Generation,"handoff abort is observed while waiting for Dashboard exit");
        var closed=Make(directory,"revit-closed-before");var closedEnv=new Fake{RevitStillRunning=false};
        reject(()=>DashboardUpdateTransaction.Apply(closed.Plan,closedEnv,CancellationToken.None),"closed original Revit session blocks Dashboard activation");
        check(closedEnv.Starts==0&&DashboardPackages.ReadActive(closed.Root).Generation==closed.Old.Generation,"closed-host refusal launches nothing and preserves the old pointer");
        var tampered=Make(directory,"checkpoint-tamper");File.AppendAllText(tampered.Checkpoint,"changed");var tamperEnv=new Fake();
        reject(()=>DashboardUpdateTransaction.Apply(tampered.Plan,tamperEnv,CancellationToken.None),"modified recovery checkpoint blocks update");
        check(tamperEnv.Starts==0&&DashboardPackages.ReadActive(tampered.Root).Generation==tampered.Old.Generation,"checkpoint refusal occurs before activation");
        foreach(string mismatch in new[]{"nonce","version","pid","start"})
        {
            var wrong=Make(directory,"receipt-"+mismatch);var wrongEnv=new Fake{ReceiptMismatch=mismatch};
            reject(()=>DashboardUpdateTransaction.Apply(wrong.Plan,wrongEnv,CancellationToken.None,null,TimeSpan.FromSeconds(2),TimeSpan.Zero),"startup receipt mismatch rejected: "+mismatch);
            var active=DashboardPackages.ReadActive(wrong.Root);
            check(active.PackageId==wrong.Old.PackageId&&active.Generation!=wrong.Old.Generation&&wrongEnv.Starts==2,"invalid receipt restores and reopens the prior version: "+mismatch);
            check(File.Exists(wrong.Checkpoint),"failed restart preserves the checkpoint: "+mismatch);
        }
        var crashed=Make(directory,"new-child-crash");var crashEnv=new Fake{WriteReceipt=false,NewChildAlive=false};
        reject(()=>DashboardUpdateTransaction.Apply(crashed.Plan,crashEnv,CancellationToken.None,null,TimeSpan.FromSeconds(2),TimeSpan.Zero),"new child exit before receipt rejected");
        check(DashboardPackages.ReadActive(crashed.Root).PackageId==crashed.Old.PackageId&&crashEnv.Starts==2,"startup failure restores and reopens the prior payload");
        var startFail=Make(directory,"start-throws");var startEnv=new Fake{StartThrows=true};
        reject(()=>DashboardUpdateTransaction.Apply(startFail.Plan,startEnv,CancellationToken.None,null,TimeSpan.FromSeconds(2),TimeSpan.Zero),"process-start failure rejected");
        check(DashboardPackages.ReadActive(startFail.Root).PackageId==startFail.Old.PackageId&&File.Exists(startFail.Checkpoint),"process-start exception rolls back without deleting recovery work");
        var blocked=Make(directory,"handoff-obstruction");File.WriteAllText(Path.Combine(blocked.Root,"handoffs"),"regular file obstructs the receipt folder");
        reject(()=>DashboardUpdateTransaction.Apply(blocked.Plan,new Fake(),CancellationToken.None,null,TimeSpan.FromSeconds(2),TimeSpan.Zero),"handoff creation failure is reported");
        check(DashboardPackages.ReadActive(blocked.Root).PackageId==blocked.Old.PackageId,"handoff setup failure cannot leave an unstarted release active");
        var timeout=Make(directory,"startup-timeout");var timeoutEnv=new Fake{WriteReceipt=false,NewChildAlive=true};
        bool timedOut=false;try{DashboardUpdateTransaction.Apply(timeout.Plan,timeoutEnv,CancellationToken.None,null,TimeSpan.Zero,TimeSpan.Zero);}catch(TimeoutException){timedOut=true;}
        check(timedOut&&DashboardPackages.ReadActive(timeout.Root).PackageId==timeout.Old.PackageId&&timeoutEnv.Starts==2,"startup timeout rolls back and restarts the prior Dashboard");
        check(timeoutEnv.CloseRequests==1&&!timeoutEnv.NewChildAlive,"a live unacknowledged child is cooperatively closed before old-version restart");
        var stubborn=Make(directory,"noncooperative-child");var stubbornEnv=new Fake{WriteReceipt=false,NewChildAlive=true,CloseAcknowledged=false};
        bool refused=false;try{DashboardUpdateTransaction.Apply(stubborn.Plan,stubbornEnv,CancellationToken.None,null,TimeSpan.Zero,TimeSpan.Zero);}catch(TimeoutException){refused=true;}
        check(refused&&stubbornEnv.CloseRequests==1&&stubbornEnv.Starts==1&&stubbornEnv.NewChildAlive,"a noncooperative new child does not cause a second Dashboard process to open");
        check(DashboardPackages.ReadActive(stubborn.Root).PackageId==stubborn.Old.PackageId&&File.Exists(stubborn.Checkpoint),"noncooperative-child failure still selects the prior version and retains recovery work");
        var midCancel=Make(directory,"cancel-startup");var midToken=new CancellationTokenSource();var midEnv=new Fake{WriteReceipt=false,OnWait=()=>midToken.Cancel()};
        bool midObserved=false;try{DashboardUpdateTransaction.Apply(midCancel.Plan,midEnv,midToken.Token,null,TimeSpan.FromSeconds(2),TimeSpan.Zero);}catch(OperationCanceledException){midObserved=true;}
        check(midObserved&&midEnv.CloseRequests==1&&midEnv.Starts==2&&DashboardPackages.ReadActive(midCancel.Root).PackageId==midCancel.Old.PackageId,"cancellation after child startup cooperatively closes it and restores the prior version");
        var childAbort=Make(directory,"handoff-aborted-after-start");var childAbortEnv=new Fake{WriteReceipt=false};childAbortEnv.OnWait=()=>childAbortEnv.AbortRequested=true;
        bool childAborted=false;try{DashboardUpdateTransaction.Apply(childAbort.Plan,childAbortEnv,CancellationToken.None,null,TimeSpan.FromSeconds(2),TimeSpan.Zero);}catch(OperationCanceledException){childAborted=true;}
        check(childAborted&&childAbortEnv.CloseRequests==1&&childAbortEnv.Starts==2&&DashboardPackages.ReadActive(childAbort.Root).PackageId==childAbort.Old.PackageId,"handoff abort after startup closes only the owned child and restores the prior payload");

        var valid=Make(directory,"plan-validation");valid.Plan.Validate(valid.Root);check(true,"complete detached restart plan validates");
        var plan=Copy(valid.Plan);plan.SchemaVersion=2;reject(()=>plan.Validate(valid.Root),"future restart-plan schema rejected");
        plan=Copy(valid.Plan);plan.Root=Path.Combine(valid.Root,"other");reject(()=>plan.Validate(valid.Root),"restart root mismatch rejected");
        plan=Copy(valid.Plan);plan.ParentProcessId=0;reject(()=>plan.Validate(valid.Root),"missing Dashboard PID rejected");
        plan=Copy(valid.Plan);plan.ParentStartedUtcTicks=0;reject(()=>plan.Validate(valid.Root),"missing Dashboard process start identity rejected");
        plan=Copy(valid.Plan);plan.RevitProcessId=0;reject(()=>plan.Validate(valid.Root),"missing Revit PID rejected");
        plan=Copy(valid.Plan);plan.RevitStartedUtcTicks=0;reject(()=>plan.Validate(valid.Root),"missing Revit process start identity rejected");
        plan=Copy(valid.Plan);plan.ExpectedGeneration="invalid";reject(()=>plan.Validate(valid.Root),"invalid expected generation rejected");
        plan=Copy(valid.Plan);plan.ParentRelativeDirectory="../elsewhere";reject(()=>plan.Validate(valid.Root),"parent version-directory traversal rejected");
        plan=Copy(valid.Plan);plan.ParentExecutable=Path.Combine(valid.Root,"not-the-running-Dashboard.exe");reject(()=>plan.Validate(valid.Root),"parent executable mismatch rejected");
        plan=Copy(valid.Plan);plan.CheckpointPath=Path.Combine(valid.Root,"outside-sessions.json");reject(()=>plan.Validate(valid.Root),"checkpoint outside the dedicated session folder rejected");
        plan=Copy(valid.Plan);plan.CheckpointSha256=new string('0',64);reject(()=>plan.Validate(valid.Root),"checkpoint SHA mismatch rejected");
    }
    static Fixture Make(string directory,string label)
    {
        string root=Path.Combine(directory,"transactions",label),source=Path.Combine(directory,"transaction-feeds",label);Directory.CreateDirectory(source);
        var old=Stage(source,root,"2.0.0");var activation=DashboardPackages.Activate(root,old,null);var next=Stage(source,root,"2.1.0");
        string sessions=Path.Combine(root,"Sessions");Directory.CreateDirectory(sessions);string checkpoint=Path.Combine(sessions,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(checkpoint,"{\"unsaved\":\"independent recovery witness\"}");
        return new Fixture{Root=root,Checkpoint=checkpoint,Old=activation.Current,Plan=new DashboardUpdatePlan{Root=root,Staged=next,ExpectedGeneration=activation.Current.Generation,ParentProcessId=100,ParentStartedUtcTicks=101,ParentRelativeDirectory=old.RelativeDirectory,ParentExecutable=Path.Combine(root,old.RelativeDirectory.Replace('/',Path.DirectorySeparatorChar),DashboardPackages.EntryFile),RevitProcessId=200,RevitStartedUtcTicks=201,CheckpointPath=checkpoint,CheckpointSha256=IndependentFixtures.HashFile(checkpoint)}};
    }
    static StagedDashboard Stage(string source,string root,string version)
    {
        var names=new[]{IndependentFixtures.Entry,"MyRevitAddin.dll","Doka.DashboardUpdater.exe","google-ortools-native.dll","ortools.dll","BeamSolver/Doka.BeamSolver.exe"};
        var files=names.Select(p=>IndependentFixtures.Item(p,"MZ inert "+version+" "+p)).ToArray();var entries=files.Concat(new[]{IndependentFixtures.Item(DashboardPackages.ManifestFile,IndependentFixtures.Manifest(version,files))});
        string archive=IndependentFixtures.Zip(source,"dashboard-"+version+".zip",entries);var feed=BridgeProtocol.FromJson<DashboardFeed>(IndependentFixtures.Feed(version,Path.GetFileName(archive),IndependentFixtures.HashFile(archive),new FileInfo(archive).Length));return DashboardPackages.Stage(source,root,feed,CancellationToken.None);
    }
    static DashboardUpdatePlan Copy(DashboardUpdatePlan p)=>new DashboardUpdatePlan{SchemaVersion=p.SchemaVersion,Root=p.Root,Staged=p.Staged,ExpectedGeneration=p.ExpectedGeneration,ParentProcessId=p.ParentProcessId,ParentStartedUtcTicks=p.ParentStartedUtcTicks,ParentRelativeDirectory=p.ParentRelativeDirectory,ParentExecutable=p.ParentExecutable,RevitProcessId=p.RevitProcessId,RevitStartedUtcTicks=p.RevitStartedUtcTicks,CheckpointPath=p.CheckpointPath,CheckpointSha256=p.CheckpointSha256};
    sealed class Fake:IDashboardUpdateEnvironment,IDashboardFailedStartupCloser,IDashboardUpdateAbortSignal
    {
        internal int DashboardPollsBeforeExit,Starts,Waits,RevitChecks,CloseRequests;
        internal bool RevitStillRunning=true,WriteReceipt=true,NewChildAlive=true,StartThrows,CloseAcknowledged=true,AbortRequested;
        public bool IsAbortRequested=>AbortRequested;
        internal Action OnWait;
        internal string ReceiptMismatch,FirstExecutable,FirstCheckpoint;
        public bool IsDashboardRunning(DashboardUpdatePlan p)=>DashboardPollsBeforeExit-->0;
        public bool IsRevitRunning(DashboardUpdatePlan p){RevitChecks++;return RevitStillRunning;}
        public DashboardStartedProcess StartDashboard(string executable,string checkpoint,string acknowledgement,string nonce)
        {
            Starts++;if(Starts==1){FirstExecutable=executable;FirstCheckpoint=checkpoint;}
            if(StartThrows)throw new IOException("independent process-start failure");
            var process=new DashboardStartedProcess{ProcessId=300,StartedUtcTicks=301};
            if(WriteReceipt&&acknowledgement!=null)
            {
                var receipt=new DashboardStartupReceipt{Nonce=nonce,Version="2.1.0",ProcessId=process.ProcessId,StartedUtcTicks=process.StartedUtcTicks};
                if(ReceiptMismatch=="nonce")receipt.Nonce="wrong";if(ReceiptMismatch=="version")receipt.Version="9.0.0";if(ReceiptMismatch=="pid")receipt.ProcessId++;if(ReceiptMismatch=="start")receipt.StartedUtcTicks++;
                // JSON produced independently rather than by the production receipt writer.
                File.WriteAllText(acknowledgement,"{\"Nonce\":"+IndependentFixtures.Q(receipt.Nonce)+",\"Version\":"+IndependentFixtures.Q(receipt.Version)+",\"ProcessId\":"+receipt.ProcessId+",\"StartedUtcTicks\":"+receipt.StartedUtcTicks+"}");
            }
            return process;
        }
        public bool IsStartedDashboardRunning(DashboardStartedProcess process)=>NewChildAlive;
        public bool RequestCloseFailedStartup(DashboardStartedProcess process){CloseRequests++;if(CloseAcknowledged)NewChildAlive=false;return CloseAcknowledged;}
        public void Wait(TimeSpan duration,CancellationToken token){Waits++;OnWait?.Invoke();token.ThrowIfCancellationRequested();}
    }
}
