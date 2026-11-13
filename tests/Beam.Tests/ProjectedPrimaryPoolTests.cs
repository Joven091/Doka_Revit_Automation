using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using EngineeringDashboard;

internal static class ProjectedPrimaryPoolTests
{
    static int checks,certificates;
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Static;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception("Projected primary pool: "+message);}
    static TowerInput Input()=>new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,LegCapacityKn=70,IgnoreUplift=true,
        DeflectionDenominator=270,NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache(),Trace=new BeamSearchTrace("projected pool test")};
    static void Warm(TowerInput input,double stock,double[] supports,double start,double end)
        =>TransferredSlabOptimizer.AnalyzePrimary(input,stock,supports,new[]{new BeamDistributedLoad {StartMm=start,EndMm=end,KnPerM=1}},
            Array.Empty<BeamPointLoad>(),input.NetworkNumerics.Matrix,input.NetworkNumerics);
    static bool Range(TowerInput input,double lo,double hi,double outer=70)
        =>TransferredSlabOptimizer.TryRejectUniformPrimaryRange(input,3000,new[]{500.0,2500},0,3000,lo,hi,input.NetworkNumerics,outer,out _);
    static void BoundariesAndScopes()
    {
        var input=Input();Check(!Range(input,100,200),"cold shape stays on the original pair path");Warm(input,3000,new[]{500.0,2500},0,3000);
        Check(Range(input,100,200),"a robust failure rejects a finite same-shape factor range");
        Check(!Range(input,1,2),"a passing lower envelope never rejects the pool");
        foreach(var range in new[]{Tuple.Create(-1.0,200.0),Tuple.Create(200.0,100.0),Tuple.Create(double.NaN,200.0),Tuple.Create(100.0,double.PositiveInfinity),Tuple.Create(0.0,200.0),Tuple.Create(-0.0,200.0)})
            Check(!Range(input,range.Item1,range.Item2),"negative, zero, unordered or nonfinite ranges stay ordinary");
        Check(!Range(input,100,double.MaxValue),"overflow at the largest factor cannot become a rejection proof");
        // The reanalysis boundary uses INPUT capacity, while the final overload
        // predicate uses the separately supplied OUTER capacity.
        input.LegCapacityKn=225;Check(!Range(input,100,200,70),"a range crossing a different input-capacity reanalysis band is unknown");
        input.LegCapacityKn=150;Check(!Range(input,100,200,70),"touching the input-capacity boundary is unknown");
        input.LegCapacityKn=70;
        var outside=typeof(TransferredSlabOptimizer).GetMethod("OutsideBand",Hidden);
        bool Outside(double lo,double hi,double centre,double tolerance)=>(bool)outside.Invoke(null,new object[]{lo,hi,centre,tolerance});
        foreach(double tolerance in new[]{Math.Pow(2.0,-20),Math.Pow(2.0,-24)})
        {
            Check(!Outside(1+tolerance,2,1,tolerance),"exact upper band equality is unknown");
            Check(!Outside(0,1-tolerance,1,tolerance),"exact lower band equality is unknown");
            Check(!Outside(1-tolerance*2,1+tolerance*2,1,tolerance),"crossing the band is unknown");
            Check(Outside(PortableTestRuntime.NextUp(1+tolerance),2,1,tolerance),"a strictly separated upper range is supported");
            Check(Outside(0,PortableTestRuntime.NextDown(1-tolerance),1,tolerance),"a strictly separated lower range is supported");
        }
        Check(!Outside(-1e-7,-1e-7,0,1e-7) && !Outside(1e-7,1e-7,0,1e-7),"the actual uplift boundary touch remains unknown");
        Check(!Outside(70-1e-6,70+1e-6,70,1e-6),"the actual reaction fallback band crossing remains unknown");
        Check(!Outside(1-1e-7,1+1e-7,1,1e-7),"the actual utilization fallback band crossing remains unknown");
        foreach(var change in new Action<TowerInput>[] {x=>x.PrimaryMaterial=PrimaryBeamMaterial.WS10,x=>x.OptimizePrimaryResponses=false,x=>x.ReuseSearchWork=false,
            x=>x.ReuseNetworkNumerics=false,x=>x.NetworkNumerics=null,x=>x.Trace=new BeamSearchTrace("explicit range deadline",searchTimeLimit:TimeSpan.FromHours(1))})
        {var altered=input.CopyForSearch();change(altered);Check(!Range(altered,100,200),"unsupported flags/material/owner/timed scopes keep original checks");}
        Check(!TransferredSlabOptimizer.TryRejectUniformPrimaryRange(input,3000,new[]{500.0,1500,2500},0,3000,100,200,input.NetworkNumerics,70,out _),"unsupported support count stays ordinary");
        Check(!TransferredSlabOptimizer.TryRejectUniformPrimaryRange(input,3000,new[]{500.0,2500},-1,3000,100,200,input.NetworkNumerics,70,out _),"out-of-member load geometry stays ordinary");
        var uplift=Input();Warm(uplift,3000,new[]{100.0,700},0,3000);uplift.IgnoreUplift=false;
        bool upliftFound=uplift.NetworkNumerics.UniformShapes.TryGetValue(new TransferredSlabOptimizer.UniformShapeKey(3000,new[]{100.0,700},uplift.Primary.EI,uplift.DeflectionDenominator,0,3000),out var upliftUnit);
        Check(upliftFound && upliftUnit.DeflectionEvaluated && upliftUnit.Reactions.Min()<0,"signed-reaction fixture has a complete actual cached response");
        Check(uplift.IgnoreUplift,"the existing connected-formwork policy ignores the compatibility setter; the test cannot invent another policy");
        Check(!TransferredSlabOptimizer.TryRejectUniformPrimaryRange(uplift,3000,new[]{100.0,700},0,3000,.001,.002,uplift.NetworkNumerics,70,out _),"a small allowed signed reaction does not invent an uplift rejection");
        Check(TransferredSlabOptimizer.TryRejectUniformPrimaryRange(uplift,3000,new[]{100.0,700},0,3000,100,200,uplift.NetworkNumerics,70,out _),"negative reaction coefficients do not invalidate a robust monotone strength/positive-leg overload");
        using(var source=new CancellationTokenSource())
        {
            var cancelled=input.CopyForSearch();cancelled.Trace=new BeamSearchTrace("cancelled pool proof",source.Token);source.Cancel();bool stopped=false;
            try{Range(cancelled,100,200);}catch(OperationCanceledException){stopped=true;}Check(stopped,"warm range lookup observes cancellation");
        }
    }
    static object Call(string name,params object[] args)
    {try{return typeof(CrossBeamSupportOptimizer).GetMethod(name,Hidden).Invoke(null,args);}catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    static CrossBeamSupportOptimizer.ProjectedSecondaryOptions Options(params Tuple<double,double>[] pairs)
        =>new CrossBeamSupportOptimizer.ProjectedSecondaryOptions {Items=pairs.Select(p=>new CrossBeamSupportOptimizer.ProjectedSecondaryOption {Reactions=p}).ToArray(),
            Reactions=pairs,Envelope=CrossBeamSupportOptimizer.ProjectedReactionEnvelope.Create(pairs)};
    static void ActualPairs()
    {
        var random=new Random(73129);
        foreach(double stock in new[]{2450.0,3300,4500})foreach(double loaded in new[]{.72,.9,1.0})foreach(double inset in new[]{.12,.22,.32})
        {
            var input=Input();double span=stock*loaded,start=(span-stock)/2,edge=0;
            double[] positions={span*inset,span*(1-inset)},supports=positions.Select(p=>p-start).ToArray();
            Warm(input,stock,supports,edge-start,span-edge-start);
            for(int sample=0;sample<24;sample++)
            {
                var pairs=Enumerable.Range(0,7).Select(_=>Tuple.Create(1+random.NextDouble()*250,1+random.NextDouble()*250)).ToArray();
                var options=Options(pairs);var args=new object[]{input,input,stock,start,positions,edge,span,options,null,0,null};
                bool rejected=(bool)Call("TryRejectProjectedPrimaryPool",args);
                var envelope=options.Envelope;Check(envelope!=null && ReferenceEquals(envelope.Source,pairs),"metadata owns the exact original retained pair source");
                var representative=pairs[envelope.MinimumPair];double actual=envelope.MinimumLine==0 ? representative.Item1 : representative.Item2;
                Check(actual==envelope.MinimumGoverning && pairs.All(p=>Math.Max(p.Item1,p.Item2)>=actual),"representative governing minimum is an actual retained pair line");
                if(!rejected)continue;certificates++;
                Check((int)args[9]==envelope.MinimumLine,"reported failure belongs to that actual representative line");
                var raw=input.CopyForSearch();raw.Trace=null;raw.Diagnostics=null;raw.ReuseSearchWork=false;raw.OptimizePrimaryResponses=false;raw.NetworkNumerics=null;
                foreach(var pair in pairs)
                {
                    bool both=true;
                    for(int line=0;line<2;line++)
                    {
                        double q=(line==0 ? pair.Item1 : pair.Item2)*span/(span-2*edge);
                        var member=new TransferMember {PrimaryMaterial=input.PrimaryMaterial,H20Count=2,Name=line==0 ? "P1 L" : "P1 R",StartMm=start,LengthMm=stock,
                            SupportsMm=(double[])supports.Clone(),Points=Array.Empty<BeamPointLoad>(),Uniform=new[]{new BeamDistributedLoad {StartMm=edge-start,EndMm=span-edge-start,KnPerM=q}}};
                        var checkedMember=(TransferMember)Call("CheckDedicatedPrimary",member,raw,raw,0);
                        if(checkedMember==null){both=false;break;}
                    }
                    Check(!both,"every certified actual pair independently fails unchanged full CheckDedicatedPrimary with numerical reuse disabled");
                }
            }
        }
        Check(certificates>0,"real retained-pair families exercise nonzero whole-pool rejection");
        foreach(var pair in new[]{Tuple.Create(double.NaN,1.0),Tuple.Create(-1.0,1.0),Tuple.Create(1.0,double.PositiveInfinity)})
            Check(Options(pair).Envelope==null,"signed/nonfinite retained pairs disable the entire pool envelope");
        Check(Options(Tuple.Create(-0.0,0.0)).Envelope!=null,"signed zero metadata is retained without rounding or rejecting source pairs");
        var scope=Input();var altered=Options(Tuple.Create(100.0,150.0));altered.Reactions=(Tuple<double,double>[])altered.Reactions.Clone();
        Check(!(bool)Call("TryRejectProjectedPrimaryPool",scope,scope,3000.0,0.0,new[]{500.0,2500},0.0,3000.0,altered,null,0,null),"a changed reaction-array identity cannot reuse the native envelope");
        Check(Options().Envelope==null,"an empty pool supplies no speculative certificate");
    }
    internal static void Run(){BoundariesAndScopes();ActualPairs();Console.WriteLine("PASS "+checks+" projected whole-pool proof checks, "+certificates+" actual-pair certificates; full old per-pair checks, bands, scopes and fallback");}
}
