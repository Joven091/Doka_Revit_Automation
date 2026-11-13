using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class VirtualBeamConstraintTests
{
    static int checks;
    static readonly MethodInfo analysis=typeof(InlineBeamStiffnessTests).GetMethod("Analysis",BindingFlags.Static|BindingFlags.NonPublic);
    static readonly MethodInfo diagnostics=typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool passed,string why){checks++;if(!passed)throw new Exception("Virtual beam constraints: "+why);}
    static void Same(double a,double b,string why)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),why);
    static void Matrix(double[,] a,double[,] b,string why)
    {
        Check(a.GetLength(0)==b.GetLength(0)&&a.GetLength(1)==b.GetLength(1),why+" dimensions");
        for(int i=0;i<a.GetLength(0);i++)for(int j=0;j<a.GetLength(1);j++)Same(a[i,j],b[i,j],why+" IEEE entry");
    }
    static void Parity()
    {
        var variants=new[] {new[] {0.0,975,1950,2925,3900},new[] {150.0,1150,2750,3750},new[] {0.0,150,1950,3750,3900},
            new[] {3750.0,2750,1150,150},new[] {450.0,1950,3450}};
        foreach(var stations in variants)foreach(int criterion in new[] {270,350,500})foreach(double ei in new[] {TowerOptimizer.PairEI,865.2})foreach(double q in new[] {.125,5.25,40.375})
        {
            var oldCache=new LoadedBeamAnalysis.MatrixCache(2,true);var newCache=new LoadedBeamAnalysis.MatrixCache(2,true,leastRecentlyUsed:true,maximumBytes:1024*1024);
            var uniform=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=q},new BeamDistributedLoad {StartMm=150,EndMm=2750,KnPerM=-q*.375}};
            var points=stations.Select((x,i)=>new BeamPointLoad {PositionMm=x,Kn=(i%2==0 ? .625 : -.125)*q})
                .Concat(new[] {new BeamPointLoad {PositionMm=0,Kn=q*.125},new BeamPointLoad {PositionMm=3900,Kn=-q*.125}}).ToArray();
            double[,] retainedStiffness=null,retainedFactor=null;
            foreach(double scale in new[] {1.0,.625,1.125})
            {
                var loads=uniform.Select(p=>new BeamDistributedLoad {StartMm=p.StartMm,EndMm=p.EndMm,KnPerM=p.KnPerM*scale}).ToArray();
                var atoms=points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*scale}).ToArray();
                var expected=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(3900,stations,loads,atoms,ei,criterion,allowSigned:true,matrixCache:oldCache);
                var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(3900,stations,loads,atoms,ei,criterion,newCache,allowSignedLoads:true);
                analysis.Invoke(null,new object[] {expected,actual,3900.0,stations,true});checks++;
                Check(Math.Abs(actual.Reactions.Sum()-actual.TotalLoadKn)<=1e-7*Math.Max(1,Math.Abs(actual.TotalLoadKn)),"support-point plus UDL force equilibrium");
                var key=new LoadedBeamAnalysis.MatrixKey(3900,stations,atoms,ei,loads);var oldShape=oldCache.Get(key);var newShape=newCache.Get(key);
                Matrix(oldShape.Stiffness,newShape.Stiffness,"raw immutable stiffness");Matrix(oldShape.Factor,newShape.Factor,"virtually constrained factorization");
                if(retainedStiffness==null){retainedStiffness=(double[,])newShape.Stiffness.Clone();retainedFactor=(double[,])newShape.Factor.Clone();}
                else{Matrix(retainedStiffness,newShape.Stiffness,"warm load cannot mutate stiffness");Matrix(retainedFactor,newShape.Factor,"warm load cannot mutate factor");}
                var lhs=new TowerSearchDiagnostics(criterion,ignoreUplift:true);var rhs=new TowerSearchDiagnostics(criterion,ignoreUplift:true);
                lhs.Analysis(expected,1,2,"P1 L",3,"Crossing resolved primary");rhs.Analysis(actual,1,2,"P1 L",3,"Crossing resolved primary");
                lhs.Legs(expected.Reactions,70,"B1 stage legs","Crossing resolved primary");rhs.Legs(actual.Reactions,70,"B1 stage legs","Crossing resolved primary");
                diagnostics.Invoke(null,new object[] {lhs,rhs});checks++;
            }
            Check(oldCache.Hits==5&&newCache.Hits==5,"cold and changed-load warm matrices follow identical hit paths");
        }
    }
    static void FailureAndAllocation()
    {
        var stations=new[] {0.0,975,1950,2925,3900};var points=stations.Select(x=>new BeamPointLoad {PositionMm=x,Kn=8.125}).ToArray();
        var uniform=new[] {new BeamDistributedLoad {EndMm=3900,KnPerM=40.125},new BeamDistributedLoad {StartMm=150,EndMm=2750,KnPerM=.625}};
        foreach(double capacity in new[] {.125,70.0})
        {
            var old=LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(3900,stations,uniform,points,TowerOptimizer.PairEI,270,capacity,true,true,true);
            var actual=LoadedBeamAnalysis.AnalyzeCombinedCached(3900,stations,uniform,points,TowerOptimizer.PairEI,270,null,capacity,true,true,true);
            analysis.Invoke(null,new object[] {old,actual,3900.0,stations,false});checks++;
        }
        var supports=new[] {150.0,1150,2750,3750};var oldCache=new LoadedBeamAnalysis.MatrixCache();var cache=new LoadedBeamAnalysis.MatrixCache(2,true,leastRecentlyUsed:true);
        LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(3900,supports,uniform,points,TowerOptimizer.PairEI,270,matrixCache:oldCache);
        LoadedBeamAnalysis.AnalyzeCombinedCached(3900,supports,uniform,points,TowerOptimizer.PairEI,270,cache);
        long before=GC.GetAllocatedBytesForCurrentThread();LoadedBeamAnalysis.AnalyzeBandedAssemblyReference(3900,supports,uniform,points,TowerOptimizer.PairEI,270,matrixCache:oldCache);
        long oldBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();LoadedBeamAnalysis.AnalyzeCombinedCached(3900,supports,uniform,points,TowerOptimizer.PairEI,270,cache);
        Check(GC.GetAllocatedBytesForCurrentThread()-before<oldBytes,"warm optimized analysis allocates less while retaining only support RHS entries");
    }
    internal static void Run()
    {
        Parity();FailureAndAllocation();
        Console.WriteLine("PASS "+checks+" virtual support constraint checks: exact cold/warm raw matrices and factors, coefficients/scalars/diagrams/diagnostics, support-load conservation, partial failures, material stiffnesses and allocation");
    }
}
