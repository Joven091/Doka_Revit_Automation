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
        readonly TextBlock notes=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
        readonly Button check=new Button{Content="Check again",Margin=new Thickness(0,0,8,0)};
        readonly Button update=new Button{Content="Update Dashboard",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,8,0)};
        readonly Button integration=new Button{Content="Revit integration updates",Margin=new Thickness(0,0,8,0)};
        readonly Version installed;DashboardFeed feed;string source;bool busy,closed;
        Task<DashboardFeed> checking;
        public DesktopUpdateWindow(RevitBridgeClient client)
        {
            bridge=client;Title="Engineering Dashboard updates";Width=620;SizeToContent=SizeToContent.Height;MaxHeight=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Background=Brushes.WhiteSmoke;FontFamily=new FontFamily("Segoe UI");FontSize=13;
            Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/MyRevitAddin;component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            installed=DashboardPackages.ParseVersion(FileVersionInfo.GetVersionInfo(typeof(DesktopUpdateWindow).Assembly.Location).FileVersion);
            var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="Dashboard updates",FontSize=22,FontWeight=FontWeights.SemiBold});
            panel.Children.Add(new TextBlock{Text="Installed version "+installed.ToString(3)+" · Revit stays open",Foreground=Brushes.SlateGray,Margin=new Thickness(0,6,0,0)});
            panel.Children.Add(status);panel.Children.Add(new ScrollViewer{Content=notes,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};update.SetResourceReference(StyleProperty,"PrimaryAction");
            buttons.Children.Add(integration);buttons.Children.Add(check);buttons.Children.Add(update);var close=new Button{Content="Close",IsCancel=true};buttons.Children.Add(close);panel.Children.Add(buttons);Content=panel;
            check.Click+=async(s,e)=>await CheckAsync();update.Click+=async(s,e)=>await UpdateAsync();close.Click+=(s,e)=>Close();
            integration.Click+=async(s,e)=>{if(busy)return;SetBusy(true);try{await bridge.CallAsync<object,bool>("updates.integration",null,cancel.Token);}catch(Exception error){if(!closed)status.Text=error.Message;}finally{SetBusy(false);}};
            Closed+=(s,e)=>{closed=true;cancel.Cancel();};Loaded+=async(s,e)=>await CheckAsync();
        }
        void SetBusy(bool value){busy=value;if(!closed){check.IsEnabled=!value;update.IsEnabled=!value;integration.IsEnabled=!value;}}
        async Task CheckAsync()
        {
            if(busy)return;SetBusy(true);update.Visibility=Visibility.Collapsed;notes.Text="";status.Text="Checking the company Dashboard update folder…";
            try
            {
                if(checking==null||checking.IsCompleted)checking=Task.Run(()=>{source=MyRevitAddin.Updates.UpdateSettings.Load(DashboardPaths.SettingsRoot).SourceDirectory;return DashboardPackages.ReadFeed(source,installed,BridgeProtocol.Version);});
                if(await Task.WhenAny(checking,Task.Delay(8000,cancel.Token))!=checking){if(!closed)status.Text="The company folder is not responding. Connect to the company network or VPN, then check again.";return;}
                feed=await checking;if(closed)return;
                status.Text=feed==null?"Your Dashboard is up to date.":"Dashboard "+feed.Version+" is available.";
                if(feed!=null){notes.Text=(feed.ReleaseNotes??"")+"\n\nOnly the Dashboard will close and reopen. Your work will be captured first. Revit and its models stay open; updated calculation results may need recalculation.";update.Visibility=Visibility.Visible;}
            }
            catch(FileNotFoundException){if(!closed)status.Text="No Dashboard-only update has been published yet. Revit integration updates use the separate button.";}
            catch(DirectoryNotFoundException){if(!closed)status.Text="Connect to the company network or VPN to check for updates.";}
            catch(Exception error){if(!closed)status.Text=error.Message;}
            finally{SetBusy(false);}
        }
        async Task UpdateAsync()
        {
            if(busy||feed==null)return;SetBusy(true);Process helper=null;bool captured=false;string handoffDirectory=null;
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
                DashboardSessionController.BeginUpdateRestart(checkpoint);
                Application.Current.Shutdown();
            }
            catch(Exception error)
            {
                if(captured)DashboardSessionController.CancelUpdateRestart();
                if(handoffDirectory!=null){try{File.WriteAllText(Path.Combine(handoffDirectory,"cancel.json"),"cancelled");}catch(Exception markerError)when(markerError is IOException||markerError is UnauthorizedAccessException){}}
                if(helper!=null){try{if(!helper.HasExited)helper.CloseMainWindow();}catch(InvalidOperationException){}}
                if(!closed)status.Text=error is OperationCanceledException?"Update cancelled. Your work is retained.":error.Message;
            }
            finally{helper?.Dispose();SetBusy(false);}
        }
    }
}
#endif
