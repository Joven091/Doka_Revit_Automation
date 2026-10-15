using System;
using System.Linq;
using EngineeringDashboard;

internal static class CrossingSlabSupportTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Slab edge ledgers: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(b)),message+": "+a+" vs "+b);
    static TransferNetworkInput Input(double spacing=488)=>new TransferNetworkInput {
        UpperSpacingMm=305,PlywoodSheetLengthMm=2440,PlywoodProfileId="doka-18mm-9732-103",PlywoodDeflectionDenominator=350,
        BeamSpacingsMm=new[] {305.0,305},SlabSpacingsMm=new[] {spacing},CrossingSpacingsMm=new[] {350.0},
        Beams=new[] {new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750},new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}},
        Bays=new[] {new TransferBayRow {WidthMm=2000,ThicknessMm=200}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=3350,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150,SupportMode=CrossBeamSupportMode.Automatic}}};
    internal static void Run()
    {
        foreach(double pitch in new[] {488.0,407,350,305,275,250})
        {
            var n=Input(pitch);var map=n.CrossSlabLayout(7000,6750,0);Check(map.Valid,"reported crossing has a valid shutter-aware row layout at "+pitch);
            Check(map.Rows.All(x=>Math.Abs(x-3350)>=325+240-1e-7),"secondary flanges stay outside the 200 mm shutter zone");
            Near(map.Widths.Sum()+map.Edges.Sum(e=>e.WidthMm),6750-650,"slab force is conserved");
            Near(map.Rows.Zip(map.Widths,(x,w)=>x*w).Sum()+map.Edges.Sum(e=>e.PositionMm*e.WidthMm),(6875.0*6875-125.0*125-3675.0*3675+3025.0*3025)/2,"slab longitudinal moment is conserved");
            Near(map.Redistribution.Sum(b=>b.KnPerM*(b.EndMm-b.StartMm))+map.Edges.Sum(e=>e.WidthMm),0,"primary baseline removes exactly the load sent to ledgers");
            Near(map.Redistribution.Sum(b=>b.KnPerM*(b.EndMm*b.EndMm-b.StartMm*b.StartMm)/2)+map.Edges.Sum(e=>e.PositionMm*e.WidthMm),0,"primary redistribution retains the physical load resultant");
            Check(map.Edges.Count==2 && map.Edges.All(e=>e.WidthMm>0),"both crossing faces receive explicit slab-edge reactions");
            if(pitch==488){Check(map.Rows.Contains(4163),"first right row is 488 mm from X1's 3675 mm face");Check(map.Rows.Contains(4880),"required plywood joint at 4880 mm remains supported");}
            var weights=CrossBeamSupportOptimizer.PointRowWeights(new[] {3100.0,3400,3600},3675);
            Near(weights.Sum(),1,"ledger transfer weights conserve force");Near(weights.Zip(new[] {3100.0,3400,3600},(w,x)=>w*x).Sum(),3675,"ledger transfer weights conserve moment");
        }
        var input=Input();input.CrossingBeams=new[] {input.CrossingBeams[0],new TransferCrossingBeamRow {BayNumber=1,PositionMm=5200,WidthMm=500,DepthMm=600,BottomElevationMm=-600}};
        var two=input.CrossSlabLayout(7000,6750,0);Check(two.Valid,"two crossings share a checked intermediate slab strip");
        Near(two.Widths.Sum()+two.Edges.Sum(e=>e.WidthMm),6750-650-500,"multiple crossings count each slab strip once");
        Console.WriteLine("Passed "+checks+" slab-edge ledger equilibrium and setting-out checks.");
    }
    internal static void Reported()
    {
        var common=new TowerInput {LengthMm=7000,LegCapacityKn=70,DeflectionDenominator=270,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,Exclude600Standalone=false,IgnoreUplift=true,NetworkSearchWorkers=1};
        try
        {
            var original=TransferNetworkOptimizer.Optimize(common,Input());
            Check(original.CrossSupports[0].SlabEdgeLoadKn>0,"reported stock scope reaches a checked ledger load path");
        }
        catch(TowerOptimizationException ex)
        {
            Check(ex.Details.Contains("deflection") && !ex.Details.Contains("slab cross exclusion"),"original stock scope reaches a member limit rather than the former row-exclusion gap");
            Console.WriteLine("Original reported stock scope rejected: "+ex.Message);
        }
        // A separate passing fixture permits fitted receiving H20 stock; production never changes the user's scope.
        common.CustomH20Scope=CustomH20LengthScope.Both;
        var result=TransferNetworkOptimizer.Optimize(common,Input());
        Check(result.CrossSupports.Length==1 && result.CrossSupports[0].SlabEdgeLoadKn>0,"reported setup has a passing crossing with slab-edge loads");
        var x=result.CrossSupports[0];double concrete=result.Input.AreaLoad(x.Input.DepthMm)*x.Input.WidthMm*2000/1e6;
        Near(x.LoadKn,concrete+x.SlabEdgeLoadKn,"crossing carries concrete plus ledger reactions once");
        Check(result.Beams.All(b=>b.Utilization<=1+1e-8) && x.Members.All(m=>m.Utilization<=1+1e-8),"reported members pass capacity checks");
        Console.WriteLine("Reported slab-edge setup passed: "+result.LegCount+" main legs, "+x.SlabEdgeLoadKn+" kN through ledgers.");
    }
}
