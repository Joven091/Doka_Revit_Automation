using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EngineeringDashboard;
using MyRevitAddin.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var window=new FlexCalculatorWindow{Title="Slab calculator · standalone tester"};
            ((Button)window.FindName("ImportButton")).Visibility=Visibility.Collapsed;
            window.Loaded+=(sender,eventArgs)=>
            {
                foreach(var button in LogicalButtons(window))if(Equals(button.Content,"Change system"))button.Visibility=Visibility.Collapsed;
                if(args.Contains("--startup-check"))window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                {
                    if(typeof(Program).Assembly.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("Revit",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Standalone payload references Revit.");
                    var model=(FlexCalculatorViewModel)window.DataContext;if(!model.IsCalculationValid||!window.TryGetValidCalculation(out var current))throw new InvalidOperationException("Slab calculator defaults are invalid.");
                    double spacing=model.SuggestedPrimaryBeamSpacing;model.SlabThickness=400;if(!model.IsCalculationValid||model.SuggestedPrimaryBeamSpacing==spacing)throw new InvalidOperationException("Slab inputs did not recalculate.");
                    model.ViewPrimaryBeamDiagramCommand.Execute(null);if(!app.Windows.OfType<DiagramWindow>().Any(w=>w.IsVisible))throw new InvalidOperationException("Actual primary diagrams did not open.");
                    model.ViewSecondaryBeamDiagramCommand.Execute(null);if(app.Windows.OfType<DiagramWindow>().Count(w=>w.IsVisible)!=2)throw new InvalidOperationException("Actual secondary diagrams did not open.");
                    model.SlabThickness=300;var record=window.CaptureCalculation();model.SlabThickness=350;window.RestoreCalculation(record);if(model.SlabThickness!=300)throw new InvalidOperationException("Slab input roundtrip failed.");
                    Console.WriteLine("PASS standalone slab starts, recalculates, opens both real diagram windows and roundtrips inputs without Revit.");window.Close();
                }));
            };
            app.DispatcherUnhandledException+=(sender,error)=>{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),error.Exception.ToString());error.Handled=true;app.Shutdown(1);};
            return app.Run(window);
        }
        catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),error.ToString());return 1;}
    }
    private static System.Collections.Generic.IEnumerable<Button> LogicalButtons(DependencyObject root)
    {foreach(var item in LogicalTreeHelper.GetChildren(root))if(item is DependencyObject child){if(child is Button button)yield return button;foreach(var nested in LogicalButtons(child))yield return nested;}}
}
