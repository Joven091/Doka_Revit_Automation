using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class IndividualRowGridCacheTests
{
    static int checks;
    const BindingFlags methods=BindingFlags.Static|BindingFlags.NonPublic;
    const BindingFlags fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly MethodInfo build=typeof(TransferredSlabOptimizer).GetMethod("BuildIndividualRowGrid",methods);
    static readonly MethodInfo original=typeof(TransferredSlabOptimizer).GetMethod("CreateIndividualRowGrid",methods);
    static readonly string[] doubles={"Rows","Tributaries","LeftScales","RightScales","LeftPhysicalScales","RightPhysicalScales","SharedSlabRowsMm","LeftSourceScales","RightSourceScales","SettingOutRows"};
    static readonly string[] integers={"SlabRowIndices","SlabSourceRowIndices","SettingOutDesignRows"};
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Individual row-grid cache: "+message);}
    static object Get(object grid,string name)=>grid.GetType().GetField(name,fields).GetValue(grid);
    static void Set(object grid,string name,object value)=>grid.GetType().GetField(name,fields).SetValue(grid,value);
    static object Call(MethodInfo method,TowerInput input,double covered=3900,double coverage=3900,double edge=50)
    {
        try{return method.Invoke(null,new object[] {input,covered,coverage,edge});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static string Fingerprint(object grid)
    {
        if(grid==null)return "null";
        return string.Join("|",doubles.Select(name=>name+":"+(Get(grid,name) is double[] a ? string.Join(",",a.Select(BitConverter.DoubleToInt64Bits)) : "null")))
            +"|"+string.Join("|",integers.Select(name=>name+":"+(Get(grid,name) is int[] a ? string.Join(",",a) : "null")))
            +"|"+Get(grid,"Adjusted")+"|"+Get(grid,"Key")+"|"+(Get(grid,"SourceAnalysisKey")??"null");
    }
    static TowerInput Input(bool coordinated=false,bool shared=true)
    {
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=4000,DeflectionDenominator=270,
            Trace=new BeamSearchTrace("individual row-grid reuse"),NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache(),
            TransferredSlab=new TransferredSlabInput {LocalReceivingRows=true,UpperSpacingMm=305,LeftSlabSpacingMm=305,RightSlabSpacingMm=488,
                LeftBayMm=2200,RightBayMm=2200,PlywoodSheetLengthMm=2440,IncludeMainBeamEndLoads=true}};
        var t=input.TransferredSlab;
        if(coordinated)
        {
            input.LengthMm=10600;t.UpperSpacingMm=350;t.LeftSlabSpacingMm=t.RightSlabSpacingMm=488;
            t.SharedRowsMm=new[] {215.0,2440,4880,5100,5130,5390,5640,5960,7320,9760,10385};
            t.LeftBaselineWidthsMm=t.SharedRowsMm.Select(x=>x==5100 ? 250.0 : 0).ToArray();
            t.RightBaselineWidthsMm=t.SharedRowsMm.Select(x=>x==5100 || x==5390 || x==5640 || x==5960 ? 0.0 : 250).ToArray();
        }
        else if(shared)
        {
            t.SharedRowsMm=PlywoodSpacing.SheetSupportRows(50,3950,305);
            t.LeftBaselineWidthsMm=PlywoodSpacing.RowTributaries(50,3950,t.SharedRowsMm);
            t.RightBaselineWidthsMm=t.LeftBaselineWidthsMm.Select(x=>x/2).ToArray();
        }
        if(t.SharedRowsMm!=null)
        {t.LeftEffectiveWidthsMm=(double[])t.LeftBaselineWidthsMm.Clone();t.RightEffectiveWidthsMm=(double[])t.RightBaselineWidthsMm.Clone();}
        return input;
    }
    static void Parity(TowerInput input,double covered=3900,double coverage=3900,double edge=50)
    {
        var expected=Call(original,input,covered,coverage,edge);
        var actual=Call(build,input,covered,coverage,edge);var hit=Call(build,input,covered,coverage,edge);
        Check(Fingerprint(actual)==Fingerprint(expected) && Fingerprint(hit)==Fingerprint(expected),"all numeric bits, source/global indices, adjustment flags and analysis strings match the uncached builder");
        if(actual!=null)Check(!ReferenceEquals(actual,hit),"each context receives its own mutable RowGrid wrapper");
    }
    static void KeyDependencies()
    {
        var input=Input();var before=new TransferredSlabOptimizer.IndividualGridKey(input,3900,3900,50);
        var same=new TransferredSlabOptimizer.IndividualGridKey(Input(),3900,3900,50);
        Check(before.Equals(same) && before.GetHashCode()==same.GetHashCode(),"independent inputs with equal consumed values share an exact key");
        var changes=new Action<TowerInput>[]
        {
            i=>i.LongitudinalStartMm=PortableTestRuntime.NextUp(0.0),i=>i.TransferredSlab.FixedRowStartMm=PortableTestRuntime.NextUp(50.0),
            i=>i.TransferredSlab.UpperSpacingMm=350,i=>i.TransferredSlab.LeftSlabSpacingMm=488,i=>i.TransferredSlab.RightSlabSpacingMm=305,
            i=>i.TransferredSlab.LeftBayMm=0,i=>i.TransferredSlab.RightBayMm=0,i=>i.TransferredSlab.LocalReceivingRows=false,
            i=>i.TransferredSlab.IncludeConcreteEndStrips=true,i=>i.TransferredSlab.IncludeMainBeamEndLoads=false,
            i=>i.TransferredSlab.SharedRowsMm[0]=PortableTestRuntime.NextUp(i.TransferredSlab.SharedRowsMm[0]),
            i=>Array.Reverse(i.TransferredSlab.SharedRowsMm),i=>i.TransferredSlab.SharedRowsMm=null,
            i=>i.TransferredSlab.LeftBaselineWidthsMm[0]=PortableTestRuntime.NextUp(i.TransferredSlab.LeftBaselineWidthsMm[0]),
            i=>i.TransferredSlab.RightBaselineWidthsMm[0]=PortableTestRuntime.NextUp(i.TransferredSlab.RightBaselineWidthsMm[0]),
            i=>i.TransferredSlab.LeftBaselineWidthsMm=null,i=>i.TransferredSlab.RightBaselineWidthsMm=null,
            i=>i.TransferredSlab.LeftEffectiveWidthsMm[0]=0,i=>i.TransferredSlab.RightEffectiveWidthsMm[0]=0,
            i=>i.TransferredSlab.LeftEffectiveWidthsMm=null,i=>i.TransferredSlab.RightEffectiveWidthsMm=null,
            i=>i.TransferredSlab.ReceivingRowsMm=new[] {PortableTestRuntime.NextUp(2440.0)},
            i=>i.TransferredSlab.ReceivingCrossings=new[] {new TransferCrossingBeamRow {PositionMm=2000,WidthMm=650}},
            i=>i.TransferredSlab.EngineerReceiverCount=8
        };
        foreach(var change in changes)
        {
            var candidate=Input();change(candidate);
            Check(!before.Equals(new TransferredSlabOptimizer.IndividualGridKey(candidate,3900,3900,50)),"every consumed scalar/array dependency retains its exact value and null/order distinction");
        }
        foreach(var values in new[] {new[] {PortableTestRuntime.NextUp(3900.0),3900,50},new[] {3900.0,PortableTestRuntime.NextUp(3900.0),50},new[] {3900.0,3900,PortableTestRuntime.NextUp(50.0)}})
            Check(!before.Equals(new TransferredSlabOptimizer.IndividualGridKey(input,values[0],values[1],values[2])),"coverage, row coverage and edge keep one-ULP distinctions");
        var positiveZero=Input();positiveZero.LongitudinalStartMm=0.0;
        var negativeZero=Input();negativeZero.LongitudinalStartMm=BitConverter.Int64BitsToDouble(long.MinValue);
        Check(!new TransferredSlabOptimizer.IndividualGridKey(positiveZero,3900,3900,50).Equals(new TransferredSlabOptimizer.IndividualGridKey(negativeZero,3900,3900,50)),"signed zeros retain exact arithmetic/string identity");
        input.TransferredSlab.SharedRowsMm[0]+=1;
        Check(before.Equals(same),"a completed key owns its snapshot rather than reading later input mutations");
        var ignored=Input();ignored.TransferredSlab.LeftPlan=new UpperTransferPlan {StockLengthMm=1800,NearFootMm=450,FarFootMm=475};
        ignored.TransferredSlab.RightPlan=new UpperTransferPlan {StockLengthMm=2450,NearFootMm=600,FarFootMm=650};
        ignored.PrimaryMaterial=PrimaryBeamMaterial.WS10;ignored.LegCapacityKn=123;ignored.AllowCustomH20Lengths=true;ignored.CustomH20Scope=CustomH20LengthScope.Both;
        Check(same.Equals(new TransferredSlabOptimizer.IndividualGridKey(ignored,3900,3900,50)),"upper stock/feet, material, capacity and custom permissions do not enter the pure row builder");
        var network=new TransferNetworkInput {Beams=Enumerable.Range(0,3).Select(_=>new TransferBeamRow {BottomElevationMm=-800}).ToArray(),
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1900,WidthMm=650,BottomElevationMm=-600,SupportMode=CrossBeamSupportMode.Automatic}}};
        var n=Input();n.TransferredSlab.EngineerNetwork=network;
        var eligible=new TransferredSlabOptimizer.IndividualGridKey(n,3900,3900,50);
        Check(!eligible.Equals(same),"eligible crossing rows are part of the cache contract");
        foreach(var change in new Action[] {()=>network.CrossingBeams[0].PositionMm+=1e-10,()=>network.CrossingBeams[0].WidthMm+=1e-10,
            ()=>network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers,()=>network.CrossingBeams[0].BottomElevationMm=-900,
            ()=>network.Beams[0].BottomElevationMm=-500,()=>n.TransferredSlab.EngineerBeam=2})
        {
            network.CrossingBeams[0].PositionMm=1900;network.CrossingBeams[0].WidthMm=650;network.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;
            network.CrossingBeams[0].BottomElevationMm=-600;network.Beams[0].BottomElevationMm=-800;n.TransferredSlab.EngineerBeam=0;
            change();Check(!eligible.Equals(new TransferredSlabOptimizer.IndividualGridKey(n,3900,3900,50)),"current adjacency, elevation, support mode and crossing dimensions resolve the actual eligible family");
        }
        n.TransferredSlab.EngineerBeam=0;network.Beams[0].BottomElevationMm=-800;
        network.CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1900,WidthMm=650,BottomElevationMm=-600,SupportMode=CrossBeamSupportMode.Automatic},
            new TransferCrossingBeamRow {BayNumber=1,PositionMm=1950,WidthMm=650,BottomElevationMm=-600,SupportMode=CrossBeamSupportMode.Automatic}};
        var ordered=new TransferredSlabOptimizer.IndividualGridKey(n,3900,3900,50);Array.Reverse(network.CrossingBeams);
        Check(!ordered.Equals(new TransferredSlabOptimizer.IndividualGridKey(n,3900,3900,50)),"eligible crossing order is retained before order-sensitive80mm suppression");
        foreach(int count in new[] {2,3,8})foreach(var mode in new[] {CrossBeamSupportMode.Automatic,CrossBeamSupportMode.DedicatedTowers})
        {
            n.TransferredSlab.EngineerReceiverCount=count;network.CrossingBeams[0].SupportMode=mode;
            n.TransferredSlab.ReceivingCrossings=network.CrossingBeams;Parity(n);
        }
    }
    static void IsolationAndFailures()
    {
        var input=Input();var owner=input.NetworkNumerics;var first=Call(build,input);Check(first!=null,"ordinary source grid is valid");string expected=Fingerprint(first);
        var second=Call(build,input);
        foreach(string name in doubles.Concat(integers))
        {
            var a=Get(first,name) as Array;var b=Get(second,name) as Array;
            if(a==null)continue;
            Check(!ReferenceEquals(a,b),"cached contexts own separate "+name+" arrays");
            if(a is double[] x && x.Length>0)x[0]+=1;
            if(a is int[] map && map.Length>0)map[0]++;
            if(b is double[] y && y.Length>0)y[0]+=2;
            if(b is int[] other && other.Length>0)other[0]+=2;
        }
        ((IDictionary)Get(second,"JointLayouts")).Add("poison",second);
        second.GetType().GetMethod("Slice",fields).Invoke(second,new object[] {new TowerStockPiece {StockLengthMm=3900}});
        var clean=Input();clean.NetworkNumerics=owner;var third=Call(build,clean);
        Check(Fingerprint(third)==expected,"mutating either returned context and its public source arrays cannot poison the immutable snapshot");
        Check(((IDictionary)Get(third,"JointLayouts")).Count==0 && ((IDictionary)Get(third,"Slices")).Count==0 && Get(third,"JointContract")==null && Get(third,"StockGrids")==null && Get(third,"Quantities")==null,"joint/slice/quantity memos start private and empty on every cache hit");
        Check(clean.Trace.Count(BeamSearchCounter.IndividualRowGridCacheHits)==1,"a complete hit is observable without retaining outer owners");
        var bad=Input();bad.TransferredSlab.SharedRowsMm=new[] {100.0,100+1e-8};
        bad.TransferredSlab.LeftBaselineWidthsMm=bad.TransferredSlab.LeftEffectiveWidthsMm=new[] {250.0,250};
        bad.TransferredSlab.RightBaselineWidthsMm=bad.TransferredSlab.RightEffectiveWidthsMm=new[] {0.0,0};bad.TransferredSlab.LocalReceivingRows=false;
        foreach(string scope in new[] {"first scope · ","second scope · "})
        {
            bad.DiagnosticScope=scope;bad.Diagnostics=new TowerSearchDiagnostics(270);
            Check(Call(build,bad)==null && bad.NetworkNumerics.IndividualRows.Count==0,"complete null failures do not enter the positive cache");
            Check(bad.Diagnostics.Exception("test").Details.Contains(scope),"failure diagnostics replay in the current receiving scope");
        }
        var fault=Input();fault.TransferredSlab.LeftEffectiveWidthsMm=Array.Empty<double>();
        try{Call(build,fault);throw new Exception("Expected malformed source fault");}catch(IndexOutOfRangeException){}
        Check(fault.NetworkNumerics.IndividualRows.Count==0,"unexpected source faults do not publish provisional cache entries");
        var recovered=Input();recovered.NetworkNumerics=fault.NetworkNumerics;
        Check(Call(build,recovered)!=null && recovered.NetworkNumerics.IndividualRows.Count==1,"a later valid context retries normally after a source fault");
        using(var cancellation=new CancellationTokenSource())
        {
            recovered.Trace=new BeamSearchTrace("cancelled cached row grid",cancellation.Token);cancellation.Cancel();
            try{Call(build,recovered);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            Check(recovered.NetworkNumerics.IndividualRows.Count==1,"a completed cache hit cannot bypass cancellation or alter saved data");
        }
        foreach(int gate in Enumerable.Range(0,4))
        {
            var guarded=Input();var cache=guarded.NetworkNumerics;
            if(gate==0)guarded.ReuseSearchWork=false;
            if(gate==1)guarded.NetworkNumerics=null;
            if(gate==2)guarded.Trace=new BeamSearchTrace("timed original row-grid path",default,TimeSpan.FromSeconds(1));
            if(gate==3){guarded.TransferredSlab.SharedRowsMm=null;guarded.TransferredSlab.LeftSlabSpacingMm=guarded.TransferredSlab.RightSlabSpacingMm=0;}
            Check(Fingerprint(Call(build,guarded))==Fingerprint(Call(original,guarded)) && cache.IndividualRows.Count==0,"disabled, cacheless, timed and non-individual contexts keep the original path");
        }
        var fallback=Input();fallback.TransferredSlab.SharedSearchWork=fallback.NetworkNumerics;fallback.NetworkNumerics=null;
        Call(build,fallback);Call(build,fallback);Check(fallback.Trace.Count(BeamSearchCounter.IndividualRowGridCacheHits)==1,"the existing per-worker transferred numerical owner also supplies row reuse");
        var bounded=Input();Call(build,bounded);var key=new TransferredSlabOptimizer.IndividualGridKey(bounded,3900,3900,50);
        bounded.NetworkNumerics.IndividualRows.TryGetValue(key,out var payload);
        for(int i=0;i<128;i++)bounded.NetworkNumerics.IndividualRows.Store(new TransferredSlabOptimizer.IndividualGridKey(bounded,3900,3900,i+50.25),payload);
        Check(bounded.NetworkNumerics.IndividualRows.Count==128 && !bounded.NetworkNumerics.IndividualRows.TryGetValue(key,out _),"the worker's positive grid history is bounded at128 entries");
        var parallelOwner=new TransferredSlabOptimizer.SearchWorkCache();
        Parallel.For(0,16,_=>{var value=Input();value.NetworkNumerics=parallelOwner;if(Fingerprint(Call(build,value))!=expected)throw new Exception("Concurrent grid mismatch");});
        Check(parallelOwner.IndividualRows.Count==1,"concurrent completed publication cannot expose mutable context state");
    }
    static void FullSearchParity()
    {
        var generated=typeof(ExhaustedStationReuseTests).GetMethod("Generated",methods);
        var core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",methods);
        var fingerprint=typeof(ExhaustedStationReuseTests).GetMethod("Fingerprint",methods);
        foreach(double width in new[] {1524.0,1000.0})
        {
            var stocks=width==1524 ? new[] {3900.0} : new[] {3900.0,3900};
            Func<TowerInput> make=()=>
            {
                var value=(TowerInput)generated.Invoke(null,new object[] {stocks,width});value.EngineerStationLimit=6;
                var t=value.TransferredSlab;t.SharedRowsMm=PlywoodSpacing.SheetSupportRows(0,value.LengthMm,305);
                t.LeftBaselineWidthsMm=t.LeftEffectiveWidthsMm=new double[t.SharedRowsMm.Length];
                t.RightBaselineWidthsMm=t.RightEffectiveWidthsMm=PlywoodSpacing.RowTributaries(0,value.LengthMm,t.SharedRowsMm);
                t.RightSlabSpacingMm=305;t.RightBayMm=2200;t.RightAreaLoadKnM2=6.9;t.LocalReceivingRows=true;
                t.RightPlan=new UpperTransferPlan {StockLengthMm=1800,EdgeMm=200,NearFootMm=450,FarFootMm=450};
                var n=new TransferNetworkInput {Beams=Enumerable.Range(0,2).Select(_=>new TransferBeamRow {WidthMm=400,DepthMm=200}).ToArray(),Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}},UpperSpacingMm=305};
                value.EngineerPlan=EngineerPrimaryPlan.Create(value,n,0,false,true);value.EngineerPlan.InitialLegs=3;
                t.SharedSearchWork=value.NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache();return value;
            };
            var actual=make();var reference=make();Call(build,actual,actual.LengthMm,actual.LengthMm,0);
            var a=new List<string>();var b=new List<string>();
            actual.TransferLayoutAcceptance=l=>{a.Add((string)fingerprint.Invoke(null,new object[] {l}));return l.PositionsMm.Length>=5;};
            reference.TransferLayoutAcceptance=l=>{b.Add((string)fingerprint.Invoke(null,new object[] {l}));return l.PositionsMm.Length>=5;};
            var selected=(TowerLayout)core.Invoke(null,new object[] {actual,true,null});var expected=(TowerLayout)core.Invoke(null,new object[] {reference,true,null});
            Check((string)fingerprint.Invoke(null,new object[] {selected})==(string)fingerprint.Invoke(null,new object[] {expected}),"warm-grid and original cold-grid searches select identical geometry, stock, forces and full member analyses");
            Check(a.Count>0 && a.SequenceEqual(b),"complete receiving callbacks retain their original order and values");
            Check(actual.Trace.Count(BeamSearchCounter.IndividualRowGridCacheHits)>0 && reference.Trace.Count(BeamSearchCounter.IndividualRowGridCacheHits)==0,"full search parity exercises an actual cache hit against a cold builder");
        }
    }
    internal static void Run()
    {
        KeyDependencies();
        foreach(bool shared in new[] {false,true})foreach(bool local in new[] {false,true})
        foreach(bool concrete in new[] {false,true})foreach(bool beam in new[] {false,true})
        foreach(double edge in new[] {PortableTestRuntime.NextDown(50.0),50.0,PortableTestRuntime.NextUp(50.0)})
        {
            var value=Input(false,shared);value.TransferredSlab.LocalReceivingRows=local;
            value.TransferredSlab.IncludeConcreteEndStrips=concrete;value.TransferredSlab.IncludeMainBeamEndLoads=beam;Parity(value,3900,3900,edge);
        }
        var coordinated=Input(true);Parity(coordinated,10500,10500,50);
        Check((bool)Get(Call(build,coordinated,10500,10500,50),"Adjusted"),"the cached path includes real opposite-side row coordination and source mapping");
        var unusedKey=new TransferredSlabOptimizer.IndividualGridKey(coordinated,10500,10500,50);
        coordinated.TransferredSlab.SharedRowsMm[5]=PortableTestRuntime.NextUp(coordinated.TransferredSlab.SharedRowsMm[5]);
        Check(!unusedKey.Equals(new TransferredSlabOptimizer.IndividualGridKey(coordinated,10500,10500,50)),"even an inactive remote source coordinate remains part of the global ordinal contract");
        Parity(coordinated,10500,10500,50);
        const double parentLength=6099.999812417978,beamLength=6099.999812417687,covered=5950;
        var reported=Input();reported.LengthMm=beamLength;var t=reported.TransferredSlab;
        t.FixedRowStartMm=(parentLength-covered)/2;t.UpperSpacingMm=t.LeftSlabSpacingMm=t.RightSlabSpacingMm=250;
        t.SharedRowsMm=PlywoodSpacing.SheetSupportRows(t.FixedRowStartMm.Value,t.FixedRowStartMm.Value+covered,250);
        t.LeftBaselineWidthsMm=t.LeftEffectiveWidthsMm=PlywoodSpacing.RowTributaries(t.FixedRowStartMm.Value,t.FixedRowStartMm.Value+covered,t.SharedRowsMm);
        t.RightBaselineWidthsMm=t.RightEffectiveWidthsMm=new double[t.SharedRowsMm.Length];
        Parity(reported,covered,covered,(beamLength-covered)/2);
        IsolationAndFailures();FullSearchParity();
        Console.WriteLine("PASS "+checks+" individual row-grid cache checks: exact source/key bits, dependency changes, private arrays/memos, positive-only scope diagnostics, bounded/concurrent ownership and complete search/callback parity");
    }
}
