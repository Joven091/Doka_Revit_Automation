using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class GeometryExtensionTests
{
    static string Signature(TransferredSlabOptimizer.Geometry g)=>string.Join(";",g.Positions.Select(x=>x.ToString("R")))+"|"+string.Join(";",g.Joined)+"|"
        +string.Join(";",g.Stock.Select(p=>p.StartMm.ToString("R")+","+p.StockLengthMm.ToString("R")+","+p.FirstSupport+","+p.LastSupport+","+p.IsCustomLength))+"|"
        +string.Join(";",g.MemberSupports.Select(a=>string.Join(",",a.Select(x=>x.ToString("R")))));
    static string[] Eligible(TransferredSlabOptimizer.Geometry[] items,int limit)=>items.Where(g=>g.Positions.Length<=limit)
        .OrderBy(g=>g.Positions.Length).Select(Signature).ToArray();
    internal static void Run()
    {
        int checks=0;
        foreach(bool rotated in new[] {false,true})
        foreach(bool exclude600 in new[] {false,true})
        foreach(var stocks in new[] {new[] {3300.0,3600},new[] {2450.0,2450,2450},new[] {3300.0,3300,3300,3900}})
        {
            var input=ReportedCombinedTests.Input(1);input.Exclude600Standalone=exclude600;
            input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
            input.EngineerPlan=EngineerPrimaryPlan.Create(input,input.Network,0);input.EngineerPlan.InitialLegs=7;
            var cache=new TransferredSlabOptimizer.GeometryCache();
            foreach(int limit in new[] {6,8,7,10,6,9})
            {
                input.EngineerStationLimit=limit;
                var reference=TransferredSlabOptimizer.SearchGeometries(input,null,stocks,rotated);
                var actual=TransferredSlabOptimizer.SearchGeometries(input,cache,stocks,rotated);
                if(!Eligible(reference,limit).SequenceEqual(Eligible(actual,limit)))throw new Exception("Incremental geometry changed eligible patterns or their within-tier order: "+rotated+" "+exclude600+" "+string.Join(",",stocks)+" "+limit);
                checks++;
            }
            // Reusing a cache with another generation constraint must not reuse
            // patterns violating the changed project setting.
            input.StockEndSetbackMm=300;input.EngineerStationLimit=8;
            if(!Eligible(TransferredSlabOptimizer.SearchGeometries(input,null,stocks,rotated),8).SequenceEqual(Eligible(TransferredSlabOptimizer.SearchGeometries(input,cache,stocks,rotated),8)))throw new Exception("Changed setback reused another geometry family");
            checks++;
        }
        var shared=new TransferredSlabOptimizer.GeometryCache();
        Parallel.ForEach(new[] {10,6,8,7},limit=>
        {
            var input=ReportedCombinedTests.Input(1);input.EngineerStationLimit=limit;input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
            var expected=Eligible(TransferredSlabOptimizer.SearchGeometries(input,null,new[] {3300.0,3600},false),limit);
            var actual=Eligible(TransferredSlabOptimizer.SearchGeometries(input,shared,new[] {3300.0,3600},false),limit);
            if(!expected.SequenceEqual(actual))throw new Exception("Concurrent geometry extension lost or reordered a tier");
        });
        // A cancelled extension must not poison its completed prefix.
        var transactional=new TransferredSlabOptimizer.GeometryCache();var seed=new TransferredSlabOptimizer.Geometry {Positions=new double[2]};
        transactional.GetEngineer("cancel",2,_=>new[] {seed},out _,out _);
        try {transactional.GetEngineer("cancel",4,_=>throw new OperationCanceledException(),out _,out _);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        var recovered=transactional.GetEngineer("cancel",4,_=>new[] {new TransferredSlabOptimizer.Geometry {Positions=new double[4]}},out _,out _);
        if(recovered.Length!=2 || !ReferenceEquals(recovered[0],seed))throw new Exception("Cancelled extension discarded or poisoned a completed tier");
        Console.WriteLine("PASS geometry extension: "+checks+" ordered range comparisons, changed setbacks, concurrent limits and cancellation rollback");
    }
}
