using System;
using System.Collections.Generic;
using System.Linq;
using EngineeringDashboard;

internal static class PrimaryStartsAllocationTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception("Primary start obstacle allocation: "+reason);}
    // Literal original search: the obstacle order and both signed primary
    // positions govern which exact boundary starts the caller tries first.
    static double[] Original(TowerInput common,double preferred,double earliest,double latest,double stock,double face,double centre,double width,double top,IEnumerable<FormworkSolid> obstacles)
    {
        if(!common.OptimizeCrossingSearch)return new[]{preferred};
        double half=common.Primary.WidthMm/2,bottom=top-common.Primary.DepthMm;
        var boundaries=obstacles.Where(o=>!o.IsTowerEnvelope && o.Z0<top-1e-7 && o.Z1>bottom+1e-7
            && new[]{-1,1}.Any(side=>o.X0<centre+side*width/2+half-1e-7 && o.X1>centre+side*width/2-half+1e-7))
            .SelectMany(o=>new[]{o.Y1-face,o.Y0-face-stock});
        return new[]{preferred,earliest,latest}.Concat(boundaries).Where(s=>s>=earliest-1e-7 && s<=latest+1e-7)
            .Select(s=>Math.Max(earliest,Math.Min(latest,s))).Distinct().ToArray();
    }
    static void Compare(TowerInput input,double centre,double width,double top,FormworkSolid[] obstacles)
    {
        var expected=Original(input,-287.5,-287.5,-269.5,1375,512.5,centre,width,top,obstacles);
        var actual=CrossBeamSupportOptimizer.PrimaryStarts(input,-287.5,-287.5,-269.5,1375,512.5,centre,width,top,obstacles);
        Check(expected.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(actual.Select(BitConverter.DoubleToInt64Bits)),"exact primary start values, order and deduplication match the original");
    }
    static IEnumerable<FormworkSolid> MustNotEnumerate()=>Enumerable.Range(0,1).Select<int,FormworkSolid>(_=>throw new Exception("enumeration was not expected"));
    static void Parity()
    {
        var random=new Random(519070);
        foreach(var material in new[]{PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var input=new TowerInput {PrimaryMaterial=material,OptimizeCrossingSearch=true};
            for(int sample=0;sample<500;sample++)
            {
                double centre=random.NextDouble()*10000-5000,width=random.NextDouble()*2500,top=random.NextDouble()*3000-1500;
                var obstacles=Enumerable.Range(0,24).Select(i=>new FormworkSolid {X0=centre+(random.NextDouble()-.5)*3000,X1=centre+(random.NextDouble()-.5)*3000,
                    Y0=1520+random.NextDouble()*100,Y1=210+random.NextDouble()*50,Z0=top-250+random.NextDouble()*400,Z1=top-250+random.NextDouble()*400,IsTowerEnvelope=i%7==0}).ToArray();
                Compare(input,centre,width,top,obstacles);
            }
            double half=input.Primary.WidthMm/2,top0=-1350,bottom=top0-input.Primary.DepthMm;
            foreach(int side in new[]{-1,1})foreach(double delta in new[]{-1e-7,0.0,1e-7})
            foreach(bool envelope in new[]{false,true})
            {
                double x=3450+side*1524.0/2;
                Compare(input,3450,1524,top0,new[]{new FormworkSolid {X0=x+half-1e-7+delta,X1=x+half+100,Y0=1605,Y1=230,Z0=bottom-10,Z1=top0+10,IsTowerEnvelope=envelope},
                    new FormworkSolid {X0=x-half-100,X1=x-half+1e-7+delta,Y0=1605,Y1=230,Z0=bottom-10,Z1=top0+10,IsTowerEnvelope=envelope},
                    new FormworkSolid {X0=x-half,X1=x+half,Y0=1605,Y1=230,Z0=top0-1e-7+delta,Z1=top0+100},
                    new FormworkSolid {X0=x-half,X1=x+half,Y0=1605,Y1=230,Z0=bottom-100,Z1=bottom+1e-7+delta}});
            }
            foreach(double value in new[]{-0.0,0.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue})
                Compare(input,value,value,top0,new[]{new FormworkSolid {X0=-10000,X1=10000,Y0=1605,Y1=230,Z0=-2000,Z1=0}});
            input.OptimizeCrossingSearch=false;
            Check(CrossBeamSupportOptimizer.PrimaryStarts(input,123,0,100,1000,0,0,1524,0,MustNotEnumerate()).SequenceEqual(new[]{123.0}),"disabled crossing search returns its original single start before obstacle enumeration");
        }
    }
    static Exception Capture(Action action){try{action();return null;}catch(Exception error){return error;}}
    static void ExceptionalInputs()
    {
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,OptimizeCrossingSearch=true};
        foreach(var obstacles in new IEnumerable<FormworkSolid>[] {null,new FormworkSolid[]{null},MustNotEnumerate()})
        {
            var a=Capture(()=>Original(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,obstacles));
            var b=Capture(()=>CrossBeamSupportOptimizer.PrimaryStarts(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,obstacles));
            Check(a!=null && b!=null && a.GetType()==b.GetType() && a.Message==b.Message,"null and throwing obstacle enumeration preserve the original exceptions");
        }
    }
    static void Allocations()
    {
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,OptimizeCrossingSearch=true};
        var obstacles=Enumerable.Range(0,128).Select(i=>new FormworkSolid {X0=2600,X1=4300,Y0=1605,Y1=230,Z0=-1600,Z1=-1300}).ToArray();
        for(int i=0;i<32;i++){Original(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,obstacles);CrossBeamSupportOptimizer.PrimaryStarts(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,obstacles);}
        const int repetitions=256;long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<repetitions;i++)Original(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,obstacles);
        long before=GC.GetAllocatedBytesForCurrentThread()-start;start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<repetitions;i++)CrossBeamSupportOptimizer.PrimaryStarts(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,obstacles);
        long after=GC.GetAllocatedBytesForCurrentThread()-start;
        Console.WriteLine("PRIMARY_STARTS_ALLOCATIONS originalBytes="+before+" directBytes="+after+" repetitions="+repetitions+" obstacles="+obstacles.Length);
        Check(before-after>=repetitions*obstacles.Length*32,"per-obstacle temporary allocations are removed on equivalent clearance searches");
    }
    internal static void Run(){Parity();ExceptionalInputs();Allocations();Console.WriteLine("PASS "+checks+" primary start obstacle checks: exact ordered starts, tolerance contacts, both primary materials, nonfinite arithmetic and allocations");}
}
