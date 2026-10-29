using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EngineeringDashboard.SteelShape
{
    public sealed partial class SteelShapeWindow : Window
    {
        private readonly WebView2 view = new WebView2();
        private readonly TaskCompletionSource<bool> initialized = new TaskCompletionSource<bool>();
        private readonly Func<Task<CoreWebView2Environment>> environmentFactory;
        private readonly StackPanel fallback;
        private readonly TextBlock status;
        private bool closed;
        private bool browserDisposed;

        public SteelShapeWindow(Func<Task<CoreWebView2Environment>> environmentFactory = null)
        {
            this.environmentFactory = environmentFactory ?? CreateEnvironment;
            Title = "Steel Shape";
            Width = 1440; Height = 960; MinWidth = 760; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.WhiteSmoke;
            Foreground = new SolidColorBrush(Color.FromRgb(32,42,51));
            FontFamily = new FontFamily("Segoe UI");
            Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/MyRevitAddin;component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            var grid = new Grid(); Content = grid;grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());
            var toolbar=new DashboardToolbar(this,"Steel Shape",null,null);toolbar.ToolsMenu.Items.Add(DashboardToolbar.Item("Open in browser",()=>{try{Process.Start(new ProcessStartInfo(SteelShapeContent.WriteBrowserCopy()){UseShellExecute=true});}catch(Exception error){MessageBox.Show(this,error.Message,"Steel Shape");}}));grid.Children.Add(toolbar);
            AutomationProperties.SetAutomationId(view, "SteelShapeBrowser");
            Grid.SetRow(view,1);grid.Children.Add(view);
            fallback = new StackPanel { MaxWidth = 530, Margin = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            status = new TextBlock { Text = "Opening Steel Shape…", FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            fallback.Children.Add(status);
            var open = new Button { Content = "Open in browser", Padding = new Thickness(16, 9, 16, 9), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(open, "SteelShapeOpenInBrowser");
            open.Click += (sender, e) =>
            {
                try { Process.Start(new ProcessStartInfo(SteelShapeContent.WriteBrowserCopy()) { UseShellExecute = true }); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Steel Shape", MessageBoxButton.OK, MessageBoxImage.Information); }
            };
            fallback.Children.Add(open); Grid.SetRow(fallback,1);grid.Children.Add(fallback);
            Loaded += async (sender, e) => await InitializeBrowser();
            Closed += (sender, e) => { closed = true; initialized.TrySetResult(false); DisposeBrowser(); };
        }

        public Task<bool> Initialization => initialized.Task;
        public Exception InitializationError { get; private set; }
        public CoreWebView2 Browser => view.CoreWebView2;

        private static Task<CoreWebView2Environment> CreateEnvironment()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DokaRevitAutomation", "SteelShape", "WebView2", "Revit2024-" + Process.GetCurrentProcess().Id);
            return CoreWebView2Environment.CreateAsync(null, folder);
        }

        private async Task InitializeBrowser()
        {
            try
            {
                var environment = await environmentFactory();
                if (closed) return;
                await view.EnsureCoreWebView2Async(environment);
                if (closed) return;
                view.CoreWebView2.Settings.IsWebMessageEnabled = false;
                view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                bool pageReady = false;
                view.CoreWebView2.NavigationStarting += (sender, e) => { if (pageReady) e.Cancel = true; };
                view.CoreWebView2.NewWindowRequested += (sender, e) => e.Handled = true;
                view.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                view.CoreWebView2.WebResourceRequested += (sender, e) =>
                {
                    if (e.Request.Uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || e.Request.Uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                        e.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Offline", "Content-Type: text/plain");
                };
                var loaded = new TaskCompletionSource<bool>();
                view.CoreWebView2.NavigationCompleted += async (sender, e) =>
                {
                    // WebView2 may finish/cancel its initial empty document after
                    // NavigateToString starts. Only the actual embedded page is ready.
                    if (closed || loaded.Task.IsCompleted) return;
                    if (!e.IsSuccess)
                    {
                        if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                            loaded.TrySetException(new InvalidOperationException("Steel Shape navigation failed: " + e.WebErrorStatus));
                        return;
                    }
                    try
                    {
                        var ready = await view.CoreWebView2.ExecuteScriptAsync("Boolean(document.getElementById('calculator-menu') && window.SteelWeight)");
                        if (ready == "true") { pageReady = true; loaded.TrySetResult(true); }
                    }
                    catch (Exception error) { loaded.TrySetException(error); }
                };
                view.CoreWebView2.NavigateToString(SteelShapeContent.Read());
                if (await Task.WhenAny(loaded.Task, Task.Delay(30000)) != loaded.Task)
                    throw new TimeoutException("Steel Shape did not finish opening.");
                await loaded.Task;
                if (closed) return;
                fallback.Visibility = Visibility.Collapsed;
                initialized.TrySetResult(true);
            }
            catch (Exception error)
            {
                InitializationError = error;
                if (!closed)
                {
                    view.Visibility = Visibility.Collapsed;
                    status.Text = "Steel Shape could not open in this window. You can continue in your browser.";
                    fallback.Visibility = Visibility.Visible;
                    DisposeBrowser();
                }
                initialized.TrySetResult(false);
            }
        }

        private void DisposeBrowser()
        {
            if (browserDisposed) return;
            browserDisposed = true;
            view.Dispose();
        }
    }
}
