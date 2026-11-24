#if DASHBOARD_DESKTOP
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard.DesktopHost
{
    public static class DashboardUpdateEntry
    {
        static RevitBridgeClient bridge;
        static DesktopUpdateWindow current;
        public static void Configure(RevitBridgeClient client){bridge=client??throw new ArgumentNullException(nameof(client));}
        internal static Task<bool> OpenIntegrationAsync() => bridge.CallAsync<object,bool>("updates.integration", null, CancellationToken.None);
        public static Window Open(Window owner)
        {
            if(bridge==null)throw new InvalidOperationException("Open the Dashboard from Revit before checking for updates.");
            if(current?.IsLoaded==true)
            {
                if(current.WindowState==WindowState.Minimized)current.WindowState=WindowState.Normal;
                if(!current.IsVisible)current.Show();current.Activate();return current;
            }
            var window=new DesktopUpdateWindow(bridge);current=window;
            window.Closed+=(sender,args)=>{if(ReferenceEquals(current,window))current=null;};
            if(owner!=null&&owner.IsLoaded)window.Owner=owner;
            try{window.Show();return window;}
            catch{if(ReferenceEquals(current,window))current=null;throw;}
        }
    }
    public sealed class DesktopUpdateWindow : Window
    {
        readonly RevitBridgeClient bridge;readonly CancellationTokenSource cancel=new CancellationTokenSource();
        readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,12)};
        readonly Button check=new Button{Content="Retry",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,8,0)};
        readonly Button update=new Button{Content="Yes",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,8,0)};
        readonly Button integration=new Button{Content="Revit integration updates",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,8,0)};
        readonly Button close=new Button{Content="Close",IsCancel=true};
        readonly Version installed;DashboardFeed feed;string source;bool busy,closed;
        Task<DashboardFeed> checking;
        public DesktopUpdateWindow(RevitBridgeClient client)
        {
            bridge=client;Title="Engineering Dashboard updates";Width=370;SizeToContent=SizeToContent.Height;MaxHeight=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Background=Brushes.WhiteSmoke;FontFamily=new FontFamily("Segoe UI");FontSize=13;
            Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/MyRevitAddin;component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            installed=DashboardPackages.ParseVersion(FileVersionInfo.GetVersionInfo(typeof(DesktopUpdateWindow).Assembly.Location).FileVersion);
            var panel=new StackPanel{Margin=new Thickness(24)};
            panel.Children.Add(status);
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};update.SetResourceReference(StyleProperty,"PrimaryAction");
            buttons.Children.Add(integration);buttons.Children.Add(check);buttons.Children.Add(update);buttons.Children.Add(close);panel.Children.Add(buttons);Content=panel;
            check.Click+=async(s,e)=>await CheckAsync();update.Click+=async(s,e)=>await UpdateAsync();close.Click+=(s,e)=>Close();
            integration.Click+=async(s,e)=>{if(busy)return;SetBusy(true);try{await bridge.CallAsync<object,bool>("updates.integration",null,cancel.Token);}catch(Exception error){if(!closed)status.Text=error.Message;}finally{SetBusy(false);}};
            Closed+=(s,e)=>{closed=true;cancel.Cancel();};Loaded+=async(s,e)=>await CheckAsync();
        }
        void SetBusy(bool value){busy=value;if(!closed){check.IsEnabled=!value;update.IsEnabled=!value;integration.IsEnabled=!value;}}
        async Task CheckAsync()
        {
            if(busy)return;SetBusy(true);update.Visibility=Visibility.Collapsed;check.Visibility=integration.Visibility=Visibility.Collapsed;close.Content="Close";feed=null;status.Text="Checking for updates…";
            try
            {
                if(checking==null||checking.IsCompleted)checking=Task.Run(()=>{source=MyRevitAddin.Updates.UpdateSettings.Load(DashboardPaths.SettingsRoot).SourceDirectory;return DashboardPackages.ReadFeed(source,installed,BridgeProtocol.Version);});
                if(await Task.WhenAny(checking,Task.Delay(8000,cancel.Token))!=checking){if(!closed){status.Text="Update check timed out. Connect to the company network or VPN and retry.";check.Visibility=Visibility.Visible;}return;}
                feed=await checking;if(closed)return;
                status.Text=feed==null?"No Dashboard update is available.":"A Dashboard update is available. Update now?";
                if(feed!=null){update.Visibility=Visibility.Visible;close.Content="No";}
            }
            catch(FileNotFoundException ex) when (string.Equals(Path.GetFileName(ex.FileName), "dashboard-latest.json", StringComparison.OrdinalIgnoreCase))
            {
                if(!closed)status.Text="No Dashboard update is available.";
            }
            catch(DirectoryNotFoundException){if(!closed){status.Text="Update folder unavailable. Connect to the company network or VPN and retry.";check.Visibility=Visibility.Visible;}}
            catch(Exception error){if(!closed){status.Text=error.Message;check.Visibility=Visibility.Visible;if(error is InvalidDataException)integration.Visibility=Visibility.Visible;}}
            finally{SetBusy(false);}
        }
        async Task UpdateAsync()
        {
            if(busy||feed==null)return;SetBusy(true);update.Visibility=Visibility.Collapsed;close.Content="Close";Process helper=null;bool captured=false;string handoffDirectory=null;
            try
            {
                if(bridge.IsBusy||DashboardSessionController.HasBusyWork)throw new InvalidOperationException("Finish or cancel the current calculation or Revit operation before updating.");
                status.Text="Downloading and verifying the Dashboard update…";
                var staged=await Task.Run(()=>DashboardPackages.Stage(source,DashboardPaths.Root,feed,cancel.Token));cancel.Token.ThrowIfCancellationRequested();
                if(bridge.IsBusy||DashboardSessionController.HasBusyWork)throw new InvalidOperationException("Finish or cancel the current calculation or Revit operation before updating.");
                string checkpoint,error;
                if(!DashboardSessionController.TryPrepareForUpdate(out checkpoint,out error))throw new InvalidOperationException(error);
                captured=true;string executable;
                DashboardUpdatePlan plan;
                using(var self=Process.GetCurrentProcess())
                {
                    executable=self.MainModule.FileName;string folder=Path.GetDirectoryName(executable),prefix=Path.GetFullPath(DashboardPaths.Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!folder.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("This Dashboard is a development preview. Install the reviewed Revit integration and versioned Dashboard before applying an update.");
                    plan=new DashboardUpdatePlan{Root=DashboardPaths.Root,Staged=staged,ExpectedGeneration=DashboardPackages.ReadActive(DashboardPaths.Root)?.Generation,
                        ParentProcessId=self.Id,ParentStartedUtcTicks=self.StartTime.ToUniversalTime().Ticks,ParentExecutable=executable,ParentRelativeDirectory=folder.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar,'/'),
                        RevitProcessId=bridge.HostProcessId,RevitStartedUtcTicks=bridge.HostStartedUtcTicks,CheckpointPath=checkpoint,CheckpointSha256=DashboardPackages.Hash(checkpoint)};
                }
                plan.Validate(DashboardPaths.Root);
                string helperPath=Path.Combine(Path.GetDirectoryName(executable),"Doka.DashboardUpdater.exe");
                var currentManifest=DashboardPackages.ReadJson<DashboardPackageManifest>(Path.Combine(Path.GetDirectoryName(executable),DashboardPackages.ManifestFile),1024*1024);
                var helperFile=currentManifest.Files.Single(f=>f.Path=="Doka.DashboardUpdater.exe");
                if(new FileInfo(helperPath).Length!=helperFile.SizeBytes||DashboardPackages.Hash(helperPath)!=helperFile.Sha256)throw new InvalidDataException("The Dashboard update helper is missing or changed.");
                string directory=Path.Combine(DashboardPaths.Root,"handoffs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);handoffDirectory=directory;
                string path=Path.Combine(directory,"update-plan.json");DashboardPackages.WriteJson(path,plan);
                helper=Process.Start(new ProcessStartInfo(helperPath,"--plan \""+path+"\""){UseShellExecute=false});
                string ready=Path.Combine(directory,"helper-ready.json");var clock=Stopwatch.StartNew();
                while(!File.Exists(ready))
                {cancel.Token.ThrowIfCancellationRequested();if(helper.HasExited||clock.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("The update helper could not start. The Dashboard remains open and your work is retained.");await Task.Delay(100,cancel.Token);}
                if(helper.HasExited)throw new InvalidOperationException("The Dashboard update helper stopped. Your work is retained.");
                if(DashboardPackages.ReadJson<DashboardHelperReadiness>(ready,4096)?.Ready!=true)throw new InvalidDataException("The update helper did not confirm readiness. Your work is retained.");
                DashboardSessionController.BeginUpdateRestart(checkpoint);
                Application.Current.Shutdown();
            }
            catch(Exception error)
            {
                if(captured)DashboardSessionController.CancelUpdateRestart();
                if(handoffDirectory!=null){try{File.WriteAllText(Path.Combine(handoffDirectory,"cancel.json"),"cancelled");}catch(Exception markerError)when(markerError is IOException||markerError is UnauthorizedAccessException){}}
                if(helper!=null){try{if(!helper.HasExited)helper.CloseMainWindow();}catch(InvalidOperationException){}}
                if(!closed){status.Text=error is OperationCanceledException?"Update cancelled.":error.Message;check.Visibility=Visibility.Visible;}
            }
            finally{helper?.Dispose();SetBusy(false);}
        }
    }
}
#endif
