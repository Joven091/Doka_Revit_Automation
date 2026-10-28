using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool startupCheck=args.Length==2&&args[0]=="--startup-check";
        try
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
            var model=new Top50ViewModel();
            app.DispatcherUnhandledException+=(sender,e)=>
            {
                e.Handled=true;model.RecordDiagnosticError("Standalone application",e.Exception);
                ReportFailure(e.Exception,startupCheck);app.Shutdown(1);
            };
            if(!startupCheck&&args.Length>0)
            {
                if(args.Length!=1)throw new ArgumentException("Open one .top50 project or .dxf drawing at a time.");
                string path=Path.GetFullPath(args[0]);
                if(string.Equals(Path.GetExtension(path),".top50",StringComparison.OrdinalIgnoreCase))
                    model.Open(path,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Doka","Top50StandaloneTester","SourceCache"));
                else if(string.Equals(Path.GetExtension(path),".dxf",StringComparison.OrdinalIgnoreCase))model.Import(path);
                else throw new ArgumentException("Choose a .top50 project or a .dxf drawing.");
                model.CurrentPage=model.Selected==null?0:2;
            }
            var window=new Top50Window(model)
            {
                Title="Top50 Planner — standalone tester "+Assembly.GetExecutingAssembly().GetName().Version.ToString(3),
                WindowStartupLocation=WindowStartupLocation.CenterScreen
            };
            // CI launches the real portable entry point without leaving a window running.
            if(startupCheck)window.Loaded+=(sender,e)=>window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                if(!window.IsVisible||!model.BuildDiagnosticReport().Contains("No input imported or added"))
                    throw new InvalidOperationException("The standalone planner did not initialize correctly.");
                File.WriteAllText(Path.GetFullPath(args[1]),window.Title+Environment.NewLine+"Empty planner and diagnosis ready.");
                window.Close();
            }));
            return app.Run(window);
        }
        catch(Exception error){ReportFailure(error,startupCheck);return 1;}
    }
    private static void ReportFailure(Exception error,bool silent)
    {
        string saved="";
        try
        {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Doka","Top50StandaloneTester","Logs");
            Directory.CreateDirectory(directory);saved=Path.Combine(directory,"startup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt");
            File.WriteAllText(saved,error.ToString());
        }
        catch{ /* Retain a visible error even if the local log directory is unavailable. */ }
        if(!silent)MessageBox.Show(error.Message+(saved.Length==0?"":"\n\nDetails saved to:\n"+saved),"Top50 Planner — standalone tester",MessageBoxButton.OK,MessageBoxImage.Error);
    }
}
