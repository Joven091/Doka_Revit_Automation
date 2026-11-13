using System;
using System.Linq;
using System.Threading.Tasks;
using EngineeringDashboard;

internal static class GeometryRankingReuseTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Geometry ranking reuse: "+message);}
    internal static void Run()
    {
        var geometry=new TransferredSlabOptimizer.Geometry {
            Positions=new[] {450.0,1450,2450,3450},Joined=new[] {true,false,true},
            Stock=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=3900,FirstSupport=0,LastSupport=3}}};
        foreach(double width in new[] {600.0,1000,1500,1524,2000})foreach(bool rotated in new[] {false,true})
        foreach(double centre in new[] {1950.0,1950.00000001,1900})foreach(bool engineer in new[] {false,true})
        {
            var expected=TransferredSlabOptimizer.RankGeometry(geometry,width,rotated,centre,engineer,false);
            var actual=TransferredSlabOptimizer.RankGeometry(geometry,width,rotated,centre,engineer,true);
            Check(actual.Joined==expected.Joined && actual.Narrow==expected.Narrow && actual.Brace==expected.Brace
                && actual.Cantilever==expected.Cantilever && actual.Symmetry==expected.Symmetry,"all ranking values retain their original exact calculation");
            Check(ReferenceEquals(actual,TransferredSlabOptimizer.RankGeometry(geometry,width,rotated,centre,engineer,true)),"the exact retained context reuses its immutable metrics");
            Check(geometry.Rankings.Length<=16,"the per-geometry context history is bounded");
        }
        var shared=TransferredSlabOptimizer.RankGeometry(geometry,1524,false,1950,true,true);
        Parallel.For(0,128,_=>Check(ReferenceEquals(shared,TransferredSlabOptimizer.RankGeometry(geometry,1524,false,1950,true,true)),"parallel readers share the same complete immutable entry"));
        Check(!ReferenceEquals(shared,TransferredSlabOptimizer.RankGeometry(geometry,1524,false,1950.00000001,true,true)),"precise centre changes keep their own symmetry calculation");
        Check(!ReferenceEquals(shared,TransferredSlabOptimizer.RankGeometry(geometry,1000,false,1950,true,true)),"changed brace widths have their own metrics");
        Check(!ReferenceEquals(shared,TransferredSlabOptimizer.RankGeometry(geometry,1524,true,1950,true,true)),"rotation remains part of the ranking context");
        Check(!ReferenceEquals(shared,TransferredSlabOptimizer.RankGeometry(geometry,1524,false,1950,false,true)),"historical non-engineer symmetry behavior remains distinct");
        Console.WriteLine("PASS exact geometry ranking, context separation, bounded memory and concurrent reuse");
    }
}
