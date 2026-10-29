using System;
using Autodesk.Revit.UI;
using MyRevitAddin.Access;

namespace MyRevitAddin
{
    // This module intentionally contains no calculator/native-solver references.
    // Its dispatcher is owned by the Revit session, never by a Dashboard window.
    public sealed class App : IExternalApplication
    {
        internal static EngineeringDashboard.RevitBridgeController Controller;
        public Result OnStartup(UIControlledApplication app)=>Result.Succeeded;
        public Result OnShutdown(UIControlledApplication app){Controller?.Dispose();Controller=null;return Result.Succeeded;}
        internal static EngineeringDashboard.RevitBridgeController Get(UIApplication app)
            =>Controller??(Controller=new EngineeringDashboard.RevitBridgeController(app.MainWindowHandle));
    }
}
namespace EngineeringDashboard
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public sealed class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,Autodesk.Revit.DB.ElementSet elements)
        {
            try{if(!AccessWindow.Require())return Result.Cancelled;MyRevitAddin.App.Get(data.Application).LaunchOrActivate(false);return Result.Succeeded;}
            catch(Exception error){message=error.Message;return Result.Failed;}
        }
    }
}
namespace MyRevitAddin.Updates
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public sealed class UpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,Autodesk.Revit.DB.ElementSet elements)
        {
            try{MyRevitAddin.App.Get(data.Application).LaunchOrActivate(true);return Result.Succeeded;}
            catch(Exception error){message=error.Message;return Result.Failed;}
        }
    }
}
