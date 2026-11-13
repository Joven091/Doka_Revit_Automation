using System;
using System.Linq;
using EngineeringDashboard;

internal static class GeometryPlanKeyTests
{
    static string Signature(TransferredSlabOptimizer.Geometry g)=>string.Join(";",g.Positions.Select(x=>x.ToString("R")))+"|"+string.Join(";",g.Joined)+"|"
        +string.Join(";",g.Stock.Select(p=>p.StartMm.ToString("R")+","+p.StockLengthMm.ToString("R")+","+p.FirstSupport+","+p.LastSupport+","+p.IsCustomLength))+"|"
        +string.Join(";",g.MemberSupports.Select(a=>string.Join(",",a.Select(x=>x.ToString("R")))));
    static string[] Ordered(TransferredSlabOptimizer.Geometry[] items,int limit)=>items.Where(g=>g.Positions.Length<=limit).OrderBy(g=>g.Positions.Length).Select(Signature).ToArray();
    internal static void Run()
    {
        int checks=0;
        foreach(bool rotated in new[] {false,true})foreach(bool crossings in new[] {false,true})foreach(var stocks in new[] {new[] {3300.0,3600},new[] {3300.0,3300,3300,3900}})
        {
            var input=ReportedCombinedTests.Input(1);input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};input.EngineerStationLimit=8;
            var network=crossings ? input.Network : new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=400,DepthMm=200}},Bays=Array.Empty<TransferBayRow>()};
            input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);var cache=new TransferredSlabOptimizer.GeometryCache();
            TransferredSlabOptimizer.Geometry[] first=null;
            foreach(int initial in new[] {6,8,10,6})
            {
                input.EngineerPlan.InitialLegs=initial;
                var actual=TransferredSlabOptimizer.SearchGeometries(input,cache,stocks,rotated);
                var expected=TransferredSlabOptimizer.SearchGeometries(input,null,stocks,rotated);
                if(!Ordered(actual,8).SequenceEqual(Ordered(expected,8)))throw new Exception("Geometry plan key changed ordered stock/support geometry");
                if(first!=null && (rotated || stocks.Length>3 || !crossings) && !ReferenceEquals(first,actual))throw new Exception("An irrelevant primary-plan tier repeated full geometry generation");
                first=first??actual;checks++;
            }
        }
        Console.WriteLine("PASS "+checks+" geometry plan-key checks: exact eligible plan-dependent patterns and reuse across irrelevant initial tiers");
    }
}
