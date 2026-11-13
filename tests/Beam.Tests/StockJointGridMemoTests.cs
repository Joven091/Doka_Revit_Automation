using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class StockJointGridMemoTests
{
    static int checks;
    static readonly BindingFlags fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly Type gridType=typeof(TransferredSlabOptimizer).GetNestedType("RowGrid",BindingFlags.NonPublic);
    static readonly MethodInfo resolve=typeof(TransferredSlabOptimizer).GetMethod("ResolveStockGrid",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo repair=typeof(TransferredSlabOptimizer).GetMethod("GridForStocks",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo conflict=typeof(TransferredSlabOptimizer).GetMethod("ConflictingRow",BindingFlags.NonPublic|BindingFlags.Static);
    static object Get(object value,string field)=>gridType.GetField(field,fields).GetValue(value);
    static void Set(object value,string field,object content)=>gridType.GetField(field,fields).SetValue(value,content);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Stock/joint grid memo: "+message);}
    static object Call(MethodInfo method,object[] arguments)
    {
        try{return method.Invoke(null,arguments);}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static TowerInput Input(bool reuse=true)=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,ReuseSearchWork=reuse,
        Trace=new BeamSearchTrace("stock shape joint-grid memo regression"),TransferredSlab=new TransferredSlabInput {UpperSpacingMm=305}};
    static object Source(double maximum=305)
    {
        var grid=Activator.CreateInstance(gridType,true);var rows=PlywoodSpacing.SheetSupportRows(0,3900,maximum);
        Set(grid,"Rows",rows);Set(grid,"Tributaries",PlywoodSpacing.RowTributaries(0,3900,rows));
        foreach(string name in new[] {"LeftScales","RightScales","LeftPhysicalScales","RightPhysicalScales","LeftSourceScales","RightSourceScales"})
            Set(grid,name,rows.Select((x,i)=>.1+i*.03+(name.StartsWith("Left",StringComparison.Ordinal) ? .2 : .7)).ToArray());
        Set(grid,"SharedSlabRowsMm",(double[])rows.Clone());
        Set(grid,"SlabRowIndices",Enumerable.Range(0,rows.Length).ToArray());Set(grid,"SlabSourceRowIndices",Enumerable.Range(0,rows.Length).ToArray());
        Set(grid,"Key","exact source "+maximum.ToString("R",CultureInfo.InvariantCulture));return grid;
    }
    static TransferredSlabOptimizer.Geometry Geometry(double joint,bool alternateSupports=false)
        =>new TransferredSlabOptimizer.Geometry {Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=joint},new TowerStockPiece {StartMm=joint,StockLengthMm=3900-joint}},
            Positions=alternateSupports ? new[] {300.0,joint-300,joint+300,3600} : new[] {150.0,joint-150,joint+150,3750}};
    static object Resolve(TowerInput input,TransferredSlabOptimizer.Geometry geometry,object source,double edge,out double firstConflict)
    {
        object[] args={input,geometry,source,edge,0.0};var result=Call(resolve,args);firstConflict=(double)args[4];return result;
    }
    static object Reference(TowerInput input,TransferredSlabOptimizer.Geometry geometry,object source,double edge,out double firstConflict)
    {
        var rows=(double[])Get(source,"Rows");
        firstConflict=input.PrepareReceiverChecks ? (double)Call(conflict,new object[] {geometry,rows})
            : rows.FirstOrDefault(r=>geometry.Stock.Skip(1).Any(s=>Math.Abs(s.StartMm-r)<40));
        return firstConflict>0 ? Call(repair,new[] {(object)input,geometry,source,edge}) : source;
    }
    static string Fingerprint(object grid)
    {
        if(grid==null)return "null";
        return string.Join("|",new[] {"Rows","Tributaries","LeftScales","RightScales","LeftPhysicalScales","RightPhysicalScales","LeftSourceScales","RightSourceScales","SharedSlabRowsMm","SettingOutRows"}.Select(name=>
            name+":"+(Get(grid,name) is double[] values ? string.Join(",",values.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))) : "null")))
            +"|"+(Get(grid,"SettingOutDesignRows") is int[] map ? string.Join(",",map) : "null")+"|"+Get(grid,"Key");
    }
    static void Parity(TowerInput input,TransferredSlabOptimizer.Geometry geometry,object source,double edge)
    {
        var expectedSource=Source();
        var expected=Reference(input,geometry,expectedSource,edge,out double expectedConflict);
        var actual=Resolve(input,geometry,source,edge,out double actualConflict);
        Check(actualConflict==expectedConflict,"first diagnostic conflict is exact, including the original 40mm boundary");
        Check(Fingerprint(actual)==Fingerprint(expected),"repair rows, design/source maps, tributaries and diagnostic key match the original uncached caller");
    }
    internal static void Run()
    {
        var input=Input();var source=Source();var a=Geometry(1875);var b=Geometry(2000);
        var first=Resolve(input,a,source,0,out double initialConflict);Check(first!=null && initialConflict>0,"a real internal-joint conflict has a passing repaired layout");
        var equivalent=Resolve(input,Geometry(1875,true),source,0,out double equivalentConflict);
        Check(ReferenceEquals(first,equivalent) && initialConflict==equivalentConflict,"different primary support stations share the exact stock-shape repair");
        Resolve(input,b,source,0,out _);var again=Resolve(input,a,source,0,out double repeatedConflict);
        Check(ReferenceEquals(first,again) && repeatedConflict==initialConflict,"coverA/B/A restores the exact earlier answer and diagnostic conflict");
        Parity(input,a,source,0);Parity(input,b,source,0);
        var exactMemo=Get(source,"LastStockGrid");Parity(input,Geometry(2000+1e-10),source,0);
        Check(!ReferenceEquals(exactMemo,Get(source,"LastStockGrid")),"stock shape changes smaller than a practical setting-out tolerance retain exact cache identity");
        var noConflict=Geometry(1950);var original=Resolve(input,noConflict,source,0,out double zero);
        Check(ReferenceEquals(original,source) && zero==0,"a nonconflicting stock family keeps the exact original source grid");
        Check(ReferenceEquals(source,Resolve(input,Geometry(1950,true),source,0,out zero)) && zero==0,"no-conflict results are reused across support patterns");
        foreach(bool engineer in new[] {false,true})foreach(bool prepared in new[] {false,true})foreach(double edge in new[] {0.0,1e-9,125})
        {
            input.FullLoadingOnly=engineer;input.PrepareReceiverChecks=prepared;
            Parity(input,a,source,edge);Parity(input,noConflict,source,edge);
        }
        input.FullLoadingOnly=true;input.PrepareReceiverChecks=true;
        double row=((double[])Get(source,"Rows")).OrderBy(x=>Math.Abs(x-1875)).First();
        input.TransferredSlab.ReceivingCrossings=new[] {new TransferCrossingBeamRow {PositionMm=row,WidthMm=80}};
        var impossible=Resolve(input,a,source,0,out double blockedConflict);
        Check(impossible==null && blockedConflict==initialConflict,"a crossing-constrained fixed row can make the same stock-shape repair fail");
        Check(Resolve(input,Geometry(1875,true),source,0,out double blockedAgain)==null && blockedAgain==blockedConflict,"complete null repairs retain their exact first-conflict evidence");
        Parity(input,a,source,0);
        input.TransferredSlab.ReceivingCrossings[0].WidthMm=1000;Parity(input,a,source,0);
        input.TransferredSlab.ReceivingCrossings=Array.Empty<TransferCrossingBeamRow>();
        Check(Resolve(input,a,source,0,out _)!=null,"restoring the repair-input contract cannot reuse an earlier constrained null");
        input.TransferredSlab.UpperSpacingMm=488;Parity(input,a,source,0);input.TransferredSlab.UpperSpacingMm=305;
        var other=Source();var otherResult=Resolve(input,a,other,0,out _);
        Check(otherResult!=null && !ReferenceEquals(otherResult,first),"a different base RowGrid never inherits another grid's cached repair");
        for(int i=0;i<20;i++)Resolve(input,Geometry(1700+i*10),source,0,out _);
        Check(((Array)Get(source,"StockGrids")).Length==8,"the fast stock-shape cache stays bounded at eight entries");Parity(input,a,source,0);
        var shapeSnapshot=Geometry(1875);Resolve(input,shapeSnapshot,source,0,out _);shapeSnapshot.Stock[0].StockLengthMm=2000;shapeSnapshot.Stock[1].StartMm=2000;shapeSnapshot.Stock[1].StockLengthMm=1900;
        Parity(input,shapeSnapshot,source,0);
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=Input();interrupted.Trace=new BeamSearchTrace("cancelled stock-grid memo",cancellation.Token);var empty=Source();cancellation.Cancel();
            bool failed=false;try{Resolve(interrupted,a,empty,0,out _);}catch(OperationCanceledException){failed=true;}
            Check(failed && Get(empty,"LastStockGrid")==null && ((IDictionary)Get(empty,"JointLayouts")).Count==0,"cancellation before lookup cannot publish a positive or negative repair");
            interrupted.Trace=new BeamSearchTrace("retry stock-grid memo");Check(Resolve(interrupted,a,empty,0,out _)!=null,"an interrupted repair can be retried normally");
            interrupted.Trace=new BeamSearchTrace("cancelled existing stock-grid memo",cancellation.Token);
            failed=false;try{Resolve(interrupted,a,empty,0,out _);}catch(OperationCanceledException){failed=true;}
            Check(failed,"a complete cached repair cannot bypass current cancellation");
        }
        int ticks=0;
        var expired=Input();expired.Trace=new BeamSearchTrace("deadline before joint-grid publication",default,TimeSpan.FromSeconds(1),()=>++ticks>=4 ? 2*Stopwatch.Frequency : 0);
        var unfinished=Source();bool deadline=false;
        try{Resolve(expired,a,unfinished,0,out _);}catch(BeamSearchDeadlineException){deadline=true;}
        Check(deadline && Get(unfinished,"LastStockGrid")==null && ((IDictionary)Get(unfinished,"JointLayouts")).Count==0,"deadline after placement and before publication leaves no provisional null or incomplete result");
        expired.Trace=new BeamSearchTrace("retry deadline joint-grid placement");Check(Resolve(expired,a,unfinished,0,out _)!=null,"a placement interrupted before publication is recomputed on retry");
        var generated=typeof(ExhaustedStationReuseTests).GetMethod("Generated",BindingFlags.Static|BindingFlags.NonPublic);
        var fingerprint=typeof(ExhaustedStationReuseTests).GetMethod("Fingerprint",BindingFlags.Static|BindingFlags.NonPublic);
        var core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(double width in new[] {1000.0,1524})
        {
            var expected=(TowerInput)generated.Invoke(null,new object[] {new[] {4000.0,3800},width});expected.EngineerStationLimit=6;expected.PrepareReceiverChecks=false;
            var actual=(TowerInput)generated.Invoke(null,new object[] {new[] {4000.0,3800},width});actual.EngineerStationLimit=6;
            expected.StockLengthsMm=actual.StockLengthsMm=new[] {1250.0,1800,2450,3800,3900,4000};
            var expectedLayout=(TowerLayout)Call(core,new object[] {expected,true,null});var actualLayout=(TowerLayout)Call(core,new object[] {actual,true,null});
            Check((string)fingerprint.Invoke(null,new object[] {actualLayout})==(string)fingerprint.Invoke(null,new object[] {expectedLayout}),"complete two-stock receiver geometry, repaired physical rows, ranking and final member forces match the original caller path");
            Check(actualLayout.Transfer.SettingOutRowsMm!=null,"complete multi-stock parity exercises actual repaired physical bearer rows");
        }
        Console.WriteLine("PASS "+checks+" stock-shape joint-grid memo checks: exact conflict/repair/null parity, unchanged source maps, support-independent reuse, A/B/A, conditional contexts, eviction and cancellation");
    }
}
