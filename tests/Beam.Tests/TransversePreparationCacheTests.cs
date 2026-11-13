using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class TransversePreparationCacheTests
{
    const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly MethodInfo options=typeof(TransferredSlabOptimizer).GetMethod("CrossOptions",hidden);
    static readonly MethodInfo grid=typeof(TransferredSlabOptimizer).GetMethod("BuildRowGrid",hidden);
    static readonly MethodInfo accept=typeof(TransferredSlabOptimizer).GetMethod("AcceptTransverseCases",hidden);
    static object Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(target);
    static void Check(bool passed,string message){if(!passed)throw new Exception("Transverse preparation cache: "+message);}
    static object Cross(TowerInput input)=>((IEnumerable)options.Invoke(null,new object[] {input,1524.0,305.0})).Cast<object>().First();
    static object Grid(TowerInput input,double edge)=>grid.Invoke(null,new object[] {input,3900-2*edge,3900-2*edge,0.0,13,0.0,edge});
    static bool Accept(TowerInput input,object cross,object rows,double edge)
    {
        try{return (bool)accept.Invoke(null,new object[] {input,cross,1524.0,rows,edge});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static TowerInput Input(bool reuse,int criterion=270,double load=3,bool longStock=false)=>new TowerInput
    {
        EngineerSearch=true,FullLoadingOnly=true,LengthMm=3900,LegCapacityKn=70,StockEndSetbackMm=150,DeflectionDenominator=criterion,
        StockLengthsMm=longStock ? new[] {1800.0,3900} : new[] {1800.0,2450,3900},IgnoreUplift=true,
        ReuseTransversePreparations=reuse,Trace=new BeamSearchTrace("alternating transverse preparation regression"),
        TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=load,UpperSpacingMm=305,PlywoodSheetLengthMm=2440}
    };
    static string Fingerprint(TowerLayout probe)
    {
        Func<double,string> number=x=>x.ToString("R",CultureInfo.InvariantCulture);
        return string.Join(",",probe.Transfer.RowsMm.Select(number))+"|"+string.Join(";",probe.Transfer.LowerCases.SelectMany(c=>c).Select(m=>
            string.Join(",",new[] {m.StartMm,m.LengthMm,m.Analysis.TotalLoadKn,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm,m.Analysis.DeflectionUtilization}.Select(number))
            +":"+string.Join(",",m.Analysis.Reactions.Select(number))+":"+string.Join(",",m.Uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm,q.KnPerM}).Select(number))));
    }
    static Tuple<string[],int,int> Alternating(bool reuse,int criterion,double load,bool longStock,bool rejectOne)
    {
        var input=Input(reuse,criterion,load,longStock);var cross=Cross(input);
        var rows=new[] {Grid(input,0),Grid(input,25)};
        double[] failedRows=((double[])Field(rows[1],"Rows")).Select(x=>x+25).ToArray();
        int lowerCalls=0,preparations=0,geometryCalls=0;var probes=new Dictionary<double,string>();
        input.TransferLowerAcceptance=probe=>{lowerCalls++;return !rejectOne || !probe.Transfer.RowsMm.SequenceEqual(failedRows);};
        input.TransferReceiverPreparation=probe=>
        {
            preparations++;double first=probe.Transfer.RowsMm[0];probes[first]=Fingerprint(probe);
            return (geometry,edge)=>{geometryCalls++;return geometry.Positions.Length>=3 && edge<30;};
        };
        var answers=new List<string>();
        for(int trial=0;trial<12;trial++)
        {
            int i=trial%2;double edge=i*25;bool passed=Accept(input,cross,rows[i],edge);
            var receiver=(Func<TransferredSlabOptimizer.Geometry,double,bool>)Field(cross,"PreparedReceiver");
            bool geometryPassed=passed && receiver!=null && receiver(new TransferredSlabOptimizer.Geometry {Positions=new double[2+trial%3]},edge);
            answers.Add(passed+"/"+geometryPassed+(passed ? "/"+probes[((double[])Field(rows[i],"Rows"))[0]+edge] : ""));
        }
        Check(geometryCalls==(rejectOne ? 6 : 12),"every accepted geometry still invokes its prepared callback");
        Check(!reuse || lowerCalls==2 && preparations==(rejectOne ? 1 : 2),"alternating exact grids retain passing and failed transverse evidence");
        return Tuple.Create(answers.ToArray(),lowerCalls,preparations);
    }
    internal static void Run()
    {
        foreach(int criterion in new[] {270,500})foreach(double load in new[] {3.0,4})foreach(bool longStock in new[] {false,true})foreach(bool rejectOne in new[] {false,true})
        {
            var reference=Alternating(false,criterion,load,longStock,rejectOne);var actual=Alternating(true,criterion,load,longStock,rejectOne);
            Check(actual.Item1.SequenceEqual(reference.Item1),"exact lower responses and geometry decisions match across criteria, loads and stock contexts");
            Check(actual.Item2<reference.Item2 && actual.Item3<reference.Item3,"alternation avoids repeated lower checks and resolved closure preparation");
        }
        var eviction=Input(true);var evictionCross=Cross(eviction);int count=0;eviction.TransferLowerAcceptance=probe=>{count++;return true;};
        var firstGrid=Grid(eviction,0);Check(Accept(eviction,evictionCross,firstGrid,0),"initial grid passes");
        for(int edge=1;edge<=8;edge++)Check(Accept(eviction,evictionCross,Grid(eviction,edge),edge),"eviction grid passes");
        var cache=Field(evictionCross,"TransversePreparations");
        Check((int)cache.GetType().GetProperty("Count",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cache)==8,"preparation history is bounded to eight grids");
        Check(Accept(eviction,evictionCross,firstGrid,0) && count==10,"an evicted grid receives its full check again");
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=Input(true);interrupted.Trace=new BeamSearchTrace("cancelled grid preparation",cancellation.Token);
            var interruptedCross=Cross(interrupted);var interruptedGrid=Grid(interrupted,0);bool cancelNow=true;int calls=0;
            interrupted.TransferLowerAcceptance=probe=>{calls++;if(cancelNow)cancellation.Cancel();return true;};
            try {Accept(interrupted,interruptedCross,interruptedGrid,0);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            Check(Field(interruptedCross,"TransversePreparations")==null && Field(interruptedCross,"TransverseKey")==null,"cancelled preparation commits neither history nor latest-grid evidence");
            cancelNow=false;interrupted.Trace=new BeamSearchTrace("retry interrupted grid");
            Check(Accept(interrupted,interruptedCross,interruptedGrid,0) && calls==2,"the same grid is fully checked after interruption");
        }
        Console.WriteLine("PASS alternating transverse preparation cache: exact response/decision parity, geometry callbacks, failures, eight-grid eviction and cancellation");
    }
}
