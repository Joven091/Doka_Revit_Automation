using System;
using System.Collections.Generic;
using EngineeringDashboard;

internal static class UniformShapeValueKeyTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Uniform shape value key: "+message);}
    static int LegacyHash(double length,double[] supports,double ei,int denominator,double start,double end)
    {
        unchecked{int hash=(((length.GetHashCode()*31+ei.GetHashCode())*31+denominator)*31+start.GetHashCode())*31+end.GetHashCode();foreach(double x in supports)hash=hash*31+x.GetHashCode();return hash;}
    }
    internal static void Run()
    {
        var supports=new[] {300.0,1500,3700};
        var key=new TransferredSlabOptimizer.UniformShapeKey(4000,supports,899.7512,270);
        var equal=new TransferredSlabOptimizer.UniformShapeKey(4000,(double[])supports.Clone(),899.7512,270,0,4000);
        Check(key.Equals(equal) && key.GetHashCode()==equal.GetHashCode(),"exact coordinates and an explicit default end preserve identity");
        Check(key.GetHashCode()==LegacyHash(4000,supports,899.7512,270,0,4000),"hash arithmetic retains the original ordered recipe");
        foreach(var changed in new[] {
            new TransferredSlabOptimizer.UniformShapeKey(4000,new[] {300.0,1500.000000001,3700},899.7512,270),
            new TransferredSlabOptimizer.UniformShapeKey(4025,supports,899.7512,270),
            new TransferredSlabOptimizer.UniformShapeKey(4000,supports,865.2,270),
            new TransferredSlabOptimizer.UniformShapeKey(4000,supports,899.7512,500),
            new TransferredSlabOptimizer.UniformShapeKey(4000,supports,899.7512,270,25,4000),
            new TransferredSlabOptimizer.UniformShapeKey(4000,supports,899.7512,270,0,3975)})
            Check(!key.Equals(changed),"stock, material, criterion, interval and unrounded support changes remain distinct");
        var zero=new TransferredSlabOptimizer.UniformShapeKey(4000,new[] {0.0,3700},899.7512,270);
        var negativeZero=new TransferredSlabOptimizer.UniformShapeKey(4000,new[] {-0.0,3700},899.7512,270);
        Check(zero.Equals(negativeZero) && zero.GetHashCode()==negativeZero.GetHashCode(),"signed zero keeps the former double equality");
        var nan=new TransferredSlabOptimizer.UniformShapeKey(4000,new[] {double.NaN,3700},899.7512,270);
        Check(nan.Equals(new TransferredSlabOptimizer.UniformShapeKey(4000,new[] {double.NaN,3700},899.7512,270)),"support equality retains former Double.Equals behavior");
        var map=new Dictionary<TransferredSlabOptimizer.UniformShapeKey,string> {{key,"exact"}};
        Check(map.TryGetValue(equal,out string value) && value=="exact","dictionary lookup preserves the checked response context");
        var retained=new TransferredSlabOptimizer.UniformShapeKey[4096];
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<retained.Length;i++)retained[i]=new TransferredSlabOptimizer.UniformShapeKey(4000,supports,899.7512,270,0,4000);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(retained);
        Check(allocated<2048,"repeated exact cache-key construction no longer allocates one object per uniform check");
        Console.WriteLine("PASS "+checks+" uniform shape value-key checks: exact hash/equality/context and allocation-free lookup keys");
    }
}
