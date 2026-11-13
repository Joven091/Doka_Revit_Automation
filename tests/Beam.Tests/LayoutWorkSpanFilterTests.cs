using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class LayoutWorkSpanFilterTests
{
    static int checks;
    static readonly MethodInfo core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Worklist span filter: "+message);}
    static TowerLayout Solve(TowerInput input)
    {
        try{return (TowerLayout)core.Invoke(null,new object[] {input,true,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void Fails<T>(Action action) where T:Exception
    {bool failed=false;try{action();}catch(T){failed=true;}Check(failed,"expected "+typeof(T).Name);}
    static TowerInput Input(bool early,bool onlyFailing=false,bool positioned=true,bool defer=true)
    {
        const double length=3900;
        var network=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=400,DepthMm=3000},new TransferBeamRow {WidthMm=400,DepthMm=3000}},
            Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}},
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1950,WidthMm=650,DepthMm=600,BottomElevationMm=-600}}};
        var sets=onlyFailing ? new[] {new[] {1200.0,2700}} : new[] {new[] {1200.0,2700},new[] {450.0,1450,2450,3450},new[] {450.0,1050,1650,2250,2850,3450}};
        var geometries=sets.Select(positions=>new TransferredSlabOptimizer.Geometry {Positions=positions,Joined=Enumerable.Repeat(true,positions.Length-1).ToArray(),
            Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=length,FirstSupport=0,LastSupport=positions.Length-1}},MemberSupports=new[] {positions}}).ToArray();
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,DeferCrossingChecks=true,DeferTransferLayout=defer,PrepareSpanGeometry=early,
            LengthMm=length,EngineerStationLimit=6,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1800,2450,3900},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,
            DeflectionDenominator=270,IgnoreUplift=true,Trace=new BeamSearchTrace("worklist necessary-span regression"),
            TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=network.AreaLoad(3000)*.4,
                UpperSpacingMm=305,PlywoodSheetLengthMm=2440,FixedCoveredLengthMm=length,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {length}},
                SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache {Fixed=geometries}}};
        if(positioned){input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.EngineerPlan.InitialLegs=4;}
        input.TransferLowerAcceptance=probe=>true;
        input.TransferReceiverPreparation=probe=>(g,e)=>g.Positions.Length>=3;
        return input;
    }
    static string Fingerprint(TowerLayout layout)
    {
        Func<double,string> number=x=>x.ToString("R",CultureInfo.InvariantCulture);
        return string.Join(",",layout.PositionsMm.Select(number))+"|"+string.Join(",",layout.Joined)+"|"+number(layout.WidthMm)
            +"|"+string.Join(";",layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(x=>x).Concat(layout.Transfer.UpperMembers).Select(m=>
                m.Name+":"+string.Join(",",new[] {m.StartMm,m.LengthMm,m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Utilization}.Select(number))
                +":"+string.Join(",",m.SupportsMm.Select(number))+":"+string.Join(",",m.Analysis.Reactions.Select(number))
                +":"+string.Join(",",m.Uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm,q.KnPerM}).Select(number))));
    }
    static void OrderedOptions()
    {
        var crossType=typeof(TransferredSlabOptimizer).GetNestedType("CrossOption",BindingFlags.NonPublic);
        var method=typeof(TransferredSlabOptimizer).GetMethod("FirstPrimarySpansPassing",BindingFlags.Static|BindingFlags.NonPublic);
        var input=Input(true);var geometry=input.TransferredSlab.SharedGeometryCache.Fixed[0];
        foreach(int passing in new[] {-1,0,1,2})
        {
            var crosses=Array.CreateInstance(crossType,3);
            for(int i=0;i<crosses.Length;i++)
            {
                object cross=Activator.CreateInstance(crossType,true);
                crossType.GetField("SpanPrepared",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cross,true);
                crossType.GetField("MinimumLineLoads",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cross,new[] {i==passing ? 1.0 : 100,0.0});
                crossType.GetField("UnknownSpans",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cross,Array.Empty<BeamDistributedLoad>());
                crossType.GetField("DesignCases").SetValue(cross,new[] {0,1});crosses.SetValue(cross,i);
            }
            var before=crosses.Cast<object>().ToArray();
            int actual=(int)method.Invoke(null,new object[] {input,geometry,crosses,0.0});
            Check(actual==passing,"all prior crossing options fail and the first complete passing stage set keeps its exact index");
            Check(before.SequenceEqual(crosses.Cast<object>()),"necessary filtering never reorders crossing options");
        }
    }
    internal static void Run()
    {
        OrderedOptions();
        foreach(bool positioned in new[] {false,true})foreach(bool defer in new[] {false,true})
        {
            var expectedInput=Input(false,positioned:positioned,defer:defer);var actualInput=Input(true,positioned:positioned,defer:defer);
            int expectedCalls=0,actualCalls=0;
            expectedInput.TransferLayoutAcceptance=layout=>{expectedCalls++;return layout.PositionsMm.Length>=4;};
            actualInput.TransferLayoutAcceptance=layout=>{actualCalls++;return layout.PositionsMm.Length>=4;};
            var expected=Solve(expectedInput);var actual=Solve(actualInput);
            Check(Fingerprint(actual)==Fingerprint(expected),"fixed-plan, planless and reference-layout paths preserve exact selected geometry and all final member demands");
            Check(actualCalls==expectedCalls,"the passing candidate sequence still invokes ordinary full-layout acceptance");
            if(!positioned || !defer)Check(actualInput.Trace.Count(BeamSearchCounter.TransferCandidates)==expectedInput.Trace.Count(BeamSearchCounter.TransferCandidates),"planless initial inference and nondeferred candidates retain their original worklist");
            foreach(var member in actual.Transfer.PrimaryCases.Concat(actual.Transfer.LowerCases).SelectMany(x=>x))
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && Math.Abs(member.Analysis.Reactions.Sum()-member.Analysis.TotalLoadKn)<1e-7,"surviving candidates retain complete member and equilibrium checks");
        }
        var rejected=Input(true,true);var reference=Input(false,true);
        Fails<TowerOptimizationException>(()=>Solve(rejected));Fails<TowerOptimizationException>(()=>Solve(reference));
        Check(rejected.Trace.Count(BeamSearchCounter.TransferCandidates)==0 && reference.Trace.Count(BeamSearchCounter.TransferCandidates)>0,"a geometry failing every necessary cross option is removed before candidate evaluation");
        Check(rejected.TransferredSlab.SharedGeometryCache.Fixed[0].Rankings==null && reference.TransferredSlab.SharedGeometryCache.Fixed[0].Rankings!=null,"all-cross failure is removed before ranking metadata is built");
        using(var cancellation=new CancellationTokenSource())
        {
            var input=Input(true);input.Trace=new BeamSearchTrace("cancelled necessary worklist",cancellation.Token);cancellation.Cancel();
            Fails<OperationCanceledException>(()=>Solve(input));
        }
        Console.WriteLine("PASS "+checks+" early worklist span checks: all-option failure before ranking, exact final/reference/planless parity, option order, complete callbacks and cancellation");
    }
}
