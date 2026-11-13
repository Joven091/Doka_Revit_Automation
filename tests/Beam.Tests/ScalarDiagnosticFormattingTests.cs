using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class ScalarDiagnosticFormattingTests
{
    const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly MethodInfo compare=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",hidden);
    static int checks,sequences;
    static void Assert(bool value,string text){checks++;if(!value)throw new Exception("Scalar diagnostic formatting: "+text);}
    // Frozen old AnalysisScalars behavior: build details before the unchanged
    // public Limit calls, with exactly the original arithmetic and field order.
    static void Reference(TowerSearchDiagnostics diag,int denominator,double moment,double shear,double deflectionUse,double length,double minimum,bool evaluated,int count,string scope,int stage,string location,bool lazy)
    {
        string advice=stage==1 ? "Review upper spacing, stock length and foot positions." : stage==2 ? "Review upper row spacing, lower secondary stock and foot/primary bearing positions." : "Review primary stock lengths and tower support spacing.";
        diag.Limit(scope,"moment",stage,moment*1,TowerOptimizer.PairMoment*count/2,"kNm",location,advice);
        diag.Limit(scope,"shear",stage,shear*1,TowerOptimizer.PairShear*count/2,"kN",location,advice);
        double allowable=length/denominator;
        if(evaluated)
        {
            double demand=deflectionUse*allowable*1;
            string details=!lazy || demand>allowable+allowable*1e-8 ? location+$" · governing span/overhang {length:0.###} mm (L/{denominator})" : "";
            diag.Limit(scope,"deflection",stage,demand,allowable,"mm",details,advice,allowable*1e-8);
        }
        diag.Bearing(scope,stage,minimum*1,location);
    }
    static void Equal(TowerSearchDiagnostics a,TowerSearchDiagnostics b)
    {
        try{compare.Invoke(null,new object[]{a,b});}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        Assert(true,"all private failure facts, score IEEE bits, multiplicities and full report text match");
    }
    static void Sequences()
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            foreach(var culture in new[]{CultureInfo.InvariantCulture,new CultureInfo("de-DE"),new CultureInfo("fr-FR")})
            foreach(int denominator in new[]{0,270,500})foreach(bool lazy in new[]{false,true})foreach(bool ignore in new[]{false,true})
            {
                CultureInfo.CurrentCulture=culture;var x=new BeamSearchTrace("scalar diagnostics parity");var y=new BeamSearchTrace("scalar diagnostics parity");
                var old=new TowerSearchDiagnostics(denominator,trace:x,ignoreUplift:ignore){LazyLocations=lazy};var actual=new TowerSearchDiagnostics(denominator,trace:y,ignoreUplift:ignore){LazyLocations=lazy};
                var uses=new[]{0.0,-0.0,1.0,PortableTestRuntime.NextDown(1.0+1e-8),PortableTestRuntime.NextUp(1.0+1e-8),5.0,6.0,5.0,4.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1.0};
                var lengths=new[]{0.0,1000,1000.0001,double.NaN,double.PositiveInfinity,double.NegativeInfinity};
                for(int i=0;i<78;i++)
                {
                    double use=uses[i%uses.Length],length=lengths[i%lengths.Length],moment=i%7==0 ? double.NaN : i%3==0 ? 12 : 0,shear=i%11==0 ? double.PositiveInfinity : i%3==1 ? 24 : 0;
                    double minimum=i%5==0 ? -1 : i%6==0 ? double.NaN : 1;bool evaluated=i%9!=0;int count=i%4==0 ? 1 : 2,stage=1+i%3;string scope=i%2==0 ? "P1 L" : "P1 R",location="X2 dedicated primary UDL + end strips";
                    Reference(old,denominator,moment,shear,use,length,minimum,evaluated,count,scope,stage,location,lazy);
                    actual.AnalysisScalars(moment,shear,use,length,minimum,evaluated,count,scope,stage,location);Equal(old,actual);
                    foreach(var counter in new[]{BeamSearchCounter.NumericalChecks,BeamSearchCounter.RejectionChecks})Assert(x.Count(counter)==y.Count(counter),"trace counter and first/equal/worse/improving-failure provenance is identical");
                }
                sequences++;
            }
        }
        finally{CultureInfo.CurrentCulture=previous;}
    }
    static void Allocation()
    {
        var old=new TowerSearchDiagnostics(270,ignoreUplift:true){LazyLocations=true};var actual=new TowerSearchDiagnostics(270,ignoreUplift:true){LazyLocations=true};
        Reference(old,270,0,0,2,1234.56789,1,true,2,"P1 L",2,"X1 dedicated primary UDL + end strips",true);
        actual.AnalysisScalars(0,0,2,1234.56789,1,true,2,"P1 L",2,"X1 dedicated primary UDL + end strips");
        const int visits=512;long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++)Reference(old,270,0,0,2,1234.56789,1,true,2,"P1 L",2,"X1 dedicated primary UDL + end strips",true);long oldBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++)actual.AnalysisScalars(0,0,2,1234.56789,1,true,2,"P1 L",2,"X1 dedicated primary UDL + end strips");long newBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Equal(old,actual);Console.WriteLine("SCALAR_DIAGNOSTIC_ALLOCATIONS visits="+visits+" oldEagerBytes="+oldBytes+" lazyBytes="+newBytes);
        Assert(oldBytes-newBytes>=visits*32,"repeated failure annotations avoid floating-format/string allocation while preserving every recorded fact");
    }
    static void Cancellation()
    {
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();var a=new BeamSearchTrace("scalar diagnostics cancellation",cancel.Token);var b=new BeamSearchTrace("scalar diagnostics cancellation",cancel.Token);
            var x=new TowerSearchDiagnostics(270,trace:a);var y=new TowerSearchDiagnostics(270,trace:b);Exception oldError=null,newError=null;
            try{Reference(x,270,12,24,2,1000,-1,true,2,"P1 L",2,"same",true);}catch(Exception e){oldError=e;}
            try{y.AnalysisScalars(12,24,2,1000,-1,true,2,"P1 L",2,"same");}catch(Exception e){newError=e;}
            Assert((oldError==null)==(newError==null) && (oldError==null || oldError.GetType()==newError.GetType()),"original cancellation checkpoints remain at the same counter operations");Equal(x,y);
        }
    }
    internal static void Run(){Sequences();Allocation();Cancellation();Console.WriteLine("PASS "+checks+" scalar diagnostic formatting checks across "+sequences+" criterion/culture/lazy/uplift sequences; exact reports/counters/NaN/Inf/repeated improvements and allocations");}
}
