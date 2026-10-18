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
                foreach(int receiverCount in new[] {2,8})foreach(bool narrow in new[] {false,true})
                {
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
        SpanScreenParity();Console.WriteLine("Passed "+checks+" prepared receiver and span-screen checks.");
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
            input.PrepareReceiverChecks=true;bool actual=(bool)method.Invoke(null,new object[] {input,geometry,cross,edge,0});
            Check(actual==expected,"necessary span screen preserves unknown footprint exclusions and signed load bounds");
        }
    }
}
