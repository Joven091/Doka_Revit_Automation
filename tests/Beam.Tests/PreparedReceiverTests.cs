using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class PreparedReceiverTests
{
    static int checks;
    static void Check(bool passed,string name){checks++;if(!passed)throw new Exception("Prepared receiver: "+name);}
    internal static void Run()
    {
        foreach(bool dedicated in new[] {false,true})
        {
            var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,StockLengthsMm=new[] {1250.0,1800,2450,2900},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=175,DeflectionDenominator=270,AllowSeparateSlabFallback=false,IgnoreUplift=true,NetworkSearchWorkers=1};
            var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new[] {350.0},
                Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}},
                CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600,SupportMode=dedicated ? CrossBeamSupportMode.DedicatedTowers : CrossBeamSupportMode.Automatic}}};
            common.PrepareReceiverChecks=false;var expected=TransferNetworkOptimizer.Optimize(common,network);
            common.PrepareReceiverChecks=true;var actual=TransferNetworkOptimizer.Optimize(common,network);
            Check(ControlledPerformanceTests.Fingerprint(actual)==ControlledPerformanceTests.Fingerprint(expected),"complete shared/dedicated network preserves selected layout and all forces");
            ControlledPerformanceTests.Validate(common,actual);
            for(int beam=0;beam<2;beam++)
            {
                var probe=expected.Beams[beam];var t=probe.Transfer;
                var input=common.CopyForSearch();input.TransferredSlab=new TransferredSlabInput {EngineerNetwork=EngineerPrimaryPlan.Adjacent(network,beam).Any() ? network : null,FixedCoveredLengthMm=2450};input.EngineerPlan=t.PrimaryPlan;
                input.NetworkNumerics=new TransferredSlabOptimizer.SearchWorkCache();
                foreach(double legLimit in new[] {5.0,70})foreach(bool ignoreUplift in new[] {false,true})foreach(int receiverCount in new[] {2,8})foreach(bool narrow in new[] {false,true})
                {
                    input.LegCapacityKn=legLimit;input.IgnoreUplift=ignoreUplift;
                    var template=(TowerLayout)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(probe,null);
                    template.Transfer=t.Copy();template.Transfer.EngineerReceiverCount=receiverCount;input.AllowLocalShutterExceptions=narrow;
                    var prepared=CrossBeamSupportOptimizer.PrepareReceiver(input,network,beam,template);Check(prepared!=null,"modern fully loaded check can be prepared");
                    for(int shift=-250;shift<=250;shift+=25)
                    {
                        var layout=(TowerLayout)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(template,null);
                        layout.PositionsMm=template.PositionsMm.Select(x=>x+shift).ToArray();layout.Transfer=template.Transfer.Copy();
                        layout.Transfer.PrimaryCases=template.Transfer.PrimaryCases.Select(stage=>stage.Select(m=>
                        {
                            var copy=new TransferMember {Name=m.Name,LoadCase=m.LoadCase,H20Count=m.H20Count,PrimaryMaterial=m.PrimaryMaterial,StartMm=m.StartMm,LengthMm=m.LengthMm,SupportsMm=m.SupportsMm.Select(x=>x+shift).ToArray(),Uniform=m.Uniform,Points=m.Points};
                            copy.Analysis=LoadedBeamAnalysis.AnalyzeSigned(copy.LengthMm,copy.SupportsMm,copy.Uniform,copy.Points,copy.EI,input.DeflectionDenominator);return copy;
                        }).ToArray()).ToArray();
                        if(layout.Transfer.PrimaryCases.Any(stage=>stage.Any(m=>m.SupportsMm[0]<0 || m.SupportsMm.Last()>m.LengthMm)))continue;
                        bool reference=(input.TransferredSlab.EngineerNetwork==null || CrossBeamSupportOptimizer.CheckEngineerReceiver(input,network,beam,layout)) && CrossBeamSupportOptimizer.CheckJunctionReceiver(input,network,beam,layout);
                        var geometry=new TransferredSlabOptimizer.Geometry {Positions=layout.PositionsMm,Joined=layout.Joined,Stock=layout.StockPlan.Pieces,MemberSupports=layout.Transfer.PrimaryCases[0].Take(layout.StockPlan.Pieces.Length).Select(m=>m.SupportsMm).ToArray()};
                        Check(prepared(geometry,0)==reference,"prepared checks match physical secondary, primary and leg decisions over shifted supports");
                    }
                }
            }
        }
        GeometryParity();SpanScreenParity();Console.WriteLine("Passed "+checks+" prepared receiver and span-screen checks.");
    }
    static void GeometryParity()
    {
        var a=new[] {150.0,1150,2300};var b=(double[])a.Clone();var c=(double[])a.Clone();c[1]+=1e-10;
        Check(new TransferredSlabOptimizer.GeometryKey(a,new[] {true,false}).Equals(new TransferredSlabOptimizer.GeometryKey(b,new[] {true,false})),"exact geometry identity");
        Check(!new TransferredSlabOptimizer.GeometryKey(a,new[] {true,false}).Equals(new TransferredSlabOptimizer.GeometryKey(c,new[] {true,false})),"precise coordinates retained");
        Check(!new TransferredSlabOptimizer.GeometryKey(a,new[] {true,false}).Equals(new TransferredSlabOptimizer.GeometryKey(b,new[] {false,true})),"brace topology retained");
        var conflict=typeof(TransferredSlabOptimizer).GetMethod("ConflictingRow",BindingFlags.Static|BindingFlags.NonPublic);var random=new Random(101838);
        for(int sample=0;sample<1000;sample++)
        {
            var rows=Enumerable.Range(1,100).Select(i=>i*40.0+random.NextDouble()*20).ToArray();
            var geometry=new TransferredSlabOptimizer.Geometry {Stock=new[] {new TowerStockPiece(),new TowerStockPiece {StartMm=sample%2==0 ? rows[sample%100]+40 : 10+random.NextDouble()*4100},new TowerStockPiece {StartMm=10+random.NextDouble()*4100}}};
            double expected=rows.FirstOrDefault(r=>geometry.Stock.Skip(1).Any(stock=>Math.Abs(stock.StartMm-r)<40));
            Check((double)conflict.Invoke(null,new object[] {geometry,rows})==expected,"binary row/joint check retains the strict 40 mm boundary and first conflict");
        }
    }
    static void SpanScreenParity()
    {
        var random=new Random(101837);var method=typeof(TransferredSlabOptimizer).GetMethod("NecessaryPrimarySpans",BindingFlags.Static|BindingFlags.NonPublic);
        var crossType=typeof(TransferredSlabOptimizer).GetNestedType("CrossOption",BindingFlags.NonPublic);
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        for(int sample=0;sample<500;sample++)
        {
            var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,DeferCrossingChecks=true,PrimaryMaterial=material,LengthMm=7000};
            var n=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750},new TransferBeamRow {WidthMm=800,DepthMm=1750}},Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3350,WidthMm=650,DepthMm=1150}}};
            input.EngineerPlan=EngineerPrimaryPlan.Create(input,n,0);input.TransferredSlab=new TransferredSlabInput();
            var plan=input.EngineerPlan;plan.SecondaryBasis=new TransferMember {SupportsMm=new[] {463.0,1987}};
            plan.OwnLoads=new[] {new PositionedSecondaryLoad {IsUniform=true,PositionMm=800,EndMm=1600,ValuePerMetre=random.NextDouble()*150}};
            plan.SlabLoads[0]=new[] {new PositionedSecondaryLoad {PositionMm=100,ValuePerMetre=10*random.NextDouble()}};
            double length=material==PrimaryBeamMaterial.WS10 ? new[] {2500.0,3000,4000,4500}[sample%4] : 1800+random.Next(3200),edge=random.Next(151),start=random.Next(4000),left=100+random.Next(500),right=length-100-random.Next(500);
            var geometry=new TransferredSlabOptimizer.Geometry {Stock=new[] {new TowerStockPiece {StartMm=start,StockLengthMm=length}},MemberSupports=new[] {sample%2==0 ? new[] {left,right} : new[] {left,(left+right)/2,right}}};
            object cross=Activator.CreateInstance(crossType,true);input.PrepareReceiverChecks=false;bool expected=(bool)method.Invoke(null,new object[] {input,geometry,cross,edge,0});
            input.PrepareReceiverChecks=true;input.PrepareSpanGeometry=false;
            bool uncached=(bool)method.Invoke(null,new object[] {input,geometry,cross,edge,0});
            input.PrepareSpanGeometry=true;bool actual=(bool)method.Invoke(null,new object[] {input,geometry,cross,edge,0});
            Check(actual==expected && actual==uncached,"necessary span screen preserves unknown footprint exclusions and signed load bounds");
            // Reuse the same geometry after a changed cover edge and transverse
            // load. Its prepared dimensions must not freeze either decision.
            edge+=sample%2==0 ? .000000001 : 150;
            plan.OwnLoads[0].ValuePerMetre*=3;
            cross=Activator.CreateInstance(crossType,true);input.PrepareSpanGeometry=false;
            expected=(bool)method.Invoke(null,new object[] {input,geometry,cross,edge,0});
            input.PrepareSpanGeometry=true;actual=(bool)method.Invoke(null,new object[] {input,geometry,cross,edge,0});
            Check(actual==expected,"prepared geometry retains changed load and precise cover edge");
        }
        var fixedGeometry=new TransferredSlabOptimizer.Geometry {Stock=new[] {new TowerStockPiece {StockLengthMm=3500}},MemberSupports=new[] {new[] {750.0,2750}}};
        var bands=new[] {new BeamDistributedLoad {StartMm=300,EndMm=950},new BeamDistributedLoad {StartMm=2000,EndMm=2500}};
        var context=new TransferredSlabOptimizer.SpanGeometryContext(50,bands);
        var shape=TransferredSlabOptimizer.PrepareSpanShape(fixedGeometry,context);
        Check(shape.Tails[0]==.75 && Math.Abs(shape.ClearSpans[0]-1.05)<1e-12,"overlapping tail excluded and longest clear interval retained");
        Check(ReferenceEquals(shape,TransferredSlabOptimizer.PrepareSpanShape(fixedGeometry,new TransferredSlabOptimizer.SpanGeometryContext(50,bands))),"equal footprint values reuse the geometry preparation across option instances");
        bands[1].StartMm+=1e-9;
        Check(!ReferenceEquals(shape,TransferredSlabOptimizer.PrepareSpanShape(fixedGeometry,new TransferredSlabOptimizer.SpanGeometryContext(50,bands))),"precise footprint changes invalidate geometry preparation");
        Check(context.Boundaries[2]==2000,"prepared context owns an immutable footprint snapshot");
        var h20=new TowerInput();var steel=new TowerInput {PrimaryMaterial=PrimaryBeamMaterial.WS10};
        Check(TransferredSlabOptimizer.PreparedSpanShapeFits(steel,fixedGeometry,context,31),"WS10 evaluates its own material limits and catalogue self-weight");
        Check(!TransferredSlabOptimizer.PreparedSpanShapeFits(h20,fixedGeometry,context,31),"prepared geometry does not retain a previous material's capacity decision");
    }
}
