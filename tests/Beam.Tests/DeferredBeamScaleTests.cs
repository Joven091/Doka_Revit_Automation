using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class DeferredBeamScaleTests
{
    const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static object Field(LoadedBeamAnalysis analysis,string name)=>typeof(LoadedBeamAnalysis).GetField(name,hidden).GetValue(analysis);
    static void Set(LoadedBeamAnalysis analysis,string name,object value)=>typeof(LoadedBeamAnalysis).GetField(name,hidden).SetValue(analysis,value);
    static int Elements(LoadedBeamAnalysis analysis)=>((IList)Field(analysis,"elements")).Count;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Deferred beam scale: "+message);}
    static void Same(double expected,double actual,string message)=>Check(BitConverter.DoubleToInt64Bits(expected)==BitConverter.DoubleToInt64Bits(actual),message);
    static void Same(double[] expected,double[] actual,string message)
    {Check(expected.Length==actual.Length,message+" length");for(int i=0;i<expected.Length;i++)Same(expected[i],actual[i],message);}
    static LoadedBeamAnalysis Unit(double ei=899.7512)=>LoadedBeamAnalysis.AnalyzeSigned(3900,new[] {450.0,1950,3450},
        new[] {new BeamDistributedLoad {StartMm=0,EndMm=3900,KnPerM=1},new BeamDistributedLoad {StartMm=750,EndMm=2775,KnPerM=.375}},
        new[] {new BeamPointLoad {PositionMm=1125,Kn=.625},new BeamPointLoad {PositionMm=2525,Kn=-.125}},ei,270);
    static void Scalars(LoadedBeamAnalysis expected,LoadedBeamAnalysis actual)
    {
        Same(new[] {expected.MomentKnM,expected.ShearKn,expected.DeflectionMm,expected.DeflectionUtilization,expected.TotalLoadKn,expected.GoverningLengthMm},
            new[] {actual.MomentKnM,actual.ShearKn,actual.DeflectionMm,actual.DeflectionUtilization,actual.TotalLoadKn,actual.GoverningLengthMm},"scalar IEEE bits");
        Same(expected.Reactions,actual.Reactions,"reaction IEEE bits");Check(expected.DeflectionEvaluated==actual.DeflectionEvaluated,"deflection completion flag");
    }
    static void Coefficients(LoadedBeamAnalysis expected,LoadedBeamAnalysis actual)
    {
        var a=(IList)Field(expected,"elements");var b=(IList)Field(actual,"elements");Check(a.Count==b.Count,"complete coefficient count");
        for(int i=0;i<a.Count;i++)foreach(var field in a[i].GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
            Same((double)field.GetValue(a[i]),(double)field.GetValue(b[i]),"coefficient IEEE bits: "+field.Name);
    }
    static void Queries(LoadedBeamAnalysis expected,LoadedBeamAnalysis actual)
    {
        Scalars(expected,actual);
        foreach(double x in Enumerable.Range(0,105).Select(i=>Math.Min(3900,i*37.5)).Concat(new[] {450.0,750,1125,1950,2525,2775,3450,3900}))
        {
            Same(expected.Response(x),actual.Response(x),"default response IEEE bits");
            Same(expected.Response(x,false),actual.Response(x,false),"left response IEEE bits");
            Same(expected.Response(x,true),actual.Response(x,true),"right response IEEE bits");
        }
        foreach(double movement in new[] {0.0,.625,-.375})Same(expected.DeflectionWithOffsetMm(movement),actual.DeflectionWithOffsetMm(movement),"equal-offset deflection IEEE bits");
        Same(expected.DeflectionWithAffineOffsetMm(.625,-.375,450,3450),actual.DeflectionWithAffineOffsetMm(.625,-.375,450,3450),"affine-offset deflection IEEE bits");
        Same(expected.WS10StrengthUtilization(),actual.WS10StrengthUtilization(),"coefficient-based strength IEEE bits");
        Coefficients(expected,actual);Check(Field(actual,"scaledSource")==null,"completed materialization releases its source");
    }
    static BeamSolverReply Reply(LoadedBeamAnalysis analysis)
    {
        var member=new TransferMember {Name="P1 L",H20Count=2,LengthMm=3900,SupportsMm=new[] {450.0,1950,3450},Uniform=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=.125}},Points=Array.Empty<BeamPointLoad>(),Analysis=analysis};
        return new BeamSolverReply {Main=new TowerLayout {LengthMm=3900,PositionsMm=member.SupportsMm,Joined=new[] {true,true},LeftReactions=analysis.Reactions,RightReactions=analysis.Reactions,
            StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StockLengthMm=3900,FirstSupport=0,LastSupport=2}}},
            Transfer=new TransferredSlabResult {RowsMm=new[] {150.0,3750},CaseNames=new[] {"Full"},PrimaryCases=new[] {new[] {member}},LowerCases=new[] {Array.Empty<TransferMember>()},UpperMembers=Array.Empty<TransferMember>()}},
            Counters=new long[Enum.GetValues(typeof(BeamSearchCounter)).Length]};
    }
    static BeamSolverReply RoundTrip(BeamSolverReply reply)
    {
        string path=Path.Combine(Path.GetTempPath(),"beam-deferred-scale-"+Guid.NewGuid().ToString("N")+".bin");
        try{BeamWire.Write(path,reply);return BeamWire.Read<BeamSolverReply>(path);}finally{File.Delete(path);}
    }
    static BeamSolverRequest Request()=>new BeamSolverRequest {Main=new TowerInput {TransferredSlab=new TransferredSlabInput()}};
    static void Wire()
    {
        var unit=Unit();var view=unit.ScaleForCheck(.125).ScaleForCheck(.375);var expected=unit.Scale(.125).Scale(.375);
        var reply=RoundTrip(Reply(view));var read=reply.Main.Transfer.PrimaryCases[0][0].Analysis;
        Check(Elements(read)==0 && Field(read,"scaledSource")!=null,"wire preserves a deferred reference graph without dropping its source");
        BeamSolverSession.Validate(reply,Request());Queries(expected,read);
        var materialized=RoundTrip(reply);BeamSolverSession.Validate(materialized,Request());Queries(expected,materialized.Main.Transfer.PrimaryCases[0][0].Analysis);
        foreach(string corrupt in new[] {"self","two-cycle","incomplete","negative","nan","partial","missing","positive-factor","different-source"})
        {
            var invalid=unit.ScaleForCheck(.125);
            if(corrupt=="self")Set(invalid,"scaledSource",invalid);
            if(corrupt=="two-cycle"){var other=invalid.ScaleForCheck(.375);Set(invalid,"scaledSource",other);}
            if(corrupt=="incomplete")Set(invalid,"scaledSource",new LoadedBeamAnalysis());
            if(corrupt=="negative")Set(invalid,"scaledFactor",-1.0);
            if(corrupt=="nan")Set(invalid,"scaledFactor",double.NaN);
            if(corrupt=="partial")((IList)Field(invalid,"elements")).Add(((IList)Field(unit,"elements"))[0]);
            if(corrupt=="missing")Set(invalid,"scaledSource",null);
            if(corrupt=="positive-factor")Set(invalid,"scaledFactor",.5);
            if(corrupt=="different-source")Set(invalid,"scaledSource",unit.Scale(.5));
            var decoded=RoundTrip(Reply(invalid));
            bool rejected=false;try{BeamSolverSession.Validate(decoded,Request());}catch(InvalidDataException){rejected=true;}
            Check(rejected,"worker validation rejects corrupt deferred source: "+corrupt);
        }
    }
    static void IntegrationAndCancellation()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var input=new TowerInput {PrimaryMaterial=material,DeflectionDenominator=270,LegCapacityKn=70};
            var shared=new TransferredSlabOptimizer.SearchWorkCache();var matrices=new LoadedBeamAnalysis.MatrixCache();
            var supports=new[] {450.0,1950,3450};var loads=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=2.125}};
            var analysis=TransferredSlabOptimizer.AnalyzePrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),matrices,shared);
            Check((Elements(analysis)==0)==(material==PrimaryBeamMaterial.DoubleH20),"only the H20 uniform scalar path defers coefficients");
            var key=new TransferredSlabOptimizer.UniformShapeKey(3900,supports,input.Primary.EI,270);
            Check(shared.UniformShapes.TryGetValue(key,out var unit),"the original complete unit remains cached");Queries(unit.Scale(2.125),analysis);
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();input.Trace=new BeamSearchTrace("cancelled cached uniform scale",cancellation.Token);
                bool cancelled=false;try{TransferredSlabOptimizer.AnalyzePrimary(input,3900,supports,loads,Array.Empty<BeamPointLoad>(),matrices,shared);}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled,"cancellation stops a warm uniform response before publishing a new view");
            }
        }
    }
    static void AllocationAndColdConsumers()
    {
        var source=Unit();var eager=source.Scale(.375);
        foreach(var query in new Func<LoadedBeamAnalysis,double>[] {a=>a.Response(1234.5,false)[2],a=>a.Response(1234.5,true)[2],
            a=>a.DeflectionWithOffsetMm(.625),a=>a.DeflectionWithAffineOffsetMm(.625,-.375,450,3450),a=>a.WS10StrengthUtilization()})
        {
            var cold=source.ScaleForCheck(.375);Same(query(eager),query(cold),"cold coefficient-consumer IEEE bits");
            Check(Field(cold,"scaledSource")==null && Elements(cold)>0,"every coefficient consumer materializes a cold view");
        }
        var supports=Enumerable.Range(0,24).Select(i=>150+i*500.0).ToArray();
        var many=LoadedBeamAnalysis.Analyze(12000,supports,new[] {new BeamDistributedLoad {EndMm=12000,KnPerM=1}},Array.Empty<BeamPointLoad>(),899.7512,270);
        var retained=new LoadedBeamAnalysis[256];long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<retained.Length;i++)retained[i]=many.Scale(.375);
        long eagerBytes=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(retained);
        before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<retained.Length;i++)retained[i]=many.ScaleForCheck(.375);
        long lazyBytes=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(retained);
        Check(lazyBytes<eagerBytes/2,"scalar-only views avoid most allocation for a multi-element uniform screen");
        Check(retained.All(a=>Elements(a)==0),"allocation check does not hide deferred coefficient work");
        var partial=LoadedBeamAnalysis.AnalyzeCombinedCached(3900,new[] {450.0,1950,3450},new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=1}},Array.Empty<BeamPointLoad>(),899.7512,270,null,1,true,true);
        Check(!partial.DeflectionEvaluated,"partial-failure fixture stops before deflection");
        bool rejected=false;try{partial.ScaleForCheck(.375);}catch(ArgumentException){rejected=true;}
        Check(rejected && !partial.Scale(.375).DeflectionEvaluated,"deferred sources must be complete while public Scale retains partial-response behavior");
    }
    internal static void Run()
    {
        foreach(double factor in new[] {0.0,-0.0,.125,2.125,19.375})
        {
            var unit=Unit();var expected=unit.Scale(factor);var view=unit.ScaleForCheck(factor);
            Check(Elements(view)==0 && Elements(expected)>0,"scalar-only scaling allocates no coefficient elements");Scalars(expected,view);
            Check(Elements(view)==0,"scalar and reaction checks do not force materialization");Queries(expected,view);
        }
        var source=Unit();var nested=source.ScaleForCheck(.375).ScaleForCheck(2.125).ScaleForCheck(.625);
        Queries(source.Scale(.375).Scale(2.125).Scale(.625),nested);
        var publicSource=source.ScaleForCheck(.375);var publicResult=publicSource.Scale(2.125);
        Check(Field(publicResult,"scaledSource")==null && Elements(publicResult)>0,"public Scale remains eager on a deferred source");Queries(source.Scale(.375).Scale(2.125),publicResult);
        var concurrent=source.ScaleForCheck(.375).ScaleForCheck(2.125);var eager=source.Scale(.375).Scale(2.125);
        Parallel.For(0,16,i=>
        {
            var a=eager.Response(1500+i*25,i%2==0);var b=concurrent.Response(1500+i*25,i%2==0);
            for(int k=0;k<a.Length;k++)if(BitConverter.DoubleToInt64Bits(a[k])!=BitConverter.DoubleToInt64Bits(b[k]))throw new Exception("Concurrent materialization changed a coefficient");
        });Coefficients(eager,concurrent);
        foreach(double invalid in new[] {-1.0,double.NaN,double.PositiveInfinity})
        {bool rejected=false;try{source.ScaleForCheck(invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"invalid scale is rejected immediately");}
        Wire();IntegrationAndCancellation();AllocationAndColdConsumers();
        Console.WriteLine("PASS "+checks+" deferred beam scale checks: bitwise scalars/reactions/coefficients/queries, offsets, nested and concurrent scaling, wire graphs/corruption, H20 integration and cancellation");
    }
}
