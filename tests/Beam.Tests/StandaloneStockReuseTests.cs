using System;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class StandaloneStockReuseTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Standalone stock reuse: "+message);}
    static void Same(double a,double b,string message)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),message);
    static TowerInput Input(bool custom=false,PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20)=>new TowerInput
    {
        LengthMm=3900,TotalLineLoad=12,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1800,2450,3900},
        AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,PrimaryMaterial=material,
        DeflectionDenominator=270,IgnoreUplift=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,
        Trace=new BeamSearchTrace("Standalone exact stock response reuse")
    };
    static void Response(TowerBeamAnalysis expected,TowerBeamAnalysis actual)
    {
        Same(expected.Moment,actual.Moment,"moment bits");Same(expected.Shear,actual.Shear,"shear bits");Same(expected.DeflectionMm,actual.DeflectionMm,"displacement bits");
        Check(expected.Reactions.Length==actual.Reactions.Length,"reaction count");for(int i=0;i<expected.Reactions.Length;i++)Same(expected.Reactions[i],actual.Reactions[i],"reaction bits");
        Check(expected.DeflectionRegions.Count==actual.DeflectionRegions.Count,"deflection region count");
        for(int i=0;i<expected.DeflectionRegions.Count;i++)
        {
            var a=expected.DeflectionRegions[i];var b=actual.DeflectionRegions[i];
            Same(a.StartMm,b.StartMm,"region start bits");Same(a.EndMm,b.EndMm,"region end bits");Same(a.PeakMm,b.PeakMm,"region peak bits");Check(a.IsOverhang==b.IsOverhang,"region type");
        }
        foreach(int d in new[] {270,350,500})Same(expected.DeflectionUtilization(0,d),actual.DeflectionUtilization(0,d),"criterion-independent response");
        for(double x=0;x<=3.9;x+=.13)foreach(bool side in new[] {false,true})
        {var a=expected.Response(x,side);var b=actual.Response(x,side);for(int i=0;i<a.Length;i++)Same(a[i],b[i],"diagram bits");}
    }
    static void Plan(TowerStockPlan expected,TowerStockPlan actual)
    {
        Check((expected==null)==(actual==null),"stock feasibility");if(expected==null)return;
        Check(expected.Pieces.Length==actual.Pieces.Length,"selected stock count");Same(expected.Utilization,actual.Utilization,"selected utilization bits");
        for(int i=0;i<expected.Reactions.Length;i++)Same(expected.Reactions[i],actual.Reactions[i],"stock reaction bits");
        for(int i=0;i<expected.Pieces.Length;i++)
        {
            var a=expected.Pieces[i];var b=actual.Pieces[i];
            Same(a.StartMm,b.StartMm,"selected start");Same(a.StockLengthMm,b.StockLengthMm,"selected length");
            Check(a.FirstSupport==b.FirstSupport&&a.LastSupport==b.LastSupport&&a.IsCustomLength==b.IsCustomLength,"selected support range/custom provenance");
            Same(a.MomentKnM,b.MomentKnM,"selected moment");Same(a.ShearKn,b.ShearKn,"selected shear");Same(a.DeflectionMm,b.DeflectionMm,"selected displacement");
            Same(a.DeflectionUtilization,b.DeflectionUtilization,"selected deflection criterion");Same(a.StrengthUtilization,b.StrengthUtilization,"selected strength");
            Same(a.GoverningDeflectionMm,b.GoverningDeflectionMm,"selected governing peak");Same(a.GoverningDeflectionLimitMm,b.GoverningDeflectionLimitMm,"selected allowable");Check(a.GoverningDeflectionRegion==b.GoverningDeflectionRegion,"selected governing region");
        }
    }
    internal static void Run()
    {
        var input=Input();var cache=new TowerStockPlanner.FindWorkCache(2);
        var random=new Random(62134);int rejected=0,passing=0;
        for(int sample=0;sample<400;sample++)
        {
            double length=1.25+random.NextDouble()*6.5,q=.125+random.NextDouble()*55;
            int count=2+random.Next(5);var stations=Enumerable.Range(0,count).Select(i=>length*(.08+.84*i/(count-1))).ToArray();
            var reference=TowerBeamAnalysis.Analyze(length,stations,q,input.Primary.EI);
            bool accepted=reference.Moment<=input.Primary.MomentLimitKnM+1e-8 && reference.Shear<=input.Primary.ShearLimitKn+1e-8
                && reference.Reactions.Max()<=input.LegCapacityKn+1e-8 && reference.DeflectionUtilization(0,270)<=1+1e-8;
            bool allowed=TowerStockPlanner.UniformMemberForcesFit(input,length,stations,q);
            Check(!accepted || allowed,"necessary force bounds retain every passing member");
            if(!allowed){rejected++;Check(!accepted,"force bound excludes only failed members");}if(accepted)passing++;
        }
        Check(rejected>100&&passing>20,"force-bound sweep exercises failures and passing members");
        Check(TowerStockPlanner.UniformMemberForcesFit(input,3.6,new[] {.8,1.8,2.8},27.5*(1+1e-7)),"force boundary keeps a conservative numerical margin");
        Check(!TowerStockPlanner.UniformMemberForcesFit(input,3.6,new[] {.8,1.8,2.8},27.5*(1+1e-4)),"free-tail shear rejects a definite excess");
        var supports=new[] { .15,1.45,2.45,3.75 };
        foreach(double q in new[] {.125,7.125,21.375})foreach(double ei in new[] {899.7512,865.2})
        {
            var direct=TowerBeamAnalysis.Analyze(3.9,supports,q,ei);var first=cache.Analyze(input,3.9,(double[])supports.Clone(),q,ei);
            var second=cache.Analyze(input,3.9,(double[])supports.Clone(),q,ei);Check(ReferenceEquals(first,second),"immutable exact response hit");Response(direct,second);
        }
        Check(input.Trace.Count(BeamSearchCounter.PrimaryCacheHits)==6,"hits counted independently of analyses");
        var near=(double[])supports.Clone();near[1]=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(near[1])+1);
        long before=input.Trace.Count(BeamSearchCounter.PrimaryAnalyses);cache.Analyze(input,3.9,near,21.375,865.2);Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==before+1,"one-bit support change is not rounded");
        cache.Analyze(input,3.9,supports,.125,899.7512);Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==before+2,"evicted response recomputed");
        before=input.Trace.Count(BeamSearchCounter.PrimaryAnalyses);
        for(int i=0;i<2;i++){bool failed=false;try{cache.Analyze(input,3.9,new[] {.15,.15},7.125,899.7512);}catch(ArgumentException){failed=true;}Check(failed,"invalid geometry remains rejected");}
        Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==before+2,"exceptions are not cached");
        using(var cancelled=new CancellationTokenSource())
        {
            cache.Analyze(input,3.9,supports,.125,899.7512);cancelled.Cancel();input.Trace=new BeamSearchTrace("Cancelled cache hit",cancelled.Token);
            bool stopped=false;try{cache.Analyze(input,3.9,supports,.125,899.7512);}catch(OperationCanceledException){stopped=true;}Check(stopped,"cancellation precedes cache hit");
        }
        foreach(bool custom in new[] {false,true})foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(int criterion in new[] {270,350,500})foreach(bool uplift in new[] {false,true})
        {
            input=Input(custom,material);input.DeflectionDenominator=criterion;input.IgnoreUplift=uplift;cache=new TowerStockPlanner.FindWorkCache();
            foreach(var positions in new[] {new[] {150.0,1450,2450,3750},new[] {300.0,1300,2600,3600},new[] {150.0,1150,2750,3750}})
            {
                var expected=TowerStockPlanner.Find(input,positions,6.125);Plan(expected,TowerStockPlanner.Find(input,positions,6.125,cache));
                var fresh=TowerStockPlanner.Find(input,positions,6.125,cache);Plan(expected,fresh);
                if(fresh!=null){fresh.Pieces[0].StartMm+=100;fresh.Reactions[0]+=1;Plan(expected,TowerStockPlanner.Find(input,positions,6.125,cache));}
            }
            Check(input.Trace.Count(BeamSearchCounter.PrimaryCacheHits)>0 || cache.PlanHits>0,"real stock selection reuses checked work");
        }
        input=Input();cache=new TowerStockPlanner.FindWorkCache();var mutablePositions=new[] {150.0,1450,2450,3750};
        foreach(var change in new Action<TowerInput>[] {i=>i.LegCapacityKn=.1,i=>i.LegCapacityKn=70,i=>i.DeflectionDenominator=500,
            i=>i.DeflectionDenominator=270,i=>i.IgnoreUplift=false,i=>i.AllowCustomH20Lengths=true,i=>i.StockEndSetbackMm=300,i=>i.StockEndSetbackMm=150,
            i=>i.PrimaryMaterial=PrimaryBeamMaterial.WS10,i=>i.PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,i=>i.StockLengthsMm[3]=3600})
        {
            TowerStockPlanner.Find(input,mutablePositions,3,cache);change(input);
            Plan(TowerStockPlanner.Find(input,mutablePositions,3),TowerStockPlanner.Find(input,mutablePositions,3,cache));
        }
        input=Input();cache=new TowerStockPlanner.FindWorkCache();var untouched=(double[])mutablePositions.Clone();
        var retained=TowerStockPlanner.Find(input,untouched,3);TowerStockPlanner.Find(input,mutablePositions,3,cache);mutablePositions[1]+=25;
        Plan(retained,TowerStockPlanner.Find(input,untouched,3,cache));Check(cache.PlanHits==1,"retained plan key owns its support coordinates");
        input.Diagnostics=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:input.IgnoreUplift);
        TowerStockPlanner.Find(input,untouched,12,cache);var once=input.Diagnostics.Exception("test").Details;
        var expectedDiagnostic=new TowerSearchDiagnostics(270,trace:input.Trace,ignoreUplift:input.IgnoreUplift);input.Diagnostics=expectedDiagnostic;
        TowerStockPlanner.Find(input,untouched,12,cache);Check(once==expectedDiagnostic.Exception("test").Details,"plan hit retains full rejection evidence");
        var rankInput=Input();rankInput.TotalLineLoad=2;
        var ranked=new[] {
            TowerOptimizer.Evaluate(rankInput,new[] {150.0,1150,2750,3750},new[] {true,false,true},1524,false),
            TowerOptimizer.Evaluate(rankInput,new[] {150.0,1650,2250,3750},new[] {true,false,true},1524,false),
            TowerOptimizer.Evaluate(rankInput,new[] {250.0,1774,2126,3650},new[] {true,false,true},1000,true),
            TowerOptimizer.Evaluate(rankInput,new[] {250.0,1774,2126,3650},new[] {true,false,true},1500,true)
        };
        Check(ranked.All(l=>l!=null),"ranking fixtures are physically checked stock layouts");
        foreach(var layout in ranked)layout.EngineerSearch=true;
        var better=typeof(TowerOptimizer).GetMethod("Better",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        int rankRejected=0;
        foreach(var incumbent in ranked)foreach(var candidate in ranked)
        {
            bool possible=TowerOptimizer.StandaloneBraceMayImprove(candidate.PositionsMm,candidate.Joined,candidate.WidthMm,candidate.Rotated,incumbent);
            if(!possible){rankRejected++;Check(!(bool)better.Invoke(null,new object[] {candidate,incumbent}),"fixed support-brace rank cannot discard a better stock arrangement");}
        }
        Check(rankRejected>0,"ranking bound exercises strictly worse passing arrangements");
        foreach(bool custom in new[] {false,true})foreach(double moment in new[] {0.0,.625})
        {
            input=Input(custom);input.LengthMm=2450;input.StockLengthsMm=new[] {1250.0,2450};input.TotalLineLoad=9;input.EngineerSearch=true;
            input.TransverseLoadMoment=moment;input.ReuseSearchWork=false;input.PruneUneconomicCandidates=false;var direct=TowerOptimizer.Optimize(input);
            input.ReuseSearchWork=true;input.PruneUneconomicCandidates=true;var actual=TowerOptimizer.Optimize(input);
            Check(direct.PositionsMm.SequenceEqual(actual.PositionsMm)&&direct.Joined.SequenceEqual(actual.Joined),"engineer search support/ranking parity");
            Same(direct.WidthMm,actual.WidthMm,"engineer width parity");Check(direct.Rotated==actual.Rotated,"engineer rotation parity");Plan(direct.StockPlan,actual.StockPlan);
        }
        Console.WriteLine("PASS "+checks+" standalone stock reuse checks: exact responses, diagrams, all criteria, custom/catalogue and WS10, stock choices, eviction, ownership, cancellation and final ranking");
    }
}
