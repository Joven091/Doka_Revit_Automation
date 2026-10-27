using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EngineeringDashboard;
using EngineeringDashboard.SteelShape;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL " + message); checks++; Console.WriteLine("PASS " + message); }

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try
        {
            var html = SteelShapeContent.Read();
            Check(html.Contains("<title>Steel Shape</title>"), "embedded page has approved Steel Shape branding");
            Check(!html.Contains(">Engineering Dashboard<"), "Steel Shape page has no dashboard heading");
            var browserCopy = SteelShapeContent.WriteBrowserCopy();
            Check(File.ReadAllText(browserCopy, Encoding.UTF8) == html, "browser fallback contains the exact embedded page");
            using (var hash = SHA256.Create())
                File.WriteAllText(Path.Combine(output, "embedded-html.sha256"), BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(html))).Replace("-", "").ToLowerInvariant());

            var launcher = new LauncherWindow(); launcher.Show(); Pump();
            var buttons = Find<Button>((DependencyObject)launcher.Content).ToArray();
            Check(buttons.Single(b => b.Name == "BtnSteelShape").IsEnabled, "Steel Shape dashboard card is enabled");
            Check(AutomationProperties.GetName(buttons.Single(b => b.Name == "BtnSteelShape")) == "Steel Shape", "dashboard card has an accessible Steel Shape label");
            Check(buttons.Single(b => b.Name == "BtnTop50").IsEnabled, "Top50 remains available as a planner");
            Check(!buttons.Single(b => b.Name == "BtnDomeCalculator").IsEnabled, "Dome remains locked Coming soon");

            var window = new SteelShapeWindow { Owner = launcher }; window.Show();
            Wait(window.Initialization);
            Check(window.Initialization.Result, "actual WPF WebView2 opens the embedded page: " + window.InitializationError);
            Check(window.Title == "Steel Shape", "window title is Steel Shape");
            Func<string, bool> js = expression => { var task = window.Browser.ExecuteScriptAsync("Boolean(" + expression + ")"); Wait(task); return task.Result == "true"; };
            Check(js("document.querySelectorAll('[data-shape]').length===16"), "all sixteen steel shapes are available in the embedded browser");
            Check(js("document.getElementById('mass').textContent==='6.66' && document.getElementById('total').textContent==='39.97'"), "embedded browser retains approved RHS reference result");
            Check(js("typeof BigInt==='function' && document.querySelector('.navigation h2').textContent==='Steel Shape'"), "WebView2 supports exact arithmetic and approved navigation branding");
            Check(js("!document.querySelector('main').innerText.includes('Engineering Dashboard') && !Array.from(document.querySelectorAll('button')).some(b=>/^Calculate$/i.test(b.textContent.trim()))"), "removed headings and Calculate control stay absent");
            Run(window, "window.__steelMain=document.getElementById('section-drawing').innerHTML; const tick=document.getElementById('weld-enabled');tick.checked=true;tick.dispatchEvent(new Event('change',{bubbles:true}));const z=document.getElementById('weld-z');z.value='4';z.dispatchEvent(new Event('input',{bubbles:true}));");
            Check(js("document.getElementById('weld-lengthMm').value==='292.27' && document.getElementById('total').textContent==='39.98'"), "optional weld updates automatic length and combined mass in WebView2");
            Check(js("document.getElementById('section-drawing').innerHTML===window.__steelMain"), "optional weld retains original section schematic");
            Run(window, "document.getElementById('weld-clear').click();");
            Check(js("document.getElementById('weld-lengthMm').value==='0.00' && document.getElementById('total').textContent==='39.97'"), "Clear restores steel-only mass");
            Run(window, "document.querySelector('[data-weld-edge=\"edge-0\"]').dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}));");
            Check(js("document.getElementById('weld-lengthMm').value==='41.00'"), "keyboard activates the selected weld edge");
            Run(window, "const full=document.getElementById('weld-edge-full');full.checked=false;full.dispatchEvent(new Event('change',{bubbles:true}));const length=document.getElementById('weld-edge-length');length.value='25.5';length.dispatchEvent(new Event('input',{bubbles:true}));");
            Check(js("document.getElementById('weld-lengthMm').value==='25.50'"), "partial weld length updates in the installed browser engine");
            using (var stream = File.Create(Path.Combine(output, "steel-shape-webview2.png")))
                Wait(window.Browser.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream));
            File.WriteAllText(Path.Combine(output, "browser-version.txt"), window.Browser.Environment.BrowserVersionString);
            window.Close();

            var unavailable = new SteelShapeWindow(() => Task.FromException<CoreWebView2Environment>(new InvalidOperationException("Controlled missing-runtime fixture"))) { Owner = launcher };
            unavailable.Show(); Wait(unavailable.Initialization);
            Check(!unavailable.Initialization.Result && unavailable.InitializationError != null, "missing embedded-browser runtime produces a recoverable state");
            var fallback = Find<Button>((DependencyObject)unavailable.Content).Single(b => AutomationProperties.GetAutomationId(b) == "SteelShapeOpenInBrowser");
            Check(fallback.IsVisible && fallback.IsEnabled, "browser fallback is visible and usable after initialization failure");
            unavailable.Close(); launcher.Close();
            File.WriteAllText(Path.Combine(output, "host-checks.txt"), checks + " WPF/WebView2 integration checks passed. Windows test host only; actual Revit2024 host acceptance remains unrun.");
            Console.WriteLine(checks + " Steel Shape WPF/WebView2 checks passed."); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { app.Shutdown(); }
    }

    private static void Run(SteelShapeWindow window, string script) { Wait(window.Browser.ExecuteScriptAsync("(()=>{" + script + "})()")); }
    private static void Wait(Task task)
    { var end = DateTime.UtcNow.AddSeconds(60); while (!task.IsCompleted) { if (DateTime.UtcNow > end) throw new TimeoutException("Steel Shape operation did not finish"); Pump(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); Pump(); }
    private static void Pump()
    { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { if (root is T item) yield return item; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
