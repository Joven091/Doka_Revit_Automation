using System;
using System.Linq;
using EngineeringDashboard;

internal static class UnifiedTransferTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Unified transfer: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(b)),message+$" ({a:R} / {b:R})");
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,IgnoreUplift=true,DeflectionDenominator=270};
    // The shared side uses a 2125 mm clear bay: catalogue 1800 mm H20
    // leaves two valid bearing/end allowances. The exact automatic report stays unchanged.
    static TransferNetworkInput Network(bool mixed=false)=>new TransferNetworkInput
    {
        PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,
        UpperSpacingMm=305,BeamSpacingsMm=mixed ? new[] {305.0,275,305} : new[] {350.0,275,350},SlabSpacingsMm=new[] {488.0,488},CrossingSpacingsMm=new[] {350.0,350},
        Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=i==1 || mixed ? 800 : 650,DepthMm=i==1 || mixed ? 1750 : 1150,BottomElevationMm=i==1 || mixed ? -1750 : -1150}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=mixed ? 2125 : 2272,ThicknessMm=mixed ? 200 : 300},new TransferBayRow {WidthMm=2252,ThicknessMm=300}},
        CrossingBeams=Enumerable.Range(0,2).Select(i=>new TransferCrossingBeamRow {BayNumber=i+1,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150,SupportMode=mixed ? i==0 ? CrossBeamSupportMode.ExistingSupportsOnly : CrossBeamSupportMode.DedicatedTowers : CrossBeamSupportMode.Automatic}).ToArray()
    };
    static void Verify(TransferNetworkResult result)
    {
        foreach(var layout in result.Beams)
        {
            var t=layout.Transfer;
            Check(t.PrimaryPlan!=null && t.PrimaryPlan.IsResolved,"selected support path retains a resolved primary-zone model");
            Check(t.FullLoadingOnly && t.CaseNames.Length==1,"one fully loaded production case");
            foreach(var m in t.PrimaryCases[0])
            {
                Check(m.Points.Length==0,"primary has no row point loads");
                foreach(var q in m.Uniform)
                {
                    Check(new[] {q.StartMm,q.EndMm}.All(edge=>Math.Abs(edge)<1e-7 || Math.Abs(edge-m.LengthMm)<1e-7 || result.Input.CrossingBeams.Any(c=>Math.Abs(m.StartMm+edge-(c.PositionMm-c.WidthMm/2))<1e-7 || Math.Abs(m.StartMm+edge-(c.PositionMm+c.WidthMm/2))<1e-7)),"UDL boundaries are stock ends or physical crossing faces, never 80 mm flange/ledger strips");
                }
                var manual=LoadedBeamAnalysis.AnalyzeDenseReference(m.LengthMm,m.SupportsMm,m.Uniform,null,m.EI,layout.DeflectionDenominator);
                Near(m.Analysis.MomentKnM,manual.MomentKnM,"independent moment uses displayed zones");Near(m.Analysis.ShearKn,manual.ShearKn,"independent shear uses displayed zones");Near(m.Analysis.DeflectionMm,manual.DeflectionMm,"independent deflection uses displayed zones");
                for(int i=0;i<m.SupportsMm.Length;i++)Near(m.Analysis.Reactions[i],manual.Reactions[i],"independent support reaction");
                Check(m.Utilization<=1+1e-8 && m.Analysis.Reactions.Max()<=layout.LegCapacityKn+1e-8,"member and leg limits pass the same load model");
            }
            for(int row=0;row<t.RowsMm.Length;row++)
            {
                var actual=t.LowerCases[0][row];var analysis=actual.Analysis;var loads=actual.Uniform;var points=actual.Points;
                var view=EquivalentTransferredLoading.Create(layout,row,0);
                Check(view.IsAvailable && view.IsDesignSlice,"production reference diagram remains available on every row");
                Near(view.ReferenceSpacingMm,t.EnteredUpperSpacingMm,"uses this beam's actual reference pitch");
                for(int line=0;line<2;line++)Near(view.Member.Analysis.Reactions[line]/(view.ReferenceSpacingMm/1000),view.PrimaryTransferKnM[line],"one spacing division exactly reproduces checked primary transfer");
                foreach(var c in view.Components)Near(c.SourceValue*c.ReferenceSpacingMm/c.NominalSpacingMm*c.AllocationFactor,c.EquivalentValue,"card conversion reproduces displayed equivalent load");
                Check(ReferenceEquals(actual.Analysis,analysis) && ReferenceEquals(actual.Uniform,loads) && ReferenceEquals(actual.Points,points),"reference view does not change physical secondary checks");
            }
        }
        var n=result.Input;double expected=0;
        foreach(var b in n.Beams)expected+=n.AreaLoad(b.DepthMm)*b.WidthMm/1000*result.Beams[0].LengthMm/1000;
        for(int bay=0;bay<n.Bays.Length;bay++)
        {
            var crosses=n.CrossingBeams.Where(c=>c.BayNumber==bay+1).ToArray();
            expected+=n.AreaLoad(n.Bays[bay].ThicknessMm)*n.Bays[bay].WidthMm/1000*(result.Beams[0].LengthMm-crosses.Sum(c=>c.WidthMm))/1000;
            expected+=crosses.Sum(c=>n.AreaLoad(c.DepthMm)*c.WidthMm*n.Bays[bay].WidthMm/1e6);
        }
        double main=result.Beams.Sum(b=>b.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn)+b.Transfer.ColumnSupportLoadKn(0));
        double dedicated=result.CrossSupports.Where(c=>c.Towers!=null).Sum(c=>c.Towers.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn));
        Near(main+dedicated,expected,"beam, slab and crossing force counted once across main primaries, dedicated towers and external ends");
        foreach(var support in result.CrossSupports.Where(c=>c.Towers!=null))
        {
            if(support.Towers.CantileverImbalanceMm>1e-7)Check(!string.IsNullOrEmpty(support.SymmetryNotice),"retained asymmetric crossing has a reason");
        }
    }
    internal static void Run(bool mixed=false)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(Common(),Network(mixed));
        Console.WriteLine("Unified "+(mixed ? "mixed" : "reported automatic")+" seconds="+timer.Elapsed.TotalSeconds);Console.WriteLine(result.LayoutDiagnostics);
        Check(result.Bays.All(b=>b!=null),"complete three-beam system retains both slabs");
        if(mixed)
        {
            Check(result.CrossSupports[0].UsesSpreader && result.CrossSupports[1].Towers!=null,"explicit shared and dedicated supports coexist");
            var middle=result.Beams[1].Transfer.PrimaryPlan;
            Check(middle.CrossingLoads.ContainsKey(0) && middle.CrossingTransfers.ContainsKey(1),"dedicated side does not disable the shared crossing model on the other side");
        }
        else Check(result.CrossSupports.All(c=>c.Towers!=null),"exact reported equal-soffit crossings take their dedicated fallback");
        Verify(result);Symmetry();PendingAssignments();
        Console.WriteLine($"Passed {checks} unified transfer acceptance checks.");
    }
    static void PendingAssignments()
    {
        var common=Common();var network=Network(true);
        foreach(var crossing in network.CrossingBeams)crossing.SupportMode=CrossBeamSupportMode.Automatic;
        var plan=EngineerPrimaryPlan.Create(common,network,1);
        var result=new TransferNetworkResult {Beams=new[] {new TowerLayout {Transfer=new TransferredSlabResult()}},BeamCentresMm=new[] {0.0}};
        var first=new CrossBeamSupportResult {Input=network.CrossingBeams[0],UsesSpreader=true};
        var second=new CrossBeamSupportResult {Input=network.CrossingBeams[1],UsesSpreader=true};
        var partial=plan.Resolve(common,result,0,new[] {first},pendingAssignments:true);
        double x=network.CrossingBeams[0].PositionMm,own=network.AreaLoad(network.Beams[1].DepthMm)*network.Beams[1].WidthMm/2000;
        Near(partial.ValueAt(x,0),plan.ValueAt(x,0),"assigned shared crossing retains its complete tributary load in a prefix");
        Near(partial.ValueAt(x,1),own,"unassigned automatic crossing is not prematurely charged as full shared load");
        var complete=plan.Resolve(common,result,0,new[] {first,second});
        for(int line=0;line<2;line++)Near(complete.ValueAt(x,line),plan.ValueAt(x,line),"complete shared system restores every assigned crossing before final checks");
        var planning=plan.Resolve(common,result,0,new[] {first});
        Near(planning.ValueAt(x,1),plan.ValueAt(x,1),"local shared-receiver planning retains the known full-tributary design basis");
    }
    static void Symmetry()
    {
        Func<double,TowerLayout> fixture=first=>new TowerLayout {EngineerSearch=true,LengthMm=1800,WidthMm=1524,PositionsMm=new[] {first,first+1000},Joined=new[] {true},StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=1800,FirstSupport=0,LastSupport=1}}}};
        var balanced=fixture(400);var unbalanced=fixture(150);
        Near(balanced.CantileverImbalanceMm,0,"400/400 cantilevers are balanced");Near(unbalanced.CantileverImbalanceMm,500,"150/650 imbalance measured from actual stock ends");
        Check(TransferredSlabOptimizer.Better(balanced,unbalanced) && !TransferredSlabOptimizer.Better(unbalanced,balanced),"symmetry preference applies to ordinary primary layouts as well as crossings");
        foreach(double value in new[] {.0001,-.0001,.0000001})Check(LoadValue.Format(value)!="0" && !LoadValue.Format(value).StartsWith("0.000 kN"),"tiny signed loads cannot be rounded to a zero applied-load label");
    }
}
