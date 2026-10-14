using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class SearchBudgetUiTests
{
    static void Wait(Task task)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted)
        {
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
            Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>30)throw new Exception("Search-budget UI fixture timeout");
        }
        task.GetAwaiter().GetResult();
    }
    internal static void Run()
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,Depth=200,Width=300,BeamLengthMm=2500,StockLengthsText="2500",MaximumConcreteEndOverhangMm=0,TowerSearchTimeLimit=TimeSpan.FromTicks(1)};
        var workspace=new BeamFormworkWorkspace(vm,null,null,null);workspace.Show();
        try
        {
            Wait(workspace.CalculateAsync());
            if(!vm.IsCalculationValid || vm.CanSearchLonger || workspace.Buttons.ContainsKey("SearchLonger") || !vm.BuildDiagnosticReport().Contains("Search budget seconds: Unlimited"))
                throw new Exception("Normal workspace calculation must ignore the test deadline and finish a complete checked layout");
        }
        finally {workspace.Close();}
        Console.WriteLine("Passed normal workspace calculation without an elapsed-time limit.");
    }
}
