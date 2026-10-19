using System;
using System.Linq;
using EngineeringDashboard;

internal static class RequiredRowTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Reject(double start,double end,double maximum,double[] required,string expected)
    {
        try {PlywoodSpacing.FillRequiredRows(start,end,maximum,required);throw new Exception("Conflicting mandatory rows were accepted.");}
        catch(ArgumentException ex){Check(ex.Message.Contains(expected),"Controlled rejection explains the row conflict: "+ex.Message);}
    }
    static void Verify(double start,double end,double maximum,double[] required)
    {
        var result=PlywoodSpacing.FillRequiredRows(start,end,maximum,required);
        Check(required.All(result.Contains),"Every mandatory row retains its exact load-mapping coordinate");
        Check(result.All(x=>x>start && x<end),"Every row remains within primary coverage");
        Check(result.Zip(result.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=maximum+1e-7),"Flange separation and maximum plywood span both pass");
        Check(result.SequenceEqual(PlywoodSpacing.FillRequiredRows(start,end,maximum,required.Reverse().ToArray())),"Required-row order does not change setting-out");
        foreach(double joint in PlywoodSpacing.SheetSupportRows(start,end,50000).Where(x=>Math.Abs(x/2440-Math.Round(x/2440))<1e-7))
            Check(result.Contains(joint),"Mandatory plywood sheet joints are retained");
    }
    internal static void Run()
    {
        // These two rows from the 18 Oct four-beam diagnosis used to delete the
        // first row, leaving SlabRowIndices=-1 and crashing load mapping.
        foreach(var pair in new[] {new[] {5368.0,5390},new[] {5390.0,5368}})
            Reject(175,10425,350,pair,"5368 and 5390 mm are 22 mm apart");
        Reject(0,10600,350,new[] {2440.0,2480},"80 mm");
        Reject(0,10600,350,new[] {2460.0},"mandatory beam sheet joint");
        Reject(0,10600,350,new[] {-1.0},"outside primary coverage");
        Verify(0,10600,350,new[] {100.0,180,2440,4880,7320,9760,10500});
        Verify(0,10600,350,new[] {100.0,100,180,2440});
        Verify(0,10600,350,new double[0]);
        var random=new Random(10181935);
        foreach(double spacing in PlywoodSpacing.StandardSpacingsMm)
        foreach(double edge in new[] {0.0,25,75,150,175})
        for(int sample=0;sample<20;sample++)
        {
            var required=PlywoodSpacing.SheetSupportRows(edge,10600-edge,spacing).Where(x=>random.Next(3)==0).ToArray();
            Verify(edge,10600-edge,spacing,required);
        }
        var vm=ControlledPerformanceTests.Model("search-error");
        Check(vm.NetworkBeams.Count==4 && vm.NetworkBays.Count==3 && vm.CrossingBeams.Count==3,"Exact reported network size");
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        string diagnostic=vm.BuildDiagnosticReport();
        Check(!vm.IsOptimizing,"Complete reported search finishes");
        Check(!diagnostic.Contains("Unexpected error") && !diagnostic.Contains("IndexOutOfRangeException"),"Reported search never crashes or enters unexpected-error handling");
        if(vm.IsCalculationValid)
        {
            Check(vm.NetworkResult!=null,"Passing search has a complete network");
            ControlledPerformanceTests.Validate(ControlledPerformanceTests.Input("search-error"),vm.NetworkResult);
        }
        else
        {
            Check(vm.HasTowerFailure && !string.IsNullOrEmpty(vm.TowerFailureDetails),"Unsuccessful bounded search returns checked engineering rejections");
            Check(diagnostic.Contains("Search outcome: No solution"),"View model reports a normal engineering rejection");
        }
        Console.WriteLine("Reported four-beam outcome: "+(vm.IsCalculationValid ? "valid solution" : vm.WarningMessage));
        Console.WriteLine("Passed "+checks+" required-row and reported-crash checks.");
    }
}
