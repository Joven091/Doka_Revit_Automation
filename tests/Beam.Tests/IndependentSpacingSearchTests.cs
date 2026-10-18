using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;

using EngineeringDashboard;

internal static class IndependentSpacingSearchTests
{
    static int checks;
    static void Check(bool ok,string message) {checks++;if(!ok)throw new Exception("Independent spacing search: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message);
    static TransferNetworkInput Reported()=>new TransferNetworkInput {
        PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,UpperSpacingMm=275,PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm,
        BeamSpacingsMm=Enumerable.Repeat(275.0,8).ToArray(),SlabSpacingsMm=Enumerable.Repeat(275.0,7).ToArray(),CrossingSpacingsMm=Enumerable.Repeat(275.0,7).ToArray(),
        Beams=Enumerable.Range(0,8).Select(i=>new TransferBeamRow {WidthMm=i%2==0 ? 800 : 650,DepthMm=i%2==0 ? 1750 : 1150}).ToArray(),
        Bays=new[] {2677.0,1875,3275,2275,2278,2272,2252}.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray(),
        CrossingBeams=Enumerable.Range(1,7).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=3450,WidthMm=650,DepthMm=1150,SupportMode=CrossBeamSupportMode.LegacySlabRows}).ToArray()};
    static TransferNetworkInput Fresh()=>new TransferNetworkInput {
        PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,UpperSpacingMm=305,PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm,
        BeamSpacingsMm=new[] {350.0,305,350},SlabSpacingsMm=new[] {488.0,488},CrossingSpacingsMm=new[] {350.0,350},
        Beams=new[] {650.0,800,650}.Select((w,i)=>new TransferBeamRow {WidthMm=w,DepthMm=i==1 ? 1750 : 1150,BottomElevationMm=i==1 ? -1750 : -1150}).ToArray(),
        Bays=new[] {2272.0,2252}.Select(w=>new TransferBayRow {WidthMm=w,ThicknessMm=300}).ToArray(),
        CrossingBeams=Enumerable.Range(1,2).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150,SupportMode=CrossBeamSupportMode.Automatic}).ToArray()};
    static TowerInput Common(bool fresh)=>new TowerInput {LengthMm=6900,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=fresh ? 150 : 75,
        Exclude600Standalone=!fresh,DeflectionDenominator=270,AllowCustomH20Lengths=true,CustomH20Scope=fresh ? CustomH20LengthScope.SlabSupports : CustomH20LengthScope.Both};
    static void Engineering(TransferNetworkResult result)
    {
        foreach(var layout in result.Beams.Concat(result.CrossSupports.Where(c=>c.Towers!=null).Select(c=>c.Towers)))
        {
            Check(layout.IgnoreUplift && layout.MaxLegKn<=layout.LegCapacityKn+1e-8 && layout.Utilization<=1+1e-8,"individual leg capacity and uplift policy retained");
            foreach(var m in layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers))Member(m);
        }
        foreach(var member in result.CrossSupports.SelectMany(c=>c.Members))Member(member);
        Check(!result.RequiresSeparateSlabFormwork,"all bays calculated");
    }
    static void Member(TransferMember member)
    {
        var a=member.Analysis;
        Check(a.DeflectionEvaluated && member.Utilization<=1+1e-8,"accepted members retain strength and full deflection checks");
        Near(a.Reactions.Sum(),a.TotalLoadKn,"signed force equilibrium");
        double applied=(member.Points??new BeamPointLoad[0]).Sum(p=>p.Kn*p.PositionMm/1000)+(member.Uniform??new BeamDistributedLoad[0]).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.EndMm+q.StartMm)/2000);
        Near(a.Reactions.Zip(member.SupportsMm,(r,x)=>r*x/1000).Sum(),applied,"signed moment equilibrium");
    }
    internal static void Run(bool baseline=false)
    {
        foreach(bool fresh in new[] {false,true})
        {
            var input=fresh ? Fresh() : Reported();var common=Common(fresh);common.Trace=new BeamSearchTrace("independent spacing regression");
            var timer=Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,input);timer.Stop();
            Console.WriteLine($"{(fresh ? "Fresh three-beam" : "Reported eight-beam")}: {timer.Elapsed.TotalSeconds:0.000}s; beams={string.Join(",",result.Input.BeamSpacingsMm)}; slabs={string.Join(",",result.Input.SlabSpacingsMm)}; crossing limits={string.Join(",",result.Input.CrossingSpacingsMm)}; dedicated crossing pitches={string.Join(",",result.CrossSupports.Where(c=>c.Towers!=null).Select(c=>c.Towers.Transfer.EnteredUpperSpacingMm))}; H20s={result.H20Count}; timber={result.TimberMm/1000:0.00}m; legs={result.LegCount}");
            Engineering(result);
            Check(input.BeamSpacingsMm.SequenceEqual(fresh ? new[] {350.0,305,350} : Enumerable.Repeat(275.0,8)),"source maxima remain unchanged");
            if(!baseline)
            {
                // The wider plywood-based end allowance changes eligible slab stocks
                // and their actual receiving reactions; keep the checked spacing regression.
                Check(result.Input.BeamSpacingsMm.SequenceEqual(fresh ? new[] {350.0,275,350} : new[] {275.0,275,250,275,275,275,275,275}),"passing beam maxima retained");
                Check(result.Input.SlabSpacingsMm.SequenceEqual(fresh ? new[] {488.0,488} : new[] {250.0,275,250,275,275,275,275}),"only necessary slab reductions retained");
                Check(result.Input.CrossingSpacingsMm.SequenceEqual(fresh ? new[] {350.0,350} : Enumerable.Repeat(275.0,7)),"passing crossing maxima retained");
                int elements=input.Beams.Length+input.Bays.Length+input.CrossingBeams.Length;
                Check(common.Trace.Count(BeamSearchCounter.NetworkSpacingTrials)<=10+7*elements,"search vector count is linear in elements, never a Cartesian product");
                Console.WriteLine($"Spacing vectors searched: {common.Trace.Count(BeamSearchCounter.NetworkSpacingTrials)} (linear cap {10+7*elements})");
                foreach(var c in result.CrossSupports.Where(c=>c.Towers!=null))
                {
                    Near(c.NominalSecondarySpacingMm,c.Towers.Transfer.EnteredUpperSpacingMm,"crossing output uses actual dedicated pitch");
                    Check(c.Schedule.Contains($"Nominal crossing secondary spacing {c.NominalSecondarySpacingMm:0.###} mm"),"crossing schedule distinguishes actual pitch from its limit");
                    Check(result.Summary.Contains($"X{c.CrossingIndex+1} secondary spacing limit {result.Input.CrossSpacing(c.CrossingIndex):0} mm"),"summary retains crossing limit");
                }
                foreach(int i in Enumerable.Range(0,result.Input.Bays.Length))
                {
                    Check(result.Schedule.Contains($"maximum row spacing {result.Input.BaySpacing(i):0.###} mm"),"schedule uses selected slab spacing");
                    foreach(var t in new[] {result.Beams[i].Transfer,result.Beams[i+1].Transfer})
                    {
                        bool left=ReferenceEquals(t,result.Beams[i+1].Transfer);
                        Near(t.SlabSpacing(left),result.Input.BaySpacing(i),"diagram model retains selected slab spacing");
                        foreach(int row in Enumerable.Range(0,t.RowsMm.Length).Where(r=>t.UpperScale(left,r)>0))
                        {
                            var m=t.UpperMemberAtRow(left,row);Member(m);
                            Check(t.UpperSpacingDescription(left,row).Contains($"Nominal slab design spacing {result.Input.BaySpacing(i):0.###} mm"),"diagram label uses selected spacing");
                        }
                    }
                }
            }
        }
        Console.WriteLine($"Passed {checks} independent spacing search checks.");
    }
    internal static void Controls()
    {
        // The first passing category seed is available at vector 3. Expire only
        // when recovery starts another vector, rather than during initial search.
        foreach(bool cancel in new[] {false,true})using(var source=new CancellationTokenSource())
        {
            var common=Common(false);common.NetworkSearchWorkers=1;
            BeamSearchTrace trace=null;
            trace=new BeamSearchTrace("spacing recovery control",source.Token,TimeSpan.FromSeconds(1),()=>
            {
                if(trace!=null && trace.Count(BeamSearchCounter.NetworkSpacingTrials)>=4)
                {
                    if(cancel) {source.Cancel();return 0;}
                    return 2*Stopwatch.Frequency;
                }
                return 0;
            });
            common.Trace=trace;
            try
            {
                var result=TransferNetworkOptimizer.Optimize(common,Reported());
                Check(!cancel && trace.BudgetReached && result.SearchTimeLimitReached,"deadline recovery retains complete checked seed");
                Engineering(result);
                Check(result.Beams.All(b=>b.SearchTimeLimitReached && !b.MinimumLegCountProven),"deadline cannot claim proven optimum");
                Check(common.AllowCustomH20Lengths,"custom-length flag restored after deadline");
            }
            catch(OperationCanceledException) {Check(cancel && !trace.BudgetReached,"user cancellation takes precedence and is never swallowed");}
        }
        var maximum=Fresh();maximum.Beams=maximum.Beams.Take(2).ToArray();maximum.Bays=maximum.Bays.Take(1).ToArray();
        maximum.BeamSpacingsMm=new[] {250.0,250};maximum.SlabSpacingsMm=new[] {250.0};maximum.CrossingBeams=new TransferCrossingBeamRow[0];maximum.CrossingSpacingsMm=new double[0];
        var easy=Common(false);easy.LengthMm=2450;easy.MaximumConcreteEndOverhangMm=0;easy.Trace=new BeamSearchTrace("passing maxima");
        maximum.Beams=maximum.Beams.Select(b=>new TransferBeamRow {WidthMm=300,DepthMm=400}).ToArray();maximum.Bays[0].WidthMm=1800;maximum.Bays[0].ThicknessMm=100;
        var unchanged=TransferNetworkOptimizer.Optimize(easy,maximum);
        Check(easy.Trace.Count(BeamSearchCounter.NetworkSpacingTrials)==1 && ReferenceEquals(unchanged.Input,maximum),"passing maxima cost one vector with no recovery overhead");
        Console.WriteLine($"Passed {checks} independent spacing control checks.");
    }
}
