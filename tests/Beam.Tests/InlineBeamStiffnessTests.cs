using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class InlineBeamStiffnessTests
{
    static int checks;
    static readonly Action<double[,],int,double,double> assemble=(Action<double[,],int,double,double>)typeof(LoadedBeamAnalysis)
        .GetMethod("AddBandedElement",BindingFlags.Static|BindingFlags.NonPublic).CreateDelegate(typeof(Action<double[,],int,double,double>));
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Inline beam stiffness: "+message);}
    static void Same(double a,double b,string message)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),message);
    static void OldElement(double[,] k,int at,double c,double l)
    {
        var ke=new double[,] {{12,6*l,-12,6*l},{6*l,4*l*l,-6*l,2*l*l},{-12,-6*l,12,-6*l},{6*l,2*l*l,-6*l,4*l*l}};
        for(int i=0;i<4;i++)for(int j=0;j<4;j++)k[at+i,j-i+3]+=c*ke[i,j];
    }
    static void Assembly()
    {
        var random=new Random(480116);
        for(int sample=0;sample<700;sample++)
        {
            var a=new double[16,7];var b=new double[16,7];
            for(int i=0;i<16;i++)for(int j=0;j<7;j++)a[i,j]=b[i,j]=(random.NextDouble()-.5)*Math.Pow(2,sample%100-50);
            foreach(int at in new[] {0,2,4,6,8,10,12})
            {
                double c=Math.Pow(2,sample%160-80)*(.5+random.NextDouble()),l=Math.Pow(2,sample%50-25)*(.5+random.NextDouble());
                if(sample<8){c=new[] {0.0,-0.0,double.Epsilon,-double.Epsilon,1.0,-1.0,double.MaxValue,double.MinValue}[sample];l=.125;}
                OldElement(a,at,c,l);assemble(b,at,c,l);
            }
            for(int i=0;i<16;i++)for(int j=0;j<7;j++)Same(a[i,j],b[i,j],"every accumulated band coefficient retains its IEEE bits");
        }
        var matrix=new double[8,7];assemble(matrix,0,1.25,.375);long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100;i++)assemble(matrix,0,1.25,.375);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"element assembly allocates no rectangular array");
    }
    static void Analysis(LoadedBeamAnalysis expected,LoadedBeamAnalysis actual,double length,double[] stations,bool complete=true)
    {
        foreach(var property in typeof(LoadedBeamAnalysis).GetProperties(BindingFlags.Instance|BindingFlags.Public))
        {
            if(property.PropertyType==typeof(double))Same((double)property.GetValue(expected),(double)property.GetValue(actual),"scalar "+property.Name);
            if(property.PropertyType==typeof(bool))Check((bool)property.GetValue(expected)==(bool)property.GetValue(actual),"completion flag");
        }
        Check(expected.Reactions.Length==actual.Reactions.Length,"reaction count");
        for(int i=0;i<expected.Reactions.Length;i++)Same(expected.Reactions[i],actual.Reactions[i],"reaction bits");
        var field=typeof(LoadedBeamAnalysis).GetField("elements",BindingFlags.Instance|BindingFlags.NonPublic);
        var a=(IList)field.GetValue(expected);var b=(IList)field.GetValue(actual);Check(a.Count==b.Count,"element count");
        for(int i=0;i<a.Count;i++)foreach(var coefficient in a[i].GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
            Same((double)coefficient.GetValue(a[i]),(double)coefficient.GetValue(b[i]),"complete coefficient "+coefficient.Name);
        foreach(double station in Enumerable.Range(0,17).Select(i=>length*i/16).Concat(stations))foreach(bool right in new[] {false,true})
        {
            var x=expected.Response(station,right);var y=actual.Response(station,right);
            for(int i=0;i<x.Length;i++)Same(x[i],y[i],"full force/displacement diagram bits");
        }
        if(complete && expected.DeflectionEvaluated)
        {
            Same(expected.DeflectionWithAffineOffsetMm(.625,-.375,stations.Min(),stations.Max()),actual.DeflectionWithAffineOffsetMm(.625,-.375,stations.Min(),stations.Max()),"affine-support displacement");
            Same(expected.WS10StrengthUtilization(),actual.WS10StrengthUtilization(),"full WS10 interaction");
        }
    }
    static void CompleteAndCached()
    {
        var random=new Random(159003);
        for(int sample=0;sample<60;sample++)
        {
            double length=new[] {1800.0,3900,10600}[sample%3];int count=2+sample%7,criterion=new[] {270,350,500}[sample%3];double ei=sample%2==0 ? TowerOptimizer.PairEI : 2200.125;
            var supports=Enumerable.Range(0,count).Select(i=>150+(length-300)*i/(count-1)).ToArray();
            var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=.125+random.NextDouble()*18},
                new BeamDistributedLoad {StartMm=length*.23,EndMm=length*.71,KnPerM=sample%2==0 ? -2.375 : 3.125}};
            var points=Enumerable.Range(0,5).Select(i=>new BeamPointLoad {PositionMm=length*(i+.37)/5,Kn=(sample%2==0 ? -.125 : .25)+i*.375}).ToArray();
            var expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,uniform,points,ei,criterion,allowSigned:true);
            var cache=new LoadedBeamAnalysis.MatrixCache();
            var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,uniform,points,ei,criterion,cache,allowSignedLoads:true);Analysis(expected,actual,length,supports);
            var changed=uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*.625}).ToArray();
            expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,changed,points,ei,criterion,allowSigned:true);
            actual=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,changed,points,ei,criterion,cache,allowSignedLoads:true);Analysis(expected,actual,length,supports);
            Check(cache.Hits==(count==2 ? 0 : 1),"immutable matrix preparation remains reusable with changed loads");
        }
    }
    static void BoundariesAndAllocation()
    {
        const double length=3900;var supports=new[] {150.0,1150,2750,3750};var points=Array.Empty<BeamPointLoad>();
        var unit=new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=1},new BeamDistributedLoad {StartMm=850,EndMm=3075,KnPerM=.375}};
        var shape=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,unit,points,TowerOptimizer.PairEI,270);
        foreach(double scale in new[] {10/shape.MomentKnM,22/shape.ShearKn,70/shape.Reactions.Max(),1/shape.DeflectionUtilization})
        foreach(double factor in new[] {Math.BitDecrement(scale),scale,Math.BitIncrement(scale),scale*(1-1e-6),scale*(1+1e-6)})
        {
            var loads=unit.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*factor}).ToArray();
            var expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,loads,points,TowerOptimizer.PairEI,270,70,true,false,true);
            var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,loads,points,TowerOptimizer.PairEI,270,null,70,true,true);
            Analysis(expected,actual,length,supports,false);
            Check(expected.DeflectionEvaluated==actual.DeflectionEvaluated,"adjacent-value strength/reaction/deflection limits preserve early/full checking");
        }
        var many=Enumerable.Range(0,17).Select(i=>new BeamPointLoad {PositionMm=(i+.35)*length/17,Kn=.625}).ToArray();
        LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,unit,many,TowerOptimizer.PairEI,270);
        LoadedBeamAnalysis.Analyze(length,supports,unit,many,TowerOptimizer.PairEI,270);
        long before=GC.GetAllocatedBytesForCurrentThread();LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,unit,many,TowerOptimizer.PairEI,270);
        long oldBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();LoadedBeamAnalysis.Analyze(length,supports,unit,many,TowerOptimizer.PairEI,270);
        long newBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(newBytes+2000<oldBytes,"complete uncached FE assembly omits its per-element rectangular allocations");
    }
    static void ElementLoadReuse()
    {
        const double length=3900;var supports=new[] {150.0,1150,2750,3750};var points=new[] {new BeamPointLoad {PositionMm=1725.125,Kn=.375}};
        foreach(int criterion in new[] {270,350,500})foreach(double ei in new[] {TowerOptimizer.PairEI,2200.125})
        {
            var cache=new LoadedBeamAnalysis.MatrixCache();
            var first=new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=1.25},
                new BeamDistributedLoad {StartMm=725.125,EndMm=3075.625,KnPerM=-.375},
                new BeamDistributedLoad {StartMm=725.125+1e-8,EndMm=3075.625-1e-8,KnPerM=.125}};
            foreach(double scale in new[] {1.0,.625,-.375,0.0,1.0})
            {
                var loads=first.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*scale}).ToArray();
                var expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,loads,points,ei,criterion,allowSigned:true);
                var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,loads,points,ei,criterion,cache,allowSignedLoads:true);
                Analysis(expected,actual,length,supports);
            }
            Check(cache.Hits==4,"changed and repeated signed loads reuse structure with fresh element UDL values");
            cache=new LoadedBeamAnalysis.MatrixCache();
            foreach(double scale in new[] {1.0,.625,-.375,0.0,1.0})
            {
                var loads=points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*scale}).ToArray();
                var expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(length,supports,Array.Empty<BeamDistributedLoad>(),loads,ei,criterion,allowSigned:true);
                var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(length,supports,Array.Empty<BeamDistributedLoad>(),loads,ei,criterion,cache,allowSignedLoads:true);
                Analysis(expected,actual,length,supports);
            }
            Check(cache.Hits==4,"point-only cached recovery keeps the original positive-zero UDL value");
        }
    }
    internal static void Run()
    {
        Assembly();CompleteAndCached();BoundariesAndAllocation();ElementLoadReuse();
        Console.WriteLine("PASS "+checks+" inline FE stiffness checks: bitwise assembly/scalars/reactions/coefficients/diagrams, signed and changed loads, matrix reuse, all criteria/material stiffness, boundaries and allocation");
    }
}
