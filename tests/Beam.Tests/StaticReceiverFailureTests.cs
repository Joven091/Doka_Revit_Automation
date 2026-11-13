using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class StaticReceiverFailureTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Static receiver failure: "+message);}
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Combine(string name,Func<TransferredSlabOptimizer.Geometry,double,bool> a,Func<TransferredSlabOptimizer.Geometry,double,bool> b)
        =>(Func<TransferredSlabOptimizer.Geometry,double,bool>)typeof(CrossBeamSupportOptimizer).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[] {a,b});
    static TowerInput Input(bool proven)
    {
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=3900,EngineerStationLimit=4,LegCapacityKn=70,
            StockLengthsMm=new[] {1250.0,1800,2450,3900},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,IgnoreUplift=true,
            Trace=new BeamSearchTrace("static receiver rejection regression"),
            TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=3,UpperSpacingMm=305,PlywoodSheetLengthMm=2440,
                FixedCoveredLengthMm=3900,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {3900.0}}}};
        input.TransferLowerAcceptance=probe=>true;
        input.TransferReceiverPreparation=probe=>proven ? CrossBeamSupportOptimizer.FailedReceiver : (g,e)=>false;
        return input;
    }
    static void NoSolution(TowerInput input)
    {
        var core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",BindingFlags.NonPublic|BindingFlags.Static);
        try{core.Invoke(null,new object[] {input,true,null});throw new Exception("Expected no feasible receiver");}
        catch(TargetInvocationException error)when(error.InnerException is TowerOptimizationException){}
    }
    internal static void Run()
    {
        var failed=CrossBeamSupportOptimizer.FailedReceiver;
        foreach(bool a in new[] {false,true})foreach(bool b in new[] {false,true})
        {
            Func<TransferredSlabOptimizer.Geometry,double,bool> first=a ? (g,e)=>g.Positions.Length>=3 : failed;
            Func<TransferredSlabOptimizer.Geometry,double,bool> second=b ? (g,e)=>e>=25 : failed;
            var either=Combine("EitherReceiver",first,second);var both=Combine("BothReceivers",first,second);
            Check(ReferenceEquals(either,failed)==(!a && !b),"OR retains a viable primary, secondary or narrow-shutter alternative");
            Check(ReferenceEquals(both,failed)==(!a || !b),"AND propagates a geometry-independent failure");
            foreach(int count in new[] {2,3})foreach(double edge in new[] {0.0,25})
            {
                var geometry=new TransferredSlabOptimizer.Geometry {Positions=new double[count]};
                Check(either(geometry,edge)==(first(geometry,edge)||second(geometry,edge)),"OR keeps geometry and offset decisions");
                Check(both(geometry,edge)==(first(geometry,edge)&&second(geometry,edge)),"AND keeps geometry and offset decisions");
            }
        }
        var dynamicFailure=Combine("EitherReceiver",(g,e)=>false,null);
        Check(!ReferenceEquals(dynamicFailure,failed),"an ordinary callback is never assumed to be a static failure");
        var actual=Input(true);var expected=Input(false);NoSolution(actual);NoSolution(expected);
        Check(actual.Trace.Count(BeamSearchCounter.TransferCandidates)==0,"a proven failed fixed transverse option enumerates no primary candidates");
        Check(expected.Trace.Count(BeamSearchCounter.TransferCandidates)>0,"the reference still evaluates geometry-dependent callbacks");
        Console.WriteLine("PASS "+checks+" static receiver failure checks: exact AND/OR alternatives and rejection before geometry search");
    }
}
