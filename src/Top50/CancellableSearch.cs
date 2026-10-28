using System;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    internal static class CancellableSearch
    {
        internal static T Run<T>(Func<T> solve,Action stop,object stopGate,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if(!cancellation.CanBeCanceled)return solve();
            using(var finished=new ManualResetEventSlim(false))
            {
                Thread interrupter=null;Exception stopFailure=null;
                var registration=cancellation.Register(()=>
                {
                    // OR-Tools can consume StopSearch before creating its native
                    // wrapper. Repeat only after cancellation, until Solve exits.
                    // A dedicated thread avoids waiting for a busy solver pool.
                    interrupter=new Thread(()=>
                    {
                        while(!finished.IsSet)
                        {
                            try {lock(stopGate)stop();}
                            catch(Exception error){Interlocked.CompareExchange(ref stopFailure,error,null);return;}
                            if(finished.Wait(10))return;
                        }
                    }){IsBackground=true,Name="Top50 search cancellation"};
                    interrupter.Start();
                });
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var result=solve();
                    if(cancellation.IsCancellationRequested)
                        throw new OperationCanceledException("Top50 search cancelled.",Volatile.Read(ref stopFailure),cancellation);
                    return result;
                }
                finally
                {
                    finished.Set();
                    // Dispose waits for an in-flight registration callback before
                    // reading/joining its thread and disposing the shared event.
                    registration.Dispose();
                    interrupter?.Join();
                }
            }
        }
    }
}
