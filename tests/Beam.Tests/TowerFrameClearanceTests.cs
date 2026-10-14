using System;
using System.Linq;
using EngineeringDashboard;

internal static class TowerFrameClearanceTests
{
    static int checks;
    static void Check(bool value,string name) {checks++;if(!value)throw new Exception("Tower frame clearance: "+name);}
    public static void Run()
    {
        var layout=new TowerLayout {PositionsMm=new[] {100.0,1100,2300,3300},Joined=new[] {true,false,true},WidthMm=1524};
        var main=CrossBeamSupportOptimizer.TowerEnvelopes(layout,"B1",0,0,true,-2150).ToArray();
        Check(main.Length==2 && main[0].X0==60 && main[0].X1==1140 && main[1].X0==2260,"each connected tower reserves its complete frame and cross footprint; unbraced gaps remain open");
        Check(main[0].Y0==-802 && main[0].Y1==802,"reservation includes 40 mm around the outer leg centres");
        var cross=CrossBeamSupportOptimizer.TowerEnvelopes(new TowerLayout {PositionsMm=new[] {500.0,1500},Joined=new[] {true},WidthMm=1000},"X1",600,250,false,-1550).Single();
        Check(cross.X0==60 && cross.X1==1140 && cross.Y0==710 && cross.Y1==1790,"crossing tower reservation rotates into network coordinates");
        Check(main[0].Conflicts(cross) && cross.Conflicts(main[0]),"frame/cross intersection is rejected even at different head elevations");
        cross.Y0=main[0].Y1;Check(!main[0].Conflicts(cross),"contact between reserved exterior surfaces is allowed");
        var ownLeg=new FormworkSolid {IsLeg=true,TowerOwner="B1",X0=90,X1=110,Y0=-10,Y1=10,Z0=-1e6,Z1=-2150};
        Check(!main[0].Conflicts(ownLeg),"a tower's own columns belong within its reservation");ownLeg.TowerOwner="X1";
        Check(main[0].Conflicts(ownLeg),"another tower's columns cannot enter the braced footprint");
        var gap=new FormworkSolid {IsLeg=true,TowerOwner="X1",X0=1600,X1=1680,Y0=-40,Y1=40,Z0=-1e6,Z1=-2150};
        Check(main.All(m=>!m.Conflicts(gap)),"a separate tower can use a real gap between independent groups");
        var common=new TowerInput {LengthMm=3000,LegCapacityKn=70,AllowCustomH20Lengths=true,IgnoreUplift=true,StockEndSetbackMm=150,DeflectionDenominator=270,ReceiverTransverseOffsetMm=-25,
            TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamCentreOffsetMm=25,BeamWidthMm=650,BeamLineLoadKnM=20.5725,UpperSpacingMm=250,LeftBayMm=2000,LeftAreaLoadKnM2=9.9}};
        var shifted=TransferredSlabOptimizer.Optimize(common,true);
        Check(shifted.TransverseOffsetMm==-25,"receiving tower displacement is retained in the returned layout");
        foreach(var member in shifted.Transfer.LowerCases.SelectMany(stage=>stage))
        {
            Check(Math.Abs(member.StartMm+member.Uniform[0].StartMm+325)<1e-8 && Math.Abs(member.StartMm+member.Uniform[0].EndMm-325)<1e-8,"eccentric tower placement preserves the concrete footprint");
            Check(Math.Abs(member.StartMm+member.SupportsMm[0]-(shifted.TransverseOffsetMm-shifted.WidthMm/2))<1e-8 && Math.Abs(member.StartMm+member.SupportsMm[1]-(shifted.TransverseOffsetMm+shifted.WidthMm/2))<1e-8,"lower reactions act at the actual displaced primary and leg lines");
            var independent=LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,TowerOptimizer.PairEI/2,270);
            Check(independent.ShearKn<=11+1e-8 && independent.MomentKnM<=5+1e-8 && independent.DeflectionUtilization<=1+1e-8,"displaced secondaries retain full shear, moment and deflection checks");
        }
        Console.WriteLine($"Tower frame clearance: {checks} checks passed.");
    }
}
