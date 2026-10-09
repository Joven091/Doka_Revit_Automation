using System;
using EngineeringDashboard;

internal static class SideShutterClearanceTests
{
    public static void Run()
    {
        var result=new TransferNetworkResult {BeamCentresMm=new[] {0.0,3300},Input=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=600},new TransferBeamRow {WidthMm=600}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1}}}};
        var timber=new FormworkSolid {Y0=500,Y1=2800,Member=new TransferMember {H20Count=2}};
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber})!=null)throw new Exception("Exactly 200 mm primary-end clearance must pass.");
        foreach(bool near in new[] {true,false})
        {
            timber.Y0=near ? 499.99 : 500;timber.Y1=near ? 2800 : 2800.01;
            var failure=CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber});
            if(failure==null || !failure.Contains("side shutters"))throw new Exception("Full physical primary stock, including unloaded tails, must retain the shutter gap.");
        }
        timber.Member.H20Count=1;
        if(CrossBeamSupportOptimizer.PrimaryShutterClash(result,0,new[] {timber})!=null)throw new Exception("Primary-end clearance must not remove receiving secondary support.");
        Console.WriteLine("Side shutter clearance: exact gap and both full-stock ends checked.");
    }
}
