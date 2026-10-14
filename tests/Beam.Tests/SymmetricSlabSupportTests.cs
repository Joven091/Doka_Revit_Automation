using System;
using System.Linq;
using EngineeringDashboard;

internal static class SymmetricSlabSupportTests
{
    static int checks;
    static void Check(bool condition,string message) {checks++;if(!condition)throw new Exception("Symmetric slab supports: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message);
    static TowerInput Common()=>new TowerInput {LengthMm=2450,LegCapacityKn=70,DeflectionDenominator=350,AllowSeparateSlabFallback=false,NetworkSearchWorkers=1};
    static UpperTransferPlan Plan(double far=550)=>new UpperTransferPlan {StockLengthMm=3300,EdgeMm=300,NearFootMm=550,FarFootMm=far};
    static void Reject(Action action,string message)
    {
        bool rejected=false;
        try {action();}catch(ArgumentException e) {rejected=true;Check(e.Message.Contains("equal setbacks"),"asymmetric-plan rejection explains the required geometry");}
        Check(rejected,message);
    }
    static void CheckLayout(TowerLayout layout)
    {
        var t=layout.Transfer;
        foreach(var upper in t.UpperMembers)
        {
            Near(upper.SupportsMm[0],upper.LengthMm-upper.SupportsMm[1],"equal stock-end overhangs");
            Near(upper.Analysis.Reactions[0],upper.Analysis.TotalLoadKn/2,"uniform slab load delivers half its load to each foot");
            Near(upper.Analysis.Reactions[1],upper.Analysis.TotalLoadKn/2,"opposite slab foot has the same reaction");
        }
        foreach(var stage in t.LowerCases)foreach(var lower in stage)
        {
            // Independent transverse equilibrium retains eccentric foot/shutter reactions.
            double force=lower.Points.Sum(p=>p.Kn)+lower.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000);
            double moment=lower.Points.Sum(p=>p.Kn*p.PositionMm)+lower.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000*(q.StartMm+q.EndMm)/2);
            double right=(moment-force*lower.SupportsMm[0])/(lower.SupportsMm[1]-lower.SupportsMm[0]);
            Near(lower.Analysis.Reactions[1],right,"actual receiving reaction follows moment equilibrium");
            Near(lower.Analysis.Reactions[0],force-right,"opposite receiving reaction is not averaged");
        }
        foreach(var member in t.PrimaryCases.Concat(t.LowerCases).SelectMany(c=>c))
            Check(member.Utilization<=1+1e-8 && member.Analysis.Reactions.All(r=>r>=-1e-7),"all member and uplift stage checks retained");
        Check(t.PrimaryCases.SelectMany(c=>c).All(m=>m.Points.Length==0 && m.Uniform.Length>0 && m.Analysis.Reactions.All(r=>r<=layout.LegCapacityKn+1e-8)),"primary UDL and individual leg capacities retained");
        int runs=layout.StockPlan.Pieces.Length;
        Check(t.PrimaryCases.Skip(1).Any(c=>Math.Abs(c.Take(runs).Sum(m=>m.Analysis.TotalLoadKn)-c.Skip(runs).Sum(m=>m.Analysis.TotalLoadKn))>1e-6),"one-sided slab stages keep unequal primary-line and leg loading");
        Near(layout.MaxLegKn,t.PrimaryCases.SelectMany(c=>c).SelectMany(m=>m.Analysis.Reactions).Max(),"reported peak is the actual heaviest leg across stages");
        Check(t.Schedule.Contains("equal foot setback") && t.Schedule.Contains($"Maximum row spacing {t.SlabSpacing(false):0.###}"),"schedule gives repeated setbacks and site spacing");
    }
    public static void Run()
    {
        var common=Common();
        Plan().Validate(3900,common.StockLengthsMm);
        Plan().Reverse().Validate(3900,common.StockLengthsMm);
        Reject(()=>Plan(600).Validate(3900,common.StockLengthsMm),"otherwise valid asymmetric shared plan rejected");
        var transfer=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=5.25,UpperSpacingMm=250,RightBayMm=3900,RightAreaLoadKnM2=4.4,RightPlan=Plan(600)};
        Reject(()=>transfer.Validate(common),"fixed-plan caller cannot bypass the symmetric arrangement");
        transfer.RightPlan=Plan();common.TransferredSlab=transfer;
        CheckLayout(TransferredSlabOptimizer.Optimize(common));
        common=Common();
        var network=new TransferNetworkInput {UpperSpacingMm=250,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=400,DepthMm=800},new TransferBeamRow {WidthMm=350,DepthMm=700}},Bays=new[] {new TransferBayRow {WidthMm=3900,ThicknessMm=100},new TransferBayRow {WidthMm=3900,ThicknessMm=150}}};
        var result=TransferNetworkOptimizer.Optimize(common,network);
        for(int bay=0;bay<result.Bays.Length;bay++)
        {
            var plan=result.Bays[bay];
            Near(plan.NearFootMm,plan.FarFootMm,"different main beam widths cannot shift one slab foot independently");
            plan.Validate(network.Bays[bay].WidthMm,common.StockLengthsMm);
            var left=result.Beams[bay].Transfer;var right=result.Beams[bay+1].Transfer;
            Near(left.RightFootMm,plan.NearFootMm,"same setback used at every row on the first main beam");
            Near(right.LeftFootMm,plan.FarFootMm,"same setback used at every row on the opposite main beam");
        }
        foreach(var beam in result.Beams)CheckLayout(beam);
        Check(result.Beams[1].Transfer.CaseNames.Length==4,"interior receiving beam retains beam-only, each one-sided and combined slab stages");
        Near(result.SlabLoadKn,network.Bays.Sum(b=>network.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*common.LengthMm/1000,"full slab and shutter load conserved");
        Near(result.UpperFootCount,result.Bays.Length*result.SlabRowsMm.Length*2,"symmetry does not add or remove physical slab feet");
        // Symmetric bearings alone cannot justify averaged reactions under uneven loading.
        var uneven=LoadedBeamAnalysis.Analyze(3300,new[] {250.0,3050},new[] {new BeamDistributedLoad {StartMm=0,EndMm=1650,KnPerM=2}},null,TowerOptimizer.PairEI/2,350);
        Near(uneven.Reactions[1],3.3*(825-250)/2800,"uneven loading reaction agrees with independent statics");
        Check(uneven.Reactions.Average()<2 && uneven.Reactions.Max()>2,"averaging would conceal an overloaded support");
        var diagnostic=new TowerSearchDiagnostics(350);diagnostic.Legs(uneven.Reactions,2,"Symmetric bearings", "Uneven construction stage");
        Check(diagnostic.Exception("Overloaded support").Details.Contains("leg load"),"actual overloaded reaction is diagnosed despite symmetry");
        Console.WriteLine($"Symmetric slab supports: {checks} checks passed.");
    }
}
