using System;
using System.Linq;
using EngineeringDashboard;

internal static class CrossEndStripTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Cross end strips: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<=1e-7*Math.Max(1,Math.Abs(expected)),message);
    static TowerInput Common()=>new TowerInput {LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true,IgnoreUplift=true};
    static TransferMember[] Members(TowerLayout layout)=>layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers).ToArray();
    static void MemberChecks(TransferMember member,int denominator)
    {
        var independent=LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,TowerOptimizer.PairEI*member.H20Count/2,denominator);
        Near(member.Analysis.MomentKnM,independent.MomentKnM,"reported moment includes all applied loads");
        Near(member.Analysis.ShearKn,independent.ShearKn,"reported shear includes all applied loads");
        Near(member.Analysis.DeflectionMm,independent.DeflectionMm,"reported deflection includes all applied loads");
        Check(member.Analysis.DeflectionEvaluated && member.Analysis.MomentKnM<=member.MomentLimitKnM+1e-8 && member.Analysis.ShearKn<=member.ShearLimitKn+1e-8 && member.Analysis.DeflectionUtilization<=1+1e-8,"every accepted H20 passes moment, shear and deflection");
        Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"signed member force equilibrium");
        double applied=(member.Uniform??new BeamDistributedLoad[0]).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.StartMm+q.EndMm)/2000)+(member.Points??new BeamPointLoad[0]).Sum(p=>p.Kn*p.PositionMm/1000);
        Near(member.Analysis.Reactions.Zip(member.SupportsMm,(r,x)=>r*x/1000).Sum(),applied,"signed member moment equilibrium");
    }
    static void EndStripChecks(TowerLayout layout,double q,double span,double maximumEnd=75)
    {
        double edge=layout.ConcreteEndOverhangMm;
        Check(edge>=0 && edge<=maximumEnd+1e-7 && layout.MaxLegKn<=layout.LegCapacityKn+1e-8,"entered end allowance and individual leg limit retained");
        Near(layout.Transfer.BeamLoadKn,q*span/1000,"physical concrete load includes both end strips");
        Near(layout.Transfer.TributaryWidthsMm.Sum(),span,"secondary tributaries retain full concrete span");
        var primary=layout.Transfer.PrimaryCases[0];
        Near(primary.Sum(m=>m.Analysis.TotalLoadKn),q*span/1000,"full cross load reaches primary pairs");
        Near(primary.Sum(m=>m.Analysis.Reactions.Sum()),q*span/1000,"full cross load reaches individual legs");
        Near(primary.Sum(m=>m.Uniform.Sum(u=>u.KnPerM*(u.EndMm-u.StartMm)/1000)),q*span/1000,"primary UDL retains full concrete load including end strips");
        Check(primary.All(m=>m.Points.Length==0),"all crossing primary design loads are UDLs without row point loads");
        Near(primary.Sum(m=>m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*(x+m.StartMm)/1000).Sum()),q*span/1000*span/2000,"end-strip transfer preserves full concrete moment about bay face");
        foreach(var member in Members(layout))MemberChecks(member,layout.DeflectionDenominator);
        if(edge<=1e-7)return;
        var rows=layout.Transfer.RowsMm;int last=rows.Length-1;
        double force=q*edge/2000;
        double leftSecond=force*(edge/2-rows[0])/(rows[1]-rows[0]);
        double rightLast=force*(span-edge/2-rows[last-1])/(rows[last]-rows[last-1]);
        var expected=new double[rows.Length];expected[0]+=force-leftSecond;expected[1]+=leftSecond;expected[last-1]+=force-rightLast;expected[last]+=rightLast;
        var influence=TransferredSlabOptimizer.ConcreteEndStripTributaries(span-2*edge,rows.Select(x=>x-edge).ToArray(),edge);
        Check(influence.Any(v=>v<0),"signed sheathing end-strip influence retained in secondary tributaries");
        for(int row=0;row<rows.Length;row++)Near(influence[row]*q/2000,expected[row],"secondary end-strip influence agrees with independent two-row statics");

    }
    static TransferNetworkInput Small(CrossBeamSupportMode mode=CrossBeamSupportMode.DedicatedTowers)=>new TransferNetworkInput {UpperSpacingMm=250,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=300,DepthMm=600}},Bays=new[] {new TransferBayRow {WidthMm=2677,ThicknessMm=100}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=400,DepthMm=150,BottomElevationMm=-1400,SupportMode=mode}}};
    public static void Run()
    {
        var rows=new[] {100.0,350,600,850};
        var widths=TransferredSlabOptimizer.ConcreteEndStripTributaries(950,rows,75);
        Near(widths.Sum(),150,"strip statics preserve force");Near(widths.Zip(rows,(w,x)=>w*x).Sum(),75*950,"strip statics preserve moment");
        Check(widths[0]>75 && widths[1]<0 && widths[2]<0 && widths[3]>75,"cantilever sheathing influence reactions are signed");
        double q=31.65*.65;
        foreach(double span in new[] {2677.0,2278,2272,2252})
        {
            var common=Common();common.LengthMm=span;
            common.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeConcreteEndStrips=true,BeamWidthMm=650,BeamLineLoadKnM=q,UpperSpacingMm=250};
            var result=TransferredSlabOptimizer.Optimize(common);EndStripChecks(result,q,span);
            Check(result.ConcreteEndOverhangMm>0,"non-25 mm span is supported using permitted end strips");
            Check(result.StockPlan.Pieces.All(p=>!p.IsCustomLength || p.StockLengthMm%25==0),"custom lengths remain multiples of 25 mm");
            // Replaying without caches/deferred construction must retain the same end-strip loads.
            common.ReuseSearchWork=false;common.SearchByLegTier=false;common.DeferTransferLayout=false;
            var reference=TransferredSlabOptimizer.Optimize(common);EndStripChecks(reference,q,span);
            Near(reference.MaxLegKn,result.MaxLegKn,"cached and reference end-strip leg loads agree");
            Near(reference.StockPlan.TotalStockLengthMm,result.StockPlan.TotalStockLengthMm,"cached and reference stock plans agree");
        }
        var legacy=Common();legacy.IgnoreUplift=false;legacy.LengthMm=2677;legacy.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeConcreteEndStrips=true,BeamWidthMm=650,BeamLineLoadKnM=q,UpperSpacingMm=250};
        var layout=TransferredSlabOptimizer.Optimize(legacy);EndStripChecks(layout,q,2677);
        Check(layout.IgnoreUplift && Members(layout).All(m=>m.Analysis.Reactions.All(TowerBeamAnalysis.Finite)),"legacy false retains signed end-strip reactions under fixed search policy");
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=1}};
        var supports=new[] {0.0,1000};
        var a=new TransferredSlabOptimizer.PrimaryKey(1000,supports,uniform,new[] {new BeamPointLoad {PositionMm=100,Kn=.1}});
        var b=new TransferredSlabOptimizer.PrimaryKey(1000,supports,uniform,new[] {new BeamPointLoad {PositionMm=100,Kn=.2}});
        Check(!a.Equals(b),"mixed-load response cache distinguishes end-strip forces");
        var commonNetwork=Common();commonNetwork.LengthMm=2450;commonNetwork.DeflectionDenominator=350;
        // This independent crossing is below the receiving H20s, so its own
        // permitted concrete end strips must also provide the 200 mm shutter gap.
        commonNetwork.MaximumConcreteEndOverhangMm=300;
        var dedicated=TransferNetworkOptimizer.Optimize(commonNetwork,Small());var cross=dedicated.CrossSupports.Single();
        Check(cross.Towers!=null && cross.Towers.ConcreteEndOverhangMm>0 && cross.Schedule.Contains("end strips"),"dedicated support retains end-strip detail in its schedule");
        EndStripChecks(cross.Towers,Small().AreaLoad(150)*.4,2677,300);
        Check(!cross.Bearings.Any(b=>b.IsShutterEndZone),"a receiver above the crossing cannot carry its end load down through a side shutter");
        Near(cross.LoadKn,Small().AreaLoad(150)*.4*2.677,"independent end strips retain the full cross load");
        foreach(var solid in cross.Solids)foreach(var existing in CrossBeamSupportOptimizer.NetworkSolids(dedicated))Check(!solid.Conflicts(existing),"end allowance does not bypass physical clashes");
        commonNetwork.MaximumConcreteEndOverhangMm=0;
        bool failed=false;
        try {TransferNetworkOptimizer.Optimize(commonNetwork,Small(CrossBeamSupportMode.Automatic));}
        catch(TowerOptimizationException e) {failed=true;Check(e.Message.Contains("X1") && e.Details.Contains("X1 dedicated") && e.Details.Contains("200 mm") && !e.Details.Contains("Dedicated cross-beam stock/tower search failed."),"cross failure retains the shutter-gap and exact-coverage constraint");}
        Check(failed,"zero end allowance still rejects impossible exact stock coverage");
        var vm=new BeamCalculatorViewModel {UseBeamNetwork=true};vm.AddCrossingBeam();vm.NetworkBeams[0].BottomElevationMm=-1750;vm.CrossingBeams[0].BottomElevationMm=-1150;vm.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        var report=vm.BuildDiagnosticReport();Check(report.Contains("bottom=-1750 mm") && report.Contains("bottom=-1150 mm; supports=DedicatedTowers"),"diagnostics capture soffit elevations and requested cross support mode");
        var currentCommon=Common();currentCommon.LengthMm=10450;
        var currentInput=new TransferNetworkInput {UpperSpacingMm=275,Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=3550,ThicknessMm=300},new TransferBayRow {WidthMm=2575,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=2500,WidthMm=300,DepthMm=600,SupportMode=CrossBeamSupportMode.Automatic}}};
        var current=TransferNetworkOptimizer.Optimize(currentCommon,currentInput);var currentCross=current.CrossSupports.Single();
        Check(current.SeparateSlabCount==0 && currentCross.Towers!=null,"current three-beam report still returns complete slab transfer and its own crossing tower");
        EndStripChecks(currentCross.Towers,currentInput.AreaLoad(600)*.3,3150);
        foreach(var beam in current.Beams)foreach(var member in Members(beam))MemberChecks(member,270);
        Near(currentCross.LoadKn,18.79725,"current X1 UDL retains full concrete and construction load");
        Console.WriteLine($"CURRENT DRAWING REPORT: {current.LegCount} legs; peak {current.PeakLegKn:0.000} kN; X1 primary load model UDL.");
        Console.WriteLine($"Cross end strips: {checks} checks passed.");
    }
    public static void ShutterSupported()
    {
        // Exact 2026-10-15 13:53 report: a catalogue 1800 mm shared H20 leaves
        // 162.5 mm shutter-supported strips in the 2125 mm bay. Beam custom
        // lengths are deliberately forbidden and the main end limit stays 150.
        var common=new TowerInput {FullLoadingOnly=true,EngineerSearch=true,LengthMm=7000,LegCapacityKn=70,
            StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,Exclude600Standalone=false,
            DeflectionDenominator=270,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,
            StockLengthsMm=new[] {1250.0,1800,2450,2650,2900,3300,3600,3900,4500,4900}};
        var input=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,
            PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm,UpperSpacingMm=305,
            BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new[] {350.0},
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300}},
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3375,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150,SupportMode=CrossBeamSupportMode.Automatic}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();
        // First exercise the exact reported loads: admitting stock must not hide
        // a subsequent receiving-member failure or approve an overloaded system.
        try
        {
            var reported=TransferNetworkOptimizer.Optimize(common,input);
            foreach(var beam in reported.Beams)
            {
                Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r<=70+1e-8),"reported solution respects each leg capacity");
                foreach(var member in Members(beam))MemberChecks(member,270);
            }
            foreach(var member in reported.CrossSupports.SelectMany(c=>c.Members))MemberChecks(member,270);
            Console.WriteLine("SHUTTER-SUPPORTED EXACT REPORT: complete passing system; legs="+reported.LegCount);
        }
        catch(TowerOptimizationException e)
        {
            Check(e.Details.Contains("combined receiving-secondary, primary or leg") && !e.Details.Contains("no permitted shared H20 stock"),"reported 1800 mm stock reaches combined receiver checks instead of the end-strip filter");
            Check(e.Details.Contains("shear:") || e.Details.Contains("moment:") || e.Details.Contains("deflection:"),"remaining failure identifies actual member demand");
            Console.WriteLine("SHUTTER-SUPPORTED EXACT REPORT: no complete passing system within bounded search; stock admitted, receiving capacity checks reject it.");
            Console.WriteLine(string.Join("\n",e.Details.Split('\n').Where(line=>line.StartsWith("T") && (line.Contains("shear:") || line.Contains("moment:") || line.Contains("deflection:")))));
        }
        // A lighter crossing with the same plan geometry verifies the accepted
        // 1800 mm stock, full shutter-strip transfer and downstream capacities.
        input.CrossingBeams[0].DepthMm=200;input.CrossingBeams[0].BottomElevationMm=-200;
        var result=TransferNetworkOptimizer.Optimize(common,input);var cross=result.CrossSupports.Single();
        Check(cross.Towers==null && cross.Members.All(m=>m.LengthMm==1800),"matching plan geometry uses shared catalogue 1800 mm H20s without a dedicated tower");
        Check(!common.AllowCustomBeamLengths && common.MaximumConcreteEndOverhangMm==150,"beam custom scope and main end allowance unchanged");
        var strips=cross.Bearings.Where(b=>b.IsConcreteEndZone && b.Label=="Crossing end strip").ToArray();
        Check(strips.Length>0 && strips.All(b=>b.IsShutterEndZone),"crossing concrete end strips use existing side shutters");
        foreach(var strip in strips)Near(strip.EndZoneEndMm-strip.EndZoneStartMm,162.5,"actual supported strip exceeds the unrelated 150 mm limit");
        Near(strips.Sum(b=>b.ReactionKn),input.AreaLoad(200)*.65*.325,"both concrete end strips counted once");
        Near(cross.Bearings.Sum(b=>b.ReactionKn),cross.LoadKn,"all crossing and ledger force reaches the receivers");
        double face=result.BeamCentresMm[0]+400;
        Near(cross.Bearings.Sum(b=>b.ReactionKn*(b.Ymm-face)/1000),cross.LoadKn*2.125/2,"shutter strips and H20 bearings retain transverse moment");
        foreach(var member in cross.Members)
        {
            Check(member.SupportsMm[0]>=150-1e-7 && member.LengthMm-member.SupportsMm.Last()>=150-1e-7,"real bearing-to-stock-end allowances retained");
            MemberChecks(member,270);
        }
        foreach(var beam in result.Beams)
        {
            Check(beam.EngineerSearch && beam.Transfer.FullLoadingOnly,"production search and fully loaded member checks retained");
            Check(beam.ConcreteEndOverhangMm<=150+1e-7,"main-beam concrete end allowance still enforced");
            Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r<=70+1e-8),"every leg passes with X1 and shutter reactions included");
            foreach(var member in Members(beam))MemberChecks(member,270);
        }
        var obstacles=CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).ToArray();
        foreach(var solid in cross.Solids)foreach(var obstacle in obstacles)Check(!solid.Conflicts(obstacle),"shared crossing retains physical clash checks");
        Console.WriteLine("SHUTTER-SUPPORTED REPORT seconds="+timer.Elapsed.TotalSeconds+" legs="+string.Join(",",result.Beams.Select(b=>b.LegCount))+" peak="+result.PeakLegKn);
        Console.WriteLine(result.LayoutDiagnostics);
        Console.WriteLine("Shutter-supported crossing checks passed: "+checks);
    }
    public static void Reported()
    {
        TowerFrameClearanceTests.Run();
        var network=new TransferNetworkInput {UpperSpacingMm=275,Beams=Enumerable.Range(0,8).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),Bays=new[] {2677.0,1875,3275,2275,2278,2272,2252}.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray(),CrossingBeams=Enumerable.Range(1,7).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=3450,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.Automatic}).ToArray()};
        var common=Common();common.StockLengthsMm=new[] {1250.0,1800,2450,2650,2900,3300,3600,3900,4500,4900};
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,network);
        Check(result.Beams.Length==8 && result.CrossSupports.Length==7 && result.SeparateSlabCount==0,"reported network supports all beams, crossings and slab bays");
        Check(result.PeakLegKn<=70+1e-8 && common.IgnoreUplift && common.Exclude600Standalone && common.StockEndSetbackMm==150,"leg, setback and standalone-600 settings remain in force");
        Check(network.UpperSpacingMm==275 && result.Input.UpperSpacingMm<=275 && result.Schedule.Contains("entered maximum 275"),"automatic spacing adjustment is within the unchanged input maximum and explained");
        Check(result.CrossSupports.All(c=>c.Towers!=null && c.Bearings.Any(b=>b.IsConcreteEndZone && !b.IsShutterEndZone) && c.Bearings.Any(b=>b.IsShutterEndZone)),"every mixed-depth crossing uses its own tower, a direct end zone and an existing side-shutter end zone");
        Check(result.CrossSupports.Any(c=>Math.Abs(c.TowerSpanOffsetMm)>0),"crossing tower supports can be displaced along their span");
        Check(result.CrossSupports.Any(c=>c.PrimaryNearExtensionMm+c.PrimaryFarExtensionMm>0),"short crossing spans coordinate permitted unloaded primary tails with complete tower footprints");
        foreach(var layout in result.Beams.Concat(result.CrossSupports.Select(c=>c.Towers)))
        {
            Check(layout.MaxLegKn<=70+1e-8,"each tower retains the individual leg limit");
            foreach(var piece in layout.StockPlan.Pieces)
            {
                Check(layout.PositionsMm[piece.FirstSupport]-piece.StartMm>=150-1e-7 && piece.EndMm-layout.PositionsMm[piece.LastSupport]>=150-1e-7,"each real support retains the stock end setbacks after translation");
                Check(!piece.IsCustomLength || piece.StockLengthMm%25==0,"custom beam stocks retain 25 mm increments");
            }
            for(int first=0;first<layout.PositionsMm.Length;)
            {
                int last=first;while(last<layout.Joined.Length && layout.Joined[last])last++;
                Check(last!=first+1 || Math.Abs(layout.PositionsMm[last]-layout.PositionsMm[first]-600)>1e-7,"no standalone 600 mm tower is introduced");first=last+1;
            }
            foreach(var member in Members(layout))MemberChecks(member,270);
            Check(layout.Transfer.PrimaryCases.SelectMany(c=>c).All(m=>m.Points.Length==0),"primary member checks retain their UDL design model");
        }
        var obstacles=CrossBeamSupportOptimizer.NetworkSolids(result).Concat(CrossBeamSupportOptimizer.Concrete(result)).ToArray();
        var crossingSolids=result.CrossSupports.SelectMany(c=>c.Solids).ToArray();
        foreach(var solid in crossingSolids)
        {
            foreach(var obstacle in obstacles)Check(!solid.Conflicts(obstacle),"cross timber and full tower columns clear all main formwork and concrete");
            foreach(var other in crossingSolids.Where(o=>!ReferenceEquals(o,solid)))Check(!solid.Conflicts(other),"all crossing members and columns clear one another");
        }
        foreach(var crossing in result.CrossSupports)
        {
            Check(CrossBeamSupportOptimizer.PrimaryShutterClash(result,crossing.CrossingIndex,crossing.Solids)==null,"every crossing primary stock end keeps the 200 mm side-shutter gap");
            Check(crossing.Towers.Transfer.RowsMm.Skip(1).Where((x,i)=>x-crossing.Towers.Transfer.RowsMm[i]>network.UpperSpacingMm+1e-7).Any()==false,"every crossing secondary row interval respects the entered spacing limit");
            int bay=crossing.Input.BayNumber-1;double face=result.BeamCentresMm[bay]+network.Beams[bay].WidthMm/2;
            double expected=network.AreaLoad(1150)*.65*network.Bays[bay].WidthMm/1000;
            var reactions=crossing.Bearings.Where(b=>b.IsConcreteEndZone || b.IsDedicated && b.Label.StartsWith("Dedicated P")).ToArray();
            Near(crossing.LoadKn,expected,"full crossing concrete and construction load conserved");
            Near(reactions.Sum(b=>b.ReactionKn),expected,"cross load reaches the direct end zone and tower legs exactly once");
            Near(reactions.Sum(b=>b.ReactionKn*b.Xmm/1000),expected*3.45,"eccentric width placement preserves force moment along the main beams");
            Near(reactions.Sum(b=>b.ReactionKn*b.Ymm/1000),expected*(face+network.Bays[bay].WidthMm/2)/1000,"translated tower legs preserve force moment along the crossing span");
        }
        Console.WriteLine($"REPORTED COORDINATED PASS: {result.LegCount} legs; peak {result.PeakLegKn:0.000} kN; secondary spacing {result.Input.UpperSpacingMm:0.###}/275 mm; {timer.Elapsed.TotalSeconds:0.00}s; {checks} checks.");
    }
}
