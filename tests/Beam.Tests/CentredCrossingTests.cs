using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class CentredCrossingTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Centred crossing: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),message+$" ({a:R} / {b:R})");
    static LoadedBeamAnalysis Solve(TransferMember m)=>LoadedBeamAnalysis.AnalyzeDenseReference(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,m.EI,270);
    internal static TransferNetworkResult Fixture(TowerInput common)
    {
        double[] rows={40,315,590,865,1140,1415,1690,1965,2240,2440,2637,2718,2990,3265,3540,3815,4000,4182,4263,4480,4680,4880,5155,5430,5705,5980,6255,6530,6695,6860};
        var input=new TransferNetworkInput {UpperSpacingMm=275,PlywoodSheetLengthMm=2440,
            Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=i==1 ? 800 : 650,DepthMm=i==1 ? 1750 : 1150,BottomElevationMm=i==1 ? -1750 : -1150}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=2272,ThicknessMm=300},new TransferBayRow {WidthMm=2252,ThicknessMm=300}},
            CrossingBeams=Enumerable.Range(0,2).Select(i=>new TransferCrossingBeamRow {BayNumber=i+1,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}).ToArray()};
        var beams=Enumerable.Range(0,3).Select(b=>
        {
            var lower=new TransferMember {Name="T",H20Count=1,StartMm=-1225,LengthMm=2450,SupportsMm=new[] {463.0,1987},
                Uniform=new[] {new BeamDistributedLoad {StartMm=1225-input.Beams[b].WidthMm/2,EndMm=1225+input.Beams[b].WidthMm/2,KnPerM=9.0}},Points=new BeamPointLoad[0]};lower.Analysis=Solve(lower);
            var primary=Enumerable.Range(0,2).Select(line=>
            {
                var p=new TransferMember {PrimaryMaterial=common.PrimaryMaterial,Name=line==0 ? "P1 L" : "P1 R",H20Count=2,StartMm=0,LengthMm=6900,SupportsMm=new[] {400.0,6400},
                    Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=6900,KnPerM=.2}},Points=new BeamPointLoad[0]};p.Analysis=Solve(p);return p;
            }).ToArray();
            var slab=rows.Select((x,i)=>new {X=x,I=i}).Where(x=>new[] {2637.0,2718,4182,4263}.Contains(x.X)).Select(x=>x.I).ToArray();
            return new TowerLayout {EngineerSearch=true,PrimaryMaterial=common.PrimaryMaterial,LengthMm=6900,WidthMm=1524,DeflectionDenominator=270,LegCapacityKn=70,PositionsMm=new[] {400.0,6400},Joined=new[] {false},
                StockPlan=new TowerStockPlan {PrimaryMaterial=common.PrimaryMaterial,Pieces=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=6900,FirstSupport=0,LastSupport=1}}},
                Transfer=new TransferredSlabResult {FullLoadingOnly=true,RowsMm=(double[])rows.Clone(),EnteredUpperSpacingMm=275,LowerStockStartMm=-1225,LowerStockLengthMm=2450,
                    SlabRowIndices=slab,LeftUpperScales=rows.Select((x,i)=>slab.Contains(i) ? .488 : 0).ToArray(),RightUpperScales=new double[rows.Length],
                    TributaryWidthsMm=Enumerable.Repeat(275.0,rows.Length).ToArray(),CaseNames=new[] {"Beam + Slab"},PrimaryCases=new[] {primary},LowerCases=new[] {rows.Select(x=>lower).ToArray()},UpperMembers=new TransferMember[0]}};
        }).ToArray();
        var result=new TransferNetworkResult {Input=input,Beams=beams,Bays=new UpperTransferPlan[2],BeamCentresMm=new[] {0.0,2997,5974},SlabCoveredLengthMm=6900};
        result.CrossSupports=Enumerable.Range(0,2).Select(i=>
        {
            double span=i==0 ? 1172 : 1152,start=i==0 ? -78 : 0,face=i==0 ? 1225 : 3597,ledger=i==0 ? 2.2936 : 1.9129;
            var unit=new TransferMember {Name="Unit",H20Count=1,StartMm=-1375,LengthMm=2450,SupportsMm=new[] {613.0,2137},
                Uniform=new[] {new BeamDistributedLoad {StartMm=900,EndMm=1550,KnPerM=31.65}},
                Points=new[] {new BeamPointLoad {PositionMm=900,Kn=ledger},new BeamPointLoad {PositionMm=1550,Kn=ledger}}};unit.Analysis=Solve(unit);
            var crossRows=PlywoodSpacing.SheetSupportRows(0,span,350);
            var lower=crossRows.Select((x,j)=>
            {
                var m=new TransferMember {Name="T"+(j+1),H20Count=1,StartMm=unit.StartMm,LengthMm=unit.LengthMm,SupportsMm=unit.SupportsMm,
                    Uniform=unit.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*.35}).ToArray(),
                    Points=unit.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*.35}).ToArray()};m.Analysis=Solve(m);return m;
            }).ToArray();
            var positions=new[] {start+325,start+925};
            var primary=Enumerable.Range(0,2).Select(line=>new TransferMember {PrimaryMaterial=common.PrimaryMaterial,Name=line==0 ? "P1 L" : "P1 R",H20Count=2,StartMm=start,LengthMm=1250,SupportsMm=new[] {325.0,925},Points=new BeamPointLoad[0],
                Uniform=new[] {new BeamDistributedLoad {StartMm=-start,EndMm=span-start,KnPerM=unit.Analysis.Reactions[line]}}}).ToArray();
            var tower=new TowerLayout {EngineerSearch=true,PrimaryMaterial=common.PrimaryMaterial,LengthMm=span,WidthMm=1524,PositionsMm=positions,Joined=new[] {true},DeflectionDenominator=270,LegCapacityKn=70,
                StockPlan=new TowerStockPlan {PrimaryMaterial=common.PrimaryMaterial,Pieces=new[] {new TowerStockPiece {StartMm=start,StockLengthMm=1250,FirstSupport=0,LastSupport=1}}},
                Transfer=new TransferredSlabResult {FullLoadingOnly=true,RowsMm=crossRows,TributaryWidthsMm=Enumerable.Repeat(350.0,crossRows.Length).ToArray(),EnteredUpperSpacingMm=350,LowerStockStartMm=-1375,LowerStockLengthMm=2450,
                    CaseNames=new[] {"Cross concrete + construction/formwork"},PrimaryCases=new[] {primary},LowerCases=new[] {lower},UnitLowerCases=new[] {unit},UpperMembers=new TransferMember[0],BeamLoadKn=unit.Analysis.TotalLoadKn*span/1000}};
            var args=new object[] {common,result,i,face,150.0,i==0 ? -39.0 : 49.0,tower,common,null,i==0 ? 78.0 : 0.0,i==0 ? 0.0 : 98.0,false,CrossShutterAllowance.Standard};
            var cross=(CrossBeamSupportResult)typeof(CrossBeamSupportOptimizer).GetMethod("FinishDedicated",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
            Check(cross!=null,"reported offset crossing fixture passes its existing member limits: "+args[8]);
            cross.LoadKn=i==0 ? 57.163 : 54.945;cross.SlabEdgeLoadKn=i==0 ? 10.422 : 8.616;
            cross.Bearings=cross.Bearings.Concat(new[] {new CrossBeamBearing {Beam=1,Row=13,IsConcreteEndZone=true,IsShutterEndZone=true,ReactionKn=1,EndZoneStartMm=i==0 ? 2397 : 3397,EndZoneEndMm=i==0 ? 2597 : 3597}}).ToArray();
            return cross;
        }).ToArray();
        return result;
    }
    internal static void Run()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})RunForMaterial(material);
    }
    static void RunForMaterial(PrimaryBeamMaterial material)
    {
        var common=new TowerInput {PrimaryMaterial=material,EngineerSearch=true,FullLoadingOnly=true,LengthMm=6900,LegCapacityKn=70,IgnoreUplift=true,DeflectionDenominator=270};
        var r=Fixture(common);var originalBeams=r.Beams.Select(b=>b.Transfer).ToArray();var originalCross=r.CrossSupports;
        CrossBeamSupportOptimizer.CentreCrossingFrames(common,r,Console.WriteLine);
        CrossBeamSupportOptimizer.BalanceSettingOut(common,r);
        for(int i=0;i<2;i++)
        {
            var old=originalCross[i];var cross=r.CrossSupports[i];var legs=cross.Bearings.Where(b=>b.IsDedicated && b.Label.StartsWith("Dedicated P")).ToArray();
            Near(legs.Average(b=>b.Xmm),3450,"X"+(i+1)+" frame is centred on the concrete beam");
            Near(legs.Min(b=>b.Xmm),2688,"left primary centre");Near(legs.Max(b=>b.Xmm),4212,"right primary centre");
            Near(cross.LoadKn,old.LoadKn,"crossing load allocation retained");Near(cross.SlabEdgeLoadKn,old.SlabEdgeLoadKn,"slab-edge allocation retained");
            Check(cross.Bearings.Where(b=>!b.IsDedicated).SequenceEqual(old.Bearings.Where(b=>!b.IsDedicated)),"receiving end-zone transfers retained exactly");
            foreach(var m in cross.Towers.Transfer.LowerCases[0])
            {
                Near(m.Analysis.Reactions[0],m.Analysis.Reactions[1],"symmetric load produces equal reactions onto crossing primaries");
                var a=Solve(m);Near(a.MomentKnM,m.Analysis.MomentKnM,"independent centred secondary moment");Near(a.DeflectionMm,m.Analysis.DeflectionMm,"independent centred secondary deflection");
            }
            Near(cross.Towers.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn),old.Towers.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn),"cross-primary force conserved");
            Check(cross.Members.All(m=>m.Utilization<=1+1e-8) && cross.Towers.MaxLegKn<=70,"all revised crossing members and legs pass");
            Near(old.Bearings.First(b=>b.Label=="Dedicated T1 / primary L").Xmm,2838,"cached offset input remains untouched");
        }
        for(int i=0;i<3;i++)
        {
            var t=r.Beams[i].Transfer;var old=originalBeams[i];
            Check(ReferenceEquals(t.PrimaryCases,old.PrimaryCases),"B"+(i+1)+" retains exact primary UDL, responses and reactions");
            Check(ReferenceEquals(t.LowerCases,old.LowerCases) && ReferenceEquals(t.RowsMm,old.RowsMm) && ReferenceEquals(t.LeftUpperScales,old.LeftUpperScales),"existing slab-foot and lower-member calculation retained");
            if(t.SettingOutRowsMm!=null)Check(t.SettingOutRowsMm.Zip(t.SettingOutRowsMm.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=275+1e-7),"lower setting-out meets existing gap limits");
        }
        Check(r.Beams[1].Transfer.SettingOutRowsMm!=null,"B2 slab-carrying lower rows can clear centred legs independently of the slab feet");
        var solids=CrossBeamSupportOptimizer.NetworkSolids(r).Concat(CrossBeamSupportOptimizer.Concrete(r)).ToArray();
        foreach(var c in r.CrossSupports)Check(c.Solids.All(s=>solids.All(o=>!s.Conflicts(o))),"centred frame clears all actual receiving timber, concrete and tower envelopes");
        int h20=r.H20Count;var centred=r.CrossSupports;CrossBeamSupportOptimizer.BalanceSettingOut(common,r);
        Check(r.H20Count==h20 && r.CrossSupports.SequenceEqual(centred),"centred setting-out is idempotent");
        Console.WriteLine($"Passed {checks} centred crossing checks ({material}); B1/B2/B3 primary arrays retained exactly.");
    }
}
