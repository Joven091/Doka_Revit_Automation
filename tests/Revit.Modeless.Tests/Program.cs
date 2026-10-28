using System;
using System.Linq;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static bool Failed<T>(Task<T> task,string text)
    {try {task.GetAwaiter().GetResult();return false;}catch(Exception error){return error.Message.Contains(text);}}
    private static void Main()
    {
        int scheduled=0,reads=0;
        var queue=new RevitRequestQueue<string>(()=>scheduled++);
        var first=queue.Request(context=>{reads++;return context;});
        Check(!first.IsCompleted && reads==0 && scheduled==1,"Revit data is not accessed from the modeless button handler");
        var duplicate=queue.Request(context=>"unexpected");Check(Failed(duplicate,"in progress") && scheduled==1,"duplicate request is rejected without rescheduling");
        queue.Execute("active document");Check(first.Result=="active document" && reads==1,"callback supplies the active API context");
        queue.Execute("another document");Check(reads==1,"a repeated callback cannot replay an operation");
        var broken=queue.Request<string>(context=>throw new InvalidOperationException("reader failed"));queue.Execute("context");Check(Failed(broken,"reader failed"),"reader exception reaches the UI task");
        var retry=queue.Request(context=>42);queue.Execute("context");Check(retry.Result==42,"a failed operation releases the request slot");
        Task<int> nested=null;var outer=queue.Request(context=>{nested=queue.Request(c=>1);return 2;});queue.Execute("context");
        Check(outer.Result==2 && Failed(nested,"in progress"),"reentrant operation cannot enter the Revit API twice");queue.Dispose();
        Check(Failed(queue.Request(context=>1),"disposed"),"closed dashboard cannot submit another operation");
        bool deny=true;queue=new RevitRequestQueue<string>(()=>{if(deny)throw new InvalidOperationException("Revit denied event");});
        Check(Failed(queue.Request(context=>1),"denied"),"event refusal is reported immediately");deny=false;retry=queue.Request(context=>7);queue.Execute("context");Check(retry.Result==7,"event refusal does not strand the next request");queue.Dispose();
        queue=new RevitRequestQueue<string>(()=>{});reads=0;var abandoned=queue.Request(context=>++reads);queue.Dispose();queue.Execute("context");
        Check(Failed(abandoned,"disposed") && reads==0,"closing with an event pending cancels it before model access");
        queue=new RevitRequestQueue<string>(()=>{});var tasks=Enumerable.Range(0,16).Select(i=>Task.Run(()=>queue.Request(c=>i))).ToArray();
        System.Threading.SpinWait.SpinUntil(()=>tasks.Count(t=>t.IsCompleted)==15,5000);
        queue.Execute("context");Task.WaitAll(tasks.Select(t=>t.ContinueWith(_=>{})).ToArray());
        Check(tasks.Count(t=>t.Status==TaskStatus.RanToCompletion)==1 && tasks.Count(t=>t.IsFaulted)==15,"simultaneous callers cannot overwrite a pending Revit operation");queue.Dispose();
        Console.WriteLine(checks+" modeless Revit request lifecycle checks passed.");
    }
}
