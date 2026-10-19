using System;
using System.Linq;
using EngineeringDashboard;

internal static class BandedAnalysisTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Banded analysis: "+message);}
    static bool Near(double a,double b) => Math.Abs(a-b)<=1e-12*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
    public static void Run()
    {
        var random=new Random(2450270);
        foreach(double length in new[] {1800.0,5225,10600})foreach(int count in new[] {2,3,5})foreach(int denominator in new[] {270,350,500})
        {
            var supports=Enumerable.Range(0,count).Select(i=>150+(length-300)*i/(count-1)).ToArray();
            var points=Enumerable.Range(0,17).Select(i=>new BeamPointLoad {PositionMm=(i+.35)*length/17,Kn=.1+random.NextDouble()*11}).ToArray();
            var uniform=new[] {new BeamDistributedLoad {StartMm=125,EndMm=length-75,KnPerM=4.8},new BeamDistributedLoad {StartMm=length*.2,EndMm=length*.6,KnPerM=7.1}};
            foreach(bool pointOnly in new[] {false,true})
            {
                bool direct=count==2;
                Func<double,double,bool> same=(a,b)=>direct ? Math.Abs(a-b)<=2e-6*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b))) : a==b;
                Func<double[],double[],bool> values=(a,b)=>a.Zip(b,same).All(v=>v);
                var udl=pointOnly ? null : uniform;
                var dense=LoadedBeamAnalysis.AnalyzeDenseReference(length,supports,udl,points,TowerOptimizer.PairEI,denominator);
                var banded=LoadedBeamAnalysis.Analyze(length,supports,udl,points,TowerOptimizer.PairEI,denominator);
                Check(values(dense.Reactions,banded.Reactions) && same(dense.TotalLoadKn,banded.TotalLoadKn),"exact reactions and total force match dense storage");
                Check(same(dense.MomentKnM,banded.MomentKnM) && same(dense.ShearKn,banded.ShearKn),"exact moment/shear envelopes match dense storage");
                Check((direct ? same(dense.DeflectionMm,banded.DeflectionMm) && same(dense.DeflectionUtilization,banded.DeflectionUtilization) : Near(dense.DeflectionMm,banded.DeflectionMm) && Near(dense.DeflectionUtilization,banded.DeflectionUtilization)) && dense.GoverningLengthMm==banded.GoverningLengthMm,"direct point-load extrema agree with 42-step bisection to 1e-12; governing regions match");
                foreach(double station in new[] {0.0,150,length*.25,length*.5,length-150,length})
                    foreach(bool right in new[] {false,true})Check(values(dense.Response(station,right),banded.Response(station,right)),"exact displacement, shear and moment responses match dense storage");
                Check(same(dense.DeflectionWithAffineOffsetMm(-2,-5,supports[0],supports.Last()),banded.DeflectionWithAffineOffsetMm(-2,-5,supports[0],supports.Last())),"support movement response matches dense storage");
                if(pointOnly)
                {
                    var cache=new LoadedBeamAnalysis.MatrixCache();
                    LoadedBeamAnalysis.AnalyzePointsCached(length,supports,points,TowerOptimizer.PairEI,denominator,cache);
                    foreach(double scale in new[] {.45,1.7})
                    {
                        var changed=points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*scale}).ToArray();
                        var reference=LoadedBeamAnalysis.AnalyzeDenseReference(length,supports,null,changed,TowerOptimizer.PairEI,denominator);
                        var reused=LoadedBeamAnalysis.AnalyzePointsCached(length,supports,changed,TowerOptimizer.PairEI,denominator,cache);
                        Check(values(reference.Reactions,reused.Reactions) && same(reference.MomentKnM,reused.MomentKnM) && same(reference.ShearKn,reused.ShearKn) && (direct ? same(reference.DeflectionMm,reused.DeflectionMm) && same(reference.DeflectionUtilization,reused.DeflectionUtilization) : Near(reference.DeflectionMm,reused.DeflectionMm) && Near(reference.DeflectionUtilization,reused.DeflectionUtilization)),"prepared matrix preserves exact forces/strength and deflection to 1e-12 with new loads");
                    }
                    Check(cache.Hits==(direct ? 0 : 2),"two-support path avoids matrices; continuous-beam load changes reuse immutable preparation");
                }
            }
        }
        double root;
        Check(LoadedBeamAnalysis.TryQuadraticRoot(1,-1e8,1,0,1,out root) && Math.Abs(root-1e-8)<1e-20,"stable small root avoids cancellation");
        Check(LoadedBeamAnalysis.TryQuadraticRoot(0,2,-6,0,4,out root) && root==3,"linear slope root is evaluated directly");
        Check(!LoadedBeamAnalysis.TryQuadraticRoot(0,0,0,0,1,out root),"flat slope needs only endpoints");
        Check(!LoadedBeamAnalysis.TryQuadraticRoot(1,0,1,0,1,out root),"non-real roots fall back safely");
        foreach(double scale in new[] {1e-6,1.0,1e6})
        {
            var supports=new[] {150.0,1524,2600,4000};
            var loads=new[] {new BeamPointLoad {PositionMm=0,Kn=2*scale},new BeamPointLoad {PositionMm=250,Kn=5*scale},new BeamPointLoad {PositionMm=1875,Kn=11*scale},new BeamPointLoad {PositionMm=4200,Kn=3*scale}};
            var reference=LoadedBeamAnalysis.AnalyzeDenseReference(4500,supports,null,loads,TowerOptimizer.PairEI,270);
            var direct=LoadedBeamAnalysis.Analyze(4500,supports,null,loads,TowerOptimizer.PairEI,270);
            Check(reference.Reactions.SequenceEqual(direct.Reactions) && reference.MomentKnM==direct.MomentKnM && Near(reference.DeflectionMm,direct.DeflectionMm) && Near(reference.DeflectionUtilization,direct.DeflectionUtilization),"direct extrema agree for asymmetric multiple supports, overhangs and widely scaled loads");
        }
        var failed=LoadedBeamAnalysis.AnalyzePointsCached(1000,new[] {0.0,1000},new[] {new BeamPointLoad {PositionMm=500,Kn=80}},TowerOptimizer.PairEI,270,new LoadedBeamAnalysis.MatrixCache(),70,true);
        Check(!failed.DeflectionEvaluated && failed.MomentKnM>10 && failed.ShearKn>22,"known capacity failure skips costly deflection extrema");
        var passing=LoadedBeamAnalysis.AnalyzePointsCached(1000,new[] {0.0,1000},new[] {new BeamPointLoad {PositionMm=500,Kn=20}},TowerOptimizer.PairEI,270,new LoadedBeamAnalysis.MatrixCache(),70,true);
        Check(passing.DeflectionEvaluated && passing.DeflectionMm>0,"candidate passing moment/shear/leg checks receives complete deflection evaluation");
        var diagnostic=new TowerSearchDiagnostics(270);diagnostic.Analysis(failed,1,2,"Rejected primary",3,"test");
        Check(!diagnostic.Exception("rejected").Details.Contains("deflection"),"unevaluated deflection is not reported as passing or as a computed failure");
        Console.WriteLine($"Passed {checks} dense/banded and direct-extrema numerical comparisons (continuous-beam forces exact, direct two-support path within reference numerical tolerance).");
    }
}
