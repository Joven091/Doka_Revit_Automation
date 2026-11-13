using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class CatalogueIncumbentHandoffTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Catalogue incumbent handoff: "+message);}
    static TowerInput Common()=>new TowerInput {EngineerSearch=true,LocalNetworkSearch=true,PruneNetworkBounds=true,AllowCustomH20Lengths=true,
        LengthMm=2450,LegCapacityKn=70,StockLengthsMm=new[] {1250.0,1800,2450},StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,
        DeflectionDenominator=270,IgnoreUplift=true,FullLoadingOnly=true,NetworkSearchWorkers=1,AllowSeparateSlabFallback=false,
        Trace=new BeamSearchTrace("catalogue incumbent handoff")};
    static TransferNetworkInput Network()=>new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,
        PlywoodSheetLengthMm=2440,UpperSpacingMm=305,Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800},new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}},
        Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=200}}};
    // Small synthetic rank records test only handoff/exception semantics. They
    // are not numerical designs; an actual full-search parity case follows.
    static TransferNetworkResult RankRecord(TransferNetworkInput network,int legs)=>new TransferNetworkResult {Input=network,Bays=Array.Empty<UpperTransferPlan>(),
        Beams=new[] {new TowerLayout {EngineerSearch=true,LengthMm=2000,WidthMm=1524,PositionsMm=Enumerable.Range(0,legs/2).Select(i=>150.0+i*100).ToArray(),Joined=new bool[legs/2-1],
            StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=2000,FirstSupport=0,LastSupport=legs/2-1}}},
            Transfer=new TransferredSlabResult {RowsMm=Array.Empty<double>(),LowerCases=Array.Empty<TransferMember[]>(),UpperMembers=Array.Empty<TransferMember>()}}}};
    static void MetadataCases()
    {
        foreach(var scope in new[] {CustomH20LengthScope.Both,CustomH20LengthScope.BeamSupports,CustomH20LengthScope.SlabSupports})
        foreach(int previousLegs in new[] {6,10})
        {
            var common=Common();var network=Network();common.CustomH20Scope=scope;var old=RankRecord(network,previousLegs);var stock=RankRecord(network,8);var custom=RankRecord(network,4);
            common.NetworkWarmStart=old;var originalBound=new NetworkCatalogueBound(network,old);common.NetworkCatalogueBound=originalBound;var calls=new List<bool>();
            var result=TransferNetworkOptimizer.SearchSpacingStockPasses(common,network,()=>
            {
                calls.Add(common.AllowCustomH20Lengths);
                Check(common.CustomH20Scope==scope,"project custom-stock scope remains unchanged in both passes");
                if(!common.AllowCustomH20Lengths){Check(ReferenceEquals(common.NetworkWarmStart,old) && ReferenceEquals(common.NetworkCatalogueBound,originalBound),"catalogue pass retains the original warm and bound context");return stock;}
                Check(ReferenceEquals(common.NetworkWarmStart,old) && ReferenceEquals(common.NetworkCatalogueBound.Result,previousLegs<8 ? old : stock) && ReferenceEquals(common.NetworkCatalogueBound.SourceInput,network),"custom pass receives the stronger completed exact-context bound without changing warm geometry");return custom;
            });
            Check(calls.SequenceEqual(new[] {false,true}) && ReferenceEquals(result,custom),"catalogue/custom order and final ranking are unchanged");
            Check(ReferenceEquals(common.NetworkWarmStart,old) && ReferenceEquals(common.NetworkCatalogueBound,originalBound) && common.AllowCustomH20Lengths,"original warm/bound state and stock permissions are restored");
        }
        foreach(string mode in new[] {"wrong existing context","cloned result input","time limited","custom fault","custom cancellation","stock cancellation","stock failure"})
        {
            var common=Common();var network=Network();var old=RankRecord(network,12);var stock=RankRecord(mode=="cloned result input" ? network.WithSpacings() : network,8);
            if(mode=="time limited")stock.SearchTimeLimitReached=true;
            common.NetworkWarmStart=old;var originalBound=new NetworkCatalogueBound(mode=="wrong existing context" ? Network() : network,old);common.NetworkCatalogueBound=originalBound;bool cancelled=false;TransferNetworkResult result=null;
            try
            {
                result=TransferNetworkOptimizer.SearchSpacingStockPasses(common,network,()=>
                {
                    if(!common.AllowCustomH20Lengths)
                    {
                        if(mode=="stock cancellation")throw new OperationCanceledException();
                        if(mode=="stock failure")throw new TowerOptimizationException("synthetic exhausted catalogue","fixture");
                        return stock;
                    }
                    bool eligible=mode!="time limited" && mode!="stock failure";
                    Check(ReferenceEquals(common.NetworkWarmStart,old) && ReferenceEquals(common.NetworkCatalogueBound.Result,eligible ? stock : old),"only complete catalogue results seed the exact producer context, including terminal input copies");
                    if(eligible)Check(ReferenceEquals(common.NetworkCatalogueBound.SourceInput,network),"the bound retains its real source search input rather than the result's prefix copy");
                    if(mode=="custom fault")throw new ArgumentException("synthetic custom failure");
                    if(mode=="custom cancellation")throw new OperationCanceledException();
                    return RankRecord(network,4);
                });
            }
            catch(OperationCanceledException){cancelled=true;}
            Check(cancelled==(mode=="custom cancellation" || mode=="stock cancellation"),"user cancellation propagates without becoming a completed result");
            Check(ReferenceEquals(common.NetworkWarmStart,old) && ReferenceEquals(common.NetworkCatalogueBound,originalBound) && common.AllowCustomH20Lengths,"warm/bound state is restored on every failure/cancellation path");
            if(mode=="custom fault")Check(ReferenceEquals(result,stock),"ordinary custom failure retains the completed catalogue result");
        }
        foreach(string bypass in new[] {"catalogue only","first complete","historical","nonlocal","bounds off","deadline"})
        {
            var common=Common();var network=Network();var old=RankRecord(network,12);common.NetworkWarmStart=old;var originalBound=new NetworkCatalogueBound(network,old);common.NetworkCatalogueBound=originalBound;
            if(bypass=="catalogue only")common.AllowCustomH20Lengths=false;
            if(bypass=="first complete")common.FirstCompleteNetworkOnly=true;
            if(bypass=="historical")common.EngineerSearch=false;
            if(bypass=="nonlocal")common.LocalNetworkSearch=false;
            if(bypass=="bounds off")common.PruneNetworkBounds=false;
            if(bypass=="deadline")common.Trace=new BeamSearchTrace("explicit budget bypass",default,TimeSpan.FromHours(1),()=>0);
            int calls=0;TransferNetworkOptimizer.SearchSpacingStockPasses(common,network,()=>{calls++;Check(ReferenceEquals(common.NetworkWarmStart,old) && ReferenceEquals(common.NetworkCatalogueBound,originalBound),"bypassed search retains original warm/bound semantics");return RankRecord(network,8);});
            Check(calls==(bypass=="catalogue only" || bypass=="first complete" ? 1 : 2),"bypassed catalogue/custom invocation count is unchanged");
        }
    }
    static string Fingerprint(TransferNetworkResult value)=>value.LegCount+"|"+value.H20Count+"|"+value.EconomyScoreMm.ToString("R",CultureInfo.InvariantCulture)+"|"+string.Join(";",value.Beams.Select(b=>
        b.WidthMm.ToString("R",CultureInfo.InvariantCulture)+":"+string.Join(",",b.PositionsMm.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+":"+string.Join(",",b.Joined)+":"+
        string.Join("/",b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(x=>x).Concat(b.Transfer.UpperMembers).Select(m=>m.Name+":"+string.Join(",",new[] {m.StartMm,m.LengthMm,m.Analysis.MomentKnM,m.Analysis.ShearKn,m.Analysis.DeflectionMm}.Concat(m.Analysis.Reactions).Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))))));
    static void ActualSearch(bool coordinated)
    {
        var network=Network();var actual=Common();var reference=Common();reference.PruneNetworkBounds=false;
        actual.CoordinateCrossingLayouts=reference.CoordinateCrossingLayouts=coordinated;
        var method=typeof(TransferNetworkOptimizer).GetMethod("OptimizeCore",BindingFlags.Static|BindingFlags.NonPublic);
        TransferNetworkResult Solve(TowerInput input)
        {
            try{return (TransferNetworkResult)method.Invoke(null,new object[] {input,network,false});}
            catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        int customSeen=0;
        var result=TransferNetworkOptimizer.SearchSpacingStockPasses(actual,network,()=>
        {
            if(actual.AllowCustomH20Lengths)
            {
                Check(actual.NetworkCatalogueBound!=null && ReferenceEquals(actual.NetworkCatalogueBound.SourceInput,network) && actual.NetworkWarmStart==null,"actual custom search receives its freshly completed catalogue bound without changing warm geometry");
                if(coordinated)Check(!ReferenceEquals(actual.NetworkCatalogueBound.Result.Input,network),"terminal prefix input copies keep their exact producer provenance");
                customSeen++;
            }
            return Solve(actual);
        });
        var expected=TransferNetworkOptimizer.SearchSpacingStockPasses(reference,network,()=>Solve(reference));
        Check(customSeen==1 && Fingerprint(result)==Fingerprint(expected),"actual complete network agrees exactly with original unbounded-prefix catalogue/custom search");
        Check(actual.NetworkWarmStart==null && actual.NetworkCatalogueBound==null,"actual network's prior warm and bound states are restored");
        var wrong=Common();wrong.CoordinateCrossingLayouts=coordinated;
        var foreign=new NetworkCatalogueBound(Network(),RankRecord(Network(),4));wrong.NetworkCatalogueBound=foreign;
        Check(Fingerprint(TransferNetworkOptimizer.SearchSpacingStockPasses(wrong,network,()=>Solve(wrong)))==Fingerprint(expected) && ReferenceEquals(wrong.NetworkCatalogueBound,foreign),"an unrelated spacing/input context cannot supply an incumbent to this solve");
        foreach(var member in result.Beams.SelectMany(b=>b.Transfer.PrimaryCases.Concat(b.Transfer.LowerCases).SelectMany(x=>x).Concat(b.Transfer.UpperMembers)))
        {
            var dense=LoadedBeamAnalysis.AnalyzeDenseReference(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,270);
            Check(member.Utilization<=1+1e-8 && member.Analysis.DeflectionEvaluated && Math.Abs(dense.MomentKnM-member.Analysis.MomentKnM)<1e-7
                && Math.Abs(dense.ShearKn-member.Analysis.ShearKn)<1e-7 && Math.Abs(dense.DeflectionMm-member.Analysis.DeflectionMm)<1e-7,"actual final members retain independent numerical and capacity checks");
        }
    }
    static void WireSkip()
    {
        var common=Common();var network=Network();var bound=new NetworkCatalogueBound(network,RankRecord(network,8));common.NetworkCatalogueBound=bound;
        Check(ReferenceEquals(common.CopyForSearch().NetworkCatalogueBound,bound),"solve-local worker copies inherit the immutable bound context");
        string path=Path.Combine(Path.GetTempPath(),"catalogue-bound-wire-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            BeamWire.Write(path,new BeamSolverRequest {Main=common});var roundTrip=BeamWire.Read<BeamSolverRequest>(path);
            Check(roundTrip.Main.NetworkCatalogueBound==null && common.NetworkCatalogueBound==bound,"ephemeral bound provenance is omitted from request wire graphs");
        }
        finally{File.Delete(path);}
    }
    internal static void Run(){MetadataCases();ActualSearch(false);ActualSearch(true);WireSkip();Console.WriteLine("PASS "+checks+" catalogue-incumbent handoff checks: scopes, ranking, same-input guard, failure/cancellation restoration, bypasses and actual dense full-search parity");}
}
