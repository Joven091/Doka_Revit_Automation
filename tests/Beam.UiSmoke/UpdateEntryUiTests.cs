using System;
using System.Windows;
using System.Windows.Controls;
using EngineeringDashboard;

internal static class UpdateEntryUiTests
{
    internal static void Run()
    {
        var model=new BeamCalculatorViewModel();int opened=0;
        var revit=new BeamFormworkWorkspace(model,null,null,null,checkForUpdates:()=>opened++);
        if(!revit.Buttons.ContainsKey("CheckUpdates"))throw new Exception("Revit workspace update entry missing");
        revit.Buttons["CheckUpdates"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if(opened!=1)throw new Exception("Update entry did not call Revit host");
        revit.Close();
        var standalone=new BeamFormworkWorkspace(model,null,null,null);
        if(standalone.Buttons.ContainsKey("CheckUpdates"))throw new Exception("Revit updater appeared in standalone preview");
        standalone.Close();Console.WriteLine("PASS: Revit update entry routes to host; standalone has no updater");
    }
}
