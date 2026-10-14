using System;
using System.Linq;
using EngineeringDashboard;

internal static class IndividualSpacingTests
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-7,message+": "+actual);
    private static void Reject(Action action,string message){bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,message);}
    private static void Rows(double[] rows,double maximum,string message)
    {
        Check(rows.Length>=2,message+" has supports");
        Check(rows.Zip(rows.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=maximum+1e-7),message+" respects spacing and H20 flange clearance");
    }
    private static void Members(TowerLayout layout)
    {
        Check(layout.Utilization<=1+1e-8,"receiving layout passes every capacity check");
        foreach(var m in layout.Transfer.LowerCases.SelectMany(x=>x).Concat(layout.Transfer.UpperMembers))
        {
            Check(m.H20Count==1 && m.Analysis.MomentKnM<=5+1e-8 && m.Analysis.ShearKn<=11+1e-8 && m.Analysis.DeflectionUtilization<=1+1e-8,"single H20 passes M=5 kNm, V=11 kN and selected deflection criterion");
            var reference=LoadedBeamAnalysis.Analyze(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,449.8756,layout.DeflectionDenominator);
            Near(m.Analysis.MomentKnM,reference.MomentKnM,"reported H20 moment agrees with its actual loading");
            Near(m.Analysis.ShearKn,reference.ShearKn,"reported H20 shear agrees with its actual loading");
            Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"H20 reactions balance applied loads");
        }
        foreach(var m in layout.Transfer.PrimaryCases.SelectMany(x=>x))Check(m.Utilization<=1+1e-8,"primary passes each construction stage");
        Check(layout.LeftReactions.Concat(layout.RightReactions).All(x=>x<=layout.LegCapacityKn+1e-8),"individual tower legs remain checked");
    }
    private static void Loads(TransferNetworkResult result)
    {
        for(int beam=0;beam<result.Beams.Length;beam++)
        {
            var t=result.Beams[beam].Transfer;var b=result.Input.Beams[beam];
            foreach(var upper in t.UpperMembers)
            {
                bool left=upper.Name=="Upper left";int bay=left ? beam-1 : beam;
                Check(upper.Uniform.Sum(q=>q.KnPerM)>=result.Input.AreaLoad(result.Input.Bays[bay].ThicknessMm)*result.Input.BaySpacing(bay)/1000-1e-8,"slab H20 design uses at least its own bay spacing, never a tighter beam's spacing");
            }
            for(int c=0;c<t.LowerCases.Length;c++)for(int row=0;row<t.RowsMm.Length;row++)
            {
                var m=t.LowerCases[c][row];
                double own=result.Input.AreaLoad(b.DepthMm)*b.WidthMm/1000*Math.Max(t.TributaryWidthsMm[row],result.Input.BeamSpacing(beam))/1000;
                Near(m.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),own,"lower H20 beam force follows this beam's depth and spacing");
                bool hasLeft=t.CaseNames[c].Contains("left") || t.CaseNames[c].Contains("both"),hasRight=t.CaseNames[c].Contains("right") || t.CaseNames[c].Contains("both");
                double feet=0,shutters=0;
                if(hasLeft && beam>0){double q=result.Input.AreaLoad(result.Input.Bays[beam-1].ThicknessMm),scale=t.UpperScale(true,row);feet+=q*t.LeftUpperStockMm/1000*scale/2;shutters+=q*t.LeftUpperEdgeMm/1000*scale;}
                if(hasRight && beam<result.Input.Bays.Length){double q=result.Input.AreaLoad(result.Input.Bays[beam].ThicknessMm),scale=t.UpperScale(false,row);feet+=q*t.RightUpperStockMm/1000*scale/2;shutters+=q*t.RightUpperEdgeMm/1000*scale;}
                Near(m.Points.Sum(q=>q.Kn),feet+shutters,"slab foot and shutter forces use the correct independent slab tributaries");
            }
        }
    }
    private static void CompareReceiverReuse(BeamCalculatorViewModel vm)
    {
        // Replay the same crossing with reuse disabled: pruning must preserve the selected layout.
        Func<bool,TowerInput> common=reuse=>new TowerInput {LengthMm=vm.BeamLengthMm,LegCapacityKn=vm.TowerLegCapacityKn,
            TotalLineLoad=vm.NetworkResult.Input.AreaLoad(1750)*.8,DeflectionDenominator=270,
            StockLengthsMm=TowerStockPlanner.ParseLengths(vm.StockLengthsText),StockEndSetbackMm=vm.StockEndSetbackMm,
            MaximumConcreteEndOverhangMm=vm.MaximumConcreteEndOverhangMm,AllowCustomH20Lengths=true,
            CustomH20Scope=vm.CustomH20Scope,Exclude600Standalone=true,IgnoreUplift=true,ReuseSearchWork=reuse};
        var reused=TransferNetworkOptimizer.Optimize(common(true),vm.NetworkResult.Input);
        var reference=TransferNetworkOptimizer.Optimize(common(false),vm.NetworkResult.Input);
        Near(reused.LegCount,reference.LegCount,"receiver failure pruning preserves tower count");
        Near(reused.EconomyScoreMm,reference.EconomyScoreMm,"receiver failure pruning preserves layout economy");
        for(int i=0;i<reused.Beams.Length;i++)
        {
            Check(reused.Beams[i].PositionsMm.SequenceEqual(reference.Beams[i].PositionsMm),"receiver reuse preserves actual tower positions");
            Check(reused.Beams[i].Transfer.RowsMm.SequenceEqual(reference.Beams[i].Transfer.RowsMm),"receiver reuse preserves actual secondary rows");
            Members(reused.Beams[i]);Members(reference.Beams[i]);
        }
    }
    internal static void PrefixCacheParity()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,UseBeamNetwork=true,TransferSlabLoads=true,
            BeamLengthMm=4500,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=75,AllowCustomH20Lengths=true,
            IgnoreUplift=true,Exclude600Standalone=true,DeflectionLimit=BeamCalculatorViewModel.AvailableDeflectionOptions.Single(d=>d.Denominator==270)};
        vm.AddNetworkBay();for(int i=0;i<vm.NetworkBeams.Count;i++){vm.NetworkBeams[i].WidthMm=i==1 ? 650 : 800;vm.NetworkBeams[i].SetDepthKeepingTopMm(i==1 ? 1150 : 1750);}
        foreach(var bay in vm.NetworkBays){bay.WidthMm=2575;bay.ThicknessMm=300;}
        // Bay 2 comes first: adding the third beam changes the crossing labels in the prefix.
        foreach(int bay in new[] {2,1})vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=bay,PositionMm=2250,WidthMm=650,DepthMm=1150});
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && vm.NetworkResult!=null,"reordered crossing prefixes calculate safely: "+vm.WarningMessage);
        Check(vm.NetworkResult.CrossSupports.All(c=>c.Input.BayNumber==vm.NetworkResult.Input.CrossingBeams[c.CrossingIndex].BayNumber),"cache reuse preserves each crossing's own bay and label");
        foreach(var c in vm.NetworkResult.CrossSupports)
        {
            Near(c.LoadKn,vm.NetworkResult.Input.AreaLoad(c.Input.DepthMm)*c.Input.WidthMm/1000*vm.NetworkBays[c.Input.BayNumber-1].WidthMm/1000,"reordered crossing retains its own complete concrete load");
            Check(c.Members.All(m=>m.Utilization<=1+1e-8),"reordered crossing H20s pass all checks");
        }
        CompareReceiverReuse(vm);
        Console.WriteLine($"Passed {checks} crossing-prefix cache parity checks.");
    }
    internal static void ShutterException()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,BeamLengthMm=4500,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=75,AllowCustomH20Lengths=true,IgnoreUplift=true,TransferSlabLoads=true,UseBeamNetwork=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,Exclude600Standalone=true,DeflectionLimit=BeamCalculatorViewModel.AvailableDeflectionOptions.Single(d=>d.Denominator==270)};
        foreach(var b in vm.NetworkBeams){b.WidthMm=800;b.SetDepthKeepingTopMm(1750);}
        vm.NetworkBays[0].WidthMm=2175;vm.NetworkBays[0].ThicknessMm=300;
        vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=1,PositionMm=2250,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.DedicatedTowers});
        Check(vm.CrossingBeams[0].MaximumSecondarySpacingMm>0,"directly added crossings calculate their own spacing immediately");vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && vm.NetworkResult!=null,"independent spacing preserves receiving-layout coordination: "+vm.WarningMessage);
        Check(vm.NetworkResult.CrossSupports.Single().HasReducedShutterClearance,"coordinated local shutter exception remains available");
        foreach(var b in vm.NetworkResult.Beams)Members(b);
        Check(vm.NetworkResult.CrossSupports.SelectMany(c=>c.Members).All(m=>m.Utilization<=1+1e-8),"crossing H20 capacities remain checked in the local exception");
        CompareReceiverReuse(vm);
        Console.WriteLine($"Passed {checks} coordinated individual-spacing checks.");
    }
    internal static void Run()
    {
        var signedInput=new TowerInput {NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache()};
        var downward=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=-1}};var supports=new[] {0.0,1000};
        var signed=TransferredSlabOptimizer.AnalyzeTransverse(signedInput,BeamSearchCounter.LowerAnalyses,1000,supports,downward,null,449.8756,350,signed:true);
        Near(signed.TotalLoadKn,-1,"signed crossing increments retain their complete negative force");
        Near(TransferredSlabOptimizer.AnalyzeTransverse(signedInput,BeamSearchCounter.LowerAnalyses,1000,supports,downward,null,449.8756,350).TotalLoadKn,-1,"fixed search policy retains signed loads when reusing crossing responses");
        Reject(()=>LoadedBeamAnalysis.Analyze(1000,supports,downward,null,449.8756,350),"ordinary low-level analysis still validates unsigned loads");
        Near(TransferredSlabOptimizer.AnalyzeTransverse(signedInput,BeamSearchCounter.LowerAnalyses,1000,supports,downward,null,449.8756,350,signed:true).TotalLoadKn,-1,"reused signed crossing response retains its force");
        foreach(double first in PlywoodSpacing.StandardSpacingsMm)
        foreach(double second in PlywoodSpacing.StandardSpacingsMm)
        foreach(double length in new[] {2450.0,6100,10600})
        {
            var a=PlywoodSpacing.SheetSupportRows(0,length,first);var b=PlywoodSpacing.CompatibleSheetRows(0,length,second,a);
            Rows(a,first,"first slab grid");Rows(b,second,"adjacent slab grid");
            var union=a.Concat(b).Distinct().OrderBy(x=>x).ToArray();Rows(union,Math.Max(first,second),"compatible receiving stations");
            foreach(double joint in Enumerable.Range(1,(int)(length/2440)).Select(i=>2440.0*i).Where(x=>x<length))
                Check(a.Contains(joint) && b.Contains(joint),"both slab grids retain each plywood joint");
            var beam=PlywoodSpacing.FillRequiredRows(0,length,250,union);Rows(beam,250,"deeper beam's additional rows");
            Check(union.All(beam.Contains),"every slab foot retains a receiving H20");
        }
        var single=new BeamCalculatorViewModel {UseTowers=true,Depth=500,Width=800,BeamLengthMm=6100,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,MaximumConcreteEndOverhangMm=75,HasLeftSlab=true,LeftSlabThickness=300,LeftSlabBayMm=3900,TransferSlabLoads=true};
        Near(single.MaximumSecondarySpacingMm,407,"500 mm beam plywood limit");Near(single.LeftSlabSecondarySpacingMm,488,"300 mm slab plywood limit");
        single.Depth=900;single.BeamLengthMm=10600;single.LeftSlabBayMm=2575;
        single.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(single.IsCalculationValid && single.TowerResult?.Transfer!=null,"single-beam transfer passes with independent spacing: "+single.WarningMessage);
        var t=single.TowerResult.Transfer;Rows(t.RowsMm,t.EnteredUpperSpacingMm,"single beam grid");
        Check(t.LeftSlabSpacingMm>0 && t.LeftSlabSpacingMm<=488 && t.EnteredUpperSpacingMm<=350,"single transfer keeps independent limits through capacity search");Members(single.TowerResult);

        var vm=new BeamCalculatorViewModel {UseTowers=true,Depth=1750,Width=800,BeamLengthMm=6100,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,MaximumConcreteEndOverhangMm=75,HasLeftSlab=true,LeftSlabThickness=300,LeftSlabBayMm=3900,TransferSlabLoads=true,UseBeamNetwork=true};
        vm.NetworkBeams[1].DepthMm=1150;vm.NetworkBeams[1].WidthMm=650;
        Near(vm.NetworkBeams[0].MaximumSecondarySpacingMm,305,"deep beam has its own tighter plywood limit");
        Near(vm.NetworkBeams[1].MaximumSecondarySpacingMm,350,"shallower beam has its own wider plywood limit");
        Near(vm.NetworkBays[0].MaximumSecondarySpacingMm,488,"thin slab does not inherit the deep beam spacing");
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && vm.NetworkResult!=null && !vm.NetworkResult.RequiresSeparateSlabFormwork,"network fully transfers independent slab rows: "+vm.WarningMessage);
        var r=vm.NetworkResult;Check(r.Input.BaySpacing(0)>r.Input.BeamSpacingsMm.Min(),"actual slab spacing remains wider than the deeper beam where H20 capacities allow");Check(r.Input.HasIndividualSpacings,"calculated spacing arrays reach the actual solver");
        var slab=r.SlabRowsMm.Where((x,i)=>r.SlabRowActive(0,i)).ToArray();Rows(slab,r.Input.BaySpacing(0),"slab's actual row grid");
        Check(r.Beams.All(b=>b.Transfer.RowsMm.Length>slab.Length),"deeper beams gain local H20 rows without extra slab feet");
        Near(r.UpperFootCount,2*slab.Length,"feet count follows actual slab rows");
        Near(r.SlabLoadKn,r.Input.AreaLoad(300)*3.9*6.1,"full physical slab load transfers once");
        for(int i=0;i<r.Beams.Length;i++)
        {
            Rows(r.Beams[i].Transfer.RowsMm,r.Input.BeamSpacing(i),"receiving beam "+i);
            Check(slab.All(x=>r.Beams[i].Transfer.RowsMm.Contains(x)),"slab H20 feet align on both receiving beams");Members(r.Beams[i]);
        }
        Loads(r);
        var bad=r.Input.WithSpacings();bad.Bays=bad.Bays.Select(b=>new TransferBayRow {WidthMm=b.WidthMm,ThicknessMm=b.ThicknessMm}).ToArray();bad.SlabSpacingsMm[0]=488;bad.Bays[0].ThicknessMm=900;
        Reject(()=>bad.Validate(new TowerInput {LengthMm=6100,LegCapacityKn=70,StockLengthsMm=new[] {3300.0,3900},DeflectionDenominator=350}),"unsafe per-slab plywood spacing is rejected before H20 search");
        vm.SetTextInputErrors(true);Check(vm.NetworkBeams.All(b=>b.MaximumSecondarySpacingMm==0) && vm.NetworkBays.All(b=>b.MaximumSecondarySpacingMm==0),"invalid inputs clear all per-element spacing fields");vm.SetTextInputErrors(false);
        Check(vm.NetworkBays[0].MaximumSecondarySpacingMm>0,"per-element results recover");
        var overloaded=new BeamCalculatorViewModel {UseTowers=true,Depth=1750,Width=4000,BeamLengthMm=4900,TowerLegCapacityKn=1000,AllowCustomH20Lengths=true,HasLeftSlab=true,LeftSlabThickness=300,LeftSlabBayMm=2575,TransferSlabLoads=true};
        Check(overloaded.MaximumSecondarySpacingMm>0,"plywood alone permits a listed spacing in the overload fixture");
        Check(46.65*.25*Math.Pow((4.0-2.0)/2,2)/2>5,"even 250 mm spacing exceeds the single lower H20 cantilever moment at the widest tower");
        overloaded.OptimizeTowersAsync().GetAwaiter().GetResult();Check(!overloaded.IsCalculationValid && overloaded.TowerResult==null,"overstressed H20s cannot be accepted despite passing plywood limits");
        vm.AddNetworkBay();vm.NetworkBays[1].ThicknessMm=400;
        Near(vm.NetworkBays[0].MaximumSecondarySpacingMm,488,"first slab retains its own thickness-based limit");Near(vm.NetworkBays[1].MaximumSecondarySpacingMm,407,"thicker second slab gets a closer limit");
        vm.PlywoodDeflectionLimit="L/500";Near(vm.NetworkBays[1].MaximumSecondarySpacingMm,PlywoodSpacing.CalculateForSheet(vm.SelectedPlywood,12.15,500).MaximumSpacingMm,"each bay independently responds to the plywood criterion");
        vm.PlywoodDeflectionLimit="L/350";vm.NetworkBays[0].WidthMm=2575;vm.NetworkBays[1].WidthMm=2575;vm.BeamLengthMm=6900;vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && vm.NetworkResult!=null && !vm.NetworkResult.RequiresSeparateSlabFormwork,"mixed slab thicknesses transfer safely: "+vm.WarningMessage);
        var mixed=vm.NetworkResult;
        for(int i=0;i<mixed.Bays.Length;i++)
        {
            var actual=mixed.SlabRowsMm.Where((x,j)=>mixed.SlabRowActive(i,j)).ToArray();Rows(actual,mixed.Input.BaySpacing(i),"mixed slab "+i);
            Check(actual.All(x=>mixed.Beams[i].Transfer.RowsMm.Contains(x) && mixed.Beams[i+1].Transfer.RowsMm.Contains(x)),"mixed-bay feet have physical receivers at both ends");
            Near(mixed.Beams[i].Transfer.RightUpperScales.Count(x=>x>0),actual.Length,"unrelated slab rows add no phantom feet");
        }
        Near(mixed.SlabLoadKn,mixed.Input.Bays.Sum(b=>mixed.Input.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*6.9,"different slab loads are conserved individually in the network total");
        foreach(var b in mixed.Beams)Members(b);Loads(mixed);
        ShutterException();PrefixCacheParity();
        Console.WriteLine($"Passed {checks} individual plywood spacing and H20 capacity checks.");
    }
}
