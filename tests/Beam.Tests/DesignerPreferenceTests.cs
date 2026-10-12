using System;
using System.Linq;
using EngineeringDashboard;

internal static class DesignerPreferenceTests
{
    static void Check(bool value,string message) {if(!value)throw new Exception("Designer preference: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(b)),message);
    static TowerInput Beam(double length)=>new TowerInput {LengthMm=length,StockLengthsMm=new[] {length},TotalLineLoad=5,LegCapacityKn=70,DeflectionLimitMm=10};
    public static void Run()
    {
        var wide=TowerOptimizer.Optimize(Beam(2450));
        Check(wide.LegCount==4 && TowerOptimizer.StandaloneBraces(wide.PositionsMm,wide.Joined,wide.WidthMm,wide.Rotated).Single()==2000,"roomy beam chooses a 2000 mm standalone brace");
        var small=TowerOptimizer.Optimize(Beam(950));
        Check(small.LegCount==4 && small.Standalone600Count==1,"600 mm standalone remains available when geometry needs it");
        var linked=TowerOptimizer.Evaluate(Beam(2450),new[] {325.0,925,1525,2125},new[] {true,true,true},1524,false);
        Check(linked!=null && linked.GroupCount==1 && linked.Standalone600Count==0,"linked 600 mm bays are retained instead of splitting into narrow standalone towers");
        var mixed=TowerOptimizer.Evaluate(Beam(4900),new[] {325.0,925,1525,2125,3375,3975},new[] {true,true,true,false,true},1524,false);
        Check(mixed!=null && mixed.GroupCount==2 && mixed.Standalone600Count==1,"a constrained narrow standalone does not split unrelated linked bays");
        var rotated=TowerOptimizer.Evaluate(Beam(2450),new[] {463.0,1987},new[] {true},600,true);
        Check(rotated!=null && rotated.Standalone600Count==1,"rotated tower classifies the 600 mm cross brace rather than its frame span");
        var oneSided=new TowerInput {LengthMm=3300,StockLengthsMm=new[] {1800.0,2450,3300,3900},LegCapacityKn=70,StockEndSetbackMm=150,DeflectionDenominator=270,
            TransferredSlab=new TransferredSlabInput {BeamWidthMm=800,BeamLineLoadKnM=25,UpperSpacingMm=200,RightBayMm=2677,RightAreaLoadKnM2=9.4}};
        var transfer=TowerOptimizer.Optimize(oneSided);
        Verify(transfer);
        var t=oneSided.TransferredSlab;
        t.LeftBayMm=t.RightBayMm;t.LeftAreaLoadKnM2=t.RightAreaLoadKnM2;t.RightBayMm=0;t.RightAreaLoadKnM2=0;
        var mirror=TowerOptimizer.Optimize(oneSided);Verify(mirror);
        Near(mirror.Transfer.LowerStockLengthMm,transfer.Transfer.LowerStockLengthMm,"mirrored one-sided slab keeps centred stock length");
        Near(mirror.LeftReactions.Sum(),transfer.RightReactions.Sum(),"mirrored load swaps primary-line leg forces");
        var common=new TowerInput {LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,DeflectionDenominator=270,AllowCustomH20Lengths=true};
        var input=new TransferNetworkInput {UpperSpacingMm=275,Beams=new[] {
            new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},
            Bays=new[] {new TransferBayRow {WidthMm=2677,ThicknessMm=300},new TransferBayRow {WidthMm=1875,ThicknessMm=300}}};
        var result=TransferNetworkOptimizer.Optimize(common,input);
        foreach(var beam in result.Beams)Verify(beam);
        Check(result.Beams[0].Standalone600Count==0,"B1 avoids standalone 600 mm towers in the reported parallel-beam geometry");
        Console.WriteLine(result.LayoutDiagnostics);
        Console.WriteLine("Designer preference checks passed.");
    }
    static void Verify(TowerLayout beam)
    {
        var t=beam.Transfer;
        Near(t.LowerStockStartMm+t.LowerStockLengthMm/2,0,"secondary midpoint coincides with the concrete beam centre");
        foreach(var m in t.LowerCases.Concat(t.PrimaryCases).SelectMany(c=>c).Concat(t.UpperMembers))
        {
            Check(m.Utilization<=1+1e-8 && m.Analysis.Reactions.All(r=>r>=-1e-7),"centred layout passes all stages including deflection and no uplift");
            Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"centred member conserves force");
            Check(m.SupportsMm.All(x=>x>=0 && x<=m.LengthMm),"every bearing lies on the actual stock");
        }
        Check(beam.MaxLegKn<=beam.LegCapacityKn+1e-8,"individual leg capacities pass");
    }
}
