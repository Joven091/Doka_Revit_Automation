using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EngineeringDashboard;

// An ordinary test executable supplies an entry assembly and masks the resource
// lookup failure seen in native Revit. Loading into a fresh domain does not.
public sealed class NativeHostResourceChecks : MarshalByRefObject
{
    public static void Run()
    {
        var domain = AppDomain.CreateDomain("Native Revit resource lookup", null,
            new AppDomainSetup { ApplicationBase = AppDomain.CurrentDomain.BaseDirectory });
        try
        {
            var checks = (NativeHostResourceChecks)domain.CreateInstanceAndUnwrap(
                typeof(NativeHostResourceChecks).Assembly.FullName,
                typeof(NativeHostResourceChecks).FullName);
            checks.RunOnStaThread();
        }
        finally { AppDomain.Unload(domain); }
    }

    public void RunOnStaThread()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Assembly.GetEntryAssembly() != null)
                    throw new Exception("Regression host must have no managed entry assembly.");
                var model = new BeamCalculatorViewModel();
                var window = new BeamCalculatorWindow { DataContext = model };
                try
                {
                    window.Show();
                    ((Button)window.FindName("PickTowersButton")).RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                    window.UpdateLayout();
                    if (!model.HasSelectedSupportSystem || !model.UseTowers ||
                        ((Grid)window.FindName("CalculatorWorkspace")).Visibility != Visibility.Visible)
                        throw new Exception("D3 selection did not reveal the calculator.");
                    if (!(window.FindResource("PrimaryAction") is Style))
                        throw new Exception("Shared calculator styles did not load.");
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Native-host D3 resource check failed.", failure);
    }
}
