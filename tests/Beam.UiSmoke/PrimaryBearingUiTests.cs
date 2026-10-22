using System;
using System.Linq;
using EngineeringDashboard;
internal static class PrimaryBearingUiTests
{
    internal static void Run()
    {
        var c=new TransferCrossingBeamRow {BayNumber=1,PositionMm=3450,WidthMm=650,DepthMm=1150};
        var network=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=1875,ThicknessMm=300}},CrossingBeams=new[] {c},UpperSpacingMm=305};
        var common=new TowerInput {LengthMm=6900,FullLoadingOnly=true,EngineerSearch=true,LegCapacityKn=70};
        var basis=new TransferMember {LengthMm=1800,SupportsMm=new[] {150.0,1650},Uniform=new[] {new BeamDistributedLoad {StartMm=575,EndMm=1225,KnPerM=31.65}},Points=Array.Empty<BeamPointLoad>()};
        var plan=EngineerPrimaryPlan.Create(common,network,0,false,false).WithGeometry(common,basis,Array.Empty<string>());
        plan.IsResolved=true;plan.PrimaryBearingTransfers[0]=new[] {0.0,12};plan.CrossingTransfers[0]=new[] {0.0,12};
        var q=new BeamDistributedLoad {StartMm=3125,EndMm=3775,KnPerM=22.28625};
        var card=MemberLoadingView.Tributary(new TransferMember {LengthMm=6900},plan,1,q);
        if(!card.Lines.Any(l=>l.Contains("7.8 / 0.65 = 12")))throw new Exception("Direct primary reaction conversion missing");
        if(!card.Lines.Any(l=>l.StartsWith("UDL =") && l.Contains("22.28625")))throw new Exception("Displayed UDL omits direct crossing transfer");
        var hidden=new CalculationCard();if(hidden.IsOpen)throw new Exception("Calculation card opened before a click");
        Console.WriteLine("PASS primary-bearing UDL card includes direct transfer and remains closed initially");
    }
}
