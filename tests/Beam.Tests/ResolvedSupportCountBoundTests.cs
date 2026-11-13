using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class ResolvedSupportCountBoundTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Resolved support-count bound: "+message);}
    static TowerInput Input(double capacity=70,bool optimized=true,CancellationToken token=default)=>new TowerInput {
        EngineerSearch=true,FullLoadingOnly=true,LengthMm=2450,LegCapacityKn=capacity,DeflectionDenominator=270,
        IgnoreUplift=true,OptimizeCrossingSearch=optimized,Trace=new BeamSearchTrace("resolved UDL support-count bound",token),
        Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true),TransferredSlab=new TransferredSlabInput()};
    static TransferredSlabOptimizer.Geometry Geometry()=>new TransferredSlabOptimizer.Geometry {
        Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=2450,FirstSupport=0,LastSupport=1}},
        Positions=new[] {200.0,2250},Joined=new[] {true},MemberSupports=new[] {new[] {200.0,2250}}};
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Prepare(TowerInput input,double force=0)
    {
        var cross=new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=100,DepthMm=200,BottomElevationMm=-200};
        var network=new TransferNetworkInput {
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=200,DepthMm=300,BottomElevationMm=-300}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},CrossingBeams=new[] {cross}};
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1800,KnPerM=.5}};
        var lower=new TransferMember {H20Count=1,Name="T1",LengthMm=1800,StartMm=-900,SupportsMm=new[] {138.0,1662},
            Uniform=uniform,Points=Array.Empty<BeamPointLoad>(),Analysis=LoadedBeamAnalysis.Analyze(1800,new[] {138.0,1662},uniform,null,TowerOptimizer.PairEI/2,270)};
        var probe=new TowerLayout {PrimaryMaterial=input.PrimaryMaterial,LengthMm=2450,WidthMm=1524,PositionsMm=new[] {200.0,2250},Joined=new[] {true},
            StockPlan=new TowerStockPlan {Pieces=Geometry().Stock},
            Transfer=new TransferredSlabResult {PrimaryPlan=EngineerPrimaryPlan.Create(input,network,0,false,false),RowsMm=new[] {1225.0},
                CaseNames=new[] {"Beam"},LowerCases=new[] {new[] {lower}}}};
        var support=new CrossBeamSupportResult {Input=cross,CrossingIndex=0,Bearings=new[] {
            new CrossBeamBearing {Beam=0,Row=-1,PrimaryLine=0,Xmm=1225,Ymm=0,ReactionKn=force}}};
        var method=typeof(CrossBeamSupportOptimizer).GetMethod("PrepareRecheck",BindingFlags.NonPublic|BindingFlags.Static);
        try{return (Func<TransferredSlabOptimizer.Geometry,double,bool>)method.Invoke(null,new object[] {input,network,0,probe,new[] {support}});}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void NumericalNecessaryCondition()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(double capacity in new[] {15.0,70,250})
        foreach(int count in new[] {2,3,4,6})
        foreach(double q in new[] {.1,10,25,50,100,250})
        {
            var input=Input(capacity);input.PrimaryMaterial=material;
            var supports=Enumerable.Range(0,count).Select(i=>150+3700.0*i/(count-1)).ToArray();
            var loads=new[] {new BeamDistributedLoad {StartMm=0,EndMm=1700,KnPerM=q},new BeamDistributedLoad {StartMm=1700,EndMm=4000,KnPerM=q*.7}};
            double force=loads.Sum(load=>load.KnPerM*(load.EndMm-load.StartMm)/1000);
            bool permitted=CrossBeamSupportOptimizer.PrimarySupportCountFits(input,count,force);
            var dense=LoadedBeamAnalysis.AnalyzeDenseReference(4000,supports,loads,null,input.Primary.EI,270);
            Check(Math.Abs(dense.Reactions.Sum()-force)<1e-7,"independent dense member conserves its distributed force");
            Check(permitted || dense.ShearKn>input.Primary.ShearLimitKn+1e-8 || dense.Reactions.Max()>capacity+1e-8,
                "every rejected support count independently fails shear or a positive leg reaction");
        }
        var uplift=Input(1);
        Check(CrossBeamSupportOptimizer.PrimarySupportCountFits(uplift,2,-40),"permitted uplift is not bounded by the positive leg capacity");
        var signed=LoadedBeamAnalysis.AnalyzeSigned(2450,new[] {475.0,1975},new[] {new BeamDistributedLoad {StartMm=0,EndMm=2450,KnPerM=-40/2.45}},null,uplift.Primary.EI,270);
        Check(signed.Reactions.Max()< -1 && signed.ShearKn<uplift.Primary.ShearLimitKn,"signed member provides a physical counterexample to using the leg limit on negative resultants");
        Check(!CrossBeamSupportOptimizer.PrimarySupportCountFits(uplift,2,40),"the same downward resultant obeys positive leg capacity");
        Check(!CrossBeamSupportOptimizer.PrimarySupportCountFits(uplift,2,-89),"negative resultants still obey both member face shears");
        var shear=Input(70);
        Check(CrossBeamSupportOptimizer.PrimarySupportCountFits(shear,2,88+1e-5),"near-threshold numerical uncertainty is retained");
        Check(!CrossBeamSupportOptimizer.PrimarySupportCountFits(shear,2,88.01),"a strict positive shear-jump violation is rejected");
    }
    static void PreparedParity()
    {
        var bounded=Input(1);var reference=Input(1,false);var check=Prepare(bounded);var old=Prepare(reference);
        long boundedBefore=bounded.Trace.Count(BeamSearchCounter.PrimaryAnalyses),referenceBefore=reference.Trace.Count(BeamSearchCounter.PrimaryAnalyses);
        Check(!check(Geometry(),0) && !old(Geometry(),0),"the early total-force bound matches the original complete resolved member rejection");
        Check(bounded.Trace.Count(BeamSearchCounter.PrimaryAnalyses)==boundedBefore
            && reference.Trace.Count(BeamSearchCounter.PrimaryAnalyses)>referenceBefore,"impossible support capacity is rejected before primary numerical analysis");
        Check(bounded.Diagnostics.Exception("rejected stock").Details.Contains("support-count force lower bound"),"the rejection has a concrete engineering explanation");
        var passing=Input();var passingReference=Input(70,false);check=Prepare(passing);old=Prepare(passingReference);
        boundedBefore=passing.Trace.Count(BeamSearchCounter.PrimaryAnalyses);
        Check(check(Geometry(),0) && old(Geometry(),0),"passing resolved geometry retains the original acceptance result");
        Check(passing.Trace.Count(BeamSearchCounter.PrimaryAnalyses)>boundedBefore,"passing candidates still execute their complete primary member analyses");
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=Input(1,true,cancellation.Token);var pending=Prepare(interrupted);cancellation.Cancel();bool threw=false;
            try{pending(Geometry(),0);}catch(OperationCanceledException){threw=true;}
            Check(threw,"early force bounds preserve cancellation");
            threw=false;try{PreparedReceiverSupportBounds.Minimum(pending,new[] {2450.0},0);}catch(OperationCanceledException){threw=true;}
            Check(threw,"early cover-count queries preserve cancellation");
            threw=false;try{PreparedReceiverSupportBounds.MayFit(pending,Geometry(),0);}catch(OperationCanceledException){threw=true;}
            Check(threw,"early per-stock queries preserve cancellation");
        }
        var invalid=Input();invalid.PrimaryMaterial=PrimaryBeamMaterial.WS10;var invalidStock=Prepare(invalid);
        Check(PreparedReceiverSupportBounds.Minimum(invalidStock,new[] {2450.0},0)==0,"invalid WS10 stock supplies no early metadata proof");
        Check(PreparedReceiverSupportBounds.MayFit(invalidStock,Geometry(),0),"invalid WS10 stock supplies no early per-stock rejection");
        bool unsupportedThrew=false;try{invalidStock(Geometry(),0);}catch(ArgumentException){unsupportedThrew=true;}
        Check(unsupportedThrew,"the original callback retains its unsupported-stock behavior after an inconclusive floor query");
    }
    static bool OriginalFits(TowerInput input,int count,double force)
    {
        double per=2*input.Primary.ShearLimitKn;if(force>=0)per=Math.Min(per,input.LegCapacityKn);
        double limit=count*per,demand=Math.Abs(force),margin=1e-6+Math.Max(1,Math.Max(limit,demand))*1e-6;
        return demand<=limit+margin;
    }
    static void MinimumPredicateParity()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(double capacity in new[] {1.0,22,44,70,164,250})
        foreach(double sign in new[] {-1.0,1.0})
        for(int count=0;count<=40;count++)
        {
            var input=Input(capacity);input.Diagnostics=null;input.PrimaryMaterial=material;
            double per=sign<0 ? 2*input.Primary.ShearLimitKn : Math.Min(2*input.Primary.ShearLimitKn,capacity);
            double limit=count*per;
            double boundary=(limit+1e-6)/(1-1e-6);
            foreach(double magnitude in new[] {0,limit,PortableTestRuntime.NextDown(boundary),boundary,PortableTestRuntime.NextUp(boundary),boundary+1e-3,1e20})
            {
                double force=sign*magnitude;int floor=CrossBeamSupportOptimizer.PrimarySupportCountFloor(input,force);
                for(int supports=Math.Max(0,count-1);supports<=Math.Min(40,count+1);supports++)
                    Check(OriginalFits(input,supports,force)==(supports>=floor),"minimum count preserves the original signed predicate at exact/ULP tolerance boundaries");
            }
        }
    }
    static Func<TransferredSlabOptimizer.Geometry,double,bool> Combine(string method,Func<TransferredSlabOptimizer.Geometry,double,bool> a,Func<TransferredSlabOptimizer.Geometry,double,bool> b)
        =>(Func<TransferredSlabOptimizer.Geometry,double,bool>)typeof(CrossBeamSupportOptimizer).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {a,b});
    static bool AcceptAtLeastTwo(TransferredSlabOptimizer.Geometry g,double e)=>g.Positions.Length>=2;
    static void Composition()
    {
        var stocks=new[] {2450.0};
        var a=PreparedReceiverSupportBounds.Attach((g,e)=>g.Positions.Length>=3,(s,e)=>3);
        var b=PreparedReceiverSupportBounds.Attach((g,e)=>g.Positions.Length>=7,(s,e)=>7);
        Func<TransferredSlabOptimizer.Geometry,double,bool> unknown=(g,e)=>true;
        var either=Combine("EitherReceiver",a,b);var both=Combine("BothReceivers",a,b);
        Check(PreparedReceiverSupportBounds.Minimum(either,stocks,0)==3,"OR retains the weaker necessary count");
        Check(PreparedReceiverSupportBounds.Minimum(both,stocks,0)==7,"AND retains the stronger necessary count");
        for(int count=0;count<=9;count++)
        {
            var geometry=new TransferredSlabOptimizer.Geometry {Positions=new double[count]};
            Check(either(geometry,0)==(a(geometry,0)||b(geometry,0)) && both(geometry,0)==(a(geometry,0)&&b(geometry,0)),"composed callback acceptance and order are unchanged");
        }
        Check(PreparedReceiverSupportBounds.Minimum(Combine("EitherReceiver",a,unknown),stocks,0)==0,"unknown accepting OR fallback prevents an unsafe count bound");
        Check(PreparedReceiverSupportBounds.Minimum(Combine("BothReceivers",a,unknown),stocks,0)==3,"unknown extra AND restriction retains the known floor");
        Check(PreparedReceiverSupportBounds.Minimum(Combine("EitherReceiver",a,null),stocks,0)==3,"null remains an absent OR branch");
        Check(PreparedReceiverSupportBounds.Minimum(Combine("BothReceivers",a,CrossBeamSupportOptimizer.FailedReceiver),stocks,0)==int.MaxValue,"failed AND is impossible");
        Check(PreparedReceiverSupportBounds.Minimum(Combine("EitherReceiver",a,CrossBeamSupportOptimizer.FailedReceiver),stocks,0)==3,"failed OR does not strengthen the surviving branch");
        var framed=PreparedReceiverSupportBounds.Preserve((g,e)=>g.Positions.Length%2==0 && a(g,e),a);
        Check(PreparedReceiverSupportBounds.Minimum(framed,stocks,0)==3 && !framed(new TransferredSlabOptimizer.Geometry {Positions=new double[3]},0),"extra frame AND checks retain metadata and their ordinary rejection");
        var first=new Func<TransferredSlabOptimizer.Geometry,double,bool>(AcceptAtLeastTwo);
        var equal=new Func<TransferredSlabOptimizer.Geometry,double,bool>(AcceptAtLeastTwo);
        PreparedReceiverSupportBounds.Attach(first,(s,e)=>2);
        Check(first.Equals(equal) && !ReferenceEquals(first,equal) && PreparedReceiverSupportBounds.Minimum(equal,stocks,0)==0,"metadata keys use exact delegate identity");
    }
    static TransferredSlabOptimizer.Geometry RunGeometry(double[] stocks,int[] counts,double secondStart=double.NaN)
    {
        double start=0;var pieces=new TowerStockPiece[stocks.Length];var members=new double[stocks.Length][];
        var positions=new System.Collections.Generic.List<double>();
        for(int run=0;run<stocks.Length;run++)
        {
            if(run==1 && !double.IsNaN(secondStart))start=secondStart;
            pieces[run]=new TowerStockPiece{StartMm=start,StockLengthMm=stocks[run],FirstSupport=positions.Count,LastSupport=positions.Count+counts[run]-1};
            members[run]=Enumerable.Range(0,counts[run]).Select(i=>150+(stocks[run]-300)*i/Math.Max(1,counts[run]-1)).ToArray();
            positions.AddRange(members[run].Select(x=>start+x));start+=stocks[run];
        }
        return new TransferredSlabOptimizer.Geometry{Stock=pieces,MemberSupports=members,Positions=positions.ToArray(),Joined=new bool[Math.Max(0,positions.Count-1)]};
    }
    static void RunComposition()
    {
        var stocks=new[]{3900.0,2650};
        Func<TransferredSlabOptimizer.Geometry,double,bool> a=(g,e)=>g.MemberSupports[0].Length>=4 && g.MemberSupports[1].Length>=2;
        Func<TransferredSlabOptimizer.Geometry,double,bool> b=(g,e)=>g.MemberSupports[0].Length>=2 && g.MemberSupports[1].Length>=4;
        PreparedReceiverSupportBounds.Attach(a,(s,e)=>6,a);PreparedReceiverSupportBounds.Attach(b,(s,e)=>6,b);
        var either=Combine("EitherReceiver",a,b);var both=Combine("BothReceivers",a,b);
        foreach(var counts in new[]{new[]{2,4},new[]{4,2},new[]{3,3},new[]{4,4}})
        {
            var geometry=RunGeometry(stocks,counts);
            Check(PreparedReceiverSupportBounds.MayFit(either,geometry,0)==(a(geometry,0)||b(geometry,0)),"OR keeps a geometry when either run distribution may fit");
            Check(PreparedReceiverSupportBounds.MayFit(both,geometry,0)==(a(geometry,0)&&b(geometry,0)),"AND can reject a geometry when either necessary run distribution fails");
        }
        var wrong=RunGeometry(stocks,new[]{3,3});Func<TransferredSlabOptimizer.Geometry,double,bool> unknown=(g,e)=>true;
        Check(PreparedReceiverSupportBounds.Minimum(either,stocks,0)==wrong.Positions.Length && !PreparedReceiverSupportBounds.MayFit(either,wrong,0),"adequate total supports do not satisfy either incompatible per-stock distribution");
        Check(PreparedReceiverSupportBounds.MayFit(Combine("EitherReceiver",a,unknown),wrong,0),"unknown OR fallback cannot be rejected by a known run distribution");
        Check(!PreparedReceiverSupportBounds.MayFit(Combine("BothReceivers",a,unknown),wrong,0),"unknown AND condition retains the known necessary run distribution");
        Check(PreparedReceiverSupportBounds.MayFit(null,wrong,0) && !PreparedReceiverSupportBounds.MayFit(CrossBeamSupportOptimizer.FailedReceiver,wrong,0),"null and failed run metadata remain optimistic and impossible respectively");
        Check(!PreparedReceiverSupportBounds.MayFit(PreparedReceiverSupportBounds.Preserve((g,e)=>a(g,e),a),wrong,0),"frame wrappers retain exact run-count metadata");
    }
    static void PreparedMetadata()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(double capacity in new[] {1.0,70,250})foreach(double crossingForce in new[] {-135.0,0,135})
        {
            var input=Input(capacity);input.PrimaryMaterial=material;input.Diagnostics=null;
            var prepared=Prepare(input,crossingForce);
            var stocks=material==PrimaryBeamMaterial.WS10 ? new[] {1250.0,1250} : new[] {1250.0,1200};
            foreach(double edge in new[] {0.0,PortableTestRuntime.NextUp(0.0),.000000001,150.0})
            {
                double start=edge;int expected=0;var required=new System.Collections.Generic.List<int>();
                foreach(double length in stocks)
                {
                    double own=new TransferNetworkInput().AreaLoad(300)*.2/2*Math.Max(0,Math.Min(start+length,2450)-Math.Max(start,0))/1000;
                    double crossing=crossingForce*Math.Max(0,Math.Min(start+length,1275)-Math.Max(start,1175))/100;
                    double weight=input.Primary.SelfWeightKnM(length)*length/1000;
                    int left=0,right=0;
                    while(left<=40 && !OriginalFits(input,left,own+crossing+weight))left++;
                    while(right<=40 && !OriginalFits(input,right,own+weight))right++;
                    int count=Math.Max(2,Math.Max(left,right));required.Add(count);
                    expected=Math.Min(41,expected+count);start+=length;
                }
                Check(PreparedReceiverSupportBounds.Minimum(prepared,stocks,edge)==expected,"prepared floor clips exact signed loads and stock-specific WS10 weight, then sums the strongest line per independent stock");
                double origin=edge;
                foreach(double length in stocks){PreparedReceiverSupportBounds.Minimum(prepared,new[]{length},origin);origin+=length;}
                var geometry=RunGeometry(stocks,required.ToArray());
                bool mayFit=PreparedReceiverSupportBounds.MayFit(prepared,geometry,edge);
                Check(mayFit || !prepared(geometry,edge),"a resolved span rejection beyond the count floor also fails the unchanged ordinary callback");
                for(int run=0;run<stocks.Length;run++)
                {
                    var fewer=required.ToArray();fewer[run]--;
                    Check(!PreparedReceiverSupportBounds.MayFit(prepared,RunGeometry(stocks,fewer),edge),"a missing support on either independent stock fails its own line-force floor");
                }
            }
            input.OptimizeCrossingSearch=false;
            Check(PreparedReceiverSupportBounds.Minimum(prepared,stocks,0)==0,"disabled primitive keeps the original callback behavior");
            Check(PreparedReceiverSupportBounds.MayFit(prepared,RunGeometry(stocks,new[]{2,2}),0),"disabled primitive supplies no run-distribution rejection");
        }
        var mismatched=Input(1);var exact=Prepare(mismatched,3);var cover=new[]{1250.0,1200};
        var wrong=RunGeometry(cover,new[]{2,4});
        Check(PreparedReceiverSupportBounds.Minimum(exact,cover,0)==wrong.Positions.Length && !PreparedReceiverSupportBounds.MayFit(exact,wrong,0),"real resolved loads reject an adequate total assigned to the wrong independent stock");
        Check(!exact(wrong,0),"the unchanged actual receiver callback also rejects the wrong stock distribution");
        var shifted=RunGeometry(cover,new[]{4,2},1175);
        Check(PreparedReceiverSupportBounds.MayFit(exact,shifted,0),"an unprepared exact stock origin remains inconclusive");
        PreparedReceiverSupportBounds.Minimum(exact,new[]{1200.0},1175);
        Check(PreparedReceiverSupportBounds.MayFit(exact,RunGeometry(cover,new[]{4,2}),0)
            && !PreparedReceiverSupportBounds.MayFit(exact,shifted,0),"run metadata uses each actual stock origin rather than a cover-prefix approximation");
    }
    static TowerInput CoverInput()
    {
        var input=Input(1);input.DeferCrossingChecks=true;input.EngineerStationLimit=2;input.NetworkLegCeiling=4;input.StockLengthsMm=new[] {2450.0};input.MaximumConcreteEndOverhangMm=0;
        input.PrepareSpanGeometry=false;
        var network=new TransferNetworkInput {
            Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=200,DepthMm=300}).ToArray(),
            Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},
            CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=100,DepthMm=200}}};
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0,false,false);
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=200,BeamLineLoadKnM=network.AreaLoad(300)*.2,
            UpperSpacingMm=305,PlywoodSheetLengthMm=2440,FixedCoveredLengthMm=2450,FixedTowerWidthMm=1524,StockCovers=new[] {new[] {2450.0}}};
        input.TransferLowerAcceptance=probe=>true;input.TransferReceiverPreparation=probe=>Prepare(input);
        return input;
    }
    static void SolveRejected(TowerInput input)
    {
        var method=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",BindingFlags.Static|BindingFlags.NonPublic);
        try{method.Invoke(null,new object[] {input,true,null});throw new Exception("Expected rejection");}
        catch(TargetInvocationException error){if(!(error.InnerException is TowerOptimizationException))System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();}
    }
    static void BeforeGeometry()
    {
        var bounded=CoverInput();SolveRejected(bounded);
        Check(bounded.Trace.Count(BeamSearchCounter.GeometryBuilds)==0,"an actual prepared resolved floor above the ceiling rejects the cover before SearchGeometries");
        Check(bounded.Diagnostics.Exception("no candidate").Details.Contains("at least 3 support stations") && bounded.Diagnostics.Exception("no candidate").Details.Contains("ceiling allows 2"),"pre-geometry rejection retains its auditable required and available support counts");
        foreach(Action<TowerInput> disable in new Action<TowerInput>[] {
            i=>i.ReuseTransversePreparations=false,i=>i.PrepareReceiverChecks=false,i=>i.ReuseSearchWork=false,i=>i.OptimizeCrossingSearch=false,
            i=>i.Trace=new BeamSearchTrace("deadline path unchanged",searchTimeLimit:TimeSpan.FromHours(1)),
            i=>i.TransferReceiverPreparation=p=>(g,e)=>false})
        {
            var reference=CoverInput();disable(reference);SolveRejected(reference);
            Check(reference.Trace.Count(BeamSearchCounter.GeometryBuilds)>0,"disabled flags and unrecognized external callbacks retain the original geometry path");
        }
        var fixedInput=CoverInput();var fixedGeometry=Geometry();
        fixedInput.TransferredSlab.SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache {Fixed=new[] {fixedGeometry}};
        SolveRejected(fixedInput);
        Check(fixedGeometry.Rankings!=null,"fixed geometry caches bypass the new cover-derived floor");
    }
    static TowerInput DistributionInput(bool perStock)
    {
        var input=CoverInput();input.LengthMm=6550;input.LegCapacityKn=2;input.EngineerStationLimit=7;input.NetworkLegCeiling=14;
        input.StockLengthsMm=new[]{2650.0,3900};input.PrepareSpanGeometry=true;
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,input.EngineerPlan.Network,0,false,false);
        input.TransferredSlab.FixedCoveredLengthMm=6550;input.TransferredSlab.StockCovers=new[]{new[]{3900.0,2650}};
        input.TransferredSlab.SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache();
        input.TransferReceiverPreparation=probe=>
        {
            var prepared=Prepare(input,5);
            return perStock ? prepared : PreparedReceiverSupportBounds.Attach((g,e)=>prepared(g,e),(s,e)=>PreparedReceiverSupportBounds.Minimum(prepared,s,e));
        };
        return input;
    }
    static TowerLayout OptionalSolve(TowerInput input)
    {
        var method=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",BindingFlags.Static|BindingFlags.NonPublic);
        try{return (TowerLayout)method.Invoke(null,new object[]{input,true,null});}
        catch(TargetInvocationException error)
        {
            if(error.InnerException is TowerOptimizationException)return null;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;
        }
    }
    static string LayoutIdentity(TowerLayout layout)
        =>layout==null ? "rejected" : string.Join(",",layout.PositionsMm)+"|"+string.Join(",",layout.Joined)+"|"+layout.WidthMm+"|"
            +string.Join(";",layout.Transfer.PrimaryCases.SelectMany(s=>s).Select(m=>m.Name+":"+string.Join(",",m.Analysis.Reactions)+":"+m.Analysis.MomentKnM+":"+m.Analysis.ShearKn));
    static void BeforeSpanAndRank()
    {
        var actual=DistributionInput(true);var reference=DistributionInput(false);var stocks=actual.TransferredSlab.StockCovers[0];
        var boundedGeometry=TransferredSlabOptimizer.SearchGeometries(actual,actual.TransferredSlab.SharedGeometryCache,stocks,false);
        var referenceGeometry=TransferredSlabOptimizer.SearchGeometries(reference,reference.TransferredSlab.SharedGeometryCache,stocks,false);
        var prepared=Prepare(actual,5);int floor=PreparedReceiverSupportBounds.Minimum(prepared,stocks,0);
        Check(floor==7,"integration fixture has an adequate seven-station total and a five-plus-two independent stock floor");
        var rejected=boundedGeometry.Where(g=>g.Positions.Length==7 && !PreparedReceiverSupportBounds.MayFit(prepared,g,0)).ToArray();
        Check(rejected.Length>0,"existing unchanged geometry generation includes adequate totals assigned to the wrong independent stock");
        var expected=OptionalSolve(reference);var result=OptionalSolve(actual);
        Check(LayoutIdentity(result)==LayoutIdentity(expected),"per-stock filtering preserves the scalar-only path's selected geometry and full primary forces");
        Check(rejected.All(g=>g.SpanShape==null && g.Rankings==null),"wrong run distributions are rejected before span preparation and rank metadata");
        Check(referenceGeometry.Any(g=>g.Positions.Length==7 && g.Stock[0].LastSupport-g.Stock[0].FirstSupport+1<5 && g.SpanShape!=null),"the same distributions enter span preparation on the previous scalar-only path");
        Check(actual.Trace.Count(BeamSearchCounter.TransferCandidates)<=reference.Trace.Count(BeamSearchCounter.TransferCandidates),"necessary run filtering does not add candidate evaluations");
        Check(actual.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)>0 && reference.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)==0,"the counter isolates early per-stock rejections from the scalar-only path");
        Console.WriteLine("Per-stock integration: "+boundedGeometry.Length+" generated; "+rejected.Length+" wrong distributions; "+actual.Trace.Count(BeamSearchCounter.ResolvedStockCountsRejected)+" rejected before spans/grid/rank.");
    }
    internal static void Run()
    {NumericalNecessaryCondition();PreparedParity();MinimumPredicateParity();Composition();RunComposition();PreparedMetadata();BeforeGeometry();BeforeSpanAndRank();Console.WriteLine("PASS "+checks+" resolved support-count checks: dense proof, signed exact predicate, callback algebra, stock self-weight, early cover/run rejection and disabled/fixed paths");}
}
