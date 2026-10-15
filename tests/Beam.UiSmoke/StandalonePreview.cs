using System;
using System.Windows;
using System.Windows.Controls;
using EngineeringDashboard;

internal static class StandalonePreview
{
    internal static int Run(bool smoke=false)
    {
        var application=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
        var window=new BeamCalculatorWindow {DataContext=new BeamCalculatorViewModel()};
        var import=(Button)window.FindName("ImportButton");
        if(import!=null)import.Visibility=Visibility.Collapsed;
        window.Title="Beam calculator · standalone preview";
        if(smoke)window.Loaded+=(s,e)=>window.Dispatcher.BeginInvoke(new Action(()=>
        {
            ((Button)window.FindName("PickTowersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(window.FormworkWorkspace==null || window.FormworkWorkspace.Buttons["Calculate"]==null)throw new InvalidOperationException("Standalone D3 workspace did not open.");
            window.Close();
        }));
        application.Run(window);return 0;
    }
}
