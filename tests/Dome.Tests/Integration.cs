using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard.Dome;

static partial class Program
{
    private static void Integration()
    {
        using var approvedSession=new ApprovedLauncherSession(typeof(EngineeringDashboard.LauncherWindow).Assembly);
        var launcher = new EngineeringDashboard.LauncherWindow();
        var root = (FrameworkElement)launcher.Content;
        root.Measure(new Size(600, 680)); root.Arrange(new Rect(0, 0, 600, 680)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var slab = (Button)launcher.FindName("BtnFlexCalculator");
        var beam = (Button)launcher.FindName("BtnBeamCalculator");
        var back = (Button)launcher.FindName("BtnBackpropping");
        var dome = (Button)launcher.FindName("BtnDomeCalculator");
        Check(new[] { slab, beam, back, dome }.All(b => b != null), "Four calculator choices available");
        double Gap(Button first, Button second) => second.TranslatePoint(new Point(), root).Y - first.TranslatePoint(new Point(0, first.ActualHeight), root).Y;
        Check(Gap(back, dome) > Gap(slab, beam) + 20, "Locked Beta choice separated from available calculators");
        Check(!dome.IsEnabled && System.Windows.Automation.AutomationProperties.GetName(dome).Contains("Locked"), "Dome disabled with accessible Locked label");
        Render(launcher, "doka-launcher.png", 600, 680);
        // Locked launcher remains safe even when a click is raised programmatically.
        dome.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!Application.Current.Windows.OfType<MainWindow>().Any(w=>w.IsVisible), "Disabled Dome cannot launch");
        // Exercise future calculator internals directly without unlocking the release UI.
        launcher.Show();
        MainViewModel? retained = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int current = attempt;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var input = Application.Current.Windows.OfType<MainWindow>().Single(w => w.IsVisible);
                Check(input.Owner == launcher, "Calculator owned by launcher");
                Check(input.Title.Contains("Beta"), "Calculator title identifies beta");
                var vm = (MainViewModel)input.DataContext;
                if (current == 0)
                {
                    retained = vm;
                    vm.CombinedLoad = "50"; vm.SecondaryBeamSize = "3.3";
                    Render(input, "doka-dome-inputs.png", 660, 810);
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        var diagrams = Application.Current.Windows.OfType<DiagramWindow>().Single(w => w.IsVisible);
                        Check(diagrams.Owner == input, "Diagram owned by calculator, independent of Application.MainWindow");
                        Check(diagrams.Title.Contains("Beta"), "Diagram title identifies beta");
                        Render(diagrams, "doka-dome-diagrams.png", 1260, 740);
                        diagrams.Close();
                    }), DispatcherPriority.ApplicationIdle);
                    vm.ShowDiagramCommand.Execute(null);
                }
                else
                {
                    Check(ReferenceEquals(retained, vm) && vm.CombinedLoad == "50", "Reopening retains calculator inputs");
                }
                input.Close();
            }), DispatcherPriority.ApplicationIdle);
            new MainWindow(retained ?? new MainViewModel()) {Owner=launcher}.ShowDialog();
            Check(launcher.IsVisible, "Closing calculator returns to launcher");
        }
        launcher.Close();
        Console.WriteLine("PASS Beta lock, direct calculator modal ownership and retained state");
    }

    private static void Render(Window window, string name, int width, int height)
    {
        string? directory = Environment.GetEnvironmentVariable("DOME_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory!);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory!, name))) encoder.Save(stream);
    }
}
