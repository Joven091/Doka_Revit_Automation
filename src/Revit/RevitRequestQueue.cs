using System;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    // No Revit objects leave Execute. Scheduling is injected so the lifecycle can
    // be checked without pretending that a test runner is a Revit API context.
    internal sealed class RevitRequestQueue<TContext> : IDisposable
    {
        private readonly Action schedule;
        private readonly object gate=new object();
        private Action<TContext> pending;
        private Action<Exception> fail;
        private bool running,disposed;
        internal RevitRequestQueue(Action schedule) { this.schedule=schedule; }
        internal Task<T> Request<T>(Func<TContext,T> operation)
        {
            var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock(gate)
            {
                if(disposed) {completion.SetException(new ObjectDisposedException("Revit dashboard"));return completion.Task;}
                if(pending!=null || running) {completion.SetException(new InvalidOperationException("Another Revit operation is in progress. Try again when it finishes."));return completion.Task;}
                pending=context=> {try {completion.TrySetResult(operation(context));}catch(Exception error){completion.TrySetException(error);}};
                fail=error=>completion.TrySetException(error);
                try {schedule();}
                catch(Exception error){pending=null;fail(error);fail=null;}
            }
            return completion.Task;
        }
        internal void Execute(TContext context)
        {
            Action<TContext> action;
            lock(gate){if(disposed || pending==null)return;action=pending;pending=null;running=true;}
            try {action(context);}
            finally {lock(gate){running=false;fail=null;}}
        }
        public void Dispose()
        {
            lock(gate)
            {
                disposed=true;pending=null;
                if(!running){fail?.Invoke(new ObjectDisposedException("Revit dashboard"));fail=null;}
            }
        }
    }
}
