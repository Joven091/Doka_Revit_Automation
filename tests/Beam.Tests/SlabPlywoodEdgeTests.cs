using System;
using System.Linq;
using EngineeringDashboard;

internal static class SlabPlywoodEdgeTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Slab plywood edges: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message+$" ({actual:R} / {expected:R})");
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,message);}
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=70,StockEndSetbackMm=150,DeflectionDenominator=270,AllowCustomH20Lengths=false,AllowSeparateSlabFallback=false,NetworkSearchWorkers=1};
    static TransferNetworkInput Network()=>new TransferNetworkInput {
        PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
        UpperSpacingMm=305,BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new double[0],
        Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=1750}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300}}
    };
    static void Verify(TransferNetworkResult r)
    {
        Check(r.Bays.All(b=>b!=null) && !r.RequiresSeparateSlabFormwork,"all slab bays retain their checked beam supports");
        for(int bay=0;bay<r.Bays.Length;bay++)
        {
            var p=r.Bays[bay];p.Validate(r.Input.Bays[bay].WidthMm,Common().StockLengthsMm,r.Input.MaximumSlabEndStripMm(bay));
            Check(p.EdgeMm>=200 && p.EdgeMm<=r.Input.MaximumSlabEndStripMm(bay),"selected strip is within its own slab plywood allowance");
            Near(p.NearFootMm,p.FarFootMm,"equal slab-foot setbacks retained");
        }
        foreach(var b in r.Beams.Concat(r.CrossSupports.Where(c=>c.Towers!=null).Select(c=>c.Towers)))
        {
            Check(b.MaxLegKn<=b.LegCapacityKn+1e-8,"individual leg capacities retained");
            foreach(var m in b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers))
            {
                Check(m.Utilization<=1+1e-8 && m.Analysis.DeflectionEvaluated,"all actual member checks pass");
                Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"member force equilibrium");
            }
        }
        double expected=r.Input.Beams.Sum(b=>r.Input.AreaLoad(b.DepthMm)*b.WidthMm/1000)*r.Beams[0].LengthMm/1000;
        for(int bay=0;bay<r.Input.Bays.Length;bay++)
        {
            var crossings=r.Input.CrossingBeams.Where(c=>c.BayNumber==bay+1).ToArray();
            expected+=r.Input.AreaLoad(r.Input.Bays[bay].ThicknessMm)*r.Input.Bays[bay].WidthMm/1000*(r.Beams[0].LengthMm-crossings.Sum(c=>c.WidthMm))/1000;
            expected+=crossings.Sum(c=>r.Input.AreaLoad(c.DepthMm)*c.WidthMm*r.Input.Bays[bay].WidthMm/1e6);
        }
        double carried=r.Beams.Concat(r.CrossSupports.Where(c=>c.Towers!=null).Select(c=>c.Towers)).Sum(b=>b.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn)+b.Transfer.ColumnSupportLoadKn(0));
        Near(carried,expected,"beam, slab, shutter and crossing loads reach the supports once");
    }
    internal static void Run()
    {
        var n=Network();double limit=n.MaximumSlabEndStripMm(0);
        Near(n.AreaLoad(300),9.4,"reported slab pressure");Near(limit,488,"reported slab plywood allowance");
        var stocks=Common().StockLengthsMm;
        Check(UpperTransferPlan.SlabLengths(2125,stocks,limit).SequenceEqual(new[] {1250.0}),"1250 mm is eligible without custom lengths; 1800 mm still violates the 200 mm minimum");
        var plan=new UpperTransferPlan {StockLengthMm=1250,EdgeMm=437.5,NearFootMm=587.5,FarFootMm=587.5};
        plan.Validate(2125,stocks,limit);plan.Reverse().Validate(2125,stocks,limit);
        Reject(()=>plan.Validate(2125,stocks,407),"fixed plan cannot bypass a lower plywood allowance");
        Check(UpperTransferPlan.SlabLengths(2226,stocks,488).Contains(1250),"maximum edge equality is permitted");
        Check(!UpperTransferPlan.SlabLengths(2226.1,stocks,488).Contains(1250),"edge above the plywood maximum is rejected");
        Check(UpperTransferPlan.SlabLengths(1650,stocks,488).Contains(1250) && !UpperTransferPlan.SlabLengths(1649.9,stocks,488).Contains(1250),"200 mm minimum is retained");
        var custom=UpperTransferPlan.SlabLengths(2125,stocks,limit,true);
        Check(custom.All(s=>s%25==0 && (2125-s)/2>=200 && (2125-s)/2<=488),"custom lengths use the same plywood allowance and 25 mm increments");
        Reject(()=>UpperTransferPlan.SlabLengths(2125,stocks,double.NaN),"invalid allowance rejected");
        n.SlabSpacingsMm[0]=250;Near(n.MaximumSlabEndStripMm(0),488,"closer row retry does not shrink the slab allowance");
        n.PlywoodDeflectionDenominator=500;Near(n.MaximumSlabEndStripMm(0),407,"plywood criterion affects end-strip selection");
        n.PlywoodProfileId="doka-21mm-9732-104";Near(n.MaximumSlabEndStripMm(0),488,"selected plywood product affects end-strip selection");
        n.CustomLoads=true;n.LiveKnM2=30;Check(n.MaximumSlabEndStripMm(0)<488,"custom slab allowances affect the plywood maximum");
        var transfer=new TransferredSlabInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
            BeamWidthMm=800,BeamLineLoadKnM=37.32,UpperSpacingMm=305,LeftSlabSpacingMm=488,LeftBayMm=2125,LeftAreaLoadKnM2=9.4,LeftPlan=plan};
        transfer.Validate(Common());Near(transfer.MaximumSlabEndStripMm(true),488,"single-beam validation uses slab rather than beam spacing");
        transfer.RightAreaLoadKnM2=46.65;Near(transfer.MaximumSlabEndStripMm(false),305,"opposite slab has an independent load-based allowance");transfer.RightAreaLoadKnM2=0;
        transfer.PlywoodDeflectionDenominator=500;Reject(()=>transfer.Validate(Common()),"single-beam fixed plan respects changed plywood criterion");transfer.PlywoodDeflectionDenominator=350;
        var single=Common();single.TransferredSlab=transfer;var layout=TransferredSlabOptimizer.Optimize(single);
        Near(layout.Transfer.LeftUpperStockMm,1250,"single-beam selected catalogue slab stock");Near(layout.Transfer.LeftUpperEdgeMm,437.5,"single-beam selected end strip");
        var result=TransferNetworkOptimizer.Optimize(Common(),Network());Verify(result);
        Near(result.Bays[0].StockLengthMm,1250,"network selected catalogue slab stock");Near(result.Bays[0].EdgeMm,437.5,"network selected end strip");
        var closer=Network().WithSpacings(5);var repeated=TransferNetworkOptimizer.Optimize(Common(),closer);Verify(repeated);
        Near(repeated.Bays[0].StockLengthMm,1250,"250 mm search retry still permits 1250 mm stock");
        Console.WriteLine($"Passed {checks} slab plywood end-strip checks.");
    }
    internal static void Reported()
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,
            BeamLengthMm=7000,TowerLegCapacityKn=70,AllowCustomH20Lengths=false,MaximumConcreteEndOverhangMm=175,StockEndSetbackMm=150,
            Exclude600Standalone=false,PlywoodDeflectionLimit="L/350",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        vm.AddNetworkBay();
        for(int i=0;i<vm.NetworkBeams.Count;i++){vm.NetworkBeams[i].WidthMm=i==0 ? 650 : 800;vm.NetworkBeams[i].DepthMm=i==0 ? 1150 : 1750;vm.NetworkBeams[i].BottomElevationMm=i==0 ? -1150 : -1750;}
        vm.NetworkBays[0].WidthMm=3000;vm.NetworkBays[0].ThicknessMm=200;vm.NetworkBays[1].WidthMm=2125;vm.NetworkBays[1].ThicknessMm=300;
        vm.AddCrossingBeam();var c=vm.CrossingBeams[0];c.BayNumber=2;c.PositionMm=3350;c.WidthMm=650;c.DepthMm=1150;c.BottomElevationMm=-1150;
        vm.OptimizeTowersAsync(false).GetAwaiter().GetResult();Console.WriteLine(vm.BuildDiagnosticReport("2026-10-18 S2 plywood edge regression"));
        Check(vm.IsCalculationValid,"reported calculation completes");var r=vm.NetworkResult;Verify(r);
        Near(r.Bays[1].StockLengthMm,1250,"reported S2 retains the 1250 mm catalogue H20");Near(r.Bays[1].EdgeMm,437.5,"reported S2 end strips");
        Check(!r.Bays[1].IsCustomLength,"reported S2 does not need custom stock");
        Console.WriteLine($"Passed {checks} reported slab plywood end-strip checks.");
    }
}
