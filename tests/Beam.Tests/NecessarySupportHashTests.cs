using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;
internal static class NecessarySupportHashTests
{
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Necessary support hash: "+text);}
    internal static void Run()
    {
        var random=new Random(31415);
        for(int length=0;length<=40;length++)for(int sample=0;sample<20;sample++)
        {
            var supports=Enumerable.Range(0,length).Select(i=>(random.NextDouble()-.5)*100000).ToArray();
            var geometry=new TransferredSlabOptimizer.Geometry {MemberSupports=new[]{supports}};
            TransferredSlabOptimizer.NecessarySupportHash(geometry,0,null,out int polynomial,out int power);
            double start=(random.NextDouble()-.5)*1000,stock=2450+sample;int line=sample%2;
            var original=new CrossBeamSupportOptimizer.NecessaryMemberKey(start,stock,line,supports);
            var fast=new CrossBeamSupportOptimizer.NecessaryMemberKey(start,stock,line,supports,polynomial,power);
            Check(original.GetHashCode()==fast.GetHashCode() && original.Equals(fast) && fast.Equals(original),"precomputed polynomial/power reproduces the exact original ordered hash under integer overflow");
            var prior=geometry.NecessarySupportHashes;TransferredSlabOptimizer.NecessarySupportHash(geometry,0,null,out int again,out int againPower);
            Check(ReferenceEquals(prior,geometry.NecessarySupportHashes) && polynomial==again && power==againPower,"a retained immutable support template is reused without another support traversal");
        }
        var shared=new TransferredSlabOptimizer.Geometry {MemberSupports=new[]{new[]{200.0,1225,2250},new[]{150.0,1650}}};
        var results=new int[16];Parallel.For(0,results.Length,i=>{TransferredSlabOptimizer.NecessarySupportHash(shared,i%2,null,out int h,out int p);results[i]=h^p;});
        Check(results.Where((v,i)=>i%2==0).Distinct().Count()==1 && results.Where((v,i)=>i%2==1).Distinct().Count()==1,"racing pure builders publish complete identical per-run templates");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();var untouched=new TransferredSlabOptimizer.Geometry {MemberSupports=new[]{new[]{200.0,2250}}};bool cancelled=false;
            try{TransferredSlabOptimizer.NecessarySupportHash(untouched,0,new BeamSearchTrace("support template cancellation",cancellation.Token),out _,out _);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled && untouched.NecessarySupportHashes==null,"cancelled template construction publishes no partial hash array");
        }
        Console.WriteLine("PASS "+checks+" necessary support-hash checks: exact integer algebra/order/overflow, retained template reuse, racing publication and cancelled construction");
    }
}
