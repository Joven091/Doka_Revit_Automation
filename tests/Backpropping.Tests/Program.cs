using System;
using EngineeringDashboard.Backpropping;

internal static class Program
{
    private static int checks;
    private static void Equal(double expected, double actual) { checks++; if (double.IsNaN(actual) || Math.Abs(expected-actual)>1e-8) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void True(bool value) { checks++; if (!value) throw new Exception("Assertion failed"); }
    private static void Reject(Action action) { checks++; try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid input rejection"); }
    private static SlabInput Slab(double age, double sidl=3, double live=3) => new SlabInput { Name="Level", ThicknessMm=200, ClearHeightBelowMm=3000, AgeDays=age, Sidl=sidl, FinalLiveLoad=live };
    private static void Main()
    {
        foreach (var curve in new[] { TemperatureCurve.A, TemperatureCurve.B, TemperatureCurve.C })
        { Equal(0,BackproppingCalculator.Strength(0,curve)); Equal(100,BackproppingCalculator.Strength(28,curve)); Equal(100,BackproppingCalculator.Strength(70,curve)); }
        Equal(50,BackproppingCalculator.Strength(2,TemperatureCurve.A)); Equal(70,BackproppingCalculator.Strength(4,TemperatureCurve.A));
        Equal(80,BackproppingCalculator.Strength(12,TemperatureCurve.A)); Equal(90,BackproppingCalculator.Strength(20,TemperatureCurve.A));
        Equal(77.5,BackproppingCalculator.Strength(10,TemperatureCurve.A));
        Equal(50,BackproppingCalculator.Strength(4,TemperatureCurve.B)); Equal(70,BackproppingCalculator.Strength(8,TemperatureCurve.B)); Equal(85,BackproppingCalculator.Strength(18,TemperatureCurve.B));
        Equal(50,BackproppingCalculator.Strength(6,TemperatureCurve.C)); Equal(70,BackproppingCalculator.Strength(12,TemperatureCurve.C)); Equal(85,BackproppingCalculator.Strength(20,TemperatureCurve.C));
        Equal(.75,BackproppingCalculator.HeapingLoad(5)); Equal(.75,BackproppingCalculator.HeapingLoad(7.5)); Equal(1,BackproppingCalculator.HeapingLoad(10)); Equal(1.75,BackproppingCalculator.HeapingLoad(17.5)); Equal(1.75,BackproppingCalculator.HeapingLoad(25));
        var slabs=new[] {Slab(0),Slab(10),Slab(10),Slab(28)};
        var loads=BackproppingCalculator.CalculateLoads(slabs,TemperatureCurve.A);
        True(loads.Complete); Equal(7,loads.PourLoad); Equal(12.75,loads.Slabs[1].Demand); Equal(8.525,loads.Slabs[1].Capacity);
        Equal(4.225,loads.Slabs[1].Transfer); Equal(4.625,loads.Slabs[1].DownwardLoad); Equal(1.5,loads.Slabs[2].DownwardLoad); Equal(0,loads.Slabs[3].DownwardLoad);
        Equal(0,loads.Slabs[2].ConstructionLive); Equal(6.5,loads.Slabs[3].Demand);
        True(!BackproppingCalculator.CalculateLoads(new[]{Slab(0),Slab(10)},TemperatureCurve.A).Complete);
        True(!BackproppingCalculator.CalculateLoads(Array.Empty<SlabInput>(),TemperatureCurve.A).Complete);
        var stop=BackproppingCalculator.CalculateLoads(new[]{Slab(0),Slab(28,10,10),Slab(28)},TemperatureCurve.A);
        True(stop.Complete); Equal(2,stop.Slabs.Count); Equal(0,stop.Slabs[1].DownwardLoad);
        var varying=new[]{Slab(0),Slab(28,2,5)}; varying[1].ThicknessMm=300;
        Equal(7.5,BackproppingCalculator.CalculateLoads(varying,TemperatureCurve.A).Slabs[1].SelfWeight);
        Reject(()=>BackproppingCalculator.CalculateLoads(new[]{Slab(0),Slab(10),Slab(9)},TemperatureCurve.A));
        Reject(()=>BackproppingCalculator.CalculateLoads(new[]{Slab(1)},TemperatureCurve.A));
        Reject(()=>BackproppingCalculator.Strength(double.NaN,TemperatureCurve.A)); Reject(()=>BackproppingCalculator.Strength(40,(TemperatureCurve)99));
        Reject(()=>BackproppingCalculator.Strength(-1,TemperatureCurve.A)); Reject(()=>BackproppingCalculator.HeapingLoad(double.PositiveInfinity));
        double lookup;
        Equal(34,BackproppingPropCatalog.Capacity("DokaFit Prop 400",3701,out lookup)); Equal(3800,lookup);
        Equal(35,BackproppingPropCatalog.Capacity("DokaFit Prop 400",3700,out lookup));
        Equal(31.5,BackproppingPropCatalog.Capacity("DokaFit Prop 400",3900,out lookup)); Equal(30,BackproppingPropCatalog.Capacity("DokaFit Prop 400",4000,out lookup));
        Reject(()=>BackproppingPropCatalog.Capacity("DokaFit Prop 400",2500,out lookup)); Reject(()=>BackproppingPropCatalog.Capacity("DokaFit Prop 400",4001,out lookup));
        Reject(()=>BackproppingPropCatalog.Capacity("DokaFit Prop 300",1800,out lookup)); Reject(()=>BackproppingPropCatalog.Capacity("DokaFit Prop 350",2100,out lookup));
        Equal(40,BackproppingPropCatalog.Capacity("Eurex 20 400",3500,out lookup)); Equal(36.7,BackproppingPropCatalog.Capacity("Eurex 20 top 400",3500,out lookup));
        Reject(()=>BackproppingPropCatalog.Capacity("Eurex 20 top 400",2200,out lookup));
        Reject(()=>BackproppingCalculator.CalculateLoads(new[]{Slab(0),Slab(28,double.MaxValue,double.MaxValue)},TemperatureCurve.A));
        foreach(var prop in BackproppingPropCatalog.All)
        {
            True(BackproppingPropCatalog.Capacity(prop.Name,prop.MinimumMm,out lookup)>0);
            True(BackproppingPropCatalog.Capacity(prop.Name,prop.MaximumMm,out lookup)>0);
            Reject(()=>BackproppingPropCatalog.Capacity(prop.Name,prop.MinimumMm-1,out lookup)); Reject(()=>BackproppingPropCatalog.Capacity(prop.Name,prop.MaximumMm+1,out lookup));
        }
        var spacing=BackproppingCalculator.CalculateSpacing(4.625,"DokaFit Prop 300",3000,null);
        Equal(2750,spacing.SpacingMm); True(spacing.PropLoadKn<=35); True(spacing.UtilizationPercent<=100); True(!spacing.HasProjectLimit);
        True(4.625*Math.Pow((spacing.SpacingMm+25)/1000,2)>35);
        spacing=BackproppingCalculator.CalculateSpacing(4.625,"DokaFit Prop 300",3000,2010);
        Equal(2000,spacing.SpacingMm); True(spacing.HasProjectLimit); Equal(18.5,spacing.PropLoadKn);
        Reject(()=>BackproppingCalculator.CalculateSpacing(0,"DokaFit Prop 300",3000,null));
        Reject(()=>BackproppingCalculator.CalculateSpacing(7,null,3000,null));
        Reject(()=>BackproppingCalculator.CalculateSpacing(7,"DokaFit Prop 300",3000,24));
        Console.WriteLine($"Passed {checks} backpropping checks.");
    }
}
