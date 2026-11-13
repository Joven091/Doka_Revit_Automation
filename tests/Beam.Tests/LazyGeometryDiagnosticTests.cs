using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class LazyGeometryDiagnosticTests
{
    static readonly MethodInfo compare=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.NonPublic|BindingFlags.Static);
    static int checks,formatted;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception("Lazy geometry diagnostics: "+reason);}
    static void Equal(TowerSearchDiagnostics expected,TowerSearchDiagnostics actual,BeamSearchTrace a=null,BeamSearchTrace b=null)
    {
        try{compare.Invoke(null,new object[]{expected,actual});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        Check(expected.Exception("failure").Message==actual.Exception("failure").Message,"first retained summary and failure order");
        Check(expected.SeparateBayExplanation(0)==actual.SeparateBayExplanation(0),"scoped receiving/slab explanation");
        if(a!=null)Check(a.Count(BeamSearchCounter.RejectionChecks)==b.Count(BeamSearchCounter.RejectionChecks),"exact recorded rejection multiplicity");
    }
    static void Add(TowerSearchDiagnostics diagnostics,string key,double score,int visit,bool lazy)
    {
        if(lazy && diagnostics.GeometryRepeated(key,score))return;
        formatted++;
        diagnostics.Geometry(key,$"support row x={1250.123+visit:0.###} mm; stock {2450.0+visit:0.###} mm", "advice "+visit,score,visit%2==0 ? "summary "+visit : null,visit%3==0);
    }
    static void Streams()
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            foreach(var culture in new[]{CultureInfo.InvariantCulture,new CultureInfo("de-DE"),new CultureInfo("fr-FR")})
            foreach(double first in new[]{0.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            {
                CultureInfo.CurrentCulture=culture;
                var x=new BeamSearchTrace("eager geometry diagnostics");var y=new BeamSearchTrace("lazy geometry diagnostics");
                var old=new TowerSearchDiagnostics(270,trace:x);var actual=new TowerSearchDiagnostics(270,trace:y);
                var scores=new[]{first,40.0,40.0,30.0,35.0,0.0,-0.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.NegativeInfinity};
                for(int i=0;i<scores.Length*3;i++)
                {
                    string key=i%3==0 ? "B1 · row/joint" : i%3==1 ? "B2 · tower geometry" : "S1 slab geometry";
                    Add(old,key,scores[i%scores.Length],i,false);Add(actual,key,scores[i%scores.Length],i,true);Equal(old,actual,x,y);
                }
                var a=old.Fork();var b=actual.Fork();
                for(int i=0;i<8;i++){Add(a,"B1 · row/joint",20-i,i,false);Add(b,"B1 · row/joint",20-i,i,true);Equal(a,b);}
                old.Merge(a);actual.Merge(b);Equal(old,actual,x,y);
            }
        }
        finally{CultureInfo.CurrentCulture=previous;}
    }
    static void DiscardedFormatting()
    {
        var old=new TowerSearchDiagnostics(270);var actual=new TowerSearchDiagnostics(270);
        formatted=0;for(int i=0;i<256;i++)Add(old,"B1 row/joint",40,i,false);int eager=formatted;
        formatted=0;for(int i=0;i<256;i++)Add(actual,"B1 row/joint",40,i,true);int lazy=formatted;
        Check(eager==256 && lazy==1,"only the retained first example is formatted for tied failures");Equal(old,actual);
        Add(old,"B1 row/joint",20,300,false);Add(actual,"B1 row/joint",20,300,true);Equal(old,actual);
    }
    static void Cancellation()
    {
        foreach(bool repeated in new[]{false,true})foreach(bool timed in new[]{false,true})
        using(var stop=new CancellationTokenSource())
        {
            var x=new BeamSearchTrace("cancel eager",stop.Token,searchTimeLimit:timed ? TimeSpan.FromHours(1) : (TimeSpan?)null);
            var y=new BeamSearchTrace("cancel lazy",stop.Token,searchTimeLimit:timed ? TimeSpan.FromHours(1) : (TimeSpan?)null);
            var old=new TowerSearchDiagnostics(270,trace:x);var actual=new TowerSearchDiagnostics(270,trace:y);
            if(repeated){Add(old,"B1 row/joint",40,0,false);Add(actual,"B1 row/joint",40,0,true);}
            stop.Cancel();Exception a=null,b=null;
            try{Add(old,"B1 row/joint",40,1,false);}catch(Exception error){a=error;}
            try{Add(actual,"B1 row/joint",40,1,true);}catch(Exception error){b=error;}
            Check(a is OperationCanceledException && b is OperationCanceledException,"same cancellation on new and repeated events");
            Equal(old,actual,x,y);
        }
    }
    internal static void Run(){Streams();DiscardedFormatting();Cancellation();Console.WriteLine("PASS "+checks+" lazy geometry diagnostic checks: exact retained failures/counts/scores/cultures/forks/merge/cancellation, discarded formatting avoided");}
}
