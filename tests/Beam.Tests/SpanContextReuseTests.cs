using System;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class SpanContextReuseTests
{
    const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public;
    static readonly Type crossType=typeof(TransferredSlabOptimizer).GetNestedType("CrossOption",BindingFlags.NonPublic);
    static readonly MethodInfo screen=typeof(TransferredSlabOptimizer).GetMethod("PreparedSpanScreen",BindingFlags.NonPublic|BindingFlags.Static);
    static object Field(object value,string name)=>value.GetType().GetField(name,hidden).GetValue(value);
    static void Set(object value,string name,object field)=>value.GetType().GetField(name,hidden).SetValue(value,field);
    static object Cross(double boundary=500)
    {
        var cross=Activator.CreateInstance(crossType,true);Set(cross,"SpanPrepared",true);Set(cross,"MinimumLineLoads",new[] {4.0,12});
        Set(cross,"UnknownSpans",new[] {new BeamDistributedLoad {StartMm=boundary,EndMm=900},new BeamDistributedLoad {StartMm=2250,EndMm=2800}});return cross;
    }
    static bool Screen(TowerInput input,object cross,TransferredSlabOptimizer.Geometry geometry,double edge)
    {
        try{return (bool)screen.Invoke(null,new object[] {input,geometry,cross,edge,null});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void Check(bool passed,string message){if(!passed)throw new Exception("Span context reuse: "+message);}
    internal static void Run()
    {
        var geometry=new TransferredSlabOptimizer.Geometry {Stock=new[] {new TowerStockPiece {StockLengthMm=4000},new TowerStockPiece {StartMm=4000,StockLengthMm=3000}},
            MemberSupports=new[] {new[] {300.0,1800,3700},new[] {250.0,1250,2750}}};
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var input=new TowerInput {PrimaryMaterial=material};var cross=Cross();
            Screen(input,cross,geometry,0);var first=Field(cross,"SpanContext");
            Screen(input,cross,geometry,25);var second=Field(cross,"SpanContext");
            for(int i=0;i<20;i++)
            {
                double edge=i%2*25;input.PrepareSpanGeometry=false;bool expected=Screen(input,cross,geometry,edge);
                input.PrepareSpanGeometry=true;Check(Screen(input,cross,geometry,edge)==expected,"alternating exact edges preserve original span/tail decisions");
                Check(ReferenceEquals(Field(cross,"SpanContext"),i%2==0 ? first : second),"alternating edges reuse their immutable boundary snapshot");
            }
            Screen(input,cross,geometry,1e-9);Check(!ReferenceEquals(Field(cross,"SpanContext"),first),"unrounded edges retain distinct contexts");
            for(int edge=1;edge<=8;edge++)Screen(input,cross,geometry,edge);
            var cache=Field(cross,"SpanContexts");Check((int)cache.GetType().GetProperty("Count",hidden).GetValue(cache)==8,"history retains at most eight edges");
            Screen(input,cross,geometry,0);Check(!ReferenceEquals(Field(cross,"SpanContext"),first),"eviction rebuilds only the same exact boundary context");
            var different=Cross(500.000000001);Screen(input,different,geometry,0);
            Check(!ReferenceEquals(Field(cross,"SpanContext"),Field(different,"SpanContext")),"another footprint scope owns its own edge history");
            var previous=Field(cross,"SpanContext");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();input.Trace=new BeamSearchTrace("cancelled span context",cancellation.Token);
                try{Screen(input,cross,geometry,100);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
                Check(ReferenceEquals(previous,Field(cross,"SpanContext")),"interrupted context preparation does not replace its last completed edge");
            }
        }
        Console.WriteLine("PASS alternating span context reuse: exact decisions, immutable context identity, eight-edge eviction, precise scopes and cancellation");
    }
}
