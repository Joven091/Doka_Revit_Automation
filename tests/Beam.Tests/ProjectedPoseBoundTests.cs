using System;
using System.Linq;
using EngineeringDashboard;

internal static class ProjectedPoseBoundTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Projected pose bound: "+message);}
    static TowerInput Input()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,DeflectionDenominator=270,IgnoreUplift=true};
    static bool ActualFailure(TowerInput input,double span,double[] positions,double total,double left,double edge)
    {
        double start=Math.Min(0,positions[0]-150),end=Math.Max(span,positions[1]+150),length=end-start;
        return new[] {left,total-left}.Any(reaction=>
        {
            var uniform=new[] {new BeamDistributedLoad {StartMm=edge-start,EndMm=span-edge-start,KnPerM=reaction*span/(span-2*edge)}};
            var a=LoadedBeamAnalysis.AnalyzeSigned(length,positions.Select(x=>x-start).ToArray(),uniform,Array.Empty<BeamPointLoad>(),input.Primary.EI,input.DeflectionDenominator);
            return Math.Max(a.MomentKnM/input.Primary.MomentLimitKnM,a.ShearKn/input.Primary.ShearLimitKn)>1+1e-8;
        });
    }
    internal static void Run()
    {
        var input=Input();var positions=new[] {1750.0,3250};double tail=(1750-150)/1000.0;
        double boundary=4*input.Primary.MomentLimitKnM/(tail*tail);
        foreach(int sign in new[] {-1,1})
        {
            Check(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,5000,positions,sign*boundary*(1+0.5e-6)),"numerical boundary retains the complete stock/start search");
            Check(!CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,5000,positions,sign*boundary*(1+2e-6)),"only a failure beyond the conservative margin prunes");
        }
        Check(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,5000,positions,0),"opposite signed transverse reactions need their actual checks");
        Check(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,5000,new[] {4900.0,6400},1000),"supports beyond the loaded interval do not use the cantilever formula");
        Check(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,5000,new[] {-1400.0,100},1000),"supports before the loaded interval do not use the cantilever formula");
        Check(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,250,new[] {25.0,225},1000),"an allowance beyond half the central span retains full checks");
        input.PrimaryMaterial=PrimaryBeamMaterial.WS10;
        Check(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,5000,positions,1000),"WS10 self-weight and signed-load combinations retain their existing checks");input.PrimaryMaterial=PrimaryBeamMaterial.DoubleH20;
        var random=new Random(591284);int rejected=0;
        for(int trial=0;trial<150;trial++)
        {
            double span=3000+random.NextDouble()*3500,bay=1000+random.NextDouble()*1000,shift=(random.NextDouble()-.5)*900;
            var supports=new[] {span/2+shift-bay/2,span/2+shift+bay/2};double total=(random.NextDouble()-.5)*80;
            if(CrossBeamSupportOptimizer.ProjectedPoseForcesFit(input,span,supports,total))continue;
            rejected++;
            foreach(double edge in new[] {0.0,75,150})foreach(double split in new[] {-.5,.5,1.5})
                Check(ActualFailure(input,span,supports,total,total*split,edge),"every edge and signed reaction split has an actual failing primary after pose pruning");
        }
        Check(rejected>20,"random checks exercise many provably failed poses");
        Console.WriteLine("PASS "+checks+" projected whole-pose lower-bound checks: signed reactions, borders, central spans, exterior supports and WS10 guards");
    }
}
