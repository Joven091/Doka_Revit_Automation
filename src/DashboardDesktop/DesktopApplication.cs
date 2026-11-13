#if DASHBOARD_DESKTOP
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace EngineeringDashboard.DesktopHost
{
    public static class DashboardDesktopApplication
    {
        public static int Run(string[] args)
        {
            RevitBridgeClient client=null;RegisteredWaitHandle activation=null,updateActivation=null;EventWaitHandle signal=null,updateSignal=null;
            try
            {
                bool updates=args.Length==1&&args[0]=="--updates";
                string checkpoint=null;
                if(!updates&&args.Length>0)
                {if(args.Length!=2||args[0]!="--restore-session")throw new ArgumentException("The Dashboard startup arguments are invalid.");checkpoint=args[1];}
                var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                client=RevitBridgeClient.FromEnvironment();DesktopRuntime.Configure(client);
                var hello=DesktopRuntime.Wait(client.HelloAsync());
                if(hello==null||hello.ProtocolVersion!=BridgeProtocol.Version)throw new InvalidOperationException("This Dashboard needs a compatible Revit integration update. Close and reopen Revit after installing it.");
                if(checkpoint!=null)updates=DashboardSessionController.IsUpdatesOnlyCheckpoint(checkpoint);
                Window window;
                if(updates)window=DashboardUpdateEntry.Open(null);
                else
                {
                    if(!DesktopRuntime.Wait(client.CallAsync<object,bool>("access.ensure",null)))return 0;
                    window=new LauncherWindow();
                }
                app.MainWindow=window;
                var startupWindow=window;
                string activationName=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_ACTIVATE_EVENT");
                if(!string.IsNullOrEmpty(activationName))
                {
                    if(!activationName.StartsWith("Local\\DokaDashboardActivate-",StringComparison.Ordinal))throw new InvalidOperationException("Invalid Dashboard activation event.");
                    signal=EventWaitHandle.OpenExisting(activationName);
                    activation=ThreadPool.RegisterWaitForSingleObject(signal,(state,timedOut)=>DispatchActivation(app,"Dashboard",()=>
                    {
                        if(startupWindow is LauncherWindow launcher)launcher.ActivateCurrentTool();
                        else if(!DashboardSessionController.HasBusyWork&&MyRevitAddin.Access.AccessWindow.Require(startupWindow))
                        {
                            // An update-only start must not make the normal Dashboard ribbon feel stuck.
                            var previous=startupWindow;var dashboard=new LauncherWindow();
                            app.MainWindow=dashboard;startupWindow=dashboard;updates=false;dashboard.Show();
                            previous.Owner=dashboard;previous.Hide();dashboard.ActivateCurrentTool();
                        }
                        else{if(startupWindow.WindowState==WindowState.Minimized)startupWindow.WindowState=WindowState.Normal;startupWindow.Activate();}
                    }),null,Timeout.Infinite,false);
                }
                string updateEventName=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_UPDATE_EVENT");
                if(!string.IsNullOrEmpty(updateEventName))
                {
                    if(!updateEventName.StartsWith("Local\\DokaDashboardUpdates-",StringComparison.Ordinal))throw new InvalidOperationException("Invalid Dashboard update event.");
                    updateSignal=EventWaitHandle.OpenExisting(updateEventName);
                    updateActivation=ThreadPool.RegisterWaitForSingleObject(updateSignal,(state,timedOut)=>DispatchActivation(app,"Updates",()=>
                    {
                        Window owner=startupWindow is LauncherWindow dashboard?dashboard.CurrentTool??dashboard:startupWindow;
                        var opened=DashboardUpdateEntry.Open(owner);TraceActivation("Update window visible="+opened.IsVisible+"; enabled="+opened.IsEnabled);
                    }),null,Timeout.Infinite,false);
                }
                TraceActivation("Activation registrations ready.");
                bool ready=false;
                startupWindow.Loaded+=(sender,e)=>
                {
                    if(ready)return;ready=true;
                    try{if(checkpoint!=null)DashboardSessionController.Restore(checkpoint);StartupAcknowledgement.WriteFromEnvironment();}
                    catch(Exception error){Log(error);app.Shutdown(1);}
                };
                app.DispatcherUnhandledException+=(sender,e)=>
                {Log(e.Exception);e.Handled=true;MessageBox.Show(e.Exception.Message,"Engineering Dashboard",MessageBoxButton.OK,MessageBoxImage.Error);};
                if(updates&&startupWindow.IsLoaded){ready=true;if(checkpoint!=null)DashboardSessionController.Restore(checkpoint);StartupAcknowledgement.WriteFromEnvironment();}
                return app.Run(window);
            }
            catch(Exception error)
            {Log(error);MessageBox.Show(error.Message,"Engineering Dashboard",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
            finally{activation?.Unregister(null);updateActivation?.Unregister(null);signal?.Dispose();updateSignal?.Dispose();client?.Dispose();}
        }
        static void DispatchActivation(Application app,string kind,Action action)
        {
            TraceActivation(kind+" signal received.");
            try
            {
                if(app.Dispatcher.HasShutdownStarted||app.Dispatcher.HasShutdownFinished)return;
                app.Dispatcher.BeginInvoke(new Action(()=>{TraceActivation(kind+" signal dispatched.");action();}));
            }
            catch(InvalidOperationException error){Log(error);}
        }
        static void TraceActivation(string message)
        {
            try
            {
                string directory=Path.Combine(DashboardPaths.Root,"Logs");Directory.CreateDirectory(directory);string path=Path.Combine(directory,"activation.log");
                if(File.Exists(path)&&new FileInfo(path).Length>65536)File.Delete(path);
                File.AppendAllText(path,DateTime.UtcNow.ToString("O")+" "+message+Environment.NewLine);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        static void Log(Exception error)
        {
            try{string root=Path.Combine(DashboardPaths.Root,"Logs");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"desktop-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt"),error.ToString());}catch{}
        }
    }
}
#endif
