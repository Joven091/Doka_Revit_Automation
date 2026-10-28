using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace EngineeringDashboard
{
    internal sealed class RevitRequestDispatcher : IExternalEventHandler,IDisposable
    {
        private readonly RevitRequestQueue<UIApplication> requests;
        private readonly ExternalEvent externalEvent;
        private bool disposed;
        // Constructed by IExternalCommand.Execute, which is a valid API context.
        internal RevitRequestDispatcher()
        {
            externalEvent=ExternalEvent.Create(this);
            requests=new RevitRequestQueue<UIApplication>(()=>
            {
                var result=externalEvent.Raise();
                if(result!=ExternalEventRequest.Accepted)throw new InvalidOperationException("Revit could not accept the request ("+result+"). Finish the current Revit command and try again.");
            });
        }
        internal Task<T> Request<T>(Func<UIApplication,T> operation)=>requests.Request(operation);
        public void Execute(UIApplication app)=>requests.Execute(app);
        public string GetName()=>"Engineering Dashboard model operations";
        public void Dispose(){if(disposed)return;disposed=true;requests.Dispose();externalEvent.Dispose();}
    }
}
