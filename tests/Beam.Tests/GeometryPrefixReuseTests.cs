using System;
using System.Linq;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class GeometryPrefixReuseTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Geometry prefix reuse: "+message);}
    static string Signature(TransferredSlabOptimizer.Geometry g)=>string.Join(";",g.Positions.Select(x=>x.ToString("R")))+"|"+string.Join(";",g.Joined)+"|"
        +string.Join(";",g.Stock.Select(p=>p.StartMm.ToString("R")+","+p.StockLengthMm.ToString("R")+","+p.FirstSupport+","+p.LastSupport+","+p.IsCustomLength))+"|"
        +string.Join(";",g.MemberSupports.Select(a=>string.Join(",",a.Select(x=>x.ToString("R")))));
    static string[] Eligible(TransferredSlabOptimizer.Geometry[] items,int limit)=>items.Where(g=>g.Positions.Length<=limit).OrderBy(g=>g.Positions.Length).Select(Signature).ToArray();
    internal static void Run()
    {
        foreach(bool engineer in new[] {false,true})foreach(bool rotated in new[] {false,true})foreach(bool exclude in new[] {false,true})foreach(double setback in new[] {150.0,165.125})
        {
            var cache=new TransferredSlabOptimizer.GeometryCache();var input=ReportedCombinedTests.Input(1);
            input.EngineerSearch=engineer;input.Exclude600Standalone=exclude;input.StockEndSetbackMm=setback;
            input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
            foreach(int limit in engineer ? new[] {6,8,7} : new[] {8})
            foreach(var stocks in new[] {new[] {3300.0,3600},new[] {3300.0,3900},new[] {3300.0,3600,2450}})
            {
                input.EngineerStationLimit=limit;
                var expected=TransferredSlabOptimizer.SearchGeometries(input,null,stocks,rotated);
                var actual=TransferredSlabOptimizer.SearchGeometries(input,cache,stocks,rotated);
                int eligible=engineer ? limit : int.MaxValue;
                Check(Eligible(actual,eligible).SequenceEqual(Eligible(expected,eligible)),"shared prefixes retain exact coordinates, brace topology, stocks and ordered member supports");
            }
        }
        var bounded=new TransferredSlabOptimizer.GeometryCache();int builds=0;
        Func<TransferredSlabOptimizer.Geometry[]> build=()=>{builds++;return Array.Empty<TransferredSlabOptimizer.Geometry>();};
        bounded.GetRegularPrefix("first",build,out bool firstHit);bounded.GetRegularPrefix("first",build,out bool repeatHit);
        Check(!firstHit && repeatHit && builds==1,"repeated prefix avoids its complete state build");
        for(int i=0;i<128;i++)bounded.GetRegularPrefix(i.ToString(),build,out _);
        bounded.GetRegularPrefix("first",build,out bool evictedHit);
        Check(!evictedHit && builds==130,"history is bounded and eviction only rebuilds a prefix");
        try{bounded.GetRegularPrefix("interrupted",()=>throw new OperationCanceledException(),out _);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        bounded.GetRegularPrefix("interrupted",build,out bool interruptedHit);Check(!interruptedHit,"interrupted state generation leaves no cached evidence");
        var concurrent=new TransferredSlabOptimizer.GeometryCache();
        Parallel.For(0,4,i=>concurrent.GetRegularPrefix("same",()=>new[] {new TransferredSlabOptimizer.Geometry {Positions=new[] {150.0,1150}}},out _));
        var saved=concurrent.GetRegularPrefix("same",()=>throw new Exception("Expected completed prefix"),out bool completedHit);
        Check(completedHit && saved[0].Positions.SequenceEqual(new[] {150.0,1150}),"concurrent publication retains a completed exact result");
        Console.WriteLine("PASS "+checks+" geometry prefix checks: ordered exact parity, contexts, bounded eviction, cancellation and concurrent publication");
    }
}
