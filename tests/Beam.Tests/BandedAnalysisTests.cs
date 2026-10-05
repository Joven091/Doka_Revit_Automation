using System;
using System.Linq;
using EngineeringDashboard;

internal static class BandedAnalysisTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Banded analysis: "+message);}
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
                var udl=pointOnly ? null : uniform;
                var dense=LoadedBeamAnalysis.AnalyzeDenseReference(length,supports,udl,points,TowerOptimizer.PairEI,denominator);
                var banded=LoadedBeamAnalysis.Analyze(length,supports,udl,points,TowerOptimizer.PairEI,denominator);
                Check(dense.Reactions.SequenceEqual(banded.Reactions) && dense.TotalLoadKn==banded.TotalLoadKn,"exact reactions and total force match dense storage");
                Check(dense.MomentKnM==banded.MomentKnM && dense.ShearKn==banded.ShearKn,"exact moment/shear envelopes match dense storage");
                Check(dense.DeflectionMm==banded.DeflectionMm && dense.DeflectionUtilization==banded.DeflectionUtilization && dense.GoverningLengthMm==banded.GoverningLengthMm,"exact deflection and governing regions match dense storage");
                foreach(double station in new[] {0.0,150,length*.25,length*.5,length-150,length})
                    foreach(bool right in new[] {false,true})Check(dense.Response(station,right).SequenceEqual(banded.Response(station,right)),"exact displacement, shear and moment responses match dense storage");
                Check(dense.DeflectionWithAffineOffsetMm(-2,-5,supports[0],supports.Last())==banded.DeflectionWithAffineOffsetMm(-2,-5,supports[0],supports.Last()),"support movement response matches dense storage");
            }
        }
        Console.WriteLine($"Passed {checks} dense/banded exact numerical comparisons.");
    }
}
