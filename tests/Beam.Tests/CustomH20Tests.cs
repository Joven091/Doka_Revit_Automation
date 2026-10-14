using System;
using System.Linq;
using EngineeringDashboard;

internal static class CustomH20Tests
{
    static int checks;
    static void Check(bool value,string name) {checks++;if(!value)throw new Exception("Custom H20: "+name);}
    static void Reject(Action action,string name) {bool rejected=false;try {action();}catch(ArgumentException){rejected=true;}Check(rejected,name);}
    static TowerInput Input() => new TowerInput {LengthMm=2125,StockLengthsMm=new[] {300.0},TotalLineLoad=2,LegCapacityKn=70,DeflectionDenominator=350};
    static void Members(TowerLayout result,double[] catalogue)
    {
        Check(result.StockPlan.Pieces.All(p=>catalogue.Contains(p.StockLengthMm) || p.StockLengthMm%25==0),"primary lengths lie on grid");
        Check(result.StockPlan.Pieces.All(p=>p.IsCustomLength==!catalogue.Contains(p.StockLengthMm)),"primary custom labels");
        Check(result.StockPlan.Pieces.All(p=>p.FirstSupport<p.LastSupport),"independent primary members have two or more supports");
        Check(result.MaxLegKn<=70+1e-8 && result.Utilization<=1+1e-8,"leg and structural limits retained");
        if(result.Transfer!=null)
        {
            var t=result.Transfer;
            Check(catalogue.Contains(t.LowerStockLengthMm) || t.LowerStockLengthMm%25==0,"lower length lies on grid");
            Check(t.LowerCustom==!catalogue.Contains(t.LowerStockLengthMm),"lower custom label");
            Check(t.UpperMembers.All(m=>catalogue.Contains(m.LengthMm) || m.LengthMm%25==0),"slab lengths lie on grid");
            Check(t.PrimaryCases.Concat(t.LowerCases).SelectMany(c=>c).Concat(t.UpperMembers).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.All(r=>r>=-1e-7)),"all members and stages pass without bearing uplift");
        }
    }
    static TransferNetworkInput ScopeNetwork(double bay) => new TransferNetworkInput {UpperSpacingMm=250,CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,
        Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=300,DepthMm=200}},
        Bays=new[] {new TransferBayRow {WidthMm=bay,ThicknessMm=100}}};
    public static void Scope()
    {
        foreach(CustomH20LengthScope scope in Enum.GetValues(typeof(CustomH20LengthScope)))
        {
            var i=Input();i.CustomH20Scope=scope;
            Check(!i.AllowCustomBeamLengths && !i.AllowCustomSlabLengths,"master switch disables both scoped options");
            i.AllowCustomH20Lengths=true;
            Check(i.AllowCustomBeamLengths==(scope!=CustomH20LengthScope.SlabSupports),"beam custom permission follows scope");
            Check(i.AllowCustomSlabLengths==(scope!=CustomH20LengthScope.BeamSupports),"slab custom permission follows scope");
            Check(i.CopyForSearch().CustomH20Scope==scope,"worker copies retain scope");
        }
        Reject(()=>new TowerInput {CustomH20Scope=(CustomH20LengthScope)99},"unknown numerical scope rejected");
        Reject(()=>new BeamCalculatorViewModel {CustomH20Scope=(CustomH20LengthScope)99},"unknown view-model scope rejected");
        var common=Input();common.AllowCustomH20Lengths=true;common.AllowSeparateSlabFallback=false;
        common.StockLengthsMm=new[] {1250.0,1800};common.CustomH20Scope=CustomH20LengthScope.BeamSupports;
        var beam=TransferNetworkOptimizer.Optimize(common,ScopeNetwork(1800));
        Check(beam.Beams.All(b=>b.StockPlan.Pieces.Any(p=>p.IsCustomLength)),"beam-only custom primaries solve missing catalogue coverage");
        Check(beam.Bays.All(b=>!b.IsCustomLength && common.StockLengthsMm.Contains(b.StockLengthMm)),"beam-only slabs remain catalogue lengths");
        Check(beam.Beams.SelectMany(b=>b.Transfer.UpperMembers).All(m=>common.StockLengthsMm.Contains(m.LengthMm)),"beam-only upper members remain catalogue lengths");
        Reject(()=>TransferNetworkOptimizer.Optimize(common,ScopeNetwork(2600)),"beam-only cannot silently generate a custom slab fallback");
        common.CustomH20Scope=CustomH20LengthScope.SlabSupports;
        Reject(()=>TransferNetworkOptimizer.Optimize(common,ScopeNetwork(1800)),"slab-only cannot generate custom receiving-beam coverage");
        common.LengthMm=2450;common.StockLengthsMm=new[] {1800.0,2450};
        var slab=TransferNetworkOptimizer.Optimize(common,ScopeNetwork(2600));
        Check(slab.Bays.All(b=>b.IsCustomLength && b.StockLengthMm%25==0),"slab-only solves bay with custom slab H20s");
        Check(slab.Beams.All(b=>b.StockPlan.Pieces.All(p=>!p.IsCustomLength) && !b.Transfer.LowerCustom),"slab-only keeps primary and lower H20s catalogue lengths");
        foreach(var b in slab.Beams)Members(b,common.StockLengthsMm);
        common.AllowCustomH20Lengths=false;
        Reject(()=>TransferNetworkOptimizer.Optimize(common,ScopeNetwork(2600)),"disabled master switch cannot retain slab-only custom lengths");
        var standalone=Input();standalone.AllowCustomH20Lengths=true;standalone.CustomH20Scope=CustomH20LengthScope.BeamSupports;standalone.StockLengthsMm=new[] {1250.0,2450};
        standalone.TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=2,UpperSpacingMm=250,RightBayMm=2600,RightAreaLoadKnM2=2};
        Reject(()=>TowerOptimizer.Optimize(standalone),"standalone beam-only rejects implicit custom slab fallback");
        var crossInput=new TransferNetworkInput {UpperSpacingMm=250,LocalRowGapMm=100,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=300,DepthMm=600}},Bays=new[] {new TransferBayRow {WidthMm=3900,ThicknessMm=100}},CrossingBeams=new[] {new TransferCrossingBeamRow {PositionMm=1225,WidthMm=400,DepthMm=150,BottomElevationMm=-400,SupportMode=CrossBeamSupportMode.ExistingSupportsOnly}}};
        var crossCommon=new TowerInput {LengthMm=2450,LegCapacityKn=70,DeflectionDenominator=350,AllowSeparateSlabFallback=false,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.BeamSupports,StockLengthsMm=new[] {1250.0,1800,2450,3300}};
        var cross=TransferNetworkOptimizer.Optimize(crossCommon,crossInput);
        Check(cross.CrossSupports.Single().Members.Any(m=>!crossCommon.StockLengthsMm.Contains(m.LengthMm)) && cross.Bays.All(b=>!b.IsCustomLength),"beam-only allows custom shared crossing H20s with catalogue slab H20s");
        crossCommon.CustomH20Scope=CustomH20LengthScope.SlabSupports;
        Reject(()=>TransferNetworkOptimizer.Optimize(crossCommon,crossInput),"slab-only cannot generate custom shared crossing H20s");
        crossInput.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;crossInput.CrossingBeams[0].BottomElevationMm=-600;
        crossCommon.CustomH20Scope=CustomH20LengthScope.BeamSupports;
        var dedicated=TransferNetworkOptimizer.Optimize(crossCommon,crossInput).CrossSupports.Single();
        Check(dedicated.Towers!=null && dedicated.Bearings.Any(b=>b.IsConcreteEndZone) && dedicated.Towers.StockPlan.Pieces.Any(p=>p.IsCustomLength),"beam-only scope reaches dedicated crossing primaries with checked junction ends");
        crossCommon.CustomH20Scope=CustomH20LengthScope.SlabSupports;
        var catalogueDedicated=TransferNetworkOptimizer.Optimize(crossCommon,crossInput).CrossSupports.Single();
        Check(catalogueDedicated.Towers!=null && catalogueDedicated.Towers.StockPlan.Pieces.All(p=>!p.IsCustomLength && crossCommon.StockLengthsMm.Contains(p.StockLengthMm)) && !catalogueDedicated.Towers.Transfer.LowerCustom,"slab-only dedicated crossing uses available catalogue primary and secondary lengths");
        var vm=new BeamCalculatorViewModel {UseTowers=true,BeamLengthMm=2125,Depth=200,Width=300,StockLengthsText="1250",MaximumConcreteEndOverhangMm=0,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.BeamSupports};
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();Check(vm.IsCalculationValid,"view model propagates beam-only scope");
        Check(vm.BuildDiagnosticReport().Contains("CustomH20Scope = BeamSupports"),"diagnostics include selected scope");
        vm.CustomH20Scope=CustomH20LengthScope.SlabSupports;Check(!vm.IsCalculationValid && vm.TowerResult==null,"scope changes invalidate existing calculation");
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();Check(!vm.IsCalculationValid && vm.TowerResult==null,"view model slab-only cannot cut beam stock");
        vm.StartNewSession();Check(vm.CustomH20Scope==CustomH20LengthScope.Both && !vm.AllowCustomH20Lengths,"new session resets scope and master switch");
        Console.WriteLine($"Passed {checks} custom H20 scope checks.");
    }
    public static void Run()
    {
        var input=Input();input.StockLengthsMm=new[] {1250.0};
        Reject(()=>TowerOptimizer.Optimize(input),"catalogue-only exact coverage still rejects missing length");
        input.AllowCustomH20Lengths=true;var pure=TowerOptimizer.Optimize(input);Members(pure,input.StockLengthsMm);
        Check(pure.StockPlan.Pieces.Any(p=>p.IsCustomLength) && pure.StockSchedule.Contains("custom length"),"custom primary solves missing-length case and is reported");
        Check(input.StockLengthsMm.SequenceEqual(new[] {1250.0}) && input.AllowCustomH20Lengths,"search preserves catalogue and enabled flag");
        input.LengthMm=2137;Reject(()=>TowerOptimizer.Optimize(input),"25 mm lengths cannot give exact non-grid coverage");
        input.MaximumConcreteEndOverhangMm=75;var ends=TowerOptimizer.Optimize(input);Members(ends,input.StockLengthsMm);
        Check(ends.ConcreteEndOverhangMm<=75 && ends.StockPlan.Pieces.All(p=>p.StartMm>=0 && p.EndMm<=2137),"non-grid concrete length uses allowed ends without projection");
        var stockInput=Input();stockInput.StockLengthsMm=new[] {2125.0};var original=TowerOptimizer.Optimize(stockInput);
        stockInput.AllowCustomH20Lengths=true;var enabled=TowerOptimizer.Optimize(stockInput);
        Check(enabled.LegCount<original.LegCount || enabled.LegCount==original.LegCount && (enabled.BayCount<original.BayCount || enabled.BayCount==original.BayCount && (enabled.StockPlan.TotalH20Count<original.StockPlan.TotalH20Count || enabled.StockPlan.TotalH20Count==original.StockPlan.TotalH20Count && enabled.Utilization<=original.Utilization+1e-10)),"existing ranking cannot worsen catalogue result");
        var upper=UpperTransferPlan.SlabLengths(1800,new[] {1250.0},true);
        Check(upper.Contains(1250) && upper.Contains(1100) && upper.Contains(1400) && upper.All(x=>x%25==0),"slab search includes grid even when catalogue fits");
        Check(UpperTransferPlan.SlabLengths(1800,new[] {1250.0}).SequenceEqual(new[] {1250.0}),"disabled slab behaviour retained");
        input=Input();input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=2,UpperSpacingMm=250,RightBayMm=1800,RightAreaLoadKnM2=2};
        Reject(()=>TowerOptimizer.Optimize(input),"transfer missing primary/lower stocks rejected with option off");
        input.AllowCustomH20Lengths=true;var transfer=TowerOptimizer.Optimize(input);Members(transfer,input.StockLengthsMm);
        Check(transfer.Transfer.LowerCustom && transfer.StockPlan.Pieces.All(p=>p.IsCustomLength),"custom primary and lower members solve transfer");
        var common=Input();common.AllowCustomH20Lengths=true;
        var network=new TransferNetworkInput {UpperSpacingMm=250,CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=300,DepthMm=200}},Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,network);
        foreach(var beam in result.Beams)Members(beam,common.StockLengthsMm);
        Check(result.Bays.All(b=>b.StockLengthMm%25==0) && result.Beams.All(b=>b.Transfer.LowerCustom),"network propagates custom mode to shared upper and every receiving beam");
        Check(result.Beams[0].Transfer.SlabRowIndices.Select(i=>result.Beams[0].Transfer.RowsMm[i]).SequenceEqual(result.Beams[1].Transfer.SlabRowIndices.Select(i=>result.Beams[1].Transfer.RowsMm[i])),"network support rows remain aligned");
        Check(timer.Elapsed.TotalSeconds<45,"small custom network completes within workspace calculation budget");
        var vm=new BeamCalculatorViewModel {UseTowers=true,BeamLengthMm=2125,Depth=200,Width=300,StockLengthsText="1250",MaximumConcreteEndOverhangMm=0,AllowCustomH20Lengths=true};
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();Check(vm.IsCalculationValid,"view model passes custom setting to optimizer");
        vm.AllowCustomH20Lengths=false;Check(!vm.IsCalculationValid && vm.TowerResult==null,"toggle invalidates existing result");
        Console.WriteLine($"Passed {checks} custom H20 checks; network {timer.Elapsed.TotalSeconds:0.00}s.");
    }
}
