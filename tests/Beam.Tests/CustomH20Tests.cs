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
        input=Input();input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=2,UpperSpacingMm=200,RightBayMm=1800,RightAreaLoadKnM2=2};
        Reject(()=>TowerOptimizer.Optimize(input),"transfer missing primary/lower stocks rejected with option off");
        input.AllowCustomH20Lengths=true;var transfer=TowerOptimizer.Optimize(input);Members(transfer,input.StockLengthsMm);
        Check(transfer.Transfer.LowerCustom && transfer.StockPlan.Pieces.All(p=>p.IsCustomLength),"custom primary and lower members solve transfer");
        var common=Input();common.AllowCustomH20Lengths=true;
        var network=new TransferNetworkInput {UpperSpacingMm=200,CustomLoads=true,LiveKnM2=0,FormworkKnM2=0,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=300,DepthMm=200}},Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,network);
        foreach(var beam in result.Beams)Members(beam,common.StockLengthsMm);
        Check(result.Bays.All(b=>b.StockLengthMm%25==0) && result.Beams.All(b=>b.Transfer.LowerCustom),"network propagates custom mode to shared upper and every receiving beam");
        Check(result.Beams[0].Transfer.RowsMm.SequenceEqual(result.Beams[1].Transfer.RowsMm),"network support rows remain aligned");
        Check(timer.Elapsed.TotalSeconds<45,"small custom network completes within workspace calculation budget");
        var vm=new BeamCalculatorViewModel {UseTowers=true,BeamLengthMm=2125,Depth=200,Width=300,StockLengthsText="1250",MaximumConcreteEndOverhangMm=0,AllowCustomH20Lengths=true};
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();Check(vm.IsCalculationValid,"view model passes custom setting to optimizer");
        vm.AllowCustomH20Lengths=false;Check(!vm.IsCalculationValid && vm.TowerResult==null,"toggle invalidates existing result");
        Console.WriteLine($"Passed {checks} custom H20 checks; network {timer.Elapsed.TotalSeconds:0.00}s.");
    }
}
