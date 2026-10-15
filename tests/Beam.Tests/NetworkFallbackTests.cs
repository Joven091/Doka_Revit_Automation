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
    internal static TransferNetworkInput Network()=>new TransferNetworkInput {UpperSpacingMm=250,
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
        Near(result.SlabLoadKn,input.AreaLoad(300)*input.Bays[1].WidthMm/1000*result.Beams[0].LengthMm/1000,"only the retained bay load transfers");
        Near(result.TimberMm,result.Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*b.Transfer.LowerStockLengthMm)+result.Bays[1].StockLengthMm*result.SlabRowsMm.Length,"separate slab timber is excluded");
        Check(result.UpperFootCount==2*result.SlabRowsMm.Length,"separate slab feet are excluded");
    }
    public static void Run()
    {
        ReportedEngineerFallback();
        var common=Common();var input=Network();common.AllowSeparateSlabFallback=false;
        TowerOptimizationException original=null;bool failed=false;try {TransferNetworkOptimizer.Optimize(common,input);}catch(TowerOptimizationException ex){original=ex;failed=true;}
        Check(failed,"full transfer cannot support the wide first bay");
        common.AllowSeparateSlabFallback=true;common.NetworkSearchWorkers=1;
        var serial=TransferNetworkOptimizer.Optimize(common,input);
        Check(serial.FullTransferFailureDetails==original.Details,"successful fallback retains the exact rejected full-transfer evidence");
        Check(serial.SeparateSlabReasons.Length==input.Bays.Length && serial.SeparateSlabExplanationForBay(1)=="","retained bay has no independent-support reason");
        Check(serial.SeparateSlabExplanationForBay(0).Contains("S1: no passing") && serial.SeparateSlabExplanationForBay(0).Contains("10000 mm") && serial.SeparateSlabExplanationForBay(0).Contains("no permitted slab H20 length"),"excluded bay identifies the stock coverage failure and actual span");
        Check(serial.SeparateSlabExplanation.Contains("bounded search does not prove impossibility") && serial.SeparateSlabExplanation.Contains("not calculated or included in totals"),"explanation identifies search and design scope");
        Check(serial.Schedule.Contains(serial.SeparateSlabExplanation) && serial.LayoutDiagnostics.Contains(serial.SeparateSlabExplanation),"reason survives schedules and diagnostic capture");
        var evidence=new TowerSearchDiagnostics(350);
        evidence.Limit("S1 slab H20","shear",1,12,11,"kN","T3 · x=500 mm","Reduce secondary spacing.");
        evidence.Limit("B2 · primary","leg load",4,71,70,"kN","support 2","Review tower positions.");
        evidence.Geometry("S10 length","S10 unrelated","Review S10.");
        var scoped=evidence.SeparateBayExplanation(0);
        Check(scoped.Contains("12 / 11 kN") && scoped.Contains("T3 · x=500 mm") && scoped.Contains("Reduce secondary spacing") && scoped.Contains("71 / 70 kN") && scoped.Contains("may also involve adjacent bays") && !scoped.Contains("S10 unrelated"),"scoped evidence preserves demand, limit, location and advice without prefix collisions or false attribution");
        Check(serial.RequiresSeparateSlabFormwork && serial.SeparateSlabCount==1 && serial.Bays[0]==null && serial.Bays[1]!=null,"only S1 moves to separate slab formwork");
        Check(serial.Beams[0].Transfer.UpperMembers.Length==0 && serial.Beams[0].Transfer.CaseNames.SequenceEqual(new[] {"Beam"}),"B1 is checked for its own beam load");
        Near(serial.Beams[0].Transfer.SlabLoadKn,0,"B1 has no slab or shutter load");
        Near(serial.Beams[0].TotalSupportedLoadKn,input.AreaLoad(input.Beams[0].DepthMm)*input.Beams[0].WidthMm/1000*common.LengthMm/1000,"summary excludes separate slab formwork loads");
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
        Check(full.FullTransferFailureDetails=="" && full.SeparateSlabReasons.Length==0,"full transfer has no fallback explanation");
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

    private static void ReportedEngineerFallback()
    {
        // Exact 2026-10-15 report: the copied coordinated search failed full
        // slab transfer, then a valid separate-formwork result crashed on apply.
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,
            Depth=1750,Width=800,BeamLengthMm=6100,FloorHeight=3000,TowerLegCapacityKn=30,AllowCustomH20Lengths=false,
            IgnoreUplift=true,Exclude600Standalone=true,PlywoodDeflectionLimit="L/350",
            DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        foreach(var beam in vm.NetworkBeams){beam.WidthMm=800;beam.DepthMm=1750;beam.BottomElevationMm=-1750;}
        vm.NetworkBays[0].WidthMm=2125;vm.NetworkBays[0].ThicknessMm=200;
        vm.AddCrossingBeam();var cross=vm.CrossingBeams[0];cross.PositionMm=3350;cross.WidthMm=650;cross.DepthMm=1150;cross.BottomElevationMm=-1150;
        vm.OptimizeTowersAsync(false).GetAwaiter().GetResult();
        Check(vm.IsCalculationValid,vm.BuildDiagnosticReport("reported fallback regression"));
        var result=vm.NetworkResult;
        Check(result.RequiresSeparateSlabFormwork && result.Bays[0]==null,"reported slab is explicitly excluded from beam-supported formwork");
        Check(result.SeparateSlabExplanationForBay(0).Contains("2125 mm") && result.SeparateSlabExplanationForBay(0).Contains("no permitted slab H20 length"),"copied failed search preserves the actual slab span and stock rejection");
        Check(result.FullTransferFailureDetails.Contains("no permitted slab H20 length"),"original rejected full-transfer evidence remains available");
        var crossing=result.CrossSupports.Single(c=>c.CrossingIndex==0);
        Check(crossing.Members.Length>0,"modern X1 support remains calculated when S1 needs separate formwork");
        Near(result.SlabLoadKn,0,"separate S1 contributes no slab load to the returned design");
        Near(crossing.SlabEdgeLoadKn,0,"separate S1 contributes no slab-edge ledger loads to X1");
        Near(crossing.LoadKn,result.Input.AreaLoad(1150)*.65*2.125,"X1 retains its complete concrete and construction load");
        var layouts=result.Beams.Concat(result.CrossSupports.Where(c=>c.Towers!=null).Select(c=>c.Towers)).ToArray();
        foreach(var beam in layouts)
        {
            Check(beam.MaxLegKn<=30+1e-8,"reported 30 kN leg limit is retained");
            foreach(var member in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c).Concat(beam.Transfer.UpperMembers))
            {
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8,"returned members pass all final checks");
                Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"reported fallback member equilibrium");
            }
        }
        foreach(var member in crossing.Members)
        {
            Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8,"X1 members retain full passing checks");
            Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"X1 member equilibrium");
        }
        double carried=layouts.Sum(b=>b.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn));
        double external=layouts.Sum(b=>b.Transfer.ColumnSupportLoadKn(b.Transfer.CaseNames.Length-1));
        Near(carried+external,2*result.Input.AreaLoad(1750)*.8*6.1+crossing.LoadKn,"full beam and X1 load included once; separate slab load excluded");
        Console.WriteLine("Reported 6100 mm engineer fallback passed; S1 requires separate formwork and X1 remains calculated.");
    }
}
