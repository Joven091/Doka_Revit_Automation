using System;
using System.Linq;
using EngineeringDashboard;

internal static class NetworkSearchReuseTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Network search reuse: "+message);}
    internal static void Run()
    {
        var input=ControlledPerformanceTests.Input("three-custom-slabs");var network=input.Network;
        Check(network.Beams.Length==3 && network.CrossingBeams.Length==2 && network.Bays[0].WidthMm==2272 && network.Bays[1].WidthMm==2252,"uploaded model is reproduced");
        Check(input.AllowCustomSlabLengths && !input.AllowCustomBeamLengths,"uploaded custom scope stays limited to slab supports");
        Check(network.CrossingBeams.All(c=>CrossBeamSupportOptimizer.PrimaryBearingPossible(input,network,c)),"negative-gap crossings can use primary bearings");
        Check(TransferNetworkOptimizer.MinimumDedicatedLegs(input,network)==0,"automatic direct-primary bearings reserve no fictitious tower legs");
        network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegs(input,network)==4,"forced dedicated tower still reserves four legs");
        network.CrossingBeams[1].BottomElevationMm=-1900;
        Check(TransferNetworkOptimizer.MinimumDedicatedLegs(input,network)==8,"crossing below all receiving options retains mandatory legs");
        input=ControlledPerformanceTests.Input("three-custom-slabs");network=input.Network;
        input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=650,BeamLineLoadKnM=20.5725,UpperSpacingMm=350,
            StockCovers=new[] {new[] {3300.0,3300}},ReceivingRowsMm=Array.Empty<double>(),RightBayMm=2272,
            RightPlan=new UpperTransferPlan {StockLengthMm=1800,EdgeMm=236,NearFootMm=636,FarFootMm=636}};
        input.AllowCustomH20Lengths=false;var stockKey=new ReceiverSolveKey(input,network,0,false);
        input.AllowCustomH20Lengths=true;
        Check(stockKey.Equals(new ReceiverSolveKey(input,network,0,false)),"identical fixed slab stocks and feet share receiver work between stock/custom passes");
        input.TransferredSlab.RightPlan.NearFootMm+=25;
        Check(!stockKey.Equals(new ReceiverSolveKey(input,network,0,false)),"changed slab foot invalidates receiver reuse");input.TransferredSlab.RightPlan.NearFootMm-=25;
        input.TransferredSlab.RightPlan.StockLengthMm=1775;
        Check(!stockKey.Equals(new ReceiverSolveKey(input,network,0,false)),"changed slab stock invalidates receiver reuse");
        input.TransferredSlab.RightPlan=null;input.AllowCustomH20Lengths=false;stockKey=new ReceiverSolveKey(input,network,0,false);input.AllowCustomH20Lengths=true;
        Check(!stockKey.Equals(new ReceiverSolveKey(input,network,0,false)),"free slab searches retain their custom eligibility in the key");
        foreach(bool dedicated in new[] {false,true})
        {
            var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,
                StockLengthsMm=new[] {1250.0,1800,2450,2900},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=175,
                DeflectionDenominator=270,AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=1};
            var n=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
                UpperSpacingMm=305,BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new[] {350.0},
                Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),
                Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}},
                CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600,
                    SupportMode=dedicated ? CrossBeamSupportMode.DedicatedTowers : CrossBeamSupportMode.Automatic}}};
            common.ReuseTransversePreparations=false;var expected=TransferNetworkOptimizer.Optimize(common,n);
            common.ReuseTransversePreparations=true;var actual=TransferNetworkOptimizer.Optimize(common,n);
            Check(ControlledPerformanceTests.Fingerprint(expected)==ControlledPerformanceTests.Fingerprint(actual),"prepared design reuse preserves complete shared/dedicated geometry, UDLs and reactions");
            ControlledPerformanceTests.Validate(common,actual);
        }
        Console.WriteLine("Passed "+checks+" network search reuse and direct-primary bound checks.");
    }
}
