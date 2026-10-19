using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class SecondarySettingOutTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Secondary setting-out: "+message);}
    internal static TransferNetworkResult Fixture()
    {
        double[] rows={100,375,650,925,1200,1475,1750,2025,2300,2440,2715,2990,3265,3540,3815};
        Func<TowerLayout> beam=()=>new TowerLayout {LengthMm=4000,WidthMm=1000,PositionsMm=new[] {150.0,3850},Joined=new[] {false},StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=4000,FirstSupport=0,LastSupport=1}}},Transfer=new TransferredSlabResult {RowsMm=(double[])rows.Clone(),EnteredUpperSpacingMm=275,LowerStockStartMm=-900,LowerStockLengthMm=1800,SlabRowIndices=new[] {2},LeftUpperScales=new double[rows.Length],RightUpperScales=new double[rows.Length],TributaryWidthsMm=Enumerable.Repeat(275.0,rows.Length).ToArray(),UpperMembers=new TransferMember[0],CaseNames=new[] {"Beam"},PrimaryCases=new[] {new TransferMember[0]},LowerCases=new[] {new TransferMember[0]}}};
        return new TransferNetworkResult {Input=new TransferNetworkInput {PlywoodSheetLengthMm=2440,Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=500,BottomElevationMm=-500},new TransferBeamRow {WidthMm=650,DepthMm=500,BottomElevationMm=-500}},Bays=new[] {new TransferBayRow {WidthMm=2350,ThicknessMm=300}}},Beams=new[] {beam(),beam()},Bays=new UpperTransferPlan[1],BeamCentresMm=new[] {0.0,3000}};
    }
    static FormworkSolid Leg(double x)=>new FormworkSolid {Name="X1 tower leg",IsLeg=true,TowerOwner="X1",X0=x-40,X1=x+40,Y0=650,Y1=730,Z0=-1e6,Z1=-600};
    public static void Run()
    {
        var r=Fixture();var original=r.Beams[0].Transfer;int count=original.RowsMm.Length;
        Check(CrossBeamSupportOptimizer.TryAdjustSecondaries(r,new[] {Leg(2688)},new FormworkSolid[0],out var adjusted),"move/add clears the reported leg/H20 overlap");
        var t=adjusted[0].Transfer;
        Check(t.AddedSecondaryCount==1 && t.PhysicalRowCount==count+1,"one extra H20 restores the gap");
        Check(t.SettingOutRowsMm.Contains(2440) && t.SettingOutRowsMm.Contains(650),"plywood joint and slab-foot supports stay fixed");
        Check(t.SettingOutRowsMm.Zip(t.SettingOutRowsMm.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=275+1e-7),"every physical gap meets the original design spacing and flange clearance");
        Check(t.SettingOutRowsMm.All(x=>Math.Abs(x-2688)>=82-1e-7),"physical H20 flanges clear the full leg column");
        Check(ReferenceEquals(t.RowsMm,original.RowsMm) && t.EnteredUpperSpacingMm==275,"design rows and nominal spacing are retained");
        Check(ReferenceEquals(t.PrimaryCases,original.PrimaryCases) && ReferenceEquals(t.LowerCases,original.LowerCases),"primary UDL and conservative secondary analyses are retained");
        Check(original.SettingOutRowsMm==null && ReferenceEquals(r.Beams[0].Transfer,original),"trial never mutates the original network or cached analysis");
        Check(t.AdditionalH20Count-original.AdditionalH20Count==1 && t.AdditionalTimberMm-original.AdditionalTimberMm==1800,"extra stock is counted once");
        Check(t.Schedule.Contains("design spacing remains 275") && t.Schedule.Contains("primary UDL is unchanged"),"schedule states the retained calculation basis");
        Check(!CrossBeamSupportOptimizer.TryAdjustSecondaries(r,new[] {Leg(2440)},new FormworkSolid[0],out _),"mandatory plywood joint cannot move");
        Check(!CrossBeamSupportOptimizer.TryAdjustSecondaries(r,new[] {Leg(650)},new FormworkSolid[0],out _),"shared slab foot cannot move");
        var fixedClash=Leg(0);fixedClash.Y0=-550;fixedClash.Y1=-450;
        Check(!CrossBeamSupportOptimizer.TryAdjustSecondaries(r,new[] {fixedClash},new FormworkSolid[0],out _),"fixed main primary stock cannot be repaired by moving an H20");
        Console.WriteLine($"Passed {checks} secondary setting-out checks.");
    }
    public static void Reported()
    {
        var c=new TowerInput {LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,DeflectionDenominator=270,IgnoreUplift=true,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,CoordinateCrossingLayouts=true,Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true)};
        var n=new TransferNetworkInput {UpperSpacingMm=275,PlywoodSheetLengthMm=2440,PlywoodProfileId="doka-18mm-9732-103",PlywoodDeflectionDenominator=350,BeamSpacingsMm=new double[] {275,305,275,305},SlabSpacingsMm=new double[] {407,407,407},CrossingSpacingsMm=new double[] {305,305},Beams=Enumerable.Range(0,4).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150,BottomElevationMm=i%2==0 ? -1750 : -1150}).ToArray(),Bays=new[] {new TransferBayRow {WidthMm=2278,ThicknessMm=300},new TransferBayRow {WidthMm=2272,ThicknessMm=300},new TransferBayRow {WidthMm=2252,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=2,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150},new TransferCrossingBeamRow {BayNumber=3,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}}};
        // Restrict only the shared coverage/phase to the reported selection; member and
        // crossing checks still run through the production solver before coordination.
        // Keep this historical setting-out fixture independent of the new default shared-coverage policy.
        c.IndependentNetworkPrimaryCoverage=true;
        // Replay the historical crossing/secondary geometry; new ledger loads and setting-out are covered by CrossingSlabSupportTests.
        n.UseCrossingSlabLedgers=false;
        var covers=TransferredSlabOptimizer.StockCovers(c);
        var scenario=typeof(TransferNetworkOptimizer).GetMethod("SearchScenario",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {c,n,covers,new TransferredSlabOptimizer.GeometryCache(),null,8,6750.0,0.0,false,null});
        var candidates=(System.Collections.Generic.List<TransferNetworkResult>)scenario.GetType().GetField("Improvements",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scenario);
        var r=candidates.OrderBy(x=>x.EconomyScoreMm).First();
        var before=r.Beams.Select(b=>b.Transfer.PrimaryCases).ToArray();int legs=r.LegCount,h20=r.H20Count;
        var original=r.Beams.Select(b=>b.Transfer).ToArray();double load=r.CrossSupports.Sum(x=>x.LoadKn);
        CrossBeamSupportOptimizer.BalanceSettingOut(c,r);
        var x1=r.CrossSupports[0];var stock=x1.Towers.StockPlan.Pieces.Single();
        Check(Math.Abs(x1.Towers.PositionsMm[0]-stock.StartMm-400)<1e-7 && Math.Abs(stock.EndMm-x1.Towers.PositionsMm[1]-400)<1e-7,"reported X1 reaches 400/400 primary overhangs");
        Check(r.LegCount==legs && r.H20Count>=h20 && r.Beams.Any(b=>b.Transfer.SettingOutRowsMm!=null),"coordination clears receiving H20s without adding tower legs");
        Check(Math.Abs(r.CrossSupports.Sum(x=>x.LoadKn)-load)<1e-7,"cross concrete load is conserved");
        for(int b=0;b<r.Beams.Length;b++)
        {
            var t=r.Beams[b].Transfer;
            Check(ReferenceEquals(t.PrimaryCases,before[b]),"B"+(b+1)+" keeps exact primary UDL and responses");
            Check(ReferenceEquals(t.RowsMm,original[b].RowsMm),"B"+(b+1)+" keeps design row positions");
            if(t.SettingOutRowsMm!=null)Check(t.SettingOutRowsMm.Zip(t.SettingOutRowsMm.Skip(1),(a,z)=>z-a).All(g=>g>=80-1e-7 && g<=t.EnteredUpperSpacingMm+1e-7),"local physical gaps stay within the nominal spacing");
        }
        var existing=CrossBeamSupportOptimizer.NetworkSolids(r).Concat(CrossBeamSupportOptimizer.Concrete(r)).ToArray();
        foreach(var x in r.CrossSupports)
        {
            Check(x.Solids.All(s=>existing.All(o=>!s.Conflicts(o))),"balanced crossing clears complete receiving geometry");
            Check(x.Towers.Transfer.PrimaryCases.SelectMany(stage=>stage).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.Max()<=70),"rebalanced primary and leg checks pass");
        }
        int revised=r.H20Count;CrossBeamSupportOptimizer.BalanceSettingOut(c,r);Check(r.H20Count==revised,"coordination is idempotent");
        Console.WriteLine($"Passed {checks} reported crossing coordination checks; {r.LegCount} legs, {r.H20Count-h20} extra H20s.");
    }
}
