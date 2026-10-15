using System;
using System.Windows;
using System.Windows.Controls;
using EngineeringDashboard;

internal static class StandalonePreview
{
    internal static int Run()
    {
        var application=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
        var window=new BeamCalculatorWindow {DataContext=new BeamCalculatorViewModel()};
        var import=(Button)window.FindName("ImportButton");
        if(import!=null)import.Visibility=Visibility.Collapsed;
        window.Title="Beam calculator · standalone preview";
        application.Run(window);return 0;
    }
}
