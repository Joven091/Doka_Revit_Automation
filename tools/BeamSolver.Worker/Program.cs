using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using EngineeringDashboard;
internal static class Program
{
    static int Main(string[] args)
    {
        if(args.Length!=2 || args[0]!="--server")return 2;
        string root=Path.GetFullPath(args[1]);if(!Directory.Exists(root))return 2;
        try
        {
            var ownerData=File.ReadAllText(Path.Combine(root,"owner.txt")).Split(' ');
            using var owner=Process.GetProcessById(int.Parse(ownerData[0],CultureInfo.InvariantCulture));
            // Windows reports stable FILETIME ticks. Linux derives StartTime from
            // the boot clock; tolerate its sub-second conversion variation.
            long expectedStart=long.Parse(ownerData[1],CultureInfo.InvariantCulture);
            if(Math.Abs(owner.StartTime.ToUniversalTime().Ticks-expectedStart)>TimeSpan.FromSeconds(1).Ticks)throw new InvalidDataException("The beam worker owner process changed before startup.");
            File.WriteAllText(Path.Combine(root,"ready.tmp"),"beam-worker/"+BeamWire.Protocol+"/"+BeamWire.EngineIdentity);File.Move(Path.Combine(root,"ready.tmp"),Path.Combine(root,"ready.txt"));
            using var requests=BeamWire.RequestReader(Console.OpenStandardInput());
            string id;
            while((id=requests.ReadLine())!=null)
            {
                if(!Guid.TryParseExact(id,"N",out _)){Console.Error.WriteLine("Invalid beam worker request identifier (length "+id.Length+").");return 3;}
                string folder=Path.Combine(root,id);var reply=new BeamSolverReply();BeamSearchTrace trace=null;
                using(var cancel=new CancellationTokenSource())
                {
                    var monitor=new Timer(_=>
                    {
                        try
                        {
                            if(owner.HasExited || File.Exists(Path.Combine(folder,"cancel.flag")))cancel.Cancel();
                            File.WriteAllText(Path.Combine(folder,"progress.txt"),trace?.ProgressText??"Preparing the calculation");
                        }
                        catch(Exception error)
                        {
                            try{cancel.Cancel();}catch(ObjectDisposedException){}
                            try{Console.Error.WriteLine("Beam worker monitor: "+error.Message);}catch(IOException){}
                        }
                    },null,0,250);
                    try
                    {
                    try
                    {
                        var request=BeamWire.Read<BeamSolverRequest>(Path.Combine(folder,"request.bin"));
                        trace=new BeamSearchTrace("Detached .NET 10 worker",cancel.Token);request.Main.Trace=trace;foreach(var input in request.Independent)input.Trace=trace;
                        reply=BeamSolverSession.SolveLocally(request);cancel.Token.ThrowIfCancellationRequested();trace.Complete("Succeeded",summary:reply.Network?.Summary??reply.Main.Summary);
                    }
                    catch(OperationCanceledException error){reply.ErrorKind="cancelled";reply.Error=error.Message;trace?.Complete("Cancelled");}
                    catch(TowerOptimizationException error){reply.ErrorKind="no-solution";reply.Error=error.Message;reply.ErrorDetails=error.Details;trace?.Complete("No solution",error.ToString()+"\n"+error.Details);}
                    catch(ArgumentException error){reply.ErrorKind="input";reply.Error=error.Message;reply.ErrorDetails=error.ToString();trace?.Complete("Rejected input/search",error.ToString());}
                    catch(Exception error){reply.ErrorKind="unexpected";reply.Error=error.Message;reply.ErrorDetails=error.ToString();trace?.Complete("Unexpected error",error.ToString());}
                    reply.Diagnostics=trace?.Snapshot();reply.Counters=trace?.ExportWorkerCounters()??Array.Empty<long>();
                    }
                    finally {monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();}
                }
                if(owner.HasExited)return 0;
                BeamWire.Write(Path.Combine(folder,"response.tmp"),reply);File.Move(Path.Combine(folder,"response.tmp"),Path.Combine(folder,"response.bin"));
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
