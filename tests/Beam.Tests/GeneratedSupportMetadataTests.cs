using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using EngineeringDashboard;
internal static class GeneratedSupportMetadataTests
{
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Generated support metadata: "+text);}
    static void OriginalBits(TransferredSlabOptimizer.Geometry geometry)
    {
        Check(geometry.MemberSupports!=null && geometry.MemberSupports.Length==geometry.Stock.Length,"native engineer geometry owns complete per-member metadata");
        for(int run=0;run<geometry.Stock.Length;run++)
        {
            var stock=geometry.Stock[run];var supports=geometry.MemberSupports[run];
            Check(supports.Length==stock.LastSupport-stock.FirstSupport+1,"member support count retains the stock slice");
            for(int i=0;i<supports.Length;i++)Check(BitConverter.DoubleToInt64Bits(supports[i])==BitConverter.DoubleToInt64Bits(geometry.Positions[stock.FirstSupport+i]-stock.StartMm),"retained metadata has the exact original global-add/local-subtract bits");
        }
    }
    static void ActualGeneratorCensus()
    {
        var common=ReportedScenarioTests.DiagnosticPart(3,1).Item2.CreateTowerInput();var network=common.Network;var input=common.CopyForSearch();
        input.LengthMm=network.Beams[0].SpanLength(common.LengthMm);input.LongitudinalStartMm=network.Beams[0].SpanStartMm;
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
        var covers=TransferredSlabOptimizer.StockCovers(input).Where(c=>c.Length>=2 && c.Length<=3).GroupBy(c=>c.Length).Select(g=>g.First()).ToArray();
        Check(covers.Length>0,"actual source has representative multi-stock primary covers");var rows=new List<object>();long totalAvoided=0;int totalExisting=0,totalShifted=0;
        foreach(var cover in covers)
        {
            int count=Math.Max(cover.Length*2,Math.Min(10,input.EngineerPlan.InitialLegs));
            var generated=EngineerTowerSearch.StockRunGeometries(cover,input).Concat(EngineerTowerSearch.Geometries(cover,false,input,count-1,count))
                .GroupBy(g=>new TransferredSlabOptimizer.GeometryKey(g.Positions,g.Joined)).Select(g=>g.First()).ToArray();
            Check(generated.Length>0,"native bounded station-tier sample emits actual source geometry");
            var originals=new HashSet<TransferredSlabOptimizer.Geometry>(generated);var owners=new Dictionary<double[],TransferredSlabOptimizer.Geometry>();
            foreach(var original in generated)
            {
                OriginalBits(original);
                foreach(var supports in original.MemberSupports)
                {Check(!owners.TryGetValue(supports,out var owner) || ReferenceEquals(owner,original),"support preservation introduces no cross-geometry array sharing");owners[supports]=original;}
            }
            var retained=generated.SelectMany(g=>TransferredSlabOptimizer.SiteGeometries(g,input.StockEndSetbackMm))
                .GroupBy(g=>new TransferredSlabOptimizer.GeometryKey(g.Positions,g.Joined)).Select(g=>g.First()).ToArray();
            int existing=0,shifted=0;long avoidedArrays=0,avoidedBytes=0;
            foreach(var geometry in retained)
            {
                if(geometry.MemberSupports==null){shifted++;Check(!originals.Contains(geometry),"translated site geometry still requires fresh metadata");continue;}
                existing++;Check(originals.Contains(geometry),"only unchanged engineer objects carry existing metadata through site normalization");OriginalBits(geometry);
                avoidedArrays+=1+geometry.MemberSupports.Length;avoidedBytes+=24+8L*geometry.MemberSupports.Length+geometry.MemberSupports.Sum(s=>24+8L*s.Length);
            }
            Check(existing>0,"actual source contains unchanged metadata that the former final assignment recopied");
            totalExisting+=existing;totalShifted+=shifted;totalAvoided+=avoidedBytes;
            rows.Add(new {Stocks=cover,StationTier=count,Generated=generated.Length,AfterSiteDedup=retained.Length,ExistingMetadata=existing,TranslatedMetadataNeeded=shifted,AvoidedArrays=avoidedArrays,Estimated64BitClrArrayBytesAvoided=avoidedBytes});
        }
        Check(totalExisting>0 && totalAvoided>0,"source census observes actual duplicate-final allocations avoided");
        Console.WriteLine("GENERATED_SUPPORT_METADATA_CENSUS "+JsonSerializer.Serialize(new {Scope="Actual scenario3 source covers; stock-run family plus one explicit complete station tier of the global engineer generator, followed by unchanged native site normalization/dedup. Diagnostic allocation estimate only; not full-scenario timing or retained-memory savings.",Rows=rows,TotalExisting=totalExisting,TotalTranslated=totalShifted,Estimated64BitClrArrayBytesAvoided=totalAvoided}));
    }
    internal static void Run(){ActualGeneratorCensus();Console.WriteLine("PASS "+checks+" generated support-metadata checks: actual native global arithmetic, per-geometry ownership, unchanged site normalization and duplicate-allocation census");}
}
