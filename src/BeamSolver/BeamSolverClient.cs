using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace EngineeringDashboard
{
    // One session per user calculation; combined areas reuse the child runtime.
    // All calls are outside Revit API context and carry detached model data only.
    internal sealed class BeamSolverSession : IDisposable
    {
        readonly object gate=new object();readonly string executable,dotnetHost;
        Process process;string directory;bool unavailable,disposed;readonly StringBuilder errors=new StringBuilder();
        internal BeamSolverSession(string executable=null,string dotnetHost=null)
        {
            this.executable=executable??Path.Combine(Path.GetDirectoryName(typeof(BeamSolverSession).Assembly.Location),"BeamSolver","Doka.BeamSolver.exe");this.dotnetHost=dotnetHost;
        }
        private int ready;
        internal bool IsReady=>Volatile.Read(ref ready)!=0;
        internal int OwnedProcessId {get{try{return process?.Id??0;}catch(InvalidOperationException){return 0;}}}
        static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
        internal BeamSolverReply Solve(BeamSolverRequest request)
        {
            lock(gate)
            {
                var trace=request.Main.Trace;var cancellation=trace?.CancellationToken??CancellationToken.None;cancellation.ThrowIfCancellationRequested();
                if(disposed)throw new ObjectDisposedException(nameof(BeamSolverSession));
                string reason=null;
                foreach(var input in new[] {request.Main}.Concat(request.Independent))
                {
                    if(input==null)throw new ArgumentException("Beam worker inputs must be complete.");
                    if(input.Trace?.HasTimeLimit==true)reason="Explicit timed legacy searches use the existing runtime to preserve their budget.";
                    if(input.TransferLayoutAcceptance!=null || input.TransferLowerAcceptance!=null || input.TransferReceiverPreparation!=null)reason="This specialized search uses in-process acceptance callbacks.";
                    if(input.Trace!=null && !ReferenceEquals(input.Trace,trace))reason="This calculation has independent cancellation/diagnostic scopes.";
                    if(input.NetworkWarmStart!=null || input.NetworkSeedCompletion!=null || input.ReceiverAlternatives!=null || input.NetworkGeometryWork!=null || input.NetworkReceiverWork!=null || input.NetworkPrefixWork!=null || input.DedicatedCrossCache!=null || input.CrossingGeometryWork!=null || input.CrossLowerCases!=null || input.NetworkNumerics!=null || input.NetworkNumericalWork!=null || input.TransferredSlab?.SharedGeometryCache!=null || input.TransferredSlab?.SharedSearchWork!=null)
                        reason="This specialized search carries in-process prepared geometry or warm-start state.";
                }
                if(!unavailable && reason==null)
                {
                    try
                    {
                        Start(cancellation);string id=Guid.NewGuid().ToString("N");string folder=Path.Combine(directory,id);Directory.CreateDirectory(folder);
                        BeamWire.Write(Path.Combine(folder,"request.bin"),request);cancellation.ThrowIfCancellationRequested();process.StandardInput.WriteLine(id);process.StandardInput.Flush();
                        string response=Path.Combine(folder,"response.bin"),progress=Path.Combine(folder,"progress.txt");
                        while(!File.Exists(response))
                        {
                            if(cancellation.IsCancellationRequested){TryCancel(folder);throw new OperationCanceledException(cancellation);}
                            if(process.HasExited)throw new IOException("Beam worker exited unexpectedly ("+process.ExitCode+"). "+errors);
                            ReadProgress(progress,trace);Thread.Sleep(50);
                        }
                        cancellation.ThrowIfCancellationRequested();var reply=BeamWire.Read<BeamSolverReply>(response);
                        trace?.WorkerStatus(".NET 10 worker response received",reply.Diagnostics,runtime:true);
                        if(reply.ErrorKind=="cancelled")
                        {
                            cancellation.ThrowIfCancellationRequested();
                            throw new IOException("The beam worker stopped before completing the calculation.");
                        }
                        if(reply.ErrorKind=="unexpected")throw new IOException("Beam worker search failed: "+reply.Error+"\n"+reply.ErrorDetails);
                        if(reply.Error!=null)ThrowSearchError(reply);
                        if(reply.Main==null || reply.Independent==null || reply.Independent.Length!=request.Independent.Length || request.Main.Network!=null && (reply.Network?.Beams==null || reply.Network.Beams.Length!=request.Main.Network.Beams.Length))throw new InvalidDataException("Beam worker returned an incomplete result.");
                        Validate(reply,request);trace?.ImportWorkerCounters(reply.Counters);
                        trace?.WorkerStatus(".NET 10 worker; complete result returned",reply.Diagnostics,runtime:true);
                        return reply;
                    }
                    catch(OperationCanceledException){Stop();throw;}
                    catch(BeamWorkerSearchException error){error.Rethrow();throw;}
                    catch(Exception error) when(error is IOException || error is InvalidDataException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException || error is ArgumentException || error is TargetInvocationException)
                    {reason=error.GetType().Name+": "+error.Message;unavailable=true;Stop();}
                }
                cancellation.ThrowIfCancellationRequested();trace?.WorkerStatus("Using .NET Framework fallback: "+(reason??"worker unavailable in this calculation"),runtime:true);
                return SolveLocally(request);
            }
        }
        internal static void Validate(BeamSolverReply reply,BeamSolverRequest request)
        {
            if(reply.Main==null || reply.Independent==null || reply.Independent.Length!=request.Independent.Length || (reply.Network==null)!=(request.Main.Network==null))throw new InvalidDataException("Worker result does not match the requested calculation mode.");
            if(reply.Network!=null && (reply.Network.Beams==null || reply.Network.Beams.Length!=request.Main.Network.Beams.Length || reply.Network.Beams.Length==0 || !ReferenceEquals(reply.Main,reply.Network.Beams[0])))throw new InvalidDataException("Worker network/main result identity is incomplete.");
            if(request.Main.TransferredSlab!=null && reply.Main.Transfer==null
                || reply.Network!=null && reply.Network.Beams.Any(layout=>layout?.Transfer==null)
                || request.Independent.Zip(reply.Independent,(input,layout)=>input.TransferredSlab!=null && layout?.Transfer==null).Any(missing=>missing))throw new InvalidDataException("Worker result is missing a required transfer model.");
            if(reply.Counters==null || reply.Counters.Length!=Enum.GetValues(typeof(BeamSearchCounter)).Length)throw new InvalidDataException("Incomplete worker counters.");
            var layouts=(reply.Network?.Beams??new[] {reply.Main}).Concat(reply.Independent);
            foreach(var layout in layouts)
            {
                if(layout==null || layout.PositionsMm==null || layout.PositionsMm.Length<2 || layout.Joined==null || layout.Joined.Length!=layout.PositionsMm.Length-1 || layout.StockPlan?.Pieces==null || layout.StockPlan.Pieces.Length==0
                    || layout.LeftReactions==null || layout.RightReactions==null || layout.LeftReactions.Length!=layout.PositionsMm.Length || layout.RightReactions.Length!=layout.PositionsMm.Length
                    || !TowerBeamAnalysis.Finite(layout.Utilization) || layout.Utilization>1+1e-8 || layout.LeftReactions.Concat(layout.RightReactions).Any(v=>!TowerBeamAnalysis.Finite(v)))throw new InvalidDataException("Incomplete or invalid worker layout.");
                var transfer=layout.Transfer;if(transfer==null)continue;
                if(transfer.RowsMm==null || transfer.PrimaryCases==null || transfer.LowerCases==null || transfer.UpperMembers==null || transfer.CaseNames==null || transfer.PrimaryCases.Length!=transfer.CaseNames.Length || transfer.LowerCases.Length!=transfer.CaseNames.Length || transfer.PrimaryCases.Any(c=>c==null) || transfer.LowerCases.Any(c=>c==null))throw new InvalidDataException("Incomplete worker transfer cases.");
                foreach(var member in transfer.PrimaryCases.SelectMany(c=>c).Concat(transfer.LowerCases.SelectMany(c=>c)).Concat(transfer.UpperMembers))Member(member);
            }
            if(reply.Network!=null)
            {
                if(reply.Network.Input==null || reply.Network.CrossSupports==null || reply.Network.Bays==null || reply.Network.BeamCentresMm==null || reply.Network.Bays.Length!=request.Main.Network.Bays.Length)throw new InvalidDataException("Incomplete worker network.");
                foreach(var cross in reply.Network.CrossSupports)
                {
                    if(cross?.Members==null || cross.Solids==null || cross.Bearings==null)throw new InvalidDataException("Incomplete worker crossing.");
                    foreach(var member in cross.Members)Member(member);
                }
            }
        }
        static void Member(TransferMember member)
        {
            if(member?.Analysis==null || member.SupportsMm==null || member.SupportsMm.Length<2 || member.Points==null || member.Uniform==null || member.Analysis.Reactions==null || member.Analysis.Reactions.Length!=member.SupportsMm.Length
                || !TowerBeamAnalysis.Finite(member.LengthMm) || member.LengthMm<=0 || member.SupportsMm.Concat(member.Analysis.Reactions).Any(value=>!TowerBeamAnalysis.Finite(value))
                || !member.Analysis.DeflectionEvaluated || !TowerBeamAnalysis.Finite(member.Utilization) || member.Utilization>1+1e-8)throw new InvalidDataException("Incomplete worker member response.");
            try{if(member.Analysis.Response(0).Concat(member.Analysis.Response(member.LengthMm)).Any(v=>!TowerBeamAnalysis.Finite(v)))throw new InvalidDataException("Invalid worker diagram coefficients.");}
            catch(Exception error) when(error is InvalidOperationException || error is NullReferenceException || error is ArgumentException || error is IndexOutOfRangeException){throw new InvalidDataException("Incomplete worker diagram coefficients.",error);}
        }
        static void ThrowSearchError(BeamSolverReply reply)=>throw new BeamWorkerSearchException(reply);
        sealed class BeamWorkerSearchException : Exception
        {
            readonly BeamSolverReply reply;internal BeamWorkerSearchException(BeamSolverReply reply):base(reply.Error){this.reply=reply;}
            internal void Rethrow()
            {
                if(reply.ErrorKind=="no-solution")throw new TowerOptimizationException(reply.Error,reply.ErrorDetails??"");
                if(reply.ErrorKind=="input")throw new ArgumentException(reply.Error);
                throw new InvalidOperationException("Beam worker search failed: "+reply.Error+"\n"+reply.ErrorDetails);
            }
        }
        internal static BeamSolverReply SolveLocally(BeamSolverRequest request)
        {
            var result=new BeamSolverReply();
            result.Main=request.Main.Network==null ? TowerOptimizer.Optimize(request.Main) : (result.Network=TransferNetworkOptimizer.Optimize(request.Main,request.Main.Network)).Beams[0];
            result.Independent=new TowerLayout[request.Independent.Length];for(int i=0;i<result.Independent.Length;i++)result.Independent[i]=TransferredSlabOptimizer.Optimize(request.Independent[i]);return result;
        }
        void Start(CancellationToken cancellation)
        {
            if(process!=null && !process.HasExited)return;
            if(!File.Exists(executable))throw new FileNotFoundException("The bundled .NET 10 beam worker is missing.",executable);
            directory=Path.Combine(Path.GetTempPath(),"DokaEngineeringDashboard","BeamSolver",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            using(var owner=Process.GetCurrentProcess())File.WriteAllText(Path.Combine(directory,"owner.txt"),owner.Id+" "+owner.StartTime.ToUniversalTime().Ticks);
            var start=new ProcessStartInfo {FileName=dotnetHost??executable,Arguments=(dotnetHost==null ? "" : Quote(executable)+" ")+"--server "+Quote(directory),UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
            process=new Process {StartInfo=start};process.ErrorDataReceived+=(sender,e)=>{if(e.Data!=null)lock(errors){if(errors.Length<8192)errors.AppendLine(e.Data);}};
            if(!process.Start())throw new IOException("Unable to start the beam worker.");process.BeginErrorReadLine();var watch=Stopwatch.StartNew();string readyPath=Path.Combine(directory,"ready.txt");
            while(!File.Exists(readyPath))
            {
                cancellation.ThrowIfCancellationRequested();if(process.HasExited)throw new IOException("Beam worker failed during startup ("+process.ExitCode+"). "+errors);
                if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new IOException("Beam worker did not complete its startup handshake.");Thread.Sleep(25);
            }
            if(File.ReadAllText(readyPath)!="beam-worker/"+BeamWire.Protocol+"/"+BeamWire.EngineIdentity)throw new InvalidDataException("Beam worker protocol/source identity mismatch.");
            Volatile.Write(ref ready,1);
        }
        static void ReadProgress(string path,BeamSearchTrace trace)
        {
            if(trace==null || !File.Exists(path))return;
            try {using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream))trace.WorkerStatus(reader.ReadToEnd());}catch(IOException){}
        }
        void TryCancel(string folder)
        {
            try {File.WriteAllText(Path.Combine(folder,"cancel.flag"),"");}catch(IOException){}
            if(process!=null && !process.HasExited && !process.WaitForExit(300))Stop();
        }
        void Stop()
        {
            Volatile.Write(ref ready,0);
            if(process!=null){try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}try{process.WaitForExit(1000);}catch(InvalidOperationException){}process.Dispose();process=null;}
        }
        public void Dispose()
        {
            lock(gate)
            {
                if(disposed)return;disposed=true;
                if(process!=null){try{process.StandardInput.Close();if(!process.WaitForExit(500))Stop();}catch(InvalidOperationException){Stop();}catch(IOException){Stop();}finally{Stop();}}
                if(directory!=null)try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
}
