using System;
using System.Linq;
using EngineeringDashboard;

internal static class UpliftAssumptionTests
{
    static int checks;
    static void Check(bool value,string text) {checks++;if(!value)throw new Exception("Connected formwork: "+text);}
    static void Near(double actual,double expected,string text)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),text);
    static TransferMember[] Members(TowerLayout b)=>b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(c=>c).Concat(b.Transfer.UpperMembers).ToArray();
    static void Engineering(TowerLayout b)
    {
        Check(b.IgnoreUplift && b.MaxLegKn<=b.LegCapacityKn+1e-8 && b.Utilization<=1+1e-8,"only uplift acceptance is excluded");
        foreach(var m in Members(b))
        {
            Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8,"every accepted member has full strength and deflection checks");
            Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"signed force equilibrium");
            double moment=m.Analysis.Reactions.Zip(m.SupportsMm,(r,x)=>r*x/1000).Sum();
            double applied=(m.Points??new BeamPointLoad[0]).Sum(p=>p.Kn*p.PositionMm/1000)+(m.Uniform??new BeamDistributedLoad[0]).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.EndMm+q.StartMm)/2000);
            Near(moment,applied,"signed moment equilibrium");
        }
        Near(b.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn),b.Transfer.BeamLoadKn,"beam-only UDL preserves covered beam load");
        Check(b.Transfer.PrimaryCases.Max(c=>c.Sum(m=>m.Analysis.TotalLoadKn))>=b.Transfer.BeamLoadKn+b.Transfer.SlabLoadKn-1e-6,"primary UDL covers physical beam and slab reference load");
        for(int i=0;i<b.PositionsMm.Length;i++)for(int line=0;line<2;line++)
        {
            var values=b.Transfer.PrimaryCases.SelectMany(c=>c).Where(m=>m.Name.EndsWith(line==0 ? " L" : " R"))
                .SelectMany(m=>m.SupportsMm.Select((x,j)=>new {X=x+m.StartMm,R=m.Analysis.Reactions[j]}))
                .Where(v=>Math.Abs(v.X-b.PositionsMm[i])<1e-6).Select(v=>v.R).ToArray();
            Near(b.MinimumLegReaction(line==0,i),values.Min(),"displayed minimum agrees with signed stage analyses");
            Near((line==0 ? b.LeftReactions[i] : b.RightReactions[i]),values.Max(),"displayed maximum agrees with stage analyses");
            // Output minima are separately retained from compression maxima.
            Check(TowerBeamAnalysis.Finite(b.MinimumLegReaction(line==0,i)) && b.MinimumLegReaction(line==0,i)<=(line==0 ? b.LeftReactions[i] : b.RightReactions[i])+1e-8,"signed leg envelopes are finite and ordered");
        }
    }
    static TransferMember SimplySupported(double length,double q,double point,int denominator)
    {
        var supports=new[] {0.0,length};
        var uniform=q==0 ? new BeamDistributedLoad[0] : new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=q}};
        var points=point==0 ? new BeamPointLoad[0] : new[] {new BeamPointLoad {PositionMm=length/2,Kn=point}};
        return new TransferMember {Name="H20 rejection fixture",H20Count=1,LengthMm=length,SupportsMm=supports,Uniform=uniform,Points=points,Analysis=LoadedBeamAnalysis.Analyze(length,supports,uniform,points,TowerOptimizer.PairEI/2,denominator)};
    }
    static void MandatoryMemberChecks()
    {
        var input=new TowerInput {IgnoreUplift=true,LegCapacityKn=70,DeflectionDenominator=500};
        var moment=SimplySupported(1000,0,22,500);
        Near(moment.Analysis.MomentKnM,5.5,"independent PL/4 moment");
        Check(moment.Analysis.ShearKn<=11+1e-8 && moment.Analysis.DeflectionUtilization<1,"moment fixture isolates bending failure");
        Check(!CrossBeamSupportOptimizer.Pass(moment,input,"test"),"moment failure rejected with uplift excluded");
        var shear=SimplySupported(100,0,23,500);
        Near(shear.Analysis.ShearKn,11.5,"independent P/2 shear");
        Check(shear.Analysis.MomentKnM<5 && shear.Analysis.DeflectionUtilization<1,"shear fixture isolates shear failure");
        Check(!CrossBeamSupportOptimizer.Pass(shear,input,"test"),"shear failure rejected with uplift excluded");
        var deflection=SimplySupported(4500,1,0,500);
        Near(deflection.Analysis.DeflectionMm,5*Math.Pow(4.5,4)/(384*(TowerOptimizer.PairEI/2))*1000,"independent 5qL^4/384EI deflection");
        Check(deflection.Analysis.MomentKnM<5 && deflection.Analysis.ShearKn<11 && deflection.Analysis.DeflectionUtilization>1,"deflection fixture isolates deflection failure");
        Check(!CrossBeamSupportOptimizer.Pass(deflection,input,"test"),"deflection failure rejected with uplift excluded");
        foreach(var failed in new[] {moment,shear,deflection})
            Check(!CrossBeamSupportOptimizer.Envelope(input,failed,new TransferMember[0],null,new TransferMember()),"subset bound retains each mandatory H20 check");
        var uplift=new TransferMember {Name="signed subset",H20Count=2,Analysis=LoadedBeamAnalysis.AnalyzeSigned(1000,new[] {150.0,850},null,new[] {new BeamPointLoad {PositionMm=100,Kn=-.2},new BeamPointLoad {PositionMm=900,Kn=1}},TowerOptimizer.PairEI,270)};
        var reported=new TransferMember();
        input.IgnoreUplift=false;
        Check(!CrossBeamSupportOptimizer.Pass(uplift,input,"test") && !CrossBeamSupportOptimizer.Envelope(input,uplift,new TransferMember[0],70,reported),"cross member and subset uplift rejected by default");
        input.IgnoreUplift=true;
        Check(CrossBeamSupportOptimizer.Pass(uplift,input,"test") && CrossBeamSupportOptimizer.Envelope(input,uplift,new TransferMember[0],70,reported),"cross member and subset uplift-only failures explicitly excluded");
        Near(reported.SubsetMinimumReactionsKn.Min(),uplift.Analysis.Reactions.Min(),"subset retains negative reaction bound");
        Check(!CrossBeamSupportOptimizer.Envelope(input,uplift,new TransferMember[0],.01,reported),"subset individual leg capacity remains enforced");
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=-.5}};
        var signed=LoadedBeamAnalysis.AnalyzeUniformCached(1000,new[] {150.0,850},uniform,TowerOptimizer.PairEI,270,null,70,true,true);
        Check(signed.DeflectionEvaluated && signed.Reactions.All(r=>r<0),"signed primary UDL gets complete deflection analysis");
        Near(signed.Reactions.Sum(),-.5,"upward distributed transfer force retained");
        var combined=TransferredSlabOptimizer.PrimaryUniformLoads(0,1000,.1,-.6,0,null,null,true);
        Near(combined.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),-.5,"primary load combination preserves signed UDL");
    }
    static void ModernCrossChecks()
    {
        foreach(var mode in new[] {CrossBeamSupportMode.ExistingSupportsOnly,CrossBeamSupportMode.DedicatedTowers})
        {
            var common=new TowerInput {IgnoreUplift=true,LengthMm=2450,LegCapacityKn=70,DeflectionDenominator=350,MaximumConcreteEndOverhangMm=0,AllowSeparateSlabFallback=false,NetworkSearchWorkers=1};
            if(mode==CrossBeamSupportMode.DedicatedTowers)common.MaximumConcreteEndOverhangMm=300; // Independent below-main crossing must clear the side shutters at both ends.
            var network=new TransferNetworkInput {UpperSpacingMm=100,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=300,DepthMm=600}},Bays=new[] {new TransferBayRow {WidthMm=3900,ThicknessMm=100}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=400,DepthMm=150,BottomElevationMm=mode==CrossBeamSupportMode.DedicatedTowers ? -1400 : -400,SupportMode=mode}}};
            var result=TransferNetworkOptimizer.Optimize(common,network);
            var cross=result.CrossSupports.Single();
            var layouts=result.Beams.Concat(cross.Towers==null ? new TowerLayout[0] : new[] {cross.Towers}).ToArray();
            Check(layouts.All(b=>b.IgnoreUplift && b.MaxLegKn<=common.LegCapacityKn+1e-8),"uplift assumption and leg limit retained after modern cross recheck");
            foreach(var b in layouts)for(int i=0;i<b.PositionsMm.Length;i++)for(int line=0;line<2;line++)
            {
                var reactions=b.Transfer.PrimaryCases.SelectMany(c=>c).Where(m=>m.Name.EndsWith(line==0 ? " L" : " R"))
                    .SelectMany(m=>m.SupportsMm.Select((x,j)=>new {X=x+m.StartMm,R=m.Analysis.Reactions[j]})).Where(v=>Math.Abs(v.X-b.PositionsMm[i])<1e-6).Select(v=>v.R).ToArray();
                Near(b.MinimumLegReaction(line==0,i),reactions.Min(),"modern cross minimum signed leg envelope");
                Near((line==0 ? b.LeftReactions[i] : b.RightReactions[i]),reactions.Max(),"modern cross maximum individual leg envelope");
            }
            foreach(var member in layouts.SelectMany(Members).Concat(cross.Members))
            {
                Check(member.Analysis.DeflectionEvaluated && member.Analysis.MomentKnM<=member.MomentLimitKnM+1e-8 && member.Analysis.ShearKn<=member.ShearLimitKn+1e-8 && member.Analysis.DeflectionUtilization<=1+1e-8,"shared and dedicated H20s pass moment, shear and deflection");
                Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"modern cross signed force equilibrium");
            }
        }
    }
    public static void Run()
    {
        MandatoryMemberChecks();
        ModernCrossChecks();
        Check(!new TowerInput().IgnoreUplift && new BeamCalculatorViewModel().IgnoreUplift,"core bearing checks remain available; calculator uplift exclusion is fixed on");
        var positions=new[] {150.0,750,1350};var joined=new[] {true,true};
        var input=new TowerInput {LengthMm=3000,StockLengthsMm=new[] {3000.0},TotalLineLoad=.5,LegCapacityKn=70,DeflectionDenominator=270};
        Check(TowerOptimizer.Evaluate(input,positions,joined,1524,false)==null,"uplift-only geometry rejected by default");
        input.IgnoreUplift=true;
        var layout=TowerOptimizer.Evaluate(input,positions,joined,1524,false);
        Check(layout!=null && layout.MinimumReactionKn<0 && layout.UpliftSummary.Contains("assumed by the user"),"opt-in retains actual negative reactions and labels assumption");
        Near(layout.LeftReactions.Sum()+layout.RightReactions.Sum(),1.5,"signed tower reactions conserve load");
        input.LegCapacityKn=.01;Check(TowerOptimizer.Evaluate(input,positions,joined,1524,false)==null,"leg capacity still rejects");
        input.LegCapacityKn=70;input.TotalLineLoad=100;Check(TowerOptimizer.Evaluate(input,positions,joined,1524,false)==null,"strength/deflection still reject");
        var points=new[] {new BeamPointLoad {PositionMm=100,Kn=-.2},new BeamPointLoad {PositionMm=900,Kn=1}};
        bool rejected=false;try {LoadedBeamAnalysis.AnalyzePointsCached(1000,new[] {150.0,850},points,TowerOptimizer.PairEI,270,null);}catch(ArgumentException){rejected=true;}
        Check(rejected,"negative applied loads require explicit opt-in");
        var signed=LoadedBeamAnalysis.AnalyzePointsCached(1000,new[] {150.0,850},points,TowerOptimizer.PairEI,270,null,70,true,true);
        Check(signed.DeflectionEvaluated && signed.Reactions.Min()<0,"staged primary check evaluates deflection when only uplift is excluded");
        Near(signed.TotalLoadKn,.8,"negative transfer force retained");Near(signed.Reactions.Sum(),.8,"signed point-load equilibrium");
        var strictDiag=new TowerSearchDiagnostics(270);strictDiag.Analysis(signed,1,2,"primary",3,"test");
        Check(strictDiag.Exception("test").Details.Contains("uplift:"),"default diagnostics record uplift");
        var assumedDiag=new TowerSearchDiagnostics(270,ignoreUplift:true);assumedDiag.Analysis(signed,1,2,"primary",3,"test");assumedDiag.Legs(signed.Reactions,.01,"legs","test");
        var details=assumedDiag.Exception("test").Details;Check(!details.Contains("uplift:") && details.Contains("leg load:"),"diagnostics omit excluded uplift and retain capacity failures");
        var vm=new BeamCalculatorViewModel {UseTowers=true,IgnoreUplift=true,BeamLengthMm=2500,StockLengthsText="2500",Depth=200,Width=300};
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();Check(vm.IsCalculationValid && vm.TowerResult.IgnoreUplift,"view model passes opt-in");
        Check(vm.BuildDiagnosticReport().Contains("IgnoreUplift = True") && vm.TowerCalculationBasis.Contains("not calculated"),"diagnostic and calculation basis identify assumption");
        vm.IgnoreUplift=false;Check(vm.IgnoreUplift && vm.IsCalculationValid && vm.TowerResult!=null,"fixed exclusion cannot be toggled through legacy inputs");
        vm.IgnoreUplift=true;vm.StartNewSession();Check(vm.IgnoreUplift,"new session retains the fixed uplift assumption");
        Console.WriteLine($"Passed {checks} uplift assumption checks.");
    }
    public static void ReportedCase(string schedulePath=null)
    {
        var network=new TransferNetworkInput {UpperSpacingMm=275,
            Beams=Enumerable.Range(0,8).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),
            Bays=new[] {2677.0,1875,3275,2275,2278,2272,2252}.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray(),
            CrossingBeams=Enumerable.Range(1,7).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=3450,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.LegacySlabRows}).ToArray()};
        var common=new TowerInput {LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,Exclude600Standalone=true,DeflectionDenominator=270,AllowCustomH20Lengths=true};
        foreach(bool ignore in new[] {false,true})
        {
            common.IgnoreUplift=ignore;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result=TransferNetworkOptimizer.Optimize(common,network);
                Check(ignore,"reported case remains rejected under default bearing-only assumptions");
                Check(!result.RequiresSeparateSlabFormwork,"all entered bays calculated");
                foreach(var b in result.Beams)Engineering(b);
                Console.WriteLine($"Reported 6900 mm case: IgnoreUplift={ignore}; {timer.Elapsed.TotalSeconds:0.00}s; {result.Summary}");
                foreach(var b in result.Beams)Console.WriteLine(b.UpliftSummary);
                Check(result.Beams.All(b=>TowerBeamAnalysis.Finite(b.MinimumReactionKn)),"reported signed reactions retained");
            }
            catch(TowerOptimizationException ex)
            {
                Console.WriteLine($"Reported 6900 mm case: IgnoreUplift={ignore}; no solution; {timer.Elapsed.TotalSeconds:0.00}s; {ex.Message}");
                if(ignore)Check(!ex.Details.Contains("uplift:"),"excluded uplift removed while remaining checks reject at 275 mm");
                else Check(ex.Details.Contains("uplift:"),"reported default failure reproduced");
            }
        }
        common.IgnoreUplift=true;
        foreach(double spacing in new[] {250.0,225,200,175,150})
        {
            network.UpperSpacingMm=spacing;var timer=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result=TransferNetworkOptimizer.Optimize(common,network);
                Check(!result.RequiresSeparateSlabFormwork,"all entered bays calculated at closer spacing");
                foreach(var beam in result.Beams)Engineering(beam);
                Check(result.Beams.All(b=>TowerBeamAnalysis.Finite(b.MinimumReactionKn)),"accepted signed reaction envelopes retained");
                Console.WriteLine($"Reported case at {spacing:0} mm: {timer.Elapsed.TotalSeconds:0.00}s; {result.Summary}");
                Console.WriteLine(result.LayoutDiagnostics);
                var members=result.Beams.SelectMany(Members).ToArray();
                Console.WriteLine($"H20 maximum check utilizations: moment {members.Max(m=>m.Analysis.MomentKnM/m.MomentLimitKnM)*100:0.00}%; shear {members.Max(m=>m.Analysis.ShearKn/m.ShearLimitKn)*100:0.00}%; deflection {members.Max(m=>m.Analysis.DeflectionUtilization)*100:0.00}%.");
                foreach(var beam in result.Beams)Console.WriteLine(beam.UpliftSummary);
                if(schedulePath!=null)System.IO.File.WriteAllText(schedulePath,
                    $"Upper spacing {spacing:0} mm; uplift excluded under the user-selected connected plywood/deadweight assumption.\n"
                    +result.Summary+"\n\n"+result.LayoutDiagnostics+"\n\n"+string.Join("\n",result.Beams.Select((b,i)=>$"B{i+1}: {b.UpliftSummary}"))+"\n\n"+result.Schedule);
                Console.WriteLine($"Passed {checks} reported-case checks.");return;
            }
            catch(TowerOptimizationException ex) {Console.WriteLine($"Reported case at {spacing:0} mm: no solution; {timer.Elapsed.TotalSeconds:0.00}s; {ex.Message}");}
        }
        throw new Exception("No passing reported-case layout found at checked upper spacings.");
    }
}
