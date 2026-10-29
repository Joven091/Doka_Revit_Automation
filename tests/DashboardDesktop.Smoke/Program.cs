using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using static TestSupport;

internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        bool helperMode=args.Length==4&&args[0]=="--helper-update";
        if(args.Length!=2&&!helperMode){Console.Error.WriteLine("Usage: DashboardDesktop.Smoke.exe <production-Doka.EngineeringDashboard.exe> <evidence-directory>\nOr: DashboardDesktop.Smoke.exe --helper-update <initial production folder> <candidate production folder> <evidence-directory>");return 2;}
        if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.Error.WriteLine("Requires native Windows with an interactive desktop and WebView2. No checks were run.");return 2;}
        string executable=Path.GetFullPath(helperMode?Path.Combine(args[1],"Doka.EngineeringDashboard.exe"):args[0]);Output=Path.GetFullPath(helperMode?args[3]:args[1]);Directory.CreateDirectory(Output);
        string root=Path.Combine(Output,"isolated-dashboard-root");Directory.CreateDirectory(root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        object client=null;
        try
        {
            string payload=Path.GetDirectoryName(executable);AppDomain.CurrentDomain.AssemblyResolve+=(sender,eventArgs)=>{string path=Path.Combine(payload,new AssemblyName(eventArgs.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
            Product=Assembly.LoadFrom(Path.Combine(payload,"MyRevitAddin.dll"));Application.ResourceAssembly=Product;
            Check(Product.GetType("EngineeringDashboard.DesktopHost.DashboardSessionController")!=null,"tests use the production desktop-variant assembly");
            using(var peer=new FakeRevitPeer())
            {
                peer.Configure(Environment.SetEnvironmentVariable,root);
                if(helperMode)HelperUpdateChecks.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),peer,root);
                else
                {
                    client=Call(Type("EngineeringDashboard.DesktopHost.RevitBridgeClient"),"FromEnvironment");
                    Call(Type("EngineeringDashboard.DesktopHost.DesktopRuntime"),"Configure",client);
                    NativeSessionChecks.Run(peer,root,client);
                    ((IDisposable)client).Dispose();client=null;
                    ExternalEntryChecks.Run(executable,peer,root);
                }
            }
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="RevitAPI"||a.GetName().Name=="RevitAPIUI"),"native desktop tests do not load Autodesk Revit assemblies");
            File.WriteAllText(Path.Combine(Output,"acceptance.txt"),Checks+" native WPF / WebView2 / external production entry checks passed. A local named-pipe contract peer supplied controlled geometry and auth replies. Actual Revit2024 host, ExternalEvent dispatch, modelling coexistence, helper transactions, company share and installer acceptance remain unrun. No installer, publication or merge was performed.");
            Console.WriteLine(Checks+" native desktop checks passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());return 1;}
        finally
        {
            ((IDisposable)client)?.Dispose();
            foreach(Window window in app.Windows.Cast<Window>().ToArray())
            {
                // Avoid an unsaved-project modal during test failure cleanup.
                try{var model=Get(Type("EngineeringDashboard.LauncherWindow"),"_top50ViewModel");if(model!=null)Set(model,"Dirty",false);window.Close();}catch{}
            }
            app.Shutdown();
        }
    }
}
