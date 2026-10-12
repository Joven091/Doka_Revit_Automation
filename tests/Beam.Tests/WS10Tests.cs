using System;
using System.Linq;
using EngineeringDashboard;

internal static class WS10Tests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("WS10: "+message);}
    static void Near(double actual,double expected,double tolerance,string message)=>Check(Math.Abs(actual-expected)<=tolerance*Math.Max(1,Math.Abs(expected)),message+$" ({actual} vs {expected})");
    static double Sample(double length,Func<double,bool,double[]> response,double[] jumps)
    {
        double peak=0;
        foreach(double x in Enumerable.Range(0,20001).Select(i=>length*i/20000).Concat(jumps).Concat(new[] {300.0,length-300}).Where(x=>x>=0 && x<=length))
            foreach(bool right in new[] {false,true})
            {
                var r=response(x,right);double limit=x<=300 || x>=length-300 ? 10 : 12.3;
                peak=Math.Max(peak,Math.Abs(r[1])/limit+Math.Abs(r[0])/82);
            }
        return peak;
    }
    internal static void Run()
    {
        var profile=PrimaryBeamProfile.For(PrimaryBeamMaterial.WS10);
        Near(profile.EI,210000e6*412e-8/1000,1e-12,"EI from E=210 GPa and I=412 cm4");
        Check(PrimaryBeamProfile.WS10LengthsMm.SequenceEqual(new double[] {500,750,1000,1250,1500,1750,2000,2250,2500,2750,3000,3500,4000,4500,5000,5500,6000}),"exact manufacturer catalogue");
        var copy=PrimaryBeamProfile.WS10LengthsMm;copy[0]=123;Check(PrimaryBeamProfile.WS10LengthsMm[0]==500,"catalogue cannot be edited by callers");
        Near(profile.SelfWeightKnM(2000)*2,38.9*9.80665/1000,1e-12,"catalogue mass included once per assembly");
        bool rejected=false;try {profile.SelfWeightKnM(2475);}catch(ArgumentException){rejected=true;}Check(rejected,"non-catalogue steel length rejected");
        var simple=TowerBeamAnalysis.Analyze(2,new[] {0.0,2},10,profile.EI);
        Near(simple.DeflectionMm,5*10*16/(384*profile.EI)*1000,1e-12,"steel stiffness used in deflection");
        var random=new Random(2510);
        for(int trial=0;trial<20;trial++)
        {
            double q=2+random.NextDouble()*30;var supports=new[] {.15,.95,1.85};
            var beam=TowerBeamAnalysis.Analyze(2,supports,q,profile.EI,random.NextDouble()*4,random.NextDouble()*4);
            Near(profile.Strength(beam),Sample(2000,(x,r)=>beam.Response(x/1000,r),supports.Select(x=>x*1000).ToArray()),1e-6,"exact interaction matches independent dense envelope including support jumps and end zones");
            var points=new[] {new BeamPointLoad {PositionMm=125,Kn=3},new BeamPointLoad {PositionMm=1100,Kn=7}};
            var loaded=LoadedBeamAnalysis.Analyze(2000,new[] {150.0,950,1850},new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=q},new BeamDistributedLoad {StartMm=550,EndMm=1700,KnPerM=3}},points,profile.EI,350);
            Near(profile.Strength(loaded),Sample(2000,loaded.Response,new[] {125.0,150,550,950,1100,1700,1850}),1e-6,"piecewise UDL/point interaction matches independent envelope");
        }
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.WS10,LengthMm=2000,LegCapacityKn=70,DeflectionLimitMm=100,TotalLineLoad=20,StockLengthsMm=new[] {2475.0},AllowCustomH20Lengths=true};
        Check(!input.AllowCustomPrimaryLengths && TowerStockPlanner.CandidateLengths(input,25,6000).SequenceEqual(PrimaryBeamProfile.WS10LengthsMm),"custom H20 and user timber catalogue cannot alter steel stocks");
        var passing=TowerStockPlanner.Find(input,new[] {150.0,1850},10);
        Check(passing!=null && passing.WS10Count==2 && passing.TotalH20Count==0 && passing.TotalStockLengthMm==0,"one steel assembly per primary line and separate quantities");
        Near(passing.Reactions.Sum(),20+38.9*9.80665/1000,1e-10,"self-weight retained in leg equilibrium");
        input.TransverseLoadMoment=5;
        var unequal=TowerOptimizer.Evaluate(input,new[] {250.0,1750},new[] {true},1524,false);
        Check(unequal!=null,"unequal beam-only line loads produce a steel layout");
        Near(unequal.LeftReactions.Sum(),unequal.LeftLineLoad*2+38.9*9.80665/1000,1e-10,"left line retains its own full steel deadweight");
        Near(unequal.RightReactions.Sum(),unequal.RightLineLoad*2+38.9*9.80665/1000,1e-10,"right line retains its own full steel deadweight");
        input.TransverseLoadMoment=0;
        var combined=TowerBeamAnalysis.Analyze(2,new[] {.15,1.85},35,profile.EI);
        Check(combined.Moment<12.3 && combined.Shear<82 && profile.Strength(combined)>1,"M and V may pass separately while interaction fails");
        Check(TowerStockPlanner.Find(input,new[] {150.0,1850},35)==null,"planner rejects failed interaction");
        input.DeflectionLimitMm=.01;Check(TowerStockPlanner.Find(input,new[] {150.0,1850},10)==null,"steel deflection remains mandatory");
        input.DeflectionLimitMm=100;input.LegCapacityKn=1;Check(TowerStockPlanner.Find(input,new[] {150.0,1850},10)==null,"leg capacity remains mandatory");

        var common=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.WS10,LengthMm=2500,LegCapacityKn=70,DeflectionDenominator=350,AllowCustomH20Lengths=true,AllowSeparateSlabFallback=false,NetworkSearchWorkers=1};
        var network=new TransferNetworkInput {UpperSpacingMm=200,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=300,DepthMm=600}},Bays=new[] {new TransferBayRow {WidthMm=2900,ThicknessMm=100}}};
        var result=TransferNetworkOptimizer.Optimize(common,network);
        Check(result.WS10Count>0 && result.H20Count>0 && result.MaterialMm==result.TimberMm+result.WS10LengthMm,"mixed timber/steel quantities");
        foreach(var b in result.Beams)
        {
            Check(b.Primary.IsWS10 && b.StockPlan.Primary.IsWS10 && b.StockPlan.Pieces.All(p=>PrimaryBeamProfile.WS10LengthsMm.Contains(p.StockLengthMm) && !p.IsCustomLength),"all network primary lengths remain uncut steel");
            foreach(var m in b.Transfer.PrimaryCases.SelectMany(c=>c))
            {
                Check(m.IsWS10 && m.Utilization<=1+1e-8,"every steel stage passes strength and deflection");
                Near(m.Analysis.TotalLoadKn,m.Uniform.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000),1e-9,"member equilibrium includes self-weight");
                Near(m.Uniform.Last().KnPerM,profile.SelfWeightKnM(m.LengthMm),1e-12,"self-weight preserved once in every stage");
            }
            Check(b.Transfer.LowerCases.SelectMany(c=>c).Concat(b.Transfer.UpperMembers).All(m=>!m.IsWS10 && m.H20Quantity==1 && m.EI==TowerOptimizer.PairEI/2),"secondary and slab H20s keep single-H20 stiffness and limits");
        }
        network.UpperSpacingMm=100;common.IgnoreUplift=true;common.MaximumConcreteEndOverhangMm=75;
        network.CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1250,WidthMm=400,DepthMm=600,BottomElevationMm=-600,SupportMode=CrossBeamSupportMode.DedicatedTowers}};
        var crossed=TransferNetworkOptimizer.Optimize(common,network);var cross=crossed.CrossSupports.Single();
        Check(cross.Towers!=null && cross.Towers.Primary.IsWS10 && cross.Members.Where(m=>m.IsPrimary).All(m=>m.IsWS10 && PrimaryBeamProfile.WS10LengthsMm.Contains(m.LengthMm) && m.Utilization<=1+1e-8),"dedicated crossing primaries use passing catalogue WS10s");
        Check(cross.Solids.Where(s=>s.Member?.IsWS10==true).All(s=>Math.Abs(s.Z1-s.Z0-100)<1e-8),"steel crossing clash geometry uses its actual depth");
        Check(CrossBeamSupportOptimizer.PrimaryShutterClash(crossed,0,cross.Solids)==null,"steel primaries keep the 200 mm side-shutter clearance");
        foreach(var m in cross.Members.Where(m=>m.IsWS10))Near(m.Uniform.Last().KnPerM,profile.SelfWeightKnM(m.LengthMm),1e-12,"crossing self-weight retained once after projected/shifted stock search");
        network.CrossingBeams=new TransferCrossingBeamRow[0];network.UpperSpacingMm=200;
        network.Beams=Enumerable.Range(0,15).Select(i=>new TransferBeamRow {WidthMm=300,DepthMm=600}).ToArray();network.Bays=Enumerable.Range(0,14).Select(i=>new TransferBayRow {WidthMm=2900,ThicknessMm=100}).ToArray();
        network.Validate(common);Check(network.Beams.Length==15,"fifteen-beam model accepted");
        var fifteen=TransferNetworkOptimizer.Optimize(common,network);Check(fifteen.Beams.Length==15 && fifteen.Beams.All(b=>b.Primary.IsWS10 && b.Utilization<=1+1e-8),"fifteen beams solve with passing WS10 checks");
        network.Beams=network.Beams.Concat(new[] {new TransferBeamRow {WidthMm=300,DepthMm=600}}).ToArray();network.Bays=network.Bays.Concat(new[] {new TransferBayRow {WidthMm=2900,ThicknessMm=100}}).ToArray();
        rejected=false;try {network.Validate(common);}catch(ArgumentException){rejected=true;}Check(rejected,"sixteenth beam rejected consistently");
        NetworkEditTests.Run();
        Console.WriteLine($"Passed {checks} WS10 material and stock checks.");
    }
}
