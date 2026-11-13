using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using EngineeringDashboard;
using G=EngineeringDashboard.TransferredSlabOptimizer.Geometry;
using B=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;
internal static class NativeSupportSliceTests
{
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Native support slices: "+text);}
    static void Bits(double a,double b,string text)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),text);
    static double[][] Original(G g)=>g.Stock.Select(s=>g.Positions.Skip(s.FirstSupport).Take(s.LastSupport-s.FirstSupport+1).Select(x=>x-s.StartMm).ToArray()).ToArray();
    static G Geometry(bool native=true)
    {
        var positions=new[]{150.1,1150.1,2150.1,2850.1,3150.1,4150.1,5150.1,5850.1};
        var stock=new[]{new TowerStockPiece {StockLengthMm=3000,FirstSupport=0,LastSupport=3},new TowerStockPiece {StartMm=3000,StockLengthMm=3000,FirstSupport=4,LastSupport=7}};
        var joined=new[]{true,true,true,false,true,true,true};
        var g=native ? G.Generated(stock,positions,joined,null) : new G {Stock=stock,Positions=positions,Joined=joined};
        if(!native)g.MemberSupports=Original(g);return g;
    }
    static void ZeroEagerAndEarlyConsumers()
    {
        var g=Geometry();var expected=Original(g);var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20};
        Check(g.HasNativeSupportSlices && g.HasSupportMetadata && !g.HasMaterializedSupports,"trusted factory registers slices without allocating local arrays");
        Check(g.SupportRuns==expected.Length,"native count does not invoke compatibility materialization");
        var nativeKeys=new CrossBeamSupportOptimizer.NecessaryMemberKey[expected.Length];
        for(int run=0;run<expected.Length;run++)
        {
            var vector=g.Supports(run);Check(vector.IsNative,"unmaterialized native access is a stored-coordinate slice");
            for(int i=0;i<vector.Length;i++)Bits(vector[i],expected[run][i],"native values retain exact old subtraction bits");
            TransferredSlabOptimizer.NecessarySupportHash(g,run,null,out int h,out int p);
            var raw=new CrossBeamSupportOptimizer.NecessaryMemberKey(g.Stock[run].StartMm,g.Stock[run].StockLengthMm,0,expected[run]);
            var slice=new CrossBeamSupportOptimizer.NecessaryMemberKey(g.Stock[run].StartMm,g.Stock[run].StockLengthMm,0,vector,h,p);
            nativeKeys[run]=slice;
            Check(raw.Equals(slice) && raw.GetHashCode()==slice.GetHashCode(),"native early response keys equal original materialized keys");
            Check(CrossBeamSupportOptimizer.GenericPrimaryForcesFitSupports(input,3000,vector,Array.Empty<BeamDistributedLoad>(),null),"typed necessary checks consume native values directly");
        }
        TransferredSlabOptimizer.PrepareSpanShape(g,new TransferredSlabOptimizer.SpanGeometryContext(25.125,Array.Empty<BeamDistributedLoad>()));
        var view=(B.UniversalView)typeof(B).GetMethod("BuildUniversalView",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{input,new[]{g},0.0,Array.Empty<BeamDistributedLoad>(),null,null});
        Check(view.Kept.Length==1 && !g.HasMaterializedSupports,"hash/span/global/necessary checks leave every native local array unmaterialized");
        var arrays=g.MaterializeSupports(null);Check(g.HasMaterializedSupports && ReferenceEquals(arrays,g.MemberSupports),"compatibility inspection returns the completed owned arrays");
        for(int run=0;run<arrays.Length;run++)
        {
            for(int i=0;i<arrays[run].Length;i++)Bits(arrays[run][i],expected[run][i],"materialized full-response arrays retain all original bits");
            var after=new CrossBeamSupportOptimizer.NecessaryMemberKey(g.Stock[run].StartMm,g.Stock[run].StockLengthMm,0,g.Supports(run));
            Check(nativeKeys[run].Equals(after) && after.Equals(nativeKeys[run]) && after.GetHashCode()==nativeKeys[run].GetHashCode(),"pre/post-materialization keys and exact hashes remain interchangeable");
        }
        var second=Geometry();var other=second.MaterializeSupports(null);
        Check(!ReferenceEquals(arrays,other) && arrays.Where((a,i)=>ReferenceEquals(a,other[i])).Count()==0,"different geometries never share materialized local-array ownership");
        g.Positions[0]+=25;g.Stock[0].StartMm+=25;Bits(g.MemberSupports[0][0],expected[0][0],"completed local copies remain independent of later source-coordinate edits");
    }
    static void ActualCallbackAndEscapeParity()
    {
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
        var inputFactory=typeof(ResolvedSupportCountBoundTests).GetMethod("Input",hidden);var prepare=typeof(ResolvedSupportCountBoundTests).GetMethod("Prepare",hidden);
        foreach(double force in new[]{60.0,200})
        {
            var a=(TowerInput)inputFactory.Invoke(null,new object[]{70.0,true,default(CancellationToken)});var b=(TowerInput)inputFactory.Invoke(null,new object[]{70.0,true,default(CancellationToken)});
            var first=(Func<G,double,bool>)prepare.Invoke(null,new object[]{a,force});var second=(Func<G,double,bool>)prepare.Invoke(null,new object[]{b,force});
            var positions=new[]{200.0,1225,2250};var stock=new[]{new TowerStockPiece {StockLengthMm=2450,FirstSupport=0,LastSupport=2}};
            var native=G.Generated(stock,positions,new[]{true,true},null);var raw=new G {Stock=stock,Positions=positions,Joined=new[]{true,true},MemberSupports=new[]{(double[])positions.Clone()}};
            PreparedReceiverSupportBounds.Minimum(first,new[]{2450.0},0);PreparedReceiverSupportBounds.Minimum(second,new[]{2450.0},0);
            Check(PreparedReceiverSupportBounds.MayFit(first,native,0)==PreparedReceiverSupportBounds.MayFit(second,raw,0) && !native.HasMaterializedSupports,"actual resolved necessary callbacks match eager geometry without materializing local arrays");
            Check(first(native,0)==second(raw,0),"actual full resolved callback/FE agrees with eager geometry");
            Check(native.HasMaterializedSupports==(force==60),"only the full-response path beyond necessary count checks materializes arrays");
            typeof(DedicatedUniformScalarTests).GetMethod("Diagnostics",hidden).Invoke(null,new object[]{a.Diagnostics,b.Diagnostics});checks++;
        }
        var input=new TowerInput {LengthMm=6000,TotalLineLoad=2,LegCapacityKn=70,PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,DeflectionDenominator=270,StockLengthsMm=new[]{3000.0},StockEndSetbackMm=150,Exclude600Standalone=true};
        var positions2=new[]{250.0,1250,2250,3250,4250,5250};var stock2=new[]{new TowerStockPiece {StockLengthMm=3000,FirstSupport=0,LastSupport=2},new TowerStockPiece {StartMm=3000,StockLengthMm=3000,FirstSupport=3,LastSupport=5}};
        var source=G.Generated(stock2,positions2,new[]{true,true,false,true,true},null);var result=TowerOptimizer.Evaluate(input,source.Positions,source.Joined,1524,false);
        Check(result!=null && !ReferenceEquals(result.PositionsMm,source.Positions) && result.StockPlan.Pieces.All(p=>source.Stock.All(s=>!ReferenceEquals(p,s))),"actual standalone output copies positions and pieces rather than exposing native slice sources");
        double before=source.Supports(0)[0];result.PositionsMm[0]+=25;result.StockPlan.Pieces[0].StartMm+=25;
        Check(!source.HasMaterializedSupports && source.Supports(0)[0]==before && source.Positions[0]==250 && source.Stock[0].StartMm==0,"mutating published standalone output cannot change unmaterialized native support coordinates");
    }
    static void PublicationAndFallbacks()
    {
        var g=Geometry();var results=new double[24][][];
        Parallel.For(0,results.Length,i=>results[i]=g.MaterializeSupports(null));
        Check(results.All(a=>ReferenceEquals(a,results[0])) && results[0].All(a=>a!=null),"concurrent materializers publish one whole completed array graph");
        using(var cts=new CancellationTokenSource())
        {
            var cold=Geometry();cts.Cancel();var trace=new BeamSearchTrace("cancel native support materialization",cts.Token);bool cancelled=false;
            try{cold.MaterializeSupports(trace);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled && !cold.HasMaterializedSupports,"cancelled cold materialization publishes no partial arrays");
            cancelled=false;try{g.MaterializeSupports(trace);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"a completed array cannot bypass explicit cancellation");
            var unregistered=new G {Positions=cold.Positions,Stock=cold.Stock,Joined=cold.Joined};cancelled=false;
            try{unregistered.PrepareGeneratedSupports(trace);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled && !unregistered.HasSupportMetadata,"cancelled registration publishes no native provenance");
        }
        var provided=new[]{new[]{200.0,2250}};var manual=new G {Positions=new[]{999.0},Stock=new[]{new TowerStockPiece {StartMm=800,FirstSupport=7,LastSupport=8}},MemberSupports=provided};
        Check(!manual.HasNativeSupportSlices && ReferenceEquals(manual.MaterializeSupports(null),provided) && manual.Supports(0)[0]==200,"manual provided vectors remain untouched despite unrelated positions/indices");
        var unmarked=new G {Positions=Geometry().Positions,Stock=Geometry().Stock};Check(!unmarked.HasSupportMetadata && unmarked.MemberSupports==null,"unmarked valid-looking coordinates never gain inferred provenance");
        var unsupported=new G {Positions=new[]{10.0,20},Stock=new[]{new TowerStockPiece {StockLengthMm=100,FirstSupport=-1,LastSupport=1}},Joined=new[]{true}};var old=Original(unsupported);unsupported.PrepareGeneratedSupports(null);
        Check(!unsupported.HasNativeSupportSlices && unsupported.HasMaterializedSupports && unsupported.MemberSupports[0].SequenceEqual(old[0]),"unsupported generated mappings keep exact old LINQ fallback");
        var nonfinite=new G {Positions=new[]{double.NaN,20},Stock=new[]{new TowerStockPiece {StockLengthMm=100,FirstSupport=0,LastSupport=1}},Joined=new[]{true}};old=Original(nonfinite);nonfinite.PrepareGeneratedSupports(null);
        Check(!nonfinite.HasNativeSupportSlices && nonfinite.HasMaterializedSupports,"nonfinite producer coordinates retain eager raw semantics");Bits(nonfinite.MemberSupports[0][0],old[0][0],"unsupported NaN payload retains old subtraction result");
        var fault=new G {Positions=new[]{10.0,20,30,40},Stock=new[]{new TowerStockPiece {StockLengthMm=100,FirstSupport=0,LastSupport=1},null},Joined=new[]{true,false,true}};bool failed=false;
        try{fault.PrepareGeneratedSupports(null);}catch(NullReferenceException){failed=true;}
        Check(failed && !fault.HasSupportMetadata && !fault.HasMaterializedSupports,"a failed old-expression fallback publishes neither provenance nor partial arrays");
        fault.Stock[1]=new TowerStockPiece {StockLengthMm=100,FirstSupport=2,LastSupport=3};fault.PrepareGeneratedSupports(null);
        Check(fault.HasNativeSupportSlices && !fault.HasMaterializedSupports,"an unpublished failed registration can retry after its source is repaired");
        var overridden=Geometry();overridden.MemberSupports=provided;Check(!overridden.HasNativeSupportSlices && ReferenceEquals(overridden.MemberSupports,provided),"explicit raw replacement clears native provenance");
        var fixedLayout=new TowerLayout {PositionsMm=new[]{150.0,1150,2850},Joined=new[]{true,true},StockPlan=new TowerStockPlan {Pieces=new[]{new TowerStockPiece {StockLengthMm=3000,FirstSupport=0,LastSupport=2}}}};
        var fixedGeometry=TransferredSlabOptimizer.FixedGeometry(fixedLayout).Fixed.Single();
        Check(!fixedGeometry.HasNativeSupportSlices && fixedGeometry.HasMaterializedSupports,"explicit fixed-layout reconstruction keeps its former eager owned metadata");
    }
    static void ActualGeneratorAndAllocationCensus()
    {
        const int allocationSamples=256;var backing=Geometry();var held=new G[allocationSamples];
        for(int i=0;i<8;i++)G.Generated(backing.Stock,backing.Positions,backing.Joined,null);
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<held.Length;i++)held[i]=new G();long bare=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<held.Length;i++)held[i]=G.Generated(backing.Stock,backing.Positions,backing.Joined,null);long native=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(native==bare && held.All(g=>g.HasNativeSupportSlices && !g.HasMaterializedSupports),"trusted native registration allocates only its Geometry object, matching bare construction byte-for-byte");
        var common=ReportedScenarioTests.DiagnosticPart(3,1).Item2.CreateTowerInput();var network=common.Network;var input=common.CopyForSearch();
        input.LengthMm=network.Beams[0].SpanLength(common.LengthMm);input.LongitudinalStartMm=network.Beams[0].SpanStartMm;input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0);input.TransferredSlab=new TransferredSlabInput {IsNetwork=true};
        var cover=TransferredSlabOptimizer.StockCovers(input).First(c=>c.Length==2);int count=Math.Max(cover.Length*2,Math.Min(10,input.EngineerPlan.InitialLegs));
        var generated=EngineerTowerSearch.Geometries(cover,false,input,count-1,count).ToArray();Check(generated.Length>0,"actual source tier emits native geometry");
        long localArrays=0,localBytes=0;
        foreach(var g in generated)
        {
            Check(g.HasNativeSupportSlices && !g.HasMaterializedSupports,"actual generator retains zero eager local-support arrays");localArrays+=1+g.Stock.Length;
            localBytes+=24+8L*g.Stock.Length;
            for(int run=0;run<g.Stock.Length;run++)
            {
                var vector=g.Supports(run);localBytes+=24+8L*vector.Length;
                for(int i=0;i<vector.Length;i++)Bits(vector[i],g.Positions[g.Stock[run].FirstSupport+i]-g.Stock[run].StartMm,"actual lazy vectors preserve source global/local arithmetic");
            }
        }
        var translated=generated.SelectMany(g=>TransferredSlabOptimizer.SiteGeometries(g,input.StockEndSetbackMm)).FirstOrDefault(g=>!g.HasSupportMetadata);
        Check(translated!=null,"actual source includes a new site-translated geometry");translated.PrepareGeneratedSupports(null);
        Check(translated.HasNativeSupportSlices && !translated.HasMaterializedSupports,"site translations register their own shifted coordinates without local arrays");
        Console.WriteLine("NATIVE_SUPPORT_SLICE_CENSUS "+JsonSerializer.Serialize(new {Scope="Actual scenario3 source, one complete native station tier only; array-payload estimate, no whole-project RSS/runtime claim",Stocks=cover,StationTier=count,Geometries=generated.Length,EagerLocalArrays=0,FormerLocalArrays=localArrays,Estimated64BitClrLocalArrayBytesNotRetained=localBytes,MeasuredNativeFactoryBytesPerGeometry=native/allocationSamples,MeasuredBareGeometryBytesPerObject=bare/allocationSamples}));
    }
    internal static void Run(){ZeroEagerAndEarlyConsumers();PublicationAndFallbacks();ActualCallbackAndEscapeParity();ActualGeneratorAndAllocationCensus();Console.WriteLine("PASS "+checks+" native support-slice checks: zero eager arrays, typed early paths, exact source bits, owned whole-CAS publication, raw fallbacks, cancellation, full-callback/output parity and native allocation census");}
}
