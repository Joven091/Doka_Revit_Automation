using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class CoordinatedDesignerTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Coordinated designer: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),message+$" ({a:G12} vs {b:G12})");
    static TowerInput Common(double length=6900)=>new TowerInput {LengthMm=length,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=200,DeflectionDenominator=270,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,IgnoreUplift=true,NetworkSearchWorkers=1};
    static TransferNetworkInput Network(double first=2677,double second=1875,double length=6900)=>new TransferNetworkInput {UpperSpacingMm=275,
        Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=650,DepthMm=1150},new TransferBeamRow {WidthMm=800,DepthMm=1750}},
        Bays=new[] {new TransferBayRow {WidthMm=first,ThicknessMm=300},new TransferBayRow {WidthMm=second,ThicknessMm=300}},
        CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=length/2,WidthMm=650,DepthMm=1150},new TransferCrossingBeamRow {BayNumber=2,PositionMm=length/2,WidthMm=650,DepthMm=1150}}};
    static void Member(TransferMember m)
    {
        Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8,"every complete-stage member passes strength and deflection");
        var a=m.Analysis;Near(a.Reactions.Sum(),a.TotalLoadKn,"member force equilibrium");
        double moment=(m.Points??new BeamPointLoad[0]).Sum(p=>p.Kn*p.PositionMm/1000)+(m.Uniform??new BeamDistributedLoad[0]).Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.EndMm+q.StartMm)/2000);
        Near(a.Reactions.Zip(m.SupportsMm,(r,x)=>r*x/1000).Sum(),moment,"member moment equilibrium");
    }
    static TransferNetworkResult Solve(string name,TowerInput common,TransferNetworkInput input)
    {
        var timer=Stopwatch.StartNew();
        using(var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(4)))
        {
            common.Trace=new BeamSearchTrace(name,cancellation.Token);
            var result=TransferNetworkOptimizer.Optimize(common,input);
            Check(result.SeparateSlabCount==0 && result.CrossSupports.Length==input.CrossingBeams.Length,"complete reference network includes every crossing and slab");
            for(int beam=0;beam<result.Beams.Length;beam++)
            {
                var l=result.Beams[beam];var t=l.Transfer;
                Check(t.SlabRowIndices.Select(i=>t.RowsMm[i]).SequenceEqual(result.SlabRowsMm),"shared slab rows remain aligned despite local beam rows");
                double concrete=input.AreaLoad(input.Beams[beam].DepthMm)*input.Beams[beam].WidthMm*common.LengthMm/1e6;
                Near(t.BeamLoadKn,concrete,"full beam concrete including end strips retained");
                Near(t.PrimaryCases[Array.IndexOf(t.CaseNames,"Beam")].Sum(m=>m.Analysis.TotalLoadKn),concrete,"full beam-concrete force reaches primary UDL");
                string combined=beam==0 ? "Beam + right slab" : beam==result.Beams.Length-1 ? "Beam + left slab" : "Beam + both slabs";
                Near(t.PrimaryCases[Array.IndexOf(t.CaseNames,combined)].Sum(m=>m.Analysis.TotalLoadKn),concrete+t.SlabLoadKn,"primary slab transfer conserves physical slab force without extending past shared coverage");
                foreach(int row in Enumerable.Range(0,t.RowsMm.Length).Except(t.SlabRowIndices))
                {
                    Near(t.UpperScale(true,row),0,"beam-only row has no left slab foot");Near(t.UpperScale(false,row),0,"beam-only row has no right slab foot");
                    Check(t.LowerCases.TakeWhile(c=>!c[0].LoadCase.Contains(" + X")).All(c=>c[row].Points.All(p=>Math.Abs(p.Kn)<1e-8)),"beam-only secondary has no invented slab or shutter force");
                }
                Check(l.LeftReactions.Concat(l.RightReactions).All(r=>r<=common.LegCapacityKn+1e-8),"every individual leg passes capacity");
                Near(t.LowerStockStartMm+t.LowerStockLengthMm/2,0,"secondaries remain centred on concrete");
            }
            foreach(var l in result.Beams.Concat(result.CrossSupports.Select(c=>c.Towers).Where(l=>l!=null)))
                foreach(var m in l.Transfer.PrimaryCases.Concat(l.Transfer.LowerCases).SelectMany(c=>c).Concat(l.Transfer.UpperMembers))Member(m);
            Near(result.UpperFootCount,result.Bays.Select((p,b)=>result.SlabRowCount(b)*2).Sum(),"local beam rows add no upper feet");
            Console.WriteLine($"DESIGNER {name}: {result.LegCount} legs; {result.UpperFootCount} upper feet; {result.TimberMm/1000:0.00} m timber; peak {result.PeakLegKn:0.00} kN; {timer.Elapsed.TotalSeconds:0.00}s");
            Console.WriteLine(result.LayoutDiagnostics.Split(new[] {'\n'}).Where(s=>s.StartsWith("B1:") || s.StartsWith("B2:") || s.StartsWith("B3:") || s.StartsWith("Shared")).Aggregate("",(a,b)=>a+b+"\n"));
            return result;
        }
    }
    public static void Options()
    {
        var common=Common();common.MaximumConcreteEndOverhangMm=150;
        var current=Solve("25 mm and alternatives",common,Network());string original=current.Schedule;
        foreach(var beam in current.Beams)for(int i=0;i<beam.Joined.Length;i++)if(!beam.Joined[i])
            Near((beam.PositionsMm[i+1]-beam.PositionsMm[i])/25,Math.Round((beam.PositionsMm[i+1]-beam.PositionsMm[i])/25),"independent tower gaps use 25 mm increments");
        Check(Math.Abs(current.CrossSupports[0].TowerSpanOffsetMm)<225,"X1 checks transverse placements before accepting the former 225 mm span shift");
        var options=TransferNetworkOptimizer.BeamOptions(common,current,1);
        Check(options.Count(o=>o.IsAvailable)>1 && ReferenceEquals(options[0].Result,current),"current layout and complete alternatives retained");
        Check(current.Schedule==original,"searching alternatives does not mutate selected stages or stock demands");
        var repeated=options.FirstOrDefault(o=>o.Label.Contains("as B3"));
        Check(repeated!=null,"B3 pattern is explicitly evaluated on B2");
        if(repeated.IsAvailable){Near(repeated.Result.Beams[1].SymmetryErrorMm,0,"B2 alternate pattern is symmetric");Check(repeated.Result.LegCount<=current.LegCount,"repeated pattern does not add legs");}
        else Check(repeated.RejectionReason.Contains("X2"),"unavailable B3 pattern explains its remaining X2 constraint");
        foreach(var option in options.Where(o=>o.IsAvailable))
        {
            Check(option.Result.CrossSupports.Length==2,"both crossings checked for each option");
            foreach(var beam in option.Result.Beams)
            {
                Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r<=70+1e-7),"alternate network individual leg capacities pass");
                foreach(var m in beam.Transfer.PrimaryCases.Concat(beam.Transfer.LowerCases).SelectMany(c=>c))Member(m);
                for(int i=0;i<beam.Joined.Length;i++)if(!beam.Joined[i])Near((beam.PositionsMm[i+1]-beam.PositionsMm[i])/25,Math.Round((beam.PositionsMm[i+1]-beam.PositionsMm[i])/25),"alternate gaps use 25 mm increments");
            }
            Console.WriteLine(option.Label+" · "+option.Result.TimberMm/1000+" m timber");
        }
        Console.WriteLine($"Beam alternatives: {checks} checks passed.");
    }
    public static void Run()
    {
        var covers=TransferredSlabOptimizer.StockCovers(Common());Check(covers.Any(c=>c.SequenceEqual(new[] {1800.0,3300,1800})),"mirrored centre-stock family survives short-cover DP");
        Check(2*TowerOptimizer.TowerLegTimberEquivalentMm>5000,"two saved tower legs justify five extra metres of timber");
        Check(TowerOptimizer.UpperFootTimberEquivalentMm<TowerOptimizer.TowerLegTimberEquivalentMm,"upper feet and tower legs carry distinct economic weights");
        var reference=Solve("reported geometry",Common(),Network());
        Check(reference.LegCount<=46,"reported network finds the reduced-leg complete arrangement");
        Check(reference.Beams[0].StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(new[] {1800.0,3300,1800}) && reference.Beams[0].LegCount==14,"B1 discovers the designer centre-linked layout");
        Check(reference.Beams[0].GroupCount==3 && reference.Beams[0].Joined.Count(j=>j)==4,"linked centre saves a support frame without adding braced bays");
        Check(reference.Beams.Any(b=>b.Transfer.BeamOnlyRowCount>0),"local beam-secondary refinement is exercised");
        Near(reference.Beams[0].SymmetryErrorMm,0,"B1 stock and supports mirror the concrete centre");
        Check(reference.CrossSupports[0].Towers.Standalone600Count==0,"X1 tries larger bays at later passing shifts before a narrow early-position fallback");
        Solve("drawing bay dimensions",Common(),Network(2752,1950));
        var shifted=Network();shifted.CrossingBeams[0].PositionMm=3100;shifted.CrossingBeams[1].PositionMm=3800;
        Solve("offset crossings",Common(),shifted);
        var asymmetric=Network(3150,2250,7200);asymmetric.Beams[0].SetDepthKeepingTopMm(1450);asymmetric.Beams[2].WidthMm=700;asymmetric.CrossingBeams=new TransferCrossingBeamRow[0];
        Solve("longer unequal beam loads",Common(7200),asymmetric);
        Console.WriteLine($"Coordinated designer: {checks} checks passed.");
    }
}
