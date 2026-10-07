using System;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class NetworkFallbackTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Network fallback: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(b)),message);
    internal static TowerInput Common()=>new TowerInput {LengthMm=3300,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=350,AllowCustomH20Lengths=false};
    internal static TransferNetworkInput Network()=>new TransferNetworkInput {UpperSpacingMm=200,
        Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=400,DepthMm=500}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=10000,ThicknessMm=300},new TransferBayRow {WidthMm=2500,ThicknessMm=300}}};
    static void Engineering(TransferNetworkResult result)
    {
        var input=result.Input;
        foreach(var beam in result.Beams)
        {
            Check(beam.MaxLegKn<=70+1e-8 && beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7),"calculated legs retain capacity and no-uplift checks");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"every retained member has full passing checks");
                Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"member equilibrium");
            }
        }
        Near(result.SlabLoadKn,input.AreaLoad(300)*input.Bays[1].WidthMm/1000*result.SlabCoveredLengthMm/1000,"only the retained bay load transfers");
        Near(result.TimberMm,result.Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*b.Transfer.LowerStockLengthMm)+result.Bays[1].StockLengthMm*result.Beams[0].Transfer.RowsMm.Length,"separate slab timber is excluded");
        Check(result.UpperFootCount==2*result.Beams[0].Transfer.RowsMm.Length,"separate slab feet are excluded");
    }
    public static void Run()
    {
        var common=Common();var input=Network();common.AllowSeparateSlabFallback=false;
        bool failed=false;try {TransferNetworkOptimizer.Optimize(common,input);}catch(TowerOptimizationException){failed=true;}
        Check(failed,"full transfer cannot support the wide first bay");
        common.AllowSeparateSlabFallback=true;common.NetworkSearchWorkers=1;
        var serial=TransferNetworkOptimizer.Optimize(common,input);
        Check(serial.RequiresSeparateSlabFormwork && serial.SeparateSlabCount==1 && serial.Bays[0]==null && serial.Bays[1]!=null,"only S1 moves to separate slab formwork");
        Check(serial.Beams[0].Transfer.UpperMembers.Length==0 && serial.Beams[0].Transfer.CaseNames.SequenceEqual(new[] {"Beam"}),"B1 is checked for its own beam load");
        Near(serial.Beams[0].Transfer.SlabLoadKn,0,"B1 has no slab or shutter load");
        Check(serial.Beams[1].Transfer.LeftUpperStockMm==0 && serial.Beams[1].Transfer.RightUpperStockMm>0 && serial.Beams[2].Transfer.LeftUpperStockMm>0,"B2 and B3 still carry S2");
        Check(serial.Summary.Contains("Separate slab formwork required: S1") && serial.Schedule.Contains("design are excluded") && serial.LayoutDiagnostics.Contains("S1: separate slab formwork"),"scope exclusion is visible in summaries, schedules and diagnostics");
        Check(serial.Beams.All(b=>b.StockPlan.Pieces.All(p=>common.StockLengthsMm.Contains(p.StockLengthMm)) && common.StockLengthsMm.Contains(b.Transfer.LowerStockLengthMm)),"fallback retains catalogue primary and lower stocks");
        Check(serial.Bays.Where(b=>b!=null).All(b=>!b.IsCustomLength && common.StockLengthsMm.Contains(b.StockLengthMm)),"disabled custom lengths also retain catalogue upper stocks");
        Engineering(serial);
        common.NetworkSearchWorkers=4;var parallel=TransferNetworkOptimizer.Optimize(common,input);
        Check(serial.Summary==parallel.Summary && serial.Schedule==parallel.Schedule && serial.LayoutDiagnostics==parallel.LayoutDiagnostics,"serial and parallel fallback choices agree");
        common.PruneNetworkBounds=false;common.ReuseTransverseNumerics=false;common.SearchByLegTier=false;
        var reference=TransferNetworkOptimizer.Optimize(common,input);
        Check(serial.Summary==reference.Summary && serial.Schedule==reference.Schedule,"optimized fallback matches the unpruned reference search");
        // A crossing beam within an independently supported bay belongs to that separate design.
        input.CrossingBeams=new[] {new TransferCrossingBeamRow {SupportMode=CrossBeamSupportMode.LegacySlabRows,BayNumber=1,PositionMm=1650,WidthMm=300,DepthMm=10000}};
        var crossing=TransferNetworkOptimizer.Optimize(Common(),input);
        Check(crossing.Bays[0]==null && crossing.Schedule.Contains("X1: S1 · separate formwork design required (not calculated)"),"excluded crossing is expressly identified");
        Engineering(crossing);
        input=Network();input.Bays[0].WidthMm=2575;
        var noCut=TransferNetworkOptimizer.Optimize(Common(),input);
        Check(noCut.Bays[0]==null && noCut.Bays[1]!=null,"custom disabled does not silently cut an upper H20 when no catalogue length fits");
        // Full transfer remains preferred when it passes; excluded slab costs are never used to claim savings.
        input=Network();input.Bays[0].WidthMm=2500;
        var full=TransferNetworkOptimizer.Optimize(Common(),input);
        Check(!full.RequiresSeparateSlabFormwork && full.Bays.All(b=>b!=null),"a passing full-transfer network does not invoke the fallback");
        input=Network();input.Beams[0].WidthMm=10000;input.Beams[0].DepthMm=10000;
        failed=false;try {TransferNetworkOptimizer.Optimize(Common(),input);}catch(TowerOptimizationException){failed=true;}
        Check(failed,"fallback cannot approve a beam whose own load fails");
        using(var source=new CancellationTokenSource())
        {
            source.Cancel();common=Common();common.Trace=new BeamSearchTrace("fallback cancellation",source.Token);
            bool cancelled=false;try {TransferNetworkOptimizer.Optimize(common,Network());}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"fallback preserves cancellation");
        }
        Console.WriteLine($"Passed {checks} network fallback checks.");
    }
}
