using System;
using System.IO;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class CombinedConstraintTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    internal static void Run()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,Depth=1750,Width=800,BeamLengthMm=4098.1706196927662,FloorHeight=3000,TowerLegCapacityKn=30,
            IgnoreUplift=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,Exclude600Standalone=true,AllowCustomH20Lengths=false,
            StockLengthsText="1250;1800;2450;2650;2900;3300;3600;3900;4500;4900"};
        var input=vm.CreateTowerInput();input.ObstacleBottomMm=-1750;
        var original=TowerOptimizer.Optimize(input);
        var block=new FormworkSolid {Name="Neighboring tower leg",TowerOwner="other part / B1",IsLeg=true,X0=1000,X1=1080,Y0=-800,Y1=-750,Z0=-3000,Z1=-1950};
        input.ExternalObstacles=new[] {block};
        Check(!CrossBeamSupportOptimizer.ExternalLayoutFits(input,original),"Full H20 pairs clash with the reported leg, including the outer flange.");
        var fixedLayout=TowerOptimizer.Optimize(input);
        Check(CrossBeamSupportOptimizer.ExternalLayoutFits(input,fixedLayout),"A complete checked alternative avoids the fixed neighbor.");
        Check(fixedLayout.Utilization<=1 && fixedLayout.MaxLegKn<=30,"Clearance does not bypass strength, deflection or leg checks.");
        Check(fixedLayout.WidthMm==1000 && fixedLayout.Rotated && fixedLayout.LegCount==8,"An actual supported arrangement changes, rather than moving graphics or ignoring the clash.");
        Check(Math.Abs(fixedLayout.LeftLineLoad-original.LeftLineLoad)<1e-10 && Math.Abs(fixedLayout.RightLineLoad-original.RightLineLoad)<1e-10,"Original design UDLs are retained.");
        Check(fixedLayout.StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(original.StockPlan.Pieces.Select(p=>p.StockLengthMm)),"Primary stock coverage stays the same.");
        var translated=input.CopyForSearch();translated.LongitudinalStartMm=237.125;translated.ExternalObstacleIndex=null;
        translated.ExternalObstacles=new[] {new FormworkSolid {Name=block.Name,TowerOwner=block.TowerOwner,IsLeg=true,X0=block.X0+237.125,X1=block.X1+237.125,Y0=block.Y0,Y1=block.Y1,Z0=block.Z0,Z1=block.Z1}};
        var moved=TowerOptimizer.Optimize(translated);
        Check(moved.PositionsMm.Zip(fixedLayout.PositionsMm,(a,b)=>Math.Abs(a-b-237.125)<1e-8).All(x=>x),"Nonzero column-bounded span origin translates the constraint and supports exactly once.");
        Check(CrossBeamSupportOptimizer.ExternalLayoutFits(translated,moved),"Final physical solids use the actual column-bounded coordinates.");
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".beam");
        try
        {
            BeamWire.Write(path,new BeamSolverRequest {Main=input});
            var read=BeamWire.Read<BeamSolverRequest>(path).Main;
            Check(read.ExternalObstacles.Length==1 && read.ExternalObstacles[0].Y0==block.Y0 && read.ExternalObstacleIndex==null,"Worker transport preserves exact constraints and excludes runtime BVH caches.");
            var remote=TowerOptimizer.Optimize(read);
            Check(remote.PositionsMm.SequenceEqual(fixedLayout.PositionsMm) && remote.WidthMm==fixedLayout.WidthMm,"Worker input round trip produces the same checked arrangement.");
        }
        finally{File.Delete(path);}
        var cancelled=input.CopyForSearch();using(var stop=new CancellationTokenSource())
        {
            stop.Cancel();cancelled.Trace=new BeamSearchTrace("cancelled coordination",stop.Token);
            bool observed=false;try{TowerOptimizer.Optimize(cancelled);}catch(OperationCanceledException){observed=true;}
            Check(observed,"Cancellation still stops a constrained retry.");
        }
        Console.WriteLine("Passed "+checks+" coordinated constraint, loading, coordinate, cancellation and worker transport checks.");
    }
    internal static void Network()
    {
        var input=ReportedCombinedTests.Input(5);input.LegCapacityKn=30;input.Exclude600Standalone=true;
        var original=TransferNetworkOptimizer.Optimize(input,input.Network);
        input.ExternalObstacles=new[] {new FormworkSolid {Name="neighbor / tower leg",TowerOwner="neighbor / B1",IsLeg=true,X0=360,X1=440,Y0=-802,Y1=-722,Z0=-3000,Z1=-2000}};
        Check(!CrossBeamSupportOptimizer.ExternalNetworkFits(input,original),"A neighboring leg is recognized in network coordinates.");
        var result=TransferNetworkOptimizer.Optimize(input,input.Network);
        Check(CrossBeamSupportOptimizer.ExternalNetworkFits(input,result),"A network alternative clears fixed neighboring geometry, including after centring.");
        Check(result.Beams.All(b=>b.Utilization<=1+1e-8 && b.MaxLegKn<=30+1e-8),"Constrained network retains member/leg checks.");
        Check(Math.Abs((result.SlabLoadKn+result.CrossSupports.Sum(c=>c.LoadKn))-(original.SlabLoadKn+original.CrossSupports.Sum(c=>c.LoadKn)))<1e-7,"Total slab/crossing load is conserved when a different crossing arrangement redistributes edge loads.");
        Console.WriteLine("Passed "+checks+" constrained network checks; "+result.LegCount+" legs; peak "+result.PeakLegKn.ToString("0.00")+" kN.");
    }

}
