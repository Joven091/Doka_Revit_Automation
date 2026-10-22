using System;
using System.Linq;
using EngineeringDashboard;

internal static class CrossingSearchTests
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception("Crossing search: "+name);}
    internal static void Run()
    {
        var input=ControlledPerformanceTests.Input("eight-crossings");var network=input.Network;
        Check(network.Beams.Length==8 && network.Bays.Length==7 && network.CrossingBeams.Length==7,"exact reported model size");
        Check(network.Bays.Select(b=>b.WidthMm).SequenceEqual(new[] {2677.0,1875,3275,2275,2278,2272,2252}),"reported unequal clear bays retained");
        input.AllowCustomH20Lengths=false;
        Check(CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"automatic X2 retains its new primary-bearing option");
        network.CrossingBeams[1].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        Check(!CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"1875 mm X2 cannot fit catalogue primary and permitted bracing");
        input.AllowCustomH20Lengths=true;
        Check(CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"custom primary is retained for full design checks");
        input.CustomH20Scope=CustomH20LengthScope.SlabSupports;
        Check(!CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"slab-only custom permission cannot supply a cut crossing primary");
        input.CustomH20Scope=CustomH20LengthScope.Both;input.AllowCustomH20Lengths=false;
        input.StockLengthsMm=new[] {1250.0,1300,1800,2450,3300,4900};
        Check(CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"1300 mm stock remains available for independent member checks");
        input.StockLengthsMm=new[] {1250.0,1500,1800,2450,3300,4900};
        Check(!CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"1500 mm does not fit two standard 200 mm gaps");
        input.AllowLocalShutterExceptions=true;
        Check(CrossBeamSupportOptimizer.MandatoryStocksFit(input,network),"local 175 mm detail can retain 1500 mm stock");
        input=ControlledPerformanceTests.Input("eight-crossings");input.AllowCustomH20Lengths=false;input.AllowSeparateSlabFallback=false;
        input.Network.CrossingBeams[1].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        input.Trace=new BeamSearchTrace("mandatory catalogue crossing geometry");bool rejected=false;
        try{TransferNetworkOptimizer.Optimize(input,input.Network);}
        catch(TowerOptimizationException ex){rejected=ex.Details.Contains("X2") && ex.Details.Contains("1300") && ex.Details.Contains("1475");}
        Check(rejected,"early failure identifies actual stock/bracing interval");
        Check(input.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==0 && input.Trace.Count(BeamSearchCounter.CoveragePlans)==0,"impossible catalogue pass needs no primary search");
        GeometryKeys();Frames();StockStarts();ForceBounds();ZonedPrimaryReuse();ResolvedRejection();ReceiverSearchReuse();LocalDependencyKeys();RecoveryParity();Parity();Console.WriteLine("Passed "+checks+" crossing search checks.");
    }
    static void RecoveryParity()
    {
        var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,
            AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.Both,StockLengthsMm=new[] {1250.0,1800,2450,2900},
            LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,DeflectionDenominator=270,
            AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=1};
        var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
            BeamSpacingsMm=Enumerable.Repeat(305.0,4).ToArray(),SlabSpacingsMm=Enumerable.Repeat(488.0,3).ToArray(),CrossingSpacingsMm=Enumerable.Repeat(350.0,3).ToArray(),
            Beams=Enumerable.Range(0,4).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=Enumerable.Range(0,3).Select(i=>new TransferBayRow {WidthMm=2200,ThicknessMm=200}).ToArray(),
            CrossingBeams=Enumerable.Range(1,3).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600}).ToArray()};
        input.ReuseReceiverSearches=false;input.WarmStartNetwork=false;input.LocalNetworkSearch=false;
        var expected=TransferNetworkOptimizer.Optimize(input,network);ControlledPerformanceTests.Validate(input,expected);
        input.ReuseReceiverSearches=true;input.WarmStartNetwork=true;input.LocalNetworkSearch=true;input.Trace=new BeamSearchTrace("warm spacing recovery parity");
        var actual=TransferNetworkOptimizer.Optimize(input,network);ControlledPerformanceTests.Validate(input,actual);
        Check(ControlledPerformanceTests.Fingerprint(actual)==ControlledPerformanceTests.Fingerprint(expected),"full ranking after spacing recovery retains geometry, loads and reactions");
        Check(actual.Input.BeamSpacingsMm.SequenceEqual(expected.Input.BeamSpacingsMm) && actual.Input.SlabSpacingsMm.SequenceEqual(expected.Input.SlabSpacingsMm),"warm recovery retains the same passing pitches");
        Check(input.Trace.Count(BeamSearchCounter.WarmNetworkCandidates)>0,"complete warm networks still receive all member and physical checks");
        Func<int,int,int,double,double,double,double,bool> mayImprove=(legs,narrow,bays,brace,cant,sym,location)=>TransferNetworkOptimizer.EngineerPrefixMayImprove(actual,legs,narrow,bays,brace,cant,sym,location);
        Check(mayImprove(actual.LegCount-2,100,100,1e6,1e6,1e6,1e6),"fewer possible legs retain a branch despite worse later criteria");
        Check(!mayImprove(actual.LegCount+2,0,0,0,0,0,0),"proven excess legs cannot improve the incumbent");
        Check(!mayImprove(actual.LegCount,actual.Standalone600Count+1,actual.BracedBayCount,actual.StandaloneBracePenalty,0,0,0),"proven worse narrow-tower count can be pruned");
        Check(!mayImprove(actual.LegCount,actual.Standalone600Count,actual.BracedBayCount+1,0,0,0,0),"proven worse braced-bay count can be pruned");
        Check(!mayImprove(actual.LegCount,actual.Standalone600Count,actual.BracedBayCount,actual.StandaloneBracePenalty+1,0,0,0),"proven worse brace preference can be pruned");
        Check(!mayImprove(actual.LegCount,actual.Standalone600Count,actual.BracedBayCount,actual.StandaloneBracePenalty,actual.CantileverImbalanceMm+1,0,0),"proven worse cantilevers can be pruned");
        Check(mayImprove(actual.LegCount,actual.Standalone600Count,actual.BracedBayCount,actual.StandaloneBracePenalty,actual.CantileverImbalanceMm,actual.SymmetryErrorMm,actual.BraceLocationPenalty),"equal geometry bounds retain possible timber improvements");
        var key=new CheckedPrefixKey(input,actual);Check(key.Equals(new CheckedPrefixKey(input,actual)),"identical immutable prefix is reusable");
        var prior=actual.Input.BeamSpacingsMm[0];actual.Input.BeamSpacingsMm[0]=275;
        Check(!key.Equals(new CheckedPrefixKey(input,actual)),"prefix pitch change invalidates its previous check");actual.Input.BeamSpacingsMm[0]=prior;
        var old=actual.Beams[0];actual.Beams[0]=new TowerLayout();
        Check(!key.Equals(new CheckedPrefixKey(input,actual)),"a different base receiver invalidates its prefix check");actual.Beams[0]=old;
    }
    static void LocalDependencyKeys()
    {
        var input=ControlledPerformanceTests.Input("eight-crossings");var network=input.Network;
        input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=800,BeamLineLoadKnM=35,UpperSpacingMm=305,
            StockCovers=new[] {new[] {3300.0,3600}},SharedRowsMm=new[] {250.0,550},LeftBaselineWidthsMm=new[] {250.0,250},RightBaselineWidthsMm=new[] {250.0,250},ReceivingRowsMm=new double[0]};
        var key=new ReceiverSolveKey(input,network,0,false);var remote=network.CrossingSpacingsMm[6];
        network.CrossingSpacingsMm[6]=250;Check(key.Equals(new ReceiverSolveKey(input,network,0,false)),"remote crossing pitch cannot invalidate unchanged local mathematical inputs");
        var local=network.CrossingSpacingsMm[0];network.CrossingSpacingsMm[0]=250;
        Check(!key.Equals(new ReceiverSolveKey(input,network,0,false)),"adjacent crossing pitch invalidates its receiver");network.CrossingSpacingsMm[0]=local;network.CrossingSpacingsMm[6]=remote;
        var identity=new ReceiverSolveKey(input,network,0,false,true);
        input.NetworkLegCeiling=12;input.EngineerStationLimit=6;
        Check(identity.Equals(new ReceiverSolveKey(input,network,0,false,true)),"identical solved geometry can retain identity across search-only ceilings");
        Check(!key.Equals(new ReceiverSolveKey(input,network,0,false)),"completed receiver search still retains its exact search ceiling");
        input.LegCapacityKn-=1;Check(!identity.Equals(new ReceiverSolveKey(input,network,0,false,true)),"capacity changes invalidate receiver identities");
    }
    static void ReceiverSearchReuse()
    {
        var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,
            StockLengthsMm=new[] {1250.0,1800,2450},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,
            DeflectionDenominator=270,AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=1,
            Trace=new BeamSearchTrace("complete receiver reuse"),NetworkReceiverWork=new NetworkReceiverCache()};
        var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
            BeamSpacingsMm=new[] {305.0,305,305},SlabSpacingsMm=new[] {488.0,488},CrossingSpacingsMm=new double[0],
            Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=Enumerable.Range(0,2).Select(i=>new TransferBayRow {WidthMm=2125,ThicknessMm=200}).ToArray()};
        var method=typeof(TransferNetworkOptimizer).GetMethod("Solve",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        double setback=362.5;
        Func<TowerLayout,TowerLayout> solve=previous=>
        {
            common.Diagnostics=new TowerSearchDiagnostics(270,trace:common.Trace,ignoreUplift:true);
            var plan=new UpperTransferPlan {StockLengthMm=1700,EdgeMm=212.5,NearFootMm=setback,FarFootMm=setback,IsCustomLength=true};
            return (TowerLayout)method.Invoke(null,new object[] {common,network,0,null,plan,2450.0,0.0,new[] {new[] {2450.0}},new TransferredSlabOptimizer.GeometryCache(),int.MaxValue,
                previous==null ? (double?)null : previous.WidthMm,0.0,null,previous});
        };
        common.AllowCustomH20Lengths=true;common.CustomH20Scope=CustomH20LengthScope.Both;
        var original=solve(null);if(original==null)throw common.Diagnostics.Exception("Receiver reuse fixture failed");Check(true,"receiver fixture passes its complete local checks");
        long solves=common.Trace.Count(BeamSearchCounter.NetworkSolves);
        var cached=solve(null);Check(ReferenceEquals(original,cached) && common.Trace.Count(BeamSearchCounter.NetworkSolves)==solves,"value-identical independently rebuilt slab plans reuse a complete receiver search");
        setback+=1e-10;solve(null);Check(common.Trace.Count(BeamSearchCounter.NetworkSolves)>solves,"receiver cache does not round support coordinates");setback=362.5;
        network.BeamSpacingsMm[1]=350;
        Check(ReferenceEquals(original,solve(null)),"unrelated beam pitch retains the exact local receiver result");
        network.SlabSpacingsMm[1]=407;solves=common.Trace.Count(BeamSearchCounter.NetworkSolves);
        var remapped=solve(null);
        Check(remapped!=null && common.Trace.Count(BeamSearchCounter.NetworkSolves)==solves,"remote slab pitch reuses the unchanged local geometry search");
        Check(original.ReceiverIdentity.Equals(remapped.ReceiverIdentity),"drawing registry rebuild retains exact local receiver identity");
        Check(!remapped.Transfer.SharedSlabRowsMm.SequenceEqual(original.Transfer.SharedSlabRowsMm)
            && remapped.Transfer.RowsMm.SequenceEqual(original.Transfer.RowsMm),"cache recheck refreshes global slab-source IDs while retaining correct local rows");
        network.BeamSpacingsMm[0]=275;network.UpperSpacingMm=275;
        var warm=solve(original);Check(warm!=null && warm.PositionsMm.SequenceEqual(original.PositionsMm),"new spacing can reuse the old support arrangement after full reanalysis");
        Check(warm.Transfer.EnteredUpperSpacingMm==275 && !warm.Transfer.RowsMm.SequenceEqual(original.Transfer.RowsMm),"warm recheck rebuilds actual rows and spacing metadata");
        foreach(var m in warm.Transfer.PrimaryCases.Concat(warm.Transfer.LowerCases).SelectMany(c=>c).Concat(warm.Transfer.UpperMembers))
        {
            var reference=LoadedBeamAnalysis.AnalyzeDenseReference(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,m.EI,270);
            Check(m.Analysis.DeflectionEvaluated && m.Utilization<=1+1e-8 && Math.Abs(m.Analysis.MomentKnM-reference.MomentKnM)<1e-7
                && Math.Abs(m.Analysis.ShearKn-reference.ShearKn)<1e-7 && Math.Abs(m.Analysis.DeflectionMm-reference.DeflectionMm)<1e-7,"rebuilt warm member retains independent full numerical checks");
        }
        common.LegCapacityKn=1;Check(solve(original)==null,"warm geometry cannot bypass a changed leg capacity");
        Check(solve(null)==null,"changed capacity invalidates the complete receiver cache");
        solves=common.Trace.Count(BeamSearchCounter.NetworkSolves);Check(solve(null)==null && common.Trace.Count(BeamSearchCounter.NetworkSolves)==solves,"a completed exact local failure is reusable");
        Check(common.Diagnostics.Exception("cached failure").Details.Contains("leg"),"cached failure retains its engineering explanation");
    }
    static void ZonedPrimaryReuse()
    {
        var input=new TowerInput {NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache(),Trace=new BeamSearchTrace("zoned crossing primary reuse")};
        var supports=new[] {250.0,1250,2550,3550};var random=new Random(191071);
        for(int sample=0;sample<80;sample++)
        {
            var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2975,KnPerM=random.NextDouble()*45},new BeamDistributedLoad {StartMm=2975,EndMm=3900,KnPerM=random.NextDouble()*70}};
            var points=new[] {new BeamPointLoad {PositionMm=3300,Kn=(random.NextDouble()-.5)*5}};
            var actual=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.PrimaryAnalyses,3900,supports,loads,points,input.Primary.EI,270,signed:true);
            var expected=LoadedBeamAnalysis.AnalyzeSigned(3900,supports,loads,points,input.Primary.EI,270);
            Func<double,double,bool> same=(a,b)=>Math.Abs(a-b)<1e-9*Math.Max(1,Math.Abs(b));
            Check(actual.DeflectionEvaluated && same(actual.MomentKnM,expected.MomentKnM) && same(actual.ShearKn,expected.ShearKn) && same(actual.DeflectionMm,expected.DeflectionMm),"multi-zone cached primary retains full extrema and deflection");
            Check(actual.Reactions.Zip(expected.Reactions,same).All(v=>v),"multi-zone primary retains each signed-load reaction");
        }
        Check(input.Trace.Count(BeamSearchCounter.PrimaryMatrixCacheHits)==79,"different zoned loads reuse the same stiffness factorization");
    }
    static void ResolvedRejection()
    {
        var input=new TowerInput {NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache(),LegCapacityKn=70,IgnoreUplift=true,Trace=new BeamSearchTrace("resolved primary staging")};
        var supports=new[] {300.0,1600,2900,3600};
        var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=3200,KnPerM=100},new BeamDistributedLoad {StartMm=3200,EndMm=3900,KnPerM=180}};
        var points=Array.Empty<BeamPointLoad>();
        var rejected=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.PrimaryAnalyses,3900,supports,loads,points,input.Primary.EI,270,true,true);
        Check(!rejected.DeflectionEvaluated && (rejected.ShearKn>22 || rejected.MomentKnM>10),"fully resolved strength failure skips deflection");
        var complete=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.PrimaryAnalyses,3900,supports,loads,points,input.Primary.EI,270,true);
        var dense=LoadedBeamAnalysis.AnalyzeDenseReference(3900,supports,loads,points,input.Primary.EI,270);
        Check(complete.DeflectionEvaluated && Math.Abs(complete.DeflectionMm-dense.DeflectionMm)<1e-7,"partial rejection cannot poison a later complete response");
        Check(rejected.Reactions.SequenceEqual(complete.Reactions) && rejected.MomentKnM==complete.MomentKnM && rejected.ShearKn==complete.ShearKn,"strength-first check retains exact forces and reactions");
        var near=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2450,KnPerM=80/(2.45*2.45)*(1+5e-9)}};
        var boundary=LoadedBeamAnalysis.AnalyzeCombinedCached(2450,new[] {0.0,2450},near,points,input.Primary.EI,270,null,70,true,true);
        Check(boundary.DeflectionEvaluated,"accepted strength tolerance cannot bypass deflection near the limit");
        input.PrimaryMaterial=PrimaryBeamMaterial.WS10;input.NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache();
        var waler=TransferredSlabOptimizer.AnalyzeTransverse(input,BeamSearchCounter.PrimaryAnalyses,3900,supports,loads,points,input.Primary.EI,270,true,true);
        Check(waler.DeflectionEvaluated,"WS10 keeps its full interaction and deflection response");
    }
    static void GeometryKeys()
    {
        var n=new TransferNetworkInput {UpperSpacingMm=350,Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750},new TransferBeamRow {WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}},Bays=new[] {new TransferBayRow {WidthMm=1875,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150}}};
        var r=new TransferNetworkResult {Input=n,BeamCentresMm=new[] {0.0,2600},Bays=new UpperTransferPlan[1],Beams=Enumerable.Range(0,2).Select(i=>new TowerLayout {LengthMm=2450,Transfer=new TransferredSlabResult {RowsMm=new[] {100.0,300,500},LowerStockStartMm=-1225,LowerStockLengthMm=2450}}).ToArray()};
        var obstacle=new FormworkSolid {Name="B1 primary",X0=0,X1=2450,Y0=700,Y1=780,Z0=-2150,Z1=-1950};
        var slab=new FormworkSolid {Name="S1 slab H20",X0=80,X1=160,Y0=600,Y1=1800,Z0=-500,Z1=-300};
        Func<CrossGeometryKey> key=()=>new CrossGeometryKey(r,0,CrossShutterAllowance.Standard,new[] {obstacle,slab});
        var original=key();Check(original.Equals(key()) && original.GetHashCode()==key().GetHashCode(),"identical immutable geometry key");
        r.Beams[0].Transfer.RowsMm[0]+=1e-10;Check(!original.Equals(key()),"no row rounding in geometry identity");r.Beams[0].Transfer.RowsMm[0]=100;
        obstacle.Y0+=1e-10;Check(!original.Equals(key()),"tiny obstacle changes invalidate reuse");obstacle.Y0=700;
        obstacle.IsTowerEnvelope=true;Check(!original.Equals(key()),"frame reservation rules are included");obstacle.IsTowerEnvelope=false;
        slab.Y0+=25;Check(original.Equals(key()),"nonintersecting high slab timber is independent of lower crossing search");
        r.Input.CrossingBeams[0].BottomElevationMm-=1;Check(!original.Equals(key()),"crossing elevation is included");r.Input.CrossingBeams[0].BottomElevationMm=-1150;
        Check(!original.Equals(new CrossGeometryKey(r,0,CrossShutterAllowance.Candidates[3],new[] {obstacle,slab})),"shutter detail is included");
        obstacle.Z0=-1000;var highPrimary=key();obstacle.Y0+=25;Check(!highPrimary.Equals(key()),"higher primary still contributes exact setting-out boundaries");
        var cache=new CrossGeometryCache();for(int i=0;i<150;i++){obstacle.Y0=i;cache.Results.Store(key(),new DedicatedCrossSearch {Failure="fixture"});}
        Check(cache.Results.Count==128,"geometry reuse has a fixed memory bound");
    }
    static void StockStarts()
    {
        var input=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.DoubleH20,OptimizeCrossingSearch=true};
        var obstacle=new FormworkSolid {Name="B2 primary",X0=75,X1=6825,Y0=70,Y1=230,Z0=-1550,Z1=-1350};
        var starts=CrossBeamSupportOptimizer.PrimaryStarts(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,new[] {obstacle});
        Check(starts[0]==-287.5 && starts.Contains(-282.5),"existing preferred start is retained before the exact five-millimetre clearance repair");
        Func<double,FormworkSolid> timber=start=>new FormworkSolid {X0=2608,X1=2768,Y0=512.5+start,Y1=512.5+start+1375,Z0=-1550,Z1=-1350};
        Check(timber(starts[0]).Conflicts(obstacle),"reported preferred primary position actually clashes");
        Check(starts.Any(start=>!timber(start).Conflicts(obstacle)),"same stock and support geometry can clear at another permitted start");
        Check(starts.All(start=>start>=-287.5 && start<=-269.5),"no primary alternative exceeds the stock/setback interval");
        input.OptimizeCrossingSearch=false;
        Check(CrossBeamSupportOptimizer.PrimaryStarts(input,-287.5,-287.5,-269.5,1375,512.5,3450,1524,-1350,new[] {obstacle}).SequenceEqual(new[] {-287.5}),"reference retains the former one-position search");
    }
    static void ForceBounds()
    {
        var random=new Random(191070);
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        for(int sample=0;sample<300;sample++)
        {
            var input=new TowerInput {PrimaryMaterial=material};double length=1250+random.Next(3600),a=50+random.Next(400),b=length-50-random.Next(400);
            var supports=sample%2==0 ? new[] {a,b} : new[] {a,(a+b)/2,b};
            var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=random.NextDouble()*100},new BeamDistributedLoad {StartMm=length*.2,EndMm=length*.8,KnPerM=random.NextDouble()*50}};
            bool accepted=CrossBeamSupportOptimizer.PrimaryForcesFit(input,length,supports,loads,"independent bound fixture");
            var reference=LoadedBeamAnalysis.AnalyzeDenseReference(length,supports,loads,new BeamPointLoad[0],input.Primary.EI,270);
            Check(accepted || reference.ShearKn>input.Primary.ShearLimitKn+1e-8 || reference.MomentKnM>input.Primary.MomentLimitKnM+1e-8,"force screen never rejects an independently passing member");
        }
        var h20=new TowerInput();var q=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=34}};
        Check(!CrossBeamSupportOptimizer.PrimaryForcesFit(h20,2000,new[] {200.0,1724},q,"reported narrow receiver"),"34 kN/m cannot fit a 1524 mm span within two 22 kN shear limits");
        q[0].KnPerM=44/1.524;
        Check(CrossBeamSupportOptimizer.PrimaryForcesFit(h20,2000,new[] {200.0,1724},q,"capacity boundary"),"exact shear force boundary proceeds to full checks");
    }
    static void Parity()
    {
        foreach(bool dedicated in new[] {false,true})
        {
            var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,StockLengthsMm=new[] {1250.0,1800,2450,2900},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=175,DeflectionDenominator=270,AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=1};
            var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new[] {350.0},Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600,SupportMode=dedicated ? CrossBeamSupportMode.DedicatedTowers : CrossBeamSupportMode.Automatic}}};
            common.OptimizeCrossingSearch=false;var expected=TransferNetworkOptimizer.Optimize(common,network);
            common.OptimizeCrossingSearch=true;common.Trace=new BeamSearchTrace("crossing reuse parity");var actual=TransferNetworkOptimizer.Optimize(common,network);
            Check(actual.LegCount<=expected.LegCount,"complete shared/dedicated search retains or improves leg count");
            Func<TransferNetworkResult,double> load=r=>r.Beams.Sum(b=>b.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn)+b.Transfer.ColumnSupportLoadKn(0))
                +r.CrossSupports.Where(c=>c.Towers!=null).Sum(c=>c.Towers.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn));
            Check(Math.Abs(load(expected)-load(actual))<1e-6,"complete main/dedicated/column system conserves the same applied force");
            ControlledPerformanceTests.Validate(common,actual);
            common.FirstCompleteNetworkOnly=true;common.Trace=new BeamSearchTrace("first checked network");
            var seed=TransferNetworkOptimizer.Optimize(common,network);ControlledPerformanceTests.Validate(common,seed);
            Check(common.Trace.Count(BeamSearchCounter.CompleteNetworkCandidates)==1,"feasibility traversal stops after a complete checked network");
            common.FirstCompleteNetworkOnly=false;common.Trace=new BeamSearchTrace("final full ranking");
            var ranked=TransferNetworkOptimizer.Optimize(common,network);
            Check(ControlledPerformanceTests.Fingerprint(ranked)==ControlledPerformanceTests.Fingerprint(actual),"full ranking after a feasibility seed retains its deterministic result");
        }
    }
    static void Frames()
    {
        var input=ControlledPerformanceTests.Input("eight-crossings");var n=input.Network;
        foreach(var c in n.CrossingBeams)c.SupportMode=CrossBeamSupportMode.DedicatedTowers;
        var g=new TransferredSlabOptimizer.Geometry {Positions=new[] {150.0,1150,2150,3150,4150,5150,6150},Joined=Enumerable.Repeat(true,6).ToArray()};
        Check(!CrossBeamSupportOptimizer.ReceiverFramesFit(input,n,1,g,0,2000,0),"wide continuous B2 frame blocks a mandatory X2 tower");
        Check(CrossBeamSupportOptimizer.ReceiverFramesFit(input,n,1,g,0,1524,0),"receiver screen retains an off-centre possibility until the other beam is known");
        Check(CrossBeamSupportOptimizer.ReceiverFramesFit(input,n,1,g,0,1000,0),"narrow B2 frame retains a possible X2 tower");
        var method=typeof(CrossBeamSupportOptimizer).GetMethod("FrameCentreExists",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var random=new Random(261019);
        for(int sample=0;sample<500;sample++)
        {
            var frames=Enumerable.Range(0,1+sample%6).Select(i=>
            {
                double x=random.Next(500),y=random.Next(500);
                return new[] {x,x+random.Next(200),y,y+random.Next(200)};
            }).ToList();
            double hx=10+random.Next(100),hy=10+random.Next(100);
            bool actual=(bool)method.Invoke(null,new object[] {frames,100.0,400.0,100.0,400.0,hx,hy});
            // Independent physical witnesses use the actual strict solid-overlap
            // predicate, rather than the screen's expanded forbidden rectangles.
            bool witness=false;
            for(int x=100;x<=400 && !witness;x+=10)for(int y=100;y<=400;y+=10)
            {
                var candidate=new FormworkSolid {X0=x-hx,X1=x+hx,Y0=y-hy,Y1=y+hy,Z0=-1,Z1=1};
                if(frames.All(f=>!candidate.Overlaps(new FormworkSolid {X0=f[0],X1=f[1],Y0=f[2],Y1=f[3],Z0=-1,Z1=1}))){witness=true;break;}
            }
            Check(!witness || actual,"continuous footprint screen retains every independently sampled valid placement");
        }
    }
}
