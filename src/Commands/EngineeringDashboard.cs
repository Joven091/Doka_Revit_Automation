using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace EngineeringDashboard
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public static UIApplication App;
        private static LauncherWindow launcher;
        private static RevitRequestDispatcher requests;
        internal static Task<T> Request<T>(Func<UIApplication,T> operation)
            =>requests?.Request(operation)??Task.FromException<T>(new InvalidOperationException("Reopen the dashboard from Revit to use this command."));

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!MyRevitAddin.Access.AccessWindow.Require()) return Result.Cancelled;
            bool opening=false;
            try
            {
                App = commandData.Application;

                if(launcher!=null){launcher.ActivateCurrentTool();return Result.Succeeded;}
                opening=true;
                requests=new RevitRequestDispatcher();
                launcher=new LauncherWindow();
                new WindowInteropHelper(launcher){Owner=App.MainWindowHandle};
                launcher.Closed+=(sender,args)=>{requests?.Dispose();requests=null;launcher=null;};
                launcher.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                if(opening){var failed=launcher;launcher=null;requests?.Dispose();requests=null;failed?.Close();}
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
