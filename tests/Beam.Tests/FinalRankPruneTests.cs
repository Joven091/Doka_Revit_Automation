using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class FinalRankPruneTests
{
    static int checks;
    static readonly BindingFlags fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly BindingFlags methods=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly Type crossType=typeof(TransferredSlabOptimizer).GetNestedType("CrossOption",BindingFlags.NonPublic);
    static readonly Type upperType=typeof(TransferredSlabOptimizer).GetNestedType("Upper",BindingFlags.NonPublic);
    static readonly Type gridType=typeof(TransferredSlabOptimizer).GetNestedType("RowGrid",BindingFlags.NonPublic);
    static readonly MethodInfo secondary=typeof(TransferredSlabOptimizer).GetMethod("FinalSecondarySymmetry",methods);
    static readonly MethodInfo economy=typeof(TransferredSlabOptimizer).GetMethod("FinalEconomy",methods);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Final deterministic rank: "+message);}
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,fields).SetValue(target,value);
    static object Call(MethodInfo method,object[] args)
    {
        try{return method.Invoke(null,args);}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static object Upper(double stock,double bay,double near,double far)
    {
        var value=Activator.CreateInstance(upperType,true);Set(value,"Stock",stock);Set(value,"Bay",bay);Set(value,"Edge",(bay-stock)/2);
        Set(value,"Foot",near);Set(value,"OtherFoot",far);return value;
    }
    static double Value(object value,string field)=>(double)value.GetType().GetField(field,fields).GetValue(value);
    static Tuple<object,object,TowerLayout,TransferredSlabOptimizer.Geometry,TowerInput> Sample(Random random,int count,double? lowerStock=null,double perturb=0)
    {
        double length=4900,origin=random.Next(2)==0 ? 0 : 298.30504760742406,edge=origin+random.NextDouble()*150;
        var positions=Enumerable.Range(0,count).Select(i=>400.0+i*(length-800)/(count-1)).ToArray();positions[0]+=perturb;
        var joined=Enumerable.Range(0,count-1).Select(i=>i%2==0).ToArray();
        var geometry=new TransferredSlabOptimizer.Geometry {Positions=positions,Joined=joined,
            Stock=new[] {new TowerStockPiece {StockLengthMm=length,FirstSupport=0,LastSupport=count-1}},MemberSupports=new[] {positions}};
        var input=new TowerInput {EngineerSearch=true,LengthMm=length+2*(edge-origin),LongitudinalStartMm=origin,
            PrimaryMaterial=random.Next(2)==0 ? PrimaryBeamMaterial.DoubleH20 : PrimaryBeamMaterial.WS10};
        double width=new[] {600.0,1000,1524,1500,2000}[random.Next(5)],stock=lowerStock??(2450+random.Next(5)*25.0);
        bool rotated=random.Next(2)==0;double start=-stock/2+(lowerStock.HasValue ? 0 : random.Next(3)*25.0);
        object left=random.Next(3)==0 ? null : Upper(1800,2200+random.NextDouble()*1e-9,400,400+random.Next(2)*25);
        object right=random.Next(3)==0 ? null : Upper(2450,2900+random.NextDouble()*1e-9,450,450+random.Next(2)*25);
        var cross=Activator.CreateInstance(crossType,true);Set(cross,"Start",start);Set(cross,"Stock",stock);Set(cross,"Left",left);Set(cross,"Right",right);
        var grid=Activator.CreateInstance(gridType,true);var rows=new[] {40.0,900,1800,2600,3600,4860};
        var scales=rows.Select((x,i)=>i%3==0 ? 0 : .305+i*.01).ToArray();var setting=random.Next(2)==0 ? null : rows.Concat(new[] {700.0,4200}).OrderBy(x=>x).ToArray();
        Set(grid,"Rows",rows);Set(grid,"LeftScales",scales);Set(grid,"RightScales",scales.Reverse().ToArray());Set(grid,"SettingOutRows",setting);
        Func<object,string,TransferMember> member=(u,name)=>new TransferMember {Name=name,LengthMm=Value(u,"Stock"),SupportsMm=new[] {Value(u,"Foot")-Value(u,"Edge"),Value(u,"Bay")-Value(u,"OtherFoot")-Value(u,"Edge")}};
        var upper=new List<TransferMember>();if(left!=null)upper.Add(member(left,"Upper left"));if(right!=null)upper.Add(member(right,"Upper right"));
        var layout=new TowerLayout {EngineerSearch=true,PrimaryMaterial=input.PrimaryMaterial,PositionsMm=positions.Select(x=>x+edge).ToArray(),Joined=joined,WidthMm=width,Rotated=rotated,
            LongitudinalStartMm=input.LongitudinalStartMm,LengthMm=input.LengthMm,Utilization=random.NextDouble(),
            StockPlan=new TowerStockPlan {PrimaryMaterial=input.PrimaryMaterial,Pieces=new[] {new TowerStockPiece {StartMm=edge,StockLengthMm=length,FirstSupport=0,LastSupport=count-1}}},
            Transfer=new TransferredSlabResult {RowsMm=rows,SettingOutRowsMm=setting,LowerStockLengthMm=stock,LeftUpperStockMm=left==null ? 0 : Value(left,"Stock"),RightUpperStockMm=right==null ? 0 : Value(right,"Stock"),
                LeftUpperScales=scales,RightUpperScales=scales.Reverse().ToArray(),UpperMembers=upper.ToArray(),
                LowerCases=new[] {new[] {new TransferMember {LengthMm=stock,SupportsMm=new[] {-width/2-start,width/2-start}}}}}};
        return Tuple.Create(cross,grid,layout,geometry,input);
    }
    static void Metrics(Tuple<object,object,TowerLayout,TransferredSlabOptimizer.Geometry,TowerInput> sample,TowerLayout best)
    {
        var layout=sample.Item3;var geometry=sample.Item4;var input=sample.Item5;double edge=layout.StockPlan.Pieces[0].StartMm,centre=input.LongitudinalStartMm+input.LengthMm/2;
        var initial=TransferredSlabOptimizer.RankGeometry(geometry,layout.WidthMm,layout.Rotated,centre-edge,true,true);
        var rank=TransferredSlabOptimizer.FinalRankGeometry(geometry,initial,edge,centre,true);
        double secondaryValue=(double)Call(secondary,new[] {sample.Item1,(object)layout.WidthMm});
        double cost=(double)Call(economy,new[] {(object)rank,sample.Item1,sample.Item2,input});
        Check(rank.Cantilever==layout.CantileverImbalanceMm && rank.BraceLocation==layout.BraceLocationPenalty,"absolute geometry uses the exact final position/piece arithmetic");
        Check(rank.PrimarySymmetry+(rank.Cantilever+secondaryValue)==layout.SymmetryErrorMm,"upper/lower support-tail symmetry matches final layout exactly");
        Check(cost==TransferredSlabOptimizer.Economy(layout),"cost matches exact final association, active source feet, physical repaired rows and material count");
        var incumbent=new TransferredSlabOptimizer.KnownLayoutRank(best);int order=TransferredSlabOptimizer.CompareKnownRanks(rank,secondaryValue,incumbent);
        bool rejected=order>0 || order==0 && cost>incumbent.Cost && Math.Abs(cost-incumbent.Cost)>.01;
        Check(!rejected || !TransferredSlabOptimizer.Better(layout,best),"a deterministic rejection never excludes a layout that can beat the incumbent");
        if(order==0 && Math.Abs(cost-incumbent.Cost)<=.01)Check(!rejected,"cost ties retain the unknown final utilization comparison");
        Check(ReferenceEquals(rank,TransferredSlabOptimizer.FinalRankGeometry(geometry,initial,edge,centre,true)),"identical absolute geometry reuses the final-rank preparation");
        Check(!ReferenceEquals(rank,TransferredSlabOptimizer.FinalRankGeometry(geometry,initial,edge+1e-10,centre,true)),"precise absolute edge changes invalidate final-rank preparation");
    }
    static void FullSearchParity()
    {
        var generated=typeof(ExhaustedStationReuseTests).GetMethod("Generated",methods);
        var core=typeof(TransferredSlabOptimizer).GetMethod("OptimizeCoreByLegTier",methods);
        var fingerprint=typeof(ExhaustedStationReuseTests).GetMethod("Fingerprint",methods);
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        foreach(bool alternatives in new[] {false,true})
        {
            Func<bool,TowerInput> make=prune=>
            {
                double length=material==PrimaryBeamMaterial.WS10 ? 4000 : 3900;
                var input=(TowerInput)generated.Invoke(null,new object[] {new[] {length},1524.0});input.PrimaryMaterial=material;input.EngineerStationLimit=6;input.PruneUneconomicCandidates=prune;
                input.LengthMm=material==PrimaryBeamMaterial.WS10 ? 4500 : 4050;input.MaximumConcreteEndOverhangMm=(input.LengthMm-length)/2;
                input.TransferredSlab.AllowLongerPrimaryCoverage=true;input.TransferredSlab.IncludeMainBeamEndLoads=true;
                input.TransferredSlab.StockCovers=material==PrimaryBeamMaterial.WS10 ? new[] {new[] {4000.0},new[] {4500.0}}
                    : new[] {new[] {3900.0},new[] {3950.0},new[] {4000.0}};
                input.StockLengthsMm=new[] {1250.0,1800,2450,3900,3950,4000};input.TransferredSlab.RightBayMm=2200;input.TransferredSlab.RightAreaLoadKnM2=6.9;input.TransferredSlab.RightSlabSpacingMm=305;
                input.TransferredSlab.RightPlan=new UpperTransferPlan {StockLengthMm=1800,EdgeMm=200,NearFootMm=450,FarFootMm=450};
                var network=new TransferNetworkInput {Beams=new[] {new TransferBeamRow {WidthMm=400,DepthMm=200},new TransferBeamRow {WidthMm=400,DepthMm=200}},
                    Bays=new[] {new TransferBayRow {WidthMm=2200,ThicknessMm=200}},UpperSpacingMm=305};
                input.EngineerPlan=EngineerPrimaryPlan.Create(input,network,0,false,true);input.EngineerPlan.InitialLegs=3;
                input.TransferredSlab.RightAreaLoadKnM2=network.AreaLoad(200);
                if(alternatives){input.TransferredSlab.FixedTowerWidthMm=null;input.ReceiverAlternatives=new Dictionary<double,TowerLayout>();}
                input.TransferLowerAcceptance=probe=>true;return input;
            };
            var actual=make(true);var reference=make(false);int actualProbes=0,referenceProbes=0;
            var actualAccepted=new List<string>();var referenceAccepted=new List<string>();
            actual.TransferReceiverPreparation=probe=>(g,e)=>{actualProbes++;return true;};reference.TransferReceiverPreparation=probe=>(g,e)=>{referenceProbes++;return true;};
            actual.TransferLayoutAcceptance=layout=>{actualAccepted.Add((string)fingerprint.Invoke(null,new object[] {layout}));return true;};
            reference.TransferLayoutAcceptance=layout=>{referenceAccepted.Add((string)fingerprint.Invoke(null,new object[] {layout}));return true;};
            var selected=(TowerLayout)Call(core,new object[] {actual,true,null});var expected=(TowerLayout)Call(core,new object[] {reference,true,null});
            Check((string)fingerprint.Invoke(null,new object[] {selected})==(string)fingerprint.Invoke(null,new object[] {expected}),"pruning on/off preserves the final geometry, loads and every member demand");
            Check(actualAccepted.SequenceEqual(referenceAccepted),"potential winning layouts retain their exact full-acceptance callback sequence");
            Check(actual.Trace.Count(BeamSearchCounter.FinalRankPruned)>0 && actualProbes<referenceProbes,"fixed worse secondary ranks are rejected before geometry-dependent receiver checks");
            if(alternatives)
            {
                Check(actual.ReceiverAlternatives.Keys.OrderBy(x=>x).SequenceEqual(reference.ReceiverAlternatives.Keys.OrderBy(x=>x)),"all independently ranked width alternatives remain available");
                foreach(var pair in actual.ReceiverAlternatives)Check((string)fingerprint.Invoke(null,new object[] {pair.Value})==(string)fingerprint.Invoke(null,new object[] {reference.ReceiverAlternatives[pair.Key]}),"each width preserves its exact best engineering result");
            }
            foreach(var member in selected.Transfer.PrimaryCases.Concat(selected.Transfer.LowerCases).SelectMany(x=>x).Concat(selected.Transfer.UpperMembers))
                Check(member.Analysis.DeflectionEvaluated && member.Utilization<=1+1e-8 && Math.Abs(member.Analysis.Reactions.Sum()-member.Analysis.TotalLoadKn)<1e-7,"potential winners retain full bending/shear/deflection and reaction checks");
        }
    }
    internal static void Run()
    {
        var random=new Random(25041001);
        for(int sample=0;sample<1000;sample++)
        {
            var candidate=Sample(random,2+sample%5);var best=sample%2==0 ? candidate.Item3 : Sample(random,2+sample%4).Item3;Metrics(candidate,best);
        }
        foreach(double delta in new[] {0.0,1e-7,-1e-7,PortableTestRuntime.NextUp(1e-7),PortableTestRuntime.NextDown(1e-7)})
        {
            var a=Sample(new Random(55),4,2450,delta);var b=Sample(new Random(55),4,2450,0);Metrics(a,b.Item3);
        }
        var basis=Sample(new Random(99),4,2450);double boundary=2450+.01/basis.Item3.Transfer.PhysicalRowCount;
        foreach(double stock in new[] {2450.0,boundary,PortableTestRuntime.NextUp(boundary),PortableTestRuntime.NextDown(boundary),2450-.01/basis.Item3.Transfer.PhysicalRowCount})
        {
            var a=Sample(new Random(99),4,stock);var b=Sample(new Random(99),4,2450);a.Item3.Utilization=.2;b.Item3.Utilization=.8;Metrics(a,b.Item3);
        }
        FullSearchParity();
        Console.WriteLine("PASS "+checks+" final deterministic-rank checks: exact absolute/secondary symmetry and cost, floating boundaries, prune-reference winner/callback parity, both materials and all widths");
    }
}
