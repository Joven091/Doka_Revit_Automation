using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyRevitAddin.Updates;

namespace EngineeringDashboard
{
    internal sealed class IntegrationUpdateWindow : Window
    {
        static IntegrationUpdateWindow current;
        internal static void Open(IntPtr owner)
        {
            if(current!=null&&current.IsLoaded){if(current.WindowState==WindowState.Minimized)current.WindowState=WindowState.Normal;current.Activate();return;}
            current=new IntegrationUpdateWindow();new System.Windows.Interop.WindowInteropHelper(current){Owner=owner};
            current.Closed+=(sender,args)=>current=null;current.Show();
        }
        readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        readonly TextBlock status=new TextBlock {Margin=new Thickness(0,16,0,10)};
        readonly TextBlock notes=new TextBlock {Margin=new Thickness(0,0,0,16)};
        readonly Button check=new Button {Content="Check again",Margin=new Thickness(0,0,10,0)};
        readonly Button install=new Button {Content="Install after Revit closes",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,10,0)};
        readonly ProgressBar progress=new ProgressBar {Height=5,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,16)};
        readonly string installation=Path.GetDirectoryName(typeof(IntegrationUpdateWindow).Assembly.Location);
        readonly Version installed=UpdateFiles.ParseVersion(FileVersionInfo.GetVersionInfo(typeof(IntegrationUpdateWindow).Assembly.Location).FileVersion);
        Task<UpdateManifest> checking;
        UpdateSettings settings;
        UpdateManifest release;
        bool closed,busy;

        internal IntegrationUpdateWindow()
        {
            Title="Doka Revit Automation · updates";Width=560;SizeToContent=SizeToContent.Height;MaxHeight=700;
            ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=Brushes.WhiteSmoke;

            var body=new StackPanel {Margin=new Thickness(24)};
            body.Children.Add(new TextBlock {Text="Revit integration updates",FontSize=22,FontWeight=FontWeights.SemiBold});
            body.Children.Add(new TextBlock {Text="Revit 2024 · installed version "+UpdateFiles.DisplayVersion(installed),Foreground=Brushes.SlateGray,Margin=new Thickness(0,6,0,0)});
            body.Children.Add(status);body.Children.Add(new ScrollViewer {Content=notes,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});body.Children.Add(progress);
            var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};
            var close=new Button {Content="Close",IsCancel=true};close.Click+=(s,e)=>Close();
            install.Background=Brushes.Gold;buttons.Children.Add(check);buttons.Children.Add(install);buttons.Children.Add(close);body.Children.Add(buttons);Content=body;
            check.Click+=async(s,e)=>await CheckAsync();install.Click+=async(s,e)=>await StageAsync();
            Closed+=(s,e)=>{closed=true;cancellation.Cancel();};Loaded+=async(s,e)=>await CheckAsync();
        }
        async Task CheckAsync()
        {
            if(busy)return;busy=true;check.IsEnabled=false;install.Visibility=Visibility.Collapsed;notes.Text="";status.Text="Checking the company update folder…";
            try
            {
                // Keep at most one outstanding network read, even if Windows is still resolving an offline share.
                if(checking==null || checking.IsCompleted)
                    checking=Task.Run(()=>{settings=UpdateSettings.Load(installation);return UpdateFiles.Check(settings.SourceDirectory,installed);});
                if(await Task.WhenAny(checking,Task.Delay(TimeSpan.FromSeconds(8),cancellation.Token))!=checking)
                {if(!closed)status.Text="The company folder is not responding. Connect to the company network or VPN, then check again.";return;}
                release=await checking;if(closed)return;
                status.Text=release==null ? "You have the latest published version." : "Version "+release.Version+" is available.";
                if(release!=null)
                {
                    notes.Text=(release.ReleaseNotes??"")+"\n\nThe update will wait until you close all your Revit windows. Save your work as usual before closing Revit.";
                    install.Visibility=Visibility.Visible;
                }
            }
            catch(Exception ex){if(!closed)status.Text=FriendlyError(ex);}
            finally{busy=false;if(!closed)check.IsEnabled=true;}
        }
        async Task StageAsync()
        {
            if(busy || release==null)return;busy=true;check.IsEnabled=false;install.IsEnabled=false;progress.Visibility=Visibility.Visible;
            status.Text="Preparing the update locally…";
            try
            {
                string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins","2024");
                if(!UpdateFiles.SamePath(installation,expected))throw new InvalidOperationException("This installation is managed in a different folder. Ask your administrator to update it.");
                using(var host=Process.GetCurrentProcess())
                {
                    var plan=new UpdatePlan {InstalledAssemblyFile="Doka.RevitBridge.dll",Release=release,InstallDirectory=installation,HostProcessId=host.Id,HostStartedUtcTicks=host.StartTime.ToUniversalTime().Ticks};
                    string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaRevitAutomation","Updates");
                    var reporter=new Progress<double>(value=>{if(!closed)progress.Value=value;});
                    string stage=await Task.Run(()=>UpdateFiles.Stage(settings.SourceDirectory,cache,Path.Combine(installation,"Doka.RevitUpdater.exe"),plan,cancellation.Token,
                        (done,total)=>((IProgress<double>)reporter).Report(100.0*done/total)));
                    cancellation.Token.ThrowIfCancellationRequested();
                    using(var helper=Process.Start(new ProcessStartInfo(Path.Combine(stage,"Doka.RevitUpdater.exe")) {UseShellExecute=false,Arguments="--plan \""+Path.Combine(stage,"update-plan.json")+"\""})){}
                    status.Text="Update prepared. The updater will install it after you close Revit.";install.Visibility=Visibility.Collapsed;check.Visibility=Visibility.Collapsed;
                }
            }
            catch(OperationCanceledException){}
            catch(Exception ex){if(!closed)status.Text=FriendlyError(ex);}
            finally{busy=false;if(!closed){check.IsEnabled=true;install.IsEnabled=true;progress.Visibility=Visibility.Collapsed;}}
        }
        static string FriendlyError(Exception ex)
        {
            if(ex is FileNotFoundException || ex is DirectoryNotFoundException)return "No published update was found. Connect to the company network or ask the release owner to publish the latest installer.";
            if(ex is UnauthorizedAccessException)return "You do not have access to the company update folder. Ask IT for read access.";
            if(ex is System.Runtime.Serialization.SerializationException)return "The published update information could not be read. Ask the release owner to check latest.json.";
            return ex.Message;
        }
    }

}
