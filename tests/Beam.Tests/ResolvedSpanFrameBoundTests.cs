using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class ResolvedSpanFrameBoundTests
{
    static int checks;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Resolved span/frame bound: "+message);}
    static TowerInput Input(double capacity=70,bool optimized=true,CancellationToken token=default)
        =>(TowerInput)typeof(ResolvedSupportCountBoundTests).GetMethod("Input",hidden).Invoke(null,new object[] {capacity,optimized,token});
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Prepare(TowerInput input,double force)
    {
        try{return (Func<TransferredSlabOptimizer.Geometry,double,bool>)typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",hidden).Invoke(null,new object[] {input,force});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static TransferredSlabOptimizer.Geometry Geometry(double[] supports,double length=2450)=>new TransferredSlabOptimizer.Geometry {
        Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=length,FirstSupport=0,LastSupport=supports.Length-1}},
        Positions=supports,Joined=Enumerable.Repeat(true,supports.Length-1).ToArray(),MemberSupports=new[] {supports}};
    static void PrimitiveNecessity()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(double sign in new[] {-1.0,1.0})
        {
            var input=Input();input.PrimaryMaterial=material;input.Diagnostics=null;
            double boundary=2*(input.Primary.ShearLimitKn+input.Primary.ShearLimitKn*1e-6);
            foreach(double q in new[] {1.0,10,Math.BitDecrement(boundary),boundary,Math.BitIncrement(boundary),boundary+1})
            foreach(var supports in new[] {new[] {0.0,1000},new[] {150.0,850},new[] {500.0,850}})
            {
                var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=sign*q}};
                bool necessary=CrossBeamSupportOptimizer.PrimaryForcesFit(input,1000,supports,loads,"unit member");
                var dense=sign<0 ? LoadedBeamAnalysis.AnalyzeSigned(1000,supports,loads,null,input.Primary.EI,270)
                    : LoadedBeamAnalysis.AnalyzeDenseReference(1000,supports,loads,null,input.Primary.EI,270);
                Check(necessary || dense.ShearKn>input.Primary.ShearLimitKn+1e-8 || dense.MomentKnM>input.Primary.MomentLimitKnM+1e-8,
                    "signed span/tail necessary rejection retains independent dense strength failure at adjacent tolerance values");
            }
        }
    }
    static void PreparedCases()
    {
        foreach(double force in new[] {-60.0,-20,0,20,40,60})
        foreach(var supports in new[] {new[] {200.0,2250},new[] {200.0,1225,2250},new[] {1300.0,2300},new[] {1800.0,2250}})
        {
            var input=Input();var reference=Input(70,false);var prepared=Prepare(input,force);var old=Prepare(reference,force);var geometry=Geometry(supports);
            long before=input.Trace.Count(BeamSearchCounter.PrimaryAnalyses);
            Check(PreparedReceiverSupportBounds.MayFit(prepared,geometry,0),"unprepared exact load keys remain inconclusive");
            PreparedReceiverSupportBounds.Minimum(prepared,new[] {2450.0},0);
            bool early=PreparedReceiverSupportBounds.MayFit(prepared,geometry,0);
            Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==before,"early necessary queries do not execute primary FE analyses");
            bool full=prepared(geometry,0),expected=old(geometry,0);
            Check(full==expected && (early || !full),"every early exclusion also fails the original full callback and preserves its outcome");
            Check(PreparedReceiverSupportBounds.MayFit(prepared,geometry,0)==early,"completed exact necessary outcomes may be reused within their prepared callback");
        }
        foreach(var item in new[] {Tuple.Create(60.0,new[] {200.0,2250},"span shear lower bound"),Tuple.Create(60.0,new[] {1300.0,2300},"cantilever shear"),Tuple.Create(20.0,new[] {1800.0,2250},"cantilever moment")})
        {
            var input=Input();var prepared=Prepare(input,item.Item1);PreparedReceiverSupportBounds.Minimum(prepared,new[] {2450.0},0);
            Check(!PreparedReceiverSupportBounds.MayFit(prepared,Geometry(item.Item2),0),"strict resolved span/tail violation is exposed before ordinary geometry work");
            Check(input.Diagnostics.Exception("early rejected member").Details.Contains(item.Item3),"an entirely early-rejected search retains a concrete necessary engineering explanation");
        }
        var passing=Input();var pass=Prepare(passing,0);var good=Geometry(new[] {200.0,1225,2250});PreparedReceiverSupportBounds.Minimum(pass,new[] {2450.0},0);
        long analyses=passing.Trace.Count(BeamSearchCounter.PrimaryAnalyses);
        Check(PreparedReceiverSupportBounds.MayFit(pass,good,0) && pass(good,0) && passing.Trace.Count(BeamSearchCounter.PrimaryAnalyses)>analyses,"potential winners retain their complete ordinary FE/capacity callback");
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=Input(token:cancellation.Token);var prepared=Prepare(interrupted,60);PreparedReceiverSupportBounds.Minimum(prepared,new[] {2450.0},0);
            var geometry=Geometry(new[] {200.0,2250});PreparedReceiverSupportBounds.MayFit(prepared,geometry,0);cancellation.Cancel();bool cancelled=false;
            try{PreparedReceiverSupportBounds.MayFit(prepared,geometry,0);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cached necessary failures cannot bypass cancellation");
        }
    }
    static TransferNetworkInput FrameNetwork()=>new TransferNetworkInput {
        Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800},new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}},
        Bays=new[] {new TransferBayRow {WidthMm=1500,ThicknessMm=200}},
        CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=500,BottomElevationMm=-500,SupportMode=CrossBeamSupportMode.DedicatedTowers}}};
    static void FramesAndComposition()
    {
        var input=Input();var network=FrameNetwork();var geometry=Geometry(new[] {200.0,2250});int fullCalls=0,metadataCalls=0;
        var child=PreparedReceiverSupportBounds.Attach((g,e)=>{fullCalls++;return true;},(s,e)=>2,(g,e)=>{metadataCalls++;return true;});
        var cache=new TransferredSlabOptimizer.ResponseCache<Tuple<TransferredSlabOptimizer.Geometry,double,double>,bool>(4096);
        var wide=TransferNetworkOptimizer.WithReceiverFrameChecks(input,network,0,new TowerLayout {WidthMm=2000},child,cache);
        Check(PreparedReceiverSupportBounds.Minimum(wide,new[] {2450.0},0)==2,"frame metadata preserves its child's exact scalar floor");
        Check(!PreparedReceiverSupportBounds.MayFit(wide,geometry,0) && !wide(geometry,0) && cache.Count==1 && fullCalls==0 && metadataCalls==0,
            "frame-first necessary rejection shares its ordinary cache and short-circuits the same child");
        var narrow=TransferNetworkOptimizer.WithReceiverFrameChecks(input,network,0,new TowerLayout {WidthMm=1000},child,cache);
        Check(PreparedReceiverSupportBounds.MayFit(narrow,geometry,0) && narrow(geometry,0) && cache.Count==2 && fullCalls==1 && metadataCalls==1,
            "another exact width retains its own frame result and ordinary child checks");
        Check(PreparedReceiverSupportBounds.MayFit(wide,geometry,3000) && cache.Count==3,"changed absolute edge retains an independent frame-footprint query");
        Check(input.Trace.Count(BeamSearchCounter.ReceiverFrameMetadataRejected)>0,"frame metadata has a distinct truthful rejection counter");
        var unknown=new Func<TransferredSlabOptimizer.Geometry,double,bool>((g,e)=>true);
        var either=typeof(CrossBeamSupportOptimizer).GetMethod("EitherReceiver",hidden);
        var fallback=(Func<TransferredSlabOptimizer.Geometry,double,bool>)either.Invoke(null,new object[] {wide,unknown});
        Check(PreparedReceiverSupportBounds.MayFit(fallback,geometry,0),"an unknown accepting OR alternative remains inconclusive despite another frame failure");
        using(var cancellation=new CancellationTokenSource())
        {
            var cancelledInput=Input(token:cancellation.Token);var wrapped=TransferNetworkOptimizer.WithReceiverFrameChecks(cancelledInput,network,0,new TowerLayout {WidthMm=2000},child);cancellation.Cancel();bool cancelled=false;
            try{PreparedReceiverSupportBounds.MayFit(wrapped,geometry,0);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"frame-cache queries propagate cancellation");
        }
    }
    sealed class AcceptingReceiver
    {
        internal bool Check(TransferredSlabOptimizer.Geometry geometry,double edge)=>true;
    }
    static TowerInput Cover(bool early,bool frames=false)
    {
        var input=(TowerInput)typeof(ResolvedSupportCountBoundTests).GetMethod("CoverInput",hidden).Invoke(null,null);
        input.LegCapacityKn=70;input.EngineerStationLimit=4;input.NetworkLegCeiling=8;input.PrepareSpanGeometry=true;input.TransferredSlab.SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache();
        if(frames)input.TransferredSlab.FixedTowerWidthMm=2000;
        input.TransferReceiverPreparation=probe=>
        {
            var prepared=frames ? TransferNetworkOptimizer.WithReceiverFrameChecks(input,FrameNetwork(),0,new TowerLayout {WidthMm=2000},PreparedReceiverSupportBounds.Attach(new AcceptingReceiver().Check,(s,e)=>0)) : Prepare(input,60);
            return early ? prepared : PreparedReceiverSupportBounds.Attach((g,e)=>prepared(g,e),(s,e)=>PreparedReceiverSupportBounds.Minimum(prepared,s,e));
        };
        return input;
    }
    static TowerLayout Solve(TowerInput input)
    {
        try{return (TowerLayout)typeof(ResolvedSupportCountBoundTests).GetMethod("OptionalSolve",hidden).Invoke(null,new object[] {input});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static string Identity(TowerLayout layout)=>(string)typeof(ResolvedSupportCountBoundTests).GetMethod("LayoutIdentity",hidden).Invoke(null,new object[] {layout});
    static void Integration()
    {
        foreach(bool frames in new[] {false,true})
        {
            var actual=Cover(true,frames);var reference=Cover(false,frames);var stocks=actual.TransferredSlab.StockCovers[0];
            var actualGeometries=TransferredSlabOptimizer.SearchGeometries(actual,actual.TransferredSlab.SharedGeometryCache,stocks,frames);
            var referenceGeometries=TransferredSlabOptimizer.SearchGeometries(reference,reference.TransferredSlab.SharedGeometryCache,stocks,frames);
            var prepared=actual.TransferReceiverPreparation(null);PreparedReceiverSupportBounds.Minimum(prepared,stocks,0);
            var rejected=actualGeometries.Where(g=>!PreparedReceiverSupportBounds.MayFit(prepared,g,0)).ToArray();
            Check(rejected.Length>0,"the unchanged single-stock candidate universe contains a resolved span or frame rejection");
            var expected=Solve(reference);var result=Solve(actual);
            Check(Identity(result)==Identity(expected),"early single-stock necessity preserves the late-callback selected geometry and member forces");
            Check(rejected.All(g=>g.SpanShape==null && g.Rankings==null),"single-stock necessary failures are skipped before span-shape, joint-grid and rank work");
            Check(referenceGeometries.Any(g=>g.SpanShape!=null && g.Rankings!=null),"the same former path constructs ordinary span/rank metadata before its late rejection");
            Check(actual.Trace.Count(BeamSearchCounter.PreparedGeometryBoundsRejected)>0 && reference.Trace.Count(BeamSearchCounter.PreparedGeometryBoundsRejected)==0,"combined early geometry exclusions have a distinct counter");
            Console.WriteLine((frames ? "Frame" : "Resolved span")+" single-stock integration: "+rejected.Length+" necessary failures among "+actualGeometries.Length+" generated; "+actual.Trace.Count(BeamSearchCounter.PreparedGeometryBoundsRejected)+" skipped early.");
        }
    }
    internal static void Run(){PrimitiveNecessity();PreparedCases();FramesAndComposition();Integration();Console.WriteLine("PASS "+checks+" resolved span/frame checks: unchanged necessary mathematics, ordinary callback/FE parity, truthful counters, Boolean composition, cancellation and single-stock pre-metadata pruning");}
}
