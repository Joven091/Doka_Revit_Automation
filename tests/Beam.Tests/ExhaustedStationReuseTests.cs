using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class ExhaustedStationReuseTests
{
    static readonly Type preparation=typeof(TransferredSlabOptimizer).GetNestedType("ReceivingPreparation",BindingFlags.NonPublic);
    static readonly MethodInfo core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",BindingFlags.NonPublic|BindingFlags.Static);
    static object Prepared(TowerInput input)=>Activator.CreateInstance(preparation,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] {input},CultureInfo.InvariantCulture);
    static int Exhausted(object prepared,TowerInput input)=>(int)preparation.GetMethod("ExhaustedStations",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(prepared,new object[] {input});
    static TowerLayout Solve(TowerInput input,object prepared)
    {
        try{return (TowerLayout)core.Invoke(null,new object[] {input,true,prepared});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void Fails<T>(Action action) where T:Exception
    {try{action();throw new Exception("Expected "+typeof(T).Name);}catch(T){}}
    static void Check(bool value,string message){if(!value)throw new Exception("Exhausted station reuse: "+message);}
    static TowerInput Input()
    {
        const double length=3900;
        var geometries=new[] {new[] {1200.0,2700},new[] {450.0,1450,2450,3450},new[] {450.0,1050,1650,2250,2850,3450}}.Select(positions=>
            new TransferredSlabOptimizer.Geometry {Positions=positions,Joined=Enumerable.Repeat(true,positions.Length-1).ToArray(),
                Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=length,FirstSupport=0,LastSupport=positions.Length-1}},MemberSupports=new[] {positions}}).ToArray();
        return new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=length,EngineerStationLimit=4,LegCapacityKn=70,
            StockLengthsMm=new[] {1250.0,1800,2450,3900},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,
            IgnoreUplift=true,Trace=new BeamSearchTrace("failed station ceiling regression"),
            TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=3,UpperSpacingMm=305,PlywoodSheetLengthMm=2440,
                FixedCoveredLengthMm=length,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {length}},SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache {Fixed=geometries}}};
    }
    static string Fingerprint(TowerLayout layout)
    {
        Func<double,string> number=x=>x.ToString("R",CultureInfo.InvariantCulture);
        return string.Join(",",layout.PositionsMm.Select(number))+"|"+string.Join(",",layout.Joined)+"|"+layout.WidthMm.ToString("R",CultureInfo.InvariantCulture)
            +"|"+string.Join(";",layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers).Select(m=>
                m.Name+":"+string.Join(",",new[] {m.StartMm,m.LengthMm,m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Utilization}.Select(number))
                +":"+string.Join(",",m.SupportsMm.Select(number))+":"+string.Join(",",m.Analysis.Reactions.Select(number))
                +":"+string.Join(",",m.Uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm,q.KnPerM}).Select(number))
                +":"+string.Join(",",m.Points.SelectMany(p=>new[] {p.PositionMm,p.Kn}).Select(number))));
    }
    static TowerInput Generated(double[] stocks,double width)
    {
        var input=Input();input.LengthMm=stocks.Sum();input.TransferredSlab.FixedCoveredLengthMm=input.LengthMm;
        input.TransferredSlab.StockCovers=new[] {stocks};input.TransferredSlab.FixedTowerWidthMm=width;
        input.TransferredSlab.SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache();
        var network=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=400,DepthMm=200}},Bays=Array.Empty<TransferBayRow>(),UpperSpacingMm=305};
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.EngineerPlan.InitialLegs=3;
        input.TransferLayoutAcceptance=layout=>layout.PositionsMm.Length>=5;return input;
    }
    static void GeneratedExpansionParity()
    {
        foreach(var option in new[] {Tuple.Create(new[] {3900.0},1524.0),Tuple.Create(new[] {3900.0,3900},1524.0),Tuple.Create(new[] {3900.0,3900},1000.0)})
        {
            var stocks=option.Item1;double width=option.Item2;
            var input=Generated(stocks,width);var prepared=Prepared(input);
            Fails<TowerOptimizationException>(()=>Solve(input,prepared));Check(Exhausted(prepared,input)==4,"generated initial tiers are fully exhausted");
            long before=input.Trace.Count(BeamSearchCounter.TransferCandidates);input.EngineerStationLimit=6;
            var actual=Solve(input,prepared);long remaining=input.Trace.Count(BeamSearchCounter.TransferCandidates)-before;
            var reference=Generated(stocks,width);reference.EngineerStationLimit=6;var expected=Solve(reference,null);
            Check(Fingerprint(actual)==Fingerprint(expected),"incrementally generated plan-backed tiers preserve exact ranking and member demands");
            Check(remaining<reference.Trace.Count(BeamSearchCounter.TransferCandidates),"generated earlier candidates are not rechecked after expansion");
        }
    }
    internal static void Run()
    {
        var reused=Input();reused.TransferLayoutAcceptance=layout=>layout.PositionsMm.Length>=6;
        var saved=Prepared(reused);Fails<TowerOptimizationException>(()=>Solve(reused,saved));
        Check(Exhausted(saved,reused)==4,"a complete no-solution pass records its ceiling");
        long before=reused.Trace.Count(BeamSearchCounter.TransferCandidates);
        reused.EngineerStationLimit=6;var actual=Solve(reused,saved);
        long newCandidates=reused.Trace.Count(BeamSearchCounter.TransferCandidates)-before;
        var reference=Input();reference.EngineerStationLimit=6;reference.TransferLayoutAcceptance=layout=>layout.PositionsMm.Length>=6;
        var expected=Solve(reference,null);
        Check(Fingerprint(actual)==Fingerprint(expected),"new tiers retain exact selected geometry, loads and numerical demands");
        Check(newCandidates<reference.Trace.Count(BeamSearchCounter.TransferCandidates),"the expanded ceiling does not evaluate earlier rejected candidates");
        foreach(var member in actual.Transfer.PrimaryCases.Concat(actual.Transfer.LowerCases).SelectMany(c=>c).Concat(actual.Transfer.UpperMembers))
            Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && Math.Abs(member.Analysis.Reactions.Sum()-member.Analysis.TotalLoadKn)<1e-8,"selected member checks and equilibrium remain complete");
        var integrated=Input();integrated.TransferLayoutAcceptance=layout=>layout.PositionsMm.Length>=6;
        var integratedResult=TransferredSlabOptimizer.Optimize(integrated,true);
        var replay=Input();replay.TransferLayoutAcceptance=layout=>layout.PositionsMm.Length>=6;
        var replayResult=TransferredSlabOptimizer.Optimize(replay,false);
        Check(Fingerprint(integratedResult)==Fingerprint(replayResult),"automatic engineer ceiling expansion retains exact final result");
        Check(integrated.Trace.Count(BeamSearchCounter.TransferCandidates)<replay.Trace.Count(BeamSearchCounter.TransferCandidates),"automatic ceiling expansion reuses its completed failed preparation");
        var changed=Input();changed.TransferLayoutAcceptance=layout=>false;var changedPreparation=Prepared(changed);
        Fails<TowerOptimizationException>(()=>Solve(changed,changedPreparation));changed.TransferLayoutAcceptance=layout=>true;
        Check(Exhausted(changedPreparation,changed)==0,"changed acceptance callback invalidates failed-tier reuse");
        Check(Solve(changed,changedPreparation)!=null,"changed callback still searches the prior station tiers");
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=Input();interrupted.Trace=new BeamSearchTrace("cancelled station ceiling",cancellation.Token);
            interrupted.TransferLayoutAcceptance=layout=>{cancellation.Cancel();return false;};var interruptedPreparation=Prepared(interrupted);
            Fails<OperationCanceledException>(()=>Solve(interrupted,interruptedPreparation));
            Check(Exhausted(interruptedPreparation,interrupted)==0,"cancellation cannot record exhaustion");
        }
        long now=0;
        var deadline=Input();deadline.Trace=new BeamSearchTrace("expired station ceiling",default,TimeSpan.FromSeconds(1),()=>now);
        deadline.TransferLayoutAcceptance=layout=>{now=2*Stopwatch.Frequency;return false;};var deadlinePreparation=Prepared(deadline);
        Fails<BeamSearchDeadlineException>(()=>Solve(deadline,deadlinePreparation));
        Check(Exhausted(deadlinePreparation,deadline)==0,"deadline cannot record exhaustion");
        var completedSeed=Input();completedSeed.TransferLayoutAcceptance=layout=>false;completedSeed.NetworkSeedCompletion=new NetworkSeedCompletion();completedSeed.NetworkSeedCompletion.Complete();
        var seedPreparation=Prepared(completedSeed);Fails<TowerOptimizationException>(()=>Solve(completedSeed,seedPreparation));
        Check(Exhausted(seedPreparation,completedSeed)==0,"another completed seed cannot record exhaustive failure");
        GeneratedExpansionParity();
        Console.WriteLine("PASS failed station ceiling reuse: exact result/member parity, fewer repeated candidates, callback change, cancellation, deadline and seed guards");
    }
}
